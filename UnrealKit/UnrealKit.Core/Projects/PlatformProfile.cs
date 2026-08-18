namespace UnrealKit.Core.Projects;

/// <summary>
/// 单个平台的工程配置。同一工程可以同时配置多个平台，它们互不排斥——
/// 一个工程既能跑 Android 又能跑 Win64 是常态，不是二选一。
///
/// 「本次操作用哪个平台」不由配置决定，而由所选设备决定，因此这里没有
/// 「当前平台」字段。配置只回答「这个平台怎么跑」。
///
/// 平台相关的路径展开与校验都在子类内部完成，向外只暴露平台中立的
/// <see cref="PlatformTarget"/>，调用方因此不需要按平台分支。
/// </summary>
public abstract record PlatformProfile
{
    /// <summary>该配置描述的平台。</summary>
    public abstract TargetPlatform Platform { get; }

    /// <summary>该平台设备端的路径风格。</summary>
    public abstract DevicePathStyle PathStyle { get; }

    /// <summary>平台的稳定字符串标识。</summary>
    public string PlatformName => PlatformNames.ToName(Platform);

    /// <summary>
    /// 将配置解析为本次操作的落地值。缺少必需配置时抛出并指明缺哪一项，
    /// 不回退到猜测值——错误的路径会让采集拉到空目录却报告成功。
    /// </summary>
    /// <param name="unrealProjectName">UE 工程名，来自 <see cref="ProjectSettings.UnrealProjectName"/>。</param>
    public abstract PlatformTarget Resolve(string unrealProjectName);

    /// <summary>
    /// 保存前校验该平台配置。此处只校验「写下来的值本身是否合法」，
    /// 「是否完整到足以执行操作」由 <see cref="Resolve"/> 负责——用户可以先存一份半成品配置。
    /// </summary>
    public abstract void Validate();

    /// <summary>
    /// 校验设备端路径。Unix 风格要求正斜杠绝对路径；Windows 风格要求完全限定路径——
    /// 相对路径会按当前进程工作目录解析，GUI 与 CLI 下指向不同位置。
    /// </summary>
    protected static string ValidateDevicePath(string path, DevicePathStyle style, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        if (path.Contains('\0'))
        {
            throw new ArgumentException("设备端路径不能包含空字符。", parameterName);
        }

        switch (style)
        {
            case DevicePathStyle.Unix:
                if (!path.StartsWith('/') || path.Contains('\\'))
                {
                    throw new ArgumentException($"{parameterName} 必须是以 / 开头的绝对 Unix 路径，当前值: {path}", parameterName);
                }

                return path;

            case DevicePathStyle.Windows:
                if (!Path.IsPathFullyQualified(path))
                {
                    throw new ArgumentException($"{parameterName} 必须是绝对路径，当前值: {path}", parameterName);
                }

                return Path.GetFullPath(path);

            default:
                throw new ArgumentOutOfRangeException(nameof(style), style, "Unsupported device path style.");
        }
    }
}

/// <summary>
/// Android 平台配置。设备端路径由模板展开，占位符 <c>{PackageName}</c> 与
/// <c>{UnrealProjectName}</c> 在 <see cref="Resolve"/> 时替换。
/// </summary>
public sealed record AndroidPlatformProfile(
    string PackageName,
    string Activity,
    string GameRootTemplate,
    string SavedRootTemplate,
    string AdbPath) : PlatformProfile
{
    /// <summary>设备端游戏根目录模板的默认值，与旧工具的 UE Saved 路径规则一致。</summary>
    public const string DefaultGameRootTemplate =
        "/sdcard/Android/data/{PackageName}/files/UE4Game/{UnrealProjectName}/{UnrealProjectName}";

    /// <summary>设备端 Saved 目录模板的默认值。</summary>
    public const string DefaultSavedRootTemplate = $"{DefaultGameRootTemplate}/Saved";

    public override TargetPlatform Platform => TargetPlatform.Android;

    public override DevicePathStyle PathStyle => DevicePathStyle.Unix;

    public static AndroidPlatformProfile CreateDefaults() => new(
        PackageName: string.Empty,
        Activity: string.Empty,
        GameRootTemplate: DefaultGameRootTemplate,
        SavedRootTemplate: DefaultSavedRootTemplate,
        AdbPath: string.Empty);

    public override PlatformTarget Resolve(string unrealProjectName)
    {
        if (string.IsNullOrWhiteSpace(PackageName))
        {
            throw new InvalidOperationException(
                "Android 操作需要包名。请在工程配置的 Android 分组中填写 PackageName。");
        }

        return new PlatformTarget(
            Platform,
            PathStyle,
            ProcessIdentity: PackageName,
            LaunchTarget: PackageName,
            LaunchActivity: Activity,
            GameRootPath: ValidateDevicePath(Expand(GameRootTemplate, unrealProjectName), PathStyle, nameof(GameRootTemplate)),
            SavedRootPath: ValidateDevicePath(Expand(SavedRootTemplate, unrealProjectName), PathStyle, nameof(SavedRootTemplate)));
    }

    public override void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(GameRootTemplate, nameof(GameRootTemplate));
        ArgumentException.ThrowIfNullOrWhiteSpace(SavedRootTemplate, nameof(SavedRootTemplate));

        // 模板含未展开的占位符，只校验风格：占位符值可能尚未填写，
        // 但模板本身写成 Windows 路径一定是错的。
        ValidateDevicePath(GameRootTemplate, PathStyle, nameof(GameRootTemplate));
        ValidateDevicePath(SavedRootTemplate, PathStyle, nameof(SavedRootTemplate));
    }

    private string Expand(string template, string unrealProjectName) => template
        .Replace("{PackageName}", PackageName, StringComparison.Ordinal)
        .Replace("{UnrealProjectName}", unrealProjectName, StringComparison.Ordinal);
}

/// <summary>
/// Win64 平台配置。「设备」是本机，因此设备端路径就是本机文件系统路径。
/// </summary>
public sealed record Win64PlatformProfile(
    string Executable,
    string WorkingDirectory) : PlatformProfile
{
    public override TargetPlatform Platform => TargetPlatform.Win64;

    public override DevicePathStyle PathStyle => DevicePathStyle.Windows;

    public static Win64PlatformProfile CreateDefaults() => new(
        Executable: string.Empty,
        WorkingDirectory: string.Empty);

    public override PlatformTarget Resolve(string unrealProjectName)
    {
        if (string.IsNullOrWhiteSpace(WorkingDirectory))
        {
            throw new InvalidOperationException(
                "Win64 操作需要工作目录以定位 UE 的 Saved 目录与 uecommandline.txt。" +
                "请在工程配置的 Win64 分组中填写 WorkingDirectory。");
        }

        if (string.IsNullOrWhiteSpace(Executable))
        {
            throw new InvalidOperationException(
                "Win64 操作需要可执行文件路径。请在工程配置的 Win64 分组中填写 Executable。");
        }

        // 进程名取可执行文件名：性能计数器按进程名而不是完整路径匹配。
        var processName = Path.GetFileNameWithoutExtension(Executable);
        if (string.IsNullOrWhiteSpace(processName))
        {
            throw new InvalidOperationException(
                $"无法从 Win64 可执行文件路径推导进程名: {Executable}");
        }

        var gameRoot = ValidateDevicePath(
            Path.Combine(WorkingDirectory, unrealProjectName), PathStyle, nameof(WorkingDirectory));

        return new PlatformTarget(
            Platform,
            PathStyle,
            ProcessIdentity: processName,
            LaunchTarget: ValidateDevicePath(Executable, PathStyle, nameof(Executable)),
            LaunchActivity: null,
            GameRootPath: gameRoot,
            SavedRootPath: Path.Combine(gameRoot, "Saved"));
    }

    public override void Validate()
    {
        // 两项都可以暂时留空（配置尚未填完），但填了就必须是绝对路径。
        if (!string.IsNullOrWhiteSpace(Executable))
        {
            ValidateDevicePath(Executable, PathStyle, nameof(Executable));
        }

        if (!string.IsNullOrWhiteSpace(WorkingDirectory))
        {
            ValidateDevicePath(WorkingDirectory, PathStyle, nameof(WorkingDirectory));
        }
    }
}
