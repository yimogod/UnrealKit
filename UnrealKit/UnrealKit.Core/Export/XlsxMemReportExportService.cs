using System.Globalization;
using ClosedXML.Excel;
using UnrealKit.Core.Parsing;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Core.Export;

public interface IXlsxMemReportExportService
{
    Task<MemReportExportResult> ExportAsync(MemReportExportRequest request, CancellationToken cancellationToken = default);
}

public sealed class XlsxMemReportExportService : IXlsxMemReportExportService
{
    private readonly Func<AppVersionInfo> _versionProvider;

    public XlsxMemReportExportService(Func<AppVersionInfo>? versionProvider = null)
    {
        _versionProvider = versionProvider ?? AppVersionInfoProvider.GetCurrent;
    }

    public Task<MemReportExportResult> ExportAsync(MemReportExportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFilePath);
        var outputPath = Path.GetFullPath(request.OutputFilePath);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        return WriteXlsxAsync(request, outputPath, cancellationToken);
    }

    private Task<MemReportExportResult> WriteXlsxAsync(MemReportExportRequest request, string outputPath, CancellationToken cancellationToken)
    {
        var report = request.ParseResult.Report;
        if (report is null || !request.ParseResult.IsSuccess)
        {
            throw new InvalidOperationException("Cannot export XLSX from a failed parse result.");
        }

        var version = _versionProvider();
        using var workbook = new XLWorkbook();

        WriteMetadataSheet(workbook, request, report, version);
        WriteSummarySheet(workbook, report);

        if (request.IncludeDetails)
        {
            if (report.Textures.Count > 0) WriteTextureSheet(workbook, report);
            if (report.RenderTargets.Count > 0) WriteRenderTargetSheet(workbook, report);
            if (report.Objects.Count > 0) WriteObjectSheet(workbook, report);
        }

        WriteDiagnosticsSheet(workbook, request.ParseResult.Diagnostics);

        workbook.SaveAs(outputPath);
        return Task.FromResult(new MemReportExportResult(outputPath, MemInfoExportFormat.Xlsx));
    }

    private static void WriteMetadataSheet(XLWorkbook workbook, MemReportExportRequest request, UnrealMemReport report, AppVersionInfo version)
    {
        var sheet = workbook.AddWorksheet("Metadata");
        WriteHeader(sheet, ["Key", "Value"]);
        var data = new (string, string?)[]
        {
            ("Input File", request.ParseResult.InputPath),
            ("Capture ID", request.CaptureId),
            ("Parsed At (UTC)", request.ParsedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            ("Tool Version", version.Version),
            ("Tool Git Commit", version.GitCommit),
            ("Changelist", report.Changelist),
            ("Parse Success", request.ParseResult.IsSuccess.ToString())
        };
        for (var i = 0; i < data.Length; i++)
        {
            sheet.Cell(i + 2, 1).Value = data[i].Item1;
            sheet.Cell(i + 2, 2).Value = data[i].Item2 ?? "-";
        }
        sheet.Columns().AdjustToContents();
    }

    private static void WriteSummarySheet(XLWorkbook workbook, UnrealMemReport report)
    {
        var sheet = workbook.AddWorksheet("MemReport Summary");
        WriteHeader(sheet, ["Group", "Metric", "Value (KB)", "Raw Value", "Status"]);
        var metrics = report.Summary.Metrics;
        for (var i = 0; i < metrics.Count; i++)
        {
            var m = metrics[i];
            sheet.Cell(i + 2, 1).Value = m.Group;
            sheet.Cell(i + 2, 2).Value = m.Name;
            if (m.ValueKb.HasValue)
                sheet.Cell(i + 2, 3).SetValue(m.ValueKb.GetValueOrDefault());
            else
                sheet.Cell(i + 2, 3).SetValue("-");
            sheet.Cell(i + 2, 4).Value = m.RawValue ?? "-";
            sheet.Cell(i + 2, 5).Value = m.Status.ToString();
        }
        sheet.Columns().AdjustToContents();
    }

    private static void WriteTextureSheet(XLWorkbook workbook, UnrealMemReport report)
    {
        var sheet = workbook.AddWorksheet("Textures");
        WriteHeader(sheet, ["Name", "Width", "Height", "Format", "Memory (KB)", "Line"]);
        for (var i = 0; i < report.Textures.Count; i++)
        {
            var t = report.Textures[i];
            sheet.Cell(i + 2, 1).Value = t.Name;
            SetNumericCell(sheet, i + 2, 2, t.Width);
            SetNumericCell(sheet, i + 2, 3, t.Height);
            sheet.Cell(i + 2, 4).Value = t.Format ?? "-";
            SetNumericCell(sheet, i + 2, 5, t.MemoryKb);
            SetNumericCell(sheet, i + 2, 6, t.LineNumber);
        }
        sheet.Columns().AdjustToContents();
    }

    private static void WriteRenderTargetSheet(XLWorkbook workbook, UnrealMemReport report)
    {
        var sheet = workbook.AddWorksheet("Render Targets");
        WriteHeader(sheet, ["Name", "Width", "Height", "Format", "Memory (KB)", "Line"]);
        for (var i = 0; i < report.RenderTargets.Count; i++)
        {
            var rt = report.RenderTargets[i];
            sheet.Cell(i + 2, 1).Value = rt.Name;
            SetNumericCell(sheet, i + 2, 2, rt.Width);
            SetNumericCell(sheet, i + 2, 3, rt.Height);
            sheet.Cell(i + 2, 4).Value = rt.Format ?? "-";
            SetNumericCell(sheet, i + 2, 5, rt.MemoryKb);
            SetNumericCell(sheet, i + 2, 6, rt.LineNumber);
        }
        sheet.Columns().AdjustToContents();
    }

    private static void WriteObjectSheet(XLWorkbook workbook, UnrealMemReport report)
    {
        var sheet = workbook.AddWorksheet("Objects");
        WriteHeader(sheet, ["Class Name", "Count", "Memory (KB)", "Line"]);
        for (var i = 0; i < report.Objects.Count; i++)
        {
            var obj = report.Objects[i];
            sheet.Cell(i + 2, 1).Value = obj.ClassName;
            SetNumericCell(sheet, i + 2, 2, obj.Count);
            SetNumericCell(sheet, i + 2, 3, obj.MemoryKb);
            SetNumericCell(sheet, i + 2, 4, obj.LineNumber);
        }
        sheet.Columns().AdjustToContents();
    }

    private static void WriteDiagnosticsSheet(XLWorkbook workbook, IReadOnlyList<Diagnostics.Diagnostic> diagnostics)
    {
        if (diagnostics.Count == 0) return;
        var sheet = workbook.AddWorksheet("Diagnostics");
        WriteHeader(sheet, ["Severity", "Code", "Message", "Line", "Suggested Fix"]);
        for (var i = 0; i < diagnostics.Count; i++)
        {
            var diag = diagnostics[i];
            sheet.Cell(i + 2, 1).Value = diag.Severity.ToString();
            sheet.Cell(i + 2, 2).Value = diag.Code;
            sheet.Cell(i + 2, 3).Value = diag.Message;
            sheet.Cell(i + 2, 4).Value = diag.LineNumber?.ToString(CultureInfo.InvariantCulture) ?? "-";
            sheet.Cell(i + 2, 5).Value = diag.SuggestedFix ?? "-";
        }
        sheet.Columns().AdjustToContents();
    }

    private static void SetNumericCell(IXLWorksheet sheet, int row, int col, long? value)
    {
        if (value.HasValue)
            sheet.Cell(row, col).SetValue(value.GetValueOrDefault());
        else
            sheet.Cell(row, col).SetValue("-");
    }

    private static void SetNumericCell(IXLWorksheet sheet, int row, int col, int? value)
    {
        if (value.HasValue)
            sheet.Cell(row, col).SetValue(value.GetValueOrDefault());
        else
            sheet.Cell(row, col).SetValue("-");
    }

    private static void WriteHeader(IXLWorksheet sheet, string[] headers)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }
    }
}