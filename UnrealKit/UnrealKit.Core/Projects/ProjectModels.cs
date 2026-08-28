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
/// 预设组合模式：<see cref="Exclusive"/> 表示组内互斥（同组最多选一个），
/// <see cref="Coexist"/> 表示组内可同时存在（无约束）。
/// </summary>
public enum LaunchParameterGroupMode
{
    /// <summary>组内可同时存在，不施加任何约束。</summary>
    Coexist,
    /// <summary>组内互斥，同组最多选一个。</summary>
    Exclusive,
}

/// <summary>
/// 启动参数预设
/// </summary>
public sealed record LaunchParameterPreset(string Name, string Arguments, string Description);

/// <summary>
/// 启动参数预设分组：把有互斥关系的预设放进同一组。<see cref="Mode"/> 决定组内约束，
/// 成员是预设名。分组是组合约束的唯一来源——预设本身不携带「是否可组合」属性，
/// 因此新增预设不必写死可组合标记，只需按需归组。
/// </summary>
public sealed record LaunchParameterPresetGroup(string Name, LaunchParameterGroupMode Mode, IReadOnlyList<string> Members);

/// <summary>
/// 设备别名表：设备标识 → 人类可读别名。
///
/// 键是设备标识（Android 为 ADB 序列号，Win64 为 <c>localhost</c>），与
/// <see cref="Devices.IDevice.Id"/> 同一取值，因此别名可以在任何列出设备的地方按 Id 查到，
/// 不需要额外一次设备查询。ADB 序列号大小写不敏感（<c>DeviceResolver</c> 的
/// <c>--device</c> 匹配也是），别名查找与之一致。
///
/// 别名是纯展示信息：任何操作仍以设备标识为准，别名不参与设备选择，
/// 否则同一别名配到两台设备就会变成一次隐式选择。
/// </summary>
public sealed record DeviceAliasMap
{
    public static DeviceAliasMap Empty { get; } = new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    private readonly IReadOnlyDictionary<string, string> _aliases;

    private DeviceAliasMap(IReadOnlyDictionary<string, string> aliases) => _aliases = aliases;

    /// <summary>
    /// 由配置条目构造。键或值为空白的条目被丢弃——INI 中的 <c>Key=</c> 是空串而不是缺失，
    /// 留下它会让设备显示一个空别名，看起来像「配过但名字是空的」。
    /// </summary>
    public static DeviceAliasMap Create(IEnumerable<KeyValuePair<string, string>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (deviceId, alias) in entries)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            aliases[deviceId.Trim()] = alias.Trim();
        }

        return aliases.Count == 0 ? Empty : new DeviceAliasMap(aliases);
    }

    /// <summary>已配置的别名条目，按设备标识排序，供写回配置与展示。</summary>
    public IEnumerable<KeyValuePair<string, string>> Entries =>
        _aliases.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public int Count => _aliases.Count;

    /// <summary>
    /// 取设备别名。未配置返回 null——调用方据此显示原始标识，
    /// 不要用标识本身冒充别名，否则「配过别名」与「没配」在界面上无从区分。
    /// </summary>
    public string? TryGet(string deviceId) =>
        !string.IsNullOrWhiteSpace(deviceId) && _aliases.TryGetValue(deviceId.Trim(), out var alias) ? alias : null;

    /// <summary>
    /// 记录相等性按内容比较：默认的引用相等会让 <c>ProjectSettings</c> 的
    /// <c>with</c> 复制在别名未变时也判为不同。
    /// </summary>
    public bool Equals(DeviceAliasMap? other) =>
        other is not null
        && _aliases.Count == other._aliases.Count
        && _aliases.All(pair =>
            other._aliases.TryGetValue(pair.Key, out var alias) && string.Equals(pair.Value, alias, StringComparison.Ordinal));

    public override int GetHashCode() => _aliases.Count;
}

/// <summary>
/// FTP 下载配置。主机 / 端口 / 凭据跨平台共享（一个 FTP 服务器），
/// 各平台在各自 profile 中只配置自己的 <c>FtpPath</c> 父目录。
///
/// <see cref="Password"/> 是敏感信息：界面用密码框掩码，日志与命令行输出不得打印明文。
/// </summary>
public sealed record FtpSettings(string Host, int Port, string Username, string Password)
{
    public const int DefaultPort = 21;

    public static FtpSettings CreateDefaults() => new(string.Empty, DefaultPort, string.Empty, string.Empty);

    /// <summary>是否已配置主机。主机为空即视为未启用 FTP 下载。</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}

/// <summary>
/// 控制台预设指令的类型，决定界面用什么控件、以及能否读回当前值。
/// </summary>
public enum ConsoleCommandKind
{
    /// <summary>开关型 cvar，界面出复选框，发送 <c>&lt;cvar&gt; 0|1</c>。</summary>
    Bool,

    /// <summary>数值型 cvar，界面出输入框，发送 <c>&lt;cvar&gt; &lt;值&gt;</c>。</summary>
    Value,

    /// <summary>动作指令（如 <c>stat unit</c>），无参数，也没有「当前值」可读。</summary>
    Action
}

/// <summary>
/// 一条控制台预设指令。
///
/// <see cref="Cvar"/> 与 <see cref="Command"/> 分开而不是共用一个字段：Bool/Value 需要
/// 光秃秃的 cvar 名去读回当前值（读回函数的参数就是 cvar 名），发送时才拼上值；
/// Action 是一整条指令文本，没有可读回的变量。共用一个字段会让「该读哪个名字」无从判断。
/// </summary>
public sealed record ConsoleCommandPreset(
    string Name,
    ConsoleCommandKind Kind,
    string Group,
    string? Cvar,
    string? Command,
    string? DefaultValue,
    string Description)
{
    /// <summary>Bool/Value 有当前值可读；Action 没有。</summary>
    public bool SupportsReadBack => Kind is ConsoleCommandKind.Bool or ConsoleCommandKind.Value;

    /// <summary>
    /// 合成要发送的指令文本。
    /// Value 型的 <paramref name="value"/> 为空时回落到 <see cref="DefaultValue"/>——
    /// 界面初值就是 DefaultValue，用户清空输入框不应变成发一条缺参数的指令。
    /// </summary>
    public string BuildCommand(bool boolValue = false, string? value = null) => Kind switch
    {
        ConsoleCommandKind.Bool => $"{RequireCvar()} {(boolValue ? 1 : 0)}",
        ConsoleCommandKind.Value => $"{RequireCvar()} {ResolveValue(value)}",
        ConsoleCommandKind.Action => RequireCommand(),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unsupported console command kind.")
    };

    /// <summary>
    /// 读回当前值时使用的取值类型。Action 无值可读，调用方应先查 <see cref="SupportsReadBack"/>。
    ///
    /// 是方法而不是属性：<c>ProjectSettings</c> 会被整体序列化进 <c>CaptureManifest.json</c>，
    /// 序列化器会读取每个公开属性，一个对 Action 型抛异常的属性会让整次采集归档失败。
    /// </summary>
    public CommandChannel.ConsoleVariableType ResolveVariableType() => Kind switch
    {
        ConsoleCommandKind.Bool => CommandChannel.ConsoleVariableType.Bool,
        ConsoleCommandKind.Value => CommandChannel.ConsoleVariableType.Number,
        _ => throw new InvalidOperationException(
            $"预设 '{Name}' 是 {Kind} 型，没有可读回的控制台变量。请先检查 {nameof(SupportsReadBack)}。")
    };

    private string RequireCvar() => string.IsNullOrWhiteSpace(Cvar)
        ? throw new InvalidOperationException($"预设 '{Name}' 是 {Kind} 型，必须配置 Cvar。")
        : Cvar.Trim();

    private string RequireCommand() => string.IsNullOrWhiteSpace(Command)
        ? throw new InvalidOperationException($"预设 '{Name}' 是 Action 型，必须配置 Command。")
        : Command.Trim();

    private string ResolveValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) ? value.Trim()
        : !string.IsNullOrWhiteSpace(DefaultValue) ? DefaultValue.Trim()
        : throw new InvalidOperationException($"预设 '{Name}' 是 Value 型，需要一个取值。");
}

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
    IReadOnlyList<LaunchParameterPresetGroup> LaunchParameterGroups,
    IReadOnlyList<ConsoleSequencePreset> ConsoleSequences,
    IReadOnlyList<ConsoleCommandPreset> ConsoleCommandPresets,
    string? PreCaptureSequence,
    string? PostCaptureSequence,
    AndroidPlatformProfile? Android = null,
    Win64PlatformProfile? Win64 = null,
    DeviceAliasMap? DeviceAliases = null,
    FtpSettings? Ftp = null,
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
        LaunchParameterGroups: LaunchParameterPresetDefaults.Groups,
        ConsoleSequences: [],
        ConsoleCommandPresets: ConsoleCommandPresetDefaults.All,
        PreCaptureSequence: null,
        PostCaptureSequence: null,
        Android: AndroidPlatformProfile.CreateDefaults(),
        Win64: Win64PlatformProfile.CreateDefaults());

    /// <summary>
    /// 设备别名表。未配置时是空表而不是 null，调用方不必每处判空。
    /// </summary>
    public DeviceAliasMap Aliases => DeviceAliases ?? DeviceAliasMap.Empty;

    /// <summary>
    /// FTP 下载配置。未配置时是默认空配置而不是 null，调用方不必每处判空。
    /// </summary>
    public FtpSettings FtpSettings => Ftp ?? FtpSettings.CreateDefaults();

    /// <summary>
    /// 取设备别名，未配置返回 null。
    /// </summary>
    public string? TryGetDeviceAlias(string deviceId) => Aliases.TryGet(deviceId);

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
    private const string traceBase = "-statnamedevents -tracefile -trace=cpu,frame,log,bookmark,task,counter,stats";

    private const string traceClient_Default = $"{traceBase},gpu,screenshot,region,file,loadtime,assetloadtime,rdg,audio,audiomixer";
    private const string traceClient_All =     $"{traceBase},gpu,screenshot,region,file,loadtime,assetloadtime,rdg,audio,audiomixer,memory,net -NetTrace=1";
    private const string traceClient_Network = $"{traceBase},net -statnamedevents ";
    private const string traceClient_Memory =  $"{traceBase},memory,metadata,assetmetadata -llm -llmcsv";

    public static IReadOnlyList<LaunchParameterPreset> All { get; } =
    [
        new("Mem.LLM", "-llm", "启动llm."),
        new("Mem.LLM_CSV", "-llmcsv", "启动llm csv."),
        new("Render.OpenGL", "-OpenGLES", "使用OpenGL渲染."),
        new("Render.Vulkan", "-vulkan", "使用Vulkan渲染."),
        new("Trace.Client_All", traceClient_All, "trace default, 网络, 内存."),
        new("Trace.Client_Default", traceClient_Default, "默认trace(cpu,gpu,load)."),
        new("Trace.Client_Network", traceClient_Network, "网络trace."),
        new("Trace.Client_Memory", traceClient_Memory, "内存trace.")
    ];

    /// <summary>
    /// 内置预设分组：渲染后端 OpenGL 与 Vulkan 二选一，故同属互斥组 Render。
    /// 其余预设（内存、追踪）彼此与渲染后端都正交，不归组即可自由叠加。
    /// </summary>
    public static IReadOnlyList<LaunchParameterPresetGroup> Groups { get; } =
    [
        new("Render", LaunchParameterGroupMode.Exclusive, ["Render.OpenGL", "Render.Vulkan"])
    ];
}

/// <summary>
/// 控制台预设指令的内置默认值。
///
/// 与 <see cref="LaunchParameterPresetDefaults"/> 同构：这里是打底值，
/// <c>Config/DefaultGame.ini</c> 的 <c>[UnrealKit.ConsoleCommandPresets]</c> 按名覆盖或追加，
/// 因此团队改预设不需要改代码。
/// </summary>
public static class ConsoleCommandPresetDefaults
{
    private const string Rendering = "Rendering";
    private const string Lod = "LOD";
    private const string Memory = "Memory";
    private const string Gc = "GC";
    private const string Stats = "Stats";

    private static ConsoleCommandPreset Toggle(string group, string cvar, string description) =>
        new(cvar, ConsoleCommandKind.Bool, group, cvar, null, null, description);

    private static ConsoleCommandPreset Value(string group, string cvar, string defaultValue, string description) =>
        new(cvar, ConsoleCommandKind.Value, group, cvar, null, defaultValue, description);

    private static ConsoleCommandPreset Action(string group, string command, string description) =>
        new(command, ConsoleCommandKind.Action, group, null, command, null, description);

    public static IReadOnlyList<ConsoleCommandPreset> All { get; } =
    [
        Toggle(Rendering, "showflag.Fog", "雾效开关."),
        Toggle(Rendering, "showflag.Lighting", "光照开关."),
        Toggle(Rendering, "showflag.PostProcessing", "后处理开关."),
        Toggle(Rendering, "r.Shadow.Virtual.Enable", "虚拟阴影图开关."),
        Value(Rendering, "r.ScreenPercentage", "100", "渲染分辨率百分比."),
        Value(Rendering, "r.MobileContentScaleFactor", "1", "移动端内容缩放系数."),
        Value(Lod, "r.ForceLOD", "0", "强制所有静态网格使用指定 LOD, -1 关闭强制."),
        Value(Lod, "r.SkeletalMeshLODBias", "0", "骨骼网格 LOD 偏移."),
        Value(Lod, "r.StaticMeshLODDistanceScale", "1", "静态网格 LOD 切换距离缩放."),
        Value(Memory, "r.Streaming.PoolSize", "1024", "纹理流送池上限 (MB)."),
        Toggle(Memory, "r.Streaming.LimitPoolSizeToVRAM", "限制流送池不超过显存."),
        Value(Gc, "gc.TimeBetweenPurgingPendingKillObjects", "60", "两次 GC 之间的间隔 (秒)."),
        Toggle(Gc, "gc.AllowParallelGC", "并行 GC 开关."),
        Action(Stats, "stat unit", "显示帧耗时分解 (game/draw/gpu)."),
        Action(Stats, "stat fps", "显示帧率."),
        Action(Stats, "stat memory", "显示内存统计."),
        Action(Stats, "stat rhi", "显示 RHI 统计."),
        Action(Stats, "stat none", "关闭所有 stat 显示."),
        Action(Memory, "memreport -full", "输出完整 memreport 到设备 Saved 目录.")
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

    /// <summary>
    /// 该工程的用户设置文件（平台作用域等界面选择）。与 <see cref="ConfigFilePath"/> 同目录但分文件：
    /// <c>DefaultGame.ini</c> 是可版本化的工程配置，不该因为「谁上次看的是哪个平台」产生 diff。
    /// </summary>
    public string UserSettingFilePath => Path.Combine(ConfigDir, "UserSetting.ini");

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
