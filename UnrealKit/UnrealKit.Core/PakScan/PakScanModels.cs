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
    long EstimatedSizeBytes,
    string PakChunkId);

public enum PakMeshKind { StaticMesh, SkeletalMesh }

public sealed record PakMeshEntry(
    string Name,
    string ObjectPath,
    PakMeshKind Kind,
    int LodCount,
    int MaterialCount,
    int BoneCount,
    int VertexCount,
    int TriangleCount,
    string PakChunkId);

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

// ── 流式推送条目 ──────────────────────────────────────────────────────────────

/// <summary>ScanStreamAsync 每次 yield 的条目基类。</summary>
public abstract record PakScanEntry;

/// <summary>扫描开始，携带待扫描资产总数。</summary>
public sealed record PakScanStartEntry(int TotalAssets) : PakScanEntry;

/// <summary>扫描进度，每扫描一个资产推送一次。</summary>
public sealed record PakScanProgressEntry(int Scanned, int Total, string CurrentAsset) : PakScanEntry;

/// <summary>发现一个 Texture2D。</summary>
public sealed record PakScanTextureFound(PakTextureEntry Texture) : PakScanEntry;

/// <summary>发现一个 StaticMesh。</summary>
public sealed record PakScanStaticMeshFound(PakMeshEntry Mesh) : PakScanEntry;

/// <summary>发现一个 SkeletalMesh。</summary>
public sealed record PakScanSkeletalMeshFound(PakMeshEntry Mesh) : PakScanEntry;

/// <summary>产生一条诊断信息（警告、错误等）。</summary>
public sealed record PakScanDiagnosticEntry(Diagnostic Diagnostic) : PakScanEntry;

/// <summary>扫描完成。</summary>
public sealed record PakScanCompleteEntry(
    int TotalScanned,
    int TextureCount,
    int StaticMeshCount,
    int SkeletalMeshCount,
    TimeSpan Elapsed) : PakScanEntry;
