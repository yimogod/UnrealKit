using System.Text.Json;
using UnrealKit.Core.Adb;
using UnrealKit.Core.Capture;
using UnrealKit.Core.Export;
using UnrealKit.Core.Launch;
using UnrealKit.Core.Parsing;
using UnrealKit.Core.Processes;
using System.Linq;
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
            "export" => await RunExportAsync(arguments[1..]),
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
        Console.Error.WriteLine("Usage: unrealkit app start --project <project.ukit> --device <serial>|auto [--adb-path <path>]");
        return 2;
    }

    var project = await new ProjectService().OpenProjectAsync(GetRequiredOption(commandArguments[1..], "--project"));
    var adbService = CreateAdbService(adbPath, project.Settings.AdbPath);
    var serialNumber = await ResolveDeviceSerialAsync(adbService, commandArguments[1..]);
    var service = new LaunchParameterService(adbService);
    await service.StartApplicationAsync(project, serialNumber);
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
    var adbService = CreateAdbService(adbPath, project.Settings.AdbPath);
    var serialNumber = await ResolveDeviceSerialAsync(adbService, options);
    var service = new LaunchParameterService(adbService);
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
    if (commandArguments.Length == 0)
    {
        return FailCaptureUsage();
    }

    return commandArguments[0].ToLowerInvariant() switch
    {
        "run" => await RunCaptureRunAsync(commandArguments[1..], adbPath),
        "import" => await RunCaptureImportAsync(commandArguments[1..]),
        "list" => await ListCapturesAsync(commandArguments[1..]),
        "info" => await ShowCaptureInfoAsync(commandArguments[1..]),
        _ => FailCaptureUsage()
    };
}

static async Task<int> RunCaptureRunAsync(string[] arguments, string? adbPath)
{
    EnsureOnlyOptions(arguments, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--project", "--device", "--tag", "--format" }, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--skip-saved" });
    var project = await new ProjectService().OpenProjectAsync(GetRequiredOption(arguments, "--project"));
    var json = IsJsonFormat(arguments);
    var adbService = CreateAdbService(adbPath, project.Settings.AdbPath, streamOutput: !json);
    var serialNumber = await ResolveDeviceSerialAsync(adbService, arguments);
    var tag = GetOptionalOption(arguments, "--tag") ?? project.Settings.DefaultCaptureTag;
    var device = await GetSelectedAvailableDeviceAsync(adbService, serialNumber);
    var skipSaved = arguments.Any(option => string.Equals(option, "--skip-saved", StringComparison.OrdinalIgnoreCase));
    var result = await new CaptureService(adbService).CaptureAsync(new CaptureRequest(project, device, tag, SkipSaved: skipSaved));
    WriteCaptureResult(result, json);
    return 0;
}

static async Task<int> RunCaptureImportAsync(string[] arguments)
{
    EnsureOnlyOptions(arguments, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--project", "--source", "--platform", "--tag", "--capture-id", "--format" });
    var project = await new ProjectService().OpenProjectAsync(GetRequiredOption(arguments, "--project"));
    var source = GetRequiredOption(arguments, "--source");
    var platform = GetOptionalOption(arguments, "--platform") ?? "Android";
    var tag = GetOptionalOption(arguments, "--tag") ?? project.Settings.DefaultCaptureTag;
    var captureId = GetOptionalOption(arguments, "--capture-id");
    var json = IsJsonFormat(arguments);
    var result = await new CaptureService().ImportAsync(new CaptureImportRequest(project, source, platform, tag, captureId));
    WriteCaptureResult(result, json);
    return 0;
}

static async Task<int> ShowCaptureInfoAsync(string[] options)
{
    EnsureOnlyOptions(options, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--capture-dir", "--format" });
    var captureDir = GetRequiredOption(options, "--capture-dir");
    var json = IsJsonFormat(options);
    var service = new CaptureAnalysisService();
    var files = await service.ListCaptureFilesAsync(captureDir);

    if (json)
    {
        var manifestPath = Path.Combine(captureDir, "CaptureManifest.json");
        var hasManifest = File.Exists(manifestPath);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            CaptureDirectory = Path.GetFullPath(captureDir),
            CaptureId = Path.GetFileName(captureDir),
            HasManifest = hasManifest,
            Files = files.Select(f => new { f.Category, f.FileName, f.SizeBytes })
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    Console.WriteLine($"Capture directory: {Path.GetFullPath(captureDir)}");
    Console.WriteLine($"Capture ID: {Path.GetFileName(captureDir)}");
    var manifestPath2 = Path.Combine(captureDir, "CaptureManifest.json");
    Console.WriteLine(File.Exists(manifestPath2) ? "Manifest: present" : "Manifest: missing");
    Console.WriteLine();
    foreach (var file in files)
    {
        Console.WriteLine($"[{file.Category}] {file.FileName}  ({file.SizeBytes} bytes)");
    }

    Console.WriteLine($"{files.Count} file(s) found.");
    return 0;
}

static async Task<int> RunParseAsync(string[] arguments)
{
    if (arguments.Length == 0)
    {
        return FailParseUsage();
    }

    return arguments[0].ToLowerInvariant() switch
    {
        "meminfo" => await ParseMemInfoAsync(arguments[1..]),
        "capture-list" => await ListCapturesAsync(arguments[1..]),
        "capture-files" => await ListCaptureFilesAsync(arguments[1..]),
        "capture-meminfo" => await ParseCaptureMemInfoAsync(arguments[1..]),
        "memreport" => await ParseMemReportAsync(arguments[1..]),
        "static-camera" => await ParseStaticCameraAsync(arguments[1..]),
        _ => FailParseUsage()
    };
}
static async Task<int> RunExportAsync(string[] arguments)
{
    if (arguments.Length == 0) return FailExportUsage();
    var subCommand = arguments[0].ToLowerInvariant();
    if (subCommand is not ("meminfo" or "memreport")) return FailExportUsage();
    var options = arguments[1..];
    var inputOption = "--input";
    var outputOption = "--output";
    var includeDetailsOption = "--include-details";
    var captureIdOption = "--capture-id";
    EnsureOnlyOptions(options, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { inputOption, outputOption, includeDetailsOption, captureIdOption }, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { includeDetailsOption });
    var input = GetRequiredOption(options, inputOption);
    var output = GetRequiredOption(options, outputOption);
    var includeDetails = options.Any(option => string.Equals(option, includeDetailsOption, StringComparison.OrdinalIgnoreCase));
    var captureId = GetOptionalOption(options, captureIdOption);

    if (subCommand == "meminfo")
    {
        var result = await new AndroidMemInfoParser().ParseFileAsync(input);
        if (!result.IsSuccess) { WriteMemInfoParseResult(result, false); return 1; }
        var isXlsx = output.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
        if (isXlsx)
        {
            var exported = await new XlsxMemInfoExportService().ExportAsync(new MemInfoExportRequest(result, output, DateTimeOffset.UtcNow, includeDetails, captureId));
            Console.WriteLine(exported.OutputFilePath);
        }
        else
        {
            var exported = await new MemInfoExportService().ExportAsync(new MemInfoExportRequest(result, output, DateTimeOffset.UtcNow, includeDetails, captureId));
            Console.WriteLine(exported.OutputFilePath);
        }
    }
    else
    {
        var result = await new UnrealMemReportParser().ParseFileAsync(input);
        if (!result.IsSuccess) { WriteMemReportParseResult(result, false); return 1; }
        var isXlsx = output.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
        if (isXlsx)
        {
            var exported = await new XlsxMemReportExportService().ExportAsync(new MemReportExportRequest(result, output, DateTimeOffset.UtcNow, includeDetails, captureId));
            Console.WriteLine(exported.OutputFilePath);
        }
        else
        {
            var exported = await new MemReportExportService().ExportAsync(new MemReportExportRequest(result, output, DateTimeOffset.UtcNow, includeDetails, captureId));
            Console.WriteLine(exported.OutputFilePath);
        }
    }
    return 0;
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
    Console.WriteLine($"Root: {project.ProjectDir}");
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

static async Task<string> ResolveDeviceSerialAsync(IAdbService service, string[] options)
{
    var serialNumber = GetOptionalOption(options, "--device");
    if (!string.IsNullOrWhiteSpace(serialNumber))
    {
        if (string.Equals(serialNumber, "auto", StringComparison.OrdinalIgnoreCase))
        {
            var availableDevices = (await service.ListDevicesAsync()).Where(device => device.IsAvailable).ToArray();
            return availableDevices.Length switch
            {
                1 => availableDevices[0].SerialNumber,
                0 => throw new AdbDeviceSelectionException("No available ADB devices found for auto-selection. Connect a device or specify --device <serial>."),
                _ => throw new AdbDeviceSelectionException($"Multiple devices available ({availableDevices.Length}). Use --device <serial> to select one: {string.Join(", ", availableDevices.Select(device => device.SerialNumber))}")
            };
        }

        return serialNumber;
    }

    var devices = await service.ListDevicesAsync();
    var available = devices.Where(device => device.IsAvailable).ToArray();
    if (available.Length == 1)
    {
        Console.Error.WriteLine($"Only one available device found: {available[0].SerialNumber} ({available[0].Model ?? "unknown model"}). Use --device auto to select it.");
    }
    else if (available.Length == 0)
    {
        Console.Error.WriteLine("No available ADB devices found. Connect a device and try again.");
    }
    else
    {
        Console.Error.WriteLine("Multiple devices available. Use --device <serial> to select one:");
        foreach (var device in available)
        {
            Console.Error.WriteLine($"  {device.SerialNumber}  {device.Status}  {device.Model ?? "unknown model"}");
        }
    }

    throw new AdbDeviceSelectionException("No device specified. Use --device <serial> or --device auto when exactly one device is available.");
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

static void EnsureOnlyOptions(string[] arguments, IReadOnlySet<string> allowedOptions, IReadOnlySet<string>? flagOptions = null)
{
    for (var index = 0; index < arguments.Length; index++)
    {
        if (!arguments[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unsupported option: {arguments[index]}.");
        }

        if (!allowedOptions.Contains(arguments[index]))
        {
            throw new ArgumentException($"Unsupported option: {arguments[index]}.");
        }

        if (flagOptions?.Contains(arguments[index]) == true)
        {
            continue;
        }

        if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{arguments[index]} must be followed by a value.");
        }

        index++;
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
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  unrealkit capture run --project <project.ukit> --device <serial>|auto [--tag <tag>] [--format text|json] [--skip-saved] [--adb-path <path>]");
    Console.Error.WriteLine("  unrealkit capture import --project <project.ukit> --source <directory> [--platform <platform>] [--tag <tag>] [--capture-id <id>]");
    Console.Error.WriteLine("  unrealkit capture list --project <project.ukit> [--platform <platform>] [--tag <tag>] [--format text|json]");
    Console.Error.WriteLine("  unrealkit capture info --capture-dir <path> [--format text|json]");
    return 2;
}

static async Task<int> ParseMemInfoAsync(string[] options)
{
    EnsureOnlyOptions(options, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--input", "--format" });
    var result = await new AndroidMemInfoParser().ParseFileAsync(GetRequiredOption(options, "--input"));
    var json = IsJsonFormat(options);
    WriteMemInfoParseResult(result, json);
    return result.IsSuccess ? 0 : 1;
}

static async Task<int> ListCapturesAsync(string[] options)
{
    EnsureOnlyOptions(options, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--project", "--platform", "--tag", "--format" });
    var project = await new ProjectService().OpenProjectAsync(GetRequiredOption(options, "--project"));
    var json = IsJsonFormat(options);
    var service = new CaptureAnalysisService();
    var platform = GetOptionalOption(options, "--platform");
    var tag = GetOptionalOption(options, "--tag");
    var captures = await service.ListCaptureDirectoriesAsync(project, platform, tag);
    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(captures.Select(c => new { c.CaptureId, c.CaptureDate, c.Platform, c.Tag, c.RelativePath, c.HasManifest }), new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    foreach (var capture in captures)
    {
        var marker = capture.HasManifest ? "" : " [no manifest]";
        Console.WriteLine($"{capture.CaptureDate:yyyy-MM-dd}  {capture.CaptureId}  platform={capture.Platform}  tag={capture.Tag}{marker}");
        Console.WriteLine($"  {capture.RelativePath}");
    }

    Console.WriteLine($"{captures.Count} capture(s) found.");
    return 0;
}

static async Task<int> ListCaptureFilesAsync(string[] options)
{
    EnsureOnlyOptions(options, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--capture-dir", "--format" });
    var captureDir = GetRequiredOption(options, "--capture-dir");
    var json = IsJsonFormat(options);
    var service = new CaptureAnalysisService();
    var files = await service.ListCaptureFilesAsync(captureDir);
    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(files.Select(f => new { f.Category, f.FileName, f.SizeBytes, f.FullPath }), new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    foreach (var file in files)
    {
        Console.WriteLine($"[{file.Category}] {file.FileName}  ({file.SizeBytes} bytes)");
    }

    Console.WriteLine($"{files.Count} file(s) found.");
    return 0;
}

static async Task<int> ParseCaptureMemInfoAsync(string[] options)
{
    EnsureOnlyOptions(options, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--project", "--capture", "--file", "--analysis-id", "--format" });
    var project = await new ProjectService().OpenProjectAsync(GetRequiredOption(options, "--project"));
    var captureIdOrPath = GetRequiredOption(options, "--capture");
    var fileName = GetRequiredOption(options, "--file");
    var analysisId = GetOptionalOption(options, "--analysis-id");
    var json = IsJsonFormat(options);

    var service = new CaptureAnalysisService();

    string captureDirectoryPath;
    if (Path.IsPathRooted(captureIdOrPath) || captureIdOrPath.Contains('/') || captureIdOrPath.Contains('\\'))
    {
        captureDirectoryPath = Path.GetFullPath(captureIdOrPath);
    }
    else
    {
        var captures = await service.ListCaptureDirectoriesAsync(project, platform: null, tag: null);
        var match = captures.FirstOrDefault(c => string.Equals(c.CaptureId, captureIdOrPath, StringComparison.Ordinal));
        if (match is null)
        {
            throw new ArgumentException($"Capture not found: {captureIdOrPath}. Use 'unrealkit parse capture-list --project <project.ukit>' to list available captures.");
        }

        captureDirectoryPath = match.FullPath;
    }

    var memInfoFiles = await service.ListCaptureFilesAsync(captureDirectoryPath);
    var targetFile = memInfoFiles.FirstOrDefault(f => string.Equals(f.FileName, fileName, StringComparison.Ordinal));
    if (targetFile is null)
    {
        var availableNames = string.Join(", ", memInfoFiles
            .Where(f => string.Equals(f.Category, "MemInfo", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.FileName));
        throw new ArgumentException(
            $"Meminfo file '{fileName}' not found in capture. Available MemInfo files: {availableNames}");
    }

    if (!string.Equals(targetFile.Category, "MemInfo", StringComparison.OrdinalIgnoreCase))
    {
        throw new ArgumentException($"File '{fileName}' is in category '{targetFile.Category}', not MemInfo.");
    }

    var request = new CaptureAnalysisRequest(project, captureDirectoryPath, targetFile.FullPath, analysisId);
    var result = await service.AnalyzeMemInfoAsync(request);

    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            result.AnalysisId,
            result.AnalysisDirectory,
            result.CaptureId,
            result.InputFilePath,
            result.ResultJsonPath,
            result.ParseResult.IsSuccess,
            result.ParseResult.Report?.ProcessName,
            result.ParseResult.Report?.ProcessId,
            Summary = result.ParseResult.Report?.Summary,
            Diagnostics = result.ParseResult.Diagnostics.Select(d => new { d.Severity, d.Code, d.Message, d.LineNumber })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    else
    {
        Console.WriteLine($"Analysis ID: {result.AnalysisId}");
        Console.WriteLine($"Capture: {result.CaptureId}");
        Console.WriteLine($"Input: {result.InputFilePath}");
        Console.WriteLine($"Result: {result.ResultJsonPath}");
        WriteMemInfoParseResult(result.ParseResult, false);
    }

    return result.ParseResult.IsSuccess ? 0 : 1;
}

static async Task<int> ParseMemReportAsync(string[] options)
{
    EnsureOnlyOptions(options, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--input", "--format" });
    var result = await new UnrealMemReportParser().ParseFileAsync(GetRequiredOption(options, "--input"));
    var json = IsJsonFormat(options);
    WriteMemReportParseResult(result, json);
    return result.IsSuccess ? 0 : 1;
}

static void WriteMemReportParseResult(UnrealMemReportParseResult result, bool json)
{
    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return;
    }

    if (result.Report is not null)
    {
        Console.WriteLine($"Input: {result.InputPath}");
        Console.WriteLine($"Changelist: {result.Report.Changelist}");
        Console.WriteLine();
        Console.WriteLine("Summary Metrics:");
        foreach (var metric in result.Report.Summary.Metrics)
        {
            var status = metric.Status switch
            {
                UnrealMemReportMetricStatus.Parsed => $"{metric.ValueKb} KB",
                UnrealMemReportMetricStatus.Missing => "MISSING",
                UnrealMemReportMetricStatus.Invalid => $"INVALID ({metric.RawValue})",
                _ => "?"
            };
            Console.WriteLine($"  [{metric.Group}] {metric.Name}: {status}");
        }

        if (result.Report.Textures.Count > 0)
            Console.WriteLine($"\nTextures: {result.Report.Textures.Count}");
        if (result.Report.RenderTargets.Count > 0)
            Console.WriteLine($"Render Targets: {result.Report.RenderTargets.Count}");
        if (result.Report.Objects.Count > 0)
            Console.WriteLine($"Objects: {result.Report.Objects.Count}");
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



static async Task<int> ParseStaticCameraAsync(string[] options)
{
    EnsureOnlyOptions(options, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--input", "--screenshots", "--format" });
    var input = GetRequiredOption(options, "--input");
    var screenshots = GetOptionalOption(options, "--screenshots");
    var parser = new StaticCameraPerfParser();
    StaticCameraPerfParseResult result;
    if (!string.IsNullOrWhiteSpace(screenshots) && Directory.Exists(screenshots))
        result = await parser.ParseFileAsync(input, screenshots);
    else
        result = await parser.ParseFileAsync(input);
    var json = IsJsonFormat(options);
    WriteStaticCameraParseResult(result, json);
    return result.IsSuccess ? 0 : 1;
}

static void WriteStaticCameraParseResult(StaticCameraPerfParseResult result, bool json)
{
    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return;
    }

    if (result.Report is not null)
    {
        Console.WriteLine($"Input: {result.InputPath}");
        Console.WriteLine($"Cameras: {result.Report.ParseCameraCount} of {result.Report.CameraCount} ({(result.Report.Completeness == StaticCameraPerfDataCompleteness.Complete ? "complete" : "truncated")})");
        Console.WriteLine();
        Console.WriteLine("Device Info:");
        if (result.Report.DeviceInfo.OsPlatform is not null) Console.WriteLine($"  OS: {result.Report.DeviceInfo.OsPlatform}");
        if (result.Report.DeviceInfo.DeviceMake is not null) Console.WriteLine($"  Device: {result.Report.DeviceInfo.DeviceMake}");
        if (result.Report.DeviceInfo.GpuVendor is not null) Console.WriteLine($"  GPU: {result.Report.DeviceInfo.GpuVendor}");
        if (result.Report.DeviceInfo.VulkanAvailable.HasValue) Console.WriteLine($"  Vulkan: {result.Report.DeviceInfo.VulkanVersion ?? "available"}");
        Console.WriteLine();
        Console.WriteLine("Averages:");
        Console.WriteLine($"  Frame: {result.Report.Average.FrameTimeMs} ms");
        Console.WriteLine($"  Game:  {result.Report.Average.GameTimeMs} ms");
        Console.WriteLine($"  Draw:  {result.Report.Average.DrawTimeMs} ms");
        Console.WriteLine($"  RHI:   {result.Report.Average.RhiTimeMs} ms");
        Console.WriteLine($"  GPU:   {result.Report.Average.GpuTimeMs} ms");
        Console.WriteLine($"  DC:    {result.Report.Average.DrawCalls}");
        Console.WriteLine($"  Prim:  {result.Report.Average.Triangles:N0}");
        Console.WriteLine();
        Console.WriteLine("Per-Camera:");
        foreach (var frame in result.Report.Frames)
        {
            Console.WriteLine($"  [{frame.Index}] {frame.CameraName}: Frame={frame.FrameTimeMs}ms Game={frame.GameTimeMs}ms Draw={frame.DrawTimeMs}ms RHI={frame.RhiTimeMs}ms GPU={frame.GpuTimeMs}ms DC={frame.DrawCalls} Prim={frame.Triangles:N0}");
            if (frame.Screenshots.Count > 0)
                Console.WriteLine($"       Screenshots: {frame.Screenshots.Count}");
        }
    }

    foreach (var diagnostic in result.Diagnostics)
    {
        var line = diagnostic.LineNumber is null ? string.Empty : $" line {diagnostic.LineNumber}";
        Console.Error.WriteLine($"[{diagnostic.Severity}] {diagnostic.Code}{line}: {diagnostic.Message}");
        if (!string.IsNullOrWhiteSpace(diagnostic.SuggestedFix))
            Console.Error.WriteLine($"  Fix: {diagnostic.SuggestedFix}");
    }
}

static int FailParseUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  unrealkit parse meminfo --input <meminfo.txt> [--format text|json]");
    Console.Error.WriteLine("  unrealkit parse memreport --input <memreport.txt> [--format text|json]");
    Console.Error.WriteLine("  unrealkit parse capture-list --project <project.ukit> [--platform <platform>] [--tag <tag>]");
    Console.Error.WriteLine("  unrealkit parse capture-files --capture-dir <path>");
    Console.Error.WriteLine("  unrealkit parse capture-meminfo --project <project.ukit> --capture <capture-id> [--file <filename>] [--analysis-id <id>]");
    Console.Error.WriteLine("  unrealkit parse static-camera --input <log> [--screenshots <dir>] [--format json]");
    return 2;
}

static int FailExportUsage() { Console.Error.WriteLine("Usage: unrealkit export meminfo --input <meminfo.txt> --output <results.csv|results.tsv|results.xlsx> [--include-details] [--capture-id <capture-id>]"); Console.Error.WriteLine("       unrealkit export memreport --input <memreport.txt> --output <results.csv|results.tsv|results.xlsx> [--include-details] [--capture-id <capture-id>]"); return 2; }

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
    Console.WriteLine("  unrealkit capture run --project <project.ukit> --device <serial>|auto [--tag <tag>] [--format text|json] [--skip-saved] [--adb-path <path>]");
    Console.WriteLine("  unrealkit capture import --project <project.ukit> --source <directory> [--platform <platform>] [--tag <tag>] [--capture-id <id>]");
    Console.WriteLine("  unrealkit parse meminfo --input <meminfo.txt> [--format text|json]");
    Console.WriteLine("  unrealkit parse memreport --input <memreport.txt> [--format text|json]");
    Console.WriteLine("  unrealkit parse capture-list --project <project.ukit> [--platform <platform>] [--tag <tag>]");
    Console.WriteLine("  unrealkit parse capture-files --capture-dir <path>");
    Console.WriteLine("  unrealkit parse capture-meminfo --project <project.ukit> --capture <capture-id> [--file <filename>] [--analysis-id <id>]");
    Console.WriteLine("  unrealkit parse static-camera --input <log> [--screenshots <dir>] [--format json]");
    Console.WriteLine("  unrealkit export meminfo --input <meminfo.txt> --output <results.csv|results.tsv|results.xlsx> [--include-details] [--capture-id <capture-id>]");
  Console.WriteLine("  unrealkit export memreport --input <memreport.txt> --output <results.csv|results.tsv|results.xlsx> [--include-details] [--capture-id <capture-id>]");
}
