using UnrealKit.Core.Devices;

namespace UnrealKit.Core.Capture;

public sealed record CaptureRequest(
    Projects.UkitProject Project,
    IDevice Device,
    string Tag,
    string? CaptureId = null,
    bool SkipSaved = false);

public sealed record CapturePlan(
    string CaptureId,
    string CaptureDirectory,
    string DeviceSavedDirectory);

public sealed record CaptureFileManifestEntry(
    string RelativePath,
    long SizeBytes,
    string Sha256);

/// <summary>
/// 采集清单。DeviceSerialNumber 在 Android 上为 ADB serial，在 Win64 上为主机名。
/// DeviceModel / PackageName 在非 Android 平台可以为 null。
/// </summary>
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

public sealed record CaptureImportRequest(
    Projects.UkitProject Project,
    string SourceDirectory,
    string Platform,
    string Tag,
    string? CaptureId = null);