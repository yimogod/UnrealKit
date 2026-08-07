namespace UnrealKit.Core.Capture;

public interface ICaptureAnalysisService
{
    Task<IReadOnlyList<CaptureDirectoryInfo>> ListCaptureDirectoriesAsync(
        Projects.UkitProject project,
        string? platform = null,
        string? tag = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CaptureFileInfo>> ListCaptureFilesAsync(
        string captureDirectoryPath,
        CancellationToken cancellationToken = default);

    Task<CaptureAnalysisResult> AnalyzeMemInfoAsync(
        CaptureAnalysisRequest request,
        CancellationToken cancellationToken = default);

    string ComputeAnalysisDirectory(Projects.UkitProject project, string analysisId);
}
