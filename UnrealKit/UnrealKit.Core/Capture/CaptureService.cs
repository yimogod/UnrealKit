using System.Linq;
using UnrealKit.Core.Devices;
using UnrealKit.Core.Console;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Capture;

public sealed class CaptureService
{
    private readonly IDeviceService? _deviceService;
    private readonly IConsoleCommandService? _consoleService;
    private readonly TimeProvider _timeProvider;

    public CaptureService(
        IDeviceService? deviceService = null,
        IConsoleCommandService? consoleService = null,
        TimeProvider? timeProvider = null)
    {
        _deviceService = deviceService;
        _consoleService = consoleService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public CapturePlan CreatePlan(CaptureRequest request, DateTimeOffset? capturedAt = null)
    {
        var target = ValidateRequest(request);
        var capturedLocalTime = capturedAt ?? _timeProvider.GetLocalNow();
        var captureId = string.IsNullOrWhiteSpace(request.CaptureId)
            ? CreateCaptureId(capturedLocalTime, request.Device.Id)
            : ValidateCaptureId(request.CaptureId);
        var contentRoot = request.Project.ContentDir;
        var captureDirectory = Path.Combine(contentRoot, target.PlatformName, NormalizeTag(request.Tag), capturedLocalTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), captureId);
        return new CapturePlan(captureId, captureDirectory, target.SavedRootPath);
    }

    public async Task<CaptureResult> CaptureAsync(CaptureRequest request, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_deviceService is null) throw new InvalidOperationException("CaptureService requires an IDeviceService for live capture. Use ImportAsync for importing local data.");
        var startedAt = _timeProvider.GetLocalNow();
        var target = ValidateRequest(request);
        var plan = CreatePlan(request, startedAt);
        if (Directory.Exists(plan.CaptureDirectory))
        {
            throw new InvalidOperationException($"Capture archive already exists and will not be overwritten: {plan.CaptureDirectory}");
        }

        var stagingDirectory = Path.Combine(request.Project.IntermediateDir, "CaptureStaging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            return await CaptureToStagingAsync(request, plan, target, startedAt, stagingDirectory, progress, cancellationToken);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private async Task<CaptureResult> CaptureToStagingAsync(CaptureRequest request, CapturePlan plan, PlatformTarget target, DateTimeOffset startedAt, string stagingDirectory, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        // Pre-capture console sequence. A configured sequence that cannot run must abort the
        // capture rather than be skipped: silently collecting from an unprepared game state
        // produces data that looks valid but answers a different question.
        var preSequenceName = request.Project.Settings.PreCaptureSequence;
        if (!string.IsNullOrWhiteSpace(preSequenceName))
        {
            var preset = ResolveRequiredSequence(request.Project.Settings, preSequenceName, "Pre-capture");
            progress?.Report(new OperationProgress("capture", "PreSequence", 0, 4, $"Running pre-capture sequence: {preSequenceName}"));
            var seqDef = preset.ToSequenceDefinition();
            var preResult = await _consoleService!.RunSequenceAsync(
                new SequenceExecutionRequest(seqDef, request.Device.Id, target.ProcessIdentity),
                progress, cancellationToken);
            if (!preResult.Succeeded)
            {
                var failedSteps = preResult.StepResults.Where(r => !r.Succeeded);
                var errorDetails = string.Join("; ", failedSteps.Select(s => s.Error ?? $"step {s.StepIndex}"));
                throw new InvalidOperationException(
                    $"Pre-capture sequence '{preSequenceName}' failed: {errorDetails}. Capture aborted to prevent collecting data from an incorrect game state.");
            }
        }

        var totalSteps = request.SkipSaved ? 2 : 3;
        var currentStep = 0;

        var memInfoDirectory = Path.Combine(stagingDirectory, "MemInfo");
        Directory.CreateDirectory(memInfoDirectory);
        progress?.Report(new OperationProgress("capture", "MemInfo", ++currentStep, totalSteps, $"Collecting memory info for {target.ProcessIdentity}."));
        var memInfo = await _deviceService!.CaptureMemoryAsync(request.Device, target.ProcessIdentity, progress, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(memInfoDirectory, $"meminfo_{startedAt:yyyyMMdd-HHmmss}.txt"), memInfo.StandardOutput, cancellationToken);

        if (!request.SkipSaved)
        {
            progress?.Report(new OperationProgress("capture", "Saved", ++currentStep, totalSteps, $"Pulling UE Saved data from {plan.DeviceSavedDirectory}."));
            await _deviceService!.PullDirectoryAsync(request.Device, plan.DeviceSavedDirectory, Path.Combine(stagingDirectory, "Saved"), progress, cancellationToken);
        }

        var manifest = await CreateManifestAsync(request, plan, target, startedAt, stagingDirectory, cancellationToken);
        progress?.Report(new OperationProgress("capture", "Manifest", ++currentStep, totalSteps, "Writing capture manifest and archiving original data."));
        await WriteManifestAsync(stagingDirectory, manifest, cancellationToken);

        Directory.CreateDirectory(Path.GetDirectoryName(plan.CaptureDirectory)!);
        Directory.Move(stagingDirectory, plan.CaptureDirectory);

        // Post-capture console sequence. The archive is already committed at this point, so a
        // failing step is reported as a warning rather than aborting. Configuration problems
        // (missing service or unknown preset name) were already rejected before capture began.
        var postSequenceName = request.Project.Settings.PostCaptureSequence;
        if (!string.IsNullOrWhiteSpace(postSequenceName))
        {
            var preset = ResolveRequiredSequence(request.Project.Settings, postSequenceName, "Post-capture");
            progress?.Report(new OperationProgress("capture", "PostSequence", null, null, $"Running post-capture sequence: {postSequenceName}"));
            var seqDef = preset.ToSequenceDefinition();
            var postResult = await _consoleService!.RunSequenceAsync(
                new SequenceExecutionRequest(seqDef, request.Device.Id, target.ProcessIdentity),
                progress, cancellationToken);
            if (!postResult.Succeeded)
            {
                progress?.Report(new OperationProgress("capture", "PostSequence", null, null,
                    $"Warning: Post-capture sequence '{postSequenceName}' had {postResult.FailedSteps} failed step(s)."));
            }
        }

        return new CaptureResult(plan, Path.Combine(plan.CaptureDirectory, "CaptureManifest.json"), manifest);
    }

    // ---------------------------------------------------------------------
    //  Import (no device required)
    // ---------------------------------------------------------------------

    public async Task<CaptureResult> ImportAsync(CaptureImportRequest request, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var captureId = string.IsNullOrWhiteSpace(request.CaptureId)
            ? $"import-{_timeProvider.GetLocalNow():yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"
            : ValidateCaptureId(request.CaptureId);
        var captureDirectory = Path.Combine(request.Project.ContentDir, request.Platform, NormalizeTag(request.Tag), _timeProvider.GetLocalNow().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), captureId);
        if (Directory.Exists(captureDirectory))
        {
            throw new InvalidOperationException($"Capture archive already exists and will not be overwritten: {captureDirectory}");
        }

        var stagingDirectory = Path.Combine(request.Project.IntermediateDir, "ImportStaging", Guid.NewGuid().ToString("N"));
        try
        {
            progress?.Report(new OperationProgress("import", "Copy", 1, 3, "Copying source data to staging."));
            CopyDirectoryContents(request.SourceDirectory, stagingDirectory);

            var startedAt = _timeProvider.GetLocalNow();
            progress?.Report(new OperationProgress("import", "Manifest", 2, 3, "Creating manifest."));
            // 导入没有涉及任何设备，因此没有平台落地值可记录。写入 null 而不是硬凑一份，
            // 否则读者无法区分「导入的归档」与「采集时用过这些路径」。
            var manifest = await CreateManifestFromDirectoryAsync(stagingDirectory, captureId, request.Platform, request.Tag, startedAt, request.Project, "import", null, "imported", target: null, string.Empty, cancellationToken);
            await WriteManifestAsync(stagingDirectory, manifest, cancellationToken);

            progress?.Report(new OperationProgress("import", "Finalize", 3, 3, "Archiving to Content directory."));
            Directory.CreateDirectory(Path.GetDirectoryName(captureDirectory)!);
            Directory.Move(stagingDirectory, captureDirectory);
            return new CaptureResult(new CapturePlan(captureId, captureDirectory, string.Empty), Path.Combine(captureDirectory, "CaptureManifest.json"), manifest);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    // ---------------------------------------------------------------------
    //  Helpers
    // ---------------------------------------------------------------------

    private async Task<CaptureManifest> CreateManifestAsync(CaptureRequest request, CapturePlan plan, PlatformTarget target, DateTimeOffset startedAt, string stagingDirectory, CancellationToken cancellationToken)
    {
        return await CreateManifestFromDirectoryAsync(stagingDirectory, plan.CaptureId, target.PlatformName, request.Tag, startedAt, request.Project, request.Device.Id, request.Device.Name, request.Device.IsAvailable ? "available" : "unavailable", target, plan.DeviceSavedDirectory, cancellationToken);
    }

    private static async Task WriteManifestAsync(string stagingDirectory, CaptureManifest manifest, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(Path.Combine(stagingDirectory, "CaptureManifest.json"));
        await System.Text.Json.JsonSerializer.SerializeAsync(stream, manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }, cancellationToken);
    }


    private static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(sourceDirectory))
        {
            var relative = Path.GetRelativePath(sourceDirectory, entry);
            var destination = Path.Combine(destinationDirectory, relative);
            if (Directory.Exists(entry))
            {
                CopyDirectoryContents(entry, destination);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(entry, destination, overwrite: false);
            }
        }
    }

    private async Task<CaptureManifest> CreateManifestFromDirectoryAsync(string directory, string captureId, string platform, string tag, DateTimeOffset startedAt, Projects.UkitProject project, string deviceSerial, string? deviceModel, string deviceStatus, PlatformTarget? target, string deviceSavedDirectory, CancellationToken cancellationToken)
    {
        var files = new List<CaptureFileManifestEntry>();
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
        {
            await using var stream = File.OpenRead(path);
            var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken);
            files.Add(new CaptureFileManifestEntry(Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/'), new FileInfo(path).Length, Convert.ToHexStringLower(hash)));
        }

        return new CaptureManifest(captureId, platform, tag, startedAt, _timeProvider.GetLocalNow(), project.CreateConfigurationSnapshot(), deviceSerial, deviceModel, deviceStatus, target, deviceSavedDirectory, files);
    }

    /// <summary>
    /// 在采集开始前校验已配置的前后指令序列，让配置错误在任何设备操作之前暴露，
    /// 而不是等到归档已写入才失败。
    /// </summary>
    private void ValidateConfiguredSequences(ProjectSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.PreCaptureSequence))
        {
            ResolveRequiredSequence(settings, settings.PreCaptureSequence, "Pre-capture");
        }

        if (!string.IsNullOrWhiteSpace(settings.PostCaptureSequence))
        {
            ResolveRequiredSequence(settings, settings.PostCaptureSequence, "Post-capture");
        }
    }

    /// <summary>
    /// 查找已配置的采集前后指令序列。找不到预设或缺少 IConsoleCommandService 时抛出，
    /// 不静默跳过——配置了序列却不执行会让采集数据对应错误的游戏状态。
    /// </summary>
    private ConsoleSequencePreset ResolveRequiredSequence(ProjectSettings settings, string sequenceName, string role)
    {
        if (_consoleService is null)
        {
            throw new InvalidOperationException(
                $"{role} sequence '{sequenceName}' is configured, but this CaptureService was constructed without an IConsoleCommandService. " +
                "Pass a console command service, or clear the sequence in project settings.");
        }

        var preset = settings.ConsoleSequences
            .FirstOrDefault(s => string.Equals(s.Name, sequenceName, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
        {
            var available = settings.ConsoleSequences.Count == 0
                ? "(none defined)"
                : string.Join(", ", settings.ConsoleSequences.Select(s => s.Name));
            throw new InvalidOperationException(
                $"{role} sequence '{sequenceName}' is configured but no matching preset exists. Available sequences: {available}.");
        }

        return preset;
    }

    /// <summary>
    /// 校验采集请求并解析出本次采集的平台落地值。
    ///
    /// 目标平台由所选设备决定，而不是由工程配置指定：同一工程可以同时配置多个平台，
    /// 「本次打哪个」是会话选择。设备所在平台未配置时报错并列出已配置平台，
    /// 不回退到其他平台的配置——用错误的路径采集会拉到空目录却报告成功。
    /// </summary>
    private PlatformTarget ValidateRequest(CaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Project);
        ArgumentNullException.ThrowIfNull(request.Device);
        if (!request.Device.IsAvailable) throw new InvalidOperationException($"Capture requires a device with status 'available'. Device '{request.Device.Id}' is not available.");

        var settings = request.Project.Settings;
        ValidateConfiguredSequences(settings);
        NormalizeTag(request.Tag);

        var devicePlatform = PlatformNames.Parse(request.Device.Platform, nameof(request));
        return settings.ResolveTarget(devicePlatform, $"设备 '{request.Device.Id}' 属于 {PlatformNames.ToName(devicePlatform)} 平台。");
    }

    private static string NormalizeTag(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        var value = tag.Trim();
        if (value is "." or ".." || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains('/') || value.Contains('\\'))
        {
            throw new ArgumentException("Capture tag must be a single valid directory name.", nameof(tag));
        }

        return value;
    }

    private static string ValidateCaptureId(string captureId)
    {
        var value = captureId.Trim();
        if (value.Length == 0 || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains('/') || value.Contains('\\'))
        {
            throw new ArgumentException("Capture ID must be a valid directory name.", nameof(captureId));
        }

        return value;
    }

    private static string CreateCaptureId(DateTimeOffset localTime, string deviceId)
    {
        var devicePart = new string(deviceId.Where(char.IsLetterOrDigit).Take(12).ToArray());
        if (devicePart.Length == 0) devicePart = "device";
        return $"{localTime:yyyyMMdd-HHmmss}-{devicePart}-{Guid.NewGuid():N}";
    }
}
