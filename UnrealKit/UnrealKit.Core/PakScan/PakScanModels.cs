using UnrealKit.Core.Diagnostics;

namespace UnrealKit.Core.PakScan;

public sealed record PakTextureEntry(
    string Name,
    string ObjectPath,
    int SizeX,
    int SizeY,
    string PixelFormat,
    int LodBias,
    string LodGroup,
    int NumMips,
    long EstimatedSizeBytes);

public enum PakMeshKind { StaticMesh, SkeletalMesh }

public sealed record PakMeshEntry(
    string Name,
    string ObjectPath,
    PakMeshKind Kind,
    int LodCount,
    int MaterialCount,
    int BoneCount);

public sealed record PakScanReport(
    string InputDirectory,
    int TotalAssetsScanned,
    int TextureCount,
    IReadOnlyList<PakTextureEntry> Textures,
    int StaticMeshCount,
    IReadOnlyList<PakMeshEntry> StaticMeshes,
    int SkeletalMeshCount,
    IReadOnlyList<PakMeshEntry> SkeletalMeshes);

public sealed record PakScanResult(
    string InputPath,
    PakScanReport? Report,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsSuccess => Report is not null
        && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}
