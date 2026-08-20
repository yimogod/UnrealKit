using UnrealKit.Core.Devices;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Launch;

/// <summary>
/// 启动参数（uecommandline.txt）的构建与投放。
///
/// 此类不含任何平台分支：路径与启动目标由 <see cref="PlatformTarget"/> 提供，
/// 写文件、删文件、启动应用一律委托 IDeviceService。目标平台由传入的设备决定，
/// 不来自工程配置——同一工程可以同时跑多个平台。
/// </summary>
public sealed class LaunchParameterService : ILaunchParameterService
{
    private const string FileName = "uecommandline.txt";

    private readonly IDeviceService _deviceService;

    public LaunchParameterService(IDeviceService deviceService)
    {
        _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
    }

    public string BuildContent(ProjectSettings settings, IReadOnlyList<string> presetNames, string? customArguments = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(presetNames);
        var selectedNames = presetNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var presets = selectedNames.Select(name => settings.LaunchParameterPresets.FirstOrDefault(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown launch parameter preset: {name}", nameof(presetNames))).ToArray();
        var nonComposable = presets.Where(preset => !preset.IsComposable).ToArray();
        if (nonComposable.Length > 1 || nonComposable.Length == 1 && presets.Length > 1)
        {
            throw new ArgumentException("A non-composable launch parameter preset must be used alone.", nameof(presetNames));
        }

        // 预设参数参与合并去重；自定义参数按用户要求原样追加，不参与合并。
        var lines = MergeArguments(presets.Select(preset => preset.Arguments)).ToList();
        if (!string.IsNullOrWhiteSpace(customArguments))
        {
            lines.Add(customArguments.Trim());
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// 合并多个预设的参数块，每个 token 一行，按首次出现顺序输出。
    ///
    /// 无 <c>=</c> 的开关（如 <c>-llm</c>）按名去重，重复出现只保留首个；
    /// 有 <c>=</c> 的开关（如 <c>-trace=...</c>）对 <c>=</c> 后逗号分隔的值做并集去重。
    /// 合并是无状态的——每次都由当前选中集合重算，取消某项选择后其 token 自然从结果中消失。
    /// </summary>
    private static IReadOnlyList<string> MergeArguments(IEnumerable<string> argumentBlocks)
    {
        var output = new List<string>();
        // 无 = 的开关：按名去重。
        var seenSwitches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 有 = 的开关：key（= 前）→ output 中的占位下标；合并后的值按序保存。
        var valueSlot = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var valueOrder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var valueSeen = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in argumentBlocks.SelectMany(SplitArguments))
        {
            var separator = token.IndexOf('=');
            if (separator < 0)
            {
                if (seenSwitches.Add(token))
                {
                    output.Add(token);
                }

                continue;
            }

            var key = token[..separator];
            if (!valueSlot.TryGetValue(key, out var slot))
            {
                slot = output.Count;
                valueSlot.Add(key, slot);
                output.Add(key); // 占位，随后写入合并后的值。
            }

            if (!valueOrder.TryGetValue(key, out var order))
            {
                order = [];
                valueOrder.Add(key, order);
                valueSeen.Add(key, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }

            var seen = valueSeen[key];
            foreach (var value in SplitCommaValues(token[(separator + 1)..]))
            {
                if (seen.Add(value))
                {
                    order.Add(value);
                }
            }

            output[slot] = $"{key}={string.Join(",", order)}";
        }

        return output;
    }

    private static IEnumerable<string> SplitArguments(string block) =>
        block.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IEnumerable<string> SplitCommaValues(string valuePart) =>
        valuePart.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// 启动参数文件在设备上的路径。平台取自本服务所绑定的设备服务，
    /// 因此同一工程针对不同平台的设备会解析出各自正确的路径。
    /// </summary>
    public string GetRemotePath(ProjectSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var target = ResolveTarget(settings);
        return target.CombineDevicePath(target.GameRootPath, FileName);
    }

    public async Task<LaunchParameterPushResult> PushAsync(UkitProject project, LaunchParameterRequest request, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SerialNumber);
        var content = BuildContent(project.Settings, request.PresetNames, request.CustomArguments);
        var remotePath = GetRemotePath(project.Settings);
        var device = ResolveDevice(request.SerialNumber);

        // 内容先落到本地临时文件，再交给设备服务投放。Win64 的「推送」就是复制，
        // Android 是 adb push——两者都由 IDeviceService.PushFileAsync 负责。
        var directory = Path.Combine(Path.GetTempPath(), "UnrealKit", "LaunchParameters");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"{Guid.NewGuid():N}-{FileName}");
        try
        {
            progress?.Report(new OperationProgress("commandline-push", "Writing", 1, 2, $"Writing temporary {FileName}."));
            await File.WriteAllTextAsync(temporaryPath, content, new System.Text.UTF8Encoding(false), cancellationToken);
            progress?.Report(new OperationProgress("commandline-push", "Pushing", 2, 2, $"Pushing to {remotePath}."));
            var result = await _deviceService.PushFileAsync(device, temporaryPath, remotePath, progress, cancellationToken);
            return new LaunchParameterPushResult(content, remotePath, result);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<ProcessExecutionResult> DeleteAsync(UkitProject project, string serialNumber, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);
        var path = GetRemotePath(project.Settings);
        return _deviceService.DeleteRemoteFileAsync(ResolveDevice(serialNumber), path, progress, cancellationToken);
    }

    public Task<ProcessExecutionResult> StartApplicationAsync(UkitProject project, string serialNumber, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);

        var target = ResolveTarget(project.Settings);
        return _deviceService.StartApplicationAsync(
            ResolveDevice(serialNumber), target.LaunchTarget, target.LaunchActivity, progress, cancellationToken);
    }

    /// <summary>
    /// 解析本服务所绑定平台的落地值。该平台在工程中未配置时报错并列出已配置平台。
    /// </summary>
    private PlatformTarget ResolveTarget(ProjectSettings settings) =>
        settings.ResolveTarget(_deviceService.Platform, "投放启动参数需要该平台的配置。");

    private IDevice ResolveDevice(string serialNumber) =>
        DeviceReference.Create(serialNumber, _deviceService.Platform);
}
