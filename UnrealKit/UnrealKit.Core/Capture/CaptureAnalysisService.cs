using System.Globalization;
using System.Text.Json;
using UnrealKit.Core.Parsing;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Core.Capture;

public sealed class CaptureAnalysisService : ICaptureAnalysisService
{
    private const string MemInfoCategory = "MemInfo";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Func<AppVersionInfo> _versionProvider;
    private readonly TimeProvider _timeProvider;

    public CaptureAnalysisService(
        Func<AppVersionInfo>? versionProvider = null,
        TimeProvider? timeProvider = null)
    {
        _versionProvider = versionProvider ?? AppVersionInfoProvider.GetCurrent;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<IReadOnlyList<CaptureDirectoryInfo>> ListCaptureDirectoriesAsync(
        Projects.UkitProject project,
        string? platform = null,
        string? tag = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var root = project.ContentDir;
        if (!Directory.Exists(root))
        {
            return Task.FromResult<IReadOnlyList<CaptureDirectoryInfo>>(Array.Empty<CaptureDirectoryInfo>());
        }

        var captures = new List<CaptureDirectoryInfo>();

        // platform 为 null 时枚举 Content 下的全部平台目录。此前默认只看 Android，
        // 于是 Win64 归档在 GUI 采集列表与历史趋势里既不显示也不报错，看起来像是没采过。
        // 平台名取自目录名本身，不与 TargetPlatform 枚举比对：尚未纳入枚举的平台目录
        // 同样应当被列出，按枚举过滤会重新引入静默跳过。
        foreach (var platformPath in Directory.EnumerateDirectories(root))
        {
            var platformDir = Path.GetFileName(platformPath);
            if (platform is not null && !string.Equals(platformDir, platform, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            foreach (var tagDir in Directory.EnumerateDirectories(platformPath))
            {
                if (tag is not null && !string.Equals(Path.GetFileName(tagDir), tag, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();

                foreach (var dateDir in Directory.EnumerateDirectories(tagDir))
                {
                    foreach (var captureDir in Directory.EnumerateDirectories(dateDir))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var manifestPath = Path.Combine(captureDir, "CaptureManifest.json");
                        var hasManifest = File.Exists(manifestPath);
                        var captureId = Path.GetFileName(captureDir);
                        var dateName = Path.GetFileName(dateDir);

                        DateTimeOffset captureDate;
                        if (!DateTimeOffset.TryParseExact(
                                dateName, "yyyy-MM-dd",
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None,
                                out captureDate))
                        {
                            captureDate = DateTimeOffset.MinValue;
                        }

                        captures.Add(new CaptureDirectoryInfo(
                            captureDir,
                            Path.GetRelativePath(root, captureDir).Replace(Path.DirectorySeparatorChar, '/'),
                            captureId,
                            platformDir,
                            Path.GetFileName(tagDir),
                            captureDate,
                            manifestPath,
                            hasManifest));
                    }
                }
            }
        }

        // 同日期的多份归档按 CaptureId 稳定排序：目录枚举顺序由文件系统决定，
        // 仅按日期排会让「最近一份」在两次刷新间跳动。
        captures.Sort((a, b) => b.CaptureDate.CompareTo(a.CaptureDate) is var byDate && byDate != 0
            ? byDate
            : string.CompareOrdinal(b.CaptureId, a.CaptureId));
        return Task.FromResult<IReadOnlyList<CaptureDirectoryInfo>>(captures);
    }

    public Task<IReadOnlyList<CaptureFileInfo>> ListCaptureFilesAsync(
        string captureDirectoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(captureDirectoryPath);
        var fullPath = Path.GetFullPath(captureDirectoryPath);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Capture directory not found: {fullPath}");
        }

        var files = new List<CaptureFileInfo>();

        foreach (var categoryDir in new[] { MemInfoCategory, "Saved", "Logs", "Screenshots", "Profiling", "GPUDumps" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var categoryPath = Path.Combine(fullPath, categoryDir);
            if (!Directory.Exists(categoryPath))
            {
                continue;
            }

            foreach (var filePath in Directory.EnumerateFiles(categoryPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileInfo = new FileInfo(filePath);
                files.Add(new CaptureFileInfo(
                    fileInfo.Name,
                    filePath,
                    fileInfo.Length,
                    categoryDir));
            }
        }

        return Task.FromResult<IReadOnlyList<CaptureFileInfo>>(files);
    }

    public async Task<CaptureAnalysisResult> AnalyzeMemInfoAsync(
        CaptureAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Project);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CaptureDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InputFilePath);

        var captureDirectory = Path.GetFullPath(request.CaptureDirectoryPath);
        if (!Directory.Exists(captureDirectory))
        {
            throw new DirectoryNotFoundException($"Capture directory not found: {captureDirectory}");
        }

        var inputFilePath = Path.GetFullPath(request.InputFilePath);
        if (!File.Exists(inputFilePath))
        {
            throw new FileNotFoundException("Meminfo input file not found.", inputFilePath);
        }

        var captureId = Path.GetFileName(captureDirectory);
        var analysisId = string.IsNullOrWhiteSpace(request.AnalysisId)
            ? $"{captureId}-{_timeProvider.GetLocalNow():yyyyMMdd-HHmmss}"
            : request.AnalysisId.Trim();

        if (analysisId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || analysisId.Contains('/') || analysisId.Contains('\\'))
        {
            throw new ArgumentException("Analysis ID must be a valid directory name.", nameof(request.AnalysisId));
        }

        var analysisDirectory = ComputeAnalysisDirectory(request.Project, analysisId);
        Directory.CreateDirectory(analysisDirectory);

        var parseResult = await new AndroidMemInfoParser().ParseFileAsync(inputFilePath, cancellationToken);

        var version = _versionProvider();
        var diagnostics = new List<Diagnostics.Diagnostic>(parseResult.Diagnostics);

        var metadata = new CaptureAnalysisMetadata(
            analysisId,
            captureId,
            inputFilePath,
            Path.GetFileName(inputFilePath),
            _timeProvider.GetUtcNow(),
            version.Version,
            version.GitCommit,
            parseResult.Report?.ProcessName,
            parseResult.Report?.ProcessId,
            parseResult.Report?.Summary.TotalPssKb,
            diagnostics.Count,
            parseResult.IsSuccess);

        var resultJsonPath = Path.Combine(analysisDirectory, "result.json");
        await WriteResultJsonAsync(resultJsonPath, metadata, parseResult, diagnostics, cancellationToken);

        return new CaptureAnalysisResult(
            analysisId,
            analysisDirectory,
            captureId,
            inputFilePath,
            parseResult,
            resultJsonPath,
            diagnostics);
    }

    public string ComputeAnalysisDirectory(Projects.UkitProject project, string analysisId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisId);
        return Path.Combine(project.SavedDir, "Analysis", analysisId);
    }

    private static async Task WriteResultJsonAsync(
        string outputPath,
        CaptureAnalysisMetadata metadata,
        AndroidMemInfoParseResult parseResult,
        IReadOnlyList<Diagnostics.Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var result = new
        {
            metadata = new
            {
                metadata.AnalysisId,
                metadata.CaptureId,
                metadata.InputFilePath,
                metadata.InputFileName,
                ParsedAtUtc = metadata.ParsedAtUtc.ToString("O"),
                metadata.ToolVersion,
                metadata.ToolGitCommit,
                metadata.IsSuccess
            },
            parseResult = parseResult.Report is null ? null : new
            {
                parseResult.Report.ProcessName,
                ProcessId = parseResult.Report.ProcessId.ToString(CultureInfo.InvariantCulture),
                Summary = new
                {
                    JavaHeapKb = Fmt(parseResult.Report.Summary.JavaHeapKb),
                    NativeHeapKb = Fmt(parseResult.Report.Summary.NativeHeapKb),
                    CodeKb = Fmt(parseResult.Report.Summary.CodeKb),
                    StackKb = Fmt(parseResult.Report.Summary.StackKb),
                    GraphicsKb = Fmt(parseResult.Report.Summary.GraphicsKb),
                    PrivateOtherKb = Fmt(parseResult.Report.Summary.PrivateOtherKb),
                    SystemKb = Fmt(parseResult.Report.Summary.SystemKb),
                    TotalPssKb = Fmt(parseResult.Report.Summary.TotalPssKb)
                }
            },
            diagnostics = diagnostics.Select(d => new
            {
                d.Severity,
                d.Code,
                d.Message,
                d.Path,
                d.LineNumber,
                d.SuggestedFix
            }).ToArray()
        };

        await using var stream = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(stream, result, JsonOptions, cancellationToken);
    }

    private static string? Fmt(long? value) => value?.ToString(CultureInfo.InvariantCulture);
}