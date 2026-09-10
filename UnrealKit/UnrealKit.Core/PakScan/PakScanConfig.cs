namespace UnrealKit.Core.PakScan;

public sealed record PakScanConfig
{
    public static readonly PakScanConfig Default = new();

    public string GameVersion { get; init; } = "GAME_UE5_3";

    public string AesKey { get; init; } = string.Empty;

    public string OodleDllPath { get; init; } = string.Empty;

    public bool ExcludeEnginePaths { get; init; } = true;

    public long LargeSizeWarningBytes { get; init; } = 4L * 1024 * 1024;
}
