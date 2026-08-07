using UnrealKit.Core.Parsing;

namespace UnrealKit.Tests;

public sealed class AndroidMemInfoParserTests
{
    [Fact]
    public async Task ParseFileAsync_ParsesCompleteMemInfoGoldenSample()
    {
        var result = await new AndroidMemInfoParser().ParseFileAsync(GetSamplePath("complete-meminfo.txt"));

        Assert.True(result.IsSuccess);
        var report = Assert.IsType<AndroidMemInfoReport>(result.Report);
        Assert.Equal("com.example.performance", report.ProcessName);
        Assert.Equal(4312, report.ProcessId);
        Assert.Equal(8000, report.Summary.JavaHeapKb);
        Assert.Equal(12000, report.Summary.NativeHeapKb);
        Assert.Equal(30680, report.Summary.TotalPssKb);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task ParseFileAsync_ReportsMalformedValueAndMissingTotalWithLineNumbers()
    {
        var result = await new AndroidMemInfoParser().ParseFileAsync(GetSamplePath("missing-total-meminfo.txt"));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Report);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "AMI107" && diagnostic.LineNumber == 8);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "AMI105" && diagnostic.LineNumber == 5);
    }

    [Fact]
    public async Task ParseFileAsync_RejectsDirectoryInput()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => new AndroidMemInfoParser().ParseFileAsync(Path.GetDirectoryName(GetSamplePath("complete-meminfo.txt"))!));

        Assert.Contains("must be a file", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParseFileAsync_ParsesDetailedPssDalvikAndObjectsFromOemVariant()
    {
        var result = await new AndroidMemInfoParser().ParseFileAsync(GetSamplePath("oem-detailed-meminfo.txt"));

        Assert.True(result.IsSuccess);
        var report = Assert.IsType<AndroidMemInfoReport>(result.Report);
        var nativeHeap = Assert.Single(report.DetailedPssEntries, entry => entry.Name == "Native Heap");
        Assert.Equal(12345, nativeHeap.TotalPssKb);
        Assert.Equal(64000, nativeHeap.HeapSizeKb);
        Assert.Equal(2, report.DalvikEntries.Count);
        Assert.Contains(report.DalvikEntries, entry => entry.Name == "LinearAlloc" && entry.PssKb == 512);
        Assert.Contains(report.ObjectEntries, entry => entry.Name == "Views" && entry.Count == 42);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task ParseFileAsync_ReportsMalformedOptionalSectionsWithoutRejectingAppSummary()
    {
        var result = await new AndroidMemInfoParser().ParseFileAsync(GetSamplePath("malformed-details-meminfo.txt"));

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "AMI202" && diagnostic.LineNumber == 8);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "AMI211" && diagnostic.LineNumber == 12);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "AMI221" && diagnostic.LineNumber == 16);
    }

    private static string GetSamplePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", "MemInfo", fileName);
}
