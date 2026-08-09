namespace UnrealKit.Core.Analysis;

public interface IBaselineService
{
    /// <summary>Reads one report into a comparable metric snapshot without modifying the input file.</summary>
    Task<MetricSnapshot> LoadSnapshotAsync(
        BaselineDiffSource source,
        string inputFilePath,
        string? label = null,
        CancellationToken cancellationToken = default);

    /// <summary>Compares a current report against a baseline report of the same type.</summary>
    Task<BaselineDiffResult> DiffAsync(
        BaselineDiffRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Compares two already-loaded snapshots. Both snapshots must share the same source type.</summary>
    BaselineDiffResult Diff(
        MetricSnapshot baseline,
        MetricSnapshot current,
        IReadOnlyList<string>? metricFilter = null);
}
