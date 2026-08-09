using System.Globalization;
using ClosedXML.Excel;
using UnrealKit.Core.Analysis;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Core.Export;

/// <summary>
/// Writes a trend to a real XLSX workbook via ClosedXML. Sheet and column names are a published
/// contract; renaming or reordering them is a breaking change.
/// </summary>
public sealed class XlsxTrendExportService : IXlsxTrendExportService
{
    private readonly Func<AppVersionInfo> _versionProvider;

    public XlsxTrendExportService(Func<AppVersionInfo>? versionProvider = null)
    {
        _versionProvider = versionProvider ?? AppVersionInfoProvider.GetCurrent;
    }

    public Task<TrendExportResult> ExportAsync(TrendExportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Result);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFilePath);
        var outputPath = Path.GetFullPath(request.OutputFilePath);
        if (!string.Equals(Path.GetExtension(outputPath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("XLSX trend export output must use a .xlsx extension.", nameof(request));
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var version = _versionProvider();
        using var workbook = new XLWorkbook();

        WriteMetadataSheet(workbook, request, version);
        WriteCapturesSheet(workbook, request.Result);
        WriteSeriesSheet(workbook, request.Result);
        if (request.IncludePoints)
        {
            WritePointsSheet(workbook, request.Result, cancellationToken);
        }

        WriteDiagnosticsSheet(workbook, request.Result.Diagnostics);

        workbook.SaveAs(outputPath);
        return Task.FromResult(new TrendExportResult(outputPath, TrendExportFormat.Xlsx));
    }

    private static void WriteMetadataSheet(XLWorkbook workbook, TrendExportRequest request, AppVersionInfo version)
    {
        var sheet = workbook.AddWorksheet("Metadata");
        WriteHeader(sheet, ["Key", "Value"]);
        var result = request.Result;
        var data = new (string, string?)[]
        {
            ("Project File", result.ProjectFilePath),
            ("Source", result.Source.ToString()),
            ("Platform", result.Platform),
            ("Tag", result.Tag),
            ("Device Serial Number", result.DeviceSerialNumber),
            ("Range From", TrendExportService.FormatDate(result.From)),
            ("Range To", TrendExportService.FormatDate(result.To)),
            ("Capture Count", result.Captures.Count.ToString(CultureInfo.InvariantCulture)),
            ("Metric Count", result.Series.Count.ToString(CultureInfo.InvariantCulture)),
            ("Regressed", result.RegressedCount.ToString(CultureInfo.InvariantCulture)),
            ("Improved", result.ImprovedCount.ToString(CultureInfo.InvariantCulture)),
            ("Unchanged", result.UnchangedCount.ToString(CultureInfo.InvariantCulture)),
            ("Exported At (UTC)", request.ExportedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            ("Tool Version", version.Version),
            ("Tool Git Commit", version.GitCommit),
            ("Trend Success", result.IsSuccess.ToString())
        };

        for (var index = 0; index < data.Length; index++)
        {
            sheet.Cell(index + 2, 1).Value = data[index].Item1;
            sheet.Cell(index + 2, 2).Value = data[index].Item2 ?? "-";
        }

        sheet.Columns().AdjustToContents();
    }

    private static void WriteCapturesSheet(XLWorkbook workbook, TrendResult result)
    {
        var sheet = workbook.AddWorksheet("Trend Captures");
        WriteHeader(sheet, ["CaptureId", "CaptureDate", "Platform", "Tag", "DeviceSerialNumber", "DeviceModel", "InputFile"]);
        for (var index = 0; index < result.Captures.Count; index++)
        {
            var capture = result.Captures[index];
            var row = index + 2;
            sheet.Cell(row, 1).Value = capture.CaptureId;
            sheet.Cell(row, 2).Value = capture.CaptureDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            sheet.Cell(row, 3).Value = capture.Platform;
            sheet.Cell(row, 4).Value = capture.Tag;
            sheet.Cell(row, 5).Value = capture.DeviceSerialNumber ?? "-";
            sheet.Cell(row, 6).Value = capture.DeviceModel ?? "-";
            sheet.Cell(row, 7).Value = capture.InputPath;
        }

        sheet.Columns().AdjustToContents();
    }

    private static void WriteSeriesSheet(XLWorkbook workbook, TrendResult result)
    {
        var sheet = workbook.AddWorksheet("Trend Series");
        WriteHeader(sheet,
        [
            "Group", "Metric", "Unit", "Direction", "CaptureCount", "PresentCount", "MissingCount",
            "First", "Last", "Minimum", "Maximum", "Average", "TotalDelta", "TotalDeltaPercent", "Assessment"
        ]);

        for (var index = 0; index < result.Series.Count; index++)
        {
            var series = result.Series[index];
            var row = index + 2;
            sheet.Cell(row, 1).Value = series.Group;
            sheet.Cell(row, 2).Value = series.Name;
            sheet.Cell(row, 3).Value = series.Unit;
            sheet.Cell(row, 4).Value = series.Direction.ToString();
            sheet.Cell(row, 5).SetValue(series.PointCount);
            sheet.Cell(row, 6).SetValue(series.PresentCount);
            sheet.Cell(row, 7).SetValue(series.MissingCount);
            SetNumericCell(sheet, row, 8, series.First);
            SetNumericCell(sheet, row, 9, series.Last);
            SetNumericCell(sheet, row, 10, series.Minimum);
            SetNumericCell(sheet, row, 11, series.Maximum);
            SetNumericCell(sheet, row, 12, series.Average);
            SetNumericCell(sheet, row, 13, series.TotalDelta);
            SetNumericCell(sheet, row, 14, series.TotalDeltaPercent);
            sheet.Cell(row, 15).Value = series.OverallAssessment.ToString();
        }

        sheet.Columns().AdjustToContents();
    }

    private static void WritePointsSheet(XLWorkbook workbook, TrendResult result, CancellationToken cancellationToken)
    {
        var sheet = workbook.AddWorksheet("Trend Points");
        WriteHeader(sheet, ["Group", "Metric", "Unit", "CaptureId", "CaptureDate", "Value", "DeltaFromPrevious", "Assessment"]);
        var row = 2;
        foreach (var series in result.Series)
        {
            foreach (var point in series.Points)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sheet.Cell(row, 1).Value = series.Group;
                sheet.Cell(row, 2).Value = series.Name;
                sheet.Cell(row, 3).Value = series.Unit;
                sheet.Cell(row, 4).Value = point.CaptureId;
                sheet.Cell(row, 5).Value = point.CaptureDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                SetNumericCell(sheet, row, 6, point.Value);
                SetNumericCell(sheet, row, 7, point.DeltaFromPrevious);
                sheet.Cell(row, 8).Value = point.Assessment.ToString();
                row++;
            }
        }

        sheet.Columns().AdjustToContents();
    }

    private static void WriteDiagnosticsSheet(XLWorkbook workbook, IReadOnlyList<Diagnostics.Diagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return;
        }

        var sheet = workbook.AddWorksheet("Diagnostics");
        WriteHeader(sheet, ["Severity", "Code", "Message", "Path", "Suggested Fix"]);
        for (var index = 0; index < diagnostics.Count; index++)
        {
            var diagnostic = diagnostics[index];
            var row = index + 2;
            sheet.Cell(row, 1).Value = diagnostic.Severity.ToString();
            sheet.Cell(row, 2).Value = diagnostic.Code;
            sheet.Cell(row, 3).Value = diagnostic.Message;
            sheet.Cell(row, 4).Value = diagnostic.Path ?? "-";
            sheet.Cell(row, 5).Value = diagnostic.SuggestedFix ?? "-";
        }

        sheet.Columns().AdjustToContents();
    }

    /// <summary>A missing value is written as the explicit token "missing", never as 0 or a blank cell.</summary>
    private static void SetNumericCell(IXLWorksheet sheet, int row, int column, double? value)
    {
        if (value.HasValue)
        {
            sheet.Cell(row, column).SetValue(value.GetValueOrDefault());
        }
        else
        {
            sheet.Cell(row, column).SetValue("missing");
        }
    }

    private static void WriteHeader(IXLWorksheet sheet, string[] headers)
    {
        for (var index = 0; index < headers.Length; index++)
        {
            var cell = sheet.Cell(1, index + 1);
            cell.Value = headers[index];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }
    }
}
