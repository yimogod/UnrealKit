using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;

namespace UnrealKit.Core.Devices;

/// <summary>
/// IDeviceService 的 Win64 本地主机实现。
/// 通过 System.Diagnostics.Process 采集 Windows 进程内存，无外部依赖。
/// </summary>
public sealed class Win64DeviceService : IDeviceService
{
    private readonly IProcessRunner _processRunner;

    public Win64DeviceService(IProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new ProcessRunner();
    }

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
                return Task.FromResult(new ProcessExecutionResult(1, string.Empty,
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
        catch (Exception ex) when (ex is not InvalidOperationException && ex is not OperationCanceledException)
        {
            return Task.FromResult(new ProcessExecutionResult(1, string.Empty, ex.Message,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
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
                throw new DirectoryNotFoundException($"Source directory not found: {source}");

            var dest = Path.GetFullPath(localDirectory);
            progress?.Report(new OperationProgress("pull", "Copying", null, null, $"Copying {source} to {dest}."));

            CopyDirectoryRecursive(source, dest);

            return Task.FromResult(new ProcessExecutionResult(0, $"Copied {source} to {dest}.", string.Empty,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ProcessExecutionResult(1, string.Empty, ex.Message,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>
    /// Win64 上发送 UE 控制台指令暂不支持。
    /// </summary>
    public Task<ProcessExecutionResult> SendConsoleCommandAsync(
        IDevice device,
        string command,
        string? target = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ProcessExecutionResult(1, string.Empty,
            "Console commands are not supported on Win64 devices in this version.",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Win64 上流式读取日志暂不支持。
    /// </summary>
    public async IAsyncEnumerable<string> StreamLogAsync(
        IDevice device,
        string? filter = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

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
            return new ProcessExecutionResult(1, string.Empty, $"Executable not found: {target}",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        progress?.Report(new OperationProgress("start-app", "Launching", null, null, $"Starting {target}."));

        return await _processRunner.RunAsync(
            new ProcessExecutionRequest(target, [], null, null, null, null),
            progress,
            cancellationToken);
    }

    /// <summary>
    
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
                return Task.FromResult(new ProcessExecutionResult(1, string.Empty,
                    $"No process named '{target}' was found.",
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            }

            var killed = 0;
            foreach (var p in processes)
            {
                try
                {
                    p.Kill();
                    p.Dispose();
                    killed++;
                }
                catch (Exception ex)
                {
                    p.Dispose();
                    return Task.FromResult(new ProcessExecutionResult(1, string.Empty,
                        $"Failed to kill process '{target}' (PID {p.Id}): {ex.Message}",
                        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
                }
            }

            return Task.FromResult(new ProcessExecutionResult(0,
                $"Stopped {killed} process(es) named '{target}'.",
                string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ProcessExecutionResult(1, string.Empty, ex.Message,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
    }

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
                throw new FileNotFoundException($"Source file not found: {source}");

            var dest = Path.GetFullPath(remotePath);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(source, dest, overwrite: true);

            return Task.FromResult(new ProcessExecutionResult(0, $"Copied {source} to {dest}.", string.Empty,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ProcessExecutionResult(1, string.Empty, ex.Message,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
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
        catch (Exception ex)
        {
            return Task.FromResult(new ProcessExecutionResult(1, string.Empty, ex.Message,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
    }

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
    public string Platform => "Win64";
    public bool IsAvailable => true;
}