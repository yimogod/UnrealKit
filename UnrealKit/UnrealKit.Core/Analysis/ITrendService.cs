namespace UnrealKit.Core.Analysis;

public interface ITrendService
{
    /// <summary>
    /// Aggregates one metric series per metric across the captures matching the request,
    /// ordered oldest to newest. Capture archives are only read, never modified.
    /// </summary>
    Task<TrendResult> BuildTrendAsync(
        TrendRequest request,
        CancellationToken cancellationToken = default);
}
