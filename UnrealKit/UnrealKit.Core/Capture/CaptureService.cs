using UnrealKit.Core.Adb;
using UnrealKit.Core.Operations;

namespace UnrealKit.Core.Capture;

public sealed class CaptureService(IAdbService adbService, TimeProvider? timeProvider = null) : ICaptureService
{
    private const string Platform = "Android";
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public CapturePlan CreatePlan(CaptureRequest request, DateTimeOffset? capturedAt = null)
    {
        ValidateRequest(request);
        var capturedLocalTime = capturedAt ?? _timeProvider.GetLocalNow();
        var captureId = string.IsNullOrWhiteSpace(request.CaptureId)
            ? CreateCaptureId(capturedLocalTime, request.Device.SerialNumber)
            : ValidateCaptureId(request.CaptureId);
        var contentRoot = request.Project.ContentDir;
        var captureDirectory = Path.Combine(contentRoot, Platform, NormalizeTag(request.Tag), capturedLocalTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), captureId);
        return new CapturePlan(captureId, captureDirectory, ResolveDeviceSavedDirectory(request.Project.Settings));
    }

    public async Task<CaptureResult> CaptureAsync(CaptureRequest request, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
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
        var memInfoDirectory = Path.Combine(stagingDirectory, "MemInfo");
        Directory.CreateDirectory(memInfoDirectory);
        progress?.Report(new OperationProgress("capture", "MemInfo", 1, 3, $"Collecting dumpsys meminfo for {request.Project.Settings.PackageName}."));
        var memInfo = await adbService.RunDumpsysAsync(request.Device.SerialNumber, request.Project.Settings.PackageName, progress, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(memInfoDirectory, $"meminfo_{startedAt:yyyyMMdd-HHmmss}.txt"), memInfo.StandardOutput, cancellationToken);

        progress?.Report(new OperationProgress("capture", "Saved", 2, 3, $"Pulling UE Saved data from {plan.DeviceSavedDirectory}."));
        await adbService.PullDirectoryAsync(request.Device.SerialNumber, plan.DeviceSavedDirectory, Path.Combine(stagingDirectory, "Saved"), progress, cancellationToken);

        var manifest = await CreateManifestAsync(request, plan, startedAt, stagingDirectory, cancellationToken);
        progress?.Report(new OperationProgress("capture", "Manifest", 3, 3, "Writing capture manifest and archiving original data."));
        await WriteManifestAsync(stagingDirectory, manifest, cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(plan.CaptureDirectory)!);
        Directory.Move(stagingDirectory, plan.CaptureDirectory);
        return new CaptureResult(plan, Path.Combine(plan.CaptureDirectory, "CaptureManifest.json"), manifest);
    }

    private async Task<CaptureManifest> CreateManifestAsync(CaptureRequest request, CapturePlan plan, DateTimeOffset startedAt, string stagingDirectory, CancellationToken cancellationToken)
    {
        var files = new List<CaptureFileManifestEntry>();
        foreach (var path in Directory.EnumerateFiles(stagingDirectory, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
        {
            await using var stream = File.OpenRead(path);
            var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken);
            files.Add(new CaptureFileManifestEntry(Path.GetRelativePath(stagingDirectory, path).Replace(Path.DirectorySeparatorChar, '/'), new FileInfo(path).Length, Convert.ToHexStringLower(hash)));
        }

        return new CaptureManifest(plan.CaptureId, Platform, NormalizeTag(request.Tag), startedAt, _timeProvider.GetLocalNow(), request.Project.CreateConfigurationSnapshot(), request.Device.SerialNumber, request.Device.Model, request.Device.Status.ToString(), request.Project.Settings.PackageName, plan.DeviceSavedDirectory, files);
    }

    private static async Task WriteManifestAsync(string stagingDirectory, CaptureManifest manifest, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(Path.Combine(stagingDirectory, "CaptureManifest.json"));
        await System.Text.Json.JsonSerializer.SerializeAsync(stream, manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }, cancellationToken);
    }

    private static string ResolveDeviceSavedDirectory(Projects.ProjectSettings settings)
    {
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
        if (!request.Device.IsAvailable) throw new AdbDeviceSelectionException("Capture requires a selected ADB device with status 'device'.");
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

    private static string CreateCaptureId(DateTimeOffset localTime, string serialNumber)
    {
        var devicePart = new string(serialNumber.Where(char.IsLetterOrDigit).Take(12).ToArray());
        if (devicePart.Length == 0) devicePart = "device";
        return $"{localTime:yyyyMMdd-HHmmss}-{devicePart}-{Guid.NewGuid():N}";
    }
}
