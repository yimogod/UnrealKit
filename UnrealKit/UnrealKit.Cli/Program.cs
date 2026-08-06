using System.Text.Json;
using UnrealKit.Core.Adb;
using UnrealKit.Core.Capture;
using UnrealKit.Core.Launch;
using UnrealKit.Core.Parsing;
using UnrealKit.Core.Processes;
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
            "app" => await RunAppAsync(arguments[1..]),
            "commandline" => await RunCommandLineAsync(arguments[1..]),
            "capture" => await RunCaptureAsync(arguments[1..]),
            "parse" => await RunParseAsync(arguments[1..]),
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
        else if (exception is AdbPathResolutionException pathException)
        {
            WriteAdbPathDiagnostics(pathException.Resolution);
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
    var service = CreateAdbService(adbPath);
    return commandArguments[0].ToLowerInvariant() switch
    {
        "version" when commandArguments.Length == 1 => await ShowAdbVersionAsync(service),
        "devices" when commandArguments.Length == 1 => await ListAdbDevicesAsync(service),
        "connect" when commandArguments.Length == 2 => await ConnectAdbAsync(service, commandArguments[1]),
        "disconnect" when commandArguments.Length == 2 => await DisconnectAdbAsync(service, commandArguments[1]),
        _ => FailAdbUsage()
    };
}

static async Task<int> RunAppAsync(string[] arguments)
{
    var (commandArguments, adbPath) = ParseAdbPath(arguments);
    if (commandArguments.Length == 0 || !string.Equals(commandArguments[0], "start", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("Usage: unrealkit app start --project <project.ukit> --device <serial> [--adb-path <path>]");
        return 2;
    }

    var project = await new ProjectService().OpenProjectAsync(GetRequiredOption(commandArguments[1..], "--project"));
    var service = new LaunchParameterService(CreateAdbService(adbPath, project.Settings.AdbPath));
    var result = await service.StartApplicationAsync(project, GetRequiredOption(commandArguments[1..], "--device"));
    return 0;
}

static async Task<int> RunCommandLineAsync(string[] arguments)
{
    var (commandArguments, adbPath) = ParseAdbPath(arguments);
    if (commandArguments.Length == 0)
    {
        return FailCommandLineUsage();
    }

    var options = commandArguments[1..];
    var project = await new ProjectService().OpenProjectAsync(GetRequiredOption(options, "--project"));
    var service = new LaunchParameterService(CreateAdbService(adbPath, project.Settings.AdbPath));
    var serialNumber = GetRequiredOption(options, "--device");
    var remotePath = GetOptionalOption(options, "--remote-path");
    switch (commandArguments[0].ToLowerInvariant())
    {
        case "push":
        {
            var result = await service.PushAsync(project, new LaunchParameterRequest(serialNumber, GetOptions(options, "--preset"), GetOptionalOption(options, "--custom"), remotePath));
            Console.WriteLine($"Pushed uecommandline.txt to {result.RemotePath}");
            Console.WriteLine("Content:");
            Console.WriteLine(result.Content);
            return 0;
        }
        case "delete":
        {
            var result = await service.DeleteAsync(project, serialNumber, remotePath);
            return 0;
        }
        default:
            return FailCommandLineUsage();
    }
}

static async Task<int> RunCaptureAsync(string[] arguments)
{
    var (commandArguments, adbPath) = ParseAdbPath(arguments);
    if (commandArguments.Length == 0 || !string.Equals(commandArguments[0], "run", StringComparison.OrdinalIgnoreCase))
    {
        return FailCaptureUsage();
    }

    var options = commandArguments[1..];
    EnsureOnlyOptions(options, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--project", "--device", "--tag", "--format" });
    var project = await new ProjectService().OpenProjectAsync(GetRequiredOption(options, "--project"));
    var serialNumber = GetRequiredOption(options, "--device");
    var tag = GetOptionalOption(options, "--tag") ?? project.Settings.DefaultCaptureTag;
    var json = IsJsonFormat(options);
    var adbService = CreateAdbService(adbPath, project.Settings.AdbPath, streamOutput: !json);
    var device = await GetSelectedAvailableDeviceAsync(adbService, serialNumber);
    var result = await new CaptureService(adbService).CaptureAsync(new CaptureRequest(project, device, tag));
    WriteCaptureResult(result, json);
    return 0;
}

static async Task<int> RunParseAsync(string[] arguments)
{
    if (arguments.Length == 0 || !string.Equals(arguments[0], "meminfo", StringComparison.OrdinalIgnoreCase))
    {
        return FailParseUsage();
    }

    var options = arguments[1..];
    EnsureOnlyOptions(options, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--input", "--format" });
    var result = await new AndroidMemInfoParser().ParseFileAsync(GetRequiredOption(options, "--input"));
    var json = IsJsonFormat(options);
    WriteMemInfoParseResult(result, json);
    return result.IsSuccess ? 0 : 1;
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
    return 0;
}

static async Task<int> DisconnectAdbAsync(IAdbService service, string endpoint)
{
    var result = await service.DisconnectAsync(endpoint);
    return 0;
}

static async Task<AdbDevice> GetSelectedAvailableDeviceAsync(IAdbService service, string serialNumber)
{
    var device = (await service.ListDevicesAsync()).SingleOrDefault(candidate => string.Equals(candidate.SerialNumber, serialNumber, StringComparison.Ordinal));
    if (device is null)
    {
        throw new AdbDeviceSelectionException($"ADB device was not found: {serialNumber}");
    }

    if (!device.IsAvailable)
    {
        throw new AdbDeviceSelectionException($"ADB device '{serialNumber}' is in state '{device.Status}', not 'device'.");
    }

    return device;
}

static bool IsJsonFormat(string[] arguments)
{
    var format = GetOptionalOption(arguments, "--format");
    return format is null || string.Equals(format, "text", StringComparison.OrdinalIgnoreCase)
        ? false
        : string.Equals(format, "json", StringComparison.OrdinalIgnoreCase)
            ? true
            : throw new ArgumentException("--format must be either text or json.");
}

static void WriteCaptureResult(CaptureResult result, bool json)
{
    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { result.Plan.CaptureId, result.Plan.CaptureDirectory, result.ManifestPath, result.Manifest.DeviceSerialNumber, result.Manifest.Tag }, new JsonSerializerOptions { WriteIndented = true }));
        return;
    }

    Console.WriteLine($"Capture ID: {result.Plan.CaptureId}");
    Console.WriteLine($"Archive: {result.Plan.CaptureDirectory}");
    Console.WriteLine($"Manifest: {result.ManifestPath}");
}

static void WriteMemInfoParseResult(AndroidMemInfoParseResult result, bool json)
{
    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return;
    }

    if (result.Report is not null)
    {
        Console.WriteLine($"Input: {result.InputPath}");
        Console.WriteLine($"Process: {result.Report.ProcessName} (pid {result.Report.ProcessId})");
        Console.WriteLine($"App Summary TOTAL: {result.Report.Summary.TotalPssKb} KB");
    }

    foreach (var diagnostic in result.Diagnostics)
    {
        var line = diagnostic.LineNumber is null ? string.Empty : $" line {diagnostic.LineNumber}";
        Console.Error.WriteLine($"[{diagnostic.Severity}] {diagnostic.Code}{line}: {diagnostic.Message}");
        if (!string.IsNullOrWhiteSpace(diagnostic.SuggestedFix))
        {
            Console.Error.WriteLine($"  Fix: {diagnostic.SuggestedFix}");
        }
    }
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

static AdbService CreateAdbService(string? explicitPath, string? projectAdbPath = null, bool streamOutput = true)
{
    var resolvedPath = new AdbPathResolver().ResolveRequired(explicitPath, projectAdbPath);
    return new AdbService(new ProcessRunner(), resolvedPath, streamOutput ? new Progress<ProcessOutput>(WriteProcessOutput) : null);
}

static void WriteProcessOutput(ProcessOutput output)
{
    var writer = output.Stream == ProcessOutputStream.StandardError ? Console.Error : Console.Out;
    writer.WriteLine(output.Text);
}

static void WriteAdbPathDiagnostics(AdbPathResolution resolution)
{
    foreach (var attempt in resolution.Attempts)
    {
        var path = attempt.CandidatePath is null ? string.Empty : $" - {attempt.CandidatePath}";
        Console.Error.WriteLine($"ADB {attempt.Source} ({attempt.Description}): {attempt.Status}{path}");
    }
}

static string GetRequiredOption(string[] arguments, string optionName) => GetOptionalOption(arguments, optionName) ?? throw new ArgumentException($"Missing required option {optionName}.");

static string? GetOptionalOption(string[] arguments, string optionName)
{
    var index = Array.FindIndex(arguments, argument => string.Equals(argument, optionName, StringComparison.OrdinalIgnoreCase));
    if (index < 0)
    {
        return null;
    }

    if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
    {
        throw new ArgumentException($"{optionName} must be followed by a value.");
    }

    return arguments[index + 1];
}

static string[] GetOptions(string[] arguments, string optionName)
{
    var values = new List<string>();
    for (var index = 0; index < arguments.Length; index++)
    {
        if (!string.Equals(arguments[index], optionName, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (++index >= arguments.Length || arguments[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{optionName} must be followed by a value.");
        }

        values.Add(arguments[index]);
    }

    return values.ToArray();
}

static void EnsureOnlyOptions(string[] arguments, IReadOnlySet<string> allowedOptions)
{
    for (var index = 0; index < arguments.Length; index += 2)
    {
        if (!arguments[index].StartsWith("--", StringComparison.Ordinal) || !allowedOptions.Contains(arguments[index]))
        {
            throw new ArgumentException($"Unsupported option: {arguments[index]}.");
        }

        if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{arguments[index]} must be followed by a value.");
        }
    }
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

static int FailCommandLineUsage()
{
    Console.Error.WriteLine("Usage: unrealkit commandline <push|delete> --project <project.ukit> --device <serial> [--preset <name>] [--custom <arguments>] [--remote-path <path>] [--adb-path <path>]");
    return 2;
}

static int FailCaptureUsage()
{
    Console.Error.WriteLine("Usage: unrealkit capture run --project <project.ukit> --device <serial> [--tag <tag>] [--format text|json] [--adb-path <path>]");
    return 2;
}

static int FailParseUsage()
{
    Console.Error.WriteLine("Usage: unrealkit parse meminfo --input <meminfo.txt> [--format text|json]");
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
    Console.WriteLine("  unrealkit app start --project <project.ukit> --device <serial> [--adb-path <path>]");
    Console.WriteLine("  unrealkit commandline push --project <project.ukit> --device <serial> [--preset <name>] [--custom <arguments>] [--remote-path <path>] [--adb-path <path>]");
    Console.WriteLine("  unrealkit commandline delete --project <project.ukit> --device <serial> [--remote-path <path>] [--adb-path <path>]");
    Console.WriteLine("  unrealkit capture run --project <project.ukit> --device <serial> [--tag <tag>] [--format text|json] [--adb-path <path>]");
    Console.WriteLine("  unrealkit parse meminfo --input <meminfo.txt> [--format text|json]");
}
