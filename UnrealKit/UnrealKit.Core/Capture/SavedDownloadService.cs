using UnrealKit.Core.Devices;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Capture;

/// <summary>
/// 一次下载取回设备 Saved 树的哪一部分。
///
/// 用枚举而不是让调用方传一个自由的子目录名：子目录名是 UE 的固定布局
/// （<c>Saved/Logs</c>），可自由填写的路径会让「取回 Logs」和「取回一个拼错的名字」
/// 无法区分，后者会以「设备上没有该目录」的形式失败，读起来像设备的问题。
/// </summary>
public enum SavedDownloadScope
{
    /// <summary>整个 Saved 目录。</summary>
    All,

    /// <summary>只取 <c>Saved/Logs</c>。</summary>
    Logs
}

/// <summary>一次「把设备上的 UE Saved 数据取回本地」的请求。</summary>
public sealed record SavedDownloadRequest(
    UkitProject Project,
    IDevice Device,
    SavedDownloadScope Scope = SavedDownloadScope.All);

/// <summary>
/// 下载的落地计划。<see cref="LocalDirectory"/> 一定是尚不存在的新目录：
/// 取回设备数据不覆盖上一次的结果，否则两次取回之间的差异会被静默抹掉。
/// </summary>
/// <param name="Scope">本次取回的范围。</param>
/// <param name="DeviceDirectory">设备端源目录。整目录下载时是 Saved 本身，Logs 下载时是其 Logs 子目录。</param>
/// <param name="LocalDirectory">本地落地目录。</param>
public sealed record SavedDownloadPlan(
    SavedDownloadScope Scope,
    string DeviceDirectory,
    string LocalDirectory);

public sealed record SavedDownloadResult(SavedDownloadPlan Plan, int FileCount, long TotalBytes);

public interface ISavedDownloadService
{
    SavedDownloadPlan CreatePlan(SavedDownloadRequest request, DateTimeOffset? requestedAt = null);

    Task<SavedDownloadResult> DownloadAsync(
        SavedDownloadRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 把设备上的 UE Saved 数据取回本地，供用户直接翻看日志、截图、Profiling 文件。
/// 取整个 Saved 还是只取 Logs 由 <see cref="SavedDownloadScope"/> 决定，两者共用同一条落地流程。
///
/// 与 <see cref="CaptureService"/> 的区别是刻意的：采集会写 <c>Content/</c> 下的归档并生成
/// <c>CaptureManifest.json</c>，是可追溯的权威存档；这里只是一次「拿下来看看」，
/// 没有清单也没有采集标签，因此落地在 <c>Saved/</c>（可再生的派生数据）而不是 <c>Content/</c>——
/// 让无清单的目录混进 Content 会让归档结构里出现无法追溯来源的数据。
///
/// 本类不含平台分支：设备端路径由 <see cref="PlatformTarget"/> 提供并用
/// <see cref="PlatformTarget.CombineDevicePath"/> 拼接（分隔符按平台风格），
/// 拉取动作委托 <see cref="IDeviceService"/>。
/// </summary>
public sealed class SavedDownloadService : ISavedDownloadService
{
    /// <summary><c>Saved/</c> 下存放设备 Saved 下载结果的子目录名。</summary>
    public const string DownloadRootName = "DeviceSaved";

    /// <summary>UE 在 Saved 下存放日志的固定子目录名。</summary>
    public const string LogsDirectoryName = "Logs";

    private readonly IDeviceService _deviceService;
    private readonly TimeProvider _timeProvider;

    public SavedDownloadService(IDeviceService deviceService, TimeProvider? timeProvider = null)
    {
        _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public SavedDownloadPlan CreatePlan(SavedDownloadRequest request, DateTimeOffset? requestedAt = null)
    {
        var target = ValidateRequest(request);
        var localTime = requestedAt ?? _timeProvider.GetLocalNow();
        var leafName = ResolveLeafName(request.Scope);

        // 目录名带时间戳、设备标识与范围三者：同一天从多台设备各取一次会按设备区分，
        // 同一秒对同一设备既取 Saved 又取 Logs 会按范围区分。少任何一项都可能撞名，
        // 而撞名会以「下载目录已存在」的形式失败，读起来像是重复操作而非两次不同的取回。
        var folderName = $"{localTime.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)}-{SanitizeDeviceId(request.Device.Id)}-{leafName}";
        var localDirectory = Path.Combine(
            request.Project.SavedDir, DownloadRootName, target.PlatformName, folderName);

        return new SavedDownloadPlan(request.Scope, ResolveDeviceDirectory(target, request.Scope), localDirectory);
    }

    public async Task<SavedDownloadResult> DownloadAsync(
        SavedDownloadRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var leafName = ResolveLeafName(request.Scope);
        if (!_deviceService.Supports(DeviceCapability.PullDirectory))
        {
            throw new DeviceCapabilityNotSupportedException(
                DeviceCapability.PullDirectory,
                request.Device.Platform,
                $"该平台无法取回设备 {leafName} 目录。");
        }

        var plan = CreatePlan(request);
        if (Directory.Exists(plan.LocalDirectory))
        {
            throw new InvalidOperationException(
                $"下载目录已存在，不会覆盖：{plan.LocalDirectory}");
        }

        // 先落到 Intermediate 暂存再整体移动：中途失败或取消时留下的是暂存目录，
        // 不是一个看起来完整、实则只有一半文件的下载结果。
        var stagingRoot = Path.Combine(
            request.Project.IntermediateDir, "SavedDownloadStaging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);

        // 刻意不预先创建该目录：adb pull 在目标目录已存在时会在其下再建一层同名子目录，
        // 目标不存在时才把内容直接放进去。
        var stagingTarget = Path.Combine(stagingRoot, leafName);
        try
        {
            progress?.Report(new OperationProgress(
                "savedDownload", "Pull", 1, 2, $"正在从 {plan.DeviceDirectory} 取回 {leafName} 目录。"));
            await _deviceService.PullDirectoryAsync(
                request.Device, plan.DeviceDirectory, stagingTarget, progress, cancellationToken);

            // 拉取报告成功但本地什么也没有，说明设备端路径不存在或为空。
            // 静默产出一个空目录会让「设备上没有该目录」看起来像「取回成功但没数据」。
            if (!Directory.Exists(stagingTarget))
            {
                throw new InvalidOperationException(
                    $"设备上的 {leafName} 目录没有取回任何内容：{plan.DeviceDirectory}。" +
                    "请确认游戏已在该设备上运行过，且平台配置中的游戏根目录正确。");
            }

            progress?.Report(new OperationProgress(
                "savedDownload", "Finalize", 2, 2, $"正在写入 {plan.LocalDirectory}。"));
            Directory.CreateDirectory(Path.GetDirectoryName(plan.LocalDirectory)!);
            Directory.Move(stagingTarget, plan.LocalDirectory);

            var files = Directory.EnumerateFiles(plan.LocalDirectory, "*", SearchOption.AllDirectories).ToArray();
            var totalBytes = files.Sum(file => new FileInfo(file).Length);
            return new SavedDownloadResult(plan, files.Length, totalBytes);
        }
        finally
        {
            if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true);
        }
    }

    /// <summary>
    /// 该范围对应的目录名。同时用作本地落地目录名的后缀与暂存目录的叶子名，
    /// 因此本地目录名与设备端来源始终对得上。
    /// </summary>
    private static string ResolveLeafName(SavedDownloadScope scope) => scope switch
    {
        SavedDownloadScope.All => PlatformProfile.SavedDirectoryName,
        SavedDownloadScope.Logs => LogsDirectoryName,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "未支持的下载范围。")
    };

    /// <summary>
    /// 该范围对应的设备端源目录。用 <see cref="PlatformTarget.CombineDevicePath"/> 拼接子目录，
    /// 不用 <see cref="Path.Combine"/>——后者在 Windows 主机上会给 Android 路径写入反斜杠。
    /// </summary>
    private static string ResolveDeviceDirectory(PlatformTarget target, SavedDownloadScope scope) => scope switch
    {
        SavedDownloadScope.All => target.SavedRootPath,
        SavedDownloadScope.Logs => target.CombineDevicePath(target.SavedRootPath, LogsDirectoryName),
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "未支持的下载范围。")
    };

    private static PlatformTarget ValidateRequest(SavedDownloadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Project);
        ArgumentNullException.ThrowIfNull(request.Device);

        // 未识别的范围在任何文件系统操作之前就要拒绝，否则会先建暂存目录再失败。
        ResolveLeafName(request.Scope);

        if (!request.Device.IsAvailable)
        {
            throw new InvalidOperationException(
                $"取回设备数据需要状态可用的设备。设备 '{request.Device.Id}' 当前不可用。");
        }

        var devicePlatform = PlatformNames.Parse(request.Device.Platform, nameof(request));
        return request.Project.Settings.ResolveTarget(
            devicePlatform, $"设备 '{request.Device.Id}' 属于 {PlatformNames.ToName(devicePlatform)} 平台。");
    }

    /// <summary>
    /// 把设备 id 变成合法的单段目录名。Wi-Fi 设备的 id 形如 <c>192.168.1.100:5555</c>，
    /// 其中的 <c>:</c> 在 Windows 上不能出现在目录名里。
    /// </summary>
    private static string SanitizeDeviceId(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        var sanitized = new string(deviceId.Trim()
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)
            .ToArray());
        return sanitized is "." or ".." ? "device" : sanitized;
    }
}
