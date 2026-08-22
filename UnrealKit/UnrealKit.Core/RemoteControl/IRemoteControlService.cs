using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;

namespace UnrealKit.Core.RemoteControl;

/// <summary>
/// UE Web Remote Control HTTP 客户端抽象。
/// </summary>
public interface IRemoteControlService
{
    /// <summary>
    /// 向运行中的 UE 进程发送一条控制台指令。
    /// HTTP 非成功状态或网络失败以 <see cref="RemoteControlException"/> 表达。
    /// </summary>
    Task<ProcessExecutionResult> SendConsoleCommandAsync(
        RemoteControlCommandRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
