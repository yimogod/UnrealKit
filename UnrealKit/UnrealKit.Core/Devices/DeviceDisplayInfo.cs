using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Devices;

/// <summary>
/// 设备在列表中的展示信息：设备标识、状态、型号，以及工程配置的别名。
///
/// 别名解析放在 Core 而不是各展示层，避免 GUI 与 CLI 各写一遍「配了就显示别名，
/// 没配就显示标识」的规则，两处规则一旦分叉，同一台设备在两个界面里名字不同。
///
/// 不实现 <see cref="IDevice"/>：它是展示投影，不是可操作的设备。让它冒充设备会让
/// 采集、指令等操作接受一个展示对象，绕过设备枚举得到的真实状态。原设备由
/// <see cref="Device"/> 原样保留，操作仍传它。
/// </summary>
public sealed record DeviceDisplayInfo(IDevice Device, string? Alias)
{
    /// <summary>
    /// 按工程配置解析别名。<paramref name="settings"/> 为 null（尚未打开工程）时别名为 null，
    /// 设备列表照常可用——别名只是附加信息，缺它不该让列表不可用。
    /// </summary>
    public static DeviceDisplayInfo Create(IDevice device, ProjectSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(device);
        return new DeviceDisplayInfo(device, settings?.TryGetDeviceAlias(device.Id));
    }

    public static IReadOnlyList<DeviceDisplayInfo> CreateAll(IEnumerable<IDevice> devices, ProjectSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(devices);
        return devices.Select(device => Create(device, settings)).ToArray();
    }

    /// <summary>设备标识。Android 为 ADB 序列号。</summary>
    public string Id => Device.Id;

    /// <summary>设备自报的型号名。</summary>
    public string Name => Device.Name;

    public string Platform => Device.Platform;

    public bool IsAvailable => Device.IsAvailable;

    /// <summary>
    /// 状态列文本，沿用 ADB 自己的 device/offline 措辞，便于与 `adb devices` 的输出对照。
    /// 放在这里而不是各视图里翻译布尔值：GUI 列表、选中设备摘要、CLI 都取同一份文本，
    /// 否则同一台设备在不同位置可能显示成「离线」「offline」「不可用」三种说法。
    /// </summary>
    public string StatusText => IsAvailable ? "device" : "offline";

    /// <summary>是否配置过别名。界面据此区分「有别名」与「只有标识」。</summary>
    public bool HasAlias => !string.IsNullOrWhiteSpace(Alias);

    /// <summary>
    /// 列表中的主标签：有别名时用别名，否则回落到型号名。
    /// 设备标识始终独立显示，不并进这里——那会让「别名」与「标识」在同一列里混淆。
    /// </summary>
    public string DisplayLabel => HasAlias ? Alias! : Name;
}
