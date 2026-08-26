using UnrealKit.Core.Devices;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Unreal;


/// <summary>
/// 把设备上的 UE Saved 数据取回本地，供用户直接翻看日志、截图、Profiling 文件。
/// 取整个 Saved 还是只取 Logs 由 <see cref="UnealSavedScope"/> 决定
///
/// 本类不含平台分支：设备端路径由 <see cref="PlatformTarget"/> 提供并用
/// <see cref="PlatformTarget.CombineDevicePath"/> 拼接（分隔符按平台风格）
/// 拉取动作委托 <see cref="IDeviceService"/>。
/// </summary>
public sealed class UnrealSavedService
{
    /// <summary><c>Saved/</c> 下存放设备 Saved 下载结果的子目录名。</summary>
    public const string DownloadRootName = "Device";

    private readonly IDeviceService _deviceService;
    private readonly TimeProvider _timeProvider;

    public UnrealSavedService(IDeviceService deviceService, TimeProvider? timeProvider = null)
    {
        _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public UnrealSavedPullPlan CreatePlan(UnrealSavedPullRequest request, DateTimeOffset? requestedAt = null)
    {
        var target = ValidateRequest(request);
        var localTime = requestedAt ?? _timeProvider.GetLocalNow();
        var leafName = UnrealModels.GetScopeName(request.Scope);

        // 目录名带时间戳、设备标识与范围三者：同一天从多台设备各取一次会按设备区分，
        // 同一秒对同一设备既取 Saved 又取 Logs 会按范围区分。少任何一项都可能撞名，
        // 而撞名会以「下载目录已存在」的形式失败，读起来像是重复操作而非两次不同的取回。
        var folderName = $"{localTime.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)}-{SanitizeDeviceId(request.Device.Id)}-{leafName}";
        var localDirectory = Path.Combine(
            request.Project.SavedDir, DownloadRootName, target.PlatformName, folderName);

        var devicePath = UnrealModels.ResolveDeviceDirectory(target, request.Scope);

        return new UnrealSavedPullPlan(request.Scope, devicePath, localDirectory);
    }

    public async Task<UnrealSavedPullResult> DownloadAsync(
        UnrealSavedPullRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var leafName = UnrealModels.GetScopeName(request.Scope);
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
            return new UnrealSavedPullResult(plan, files.Length, totalBytes);
        }
        finally
        {
            if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true);
        }
    }


    private static PlatformTarget ValidateRequest(UnrealSavedPullRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Project);
        ArgumentNullException.ThrowIfNull(request.Device);

        if (!request.Device.IsAvailable)
        {
            throw new InvalidOperationException(
                $"取回设备数据需要状态可用的设备。设备 '{request.Device.Id}' 当前不可用。");
        }

        // 未识别的范围在任何文件系统操作之前就要拒绝
        UnrealModels.GetScopeName(request.Scope);

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
