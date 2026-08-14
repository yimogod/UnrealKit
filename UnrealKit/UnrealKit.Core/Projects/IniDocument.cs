using System.Text;

namespace UnrealKit.Core.Projects;

public sealed class IniDocument
{
    /// <summary>
    /// 双字典. section下保存的多个keyvalue. 忽略大小写
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, string>> _sections = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 通过解析字符串获取Ini类
    /// </summary>
    public static IniDocument Parse(string content)
    {
        var document = new IniDocument();

        // 缓存当前的section名称
        var section = string.Empty;

        // 统一回车符
        string tempContent = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        string[] lines = tempContent.Split('\n');

        // 按顺序 从上往下读取内容
        foreach (var rawLine in lines)
        {
            // 去除空格
            var line = rawLine.Trim();

            // 不处理注释等
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            // 获取section的名称
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            if(section.Length == 0)continue;

            // 获取ini的 kev, value
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)continue;

            string key = line[..separatorIndex].Trim();
            string value = line[(separatorIndex + 1)..].Trim();
            document.SetValue(section, key, value);
        }

        return document;
    }

    public bool HasSection(string section) => _sections.ContainsKey(section);

    /// <summary>
    /// 获取section下的所有的值
    /// </summary>
    public IReadOnlyDictionary<string, string> GetSection(string section) =>
        _sections.TryGetValue(section, out var values) ? values : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);


    /// <summary>
    /// 是否保存了值
    /// </summary>
    public bool HasValue(string section, string key) =>
        _sections.TryGetValue(section, out var values) && values.ContainsKey(key);

    /// <summary>
    /// 获取 section:key下面的值
    /// </summary>
    public string? GetValue(string section, string key) =>
        _sections.TryGetValue(section, out var values) && values.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// 设置section段的keyvalue值
    /// </summary>
    public void SetValue(string section, string key, string value)
    {
        if (!_sections.TryGetValue(section, out var values))
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _sections.Add(section, values);
        }

        values[key] = value;
    }

    /// <summary>
    /// 保存ini
    /// </summary>
    public async Task SaveAsync(string path, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        foreach (var section in _sections.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append('[').Append(section.Key).AppendLine("]");
            foreach (var value in section.Value.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append(value.Key).Append('=').AppendLine(value.Value);
            }

            builder.AppendLine();
        }

        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false), cancellationToken);
    }
}
