using UnrealKit.Core.Adb;
using UnrealKit.Core.CommandChannel;
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

    [Fact]
    public async Task Win64_SendConsoleCommand_UsesConfiguredTransport()
    {
        var transport = new RecordingCommandTransport(CommandTransportKind.Http, RemoteControlOptions.DefaultHttpPort);
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
            RemoteControlOptions.DefaultHttpPort);
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
        var runner = new ForwardCountingRunner();
        var service = CreateService(runner, new RecordingCommandTransport());
        var device = DeviceReference.Create("ABC123", TargetPlatform.Android);

        await service.SendConsoleCommandAsync(device, "stat fps");
        await service.SendConsoleCommandAsync(device, "stat unit");
        await service.SendConsoleCommandAsync(device, "stat rhi");

        Assert.Equal(1, runner.ForwardCallCount);
    }

    [Fact]
    public async Task SendConsoleCommand_ForwardsOncePerDistinctDevice()
    {
        var runner = new ForwardCountingRunner();
        var service = CreateService(runner, new RecordingCommandTransport());

        await service.SendConsoleCommandAsync(DeviceReference.Create("ABC123", TargetPlatform.Android), "stat fps");
        await service.SendConsoleCommandAsync(DeviceReference.Create("XYZ789", TargetPlatform.Android), "stat fps");
        await service.SendConsoleCommandAsync(DeviceReference.Create("ABC123", TargetPlatform.Android), "stat unit");

        Assert.Equal(2, runner.ForwardCallCount);
    }

    [Fact]
    public async Task SendConsoleCommand_FailedForward_IsRetriedOnNextCall()
    {
        // 失败的转发不记录，否则设备重连后永远不会重试。
        var runner = new ForwardCountingRunner { FailForward = true };
        var service = CreateService(runner, new RecordingCommandTransport());
        var device = DeviceReference.Create("ABC123", TargetPlatform.Android);

        await Assert.ThrowsAsync<DeviceCommandException>(() => service.SendConsoleCommandAsync(device, "stat fps"));

        runner.FailForward = false;
        await service.SendConsoleCommandAsync(device, "stat fps");

        Assert.Equal(2, runner.ForwardCallCount);
    }

    [Fact]
    public async Task SendConsoleCommand_ForwardsThePortTheTransportConnectsTo()
    {
        // 转发端口与实际连接端口必须同源，否则改了一处就会转发到无人监听的端口。
        var runner = new ForwardCountingRunner();
        var transport = new RecordingCommandTransport(CommandTransportKind.Http, 41234);
        var service = CreateService(runner, transport);

        await service.SendConsoleCommandAsync(DeviceReference.Create("ABC123", TargetPlatform.Android), "stat unit");

        Assert.Equal(41234, runner.LastForwardHostPort);
        Assert.Equal(41234, runner.LastForwardDevicePort);
    }

    [Fact]
    public async Task SendConsoleCommand_TransportFailure_BecomesDeviceCommandExceptionWithCode()
    {
        var runner = new ForwardCountingRunner();
        var service = CreateService(
            runner,
            new FailingCommandTransport(CommandChannelDiagnosticCodes.ConnectFailed));

        var exception = await Assert.ThrowsAsync<DeviceCommandException>(
            () => service.SendConsoleCommandAsync(DeviceReference.Create("ABC123", TargetPlatform.Android), "stat unit"));

        Assert.Contains(CommandChannelDiagnosticCodes.ConnectFailed, exception.Message);
    }

    private static AdbDeviceService CreateService(ForwardCountingRunner runner, ICommandTransport transport) =>
        new(new AdbService(runner, "adb", serverLatch: AdbServerLatch.CreateStarted()), commandTransport: transport);

    private sealed class ForwardCountingRunner : IProcessRunner
    {
        private static ProcessExecutionResult Success => new(0, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        public int ForwardCallCount { get; private set; }

        public bool FailForward { get; set; }

        public int? LastForwardHostPort { get; private set; }

        public int? LastForwardDevicePort { get; private set; }

        public Task<ProcessExecutionResult> RunAsync(
            ProcessExecutionRequest request,
            IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            // ForwardTcpAsync 展开为 [-s <serial> forward tcp:<host> tcp:<device>]。
            if (request.Arguments.Count >= 5 && request.Arguments[2] == "forward")
            {
                ForwardCallCount++;
                LastForwardHostPort = ParseTcpPort(request.Arguments[3]);
                LastForwardDevicePort = ParseTcpPort(request.Arguments[4]);
                return Task.FromResult(FailForward
                    ? new ProcessExecutionResult(1, string.Empty, "device offline", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                    : Success);
            }

            return Task.FromResult(Success);
        }

        private static int ParseTcpPort(string argument) =>
            int.Parse(argument.AsSpan("tcp:".Length), System.Globalization.CultureInfo.InvariantCulture);
    }
}

public sealed class AdbDeviceServicePullSubdirectoriesTests
{
    private sealed class PullScriptedRunner(params ProcessExecutionResult[] results) : IProcessRunner
    {
        private int _callCount;

        public List<ProcessExecutionRequest> Requests { get; } = [];

        public Task<ProcessExecutionResult> RunAsync(
            ProcessExecutionRequest request,
            IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(results[Math.Min(_callCount++, results.Length - 1)]);
        }
    }

    private static ProcessExecutionResult Success() =>
        new(0, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public async Task PullSubdirectories_CreatesLocalContainerSoAdbPullSucceeds()
    {
        // adb pull 要求本地父目录已存在；缺失时报「cannot create file/directory ... No such file or directory」，
        // 且该文本会被缺失跳过判断误判为「远端不存在」。容器必须先建好。
        var runner = new PullScriptedRunner(Success(), Success());
        var service = new AdbDeviceService(new AdbService(runner, "adb", serverLatch: AdbServerLatch.CreateStarted()));
        var local = Path.Combine(Path.GetTempPath(), "UnrealKit.Tests", Guid.NewGuid().ToString("N"));

        try
        {
            var result = await service.PullSubdirectoriesAsync(
                DeviceReference.Create("ABC123", TargetPlatform.Android),
                "/sdcard/Android/data/pkg/files/UnrealGame/Game/Game/Saved",
                ["Logs", "Profiling"],
                local);

            Assert.True(result.Succeeded);
            // 两个子目录都成功拉取，容器保留供最终移动使用。
            Assert.True(Directory.Exists(local));
            // 每条 pull 的目标路径都在刚建好的容器目录内。
            Assert.Equal(2, runner.Requests.Count);
            Assert.All(runner.Requests, request => Assert.StartsWith(local + Path.DirectorySeparatorChar, (string)request.Arguments[^1]));
        }
        finally
        {
            if (Directory.Exists(local)) Directory.Delete(local, recursive: true);
        }
    }

    [Fact]
    public async Task PullSubdirectories_AllMissing_RemovesContainerAndReportsSkips()
    {
        // 全部子目录都不存在时，容器必须撤掉，让调用方仍能用「stagingTarget 不存在」判定「没取回任何内容」。
        var missing = new ProcessExecutionResult(
            1, string.Empty, "adb: error: failed to stat remote object '.../Screenshots': No such file or directory",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var runner = new PullScriptedRunner(missing, missing);
        var service = new AdbDeviceService(new AdbService(runner, "adb", serverLatch: AdbServerLatch.CreateStarted()));
        var local = Path.Combine(Path.GetTempPath(), "UnrealKit.Tests", Guid.NewGuid().ToString("N"));
        var messages = new List<OperationProgress>();

        var result = await service.PullSubdirectoriesAsync(
            DeviceReference.Create("ABC123", TargetPlatform.Android),
            "/sdcard/Android/data/pkg/files/UnrealGame/Game/Game/Saved",
            ["Screenshots", "GPUDumps"],
            local,
            new InlineProgress<OperationProgress>(messages.Add));

        Assert.True(result.Succeeded);
        Assert.False(Directory.Exists(local));
        Assert.Equal(2, messages.Count(message => message.Stage == "Skip"));
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
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
