using UnrealKit.Core.Operations;
using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Download;

/// <summary>
/// 最小 FTP 抽象，隔离第三方 FTP 库。方法按下载流程所需的最小子集设计，
/// 业务逻辑依赖此接口，测试用假实现，不触碰真实网络。
/// </summary>
public interface IFtpClient : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FtpEntry>> ListAsync(string remotePath, CancellationToken cancellationToken = default);

    Task DownloadFileAsync(string remotePath, string localPath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);

    Task DownloadDirectoryAsync(string remotePath, string localPath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// 由配置构造 FTP 客户端。服务按操作新建客户端（连接状态不可复用），
/// 测试注入返回假客户端的工厂。
/// </summary>
public interface IFtpClientFactory
{
    IFtpClient Create(FtpDownloadSettings settings);
}
