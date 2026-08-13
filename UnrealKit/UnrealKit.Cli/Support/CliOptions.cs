namespace UnrealKit.Cli;

/// <summary>
/// 命令行参数的读取与校验，所有子命令共用同一套规则：
/// 选项以 <c>--</c> 开头、必须紧跟一个值（显式声明的开关除外），未识别的选项直接报错而不是忽略。
/// </summary>
internal static class CliOptions
{
    /// <summary>构造大小写不敏感的选项白名单，供 <see cref="EnsureOnly"/> 使用。</summary>
    internal static IReadOnlySet<string> Allowed(params string[] optionNames) =>
        new HashSet<string>(optionNames, StringComparer.OrdinalIgnoreCase);

    internal static string GetRequired(string[] arguments, string optionName) =>
        GetOptional(arguments, optionName) ?? throw new ArgumentException($"Missing required option {optionName}.");

    internal static string? GetOptional(string[] arguments, string optionName)
    {
        var index = Array.FindIndex(arguments, argument => string.Equals(argument, optionName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{optionName} must be followed by a value.");
        }

        return arguments[index + 1];
    }

    /// <summary>读取可重复传入的选项，按出现顺序返回全部取值。</summary>
    internal static string[] GetAll(string[] arguments, string optionName)
    {
        var values = new List<string>();
        for (var index = 0; index < arguments.Length; index++)
        {
            if (!string.Equals(arguments[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (++index >= arguments.Length || arguments[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"{optionName} must be followed by a value.");
            }

            values.Add(arguments[index]);
        }

        return values.ToArray();
    }

    /// <summary>读取逗号分隔或重复传入的列表型选项（如 <c>--metrics</c>）。</summary>
    internal static string[] GetCommaSeparated(string[] arguments, string optionName) =>
        GetAll(arguments, optionName)
            .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

    internal static string? GetPositional(string[] arguments, int index) =>
        index >= 0 && index < arguments.Length && !arguments[index].StartsWith('-')
            ? arguments[index]
            : null;

    internal static bool HasFlag(string[] arguments, string optionName) =>
        arguments.Any(argument => string.Equals(argument, optionName, StringComparison.OrdinalIgnoreCase));

    /// <summary><c>--format</c> 默认 text；只接受 text/json，其它取值报错而不是回退默认值。</summary>
    internal static bool IsJsonFormat(string[] arguments)
    {
        var format = GetOptional(arguments, "--format");
        if (format is null || string.Equals(format, "text", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(format, "json", StringComparison.OrdinalIgnoreCase)
            ? true
            : throw new ArgumentException("--format must be either text or json.");
    }

    internal static void EnsureOnly(string[] arguments, IReadOnlySet<string> allowedOptions, IReadOnlySet<string>? flagOptions = null)
    {
        for (var index = 0; index < arguments.Length; index++)
        {
            if (!arguments[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unsupported option: {arguments[index]}.");
            }

            if (!allowedOptions.Contains(arguments[index]))
            {
                throw new ArgumentException($"Unsupported option: {arguments[index]}.");
            }

            if (flagOptions?.Contains(arguments[index]) == true)
            {
                continue;
            }

            if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"{arguments[index]} must be followed by a value.");
            }

            index++;
        }
    }

    /// <summary>
    /// 从参数尾部摘出 <c>--adb-path</c>。要求它是最后一个选项，
    /// 这样各子命令的选项白名单不必逐个声明它。
    /// </summary>
    internal static (string[] CommandArguments, string? AdbPath) ParseAdbPath(string[] arguments)
    {
        var pathIndex = Array.FindIndex(arguments, argument => string.Equals(argument, "--adb-path", StringComparison.OrdinalIgnoreCase));
        if (pathIndex < 0)
        {
            return (arguments, null);
        }

        if (pathIndex + 1 >= arguments.Length || pathIndex != arguments.Length - 2)
        {
            throw new ArgumentException("--adb-path must be followed by a path and must be the final option.");
        }

        return (arguments[..pathIndex], arguments[pathIndex + 1]);
    }
}
