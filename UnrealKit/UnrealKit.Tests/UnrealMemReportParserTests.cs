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
    public void Parse_ParsesTextureRenderTargetAndObjectDetails()
    {
        string[] lines =
        [
            "Changelist: 123456",
            "",
            "Listing all textures.",
            "Name | Dimensions | Format | Memory",
            "Texture2D /Game/Textures/T_Stone | 2048x1024 | PF_DXT1 | 1.5 MB",
            "",
            "Render target memory:",
            "Name | Dimensions | Format | Memory",
            "TextureRenderTarget2D /Game/UI/RT_Minimap | 1024x1024 | PF_B8G8R8A8 | 4 MB",
            "",
            "Obj List:",
            "Class=Texture2D, Count=42, NumKBytes=8192",
        ];

        var result = new UnrealMemReportParser().Parse("sample.memreport", lines);

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

    [Fact]
    public async Task ParseFileAsync_ParsesListTexturesBlock()
    {
        var inputPath = Path.Combine(AppContext.BaseDirectory, "TestData", "MemReport", "complete-details.memreport");

        var result = await new UnrealMemReportParser().ParseFileAsync(inputPath);

        Assert.True(result.IsSuccess);
        Assert.True(result.Report!.TextureDetails.Count > 0, "TextureDetails should not be empty");
        var first = result.Report.TextureDetails[0];
        Assert.Equal("1024", first.CookedWidth);
        Assert.Equal("1024", first.CookedHeight);
        Assert.Equal("8224", first.CookedSizeKb);
        Assert.Equal("?", first.CookedBias);
        Assert.Equal("PF_FloatRGBA", first.Format);
        Assert.Equal("TEXTUREGROUP_16BitData", first.LodGroup);
        Assert.True(result.Report.TextureStats.Count > 0, "TextureStats should not be empty");
        Assert.Contains(result.Report.TextureStats, s => s.Label.Contains("PF_FloatRGBA"));
    }
}
