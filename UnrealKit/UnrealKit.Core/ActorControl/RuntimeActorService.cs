using UnrealKit.Core.Devices;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Unreal;

namespace UnrealKit.Core.ActorControl;

/// <summary>
/// Actor 列表刷新流程：先通过设备的 Remote Control 指令通道发送 <c>obj list class=Actor</c>，
/// 再下载 UE Saved/Logs，并从其中唯一的非 backup 日志解析列表。
/// </summary>
public sealed class RuntimeActorService
{
    public const string ListActorsCommand = "obj list class=Actor";

    private readonly IDeviceService _deviceService;
    private readonly RuntimeActorLogParser _parser;

    public RuntimeActorService(IDeviceService deviceService, RuntimeActorLogParser? parser = null)
    {
        _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
        _parser = parser ?? new RuntimeActorLogParser();
    }

    public async Task<RuntimeActorRefreshResult> RefreshAsync(
        RuntimeActorRefreshRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_deviceService.Supports(DeviceCapability.SendConsoleCommand))
        {
            throw new DeviceCapabilityNotSupportedException(
                DeviceCapability.SendConsoleCommand,
                request.Device.Platform,
                "Actor 列表需要 UE Web Remote Control 指令通道。");
        }

        progress?.Report(new OperationProgress("actor-refresh", "Listing", 1, 3, "正在请求游戏内 Actor 列表。"));
        await _deviceService.SendConsoleCommandAsync(
            request.Device, ListActorsCommand, progress: progress, cancellationToken: cancellationToken);

        progress?.Report(new OperationProgress("actor-refresh", "DownloadingLogs", 2, 3, "正在下载 UE Saved/Logs。"));
        var download = await new UnrealSavedService(_deviceService).DownloadAsync(
            new UnrealSavedPullRequest(request.Project, request.Device, UnealSavedScope.Logs), progress, cancellationToken);

        var currentLogPath = ResolveCurrentLogPath(download.Plan.LocalDirectory);
        progress?.Report(new OperationProgress("actor-refresh", "Parsing", 3, 3, $"正在解析 {currentLogPath}。"));
        var parseResult = await _parser.ParseFileAsync(currentLogPath, cancellationToken);
        return new RuntimeActorRefreshResult(download, currentLogPath, parseResult);
    }

    internal static string ResolveCurrentLogPath(string logsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDirectory);
        var candidates = Directory.EnumerateFiles(logsDirectory, "*.log", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).Contains("backup", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidDataException(
                $"在已下载的 Logs 目录中找不到不含 'backup' 的 .log 文件：{logsDirectory}。"),
            _ => throw new InvalidDataException(
                $"在已下载的 Logs 目录中找到多个不含 'backup' 的当前日志，无法隐式选择：{string.Join("; ", candidates)}。")
        };
    }
}
