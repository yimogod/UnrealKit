using UnrealKit.Core.Operations;

namespace UnrealKit.Core.Download;

/// <summary>
/// 从 FTP 下载构建产物。Android 下载最新子目录中的 .apk，Win64 下载整个最新子目录。
/// 平台父目录、主机 / 端口 / 凭据均来自工程配置，见
/// <see cref="UnrealKit.Core.Projects.FtpSettings"/> 与各 profile 的 <c>FtpPath</c>。
/// </summary>
public interface IFtpDownloadService
{
    Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
