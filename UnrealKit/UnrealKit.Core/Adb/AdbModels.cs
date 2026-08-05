using UnrealKit.Core.Processes;

namespace UnrealKit.Core.Adb;

public enum AdbDeviceStatus
{
    Device,
    Offline,
    Unauthorized,
    NoPermissions,
    Unknown
}

public enum AdbConnectionType
{
    Usb,
    Network,
    Unknown
}

public sealed record AdbDevice(
    string SerialNumber,
    AdbDeviceStatus Status,
    string? Product,
    string? Model,
    string? DeviceName,
    AdbConnectionType ConnectionType,
    string RawLine)
{
    public bool IsAvailable => Status == AdbDeviceStatus.Device;
}

public sealed class AdbCommandException : Exception
{
    public AdbCommandException(string message, ProcessExecutionResult result)
        : base(message)
    {
        Result = result;
    }

    public ProcessExecutionResult Result { get; }
}

public sealed class AdbDeviceSelectionException : InvalidOperationException
{
    public AdbDeviceSelectionException(string message)
        : base(message)
    {
    }
}
