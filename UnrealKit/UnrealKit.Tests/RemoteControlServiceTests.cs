using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using UnrealKit.Core.Processes;
using UnrealKit.Core.RemoteControl;

namespace UnrealKit.Tests;

public sealed class RemoteControlServiceTests
{
    [Fact]
    public async Task SendConsoleCommandAsync_PutsExpectedPayloadAndReturnsBody()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{""ok"":true}""", Encoding.UTF8, "application/json")
        });
        var service = new RemoteControlService(new HttpClient(handler));

        var result = await service.SendConsoleCommandAsync(new RemoteControlCommandRequest(
            30010,
            "/Script/Engine.Default__KismetSystemLibrary",
            "ExecuteConsoleCommand",
            "Command",
            "stat fps"));

        Assert.True(result.Succeeded);
        Assert.Contains("""{""ok"":true}""", result.StandardOutput);
        Assert.NotNull(handler.CapturedRequest);
        Assert.Equal(HttpMethod.Put, handler.CapturedRequest.Method);
        Assert.Equal(new Uri("http://127.0.0.1:30010/remote/object/call"), handler.CapturedRequest.RequestUri);

        var payload = handler.CapturedContent;
        using var document = JsonDocument.Parse(payload!);
        var root = document.RootElement;
        Assert.Equal("/Script/Engine.Default__KismetSystemLibrary", root.GetProperty("objectPath").GetString());
        Assert.Equal("ExecuteConsoleCommand", root.GetProperty("functionName").GetString());
        Assert.Equal("stat fps", root.GetProperty("parameters").GetProperty("Command").GetString());
        Assert.True(root.GetProperty("generateTransaction").GetBoolean());
    }

    [Fact]
    public async Task SendConsoleCommandAsync_NonSuccess_ThrowsRemoteControlExceptionWithResult()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom", Encoding.UTF8, "text/plain")
        });
        var service = new RemoteControlService(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<RemoteControlException>(() =>
            service.SendConsoleCommandAsync(new RemoteControlCommandRequest(
                30010,
                "/Script/Engine.Default__KismetSystemLibrary",
                "ExecuteConsoleCommand",
                "Command",
                "stat fps")));

        Assert.Equal(500, exception.Result.ExitCode);
        Assert.Contains("boom", exception.Result.StandardOutput);
    }

    [Fact]
    public async Task SendConsoleCommandAsync_InvalidRequest_RejectsBeforeNetworkCall()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = new RemoteControlService(new HttpClient(handler));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.SendConsoleCommandAsync(new RemoteControlCommandRequest(
                0,
                "/Script/Engine.Default__KismetSystemLibrary",
                "ExecuteConsoleCommand",
                "Command",
                "stat fps")));

        Assert.Null(handler.CapturedRequest);
    }

    [Fact]
    public async Task SendConsoleCommandAsync_ConnectionRefused_SurfacesInnerExceptionDetail()
    {
        // HttpRequestException.Message 是「An error occurred while sending the request.」这类无信息量的
        // 通用文案；真正原因（连接被拒绝等）在 InnerException，必须透传到错误信息里。
        var socketError = new SocketException((int)SocketError.ConnectionRefused);
        var handler = new ThrowingHttpMessageHandler(new HttpRequestException(
            "An error occurred while sending the request.", socketError));
        var service = new RemoteControlService(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<RemoteControlException>(() =>
            service.SendConsoleCommandAsync(new RemoteControlCommandRequest(
                30010,
                "/Script/Engine.Default__KismetSystemLibrary",
                "ExecuteConsoleCommand",
                "Command",
                "stat fps")));

        Assert.Contains("Remote Control request failed", exception.Message);
        Assert.Contains(socketError.Message, exception.Message);
    }

    [Fact]
    public async Task SendConsoleCommandAsync_Timeout_ThrowsRemoteControlExceptionNotTaskCanceled()
    {
        // HttpClient 超时表现为 TaskCanceledException，必须被包成 RemoteControlException，
        // 否则会绕过 CLI 的可预期失败处理直接打印堆栈。
        var handler = new HangingHttpMessageHandler();
        var service = new RemoteControlService(new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(50) });

        var exception = await Assert.ThrowsAsync<RemoteControlException>(() =>
            service.SendConsoleCommandAsync(new RemoteControlCommandRequest(
                30010,
                "/Script/Engine.Default__KismetSystemLibrary",
                "ExecuteConsoleCommand",
                "Command",
                "stat fps")));

        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(-1, exception.Result.ExitCode);
    }

    [Fact]
    public async Task SendConsoleCommandAsync_CallerCancellation_StaysOperationCanceled()
    {
        // 调用方主动取消与超时必须区分：取消保持取消语义，不伪装成 Remote Control 失败。
        var handler = new HangingHttpMessageHandler();
        var service = new RemoteControlService(new HttpClient(handler));
        using var cts = new CancellationTokenSource();

        var task = service.SendConsoleCommandAsync(
            new RemoteControlCommandRequest(
                30010,
                "/Script/Engine.Default__KismetSystemLibrary",
                "ExecuteConsoleCommand",
                "Command",
                "stat fps"),
            progress: null,
            cancellationToken: cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    private sealed class HangingHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class ThrowingHttpMessageHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw exception;
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }

        public string? CapturedContent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            CapturedContent = await request.Content!.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }
}
