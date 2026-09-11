using UnrealKit.Core.Operations;

namespace UnrealKit.Core.PakScan;

public interface IPakScanService
{
    /// <summary>流式扫描：每发现一个资产或产生诊断即 yield，调用方可实时消费。</summary>
    IAsyncEnumerable<PakScanEntry> ScanStreamAsync(
        string pakDirectory,
        PakScanConfig? config = null,
        CancellationToken cancellationToken = default);

    /// <summary>聚合扫描：内部调用 ScanStreamAsync，等待全部完成后返回 PakScanResult。</summary>
    Task<PakScanResult> ScanAsync(
        string pakDirectory,
        PakScanConfig? config = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按需解码指定 Texture2D 为 PNG 字节。调用前必须已完成至少一次扫描（provider 已初始化）。
    /// 返回 null 表示 provider 未就绪、资产加载失败或像素格式不受支持。
    /// </summary>
    Task<byte[]?> DecodeTexturePngAsync(string objectPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按需导出指定 Mesh（StaticMesh 或 SkeletalMesh）为 GLB 字节。
    /// 返回 null 表示 provider 未就绪、资产加载失败或不是 Mesh 类型。
    /// </summary>
    Task<byte[]?> ExportMeshGlbAsync(string objectPath, CancellationToken cancellationToken = default);
}
