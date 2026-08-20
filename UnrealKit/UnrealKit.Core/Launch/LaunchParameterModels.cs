using UnrealKit.Core.Processes;

namespace UnrealKit.Core.Launch;

public sealed record LaunchParameterRequest(string SerialNumber, IReadOnlyList<string> PresetNames, string? CustomArguments = null);

public sealed record LaunchParameterPushResult(string Content, string RemotePath, ProcessExecutionResult AdbResult);

public sealed record LaunchParameterReadResult(string RemotePath, ProcessExecutionResult ReadResult);
