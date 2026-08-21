using UnrealKit.Core.Adb;
using UnrealKit.Core.CommandChannel;
using UnrealKit.Core.Devices;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;

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

    [Fact]
    public async Task Win64_SendConsoleCommand_UsesConfiguredTransport()
    {
        var transport = new RecordingCommandTransport(CommandTransportKind.Http, 30010);
        var service = new Win64DeviceService(commandTransport: transport);
        var device = new Win64Device();

        var result = await service.SendConsoleCommandAsync(device, "stat unit");

        Assert.True(result.Succeeded);
        Assert.Equal("stat unit", Assert.Single(transport.Commands));
    }

    [Fact]
    public async Task Win64_SendConsoleCommand_TransportFailure_BecomesDeviceCommandException()
    {
        // 通道失败必须归一到 DeviceCommandException，否则 CLI 的可预期失败处理会被绕过。
        var transport = new FailingCommandTransport(
            CommandChannelDiagnosticCodes.ConnectFailed,
            CommandTransportKind.Http,
            30010);
        var service = new Win64DeviceService(commandTransport: transport);

        var exception = await Assert.ThrowsAsync<DeviceCommandException>(
            () => service.SendConsoleCommandAsync(new Win64Device(), "stat unit"));

        Assert.Contains(CommandChannelDiagnosticCodes.ConnectFailed, exception.Message);
    }
}

public sealed class AdbDeviceServicePortForwardTests
{
    [Fact]
    public async Task SendConsoleCommand_ForwardsPortOncePerDevice()
    {
        // 指令序列每步都 forward 会多起一个 adb 进程，并把 adb 输出混进序列报告。
        var adb = new ForwardCountingAdbService();
        var service = new AdbDeviceService(adb, commandTransport: new RecordingCommandTransport());
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
        var service = new AdbDeviceService(adb, commandTransport: new RecordingCommandTransport());

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
        var service = new AdbDeviceService(adb, commandTransport: new RecordingCommandTransport());
        var device = DeviceReference.Create("ABC123", TargetPlatform.Android);

        await Assert.ThrowsAsync<DeviceCommandException>(() => service.SendConsoleCommandAsync(device, "stat fps"));

        adb.FailForward = false;
        await service.SendConsoleCommandAsync(device, "stat fps");

        Assert.Equal(2, adb.ForwardCallCount);
    }

    [Fact]
    public async Task SendConsoleCommand_ForwardsThePortTheTransportConnectsTo()
    {
        // 转发端口与实际连接端口必须同源，否则改了一处就会转发到无人监听的端口。
        var adb = new ForwardCountingAdbService();
        var transport = new RecordingCommandTransport(CommandTransportKind.Tcp, 41234);
        var service = new AdbDeviceService(adb, commandTransport: transport);

        await service.SendConsoleCommandAsync(DeviceReference.Create("ABC123", TargetPlatform.Android), "stat unit");

        Assert.Equal(41234, adb.LastForwardHostPort);
        Assert.Equal(41234, adb.LastForwardDevicePort);
    }

    [Fact]
    public async Task SendConsoleCommand_TransportFailure_BecomesDeviceCommandExceptionWithCode()
    {
        var adb = new ForwardCountingAdbService();
        var service = new AdbDeviceService(
            adb,
            commandTransport: new FailingCommandTransport(CommandChannelDiagnosticCodes.ConnectFailed));

        var exception = await Assert.ThrowsAsync<DeviceCommandException>(
            () => service.SendConsoleCommandAsync(DeviceReference.Create("ABC123", TargetPlatform.Android), "stat unit"));

        Assert.Contains(CommandChannelDiagnosticCodes.ConnectFailed, exception.Message);
    }

    private sealed class ForwardCountingAdbService : IAdbService
    {
        private static ProcessExecutionResult Success => new(0, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        public int ForwardCallCount { get; private set; }

        public bool FailForward { get; set; }

        public int? LastForwardHostPort { get; private set; }

        public int? LastForwardDevicePort { get; private set; }

        public Task<ProcessExecutionResult> ForwardTcpAsync(string serialNumber, int hostPort, int devicePort, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            ForwardCallCount++;
            LastForwardHostPort = hostPort;
            LastForwardDevicePort = devicePort;
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
        public Task<ProcessExecutionResult> ReadFileAsync(string serialNumber, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> RunDumpsysAsync(string serialNumber, string packageName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> InstallApkAsync(string serialNumber, string localApkPath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> ForceStopApplicationAsync(string serialNumber, string packageName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public async IAsyncEnumerable<string> StreamLogcatAsync(string serialNumber, string? filter = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { await Task.CompletedTask; yield break; }
        public Task<IReadOnlyList<DeviceIpAddress>> GetIpAddressesAsync(string serialNumber, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DeviceIpAddress>>([]);
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

public sealed class PlatformScopeTests
{
    [Fact]
    public void All_IncludesEveryPlatformName()
    {
        Assert.True(PlatformScope.All.IsAll);
        Assert.All(PlatformNames.All, name => Assert.True(PlatformScope.All.Includes(name)));
    }

    [Fact]
    public void All_IncludesUnknownPlatformName()
    {
        // 作用域是过滤器，不承担校验职责：静默丢弃未知平台会让归档目录凭空消失。
        Assert.True(PlatformScope.All.Includes("PlayStation9"));
    }

    [Theory]
    [InlineData(TargetPlatform.Android, "Android", true)]
    [InlineData(TargetPlatform.Android, "android", true)]
    [InlineData(TargetPlatform.Android, "Win64", false)]
    [InlineData(TargetPlatform.Win64, "Win64", true)]
    [InlineData(TargetPlatform.Win64, "Android", false)]
    public void For_IncludesOnlyItsOwnPlatform(TargetPlatform platform, string candidate, bool expected)
    {
        Assert.Equal(expected, PlatformScope.For(platform).Includes(candidate));
    }

    [Fact]
    public void AllOptions_PutsAllFirstAndCoversEveryPlatform()
    {
        // 「全部」在最前，且每个平台恰好一项：下拉框缺项等于该平台无法被聚焦。
        Assert.True(PlatformScope.AllOptions[0].IsAll);
        Assert.Equal(PlatformNames.All.Count + 1, PlatformScope.AllOptions.Count);
        Assert.All(PlatformNames.All, name =>
            Assert.Single(PlatformScope.AllOptions, option => option.Name == name));
    }

    [Theory]
    [InlineData("Android")]
    [InlineData("win64")]
    [InlineData(PlatformScope.AllName)]
    [InlineData("  All  ")]
    public void TryParse_AcceptsScopeNamesCaseInsensitively(string value)
    {
        Assert.True(PlatformScope.TryParse(value, out var scope));
        Assert.NotNull(scope);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("PlayStation9")]
    [InlineData("99")]
    public void TryParse_RejectsUnknownValuesAndYieldsAll(string? value)
    {
        // 无法识别时给出「全部」而不是某个平台：陈旧记录不该让界面聚焦到
        // 用户没选过的平台，也不该隐藏任何设备。
        Assert.False(PlatformScope.TryParse(value, out var scope));
        Assert.True(scope.IsAll);
    }

    [Fact]
    public void Equality_IsByPlatform()
    {
        Assert.Equal(PlatformScope.For(TargetPlatform.Win64), PlatformScope.For(TargetPlatform.Win64));
        Assert.NotEqual(PlatformScope.For(TargetPlatform.Win64), PlatformScope.For(TargetPlatform.Android));
        Assert.NotEqual(PlatformScope.All, PlatformScope.For(TargetPlatform.Android));
    }
}
