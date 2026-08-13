using UnrealKit.Core.Adb;

namespace UnrealKit.Cli;

/// <summary>`unrealkit adb version|devices|connect|disconnect`。</summary>
internal static class AdbCommands
{
    internal static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            return FailUsage();
        }

        var (commandArguments, adbPath) = CliOptions.ParseAdbPath(arguments);
        var service = DeviceResolver.CreateAdbService(adbPath);
        return commandArguments[0].ToLowerInvariant() switch
        {
            "version" when commandArguments.Length == 1 => await ShowVersionAsync(service),
            "devices" when commandArguments.Length == 1 => await ListDevicesAsync(service),
            "connect" when commandArguments.Length == 2 => await ConnectAsync(service, commandArguments[1]),
            "disconnect" when commandArguments.Length == 2 => await DisconnectAsync(service, commandArguments[1]),
            _ => FailUsage()
        };
    }

    // 版本、连接、断开的可见输出来自 adb 自身的流式转发，这里只负责等待与退出码。
    private static async Task<int> ShowVersionAsync(IAdbService service)
    {
        await service.GetVersionAsync();
        return 0;
    }

    private static async Task<int> ListDevicesAsync(IAdbService service)
    {
        var devices = await service.ListDevicesAsync();
        foreach (var device in devices)
        {
            Console.WriteLine($"{device.SerialNumber}\t{device.Status}\t{device.ConnectionType}\t{device.Model ?? "Unknown model"}");
        }

        return 0;
    }

    private static async Task<int> ConnectAsync(IAdbService service, string endpoint)
    {
        await service.ConnectAsync(endpoint);
        return 0;
    }

    private static async Task<int> DisconnectAsync(IAdbService service, string endpoint)
    {
        await service.DisconnectAsync(endpoint);
        return 0;
    }

    private static int FailUsage()
    {
        Console.Error.WriteLine("Usage: unrealkit adb <version|devices|connect|disconnect> ... [--adb-path <path>]");
        return 2;
    }
}
