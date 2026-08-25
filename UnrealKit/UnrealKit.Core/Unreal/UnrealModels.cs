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
    Logs
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
    public static string GetRelativePath(UnealSavedScope scope) => scope switch
    {
        UnealSavedScope.All => PlatformProfile.SavedDirectoryName,
        UnealSavedScope.Logs => Path.Combine(PlatformProfile.SavedDirectoryName, "Logs"),
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "未支持的下载范围。")
    };
}
