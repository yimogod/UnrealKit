using ClosedXML.Excel;
using UnrealKit.Core.Analysis;
using UnrealKit.Core.Export;
using UnrealKit.Core.Projects;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Tests;

public sealed class TrendExportServiceTests : IDisposable
{
    private static readonly DateTimeOffset ExportedAt = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "UnrealKit.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExportAsync_Csv_WritesSeriesHeaderAndRows()
    {
        var trend = await BuildTrendAsync("CsvExport");
        var path = Path.Combine(_temporaryDirectory, "trend.csv");

        var result = await CreateService().ExportAsync(new TrendExportRequest(trend, path, ExportedAt));

        Assert.Equal(TrendExportFormat.Csv, result.Format);
        Assert.Equal(Path.GetFullPath(path), result.OutputFilePath);

        var lines = await File.ReadAllLinesAsync(path);
        Assert.Equal(
            "ProjectFile,ExportedAtUtc,ToolVersion,ToolGitCommit,Source,Platform,Tag,DeviceSerialNumber,RangeFrom,RangeTo," +
            "Group,Metric,Unit,Direction,CaptureCount,PresentCount,MissingCount,First,Last,Minimum,Maximum,Average," +
            "TotalDelta,TotalDeltaPercent,Assessment",
            lines[0]);

        var totalRow = lines.Single(line => line.Contains(",TotalPssKb,", StringComparison.Ordinal));
        Assert.Contains("MemInfo", totalRow, StringComparison.Ordinal);
        Assert.Contains("LowerIsBetter", totalRow, StringComparison.Ordinal);
        Assert.Contains("30680", totalRow, StringComparison.Ordinal);
        Assert.Contains("33704", totalRow, StringComparison.Ordinal);
        Assert.Contains("Regressed", totalRow, StringComparison.Ordinal);
        Assert.Contains("1.2.3", totalRow, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_Tsv_UsesTabDelimiter()
    {
        var trend = await BuildTrendAsync("TsvExport");
        var path = Path.Combine(_temporaryDirectory, "trend.tsv");

        var result = await CreateService().ExportAsync(new TrendExportRequest(trend, path, ExportedAt));

        Assert.Equal(TrendExportFormat.Tsv, result.Format);
        var lines = await File.ReadAllLinesAsync(path);
        Assert.StartsWith("ProjectFile\tExportedAtUtc\t", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_MissingValues_WrittenAsMissingNotZero()
    {
        var trend = await BuildTrendAsync("MissingExport");
        var path = Path.Combine(_temporaryDirectory, "missing.csv");

        await CreateService().ExportAsync(new TrendExportRequest(trend, path, ExportedAt));

        var lines = await File.ReadAllLinesAsync(path);
        // SystemKb is absent from the newer capture, so its overall delta has no value.
        var systemRow = lines.Single(line => line.Contains(",SystemKb,", StringComparison.Ordinal));
        Assert.Contains("missing", systemRow, StringComparison.Ordinal);
        Assert.DoesNotContain(",0,0,", systemRow, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_SummaryOnly_OmitsPointSection()
    {
        var trend = await BuildTrendAsync("SummaryOnly");
        var path = Path.Combine(_temporaryDirectory, "summary.csv");

        await CreateService().ExportAsync(new TrendExportRequest(trend, path, ExportedAt));

        var content = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("DeltaFromPrevious", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_IncludePoints_WritesPointSectionWithPerCaptureRows()
    {
        var trend = await BuildTrendAsync("PointsExport");
        var path = Path.Combine(_temporaryDirectory, "points.csv");

        await CreateService().ExportAsync(new TrendExportRequest(trend, path, ExportedAt, IncludePoints: true));

        var lines = await File.ReadAllLinesAsync(path);
        var pointHeaderIndex = Array.FindIndex(lines, line => line.StartsWith("ProjectFile,ExportedAtUtc,ToolVersion,ToolGitCommit,Source,Group,", StringComparison.Ordinal));
        Assert.True(pointHeaderIndex > 0, "The point section header should follow the series section.");
        Assert.Contains("DeltaFromPrevious", lines[pointHeaderIndex], StringComparison.Ordinal);

        var pointRows = lines[(pointHeaderIndex + 1)..].Where(line => line.Contains(",TotalPssKb,", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, pointRows.Length);
        Assert.Contains(pointRows, row => row.Contains("capture-a", StringComparison.Ordinal) && row.Contains("2026-08-01", StringComparison.Ordinal));
        Assert.Contains(pointRows, row => row.Contains("capture-b", StringComparison.Ordinal) && row.Contains("TestModel", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportAsync_WithDiagnostics_WritesDiagnosticsSection()
    {
        var trend = await BuildTrendAsync("DiagnosticsExport");
        var path = Path.Combine(_temporaryDirectory, "diagnostics.csv");

        await CreateService().ExportAsync(new TrendExportRequest(trend, path, ExportedAt));

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("Severity,Code,Message,Path,SuggestedFix", content, StringComparison.Ordinal);
        Assert.Contains("TRD204", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_XlsxExtensionOnTextService_Throws()
    {
        var trend = await BuildTrendAsync("WrongExtension");
        var path = Path.Combine(_temporaryDirectory, "trend.xlsx");

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService().ExportAsync(new TrendExportRequest(trend, path, ExportedAt)));
    }

    [Fact]
    public async Task ExportAsync_UnsupportedExtension_Throws()
    {
        var trend = await BuildTrendAsync("BadExtension");
        var path = Path.Combine(_temporaryDirectory, "trend.json");

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService().ExportAsync(new TrendExportRequest(trend, path, ExportedAt)));
    }

    [Fact]
    public async Task ExportXlsxAsync_WritesExpectedSheets()
    {
        var trend = await BuildTrendAsync("XlsxExport");
        var path = Path.Combine(_temporaryDirectory, "trend.xlsx");

        var result = await CreateXlsxService().ExportAsync(new TrendExportRequest(trend, path, ExportedAt));

        Assert.Equal(TrendExportFormat.Xlsx, result.Format);
        Assert.True(File.Exists(path));

        using var workbook = new XLWorkbook(path);
        var sheetNames = workbook.Worksheets.Select(sheet => sheet.Name).ToArray();
        Assert.Contains("Metadata", sheetNames);
        Assert.Contains("Trend Captures", sheetNames);
        Assert.Contains("Trend Series", sheetNames);
        Assert.Contains("Diagnostics", sheetNames);
        Assert.DoesNotContain("Trend Points", sheetNames);

        var metadata = workbook.Worksheet("Metadata");
        Assert.Equal("Key", metadata.Cell(1, 1).GetString());
        Assert.Equal("Project File", metadata.Cell(2, 1).GetString());

        var series = workbook.Worksheet("Trend Series");
        Assert.Equal("Group", series.Cell(1, 1).GetString());
        Assert.Equal("Metric", series.Cell(1, 2).GetString());
        Assert.Equal("Assessment", series.Cell(1, 15).GetString());

        var captures = workbook.Worksheet("Trend Captures");
        Assert.Equal("CaptureId", captures.Cell(1, 1).GetString());
        Assert.Equal("capture-a", captures.Cell(2, 1).GetString());
        Assert.Equal("capture-b", captures.Cell(3, 1).GetString());
    }

    [Fact]
    public async Task ExportXlsxAsync_IncludePoints_WritesPointsSheet()
    {
        var trend = await BuildTrendAsync("XlsxPoints");
        var path = Path.Combine(_temporaryDirectory, "points.xlsx");

        await CreateXlsxService().ExportAsync(new TrendExportRequest(trend, path, ExportedAt, IncludePoints: true));

        using var workbook = new XLWorkbook(path);
        var points = workbook.Worksheet("Trend Points");
        Assert.Equal("Group", points.Cell(1, 1).GetString());
        Assert.Equal("DeltaFromPrevious", points.Cell(1, 7).GetString());
        Assert.True(points.LastRowUsed()!.RowNumber() > 1, "The points sheet should have data rows.");
    }

    [Fact]
    public async Task ExportXlsxAsync_MissingValues_WrittenAsMissingNotZero()
    {
        var trend = await BuildTrendAsync("XlsxMissing");
        var path = Path.Combine(_temporaryDirectory, "missing.xlsx");

        await CreateXlsxService().ExportAsync(new TrendExportRequest(trend, path, ExportedAt));

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("Trend Series");
        var lastRow = sheet.LastRowUsed()!.RowNumber();
        var systemRow = Enumerable.Range(2, lastRow - 1)
            .Single(row => sheet.Cell(row, 2).GetString() == "SystemKb");

        Assert.Equal(1, sheet.Cell(systemRow, 7).GetValue<int>());
        // TotalDelta has no value because the metric is absent from the newer capture.
        Assert.Equal("missing", sheet.Cell(systemRow, 13).GetString());
    }

    [Fact]
    public async Task ExportXlsxAsync_NonXlsxExtension_Throws()
    {
        var trend = await BuildTrendAsync("XlsxWrongExtension");
        var path = Path.Combine(_temporaryDirectory, "trend.csv");

        await Assert.ThrowsAsync<ArgumentException>(() => CreateXlsxService().ExportAsync(new TrendExportRequest(trend, path, ExportedAt)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private static TrendExportService CreateService() =>
        new(() => new AppVersionInfo("1.2.3", "abc1234", ExportedAt));

    private static XlsxTrendExportService CreateXlsxService() =>
        new(() => new AppVersionInfo("1.2.3", "abc1234", ExportedAt));

    /// <summary>
    /// Two captures where the newer one omits the System line, so the trend contains both a
    /// regressed metric and a metric that is missing at one point.
    /// </summary>
    private async Task<TrendResult> BuildTrendAsync(string projectName)
    {
        var project = await CreateProjectAsync(projectName);
        TrendServiceTests.AddMemInfoCapture(project, "Nightly", "2026-08-01", "capture-a", "complete-meminfo.txt", "dev-01");
        TrendServiceTests.AddMemInfoCapture(project, "Nightly", "2026-08-09", "capture-b", "current-meminfo.txt", "dev-01");
        return await new TrendService().BuildTrendAsync(new TrendRequest(project, BaselineDiffSource.MemInfo, Tag: "Nightly"));
    }

    private async Task<UkitProject> CreateProjectAsync(string projectName)
    {
        var result = await new ProjectService().CreateProjectAsync(
            new CreateProjectRequest(Path.Combine(_temporaryDirectory, projectName), projectName));
        return result.Project;
    }
}
