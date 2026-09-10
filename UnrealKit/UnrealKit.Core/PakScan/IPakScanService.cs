using UnrealKit.Core.Operations;

namespace UnrealKit.Core.PakScan;

public interface IPakScanService
{
    Task<PakScanResult> ScanAsync(
        string pakDirectory,
        PakScanConfig? config = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
