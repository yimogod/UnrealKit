using System.Net;
using System.Net.Sockets;
using System.Text;
using UnrealKit.Core.CommandChannel;

namespace UnrealKit.Tests;

/// <summary>
/// <see cref="TcpCommandTransport"/> 的金样测试：用本地 <see cref="TcpListener"/> 假冒
/// UE 侧命令插件，覆盖成功、命令失败、连接被拒绝、协议异常四条路径。
/// </summary>
public sealed class TcpCommandTransportTests
{
    [Fact]
    public async Task SendConsoleCommandAsync_Ok_ReturnsOutputAsStandardOutput()
    {
        await using var server = FakeCommandServer.Respond("""{"ok":true,"output":"Frame: 16.6ms"}""");
        var transport = new TcpCommandTransport(server.Port);

        var result = await transport.SendConsoleCommandAsync("stat unit");

        Assert.True(result.Succeeded);
        Assert.Equal("Frame: 16.6ms", result.StandardOutput);
        Assert.Empty(result.StandardError);
        // 请求必须是「命令 + 换行」，UE 侧按行分帧读取。
        Assert.Equal("stat unit\n", await server.GetReceivedAsync());
    }

    [Fact]
    public async Task SendConsoleCommandAsync_MissingOutputField_SucceedsWithEmptyOutput()
    {
        // output 是可选字段：不产出文本的命令（如 `r.ScreenPercentage 50`）也算成功执行。
        await using var server = FakeCommandServer.Respond("""{"ok":true}""");
        var transport = new TcpCommandTransport(server.Port);

        var result = await transport.SendConsoleCommandAsync("r.ScreenPercentage 50");

        Assert.True(result.Succeeded);
        Assert.Empty(result.StandardOutput);
    }

    [Fact]
    public async Task SendConsoleCommandAsync_NotOk_ThrowsCommandFailedWithReason()
    {
        await using var server = FakeCommandServer.Respond("""{"ok":false,"error":"Unknown command: stat nonsense"}""");
        var transport = new TcpCommandTransport(server.Port);

        var exception = await Assert.ThrowsAsync<CommandTransportException>(
            () => transport.SendConsoleCommandAsync("stat nonsense"));

        Assert.Equal(CommandChannelDiagnosticCodes.CommandFailed, exception.Code);
        // 失败原因必须透传，不静默：约定要求失败要具体。
        Assert.Contains("Unknown command: stat nonsense", exception.Message);
        Assert.Contains("Unknown command: stat nonsense", exception.Result.StandardError);
        Assert.False(exception.Result.Succeeded);
    }

    [Fact]
    public async Task SendConsoleCommandAsync_NotOkWithoutError_StatesThatUeGaveNoReason()
    {
        // 「UE 说失败但没给原因」与「UE 给了原因」要能区分，不能显示成空白失败。
        await using var server = FakeCommandServer.Respond("""{"ok":false}""");
        var transport = new TcpCommandTransport(server.Port);

        var exception = await Assert.ThrowsAsync<CommandTransportException>(
            () => transport.SendConsoleCommandAsync("stat unit"));

        Assert.Equal(CommandChannelDiagnosticCodes.CommandFailed, exception.Code);
        Assert.Contains("未给出失败原因", exception.Message);
    }

    [Fact]
    public async Task SendConsoleCommandAsync_ConnectionRefused_ThrowsConnectFailedWithHint()
    {
        var port = FakeCommandServer.ReserveUnusedPort();
        var transport = new TcpCommandTransport(port);

        var exception = await Assert.ThrowsAsync<CommandTransportException>(
            () => transport.SendConsoleCommandAsync("stat unit"));

        Assert.Equal(CommandChannelDiagnosticCodes.ConnectFailed, exception.Code);
        Assert.Contains(port.ToString(System.Globalization.CultureInfo.InvariantCulture), exception.Message);
        // 提示要指向可操作的三个原因：UE 未启动、adb forward 未生效、端口配置不一致。
        Assert.Contains("adb forward", exception.Message);
    }

    [Fact]
    public async Task SendConsoleCommandAsync_NonJsonResponse_ThrowsProtocolErrorKeepingRawLine()
    {
        await using var server = FakeCommandServer.Respond("not json at all");
        var transport = new TcpCommandTransport(server.Port);

        var exception = await Assert.ThrowsAsync<CommandTransportException>(
            () => transport.SendConsoleCommandAsync("stat unit"));

        Assert.Equal(CommandChannelDiagnosticCodes.ProtocolError, exception.Code);
        // 原始响应要保留：没有它就无法判断对端到底回了什么。
        Assert.Equal("not json at all", exception.Result.StandardOutput);
    }

    [Fact]
    public async Task SendConsoleCommandAsync_JsonWithoutOkField_ThrowsProtocolErrorNotSuccess()
    {
        // 缺 ok 不能当成功：会让一条根本没执行的命令显示为已生效。
        await using var server = FakeCommandServer.Respond("""{"output":"something"}""");
        var transport = new TcpCommandTransport(server.Port);

        var exception = await Assert.ThrowsAsync<CommandTransportException>(
            () => transport.SendConsoleCommandAsync("stat unit"));

        Assert.Equal(CommandChannelDiagnosticCodes.ProtocolError, exception.Code);
        Assert.Contains("ok", exception.Message);
    }

    [Fact]
    public async Task SendConsoleCommandAsync_ClosedWithoutNewline_ThrowsProtocolError()
    {
        // 半条 JSON 不能拿去解析，否则「响应被截断」会表现成「命令执行失败」。
        await using var server = FakeCommandServer.Respond("""{"ok":true,"output":"partia""", appendNewline: false);
        var transport = new TcpCommandTransport(server.Port);

        var exception = await Assert.ThrowsAsync<CommandTransportException>(
            () => transport.SendConsoleCommandAsync("stat unit"));

        Assert.Equal(CommandChannelDiagnosticCodes.ProtocolError, exception.Code);
        Assert.Contains("关闭了连接", exception.Message);
    }

    [Fact]
    public async Task SendConsoleCommandAsync_NoResponse_TimesOutAsProtocolError()
    {
        await using var server = FakeCommandServer.Silent();
        var transport = new TcpCommandTransport(server.Port, responseTimeout: TimeSpan.FromMilliseconds(150));

        var exception = await Assert.ThrowsAsync<CommandTransportException>(
            () => transport.SendConsoleCommandAsync("stat unit"));

        Assert.Equal(CommandChannelDiagnosticCodes.ProtocolError, exception.Code);
        Assert.Contains("超时", exception.Message);
    }

    [Fact]
    public async Task SendConsoleCommandAsync_CallerCancellation_StaysOperationCanceled()
    {
        // 调用方主动取消与超时必须区分：取消保持取消语义，不伪装成通道失败。
        await using var server = FakeCommandServer.Silent();
        var transport = new TcpCommandTransport(server.Port);
        using var cts = new CancellationTokenSource();

        var task = transport.SendConsoleCommandAsync("stat unit", progress: null, cancellationToken: cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Theory]
    [InlineData("stat unit\nstat fps")]
    [InlineData("stat unit\r\nstat fps")]
    public async Task SendConsoleCommandAsync_MultilineCommand_RejectedBeforeConnecting(string command)
    {
        // 协议以换行分帧，含换行的命令会被对端读成两条，后半截当作未知命令执行。
        var transport = new TcpCommandTransport(FakeCommandServer.ReserveUnusedPort());

        await Assert.ThrowsAsync<ArgumentException>(() => transport.SendConsoleCommandAsync(command));
    }

    [Fact]
    public void Constructor_RejectsOutOfRangePort()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TcpCommandTransport(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TcpCommandTransport(65536));
    }

    [Fact]
    public void Kind_And_Port_ReportTheConfiguredChannel()
    {
        var transport = new TcpCommandTransport(41234);

        Assert.Equal(CommandTransportKind.Tcp, transport.Kind);
        Assert.Equal(41234, transport.Port);
    }

    /// <summary>
    /// 假冒 UE 侧的 TCP 命令插件：接受一个连接、读一行请求、按配置回一行响应。
    /// 只监听回环，端口由系统分配以免与真实服务或并行测试冲突。
    /// </summary>
    private sealed class FakeCommandServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _shutdown = new();
        private readonly Task _serverLoop;
        private readonly TaskCompletionSource<string> _received =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private FakeCommandServer(string? response, bool appendNewline)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serverLoop = RunAsync(response, appendNewline);
        }

        internal int Port { get; }

        /// <summary>回一行指定响应的服务器。</summary>
        internal static FakeCommandServer Respond(string response, bool appendNewline = true) =>
            new(response, appendNewline);

        /// <summary>接受连接但永不回话，用于覆盖响应超时与取消。</summary>
        internal static FakeCommandServer Silent() => new(null, false);

        /// <summary>取一个当前无人监听的回环端口，用于覆盖「连接被拒绝」。</summary>
        internal static int ReserveUnusedPort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        /// <summary>服务器收到的原始请求字节（含换行），用于校验请求格式。</summary>
        internal Task<string> GetReceivedAsync() => _received.Task;

        private async Task RunAsync(string? response, bool appendNewline)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_shutdown.Token);
                var stream = client.GetStream();

                var buffer = new byte[1024];
                var read = await stream.ReadAsync(buffer, _shutdown.Token);
                _received.TrySetResult(Encoding.UTF8.GetString(buffer, 0, read));

                if (response is null)
                {
                    // 静默服务器：保持连接直到测试释放，让客户端走到响应超时。
                    // 必须响应 _shutdown，否则 DisposeAsync 会一直等这个任务。
                    await Task.Delay(Timeout.Infinite, _shutdown.Token);
                    return;
                }

                var payload = Encoding.UTF8.GetBytes(appendNewline ? response + "\n" : response);
                await stream.WriteAsync(payload, _shutdown.Token);
                await stream.FlushAsync(_shutdown.Token);
            }
            catch (Exception exception)
            {
                // 测试释放服务器会让 Accept/Read 抛出，这是正常收尾而不是失败。
                _received.TrySetException(exception);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _shutdown.CancelAsync();
            _listener.Stop();
            try
            {
                await _serverLoop;
            }
            catch
            {
                // 收尾异常已在 RunAsync 中记录，此处无需再传播。
            }

            _shutdown.Dispose();
        }
    }
}
