namespace UnrealKit.Core.Export;

public interface IMemInfoExportService
{
    Task<MemInfoExportResult> ExportAsync(MemInfoExportRequest request, CancellationToken cancellationToken = default);
}
