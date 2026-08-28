using UnrealKit.Core.CommandChannel;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Devices;

/// <summary>
/// IDeviceService 的 Win64 本地主机实现。
/// 通过 System.Diagnostics.Process 采集 Windows 进程内存，无外部依赖。
/// </summary>
public sealed class Win64DeviceService : IDeviceService
{
    private readonly IProcessRunner _processRunner;

    /// <summary>
    /// 控制台指令通道。Win64 与 Android 统一走引擎自带 Web Remote Control 的 HTTP 服务。
    /// </summary>
    private readonly ICommandTransport _commandTransport;

    /// <param name="processRunner">外部进程调用。</param>
    /// <param name="channelOptions">指令通道配置。null 取内置默认（Web Remote Control HTTP）。</param>
    /// <param name="commandTransport">显式指定的通道实例，仅用于测试注入；否则按配置构造。</param>
    public Win64DeviceService(
        IProcessRunner? processRunner = null,
        CommandChannelOptions? channelOptions = null,
        ICommandTransport? commandTransport = null)
    {
        _processRunner = processRunner ?? new ProcessRunner();
        _commandTransport = commandTransport
            ?? (channelOptions ?? CommandChannelOptions.Default).CreateTransport();
    }

    public TargetPlatform Platform => TargetPlatform.Win64;

    /// <summary>
    /// Win64 通过本机进程操作实现大部分能力；日志流依赖 UE 端通道，尚未实现。
    /// 安装包是 Android 专属能力，Win64 是解包后直接运行，不支持「安装」。
    /// </summary>
    public bool Supports(DeviceCapability capability) => capability switch
    {
        DeviceCapability.SendConsoleCommand => true,
        DeviceCapability.StreamLog => false,
        DeviceCapability.InstallApplication => false,
        _ => true
    };

    /// <summary>
    /// 列出当前可用设备。Win64 永远返回本地主机。
    /// </summary>
    public Task<IReadOnlyList<IDevice>> ListDevicesAsync(
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<IDevice>>([new Win64Device()]);
    }

    /// <summary>
    /// 采集目标进程的内存信息，返回结构化文本输出供 Win64MemInfoParser 解析。
    /// </summary>
    public Task<ProcessExecutionResult> CaptureMemoryAsync(
        IDevice device,
        string target,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(new OperationProgress("capture-memory", "Collecting", null, null, $"Collecting memory info for process '{target}'."));

        try
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(target);
            if (processes.Length == 0)
            {
                throw new DeviceCommandException($"No process named '{target}' was found. Ensure the application is running.",
                    new ProcessExecutionResult(1, string.Empty,
                        $"No process named '{target}' was found. Ensure the application is running.",
                        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            }

            // Use the one with the most memory as the primary if multiple match
            System.Diagnostics.Process process;
            if (processes.Length > 1)
            {
                var ordered = processes.OrderByDescending(p =>
                {
                    try { return p.WorkingSet64; }
                    catch { return 0L; }
                }).ToArray();
                process = ordered[0];
                // Dispose the others
                foreach (var p in ordered.Skip(1))
                    p.Dispose();
            }
            else
            {
                process = processes[0];
            }

            process.Refresh();

            string processName;
            int processId;
            long workingSet64;
            long privateMemorySize64;
            long virtualMemorySize64;
            long pagedMemorySize64;
            long nonpagedSystemMemorySize64;
            long peakWorkingSet64;
            long peakVirtualMemorySize64;
            int threadCount;
            int handleCount;
            TimeSpan totalProcessorTime;

            try
            {
                processName = process.ProcessName;
                processId = process.Id;
                workingSet64 = process.WorkingSet64;
                privateMemorySize64 = process.PrivateMemorySize64;
                virtualMemorySize64 = process.VirtualMemorySize64;
                pagedMemorySize64 = process.PagedMemorySize64;
                nonpagedSystemMemorySize64 = process.NonpagedSystemMemorySize64;
                peakWorkingSet64 = process.PeakWorkingSet64;
                peakVirtualMemorySize64 = process.PeakVirtualMemorySize64;
                threadCount = process.Threads.Count;
                handleCount = process.HandleCount;
                totalProcessorTime = process.TotalProcessorTime;
            }
            finally
            {
                process.Dispose();
            }

            var output = BuildMemInfoOutput(processName, processId,
                workingSet64, privateMemorySize64, virtualMemorySize64,
                pagedMemorySize64, nonpagedSystemMemorySize64,
                peakWorkingSet64, peakVirtualMemorySize64,
                threadCount, handleCount, totalProcessorTime);

            return Task.FromResult(new ProcessExecutionResult(0, output, string.Empty,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
        catch (DeviceCommandException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DeviceCommandException($"Failed to capture memory for process '{target}': {ex.Message}",
                new ProcessExecutionResult(1, string.Empty, ex.Message,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), ex);
        }
    }

    /// <summary>
    /// Win64 上 "拉取" 目录即复制本地目录。
    /// </summary>
    public Task<ProcessExecutionResult> PullDirectoryAsync(
        IDevice device,
        string remotePath,
        string localDirectory,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(localDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var source = Path.GetFullPath(remotePath);
            if (!Directory.Exists(source))
                throw new DeviceCommandException($"Source directory not found: {source}",
                    new ProcessExecutionResult(1, string.Empty, $"Source directory not found: {source}",
                        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

            var dest = Path.GetFullPath(localDirectory);
            progress?.Report(new OperationProgress("pull", "Copying", null, null, $"Copying {source} to {dest}."));

            CopyDirectoryRecursive(source, dest);

            return Task.FromResult(new ProcessExecutionResult(0, $"Copied {source} to {dest}.", string.Empty,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
        catch (DeviceCommandException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DeviceCommandException($"Failed to pull directory '{remotePath}': {ex.Message}",
                new ProcessExecutionResult(1, string.Empty, ex.Message,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), ex);
        }
    }

    /// <summary>
    /// 拉取多个可选子目录。与单目录拉取不同，源子目录不存在不是错误——「还没生成 GPUDumps」是常态，
    /// 因此按 <see cref="Directory.Exists"/> 判断后跳过，而不是让整次取回失败。
    /// </summary>
    public Task<ProcessExecutionResult> PullSubdirectoriesAsync(
        IDevice device,
        string remoteDirectory,
        IReadOnlyList<string> subdirectoryNames,
        string localDirectory,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteDirectory);
        ArgumentNullException.ThrowIfNull(subdirectoryNames);
        ArgumentException.ThrowIfNullOrWhiteSpace(localDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var sourceRoot = Path.GetFullPath(remoteDirectory);
            var destRoot = Path.GetFullPath(localDirectory);
            var copied = 0;

            foreach (var name in subdirectoryNames)
            {
                var source = Path.Combine(sourceRoot, name);
                if (!Directory.Exists(source))
                {
                    progress?.Report(new OperationProgress(
                        "pull", "Skip", null, null, $"本机不存在子目录 {name}，跳过。"));
                    continue;
                }

                var dest = Path.Combine(destRoot, name);
                CopyDirectoryRecursive(source, dest);
                copied++;
            }

            return Task.FromResult(new ProcessExecutionResult(
                0, $"Copied {copied} of {subdirectoryNames.Count} subdirectories from {sourceRoot}.", string.Empty,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
        catch (DeviceCommandException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DeviceCommandException($"Failed to pull subdirectories under '{remoteDirectory}': {ex.Message}",
                new ProcessExecutionResult(1, string.Empty, ex.Message,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), ex);
        }
    }

    /// <summary>
    /// Win64 上发送 UE 控制台指令走本机的 Web Remote Control HTTP 通道。
    /// 「设备」就是本机，因此不需要端口转发。
    /// </summary>
    public async Task<ProcessExecutionResult> SendConsoleCommandAsync(
        IDevice device,
        string command,
        string? target = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        try
        {
            return await _commandTransport.SendConsoleCommandAsync(command, progress, cancellationToken);
        }
        catch (CommandTransportException exception)
        {
            throw new DeviceCommandException(exception.Message, exception.Result, exception);
        }
    }

    /// <summary>
    /// 读回 cvar。「设备」就是本机，与发送指令一样不需要端口转发。
    /// </summary>
    public async Task<ProcessExecutionResult> QueryConsoleVariableAsync(
        IDevice device,
        string variableName,
        ConsoleVariableType variableType,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);

        try
        {
            return await _commandTransport.QueryConsoleVariableAsync(
                variableName, variableType, progress, cancellationToken);
        }
        catch (CommandTransportException exception)
        {
            throw new DeviceCommandException(exception.Message, exception.Result, exception);
        }
    }

    /// <summary>
    /// Win64 上流式读取日志暂不支持。抛出而不是返回空流：
    /// 空流会被调用方误读为「已连接但暂无日志」。
    /// </summary>
    public IAsyncEnumerable<string> StreamLogAsync(
        IDevice device,
        string? filter = null,
        CancellationToken cancellationToken = default) =>
        throw new DeviceCapabilityNotSupportedException(
            DeviceCapability.StreamLog,
            PlatformNames.Win64,
            "请先用 Supports(DeviceCapability.StreamLog) 探测能力再调用。");

    /// <summary>
    /// 在本机启动 Win64 可执行文件。
    /// </summary>
    public async Task<ProcessExecutionResult> StartApplicationAsync(
        IDevice device,
        string target,
        string? activity = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        if (!File.Exists(target))
            throw new DeviceCommandException($"Executable not found: {target}",
                new ProcessExecutionResult(1, string.Empty, $"Executable not found: {target}",
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        progress?.Report(new OperationProgress("start-app", "Launching", null, null, $"Starting {target}."));

        // 工作目录固定为可执行文件所在目录：UE 会按 cwd 定位相对资源路径，
        // 继承调用方进程的 cwd 会让 GUI 与 CLI 启动出不同行为。
        var workingDirectory = Path.GetDirectoryName(Path.GetFullPath(target));

        return await _processRunner.RunAsync(
            new ProcessExecutionRequest(target, [], workingDirectory, null, null, null),
            progress,
            cancellationToken);
    }

    /// <summary>
    /// Win64 上停止应用即终止目标进程。
    /// </summary>
    public Task<ProcessExecutionResult> StopApplicationAsync(
        IDevice device,
        string target,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        try
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(target);
            if (processes.Length == 0)
            {
                throw new DeviceCommandException($"No process named '{target}' was found.",
                    new ProcessExecutionResult(1, string.Empty,
                        $"No process named '{target}' was found.",
                        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            }

            var killed = 0;
            try
            {
                foreach (var p in processes)
                {
                    // Read the PID before Kill/Dispose: Process.Id throws once the object is disposed.
                    var processId = p.Id;
                    try
                    {
                        p.Kill();
                        killed++;
                    }
                    catch (Exception ex)
                    {
                        var message = $"Failed to kill process '{target}' (PID {processId}): {ex.Message}";
                        throw new DeviceCommandException(message,
                            new ProcessExecutionResult(1, string.Empty, message,
                                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), ex);
                    }
                }
            }
            finally
            {
                foreach (var p in processes) p.Dispose();
            }

            return Task.FromResult(new ProcessExecutionResult(0,
                $"Stopped {killed} process(es) named '{target}'.",
                string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
        catch (DeviceCommandException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DeviceCommandException($"Failed to stop application '{target}': {ex.Message}",
                new ProcessExecutionResult(1, string.Empty, ex.Message,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), ex);
        }
    }

    /// <summary>
    /// Win64 上 "推送" 文件即复制本地文件。
    /// </summary>
    public Task<ProcessExecutionResult> PushFileAsync(
        IDevice device,
        string localPath,
        string remotePath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var source = Path.GetFullPath(localPath);
            if (!File.Exists(source))
                throw new DeviceCommandException($"Source file not found: {source}",
                    new ProcessExecutionResult(1, string.Empty, $"Source file not found: {source}",
                        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

            var dest = Path.GetFullPath(remotePath);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(source, dest, overwrite: true);

            return Task.FromResult(new ProcessExecutionResult(0, $"Copied {source} to {dest}.", string.Empty,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
        catch (DeviceCommandException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DeviceCommandException($"Failed to push file '{localPath}': {ex.Message}",
                new ProcessExecutionResult(1, string.Empty, ex.Message,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), ex);
        }
    }

    /// <summary>
    /// Win64 上删除文件即删除本地文件。
    /// </summary>
    public Task<ProcessExecutionResult> DeleteRemoteFileAsync(
        IDevice device,
        string remotePath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var path = Path.GetFullPath(remotePath);
            if (File.Exists(path))
            {
                File.Delete(path);
                return Task.FromResult(new ProcessExecutionResult(0, $"Deleted {path}.", string.Empty,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            }

            return Task.FromResult(new ProcessExecutionResult(0, $"File not found (no action taken): {path}.", string.Empty,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
        catch (DeviceCommandException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DeviceCommandException($"Failed to delete remote file '{remotePath}': {ex.Message}",
                new ProcessExecutionResult(1, string.Empty, ex.Message,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), ex);
        }
    }

    /// <summary>
    /// Win64 上读取文件即读取本地文件。文件不存在时返回非零退出码而非抛异常，
    /// 与「尚未投放启动参数」的查询语义保持一致。
    /// </summary>
    public Task<ProcessExecutionResult> ReadFileAsync(
        IDevice device,
        string remotePath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        cancellationToken.ThrowIfCancellationRequested();

        var path = Path.GetFullPath(remotePath);
        if (!File.Exists(path))
        {
            return Task.FromResult(new ProcessExecutionResult(1, string.Empty, $"File not found: {path}",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }

        return Task.FromResult(new ProcessExecutionResult(0, File.ReadAllText(path), string.Empty,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Win64 是解包后直接运行，没有「安装」这一步。调用方应先探测
    /// <see cref="Supports"/>(<see cref="DeviceCapability.InstallApplication"/>)。
    /// </summary>
    public Task<ProcessExecutionResult> InstallApplicationAsync(
        IDevice device,
        string localApplicationPath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        throw new DeviceCapabilityNotSupportedException(
            DeviceCapability.InstallApplication,
            PlatformNames.Win64,
            "Win64 构建解包后直接运行可执行文件，无需安装。");

    private static string BuildMemInfoOutput(string processName, int processId,
        long workingSet, long privateMem, long virtualMem,
        long pagedMem, long nonPagedMem,
        long peakWorkingSet, long peakVirtualMem,
        int threadCount, int handleCount, TimeSpan totalProcessorTime)
    {
        return $"** WIN64 MEMINFO for process {processName} (PID: {processId}) **\n" +
               $"WorkingSetMB:           {(workingSet / (1024.0 * 1024.0)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}\n" +
               $"PrivateMemoryMB:        {(privateMem / (1024.0 * 1024.0)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}\n" +
               $"VirtualMemoryMB:        {(virtualMem / (1024.0 * 1024.0)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}\n" +
               $"PagedMemoryMB:          {(pagedMem / (1024.0 * 1024.0)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}\n" +
               $"NonPagedMemoryMB:       {(nonPagedMem / (1024.0 * 1024.0)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}\n" +
               $"PeakWorkingSetMB:       {(peakWorkingSet / (1024.0 * 1024.0)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}\n" +
               $"PeakVirtualMemoryMB:    {(peakVirtualMem / (1024.0 * 1024.0)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}\n" +
               $"Threads:                {threadCount}\n" +
               $"Handles:                {handleCount}\n" +
               $"TotalProcessorTime:     {totalProcessorTime}\n";
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectoryRecursive(dir, destSubDir);
        }
    }
}

/// <summary>
/// Win64 本地主机设备实现 IDevice，表示当前运行代理的 Windows 机器。
/// </summary>
public sealed class Win64Device : IDevice
{
    public string Id => "localhost";
    public string Name => Environment.MachineName;
    public string Platform => PlatformNames.Win64;
    public bool IsAvailable => true;
}