using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;

namespace UnrealKit.Core.Adb;

/// <summary>
/// ADB 服务
/// </summary>
public sealed class AdbService : IAdbService
{
    // 进程运行器
    private readonly IProcessRunner _processRunner;
    
    // ADB 可执行文件路径
    private readonly string _adbPath;
    
    // 进度报告
    private readonly IProgress<ProcessOutput>? _output;

    /// <summary>
    /// 「adb server 已在运行」标记。adb 客户端每次调用都会在 server 未启动时自行拉起它，
    /// 那次冷启动的代价会落在一条真实命令的超时窗口内；这里用一次显式的 start-server 前置掉，
    /// 之后所有命令直接发出。标记跨实例共享，见 <see cref="AdbServerLatch"/>。
    /// </summary>
    private readonly AdbServerLatch _serverLatch;

    /// <param name="serverLatch">
    /// 共享的 server 启动标记。省略时本实例独享一个，即每个实例各自确保一次——
    /// 每次操作都新建服务的调用方应显式传入同一个实例。
    /// </param>
    public AdbService(
        IProcessRunner processRunner,
        string adbPath,
        IProgress<ProcessOutput>? output = null,
        AdbServerLatch? serverLatch = null)
    {
        _processRunner = processRunner;
        ArgumentException.ThrowIfNullOrWhiteSpace(adbPath);
        _adbPath = adbPath;
        _output = output;
        _serverLatch = serverLatch ?? new AdbServerLatch();
    }

    public Task<ProcessExecutionResult> GetVersionAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) =>
        RunRequiredAsync(["version"], progress, cancellationToken);

    public async Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = await RunRequiredAsync(["devices", "-l"], progress, cancellationToken);
        return AdbDeviceParser.Parse(result.StandardOutput);
    }

    /// <summary>
    /// 启动 ADB 服务器
    /// </summary>
    public async Task<ProcessExecutionResult> StartServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = await RunCoreRequiredAsync(["start-server"], progress, cancellationToken);
        await _serverLatch.EnsureStartedAsync(_ => Task.FromResult(true), cancellationToken);
        return result;
    }

    /// <summary>
    /// 终止 ADB 服务器
    /// </summary>
    public async Task<ProcessExecutionResult> KillServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        // server 已被杀掉，下一条命令必须重新确保它在运行——留着标记会让后续命令
        // 跳过 start-server，把冷启动代价又推回那条命令自己。
        var result = await RunCoreRequiredAsync(["kill-server"], progress, cancellationToken);
        _serverLatch.Reset();
        return result;
    }

    /// <summary>
    /// 连接到 ADB 服务器
    /// </summary>
    public Task<ProcessExecutionResult> ConnectAsync(string endpoint, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateEndpoint(endpoint);
        return RunRequiredAsync(["connect", endpoint], progress, cancellationToken);
    }

    /// <summary>
    /// 断开与 ADB 服务器的连接
    /// </summary>
    public Task<ProcessExecutionResult> DisconnectAsync(string endpoint, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateEndpoint(endpoint);
        return RunRequiredAsync(["disconnect", endpoint], progress, cancellationToken);
    }

    /// <summary>
    /// 将 ADB 服务器绑定到指定的 TCP 端口
    /// </summary>
    public Task<ProcessExecutionResult> TcpIpAsync(string serialNumber, int port, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        return RunDeviceCommandAsync(serialNumber, ["tcpip", port.ToString(System.Globalization.CultureInfo.InvariantCulture)], progress, cancellationToken);
    }

    /// <summary>
    /// 启动指定的应用程序
    /// </summary>
    public Task<ProcessExecutionResult> StartApplicationAsync(string serialNumber, string packageName, string activityName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);
        ValidatePackageName(packageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityName);
        return RunDeviceCommandAsync(serialNumber, ["shell", "am", "start", "-n", $"{packageName}/{activityName}"], progress, cancellationToken);
    }

    /// <summary>
    /// 将本地文件推送到设备
    /// </summary>
    public Task<ProcessExecutionResult> PushFileAsync(string serialNumber, string localPath, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        ValidateRemotePath(remotePath);
        return RunDeviceCommandAsync(serialNumber, ["push", Path.GetFullPath(localPath), remotePath], progress, cancellationToken);
    }

    /// <summary>
    /// 从设备拉取目录到本地
    /// </summary>
    public Task<ProcessExecutionResult> PullDirectoryAsync(string serialNumber, string remotePath, string localDirectory, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);
        ValidateRemotePath(remotePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(localDirectory);
        return RunDeviceCommandAsync(serialNumber, ["pull", remotePath, Path.GetFullPath(localDirectory)], progress, cancellationToken);
    }

    /// <summary>
    /// 从设备删除指定的文件
    /// </summary>
    public Task<ProcessExecutionResult> DeleteRemoteFileAsync(string serialNumber, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);
        ValidateRemotePath(remotePath);
        return RunDeviceCommandAsync(serialNumber, ["shell", "rm", "-f", "--", remotePath], progress, cancellationToken);
    }

    /// <summary>
    /// 读取设备上指定文本文件的内容（adb shell cat）。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="RunAllowingFailureAsync"/> 而非 RunRequiredAsync：文件不存在是
    /// 「尚未投放启动参数」的正常状态，不应抛异常，而应原样返回非零退出码与
    /// <c>No such file or directory</c>，由调用方呈现。
    /// </remarks>
    public Task<ProcessExecutionResult> ReadFileAsync(string serialNumber, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);
        ValidateRemotePath(remotePath);
        return RunAllowingFailureAsync(["-s", serialNumber, "shell", "cat", "--", remotePath], progress, cancellationToken);
    }

    /// <summary>
    /// 强制停止指定的应用程序
    /// </summary>
    public Task<ProcessExecutionResult> ForceStopApplicationAsync(string serialNumber, string packageName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        return RunDeviceCommandAsync(serialNumber, ["shell", "am", "force-stop", packageName], progress, cancellationToken);
    }

    /// <summary>
    /// 安装本地 APK 到设备
    /// </summary>
    public Task<ProcessExecutionResult> InstallApkAsync(string serialNumber, string localApkPath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(localApkPath);
        return RunDeviceCommandAsync(serialNumber, ["install", "-r", Path.GetFullPath(localApkPath)], progress, cancellationToken);
    }

    /// <summary>
    /// 将主机端口转发到设备端口
    /// </summary>
    public Task<ProcessExecutionResult> ForwardTcpAsync(string serialNumber, int hostPort, int devicePort, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hostPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hostPort, 65535);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(devicePort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(devicePort, 65535);
        return RunDeviceCommandAsync(
            serialNumber,
            ["forward", $"tcp:{hostPort}", $"tcp:{devicePort}"],
            progress,
            cancellationToken);
    }

    /// <summary>
    /// 运行 dumpsys 命令
    /// </summary>
    public Task<ProcessExecutionResult> RunDumpsysAsync(string serialNumber, string packageName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);
        ValidatePackageName(packageName);
        return RunDeviceCommandAsync(serialNumber, ["shell", "dumpsys", "meminfo", packageName], progress, cancellationToken);
    }

    /// <summary>
    /// 查询设备当前的 IPv4 地址
    /// </summary>
    /// <remarks>
    /// 先用 <c>ip -f inet addr</c>，它同时给出接口名与前缀长度；该命令在部分裁剪过的固件上不可用，
    /// 此时退到 <c>ip route</c> 取 <c>src</c> 地址（没有前缀长度）。
    /// 两条都失败时抛 <see cref="AdbDeviceAddressUnavailableException"/> 并列出尝试过的命令。
    /// 不用 <c>getprop dhcp.wlan0.ipaddress</c>：新版 Android 上该属性经常为空，作为主路径会静默返回错误答案。
    /// </remarks>
    public async Task<IReadOnlyList<DeviceIpAddress>> GetIpAddressesAsync(
        string serialNumber,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);

        var attempts = new List<string>();

        var addresses = await TryQueryAsync(
            ["shell", "ip", "-f", "inet", "addr"],
            AdbNetworkParser.ParseAddresses);
        if (addresses.Count > 0)
        {
            return addresses;
        }

        addresses = await TryQueryAsync(
            ["shell", "ip", "route"],
            AdbNetworkParser.ParseRouteSourceAddresses);
        if (addresses.Count > 0)
        {
            return addresses;
        }

        throw new AdbDeviceAddressUnavailableException(serialNumber, attempts);

        async Task<IReadOnlyList<DeviceIpAddress>> TryQueryAsync(
            string[] arguments,
            Func<string, IReadOnlyList<DeviceIpAddress>> parse)
        {
            attempts.Add($"adb -s {serialNumber} {string.Join(' ', arguments)}");
            var result = await RunAllowingFailureAsync(
                ["-s", serialNumber, .. arguments],
                progress,
                cancellationToken);

            // 命令不存在或权限不足都表现为非零退出码，此时交给下一条命令，
            // 不在这里抛——单条命令失败不等于设备没有地址。
            return result.Succeeded ? parse(result.StandardOutput) : [];
        }
    }

    /// <summary>
    /// 流式传输 logcat 日志
    /// </summary>
    public async IAsyncEnumerable<string> StreamLogcatAsync(
        string serialNumber,
        string? filter = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);

        // logcat 直接起进程而不经 RunRequiredAsync，因此在这里也走一次确保：
        // 否则 server 冷启动的输出会混进日志流的头几行。
        await EnsureServerStartedAsync(null, cancellationToken);

        var arguments = new List<string> { "-s", serialNumber, "logcat", "-v", "threadtime" };
        if (!string.IsNullOrWhiteSpace(filter))
        {
            arguments.Add("-e");
            arguments.Add(filter);
        }

        var processArguments = arguments.ToArray();
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = _adbPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        foreach (var arg in processArguments)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();

        using var registration = cancellationToken.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        });

        try
        {
            while (!process.StandardOutput.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is not null)
                    yield return line;
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                process.WaitForExit();
            }
        }
    }

    /// <summary>
    /// 运行设备命令
    /// </summary>
    private Task<ProcessExecutionResult> RunDeviceCommandAsync(string serialNumber, IReadOnlyList<string> arguments, IProgress<OperationProgress>? progress, CancellationToken cancellationToken) =>
        RunRequiredAsync(["-s", serialNumber, .. arguments], progress, cancellationToken);

    /// <summary>
    /// 确保 adb server 已在运行。共享同一个 <see cref="AdbServerLatch"/> 的所有实例合计只发一次 start-server。
    ///
    /// 失败不抛也不置标记：server 起不来时真正的命令会带着自己的退出码与 stderr 失败，
    /// 那条信息比这里的二手报错更有用；标记不置位则下一条命令会再试一次。
    /// </summary>
    private Task EnsureServerStartedAsync(IProgress<OperationProgress>? progress, CancellationToken cancellationToken) =>
        _serverLatch.EnsureStartedAsync(
            async token =>
            {
                var result = await _processRunner.RunAsync(
                    new ProcessExecutionRequest(_adbPath, ["start-server"], Output: _output),
                    progress,
                    token);
                return result.Succeeded;
            },
            cancellationToken);

    /// <summary>
    /// 运行 ADB 命令
    /// </summary>
    private async Task<ProcessExecutionResult> RunRequiredAsync(IReadOnlyList<string> arguments, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        await EnsureServerStartedAsync(progress, cancellationToken);
        return await RunCoreRequiredAsync(arguments, progress, cancellationToken);
    }

    /// <summary>
    /// 运行 ADB 命令，不经 <see cref="EnsureServerStartedAsync"/>。
    /// 供 server 生命周期自身的命令使用，避免 start-server 递归确保 server。
    /// </summary>
    private async Task<ProcessExecutionResult> RunCoreRequiredAsync(IReadOnlyList<string> arguments, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(new ProcessExecutionRequest(_adbPath, arguments, Output: _output), progress, cancellationToken);
        if (result.Succeeded)
        {
            return result;
        }

        var command = string.Join(' ', arguments);
        throw new AdbCommandException($"ADB 命令失败，退出码 {result.ExitCode}: {_adbPath} {command}", result);
    }

    /// <summary>
    /// 运行 ADB 命令并原样返回结果，非零退出码不抛异常。
    /// 供「多条命令依次探测、单条失败可接受」的场景使用；调用方必须自己检查 <see cref="ProcessExecutionResult.Succeeded"/>。
    /// </summary>
    private async Task<ProcessExecutionResult> RunAllowingFailureAsync(IReadOnlyList<string> arguments, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        await EnsureServerStartedAsync(progress, cancellationToken);
        return await _processRunner.RunAsync(new ProcessExecutionRequest(_adbPath, arguments, Output: _output), progress, cancellationToken);
    }

    /// <summary>
    /// 验证 ADB 设备序列号
    /// </summary>
    private static void ValidateSerialNumber(string serialNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);
        if (serialNumber.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("ADB 设备序列号不能包含空白字符。", nameof(serialNumber));
        }
    }

    /// <summary>
    /// 验证 ADB 网络端点
    /// </summary>
    private static void ValidateEndpoint(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        if (endpoint.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("ADB 网络端点不能包含空白字符。", nameof(endpoint));
        }
    }

    /// <summary>
    /// 验证 Android 包名
    /// </summary>
    private static void ValidatePackageName(string packageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        if (packageName.Any(char.IsWhiteSpace) || !packageName.Contains('.', StringComparison.Ordinal))
        {
            throw new ArgumentException("Android 包名无效。", nameof(packageName));
        }
    }

    /// <summary>
    /// 验证 ADB 远端路径
    /// </summary>
    private static void ValidateRemotePath(string remotePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        if (!remotePath.StartsWith("/", StringComparison.Ordinal) || remotePath.Contains('\\') || remotePath.Contains('\0'))
        {
            throw new ArgumentException("ADB 远端路径必须是 Unix 风格的绝对路径。", nameof(remotePath));
        }
    }
}
