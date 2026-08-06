using UnrealKit.Core.Operations;

namespace UnrealKit.Core.Capture;

public interface ICaptureService
{
    CapturePlan CreatePlan(CaptureRequest request, DateTimeOffset? capturedAt = null);

    Task<CaptureResult> CaptureAsync(
        CaptureRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
