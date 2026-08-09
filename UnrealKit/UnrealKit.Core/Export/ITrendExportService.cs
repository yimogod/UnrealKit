namespace UnrealKit.Core.Export;

public interface ITrendExportService
{
    Task<TrendExportResult> ExportAsync(TrendExportRequest request, CancellationToken cancellationToken = default);
}

public interface IXlsxTrendExportService
{
    Task<TrendExportResult> ExportAsync(TrendExportRequest request, CancellationToken cancellationToken = default);
}
