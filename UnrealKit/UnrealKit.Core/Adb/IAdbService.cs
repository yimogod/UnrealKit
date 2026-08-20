using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;

namespace UnrealKit.Core.Adb;

public interface IAdbService
{
    /// <summary>
    /// 将本机 TCP 端口转发到设备的 Remote Control HTTP 端口。
    /// </summary>
    Task<ProcessExecutionResult> ForwardTcpAsync(
        string serialNumber,
        int hostPort,
        int devicePort,
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

    /// <summary>
    /// 读取设备上指定文本文件的内容。文件不存在等非零退出码不抛异常，
    /// 原样返回结果，由调用方按 <see cref="ProcessExecutionResult.Succeeded"/> 与
    /// <see cref="ProcessExecutionResult.StandardError"/> 区分「文件不存在」和「读取失败」。
    /// </summary>
    Task<ProcessExecutionResult> ReadFileAsync(string serialNumber, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<ProcessExecutionResult> RunDumpsysAsync(string serialNumber, string packageName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询设备当前的 IPv4 地址，按接口逐项返回。
    /// </summary>
    /// <remarks>
    /// 这是一次真实的设备 shell 调用，比列举设备慢，且设备离线或未授权时会失败，
    /// 因此不并入 <see cref="ListDevicesAsync"/>——设备列表刷新不应为此变慢或多一个失败点。
    /// 一台设备可能同时有 WiFi、蜂窝、USB 网络共享和 VPN 地址，故返回列表而非单值，
    /// 由调用方按 <see cref="DeviceNetworkInterfaceKind"/> 决定取哪一个。
    /// 一个地址都没有时抛出 <see cref="AdbDeviceAddressUnavailableException"/>，不返回空列表——
    /// 「没连任何网络」和「查询没跑起来」必须可区分。
    /// </remarks>
    Task<IReadOnlyList<DeviceIpAddress>> GetIpAddressesAsync(
        string serialNumber,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 强制停止设备上的应用。
    /// </summary>
    Task<ProcessExecutionResult> ForceStopApplicationAsync(
        string serialNumber,
        string packageName,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 安装本地 APK 到设备（<c>adb install -r</c>，允许覆盖同版本重装）。
    /// </summary>
    Task<ProcessExecutionResult> InstallApkAsync(
        string serialNumber,
        string localApkPath,
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
