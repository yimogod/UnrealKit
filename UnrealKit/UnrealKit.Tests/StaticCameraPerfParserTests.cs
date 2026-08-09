using UnrealKit.Core.Parsing;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Tests;

public sealed class StaticCameraPerfParserTests
{
    [Fact]
    public async Task ParseFileAsync_ParsesCompleteLogWithAllCameras()
    {
        var result = await new StaticCameraPerfParser().ParseFileAsync(GetSamplePath("complete-static-camera.log"));

        Assert.True(result.IsSuccess);
        var report = Assert.IsType<StaticCameraPerfReport>(result.Report);
        Assert.Equal(3, report.CameraCount);
        Assert.Equal(3, report.ParseCameraCount);
        Assert.Equal(StaticCameraPerfDataCompleteness.Complete, report.Completeness);

        Assert.Equal("Android (14), CPU: 23127PN0CC, GPU: Adreno (TM) 750", report.DeviceInfo.OsPlatform);
        Assert.Equal("Xiaomi", report.DeviceInfo.DeviceMake);
        Assert.Equal("Qualcomm", report.DeviceInfo.GpuVendor);
        Assert.True(report.DeviceInfo.VulkanAvailable);
        Assert.Equal("1.3.128", report.DeviceInfo.VulkanVersion);

        Assert.Equal(3, report.Frames.Count);

        // Camera 0
        var cam0 = report.Frames[0];
        Assert.Equal("Camera_Base_0", cam0.CameraName);
        Assert.Equal(16.84, cam0.FrameTimeMs);
        Assert.Equal(6.25, cam0.GameTimeMs);
        Assert.Equal(2.88, cam0.DrawTimeMs);
        Assert.Equal(3.09, cam0.RhiTimeMs);
        Assert.Equal(5.98, cam0.GpuTimeMs);
        Assert.Equal(-768212992, cam0.MemoryBytes);
        Assert.Equal(192, cam0.DrawCalls);
        Assert.Equal(239429, cam0.Triangles);

        // Camera 2
        var cam2 = report.Frames[2];
        Assert.Equal("Camera_High_2", cam2.CameraName);
        Assert.Equal(35.00, cam2.FrameTimeMs);
        Assert.Equal(550, cam2.DrawCalls);
        Assert.Equal(750000, cam2.Triangles);

        // Averages (3 cameras)
        Assert.True(report.Average.FrameTimeMs > 20);
        Assert.True(report.Average.DrawCalls > 300);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task ParseFileAsync_HandlesTruncatedLog()
    {
        var result = await new StaticCameraPerfParser().ParseFileAsync(GetSamplePath("truncated-static-camera.log"));

        Assert.True(result.IsSuccess);
        var report = Assert.IsType<StaticCameraPerfReport>(result.Report);
        Assert.Equal(5, report.CameraCount);       // PointNum says 5
        Assert.Equal(2, report.ParseCameraCount);   // Only 2 full cameras parsed
        Assert.Equal(StaticCameraPerfDataCompleteness.Truncated, report.Completeness);

        Assert.Equal(2, report.Frames.Count);
        Assert.Equal("Camera_A", report.Frames[0].CameraName);
        Assert.Equal("Camera_B", report.Frames[1].CameraName);

        Assert.Contains(result.Diagnostics, d => d.Code == "SCP202" && d.Severity == Core.Diagnostics.DiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task ParseFileAsync_ReportsErrorOnMissingPointNum()
    {
        var result = await new StaticCameraPerfParser().ParseFileAsync(GetSamplePath("no-pointnum.log"));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Report);
        Assert.Contains(result.Diagnostics, d => d.Code == "SCP102");
    }

    [Fact]
    public async Task ParseFileAsync_RejectsDirectoryInput()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            new StaticCameraPerfParser().ParseFileAsync(Path.GetDirectoryName(GetSamplePath("complete-static-camera.log"))!));

        Assert.Contains("must be a file", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_Validate_ThrowsWhenWarningNotLessThanError()
    {
        Assert.Throws<InvalidOperationException>(() => new StaticCameraPerfConfig { FrameTimeWarningMs = 40, FrameTimeErrorMs = 30 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new StaticCameraPerfConfig { DrawCallWarning = 600, DrawCallError = 500 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new StaticCameraPerfConfig { TriangleWarning = 800000, TriangleError = 700000 }.Validate());
    }

    [Fact]
    public void Config_Default_IsValid()
    {
        var config = StaticCameraPerfConfig.Default;
        config.Validate(); // Should not throw
    }

    [Fact]
    public void Config_Default_DCSeparatesWarningFromError()
    {
        var config = StaticCameraPerfConfig.Default;
        Assert.True(config.DrawCallWarning < config.DrawCallError,
            "DC warning must be strictly less than DC error to fix the old script defect where both were 500.");
    }

    private static string GetSamplePath(string fileName) =>
        Path.Combine(ApplicationPaths.AppDir, "TestData", "StaticCamera", fileName);
}