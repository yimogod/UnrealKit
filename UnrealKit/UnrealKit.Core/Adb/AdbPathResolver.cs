namespace UnrealKit.Core.Adb;

/// <summary>
/// ADB 路径的来源
/// </summary>
public enum AdbPathSource
{
    Explicit,
    ProjectSettings,
    EnvironmentVariable,
    Path
}

/// <summary>
/// ADB 路径解析尝试的状态
/// </summary>
public enum AdbPathAttemptStatus
{
    NotConfigured,
    NotFound,
    Resolved
}

/// <summary>
/// ADB 路径解析数据
/// </summary>
public sealed record AdbPathAttempt(
    AdbPathSource Source,
    string Description,
    string? CandidatePath,
    AdbPathAttemptStatus Status);

public sealed record AdbPathResolution(
    string? ResolvedPath,
    IReadOnlyList<AdbPathAttempt> Attempts)
{
    public bool IsResolved => ResolvedPath is not null;
}

/// <summary>
/// ADB 路径解析异常
/// </summary>
public sealed class AdbPathResolutionException : InvalidOperationException
{
    public AdbPathResolutionException(AdbPathResolution resolution)
        : base("无法找到可执行的 ADB. 请使用 --adb-path 指定 adb.exe, 或在项目配置, ADB_PATH, ANDROID_SDK_ROOT, ANDROID_HOME 或 PATH 中配置 Android SDK Platform-Tools. ")
    {
        Resolution = resolution;
    }

    public AdbPathResolution Resolution { get; }
}

/// <summary>
/// ADB 路径解析器
/// </summary>
public sealed class AdbPathResolver
{
    private static readonly string[] EnvironmentVariableNames = ["ADB_PATH", "ANDROID_SDK_ROOT", "ANDROID_HOME"];
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly Func<string, bool> _fileExists;

    // 是否为 Windows 系统
    private readonly bool _isWindows;

    private string ExecutableName => _isWindows ? "adb.exe" : "adb";

    public AdbPathResolver(
        Func<string, string?>? getEnvironmentVariable = null,
        Func<string, bool>? fileExists = null,
        bool? isWindows = null)
    {
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        _fileExists = fileExists ?? File.Exists;
        _isWindows = isWindows ?? OperatingSystem.IsWindows();
    }

    /// <summary>
    /// 解析 ADB 路径的执行函数
    /// </summary>
    public AdbPathResolution Resolve(string? explicitPath, string? projectAdbPath)
    {
        var attempts = new List<AdbPathAttempt>();
        if (TryResolveConfiguredPath(AdbPathSource.Explicit, "--adb-path", explicitPath, attempts, out var resolvedPath) ||
            TryResolveConfiguredPath(AdbPathSource.ProjectSettings, "ProjectSettings.AdbPath", projectAdbPath, attempts, out resolvedPath) ||
            TryResolveEnvironment(attempts, out resolvedPath) ||
            TryResolvePath(attempts, out resolvedPath))
        {
            return new AdbPathResolution(resolvedPath, attempts);
        }

        return new AdbPathResolution(null, attempts);
    }

    /// <summary>
    /// 解析 ADB 路径，确保成功解析
    /// </summary>
    public string ResolveRequired(string? explicitPath, string? projectAdbPath)
    {
        var resolution = Resolve(explicitPath, projectAdbPath);
        return resolution.ResolvedPath ?? throw new AdbPathResolutionException(resolution);
    }

    /// <summary>
    /// 尝试解析配置的 ADB 路径
    /// </summary>
    private bool TryResolveEnvironment(ICollection<AdbPathAttempt> attempts, out string? resolvedPath)
    {
        foreach (var variableName in EnvironmentVariableNames)
        {
            var configuredValue = _getEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(configuredValue))
            {
                attempts.Add(new AdbPathAttempt(AdbPathSource.EnvironmentVariable, variableName, null, AdbPathAttemptStatus.NotConfigured));
                continue;
            }

            var candidatePath = variableName == "ADB_PATH"
                ? configuredValue
                : Path.Combine(configuredValue, "platform-tools", ExecutableName);
            if (_fileExists(candidatePath))
            {
                attempts.Add(new AdbPathAttempt(AdbPathSource.EnvironmentVariable, variableName, candidatePath, AdbPathAttemptStatus.Resolved));
                resolvedPath = Path.GetFullPath(candidatePath);
                return true;
            }

            attempts.Add(new AdbPathAttempt(AdbPathSource.EnvironmentVariable, variableName, candidatePath, AdbPathAttemptStatus.NotFound));
        }

        resolvedPath = null;
        return false;
    }

    /// <summary>
    /// 尝试解析 PATH 环境变量中的 ADB 路径
    /// </summary>
    private bool TryResolvePath(ICollection<AdbPathAttempt> attempts, out string? resolvedPath)
    {
        var pathValue = _getEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            attempts.Add(new AdbPathAttempt(AdbPathSource.Path, "PATH", null, AdbPathAttemptStatus.NotConfigured));
            resolvedPath = null;
            return false;
        }

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidatePath = Path.Combine(directory, ExecutableName);
            if (_fileExists(candidatePath))
            {
                attempts.Add(new AdbPathAttempt(AdbPathSource.Path, "PATH", candidatePath, AdbPathAttemptStatus.Resolved));
                resolvedPath = Path.GetFullPath(candidatePath);
                return true;
            }
        }

        attempts.Add(new AdbPathAttempt(AdbPathSource.Path, "PATH", null, AdbPathAttemptStatus.NotFound));
        resolvedPath = null;
        return false;
    }

    /// <summary>
    /// 尝试解析配置的 ADB 路径
    /// </summary> 
    private bool TryResolveConfiguredPath(AdbPathSource source, string description, string? configuredPath, ICollection<AdbPathAttempt> attempts, out string? resolvedPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            attempts.Add(new AdbPathAttempt(source, description, null, AdbPathAttemptStatus.NotConfigured));
            resolvedPath = null;
            return false;
        }

        if (_fileExists(configuredPath))
        {
            attempts.Add(new AdbPathAttempt(source, description, configuredPath, AdbPathAttemptStatus.Resolved));
            resolvedPath = Path.GetFullPath(configuredPath);
            return true;
        }

        attempts.Add(new AdbPathAttempt(source, description, configuredPath, AdbPathAttemptStatus.NotFound));
        resolvedPath = null;
        return false;
    }
}
