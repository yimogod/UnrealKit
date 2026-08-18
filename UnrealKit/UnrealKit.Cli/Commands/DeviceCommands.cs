using UnrealKit.Core.Devices;
using UnrealKit.Core.Projects;

namespace UnrealKit.Cli;

/// <summary>`unrealkit devices`：跨平台列出设备。</summary>
internal static class DeviceCommands
{
    internal static async Task<int> RunAsync(string[] arguments)
    {
        var (options, adbPath) = CliOptions.ParseAdbPath(arguments);
        CliOptions.EnsureOnly(options, CliOptions.Allowed("--project"));

        // 别名存在工程配置里，因此 --project 是别名列的前提；不传就只列设备本身，
        // 而不是去猜一个工程——猜错的工程会显示另一批设备的别名。
        var projectPath = CliOptions.GetOptional(options, "--project");
        var project = projectPath is null ? null : await new ProjectService().OpenProjectAsync(projectPath);

        var result = await DeviceResolver.CreateDeviceProvider(adbPath, project).ListDevicesAsync();
        var devices = DeviceDisplayInfo.CreateAll(result.Devices, project?.Settings);

        foreach (var device in devices)
        {
            var status = device.IsAvailable ? "available" : "unavailable";
            var line = $"{device.Id,-20} {device.Name,-30} {device.Platform,-10} {status}";

            // 只在配置了别名时追加一列：为没有别名的设备补一个占位符会让
            // 「未配置别名」看起来像是别名本身叫这个名字。
            Console.WriteLine(device.HasAlias ? $"{line,-70} {device.Alias}" : line);
        }

        // 平台枚举失败不静默：报告原因，让「没有设备」与「无法枚举」可区分。
        foreach (var failure in result.Failures)
        {
            Console.Error.WriteLine($"Failed to list {failure.Platform} devices: {failure.Message}");
        }

        return 0;
    }
}
