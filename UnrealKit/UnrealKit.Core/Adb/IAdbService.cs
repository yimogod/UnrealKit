using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;

namespace UnrealKit.Core.Adb;

public interface IAdbService
{
    /// <summary>
    /// 向运行中的 UE Android 应用发送控制台指令。使用 am broadcast 广播机制，零 UE 端配置。
    /// 如果提供了 packageName，将使用 -n 参数限定目标包名。
    /// </summary>
    Task<ProcessExecutionResult> SendConsoleCommandAsync(
        string serialNumber,
        string command,
        string? packageName = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

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

    /// <summary>
    /// 强制停止设备上的应用。
    /// </summary>
    Task<ProcessExecutionResult> ForceStopApplicationAsync(
        string serialNumber,
        string packageName,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 流式读取设备 logcat 输出，返回可取消的异步行流。
    /// </summary>
    IAsyncEnumerable<string> StreamLogcatAsync(
        string serialNumber,
        string? filter = null,
        CancellationToken cancellationToken = default);
}
