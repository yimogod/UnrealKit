using UnrealKit.Core.Adb;

namespace UnrealKit.Core.Capture;

public sealed record CaptureRequest(
    Projects.UkitProject Project,
    AdbDevice Device,
    string Tag,
    string? CaptureId = null);

public sealed record CapturePlan(
    string CaptureId,
    string CaptureDirectory,
    string DeviceSavedDirectory);

public sealed record CaptureFileManifestEntry(
    string RelativePath,
    long SizeBytes,
    string Sha256);

public sealed record CaptureManifest(
    string CaptureId,
    string Platform,
    string Tag,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    Projects.ProjectConfigurationSnapshot ProjectConfiguration,
    string DeviceSerialNumber,
    string? DeviceModel,
    string DeviceStatus,
    string PackageName,
    string DeviceSavedDirectory,
    IReadOnlyList<CaptureFileManifestEntry> InputFiles);

public sealed record CaptureResult(
    CapturePlan Plan,
    string ManifestPath,
    CaptureManifest Manifest);
