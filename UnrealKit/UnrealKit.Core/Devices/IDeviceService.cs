using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;

namespace UnrealKit.Core.Devices;

/// <summary>
/// 设备服务抽象。封装设备发现、内存采集、文件拉取、控制台指令等平台相关操作。
/// </summary>
public interface IDeviceService
{
    /// <summary>
    /// 列出当前可用设备。
    /// </summary>
    Task<IReadOnlyList<IDevice>> ListDevicesAsync(
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 采集目标进程的内存信息，返回平台原生输出文本。
    /// Android: dumpsys meminfo; Win64: 性能计数器。
    /// </summary>
    Task<ProcessExecutionResult> CaptureMemoryAsync(
        IDevice device,
        string target,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 从设备拉取目录到本地。
    /// </summary>
    Task<ProcessExecutionResult> PullDirectoryAsync(
        IDevice device,
        string remotePath,
        string localDirectory,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 向设备上运行中的 UE 进程发送控制台指令。
    /// </summary>
    Task<ProcessExecutionResult> SendConsoleCommandAsync(
        IDevice device,
        string command,
        string? target = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 流式读取 UE 日志输出。
    /// </summary>
    IAsyncEnumerable<string> StreamLogAsync(
        IDevice device,
        string? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动应用。
    /// </summary>
    Task<ProcessExecutionResult> StartApplicationAsync(
        IDevice device,
        string target,
        string? activity = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 推送文件到设备。
    /// </summary>
    Task<ProcessExecutionResult> PushFileAsync(
        IDevice device,
        string localPath,
        string remotePath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除设备上的文件。
    /// </summary>
    Task<ProcessExecutionResult> DeleteRemoteFileAsync(
        IDevice device,
        string remotePath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}