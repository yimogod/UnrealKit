using System.IO;
using System.Text;
using UnrealKit.Core.Export;

namespace UnrealKit.Cli;

// ── 列定义 ───────────────────────────────────────────────────────────────────

/// <summary>CSV 列定义。Key 是表头名，也是行数据的对应字段标识。</summary>
internal sealed record CsvColumn(string Key, string Header);

// ── 主报告构造器 ──────────────────────────────────────────────────────────────

/// <summary>
/// 通用 CSV 表格报告。接受列定义 + 行数据，生成 UTF-8 CSV 文件（RFC 4180）。
/// 与 HtmlTableReport 平行设计，调用方可复用同一份列/行准备逻辑。
/// </summary>
internal static class CsvTableReport
{
    /// <param name="columns">列定义，顺序即输出列顺序。</param>
    /// <param name="rows">行数据，每行与 columns 等长；所有值调用 ToString()，null 输出空字符串。</param>
    /// <param name="delimiter">分隔符，默认逗号；传 '\t' 生成 TSV。</param>
    internal static string Build(
        IReadOnlyList<CsvColumn> columns,
        IReadOnlyList<object?[]> rows,
        char delimiter = ',')
    {
        var sb = new StringBuilder((rows.Count + 1) * columns.Count * 20);

        // 表头
        sb.AppendLine(CsvWriter.JoinRow(columns.Select(c => c.Header), delimiter));

        // 数据行
        foreach (var row in rows)
        {
            var fields = new string[columns.Count];
            for (var i = 0; i < columns.Count; i++)
                fields[i] = i < row.Length ? (row[i]?.ToString() ?? "") : "";
            sb.AppendLine(CsvWriter.JoinRow(fields, delimiter));
        }

        return sb.ToString();
    }

    internal static void WriteAndOpen(string csv, string path)
    {
        File.WriteAllText(path, csv, Encoding.UTF8);
        Console.WriteLine($"CSV report: {path}");
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { }
    }
}
