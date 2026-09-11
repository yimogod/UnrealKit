namespace UnrealKit.Core.Export;

/// <summary>
/// 轻量 CSV/TSV 行序列化工具。RFC 4180 兼容：
/// 字段含分隔符、双引号或换行时加双引号包裹，内部双引号转义为 ""。
/// </summary>
public static class CsvWriter
{
    public static char DelimiterFor(string path) =>
        Path.GetExtension(path).Equals(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';

    public static string JoinRow(IEnumerable<string?> fields, char delimiter) =>
        string.Join(delimiter, fields.Select(f => Escape(f, delimiter)));

    public static string Escape(string? field, char delimiter)
    {
        if (string.IsNullOrEmpty(field)) return string.Empty;
        return field.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0
            ? $"\"{field.Replace("\"", "\"\"")}\""
            : field;
    }
}
