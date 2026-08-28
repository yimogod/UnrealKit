using UnrealKit.Core.CommandChannel;
using UnrealKit.Core.Console;
using UnrealKit.Core.Devices;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;

namespace UnrealKit.Tests;

/// <summary>
/// cvar 读回路径的测试：从 <see cref="ConsoleCommandService"/> 往下经设备层到通道，
/// 以及响应 body 里 <c>ReturnValue</c> 的解析。
/// </summary>
public sealed class ConsoleVariableQueryTests
{
    [Fact]
    public async Task QueryVariableAsync_NumberReturnValue_ParsesNumber()
    {
        var service = CreateService(out _, """{"ReturnValue":80.0}""");

        var value = await service.QueryVariableAsync("device-1", "r.ScreenPercentage", ConsoleVariableType.Number);

        Assert.True(value.Succeeded);
        Assert.Equal(80.0, value.NumberValue);
        Assert.Null(value.BoolValue);
        Assert.Equal("80", value.Display);
    }

    [Fact]
    public async Task QueryVariableAsync_BoolReturnValue_ParsesBool()
    {
        var service = CreateService(out _, """{"ReturnValue":true}""");

        var value = await service.QueryVariableAsync("device-1", "showflag.Fog", ConsoleVariableType.Bool);

        Assert.True(value.Succeeded);
        Assert.True(value.BoolValue);
        Assert.Equal("1", value.Display);
    }

    /// <summary>UE 对 bool cvar 也可能回 0/1 而不是 true/false，两种都要能读。</summary>
    [Theory]
    [InlineData("""{"ReturnValue":1}""", true)]
    [InlineData("""{"ReturnValue":0}""", false)]
    public async Task QueryVariableAsync_BoolAsNumber_ParsesBool(string body, bool expected)
    {
        var service = CreateService(out _, body);

        var value = await service.QueryVariableAsync("device-1", "showflag.Fog", ConsoleVariableType.Bool);

        Assert.True(value.Succeeded);
        Assert.Equal(expected, value.BoolValue);
    }

    [Fact]
    public async Task QueryVariableAsync_PassesVariableNameAndTypeToTransport()
    {
        var service = CreateService(out var transport, """{"ReturnValue":0}""");

        await service.QueryVariableAsync("device-1", "r.ForceLOD", ConsoleVariableType.Number);

        var query = Assert.Single(transport.Queries);
        Assert.Equal("r.ForceLOD", query.VariableName);
        Assert.Equal(ConsoleVariableType.Number, query.VariableType);
    }

    [Fact]
    public async Task QueryVariableAsync_MissingReturnValue_FailsWithReason()
    {
        var service = CreateService(out _, """{"Other":1}""");

        var value = await service.QueryVariableAsync("device-1", "r.ScreenPercentage", ConsoleVariableType.Number);

        Assert.False(value.Succeeded);
        Assert.NotNull(value.Error);
        Assert.Contains("ReturnValue", value.Error);
        Assert.Contains("r.ScreenPercentage", value.Error);
    }

    [Fact]
    public async Task QueryVariableAsync_MalformedJson_FailsWithReason()
    {
        var service = CreateService(out _, "not json at all");

        var value = await service.QueryVariableAsync("device-1", "r.ScreenPercentage", ConsoleVariableType.Number);

        Assert.False(value.Succeeded);
        Assert.Contains("JSON", value.Error);
    }

    /// <summary>数值型 cvar 读到非数值返回值时报具体原因，不静默替成 0。</summary>
    [Fact]
    public async Task QueryVariableAsync_WrongReturnValueKind_FailsWithReason()
    {
        var service = CreateService(out _, """{"ReturnValue":"high"}""");

        var value = await service.QueryVariableAsync("device-1", "r.ScreenPercentage", ConsoleVariableType.Number);

        Assert.False(value.Succeeded);
        Assert.Null(value.NumberValue);
        Assert.Contains("期望数值", value.Error);
    }

    /// <summary>通道失败（UE 未启动）走 UKC 码，读回不抛异常而是带回原因，批量刷新才能继续。</summary>
    [Fact]
    public async Task QueryVariableAsync_TransportFailure_FailsWithDiagnosticCode()
    {
        var deviceService = new Win64DeviceService(
            commandTransport: new FailingCommandTransport(CommandChannelDiagnosticCodes.ConnectFailed));
        var service = new ConsoleCommandService(deviceService);

        var value = await service.QueryVariableAsync("localhost", "showflag.Fog", ConsoleVariableType.Bool);

        Assert.False(value.Succeeded);
        Assert.Contains(CommandChannelDiagnosticCodes.ConnectFailed, value.Error);
    }

    [Fact]
    public async Task QueryConsoleVariableAsync_Win64_DelegatesToTransport()
    {
        var transport = new RecordingCommandTransport { QueryResponseBody = """{"ReturnValue":2}""" };
        var deviceService = new Win64DeviceService(commandTransport: transport);

        var result = await deviceService.QueryConsoleVariableAsync(
            new Win64Device(), "r.ForceLOD", ConsoleVariableType.Number);

        Assert.True(result.Succeeded);
        Assert.Equal("""{"ReturnValue":2}""", result.StandardOutput);
        Assert.Single(transport.Queries);
        // 读回不应经由发送指令的通道方法。
        Assert.Empty(transport.Commands);
    }

    [Fact]
    public async Task QueryConsoleVariableAsync_BlankVariableName_Rejected()
    {
        var deviceService = new Win64DeviceService(commandTransport: new RecordingCommandTransport());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            deviceService.QueryConsoleVariableAsync(new Win64Device(), "  ", ConsoleVariableType.Bool));
    }

    [Fact]
    public void ConsoleVariableValue_FailedDisplay_ShowsError()
    {
        var value = ConsoleVariableValue.Failed("UE 未启动。");

        Assert.False(value.Succeeded);
        Assert.Equal("UE 未启动。", value.Display);
    }

    private static ConsoleCommandService CreateService(out RecordingCommandTransport transport, string responseBody)
    {
        transport = new RecordingCommandTransport { QueryResponseBody = responseBody };
        return new ConsoleCommandService(new StubDeviceService(transport));
    }

    /// <summary>
    /// 只实现读回与发送的设备服务替身，其余能力不参与本组测试。
    /// 平台取 Win64 以避开 adb 端口转发。
    /// </summary>
    private sealed class StubDeviceService(ICommandTransport transport) : IDeviceService
    {
        private static ProcessExecutionResult Success =>
            new(0, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        public TargetPlatform Platform => TargetPlatform.Win64;

        public bool Supports(DeviceCapability capability) => true;

        public Task<IReadOnlyList<IDevice>> ListDevicesAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<IDevice>>([new Win64Device()]);
        public Task<ProcessExecutionResult> CaptureMemoryAsync(IDevice device, string target, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> PullDirectoryAsync(IDevice device, string remotePath, string localDirectory, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> PullSubdirectoriesAsync(IDevice device, string remoteDirectory, IReadOnlyList<string> subdirectoryNames, string localDirectory, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> SendConsoleCommandAsync(IDevice device, string command, string? target = null, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => transport.SendConsoleCommandAsync(command, progress, cancellationToken);
        public Task<ProcessExecutionResult> QueryConsoleVariableAsync(IDevice device, string variableName, ConsoleVariableType variableType, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => transport.QueryConsoleVariableAsync(variableName, variableType, progress, cancellationToken);
        public async IAsyncEnumerable<string> StreamLogAsync(IDevice device, string? filter = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { await Task.CompletedTask; yield break; }
        public Task<ProcessExecutionResult> StartApplicationAsync(IDevice device, string target, string? activity = null, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> StopApplicationAsync(IDevice device, string target, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> PushFileAsync(IDevice device, string localPath, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> DeleteRemoteFileAsync(IDevice device, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> ReadFileAsync(IDevice device, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> InstallApplicationAsync(IDevice device, string localApplicationPath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
    }
}
