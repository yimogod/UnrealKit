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
        var service = new LaunchParameterService(new RecordingDeviceService());
        var settings = ProjectSettings.CreateDefaults("Sample");

        var content = service.BuildContent(settings, ["Mem.LLM", "Mem.LLM_CSV"], "-log");

        Assert.Equal("-llm -llmcsv -log", content);
    }

    [Fact]
    public void BuildContent_MergesTracePresetsAndDedupesSwitchAndValues()
    {
        var service = new LaunchParameterService(new RecordingDeviceService());
        var settings = ProjectSettings.CreateDefaults("Sample");

        var content = service.BuildContent(settings, ["Trace.Client_Default", "Trace.Client_Memory"]);

        var expected = string.Join(' ',
        [
            "-statnamedevents",
            "-tracefile",
            "-trace=cpu,frame,log,bookmark,task,counter,stats,gpu,screenshot,region,file,loadtime,assetloadtime,rdg,audio,audiomixer,memory,metadata,assetmetadata",
            "-llm",
            "-llmcsv"
        ]);
        Assert.Equal(expected, content);
    }

    [Fact]
    public void BuildContent_DedupesRepeatedSwitchWithinPreset()
    {
        var service = new LaunchParameterService(new RecordingDeviceService());
        var settings = ProjectSettings.CreateDefaults("Sample");

        // Trace.Client_Network 的预设里 -statnamedevents 出现两次，且 -trace 尾部带空白。
        var content = service.BuildContent(settings, ["Trace.Client_Network"]);

        var expected = string.Join(' ',
        [
            "-statnamedevents",
            "-tracefile",
            "-trace=cpu,frame,log,bookmark,task,counter,stats,net"
        ]);
        Assert.Equal(expected, content);
    }

    [Fact]
    public void BuildContent_RecomputesWhenPresetUnselected()
    {
        var service = new LaunchParameterService(new RecordingDeviceService());
        var settings = ProjectSettings.CreateDefaults("Sample");

        var both = service.BuildContent(settings, ["Trace.Client_Default", "Trace.Client_Memory"]);
        var memoryOnly = service.BuildContent(settings, ["Trace.Client_Memory"]);

        Assert.Contains("memory,metadata,assetmetadata", both);
        Assert.Contains("memory,metadata,assetmetadata", memoryOnly);
        // 取消 Default 后，其独有的 gpu 通道不再出现。
        Assert.DoesNotContain("gpu,", memoryOnly);
    }

    [Fact]
    public void BuildContent_RejectsTwoPresetsInSameExclusiveGroup()
    {
        var service = new LaunchParameterService(new RecordingDeviceService());

        var exception = Assert.Throws<ArgumentException>(() =>
            service.BuildContent(ProjectSettings.CreateDefaults("Sample"), ["Render.OpenGL", "Render.Vulkan"]));

        Assert.Contains("mutually exclusive", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildContent_AllowsExclusiveGroupMemberWithUngroupedPreset()
    {
        var service = new LaunchParameterService(new RecordingDeviceService());
        var settings = ProjectSettings.CreateDefaults("Sample");

        // 互斥只在组内生效：OpenGL 属于 Render 组，可与组外的 Mem.LLM 叠加。
        var content = service.BuildContent(settings, ["Render.OpenGL", "Mem.LLM"]);

        Assert.Equal(string.Join(' ', ["-OpenGLES", "-llm"]), content);
    }

    [Fact]
    public void BuildContent_AllowsCoexistGroupMembersTogether()
    {
        // 同一个预设可以分到 Coexist 组，验证非互斥组不施加任何约束。
        var service = new LaunchParameterService(new RecordingDeviceService());
        var settings = ProjectSettings.CreateDefaults("Sample") with
        {
            LaunchParameterGroups =
            [
                new LaunchParameterPresetGroup("Mem", LaunchParameterGroupMode.Coexist, ["Mem.LLM", "Mem.LLM_CSV"])
            ]
        };

        var content = service.BuildContent(settings, ["Mem.LLM", "Mem.LLM_CSV"]);

        Assert.Equal(string.Join(' ', ["-llm", "-llmcsv"]), content);
    }

    [Fact]
    public async Task PushAsync_UsesExpandedPathAndDeletesTemporaryFile()
    {
        var deviceService = new RecordingDeviceService();
        var service = new LaunchParameterService(deviceService);
        var project = CreateProject();

        var result = await service.PushAsync(project, new LaunchParameterRequest("R58M123ABC", ["Mem.LLM"]));

        Assert.Equal("-llm", result.Content);
        Assert.Equal("/sdcard/Android/data/com.example.game/files/UnrealGame/Sample/Sample/uecommandline.txt", result.RemotePath);
        Assert.Equal("R58M123ABC", deviceService.PushSerialNumber);
        Assert.Equal(result.RemotePath, deviceService.PushRemotePath);
        Assert.Equal("-llm", deviceService.PushedContent);
        Assert.False(File.Exists(deviceService.PushLocalPath));
    }

    [Fact]
    public async Task DeleteAndStart_UseProjectConfiguration()
    {
        var deviceService = new RecordingDeviceService();
        var service = new LaunchParameterService(deviceService);
        var project = CreateProject();

        await service.DeleteAsync(project, "R58M123ABC");
        await service.StartApplicationAsync(project, "R58M123ABC");
        await service.StopApplicationAsync(project, "R58M123ABC");

        Assert.Equal("/sdcard/Android/data/com.example.game/files/UnrealGame/Sample/Sample/uecommandline.txt", deviceService.DeletedRemotePath);
        Assert.Equal(("R58M123ABC", "com.example.game", "com.example.game.MainActivity"), deviceService.StartRequest);
        Assert.Equal(("R58M123ABC", "com.example.game"), deviceService.ForceStopRequest);
    }

    [Fact]
    public async Task ReadAsync_ReturnsDeviceFileContent()
    {
        var deviceService = new RecordingDeviceService { ReadFileContent = "-RCWebControlEnable\n-RCWebInterfaceEnable" };
        var service = new LaunchParameterService(deviceService);
        var project = CreateProject();

        var result = await service.ReadAsync(project, "R58M123ABC");

        Assert.True(result.ReadResult.Succeeded);
        Assert.Equal("-RCWebControlEnable\n-RCWebInterfaceEnable", result.ReadResult.StandardOutput);
        Assert.Equal("/sdcard/Android/data/com.example.game/files/UnrealGame/Sample/Sample/uecommandline.txt", result.RemotePath);
        Assert.Equal("R58M123ABC", deviceService.ReadSerialNumber);
        Assert.Equal(result.RemotePath, deviceService.ReadRemotePath);
    }

    [Fact]
    public async Task ReadAsync_MissingFile_ReturnsNonZeroResultWithoutThrowing()
    {
        var deviceService = new RecordingDeviceService { ReadFileMissing = true };
        var service = new LaunchParameterService(deviceService);
        var project = CreateProject();

        var result = await service.ReadAsync(project, "R58M123ABC");

        Assert.False(result.ReadResult.Succeeded);
        Assert.Contains("No such file or directory", result.ReadResult.StandardError);
    }

    private static UkitProject CreateProject()
    {
        var settings = ProjectSettings.CreateDefaults("Sample") with
        {
            Android = AndroidPlatformProfile.CreateDefaults() with { PackageName = "com.example.game", Activity = "com.example.game.MainActivity" }
        };
        return new UkitProject("C:\\Projects\\Sample\\Sample.ukit", "C:\\Projects\\Sample", UkitProjectDescriptor.CreateDefault("Sample"), settings);
    }

    private sealed class RecordingDeviceService : IDeviceService
    {
        private static ProcessExecutionResult Success => new(0, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        public string? PushSerialNumber { get; private set; }
        public string? PushLocalPath { get; private set; }
        public string? PushRemotePath { get; private set; }
        public string? PushedContent { get; private set; }
        public string? DeletedRemotePath { get; private set; }
        public string? ReadSerialNumber { get; private set; }
        public string? ReadRemotePath { get; private set; }
        public string ReadFileContent { get; set; } = "-llm";
        public bool ReadFileMissing { get; set; }
        public (string SerialNumber, string PackageName, string ActivityName)? StartRequest { get; private set; }
        public (string SerialNumber, string PackageName)? ForceStopRequest { get; private set; }

        public TargetPlatform Platform => TargetPlatform.Android;

        public bool Supports(DeviceCapability capability) => true;

        public Task<IReadOnlyList<IDevice>> ListDevicesAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<IDevice>>([]);

        public Task<ProcessExecutionResult> CaptureMemoryAsync(IDevice device, string target, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);

        public Task<ProcessExecutionResult> PullDirectoryAsync(IDevice device, string remotePath, string localDirectory, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);

        public Task<ProcessExecutionResult> SendConsoleCommandAsync(IDevice device, string command, string? target = null, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);

        public async IAsyncEnumerable<string> StreamLogAsync(IDevice device, string? filter = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { await System.Threading.Tasks.Task.CompletedTask; yield break; }

        public Task<ProcessExecutionResult> StartApplicationAsync(IDevice device, string target, string? activity = null, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            StartRequest = (device.Id, target, activity ?? string.Empty);
            return Task.FromResult(Success);
        }

        public Task<ProcessExecutionResult> StopApplicationAsync(IDevice device, string target, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            ForceStopRequest = (device.Id, target);
            return Task.FromResult(Success);
        }

        public async Task<ProcessExecutionResult> PushFileAsync(IDevice device, string localPath, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            PushSerialNumber = device.Id;
            PushLocalPath = localPath;
            PushRemotePath = remotePath;
            PushedContent = await File.ReadAllTextAsync(localPath, cancellationToken);
            return Success;
        }

        public Task<ProcessExecutionResult> DeleteRemoteFileAsync(IDevice device, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            DeletedRemotePath = remotePath;
            return Task.FromResult(Success);
        }

        public Task<ProcessExecutionResult> ReadFileAsync(IDevice device, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            ReadSerialNumber = device.Id;
            ReadRemotePath = remotePath;
            return Task.FromResult(ReadFileMissing
                ? new ProcessExecutionResult(1, string.Empty, "No such file or directory", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                : new ProcessExecutionResult(0, ReadFileContent, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }

        public Task<ProcessExecutionResult> InstallApplicationAsync(IDevice device, string localApplicationPath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    }
}
