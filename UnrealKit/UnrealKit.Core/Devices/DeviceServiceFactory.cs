using UnrealKit.Core.Adb;
using UnrealKit.Core.Processes;

namespace UnrealKit.Core.Devices;

/// <summary>
/// 璁惧鏈嶅姟宸ュ巶銆傛牴鎹澶囧钩鍙板垱寤哄搴旂殑 IDeviceService 瀹炰緥锛屼緵 Desktop / CLI 灞備娇鐢ㄣ€?/// </summary>
public interface IDeviceServiceFactory
{
    /// <summary>
    /// 鏍规嵁璁惧骞冲彴鍒涘缓瀵瑰簲鐨勮澶囨湇鍔″疄渚嬨€?    /// </summary>
    IDeviceService CreateForDevice(IDevice device);
}

/// <summary>
/// IDeviceServiceFactory 鐨勯粯璁ゅ疄鐜般€?/// 瀵?Android 璁惧鍒涘缓 AdbDeviceService锛涘 Win64 璁惧鍒涘缓 Win64DeviceService銆?/// </summary>
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
        return device.Platform switch
        {
            "Win64" => new Win64DeviceService(_processRunner),
            "Android" => _adbService is not null
                ? new AdbDeviceService(_adbService)
                : throw new InvalidOperationException("ADB service is required for Android devices but was not provided."),
            _ => throw new ArgumentException($"Unsupported platform: {device.Platform}", nameof(device))
        };
    }
}
