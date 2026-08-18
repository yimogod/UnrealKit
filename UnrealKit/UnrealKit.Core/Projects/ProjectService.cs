using UnrealKit.Core.Diagnostics;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Core.Projects;

/// <summary>
/// 项目服务
/// </summary>
public sealed class ProjectService : IProjectService
{
    private const string DescriptorSection = "UnrealKit.Project";
    private const string SettingsSection = "UnrealKit.ProjectSettings";
    private const string PresetsSection = "UnrealKit.LaunchPresets";
    private const string ConsoleSequencesSection = "UnrealKit.ConsoleSequences";
    private const string BaseGameIniFileName = "BaseGame.ini";
    private readonly IOperationLogger _logger;

    public ProjectService(IOperationLogger? logger = null)
    {
        _logger = logger ?? NullOperationLogger.Instance;
    }

    /// <summary>
    /// 异步创建项目
    /// </summary>
    public async Task<ProjectCreateResult> CreateProjectAsync(CreateProjectRequest request, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        const string operationId = "project-create";
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DirectoryPath);

        ValidateProjectName(request.ProjectName);
        var rootDirectory = Path.GetFullPath(request.DirectoryPath);
        var descriptorPath = Path.Combine(rootDirectory, $"{request.ProjectName}.ukit");
        Report(progress, operationId, "Validating", "正在校验工程目录。");

        if (Directory.Exists(rootDirectory) && Directory.EnumerateFileSystemEntries(rootDirectory).Any())
        {
            throw new InvalidOperationException($"工程目录不是空目录，拒绝创建以避免覆盖现有文件: {rootDirectory}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var descriptor = UkitProjectDescriptor.CreateDefault(request.ProjectName);
        var settings = ProjectSettings.CreateDefaults(request.ProjectName);
        Directory.CreateDirectory(rootDirectory);
        foreach (var directoryName in new[] { descriptor.ConfigRoot, descriptor.ContentRoot, descriptor.SavedRoot, descriptor.IntermediateRoot })
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(rootDirectory, directoryName));
        }

        Report(progress, operationId, "Creating", "正在写入工程描述与默认配置。", 1, 2);
        await WriteAgentTemplatesAsync(rootDirectory, request.ProjectName, cancellationToken);
        await WriteDescriptorAsync(descriptorPath, descriptor, cancellationToken);
        await WriteSettingsAsync(Path.Combine(rootDirectory, descriptor.ConfigRoot, "DefaultGame.ini"), settings, cancellationToken);
        var validation = await ValidateProjectAsync(descriptorPath, progress, cancellationToken);
        Report(progress, operationId, "Completed", "工程创建完成。", 2, 2);
        _logger.Log(new LogEvent(DateTimeOffset.UtcNow, LogLevel.Information, operationId, "Project created", new Dictionary<string, string> { ["path"] = descriptorPath }));
        return new ProjectCreateResult(new UkitProject(descriptorPath, rootDirectory, descriptor, settings), validation);
    }

    /// <summary>
    /// 异步打开项目
    /// </summary>
    public async Task<UkitProject> OpenProjectAsync(string projectFilePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        const string operationId = "project-open";
        var fullPath = GetProjectFilePath(projectFilePath);
        Report(progress, operationId, "Loading", "正在读取工程描述文件。", 1, 2);
        var descriptor = await ReadDescriptorAsync(fullPath, cancellationToken);
        var rootDirectory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("无法确定工程根目录。");
        var settings = await ReadSettingsAsync(Path.Combine(rootDirectory, descriptor.ConfigRoot, "DefaultGame.ini"), descriptor.ProjectName, cancellationToken);
        Report(progress, operationId, "Completed", "工程已加载。", 2, 2);
        return new UkitProject(fullPath, rootDirectory, descriptor, settings);
    }

    /// <summary>
    /// 异步更新项目设置
    /// </summary>
    public async Task<UkitProject> UpdateSettingsAsync(UkitProject project, ProjectSettings settings, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        const string operationId = "project-settings-update";
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(settings);
        ValidateSettings(settings);
        var fullPath = GetProjectFilePath(project.ProjectFilePath);
        Report(progress, operationId, "Writing", "正在保存项目默认配置。", 1, 2);
        await WriteSettingsAsync(project.ConfigFilePath, settings, cancellationToken);
        Report(progress, operationId, "Completed", "项目默认配置已保存。", 2, 2);
        _logger.Log(new LogEvent(DateTimeOffset.UtcNow, LogLevel.Information, operationId, "Project settings updated", new Dictionary<string, string> { ["path"] = fullPath }));
        return project with { Settings = settings };
    }

    /// <summary>
    /// 异步校验项目
    /// </summary>
    public async Task<ProjectValidationResult> ValidateProjectAsync(string projectFilePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        const string operationId = "project-validate";
        var fullPath = GetProjectFilePath(projectFilePath);
        var rootDirectory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("无法确定工程根目录。");
        Report(progress, operationId, "Validating", "正在校验工程结构与格式。", 1, 2);
        var diagnostics = new List<Diagnostic>();
        UkitProjectDescriptor descriptor;
        try
        {
            descriptor = await ReadDescriptorAsync(fullPath, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "UKIT001", "无法读取工程描述文件。", fullPath, $"详细信息: {ex.Message}"));
            Report(progress, operationId, "Completed", "校验完成。", 2, 2);
            return new ProjectValidationResult(diagnostics);
        }

        cancellationToken.ThrowIfCancellationRequested();
        ValidateProjectName(descriptor.ProjectName, diagnostics, fullPath);
        ValidateRoot(descriptor.ContentRoot, nameof(descriptor.ContentRoot), rootDirectory, diagnostics);
        ValidateRoot(descriptor.ConfigRoot, nameof(descriptor.ConfigRoot), rootDirectory, diagnostics);
        ValidateRoot(descriptor.SavedRoot, nameof(descriptor.SavedRoot), rootDirectory, diagnostics);
        ValidateRoot(descriptor.IntermediateRoot, nameof(descriptor.IntermediateRoot), rootDirectory, diagnostics);

        if (descriptor.FormatVersion != UkitProjectDescriptor.CurrentFormatVersion)
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "UKIT002", $"不支持的工程格式版本: {descriptor.FormatVersion}（当前版本: {UkitProjectDescriptor.CurrentFormatVersion}）", fullPath, "使用新版 UnrealKit 重新创建工程或查阅迁移文档。"));
        }

        var settingsPath = Path.Combine(rootDirectory, descriptor.ConfigRoot, "DefaultGame.ini");
        if (!File.Exists(settingsPath))
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "UKIT003", "未找到可选配置文件 DefaultGame.ini。", settingsPath, "创建 Config/DefaultGame.ini 以保存项目默认配置。"));
        }

        Report(progress, operationId, "Completed", "校验完成。", 2, 2);
        return new ProjectValidationResult(diagnostics);
    }

    /// <summary>
    /// 获取 BaseGame.ini 路径
    /// </summary>
    public static string ResolveBaseGameIniPath() => Path.Combine(ApplicationPaths.AppDir, BaseGameIniFileName);

    /// <summary>
    /// 异步创建 Agent需要的AGENTS.md和Skill文件
    /// </summary>
    private static async Task WriteAgentTemplatesAsync(string rootDirectory, string projectName, CancellationToken cancellationToken)
    {
        // AGENTS.md
        var agentsMdPath = Path.Combine(rootDirectory, AgentTemplates.AgentsMdFileName);
        await File.WriteAllTextAsync(agentsMdPath, AgentTemplates.AgentsMdContent(projectName), cancellationToken);

        // SKILL.md for .codex/skills/ukit-analyze
        var skillDir = Path.Combine(rootDirectory, AgentTemplates.SkillDirectory);
        Directory.CreateDirectory(skillDir);
        var skillPath = Path.Combine(skillDir, AgentTemplates.SkillFileName);
        await File.WriteAllTextAsync(skillPath, AgentTemplates.SkillMdContent, cancellationToken);
    }

    private static async Task WriteDescriptorAsync(string path, UkitProjectDescriptor descriptor, CancellationToken cancellationToken)
    {
        var document = new IniDocument();
        document.SetValue(DescriptorSection, "FormatVersion", descriptor.FormatVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        document.SetValue(DescriptorSection, "ProjectName", descriptor.ProjectName);
        document.SetValue(DescriptorSection, "ContentRoot", descriptor.ContentRoot);
        document.SetValue(DescriptorSection, "ConfigRoot", descriptor.ConfigRoot);
        document.SetValue(DescriptorSection, "SavedRoot", descriptor.SavedRoot);
        document.SetValue(DescriptorSection, "IntermediateRoot", descriptor.IntermediateRoot);
        await document.SaveAsync(path, cancellationToken);
    }

    private static async Task<UkitProjectDescriptor> ReadDescriptorAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"找不到工程描述文件: {path}");
        }

        var document = IniDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        if (!int.TryParse(document.GetValue(DescriptorSection, "FormatVersion"), out var formatVersion))
        {
            throw new InvalidDataException(".ukit 缺少或包含无效的 UnrealKit.Project/FormatVersion。");
        }

        return new UkitProjectDescriptor(formatVersion, RequireValue(document, "ProjectName"), RequireValue(document, "ContentRoot"), RequireValue(document, "ConfigRoot"), RequireValue(document, "SavedRoot"), RequireValue(document, "IntermediateRoot"));
    }

    private static async Task WriteSettingsAsync(string path, ProjectSettings settings, CancellationToken cancellationToken)
    {
        var document = new IniDocument();
        document.SetValue(SettingsSection, "SettingsVersion", ProjectSettingsFormat.CurrentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        document.SetValue(SettingsSection, "UnrealProjectName", settings.UnrealProjectName);
        document.SetValue(SettingsSection, "LocalWorkingDirectory", settings.LocalWorkingDirectory);
        document.SetValue(SettingsSection, "RemoteControlHttpPort", settings.RemoteControlHttpPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        document.SetValue(SettingsSection, "RemoteControlObjectPath", settings.RemoteControlObjectPath);
        document.SetValue(SettingsSection, "RemoteControlFunctionName", settings.RemoteControlFunctionName);
        document.SetValue(SettingsSection, "RemoteControlCommandParameter", settings.RemoteControlCommandParameter);
        document.SetValue(SettingsSection, "DefaultCaptureTag", settings.DefaultCaptureTag);
        document.SetValue(SettingsSection, "DefaultExportDirectory", settings.DefaultExportDirectory);

        // 只写出已配置的平台。写一份全默认值的空节会让「该平台未配置」无法与
        // 「配置过但字段留空」区分，用户删掉平台后下次打开又会看到它。
        foreach (var profile in settings.ConfiguredProfiles)
        {
            PlatformProfileIni.Write(document, profile);
        }

        foreach (var preset in settings.LaunchParameterPresets)
        {
            document.SetValue(PresetsSection, preset.Name, preset.Arguments);
        }

        foreach (var sequence in settings.ConsoleSequences)
        {
            document.SetValue(ConsoleSequencesSection, sequence.Name, sequence.StepsDefinition);
        }

        if (!string.IsNullOrWhiteSpace(settings.PreCaptureSequence))
            document.SetValue(SettingsSection, "PreCaptureSequence", settings.PreCaptureSequence);

        if (!string.IsNullOrWhiteSpace(settings.PostCaptureSequence))
            document.SetValue(SettingsSection, "PostCaptureSequence", settings.PostCaptureSequence);

        await document.SaveAsync(path, cancellationToken);
    }

    private static async Task<ProjectSettings> ReadSettingsAsync(string defaultGameIniPath, string projectName, CancellationToken cancellationToken)
    {
        var defaults = ProjectSettings.CreateDefaults(projectName);
        var basePath = ResolveBaseGameIniPath();
        var layered = await LayeredIniDocument.FromFilesAsync(basePath, defaultGameIniPath, cancellationToken);
        ProjectSettingsFormat.RequireSupportedVersion(
            layered.Override.GetValue(SettingsSection, "SettingsVersion"),
            layered.Override.HasSection(SettingsSection),
            defaultGameIniPath);

        var configuredPresets = layered.GetSection(PresetsSection);
        var presets = LaunchParameterPresetDefaults.All
            .Select(defaultPreset => configuredPresets.TryGetValue(defaultPreset.Name, out var arguments)
                ? defaultPreset with { Arguments = arguments }
                : defaultPreset)
            .Concat(configuredPresets
                .Where(pair => !LaunchParameterPresetDefaults.All.Any(defaultPreset => string.Equals(defaultPreset.Name, pair.Key, StringComparison.OrdinalIgnoreCase)))
                .Select(pair => new LaunchParameterPreset(pair.Key, pair.Value, string.Empty, true)))
            .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var configuredSequences = layered.GetSection(ConsoleSequencesSection);
        var sequences = configuredSequences
            .Select(kvp => new ConsoleSequencePreset(kvp.Key, kvp.Value, string.Empty))
            .ToList();

        return new ProjectSettings(
            layered.GetValue(SettingsSection, "UnrealProjectName") ?? defaults.UnrealProjectName,
            layered.GetValue(SettingsSection, "LocalWorkingDirectory") ?? defaults.LocalWorkingDirectory,
            layered.GetValue(SettingsSection, "DefaultCaptureTag") ?? defaults.DefaultCaptureTag,
            layered.GetValue(SettingsSection, "DefaultExportDirectory") ?? defaults.DefaultExportDirectory,
            presets,
            sequences,
            layered.GetValue(SettingsSection, "PreCaptureSequence"),
            layered.GetValue(SettingsSection, "PostCaptureSequence"),
            PlatformProfileIni.Read<AndroidPlatformProfile>(layered, TargetPlatform.Android),
            PlatformProfileIni.Read<Win64PlatformProfile>(layered, TargetPlatform.Win64),
            ParseRemoteControlPort(layered.GetValue(SettingsSection, "RemoteControlHttpPort"), defaults.RemoteControlHttpPort),
            RequireRemoteControlValue(layered.GetValue(SettingsSection, "RemoteControlObjectPath"), defaults.RemoteControlObjectPath),
            RequireRemoteControlValue(layered.GetValue(SettingsSection, "RemoteControlFunctionName"), defaults.RemoteControlFunctionName),
            RequireRemoteControlValue(layered.GetValue(SettingsSection, "RemoteControlCommandParameter"), defaults.RemoteControlCommandParameter));
    }

    private static string RequireValue(IniDocument document, string key)
    {
        return document.GetValue(DescriptorSection, key) is { Length: > 0 } value ? value : throw new InvalidDataException($".ukit 缺少必需字段 {DescriptorSection}/{key}。");
    }

    /// <summary>
    /// .ukit文件路径
    /// </summary>
    private static string GetProjectFilePath(string projectFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        var fullPath = Path.GetFullPath(projectFilePath);
        bool extValid = string.Equals(Path.GetExtension(fullPath), ".ukit", StringComparison.OrdinalIgnoreCase);
        return extValid ? fullPath : throw new ArgumentException("工程描述文件必须使用 .ukit 扩展名。", nameof(projectFilePath));
    }

    /// <summary>
    /// 验证项目名称
    /// </summary>
    private static void ValidateProjectName(string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName) || projectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || projectName is "." or "..")
        {
            throw new ArgumentException("工程名称不能为空，且不能包含文件名非法字符。", nameof(projectName));
        }
    }

    private static void ValidateSettings(ProjectSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.UnrealProjectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.DefaultCaptureTag);
        ValidateCaptureTag(settings.DefaultCaptureTag);

        // 只校验已配置平台，且由 profile 自己校验：Android 的 Unix 模板与 Win64 的
        // 绝对本机路径规则不同，在此处按平台分支等于把平台知识重新散出去。
        foreach (var profile in settings.ConfiguredProfiles)
        {
            profile.Validate();
        }

        if (settings.RemoteControlHttpPort is < 1 or > 65535)
        {
            throw new ArgumentException("RemoteControlHttpPort must be between 1 and 65535.", nameof(settings));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(settings.RemoteControlObjectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.RemoteControlFunctionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.RemoteControlCommandParameter);
    }

    private static void ValidateCaptureTag(string tag)
    {
        if (tag.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || tag.Contains('/') || tag.Contains('\\') || tag is "." or "..")
        {
            throw new ArgumentException("Default capture tag must be a single valid directory name.", nameof(tag));
        }
    }

    private static void ValidateProjectName(string projectName, ICollection<Diagnostic> diagnostics, string path)
    {
        if (string.IsNullOrWhiteSpace(projectName) || projectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || projectName is "." or "..")
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "UKIT005", "工程名称不能为空，且不能包含文件名非法字符。", path, "使用合法的工程名称。"));
        }
    }

    private static void ValidateRoot(string rootName, string fieldName, string rootDirectory, ICollection<Diagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(rootName) || Path.IsPathRooted(rootName) || rootName.Contains("..", StringComparison.Ordinal) || rootName.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "UKIT006", $"{fieldName} 必须是工程内的相对目录名。", rootDirectory, "使用例如 Content 或 Saved 的相对目录名。"));
            return;
        }

        var directoryPath = Path.Combine(rootDirectory, rootName);
        if (!Directory.Exists(directoryPath))
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "UKIT007", $"缺少必需工程目录: {rootName}", directoryPath, "创建该目录或修正 .ukit 中的根目录配置。"));
        }
    }

    /// <summary>
    /// 解析 Remote Control 的字符串配置。`Key=` 在 INI 里存为空串而不是 null，
    /// 直接 `?? 默认值` 会让空值绕过默认值，最后在发送指令时才以参数名报错。
    /// 空值与未配置同义，一律回退到默认值。
    /// </summary>
    private static string RequireRemoteControlValue(string? value, string defaultValue) =>
        string.IsNullOrWhiteSpace(value) ? defaultValue : value;

    /// <summary>
    /// 解析 RemoteControlHttpPort。未配置时用默认值；配置了但非法必须报错，
    /// 不让「HTTP 30010」悄悄回退到默认端口，否则用户会误以为已切到目标端口。
    /// </summary>
    private static int ParseRemoteControlPort(string? value, int defaultPort)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultPort;
        }

        if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535)
        {
            throw new InvalidDataException($"RemoteControlHttpPort 配置无效: {value}。必须是 1 到 65535 之间的整数。");
        }

        return port;
    }

    private static void Report(IProgress<OperationProgress>? progress, string operationId, string stage, string message, int? current = null, int? total = null) => progress?.Report(new OperationProgress(operationId, stage, current, total, message));
}