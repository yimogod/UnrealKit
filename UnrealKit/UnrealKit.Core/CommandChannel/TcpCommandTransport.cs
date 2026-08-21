using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;

namespace UnrealKit.Core.CommandChannel;

/// <summary>
/// 走 UE 侧自研 TCP 命令插件的指令通道，服务 Android。
///
/// 协议（见 <c>Doc/方案B-UE客户端控制台命令通道.md</c>）：
/// <list type="bullet">
///   <item>请求：单行 UTF-8 命令文本 + <c>\n</c>。</item>
///   <item>响应：单行 UTF-8 JSON + <c>\n</c>，
///     <c>{"ok":true,"output":"..."}</c> 或 <c>{"ok":false,"error":"..."}</c>。</item>
/// </list>
///
/// 只连 <c>127.0.0.1</c>：Android 侧经 <c>adb forward</c> 后本机回环即是设备端口，
/// 不需要（也不应该）连设备的局域网地址。
/// </summary>
public sealed class TcpCommandTransport : ICommandTransport
{
    /// <summary>建立连接的等待上限。UE 未启动时连接会立刻被拒绝，超时只用于兜住无响应的中间层。</summary>
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(5);

    /// <summary>等待响应行的上限。控制台命令是同步执行的，超过这个时间说明对端没按协议回话。</summary>
    public static readonly TimeSpan DefaultResponseTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 响应行的字节上限。UE 侧的 <c>output</c> 可能很长（<c>stat</c> 类命令），
    /// 但必须有界：无界读取会让一个不按协议、只顾发送的对端把内存吃光。
    /// </summary>
    private const int MaxResponseBytes = 1024 * 1024;

    private const string LoopbackHost = "127.0.0.1";

    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _responseTimeout;
    private readonly TimeProvider _timeProvider;

    public TcpCommandTransport(
        int port = CommandChannelOptions.DefaultTcpPort,
        TimeSpan? connectTimeout = null,
        TimeSpan? responseTimeout = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        Port = port;
        _connectTimeout = connectTimeout ?? DefaultConnectTimeout;
        _responseTimeout = responseTimeout ?? DefaultResponseTimeout;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public CommandTransportKind Kind => CommandTransportKind.Tcp;

    public int Port { get; }

    public async Task<ProcessExecutionResult> SendConsoleCommandAsync(
        string command,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        // 命令必须是单行：协议以 \n 分帧，含换行的命令会被对端读成两条，
        // 后半截作为一条未知命令执行。这里显式拒绝而不是悄悄替换换行符。
        if (command.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            throw new ArgumentException(
                "控制台指令不能包含换行符：TCP 命令通道以换行分帧，多行命令请逐条发送。",
                nameof(command));
        }

        progress?.Report(new OperationProgress(
            "command-channel-send",
            "Sending",
            null,
            null,
            $"通过 TCP 命令通道发送控制台指令 ({LoopbackHost}:{Port}): {command}"));

        var startedAt = _timeProvider.GetUtcNow();
        using var client = new TcpClient();

        await ConnectAsync(client, startedAt, cancellationToken);
        var response = await ExchangeAsync(client, command, startedAt, cancellationToken);
        return BuildResult(response, command, startedAt);
    }

    private async Task ConnectAsync(TcpClient client, DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(_connectTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            await client.ConnectAsync(LoopbackHost, Port, linked.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 调用方主动取消：保持取消语义上抛，不伪装成通道失败。
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw Failure(
                CommandChannelDiagnosticCodes.ConnectFailed,
                $"连接 TCP 命令通道超时（{_connectTimeout.TotalSeconds:F0}s）: {LoopbackHost}:{Port}。{ConnectHint}",
                exception,
                startedAt);
        }
        catch (SocketException exception)
        {
            throw Failure(
                CommandChannelDiagnosticCodes.ConnectFailed,
                $"连接 TCP 命令通道失败: {LoopbackHost}:{Port} ({exception.SocketErrorCode})。{ConnectHint}",
                exception,
                startedAt);
        }
    }

    /// <summary>
    /// 发送命令行并读回响应行。连接已建立后的 I/O 失败归为协议错误而不是连接失败——
    /// 端口通了却半途断开，问题在对端的实现或状态，与「没连上」的排查方向不同。
    /// </summary>
    private async Task<string> ExchangeAsync(
        TcpClient client,
        string command,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(_responseTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            var stream = client.GetStream();
            var payload = Encoding.UTF8.GetBytes(command + "\n");
            await stream.WriteAsync(payload, linked.Token);
            await stream.FlushAsync(linked.Token);

            return await ReadLineAsync(stream, linked.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw Failure(
                CommandChannelDiagnosticCodes.ProtocolError,
                $"等待 TCP 命令通道响应超时（{_responseTimeout.TotalSeconds:F0}s）: {LoopbackHost}:{Port}。" +
                "UE 侧插件收到命令后必须回一行 JSON 响应。",
                exception,
                startedAt);
        }
        catch (InvalidDataException exception)
        {
            // 分帧失败（连接提前关闭、行超长）：消息本身已说明情况，直接作为协议错误上报。
            throw Failure(
                CommandChannelDiagnosticCodes.ProtocolError,
                exception.Message,
                exception,
                startedAt);
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            throw Failure(
                CommandChannelDiagnosticCodes.ProtocolError,
                $"TCP 命令通道读写失败: {exception.Message}",
                exception,
                startedAt);
        }
    }

    /// <summary>
    /// 读取一行（不含结尾 <c>\n</c>）。自行分帧而不用 <c>StreamReader.ReadLineAsync</c>：
    /// 后者对行长无上限，一个只顾发送、不发换行的对端会让读取无限增长。
    /// </summary>
    private static async Task<string> ReadLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var line = new MemoryStream();

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                // 对端在发出换行前就关闭了连接。已读到的字节可能是半条 JSON，
                // 不能当成完整响应解析，否则「响应被截断」会表现成「命令执行失败」。
                throw new InvalidDataException(line.Length == 0
                    ? "TCP 命令通道在返回任何数据前就关闭了连接。"
                    : $"TCP 命令通道在响应结束前关闭了连接，已收到 {line.Length} 字节且不含换行结尾。");
            }

            var newlineIndex = Array.IndexOf(buffer, (byte)'\n', 0, read);
            var copyLength = newlineIndex >= 0 ? newlineIndex : read;
            if (line.Length + copyLength > MaxResponseBytes)
            {
                throw new InvalidDataException(
                    $"TCP 命令通道的响应超过 {MaxResponseBytes / 1024} KiB 仍未结束，已放弃读取。");
            }

            line.Write(buffer, 0, copyLength);
            if (newlineIndex >= 0)
            {
                // 只读一行：一次请求对应一次响应，剩余字节随连接关闭一起丢弃。
                return Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r');
            }
        }
    }

    /// <summary>
    /// 把协议响应映射到 <see cref="ProcessExecutionResult"/>：
    /// <c>ok=true</c> → 退出码 0 + <c>output</c> 走标准输出；
    /// <c>ok=false</c> → 非零退出码 + <c>error</c> 走标准错误并抛出。
    /// </summary>
    private ProcessExecutionResult BuildResult(string response, string command, DateTimeOffset startedAt)
    {
        var (ok, output, error) = ParseResponse(response, startedAt);
        var completedAt = _timeProvider.GetUtcNow();

        if (!ok)
        {
            var detail = string.IsNullOrWhiteSpace(error)
                ? "UE 未给出失败原因。"
                : error;
            var message = $"[{CommandChannelDiagnosticCodes.CommandFailed}] UE 执行控制台指令失败: {command}。{detail}";
            throw new CommandTransportException(
                CommandChannelDiagnosticCodes.CommandFailed,
                message,
                new ProcessExecutionResult(1, output, detail, startedAt, completedAt));
        }

        return new ProcessExecutionResult(0, output, string.Empty, startedAt, completedAt);
    }

    /// <summary>
    /// 解析响应行。缺少 <c>ok</c> 字段按协议错误处理，不默认视为成功——
    /// 把无法确认的响应读成成功，会让一条根本没执行的命令显示为已生效。
    /// </summary>
    private (bool Ok, string Output, string Error) ParseResponse(string response, DateTimeOffset startedAt)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            throw Failure(
                CommandChannelDiagnosticCodes.ProtocolError,
                "TCP 命令通道返回了空响应行，预期为单行 JSON。",
                null,
                startedAt,
                rawResponse: response);
        }

        try
        {
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"响应的 JSON 根节点是 {root.ValueKind}，预期为对象。");
            }

            if (!root.TryGetProperty("ok", out var okElement)
                || okElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new InvalidDataException("响应缺少布尔字段 'ok'。");
            }

            return (
                okElement.GetBoolean(),
                ReadText(root, "output"),
                ReadText(root, "error"));
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            throw Failure(
                CommandChannelDiagnosticCodes.ProtocolError,
                $"无法解析 TCP 命令通道的响应: {exception.Message}",
                exception,
                startedAt,
                rawResponse: response);
        }
    }

    /// <summary>
    /// 取可选文本字段。非字符串类型（如数字或对象）保留其 JSON 原文而不是丢弃：
    /// 静默变空串会让「UE 回了内容但格式不对」看起来像「UE 什么都没回」。
    /// </summary>
    private static string ReadText(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return string.Empty;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            JsonValueKind.String => element.GetString() ?? string.Empty,
            _ => element.GetRawText()
        };
    }

    private static string ConnectHint =>
        "请确认：UE 客户端已启动且已启用 TCP 命令插件；Android 上 `adb forward` 已生效；" +
        "配置的 CommandTcpPort 与插件监听端口一致。";

    private CommandTransportException Failure(
        string code,
        string message,
        Exception? exception,
        DateTimeOffset startedAt,
        string? rawResponse = null) =>
        new(
            code,
            $"[{code}] {message}",
            new ProcessExecutionResult(
                -1,
                rawResponse ?? string.Empty,
                exception?.Message ?? message,
                startedAt,
                _timeProvider.GetUtcNow()),
            exception);
}
