using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;

namespace UnrealKit.Core.Adb;

public interface IAdbService
{
    Task<ProcessExecutionResult> GetVersionAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<ProcessExecutionResult> StartServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<ProcessExecutionResult> KillServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<ProcessExecutionResult> ConnectAsync(string endpoint, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<ProcessExecutionResult> DisconnectAsync(string endpoint, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<ProcessExecutionResult> TcpIpAsync(string serialNumber, int port, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<ProcessExecutionResult> StartApplicationAsync(string serialNumber, string packageName, string activityName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<ProcessExecutionResult> PushFileAsync(string serialNumber, string localPath, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<ProcessExecutionResult> PullDirectoryAsync(string serialNumber, string remotePath, string localDirectory, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<ProcessExecutionResult> DeleteRemoteFileAsync(string serialNumber, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<ProcessExecutionResult> RunDumpsysAsync(string serialNumber, string packageName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);
}
