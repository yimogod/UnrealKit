using UnrealKit.Core.Analysis;
using UnrealKit.Core.Diagnostics;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Tests;

public sealed class BaselineServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "UnrealKit.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DiffAsync_MemInfo_ComputesDeltaAndDirection()
    {
        var result = await new BaselineService().DiffAsync(new BaselineDiffRequest(
            BaselineDiffSource.MemInfo,
            MemInfoSample("complete-meminfo.txt"),
            BaselineSample("current-meminfo.txt")));

        Assert.True(result.IsSuccess);

        var total = FindMetric(result, "AppSummary", "TotalPssKb");
        Assert.Equal(MetricDiffStatus.Compared, total.Status);
        Assert.Equal(30680, total.BaselineValue);
        Assert.Equal(33704, total.CurrentValue);
        Assert.Equal(3024, total.Delta);
        Assert.Equal("KB", total.Unit);
        Assert.Equal(MetricDirection.LowerIsBetter, total.Direction);
        Assert.Equal(MetricDiffAssessment.Regressed, total.Assessment);

        // Memory dropping is an improvement because the metric is LowerIsBetter.
        var javaHeap = FindMetric(result, "AppSummary", "JavaHeapKb");
        Assert.Equal(-1000, javaHeap.Delta);
        Assert.Equal(MetricDiffAssessment.Improved, javaHeap.Assessment);

        var code = FindMetric(result, "AppSummary", "CodeKb");
        Assert.Equal(0, code.Delta);
        Assert.Equal(MetricDiffAssessment.Unchanged, code.Assessment);
    }

    [Fact]
    public async Task DiffAsync_MemInfo_ComputesDeltaPercentRelativeToBaseline()
    {
        var result = await new BaselineService().DiffAsync(new BaselineDiffRequest(
            BaselineDiffSource.MemInfo,
            MemInfoSample("complete-meminfo.txt"),
            BaselineSample("current-meminfo.txt")));

        var graphics = FindMetric(result, "AppSummary", "GraphicsKb");
        Assert.Equal(4096, graphics.BaselineValue);
        Assert.Equal(6144, graphics.CurrentValue);
        Assert.Equal(50.0, graphics.DeltaPercent!.Value, 6);
    }

    [Fact]
    public async Task DiffAsync_MetricMissingOnOneSide_ReportsMissingNotZero()
    {
        var result = await new BaselineService().DiffAsync(new BaselineDiffRequest(
            BaselineDiffSource.MemInfo,
            MemInfoSample("complete-meminfo.txt"),
            BaselineSample("current-meminfo.txt")));

        // The current sample omits the System line, so the metric must read as missing.
        var system = FindMetric(result, "AppSummary", "SystemKb");
        Assert.Equal(MetricDiffStatus.MissingInCurrent, system.Status);
        Assert.Equal(1024, system.BaselineValue);
        Assert.Null(system.CurrentValue);
        Assert.Null(system.Delta);
        Assert.Null(system.DeltaPercent);
        Assert.Equal(MetricDiffAssessment.Unknown, system.Assessment);

        // Objects exist only in the current sample.
        var views = FindMetric(result, "Objects", "Views");
        Assert.Equal(MetricDiffStatus.MissingInBaseline, views.Status);
        Assert.Null(views.BaselineValue);
        Assert.Equal(8, views.CurrentValue);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BDF202" && diagnostic.Severity == DiagnosticSeverity.Warning);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DiffAsync_MemReport_ComparesSummaryAndDetailMetrics()
    {
        var result = await new BaselineService().DiffAsync(new BaselineDiffRequest(
            BaselineDiffSource.MemReport,
            MemReportSample("complete-details.memreport"),
            BaselineSample("current-details.memreport")));

        Assert.True(result.IsSuccess);

        var texture = FindMetric(result, "Textures", "Texture2D /Game/Textures/T_Stone");
        Assert.Equal(1536, texture.BaselineValue);
        Assert.Equal(2048, texture.CurrentValue);
        Assert.Equal(512, texture.Delta);
        Assert.Equal(MetricDiffAssessment.Regressed, texture.Assessment);

        var renderTarget = FindMetric(result, "RenderTargets", "TextureRenderTarget2D /Game/UI/RT_Minimap");
        Assert.Equal(MetricDiffAssessment.Unchanged, renderTarget.Assessment);

        // Detail counts carry no better/worse meaning, so a change is reported as Changed.
        var objectCount = FindMetric(result, "Details", "ObjectCount");
        Assert.Equal(MetricDirection.Neutral, objectCount.Direction);
        Assert.Equal(MetricDiffAssessment.Unchanged, objectCount.Assessment);
    }

    [Fact]
    public async Task DiffAsync_MemReport_MissingSummaryMetricsRemainVisible()
    {
        var result = await new BaselineService().DiffAsync(new BaselineDiffRequest(
            BaselineDiffSource.MemReport,
            MemReportSample("complete-details.memreport"),
            BaselineSample("current-details.memreport")));

        // Neither sample contains Shader, so the metric row must still be present as missing.
        var shader = FindMetric(result, "Shader", "Shader");
        Assert.Equal(MetricDiffStatus.MissingInBoth, shader.Status);
        Assert.Null(shader.BaselineValue);
        Assert.Null(shader.CurrentValue);
        Assert.Equal(MetricDiffAssessment.Unknown, shader.Assessment);
    }

    [Fact]
    public async Task DiffAsync_StaticCamera_MatchesCamerasByNameAndFlagsRenames()
    {
        var result = await new BaselineService().DiffAsync(new BaselineDiffRequest(
            BaselineDiffSource.StaticCamera,
            StaticCameraSample("complete-static-camera.log"),
            BaselineSample("current-static-camera.log")));

        Assert.True(result.IsSuccess);

        var averageFrame = FindMetric(result, "Average", "FrameTimeMs");
        Assert.Equal(MetricDiffStatus.Compared, averageFrame.Status);
        Assert.Equal("ms", averageFrame.Unit);

        // Camera_Mid_1 got faster in the current run.
        var midFrame = FindMetric(result, "Camera:Camera_Mid_1", "FrameTimeMs");
        Assert.Equal(22.10, midFrame.BaselineValue!.Value, 4);
        Assert.Equal(19.40, midFrame.CurrentValue!.Value, 4);
        Assert.Equal(MetricDiffAssessment.Improved, midFrame.Assessment);

        // Camera_High_2 was renamed to Camera_New_2, so neither side matches the other.
        Assert.Equal(MetricDiffStatus.MissingInCurrent, FindMetric(result, "Camera:Camera_High_2", "FrameTimeMs").Status);
        Assert.Equal(MetricDiffStatus.MissingInBaseline, FindMetric(result, "Camera:Camera_New_2", "FrameTimeMs").Status);
    }

    [Fact]
    public async Task DiffAsync_MetricFilter_RestrictsResultToRequestedNames()
    {
        var result = await new BaselineService().DiffAsync(new BaselineDiffRequest(
            BaselineDiffSource.MemInfo,
            MemInfoSample("complete-meminfo.txt"),
            BaselineSample("current-meminfo.txt"),
            MetricFilter: ["TotalPssKb", "AppSummary/GraphicsKb"]));

        Assert.Equal(2, result.Metrics.Count);
        Assert.Contains(result.Metrics, metric => metric.Name == "TotalPssKb");
        Assert.Contains(result.Metrics, metric => metric.Name == "GraphicsKb");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "BDF201");
    }

    [Fact]
    public async Task DiffAsync_UnknownMetricInFilter_WarnsWithoutFailing()
    {
        var result = await new BaselineService().DiffAsync(new BaselineDiffRequest(
            BaselineDiffSource.MemInfo,
            MemInfoSample("complete-meminfo.txt"),
            BaselineSample("current-meminfo.txt"),
            MetricFilter: ["TotalPssKb", "NoSuchMetric"]));

        Assert.Single(result.Metrics);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "BDF201" &&
            diagnostic.Severity == DiagnosticSeverity.Warning &&
            diagnostic.Message.Contains("NoSuchMetric", StringComparison.Ordinal));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DiffAsync_LabelsArePreservedOnBothSides()
    {
        var result = await new BaselineService().DiffAsync(new BaselineDiffRequest(
            BaselineDiffSource.MemInfo,
            MemInfoSample("complete-meminfo.txt"),
            BaselineSample("current-meminfo.txt"),
            BaselineLabel: "20260801-baseline",
            CurrentLabel: "20260809-current"));

        Assert.Equal("20260801-baseline", result.BaselineLabel);
        Assert.Equal("20260809-current", result.CurrentLabel);
    }

    [Fact]
    public async Task DiffAsync_BaselineParseFailure_ReturnsErrorWithoutMetrics()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var badPath = Path.Combine(_temporaryDirectory, "bad-meminfo.txt");
        await File.WriteAllTextAsync(badPath, "not a valid meminfo file");

        var result = await new BaselineService().DiffAsync(new BaselineDiffRequest(
            BaselineDiffSource.MemInfo,
            badPath,
            MemInfoSample("complete-meminfo.txt")));

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Metrics);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BDF102" && diagnostic.Severity == DiagnosticSeverity.Error);
        // The underlying parse diagnostic is carried through and tagged with the side it came from.
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "AMI101" && diagnostic.Message.StartsWith("[baseline]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiffAsync_CurrentParseFailure_ReturnsErrorWithoutMetrics()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var badPath = Path.Combine(_temporaryDirectory, "bad-meminfo.txt");
        await File.WriteAllTextAsync(badPath, "not a valid meminfo file");

        var result = await new BaselineService().DiffAsync(new BaselineDiffRequest(
            BaselineDiffSource.MemInfo,
            MemInfoSample("complete-meminfo.txt"),
            badPath));

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Metrics);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BDF103" && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "AMI101" && diagnostic.Message.StartsWith("[current]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Diff_MismatchedSources_ReturnsError()
    {
        var service = new BaselineService();
        var memInfo = await service.LoadSnapshotAsync(BaselineDiffSource.MemInfo, MemInfoSample("complete-meminfo.txt"));
        var memReport = await service.LoadSnapshotAsync(BaselineDiffSource.MemReport, MemReportSample("complete-details.memreport"));

        var result = service.Diff(memInfo, memReport);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Metrics);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BDF101" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Diff_SameSnapshotOnBothSides_ReportsEverythingUnchanged()
    {
        var service = new BaselineService();
        var snapshot = await service.LoadSnapshotAsync(BaselineDiffSource.MemInfo, MemInfoSample("complete-meminfo.txt"));

        var result = service.Diff(snapshot, snapshot);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Metrics);
        Assert.Equal(0, result.RegressedCount);
        Assert.Equal(0, result.ImprovedCount);
        Assert.Equal(result.Metrics.Count, result.UnchangedCount);
        Assert.Equal(0, result.MissingCount);
    }

    [Fact]
    public async Task LoadSnapshotAsync_PreservesInputPathAndLabel()
    {
        var path = MemInfoSample("complete-meminfo.txt");
        var snapshot = await new BaselineService().LoadSnapshotAsync(BaselineDiffSource.MemInfo, path, "baseline-01");

        Assert.Equal(BaselineDiffSource.MemInfo, snapshot.Source);
        Assert.Equal(Path.GetFullPath(path), snapshot.InputPath);
        Assert.Equal("baseline-01", snapshot.Label);
        Assert.True(snapshot.IsSuccess);
        Assert.NotEmpty(snapshot.Samples);
    }

    [Fact]
    public Task LoadSnapshotAsync_MissingFile_Throws()
    {
        return Assert.ThrowsAsync<FileNotFoundException>(() => new BaselineService().LoadSnapshotAsync(
            BaselineDiffSource.MemInfo,
            Path.Combine(_temporaryDirectory, "nonexistent.txt")));
    }

    [Fact]
    public Task LoadSnapshotAsync_DirectoryInput_Throws()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        return Assert.ThrowsAsync<ArgumentException>(() => new BaselineService().LoadSnapshotAsync(
            BaselineDiffSource.MemInfo,
            _temporaryDirectory));
    }

    [Fact]
    public Task DiffAsync_MissingBaselinePath_Throws()
    {
        return Assert.ThrowsAsync<ArgumentException>(() => new BaselineService().DiffAsync(new BaselineDiffRequest(
            BaselineDiffSource.MemInfo,
            "   ",
            MemInfoSample("complete-meminfo.txt"))));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private static MetricDiff FindMetric(BaselineDiffResult result, string group, string name)
    {
        var metric = result.Metrics.SingleOrDefault(candidate =>
            string.Equals(candidate.Group, group, StringComparison.Ordinal) &&
            string.Equals(candidate.Name, name, StringComparison.Ordinal));
        Assert.NotNull(metric);
        return metric!;
    }

    private static string MemInfoSample(string fileName) => Path.Combine(ApplicationPaths.AppDir, "TestData", "MemInfo", fileName);

    private static string MemReportSample(string fileName) => Path.Combine(ApplicationPaths.AppDir, "TestData", "MemReport", fileName);

    private static string StaticCameraSample(string fileName) => Path.Combine(ApplicationPaths.AppDir, "TestData", "StaticCamera", fileName);

    private static string BaselineSample(string fileName) => Path.Combine(ApplicationPaths.AppDir, "TestData", "Baseline", fileName);
}
