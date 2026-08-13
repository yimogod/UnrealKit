using UnrealKit.Core.Console;
using UnrealKit.Core.Launch;
using UnrealKit.Core.Projects;

namespace UnrealKit.Cli;

/// <summary>`unrealkit app start` 与 `unrealkit app console send|run`。</summary>
internal static class AppCommands
{
    internal static async Task<int> RunAsync(string[] arguments)
    {
        var (commandArguments, adbPath) = CliOptions.ParseAdbPath(arguments);
        if (commandArguments.Length == 0)
        {
            return FailUsage();
        }

        return commandArguments[0].ToLowerInvariant() switch
        {
            "start" => await StartAsync(commandArguments[1..], adbPath),
            "console" => await RunConsoleAsync(commandArguments[1..], adbPath),
            _ => FailUsage()
        };
    }

    private static async Task<int> StartAsync(string[] options, string? adbPath)
    {
        var project = await new ProjectService().OpenProjectAsync(CliOptions.GetRequired(options, "--project"));
        var (deviceService, deviceId) = await DeviceResolver.ResolveDeviceTargetAsync(project, options, adbPath);
        await new LaunchParameterService(deviceService).StartApplicationAsync(project, deviceId);
        return 0;
    }

    private static async Task<int> RunConsoleAsync(string[] arguments, string? adbPath)
    {
        var (commandArguments, parsedAdbPath) = CliOptions.ParseAdbPath(arguments);
        if (commandArguments.Length == 0)
        {
            return FailConsoleUsage();
        }

        adbPath ??= parsedAdbPath;

        return commandArguments[0].ToLowerInvariant() switch
        {
            "send" => await SendConsoleCommandAsync(commandArguments[1..], adbPath),
            "run" => await RunConsoleSequenceAsync(commandArguments[1..], adbPath),
            _ => FailConsoleUsage()
        };
    }

    private static async Task<int> SendConsoleCommandAsync(string[] options, string? adbPath)
    {
        CliOptions.EnsureOnly(options, CliOptions.Allowed("--device", "--cmd", "--project", "--adb-path"));
        var command = CliOptions.GetRequired(options, "--cmd");
        var project = await new ProjectService().OpenProjectAsync(CliOptions.GetRequired(options, "--project"));
        var (deviceService, deviceId) = await DeviceResolver.ResolveDeviceTargetAsync(project, options, adbPath);

        var consoleService = new ConsoleCommandService(deviceService);
        var result = await consoleService.SendAsync(
            deviceId,
            ConsoleCommand.Create(command),
            project.Settings.PackageName);

        Console.WriteLine($"Sent console command to {deviceId}: {command}");
        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"Failed with exit code {result.ExitCode}.");
            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                Console.Error.WriteLine(result.StandardError);
            }

            return 1;
        }

        Console.WriteLine("Command dispatched successfully.");
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            Console.WriteLine(result.StandardOutput);
        }

        return 0;
    }

    private static async Task<int> RunConsoleSequenceAsync(string[] options, string? adbPath)
    {
        CliOptions.EnsureOnly(options, CliOptions.Allowed("--project", "--device", "--sequence", "--cmds", "--adb-path"));
        var projectPath = CliOptions.GetRequired(options, "--project");
        var sequenceName = CliOptions.GetOptional(options, "--sequence");
        var inlineCmds = CliOptions.GetOptional(options, "--cmds");

        if (sequenceName is null && inlineCmds is null)
        {
            Console.Error.WriteLine("Either --sequence or --cmds is required.");
            return 2;
        }

        var project = await new ProjectService().OpenProjectAsync(projectPath);
        var (deviceService, deviceSerial) = await DeviceResolver.ResolveDeviceTargetAsync(project, options, adbPath);

        CommandSequenceDefinition sequence;
        if (sequenceName is not null)
        {
            var preset = project.Settings.ConsoleSequences
                .FirstOrDefault(s => string.Equals(s.Name, sequenceName, StringComparison.OrdinalIgnoreCase));
            if (preset is null)
            {
                var available = string.Join(", ", project.Settings.ConsoleSequences.Select(s => s.Name));
                Console.Error.WriteLine($"Sequence '{sequenceName}' not found in project presets. Available: {available}");
                return 2;
            }

            sequence = preset.ToSequenceDefinition();
        }
        else
        {
            sequence = new ConsoleSequencePreset("inline", inlineCmds!, string.Empty).ToSequenceDefinition();
        }

        var consoleService = new ConsoleCommandService(deviceService);
        var request = new SequenceExecutionRequest(sequence, deviceSerial, project.Settings.PackageName);

        Console.WriteLine($"Running sequence: {sequence.Name}");
        Console.WriteLine($"Device: {deviceSerial}");
        Console.WriteLine($"Steps: {sequence.Steps.Count}");
        Console.WriteLine();

        var result = await consoleService.RunSequenceAsync(request);
        WriteSequenceResult(result);
        return result.Succeeded ? 0 : 1;
    }

    private static void WriteSequenceResult(SequenceExecutionResult result)
    {
        foreach (var stepResult in result.StepResults)
        {
            var status = stepResult.Succeeded ? "OK" : "FAIL";
            Console.WriteLine($"  [{status}] Step {stepResult.StepIndex + 1}: {DescribeStep(stepResult.Step)}");
            if (stepResult.CommandResult is { } cmdResult)
            {
                Console.WriteLine($"         Exit: {cmdResult.ExitCode}, Duration: {cmdResult.Duration.TotalMilliseconds:F0}ms");
                if (!string.IsNullOrWhiteSpace(cmdResult.StandardOutput))
                {
                    Console.WriteLine($"         Output: {cmdResult.StandardOutput}");
                }

                if (!string.IsNullOrWhiteSpace(cmdResult.StandardError))
                {
                    Console.Error.WriteLine($"         Error: {cmdResult.StandardError}");
                }
            }

            if (!string.IsNullOrWhiteSpace(stepResult.Error))
            {
                Console.Error.WriteLine($"         Error: {stepResult.Error}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Sequence completed: {result.SuccessfulSteps}/{result.TotalSteps} steps OK, {result.FailedSteps} failed. Duration: {result.Duration.TotalSeconds:F1}s");
    }

    // 超时或取消的步骤没有对应的步骤定义，标注出来而不是显示成空白命令。
    private static string DescribeStep(SequenceStep? step) => step is null
        ? "(timeout/cancelled)"
        : step.Type switch
        {
            SequenceStepType.Command => $"CMD: {step.Command?.Command}",
            SequenceStepType.Wait => $"WAIT: {step.WaitDuration?.TotalSeconds ?? 0:F1}s",
            SequenceStepType.Tag => $"TAG: {step.Marker}",
            SequenceStepType.Group => $"GROUP: {step.Marker}",
            _ => step.Type.ToString()
        };

    private static int FailUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  unrealkit app start --project <project.ukit> --device <serial> [--adb-path <path>]");
        WriteConsoleUsageLines();
        return 2;
    }

    private static int FailConsoleUsage()
    {
        Console.Error.WriteLine("Usage:");
        WriteConsoleUsageLines();
        return 2;
    }

    private static void WriteConsoleUsageLines()
    {
        Console.Error.WriteLine("  unrealkit app console send --project <project.ukit> --device <serial> --cmd <command> [--adb-path <path>]");
        Console.Error.WriteLine("  unrealkit app console run --project <project.ukit> --device <serial> [--sequence <name>] [--cmds <inline>] [--adb-path <path>]");
    }
}