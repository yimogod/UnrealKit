using UnrealKit.Core.Diagnostics;
using UnrealKit.Core.Download;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Projects;

namespace UnrealKit.Tests;

public sealed class DownloadServiceTests
{
    private static FtpDownloadSettings ConfiguredSettings => new("ftp.example.com", 21, "user", "pass");

    private static string NewLocalBaseDirectory() =>
        Path.Combine(Path.GetTempPath(), "UnrealKit.Tests", "Download", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DownloadAsync_UnconfiguredHost_ThrowsNamingField()
    {
        var service = new FtpDownloadService(new FakeFtpClientFactory());
        var request = new DownloadRequest(
            TargetPlatform.Android,
            FtpDownloadSettings.CreateDefaults(),
            "/builds/android",
            NewLocalBaseDirectory());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadAsync(request));

        Assert.Contains("Host", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadAsync_EmptyFtpPath_ThrowsNamingPlatformAndField()
    {
        var service = new FtpDownloadService(new FakeFtpClientFactory());
        var request = new DownloadRequest(
            TargetPlatform.Android,
            ConfiguredSettings,
            string.Empty,
            NewLocalBaseDirectory());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadAsync(request));

        Assert.Contains("Android", exception.Message, StringComparison.Ordinal);
        Assert.Contains("FtpPath", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadAsync_NoSubdirectories_ReturnsDwn001()
    {
        var service = new FtpDownloadService(new FakeFtpClientFactory());
        var request = new DownloadRequest(TargetPlatform.Android, ConfiguredSettings, "/builds/android", NewLocalBaseDirectory());

        var result = await service.DownloadAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal(DownloadDiagnosticCodes.NoSubdirectory, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task DownloadAsync_Android_PicksLatestByNaturalSort()
    {
        var factory = new FakeFtpClientFactory();
        factory.Client.ListResults["/builds/android"] =
        [
            new("v1.0.9", true),
            new("v1.0.10", true),
            new("v1.0.2", true)
        ];
        factory.Client.ListResults["/builds/android/v1.0.10"] = [new("Game.apk", false)];
        var service = new FtpDownloadService(factory);
        var request = new DownloadRequest(TargetPlatform.Android, ConfiguredSettings, "/builds/android", NewLocalBaseDirectory());

        var result = await service.DownloadAsync(request);

        Assert.True(result.Succeeded);
        Assert.Equal("v1.0.10", result.SourceSubdir);
        Assert.Equal(1, result.FileCount);
        Assert.EndsWith(Path.Combine("v1.0.10", "Game.apk"), result.LocalPath);
        Assert.Equal("/builds/android/v1.0.10/Game.apk", factory.Client.DownloadedFiles[0].RemotePath);
    }

    [Fact]
    public async Task DownloadAsync_Android_MultipleApks_ReturnsDwn004()
    {
        var factory = new FakeFtpClientFactory();
        factory.Client.ListResults["/builds/android"] = [new("v2", true)];
        factory.Client.ListResults["/builds/android/v2"] =
        [
            new("Game-arm64.apk", false),
            new("Game-arm32.apk", false)
        ];
        var service = new FtpDownloadService(factory);
        var request = new DownloadRequest(TargetPlatform.Android, ConfiguredSettings, "/builds/android", NewLocalBaseDirectory());

        var result = await service.DownloadAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal(DownloadDiagnosticCodes.AmbiguousApk, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task DownloadAsync_Android_NoApk_ReturnsDwn005()
    {
        var factory = new FakeFtpClientFactory();
        factory.Client.ListResults["/builds/android"] = [new("v2", true)];
        factory.Client.ListResults["/builds/android/v2"] = [new("notes.txt", false)];
        var service = new FtpDownloadService(factory);
        var request = new DownloadRequest(TargetPlatform.Android, ConfiguredSettings, "/builds/android", NewLocalBaseDirectory());

        var result = await service.DownloadAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal(DownloadDiagnosticCodes.ApkNotFound, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task DownloadAsync_Win64_DownloadsWholeSubdirectory()
    {
        var factory = new FakeFtpClientFactory();
        factory.Client.ListResults["/builds/win64"] = [new("2024.01.01", true), new("2024.01.05", true)];
        var service = new FtpDownloadService(factory);
        var request = new DownloadRequest(TargetPlatform.Win64, ConfiguredSettings, "/builds/win64", NewLocalBaseDirectory());

        var result = await service.DownloadAsync(request);

        Assert.True(result.Succeeded);
        Assert.Equal("2024.01.05", result.SourceSubdir);
        Assert.Equal(1, result.FileCount);
        Assert.Equal("/builds/win64/2024.01.05", factory.Client.DownloadedDirectories[0].RemotePath);
        Assert.EndsWith("2024.01.05", result.LocalPath);
    }

    [Fact]
    public async Task DownloadAsync_ConnectFailure_ReturnsDwn002()
    {
        var factory = new FakeFtpClientFactory();
        factory.Client.FailConnect = true;
        var service = new FtpDownloadService(factory);
        var request = new DownloadRequest(TargetPlatform.Android, ConfiguredSettings, "/builds/android", NewLocalBaseDirectory());

        var result = await service.DownloadAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal(DownloadDiagnosticCodes.ConnectFailed, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task DownloadAsync_ListFailure_ReturnsDwn003()
    {
        var factory = new FakeFtpClientFactory();
        factory.Client.FailList = true;
        var service = new FtpDownloadService(factory);
        var request = new DownloadRequest(TargetPlatform.Android, ConfiguredSettings, "/builds/android", NewLocalBaseDirectory());

        var result = await service.DownloadAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal(DownloadDiagnosticCodes.ListFailed, Assert.Single(result.Diagnostics).Code);
    }

    private sealed class FakeFtpClientFactory : IFtpClientFactory
    {
        public FakeFtpClient Client { get; } = new();
        public IFtpClient Create(FtpDownloadSettings settings) => Client;
    }

    private sealed class FakeFtpClient : IFtpClient
    {
        public bool FailConnect { get; set; }
        public bool FailList { get; set; }
        public Dictionary<string, List<FtpEntry>> ListResults { get; } = new(StringComparer.Ordinal);
        public List<(string RemotePath, string LocalPath)> DownloadedFiles { get; } = [];
        public List<(string RemotePath, string LocalPath)> DownloadedDirectories { get; } = [];

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (FailConnect)
            {
                throw new IOException("connection refused");
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FtpEntry>> ListAsync(string remotePath, CancellationToken cancellationToken = default)
        {
            if (FailList)
            {
                throw new IOException("permission denied");
            }

            return Task.FromResult<IReadOnlyList<FtpEntry>>(
                ListResults.TryGetValue(remotePath, out var entries) ? entries : []);
        }

        public Task DownloadFileAsync(string remotePath, string localPath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            DownloadedFiles.Add((remotePath, localPath));
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            File.WriteAllText(localPath, "apk");
            return Task.CompletedTask;
        }

        public Task DownloadDirectoryAsync(string remotePath, string localPath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            DownloadedDirectories.Add((remotePath, localPath));
            Directory.CreateDirectory(localPath);
            File.WriteAllText(Path.Combine(localPath, "Game.exe"), "exe");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
