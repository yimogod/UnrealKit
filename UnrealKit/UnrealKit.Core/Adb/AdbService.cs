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

    public AdbService(IProcessRunner processRunner, string adbPath, IProgress<ProcessOutput>? output = null)
    {
        _processRunner = processRunner;
        ArgumentException.ThrowIfNullOrWhiteSpace(adbPath);
        _adbPath = adbPath;
        _output = output;
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
    public Task<ProcessExecutionResult> StartServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) =>
        RunRequiredAsync(["start-server"], progress, cancellationToken);

    /// <summary>
    /// 终止 ADB 服务器
    /// </summary>
    public Task<ProcessExecutionResult> KillServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) =>
        RunRequiredAsync(["kill-server"], progress, cancellationToken);

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
    /// 强制停止指定的应用程序
    /// </summary>
    public Task<ProcessExecutionResult> ForceStopApplicationAsync(string serialNumber, string packageName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        return RunDeviceCommandAsync(serialNumber, ["shell", "am", "force-stop", packageName], progress, cancellationToken);
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
    /// 运行 ADB 命令
    /// </summary>
    private async Task<ProcessExecutionResult> RunRequiredAsync(IReadOnlyList<string> arguments, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
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
    private Task<ProcessExecutionResult> RunAllowingFailureAsync(IReadOnlyList<string> arguments, IProgress<OperationProgress>? progress, CancellationToken cancellationToken) =>
        _processRunner.RunAsync(new ProcessExecutionRequest(_adbPath, arguments, Output: _output), progress, cancellationToken);

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
