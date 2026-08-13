using UnrealKit.Core.Adb;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;
using UnrealKit.Core.RemoteControl;

namespace UnrealKit.Core.Devices;

/// <summary>
/// IDeviceService implementation that wraps IAdbService and normalizes ADB errors into the common
/// throw-on-failure protocol via RunRequiredAsync.
/// </summary>
public sealed class AdbDeviceService : IDeviceService
{
    private readonly IAdbService _adb;
    private readonly RemoteControlOptions _remoteControlOptions;
    private readonly IRemoteControlService _remoteControl;

    /// <summary>
    /// 已完成端口转发的设备。指令序列每步都重新 forward 会多起一个 adb 进程，
    /// 并把 adb 输出混进序列报告，因此按设备记住一次。
    /// </summary>
    private readonly HashSet<string> _forwardedDevices = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _forwardLock = new(1, 1);

    public AdbDeviceService(
        IAdbService adb,
        RemoteControlOptions? remoteControlOptions = null,
        IRemoteControlService? remoteControlService = null)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _remoteControlOptions = remoteControlOptions ?? RemoteControlOptions.Default;
        _remoteControl = remoteControlService ?? new RemoteControlService();
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
            return await _remoteControl.SendConsoleCommandAsync(
                new RemoteControlCommandRequest(
                    _remoteControlOptions.HttpPort,
                    _remoteControlOptions.ObjectPath,
                    _remoteControlOptions.FunctionName,
                    _remoteControlOptions.CommandParameterName,
                    command),
                progress,
                cancellationToken);
        }
        catch (RemoteControlException exception)
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

    /// <summary>
    /// 为设备建立 Remote Control 端口转发，同一设备只执行一次。
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
                $"Forwarding TCP port {_remoteControlOptions.HttpPort} for {device.Id}."));

            await RunRequiredAsync(_adb.ForwardTcpAsync(
                device.Id,
                _remoteControlOptions.HttpPort,
                _remoteControlOptions.HttpPort,
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