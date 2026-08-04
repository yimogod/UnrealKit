using UnrealKit.Core.Diagnostics;

namespace UnrealKit.Core.Projects;

public sealed record UkitProjectDescriptor(
    int FormatVersion,
    string ProjectName,
    string ContentRoot,
    string ConfigRoot,
    string SavedRoot,
    string IntermediateRoot)
{
    public const int CurrentFormatVersion = 1;

    public static UkitProjectDescriptor CreateDefault(string projectName) => new(
        CurrentFormatVersion, projectName, "Content", "Config", "Saved", "Intermediate");
}

public sealed record LaunchParameterPreset(string Name, string Arguments);

public sealed record ProjectSettings(
    string PackageName,
    string UnrealProjectName,
    string Activity,
    string DeviceSavedRootTemplate,
    string LocalWorkingDirectory,
    string AdbPath,
    string DefaultCaptureTag,
    string DefaultExportDirectory,
    IReadOnlyList<LaunchParameterPreset> LaunchParameterPresets)
{
    public static ProjectSettings CreateDefaults(string projectName) => new(
        string.Empty,
        projectName,
        string.Empty,
        "/sdcard/Android/data/{PackageName}/files/UE4Game/{UnrealProjectName}/{UnrealProjectName}/Saved",
        string.Empty,
        "adb",
        "Default",
        "Saved/Exports",
        Array.Empty<LaunchParameterPreset>());
}

public sealed record ProjectConfigurationSnapshot(
    UkitProjectDescriptor Descriptor,
    ProjectSettings Settings,
    DateTimeOffset CapturedAt);

public sealed record ProjectValidationResult(IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

public sealed record UkitProject(
    string ProjectFilePath,
    string RootDirectory,
    UkitProjectDescriptor Descriptor,
    ProjectSettings Settings)
{
    public string ConfigFilePath => Path.Combine(RootDirectory, Descriptor.ConfigRoot, "DefaultGame.ini");

    public ProjectConfigurationSnapshot CreateConfigurationSnapshot() =>
        new(Descriptor, Settings, DateTimeOffset.UtcNow);
}

public sealed record CreateProjectRequest(string DirectoryPath, string ProjectName);

public sealed record ProjectCreateResult(UkitProject Project, ProjectValidationResult Validation);
