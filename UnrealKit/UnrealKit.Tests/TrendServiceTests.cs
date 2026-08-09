using UnrealKit.Core.Analysis;
using UnrealKit.Core.Diagnostics;
using UnrealKit.Core.Projects;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Tests;

public sealed class TrendServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "UnrealKit.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BuildTrendAsync_OrdersCapturesOldestToNewest()
    {
        var project = await CreateProjectAsync("TrendOrder");
        AddMemInfoCapture(project, "Nightly", "2026-08-09", "capture-c", "current-meminfo.txt", "dev-01");
        AddMemInfoCapture(project, "Nightly", "2026-08-01", "capture-a", "complete-meminfo.txt", "dev-01");
        AddMemInfoCapture(project, "Nightly", "2026-08-05", "capture-b", "current-meminfo.txt", "dev-01");

        var result = await new TrendService().BuildTrendAsync(new TrendRequest(project, BaselineDiffSource.MemInfo));

        Assert.True(result.IsSuccess);
        Assert.Equal(["capture-a", "capture-b", "capture-c"], result.Captures.Select(capture => capture.CaptureId));

        var total = FindSeries(result, "AppSummary", "TotalPssKb");
        Assert.Equal(["capture-a", "capture-b", "capture-c"], total.Points.Select(point => point.CaptureId));
    }

    [Fact]
    public async Task BuildTrendAsync_ComputesPerPointDeltaAndOverallTotals()
    {
        var project = await CreateProjectAsync("TrendDelta");
        AddMemInfoCapture(project, "Nightly", "2026-08-01", "capture-a", "complete-meminfo.txt", "dev-01");
        AddMemInfoCapture(project, "Nightly", "2026-08-09", "capture-b", "current-meminfo.txt", "dev-01");

        var result = await new TrendService().BuildTrendAsync(new TrendRequest(project, BaselineDiffSource.MemInfo));

        var total = FindSeries(result, "AppSummary", "TotalPssKb");
        Assert.Equal(2, total.PointCount);
        Assert.Equal(2, total.PresentCount);
        Assert.Equal(0, total.MissingCount);
        Assert.Null(total.Points[0].DeltaFromPrevious);
        Assert.Equal(3024, total.Points[1].DeltaFromPrevious);
        Assert.Equal(30680, total.First);
        Assert.Equal(33704, total.Last);
        Assert.Equal(30680, total.Minimum);
        Assert.Equal(33704, total.Maximum);
        Assert.Equal(3024, total.TotalDelta);
        Assert.Equal(9.856584, total.TotalDeltaPercent!.Value, 4);
        Assert.Equal(MetricDiffAssessment.Regressed, total.OverallAssessment);

        // Memory dropping across the range is an improvement, not a bare negative delta.
        var javaHeap = FindSeries(result, "AppSummary", "JavaHeapKb");
        Assert.Equal(-1000, javaHeap.TotalDelta);
        Assert.Equal(MetricDiffAssessment.Improved, javaHeap.OverallAssessment);
    }

    [Fact]
    public async Task BuildTrendAsync_MetricMissingInSomeCaptures_ReportsMissingNotZero()
    {
        var project = await CreateProjectAsync("TrendMissing");
        AddMemInfoCapture(project, "Nightly", "2026-08-01", "capture-a", "complete-meminfo.txt", "dev-01");
        AddMemInfoCapture(project, "Nightly", "2026-08-09", "capture-b", "current-meminfo.txt", "dev-01");

        var result = await new TrendService().BuildTrendAsync(new TrendRequest(project, BaselineDiffSource.MemInfo));

        // The current sample omits the System line, so the later point has no value at all.
        var system = FindSeries(result, "AppSummary", "SystemKb");
        Assert.Equal(2, system.PointCount);
        Assert.Equal(1, system.PresentCount);
        Assert.Equal(1, system.MissingCount);
        Assert.Equal(1024, system.Points[0].Value);
        Assert.Null(system.Points[1].Value);
        Assert.Null(system.Points[1].DeltaFromPrevious);
        Assert.Null(system.TotalDelta);
        Assert.Equal(MetricDiffAssessment.Unknown, system.OverallAssessment);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TRD204" && diagnostic.Severity == DiagnosticSeverity.Warning);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task BuildTrendAsync_DeltaSkipsGapAndStepsFromLastPresentValue()
    {
        var project = await CreateProjectAsync("TrendGap");
        AddMemInfoCapture(project, "Nightly", "2026-08-01", "capture-a", "complete-meminfo.txt", "dev-01");
        AddMemInfoCapture(project, "Nightly", "2026-08-05", "capture-b", "current-meminfo.txt", "dev-01");
        AddMemInfoCapture(project, "Nightly", "2026-08-09", "capture-c", "complete-meminfo.txt", "dev-01");

        var result = await new TrendService().BuildTrendAsync(new TrendRequest(project, BaselineDiffSource.MemInfo));

        // SystemKb exists in captures a and c but not b. The delta at c must step from a's value,
        // not treat the gap at b as a drop to zero.
        var system = FindSeries(result, "AppSummary", "SystemKb");
        Assert.Equal(1024, system.Points[0].Value);
        Assert.Null(system.Points[1].Value);
        Assert.Equal(1024, system.Points[2].Value);
        Assert.Equal(0, system.Points[2].DeltaFromPrevious);
        Assert.Equal(MetricDiffAssessment.Unchanged, system.Points[2].Assessment);
    }

    [Fact]
    public async Task BuildTrendAsync_FiltersByTag()
    {
        var project = await CreateProjectAsync("TrendTag");
        AddMemInfoCapture(project, "Nightly", "2026-08-01", "nightly-a", "complete-meminfo.txt", "dev-01");
        AddMemInfoCapture(project, "Release", "2026-08-02", "release-a", "current-meminfo.txt", "dev-01");

        var result = await new TrendService().BuildTrendAsync(new TrendRequest(project, BaselineDiffSource.MemInfo, Tag: "Nightly"));

        Assert.Single(result.Captures);
        Assert.Equal("nightly-a", result.Captures[0].CaptureId);
    }

    [Fact]
    public async Task BuildTrendAsync_FiltersByDateRangeInclusively()
    {
        var project = await CreateProjectAsync("TrendRange");
        AddMemInfoCapture(project, "Nightly", "2026-08-01", "capture-a", "complete-meminfo.txt", "dev-01");
        AddMemInfoCapture(project, "Nightly", "2026-08-05", "capture-b", "current-meminfo.txt", "dev-01");
        AddMemInfoCapture(project, "Nightly", "2026-08-09", "capture-c", "complete-meminfo.txt", "dev-01");

        var result = await new TrendService().BuildTrendAsync(new TrendRequest(
            project,
            BaselineDiffSource.MemInfo,
            From: new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero),
            To: new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal(["capture-b", "capture-c"], result.Captures.Select(capture => capture.CaptureId));
    }

    [Fact]
    public async Task BuildTrendAsync_FiltersByDeviceSerialFromManifest()
    {
        var project = await CreateProjectAsync("TrendDevice");
        AddMemInfoCapture(project, "Nightly", "2026-08-01", "capture-a", "complete-meminfo.txt", "dev-01");
        AddMemInfoCapture(project, "Nightly", "2026-08-05", "capture-b", "current-meminfo.txt", "dev-02");

        var result = await new TrendService().BuildTrendAsync(new TrendRequest(
            project, BaselineDiffSource.MemInfo, DeviceSerialNumber: "dev-01"));

        Assert.Single(result.Captures);
        Assert.Equal("capture-a", result.Captures[0].CaptureId);
        Assert.Equal("dev-01", result.Captures[0].DeviceSerialNumber);
    }

    [Fact]
    public async Task BuildTrendAsync_CaptureWithoutManifest_ExcludedFromDeviceFilterWithWarning()
    {
        var project = await CreateProjectAsync("TrendNoManifest");
        AddMemInfoCapture(project, "Nightly", "2026-08-01", "capture-a", "complete-meminfo.txt", deviceSerial: null);

        var result = await new TrendService().BuildTrendAsync(new TrendRequest(
            project, BaselineDiffSource.MemInfo, DeviceSerialNumber: "dev-01"));

        Assert.Empty(result.Captures);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TRD104");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TRD203");
    }

    [Fact]
    public async Task BuildTrendAsync_AmbiguousInputFile_ExcludesCaptureRatherThanPickingOne()
    {
        var project = await CreateProjectAsync("TrendAmbiguous");
        var captureDir = AddMemInfoCapture(project, "Nightly", "2026-08-01", "capture-a", "complete-meminfo.txt", "dev-01");
        File.Copy(BaselineSample("current-meminfo.txt"), Path.Combine(captureDir, "MemInfo", "meminfo_002.txt"));

        var result = await new TrendService().BuildTrendAsync(new TrendRequest(project, BaselineDiffSource.MemInfo));

        Assert.Empty(result.Captures);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "TRD103" &&
            diagnostic.Message.Contains("2 MemInfo files", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildTrendAsync_ExplicitFileName_ResolvesAmbiguity()
    {
        var project = await CreateProjectAsync("TrendExplicitFile");
        var captureDir = AddMemInfoCapture(project, "Nightly", "2026-08-01", "capture-a", "complete-meminfo.txt", "dev-01");
        File.Copy(BaselineSample("current-meminfo.txt"), Path.Combine(captureDir, "MemInfo", "meminfo_002.txt"));

        var result = await new TrendService().BuildTrendAsync(new TrendRequest(
            project, BaselineDiffSource.MemInfo, FileName: "meminfo_001.txt"));

        Assert.Single(result.Captures);
        Assert.EndsWith("meminfo_001.txt", result.Captures[0].InputPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildTrendAsync_RequestedFileMissingInCapture_ExcludesWithWarning()
    {
        var project = await CreateProjectAsync("TrendMissingFile");
        AddMemInfoCapture(project, "Nightly", "2026-08-01", "capture-a", "complete-meminfo.txt", "dev-01");

        var result = await new TrendService().BuildTrendAsync(new TrendRequest(
            project, BaselineDiffSource.MemInfo, FileName: "no-such-file.txt"));

        Assert.Empty(result.Captures);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TRD101");
    }

    [Fact]
    public async Task BuildTrendAsync_UnparsableCapture_ExcludedWithWarningNotFailure()
    {
        var project = await CreateProjectAsync("TrendBadCapture");
        AddMemInfoCapture(project, "Nightly", "2026-08-01", "capture-a", "complete-meminfo.txt", "dev-01");
        var badDir = Path.Combine(project.ContentDir, "Android", "Nightly", "2026-08-05", "capture-bad", "MemInfo");
        Directory.CreateDirectory(badDir);
        await File.WriteAllTextAsync(Path.Combine(badDir, "meminfo_001.txt"), "not a valid meminfo file");
        WriteManifest(Path.Combine(badDir, "..", "CaptureManifest.json"), "capture-bad", "Android", "Nightly", "2026-08-05", "dev-01");

        var result = await new TrendService().BuildTrendAsync(new TrendRequest(project, BaselineDiffSource.MemInfo));

        Assert.Single(result.Captures);
        Assert.Equal("capture-a", result.Captures[0].CaptureId);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TRD202");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "AMI101" && diagnostic.Message.StartsWith("[capture-bad]", StringComparison.Ordinal));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task BuildTrendAsync_MetricFilter_RestrictsSeriesToRequestedNames()
    {
        var project = await CreateProjectAsync("TrendFilter");
        AddMemInfoCapture(project, "Nightly", "2026-08-01", "capture-a", "complete-meminfo.txt", "dev-01");
        AddMemInfoCapture(project, "Nightly", "2026-08-09", "capture-b", "current-meminfo.txt", "dev-01");

        var result = await new TrendService().BuildTrendAsync(new TrendRequest(
            project, BaselineDiffSource.MemInfo, MetricFilter: ["TotalPssKb", "AppSummary/GraphicsKb"]));

        Assert.Equal(2, result.Series.Count);
        Assert.Contains(result.Series, series => series.Name == "TotalPssKb");
        Assert.Contains(result.Series, series => series.Name == "GraphicsKb");
    }

    [Fact]
    public async Task BuildTrendAsync_UnknownMetricInFilter_WarnsWithoutFailing()
    {
        var project = await CreateProjectAsync("TrendUnknownMetric");
        AddMemInfoCapture(project, "Nightly", "2026-08-01", "capture-a", "complete-meminfo.txt", "dev-01");

        var result = await new TrendService().BuildTrendAsync(new TrendRequest(
            project, BaselineDiffSource.MemInfo, MetricFilter: ["TotalPssKb", "NoSuchMetric"]));

        Assert.Single(result.Series);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "TRD201" &&
            diagnostic.Message.Contains("NoSuchMetric", StringComparison.Ordinal));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task BuildTrendAsync_NoMatchingCaptures_WarnsAndReturnsNoSeries()
    {
        var project = await CreateProjectAsync("TrendEmpty");

        var result = await new TrendService().BuildTrendAsync(new TrendRequest(project, BaselineDiffSource.MemInfo));

        Assert.Empty(result.Captures);
        Assert.Empty(result.Series);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TRD203");
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task BuildTrendAsync_SingleCapture_ProducesSeriesWithoutOverallDelta()
    {
        var project = await CreateProjectAsync("TrendSingle");
        AddMemInfoCapture(project, "Nightly", "2026-08-01", "capture-a", "complete-meminfo.txt", "dev-01");

        var result = await new TrendService().BuildTrendAsync(new TrendRequest(project, BaselineDiffSource.MemInfo));

        var total = FindSeries(result, "AppSummary", "TotalPssKb");
        Assert.Equal(1, total.PointCount);
        Assert.Equal(30680, total.First);
        Assert.Equal(30680, total.Last);
        Assert.Null(total.TotalDelta);
        Assert.Null(total.TotalDeltaPercent);
        Assert.Equal(MetricDiffAssessment.Unknown, total.OverallAssessment);
    }

    [Fact]
    public async Task BuildTrendAsync_InvertedDateRange_Throws()
    {
        var project = await CreateProjectAsync("TrendInverted");

        await Assert.ThrowsAsync<ArgumentException>(() => new TrendService().BuildTrendAsync(new TrendRequest(
            project,
            BaselineDiffSource.MemInfo,
            From: new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero),
            To: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero))));
    }

    [Fact]
    public async Task BuildTrendAsync_StaticCameraSource_TracksPerCameraMetrics()
    {
        var project = await CreateProjectAsync("TrendStaticCamera");
        AddSavedCapture(project, "Nightly", "2026-08-01", "capture-a", StaticCameraSample("complete-static-camera.log"), "perf.log", "dev-01");
        AddSavedCapture(project, "Nightly", "2026-08-09", "capture-b", BaselineSample("current-static-camera.log"), "perf.log", "dev-01");

        var result = await new TrendService().BuildTrendAsync(new TrendRequest(
            project, BaselineDiffSource.StaticCamera, MetricFilter: ["FrameTimeMs"]));

        Assert.Equal(2, result.Captures.Count);

        var mid = FindSeries(result, "Camera:Camera_Mid_1", "FrameTimeMs");
        Assert.Equal(22.10, mid.First!.Value, 4);
        Assert.Equal(19.40, mid.Last!.Value, 4);
        Assert.Equal(MetricDiffAssessment.Improved, mid.OverallAssessment);

        // Camera_High_2 was renamed, so each name is present in only one capture.
        Assert.Equal(1, FindSeries(result, "Camera:Camera_High_2", "FrameTimeMs").MissingCount);
        Assert.Equal(1, FindSeries(result, "Camera:Camera_New_2", "FrameTimeMs").MissingCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    internal static TrendSeries FindSeries(TrendResult result, string group, string name)
    {
        var series = result.Series.SingleOrDefault(candidate =>
            string.Equals(candidate.Group, group, StringComparison.Ordinal) &&
            string.Equals(candidate.Name, name, StringComparison.Ordinal));
        Assert.NotNull(series);
        return series!;
    }

    internal async Task<UkitProject> CreateProjectAsync(string projectName)
    {
        var result = await new ProjectService().CreateProjectAsync(
            new CreateProjectRequest(Path.Combine(_temporaryDirectory, projectName), projectName));
        return result.Project;
    }

    internal static string AddMemInfoCapture(
        UkitProject project,
        string tag,
        string date,
        string captureId,
        string sampleFileName,
        string? deviceSerial)
    {
        var captureDir = Path.Combine(project.ContentDir, "Android", tag, date, captureId);
        Directory.CreateDirectory(Path.Combine(captureDir, "MemInfo"));
        var source = sampleFileName.StartsWith("current-", StringComparison.Ordinal)
            ? BaselineSample(sampleFileName)
            : MemInfoSample(sampleFileName);
        File.Copy(source, Path.Combine(captureDir, "MemInfo", "meminfo_001.txt"));
        if (deviceSerial is not null)
        {
            WriteManifest(Path.Combine(captureDir, "CaptureManifest.json"), captureId, "Android", tag, date, deviceSerial);
        }

        return captureDir;
    }

    internal static string AddSavedCapture(
        UkitProject project,
        string tag,
        string date,
        string captureId,
        string sourcePath,
        string targetFileName,
        string? deviceSerial)
    {
        var captureDir = Path.Combine(project.ContentDir, "Android", tag, date, captureId);
        Directory.CreateDirectory(Path.Combine(captureDir, "Saved"));
        File.Copy(sourcePath, Path.Combine(captureDir, "Saved", targetFileName));
        if (deviceSerial is not null)
        {
            WriteManifest(Path.Combine(captureDir, "CaptureManifest.json"), captureId, "Android", tag, date, deviceSerial);
        }

        return captureDir;
    }

    internal static void WriteManifest(string path, string captureId, string platform, string tag, string date, string deviceSerial)
    {
        var manifest = $"{{\"CaptureId\":\"{captureId}\",\"Platform\":\"{platform}\",\"Tag\":\"{tag}\"," +
                       $"\"StartedAt\":\"{date}T00:00:00+00:00\",\"CompletedAt\":\"{date}T01:00:00+00:00\"," +
                       $"\"PackageName\":\"com.example.test\",\"DeviceSerialNumber\":\"{deviceSerial}\",\"DeviceModel\":\"TestModel\"}}";
        File.WriteAllText(Path.GetFullPath(path), manifest);
    }

    internal static string MemInfoSample(string fileName) => Path.Combine(ApplicationPaths.AppDir, "TestData", "MemInfo", fileName);

    internal static string StaticCameraSample(string fileName) => Path.Combine(ApplicationPaths.AppDir, "TestData", "StaticCamera", fileName);

    internal static string BaselineSample(string fileName) => Path.Combine(ApplicationPaths.AppDir, "TestData", "Baseline", fileName);
}
