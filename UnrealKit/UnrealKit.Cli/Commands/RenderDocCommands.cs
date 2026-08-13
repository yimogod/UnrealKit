using System.Text.Json;
using UnrealKit.Core.Processes;
using UnrealKit.Core.RenderDoc;

namespace UnrealKit.Cli;

/// <summary>`unrealkit renderdoc run`：执行独立的 RenderDoc Python 脚本。</summary>
internal static class RenderDocCommands
{
    internal static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            return FailUsage();
        }

        return arguments[0].ToLowerInvariant() switch
        {
            "run" => await RunScriptAsync(arguments[1..]),
            _ => FailUsage()
        };
    }

    private static async Task<int> RunScriptAsync(string[] options)
    {
        CliOptions.EnsureOnly(options, CliOptions.Allowed("--python", "--script", "--args", "--output", "--workdir", "--format"));

        var pythonExecutable = CliOptions.GetRequired(options, "--python");
        var scriptPath = CliOptions.GetRequired(options, "--script");
        var scriptArguments = CliOptions.GetAll(options, "--args")
            .SelectMany(value => value.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .ToArray();
        var outputDirectory = CliOptions.GetOptional(options, "--output");
        var workingDirectory = CliOptions.GetOptional(options, "--workdir");
        var json = CliOptions.IsJsonFormat(options);

        var request = new RenderDocExecutionRequest(
            PythonExecutable: Path.GetFullPath(pythonExecutable),
            ScriptPath: Path.GetFullPath(scriptPath),
            ScriptArguments: scriptArguments,
            OutputDirectory: outputDirectory is not null ? Path.GetFullPath(outputDirectory) : null,
            WorkingDirectory: workingDirectory is not null ? Path.GetFullPath(workingDirectory) : null);

        var result = await new RenderDocService(new ProcessRunner()).ExecuteAsync(request);

        if (json)
        {
            WriteJson(result);
        }
        else
        {
            WriteText(result, pythonExecutable, scriptPath);
        }

        return result.Succeeded ? 0 : 1;
    }

    private static void WriteJson(RenderDocExecutionResult result) =>
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            result.ExitCode,
            result.Succeeded,
            result.OutputDirectory,
            DurationSeconds = result.Duration.TotalSeconds,
            StandardOutput = result.StandardOutput.Length > 0 ? result.StandardOutput : null,
            StandardError = result.StandardError.Length > 0 ? result.StandardError : null,
            Diagnostics = result.Diagnostics.Select(d => new
            {
                Severity = d.Severity.ToString(),
                d.Code,
                d.Message,
                d.Path,
                d.SuggestedFix,
                d.LineNumber
            })
        }, new JsonSerializerOptions { WriteIndented = true }));

    private static void WriteText(RenderDocExecutionResult result, string pythonExecutable, string scriptPath)
    {
        Console.WriteLine($"Script: {scriptPath}");
        Console.WriteLine($"Python: {pythonExecutable}");
        Console.WriteLine($"Exit code: {result.ExitCode} ({(result.Succeeded ? "success" : "failed")})");
        Console.WriteLine($"Duration: {result.Duration.TotalSeconds:F1}s");
        if (result.OutputDirectory is not null)
        {
            Console.WriteLine($"Output: {result.OutputDirectory}");
        }

        // 脚本自身的输出整段转发，便于直接复制排查。
        if (result.StandardOutput.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("--- stdout ---");
            Console.WriteLine(result.StandardOutput.TrimEnd());
        }

        if (result.StandardError.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("--- stderr ---");
            Console.WriteLine(result.StandardError.TrimEnd());
        }

        CliOutput.WriteDiagnostics(result.Diagnostics);
    }

    private static int FailUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  unrealkit renderdoc run --python <python.exe> --script <script.py> [--args <space-separated args>] [--output <dir>] [--workdir <dir>] [--format text|json]");
        return 2;
    }
}
