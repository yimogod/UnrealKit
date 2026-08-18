using UnrealKit.Core.Devices;
using UnrealKit.Core.Processes;

namespace UnrealKit.Core.Adb;

/// <summary>
/// ADB 设备状态
/// </summary>
public enum AdbDeviceStatus
{
    Device,
    Offline,
    Unauthorized,
    NoPermissions,
    Unknown
}

/// <summary>
/// ADB 连接类型
/// </summary>
public enum AdbConnectionType
{
    Usb,
    Network,
    Unknown
}

/// <summary>
/// ADB 设备数据对象
/// </summary>
public sealed record AdbDevice(
    string SerialNumber,
    AdbDeviceStatus Status,
    string? Product,
    string? Model,
    string? DeviceName,
    AdbConnectionType ConnectionType,
    string RawLine) : IDevice
{
    public bool IsAvailable => Status == AdbDeviceStatus.Device;

    string IDevice.Id => SerialNumber;
    string IDevice.Name => Model ?? DeviceName ?? SerialNumber;
    string IDevice.Platform => Projects.PlatformNames.Android;
    bool IDevice.IsAvailable => IsAvailable;
}

/// <summary>
/// ADB 命令执行异常
/// </summary>
public sealed class AdbCommandException : Exception
{
    public AdbCommandException(string message, ProcessExecutionResult result)
        : base(message)
    {
        Result = result;
    }

    public ProcessExecutionResult Result { get; }
}

/// <summary>
/// 设备上查不到任何可用 IPv4 地址。携带尝试过的命令，供用户判断是设备真的没联网还是查询本身没成功。
/// </summary>
public sealed class AdbDeviceAddressUnavailableException : InvalidOperationException
{
    public AdbDeviceAddressUnavailableException(string serialNumber, IReadOnlyList<string> attemptedCommands)
        : base(BuildMessage(serialNumber, attemptedCommands))
    {
        SerialNumber = serialNumber;
        AttemptedCommands = attemptedCommands;
    }

    public string SerialNumber { get; }

    /// <summary>已执行过的查询命令，按尝试顺序。</summary>
    public IReadOnlyList<string> AttemptedCommands { get; }

    private static string BuildMessage(string serialNumber, IReadOnlyList<string> attemptedCommands) =>
        $"设备 {serialNumber} 上未查到 IPv4 地址。已尝试：{string.Join("；", attemptedCommands)}。" +
        "设备可能未连接任何网络，或固件裁剪了 ip 命令。";
}

/// <summary>
/// ADB 设备选择异常
/// </summary>
public sealed class AdbDeviceSelectionException : InvalidOperationException
{
    public AdbDeviceSelectionException(string message)
        : base(message)
    {
    }
}