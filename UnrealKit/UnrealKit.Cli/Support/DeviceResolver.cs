using UnrealKit.Core.Adb;
using UnrealKit.Core.Devices;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;

namespace UnrealKit.Cli;

/// <summary>
/// adb 服务构造与设备选择。歧义输入一律报错并列出候选，不取「默认第一台设备」。
/// </summary>
/// <summary>
/// 一次操作的设备解析结果。同时带出 <see cref="Target"/>，让调用方拿到该平台的
/// 进程标识与路径，而不必再从工程配置里按平台分支取一遍。
/// </summary>
internal sealed record ResolvedDeviceTarget(
    IDeviceService DeviceService,
    IDevice Device,
    PlatformTarget Target)
{
    internal string DeviceId => Device.Id;
}

internal static class DeviceResolver
{
    /// <summary>
    /// adb server 启动标记，按 adb 路径共享。一条 CLI 命令内部可能构造多个 AdbService
    /// （跨平台枚举、解析设备后再建设备服务），标记若随实例走就会各自 start-server 一次。
    /// </summary>
    private static readonly Dictionary<string, AdbServerLatch> ServerLatches =
        new(StringComparer.OrdinalIgnoreCase);

    internal static AdbService CreateAdbService(string? explicitPath, string? projectAdbPath = null, bool streamOutput = true)
    {
        var resolvedPath = new AdbPathResolver().ResolveRequired(explicitPath, projectAdbPath);
        return new AdbService(
            new ProcessRunner(),
            resolvedPath,
            streamOutput ? new Progress<ProcessOutput>(CliOutput.WriteProcessOutput) : null,
            GetServerLatch(resolvedPath));
    }

    /// <summary>
    /// 取该 adb 路径对应的启动标记。不同的 adb 可执行文件可能对应不同版本的 server，
    /// 因此按路径分开，不共用同一个标记。
    /// </summary>
    private static AdbServerLatch GetServerLatch(string resolvedPath)
    {
        lock (ServerLatches)
        {
            if (!ServerLatches.TryGetValue(resolvedPath, out var latch))
            {
                latch = new AdbServerLatch();
                ServerLatches[resolvedPath] = latch;
            }

            return latch;
        }
    }

    /// <summary>
    /// 解析本次操作的设备服务与设备标识。
    ///
    /// 目标平台由 <c>--platform</c> 或所选设备决定，不取自工程配置——同一工程可以同时
    /// 配置多个平台，「本次打哪个」是每次调用的显式选择。歧义输入一律报错并列出候选。
    /// </summary>
    internal static async Task<ResolvedDeviceTarget> ResolveDeviceTargetAsync(
        UkitProject project,
        string[] options,
        string? adbPath,
        bool streamOutput = true)
    {
        var device = await ResolveDeviceAsync(project, options, adbPath, streamOutput);
        var platform = PlatformNames.Parse(device.Platform, nameof(device));
        var target = project.Settings.ResolveTarget(platform, $"设备 '{device.Id}' 属于 {device.Platform} 平台。");
        return new ResolvedDeviceTarget(CreateDeviceService(project, device, adbPath, streamOutput), device, target);
    }

    /// <summary>
    /// 选出本次操作的设备。<c>--platform</c> 缺省时跨全部平台查找，
    /// 命中多台时报错并列出带平台的候选，不取「默认第一台」。
    /// </summary>
    internal static async Task<IDevice> ResolveDeviceAsync(
        UkitProject project,
        string[] options,
        string? adbPath,
        bool streamOutput = true)
    {
        var requestedPlatform = CliOptions.GetOptional(options, "--platform") is { } platformValue
            ? PlatformNames.Parse(platformValue, "--platform")
            : (TargetPlatform?)null;
        var requestedDevice = CliOptions.GetOptional(options, "--device");

        // 指定了平台就只枚举该平台：跨平台枚举会为一个用不到的平台去起 adb，
        // 把「adb 未安装」变成 Win64 操作的失败原因。
        var result = requestedPlatform is { } platform
            ? await CreateDeviceProvider(adbPath, project, platform, streamOutput).ListDevicesAsync()
            : await CreateDeviceProvider(adbPath, project, streamOutput: streamOutput).ListDevicesAsync();

        var available = result.Devices.Where(device => device.IsAvailable).ToArray();
        if (requestedDevice is not null && !string.Equals(requestedDevice, "auto", StringComparison.OrdinalIgnoreCase))
        {
            var matches = available
                .Where(device => string.Equals(device.Id, requestedDevice, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return matches.Length switch
            {
                1 => matches[0],
                0 => throw new AdbDeviceSelectionException(
                    $"未找到可用设备 '{requestedDevice}'。{DescribeCandidates(available, result.Failures)}"),
                // 同一标识在多个平台上出现时必须显式指定平台，不能替用户挑一个。
                _ => throw new AdbDeviceSelectionException(
                    $"设备标识 '{requestedDevice}' 在多个平台上都存在: " +
                    $"{string.Join(", ", matches.Select(device => device.Platform))}。请用 --platform 指定平台。")
            };
        }

        return available.Length switch
        {
            1 => available[0],
            0 => throw new AdbDeviceSelectionException($"没有可用设备。{DescribeCandidates(available, result.Failures)}"),
            _ => throw new AdbDeviceSelectionException(
                $"有多台可用设备，请用 --device <id> 指定一台{(requestedPlatform is null ? "，或用 --platform 限定平台" : string.Empty)}。" +
                $"{DescribeCandidates(available, result.Failures)}")
        };
    }

    /// <summary>为指定设备构造设备服务。平台取自设备本身，不取自工程配置。</summary>
    internal static IDeviceService CreateDeviceService(
        UkitProject project,
        IDevice device,
        string? adbPath,
        bool streamOutput = true)
    {
        // 只有 Android 需要 adb；为 Win64 构造 AdbService 会把「adb 未安装」
        // 变成本机操作的失败原因。
        var adbService = PlatformNames.Parse(device.Platform, nameof(device)) == TargetPlatform.Android
            ? CreateAdbService(adbPath, project.Settings.Android?.AdbPath, streamOutput)
            : null;
        return new DeviceServiceFactory(adbService, new ProcessRunner())
            .CreateForDevice(device, project.Settings);
    }

    private static string DescribeCandidates(
        IReadOnlyList<IDevice> available,
        IReadOnlyList<DeviceDiscoveryFailure> failures)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(available.Count == 0
            ? "当前无可用设备。"
            : $"可用设备: {string.Join(", ", available.Select(device => $"{device.Id} ({device.Platform})"))}。");

        // 枚举失败必须保留：缺少 adb 与「确实没插设备」是不同的问题，提示不同。
        foreach (var failure in failures)
        {
            builder.Append($" {PlatformNames.ToName(failure.Platform)} 平台枚举失败: {failure.Message}。");
        }

        return builder.ToString();
    }


    /// <summary>
    /// 构造跨平台设备枚举器。ADB 不可用时该平台记为枚举失败，其他平台仍照常列出。
    /// </summary>
    /// <param name="adbPath">显式 adb 路径，来自 <c>--adb-path</c>。</param>
    /// <param name="project">已打开的工程，用于取工程级 adb 路径。未打开工程时为 null。</param>
    /// <param name="onlyPlatform">只枚举该平台。null 表示枚举全部平台。</param>
    /// <param name="streamOutput">是否把 adb 输出流式转发到控制台。</param>
    internal static AggregateDeviceProvider CreateDeviceProvider(
        string? adbPath,
        UkitProject? project = null,
        TargetPlatform? onlyPlatform = null,
        bool streamOutput = true)
    {
        var providers = new List<IDeviceProvider>();
        foreach (var platform in Enum.GetValues<TargetPlatform>())
        {
            if (onlyPlatform is { } requested && platform != requested)
            {
                continue;
            }

            providers.Add(CreateProvider(platform, adbPath, project, streamOutput));
        }

        return new AggregateDeviceProvider(providers);
    }

    /// <summary>
    /// 构造单平台设备枚举器。该平台的枚举前提不满足（例如找不到 adb）时返回
    /// <see cref="UnavailableDeviceProvider"/>，让原因出现在结果里而不是让整份列表失败。
    /// </summary>
    private static IDeviceProvider CreateProvider(
        TargetPlatform platform,
        string? adbPath,
        UkitProject? project,
        bool streamOutput) => platform switch
    {
        TargetPlatform.Win64 => new Win64DeviceService(),
        TargetPlatform.Android => TryCreateAndroidProvider(adbPath, project, streamOutput),
        _ => new UnavailableDeviceProvider(platform, $"平台 {PlatformNames.ToName(platform)} 尚未实现设备枚举。")
    };

    private static IDeviceProvider TryCreateAndroidProvider(string? adbPath, UkitProject? project, bool streamOutput)
    {
        try
        {
            return new AdbDeviceService(CreateAdbService(adbPath, project?.Settings.Android?.AdbPath, streamOutput));
        }
        catch (AdbPathResolutionException exception)
        {
            return new UnavailableDeviceProvider(TargetPlatform.Android, exception.Message);
        }
    }
}
