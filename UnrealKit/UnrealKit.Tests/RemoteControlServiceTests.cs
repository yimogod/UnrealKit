using System.Net;
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