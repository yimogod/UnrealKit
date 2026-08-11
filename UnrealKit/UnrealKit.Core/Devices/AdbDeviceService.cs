using UnrealKit.Core.Adb;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;

namespace UnrealKit.Core.Devices;

/// <summary>
/// IDeviceService implementation that wraps IAdbService and normalizes ADB errors into the common
/// throw-on-failure protocol via RunRequiredAsync.
/// </summary>
public sealed class AdbDeviceService : IDeviceService
{
    private readonly IAdbService _adb;

    public AdbDeviceService(IAdbService adb)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    }

    public async Task<IReadOnlyList<IDevice>> ListDevicesAsync(
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var adbDevices = await _adb.ListDevicesAsync(progress, cancellationToken);
        return adbDevices.Select(d => (IDevice)new AdbDeviceWrapper(d)).ToList();
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

    public Task<ProcessExecutionResult> SendConsoleCommandAsync(
        IDevice device,
        string command,
        string? target = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return RunRequiredAsync(_adb.SendConsoleCommandAsync(device.Id, command, target, progress, cancellationToken));
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
    /// Adapts an AdbDevice into an IDevice for CLI / Desktop use.
    /// </summary>
    public sealed class AdbDeviceWrapper : IDevice
    {
        private readonly AdbDevice _adbDevice;

        public AdbDeviceWrapper(AdbDevice adbDevice)
        {
            _adbDevice = adbDevice;
        }

        public string Id => _adbDevice.SerialNumber;
        public string Name => _adbDevice.Model ?? _adbDevice.SerialNumber;
        public string Platform => "Android";
        public bool IsAvailable => _adbDevice.IsAvailable;
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