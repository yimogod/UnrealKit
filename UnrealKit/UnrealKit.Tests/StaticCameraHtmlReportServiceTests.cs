using UnrealKit.Core.Diagnostics;
using UnrealKit.Core.Parsing;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Tests;

public sealed class StaticCameraHtmlReportServiceTests
{
    [Fact]
    public async Task GenerateAsync_CreatesHtmlFileFromValidParseResult()
    {
        var parser = new StaticCameraPerfParser();
        var parseResult = await parser.ParseFileAsync(GetSamplePath("complete-static-camera.log"));
        Assert.True(parseResult.IsSuccess);

        var outputPath = Path.Combine(Path.GetTempPath(), $"ukit-test-scp-report-{Guid.NewGuid()}.html");
        var service = new StaticCameraHtmlReportService();

        try
        {
            var result = await service.GenerateAsync(new StaticCameraHtmlReportRequest(
                parseResult,
                outputPath));

            Assert.Equal(outputPath, result.OutputFilePath);
            Assert.True(File.Exists(outputPath));

            var html = await File.ReadAllTextAsync(outputPath);

            // Verify key sections exist
            Assert.Contains("Static Camera Performance Report", html, StringComparison.Ordinal);
            Assert.Contains("Device Information", html, StringComparison.Ordinal);
            Assert.Contains("Xiaomi", html, StringComparison.Ordinal);
            Assert.Contains("Qualcomm", html, StringComparison.Ordinal);
            Assert.Contains("Summary", html, StringComparison.Ordinal);
            Assert.Contains("Camera Details", html, StringComparison.Ordinal);
            Assert.Contains("Camera_Base_0", html, StringComparison.Ordinal);
            Assert.Contains("Camera_Mid_1", html, StringComparison.Ordinal);
            Assert.Contains("Camera_High_2", html, StringComparison.Ordinal);
            Assert.Contains("threshold-error", html, StringComparison.Ordinal); // Camera_High_2 has DC=550 which exceeds error threshold 500
            Assert.Contains("toggleCollapsible", html, StringComparison.Ordinal);

            // No diagnostics section when empty
            // Diagnostics section header <h2>Diagnostics</h2> should not be present when empty
            Assert.DoesNotContain("<h2>Diagnostics</h2>", html, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task GenerateAsync_IncludesDiagnosticsWhenPresent()
    {
        var parser = new StaticCameraPerfParser();
        var parseResult = await parser.ParseFileAsync(GetSamplePath("truncated-static-camera.log"));
        Assert.True(parseResult.IsSuccess);
        Assert.NotEmpty(parseResult.Diagnostics);

        var outputPath = Path.Combine(Path.GetTempPath(), $"ukit-test-scp-report-{Guid.NewGuid()}.html");
        var service = new StaticCameraHtmlReportService();

        try
        {
            var result = await service.GenerateAsync(new StaticCameraHtmlReportRequest(
                parseResult,
                outputPath));

            var html = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("Diagnostics", html, StringComparison.Ordinal);
            Assert.Contains("SCP202", html, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task GenerateAsync_ReportsThresholdViolations()
    {
        var parser = new StaticCameraPerfParser();
        var parseResult = await parser.ParseFileAsync(GetSamplePath("complete-static-camera.log"));
        Assert.True(parseResult.IsSuccess);

        var outputPath = Path.Combine(Path.GetTempPath(), $"ukit-test-scp-report-{Guid.NewGuid()}.html");
        var service = new StaticCameraHtmlReportService();

        try
        {
            await service.GenerateAsync(new StaticCameraHtmlReportRequest(
                parseResult,
                outputPath));

            var html = await File.ReadAllTextAsync(outputPath);

            // Camera_High_2: DC=550 > 500 (error), Triangles=750000 >= 700000 (error)
            Assert.Contains("threshold-error", html, StringComparison.Ordinal);

            // Camera_Mid_1: DC=420 > 400 (warning but < 500), Triangles=520000 > 500000 (warning but < 700000)
            Assert.Contains("threshold-warn", html, StringComparison.Ordinal);

            // Camera_Base_0: all OK
            Assert.Contains("threshold-ok", html, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task GenerateAsync_CreatesOutputDirectoryIfMissing()
    {
        var parser = new StaticCameraPerfParser();
        var parseResult = await parser.ParseFileAsync(GetSamplePath("complete-static-camera.log"));

        var tempDir = Path.Combine(Path.GetTempPath(), $"ukit-test-scp-subdir-{Guid.NewGuid()}");
        var outputPath = Path.Combine(tempDir, "report.html");
        var service = new StaticCameraHtmlReportService();

        try
        {
            await service.GenerateAsync(new StaticCameraHtmlReportRequest(parseResult, outputPath));
            Assert.True(File.Exists(outputPath));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateAsync_ThrowsOnNullRequest()
    {
        var service = new StaticCameraHtmlReportService();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.GenerateAsync(null!));
    }

    [Fact]
    public async Task GenerateAsync_ThrowsOnNullParseResult()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "test.html");
        var service = new StaticCameraHtmlReportService();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.GenerateAsync(new StaticCameraHtmlReportRequest(null!, outputPath)));
    }

    private static string GetSamplePath(string fileName) =>
        Path.Combine(ApplicationPaths.AppDir, "TestData", "StaticCamera", fileName);
}


