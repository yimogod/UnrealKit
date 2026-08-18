namespace UnrealKit.Core.Adb;

/// <summary>
/// 设备网络接口类型。一台设备可能同时持有 WiFi、蜂窝、USB 网络共享和 VPN 地址，
/// 调用方需要知道拿到的是哪一个——写死 wlan0 在没连 WiFi 的机器上只会得到空结果。
/// </summary>
public enum DeviceNetworkInterfaceKind
{
    /// <summary>WiFi（wlan*）。</summary>
    WiFi,

    /// <summary>蜂窝数据（rmnet*、ccmni*）。</summary>
    Cellular,

    /// <summary>USB 网络共享（rndis*、usb*、ncm*）。</summary>
    UsbTethering,

    /// <summary>VPN 或点对点隧道（tun*、ppp*）。</summary>
    Vpn,

    /// <summary>其它接口（网桥、以太网、厂商私有命名等）。</summary>
    Other
}

/// <summary>
/// 设备上的一个 IPv4 地址。<paramref name="PrefixLength"/> 在来源命令未给出掩码时为 null，
/// 不用 0 或 32 代替——那会把「未知」伪装成事实。
/// </summary>
public sealed record DeviceIpAddress(
    string InterfaceName,
    string Address,
    int? PrefixLength,
    DeviceNetworkInterfaceKind Kind)
{
    /// <summary>形如 <c>wlan0 192.168.1.23/24</c>，用于日志与 CLI 输出。</summary>
    public override string ToString() =>
        PrefixLength is null ? $"{InterfaceName} {Address}" : $"{InterfaceName} {Address}/{PrefixLength}";
}
