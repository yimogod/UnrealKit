using System.Globalization;
using UnrealKit.Core.Analysis;
using UnrealKit.Core.Capture;
using UnrealKit.Core.Export;
using UnrealKit.Core.Projects;

namespace UnrealKit.Cli;

/// <summary>`unrealkit analyze diff|trend`：基线差分与历史趋势。</summary>
internal static class AnalyzeCommands
{
    internal static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            return FailUsage();
        }

        return arguments[0].ToLowerInvariant() switch
        {
            "diff" => await RunDiffAsync(arguments[1..]),
            "trend" => await RunTrendAsync(arguments[1..]),
            _ => FailUsage()
        };
    }

    private static async Task<int> RunDiffAsync(string[] options)
    {
        CliOptions.EnsureOnly(
            options,
            CliOptions.Allowed(
                "--source", "--baseline", "--current", "--project",
                "--baseline-file", "--current-file", "--metrics", "--format", "--only-changed"),
            CliOptions.Allowed("--only-changed"));

        var source = ParseSource(CliOptions.GetOptional(options, "--source"));
        var baseline = CliOptions.GetRequired(options, "--baseline");
        var current = CliOptions.GetRequired(options, "--current");
        var projectPath = CliOptions.GetOptional(options, "--project");
        var metrics = CliOptions.GetCommaSeparated(options, "--metrics");
        var onlyChanged = CliOptions.HasFlag(options, "--only-changed");
        var json = CliOptions.IsJsonFormat(options);

        var inputs = projectPath is null
            ? ResolveFileInputs(options, baseline, current)
            : await ResolveCaptureInputsAsync(options, projectPath, baseline, current, source);

        var result = await new BaselineService().DiffAsync(new BaselineDiffRequest(
            source,
            inputs.BaselinePath,
            inputs.CurrentPath,
            metrics.Length == 0 ? null : metrics,
            inputs.BaselineLabel,
            inputs.CurrentLabel));

        AnalyzeResultWriters.WriteDiff(result, onlyChanged, json);
        return result.IsSuccess ? 0 : 1;
    }

    private static async Task<int> RunTrendAsync(string[] options)
    {
        CliOptions.EnsureOnly(
            options,
            CliOptions.Allowed(
                "--project", "--source", "--platform", "--tag", "--device", "--from", "--to",
                "--metrics", "--file", "--output", "--format", "--include-points"),
            CliOptions.Allowed("--include-points"));

        var project = await new ProjectService().OpenProjectAsync(CliOptions.GetRequired(options, "--project"));
        var metrics = CliOptions.GetCommaSeparated(options, "--metrics");
        var includePoints = CliOptions.HasFlag(options, "--include-points");
        var output = CliOptions.GetOptional(options, "--output");
        var json = CliOptions.IsJsonFormat(options);

        var result = await new TrendService().BuildTrendAsync(new TrendRequest(
            project,
            ParseSource(CliOptions.GetOptional(options, "--source")),
            CliOptions.GetOptional(options, "--platform"),
            CliOptions.GetOptional(options, "--tag"),
            CliOptions.GetOptional(options, "--device"),
            ParseDate(CliOptions.GetOptional(options, "--from"), "--from"),
            ParseDate(CliOptions.GetOptional(options, "--to"), "--to"),
            metrics.Length == 0 ? null : metrics,
            CliOptions.GetOptional(options, "--file")));

        string? exportedPath = null;
        if (output is not null)
        {
            var request = new TrendExportRequest(result, output, DateTimeOffset.UtcNow, includePoints);
            exportedPath = output.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? (await new XlsxTrendExportService().ExportAsync(request)).OutputFilePath
                : (await new TrendExportService().ExportAsync(request)).OutputFilePath;
        }

        AnalyzeResultWriters.WriteTrend(result, includePoints, exportedPath, json);
        return result.IsSuccess ? 0 : 1;
    }

    private static (string BaselinePath, string CurrentPath, string? BaselineLabel, string? CurrentLabel) ResolveFileInputs(
        string[] options,
        string baseline,
        string current)
    {
        // 不带 --project 时两侧是裸文件路径，归档内选文件的选项此时无处可依。
        if (CliOptions.GetOptional(options, "--baseline-file") is not null
            || CliOptions.GetOptional(options, "--current-file") is not null)
        {
            throw new ArgumentException("--baseline-file and --current-file require --project, because they name a file inside a capture archive.");
        }

        return (baseline, current, null, null);
    }

    private static async Task<(string BaselinePath, string CurrentPath, string? BaselineLabel, string? CurrentLabel)> ResolveCaptureInputsAsync(
        string[] options,
        string projectPath,
        string baseline,
        string current,
        BaselineDiffSource source)
    {
        var project = await new ProjectService().OpenProjectAsync(projectPath);
        var analysisService = new CaptureAnalysisService();
        var captures = await analysisService.ListCaptureDirectoriesAsync(project, platform: null, tag: null);
        var baselineDirectory = ResolveCaptureDirectory(captures, baseline);
        var currentDirectory = ResolveCaptureDirectory(captures, current);

        return (
            await ResolveCaptureFileAsync(analysisService, baselineDirectory, CliOptions.GetOptional(options, "--baseline-file"), source, "--baseline-file"),
            await ResolveCaptureFileAsync(analysisService, currentDirectory, CliOptions.GetOptional(options, "--current-file"), source, "--current-file"),
            Path.GetFileName(baselineDirectory),
            Path.GetFileName(currentDirectory));
    }

    internal static BaselineDiffSource ParseSource(string? value) => (value ?? "meminfo").ToLowerInvariant() switch
    {
        "meminfo" => BaselineDiffSource.MemInfo,
        "memreport" => BaselineDiffSource.MemReport,
        "static-camera" => BaselineDiffSource.StaticCamera,
        "win64-meminfo" => BaselineDiffSource.Win64MemInfo,
        _ => throw new ArgumentException("--source must be one of meminfo, win64-meminfo, memreport, or static-camera.")
    };

    private static DateTimeOffset? ParseDate(string? value, string optionName)
    {
        if (value is null)
        {
            return null;
        }

        if (!DateTimeOffset.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw new ArgumentException($"{optionName} must be a date in yyyy-MM-dd format.");
        }

        return parsed;
    }

    /// <summary>
    /// 把 Capture ID 或目录路径解析成唯一的采集目录。
    /// ID 命中多个归档时报错并列出候选，不取第一个。
    /// </summary>
    private static string ResolveCaptureDirectory(IReadOnlyList<CaptureDirectoryInfo> captures, string captureIdOrPath)
    {
        if (Path.IsPathRooted(captureIdOrPath) || captureIdOrPath.Contains('/') || captureIdOrPath.Contains('\\'))
        {
            var fullPath = Path.GetFullPath(captureIdOrPath);
            return Directory.Exists(fullPath)
                ? fullPath
                : throw new ArgumentException($"Capture directory not found: {fullPath}");
        }

        var matches = captures.Where(capture => string.Equals(capture.CaptureId, captureIdOrPath, StringComparison.Ordinal)).ToArray();
        return matches.Length switch
        {
            1 => matches[0].FullPath,
            0 => throw new ArgumentException($"Capture not found: {captureIdOrPath}. Use 'unrealkit parse capture-list --project <project.ukit>' to list available captures."),
            _ => throw new ArgumentException($"Capture ID '{captureIdOrPath}' matches {matches.Length} archives. Pass the capture directory path instead: {string.Join(", ", matches.Select(match => match.RelativePath))}")
        };
    }

    /// <summary>
    /// 在采集归档内定位输入文件。未显式指定文件名时按报告类型推断类别，
    /// 该类别下不唯一时报错并列出候选，避免两侧读到不同种类的输入。
    /// </summary>
    private static async Task<string> ResolveCaptureFileAsync(
        CaptureAnalysisService service,
        string captureDirectory,
        string? fileName,
        BaselineDiffSource source,
        string optionName)
    {
        var files = await service.ListCaptureFilesAsync(captureDirectory);
        if (fileName is not null)
        {
            var named = files.FirstOrDefault(file => string.Equals(file.FileName, fileName, StringComparison.Ordinal));
            return named is null
                ? throw new ArgumentException($"File '{fileName}' not found in capture '{Path.GetFileName(captureDirectory)}'. Available files: {string.Join(", ", files.Select(file => file.FileName))}")
                : named.FullPath;
        }

        var category = source switch
        {
            BaselineDiffSource.MemInfo => "MemInfo",
            BaselineDiffSource.Win64MemInfo => "MemInfo",
            BaselineDiffSource.MemReport => "Saved",
            BaselineDiffSource.StaticCamera => "Saved",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unsupported baseline diff source.")
        };

        var candidates = files.Where(file => string.Equals(file.Category, category, StringComparison.OrdinalIgnoreCase)).ToArray();
        return candidates.Length switch
        {
            1 => candidates[0].FullPath,
            0 => throw new ArgumentException($"No {category} files found in capture '{Path.GetFileName(captureDirectory)}'. Use {optionName} <filename> to name the input explicitly."),
            _ => throw new ArgumentException($"Capture '{Path.GetFileName(captureDirectory)}' contains {candidates.Length} {category} files. Use {optionName} <filename> to select one: {string.Join(", ", candidates.Select(file => file.FileName))}")
        };
    }

    private static int FailUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  unrealkit analyze diff --baseline <file> --current <file> [--source meminfo|win64-meminfo|memreport|static-camera]");
        Console.Error.WriteLine("                         [--metrics <name[,name...]>] [--only-changed] [--format text|json]");
        Console.Error.WriteLine("  unrealkit analyze diff --project <project.ukit> --baseline <capture-id> --current <capture-id>");
        Console.Error.WriteLine("                         [--baseline-file <filename>] [--current-file <filename>] [--source <source>]");
        Console.Error.WriteLine("                         [--metrics <name[,name...]>] [--only-changed] [--format text|json]");
        Console.Error.WriteLine("  unrealkit analyze trend --project <project.ukit> [--source meminfo|win64-meminfo|memreport|static-camera]");
        Console.Error.WriteLine("                          [--platform <platform>] [--tag <tag>] [--device <serial>]");
        Console.Error.WriteLine("                          [--from <yyyy-MM-dd>] [--to <yyyy-MM-dd>] [--metrics <name[,name...]>]");
        Console.Error.WriteLine("                          [--file <filename>] [--output <file.csv|file.tsv|file.xlsx>]");
        Console.Error.WriteLine("                          [--include-points] [--format text|json]");
        return 2;
    }
}
