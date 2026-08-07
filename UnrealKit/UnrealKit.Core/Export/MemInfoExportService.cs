using System.Globalization;
using System.Text;
using UnrealKit.Core.Parsing;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Core.Export;

public sealed class MemInfoExportService : IMemInfoExportService
{
    private static readonly string[] SummaryColumnNames =
    [
        "SourceFile", "ParsedAtUtc", "ToolVersion", "ToolGitCommit", "ProcessName", "ProcessId",
        "JavaHeapKb", "NativeHeapKb", "CodeKb", "StackKb", "GraphicsKb", "PrivateOtherKb", "SystemKb", "TotalPssKb"
    ];

    private static readonly string[] DetailColumnNames =
    [
        "CaptureId", "InputFile", "ParsedAtUtc", "ToolVersion", "ToolGitCommit", "ProcessName", "ProcessId",
        "Section", "Name", "Metric", "Value", "LineNumber"
    ];

    private readonly Func<AppVersionInfo> _versionProvider;

    public MemInfoExportService(Func<AppVersionInfo>? versionProvider = null)
    {
        _versionProvider = versionProvider ?? AppVersionInfoProvider.GetCurrent;
    }

    public Task<MemInfoExportResult> ExportAsync(MemInfoExportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExportCoreAsync(request, cancellationToken);
    }

    private async Task<MemInfoExportResult> ExportCoreAsync(MemInfoExportRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFilePath);
        var outputFilePath = Path.GetFullPath(request.OutputFilePath);
        var format = GetFormat(outputFilePath);
        var directory = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        return await WriteAsync(request, outputFilePath, format, cancellationToken);
    }

    private static MemInfoExportFormat GetFormat(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".csv" => MemInfoExportFormat.Csv,
        ".tsv" => MemInfoExportFormat.Tsv,
        _ => throw new ArgumentException("Meminfo export output must use a .csv or .tsv extension.", nameof(path))
    };

    private Task<MemInfoExportResult> WriteAsync(MemInfoExportRequest request, string outputPath, MemInfoExportFormat format, CancellationToken cancellationToken)
    {
        return WriteRecordAsync(request, outputPath, format, cancellationToken);
    }

    private async Task<MemInfoExportResult> WriteRecordAsync(MemInfoExportRequest request, string outputPath, MemInfoExportFormat format, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var report = request.ParseResult.Report;
        if (report is null) throw new InvalidOperationException();
        if (!request.ParseResult.IsSuccess) throw new InvalidOperationException();
        var delimiter = format == MemInfoExportFormat.Csv ? ',' : '\t';
        var version = _versionProvider();
        var rows = request.IncludeDetails ? CreateDetailRows(request, report, version) : CreateSummaryRows(request, report, version);
        var columns = request.IncludeDetails ? DetailColumnNames : SummaryColumnNames;

        await using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteLineAsync(JoinDelimited(columns, delimiter));
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(JoinDelimited(row, delimiter));
        }

        return new MemInfoExportResult(outputPath, format);
    }

    private static string Fmt(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static IEnumerable<IReadOnlyList<string?>> CreateSummaryRows(MemInfoExportRequest request, AndroidMemInfoReport report, AppVersionInfo version)
    {
        var summary = report.Summary;
        yield return
        [
            request.ParseResult.InputPath, request.ParsedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            version.Version, version.GitCommit, report.ProcessName, report.ProcessId.ToString(CultureInfo.InvariantCulture),
            Fmt(summary.JavaHeapKb), Fmt(summary.NativeHeapKb), Fmt(summary.CodeKb), Fmt(summary.StackKb),
            Fmt(summary.GraphicsKb), Fmt(summary.PrivateOtherKb), Fmt(summary.SystemKb), Fmt(summary.TotalPssKb)
        ];
    }

    private static IEnumerable<IReadOnlyList<string?>> CreateDetailRows(MemInfoExportRequest request, AndroidMemInfoReport report, AppVersionInfo version)
    {
        var summary = report.Summary;
        foreach (var (metric, value) in new (string, long?)[]
        {
            ("JavaHeapKb", summary.JavaHeapKb), ("NativeHeapKb", summary.NativeHeapKb), ("CodeKb", summary.CodeKb),
            ("StackKb", summary.StackKb), ("GraphicsKb", summary.GraphicsKb), ("PrivateOtherKb", summary.PrivateOtherKb),
            ("SystemKb", summary.SystemKb), ("TotalPssKb", summary.TotalPssKb)
        })
        {
            if (value is not null) yield return DetailRow(request, report, version, "AppSummary", "AppSummary", metric, Fmt(value), null);
        }

        foreach (var entry in report.DetailedPssEntries)
        {
            foreach (var (metric, value) in new (string, long?)[]
            {
                ("TotalPssKb", entry.TotalPssKb), ("PrivateDirtyKb", entry.PrivateDirtyKb),
                ("PrivateCleanKb", entry.PrivateCleanKb), ("SwapPssKb", entry.SwapPssKb), ("RssKb", entry.RssKb),
                ("HeapSizeKb", entry.HeapSizeKb), ("HeapAllocKb", entry.HeapAllocKb), ("HeapFreeKb", entry.HeapFreeKb)
            })
            {
                if (value is not null) yield return DetailRow(request, report, version, "DetailedPss", entry.Name, metric, Fmt(value), entry.LineNumber);
            }
        }

        foreach (var entry in report.DalvikEntries)
            yield return DetailRow(request, report, version, "Dalvik", entry.Name, "PssKb", Fmt(entry.PssKb), entry.LineNumber);
        foreach (var entry in report.ObjectEntries)
            yield return DetailRow(request, report, version, "Objects", entry.Name, "Count", Fmt(entry.Count), entry.LineNumber);
        foreach (var diagnostic in request.ParseResult.Diagnostics)
            yield return DetailRow(request, report, version, "Diagnostics", diagnostic.Code, diagnostic.Severity.ToString(), diagnostic.Message, diagnostic.LineNumber);
    }

    private static IReadOnlyList<string?> DetailRow(MemInfoExportRequest request, AndroidMemInfoReport report, AppVersionInfo version, string section, string name, string metric, string? value, int? lineNumber) =>
    [
        request.CaptureId, request.ParseResult.InputPath, request.ParsedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        version.Version, version.GitCommit, report.ProcessName, report.ProcessId.ToString(CultureInfo.InvariantCulture),
        section, name, metric, value, lineNumber?.ToString(CultureInfo.InvariantCulture)
    ];

    private static string JoinDelimited(IEnumerable<string?> fields, char delimiter) => string.Join(delimiter, fields.Select(field => Escape(field, delimiter)));

    private static string Escape(string? field, char delimiter)
    {
        if (string.IsNullOrEmpty(field)) return string.Empty;
        return field.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
    }
}
