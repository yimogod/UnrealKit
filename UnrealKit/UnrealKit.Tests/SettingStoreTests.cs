using UnrealKit.Core.Projects;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Tests;

public sealed class SettingStoreTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "UnrealKit.Tests", Guid.NewGuid().ToString("N"));

    private string EditorSettingPath => Path.Combine(_temporaryDirectory, "Config", "EditorSetting.ini");

    [Fact]
    public async Task TryGetLastProjectFilePathAsync_ReturnsNullWhenNoSettingFile()
    {
        var store = new EditorSettingStore(EditorSettingPath);

        Assert.Null(await store.TryGetLastProjectFilePathAsync());
    }

    [Fact]
    public async Task SaveThenRead_RoundTripsFullPath()
    {
        var store = new EditorSettingStore(EditorSettingPath);
        var projectPath = Path.Combine(_temporaryDirectory, "Sample", "Sample.ukit");

        await store.SaveLastProjectFilePathAsync(projectPath);

        Assert.True(File.Exists(EditorSettingPath));
        Assert.Equal(Path.GetFullPath(projectPath), await store.TryGetLastProjectFilePathAsync());
    }

    [Fact]
    public async Task SaveLastProjectFilePathAsync_OverwritesPreviousRecordAndKeepsOtherKeys()
    {
        var settingPath = EditorSettingPath;
        Directory.CreateDirectory(Path.GetDirectoryName(settingPath)!);
        await File.WriteAllTextAsync(settingPath, """
            [UnrealKit.RecentProject]
            LastProjectFilePath=C:\Old\Old.ukit

            [UnrealKit.Other]
            Keep=1
            """);
        var store = new EditorSettingStore(settingPath);

        await store.SaveLastProjectFilePathAsync(@"C:\New\New.ukit");

        Assert.Equal(@"C:\New\New.ukit", await store.TryGetLastProjectFilePathAsync());
        var document = IniDocument.Parse(await File.ReadAllTextAsync(settingPath));
        Assert.Equal("1", document.GetValue("UnrealKit.Other", "Keep"));
    }

    [Fact]
    public async Task TryGetLastProjectFilePathAsync_ReturnsNullWhenRecordIsBlank()
    {
        var settingPath = EditorSettingPath;
        Directory.CreateDirectory(Path.GetDirectoryName(settingPath)!);
        await File.WriteAllTextAsync(settingPath, """
            [UnrealKit.RecentProject]
            LastProjectFilePath=
            """);

        Assert.Null(await new EditorSettingStore(settingPath).TryGetLastProjectFilePathAsync());
    }

    [Fact]
    public void DefaultSettingFilePath_LivesUnderAppConfigDirectory()
    {
        // 「上次打开哪个工程」不能存在工程里：要先知道打开哪个工程才能去读它的配置。
        Assert.StartsWith(ApplicationPaths.AppConfigDir, EditorSettingStore.DefaultSettingFilePath, StringComparison.Ordinal);
        Assert.Equal("EditorSetting.ini", Path.GetFileName(EditorSettingStore.DefaultSettingFilePath));
    }

    [Fact]
    public async Task TryGetPlatformScopeAsync_ReturnsNullWhenProjectHasNoRecord()
    {
        // 没有记录返回 null 而不是「全部」：调用方据此保留当前作用域，不重置用户刚选的平台。
        Assert.Null(await new UserSettingStore().TryGetPlatformScopeAsync(CreateProject()));
    }

    [Theory]
    [InlineData("Android")]
    [InlineData("Win64")]
    [InlineData(PlatformScope.AllName)]
    public async Task SavePlatformScopeAsync_RoundTripsThroughProjectConfigDirectory(string scopeName)
    {
        var project = CreateProject();
        var store = new UserSettingStore();
        Assert.True(PlatformScope.TryParse(scopeName, out var scope));

        await store.SavePlatformScopeAsync(project, scope);

        Assert.True(File.Exists(project.UserSettingFilePath));
        Assert.Equal(scope, await store.TryGetPlatformScopeAsync(project));
    }

    [Fact]
    public async Task PlatformScope_IsRecordedPerProject()
    {
        // 换工程就换一份作用域：软件级记录会让上一个工程的作用域藏起下一个工程的设备。
        var store = new UserSettingStore();
        var android = CreateProject("Android");
        var win64 = CreateProject("Win64");

        await store.SavePlatformScopeAsync(android, PlatformScope.For(TargetPlatform.Android));
        await store.SavePlatformScopeAsync(win64, PlatformScope.For(TargetPlatform.Win64));

        Assert.Equal(PlatformScope.For(TargetPlatform.Android), await store.TryGetPlatformScopeAsync(android));
        Assert.Equal(PlatformScope.For(TargetPlatform.Win64), await store.TryGetPlatformScopeAsync(win64));
    }

    [Fact]
    public async Task SavePlatformScopeAsync_KeepsOtherKeysInUserSettingFile()
    {
        var project = CreateProject();
        Directory.CreateDirectory(project.ConfigDir);
        await File.WriteAllTextAsync(project.UserSettingFilePath, """
            [UnrealKit.Other]
            Keep=1
            """);

        await new UserSettingStore().SavePlatformScopeAsync(project, PlatformScope.For(TargetPlatform.Win64));

        var document = IniDocument.Parse(await File.ReadAllTextAsync(project.UserSettingFilePath));
        Assert.Equal("1", document.GetValue("UnrealKit.Other", "Keep"));
    }

    [Fact]
    public async Task SavePlatformScopeAsync_DoesNotTouchDefaultGameIni()
    {
        // 作用域是个人状态，写它不能让可版本化的工程配置产生 diff。
        var project = CreateProject();
        Directory.CreateDirectory(project.ConfigDir);
        const string original = "[UnrealKit.ProjectSettings]\nUnrealProjectName=Sample\n";
        await File.WriteAllTextAsync(project.ConfigFilePath, original);

        await new UserSettingStore().SavePlatformScopeAsync(project, PlatformScope.For(TargetPlatform.Win64));

        Assert.Equal(original, await File.ReadAllTextAsync(project.ConfigFilePath));
    }

    [Fact]
    public async Task TryGetPlatformScopeAsync_ReturnsNullOnUnrecognizedValue()
    {
        // 手工改坏或版本遗留的取值当作没有记录，而不是让界面聚焦到用户没选过的平台。
        var project = CreateProject();
        Directory.CreateDirectory(project.ConfigDir);
        await File.WriteAllTextAsync(project.UserSettingFilePath, """
            [UnrealKit.Scope]
            Platform=PlayStation9
            """);

        Assert.Null(await new UserSettingStore().TryGetPlatformScopeAsync(project));
    }

    private UkitProject CreateProject(string name = "Sample")
    {
        var root = Path.Combine(_temporaryDirectory, name);
        return new UkitProject(
            Path.Combine(root, $"{name}.ukit"),
            root,
            UkitProjectDescriptor.CreateDefault(name),
            ProjectSettings.CreateDefaults(name));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
