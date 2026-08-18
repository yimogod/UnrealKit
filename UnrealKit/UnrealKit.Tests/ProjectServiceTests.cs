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
            DefaultCaptureTag = "Nightly",
            Android = new AndroidPlatformProfile(
                PackageName: "com.example.memoryreview",
                Activity: "com.epicgames.unreal.GameActivity",
                GameRootTemplate: "/sdcard/Android/data/{PackageName}/files/UnrealGame",
                AdbPath: "C:\\Android\\platform-tools\\adb.exe")
        };

        await service.UpdateSettingsAsync(created.Project, settings);
        var reopened = await service.OpenProjectAsync(created.Project.ProjectFilePath);

        Assert.Equal(settings.DefaultCaptureTag, reopened.Settings.DefaultCaptureTag);
        Assert.Equal(settings.Android, reopened.Settings.Android);
    }

    [Fact]
    public async Task UpdateSettingsAsync_MultiplePlatformsCoexist()
    {
        // 同一工程同时配置 Android 与 Win64：平台之间不互斥，
        // 保存其中一个不能把另一个清掉。
        var projectDirectory = Path.Combine(_temporaryDirectory, "MultiPlatform");
        var service = new ProjectService();
        var created = await service.CreateProjectAsync(new CreateProjectRequest(projectDirectory, "MultiPlatform"));
        var settings = created.Project.Settings with
        {
            Android = AndroidPlatformProfile.CreateDefaults() with { PackageName = "com.example.game" },
            Win64 = new Win64PlatformProfile(@"C:\Game\MyGame.exe", @"C:\Game")
        };

        await service.UpdateSettingsAsync(created.Project, settings);
        var reopened = await service.OpenProjectAsync(created.Project.ProjectFilePath);

        Assert.Equal("com.example.game", reopened.Settings.Android?.PackageName);
        Assert.Equal(@"C:\Game\MyGame.exe", reopened.Settings.Win64?.Executable);
        Assert.Equal(["Android", "Win64"], reopened.Settings.ConfiguredPlatforms);
    }

    [Fact]
    public async Task UpdateSettingsAsync_DisabledPlatformIsRemovedNotBlanked()
    {
        // 取消某个平台后必须真正消失：留一份空值配置会让「该平台未配置」的报错
        // 永不触发，改为在采集阶段以路径错误的形式出现。
        var projectDirectory = Path.Combine(_temporaryDirectory, "AndroidOnly");
        var service = new ProjectService();
        var created = await service.CreateProjectAsync(new CreateProjectRequest(projectDirectory, "AndroidOnly"));

        await service.UpdateSettingsAsync(created.Project, created.Project.Settings with { Win64 = null });
        var reopened = await service.OpenProjectAsync(created.Project.ProjectFilePath);

        Assert.Null(reopened.Settings.Win64);
        Assert.Equal(["Android"], reopened.Settings.ConfiguredPlatforms);
    }

    [Fact]
    public async Task OpenProjectAsync_LegacyV1Layout_FailsWithMigrationInstructions()
    {
        // v1 用单个 Platform 字段表示当前平台，另一平台的字段从未填写过，
        // 自动迁移只能靠猜。这里必须报错并给出改法，而不是猜一份配置出来。
        var projectDirectory = Path.Combine(_temporaryDirectory, "LegacyProject");
        var service = new ProjectService();
        var created = await service.CreateProjectAsync(new CreateProjectRequest(projectDirectory, "LegacyProject"));
        await File.WriteAllTextAsync(created.Project.ConfigFilePath, """
            [UnrealKit.ProjectSettings]
            PackageName=com.example.legacy
            UnrealProjectName=LegacyProject
            Platform=Win64
            Win64Executable=C:\Game\Legacy.exe
            DefaultCaptureTag=Default
            """);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => service.OpenProjectAsync(created.Project.ProjectFilePath));

        Assert.Contains("SettingsVersion", exception.Message, StringComparison.Ordinal);
        Assert.Contains("UnrealKit.Platform.Android", exception.Message, StringComparison.Ordinal);
        Assert.Contains("UnrealKit.Platform.Win64", exception.Message, StringComparison.Ordinal);
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
            Win64 = new Win64PlatformProfile(@"C:\Game\MyGame.exe", @"C:\Game")
        };

        await service.UpdateSettingsAsync(created.Project, settings);
        var reopened = await service.OpenProjectAsync(created.Project.ProjectFilePath);

        Assert.Equal(@"C:\Game\MyGame.exe", reopened.Settings.Win64?.Executable);
        Assert.Equal(@"C:\Game", reopened.Settings.Win64?.WorkingDirectory);
    }

    [Fact]
    public async Task CreateProjectAsync_ConfiguresBothPlatformsByDefault()
    {
        // 多平台是默认假设：新建工程不应该迫使用户先挑一个平台。
        var projectDirectory = Path.Combine(_temporaryDirectory, "DefaultPlatformProject");
        var service = new ProjectService();
        var created = await service.CreateProjectAsync(new CreateProjectRequest(projectDirectory, "DefaultPlatformProject"));

        var reopened = await service.OpenProjectAsync(created.Project.ProjectFilePath);

        Assert.Equal(["Android", "Win64"], reopened.Settings.ConfiguredPlatforms);
    }

    [Fact]
    public async Task ResolveTarget_UnconfiguredPlatform_ListsConfiguredPlatforms()
    {
        // 未配置的平台必须报错并说明有哪些可选，不能回退到另一个平台的配置。
        var projectDirectory = Path.Combine(_temporaryDirectory, "AndroidOnlyResolve");
        var service = new ProjectService();
        var created = await service.CreateProjectAsync(new CreateProjectRequest(projectDirectory, "AndroidOnlyResolve"));
        var settings = created.Project.Settings with { Win64 = null };

        var exception = Assert.Throws<InvalidOperationException>(() => settings.ResolveTarget(TargetPlatform.Win64));

        Assert.Contains("Win64", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Android", exception.Message, StringComparison.Ordinal);
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
    public async Task UpdateSettingsAsync_PersistsDeviceAliases()
    {
        var projectDirectory = Path.Combine(_temporaryDirectory, "AliasProject");
        var service = new ProjectService();
        var created = await service.CreateProjectAsync(new CreateProjectRequest(projectDirectory, "AliasProject"));
        var settings = created.Project.Settings with
        {
            DeviceAliases = DeviceAliasMap.Create(new Dictionary<string, string>
            {
                ["R58M123ABC"] = "测试机A-红米K60",
                ["192.168.1.100:5555"] = "测试机B-无线"
            })
        };

        await service.UpdateSettingsAsync(created.Project, settings);
        var reopened = await service.OpenProjectAsync(created.Project.ProjectFilePath);

        Assert.Equal("测试机A-红米K60", reopened.Settings.TryGetDeviceAlias("R58M123ABC"));
        // Wi-Fi 序列号含 `:`，INI 以首个 `=` 为分隔符，因此键不会被 `:` 截断。
        Assert.Equal("测试机B-无线", reopened.Settings.TryGetDeviceAlias("192.168.1.100:5555"));
    }

    [Fact]
    public async Task OpenProjectAsync_DeviceAliasLookupIsCaseInsensitiveAndAbsentAliasIsNull()
    {
        // ADB 序列号大小写不敏感（--device 匹配也是），别名查找与之一致，
        // 否则同一台设备在不同大小写下会显示成「没配别名」。
        var projectDirectory = Path.Combine(_temporaryDirectory, "AliasCaseProject");
        var service = new ProjectService();
        var created = await service.CreateProjectAsync(new CreateProjectRequest(projectDirectory, "AliasCaseProject"));
        await File.AppendAllTextAsync(created.Project.ConfigFilePath,
            Environment.NewLine
            + "[UnrealKit.DeviceAliases]" + Environment.NewLine
            + "r58m123abc=测试机A" + Environment.NewLine
            // 空值条目：INI 把 `Key=` 存为空串，不能变成一个空别名。
            + "EMPTYALIAS=" + Environment.NewLine);

        var reopened = await service.OpenProjectAsync(created.Project.ProjectFilePath);

        Assert.Equal("测试机A", reopened.Settings.TryGetDeviceAlias("R58M123ABC"));
        Assert.Null(reopened.Settings.TryGetDeviceAlias("EMPTYALIAS"));
        Assert.Null(reopened.Settings.TryGetDeviceAlias("UNKNOWN-SERIAL"));
        Assert.Equal(1, reopened.Settings.Aliases.Count);
    }

    [Fact]
    public async Task OpenProjectAsync_NoAliasSection_YieldsEmptyMap()
    {
        // 没配过别名的工程照常可用：别名缺失不是错误，也不该是 null 让调用方判空。
        var projectDirectory = Path.Combine(_temporaryDirectory, "NoAliasProject");
        var service = new ProjectService();
        var created = await service.CreateProjectAsync(new CreateProjectRequest(projectDirectory, "NoAliasProject"));

        var reopened = await service.OpenProjectAsync(created.Project.ProjectFilePath);

        Assert.Equal(0, reopened.Settings.Aliases.Count);
        Assert.Null(reopened.Settings.TryGetDeviceAlias("R58M123ABC"));
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
