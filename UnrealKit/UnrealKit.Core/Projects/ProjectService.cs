using UnrealKit.Core.Diagnostics;
using UnrealKit.Core.Operations;

namespace UnrealKit.Core.Projects;

public sealed class ProjectService : IProjectService
{
    private const string DescriptorSection = "UnrealKit.Project";
    private const string SettingsSection = "UnrealKit.ProjectSettings";
    private const string PresetsSection = "UnrealKit.LaunchPresets";
    private readonly IOperationLogger _logger;

    public ProjectService(IOperationLogger? logger = null)
    {
        _logger = logger ?? NullOperationLogger.Instance;
    }

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
        await WriteDescriptorAsync(descriptorPath, descriptor, cancellationToken);
        await WriteSettingsAsync(Path.Combine(rootDirectory, descriptor.ConfigRoot, "DefaultGame.ini"), settings, cancellationToken);
        var validation = await ValidateProjectAsync(descriptorPath, progress, cancellationToken);
        Report(progress, operationId, "Completed", "工程创建完成。", 2, 2);
        _logger.Log(new LogEvent(DateTimeOffset.UtcNow, LogLevel.Information, operationId, "Project created", new Dictionary<string, string> { ["path"] = descriptorPath }));
        return new ProjectCreateResult(new UkitProject(descriptorPath, rootDirectory, descriptor, settings), validation);
    }

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

    public async Task<ProjectValidationResult> ValidateProjectAsync(string projectFilePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        const string operationId = "project-validate";
        var diagnostics = new List<Diagnostic>();
        var fullPath = GetProjectFilePath(projectFilePath);
        UkitProjectDescriptor descriptor;
        try
        {
            descriptor = await ReadDescriptorAsync(fullPath, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new ProjectValidationResult([new Diagnostic(DiagnosticSeverity.Error, "UKIT001", exception.Message, fullPath, "修复或恢复 .ukit 文件后重试。")]);
        }

        Report(progress, operationId, "Checking", "正在校验工程目录和配置。", 1, 2);
        if (descriptor.FormatVersion != UkitProjectDescriptor.CurrentFormatVersion)
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "UKIT002", $"不支持工程格式版本 {descriptor.FormatVersion}。当前版本仅支持 {UkitProjectDescriptor.CurrentFormatVersion}。", fullPath, "使用兼容版本或未来的迁移工具。"));
        }

        ValidateProjectName(descriptor.ProjectName, diagnostics, fullPath);
        var rootDirectory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        ValidateRoot(descriptor.ContentRoot, "ContentRoot", rootDirectory, diagnostics);
        ValidateRoot(descriptor.ConfigRoot, "ConfigRoot", rootDirectory, diagnostics);
        ValidateRoot(descriptor.SavedRoot, "SavedRoot", rootDirectory, diagnostics);
        ValidateRoot(descriptor.IntermediateRoot, "IntermediateRoot", rootDirectory, diagnostics);

        var settingsPath = Path.Combine(rootDirectory, descriptor.ConfigRoot, "DefaultGame.ini");
        if (!File.Exists(settingsPath))
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "UKIT003", "未找到可选配置文件 DefaultGame.ini。", settingsPath, "创建 Config/DefaultGame.ini 以保存项目默认配置。"));
        }

        Report(progress, operationId, "Completed", "工程校验完成。", 2, 2);
        return new ProjectValidationResult(diagnostics);
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
        document.SetValue(SettingsSection, "PackageName", settings.PackageName);
        document.SetValue(SettingsSection, "UnrealProjectName", settings.UnrealProjectName);
        document.SetValue(SettingsSection, "Activity", settings.Activity);
        document.SetValue(SettingsSection, "DeviceGameRootTemplate", settings.DeviceGameRootTemplate);
        document.SetValue(SettingsSection, "DeviceSavedRootTemplate", settings.DeviceSavedRootTemplate);
        document.SetValue(SettingsSection, "LocalWorkingDirectory", settings.LocalWorkingDirectory);
        document.SetValue(SettingsSection, "AdbPath", settings.AdbPath);
        document.SetValue(SettingsSection, "DefaultCaptureTag", settings.DefaultCaptureTag);
        document.SetValue(SettingsSection, "DefaultExportDirectory", settings.DefaultExportDirectory);
        foreach (var preset in settings.LaunchParameterPresets)
        {
            document.SetValue(PresetsSection, preset.Name, preset.Arguments);
        }
        await document.SaveAsync(path, cancellationToken);
    }

    private static async Task<ProjectSettings> ReadSettingsAsync(string path, string projectName, CancellationToken cancellationToken)
    {
        var defaults = ProjectSettings.CreateDefaults(projectName);
        if (!File.Exists(path))
        {
            return defaults;
        }

        var document = IniDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        var configuredPresets = document.GetSection(PresetsSection);
        var presets = LaunchParameterPresetDefaults.All
            .Select(defaultPreset => configuredPresets.TryGetValue(defaultPreset.Name, out var arguments)
                ? defaultPreset with { Arguments = arguments }
                : defaultPreset)
            .Concat(configuredPresets
                .Where(pair => !LaunchParameterPresetDefaults.All.Any(defaultPreset => string.Equals(defaultPreset.Name, pair.Key, StringComparison.OrdinalIgnoreCase)))
                .Select(pair => new LaunchParameterPreset(pair.Key, pair.Value, string.Empty, true)))
            .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new ProjectSettings(
            document.GetValue(SettingsSection, "PackageName") ?? defaults.PackageName,
            document.GetValue(SettingsSection, "UnrealProjectName") ?? defaults.UnrealProjectName,
            document.GetValue(SettingsSection, "Activity") ?? defaults.Activity,
            document.GetValue(SettingsSection, "DeviceGameRootTemplate") ?? defaults.DeviceGameRootTemplate,
            document.GetValue(SettingsSection, "DeviceSavedRootTemplate") ?? defaults.DeviceSavedRootTemplate,
            document.GetValue(SettingsSection, "LocalWorkingDirectory") ?? defaults.LocalWorkingDirectory,
            document.GetValue(SettingsSection, "AdbPath") ?? defaults.AdbPath,
            document.GetValue(SettingsSection, "DefaultCaptureTag") ?? defaults.DefaultCaptureTag,
            document.GetValue(SettingsSection, "DefaultExportDirectory") ?? defaults.DefaultExportDirectory,
            presets);
    }

    private static string RequireValue(IniDocument document, string key) => document.GetValue(DescriptorSection, key) is { Length: > 0 } value ? value : throw new InvalidDataException($".ukit 缺少必需字段 {DescriptorSection}/{key}。");

    private static string GetProjectFilePath(string projectFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        var fullPath = Path.GetFullPath(projectFilePath);
        return string.Equals(Path.GetExtension(fullPath), ".ukit", StringComparison.OrdinalIgnoreCase) ? fullPath : throw new ArgumentException("工程描述文件必须使用 .ukit 扩展名。", nameof(projectFilePath));
    }

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
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.DeviceGameRootTemplate);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.DeviceSavedRootTemplate);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.DefaultCaptureTag);
        ValidateCaptureTag(settings.DefaultCaptureTag);
        ValidateUnixTemplate(settings.DeviceGameRootTemplate, nameof(settings.DeviceGameRootTemplate));
        ValidateUnixTemplate(settings.DeviceSavedRootTemplate, nameof(settings.DeviceSavedRootTemplate));
    }

    private static void ValidateCaptureTag(string tag)
    {
        if (tag.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || tag.Contains('/') || tag.Contains('\\') || tag is "." or "..")
        {
            throw new ArgumentException("Default capture tag must be a single valid directory name.", nameof(tag));
        }
    }

    private static void ValidateUnixTemplate(string path, string parameterName)
    {
        if (!path.StartsWith("/", StringComparison.Ordinal) || path.Contains('\\') || path.Contains('\0'))
        {
            throw new ArgumentException("Device path templates must be absolute Unix paths.", parameterName);
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

    private static void Report(IProgress<OperationProgress>? progress, string operationId, string stage, string message, int? current = null, int? total = null) => progress?.Report(new OperationProgress(operationId, stage, current, total, message));
}
