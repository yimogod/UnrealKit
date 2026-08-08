using UnrealKit.Core.Export;
using UnrealKit.Core.Parsing;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Tests;

public sealed class MemInfoExportServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "UnrealKit.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task ExportAsync_WritesCsvWithAllColumns()
    {
        var parse = await new AndroidMemInfoParser().ParseFileAsync(Sample("complete-meminfo.txt"));
        var path = Path.Combine(_dir, "out.csv");
        var parsedAt = new DateTimeOffset(2026, 8, 6, 12, 34, 56, TimeSpan.Zero);
        var svc = new MemInfoExportService(() => new AppVersionInfo("1.2.3", "abc", parsedAt));

        var r = await svc.ExportAsync(new MemInfoExportRequest(parse, path, parsedAt));

        Assert.Equal(MemInfoExportFormat.Csv, r.Format);
        var lines = await File.ReadAllLinesAsync(path);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("SourceFile,ParsedAtUtc,ToolVersion,ToolGitCommit,ProcessName,ProcessId", lines[0]);
        Assert.Contains("2026-08-06T12:34:56", lines[1]);
        Assert.Contains("com.example.performance,4312", lines[1]);
        Assert.Contains("30680", lines[1]);
    }

    [Fact]
    public async Task ExportAsync_UsesTabForTsv()
    {
        var parse = await new AndroidMemInfoParser().ParseFileAsync(Sample("complete-meminfo.txt"));
        var path = Path.Combine(_dir, "out.tsv");
        var svc = new MemInfoExportService();

        await svc.ExportAsync(new MemInfoExportRequest(parse, path, DateTimeOffset.UtcNow));

        var header = (await File.ReadAllLinesAsync(path))[0];
        Assert.Contains('\t', header);
        Assert.DoesNotContain(',', header);
    }

    [Fact]
    public async Task ExportAsync_WithDetails_WritesLongFormCsvGoldenRows()
    {
        var parse = await new AndroidMemInfoParser().ParseFileAsync(Sample("oem-detailed-meminfo.txt"));
        var path = Path.Combine(_dir, "details.csv");
        var parsedAt = new DateTimeOffset(2026, 8, 7, 1, 2, 3, TimeSpan.Zero);
        var service = new MemInfoExportService(() => new AppVersionInfo("1.2.3", "abc", parsedAt));

        await service.ExportAsync(new MemInfoExportRequest(parse, path, parsedAt, IncludeDetails: true, CaptureId: "Capture-001"));

        var lines = await File.ReadAllLinesAsync(path);
        Assert.Equal("CaptureId,InputFile,ParsedAtUtc,ToolVersion,ToolGitCommit,ProcessName,ProcessId,Section,Name,Metric,Value,LineNumber", lines[0]);
        Assert.Contains(lines, line => line.Contains(",AppSummary,AppSummary,TotalPssKb,19320,"));
        Assert.Contains(lines, line => line.Contains(",DetailedPss,Native Heap,HeapAllocKb,53000,7"));
        Assert.Contains(lines, line => line.Contains(",Dalvik,LinearAlloc,PssKb,512,12"));
        Assert.Contains(lines, line => line.Contains(",Objects,Views,Count,42,16"));
        Assert.All(lines.Skip(1), line => Assert.StartsWith("Capture-001,", line));
    }

    [Fact]
    public async Task ExportAsync_WithDetails_WritesTsvAndPreservesDuplicatesAndDiagnostics()
    {
        var parse = await new AndroidMemInfoParser().ParseFileAsync(Sample("duplicate-sections-meminfo.txt"));
        var path = Path.Combine(_dir, "details.tsv");
        var service = new MemInfoExportService(() => new AppVersionInfo("1.2.3", "abc", DateTimeOffset.UnixEpoch));

        await service.ExportAsync(new MemInfoExportRequest(parse, path, DateTimeOffset.UnixEpoch, IncludeDetails: true));

        var lines = await File.ReadAllLinesAsync(path);
        Assert.Contains('\t', lines[0]);
        Assert.Equal(2, lines.Count(line => line.Contains("\tDalvik\tLinearAlloc\tPssKb\t")));
        Assert.Contains(lines, line => line.Contains("\tDiagnostics\tAMI210\tWarning\t") && line.EndsWith("\t12"));
        Assert.Contains(lines, line => line.Contains("\tDiagnostics\tAMI222\tWarning\t") && line.EndsWith("\t16"));
    }

    [Fact]
    public async Task ExportAsync_WithDetails_PreservesTruncationDiagnostics()
    {
        var parse = await new AndroidMemInfoParser().ParseFileAsync(Sample("truncated-sections-meminfo.txt"));
        var path = Path.Combine(_dir, "truncated.csv");

        await new MemInfoExportService().ExportAsync(new MemInfoExportRequest(parse, path, DateTimeOffset.UtcNow, IncludeDetails: true));

        var lines = await File.ReadAllLinesAsync(path);
        Assert.Contains(lines, line => line.Contains(",Diagnostics,AMI213,Warning,") && line.EndsWith(",5"));
        Assert.Contains(lines, line => line.Contains(",Diagnostics,AMI223,Warning,") && line.EndsWith(",6"));
    }

    [Fact]
    public async Task ExportAsync_RejectsXlsxExtension()
    {
        var parse = await new AndroidMemInfoParser().ParseFileAsync(Sample("complete-meminfo.txt"));
        var svc = new MemInfoExportService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.ExportAsync(new MemInfoExportRequest(parse, Path.Combine(_dir, "out.xlsx"), DateTimeOffset.UtcNow)));
    }

    private static string Sample(string name) =>
        Path.Combine(ApplicationPaths.AppDir, "TestData", "MemInfo", name);
}
