using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;

namespace UnrealKit.Core.CommandChannel;

/// <summary>
/// 向运行中的 UE 客户端发送控制台指令的传输通道。
///
/// 平台差异集中在实现里：Win64 走引擎自带的 Web Remote Control HTTP 服务
/// （<see cref="HttpCommandTransport"/>），Android 走 UE 侧自研 TCP 命令插件
/// （<see cref="TcpCommandTransport"/>）——引擎的 <c>WebRemoteControl</c> 模块带
/// <c>PlatformAllowList</c>，Android 构建里根本不编译 HTTP 服务器，见
/// <c>Doc/方案B-UE客户端控制台命令通道.md</c>。
///
/// 抽象只接受命令文本：端口、对象路径、协议格式都是通道自身的细节，
/// 让调用方按通道类型分支准备参数，等于把平台判断又搬回上层。
/// </summary>
public interface ICommandTransport
{
    /// <summary>该通道使用的传输方式，用于日志与失败提示。</summary>
    CommandTransportKind Kind { get; }

    /// <summary>
    /// 通道在设备上监听的 TCP 端口。Android 侧的 <c>adb forward</c> 需要它——
    /// 由通道自己给出，调用方不必按 <see cref="Kind"/> 再选一次端口，
    /// 否则转发的端口与实际连接的端口会各自取值而对不上。
    /// </summary>
    int Port { get; }

    /// <summary>
    /// 发送一条控制台指令。
    /// 连接失败、命令执行失败、协议异常一律以 <see cref="CommandTransportException"/> 表达，
    /// 并带上 <c>UKC*</c> 诊断码；调用方主动取消仍以取消语义上抛。
    /// </summary>
    Task<ProcessExecutionResult> SendConsoleCommandAsync(
        string command,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
