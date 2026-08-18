using UnrealKit.Core.Projects;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Tests;

public sealed class RecentProjectStoreTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "UnrealKit.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TryGetLastProjectFilePathAsync_ReturnsNullWhenNoStateFile()
    {
        var store = new RecentProjectStore(Path.Combine(_temporaryDirectory, "UserState.ini"));

        Assert.Null(await store.TryGetLastProjectFilePathAsync());
    }

    [Fact]
    public async Task SaveThenRead_RoundTripsFullPath()
    {
        var statePath = Path.Combine(_temporaryDirectory, "UserState.ini");
        var store = new RecentProjectStore(statePath);
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
        var store = new RecentProjectStore(statePath);

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

        Assert.Null(await new RecentProjectStore(statePath).TryGetLastProjectFilePathAsync());
    }

    [Fact]
    public void DefaultStateFilePath_LivesUnderUserStateDirectory()
    {
        // 用户级状态不能落在程序目录：程序目录可能只读或整体替换。
        Assert.StartsWith(ApplicationPaths.UserStateDir, RecentProjectStore.DefaultStateFilePath, StringComparison.Ordinal);
        Assert.NotEqual(ApplicationPaths.AppDir, ApplicationPaths.UserStateDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
