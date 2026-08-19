using UnrealKit.Core.Capture;
using UnrealKit.Core.Projects;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Tests;

public sealed class CaptureAnalysisServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "UnrealKit.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ListCaptureDirectoriesAsync_ReturnsCapturesInCorrectStructure()
    {
        var project = await CreateProjectAsync("ListTest");
        CreateCapture(project, "Android", "Nightly", "2026-08-06", "20260806-120000-dev01");
        CreateCapture(project, "Android", "Baseline", "2026-08-05", "20260805-090000-dev02");

        var service = new CaptureAnalysisService();
        var captures = await service.ListCaptureDirectoriesAsync(project);

        Assert.Equal(2, captures.Count);
        Assert.Contains(captures, c => c.Tag == "Nightly" && c.CaptureId == "20260806-120000-dev01" && c.Platform == "Android" && c.HasManifest);
        Assert.Contains(captures, c => c.Tag == "Baseline" && c.CaptureId == "20260805-090000-dev02");
    }

    [Fact]
    public async Task ListCaptureDirectoriesAsync_FilteredByTag_ReturnsMatchingOnly()
    {
        var project = await CreateProjectAsync("TagFilterTest");
        CreateCapture(project, "Android", "Nightly", "2026-08-06", "20260806-120000-dev01");
        CreateCapture(project, "Android", "Baseline", "2026-08-05", "20260805-090000-dev02");

        var service = new CaptureAnalysisService();
        var captures = await service.ListCaptureDirectoriesAsync(project, tag: "Nightly");

        Assert.Single(captures);
        Assert.Equal("Nightly", captures[0].Tag);
    }

    [Fact]
    public async Task ListCaptureDirectoriesAsync_EmptyContent_ReturnsEmpty()
    {
        var project = await CreateProjectAsync("EmptyContentTest");

        var service = new CaptureAnalysisService();
        var captures = await service.ListCaptureDirectoriesAsync(project);

        Assert.Empty(captures);
    }

    [Fact]
    public async Task ListCaptureDirectoriesAsync_CaptureWithoutManifest_HasManifestFalse()
    {
        var project = await CreateProjectAsync("NoManifestTest");
        var contentRoot = project.ContentDir;
        var captureDir = Path.Combine(contentRoot, "Android", "Solo", "2026-08-06", "20260806-no-manifest");
        Directory.CreateDirectory(Path.Combine(captureDir, "MemInfo"));

        var service = new CaptureAnalysisService();
        var captures = await service.ListCaptureDirectoriesAsync(project);

        Assert.Single(captures);
        Assert.False(captures[0].HasManifest);
    }

    [Fact]
    public async Task ListCaptureDirectoriesAsync_WithoutPlatformFilter_ListsEveryPlatform()
    {
        // 不传 platform 必须列出全部平台。曾经默认只看 Android，于是 Win64 归档
        // 在 GUI 采集列表与历史趋势里既不显示也不报错，看起来像是从未采集过。
        var project = await CreateProjectAsync("AllPlatformsTest");
        CreateCapture(project, "Android", "Nightly", "2026-08-06", "20260806-120000-dev01");
        CreateCapture(project, "Win64", "Nightly", "2026-08-06", "20260806-113000-localhost");

        var captures = await new CaptureAnalysisService().ListCaptureDirectoriesAsync(project);

        Assert.Equal(2, captures.Count);
        Assert.Contains(captures, c => c.Platform == "Android");
        Assert.Contains(captures, c => c.Platform == "Win64");
    }

    [Fact]
    public async Task ListCaptureDirectoriesAsync_FilteredByPlatform_ReturnsMatchingOnly()
    {
        var project = await CreateProjectAsync("PlatformFilterTest");
        CreateCapture(project, "Android", "Nightly", "2026-08-06", "20260806-120000-dev01");
        CreateCapture(project, "Win64", "Nightly", "2026-08-06", "20260806-113000-localhost");

        var captures = await new CaptureAnalysisService().ListCaptureDirectoriesAsync(project, platform: "Win64");

        Assert.Single(captures);
        Assert.Equal("Win64", captures[0].Platform);
    }

    [Fact]
    public async Task ListCaptureDirectoriesAsync_SameDateAcrossPlatforms_OrdersStably()
    {
        // 同日期的多份归档必须稳定排序：目录枚举顺序由文件系统决定，
        // 仅按日期排会让「最近一份」在两次刷新之间跳动。
        var project = await CreateProjectAsync("StableOrderTest");
        CreateCapture(project, "Android", "Nightly", "2026-08-06", "20260806-120000-dev01");
        CreateCapture(project, "Win64", "Nightly", "2026-08-06", "20260806-113000-localhost");

        var service = new CaptureAnalysisService();
        var first = await service.ListCaptureDirectoriesAsync(project);
        var second = await service.ListCaptureDirectoriesAsync(project);

        Assert.Equal(first.Select(c => c.CaptureId), second.Select(c => c.CaptureId));
        Assert.Equal("20260806-120000-dev01", first[0].CaptureId);
    }

    [Fact]
    public async Task ListCaptureFilesAsync_ListsFilesInEachCategory()
    {
        var project = await CreateProjectAsync("FileListTest");
        var contentRoot = project.ContentDir;
        var captureDir = Path.Combine(contentRoot, "Android", "Nightly", "2026-08-06", "20260806-120000-dev01");
        Directory.CreateDirectory(Path.Combine(captureDir, "MemInfo"));
        Directory.CreateDirectory(Path.Combine(captureDir, "Saved"));
        await File.WriteAllTextAsync(Path.Combine(captureDir, "MemInfo", "meminfo_001.txt"), "meminfo content");
        await File.WriteAllTextAsync(Path.Combine(captureDir, "Saved", "ue4.log"), "log content");

        var service = new CaptureAnalysisService();
        var files = await service.ListCaptureFilesAsync(captureDir);

        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.Category == "MemInfo" && f.FileName == "meminfo_001.txt" && f.SizeBytes > 0);
        Assert.Contains(files, f => f.Category == "Saved" && f.FileName == "ue4.log");
    }

    [Fact]
    public Task ListCaptureFilesAsync_NonexistentDirectory_Throws()
    {
        var service = new CaptureAnalysisService();
        return Assert.ThrowsAsync<DirectoryNotFoundException>(() => service.ListCaptureFilesAsync(Path.Combine(_temporaryDirectory, "nonexistent")));
    }

    [Fact]
    public async Task AnalyzeMemInfoAsync_ParsesAndWritesResultToSavedAnalysis()
    {
        var project = await CreateProjectAsync("AnalyzeTest");
        var contentRoot = project.ContentDir;
        var captureDir = Path.Combine(contentRoot, "Android", "Nightly", "2026-08-06", "20260806-120000-dev01");
        Directory.CreateDirectory(Path.Combine(captureDir, "MemInfo"));
        var meminfoPath = Path.Combine(captureDir, "MemInfo", "meminfo_001.txt");
        File.Copy(GetSamplePath("complete-meminfo.txt"), meminfoPath);

        var service = new CaptureAnalysisService(() => new AppVersionInfo("2.0.0", "abc1234", DateTimeOffset.UtcNow));
        var result = await service.AnalyzeMemInfoAsync(new CaptureAnalysisRequest(project, captureDir, meminfoPath, "test-analysis-001"));

        Assert.Equal("test-analysis-001", result.AnalysisId);
        Assert.Equal("20260806-120000-dev01", result.CaptureId);
        Assert.True(result.ParseResult.IsSuccess);
        Assert.NotNull(result.ParseResult.Report);
        Assert.Equal("com.example.performance", result.ParseResult.Report!.ProcessName);

        Assert.True(File.Exists(result.ResultJsonPath));
        Assert.StartsWith(Path.Combine(project.SavedDir, "Analysis"), result.AnalysisDirectory);

        var outputContent = await File.ReadAllTextAsync(result.ResultJsonPath);
        Assert.Contains("test-analysis-001", outputContent, StringComparison.Ordinal);
        Assert.Contains("20260806-120000-dev01", outputContent, StringComparison.Ordinal);
        Assert.Contains("com.example.performance", outputContent, StringComparison.Ordinal);
        Assert.Contains("2.0.0", outputContent, StringComparison.Ordinal);
        Assert.Contains("abc1234", outputContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeMemInfoAsync_GeneratesAnalysisIdWhenNotProvided()
    {
        var project = await CreateProjectAsync("GenIdTest");
        var contentRoot = project.ContentDir;
        var captureDir = Path.Combine(contentRoot, "Android", "Nightly", "2026-08-06", "20260806-120000-dev01");
        Directory.CreateDirectory(Path.Combine(captureDir, "MemInfo"));
        var meminfoPath = Path.Combine(captureDir, "MemInfo", "meminfo_001.txt");
        File.Copy(GetSamplePath("complete-meminfo.txt"), meminfoPath);

        var service = new CaptureAnalysisService();
        var result = await service.AnalyzeMemInfoAsync(new CaptureAnalysisRequest(project, captureDir, meminfoPath));

        Assert.StartsWith("20260806-120000-dev01-", result.AnalysisId, StringComparison.Ordinal);
        Assert.True(Directory.Exists(result.AnalysisDirectory));
        Assert.True(File.Exists(result.ResultJsonPath));
    }

    [Fact]
    public Task AnalyzeMemInfoAsync_MissingCaptureDirectory_Throws()
    {
        var service = new CaptureAnalysisService();
        return Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            service.AnalyzeMemInfoAsync(new CaptureAnalysisRequest(
                new UkitProject("test.ukit", _temporaryDirectory, UkitProjectDescriptor.CreateDefault("Test"), ProjectSettings.CreateDefaults("Test")),
                Path.Combine(_temporaryDirectory, "nonexistent"),
                Path.Combine(_temporaryDirectory, "nonexistent.txt"))));
    }

    [Fact]
    public async Task AnalyzeMemInfoAsync_RecordsDiagnosticsOnParseFailure()
    {
        var project = await CreateProjectAsync("FailParseTest");
        var contentRoot = project.ContentDir;
        var captureDir = Path.Combine(contentRoot, "Android", "Nightly", "2026-08-06", "fail-capture");
        Directory.CreateDirectory(Path.Combine(captureDir, "MemInfo"));
        var meminfoPath = Path.Combine(captureDir, "MemInfo", "bad_meminfo.txt");
        await File.WriteAllTextAsync(meminfoPath, "not a valid meminfo file");

        var service = new CaptureAnalysisService();
        var result = await service.AnalyzeMemInfoAsync(new CaptureAnalysisRequest(project, captureDir, meminfoPath, "fail-test"));

        Assert.False(result.ParseResult.IsSuccess);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains(result.Diagnostics, d => d.Code == "AMI101");
        Assert.True(File.Exists(result.ResultJsonPath));

        var outputContent = await File.ReadAllTextAsync(result.ResultJsonPath);
        Assert.Contains("AMI101", outputContent, StringComparison.Ordinal);
        Assert.Contains("\"isSuccess\": false", outputContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeAnalysisDirectory_ReturnsCorrectPath()
    {
        var project = new UkitProject(
            Path.Combine(_temporaryDirectory, "test.ukit"),
            _temporaryDirectory,
            UkitProjectDescriptor.CreateDefault("Test"),
            ProjectSettings.CreateDefaults("Test"));

        var service = new CaptureAnalysisService();
        var dir = service.ComputeAnalysisDirectory(project, "my-analysis");

        Assert.Equal(Path.Combine(_temporaryDirectory, "Saved", "Analysis", "my-analysis"), dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
            Directory.Delete(_temporaryDirectory, recursive: true);
    }

    private async Task<UkitProject> CreateProjectAsync(string projectName)
    {
        var result = await new ProjectService().CreateProjectAsync(
            new CreateProjectRequest(Path.Combine(_temporaryDirectory, projectName), projectName));
        return result.Project;
    }

    private static void CreateCapture(UkitProject project, string platform, string tag, string date, string captureId)
    {
        var contentRoot = project.ContentDir;
        var captureDir = Path.Combine(contentRoot, platform, tag, date, captureId);
        Directory.CreateDirectory(Path.Combine(captureDir, "MemInfo"));
        var manifest = "{\"CaptureId\":\"" + captureId + "\",\"Platform\":\"" + platform + "\",\"Tag\":\"" + tag + "\",\"StartedAt\":\"" + date + "T00:00:00+00:00\",\"CompletedAt\":\"" + date + "T01:00:00+00:00\",\"PackageName\":\"com.example.test\",\"DeviceSerialNumber\":\"dev-01\"}";
        File.WriteAllText(Path.Combine(captureDir, "CaptureManifest.json"), manifest);
    }

    private static string GetSamplePath(string fileName) => Path.Combine(ApplicationPaths.AppDir, "TestData", "MemInfo", fileName);
}