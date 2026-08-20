using UnrealKit.Core.Adb;
using UnrealKit.Core.Console;
using UnrealKit.Core.Devices;
using UnrealKit.Core.Capture;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;

namespace UnrealKit.Tests;

public sealed class CaptureServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "UnrealKit.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CaptureAsync_ArchivesOriginalDataAndWritesManifest()
    {
        var project = await new ProjectService().CreateProjectAsync(new CreateProjectRequest(Path.Combine(_temporaryDirectory, "Project"), "Project"));
        var configPath = project.Project.ConfigFilePath;
        await File.WriteAllTextAsync(configPath, (await File.ReadAllTextAsync(configPath)).Replace("PackageName=", "PackageName=com.example.project", StringComparison.Ordinal));
        var configuredProject = await new ProjectService().OpenProjectAsync(project.Project.ProjectFilePath);
        var service = new CaptureService(new AdbDeviceService(new FakeAdbService()));
        var device = new AdbDevice("device-01", AdbDeviceStatus.Device, null, "Pixel", null, AdbConnectionType.Usb, string.Empty);

        var result = await service.CaptureAsync(new CaptureRequest(configuredProject, device, "Nightly", "capture-001"));

        var memInfoFile = Assert.Single(result.Manifest.InputFiles, file => file.RelativePath.StartsWith("MemInfo/", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(result.Plan.CaptureDirectory, memInfoFile.RelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.True(File.Exists(Path.Combine(result.Plan.CaptureDirectory, "Saved", "Saved.txt")));
        Assert.True(File.Exists(result.ManifestPath));
        Assert.Equal("Nightly", result.Manifest.Tag);
        Assert.All(result.Manifest.InputFiles, file => Assert.NotEmpty(file.Sha256));
    }

    [Fact]
    public async Task CaptureAsync_PreSequenceFailure_ThrowsInvalidOperationException()
    {
        var project = await new ProjectService().CreateProjectAsync(new CreateProjectRequest(Path.Combine(_temporaryDirectory, "PreSeqProject"), "PreSeqProject"));

        var settings = project.Project.Settings;
        var updatedSettings = settings with
        {
            Android = AndroidPlatformProfile.CreateDefaults() with { PackageName = "com.example.preseq" },
            ConsoleSequences = [ConsoleSequencePreset.Create("MyPreSeq", "stat fps")],
            PreCaptureSequence = "MyPreSeq"
        };
        var configuredProject = new UkitProject(project.Project.ProjectFilePath, project.Project.RootDirectory, project.Project.Descriptor, updatedSettings);

        var failedSeq = new CommandSequenceDefinition("MyPreSeq", null, [new SequenceStep(SequenceStepType.Tag, Marker: "fail")]);
        var failedResult = new SequenceExecutionResult(
            failedSeq,
            [new SequenceStepResult(0, Error: "Command failed with exit code 1")],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var fakeConsoleService = new FailingConsoleService(failedResult);
        var service = new CaptureService(new AdbDeviceService(new FakeAdbService()), fakeConsoleService);
        var device = new AdbDevice("device-01", AdbDeviceStatus.Device, null, "Pixel", null, AdbConnectionType.Usb, string.Empty);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CaptureAsync(new CaptureRequest(configuredProject, device, "Nightly")));

        Assert.Contains("Pre-capture sequence 'MyPreSeq' failed", ex.Message);
        Assert.Contains("Command failed with exit code 1", ex.Message);
    }

    [Fact]
    public async Task CaptureAsync_PostSequenceFailure_CompletesWithWarning()
    {
        var project = await new ProjectService().CreateProjectAsync(new CreateProjectRequest(Path.Combine(_temporaryDirectory, "PostSeqProject"), "PostSeqProject"));

        var settings = project.Project.Settings;
        var updatedSettings = settings with
        {
            Android = AndroidPlatformProfile.CreateDefaults() with { PackageName = "com.example.postseq" },
            ConsoleSequences = [ConsoleSequencePreset.Create("MyPostSeq", "stat fps")],
            PostCaptureSequence = "MyPostSeq"
        };
        var configuredProject = new UkitProject(project.Project.ProjectFilePath, project.Project.RootDirectory, project.Project.Descriptor, updatedSettings);

        var failedSeq = new CommandSequenceDefinition("MyPostSeq", null, [new SequenceStep(SequenceStepType.Command)]);
        var failedResult = new SequenceExecutionResult(
            failedSeq,
            [new SequenceStepResult(0, Error: "Command timed out")],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var fakeConsoleService = new FailingConsoleService(failedResult);
        var service = new CaptureService(new AdbDeviceService(new FakeAdbService()), fakeConsoleService);
        var device = new AdbDevice("device-01", AdbDeviceStatus.Device, null, "Pixel", null, AdbConnectionType.Usb, string.Empty);

        // 不能用 Progress<T>：它把回调 post 到线程池异步执行，断言可能早于回调到达，
        // 且从池线程写 List<T> 并不安全。满负载跑整个测试集时会随机漏掉消息。
        var progressMessages = new System.Collections.Concurrent.ConcurrentQueue<OperationProgress>();
        var progress = new SynchronousProgress<OperationProgress>(progressMessages.Enqueue);

        // Should complete without throwing — post-sequence failure is a warning, not an error.
        var result = await service.CaptureAsync(new CaptureRequest(configuredProject, device, "Nightly"), progress);

        Assert.NotNull(result);
        Assert.True(File.Exists(result.ManifestPath));
        var warning = Assert.Single(progressMessages, p => p.Stage == "PostSequence" && p.Message.Contains("had 1 failed step"));
        Assert.Contains("MyPostSeq", warning.Message);
        Assert.Contains("had 1 failed step", warning.Message);
    }

    /// <summary>
    /// 同步转发的 IProgress，回调在报告线程上立即执行，断言不会与回调竞争。
    /// </summary>
    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class FailingConsoleService : IConsoleCommandService
    {
        private readonly SequenceExecutionResult _resultToReturn;

        public FailingConsoleService(SequenceExecutionResult resultToReturn)
        {
            _resultToReturn = resultToReturn;
        }

        public Task<ConsoleCommandResult> SendAsync(string serialNumber, ConsoleCommand command, string? packageName = null, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ConsoleCommandResult(command, 1, string.Empty, "error", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        public Task<SequenceExecutionResult> RunSequenceAsync(SequenceExecutionRequest request, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_resultToReturn);

        public Task<LogcatConditionResult> RunConditionalAsync(string serialNumber, LogcatConditionStep condition, string? packageName = null, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(LogcatConditionResult.Timeout(condition));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, recursive: true);
    }

    private sealed class FakeAdbService : IAdbService
    {
        public Task<ProcessExecutionResult> RunDumpsysAsync(string serialNumber, string packageName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Result("memory report"));
        public Task<ProcessExecutionResult> PullDirectoryAsync(string serialNumber, string remotePath, string localDirectory, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) { Directory.CreateDirectory(localDirectory); File.WriteAllText(Path.Combine(localDirectory, "Saved.txt"), "saved"); return Task.FromResult(Result()); }
        private static ProcessExecutionResult Result(string output = "") => new(0, output, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        public Task<ProcessExecutionResult> GetVersionAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Result());
        public Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AdbDevice>>([]);
        public Task<ProcessExecutionResult> StartServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Result());
        public Task<ProcessExecutionResult> KillServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Result());
        public Task<ProcessExecutionResult> ConnectAsync(string endpoint, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Result());
        public Task<ProcessExecutionResult> DisconnectAsync(string endpoint, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Result());
        public Task<ProcessExecutionResult> TcpIpAsync(string serialNumber, int port, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Result());
        public Task<ProcessExecutionResult> StartApplicationAsync(string serialNumber, string packageName, string activityName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Result());
        public Task<ProcessExecutionResult> PushFileAsync(string serialNumber, string localPath, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Result());
        public Task<ProcessExecutionResult> DeleteRemoteFileAsync(string serialNumber, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Result());
        public Task<ProcessExecutionResult> ForceStopApplicationAsync(string serialNumber, string packageName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Result(""));
        public Task<ProcessExecutionResult> ForwardTcpAsync(string serialNumber, int hostPort, int devicePort, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Result());
        public Task<ProcessExecutionResult> InstallApkAsync(string serialNumber, string localApkPath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Result());
        public async IAsyncEnumerable<string> StreamLogcatAsync(string serialNumber, string? filter = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { await System.Threading.Tasks.Task.CompletedTask; yield break; }
        public Task<IReadOnlyList<DeviceIpAddress>> GetIpAddressesAsync(string serialNumber, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DeviceIpAddress>>([]);
    }
}
