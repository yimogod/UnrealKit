using UnrealKit.Core.Adb;
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
        var service = new CaptureService(new FakeAdbService());
        var device = new AdbDevice("device-01", AdbDeviceStatus.Device, null, "Pixel", null, AdbConnectionType.Usb, string.Empty);

        var result = await service.CaptureAsync(new CaptureRequest(configuredProject, device, "Nightly", "capture-001"));

        var memInfoFile = Assert.Single(result.Manifest.InputFiles, file => file.RelativePath.StartsWith("MemInfo/", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(result.Plan.CaptureDirectory, memInfoFile.RelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.True(File.Exists(Path.Combine(result.Plan.CaptureDirectory, "Saved", "Saved.txt")));
        Assert.True(File.Exists(result.ManifestPath));
        Assert.Equal("Nightly", result.Manifest.Tag);
        Assert.All(result.Manifest.InputFiles, file => Assert.NotEmpty(file.Sha256));
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
    }
}
