using System.Text.Json;
using UnrealKit.Core.Projects;

namespace UnrealKit.Cli;

/// <summary>`unrealkit project create|info|validate`。</summary>
internal static class ProjectCommands
{
    internal static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            return FailUsage();
        }

        var service = new ProjectService();
        return arguments[0].ToLowerInvariant() switch
        {
            "create" => await CreateAsync(service, arguments[1..]),
            "info" => await ShowInfoAsync(service, arguments[1..]),
            "validate" => await ValidateAsync(service, arguments[1..]),
            _ => FailUsage()
        };
    }

    private static async Task<int> CreateAsync(IProjectService service, string[] arguments)
    {
        var directory = CliOptions.GetPositional(arguments, 0);
        var name = CliOptions.GetOptional(arguments, "--name");

        // --platform 可重复：一个工程可以同时配置多个平台。不指定则全部平台都给默认配置。
        var platforms = CliOptions.GetCommaSeparated(arguments, "--platform")
            .Select(value => PlatformNames.Parse(value, "--platform"))
            .Distinct()
            .ToArray();

        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("Usage: unrealkit project create <directory> --name <name> [--platform Android|Win64] ...");
            return 2;
        }

        var result = await service.CreateProjectAsync(new CreateProjectRequest(directory, name));
        Console.WriteLine($"Created project: {result.Project.ProjectFilePath}");

        if (platforms.Length > 0)
        {
            var settings = result.Project.Settings with
            {
                Android = platforms.Contains(TargetPlatform.Android) ? AndroidPlatformProfile.CreateDefaults() : null,
                Win64 = platforms.Contains(TargetPlatform.Win64) ? Win64PlatformProfile.CreateDefaults() : null
            };
            await service.UpdateSettingsAsync(result.Project, settings);
            Console.WriteLine($"Configured platforms: {string.Join(", ", settings.ConfiguredPlatforms)}");
        }

        return CliOutput.WriteValidation(result.Validation);
    }

    private static async Task<int> ShowInfoAsync(IProjectService service, string[] arguments)
    {
        var json = arguments.Length == 3
            && string.Equals(arguments[1], "--format", StringComparison.OrdinalIgnoreCase)
            && string.Equals(arguments[2], "json", StringComparison.OrdinalIgnoreCase);
        if (arguments.Length != 1 && !json)
        {
            Console.Error.WriteLine("Usage: unrealkit project info <project.ukit> [--format json]");
            return 2;
        }

        var project = await service.OpenProjectAsync(arguments[0]);
        var validation = await service.ValidateProjectAsync(arguments[0]);
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { project, validation }, new JsonSerializerOptions { WriteIndented = true }));
            return validation.IsValid ? 0 : 1;
        }

        Console.WriteLine($"Project: {project.Descriptor.ProjectName}");
        Console.WriteLine($"Descriptor: {project.ProjectFilePath}");
        Console.WriteLine($"Root: {project.ProjectDir}");
        Console.WriteLine($"Format version: {project.Descriptor.FormatVersion}");
        Console.WriteLine($"UE project: {project.Settings.UnrealProjectName}");
        return CliOutput.WriteValidation(validation);
    }

    private static async Task<int> ValidateAsync(IProjectService service, string[] arguments)
    {
        if (arguments.Length != 1)
        {
            Console.Error.WriteLine("Usage: unrealkit project validate <project.ukit>");
            return 2;
        }

        return CliOutput.WriteValidation(await service.ValidateProjectAsync(arguments[0]));
    }

    private static int FailUsage()
    {
        Console.Error.WriteLine("Usage: unrealkit project <create|info|validate> ...");
        return 2;
    }
}
