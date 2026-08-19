namespace UnrealKit.Core.Projects;

/// <summary>
/// 平台作用域：一次分析工作聚焦在哪个平台上。
///
/// 这是**视图过滤器**，不是「当前平台」配置。<c>.ukit</c> v2 已经移除了 <c>Platform</c>
/// 字段（见 Doc/工程格式与配置.md），因为同一工程同时跑 Android 与 Win64 是常态；
/// 「本次操作用哪个平台」始终由所选设备派生（<see cref="ProjectSettings.ResolveTarget"/>）。
/// 作用域只决定列表里显示什么，不参与操作平台的判定——否则配置与所选设备会成为
/// 两个互相矛盾的真值来源，二者不一致时无论听谁的都是错的。
///
/// <see cref="All"/> 表示不过滤。没有「默认某个平台」的取值：隐式默认会让另一个平台的
/// 设备与归档静默消失，那正是此类型要消除的问题。
/// </summary>
public sealed record PlatformScope
{
    /// <summary>「全部平台」在状态文件与下拉框中的稳定标识。不是平台名，不会与之冲突。</summary>
    public const string AllName = "All";

    private PlatformScope(TargetPlatform? platform) => Platform = platform;

    /// <summary>
    /// 不过滤任何平台。显式标注类型：无标注的 <c>new(null)</c> 会与 record 自动生成的
    /// 拷贝构造函数产生二义性。
    /// </summary>
    public static PlatformScope All { get; } = new((TargetPlatform?)null);

    /// <summary>聚焦的平台。<c>null</c> 表示全部平台。</summary>
    public TargetPlatform? Platform { get; }

    /// <summary>是否为「全部平台」。</summary>
    public bool IsAll => Platform is null;

    /// <summary>作用域的显示与持久化标识：具体平台用平台名，全部用 <see cref="AllName"/>。</summary>
    public string Name => Platform is { } platform ? PlatformNames.ToName(platform) : AllName;

    /// <summary>聚焦到指定平台。</summary>
    public static PlatformScope For(TargetPlatform platform) => new(platform);

    /// <summary>
    /// 下拉框与 CLI 提示可用的全部取值，「全部」在最前。
    /// </summary>
    public static IReadOnlyList<PlatformScope> AllOptions { get; } =
        [All, .. Enum.GetValues<TargetPlatform>().Select(For)];

    /// <summary>
    /// 解析作用域标识。无法识别时返回 false 并给出 <see cref="All"/>——
    /// 状态文件里的陈旧或损坏取值不应让界面聚焦到一个用户没选过的平台。
    /// </summary>
    public static bool TryParse(string? value, out PlatformScope scope)
    {
        scope = All;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, AllName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!PlatformNames.TryParse(trimmed, out var platform))
        {
            return false;
        }

        scope = For(platform);
        return true;
    }

    /// <summary>
    /// 该平台名是否在作用域内。比较大小写不敏感，与归档目录名的比较规则一致。
    /// 无法识别的平台名在「全部」下仍然可见：作用域是过滤器，不承担校验职责，
    /// 静默丢弃未知平台会让归档目录凭空消失。
    /// </summary>
    public bool Includes(string? platformName) =>
        IsAll || string.Equals(platformName, Name, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Name;
}
