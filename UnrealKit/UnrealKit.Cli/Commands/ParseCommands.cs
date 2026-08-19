using System.Text.Json;
using UnrealKit.Core.Capture;
using UnrealKit.Core.Parsing;
using UnrealKit.Core.Projects;

namespace UnrealKit.Cli;

/// <summary>`unrealkit parse ...`：单文件解析与归档内解析。</summary>
internal static class ParseCommands
{
    internal static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            return FailUsage();
        }

        return arguments[0].ToLowerInvariant() switch
        {
            "meminfo" => await ParseMemInfoAsync(arguments[1..]),
            "win64-meminfo" => await ParseWin64MemInfoAsync(arguments[1..]),
            "capture-list" => await CaptureCommands.ListCapturesAsync(arguments[1..]),
            "capture-files" => await ListCaptureFilesAsync(arguments[1..]),
            "capture-meminfo" => await ParseCaptureMemInfoAsync(arguments[1..]),
            "memreport" => await ParseMemReportAsync(arguments[1..]),
            "static-camera" => await ParseStaticCameraAsync(arguments[1..]),
            _ => FailUsage()
        };
    }

    private static async Task<int> ParseMemInfoAsync(string[] options)
    {
        CliOptions.EnsureOnly(options, CliOptions.Allowed("--input", "--format"));
        var result = await new AndroidMemInfoParser().ParseFileAsync(CliOptions.GetRequired(options, "--input"));
        ParseResultWriters.WriteMemInfo(result, CliOptions.IsJsonFormat(options));
        return result.IsSuccess ? 0 : 1;
    }

    private static async Task<int> ParseWin64MemInfoAsync(string[] options)
    {
        CliOptions.EnsureOnly(options, CliOptions.Allowed("--input", "--format"));
        var result = await new Win64MemInfoParser().ParseFileAsync(CliOptions.GetRequired(options, "--input"));
        ParseResultWriters.WriteWin64MemInfo(result, CliOptions.IsJsonFormat(options));
        return result.IsSuccess ? 0 : 1;
    }

    private static async Task<int> ParseMemReportAsync(string[] options)
    {
        CliOptions.EnsureOnly(options, CliOptions.Allowed("--input", "--format"));
        var result = await new UnrealMemReportParser().ParseFileAsync(CliOptions.GetRequired(options, "--input"));
        ParseResultWriters.WriteMemReport(result, CliOptions.IsJsonFormat(options));
        return result.IsSuccess ? 0 : 1;
    }

    private static async Task<int> ParseStaticCameraAsync(string[] options)
    {
        CliOptions.EnsureOnly(options, CliOptions.Allowed("--input", "--screenshots", "--format"));
        var input = CliOptions.GetRequired(options, "--input");
        var screenshots = CliOptions.GetOptional(options, "--screenshots");
        var parser = new StaticCameraPerfParser();
        var result = !string.IsNullOrWhiteSpace(screenshots) && Directory.Exists(screenshots)
            ? await parser.ParseFileAsync(input, screenshots)
            : await parser.ParseFileAsync(input);
        ParseResultWriters.WriteStaticCamera(result, CliOptions.IsJsonFormat(options));
        return result.IsSuccess ? 0 : 1;
    }

    private static async Task<int> ListCaptureFilesAsync(string[] options)
    {
        CliOptions.EnsureOnly(options, CliOptions.Allowed("--capture-dir", "--format"));
        var captureDir = CliOptions.GetRequired(options, "--capture-dir");
        var json = CliOptions.IsJsonFormat(options);
        var files = await new CaptureAnalysisService().ListCaptureFilesAsync(captureDir);
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                files.Select(f => new { f.Category, f.FileName, f.SizeBytes, f.FullPath }),
                new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        foreach (var file in files)
        {
            Console.WriteLine($"[{file.Category}] {file.FileName}  ({file.SizeBytes} bytes)");
        }

        Console.WriteLine($"{files.Count} file(s) found.");
        return 0;
    }

    private static async Task<int> ParseCaptureMemInfoAsync(string[] options)
    {
        CliOptions.EnsureOnly(options, CliOptions.Allowed("--project", "--capture", "--file", "--analysis-id", "--format"));
        var project = await new ProjectService().OpenProjectAsync(CliOptions.GetRequired(options, "--project"));
        var captureIdOrPath = CliOptions.GetRequired(options, "--capture");
        var fileName = CliOptions.GetRequired(options, "--file");
        var analysisId = CliOptions.GetOptional(options, "--analysis-id");
        var json = CliOptions.IsJsonFormat(options);

        var service = new CaptureAnalysisService();
        var captureDirectoryPath = await ResolveCaptureDirectoryAsync(service, project, captureIdOrPath);

        var captureFiles = await service.ListCaptureFilesAsync(captureDirectoryPath);
        var targetFile = captureFiles.FirstOrDefault(f => string.Equals(f.FileName, fileName, StringComparison.Ordinal));
        if (targetFile is null)
        {
            var availableNames = string.Join(", ", captureFiles
                .Where(f => string.Equals(f.Category, "MemInfo", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.FileName));
            throw new ArgumentException(
                $"Meminfo file '{fileName}' not found in capture. Available MemInfo files: {availableNames}");
        }

        if (!string.Equals(targetFile.Category, "MemInfo", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"File '{fileName}' is in category '{targetFile.Category}', not MemInfo.");
        }

        var result = await service.AnalyzeMemInfoAsync(
            new CaptureAnalysisRequest(project, captureDirectoryPath, targetFile.FullPath, analysisId));

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
            ParseResultWriters.WriteMemInfo(result.ParseResult, false);
        }

        return result.ParseResult.IsSuccess ? 0 : 1;
    }

    /// <summary>`--capture` 既接受采集目录路径，也接受工程内的 Capture ID；ID 未命中时报错并给出查询命令。</summary>
    private static async Task<string> ResolveCaptureDirectoryAsync(
        CaptureAnalysisService service,
        UkitProject project,
        string captureIdOrPath)
    {
        if (Path.IsPathRooted(captureIdOrPath) || captureIdOrPath.Contains('/') || captureIdOrPath.Contains('\\'))
        {
            return Path.GetFullPath(captureIdOrPath);
        }

        // 跨全部平台查找，因此同一 ID 可能在多个平台目录下出现；命中多份时报错并
        // 列出候选，不取第一个——与 analyze diff 的解析规则一致。
        var captures = await service.ListCaptureDirectoriesAsync(project, platform: null, tag: null);
        var matches = captures.Where(c => string.Equals(c.CaptureId, captureIdOrPath, StringComparison.Ordinal)).ToArray();
        return matches.Length switch
        {
            1 => matches[0].FullPath,
            0 => throw new ArgumentException($"Capture not found: {captureIdOrPath}. Use 'unrealkit parse capture-list --project <project.ukit>' to list available captures."),
            _ => throw new ArgumentException(
                $"Capture ID '{captureIdOrPath}' matches {matches.Length} archives. Pass the capture directory path instead: " +
                string.Join(", ", matches.Select(match => match.RelativePath)))
        };
    }

    private static int FailUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  unrealkit parse meminfo --input <meminfo.txt> [--format text|json]");
        Console.Error.WriteLine("  unrealkit parse win64-meminfo --input <meminfo.txt> [--format text|json]");
        Console.Error.WriteLine("  unrealkit parse memreport --input <memreport.txt> [--format text|json]");
        Console.Error.WriteLine("  unrealkit parse capture-list --project <project.ukit> [--platform <platform>] [--tag <tag>]");
        Console.Error.WriteLine("  unrealkit parse capture-files --capture-dir <path>");
        Console.Error.WriteLine("  unrealkit parse capture-meminfo --project <project.ukit> --capture <capture-id> [--file <filename>] [--analysis-id <id>]");
        Console.Error.WriteLine("  unrealkit parse static-camera --input <log> [--screenshots <dir>] [--format json]");
        return 2;
    }
}
