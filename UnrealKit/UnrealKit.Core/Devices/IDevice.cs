namespace UnrealKit.Core.Devices;

/// <summary>
/// 设备抽象。Android 设备 / Win64 本机均实现此接口，供 Capture、Console、Launch 层使用。
/// </summary>
public interface IDevice
{
    /// <summary>
    /// 设备唯一标识。Android: serialNumber; Win64: hostname 或 "localhost"。
    /// </summary>
    string Id { get; }

    /// <summary>
    /// 设备显示名称。Android: model; Win64: Environment.MachineName。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 平台标识。取值必须来自 <see cref="Projects.PlatformNames"/>，
    /// 不要写字面量——那会让平台名散落各处、改一处漏三处。
    /// 需要枚举时用 <c>PlatformNames.Parse</c> 转换。
    /// </summary>
    string Platform { get; }

    /// <summary>
    /// 设备当前是否可用。
    /// </summary>
    bool IsAvailable { get; }
}