using UnrealKit.Core.Devices;
using UnrealKit.Core.Console;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Capture;

public sealed class CaptureService : ICaptureService
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
        ValidateRequest(request);
        var capturedLocalTime = capturedAt ?? _timeProvider.GetLocalNow();
        var captureId = string.IsNullOrWhiteSpace(request.CaptureId)
            ? CreateCaptureId(capturedLocalTime, request.Device.Id)
            : ValidateCaptureId(request.CaptureId);
        var platform = MapPlatform(request.Project.Settings.Platform);
        var contentRoot = request.Project.ContentDir;
        var captureDirectory = Path.Combine(contentRoot, platform, NormalizeTag(request.Tag), capturedLocalTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), captureId);
        return new CapturePlan(captureId, captureDirectory, ResolveDeviceSavedDirectory(request.Project.Settings));
    }

    public async Task<CaptureResult> CaptureAsync(CaptureRequest request, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_deviceService is null) throw new InvalidOperationException("CaptureService requires an IDeviceService for live capture. Use ImportAsync for importing local data.");
        var startedAt = _timeProvider.GetLocalNow();
        var plan = CreatePlan(request, startedAt);
        if (Directory.Exists(plan.CaptureDirectory))
        {
            throw new InvalidOperationException($"Capture archive already exists and will not be overwritten: {plan.CaptureDirectory}");
        }

        var stagingDirectory = Path.Combine(request.Project.IntermediateDir, "CaptureStaging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            return await CaptureToStagingAsync(request, plan, startedAt, stagingDirectory, progress, cancellationToken);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private async Task<CaptureResult> CaptureToStagingAsync(CaptureRequest request, CapturePlan plan, DateTimeOffset startedAt, string stagingDirectory, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        // Pre-capture console sequence
        var preSequenceName = request.Project.Settings.PreCaptureSequence;
        if (!string.IsNullOrWhiteSpace(preSequenceName) && _consoleService is not null)
        {
            var preset = request.Project.Settings.ConsoleSequences
                .FirstOrDefault(s => string.Equals(s.Name, preSequenceName, StringComparison.OrdinalIgnoreCase));
            if (preset is not null)
            {
                progress?.Report(new OperationProgress("capture", "PreSequence", 0, 4, $"Running pre-capture sequence: {preSequenceName}"));
                var seqDef = preset.ToSequenceDefinition();
                await _consoleService.RunSequenceAsync(
                    new SequenceExecutionRequest(seqDef, request.Device.Id, request.Project.Settings.PackageName),
                    progress, cancellationToken);
            }
        }

        var totalSteps = 3;
        var currentStep = 0;

        var memInfoDirectory = Path.Combine(stagingDirectory, "MemInfo");
        Directory.CreateDirectory(memInfoDirectory);
        currentStep++;
        progress?.Report(new OperationProgress("capture", "MemInfo", currentStep, totalSteps, $"Collecting memory info for {request.Project.Settings.PackageName}."));
        var memInfo = await _deviceService!.CaptureMemoryAsync(request.Device, request.Project.Settings.PackageName, progress, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(memInfoDirectory, $"meminfo_{startedAt:yyyyMMdd-HHmmss}.txt"), memInfo.StandardOutput, cancellationToken);

        if (!request.SkipSaved)
        {
            progress?.Report(new OperationProgress("capture", "Saved", currentStep + 1, totalSteps, $"Pulling UE Saved data from {plan.DeviceSavedDirectory}."));
            await _deviceService!.PullDirectoryAsync(request.Device, plan.DeviceSavedDirectory, Path.Combine(stagingDirectory, "Saved"), progress, cancellationToken);
        }

        var manifest = await CreateManifestAsync(request, plan, startedAt, stagingDirectory, cancellationToken);
        progress?.Report(new OperationProgress("capture", "Manifest", currentStep + 2, totalSteps, "Writing capture manifest and archiving original data."));
        await WriteManifestAsync(stagingDirectory, manifest, cancellationToken);

        Directory.CreateDirectory(Path.GetDirectoryName(plan.CaptureDirectory)!);
        Directory.Move(stagingDirectory, plan.CaptureDirectory);

        // Post-capture console sequence
        var postSequenceName = request.Project.Settings.PostCaptureSequence;
        if (!string.IsNullOrWhiteSpace(postSequenceName) && _consoleService is not null)
        {
            var preset = request.Project.Settings.ConsoleSequences
                .FirstOrDefault(s => string.Equals(s.Name, postSequenceName, StringComparison.OrdinalIgnoreCase));
            if (preset is not null)
            {
                progress?.Report(new OperationProgress("capture", "PostSequence", null, null, $"Running post-capture sequence: {postSequenceName}"));
                var seqDef = preset.ToSequenceDefinition();
                await _consoleService.RunSequenceAsync(
                    new SequenceExecutionRequest(seqDef, request.Device.Id, request.Project.Settings.PackageName),
                    progress, cancellationToken);
            }
        }

        return new CaptureResult(plan, Path.Combine(plan.CaptureDirectory, "CaptureManifest.json"), manifest);
    }

    // 闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾
    //  Import (no device required)
    // 闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾

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
            var manifest = await CreateManifestFromDirectoryAsync(stagingDirectory, captureId, request.Platform, request.Tag, startedAt, request.Project, "import", null, "imported", request.Project.Settings.PackageName, string.Empty, cancellationToken);
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

    // 闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾
    //  Helpers
    // 闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾闁冲厜鍋撻柍鍏夊亾

    private static string MapPlatform(TargetPlatform platform) => platform switch
    {
        TargetPlatform.Android => "Android",
        TargetPlatform.Win64 => "Win64",
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported platform.")
    };

    private async Task<CaptureManifest> CreateManifestAsync(CaptureRequest request, CapturePlan plan, DateTimeOffset startedAt, string stagingDirectory, CancellationToken cancellationToken)
    {
        return await CreateManifestFromDirectoryAsync(stagingDirectory, plan.CaptureId, MapPlatform(request.Project.Settings.Platform), request.Tag, startedAt, request.Project, request.Device.Id, request.Device.Name, request.Device.IsAvailable ? "available" : "unavailable", request.Project.Settings.PackageName, plan.DeviceSavedDirectory, cancellationToken);
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

    private async Task<CaptureManifest> CreateManifestFromDirectoryAsync(string directory, string captureId, string platform, string tag, DateTimeOffset startedAt, Projects.UkitProject project, string deviceSerial, string? deviceModel, string deviceStatus, string packageName, string deviceSavedDirectory, CancellationToken cancellationToken)
    {
        var files = new List<CaptureFileManifestEntry>();
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
        {
            await using var stream = File.OpenRead(path);
            var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken);
            files.Add(new CaptureFileManifestEntry(Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/'), new FileInfo(path).Length, Convert.ToHexStringLower(hash)));
        }

        return new CaptureManifest(captureId, platform, tag, startedAt, _timeProvider.GetLocalNow(), project.CreateConfigurationSnapshot(), deviceSerial, deviceModel, deviceStatus, packageName, deviceSavedDirectory, files);
    }

    private static string ResolveDeviceSavedDirectory(ProjectSettings settings)
    {
        if (settings.Platform == TargetPlatform.Win64)
        {
            // Win64: Saved directory is on local filesystem.
            if (!string.IsNullOrWhiteSpace(settings.Win64WorkingDirectory))
            {
                return Path.Combine(settings.Win64WorkingDirectory!, settings.UnrealProjectName, "Saved");
            }

            return Path.Combine(settings.UnrealProjectName, "Saved");
        }

        // Android: 閻犱緤绱曢悾鑽ゆ媼閹屾У濞戞挸锕﹀▓?Unix 閻犱警鍨扮欢?
        var path = settings.DeviceSavedRootTemplate.Replace("{PackageName}", settings.PackageName, StringComparison.Ordinal).Replace("{UnrealProjectName}", settings.UnrealProjectName, StringComparison.Ordinal);
        if (!path.StartsWith("/", StringComparison.Ordinal) || path.Contains('\\') || path.Contains('\0'))
        {
            throw new InvalidOperationException("The configured UE Saved path must be an absolute Unix path.");
        }

        return path;
    }

    private static void ValidateRequest(CaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Project);
        ArgumentNullException.ThrowIfNull(request.Device);
        if (!request.Device.IsAvailable) throw new InvalidOperationException($"Capture requires a device with status 'available'. Device '{request.Device.Id}' is not available.");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Project.Settings.PackageName);
        NormalizeTag(request.Tag);
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