using FluentFTP;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Download;

/// <summary>
/// <see cref="IFtpClient"/> 的 FluentFTP 实现，把第三方库隔离在本类内。
/// 调用方（下载服务与测试）只面对 <see cref="IFtpClient"/>，不接触 FluentFTP 类型。
/// </summary>
public sealed class FluentFtpClientAdapter : IFtpClient
{
    private readonly AsyncFtpClient _client;

    public FluentFtpClientAdapter(FtpSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _client = new AsyncFtpClient(settings.Host, settings.Username, settings.Password, settings.Port);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default) =>
        await _client.Connect(cancellationToken);

    public async Task<IReadOnlyList<FtpEntry>> ListAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var items = await _client.GetListing(remotePath, cancellationToken);
        return items
            .Select(item => new FtpEntry(item.Name, item.Type == FtpObjectType.Directory))
            .ToArray();
    }

    public async Task DownloadFileAsync(string remotePath, string localPath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var status = await _client.DownloadFile(
            localPath,
            remotePath,
            FtpLocalExists.Overwrite,
            FtpVerify.None,
            WrapProgress(progress, Path.GetFileName(remotePath)),
            cancellationToken);

        if (status != FtpStatus.Success)
        {
            throw new IOException($"FTP 文件下载未成功（状态 {status}）：{remotePath}");
        }
    }

    public async Task DownloadDirectoryAsync(string remotePath, string localPath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var results = await _client.DownloadDirectory(
            localPath,
            remotePath,
            FtpFolderSyncMode.Update,
            FtpLocalExists.Overwrite,
            FtpVerify.None,
            null,
            WrapProgress(progress, Path.GetFileName(remotePath.TrimEnd('/'))),
            cancellationToken);

        if (results.Any(result => result.IsFailed))
        {
            throw new IOException($"FTP 目录下载部分失败（{results.Count(result => result.IsFailed)}/{results.Count} 项）：{remotePath}");
        }
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private static IProgress<FtpProgress>? WrapProgress(IProgress<OperationProgress>? progress, string name) =>
        progress is null
            ? null
            : new Progress<FtpProgress>(value => progress.Report(new OperationProgress(
                "ftp-download",
                "Downloading",
                null,
                null,
                $"{name}: {value.Progress:F0}%")));
}

/// <summary>
/// 由配置构造 <see cref="FluentFtpClientAdapter"/>。
/// </summary>
public sealed class FluentFtpClientFactory : IFtpClientFactory
{
    public IFtpClient Create(FtpSettings settings) => new FluentFtpClientAdapter(settings);
}
