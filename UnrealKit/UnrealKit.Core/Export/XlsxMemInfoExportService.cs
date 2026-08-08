using System.Globalization;
using ClosedXML.Excel;
using UnrealKit.Core.Parsing;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Core.Export;

public interface IXlsxMemInfoExportService
{
    Task<MemInfoExportResult> ExportAsync(MemInfoExportRequest request, CancellationToken cancellationToken = default);
}

public sealed class XlsxMemInfoExportService : IXlsxMemInfoExportService
{
    private readonly Func<AppVersionInfo> _versionProvider;

    public XlsxMemInfoExportService(Func<AppVersionInfo>? versionProvider = null)
    {
        _versionProvider = versionProvider ?? AppVersionInfoProvider.GetCurrent;
    }

    public Task<MemInfoExportResult> ExportAsync(MemInfoExportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFilePath);
        var outputPath = Path.GetFullPath(request.OutputFilePath);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        return WriteXlsxAsync(request, outputPath, cancellationToken);
    }

    private Task<MemInfoExportResult> WriteXlsxAsync(MemInfoExportRequest request, string outputPath, CancellationToken cancellationToken)
    {
        var report = request.ParseResult.Report;
        if (report is null || !request.ParseResult.IsSuccess)
        {
            throw new InvalidOperationException("Cannot export XLSX from a failed parse result.");
        }

        var version = _versionProvider();
        using var workbook = new XLWorkbook();

        WriteMetadataSheet(workbook, request, report, version);
        WriteSummarySheet(workbook, report.Summary);
        if (request.IncludeDetails)
        {
            WriteDetailSheets(workbook, report);
        }
        WriteDiagnosticsSheet(workbook, request.ParseResult.Diagnostics);

        workbook.SaveAs(outputPath);
        return Task.FromResult(new MemInfoExportResult(outputPath, MemInfoExportFormat.Xlsx));
    }

    private static void WriteMetadataSheet(XLWorkbook workbook, MemInfoExportRequest request, AndroidMemInfoReport report, AppVersionInfo version)
    {
        var sheet = workbook.AddWorksheet("Metadata");
        var headers = new[] { "Key", "Value" };
        WriteHeader(sheet, headers);
        var data = new (string, string?)[]
        {
            ("Input File", request.ParseResult.InputPath),
            ("Capture ID", request.CaptureId),
            ("Parsed At (UTC)", request.ParsedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            ("Tool Version", version.Version),
            ("Tool Git Commit", version.GitCommit),
            ("Process Name", report.ProcessName),
            ("Process ID", report.ProcessId.ToString(CultureInfo.InvariantCulture)),
            ("Parse Success", request.ParseResult.IsSuccess.ToString())
        };
        for (var i = 0; i < data.Length; i++)
        {
            sheet.Cell(i + 2, 1).Value = data[i].Item1;
            sheet.Cell(i + 2, 2).Value = data[i].Item2 ?? "-";
        }
        sheet.Columns().AdjustToContents();
    }

    private static void WriteSummarySheet(XLWorkbook workbook, AndroidMemInfoSummary summary)
    {
        var sheet = workbook.AddWorksheet("AndroidMemInfo");
        var headers = new[] { "Metric", "Value (KB)" };
        WriteHeader(sheet, headers);
        var metrics = new (string, long?)[]
        {
            ("Java Heap", summary.JavaHeapKb),
            ("Native Heap", summary.NativeHeapKb),
            ("Code", summary.CodeKb),
            ("Stack", summary.StackKb),
            ("Graphics", summary.GraphicsKb),
            ("Private Other", summary.PrivateOtherKb),
            ("System", summary.SystemKb),
            ("TOTAL PSS", summary.TotalPssKb)
        };
        for (var i = 0; i < metrics.Length; i++)
        {
            sheet.Cell(i + 2, 1).Value = metrics[i].Item1;
            var cell = sheet.Cell(i + 2, 2);
            if (metrics[i].Item2.HasValue)
                cell.SetValue(metrics[i].Item2.GetValueOrDefault());
            else
                cell.SetValue("N/A");
        }
        sheet.Columns().AdjustToContents();
    }

    private static void WriteDetailSheets(XLWorkbook workbook, AndroidMemInfoReport report)
    {
        if (report.DetailedPssEntries.Count > 0)
        {
            var sheet = workbook.AddWorksheet("PSS Details");
            WriteHeader(sheet, new[] { "Name", "Total PSS (KB)", "Private Dirty (KB)", "Private Clean (KB)", "Swap PSS (KB)", "RSS (KB)", "Heap Size (KB)", "Heap Alloc (KB)", "Heap Free (KB)", "Line" });
            for (var i = 0; i < report.DetailedPssEntries.Count; i++)
            {
                var entry = report.DetailedPssEntries[i];
                sheet.Cell(i + 2, 1).Value = entry.Name;
                SetNumericCell(sheet, i + 2, 2, entry.TotalPssKb);
                SetNumericCell(sheet, i + 2, 3, entry.PrivateDirtyKb);
                SetNumericCell(sheet, i + 2, 4, entry.PrivateCleanKb);
                SetNumericCell(sheet, i + 2, 5, entry.SwapPssKb);
                SetNumericCell(sheet, i + 2, 6, entry.RssKb);
                SetNumericCell(sheet, i + 2, 7, entry.HeapSizeKb);
                SetNumericCell(sheet, i + 2, 8, entry.HeapAllocKb);
                SetNumericCell(sheet, i + 2, 9, entry.HeapFreeKb);
                SetNumericCell(sheet, i + 2, 10, entry.LineNumber);
            }
            sheet.Columns().AdjustToContents();
        }

        if (report.DalvikEntries.Count > 0)
        {
            var sheet = workbook.AddWorksheet("Dalvik");
            WriteHeader(sheet, new[] { "Name", "PSS (KB)", "Line" });
            for (var i = 0; i < report.DalvikEntries.Count; i++)
            {
                var entry = report.DalvikEntries[i];
                sheet.Cell(i + 2, 1).Value = entry.Name;
                SetNumericCell(sheet, i + 2, 2, entry.PssKb);
                SetNumericCell(sheet, i + 2, 3, entry.LineNumber);
            }
            sheet.Columns().AdjustToContents();
        }

        if (report.ObjectEntries.Count > 0)
        {
            var sheet = workbook.AddWorksheet("Objects");
            WriteHeader(sheet, new[] { "Name", "Count", "Line" });
            for (var i = 0; i < report.ObjectEntries.Count; i++)
            {
                var entry = report.ObjectEntries[i];
                sheet.Cell(i + 2, 1).Value = entry.Name;
                SetNumericCell(sheet, i + 2, 2, entry.Count);
                SetNumericCell(sheet, i + 2, 3, entry.LineNumber);
            }
            sheet.Columns().AdjustToContents();
        }
    }

    private static void WriteDiagnosticsSheet(XLWorkbook workbook, IReadOnlyList<Diagnostics.Diagnostic> diagnostics)
    {
        if (diagnostics.Count == 0) return;
        var sheet = workbook.AddWorksheet("Diagnostics");
        WriteHeader(sheet, new[] { "Severity", "Code", "Message", "Line", "Suggested Fix" });
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
