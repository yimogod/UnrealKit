using UnrealKit.Core.Diagnostics;

namespace UnrealKit.Core.Parsing;

public enum StaticCameraPerfDataCompleteness
{
    Complete,
    Truncated
}

public sealed record StaticCameraPerfFrame(
    int Index,
    string CameraName,
    double FrameTimeMs,
    double GameTimeMs,
    double DrawTimeMs,
    double RhiTimeMs,
    double GpuTimeMs,
    long MemoryBytes,
    long DrawCalls,
    long Triangles,
    IReadOnlyList<string> Screenshots,
    int FirstLineNumber);

public sealed record StaticCameraPerfAverage(
    double FrameTimeMs,
    double GameTimeMs,
    double DrawTimeMs,
    double RhiTimeMs,
    double GpuTimeMs,
    long MemoryBytes,
    long DrawCalls,
    long Triangles);

public sealed record StaticCameraPerfDeviceInfo(
    string? OsPlatform,
    string? DeviceMake,
    string? GpuVendor,
    bool? VulkanAvailable,
    string? VulkanVersion);

public sealed record StaticCameraPerfReport(
    int CameraCount,
    int ParseCameraCount,
    StaticCameraPerfDataCompleteness Completeness,
    StaticCameraPerfDeviceInfo DeviceInfo,
    StaticCameraPerfAverage Average,
    IReadOnlyList<StaticCameraPerfFrame> Frames);

public sealed record StaticCameraPerfParseResult(
    string InputPath,
    StaticCameraPerfReport? Report,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsSuccess => Report is not null && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}