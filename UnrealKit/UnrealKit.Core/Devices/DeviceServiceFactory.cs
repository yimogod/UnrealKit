using UnrealKit.Core.Adb;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Devices;

/// <summary>
/// 设备服务工厂。根据设备平台创建对应的 IDeviceService 实例，供 Desktop / CLI 层使用。
/// </summary>
public interface IDeviceServiceFactory
{
    /// <summary>
    /// 根据设备平台创建对应的设备服务实例。
    /// </summary>
    IDeviceService CreateForDevice(IDevice device);
}

/// <summary>
/// IDeviceServiceFactory 的默认实现。
/// 对 Android 设备创建 AdbDeviceService；对 Win64 设备创建 Win64DeviceService。
/// </summary>
public sealed class DeviceServiceFactory : IDeviceServiceFactory
{
    private readonly IAdbService? _adbService;
    private readonly IProcessRunner? _processRunner;

    public DeviceServiceFactory(IAdbService? adbService = null, IProcessRunner? processRunner = null)
    {
        _adbService = adbService;
        _processRunner = processRunner;
    }

    public IDeviceService CreateForDevice(IDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return PlatformNames.Parse(device.Platform, nameof(device)) switch
        {
            TargetPlatform.Win64 => new Win64DeviceService(_processRunner),
            TargetPlatform.Android => _adbService is not null
                ? new AdbDeviceService(_adbService)
                : throw new InvalidOperationException(
                    $"Android device '{device.Id}' requires an ADB service, but this factory was constructed without one."),
            var platform => throw new ArgumentException($"Unsupported platform: {platform}", nameof(device))
        };
    }
}
