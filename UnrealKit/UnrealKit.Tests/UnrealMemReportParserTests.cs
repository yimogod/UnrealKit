using UnrealKit.Core.Parsing;

namespace UnrealKit.Tests;

public sealed class UnrealMemReportParserTests
{
    [Fact]
    public void Parse_ParsesMetric()
    {
        var result = new UnrealMemReportParser().Parse("sample.memreport", ["Changelist: 1", "SoundBank: 1 MB"]);

        Assert.True(result.IsSuccess);
        Assert.Equal(1024, Assert.Single(result.Report!.Summary.Metrics, metric => metric.Name == "SoundBank").ValueKb);
    }

    [Fact]
    public void Parse_ReportsMissingChangelist()
    {
        var result = new UnrealMemReportParser().Parse("invalid.memreport", ["SoundBank: 1 MB"]);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "UMR101");
    }

    [Fact]
    public async Task ParseFileAsync_ParsesTextureRenderTargetAndObjectDetails()
    {
        var inputPath = Path.Combine(AppContext.BaseDirectory, "TestData", "MemReport", "complete-details.memreport");

        var result = await new UnrealMemReportParser().ParseFileAsync(inputPath);

        Assert.True(result.IsSuccess);
        var texture = Assert.Single(result.Report!.Textures);
        Assert.Equal("Texture2D /Game/Textures/T_Stone", texture.Name);
        Assert.Equal(2048, texture.Width);
        Assert.Equal(1024, texture.Height);
        Assert.Equal("PF_DXT1", texture.Format);
        Assert.Equal(1536, texture.MemoryKb);
        var renderTarget = Assert.Single(result.Report.RenderTargets);
        Assert.Equal("TextureRenderTarget2D /Game/UI/RT_Minimap", renderTarget.Name);
        Assert.Equal(4096, renderTarget.MemoryKb);
        var memoryObject = Assert.Single(result.Report.Objects);
        Assert.Equal("Texture2D", memoryObject.ClassName);
        Assert.Equal(42, memoryObject.Count);
        Assert.Equal(8192, memoryObject.MemoryKb);
    }

    [Fact]
    public void Parse_ReportsMalformedAndMissingDetailSections()
    {
        var result = new UnrealMemReportParser().Parse("invalid-details.memreport", ["Changelist: 1", "Listing all textures.", "Texture2D /Game/Bad"]);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "UMR304" && diagnostic.LineNumber == 3);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "UMR302");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "UMR303");
    }
}
