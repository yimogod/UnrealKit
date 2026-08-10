using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;

namespace UnrealKit.Core.Adb;

public sealed class AdbService : IAdbService
{
    private readonly IProcessRunner _processRunner;
    private readonly string _adbPath;
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

    public Task<ProcessExecutionResult> StartServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) =>
        RunRequiredAsync(["start-server"], progress, cancellationToken);

    public Task<ProcessExecutionResult> KillServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) =>
        RunRequiredAsync(["kill-server"], progress, cancellationToken);

    public Task<ProcessExecutionResult> ConnectAsync(string endpoint, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateEndpoint(endpoint);
        return RunRequiredAsync(["connect", endpoint], progress, cancellationToken);
    }

    public Task<ProcessExecutionResult> DisconnectAsync(string endpoint, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateEndpoint(endpoint);
        return RunRequiredAsync(["disconnect", endpoint], progress, cancellationToken);
    }

    public Task<ProcessExecutionResult> TcpIpAsync(string serialNumber, int port, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        return RunDeviceCommandAsync(serialNumber, ["tcpip", port.ToString(System.Globalization.CultureInfo.InvariantCulture)], progress, cancellationToken);
    }

    public Task<ProcessExecutionResult> StartApplicationAsync(string serialNumber, string packageName, string activityName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);
        ValidatePackageName(packageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityName);
        return RunDeviceCommandAsync(serialNumber, ["shell", "am", "start", "-n", $"{packageName}/{activityName}"], progress, cancellationToken);
    }

    public Task<ProcessExecutionResult> PushFileAsync(string serialNumber, string localPath, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        ValidateRemotePath(remotePath);
        return RunDeviceCommandAsync(serialNumber, ["push", Path.GetFullPath(localPath), remotePath], progress, cancellationToken);
    }

    public Task<ProcessExecutionResult> PullDirectoryAsync(string serialNumber, string remotePath, string localDirectory, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);
        ValidateRemotePath(remotePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(localDirectory);
        return RunDeviceCommandAsync(serialNumber, ["pull", remotePath, Path.GetFullPath(localDirectory)], progress, cancellationToken);
    }

    public Task<ProcessExecutionResult> DeleteRemoteFileAsync(string serialNumber, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);
        ValidateRemotePath(remotePath);
        return RunDeviceCommandAsync(serialNumber, ["shell", "rm", "-f", "--", remotePath], progress, cancellationToken);
    }

    
    
    public Task<ProcessExecutionResult> ForceStopApplicationAsync(string serialNumber, string packageName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        return RunDeviceCommandAsync(serialNumber, ["shell", "am", "force-stop", packageName], progress, cancellationToken);
    }

    public Task<ProcessExecutionResult> SendConsoleCommandAsync(string serialNumber, string command, string? packageName = null, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        var arguments = new List<string> { "shell", "am", "broadcast", "-a", "android.intent.action.RUN", "-e", "cmd", command };
        if (!string.IsNullOrWhiteSpace(packageName))
        {
            ValidatePackageName(packageName);
            arguments.Add(packageName);
        }

        return RunDeviceCommandAsync(serialNumber, arguments, progress, cancellationToken);
    }
public Task<ProcessExecutionResult> RunDumpsysAsync(string serialNumber, string packageName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateSerialNumber(serialNumber);
        ValidatePackageName(packageName);
        return RunDeviceCommandAsync(serialNumber, ["shell", "dumpsys", "meminfo", packageName], progress, cancellationToken);
    }

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

    // Expose _adbPath for logcat streaming
    private string AdbPath => _adbPath;

    private Task<ProcessExecutionResult> RunDeviceCommandAsync(string serialNumber, IReadOnlyList<string> arguments, IProgress<OperationProgress>? progress, CancellationToken cancellationToken) =>
        RunRequiredAsync(["-s", serialNumber, .. arguments], progress, cancellationToken);

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

    private static void ValidateSerialNumber(string serialNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);
        if (serialNumber.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("ADB 设备序列号不能包含空白字符。", nameof(serialNumber));
        }
    }

    private static void ValidateEndpoint(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        if (endpoint.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("ADB 网络端点不能包含空白字符。", nameof(endpoint));
        }
    }

    private static void ValidatePackageName(string packageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        if (packageName.Any(char.IsWhiteSpace) || !packageName.Contains('.', StringComparison.Ordinal))
        {
            throw new ArgumentException("Android 包名无效。", nameof(packageName));
        }
    }

    private static void ValidateRemotePath(string remotePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        if (!remotePath.StartsWith("/", StringComparison.Ordinal) || remotePath.Contains('\\') || remotePath.Contains('\0'))
        {
            throw new ArgumentException("ADB 远端路径必须是 Unix 风格的绝对路径。", nameof(remotePath));
        }
    }
}
