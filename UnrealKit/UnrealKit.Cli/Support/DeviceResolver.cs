using UnrealKit.Core.Adb;
using UnrealKit.Core.Devices;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;
using UnrealKit.Core.RemoteControl;

namespace UnrealKit.Cli;

/// <summary>
/// adb 服务构造与设备选择。歧义输入一律报错并列出候选，不取「默认第一台设备」。
/// </summary>
internal static class DeviceResolver
{
    internal static AdbService CreateAdbService(string? explicitPath, string? projectAdbPath = null, bool streamOutput = true)
    {
        var resolvedPath = new AdbPathResolver().ResolveRequired(explicitPath, projectAdbPath);
        return new AdbService(
            new ProcessRunner(),
            resolvedPath,
            streamOutput ? new Progress<ProcessOutput>(CliOutput.WriteProcessOutput) : null);
    }

    /// <summary>
    /// 按工程配置的目标平台解析设备服务与设备标识。
    /// Android 需要 ADB 并要求显式选择设备；Win64 只有本机一台，不需要 ADB。
    /// </summary>
    internal static async Task<(IDeviceService DeviceService, string DeviceId)> ResolveDeviceTargetAsync(
        UkitProject project,
        string[] options,
        string? adbPath,
        bool streamOutput = true)
    {
        if (project.Settings.Platform == TargetPlatform.Win64)
        {
            var requestedDevice = CliOptions.GetOptional(options, "--device");
            var localDevice = new Win64Device();
            if (requestedDevice is not null && !string.Equals(requestedDevice, localDevice.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Win64 工程只支持本机设备 '{localDevice.Id}'，无法使用 --device {requestedDevice}。");
            }

            return (new Win64DeviceService(new ProcessRunner(), RemoteControlOptions.FromProjectSettings(project.Settings)), localDevice.Id);
        }

        var adbService = CreateAdbService(adbPath, project.Settings.AdbPath, streamOutput);
        var serialNumber = await ResolveDeviceSerialAsync(adbService, options);
        return (new AdbDeviceService(adbService, RemoteControlOptions.FromProjectSettings(project.Settings)), serialNumber);
    }

    /// <summary>
    /// 从设备枚举结果中取出指定设备，并要求其处于可用状态。
    /// 「找不到设备」与「设备状态不对」是不同的失败，分别给出具体原因。
    /// </summary>
    internal static async Task<IDevice> GetSelectedAvailableDeviceAsync(IDeviceService service, string deviceId)
    {
        var devices = await service.ListDevicesAsync();
        var device = devices.SingleOrDefault(candidate => string.Equals(candidate.Id, deviceId, StringComparison.Ordinal));
        if (device is null)
        {
            var attached = devices.Count == 0
                ? "(none attached)"
                : string.Join(", ", devices.Select(candidate => candidate.Id));
            throw new AdbDeviceSelectionException(
                $"{service.Platform} device was not found: {deviceId}. Attached devices: {attached}.");
        }

        if (!device.IsAvailable)
        {
            throw new AdbDeviceSelectionException(
                $"{service.Platform} device '{deviceId}' is not available. Ensure it is connected and authorized.");
        }

        return device;
    }

    internal static async Task<string> ResolveDeviceSerialAsync(IAdbService service, string[] options)
    {
        var serialNumber = CliOptions.GetOptional(options, "--device");
        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            if (string.Equals(serialNumber, "auto", StringComparison.OrdinalIgnoreCase))
            {
                var availableDevices = (await service.ListDevicesAsync()).Where(device => device.IsAvailable).ToArray();
                return availableDevices.Length switch
                {
                    1 => availableDevices[0].SerialNumber,
                    0 => throw new AdbDeviceSelectionException("No available ADB devices found for auto-selection. Connect a device or specify --device <serial>."),
                    _ => throw new AdbDeviceSelectionException($"Multiple devices available ({availableDevices.Length}). Use --device <serial> to select one: {string.Join(", ", availableDevices.Select(device => device.SerialNumber))}")
                };
            }

            return serialNumber;
        }

        var devices = await service.ListDevicesAsync();
        var available = devices.Where(device => device.IsAvailable).ToArray();
        if (available.Length == 1)
        {
            Console.Error.WriteLine($"Only one available device found: {available[0].SerialNumber} ({available[0].Model ?? "unknown model"}). Use --device auto to select it.");
        }
        else if (available.Length == 0)
        {
            Console.Error.WriteLine("No available ADB devices found. Connect a device and try again.");
        }
        else
        {
            Console.Error.WriteLine("Multiple devices available. Use --device <serial> to select one:");
            foreach (var device in available)
            {
                Console.Error.WriteLine($"  {device.SerialNumber}  {device.Status}  {device.Model ?? "unknown model"}");
            }
        }

        throw new AdbDeviceSelectionException("No device specified. Use --device <serial> or --device auto when exactly one device is available.");
    }

    /// <summary>
    /// 构造跨平台设备枚举器。ADB 不可用时该平台记为枚举失败，Win64 仍照常列出。
    /// </summary>
    internal static AggregateDeviceProvider CreateDeviceProvider(string? adbPath)
    {
        var providers = new List<IDeviceProvider> { new Win64DeviceService() };
        try
        {
            providers.Add(new AdbDeviceService(CreateAdbService(adbPath)));
        }
        catch (AdbPathResolutionException exception)
        {
            providers.Add(new UnavailableDeviceProvider(TargetPlatform.Android, exception.Message));
        }

        return new AggregateDeviceProvider(providers);
    }
}
