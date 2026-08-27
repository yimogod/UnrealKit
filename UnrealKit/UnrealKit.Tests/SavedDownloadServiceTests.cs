using UnrealKit.Core.Capture;
using UnrealKit.Core.Devices;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;
using UnrealKit.Core.Unreal;

namespace UnrealKit.Tests;

public sealed class UnrealSavedServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(), "UnrealKit.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DownloadAsync_CopiesDeviceSavedIntoProjectSavedDirectory()
    {
        var (project, deviceSaved) = await CreateWin64ProjectAsync("Download");
        await File.WriteAllTextAsync(Path.Combine(deviceSaved, "Config", "GameUserSettings.ini"), "[/Script/Engine]");
        await File.WriteAllTextAsync(Path.Combine(deviceSaved, "Logs", "Game.log"), "log line");

        var service = new UnrealSavedService(new Win64DeviceService());

        var result = await service.DownloadAsync(new UnrealSavedPullRequest(project, new Win64Device()));

        // 落地在工程 Saved/DeviceSaved 下，而不是 Content——下载没有清单，不是采集归档。
        Assert.StartsWith(
            Path.Combine(project.SavedDir, UnrealSavedService.DownloadRootName, "Win64"),
            result.Plan.LocalDirectory,
            StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(result.Plan.LocalDirectory, "Config", "GameUserSettings.ini")));
        Assert.Equal("log line", await File.ReadAllTextAsync(Path.Combine(result.Plan.LocalDirectory, "Logs", "Game.log")));
        Assert.Equal(2, result.FileCount);
        Assert.True(result.TotalBytes > 0);

        // 原始数据只读：设备端目录不得被移动或清空。
        Assert.True(File.Exists(Path.Combine(deviceSaved, "Logs", "Game.log")));

        // 暂存目录用完即清，不在 Intermediate 下留半成品。
        var stagingRoot = Path.Combine(project.IntermediateDir, "SavedDownloadStaging");
        Assert.True(!Directory.Exists(stagingRoot) || !Directory.EnumerateFileSystemEntries(stagingRoot).Any());
    }

    [Fact]
    public async Task DownloadAsync_SecondDownload_DoesNotOverwriteTheFirst()
    {
        var (project, deviceSaved) = await CreateWin64ProjectAsync("Twice");
        await File.WriteAllTextAsync(Path.Combine(deviceSaved, "Logs", "Game.log"), "first");

        // 目录名的时间戳分辨率是秒，因此显式给出两个不同时间，验证两次下载互不覆盖，
        // 而不是依赖测试执行恰好跨过一秒边界。
        var first = await new UnrealSavedService(new Win64DeviceService(), FixedTime("2026-08-24T10:00:00+08:00"))
            .DownloadAsync(new UnrealSavedPullRequest(project, new Win64Device()));

        await File.WriteAllTextAsync(Path.Combine(deviceSaved, "Logs", "Game.log"), "second");
        var second = await new UnrealSavedService(new Win64DeviceService(), FixedTime("2026-08-24T10:00:05+08:00"))
            .DownloadAsync(new UnrealSavedPullRequest(project, new Win64Device()));

        Assert.NotEqual(first.Plan.LocalDirectory, second.Plan.LocalDirectory);
        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(first.Plan.LocalDirectory, "Logs", "Game.log")));
        Assert.Equal("second", await File.ReadAllTextAsync(Path.Combine(second.Plan.LocalDirectory, "Logs", "Game.log")));
    }

    [Fact]
    public async Task DownloadAsync_ExistingTargetDirectory_ThrowsInsteadOfOverwriting()
    {
        var (project, deviceSaved) = await CreateWin64ProjectAsync("Existing");
        await File.WriteAllTextAsync(Path.Combine(deviceSaved, "Logs", "Game.log"), "log");

        var service = new UnrealSavedService(new Win64DeviceService(), FixedTime("2026-08-24T10:00:00+08:00"));
        var plan = service.CreatePlan(new UnrealSavedPullRequest(project, new Win64Device()));
        Directory.CreateDirectory(plan.LocalDirectory);
        await File.WriteAllTextAsync(Path.Combine(plan.LocalDirectory, "Existing.txt"), "keep me");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadAsync(new UnrealSavedPullRequest(project, new Win64Device())));

        Assert.Contains(plan.LocalDirectory, exception.Message, StringComparison.Ordinal);
        Assert.Equal("keep me", await File.ReadAllTextAsync(Path.Combine(plan.LocalDirectory, "Existing.txt")));
    }

    [Fact]
    public async Task DownloadAsync_PullProducesNothing_ThrowsInsteadOfReportingEmptySuccess()
    {
        var (project, _) = await CreateWin64ProjectAsync("Empty");
        var service = new UnrealSavedService(new NoOpPullDeviceService());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadAsync(new UnrealSavedPullRequest(project, new Win64Device())));

        Assert.Contains("没有取回任何内容", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(project.SavedDir, UnrealSavedService.DownloadRootName)));
    }

    [Fact]
    public async Task DownloadAsync_PullDirectoryUnsupported_ThrowsCapabilityException()
    {
        var (project, _) = await CreateWin64ProjectAsync("Unsupported");
        var service = new UnrealSavedService(new NoPullCapabilityDeviceService());

        var exception = await Assert.ThrowsAsync<DeviceCapabilityNotSupportedException>(
            () => service.DownloadAsync(new UnrealSavedPullRequest(project, new Win64Device())));

        Assert.Equal(DeviceCapability.PullDirectory, exception.Capability);
    }

    [Fact]
    public async Task CreatePlan_WirelessDeviceId_ProducesValidSingleDirectoryName()
    {
        var (project, _) = await CreateWin64ProjectAsync("Wireless");
        var service = new UnrealSavedService(new Win64DeviceService(), FixedTime("2026-08-24T10:00:00+08:00"));

        var plan = service.CreatePlan(new UnrealSavedPullRequest(project, new StubDevice("192.168.1.100:5555", "Win64")));

        // Wi-Fi 设备 id 含 ':'，在 Windows 上不能出现在目录名里。
        var folderName = Path.GetFileName(plan.LocalDirectory);
        Assert.Equal("Saved-20260824-100000-192.168.1.100-5555", folderName);
        Assert.True(folderName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }

    [Fact]
    public async Task DownloadAsync_LogsScope_CopiesOnlyTheLogsSubdirectory()
    {
        var (project, deviceSaved) = await CreateWin64ProjectAsync("LogsOnly");
        await File.WriteAllTextAsync(Path.Combine(deviceSaved, "Logs", "Game.log"), "log line");
        await File.WriteAllTextAsync(Path.Combine(deviceSaved, "Logs", "Game-backup.log"), "older log");
        // Logs 之外的内容必须留在设备上不被取回，否则「只下载日志」就名不副实。
        await File.WriteAllTextAsync(Path.Combine(deviceSaved, "Config", "GameUserSettings.ini"), "[/Script/Engine]");

        var service = new UnrealSavedService(new Win64DeviceService());

        var result = await service.DownloadAsync(
            new UnrealSavedPullRequest(project, new Win64Device(), UnealSavedScope.Logs));

        Assert.Equal(UnealSavedScope.Logs, result.Plan.Scope);
        Assert.Equal(Path.Combine(deviceSaved, "Logs"), result.Plan.DeviceDirectory);
        Assert.StartsWith("Logs-", Path.GetFileName(result.Plan.LocalDirectory), StringComparison.Ordinal);

        // 落地目录直接就是 Logs 的内容，不多包一层 Logs 子目录。
        Assert.Equal("log line", await File.ReadAllTextAsync(Path.Combine(result.Plan.LocalDirectory, "Game.log")));
        Assert.Equal(2, result.FileCount);
        Assert.False(Directory.Exists(Path.Combine(result.Plan.LocalDirectory, "Config")));
        Assert.False(File.Exists(Path.Combine(result.Plan.LocalDirectory, "GameUserSettings.ini")));
    }

    [Fact]
    public async Task DownloadAsync_LogsAndAllScopes_LandInSeparateDirectories()
    {
        var (project, deviceSaved) = await CreateWin64ProjectAsync("BothScopes");
        await File.WriteAllTextAsync(Path.Combine(deviceSaved, "Logs", "Game.log"), "log line");

        // 同一时刻分别取 Saved 与 Logs：目录名的范围前缀是两者不撞名的唯一依据。
        var timeProvider = FixedTime("2026-08-24T10:00:00+08:00");
        var all = await new UnrealSavedService(new Win64DeviceService(), timeProvider)
            .DownloadAsync(new UnrealSavedPullRequest(project, new Win64Device(), UnealSavedScope.All));
        var logs = await new UnrealSavedService(new Win64DeviceService(), timeProvider)
            .DownloadAsync(new UnrealSavedPullRequest(project, new Win64Device(), UnealSavedScope.Logs));

        Assert.NotEqual(all.Plan.LocalDirectory, logs.Plan.LocalDirectory);
        Assert.True(File.Exists(Path.Combine(all.Plan.LocalDirectory, "Logs", "Game.log")));
        Assert.True(File.Exists(Path.Combine(logs.Plan.LocalDirectory, "Game.log")));
    }

    [Fact]
    public void CreatePlan_AndroidLogsScope_UsesForwardSlashDevicePath()
    {
        // Android 的设备端路径必须用 '/' 拼接：在 Windows 主机上用 Path.Combine
        // 会写出反斜杠，adb 会把它当成路径的一部分而找不到目录。
        var settings = ProjectSettings.CreateDefaults("Sample") with
        {
            Android = AndroidPlatformProfile.CreateDefaults() with { PackageName = "com.example.game" }
        };
        var project = new UkitProject(
            Path.Combine(_temporaryDirectory, "Android", "Sample.ukit"),
            Path.Combine(_temporaryDirectory, "Android"),
            UkitProjectDescriptor.CreateDefault("Sample"),
            settings);
        var service = new UnrealSavedService(new StubAndroidDeviceService(), FixedTime("2026-08-24T10:00:00+08:00"));
        var device = new StubDevice("R58M123ABC", "Android");

        var savedPath = service.CreatePlan(new UnrealSavedPullRequest(project, device)).DeviceDirectory;
        var logsPath = service.CreatePlan(new UnrealSavedPullRequest(project, device, UnealSavedScope.Logs)).DeviceDirectory;

        Assert.Equal($"{savedPath}/Logs", logsPath);
        Assert.DoesNotContain('\\', logsPath);
    }

    [Fact]
    public async Task DownloadAsync_CommonScope_CopiesOnlyTheCommonSubdirectories()
    {
        var (project, deviceSaved) = await CreateWin64ProjectAsync("Common");
        await File.WriteAllTextAsync(Path.Combine(deviceSaved, "Logs", "Game.log"), "log line");
        Directory.CreateDirectory(Path.Combine(deviceSaved, "Screenshots"));
        await File.WriteAllTextAsync(Path.Combine(deviceSaved, "Screenshots", "Shot.png"), "png");
        Directory.CreateDirectory(Path.Combine(deviceSaved, "Profiling"));
        await File.WriteAllTextAsync(Path.Combine(deviceSaved, "Profiling", "profile.csv"), "csv");
        Directory.CreateDirectory(Path.Combine(deviceSaved, "GPUDumps"));
        await File.WriteAllTextAsync(Path.Combine(deviceSaved, "GPUDumps", "dump.bin"), "bin");
        // 常用子目录之外的内容（Config）必须留在设备上不被取回。
        await File.WriteAllTextAsync(Path.Combine(deviceSaved, "Config", "GameUserSettings.ini"), "[/Script/Engine]");

        var service = new UnrealSavedService(new Win64DeviceService());

        var result = await service.DownloadAsync(
            new UnrealSavedPullRequest(project, new Win64Device(), UnealSavedScope.Common));

        Assert.Equal(UnealSavedScope.Common, result.Plan.Scope);
        Assert.StartsWith("Common-", Path.GetFileName(result.Plan.LocalDirectory), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(result.Plan.LocalDirectory, "Logs", "Game.log")));
        Assert.True(File.Exists(Path.Combine(result.Plan.LocalDirectory, "Screenshots", "Shot.png")));
        Assert.True(File.Exists(Path.Combine(result.Plan.LocalDirectory, "Profiling", "profile.csv")));
        Assert.True(File.Exists(Path.Combine(result.Plan.LocalDirectory, "GPUDumps", "dump.bin")));
        Assert.Equal(4, result.FileCount);
        Assert.False(Directory.Exists(Path.Combine(result.Plan.LocalDirectory, "Config")));
    }

    [Fact]
    public async Task DownloadAsync_CommonScope_SkipsMissingSubdirectories()
    {
        var (project, deviceSaved) = await CreateWin64ProjectAsync("CommonPartial");
        // 只生成其中两个子目录：GPUDumps、Screenshots 尚不存在是常态，不能因此失败。
        await File.WriteAllTextAsync(Path.Combine(deviceSaved, "Logs", "Game.log"), "log line");
        Directory.CreateDirectory(Path.Combine(deviceSaved, "Profiling"));
        await File.WriteAllTextAsync(Path.Combine(deviceSaved, "Profiling", "profile.csv"), "csv");

        var service = new UnrealSavedService(new Win64DeviceService());

        var result = await service.DownloadAsync(
            new UnrealSavedPullRequest(project, new Win64Device(), UnealSavedScope.Common));

        Assert.True(File.Exists(Path.Combine(result.Plan.LocalDirectory, "Logs", "Game.log")));
        Assert.True(File.Exists(Path.Combine(result.Plan.LocalDirectory, "Profiling", "profile.csv")));
        Assert.False(Directory.Exists(Path.Combine(result.Plan.LocalDirectory, "Screenshots")));
        Assert.False(Directory.Exists(Path.Combine(result.Plan.LocalDirectory, "GPUDumps")));
        Assert.Equal(2, result.FileCount);
    }

    [Fact]
    public async Task DownloadAsync_CommonScope_AllSubdirectoriesMissing_ThrowsInsteadOfEmptySuccess()
    {
        var (project, deviceSaved) = await CreateWin64ProjectAsync("CommonNone");
        // 设备 Saved 下只有 Config，没有任何常用子目录：应复用「没有取回任何内容」契约。
        Directory.Delete(Path.Combine(deviceSaved, "Logs"));
        await File.WriteAllTextAsync(Path.Combine(deviceSaved, "Config", "GameUserSettings.ini"), "[/Script/Engine]");

        var service = new UnrealSavedService(new Win64DeviceService());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadAsync(
                new UnrealSavedPullRequest(project, new Win64Device(), UnealSavedScope.Common)));

        Assert.Contains("没有取回任何内容", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadAsync_UnavailableDevice_ThrowsWithoutWritingAnything()
    {
        var (project, _) = await CreateWin64ProjectAsync("Unavailable");
        var service = new UnrealSavedService(new Win64DeviceService());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadAsync(new UnrealSavedPullRequest(
                project, new StubDevice("localhost", "Win64", IsAvailable: false))));

        Assert.False(Directory.Exists(Path.Combine(project.SavedDir, UnrealSavedService.DownloadRootName)));
    }

    /// <summary>
    /// 建一个配好 Win64 平台的工程，并按该平台落地值创建设备端 Saved 目录。
    /// 选 Win64 而非 Android：Win64 的「拉取」就是本机目录复制，测试因此不需要 adb。
    /// </summary>
    private async Task<(UkitProject Project, string DeviceSaved)> CreateWin64ProjectAsync(string name)
    {
        var created = await new ProjectService().CreateProjectAsync(
            new CreateProjectRequest(Path.Combine(_temporaryDirectory, name), name));

        var gameDirectory = Path.Combine(_temporaryDirectory, name + "Game");
        var executable = Path.Combine(gameDirectory, name + ".exe");
        Directory.CreateDirectory(gameDirectory);
        await File.WriteAllTextAsync(executable, string.Empty);

        var settings = created.Project.Settings with
        {
            Win64 = Win64PlatformProfile.CreateDefaults() with
            {
                Executable = executable,
                WorkingDirectory = gameDirectory
            }
        };
        var project = created.Project with { Settings = settings };

        var deviceSaved = project.Settings.ResolveTarget(TargetPlatform.Win64).SavedRootPath;
        Directory.CreateDirectory(Path.Combine(deviceSaved, "Config"));
        Directory.CreateDirectory(Path.Combine(deviceSaved, "Logs"));
        return (project, deviceSaved);
    }

    private static TimeProvider FixedTime(string timestamp) =>
        new FixedTimeProvider(DateTimeOffset.Parse(timestamp, System.Globalization.CultureInfo.InvariantCulture));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone(
            "UnrealKit.Tests.Fixed", now.Offset, "UnrealKit.Tests.Fixed", "UnrealKit.Tests.Fixed");
    }

    private sealed record StubDevice(string Id, string Platform, bool IsAvailable = true) : IDevice
    {
        public string Name => Id;
    }

    /// <summary>拉取报告成功却什么也没写下来的设备，用于验证「空结果不当成功」。</summary>
    private sealed class NoOpPullDeviceService : StubDeviceService
    {
        public override Task<ProcessExecutionResult> PullDirectoryAsync(
            IDevice device, string remotePath, string localDirectory,
            IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessExecutionResult(
                0, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    private sealed class NoPullCapabilityDeviceService : StubDeviceService
    {
        public override bool Supports(DeviceCapability capability) => capability != DeviceCapability.PullDirectory;
    }

    /// <summary>
    /// 只用于计划阶段的 Android 设备服务：验证设备端路径拼接风格不需要真的调用 adb。
    /// </summary>
    private sealed class StubAndroidDeviceService : StubDeviceService
    {
        public override TargetPlatform Platform => TargetPlatform.Android;
    }

    private abstract class StubDeviceService : IDeviceService
    {
        private static ProcessExecutionResult Success =>
            new(0, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        public virtual TargetPlatform Platform => TargetPlatform.Win64;
        public virtual bool Supports(DeviceCapability capability) => true;
        public Task<IReadOnlyList<IDevice>> ListDevicesAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<IDevice>>([new Win64Device()]);
        public Task<ProcessExecutionResult> CaptureMemoryAsync(IDevice device, string target, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public virtual Task<ProcessExecutionResult> PullDirectoryAsync(IDevice device, string remotePath, string localDirectory, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public virtual Task<ProcessExecutionResult> PullSubdirectoriesAsync(IDevice device, string remoteDirectory, IReadOnlyList<string> subdirectoryNames, string localDirectory, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> SendConsoleCommandAsync(IDevice device, string command, string? target = null, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public async IAsyncEnumerable<string> StreamLogAsync(IDevice device, string? filter = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { await Task.CompletedTask; yield break; }
        public Task<ProcessExecutionResult> StartApplicationAsync(IDevice device, string target, string? activity = null, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> StopApplicationAsync(IDevice device, string target, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> PushFileAsync(IDevice device, string localPath, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> DeleteRemoteFileAsync(IDevice device, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> ReadFileAsync(IDevice device, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> InstallApplicationAsync(IDevice device, string localApplicationPath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, recursive: true);
    }
}
