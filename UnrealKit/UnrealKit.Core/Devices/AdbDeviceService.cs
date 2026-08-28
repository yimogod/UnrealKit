using UnrealKit.Core.Adb;
using UnrealKit.Core.CommandChannel;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Devices;

/// <summary>
/// IDeviceService implementation that wraps IAdbService and normalizes ADB errors into the common
/// throw-on-failure protocol via RunRequiredAsync.
/// </summary>
public sealed class AdbDeviceService : IDeviceService
{
    private readonly AdbService _adb;

    /// <summary>
    /// 控制台指令通道。Android 与 Win64 统一走引擎自带 Web Remote Control 的 HTTP 服务；
    /// Android 需改引擎两处 <c>PlatformAllowList</c> 加入 Android（属用户改引擎的职责）。
    /// </summary>
    private readonly ICommandTransport _commandTransport;

    /// <summary>
    /// 已完成端口转发的设备。指令序列每步都重新 forward 会多起一个 adb 进程，
    /// 并把 adb 输出混进序列报告，因此按设备记住一次。
    /// </summary>
    private readonly HashSet<string> _forwardedDevices = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _forwardLock = new(1, 1);

    /// <param name="adb">ADB 调用。</param>
    /// <param name="channelOptions">指令通道配置。null 取内置默认（Web Remote Control HTTP）。</param>
    /// <param name="commandTransport">显式指定的通道实例，仅用于测试注入；否则按配置构造。</param>
    public AdbDeviceService(
        AdbService adb,
        CommandChannelOptions? channelOptions = null,
        ICommandTransport? commandTransport = null)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _commandTransport = commandTransport
            ?? (channelOptions ?? CommandChannelOptions.Default).CreateTransport();
    }

    public TargetPlatform Platform => TargetPlatform.Android;

    /// <summary>Android 经由 ADB 支持全部设备能力。</summary>
    public bool Supports(DeviceCapability capability) => true;

    public async Task<IReadOnlyList<IDevice>> ListDevicesAsync(
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // AdbDevice 本身实现 IDevice，无需额外包装层。
        var adbDevices = await _adb.ListDevicesAsync(progress, cancellationToken);
        return adbDevices.Cast<IDevice>().ToList();
    }

    public Task<ProcessExecutionResult> CaptureMemoryAsync(
        IDevice device,
        string target,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return RunRequiredAsync(_adb.RunDumpsysAsync(device.Id, target, progress, cancellationToken));
    }

    public Task<ProcessExecutionResult> PullDirectoryAsync(
        IDevice device,
        string remotePath,
        string localDirectory,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return RunRequiredAsync(_adb.PullDirectoryAsync(device.Id, remotePath, localDirectory, progress, cancellationToken));
    }

    /// <summary>
    /// 拉取多个可选子目录。远端子目录不存在不是错误（GPUDumps/Screenshots 等可能尚未生成），
    /// 由 adb 报错文本识别「不存在」后跳过；权限、设备断开等真实错误仍抛 <see cref="DeviceCommandException"/>。
    /// </summary>
    public async Task<ProcessExecutionResult> PullSubdirectoriesAsync(
        IDevice device,
        string remoteDirectory,
        IReadOnlyList<string> subdirectoryNames,
        string localDirectory,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteDirectory);
        ArgumentNullException.ThrowIfNull(subdirectoryNames);
        ArgumentException.ThrowIfNullOrWhiteSpace(localDirectory);

        var remoteRoot = remoteDirectory.TrimEnd('/');
        var pulled = 0;

        // adb pull 要求本地目标目录的父目录已存在，否则报
        // 「cannot create file/directory ... No such file or directory」，而这串文本又会被下面的
        // 缺失跳过判断误当成「远端不存在」，导致连存在的子目录都被静默跳过。
        // 因此先建好容器目录，让每个子目录能落到 localDirectory/<name> 下。
        Directory.CreateDirectory(localDirectory);

        foreach (var name in subdirectoryNames)
        {
            var remotePath = $"{remoteRoot}/{name}";
            var localTarget = Path.Combine(localDirectory, name);

            ProcessExecutionResult result;
            try
            {
                result = await _adb.TryPullDirectoryAsync(device.Id, remotePath, localTarget, progress, cancellationToken);
            }
            catch (AdbCommandException adbEx)
            {
                throw new DeviceCommandException(adbEx.Message, adbEx.Result, adbEx);
            }
            catch (AdbPathResolutionException pathEx)
            {
                throw new DeviceCommandException(pathEx.Message,
                    new ProcessExecutionResult(1, string.Empty, pathEx.Message, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                    pathEx);
            }

            if (result.Succeeded)
            {
                pulled++;
                continue;
            }

            // 远端子目录不存在是可接受的正常状态；其它失败（权限、设备断开）必须具体报错。
            if (result.StandardError.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                || result.StandardError.Contains("no such file", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(new OperationProgress(
                    "pull", "Skip", null, null, $"设备上不存在子目录 {name}，跳过。"));
                continue;
            }

            throw new DeviceCommandException(
                $"Device operation failed with exit code {result.ExitCode}: {result.StandardError}", result);
        }

        // 一个子目录都没取回时撤掉刚建的容器，保持「stagingTarget 不存在」=「没取回任何内容」的判定。
        // 容器只在本方法里为空目录，撤掉它是安全的。
        if (pulled == 0 && Directory.Exists(localDirectory))
        {
            Directory.Delete(localDirectory, recursive: true);
        }

        return new ProcessExecutionResult(
            0, $"Pulled {pulled} of {subdirectoryNames.Count} subdirectories from {remoteRoot}.", string.Empty,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }

    public async Task<ProcessExecutionResult> SendConsoleCommandAsync(
        IDevice device,
        string command,
        string? target = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        await EnsurePortForwardedAsync(device, progress, cancellationToken);

        try
        {
            return await _commandTransport.SendConsoleCommandAsync(command, progress, cancellationToken);
        }
        catch (CommandTransportException exception)
        {
            throw new DeviceCommandException(exception.Message, exception.Result, exception);
        }
    }

    /// <summary>
    /// 读回 cvar。复用与发送指令相同的端口转发，不为读回再起一次 <c>adb forward</c>。
    /// </summary>
    public async Task<ProcessExecutionResult> QueryConsoleVariableAsync(
        IDevice device,
        string variableName,
        ConsoleVariableType variableType,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);

        await EnsurePortForwardedAsync(device, progress, cancellationToken);

        try
        {
            return await _commandTransport.QueryConsoleVariableAsync(
                variableName, variableType, progress, cancellationToken);
        }
        catch (CommandTransportException exception)
        {
            throw new DeviceCommandException(exception.Message, exception.Result, exception);
        }
    }

    public IAsyncEnumerable<string> StreamLogAsync(
        IDevice device,
        string? filter = null,
        CancellationToken cancellationToken = default)
    {
        return _adb.StreamLogcatAsync(device.Id, filter, cancellationToken);
    }

    public Task<ProcessExecutionResult> StartApplicationAsync(
        IDevice device,
        string target,
        string? activity = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return RunRequiredAsync(_adb.StartApplicationAsync(device.Id, target, activity ?? string.Empty, progress, cancellationToken));
    }

    public Task<ProcessExecutionResult> StopApplicationAsync(
        IDevice device,
        string target,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return RunRequiredAsync(_adb.ForceStopApplicationAsync(device.Id, target, progress, cancellationToken));
    }

    public Task<ProcessExecutionResult> PushFileAsync(
        IDevice device,
        string localPath,
        string remotePath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return RunRequiredAsync(_adb.PushFileAsync(device.Id, localPath, remotePath, progress, cancellationToken));
    }

    public Task<ProcessExecutionResult> DeleteRemoteFileAsync(
        IDevice device,
        string remotePath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return RunRequiredAsync(_adb.DeleteRemoteFileAsync(device.Id, remotePath, progress, cancellationToken));
    }

    public Task<ProcessExecutionResult> ReadFileAsync(
        IDevice device,
        string remotePath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 读取是查询语义：文件不存在是正常状态，直接透传原始结果，不经过 RunRequiredAsync。
        ArgumentNullException.ThrowIfNull(device);
        return _adb.ReadFileAsync(device.Id, remotePath, progress, cancellationToken);
    }

    public Task<ProcessExecutionResult> InstallApplicationAsync(
        IDevice device,
        string localApplicationPath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return RunRequiredAsync(_adb.InstallApkAsync(device.Id, localApplicationPath, progress, cancellationToken));
    }

    /// <summary>
    /// 为设备建立指令通道的端口转发，同一设备只执行一次。
    /// 端口取自通道自身（<see cref="ICommandTransport.Port"/>）：转发的端口与实际连接的
    /// 端口若各取一处配置，改了一边就会转发到无人监听的端口。
    /// 失败不记录，下次调用重试。
    /// </summary>
    private async Task EnsurePortForwardedAsync(
        IDevice device,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (_forwardedDevices.Contains(device.Id))
        {
            return;
        }

        await _forwardLock.WaitAsync(cancellationToken);
        try
        {
            if (_forwardedDevices.Contains(device.Id))
            {
                return;
            }

            progress?.Report(new OperationProgress(
                "console-send",
                "Forwarding",
                null,
                null,
                $"Forwarding TCP port {_commandTransport.Port} ({_commandTransport.Kind}) for {device.Id}."));

            await RunRequiredAsync(_adb.ForwardTcpAsync(
                device.Id,
                _commandTransport.Port,
                _commandTransport.Port,
                progress,
                cancellationToken));

            _forwardedDevices.Add(device.Id);
        }
        finally
        {
            _forwardLock.Release();
        }
    }

    private static async Task<ProcessExecutionResult> RunRequiredAsync(Task<ProcessExecutionResult> task)
    {
        ProcessExecutionResult result;
        try
        {
            result = await task;
        }
        catch (AdbCommandException adbEx)
        {
            throw new DeviceCommandException(adbEx.Message, adbEx.Result, adbEx);
        }
        catch (AdbPathResolutionException pathEx)
        {
            throw new DeviceCommandException(pathEx.Message,
                new ProcessExecutionResult(1, string.Empty, pathEx.Message, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                pathEx);
        }

        if (!result.Succeeded)
        {
            throw new DeviceCommandException($"Device operation failed with exit code {result.ExitCode}: {result.StandardError}", result);
        }

        return result;
    }
}
