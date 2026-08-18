using System.Text.Json;
using UnrealKit.Core.Capture;
using UnrealKit.Core.Console;
using UnrealKit.Core.Devices;
using UnrealKit.Core.Projects;

namespace UnrealKit.Cli;

/// <summary>`unrealkit capture run|import|list|info`。</summary>
internal static class CaptureCommands
{
    internal static async Task<int> RunAsync(string[] arguments)
    {
        var (commandArguments, adbPath) = CliOptions.ParseAdbPath(arguments);
        if (commandArguments.Length == 0)
        {
            return FailUsage();
        }

        return commandArguments[0].ToLowerInvariant() switch
        {
            "run" => await CaptureRunAsync(commandArguments[1..], adbPath),
            "import" => await CaptureImportAsync(commandArguments[1..]),
            "list" => await ListCapturesAsync(commandArguments[1..]),
            "info" => await ShowCaptureInfoAsync(commandArguments[1..]),
            _ => FailUsage()
        };
    }

    private static async Task<int> CaptureRunAsync(string[] arguments, string? adbPath)
    {
        CliOptions.EnsureOnly(
            arguments,
            CliOptions.Allowed("--project", "--device", "--platform", "--tag", "--format", "--skip-saved"),
            CliOptions.Allowed("--skip-saved"));

        var project = await new ProjectService().OpenProjectAsync(CliOptions.GetRequired(arguments, "--project"));
        var json = CliOptions.IsJsonFormat(arguments);
        var tag = CliOptions.GetOptional(arguments, "--tag") ?? project.Settings.DefaultCaptureTag;
        var skipSaved = CliOptions.HasFlag(arguments, "--skip-saved");
        var resolved = await DeviceResolver.ResolveDeviceTargetAsync(project, arguments, adbPath, streamOutput: !json);

        // 能力探测替代类型判断：不支持控制台指令的平台传 null，
        // CaptureService 会在配置了采集序列时明确报错而不是静默跳过。
        var consoleService = resolved.DeviceService.Supports(DeviceCapability.SendConsoleCommand)
            ? new ConsoleCommandService(resolved.DeviceService)
            : null;

        var result = await new CaptureService(resolved.DeviceService, consoleService)
            .CaptureAsync(new CaptureRequest(project, resolved.Device, tag, SkipSaved: skipSaved));
        WriteCaptureResult(result, json);
        return 0;
    }

    private static async Task<int> CaptureImportAsync(string[] arguments)
    {
        CliOptions.EnsureOnly(arguments, CliOptions.Allowed("--project", "--source", "--platform", "--tag", "--capture-id", "--format"));
        var project = await new ProjectService().OpenProjectAsync(CliOptions.GetRequired(arguments, "--project"));
        var source = CliOptions.GetRequired(arguments, "--source");
        // --platform 必填：导入没有设备可以据以判断平台，而归档目录按平台分区。
        // 工程可能同时配置了多个平台，替用户挑一个会把数据归到错误的平台下。
        var platform = PlatformNames.ToName(
            PlatformNames.Parse(CliOptions.GetRequired(arguments, "--platform"), "--platform"));
        var tag = CliOptions.GetOptional(arguments, "--tag") ?? project.Settings.DefaultCaptureTag;
        var captureId = CliOptions.GetOptional(arguments, "--capture-id");
        var json = CliOptions.IsJsonFormat(arguments);

        var result = await new CaptureService().ImportAsync(new CaptureImportRequest(project, source, platform, tag, captureId));
        WriteCaptureResult(result, json);
        return 0;
    }

    /// <summary>列出工程内的采集归档。`capture list` 与 `parse capture-list` 共用此实现。</summary>
    internal static async Task<int> ListCapturesAsync(string[] options)
    {
        CliOptions.EnsureOnly(options, CliOptions.Allowed("--project", "--platform", "--tag", "--format"));
        var project = await new ProjectService().OpenProjectAsync(CliOptions.GetRequired(options, "--project"));
        var json = CliOptions.IsJsonFormat(options);
        var captures = await new CaptureAnalysisService().ListCaptureDirectoriesAsync(
            project,
            CliOptions.GetOptional(options, "--platform"),
            CliOptions.GetOptional(options, "--tag"));

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                captures.Select(c => new { c.CaptureId, c.CaptureDate, c.Platform, c.Tag, c.RelativePath, c.HasManifest }),
                new JsonSerializerOptions { WriteIndented = true }));
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

    private static async Task<int> ShowCaptureInfoAsync(string[] options)
    {
        CliOptions.EnsureOnly(options, CliOptions.Allowed("--capture-dir", "--format"));
        var captureDir = CliOptions.GetRequired(options, "--capture-dir");
        var json = CliOptions.IsJsonFormat(options);
        var files = await new CaptureAnalysisService().ListCaptureFilesAsync(captureDir);
        var hasManifest = File.Exists(Path.Combine(captureDir, "CaptureManifest.json"));

        if (json)
        {
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
        Console.WriteLine(hasManifest ? "Manifest: present" : "Manifest: missing");
        Console.WriteLine();
        foreach (var file in files)
        {
            Console.WriteLine($"[{file.Category}] {file.FileName}  ({file.SizeBytes} bytes)");
        }

        Console.WriteLine($"{files.Count} file(s) found.");
        return 0;
    }

    private static void WriteCaptureResult(CaptureResult result, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new { result.Plan.CaptureId, result.Plan.CaptureDirectory, result.ManifestPath, result.Manifest.DeviceSerialNumber, result.Manifest.Tag },
                new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        Console.WriteLine($"Capture ID: {result.Plan.CaptureId}");
        Console.WriteLine($"Archive: {result.Plan.CaptureDirectory}");
        Console.WriteLine($"Manifest: {result.ManifestPath}");
    }

    private static int FailUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  unrealkit capture run --project <project.ukit> --device <serial>|auto [--tag <tag>] [--format text|json] [--skip-saved] [--adb-path <path>]");
        Console.Error.WriteLine("  unrealkit capture import --project <project.ukit> --source <directory> [--platform <platform>] [--tag <tag>] [--capture-id <id>]");
        Console.Error.WriteLine("  unrealkit capture list --project <project.ukit> [--platform <platform>] [--tag <tag>] [--format text|json]");
        Console.Error.WriteLine("  unrealkit capture info --capture-dir <path> [--format text|json]");
        return 2;
    }
}
