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
///
/// ResolvedTarget 记录采集时实际用到的平台落地值（进程标识、设备端路径）。
/// ProjectConfiguration 是当时的整份工程配置，含全部已配置平台；ResolvedTarget
/// 则指明本次用的是哪一个、展开成了什么，读者不必重新展开模板去猜。
/// 导入的归档没有涉及设备，该字段为 null。
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
    Projects.PlatformTarget? ResolvedTarget,
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