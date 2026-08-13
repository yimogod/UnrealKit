using UnrealKit.Core.Adb;
using UnrealKit.Core.Devices;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;
using UnrealKit.Core.RemoteControl;

namespace UnrealKit.Tests;

public sealed class PlatformNamesTests
{
    [Theory]
    [InlineData(TargetPlatform.Android, "Android")]
    [InlineData(TargetPlatform.Win64, "Win64")]
    public void ToName_ReturnsStableContractString(TargetPlatform platform, string expected)
    {
        // 归档目录名与 .ukit 字段依赖这些字符串，属于稳定契约。
        Assert.Equal(expected, PlatformNames.ToName(platform));
    }

    [Theory]
    [InlineData("Android", TargetPlatform.Android)]
    [InlineData("android", TargetPlatform.Android)]
    [InlineData("  WIN64  ", TargetPlatform.Win64)]
    public void TryParse_IsCaseInsensitiveAndTrims(string value, TargetPlatform expected)
    {
        Assert.True(PlatformNames.TryParse(value, out var platform));
        Assert.Equal(expected, platform);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Linux")]
    [InlineData("99")]
    public void TryParse_UnknownValue_ReturnsFalseWithoutFallback(string? value)
    {
        // 不回退到默认平台：静默当作 Android 会让错配的采集看起来成功。
        Assert.False(PlatformNames.TryParse(value, out _));
    }

    [Fact]
    public void Parse_UnknownValue_ListsValidValues()
    {
        var exception = Assert.Throws<ArgumentException>(() => PlatformNames.Parse("Linux"));
        Assert.Contains("Android", exception.Message);
        Assert.Contains("Win64", exception.Message);
    }

    [Fact]
    public void ToName_RoundTripsEveryEnumMember()
    {
        // 新增平台时若忘记补映射，此测试失败。
        foreach (var platform in Enum.GetValues<TargetPlatform>())
        {
            Assert.True(PlatformNames.TryParse(PlatformNames.ToName(platform), out var parsed));
            Assert.Equal(platform, parsed);
        }
    }
}

public sealed class DeviceCapabilityTests
{
    [Fact]
    public void Win64_SupportsConsoleCommandsButNotLogStreaming()
    {
        var service = new Win64DeviceService();

        Assert.True(service.Supports(DeviceCapability.SendConsoleCommand));
        Assert.False(service.Supports(DeviceCapability.StreamLog));
        Assert.True(service.Supports(DeviceCapability.CaptureMemory));
        Assert.True(service.Supports(DeviceCapability.StartApplication));
        Assert.Equal(TargetPlatform.Win64, service.Platform);
    }

    [Fact]
    public void Win64_StreamLog_ThrowsInsteadOfReturningEmptyStream()
    {
        // 空流会被误读为「已连接但暂无日志」，因此必须显式抛出。
        var service = new Win64DeviceService();
        var device = new Win64Device();

        var exception = Assert.Throws<DeviceCapabilityNotSupportedException>(
            () => service.StreamLogAsync(device));

        Assert.Equal(DeviceCapability.StreamLog, exception.Capability);
        Assert.Equal("Win64", exception.Platform);
    }

    private sealed class RecordingRemoteControlService : IRemoteControlService
    {
        public List<RemoteControlCommandRequest> Requests { get; } = [];

        public Task<ProcessExecutionResult> SendConsoleCommandAsync(
            RemoteControlCommandRequest request,
            IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessExecutionResult(
                0,
                "ok",
                string.Empty,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));
        }
    }

    [Fact]
    public async Task Win64_SendConsoleCommand_UsesRemoteControlService()
    {
        var remoteControl = new RecordingRemoteControlService();
        var service = new Win64DeviceService(remoteControlService: remoteControl);
        var device = new Win64Device();

        var result = await service.SendConsoleCommandAsync(device, "stat unit");

        Assert.True(result.Succeeded);
        var request = Assert.Single(remoteControl.Requests);
        Assert.Equal(30010, request.HttpPort);
        Assert.Equal("stat unit", request.Command);
    }
}

public sealed class AdbDeviceServicePortForwardTests
{
    [Fact]
    public async Task SendConsoleCommand_ForwardsPortOncePerDevice()
    {
        // 指令序列每步都 forward 会多起一个 adb 进程，并把 adb 输出混进序列报告。
        var adb = new ForwardCountingAdbService();
        var service = new AdbDeviceService(adb, remoteControlService: new AlwaysOkRemoteControlService());
        var device = DeviceReference.Create("ABC123", TargetPlatform.Android);

        await service.SendConsoleCommandAsync(device, "stat fps");
        await service.SendConsoleCommandAsync(device, "stat unit");
        await service.SendConsoleCommandAsync(device, "stat rhi");

        Assert.Equal(1, adb.ForwardCallCount);
    }

    [Fact]
    public async Task SendConsoleCommand_ForwardsOncePerDistinctDevice()
    {
        var adb = new ForwardCountingAdbService();
        var service = new AdbDeviceService(adb, remoteControlService: new AlwaysOkRemoteControlService());

        await service.SendConsoleCommandAsync(DeviceReference.Create("ABC123", TargetPlatform.Android), "stat fps");
        await service.SendConsoleCommandAsync(DeviceReference.Create("XYZ789", TargetPlatform.Android), "stat fps");
        await service.SendConsoleCommandAsync(DeviceReference.Create("ABC123", TargetPlatform.Android), "stat unit");

        Assert.Equal(2, adb.ForwardCallCount);
    }

    [Fact]
    public async Task SendConsoleCommand_FailedForward_IsRetriedOnNextCall()
    {
        // 失败的转发不记录，否则设备重连后永远不会重试。
        var adb = new ForwardCountingAdbService { FailForward = true };
        var service = new AdbDeviceService(adb, remoteControlService: new AlwaysOkRemoteControlService());
        var device = DeviceReference.Create("ABC123", TargetPlatform.Android);

        await Assert.ThrowsAsync<DeviceCommandException>(() => service.SendConsoleCommandAsync(device, "stat fps"));

        adb.FailForward = false;
        await service.SendConsoleCommandAsync(device, "stat fps");

        Assert.Equal(2, adb.ForwardCallCount);
    }

    private sealed class AlwaysOkRemoteControlService : IRemoteControlService
    {
        public Task<ProcessExecutionResult> SendConsoleCommandAsync(
            RemoteControlCommandRequest request,
            IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessExecutionResult(0, "ok", string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    private sealed class ForwardCountingAdbService : IAdbService
    {
        private static ProcessExecutionResult Success => new(0, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        public int ForwardCallCount { get; private set; }

        public bool FailForward { get; set; }

        public Task<ProcessExecutionResult> ForwardTcpAsync(string serialNumber, int hostPort, int devicePort, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            ForwardCallCount++;
            return Task.FromResult(FailForward
                ? new ProcessExecutionResult(1, string.Empty, "device offline", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                : Success);
        }

        public Task<ProcessExecutionResult> GetVersionAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AdbDevice>>([]);
        public Task<ProcessExecutionResult> StartServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> KillServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> ConnectAsync(string endpoint, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> DisconnectAsync(string endpoint, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> TcpIpAsync(string serialNumber, int port, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> StartApplicationAsync(string serialNumber, string packageName, string activityName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> PushFileAsync(string serialNumber, string localPath, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> PullDirectoryAsync(string serialNumber, string remotePath, string localDirectory, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> DeleteRemoteFileAsync(string serialNumber, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> RunDumpsysAsync(string serialNumber, string packageName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> ForceStopApplicationAsync(string serialNumber, string packageName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public async IAsyncEnumerable<string> StreamLogcatAsync(string serialNumber, string? filter = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { await Task.CompletedTask; yield break; }
    }
}

public sealed class DeviceReferenceTests
{
    [Fact]
    public void Create_UsesPlatformNamesMapping()
    {
        var android = DeviceReference.Create("ABC123", TargetPlatform.Android);
        var win64 = DeviceReference.Create("localhost", TargetPlatform.Win64, "MYHOST");

        Assert.Equal("Android", android.Platform);
        Assert.Equal("ABC123", android.Name);
        Assert.Equal("Win64", win64.Platform);
        Assert.Equal("MYHOST", win64.Name);
    }

    [Fact]
    public void Create_RejectsBlankId()
    {
        Assert.Throws<ArgumentException>(() => DeviceReference.Create("  ", TargetPlatform.Android));
    }
}

public sealed class DeviceServiceFactoryTests
{
    [Fact]
    public void CreateForDevice_Win64_DoesNotRequireAdb()
    {
        var factory = new DeviceServiceFactory(adbService: null, processRunner: new ProcessRunner());
        var service = factory.CreateForDevice(new Win64Device());

        Assert.IsType<Win64DeviceService>(service);
    }

    [Fact]
    public void CreateForDevice_AndroidWithoutAdb_FailsWithActionableMessage()
    {
        var factory = new DeviceServiceFactory(adbService: null);
        var device = DeviceReference.Create("ABC123", TargetPlatform.Android);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateForDevice(device));

        Assert.Contains("ABC123", exception.Message);
        Assert.Contains("ADB", exception.Message);
    }

    [Fact]
    public void CreateForDevice_UnknownPlatform_Throws()
    {
        var factory = new DeviceServiceFactory(adbService: null, processRunner: new ProcessRunner());

        Assert.Throws<ArgumentException>(
            () => factory.CreateForDevice(new UnknownPlatformDevice()));
    }

    private sealed class UnknownPlatformDevice : IDevice
    {
        public string Id => "device-1";
        public string Name => "device-1";
        public string Platform => "Linux";
        public bool IsAvailable => true;
    }
}

public sealed class AggregateDeviceProviderTests
{
    [Fact]
    public async Task ListDevicesAsync_CombinesProvidersAndKeepsFailures()
    {
        // 一个平台不可用不应让整份列表失败，但原因必须保留。
        var provider = new AggregateDeviceProvider(
        [
            new Win64DeviceService(),
            new UnavailableDeviceProvider(TargetPlatform.Android, "adb not found on PATH")
        ]);

        var result = await provider.ListDevicesAsync();

        Assert.Single(result.Devices);
        Assert.Equal("Win64", result.Devices[0].Platform);

        var failure = Assert.Single(result.Failures);
        Assert.Equal(TargetPlatform.Android, failure.Platform);
        Assert.Contains("adb not found", failure.Message);
    }

    [Fact]
    public async Task ListDevicesAsync_AllProvidersHealthy_ReportsNoFailures()
    {
        var provider = new AggregateDeviceProvider([new Win64DeviceService()]);

        var result = await provider.ListDevicesAsync();

        Assert.Empty(result.Failures);
        Assert.Single(result.Devices);
    }

    [Fact]
    public async Task ListDevicesAsync_CancellationPropagates()
    {
        // 取消是调用方的意图，不是平台故障，不应被记成 failure。
        var provider = new AggregateDeviceProvider([new SlowProvider()]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ListDevicesAsync(cancellationToken: cts.Token));
    }

    private sealed class SlowProvider : IDeviceProvider
    {
        public TargetPlatform Platform => TargetPlatform.Android;

        public Task<IReadOnlyList<IDevice>> ListDevicesAsync(
            IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IDevice>>([]);
    }
}
