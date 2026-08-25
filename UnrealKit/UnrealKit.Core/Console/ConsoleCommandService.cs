using UnrealKit.Core.Adb;
using UnrealKit.Core.Devices;
using UnrealKit.Core.Operations;

namespace UnrealKit.Core.Console;

/// <summary>
/// 控制台指令服务实现。
///
/// 依赖 IDeviceService 而不是 IAdbService：绑定 ADB 会让指令序列在结构上永远无法支持
/// 非 Android 平台。平台能力差异由 IDeviceService.Supports 声明，
/// 不支持的平台在此显式拒绝，而不是让调用方各自分支。
/// </summary>
public sealed class ConsoleCommandService : IConsoleCommandService
{
    private readonly IDeviceService _deviceService;
    private readonly TimeProvider? _timeProvider;

    public ConsoleCommandService(IDeviceService deviceService, TimeProvider? timeProvider = null)
    {
        _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
        _timeProvider = timeProvider;
    }

    /// <summary>兼容既有调用方：由 AdbService 构造 Android 设备服务。</summary>
    public ConsoleCommandService(AdbService adbService, TimeProvider? timeProvider = null)
        : this(new AdbDeviceService(adbService), timeProvider)
    {
    }

    /// <summary>该设备平台是否支持控制台指令。</summary>
    public bool IsSupported => _deviceService.Supports(DeviceCapability.SendConsoleCommand);

    private IDevice ResolveDevice(string deviceId) =>
        DeviceReference.Create(deviceId, _deviceService.Platform);

    public async Task<ConsoleCommandResult> SendAsync(
        string serialNumber,
        ConsoleCommand command,
        string? packageName = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);
        ArgumentNullException.ThrowIfNull(command);

        progress?.Report(new OperationProgress("console-send", "Sending", null, null, $"Sending: {command.Command}"));

        var timeProvider = _timeProvider ?? TimeProvider.System;
        var startedAt = timeProvider.GetLocalNow();
        var result = await _deviceService.SendConsoleCommandAsync(ResolveDevice(serialNumber), command.Command, packageName, progress, cancellationToken);
        var completedAt = timeProvider.GetLocalNow();

        return new ConsoleCommandResult(
            command,
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            startedAt,
            completedAt);
    }

    public async Task<SequenceExecutionResult> RunSequenceAsync(
        SequenceExecutionRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Sequence);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DeviceSerialNumber);

        var timeProvider = _timeProvider ?? TimeProvider.System;
        var timeout = request.Timeout ?? TimeSpan.FromMinutes(5);
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var linkedToken = linkedCts.Token;

        var startedAt = timeProvider.GetLocalNow();
        var stepResults = new List<SequenceStepResult>();

        try
        {
            var steps = FlattenSteps(request.Sequence.Steps);
            for (var i = 0; i < steps.Count; i++)
            {
                linkedToken.ThrowIfCancellationRequested();
                var step = steps[i];
                progress?.Report(new OperationProgress("console-sequence", step.Type.ToString(), i + 1, steps.Count,
                    DescribeStep(step)));

                var stepResult = await ExecuteStepAsync(step, i, request, progress, linkedToken);
                stepResults.Add(stepResult);

                if (!stepResult.Succeeded)
                {
                    progress?.Report(new OperationProgress("console-sequence", "Failed", i + 1, steps.Count,
                        $"Step {i + 1} failed: {stepResult.Error ?? "non-zero exit code"}"));
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                stepResults.Add(new SequenceStepResult(-1, Error: $"Sequence timed out after {timeout.TotalSeconds:F0}s."));
            }
            else
            {
                stepResults.Add(new SequenceStepResult(-1, Error: "Sequence was cancelled."));
            }
        }

        var completedAt = timeProvider.GetLocalNow();
        return new SequenceExecutionResult(request.Sequence, stepResults, startedAt, completedAt);
    }

    private async Task<SequenceStepResult> ExecuteStepAsync(
        SequenceStep step,
        int index,
        SequenceExecutionRequest request,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        switch (step.Type)
        {
            case SequenceStepType.Command:
                if (step.Command is null)
                    return new SequenceStepResult(index, step, Error: "Command step has no command.");

                var result = await SendAsync(request.DeviceSerialNumber, step.Command, request.PackageName, progress, cancellationToken);
                return new SequenceStepResult(index, step, CommandResult: result);

            case SequenceStepType.Wait:
                if (step.WaitDuration is { } duration && duration > TimeSpan.Zero)
                {
                    progress?.Report(new OperationProgress("console-sequence", "Wait", null, null,
                        $"Waiting {duration.TotalSeconds:F1}s..."));
                    await Task.Delay(duration, cancellationToken);
                }

                return new SequenceStepResult(index, step);

            case SequenceStepType.Tag:
                progress?.Report(new OperationProgress("console-sequence", "Tag", null, null,
                    $"Marker: {step.Marker}"));
                return new SequenceStepResult(index, step);

            case SequenceStepType.Group:
                if (step.Children is { Count: > 0 })
                {
                    foreach (var child in step.Children)
                    {
                        var childResult = await ExecuteStepAsync(child, index, request, progress, cancellationToken);
                        if (!childResult.Succeeded) return childResult;
                    }
                }

                return new SequenceStepResult(index, step);

            default:
                return new SequenceStepResult(index, step, Error: $"Unknown step type: {step.Type}");
        }
    }

    private static IReadOnlyList<SequenceStep> FlattenSteps(IReadOnlyList<SequenceStep> steps)
    {
        // Sequences are executed linearly; groups are not flattened at this level — they're processed recursively in ExecuteStepAsync.
        return steps;
    }

    
    public async Task<LogcatConditionResult> RunConditionalAsync(
        string serialNumber,
        LogcatConditionStep condition,
        string? packageName = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);
        ArgumentNullException.ThrowIfNull(condition);

        var timeout = condition.Timeout ?? TimeSpan.FromSeconds(30);
        progress?.Report(new OperationProgress("console-conditional", "Waiting", null, null,
            $"Waiting for logcat pattern: {condition.Pattern} (timeout: {timeout.TotalSeconds:F0}s)"));

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await foreach (var line in _deviceService.StreamLogAsync(ResolveDevice(serialNumber), filter: null, linkedCts.Token))
            {
                if (line.Contains(condition.Pattern, StringComparison.Ordinal))
                {
                    progress?.Report(new OperationProgress("console-conditional", "Matched", null, null,
                        $"Pattern matched: {condition.Pattern}"));

                    return await ExecuteConditionActionAsync(serialNumber, condition, line, packageName, progress, cancellationToken);
                }
            }

            return LogcatConditionResult.Timeout(condition);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return LogcatConditionResult.Timeout(condition);
        }
        catch (OperationCanceledException)
        {
            return LogcatConditionResult.Cancelled(condition);
        }
    }

    private async Task<LogcatConditionResult> ExecuteConditionActionAsync(
        string serialNumber,
        LogcatConditionStep condition,
        string matchedLine,
        string? packageName,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        switch (condition.Action.Type)
        {
            case ConditionActionType.SendCommand:
                if (string.IsNullOrWhiteSpace(condition.Action.Argument))
                    return new LogcatConditionResult(condition, matchedLine, null, false, "SendCommand action missing command text.");

                var cmdResult = await SendAsync(serialNumber, ConsoleCommand.Create(condition.Action.Argument), packageName, progress, cancellationToken);
                return LogcatConditionResult.Success(condition, matchedLine, cmdResult);

            case ConditionActionType.CaptureTag:
                return LogcatConditionResult.Success(condition, matchedLine);

            case ConditionActionType.Fail:
                return new LogcatConditionResult(condition, matchedLine, null, false, condition.Action.Argument ?? "Condition failure triggered.");

            case ConditionActionType.Retry:
                // Retry is handled at the caller level by re-invoking RunConditionalAsync
                return LogcatConditionResult.Success(condition, matchedLine);

            default:
                return new LogcatConditionResult(condition, matchedLine, null, false, $"Unknown action type: {condition.Action.Type}");
        }
    }

    private static string DescribeStep(SequenceStep step) => step.Type switch
    {
        SequenceStepType.Command => $"Execute: {step.Command?.Command}",
        SequenceStepType.Wait => $"Wait: {step.WaitDuration?.TotalSeconds ?? 0:F1}s",
        SequenceStepType.Tag => $"Tag: {step.Marker}",
        SequenceStepType.Group => $"Group: {step.Marker} ({step.Children?.Count ?? 0} children)",
        _ => step.Type.ToString()
    };
}
