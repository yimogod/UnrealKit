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
/// ADB 设备选择异常
/// </summary>
public sealed class AdbDeviceSelectionException : InvalidOperationException
{
    public AdbDeviceSelectionException(string message)
        : base(message)
    {
    }
}