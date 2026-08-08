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
}
