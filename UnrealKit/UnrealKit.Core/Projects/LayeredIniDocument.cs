namespace UnrealKit.Core.Projects;

/// <summary>
/// 分层ini. 想 UnrealEngine中的, DefaultGame.ini的优先级高于BaseGame.ini
/// LayeredIniDocument仅支持两层, 目前足够了
/// </summary>
public sealed class LayeredIniDocument
{
    private readonly IniDocument _base;
    private readonly IniDocument _override;
    public IniDocument Base => _base;
    public IniDocument Override => _override;

    public LayeredIniDocument(IniDocument @base, IniDocument @override)
    {
        _base = @base ?? throw new ArgumentNullException(nameof(@base));
        _override = @override ?? throw new ArgumentNullException(nameof(@override));
    }

    /// <summary>
    /// 获取值
    /// </summary>
    public string? GetValue(string section, string key) =>
        _override.GetValue(section, key) ?? _base.GetValue(section, key);

    /// <summary>
    /// 获取融合后的section的所有值
    /// </summary>
    public IReadOnlyDictionary<string, string> GetSection(string section)
    {
        var baseSection = _base.GetSection(section);
        var overrideSection = _override.GetSection(section);
        var merged = new Dictionary<string, string>(baseSection, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in overrideSection)
        {
            merged[key] = value;
        }
        return merged;
    }

    public bool HasSection(string section) =>
        _override.HasSection(section) || _base.HasSection(section);

    public bool HasValue(string section, string key) =>
        _override.HasValue(section, key) || _base.HasValue(section, key);

    /// <summary>
    /// 根据两个路径创建 LayeredIniDocument
    /// </summary>
    public static LayeredIniDocument FromFiles(string basePath, string overridePath)
    {
        var baseDoc = File.Exists(basePath)
            ? IniDocument.Parse(File.ReadAllText(basePath))
            : new IniDocument();

        var overrideDoc = File.Exists(overridePath)
            ? IniDocument.Parse(File.ReadAllText(overridePath))
            : new IniDocument();

        return new LayeredIniDocument(baseDoc, overrideDoc);
    }

    /// <summary>LayeredIniDocument
    /// 异步创建 
    /// </summary>
    public static async Task<LayeredIniDocument> FromFilesAsync(
        string basePath,
        string overridePath,
        CancellationToken cancellationToken = default)
    {
        var baseDoc = File.Exists(basePath)
            ? IniDocument.Parse(await File.ReadAllTextAsync(basePath, cancellationToken))
            : new IniDocument();

        var overrideDoc = File.Exists(overridePath)
            ? IniDocument.Parse(await File.ReadAllTextAsync(overridePath, cancellationToken))
            : new IniDocument();

        return new LayeredIniDocument(baseDoc, overrideDoc);
    }
}
