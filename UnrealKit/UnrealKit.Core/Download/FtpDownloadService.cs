using UnrealKit.Core.Diagnostics;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Download;

/// <summary>
/// FTP 下载编排：连接 → 列出父目录子目录 → 自然排序取最新 → 按平台下载。
///
/// 配置缺失（Host / FtpPath 为空）在进入网络前抛出并指名缺哪一项；网络与内容层面的
/// 失败（连不上、目录不存在、无子目录、多个 apk 等）以 <c>DWN*</c> 诊断返回，
/// 由调用方决定呈现与退出码。
/// </summary>
public sealed class FtpDownloadService : IFtpDownloadService
{
    private readonly IFtpClientFactory _clientFactory;

    public FtpDownloadService(IFtpClientFactory clientFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    public async Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Settings.IsConfigured)
        {
            throw new InvalidOperationException(
                "FTP 下载未配置主机。请在工程配置的「FTP 下载」分组中填写 Host。");
        }

        if (string.IsNullOrWhiteSpace(request.FtpPath))
        {
            throw new InvalidOperationException(
                $"{PlatformNames.ToName(request.Platform)} 平台的 FTP 父目录未配置。" +
                $"请在工程配置的 {PlatformNames.ToName(request.Platform)} 分组中填写 FtpPath。");
        }

        var platformName = PlatformNames.ToName(request.Platform);
        var client = _clientFactory.Create(request.Settings);
        try
        {
            progress?.Report(new OperationProgress(
                "ftp-download", "Connecting", null, null,
                $"Connecting to {request.Settings.Host}:{request.Settings.Port}."));

            try
            {
                await client.ConnectAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return new DownloadResult(null, null, 0,
                    [Error(DownloadDiagnosticCodes.ConnectFailed,
                        $"无法连接 FTP 服务器 {request.Settings.Host}:{request.Settings.Port}：{exception.Message}",
                        "检查 Host / Port / 凭据是否正确，以及网络是否可达。")]);
            }

            IReadOnlyList<FtpEntry> entries;
            try
            {
                entries = await client.ListAsync(request.FtpPath, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return new DownloadResult(null, null, 0,
                    [Error(DownloadDiagnosticCodes.ListFailed,
                        $"无法列出 FTP 目录 {request.FtpPath}：{exception.Message}",
                        "检查 FtpPath 是否存在且当前用户有读权限。")]);
            }

            var subdirectories = entries
                .Where(entry => entry.IsDirectory && !string.IsNullOrWhiteSpace(entry.Name))
                .Select(entry => entry.Name)
                .OrderBy(name => name, NaturalSortComparer.Instance)
                .ToArray();

            if (subdirectories.Length == 0)
            {
                return new DownloadResult(null, null, 0,
                    [Error(DownloadDiagnosticCodes.NoSubdirectory,
                        $"FTP 路径 {request.FtpPath} 下没有可用于下载的子目录。",
                        "确认构建服务器已产出至少一个版本目录。")]);
            }

            var latest = subdirectories[^1];
            var sourcePath = CombineRemotePath(request.FtpPath, latest);
            progress?.Report(new OperationProgress(
                "ftp-download", "Downloading", null, null,
                $"Downloading '{latest}' for {platformName}."));

            return request.Platform switch
            {
                TargetPlatform.Android => await DownloadAndroidAsync(client, request, sourcePath, latest, progress, cancellationToken),
                TargetPlatform.Win64 => await DownloadWin64Async(client, request, sourcePath, latest, progress, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(request), request.Platform, "Unsupported platform.")
            };
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    private static async Task<DownloadResult> DownloadAndroidAsync(
        IFtpClient client,
        DownloadRequest request,
        string sourcePath,
        string latest,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FtpEntry> files;
        try
        {
            files = await client.ListAsync(sourcePath, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new DownloadResult(null, latest, 0,
                [Error(DownloadDiagnosticCodes.ListFailed,
                    $"无法列出 FTP 目录 {sourcePath}：{exception.Message}",
                    "检查目录是否存在且当前用户有读权限。")]);
        }

        var apks = files
            .Where(entry => !entry.IsDirectory && entry.Name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Name)
            .ToArray();

        if (apks.Length == 0)
        {
            return new DownloadResult(null, latest, 0,
                [Error(DownloadDiagnosticCodes.ApkNotFound,
                    $"目录 {sourcePath} 中没有 .apk 文件。",
                    "确认 Android 构建已产出 apk 并上传到该目录。")]);
        }

        if (apks.Length > 1)
        {
            // 多个 apk 时不做隐式选择：替用户挑一个可能装错版本。
            return new DownloadResult(null, latest, 0,
                [Error(DownloadDiagnosticCodes.AmbiguousApk,
                    $"目录 {sourcePath} 中有多个 .apk 文件，无法确定安装哪一个: {string.Join(", ", apks)}。",
                    "删除多余的 apk，或只保留目标版本。")]);
        }

        var localDirectory = Path.Combine(request.LocalBaseDirectory, latest);
        Directory.CreateDirectory(localDirectory);
        var localFile = Path.Combine(localDirectory, apks[0]);

        try
        {
            await client.DownloadFileAsync(CombineRemotePath(sourcePath, apks[0]), localFile, progress, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new DownloadResult(null, latest, 0,
                [Error(DownloadDiagnosticCodes.DownloadFailed,
                    $"下载 {apks[0]} 失败：{exception.Message}")]);
        }

        return new DownloadResult(localFile, latest, 1, []);
    }

    private static async Task<DownloadResult> DownloadWin64Async(
        IFtpClient client,
        DownloadRequest request,
        string sourcePath,
        string latest,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var localDirectory = Path.Combine(request.LocalBaseDirectory, latest);
        Directory.CreateDirectory(localDirectory);

        try
        {
            await client.DownloadDirectoryAsync(sourcePath, localDirectory, progress, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new DownloadResult(null, latest, 0,
                [Error(DownloadDiagnosticCodes.DownloadFailed,
                    $"下载目录 {sourcePath} 失败：{exception.Message}")]);
        }

        var fileCount = CountFiles(localDirectory);
        return new DownloadResult(localDirectory, latest, fileCount, []);
    }

    private static string CombineRemotePath(string basePath, string name) =>
        $"{basePath.TrimEnd('/')}/{name}";

    private static int CountFiles(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Count()
            : 0;

    private static Diagnostic Error(string code, string message, string? suggestedFix = null) =>
        new(DiagnosticSeverity.Error, code, message, SuggestedFix: suggestedFix);
}
