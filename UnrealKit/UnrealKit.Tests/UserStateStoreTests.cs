using UnrealKit.Core.Projects;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Tests;

public sealed class UserStateStoreTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "UnrealKit.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TryGetLastProjectFilePathAsync_ReturnsNullWhenNoStateFile()
    {
        var store = new UserStateStore(Path.Combine(_temporaryDirectory, "UserState.ini"));

        Assert.Null(await store.TryGetLastProjectFilePathAsync());
    }

    [Fact]
    public async Task SaveThenRead_RoundTripsFullPath()
    {
        var statePath = Path.Combine(_temporaryDirectory, "UserState.ini");
        var store = new UserStateStore(statePath);
        var projectPath = Path.Combine(_temporaryDirectory, "Sample", "Sample.ukit");

        await store.SaveLastProjectFilePathAsync(projectPath);

        Assert.True(File.Exists(statePath));
        Assert.Equal(Path.GetFullPath(projectPath), await store.TryGetLastProjectFilePathAsync());
    }

    [Fact]
    public async Task SaveLastProjectFilePathAsync_OverwritesPreviousRecordAndKeepsOtherKeys()
    {
        var statePath = Path.Combine(_temporaryDirectory, "UserState.ini");
        Directory.CreateDirectory(_temporaryDirectory);
        await File.WriteAllTextAsync(statePath, """
            [UnrealKit.RecentProject]
            LastProjectFilePath=C:\Old\Old.ukit

            [UnrealKit.Other]
            Keep=1
            """);
        var store = new UserStateStore(statePath);

        await store.SaveLastProjectFilePathAsync(@"C:\New\New.ukit");

        Assert.Equal(@"C:\New\New.ukit", await store.TryGetLastProjectFilePathAsync());
        var document = IniDocument.Parse(await File.ReadAllTextAsync(statePath));
        Assert.Equal("1", document.GetValue("UnrealKit.Other", "Keep"));
    }

    [Fact]
    public async Task TryGetLastProjectFilePathAsync_ReturnsNullWhenRecordIsBlank()
    {
        var statePath = Path.Combine(_temporaryDirectory, "UserState.ini");
        Directory.CreateDirectory(_temporaryDirectory);
        await File.WriteAllTextAsync(statePath, """
            [UnrealKit.RecentProject]
            LastProjectFilePath=
            """);

        Assert.Null(await new UserStateStore(statePath).TryGetLastProjectFilePathAsync());
    }

    [Fact]
    public void DefaultStateFilePath_LivesUnderUserStateDirectory()
    {
        // 用户级状态不能落在程序目录：程序目录可能只读或整体替换。
        Assert.StartsWith(ApplicationPaths.UserStateDir, UserStateStore.DefaultStateFilePath, StringComparison.Ordinal);
        Assert.NotEqual(ApplicationPaths.AppDir, ApplicationPaths.UserStateDir);
    }

    [Fact]
    public async Task GetPlatformScopeAsync_DefaultsToAllWhenUnset()
    {
        // 没有记录时必须是「全部」：默认到某个平台会静默隐藏另一个平台的设备与归档。
        var store = new UserStateStore(Path.Combine(_temporaryDirectory, "UserState.ini"));

        Assert.True((await store.GetPlatformScopeAsync()).IsAll);
    }

    [Theory]
    [InlineData("Android")]
    [InlineData("Win64")]
    [InlineData(PlatformScope.AllName)]
    public async Task SavePlatformScopeAsync_RoundTrips(string scopeName)
    {
        var store = new UserStateStore(Path.Combine(_temporaryDirectory, "UserState.ini"));
        Assert.True(PlatformScope.TryParse(scopeName, out var scope));

        await store.SavePlatformScopeAsync(scope);

        Assert.Equal(scope, await store.GetPlatformScopeAsync());
    }

    [Fact]
    public async Task GetPlatformScopeAsync_FallsBackToAllOnUnrecognizedValue()
    {
        // 手工改坏或版本遗留的取值回落到「全部」，而不是让界面聚焦到用户没选过的平台。
        var statePath = Path.Combine(_temporaryDirectory, "UserState.ini");
        Directory.CreateDirectory(_temporaryDirectory);
        await File.WriteAllTextAsync(statePath, """
            [UnrealKit.Scope]
            Platform=PlayStation9
            """);

        Assert.True((await new UserStateStore(statePath).GetPlatformScopeAsync()).IsAll);
    }

    [Fact]
    public async Task ProjectAndScope_ShareOneFileWithoutOverwritingEachOther()
    {
        // 两项状态共用一个文件，写其中一项不能抹掉另一项。
        var statePath = Path.Combine(_temporaryDirectory, "UserState.ini");
        var store = new UserStateStore(statePath);

        await store.SaveLastProjectFilePathAsync(@"C:\Games\Sample\Sample.ukit");
        await store.SavePlatformScopeAsync(PlatformScope.For(TargetPlatform.Win64));

        Assert.Equal(@"C:\Games\Sample\Sample.ukit", await store.TryGetLastProjectFilePathAsync());
        Assert.Equal(PlatformScope.For(TargetPlatform.Win64), await store.GetPlatformScopeAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
