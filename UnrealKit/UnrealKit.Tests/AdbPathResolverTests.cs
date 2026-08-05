using UnrealKit.Core.Adb;

namespace UnrealKit.Tests;

public sealed class AdbPathResolverTests
{
    [Fact]
    public void Resolve_UsesExplicitPathBeforeAllOtherSources()
    {
        var resolver = CreateResolver(["C:\\tools\\adb.exe", "C:\\project\\adb.exe", "C:\\sdk\\platform-tools\\adb.exe", "C:\\path\\adb.exe"]);

        var result = resolver.Resolve("C:\\tools\\adb.exe", "C:\\project\\adb.exe");

        Assert.Equal(Path.GetFullPath("C:\\tools\\adb.exe"), result.ResolvedPath);
        Assert.Single(result.Attempts);
        Assert.Equal(AdbPathSource.Explicit, result.Attempts[0].Source);
    }

    [Fact]
    public void Resolve_UsesProjectSettingBeforeEnvironmentAndPath()
    {
        var resolver = CreateResolver(["C:\\project\\adb.exe", "C:\\sdk\\platform-tools\\adb.exe", "C:\\path\\adb.exe"]);

        var result = resolver.Resolve(null, "C:\\project\\adb.exe");

        Assert.Equal(Path.GetFullPath("C:\\project\\adb.exe"), result.ResolvedPath);
        Assert.Equal(AdbPathSource.ProjectSettings, result.Attempts[^1].Source);
    }

    [Fact]
    public void Resolve_UsesEnvironmentBeforePath()
    {
        var resolver = CreateResolver(["C:\\sdk\\platform-tools\\adb.exe", "C:\\path\\adb.exe"]);

        var result = resolver.Resolve(null, null);

        Assert.Equal(Path.GetFullPath("C:\\sdk\\platform-tools\\adb.exe"), result.ResolvedPath);
        Assert.Equal("ANDROID_SDK_ROOT", result.Attempts[^1].Description);
    }

    [Fact]
    public void Resolve_UsesPathWhenHigherPrioritySourcesAreUnavailable()
    {
        var resolver = CreateResolver(["C:\\path\\adb.exe"]);

        var result = resolver.Resolve(null, null);

        Assert.Equal(Path.GetFullPath("C:\\path\\adb.exe"), result.ResolvedPath);
        Assert.Equal(AdbPathSource.Path, result.Attempts[^1].Source);
    }

    [Fact]
    public void Resolve_ReportsNotFoundConfiguredPathAndMissingSources()
    {
        var resolver = CreateResolver([]);

        var result = resolver.Resolve("C:\\missing\\adb.exe", null);

        Assert.False(result.IsResolved);
        Assert.Contains(result.Attempts, attempt => attempt.Source == AdbPathSource.Explicit && attempt.Status == AdbPathAttemptStatus.NotFound);
        Assert.Contains(result.Attempts, attempt => attempt.Source == AdbPathSource.Path && attempt.Status == AdbPathAttemptStatus.NotFound);
        var exception = Assert.Throws<AdbPathResolutionException>(() => resolver.ResolveRequired("C:\\missing\\adb.exe", null));
        Assert.False(exception.Resolution.IsResolved);
    }

    private static AdbPathResolver CreateResolver(IReadOnlyCollection<string> existingPaths)
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ADB_PATH"] = null,
            ["ANDROID_SDK_ROOT"] = "C:\\sdk",
            ["ANDROID_HOME"] = null,
            ["PATH"] = "C:\\path"
        };
        return new AdbPathResolver(
            name => environment.TryGetValue(name, out var value) ? value : null,
            path => existingPaths.Contains(path, StringComparer.OrdinalIgnoreCase),
            isWindows: true);
    }
}
