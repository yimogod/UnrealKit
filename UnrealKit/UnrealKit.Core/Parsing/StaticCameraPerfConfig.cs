namespace UnrealKit.Core.Parsing;

public sealed record StaticCameraPerfConfig
{
    public static readonly StaticCameraPerfConfig Default = new();

    public string PerfStartTag { get; init; } = "!!!Do Perf Start!!!";
    public string PerfEndTag { get; init; } = "!!!Do Perf End!!!";
    public string PerfLinePrefix { get; init; } = "Perf: ";
    public string PointNumTag { get; init; } = "PointNum:";
    public string CameraInfoStartTag { get; init; } = "------- Current Camera Stat Info ----------- ";
    public string CameraInfoEndTag { get; init; } = "------- Current Camera Stat Info End -----------";
    public string FocusCameraPrefix { get; init; } = "AProfilerStaticScene::FocusCamera";
    public string CameraNamePrefix { get; init; } = "CamName";
    public string FrameTimeTag { get; init; } = "frame";
    public string GameTimeTag { get; init; } = "game";
    public string DrawTimeTag { get; init; } = "draw";
    public string RhiTimeTag { get; init; } = "rhi";
    public string GpuTimeTag { get; init; } = "gpu";
    public string MemoryTag { get; init; } = "mem";
    public string DrawCallTag { get; init; } = "dc";
    public string TriangleTag { get; init; } = "prim";
    public int FramesPerCamera { get; init; } = 14;
    public int ScreenshotsPerCamera { get; init; } = 11;

    public double FrameTimeWarningMs { get; init; } = 30.0;
    public double FrameTimeErrorMs { get; init; } = 33.4;
    public long DrawCallWarning { get; init; } = 400;
    public long DrawCallError { get; init; } = 500;
    public long TriangleWarning { get; init; } = 500000;
    public long TriangleError { get; init; } = 700000;

    public string OsLogPrefix { get; init; } = "LogInit: OS: ";
    public string DeviceMakeMarker { get; init; } = "SRC_DeviceMake: ";
    public string GpuVendorMarker { get; init; } = "[SRC_GpuVendor]:";
    public string VulkanAvailableMarker { get; init; } = "SRC_VulkanAvailable:";
    public string VulkanVersionMarker { get; init; } = "SRC_VulkanVersion:";

    public void Validate()
    {
        if (FrameTimeErrorMs <= FrameTimeWarningMs)
            throw new InvalidOperationException($"FrameTimeErrorMs ({FrameTimeErrorMs}) must be greater than FrameTimeWarningMs ({FrameTimeWarningMs}).");
        if (DrawCallError <= DrawCallWarning)
            throw new InvalidOperationException($"DrawCallError ({DrawCallError}) must be greater than DrawCallWarning ({DrawCallWarning}).");
        if (TriangleError <= TriangleWarning)
            throw new InvalidOperationException($"TriangleError ({TriangleError}) must be greater than TriangleWarning ({TriangleWarning}).");
    }
}