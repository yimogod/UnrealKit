using System.Text;

namespace UnrealKit.Core.Projects;

internal sealed class IniDocument
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections = new(StringComparer.OrdinalIgnoreCase);

    public string? GetValue(string section, string key) =>
        _sections.TryGetValue(section, out var values) && values.TryGetValue(key, out var value) ? value : null;

    public IReadOnlyDictionary<string, string> GetSection(string section) =>
        _sections.TryGetValue(section, out var values) ? values : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public void SetValue(string section, string key, string value)
    {
        if (!_sections.TryGetValue(section, out var values))
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _sections.Add(section, values);
        }

        values[key] = value;
    }

    public static IniDocument Parse(string content)
    {
        var document = new IniDocument();
        var section = string.Empty;
        foreach (var rawLine in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex > 0 && section.Length > 0)
            {
                document.SetValue(section, line[..separatorIndex].Trim(), line[(separatorIndex + 1)..].Trim());
            }
        }

        return document;
    }

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
