using UnrealKit.Core.Devices;
using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Unreal;

/// <summary>
/// 一次下载取回设备 Saved 树的哪一部分。
///
/// 用枚举而不是让调用方传一个自由的子目录名：子目录名是 UE 的固定布局
/// </summary>
public enum UnealSavedScope
{
    /// <summary>整个 Saved 目录。</summary>
    All,

    /// <summary>只取 <c>Saved/Logs</c>。</summary>
    Logs,

    /// <summary>
    /// 只取一组常用子目录（<see cref="UnrealModels.CommonSubdirectories"/>）：
    /// 比「整个 Saved」小得多，又覆盖了排查问题通常要看的日志、截图、Profiling 与 GPU dump。
    /// </summary>
    Common
}


/// <summary>
/// 下载的落地计划 <see cref="LocalDirectory"/> 一定是尚不存在的新目录：
/// 取回设备数据不覆盖上一次的结果，否则两次取回之间的差异会被静默抹掉。
/// </summary>
/// <param name="Scope">本次取回的范围。</param>
/// <param name="DeviceDirectory">设备端源目录</param>
/// <param name="LocalDirectory">本地目标目录</param>
public sealed record UnrealSavedPullPlan(
    UnealSavedScope Scope,
    string DeviceDirectory,
    string LocalDirectory);

/// <summary>一次「把设备上的 UE Saved 数据取回本地」的请求。</summary>
public sealed record UnrealSavedPullRequest(
    UkitProject Project,
    IDevice Device,
    UnealSavedScope Scope = UnealSavedScope.All);

/// <summary>
/// 下载结果
/// </summary>
public sealed record UnrealSavedPullResult(
    UnrealSavedPullPlan Plan,
    int FileCount,
    long TotalBytes);

public class UnrealModels
{
    /// <summary>
    /// 「常用子目录」范围要拉取的 Saved 子目录名集合，相对 <c>Saved/</c>。
    /// 内置固定预设：不随工程配置变化，与 <see cref="LaunchParameterPresetDefaults"/> 同理。
    /// </summary>
    public static readonly IReadOnlyList<string> CommonSubdirectories =
        ["Logs", "Screenshots", "Profiling", "GPUDumps"];

    public static string GetScopeName(UnealSavedScope scope) => scope switch
    {
        UnealSavedScope.All => PlatformProfile.SavedDirectoryName,
        UnealSavedScope.Logs => "Logs",
        UnealSavedScope.Common => "Common",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "未支持的下载范围。")
    };

    /// <summary>
    /// 该范围对应的设备端源目录。用 <see cref="PlatformTarget.CombineDevicePath"/> 拼接子目录，
    /// 不用 <see cref="Path.Combine"/>——后者在 Windows 主机上会给 Android 路径写入反斜杠。
    /// <see cref="UnealSavedScope.Common"/> 返回 <c>Saved/</c> 本身（子目录集合的父目录），
    /// 子目录名见 <see cref="CommonSubdirectories"/>。
    /// </summary>
    public static string ResolveDeviceDirectory(PlatformTarget target, UnealSavedScope scope) => scope switch
    {
        UnealSavedScope.All => target.SavedRootPath,
        UnealSavedScope.Logs => target.CombineDevicePath(target.SavedRootPath, "Logs"),
        UnealSavedScope.Common => target.SavedRootPath,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "未支持的下载范围。")
    };
}
