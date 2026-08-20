using UnrealKit.Core.Download;
using UnrealKit.Core.Projects;

namespace UnrealKit.Tests;

public sealed class LocalDownloadCatalogTests
{
    private static string NewTempDirectory() =>
        Path.Combine(Path.GetTempPath(), "UnrealKit.Tests", "DownloadCatalog", Guid.NewGuid().ToString("N"));

    [Fact]
    public void List_MissingDirectory_ReturnsEmpty()
    {
        var packages = LocalDownloadCatalog.List(NewTempDirectory(), TargetPlatform.Android);

        Assert.Empty(packages);
    }

    [Fact]
    public void List_Android_ReturnsNaturalOrderedInstallablePackages()
    {
        var baseDirectory = NewTempDirectory();
        CreateApkPackage(baseDirectory, "v1.0.10", "Game.apk");
        CreateApkPackage(baseDirectory, "v1.0.2", "Game.apk");
        CreateApkPackage(baseDirectory, "v1.0.9", "Game.apk");

        var packages = LocalDownloadCatalog.List(baseDirectory, TargetPlatform.Android);

        Assert.Equal(3, packages.Count);
        Assert.Equal(["v1.0.2", "v1.0.9", "v1.0.10"], packages.Select(package => package.FolderName));
        Assert.All(packages, package => Assert.True(package.IsInstallable));
        Assert.All(packages, package => Assert.EndsWith("Game.apk", package.LocalApkPath, StringComparison.Ordinal));
    }

    [Fact]
    public void List_Android_ReportsBlockReasonWhenNoApk()
    {
        var baseDirectory = NewTempDirectory();
        CreateEmptyPackage(baseDirectory, "v2");

        var packages = LocalDownloadCatalog.List(baseDirectory, TargetPlatform.Android);

        Assert.False(Assert.Single(packages).IsInstallable);
        Assert.Contains("apk", Assert.Single(packages).InstallBlockReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void List_Android_ReportsBlockReasonWhenMultipleApks()
    {
        var baseDirectory = NewTempDirectory();
        CreateApkPackage(baseDirectory, "v3", "Game-arm64.apk", "Game-arm32.apk");

        var packages = LocalDownloadCatalog.List(baseDirectory, TargetPlatform.Android);

        Assert.False(Assert.Single(packages).IsInstallable);
        Assert.Contains("无法确定", Assert.Single(packages).InstallBlockReason, StringComparison.Ordinal);
    }

    [Fact]
    public void List_Win64_IsNeverInstallable()
    {
        var baseDirectory = NewTempDirectory();
        CreateEmptyPackage(baseDirectory, "2024.01.05");

        var packages = LocalDownloadCatalog.List(baseDirectory, TargetPlatform.Win64);

        Assert.False(Assert.Single(packages).IsInstallable);
        Assert.Contains("Win64", Assert.Single(packages).InstallBlockReason, StringComparison.Ordinal);
    }

    [Fact]
    public void List_IgnoresNonDirectoryEntries()
    {
        var baseDirectory = NewTempDirectory();
        Directory.CreateDirectory(baseDirectory);
        File.WriteAllText(Path.Combine(baseDirectory, "notes.txt"), "not a package");
        CreateApkPackage(baseDirectory, "v1", "Game.apk");

        var packages = LocalDownloadCatalog.List(baseDirectory, TargetPlatform.Android);

        Assert.Single(packages);
    }

    private static void CreateApkPackage(string baseDirectory, string folderName, params string[] apkNames)
    {
        var directory = Path.Combine(baseDirectory, folderName);
        Directory.CreateDirectory(directory);
        foreach (var apkName in apkNames)
        {
            File.WriteAllText(Path.Combine(directory, apkName), "apk");
        }
    }

    private static void CreateEmptyPackage(string baseDirectory, string folderName)
    {
        Directory.CreateDirectory(Path.Combine(baseDirectory, folderName));
    }
}
