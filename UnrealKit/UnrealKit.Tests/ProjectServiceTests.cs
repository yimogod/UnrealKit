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
    }

    [Fact]
    public async Task OpenProjectAsync_MisspelledPlatform_FailsInsteadOfDefaultingToAndroid()
    {
        // 静默回退会让 Win64 工程按 Android 采集，产出空数据却报告成功。
        var projectDirectory = Path.Combine(_temporaryDirectory, "TypoProject");
        var service = new ProjectService();
        var created = await service.CreateProjectAsync(new CreateProjectRequest(projectDirectory, "TypoProject"));
        var iniPath = created.Project.ConfigFilePath;
        var ini = await File.ReadAllTextAsync(iniPath);
        await File.WriteAllTextAsync(iniPath, ini.Replace("Platform=Android", "Platform=Andriod", StringComparison.Ordinal));

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.OpenProjectAsync(created.Project.ProjectFilePath));

        Assert.Contains("Andriod", exception.Message);
        Assert.Contains("Win64", exception.Message);
    }

    [Fact]
    public async Task OpenProjectAsync_AbsentPlatform_UsesDefault()
    {
        var projectDirectory = Path.Combine(_temporaryDirectory, "DefaultPlatformProject");
        var service = new ProjectService();
        var created = await service.CreateProjectAsync(new CreateProjectRequest(projectDirectory, "DefaultPlatformProject"));

        var reopened = await service.OpenProjectAsync(created.Project.ProjectFilePath);

        Assert.Equal(TargetPlatform.Android, reopened.Settings.Platform);
    }


    [Fact]
    public async Task UpdateSettingsAsync_PersistsRemoteControlConfiguration()
    {
        var projectDirectory = Path.Combine(_temporaryDirectory, "RemoteControlProject");
        var service = new ProjectService();
        var created = await service.CreateProjectAsync(new CreateProjectRequest(projectDirectory, "RemoteControlProject"));
        var settings = created.Project.Settings with
        {
            RemoteControlHttpPort = 31010,
            RemoteControlObjectPath = "/Game/RC/RC_Preset.RC_Preset:PersistentLevel.BP_Console",
            RemoteControlFunctionName = "RunConsoleCommand",
            RemoteControlCommandParameter = "CommandText"
        };

        await service.UpdateSettingsAsync(created.Project, settings);
        var reopened = await service.OpenProjectAsync(created.Project.ProjectFilePath);

        Assert.Equal(31010, reopened.Settings.RemoteControlHttpPort);
        Assert.Equal("/Game/RC/RC_Preset.RC_Preset:PersistentLevel.BP_Console", reopened.Settings.RemoteControlObjectPath);
        Assert.Equal("RunConsoleCommand", reopened.Settings.RemoteControlFunctionName);
        Assert.Equal("CommandText", reopened.Settings.RemoteControlCommandParameter);
    }

    [Fact]
    public async Task OpenProjectAsync_EmptyRemoteControlValues_FallBackToDefaults()
    {
        var projectDirectory = Path.Combine(_temporaryDirectory, "EmptyRemoteControlProject");
        var service = new ProjectService();
        var created = await service.CreateProjectAsync(new CreateProjectRequest(projectDirectory, "EmptyRemoteControlProject"));
        var configPath = Path.Combine(projectDirectory, "Config", "DefaultGame.ini");

        // 手工编辑出的空值：INI 把 `Key=` 存为空串而不是 null。
        await File.AppendAllTextAsync(configPath,
            Environment.NewLine
            + "RemoteControlObjectPath=" + Environment.NewLine
            + "RemoteControlFunctionName=" + Environment.NewLine
            + "RemoteControlCommandParameter=" + Environment.NewLine);

        var reopened = await service.OpenProjectAsync(created.Project.ProjectFilePath);

        Assert.Equal(created.Project.Settings.RemoteControlObjectPath, reopened.Settings.RemoteControlObjectPath);
        Assert.Equal(created.Project.Settings.RemoteControlFunctionName, reopened.Settings.RemoteControlFunctionName);
        Assert.Equal(created.Project.Settings.RemoteControlCommandParameter, reopened.Settings.RemoteControlCommandParameter);
        Assert.NotEmpty(reopened.Settings.RemoteControlObjectPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
