using UnrealKit.Core.Export;
using UnrealKit.Core.Parsing;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Tests;

public sealed class EndToEndExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "UnrealKit.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task ParseMemInfo_ExportAllFormats_ProducesValidFiles()
    {
        var parse = await new AndroidMemInfoParser().ParseFileAsync(MemInfoSample("complete-meminfo.txt"));
        var parsedAt = DateTimeOffset.UtcNow;

        var csvOut = Path.Combine(_dir, "e2e.csv");
        var tsvOut = Path.Combine(_dir, "e2e.tsv");

        var csvSvc = new MemInfoExportService(() => new AppVersionInfo("1.0.0", "test", parsedAt));
        await csvSvc.ExportAsync(new MemInfoExportRequest(parse, csvOut, parsedAt));
        Assert.True(File.Exists(csvOut));
        var csvLines = await File.ReadAllLinesAsync(csvOut);
        Assert.Equal(2, csvLines.Length);

        var tsvSvc = new MemInfoExportService();
        await tsvSvc.ExportAsync(new MemInfoExportRequest(parse, tsvOut, parsedAt));
        Assert.True(File.Exists(tsvOut));

        var xlsxOut = Path.Combine(_dir, "e2e.xlsx");
        var xlsxSvc = new XlsxMemInfoExportService(() => new AppVersionInfo("1.0.0", "test", parsedAt));
        await xlsxSvc.ExportAsync(new MemInfoExportRequest(parse, xlsxOut, parsedAt, IncludeDetails: true));
        Assert.True(File.Exists(xlsxOut));
    }

    [Fact]
    public async Task ParseMemReport_ExportAllFormats_ProducesValidFiles()
    {
        var parse = await new UnrealMemReportParser().ParseFileAsync(MemReportSample("complete-details.memreport"));
        var parsedAt = DateTimeOffset.UtcNow;
        var versionProvider = () => new AppVersionInfo("1.0.0", "test", parsedAt);

        var csvOut = Path.Combine(_dir, "mr-e2e.csv");
        var tsvOut = Path.Combine(_dir, "mr-e2e.tsv");
        var csvSvc = new MemReportExportService(versionProvider);

        await csvSvc.ExportAsync(new MemReportExportRequest(parse, csvOut, parsedAt));
        Assert.True(File.Exists(csvOut));

        var tsvSvc = new MemReportExportService();
        await tsvSvc.ExportAsync(new MemReportExportRequest(parse, tsvOut, parsedAt));
        Assert.True(File.Exists(tsvOut));

        var xlsxOut = Path.Combine(_dir, "mr-e2e.xlsx");
        var xlsxSvc = new XlsxMemReportExportService(versionProvider);
        await xlsxSvc.ExportAsync(new MemReportExportRequest(parse, xlsxOut, parsedAt, IncludeDetails: true));
        Assert.True(File.Exists(xlsxOut));
    }

    [Fact]
    public async Task ParseMemInfo_OemDetailed_ExportAllFormats_WithDetails_ProducesValidFiles()
    {
        var parse = await new AndroidMemInfoParser().ParseFileAsync(MemInfoSample("oem-detailed-meminfo.txt"));
        var parsedAt = DateTimeOffset.UtcNow;
        var provider = () => new AppVersionInfo("1.0.0", "test", parsedAt);

        var csv = Path.Combine(_dir, "oem-details.csv");
        await new MemInfoExportService(provider).ExportAsync(
            new MemInfoExportRequest(parse, csv, parsedAt, IncludeDetails: true));
        var csvLines = await File.ReadAllLinesAsync(csv);
        Assert.True(csvLines.Length > 5, "Detailed CSV should have many rows");

        var xlsx = Path.Combine(_dir, "oem-details.xlsx");
        await new XlsxMemInfoExportService(provider).ExportAsync(
            new MemInfoExportRequest(parse, xlsx, parsedAt, IncludeDetails: true));
        Assert.True(File.Exists(xlsx));
    }

    [Fact]
    public async Task ParseMemInfo_Truncated_ExportAllFormats_PreservesDiagnostics()
    {
        var parse = await new AndroidMemInfoParser().ParseFileAsync(MemInfoSample("truncated-sections-meminfo.txt"));
        var parsedAt = DateTimeOffset.UtcNow;

        var csv = Path.Combine(_dir, "trunc.csv");
        await new MemInfoExportService().ExportAsync(
            new MemInfoExportRequest(parse, csv, parsedAt, IncludeDetails: true));
        var csvLines = await File.ReadAllLinesAsync(csv);
        Assert.Contains(csvLines, line => line.Contains("AMI213"));

        var xlsx = Path.Combine(_dir, "trunc.xlsx");
        await new XlsxMemInfoExportService().ExportAsync(
            new MemInfoExportRequest(parse, xlsx, parsedAt, IncludeDetails: true));
        Assert.True(File.Exists(xlsx));
    }

    private static string MemInfoSample(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "MemInfo", name);

    private static string MemReportSample(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "MemReport", name);
}
