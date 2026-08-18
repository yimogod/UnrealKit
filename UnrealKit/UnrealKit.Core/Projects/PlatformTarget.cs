namespace UnrealKit.Core.Projects;

/// <summary>
/// 设备端路径风格。平台差异中真正影响路径拼接的只有这一个维度，
/// 按风格而非按平台分派，新增平台时只需声明它属于哪种风格。
/// </summary>
public enum DevicePathStyle
{
    /// <summary>正斜杠分隔的绝对路径，如 Android、iOS 的设备端路径。</summary>
    Unix,

    /// <summary>本机 Windows 路径。</summary>
    Windows
}

/// <summary>
/// 一次操作所需的全部平台相关落地值。由 <see cref="PlatformProfile.Resolve"/> 产出。
///
/// 该类型存在的意义是让平台差异有唯一出口：拿到 PlatformTarget 之后的代码
/// （采集、启动参数投放、启动应用）不再需要知道自己面对的是 Android 还是 Win64，
/// 因此新增平台不必修改这些调用方。所有字段都是已展开、已校验的最终值，
/// 不含 {PackageName} 之类的占位符。
/// </summary>
/// <param name="Platform">该落地值来自哪个平台的配置，用于归档目录名与清单字段。</param>
/// <param name="PathStyle">设备端路径风格，决定 <see cref="CombineDevicePath"/> 的分隔符。</param>
/// <param name="ProcessIdentity">内存采集的目标进程标识。Android 为包名，Win64 为进程名。</param>
/// <param name="LaunchTarget">启动目标。Android 为包名，Win64 为可执行文件路径。</param>
/// <param name="LaunchActivity">启动 Activity。仅 Android 有值，其他平台为 null。</param>
/// <param name="GameRootPath">设备端游戏根目录，uecommandline.txt 所在位置。</param>
/// <param name="SavedRootPath">设备端 UE Saved 目录。</param>
public sealed record PlatformTarget(
    TargetPlatform Platform,
    DevicePathStyle PathStyle,
    string ProcessIdentity,
    string LaunchTarget,
    string? LaunchActivity,
    string GameRootPath,
    string SavedRootPath)
{
    /// <summary>平台的稳定字符串标识，用于归档目录名与 CaptureManifest。</summary>
    public string PlatformName => PlatformNames.ToName(Platform);

    /// <summary>
    /// 在设备端路径下拼接文件名。按 <see cref="PathStyle"/> 分派，
    /// 不用 <see cref="Path.Combine"/>——它在 Windows 主机上会给 Android 路径写入反斜杠。
    /// </summary>
    public string CombineDevicePath(string directory, string fileName) => PathStyle switch
    {
        DevicePathStyle.Unix => $"{directory.TrimEnd('/')}/{fileName}",
        DevicePathStyle.Windows => Path.Combine(directory, fileName),
        _ => throw new ArgumentOutOfRangeException(nameof(PathStyle), PathStyle, "Unsupported device path style.")
    };
}
