using UnrealKit.Core.Diagnostics;

namespace UnrealKit.Core.Projects;

/// <summary>
/// 项目描述符, 用于描述项目的基本信息.
/// </summary>
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

/// <summary>
/// 命令序列预设, 用于预定义命令序列.
/// </summary>
public sealed record ConsoleSequencePreset(string Name, string StepsDefinition, string Description)
{
    public static ConsoleSequencePreset Create(string name, string stepsDefinition, string? description = null) =>
        new(name.Trim(), stepsDefinition.Trim(), description?.Trim() ?? string.Empty);

    /// <summary>
    /// 将步骤定义字符串解析为命令序列定义, 
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

/// <summary>
/// 启动参数预设
/// </summary>
public sealed record LaunchParameterPreset(string Name, string Arguments, string Description, bool IsComposable);

/// <summary>
/// 项目设置。平台相关配置放在 <see cref="Android"/> / <see cref="Win64"/> 等 profile 中，
/// 各平台并存互不排斥——同一工程同时跑多个平台是常态。
///
/// 这里没有「当前平台」字段：本次操作打哪个平台由所选设备决定，属于会话状态，
/// 不写进版本化的工程配置。
/// </summary>
public sealed record ProjectSettings(
    string UnrealProjectName,
    string LocalWorkingDirectory,
    string DefaultCaptureTag,
    string DefaultExportDirectory,
    IReadOnlyList<LaunchParameterPreset> LaunchParameterPresets,
    IReadOnlyList<ConsoleSequencePreset> ConsoleSequences,
    string? PreCaptureSequence,
    string? PostCaptureSequence,
    AndroidPlatformProfile? Android = null,
    Win64PlatformProfile? Win64 = null,
    int RemoteControlHttpPort = 30010,
    string RemoteControlObjectPath = "/Script/Engine.Default__KismetSystemLibrary",
    string RemoteControlFunctionName = "ExecuteConsoleCommand",
    string RemoteControlCommandParameter = "Command")
{
    /// <summary>
    /// 新建工程时两个平台都给出默认 profile：多平台工程是默认假设，
    /// 让用户先选平台再填配置会把会话选择塞回配置层。
    /// </summary>
    public static ProjectSettings CreateDefaults(string projectName) => new(
        UnrealProjectName: projectName,
        LocalWorkingDirectory: string.Empty,
        DefaultCaptureTag: "Default",
        DefaultExportDirectory: "Saved/Exports",
        LaunchParameterPresets: LaunchParameterPresetDefaults.All,
        ConsoleSequences: [],
        PreCaptureSequence: null,
        PostCaptureSequence: null,
        Android: AndroidPlatformProfile.CreateDefaults(),
        Win64: Win64PlatformProfile.CreateDefaults());

    /// <summary>
    /// 取指定平台的配置。返回 null 表示该平台未配置——调用方应报错并列出
    /// <see cref="ConfiguredPlatforms"/>，不要回退到其他平台的配置。
    /// </summary>
    public PlatformProfile? ProfileFor(TargetPlatform platform) => platform switch
    {
        TargetPlatform.Android => Android,
        TargetPlatform.Win64 => Win64,
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported platform.")
    };

    /// <summary>已配置的平台，按 <see cref="TargetPlatform"/> 声明顺序。</summary>
    public IEnumerable<PlatformProfile> ConfiguredProfiles =>
        Enum.GetValues<TargetPlatform>().Select(ProfileFor).OfType<PlatformProfile>();

    /// <summary>已配置的平台标识，用于失败提示中列出可选平台。</summary>
    public IReadOnlyList<string> ConfiguredPlatforms =>
        ConfiguredProfiles.Select(profile => profile.PlatformName).ToArray();

    /// <summary>
    /// 取指定平台的配置，未配置时抛出并列出已配置平台。
    /// </summary>
    public PlatformProfile RequireProfile(TargetPlatform platform, string? context = null)
    {
        var profile = ProfileFor(platform);
        if (profile is not null)
        {
            return profile;
        }

        var configured = ConfiguredPlatforms.Count == 0
            ? "(尚未配置任何平台)"
            : string.Join(", ", ConfiguredPlatforms);
        var prefix = context is null ? string.Empty : $"{context} ";
        throw new InvalidOperationException(
            $"{prefix}工程尚未配置 {PlatformNames.ToName(platform)} 平台。已配置的平台: {configured}。" +
            "请在工程配置中补全该平台，或改用已配置平台的设备。");
    }

    /// <summary>
    /// 解析指定平台的落地值。这是 Core 内各服务获取平台相关路径与进程标识的唯一入口。
    /// </summary>
    public PlatformTarget ResolveTarget(TargetPlatform platform, string? context = null) =>
        RequireProfile(platform, context).Resolve(UnrealProjectName);
}

/// <summary>
/// 游戏启动参数预设默认值
/// </summary>
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

/// <summary>
/// 项目配置快照
/// </summary>
public sealed record ProjectConfigurationSnapshot(
    UkitProjectDescriptor Descriptor,
    ProjectSettings Settings,
    DateTimeOffset CapturedAt);

/// <summary>
/// 项目验证结果
/// </summary>
public sealed record ProjectValidationResult(IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

/// <summary>
/// 项目实例
/// </summary>
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

/// <summary>
/// 创建项目请求
/// </summary>
public sealed record CreateProjectRequest(string DirectoryPath, string ProjectName);

/// <summary>
/// 项目创建结果
/// </summary>
public sealed record ProjectCreateResult(UkitProject Project, ProjectValidationResult Validation);