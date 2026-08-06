using System.Globalization;
using System.Text;
using UnrealKit.Core.Parsing;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Core.Export;

public sealed class MemInfoExportService : IMemInfoExportService
{
    private static readonly string[] ColumnNames =
    [
        "SourceFile", "ParsedAtUtc", "ToolVersion", "ToolGitCommit", "ProcessName", "ProcessId",
        "JavaHeapKb", "NativeHeapKb", "CodeKb", "StackKb", "GraphicsKb", "PrivateOtherKb", "SystemKb", "TotalPssKb"
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

    private Task<MemInfoExportResult> WriteRecordAsync(MemInfoExportRequest request, string outputPath, MemInfoExportFormat format, CancellationToken cancellationToken)
    {
        var report = request.ParseResult.Report;
        if (report is null) throw new InvalidOperationException();
        if (!request.ParseResult.IsSuccess) throw new InvalidOperationException();
        var summary = report.Summary;
        var delimiter = format == MemInfoExportFormat.Csv ? ',' : '\t';
        var version = _versionProvider();
        var fields = new string?[]
        {
            request.ParseResult.InputPath,
            request.ParsedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            version.Version, version.GitCommit, report.ProcessName, report.ProcessId.ToString(CultureInfo.InvariantCulture),
            Fmt(summary.JavaHeapKb), Fmt(summary.NativeHeapKb), Fmt(summary.CodeKb), Fmt(summary.StackKb),
            Fmt(summary.GraphicsKb), Fmt(summary.PrivateOtherKb), Fmt(summary.SystemKb), Fmt(summary.TotalPssKb)
        };
        File.WriteAllLines(outputPath, [string.Join(delimiter, ColumnNames), string.Join(delimiter, fields)]);
        return Task.FromResult(new MemInfoExportResult(outputPath, format));
    }

    private static string Fmt(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
}