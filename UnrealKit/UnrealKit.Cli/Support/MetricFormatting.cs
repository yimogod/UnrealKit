using System.Globalization;
using UnrealKit.Core.Analysis;

namespace UnrealKit.Cli;

/// <summary>
/// 指标数值的文本呈现。差分与趋势共用同一套格式，
/// 「缺失」与「无可比较的前值」分开表达，不都显示成 0。
/// </summary>
internal static class MetricFormatting
{
    internal static string Value(double? value) =>
        value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "missing";

    internal static string Delta(double? value) => value is null
        ? "missing"
        : value.Value.ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture);

    internal static string Percent(double? value) => value is null
        ? "-"
        : value.Value.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture) + "%";

    internal static string Date(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "any";

    internal static string DateExact(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static string Bytes(long? bytes) => bytes is null
        ? "n/a"
        : $"{bytes.Value / 1024.0 / 1024.0:F2} MB";

    internal static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "...";

    internal static string DescribeAssessment(MetricDiffAssessment assessment) => assessment switch
    {
        MetricDiffAssessment.Regressed => "regressed",
        MetricDiffAssessment.Improved => "improved",
        MetricDiffAssessment.Unchanged => "unchanged",
        MetricDiffAssessment.Changed => "changed",
        _ => "unknown"
    };
}
