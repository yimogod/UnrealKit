using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;

namespace UnrealKit.Core.Devices;

/// <summary>
/// 设备发现抽象。与 IDeviceService 分开：枚举设备不需要先持有某台设备，
/// 而针对设备的操作需要。合在一个接口里会迫使调用方「先有设备才能找设备」。
/// </summary>
public interface IDeviceProvider
{
    /// <summary>该提供者负责的平台。</summary>
    Projects.TargetPlatform Platform { get; }

    /// <summary>
    /// 列出该平台当前可用设备。
    /// </summary>
    Task<IReadOnlyList<IDevice>> ListDevicesAsync(
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 设备服务抽象。封装内存采集、文件拉取、控制台指令等针对单台设备的平台相关操作。
/// 平台之间的能力差异由 <see cref="Supports"/> 显式声明。
/// </summary>
public interface IDeviceService : IDeviceProvider
{
    /// <summary>
    /// 该平台是否支持指定能力。返回 false 时对应方法会抛出
    /// <see cref="DeviceCapabilityNotSupportedException"/>。
    /// </summary>
    bool Supports(DeviceCapability capability);

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
    /// 流式读取 UE 日志输出。平台不支持时抛出 <see cref="DeviceCapabilityNotSupportedException"/>，
    /// 不返回空流——空流无法与「有日志能力但暂时无输出」区分。
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
    /// 停止应用。
    /// </summary>
    Task<ProcessExecutionResult> StopApplicationAsync(
        IDevice device,
        string target,
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

    /// <summary>
    /// 安装应用包到设备（Android 为安装本地 APK）。平台不支持时抛出
    /// <see cref="DeviceCapabilityNotSupportedException"/>。
    /// </summary>
    Task<ProcessExecutionResult> InstallApplicationAsync(
        IDevice device,
        string localApplicationPath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}