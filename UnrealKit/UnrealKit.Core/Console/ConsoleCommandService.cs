using System.Text.Json;
using UnrealKit.Core.Adb;
using UnrealKit.Core.CommandChannel;
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

    /// <summary>
    /// 读回 cvar 当前值。
    ///
    /// 设备层只把 HTTP 响应 body 原样带回（<see cref="Processes.ProcessExecutionResult.StandardOutput"/>），
    /// 这里做唯一一次解析：取顶层 <c>ReturnValue</c>——UE 的
    /// <c>GetConsoleVariable*Value</c> 的返回值就装在这个字段里。
    ///
    /// 注意 UE 侧的局限：cvar 不存在时这两个 getter 返回 0 / false，与合法的 0 / false 无法区分，
    /// 因此这里不做「cvar 不存在」的判定，只如实返回读到的值。
    /// </summary>
    public async Task<ConsoleVariableValue> QueryVariableAsync(
        string serialNumber,
        string variableName,
        ConsoleVariableType variableType,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);

        progress?.Report(new OperationProgress(
            "console-query", "Querying", null, null, $"Reading {variableName}"));

        Processes.ProcessExecutionResult result;
        try
        {
            result = await _deviceService.QueryConsoleVariableAsync(
                ResolveDevice(serialNumber), variableName, variableType, progress, cancellationToken);
        }
        catch (DeviceCommandException exception)
        {
            return ConsoleVariableValue.Failed($"读取 {variableName} 失败: {exception.Message}");
        }

        if (!result.Succeeded)
        {
            return ConsoleVariableValue.Failed(
                $"读取 {variableName} 失败 (退出码 {result.ExitCode}): "
                + (string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError));
        }

        return ParseReturnValue(variableName, variableType, result.StandardOutput);
    }

    /// <summary>
    /// 从 <c>PUT /remote/object/call</c> 的响应 body 里取出函数返回值。
    /// 期望形如 <c>{"ReturnValue": 80.0}</c> / <c>{"ReturnValue": true}</c>。
    /// 缺字段或类型不符按具体原因失败，不静默替成 0/false。
    /// </summary>
    private static ConsoleVariableValue ParseReturnValue(
        string variableName,
        ConsoleVariableType variableType,
        string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return ConsoleVariableValue.Failed($"读取 {variableName} 失败: Remote Control 返回空响应。");
        }

        JsonElement returnValue;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(
                    RemoteControl.RemoteControlVariableQueryRequest.ReturnValuePropertyName,
                    out var property))
            {
                return ConsoleVariableValue.Failed(
                    $"读取 {variableName} 失败: 响应缺少 "
                    + $"{RemoteControl.RemoteControlVariableQueryRequest.ReturnValuePropertyName} 字段: {Truncate(body)}");
            }

            // JsonDocument 释放后 JsonElement 会失效，先克隆再出 using 作用域。
            returnValue = property.Clone();
        }
        catch (JsonException exception)
        {
            return ConsoleVariableValue.Failed(
                $"读取 {variableName} 失败: 响应不是合法 JSON ({exception.Message}): {Truncate(body)}");
        }

        if (variableType == ConsoleVariableType.Bool)
        {
            // UE 对 bool cvar 也可能回 0/1 而不是 true/false，两种都接受。
            return returnValue.ValueKind switch
            {
                JsonValueKind.True => ConsoleVariableValue.Bool(true),
                JsonValueKind.False => ConsoleVariableValue.Bool(false),
                JsonValueKind.Number when returnValue.TryGetDouble(out var number) =>
                    ConsoleVariableValue.Bool(number != 0),
                _ => ConsoleVariableValue.Failed(
                    $"读取 {variableName} 失败: 期望 bool 返回值，实际为 {returnValue.ValueKind}。")
            };
        }

        return returnValue.ValueKind == JsonValueKind.Number && returnValue.TryGetDouble(out var value)
            ? ConsoleVariableValue.Number(value)
            : ConsoleVariableValue.Failed(
                $"读取 {variableName} 失败: 期望数值返回值，实际为 {returnValue.ValueKind}。");
    }

    private static string Truncate(string text) =>
        text.Length <= 200 ? text : text[..200] + "…";

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
