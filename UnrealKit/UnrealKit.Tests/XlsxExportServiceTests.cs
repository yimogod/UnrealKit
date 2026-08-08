using ClosedXML.Excel;
using UnrealKit.Core.Export;
using UnrealKit.Core.Parsing;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Tests;

public sealed class XlsxExportServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "UnrealKit.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task ExportMemInfoXlsx_WritesExpectedSheets()
    {
        var parse = await new AndroidMemInfoParser().ParseFileAsync(MemInfoSample("complete-meminfo.txt"));
        var path = Path.Combine(_dir, "out.xlsx");
        var parsedAt = new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
        var svc = new XlsxMemInfoExportService(() => new AppVersionInfo("1.2.3", "abc", parsedAt));

        var r = await svc.ExportAsync(new MemInfoExportRequest(parse, path, parsedAt));

        Assert.Equal(MemInfoExportFormat.Xlsx, r.Format);
        Assert.True(File.Exists(path));

        using var wb = new XLWorkbook(path);
        Assert.Contains("Metadata", wb.Worksheets.Select(s => s.Name));
        Assert.Contains("AndroidMemInfo", wb.Worksheets.Select(s => s.Name));

        var meta = wb.Worksheet("Metadata");
        Assert.Equal("Key", meta.Cell(1, 1).GetString());
        Assert.Equal("Value", meta.Cell(1, 2).GetString());
        Assert.Equal("Input File", meta.Cell(2, 1).GetString());
    }

    [Fact]
    public async Task ExportMemInfoXlsx_WithDetails_WritesPssDalvikAndObjectsSheets()
    {
        var parse = await new AndroidMemInfoParser().ParseFileAsync(MemInfoSample("oem-detailed-meminfo.txt"));
        var path = Path.Combine(_dir, "details.xlsx");
        var parsedAt = new DateTimeOffset(2026, 8, 7, 1, 2, 3, TimeSpan.Zero);
        var svc = new XlsxMemInfoExportService(() => new AppVersionInfo("1.2.3", "abc", parsedAt));

        await svc.ExportAsync(new MemInfoExportRequest(parse, path, parsedAt, IncludeDetails: true, CaptureId: "Cap-001"));

        using var wb = new XLWorkbook(path);
        Assert.Contains("PSS Details", wb.Worksheets.Select(s => s.Name));
        Assert.Contains("Dalvik", wb.Worksheets.Select(s => s.Name));
        Assert.Contains("Objects", wb.Worksheets.Select(s => s.Name));

        var pss = wb.Worksheet("PSS Details");
        Assert.True(pss.LastRowUsed()!.RowNumber() > 1, "PSS Details sheet should have data rows");
    }

    [Fact]
    public async Task ExportMemInfoXlsx_WithDiagnostics_WritesDiagnosticsSheet()
    {
        var parse = await new AndroidMemInfoParser().ParseFileAsync(MemInfoSample("malformed-details-meminfo.txt"));
        var path = Path.Combine(_dir, "diag.xlsx");
        var svc = new XlsxMemInfoExportService();

        await svc.ExportAsync(new MemInfoExportRequest(parse, path, DateTimeOffset.UtcNow));

        using var wb = new XLWorkbook(path);
        Assert.Contains("Diagnostics", wb.Worksheets.Select(s => s.Name));
    }

    [Fact]
    public async Task ExportMemReportXlsx_WritesExpectedSheets()
    {
        var parse = await new UnrealMemReportParser().ParseFileAsync(MemReportSample("complete-details.memreport"));
        var path = Path.Combine(_dir, "mr.xlsx");
        var parsedAt = new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
        var svc = new XlsxMemReportExportService(() => new AppVersionInfo("1.2.3", "abc", parsedAt));

        var r = await svc.ExportAsync(new MemReportExportRequest(parse, path, parsedAt));

        Assert.Equal(MemInfoExportFormat.Xlsx, r.Format);
        using var wb = new XLWorkbook(path);
        Assert.Contains("Metadata", wb.Worksheets.Select(s => s.Name));
        Assert.Contains("MemReport Summary", wb.Worksheets.Select(s => s.Name));
    }

    [Fact]
    public async Task ExportMemReportXlsx_WithDetails_WritesTextureRenderTargetAndObjectSheets()
    {
        var parse = await new UnrealMemReportParser().ParseFileAsync(MemReportSample("complete-details.memreport"));
        var path = Path.Combine(_dir, "mr-details.xlsx");
        var parsedAt = new DateTimeOffset(2026, 8, 7, 1, 2, 3, TimeSpan.Zero);
        var svc = new XlsxMemReportExportService(() => new AppVersionInfo("1.2.3", "abc", parsedAt));

        await svc.ExportAsync(new MemReportExportRequest(parse, path, parsedAt, IncludeDetails: true, CaptureId: "Cap-001"));

        using var wb = new XLWorkbook(path);
        Assert.Contains("Textures", wb.Worksheets.Select(s => s.Name));
        Assert.Contains("Render Targets", wb.Worksheets.Select(s => s.Name));
        Assert.Contains("Objects", wb.Worksheets.Select(s => s.Name));

        var tex = wb.Worksheet("Textures");
        Assert.Contains("T_Stone", tex.Cell(2, 1).GetString());
    }

    [Fact]
    public async Task ExportMemReportXlsx_RejectsFailedParse()
    {
        var parse = new UnrealMemReportParser().Parse("bad.memreport", ["SoundBank: 1 MB"]);
        var svc = new XlsxMemReportExportService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ExportAsync(new MemReportExportRequest(parse, Path.Combine(_dir, "bad.xlsx"), DateTimeOffset.UtcNow)));
    }

    private static string MemInfoSample(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "MemInfo", name);

    private static string MemReportSample(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "MemReport", name);
}
