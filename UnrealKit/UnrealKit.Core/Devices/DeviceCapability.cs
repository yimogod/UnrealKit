namespace UnrealKit.Core.Devices;

/// <summary>
/// 设备服务的可选能力。平台之间的差异通过此枚举显式声明，
/// 不用「静默空结果」表达——空日志流无法与「该平台不支持日志」区分。
/// </summary>
public enum DeviceCapability
{
    /// <summary>采集目标进程内存信息。</summary>
    CaptureMemory,

    /// <summary>从设备拉取目录。</summary>
    PullDirectory,

    /// <summary>向运行中的 UE 进程发送控制台指令。</summary>
    SendConsoleCommand,

    /// <summary>流式读取 UE 日志输出。</summary>
    StreamLog,

    /// <summary>启动应用。</summary>
    StartApplication,

    /// <summary>停止应用。</summary>
    StopApplication,

    /// <summary>推送文件到设备。</summary>
    PushFile,

    /// <summary>删除设备上的文件。</summary>
    DeleteRemoteFile,

    /// <summary>安装应用包到设备（Android 为安装 APK）。</summary>
    InstallApplication
}

/// <summary>
/// 调用了当前平台不支持的设备能力。调用方应先用 <see cref="IDeviceService.Supports"/> 探测，
/// 而不是依赖返回空结果来判断。
/// </summary>
public sealed class DeviceCapabilityNotSupportedException : NotSupportedException
{
    public DeviceCapabilityNotSupportedException(DeviceCapability capability, string platform, string? suggestedAlternative = null)
        : base(BuildMessage(capability, platform, suggestedAlternative))
    {
        Capability = capability;
        Platform = platform;
    }

    public DeviceCapability Capability { get; }

    public string Platform { get; }

    private static string BuildMessage(DeviceCapability capability, string platform, string? suggestedAlternative)
    {
        var message = $"设备能力 {capability} 在 {platform} 平台上不受支持。";
        return suggestedAlternative is null ? message : $"{message} {suggestedAlternative}";
    }
}
