using UnrealKit.Core.Processes;

namespace UnrealKit.Core.Devices;

/// <summary>
/// Exception thrown when a device operation fails, carrying the ProcessExecutionResult for diagnostics.
/// </summary>
public sealed class DeviceCommandException : Exception
{
    public DeviceCommandException(string message, ProcessExecutionResult result)
        : base(message)
    {
        Result = result;
    }

    public DeviceCommandException(string message, ProcessExecutionResult result, Exception innerException)
        : base(message, innerException)
    {
        Result = result;
    }

    public ProcessExecutionResult Result { get; }
}
