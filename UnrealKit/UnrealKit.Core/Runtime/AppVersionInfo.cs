using System.Reflection;

namespace UnrealKit.Core.Runtime;

public sealed record AppVersionInfo(string Version, string? GitCommit, DateTimeOffset BuildTime);

public static class AppVersionInfoProvider
{
    public static AppVersionInfo GetCurrent()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppVersionInfoProvider).Assembly;
        var version = assembly.GetName().Version?.ToString() ?? "0.0.0";
        var buildTime = File.GetLastWriteTimeUtc(assembly.Location);
        return new AppVersionInfo(version, Environment.GetEnvironmentVariable("GIT_COMMIT"), new DateTimeOffset(buildTime, TimeSpan.Zero));
    }
}
