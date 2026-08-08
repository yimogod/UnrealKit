namespace UnrealKit.Core.Export;

public interface IMemReportExportService
{
    Task<MemReportExportResult> ExportAsync(MemReportExportRequest request, CancellationToken cancellationToken = default);
}