namespace UnrealKit.Core.Projects;

/// <summary>
/// TargetPlatform 与其字符串标识之间的唯一映射点。
/// IDevice.Platform、Capture 归档目录名、.ukit 的 Platform 字段、CLI 的 --platform
/// 都必须经由此处转换，不得在各处硬编码 "Android" / "Win64" 字面量。
/// </summary>
public static class PlatformNames
{
    public const string Android = "Android";
    public const string Win64 = "Win64";

    /// <summary>
    /// 返回平台的稳定字符串标识。该标识是归档目录名与 .ukit 字段的一部分，属于稳定契约。
    /// </summary>
    public static string ToName(TargetPlatform platform) => platform switch
    {
        TargetPlatform.Android => Android,
        TargetPlatform.Win64 => Win64,
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported platform.")
    };

    /// <summary>
    /// 解析平台标识。大小写不敏感；无法识别时返回 false，不回退到默认平台。
    /// </summary>
    public static bool TryParse(string? value, out TargetPlatform platform)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Enum.TryParse(value.Trim(), ignoreCase: true, out platform)
            && Enum.IsDefined(platform))
        {
            return true;
        }

        platform = default;
        return false;
    }

    /// <summary>
    /// 解析平台标识，无法识别时抛出并列出所有合法取值。
    /// </summary>
    public static TargetPlatform Parse(string? value, string? parameterName = null) =>
        TryParse(value, out var platform)
            ? platform
            : throw new ArgumentException(
                $"Unsupported platform: '{value}'. Valid values are {Android} and {Win64}.",
                parameterName ?? nameof(value));

    /// <summary>所有平台的稳定标识，供 CLI 列举与 GUI 下拉使用。</summary>
    public static IReadOnlyList<string> All { get; } = [Android, Win64];
}
