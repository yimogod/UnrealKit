using UnrealKit.Core.Diagnostics;

namespace UnrealKit.Core.Projects;

/// <summary>
/// 目标平台枚举。Core 层不得依据平台做 UI 分支，仅用于采集策略选择。
/// </summary>
public enum TargetPlatform
{
    Android,
    Win64
}

public sealed record UkitProjectDescriptor(
    int FormatVersion,
    string ProjectName,
    string ContentRoot,
    string ConfigRoot,
    string SavedRoot,
    string IntermediateRoot)
{
    public const int CurrentFormatVersion = 1;

    public static UkitProjectDescriptor CreateDefault(string projectName) => new(
        CurrentFormatVersion, projectName, "Content", "Config", "Saved", "Intermediate");
}

public sealed record ConsoleSequencePreset(string Name, string StepsDefinition, string Description)
{
    public static ConsoleSequencePreset Create(string name, string stepsDefinition, string? description = null) =>
        new(name.Trim(), stepsDefinition.Trim(), description?.Trim() ?? string.Empty);

    /// <summary>
    /// 将步骤定义字符串解析为命令序列定义。
    /// 格式：cmd1; wait 2000; cmd2; tag marker; cmd3
    /// </summary>
    public Console.CommandSequenceDefinition ToSequenceDefinition()
    {
        var steps = new List<Console.SequenceStep>();
        foreach (var part in StepsDefinition.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("wait ", StringComparison.OrdinalIgnoreCase))
            {
                var msText = trimmed[5..].Trim();
                if (int.TryParse(msText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var ms) && ms > 0)
                    steps.Add(Console.SequenceStep.CreateWait(TimeSpan.FromMilliseconds(ms), trimmed));
                else
                    throw new FormatException($"无效的等待时间: {msText}");
            }
            else if (trimmed.StartsWith("tag ", StringComparison.OrdinalIgnoreCase))
            {
                steps.Add(Console.SequenceStep.CreateTag(trimmed[4..].Trim()));
            }
            else
            {
                steps.Add(Console.SequenceStep.CreateCommand(trimmed));
            }
        }

        return Console.CommandSequenceDefinition.Create(Name, Description, steps);
    }
}

public sealed record LaunchParameterPreset(string Name, string Arguments, string Description, bool IsComposable);

public sealed record ProjectSettings(
    string PackageName,
    string UnrealProjectName,
    string Activity,
    string DeviceGameRootTemplate,
    string DeviceSavedRootTemplate,
    string LocalWorkingDirectory,
    string AdbPath,
    string DefaultCaptureTag,
    string DefaultExportDirectory,
    IReadOnlyList<LaunchParameterPreset> LaunchParameterPresets,
    IReadOnlyList<ConsoleSequencePreset> ConsoleSequences,
    string? PreCaptureSequence,
    string? PostCaptureSequence,
    TargetPlatform Platform = TargetPlatform.Android,
    string? Win64Executable = null,
    string? Win64WorkingDirectory = null)
{
    public static ProjectSettings CreateDefaults(string projectName) => new(
        string.Empty,
        projectName,
        string.Empty,
        "/sdcard/Android/data/{PackageName}/files/UE4Game/{UnrealProjectName}/{UnrealProjectName}",
        "/sdcard/Android/data/{PackageName}/files/UE4Game/{UnrealProjectName}/{UnrealProjectName}/Saved",
        string.Empty,
        string.Empty,
        "Default",
        "Saved/Exports",
        LaunchParameterPresetDefaults.All,
        [],
        null,
        null,
        TargetPlatform.Android,
        null,
        null);
}

public static class LaunchParameterPresetDefaults
{
    public static IReadOnlyList<LaunchParameterPreset> All { get; } =
    [
        new("LLM", "-llm", "Enable Unreal Low Level Memory Tracker.", true),
        new("LLM CSV", "-llmcsv", "Enable LLM CSV output.", true),
        new("OpenGL", "-OpenGLES", "Use the OpenGL ES renderer.", false),
        new("Vulkan", "-vulkan", "Use the Vulkan renderer.", false),
        new("Trace Default", string.Empty, "Configure project-compatible Trace arguments in DefaultGame.ini.", false),
        new("Trace All", string.Empty, "Configure project-compatible Trace arguments in DefaultGame.ini.", false),
        new("Trace Network", string.Empty, "Configure project-compatible Trace arguments in DefaultGame.ini.", false),
        new("Trace Memory", string.Empty, "Configure project-compatible Trace arguments in DefaultGame.ini.", false),
        new("No Update", string.Empty, "Configure the legacy no-update argument in DefaultGame.ini.", false)
    ];
}

public sealed record ProjectConfigurationSnapshot(
    UkitProjectDescriptor Descriptor,
    ProjectSettings Settings,
    DateTimeOffset CapturedAt);

public sealed record ProjectValidationResult(IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

public sealed record UkitProject(
    string ProjectFilePath,
    string RootDirectory,
    UkitProjectDescriptor Descriptor,
    ProjectSettings Settings)
{
    public string ProjectDir => RootDirectory;

    public string ContentDir => Path.Combine(ProjectDir, Descriptor.ContentRoot);

    public string ConfigDir => Path.Combine(ProjectDir, Descriptor.ConfigRoot);

    public string SavedDir => Path.Combine(ProjectDir, Descriptor.SavedRoot);

    public string IntermediateDir => Path.Combine(ProjectDir, Descriptor.IntermediateRoot);

    public string ConfigFilePath => Path.Combine(ConfigDir, "DefaultGame.ini");

    public ProjectConfigurationSnapshot CreateConfigurationSnapshot() =>
        new(Descriptor, Settings, DateTimeOffset.UtcNow);
}

public sealed record CreateProjectRequest(string DirectoryPath, string ProjectName);

public sealed record ProjectCreateResult(UkitProject Project, ProjectValidationResult Validation);