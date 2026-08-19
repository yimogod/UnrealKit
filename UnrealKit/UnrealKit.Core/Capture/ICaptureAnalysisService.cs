namespace UnrealKit.Core.Capture;

public interface ICaptureAnalysisService
{
    /// <summary>
    /// 列出工程 <c>Content/</c> 下的归档目录，按采集日期倒序。
    /// </summary>
    /// <param name="platform">
    /// 平台目录过滤。<c>null</c> 表示列出全部平台——不回退到某个默认平台，
    /// 那会让其他平台的归档既不显示也不报错，看起来像是从未采集过。
    /// </param>
    /// <param name="tag">标签过滤。<c>null</c> 表示不按标签过滤。</param>
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
