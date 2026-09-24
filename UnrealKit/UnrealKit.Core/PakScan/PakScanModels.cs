using System.IO;
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
    string PakChunkId,
    IReadOnlyList<string> UsedByMaterialNames);

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

public sealed record PakMaterialEntry(
    string Name,
    string ObjectPath,
    string BlendMode,
    string ShadingModel,
    int ReferencedTextureCount,
    bool TwoSided,
    string PakChunkId);

public sealed record PakMaterialInstanceEntry(
    string Name,
    string ObjectPath,
    string ParentName,
    int TextureParameterCount,
    string PakChunkId);

public sealed record PakScanReport(
    string InputDirectory,
    int TotalAssetsScanned,
    int TextureCount,
    IReadOnlyList<PakTextureEntry> Textures,
    int StaticMeshCount,
    IReadOnlyList<PakMeshEntry> StaticMeshes,
    int SkeletalMeshCount,
    IReadOnlyList<PakMeshEntry> SkeletalMeshes,
    int MaterialCount,
    IReadOnlyList<PakMaterialEntry> Materials,
    int MaterialInstanceCount,
    IReadOnlyList<PakMaterialInstanceEntry> MaterialInstances);

public sealed record PakScanResult(
    string InputPath,
    PakScanReport? Report,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsSuccess => Report is not null
        && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}

// ── 地图 Actor 扫描数据模型 ───────────────────────────────────────────────────

/// <summary>单张地图里某个 StaticMesh 被 StaticMeshActor 放置的次数。</summary>
public sealed record MapMeshPlacement(string MeshObjectPath, int Count);

/// <summary>单张地图的 StaticMeshActor 统计，Placements 按 Count 降序。</summary>
public sealed record MapMeshUsageEntry(
    string MapObjectPath,
    IReadOnlyList<MapMeshPlacement> Placements,
    int TotalActorCount,
    int FailedActorCount);

/// <summary>某个 StaticMesh 在所有扫描地图的累计放置统计。</summary>
public sealed record MapMeshAggregate(
    string MeshObjectPath,
    int TotalCount,
    int MapCount);

/// <summary>地图 Actor 扫描的顶层结果。</summary>
public sealed record MapActorScanResult(
    string InputDirectory,
    IReadOnlyList<MapMeshUsageEntry> PerMapEntries,
    IReadOnlyList<MapMeshUsageEntry> ReferencedLevelEntries,
    IReadOnlyList<MapMeshAggregate> Aggregates,
    int TotalMapsScanned,
    int TotalMapsWithErrors,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsSuccess => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
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

/// <summary>发现一个 Material。</summary>
public sealed record PakScanMaterialFound(PakMaterialEntry Material) : PakScanEntry;

/// <summary>发现一个 MaterialInstanceConstant。</summary>
public sealed record PakScanMaterialInstanceFound(PakMaterialInstanceEntry MaterialInstance) : PakScanEntry;

/// <summary>Texture ObjectPath → 引用该贴图的材质名称列表，扫描收尾后一次性推送。</summary>
public sealed record PakScanTextureUsageReadyEntry(IReadOnlyDictionary<string, List<string>> Usage) : PakScanEntry;

/// <summary>产生一条诊断信息（警告、错误等）。</summary>
public sealed record PakScanDiagnosticEntry(Diagnostic Diagnostic) : PakScanEntry;

/// <summary>扫描完成。</summary>
public sealed record PakScanCompleteEntry(
    int TotalScanned,
    int TextureCount,
    int StaticMeshCount,
    int SkeletalMeshCount,
    int MaterialCount,
    int MaterialInstanceCount,
    TimeSpan Elapsed) : PakScanEntry;

// ── 地图 Actor 扫描流式事件 ───────────────────────────────────────────────────

/// <summary>地图 Actor 扫描开始，携带待扫描 .umap 总数。</summary>
public sealed record PakMapScanStartEntry(int TotalMaps) : PakScanEntry;

/// <summary>完成一张地图的扫描。</summary>
public sealed record PakMapScanProgressEntry(
    int Scanned,
    int Total,
    string MapPath,
    int PlacementsFound) : PakScanEntry;

/// <summary>一张地图的 StaticMesh 放置统计已就绪。</summary>
public sealed record PakMapMeshUsageFound(MapMeshUsageEntry Entry) : PakScanEntry;

/// <summary>所有地图扫描完成，携带最终结果。</summary>
public sealed record PakMapScanCompleteEntry(MapActorScanResult Result, TimeSpan Elapsed) : PakScanEntry;

/// <summary>
/// 将地图 objectPath 解析为可读名称。
/// World Partition 生成的子关卡路径含 _Generated_ 片段，取其父目录名；否则取末段文件名。
/// 例：.../TarkovWorldA/_Generated_/CGQQ... → "TarkovWorldA"
/// </summary>
public static class MapObjectPathHelper
{
    private const string GeneratedMarker = "_Generated_";

    public static string ResolveMapName(string mapObjectPath)
    {
        int idx = mapObjectPath.IndexOf(GeneratedMarker, StringComparison.Ordinal);
        if (idx >= 0)
        {
            var before = mapObjectPath[..idx].TrimEnd('/');
            int slash = before.LastIndexOf('/');
            return slash >= 0 ? before[(slash + 1)..] : before;
        }
        return Path.GetFileNameWithoutExtension(mapObjectPath);
    }
}
