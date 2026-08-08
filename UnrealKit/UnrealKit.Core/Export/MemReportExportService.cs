using System.Globalization;
using System.Text;
using UnrealKit.Core.Parsing;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Core.Export;

public sealed class MemReportExportService : IMemReportExportService
{
    private static readonly string[] SummaryColumnNames =
    [
        "SourceFile", "ParsedAtUtc", "ToolVersion", "ToolGitCommit",
        "Changelist", "MetricGroup", "MetricName", "ValueKb", "RawValue", "Status"
    ];

    private static readonly string[] TextureColumnNames =
    [
        "CaptureId", "InputFile", "ParsedAtUtc", "ToolVersion", "ToolGitCommit", "Changelist",
        "TextureName", "Width", "Height", "Format", "MemoryKb", "Line"
    ];

    private static readonly string[] RenderTargetColumnNames =
    [
        "CaptureId", "InputFile", "ParsedAtUtc", "ToolVersion", "ToolGitCommit", "Changelist",
        "RenderTargetName", "Width", "Height", "Format", "MemoryKb", "Line"
    ];

    private static readonly string[] ObjectColumnNames =
    [
        "CaptureId", "InputFile", "ParsedAtUtc", "ToolVersion", "ToolGitCommit", "Changelist",
        "ClassName", "Count", "MemoryKb", "Line"
    ];

    private readonly Func<AppVersionInfo> _versionProvider;

    public MemReportExportService(Func<AppVersionInfo>? versionProvider = null)
    {
        _versionProvider = versionProvider ?? AppVersionInfoProvider.GetCurrent;
    }

    public Task<MemReportExportResult> ExportAsync(MemReportExportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExportCoreAsync(request, cancellationToken);
    }

    private async Task<MemReportExportResult> ExportCoreAsync(MemReportExportRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFilePath);
        var outputFilePath = Path.GetFullPath(request.OutputFilePath);
        var format = GetFormat(outputFilePath);
        var directory = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        if (request.ParseResult.Report is null) throw new InvalidOperationException("Cannot export from a failed parse result.");
        if (!request.ParseResult.IsSuccess) throw new InvalidOperationException("Cannot export from a parse result with errors.");

        var delimiter = format == MemInfoExportFormat.Csv ? ',' : '\t';
        var version = _versionProvider();
        var report = request.ParseResult.Report;

        await using var writer = new StreamWriter(outputFilePath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        // Summary sheet
        await writer.WriteLineAsync(JoinDelimited(SummaryColumnNames, delimiter));
        foreach (var metric in report.Summary.Metrics)
        {
            var row = new[]
            {
                request.ParseResult.InputPath,
                request.ParsedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                version.Version,
                version.GitCommit ?? string.Empty,
                report.Changelist,
                metric.Group,
                metric.Name,
                Fmt(metric.ValueKb),
                metric.RawValue ?? string.Empty,
                metric.Status.ToString()
            };
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(JoinDelimited(row, delimiter));
        }

        if (request.IncludeDetails)
        {
            // Textures
            if (report.Textures.Count > 0)
            {
                await writer.WriteLineAsync(string.Empty);
                await writer.WriteLineAsync(JoinDelimited(TextureColumnNames, delimiter));
                foreach (var tex in report.Textures)
                {
                    var row = new[]
                    {
                        request.CaptureId, request.ParseResult.InputPath,
                        request.ParsedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                        version.Version, version.GitCommit ?? string.Empty, report.Changelist,
                        tex.Name, Fmt(tex.Width), Fmt(tex.Height), tex.Format ?? string.Empty, Fmt(tex.MemoryKb), tex.LineNumber.ToString(CultureInfo.InvariantCulture)
                    };
                    cancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(JoinDelimited(row, delimiter));
                }
            }

            // Render Targets
            if (report.RenderTargets.Count > 0)
            {
                await writer.WriteLineAsync(string.Empty);
                await writer.WriteLineAsync(JoinDelimited(RenderTargetColumnNames, delimiter));
                foreach (var rt in report.RenderTargets)
                {
                    var row = new[]
                    {
                        request.CaptureId, request.ParseResult.InputPath,
                        request.ParsedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                        version.Version, version.GitCommit ?? string.Empty, report.Changelist,
                        rt.Name, Fmt(rt.Width), Fmt(rt.Height), rt.Format ?? string.Empty, Fmt(rt.MemoryKb), rt.LineNumber.ToString(CultureInfo.InvariantCulture)
                    };
                    cancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(JoinDelimited(row, delimiter));
                }
            }

            // Objects
            if (report.Objects.Count > 0)
            {
                await writer.WriteLineAsync(string.Empty);
                await writer.WriteLineAsync(JoinDelimited(ObjectColumnNames, delimiter));
                foreach (var obj in report.Objects)
                {
                    var row = new[]
                    {
                        request.CaptureId, request.ParseResult.InputPath,
                        request.ParsedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                        version.Version, version.GitCommit ?? string.Empty, report.Changelist,
                        obj.ClassName, Fmt(obj.Count), Fmt(obj.MemoryKb), obj.LineNumber.ToString(CultureInfo.InvariantCulture)
                    };
                    cancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(JoinDelimited(row, delimiter));
                }
            }
        }

        return new MemReportExportResult(outputFilePath, format);
    }

    private static string Fmt(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Fmt(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static MemInfoExportFormat GetFormat(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".csv" => MemInfoExportFormat.Csv,
        ".tsv" => MemInfoExportFormat.Tsv,
        _ => throw new ArgumentException("MemReport export output must use a .csv or .tsv extension.", nameof(path))
    };

    private static string JoinDelimited(IEnumerable<string?> fields, char delimiter) => string.Join(delimiter, fields.Select(field => Escape(field, delimiter)));

    private static string Escape(string? field, char delimiter)
    {
        if (string.IsNullOrEmpty(field)) return string.Empty;
        return field.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
    }
}