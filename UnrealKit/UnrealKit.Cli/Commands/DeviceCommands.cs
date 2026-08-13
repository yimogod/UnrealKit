namespace UnrealKit.Cli;

/// <summary>`unrealkit devices`：跨平台列出设备。</summary>
internal static class DeviceCommands
{
    internal static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length > 0)
        {
            Console.Error.WriteLine("Usage: unrealkit devices");
            return 2;
        }

        var result = await DeviceResolver.CreateDeviceProvider(CliOptions.GetOptional(arguments, "--adb-path")).ListDevicesAsync();

        foreach (var device in result.Devices)
        {
            var status = device.IsAvailable ? "available" : "unavailable";
            Console.WriteLine($"{device.Id,-20} {device.Name,-30} {device.Platform,-10} {status}");
        }

        // 平台枚举失败不静默：报告原因，让「没有设备」与「无法枚举」可区分。
        foreach (var failure in result.Failures)
        {
            Console.Error.WriteLine($"Failed to list {failure.Platform} devices: {failure.Message}");
        }

        return 0;
    }
}
