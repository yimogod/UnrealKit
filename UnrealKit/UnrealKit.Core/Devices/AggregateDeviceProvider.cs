using UnrealKit.Core.Operations;
using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Devices;

/// <summary>
/// 某个平台的设备枚举失败。枚举是「尽力而为」的：一个平台不可用不应让整份设备列表失败，
/// 但失败原因必须保留下来供调用方展示，不能静默吞掉。
/// </summary>
public sealed record DeviceDiscoveryFailure(TargetPlatform Platform, string Message);

/// <summary>
/// 跨平台设备枚举结果。包含成功枚举到的设备与各平台的失败原因。
/// </summary>
public sealed record DeviceDiscoveryResult(
    IReadOnlyList<IDevice> Devices,
    IReadOnlyList<DeviceDiscoveryFailure> Failures);

/// <summary>
/// 由标识符与平台构造的最小 IDevice 实现，用于调用方只持有 serial / 主机名的场景。
/// 各服务不再各自定义私有的设备包装类型。
/// </summary>
public sealed record DeviceReference(string Id, string Platform, string? DisplayName = null) : IDevice
{
    /// <summary>按平台创建设备引用。</summary>
    public static DeviceReference Create(string id, TargetPlatform platform, string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return new DeviceReference(id, PlatformNames.ToName(platform), displayName);
    }

    public string Name => DisplayName ?? Id;

    /// <summary>
    /// 设备引用不代表已验证的连接状态，因此始终为 true；
    /// 需要真实状态的调用方应从 IDeviceProvider 枚举结果取设备。
    /// </summary>
    public bool IsAvailable => true;
}

/// <summary>
/// 表示某平台的前置条件不满足（例如找不到 adb），枚举时报告失败原因而不是被静默省略。
/// </summary>
public sealed class UnavailableDeviceProvider : IDeviceProvider
{
    private readonly string _reason;

    public UnavailableDeviceProvider(TargetPlatform platform, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Platform = platform;
        _reason = reason;
    }

    public TargetPlatform Platform { get; }

    public Task<IReadOnlyList<IDevice>> ListDevicesAsync(
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(_reason);
}

/// <summary>
/// 聚合多个平台的设备枚举。GUI 与 CLI 共用此实现，
/// 不各自手写「先放一台 Win64 再 try-catch 追加 ADB」的列表拼装逻辑。
/// </summary>
public sealed class AggregateDeviceProvider
{
    private readonly IReadOnlyList<IDeviceProvider> _providers;

    public AggregateDeviceProvider(IReadOnlyList<IDeviceProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers;
    }

    /// <summary>
    /// 枚举所有平台的设备。单个平台枚举失败记入 Failures 并继续，
    /// 取消请求直接向外传播，不当作平台失败处理。
    /// </summary>
    public async Task<DeviceDiscoveryResult> ListDevicesAsync(
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var devices = new List<IDevice>();
        var failures = new List<DeviceDiscoveryFailure>();

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                devices.AddRange(await provider.ListDevicesAsync(progress, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(new DeviceDiscoveryFailure(provider.Platform, exception.Message));
            }
        }

        return new DeviceDiscoveryResult(devices, failures);
    }
}
