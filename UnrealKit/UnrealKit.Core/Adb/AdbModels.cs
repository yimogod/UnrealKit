using UnrealKit.Core.Devices;
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
    string RawLine) : IDevice
{
    public bool IsAvailable => Status == AdbDeviceStatus.Device;

    string IDevice.Id => SerialNumber;
    string IDevice.Name => Model ?? DeviceName ?? SerialNumber;
    string IDevice.Platform => Projects.PlatformNames.Android;
    bool IDevice.IsAvailable => IsAvailable;
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