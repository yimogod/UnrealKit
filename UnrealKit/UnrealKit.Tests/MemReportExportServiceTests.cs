using UnrealKit.Core.Export;
using UnrealKit.Core.Parsing;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Tests;

public sealed class MemReportExportServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "UnrealKit.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task ExportAsync_WritesCsvSummary()
    {
        var parse = await new UnrealMemReportParser().ParseFileAsync(Sample("complete-details.memreport"));
        var path = Path.Combine(_dir, "out.csv");
        var parsedAt = new DateTimeOffset(2026, 8, 6, 12, 34, 56, TimeSpan.Zero);
        var svc = new MemReportExportService(() => new AppVersionInfo("1.2.3", "abc", parsedAt));

        var r = await svc.ExportAsync(new MemReportExportRequest(parse, path, parsedAt));

        Assert.Equal(MemInfoExportFormat.Csv, r.Format);
        var lines = await File.ReadAllLinesAsync(path);
        Assert.StartsWith("SourceFile,ParsedAtUtc,ToolVersion,ToolGitCommit,Changelist,MetricGroup,MetricName,ValueKb,RawValue,Status", lines[0]);
        Assert.Contains(lines, line => line.Contains(",Wwise,SoundBank,,,Missing"));
    }

    [Fact]
    public async Task ExportAsync_UsesTabForTsv()
    {
        var parse = await new UnrealMemReportParser().ParseFileAsync(Sample("complete-details.memreport"));
        var path = Path.Combine(_dir, "out.tsv");
        var svc = new MemReportExportService();

        await svc.ExportAsync(new MemReportExportRequest(parse, path, DateTimeOffset.UtcNow));

        var header = (await File.ReadAllLinesAsync(path))[0];
        Assert.Contains('\t', header);
        Assert.DoesNotContain(',', header);
    }

    [Fact]
    public async Task ExportAsync_WithDetails_WritesTextureRenderTargetAndObjectRows()
    {
        var parse = await new UnrealMemReportParser().ParseFileAsync(Sample("complete-details.memreport"));
        var path = Path.Combine(_dir, "details.csv");
        var parsedAt = new DateTimeOffset(2026, 8, 7, 1, 2, 3, TimeSpan.Zero);
        var service = new MemReportExportService(() => new AppVersionInfo("1.2.3", "abc", parsedAt));

        await service.ExportAsync(new MemReportExportRequest(parse, path, parsedAt, IncludeDetails: true, CaptureId: "Cap-001"));

        var lines = await File.ReadAllLinesAsync(path);
        var textureLine = Assert.Single(lines, line => line.StartsWith("Cap-001,") && line.Contains("Texture2D /Game/Textures/T_Stone,"));
        Assert.Contains(",2048", textureLine);
        Assert.Contains(",1024", textureLine);
        Assert.Contains(",PF_DXT1", textureLine);
        Assert.Contains(",1536", textureLine);

        var rtLine = Assert.Single(lines, line => line.StartsWith("Cap-001,") && line.Contains("TextureRenderTarget2D /Game/UI/RT_Minimap,"));
        Assert.Contains(",4096", rtLine);

        var objLine = Assert.Single(lines, line => line.StartsWith("Cap-001,") && line.Contains(",Texture2D,"));
        Assert.Contains(",42", objLine);
        Assert.Contains(",8192", objLine);
    }

    [Fact]
    public async Task ExportAsync_RejectsXlsxExtension()
    {
        var parse = await new UnrealMemReportParser().ParseFileAsync(Sample("complete-details.memreport"));
        var svc = new MemReportExportService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.ExportAsync(new MemReportExportRequest(parse, Path.Combine(_dir, "out.xlsx"), DateTimeOffset.UtcNow)));
    }

    [Fact]
    public async Task ExportAsync_RejectsNullInput()
    {
        var svc = new MemReportExportService();
        await Assert.ThrowsAsync<ArgumentNullException>(() => svc.ExportAsync(null!));
    }

    private static string Sample(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "MemReport", name);
}
