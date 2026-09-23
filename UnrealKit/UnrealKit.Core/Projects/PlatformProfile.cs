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
    /// <summary>
    /// UE 在游戏根目录下存放 Saved 的固定子目录名。所有平台一致，因此不是可配置项：
    /// 由 Game 目录派生保证两者永远指向同一棵树。
    /// </summary>
    public const string SavedDirectoryName = "Saved";

    /// <summary>该配置描述的平台。</summary>
    public abstract TargetPlatform Platform { get; }

    /// <summary>该平台设备端的路径风格。</summary>
    public abstract DevicePathStyle PathStyle { get; }

    /// <summary>该平台在 FTP 服务器上的下载父目录（空串表示未配置）。</summary>
    public abstract string FtpPath { get; init; }

    /// <summary>解析 pak 时传给 CUE4Parse 的版本标志覆盖（null 表示无覆盖）。</summary>
    public abstract IReadOnlyDictionary<string, bool>? PakVersionOverrides { get; init; }

    /// <summary>
    /// 该平台的应用标识符。Android 是包名（如 <c>com.example.game</c>），
    /// Win64 是可执行文件名（如 <c>MyGame.exe</c>）——两者语义不同，但都回答同一个问题：
    /// 「在设备上，这个应用/进程叫什么」。统一到这一个成员上，是为了让只关心
    /// 「拿到应用标识」的上层代码（如展示、日志）不必按平台分支。
    /// </summary>
    public abstract string PackageName { get; init; }

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
    /// 由 Game 根目录派生 Saved 目录路径，按 <paramref name="style"/> 决定分隔符。
    /// 两个平台共用同一条规则：UE 自身把 Saved 固定放在 Game 目录下，
    /// 分别实现只会让两者的拼接规则悄悄跑偏。
    /// </summary>
    protected static string DeriveSavedRootPath(string gameRootPath, DevicePathStyle style) => style switch
    {
        DevicePathStyle.Unix => $"{gameRootPath.TrimEnd('/')}/{SavedDirectoryName}",
        DevicePathStyle.Windows => Path.Combine(gameRootPath, SavedDirectoryName),
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, "Unsupported device path style.")
    };

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
    string GameRoot,
    string AdbPath,
    string FtpPath = "",
    string PakFtpPath = "",
    string PakAesKey = "",
    IReadOnlyDictionary<string, bool>? PakVersionOverrides = null) : PlatformProfile
{
    /// <inheritdoc cref="PlatformProfile.PackageName" />
    /// <remarks>与主构造函数的 <c>PackageName</c> 参数是同一个值，这里只是补上基类要求的 override。</remarks>
    public override string PackageName { get; init; } = PackageName;

    /// <summary>设备端游戏根目录模板的默认值，与旧工具的 UE Saved 路径规则一致。</summary>
    public const string DefaultGameRoot =
        "/sdcard/Android/data/{PackageName}/files/UnrealGame/{UnrealProjectName}/{UnrealProjectName}";

    public override TargetPlatform Platform => TargetPlatform.Android;

    public override DevicePathStyle PathStyle => DevicePathStyle.Unix;

    public static AndroidPlatformProfile CreateDefaults() => new(
        PackageName: string.Empty,
        Activity: string.Empty,
        GameRoot: DefaultGameRoot,
        AdbPath: string.Empty,
        FtpPath: string.Empty,
        PakFtpPath: string.Empty,
        PakAesKey: string.Empty,
        PakVersionOverrides: null);

    public override PlatformTarget Resolve(string unrealProjectName)
    {
        if (string.IsNullOrWhiteSpace(PackageName))
        {
            throw new InvalidOperationException(
                "Android 操作需要包名。请在工程配置的 Android 分组中填写 PackageName。");
        }

        // Saved 目录由 Game 目录派生，与 Win64 一致：UE 自身把 Saved 固定放在
        // 游戏根目录下，单独配置只会让两者对不上，且错开的路径会让采集拉到空目录。
        var gameRoot = ValidateDevicePath(
            Expand(GameRoot, unrealProjectName), PathStyle, nameof(GameRoot));

        return new PlatformTarget(
            Platform,
            PathStyle,
            ProcessIdentity: PackageName,
            LaunchTarget: PackageName,
            LaunchActivity: Activity,
            GameRootPath: gameRoot,
            SavedRootPath: DeriveSavedRootPath(gameRoot, PathStyle));
    }

    public override void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(GameRoot, nameof(GameRoot));

        // 模板含未展开的占位符，只校验风格：占位符值可能尚未填写，
        // 但模板本身写成 Windows 路径一定是错的。
        ValidateDevicePath(GameRoot, PathStyle, nameof(GameRoot));
    }

    private string Expand(string template, string unrealProjectName) => template
        .Replace("{PackageName}", PackageName, StringComparison.Ordinal)
        .Replace("{UnrealProjectName}", unrealProjectName, StringComparison.Ordinal);
}

/// <summary>
/// Win64 平台配置。「设备」是本机，因此设备端路径就是本机文件系统路径。
///
/// <see cref="PackageName"/> 是可执行文件名（如 <c>MyGame.exe</c>），与 Android 的
/// <see cref="AndroidPlatformProfile.PackageName"/> 同名不同义，但回答的是同一个问题——
/// 「这个应用/进程叫什么」，因此复用基类的抽象成员名。
///
/// <see cref="GameRoot"/> 不是配置项：Win64 构建通过「安装包」页从 FTP 下载到本地，用户已经在
/// 该页选定了要用的版本目录，再让配置重复记一份绝对路径只会在换版本时跟着失效。它由调用方
/// （GUI/CLI）在操作发起时注入，不写入 DefaultGame.ini，也不参与 <see cref="Validate"/>——
/// 与 <see cref="AndroidPlatformProfile.GameRoot"/> 字段同名但生命周期不同：Android 那份是
/// 带占位符的版本化配置模板，这份是运行时注入的已展开绝对路径。
/// </summary>
public sealed record Win64PlatformProfile(
    string PackageName,
    string FtpPath = "",
    string PakFtpPath = "",
    string PakAesKey = "",
    IReadOnlyDictionary<string, bool>? PakVersionOverrides = null,
    string? GameRoot = null) : PlatformProfile
{
    public override TargetPlatform Platform => TargetPlatform.Win64;

    public override DevicePathStyle PathStyle => DevicePathStyle.Windows;

    public static Win64PlatformProfile CreateDefaults() => new(
        PackageName: string.Empty,
        FtpPath: string.Empty,
        PakFtpPath: string.Empty,
        PakAesKey: string.Empty,
        PakVersionOverrides: null);

    public override PlatformTarget Resolve(string unrealProjectName)
    {
        if (string.IsNullOrWhiteSpace(PackageName))
        {
            throw new InvalidOperationException(
                "Win64 操作需要可执行文件名。请在工程配置的 Win64 分组中填写 PackageName（例如 MyGame.exe）。");
        }

        if (string.IsNullOrWhiteSpace(GameRoot))
        {
            throw new InvalidOperationException(
                "Win64 操作需要选择一个已下载的构建包。请先在「安装包」页下载或选择一个 Win64 构建包。");
        }

        // exe 只在包目录第一层找，不递归、不跨层猜测：找不到就是配置或下载有问题，
        // 静默往深处搜会把「装错了目录」误报成「找到了别的 exe」。
        var executablePath = Path.Combine(GameRoot, PackageName);
        if (!File.Exists(executablePath))
        {
            throw new InvalidOperationException(
                $"在构建包目录中找不到可执行文件: {executablePath}。" +
                $"请确认「安装包」页选中的包与工程配置的 PackageName（{PackageName}）一致。");
        }

        // 进程名取可执行文件名：性能计数器按进程名而不是完整路径匹配。
        var processName = Path.GetFileNameWithoutExtension(PackageName);
        if (string.IsNullOrWhiteSpace(processName))
        {
            throw new InvalidOperationException(
                $"无法从 Win64 可执行文件名推导进程名: {PackageName}");
        }

        var gameRoot = Path.GetFullPath(GameRoot);

        return new PlatformTarget(
            Platform,
            PathStyle,
            ProcessIdentity: processName,
            LaunchTarget: executablePath,
            LaunchActivity: null,
            GameRootPath: gameRoot,
            SavedRootPath: DeriveSavedRootPath(gameRoot, PathStyle));
    }

    public override void Validate()
    {
        // 可以暂时留空（配置尚未填完），但填了就必须是纯文件名，不含路径分隔符——
        // 路径由运行时的构建包目录决定，配置里混入路径会造成两处来源打架。
        if (!string.IsNullOrWhiteSpace(PackageName) && Path.GetFileName(PackageName) != PackageName)
        {
            throw new ArgumentException(
                $"PackageName 必须是不含路径的文件名，当前值: {PackageName}", nameof(PackageName));
        }
    }
}
