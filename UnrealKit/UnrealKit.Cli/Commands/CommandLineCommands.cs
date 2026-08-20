using UnrealKit.Core.Launch;
using UnrealKit.Core.Projects;

namespace UnrealKit.Cli;

/// <summary>`unrealkit commandline push|delete`：管理设备上的 uecommandline.txt。</summary>
internal static class CommandLineCommands
{
    internal static async Task<int> RunAsync(string[] arguments)
    {
        var (commandArguments, adbPath) = CliOptions.ParseAdbPath(arguments);
        if (commandArguments.Length == 0)
        {
            return FailUsage();
        }

        var options = commandArguments[1..];
        var project = await new ProjectService().OpenProjectAsync(CliOptions.GetRequired(options, "--project"));
        var resolved = await DeviceResolver.ResolveDeviceTargetAsync(project, options, adbPath);
        var serialNumber = resolved.DeviceId;
        var service = new LaunchParameterService(resolved.DeviceService);

        switch (commandArguments[0].ToLowerInvariant())
        {
            case "push":
            {
                var result = await service.PushAsync(project, new LaunchParameterRequest(
                    serialNumber,
                    CliOptions.GetAll(options, "--preset"),
                    CliOptions.GetOptional(options, "--custom")));
                Console.WriteLine($"Pushed uecommandline.txt to {result.RemotePath}");
                Console.WriteLine("Content:");
                Console.WriteLine(result.Content);
                return 0;
            }

            case "delete":
                await service.DeleteAsync(project, serialNumber);
                return 0;

            default:
                return FailUsage();
        }
    }

    private static int FailUsage()
    {
        Console.Error.WriteLine("Usage: unrealkit commandline <push|delete> --project <project.ukit> --device <serial> [--preset <name>] [--custom <arguments>] [--adb-path <path>]");
        return 2;
    }
}
