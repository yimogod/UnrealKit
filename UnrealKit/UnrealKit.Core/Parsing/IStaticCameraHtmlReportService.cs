namespace UnrealKit.Core.Parsing;

public interface IStaticCameraHtmlReportService
{
    Task<StaticCameraHtmlReportResult> GenerateAsync(
        StaticCameraHtmlReportRequest request,
        CancellationToken cancellationToken = default);
}
