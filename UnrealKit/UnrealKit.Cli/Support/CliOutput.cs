using UnrealKit.Core.Adb;
using UnrealKit.Core.Devices;
using UnrealKit.Core.Diagnostics;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;

namespace UnrealKit.Cli;

/// <summary>
/// 控制台呈现的共用部分：诊断、校验结果、外部命令失败详情。
/// Core 只返回结构化结果，措辞与排版都在这里决定。
/// </summary>
internal static class CliOutput
{
    internal static void WriteDiagnostics(IReadOnlyList<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            var line = diagnostic.LineNumber is null ? string.Empty : $" line {diagnostic.LineNumber}";
            Console.Error.WriteLine($"[{diagnostic.Severity}] {diagnostic.Code}{line}: {diagnostic.Message}");
            if (!string.IsNullOrWhiteSpace(diagnostic.SuggestedFix))
            {
                Console.Error.WriteLine($"  Fix: {diagnostic.SuggestedFix}");
            }
        }
    }

    internal static int WriteValidation(ProjectValidationResult validation)
    {
        foreach (var diagnostic in validation.Diagnostics)
        {
            Console.WriteLine($"[{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message}{(diagnostic.Path is null ? string.Empty : $" ({diagnostic.Path})")}");
        }

        Console.WriteLine(validation.IsValid ? "Validation passed." : "Validation failed.");
        return validation.IsValid ? 0 : 1;
    }

    internal static void WriteProcessOutput(ProcessOutput output)
    {
        var writer = output.Stream == ProcessOutputStream.StandardError ? Console.Error : Console.Out;
        writer.WriteLine(output.Text);
    }

    internal static void WriteAdbFailure(AdbCommandException exception) =>
        WriteCommandFailure(exception.Result.ExitCode, exception.Result.StandardError);

    internal static void WriteDeviceCommandFailure(DeviceCommandException exception) =>
        WriteCommandFailure(exception.Result.ExitCode, exception.Result.StandardError);

    /// <summary>逐条列出 adb 路径解析的尝试，让「找不到 adb」的失败可定位到具体来源。</summary>
    internal static void WriteAdbPathDiagnostics(AdbPathResolution resolution)
    {
        foreach (var attempt in resolution.Attempts)
        {
            var path = attempt.CandidatePath is null ? string.Empty : $" - {attempt.CandidatePath}";
            Console.Error.WriteLine($"ADB {attempt.Source} ({attempt.Description}): {attempt.Status}{path}");
        }
    }

    private static void WriteCommandFailure(int exitCode, string? standardError)
    {
        Console.Error.WriteLine($"Exit code: {exitCode}");
        if (!string.IsNullOrWhiteSpace(standardError))
        {
            Console.Error.WriteLine("stderr:");
            Console.Error.WriteLine(standardError.TrimEnd());
        }
    }
}
