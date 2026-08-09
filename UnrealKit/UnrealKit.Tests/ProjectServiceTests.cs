using UnrealKit.Core.Projects;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Tests;

public sealed class ProjectServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "UnrealKit.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreateProjectAsync_CreatesExpectedProjectLayout()
    {
        var service = new ProjectService();
        var projectDirectory = Path.Combine(_temporaryDirectory, "MemoryReview");

        var result = await service.CreateProjectAsync(new CreateProjectRequest(projectDirectory, "MemoryReview"));

        Assert.True(result.Validation.IsValid);
        Assert.True(File.Exists(Path.Combine(projectDirectory, "MemoryReview.ukit")));
        Assert.True(File.Exists(Path.Combine(projectDirectory, "Config", "DefaultGame.ini")));
        Assert.True(Directory.Exists(Path.Combine(projectDirectory, "Content")));
        Assert.True(Directory.Exists(Path.Combine(projectDirectory, "Saved")));
        Assert.True(Directory.Exists(Path.Combine(projectDirectory, "Intermediate")));
    }

    [Fact]
    public async Task CreateProjectAsync_RejectsNonEmptyDirectory()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "existing.txt"), "existing");
        var service = new ProjectService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateProjectAsync(new CreateProjectRequest(_temporaryDirectory, "MemoryReview")));

        Assert.Contains("不是空目录", exception.Message);
    }

    [Fact]
    public async Task ValidateProjectAsync_ReportsUnsupportedFormatVersion()
    {
        var projectDirectory = Path.Combine(_temporaryDirectory, "MemoryReview");
        var service = new ProjectService();
        var project = await service.CreateProjectAsync(new CreateProjectRequest(projectDirectory, "MemoryReview"));
        var descriptor = await File.ReadAllTextAsync(project.Project.ProjectFilePath);
        await File.WriteAllTextAsync(project.Project.ProjectFilePath, descriptor.Replace("FormatVersion=1", "FormatVersion=999", StringComparison.Ordinal));

        var validation = await service.ValidateProjectAsync(project.Project.ProjectFilePath);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Code == "UKIT002");
    }

    [Fact]
    public async Task UpdateSettingsAsync_PersistsCaptureAndAndroidDefaults()
    {
        var projectDirectory = Path.Combine(_temporaryDirectory, "MemoryReview");
        var service = new ProjectService();
        var created = await service.CreateProjectAsync(new CreateProjectRequest(projectDirectory, "MemoryReview"));
        var settings = created.Project.Settings with
        {
            PackageName = "com.example.memoryreview",
            Activity = "com.epicgames.unreal.GameActivity",
            DefaultCaptureTag = "Nightly",
            DeviceSavedRootTemplate = "/sdcard/Android/data/{PackageName}/files/Saved",
            AdbPath = "C:\\Android\\platform-tools\\adb.exe"
        };

        await service.UpdateSettingsAsync(created.Project, settings);
        var reopened = await service.OpenProjectAsync(created.Project.ProjectFilePath);

        Assert.Equal(settings.PackageName, reopened.Settings.PackageName);
        Assert.Equal(settings.Activity, reopened.Settings.Activity);
        Assert.Equal(settings.DefaultCaptureTag, reopened.Settings.DefaultCaptureTag);
        Assert.Equal(settings.DeviceSavedRootTemplate, reopened.Settings.DeviceSavedRootTemplate);
        Assert.Equal(settings.AdbPath, reopened.Settings.AdbPath);
    }

    [Fact]
    public async Task CreateProjectAsync_ExposesUeStyleProjectDirectories()
    {
        var projectDirectory = Path.Combine(_temporaryDirectory, "MemoryReview");
        var service = new ProjectService();

        var project = (await service.CreateProjectAsync(new CreateProjectRequest(projectDirectory, "MemoryReview"))).Project;

        Assert.Equal(projectDirectory, project.ProjectDir);
        Assert.Equal(Path.Combine(projectDirectory, "Content"), project.ContentDir);
        Assert.Equal(Path.Combine(projectDirectory, "Config"), project.ConfigDir);
        Assert.Equal(Path.Combine(projectDirectory, "Saved"), project.SavedDir);
        Assert.Equal(Path.Combine(projectDirectory, "Intermediate"), project.IntermediateDir);
        Assert.Equal(Path.Combine(project.ConfigDir, "DefaultGame.ini"), project.ConfigFilePath);
    }

    [Fact]
    public void ApplicationPaths_AppDirMatchesRuntimeBaseDirectory()
    {
        Assert.Equal(AppContext.BaseDirectory, ApplicationPaths.AppDir);
    }


    [Fact]
    public async Task UpdateSettingsAsync_PersistsWin64PlatformFields()
    {
        var projectDirectory = Path.Combine(_temporaryDirectory, "Win64Project");
        var service = new ProjectService();
        var created = await service.CreateProjectAsync(new CreateProjectRequest(projectDirectory, "Win64Project"));
        var settings = created.Project.Settings with
        {
            Platform = TargetPlatform.Win64,
            Win64Executable = @"C:\Game\MyGame.exe",
            Win64WorkingDirectory = @"C:\Game",
            PackageName = "MyGame-Win64-Shipping"
        };

        await service.UpdateSettingsAsync(created.Project, settings);
        var reopened = await service.OpenProjectAsync(created.Project.ProjectFilePath);

        Assert.Equal(TargetPlatform.Win64, reopened.Settings.Platform);
        Assert.Equal(@"C:\Game\MyGame.exe", reopened.Settings.Win64Executable);
        Assert.Equal(@"C:\Game", reopened.Settings.Win64WorkingDirectory);
        Assert.Equal("MyGame-Win64-Shipping", reopened.Settings.PackageName);
    }    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
