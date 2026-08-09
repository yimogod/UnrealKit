using UnrealKit.Core.Adb;
using UnrealKit.Core.Devices;
using UnrealKit.Core.Launch;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;

namespace UnrealKit.Tests;

public sealed class LaunchParameterServiceTests
{
    [Fact]
    public void BuildContent_CombinesComposablePresetsAndCustomArguments()
    {
        var service = new LaunchParameterService(new AdbDeviceService(new RecordingAdbService()));
        var settings = ProjectSettings.CreateDefaults("Sample");

        var content = service.BuildContent(settings, ["LLM", "LLM CSV"], "-log");

        Assert.Equal($"-llm{Environment.NewLine}-llmcsv{Environment.NewLine}-log", content);
    }

    [Fact]
    public void BuildContent_RejectsNonComposablePresetWithAnotherPreset()
    {
        var service = new LaunchParameterService(new AdbDeviceService(new RecordingAdbService()));

        Assert.Throws<ArgumentException>(() => service.BuildContent(ProjectSettings.CreateDefaults("Sample"), ["OpenGL", "LLM"]));
    }

    [Fact]
    public async Task PushAsync_UsesExpandedPathAndDeletesTemporaryFile()
    {
        var adbService = new RecordingAdbService();
        var service = new LaunchParameterService(new AdbDeviceService(adbService));
        var project = CreateProject();

        var result = await service.PushAsync(project, new LaunchParameterRequest("R58M123ABC", ["LLM"]));

        Assert.Equal("-llm", result.Content);
        Assert.Equal("/sdcard/Android/data/com.example.game/files/UE4Game/Sample/Sample/uecommandline.txt", result.RemotePath);
        Assert.Equal("R58M123ABC", adbService.PushSerialNumber);
        Assert.Equal(result.RemotePath, adbService.PushRemotePath);
        Assert.Equal("-llm", adbService.PushedContent);
        Assert.False(File.Exists(adbService.PushLocalPath));
    }

    [Fact]
    public async Task DeleteAndStart_UseProjectConfiguration()
    {
        var adbService = new RecordingAdbService();
        var service = new LaunchParameterService(new AdbDeviceService(adbService));
        var project = CreateProject();

        await service.DeleteAsync(project, "R58M123ABC");
        await service.StartApplicationAsync(project, "R58M123ABC");

        Assert.Equal("/sdcard/Android/data/com.example.game/files/UE4Game/Sample/Sample/uecommandline.txt", adbService.DeletedRemotePath);
        Assert.Equal(("R58M123ABC", "com.example.game", "com.example.game.MainActivity"), adbService.StartRequest);
    }

    private static UkitProject CreateProject()
    {
        var settings = ProjectSettings.CreateDefaults("Sample") with
        {
            PackageName = "com.example.game",
            Activity = "com.example.game.MainActivity"
        };
        return new UkitProject("C:\\Projects\\Sample\\Sample.ukit", "C:\\Projects\\Sample", UkitProjectDescriptor.CreateDefault("Sample"), settings);
    }

    private sealed class RecordingAdbService : IAdbService
    {
        private static ProcessExecutionResult Success => new(0, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        public string? PushSerialNumber { get; private set; }
        public string? PushLocalPath { get; private set; }
        public string? PushRemotePath { get; private set; }
        public string? PushedContent { get; private set; }
        public string? DeletedRemotePath { get; private set; }
        public (string SerialNumber, string PackageName, string ActivityName)? StartRequest { get; private set; }

        public Task<ProcessExecutionResult> GetVersionAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AdbDevice>>([]);
        public Task<ProcessExecutionResult> StartServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> KillServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> ConnectAsync(string endpoint, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> DisconnectAsync(string endpoint, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> TcpIpAsync(string serialNumber, int port, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);

        public Task<ProcessExecutionResult> SendConsoleCommandAsync(string serialNumber, string command, string? packageName = null, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public async IAsyncEnumerable<string> StreamLogcatAsync(string serialNumber, string? filter = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { await System.Threading.Tasks.Task.CompletedTask; yield break; }

        public Task<ProcessExecutionResult> StartApplicationAsync(string serialNumber, string packageName, string activityName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            StartRequest = (serialNumber, packageName, activityName);
            return Task.FromResult(Success);
        }

        public async Task<ProcessExecutionResult> PushFileAsync(string serialNumber, string localPath, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            PushSerialNumber = serialNumber;
            PushLocalPath = localPath;
            PushRemotePath = remotePath;
            PushedContent = await File.ReadAllTextAsync(localPath, cancellationToken);
            return Success;
        }

        public Task<ProcessExecutionResult> PullDirectoryAsync(string serialNumber, string remotePath, string localDirectory, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);

        public Task<ProcessExecutionResult> DeleteRemoteFileAsync(string serialNumber, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            DeletedRemotePath = remotePath;
            return Task.FromResult(Success);
        }

        public Task<ProcessExecutionResult> RunDumpsysAsync(string serialNumber, string packageName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    }
}
