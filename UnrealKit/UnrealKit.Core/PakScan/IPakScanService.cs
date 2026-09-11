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
}
