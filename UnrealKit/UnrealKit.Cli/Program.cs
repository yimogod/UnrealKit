using System.Text.Json;
using UnrealKit.Core.Adb;
using UnrealKit.Core.Projects;

return await RunAsync(args);

static async Task<int> RunAsync(string[] arguments)
{
    if (arguments.Length == 0 || arguments[0] is "-h" or "--help" or "help")
    {
        PrintUsage();
        return 0;
    }

    try
    {
        return arguments[0].ToLowerInvariant() switch
        {
            "project" => await RunProjectAsync(arguments[1..]),
            "adb" => await RunAdbAsync(arguments[1..]),
            _ => FailUnknownCommand()
        };
    }
    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException or AdbCommandException or TimeoutException)
    {
        Console.Error.WriteLine($"Error: {exception.Message}");
        if (exception is AdbCommandException adbException)
        {
            WriteAdbFailure(adbException);
        }

        return 1;
    }
}

static async Task<int> RunProjectAsync(string[] arguments)
{
    if (arguments.Length == 0)
    {
        return FailProjectUsage();
    }

    var service = new ProjectService();
    return arguments[0].ToLowerInvariant() switch
    {
        "create" => await CreateProjectAsync(service, arguments[1..]),
        "info" => await ShowProjectInfoAsync(service, arguments[1..]),
        "validate" => await ValidateProjectAsync(service, arguments[1..]),
        _ => FailProjectUsage()
    };
}

static async Task<int> RunAdbAsync(string[] arguments)
{
    if (arguments.Length == 0)
    {
        return FailAdbUsage();
    }

    var (commandArguments, adbPath) = ParseAdbPath(arguments);
    var service = new AdbService(new UnrealKit.Core.Processes.ProcessRunner(), adbPath);
    return commandArguments[0].ToLowerInvariant() switch
    {
        "version" when commandArguments.Length == 1 => await ShowAdbVersionAsync(service),
        "devices" when commandArguments.Length == 1 => await ListAdbDevicesAsync(service),
        "connect" when commandArguments.Length == 2 => await ConnectAdbAsync(service, commandArguments[1]),
        "disconnect" when commandArguments.Length == 2 => await DisconnectAdbAsync(service, commandArguments[1]),
        _ => FailAdbUsage()
    };
}

static async Task<int> CreateProjectAsync(IProjectService service, string[] arguments)
{
    if (arguments.Length != 3 || !string.Equals(arguments[1], "--name", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("Usage: unrealkit project create <directory> --name <name>");
        return 2;
    }

    var result = await service.CreateProjectAsync(new CreateProjectRequest(arguments[0], arguments[2]));
    Console.WriteLine($"Created project: {result.Project.ProjectFilePath}");
    return WriteValidation(result.Validation);
}

static async Task<int> ShowProjectInfoAsync(IProjectService service, string[] arguments)
{
    var json = arguments.Length == 3 && string.Equals(arguments[1], "--format", StringComparison.OrdinalIgnoreCase) && string.Equals(arguments[2], "json", StringComparison.OrdinalIgnoreCase);
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
    Console.WriteLine($"Root: {project.RootDirectory}");
    Console.WriteLine($"Format version: {project.Descriptor.FormatVersion}");
    Console.WriteLine($"UE project: {project.Settings.UnrealProjectName}");
    return WriteValidation(validation);
}

static async Task<int> ValidateProjectAsync(IProjectService service, string[] arguments)
{
    if (arguments.Length != 1)
    {
        Console.Error.WriteLine("Usage: unrealkit project validate <project.ukit>");
        return 2;
    }

    return WriteValidation(await service.ValidateProjectAsync(arguments[0]));
}

static async Task<int> ShowAdbVersionAsync(IAdbService service)
{
    var result = await service.GetVersionAsync();
    Console.Write(result.StandardOutput);
    return 0;
}

static async Task<int> ListAdbDevicesAsync(IAdbService service)
{
    var devices = await service.ListDevicesAsync();
    foreach (var device in devices)
    {
        Console.WriteLine($"{device.SerialNumber}\t{device.Status}\t{device.ConnectionType}\t{device.Model ?? "Unknown model"}");
    }

    return 0;
}

static async Task<int> ConnectAdbAsync(IAdbService service, string endpoint)
{
    var result = await service.ConnectAsync(endpoint);
    Console.Write(result.StandardOutput);
    return 0;
}

static async Task<int> DisconnectAdbAsync(IAdbService service, string endpoint)
{
    var result = await service.DisconnectAsync(endpoint);
    Console.Write(result.StandardOutput);
    return 0;
}

static (string[] CommandArguments, string? AdbPath) ParseAdbPath(string[] arguments)
{
    var pathIndex = Array.FindIndex(arguments, argument => string.Equals(argument, "--adb-path", StringComparison.OrdinalIgnoreCase));
    if (pathIndex < 0)
    {
        return (arguments, null);
    }

    if (pathIndex + 1 >= arguments.Length || pathIndex != arguments.Length - 2)
    {
        throw new ArgumentException("--adb-path must be followed by a path and must be the final option.");
    }

    return (arguments[..pathIndex], arguments[pathIndex + 1]);
}

static int WriteValidation(ProjectValidationResult validation)
{
    foreach (var diagnostic in validation.Diagnostics)
    {
        Console.WriteLine($"[{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message}{(diagnostic.Path is null ? string.Empty : $" ({diagnostic.Path})")}");
    }

    Console.WriteLine(validation.IsValid ? "Validation passed." : "Validation failed.");
    return validation.IsValid ? 0 : 1;
}

static void WriteAdbFailure(AdbCommandException exception)
{
    Console.Error.WriteLine($"Exit code: {exception.Result.ExitCode}");
    if (!string.IsNullOrWhiteSpace(exception.Result.StandardError))
    {
        Console.Error.WriteLine("stderr:");
        Console.Error.WriteLine(exception.Result.StandardError.TrimEnd());
    }
}

static int FailUnknownCommand()
{
    Console.Error.WriteLine("Unknown command.");
    PrintUsage();
    return 2;
}

static int FailProjectUsage()
{
    Console.Error.WriteLine("Usage: unrealkit project <create|info|validate> ...");
    return 2;
}

static int FailAdbUsage()
{
    Console.Error.WriteLine("Usage: unrealkit adb <version|devices|connect|disconnect> ... [--adb-path <path>]");
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine("UnrealKit CLI");
    Console.WriteLine("  unrealkit project create <directory> --name <name>");
    Console.WriteLine("  unrealkit project info <project.ukit> [--format json]");
    Console.WriteLine("  unrealkit project validate <project.ukit>");
    Console.WriteLine("  unrealkit adb version [--adb-path <path>]");
    Console.WriteLine("  unrealkit adb devices [--adb-path <path>]");
    Console.WriteLine("  unrealkit adb connect <host:port> [--adb-path <path>]");
    Console.WriteLine("  unrealkit adb disconnect <host:port> [--adb-path <path>]");
}
