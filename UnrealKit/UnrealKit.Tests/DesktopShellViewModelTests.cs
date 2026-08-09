using UnrealKit.Core.Adb;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;
using UnrealKit.Desktop;

namespace UnrealKit.Tests;

public sealed class DesktopShellViewModelTests
{
    [Fact]
    public async Task DeviceAndLaunchParameterWorkflow_UsesSelectedDeviceAndShowsTarget()
    {
        var adb = new RecordingAdbService();
        var project = CreateProject();
        var confirmation = new RecordingConfirmationService(true);
        var viewModel = new ShellViewModel(new StaticProjectService(project), new StaticAdbServiceFactory(adb), confirmation)
        {
            ProjectFilePath = project.ProjectFilePath,
            CustomLaunchArguments = "-trace=memory"
        };

        await ((AsyncDelegateCommand)viewModel.OpenProjectCommand).ExecuteAsync();
        await ((AsyncDelegateCommand)viewModel.RefreshDevicesCommand).ExecuteAsync();
        Assert.NotNull(viewModel.SelectedDevice);
        Assert.Equal("R58M123ABC", viewModel.SelectedDevice?.SerialNumber);
        await ((AsyncDelegateCommand)viewModel.PushLaunchParametersCommand).ExecuteAsync();
        await ((AsyncDelegateCommand)viewModel.StartApplicationCommand).ExecuteAsync();
        await ((AsyncDelegateCommand)viewModel.DeleteLaunchParametersCommand).ExecuteAsync();

        Assert.Equal("R58M123ABC", viewModel.SelectedDevice?.SerialNumber);
        Assert.Contains("R58M123ABC", viewModel.LaunchOperationSummary);
        Assert.Contains("com.example.game", viewModel.LaunchOperationSummary);
        Assert.Contains("uecommandline.txt", viewModel.LaunchParameterPreview);
        Assert.Equal("R58M123ABC", adb.PushSerialNumber);
        Assert.Contains("-trace=memory", adb.PushedContent);
        Assert.Equal(("R58M123ABC", "com.example.game", "com.example.game.MainActivity"), adb.StartRequest);
        Assert.Equal("R58M123ABC", adb.DeleteSerialNumber);
        Assert.True(confirmation.WasAsked);
        Assert.NotEmpty(viewModel.OperationLogs);
    }

    [Fact]
    public async Task DeleteLaunchParameters_DoesNotCallAdbWhenUserDeclines()
    {
        var adb = new RecordingAdbService();
        var project = CreateProject();
        var confirmation = new RecordingConfirmationService(false);
        var viewModel = new ShellViewModel(new StaticProjectService(project), new StaticAdbServiceFactory(adb), confirmation)
        {
            ProjectFilePath = project.ProjectFilePath
        };

        await ((AsyncDelegateCommand)viewModel.OpenProjectCommand).ExecuteAsync();
        await ((AsyncDelegateCommand)viewModel.RefreshDevicesCommand).ExecuteAsync();
        viewModel.SelectedDevice = viewModel.Devices.Single();
        await ((AsyncDelegateCommand)viewModel.DeleteLaunchParametersCommand).ExecuteAsync();

        Assert.True(confirmation.WasAsked);
        Assert.Null(adb.DeleteSerialNumber);
        Assert.Equal("已取消删除设备启动参数。", viewModel.StatusMessage);
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

    private sealed class StaticProjectService(UkitProject project) : IProjectService
    {
        public Task<ProjectCreateResult> CreateProjectAsync(CreateProjectRequest request, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UkitProject> OpenProjectAsync(string projectFilePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(project);
        public Task<UkitProject> UpdateSettingsAsync(UkitProject updatedProject, ProjectSettings settings, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(updatedProject with { Settings = settings });
        public Task<ProjectValidationResult> ValidateProjectAsync(string projectFilePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(new ProjectValidationResult([]));
    }

    private sealed class StaticAdbServiceFactory(IAdbService adb) : IDesktopAdbServiceFactory
    {
        public IAdbService Create(ProjectSettings? settings, IProgress<ProcessOutput>? output) => new OutputForwardingAdbService(adb, output);
        public AdbPathResolution Resolve(ProjectSettings? settings) => new(null, []);
    }

    private sealed class RecordingConfirmationService(bool response) : IUserConfirmationService
    {
        public bool WasAsked { get; private set; }
        public Task<bool> ConfirmDeleteLaunchParametersAsync(LaunchOperationTarget target)
        {
            WasAsked = true;
            return Task.FromResult(response);
        }
    }

    private sealed class OutputForwardingAdbService(IAdbService inner, IProgress<ProcessOutput>? output) : IAdbService
    {
        private static ProcessExecutionResult Success => new(0, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        private void Write(string text) => output?.Report(new ProcessOutput(DateTimeOffset.UtcNow, ProcessOutputStream.StandardOutput, text));
        public Task<ProcessExecutionResult> GetVersionAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.GetVersionAsync(progress, cancellationToken);
        public async Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) { Write("adb devices -l"); return await inner.ListDevicesAsync(progress, cancellationToken); }
        public Task<ProcessExecutionResult> StartServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.StartServerAsync(progress, cancellationToken);
        public Task<ProcessExecutionResult> KillServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.KillServerAsync(progress, cancellationToken);
        public Task<ProcessExecutionResult> ConnectAsync(string endpoint, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.ConnectAsync(endpoint, progress, cancellationToken);
        public Task<ProcessExecutionResult> DisconnectAsync(string endpoint, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.DisconnectAsync(endpoint, progress, cancellationToken);
        public Task<ProcessExecutionResult> TcpIpAsync(string serialNumber, int port, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.TcpIpAsync(serialNumber, port, progress, cancellationToken);
        public async Task<ProcessExecutionResult> StartApplicationAsync(string serialNumber, string packageName, string activityName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) { Write("am start"); return await inner.StartApplicationAsync(serialNumber, packageName, activityName, progress, cancellationToken); }
        public async Task<ProcessExecutionResult> PushFileAsync(string serialNumber, string localPath, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) { Write("adb push"); return await inner.PushFileAsync(serialNumber, localPath, remotePath, progress, cancellationToken); }
        public Task<ProcessExecutionResult> PullDirectoryAsync(string serialNumber, string remotePath, string localDirectory, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.PullDirectoryAsync(serialNumber, remotePath, localDirectory, progress, cancellationToken);
        public async Task<ProcessExecutionResult> DeleteRemoteFileAsync(string serialNumber, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) { Write("adb shell rm"); return await inner.DeleteRemoteFileAsync(serialNumber, remotePath, progress, cancellationToken); }
        public Task<ProcessExecutionResult> RunDumpsysAsync(string serialNumber, string packageName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.RunDumpsysAsync(serialNumber, packageName, progress, cancellationToken);
        public Task<ProcessExecutionResult> SendConsoleCommandAsync(string serialNumber, string command, string? packageName = null, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.SendConsoleCommandAsync(serialNumber, command, packageName, progress, cancellationToken);
        public IAsyncEnumerable<string> StreamLogcatAsync(string serialNumber, string? filter = null, CancellationToken cancellationToken = default) => inner.StreamLogcatAsync(serialNumber, filter, cancellationToken);
    }

    private sealed class RecordingAdbService : IAdbService
    {
        private static ProcessExecutionResult Success => new(0, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        public string? PushSerialNumber { get; private set; }
        public string? PushedContent { get; private set; }
        public string? DeleteSerialNumber { get; private set; }
        public (string SerialNumber, string PackageName, string ActivityName)? StartRequest { get; private set; }
        public Task<ProcessExecutionResult> GetVersionAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AdbDevice>>([new("R58M123ABC", AdbDeviceStatus.Device, null, "Pixel", null, AdbConnectionType.Usb, "R58M123ABC device model:Pixel")]);
        public Task<ProcessExecutionResult> StartServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> KillServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> ConnectAsync(string endpoint, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> DisconnectAsync(string endpoint, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> TcpIpAsync(string serialNumber, int port, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> StartApplicationAsync(string serialNumber, string packageName, string activityName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) { StartRequest = (serialNumber, packageName, activityName); return Task.FromResult(Success); }
        public async Task<ProcessExecutionResult> PushFileAsync(string serialNumber, string localPath, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) { PushSerialNumber = serialNumber; PushedContent = await File.ReadAllTextAsync(localPath, cancellationToken); return Success; }
        public Task<ProcessExecutionResult> PullDirectoryAsync(string serialNumber, string remotePath, string localDirectory, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> DeleteRemoteFileAsync(string serialNumber, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) { DeleteSerialNumber = serialNumber; return Task.FromResult(Success); }
        public Task<ProcessExecutionResult> RunDumpsysAsync(string serialNumber, string packageName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> SendConsoleCommandAsync(string serialNumber, string command, string? packageName = null, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public async IAsyncEnumerable<string> StreamLogcatAsync(string serialNumber, string? filter = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { await System.Threading.Tasks.Task.CompletedTask; yield break; }
    }
}
