using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using UnrealKit.Core.Diagnostics;
using UnrealKit.Core.Operations;

namespace UnrealKit.Core.PakScan;

public sealed class PakScanService : IPakScanService
{
    private static readonly IReadOnlyDictionary<string, EGame> GameVersionMap =
        new Dictionary<string, EGame>(StringComparer.OrdinalIgnoreCase)
        {
            ["GAME_UE5_0"] = EGame.GAME_UE5_0,
            ["GAME_UE5_1"] = EGame.GAME_UE5_1,
            ["GAME_UE5_2"] = EGame.GAME_UE5_2,
            ["GAME_UE5_3"] = EGame.GAME_UE5_3,
            ["GAME_UE5_4"] = EGame.GAME_UE5_4,
            ["GAME_UE5_5"] = EGame.GAME_UE5_5,
            ["GAME_UE5_6"] = EGame.GAME_UE5_6,
            ["GAME_UE4_27"] = EGame.GAME_UE4_27,
            ["GAME_UE4_26"] = EGame.GAME_UE4_26,
            ["GAME_UE4_25"] = EGame.GAME_UE4_25,
        };

    public async Task<PakScanResult> ScanAsync(
        string pakDirectory,
        PakScanConfig? config = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        config ??= PakScanConfig.Default;
        var diagnostics = new List<Diagnostic>();

        if (!Directory.Exists(pakDirectory))
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "PKS001",
                $"Pak 目录不存在：{pakDirectory}", pakDirectory));
            return new PakScanResult(pakDirectory, null, diagnostics);
        }

        var hasPakFiles = Directory.EnumerateFiles(pakDirectory, "*.pak", SearchOption.AllDirectories).Any()
            || Directory.EnumerateFiles(pakDirectory, "*.utoc", SearchOption.AllDirectories).Any()
            || Directory.EnumerateFiles(pakDirectory, "*.ucas", SearchOption.AllDirectories).Any();

        if (!hasPakFiles)
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "PKS002",
                $"目录中未找到任何 .pak / .utoc / .ucas 文件：{pakDirectory}", pakDirectory,
                "请确认路径指向包含游戏包文件的目录"));
            return new PakScanResult(pakDirectory, null, diagnostics);
        }

        var game = ResolveGameVersion(config.GameVersion);
        var provider = new DefaultFileProvider(
            pakDirectory,
            SearchOption.AllDirectories,
            new VersionContainer(game),
            StringComparer.OrdinalIgnoreCase);

        provider.Initialize();

        if (!string.IsNullOrWhiteSpace(config.AesKey))
        {
            try
            {
                var key = new FAesKey(config.AesKey);
                await provider.SubmitKeyAsync(new FGuid(), key);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "PKS004",
                    $"AES 密钥提交失败，加密资产将无法读取：{ex.Message}",
                    SuggestedFix: "请确认密钥格式为十六进制字符串，例如 0x1A2B3C4D..."));
            }
        }

        var allPaths = provider.Files.Keys
            .Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .Where(p => !config.ExcludeEnginePaths || !p.Contains("/Engine/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var totalCount = allPaths.Count;
        var scannedCount = 0;
        var textures = new List<PakTextureEntry>();

        progress?.Report(new OperationProgress("pakScan", "Scan", 0, totalCount, $"开始扫描，共 {totalCount} 个资产…"));

        foreach (var path in allPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scannedCount++;

            if (scannedCount % 50 == 0)
            {
                progress?.Report(new OperationProgress("pakScan", "Scan", scannedCount, totalCount,
                    $"扫描中… {scannedCount}/{totalCount}"));
            }

            try
            {
                var objectPath = path[..^".uasset".Length];
                if (!provider.TryLoadPackageObject<UTexture2D>(objectPath, out var tex) || tex is null)
                    continue;

                var entry = BuildEntry(tex, objectPath, config, diagnostics);
                textures.Add(entry);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "PKS005",
                    $"资产反序列化失败，已跳过：{path} — {ex.Message}", path));
            }
        }

        progress?.Report(new OperationProgress("pakScan", "Done", totalCount, totalCount,
            $"扫描完成：{textures.Count} 个纹理，共 {scannedCount} 个资产"));

        diagnostics.Add(new Diagnostic(DiagnosticSeverity.Information, "PKS006",
            $"扫描完成：{textures.Count} 个 Texture2D，共扫描 {scannedCount} 个资产"));

        var report = new PakScanReport(pakDirectory, scannedCount, textures.Count, textures);
        return new PakScanResult(pakDirectory, report, diagnostics);
    }

    private static PakTextureEntry BuildEntry(
        UTexture2D tex,
        string objectPath,
        PakScanConfig config,
        List<Diagnostic> diagnostics)
    {
        var platData = tex.PlatformData;
        int sizeX = platData?.SizeX ?? (int)tex.ImportedSize.X;
        int sizeY = platData?.SizeY ?? (int)tex.ImportedSize.Y;
        string pixelFormat = platData?.PixelFormat ?? "Unknown";
        int numMips = platData?.Mips?.Length ?? 0;

        int lodBias = ReadIntProperty(tex.Properties, "LODBias");
        string lodGroup = ReadEnumProperty(tex.Properties, "LODGroup");

        long estimatedBytes = EstimateSize(sizeX, sizeY, pixelFormat);

        if (estimatedBytes > config.LargeSizeWarningBytes)
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "PKS007",
                $"大尺寸纹理：{tex.Name} ({sizeX}x{sizeY} {pixelFormat}, 估算 {estimatedBytes / 1024 / 1024} MB)",
                objectPath));
        }

        return new PakTextureEntry(
            tex.Name,
            objectPath,
            sizeX,
            sizeY,
            pixelFormat,
            lodBias,
            lodGroup,
            numMips,
            estimatedBytes);
    }

    private static int ReadIntProperty(List<FPropertyTag> properties, string name)
    {
        var tag = properties.FirstOrDefault(p => p.Name.Text == name);
        if (tag?.Tag is FPropertyTagType<int> intTag)
            return intTag.Value;
        return 0;
    }

    private static string ReadEnumProperty(List<FPropertyTag> properties, string name)
    {
        var tag = properties.FirstOrDefault(p => p.Name.Text == name);
        if (tag?.Tag is null) return "TEXTUREGROUP_World";

        var raw = tag.Tag.GetValue<string>()
            ?? tag.Tag.GenericValue?.ToString()
            ?? "TEXTUREGROUP_World";

        // strip enum class prefix e.g. "TextureGroup::TEXTUREGROUP_World" -> "TEXTUREGROUP_World"
        var colonIdx = raw.LastIndexOf(':');
        return colonIdx >= 0 ? raw[(colonIdx + 1)..] : raw;
    }

    // Rough bytes estimate: Mip0 texels × bits-per-pixel ÷ 8
    // Block-compressed formats: texels rounded up to 4×4 blocks
    private static long EstimateSize(int width, int height, string pixelFormat)
    {
        if (width <= 0 || height <= 0) return 0;

        return pixelFormat switch
        {
            // BC1 / DXT1: 4 bpp
            "PF_DXT1" or "PF_BC1" => (long)Math.Ceiling(width / 4.0) * (long)Math.Ceiling(height / 4.0) * 8,
            // BC2 / DXT3: 8 bpp
            "PF_DXT3" or "PF_BC2" => (long)Math.Ceiling(width / 4.0) * (long)Math.Ceiling(height / 4.0) * 16,
            // BC3 / DXT5: 8 bpp
            "PF_DXT5" or "PF_BC3" => (long)Math.Ceiling(width / 4.0) * (long)Math.Ceiling(height / 4.0) * 16,
            // BC4: 4 bpp
            "PF_BC4" => (long)Math.Ceiling(width / 4.0) * (long)Math.Ceiling(height / 4.0) * 8,
            // BC5: 8 bpp
            "PF_BC5" => (long)Math.Ceiling(width / 4.0) * (long)Math.Ceiling(height / 4.0) * 16,
            // BC6H: 8 bpp
            "PF_BC6H" => (long)Math.Ceiling(width / 4.0) * (long)Math.Ceiling(height / 4.0) * 16,
            // BC7: 8 bpp
            "PF_BC7" => (long)Math.Ceiling(width / 4.0) * (long)Math.Ceiling(height / 4.0) * 16,
            // ASTC 4×4: 8 bpp
            "PF_ASTC_4x4" => (long)Math.Ceiling(width / 4.0) * (long)Math.Ceiling(height / 4.0) * 16,
            // ASTC 6×6: ~3.6 bpp, approx 16 bytes per block
            "PF_ASTC_6x6" => (long)Math.Ceiling(width / 6.0) * (long)Math.Ceiling(height / 6.0) * 16,
            // ASTC 8×8: 2 bpp
            "PF_ASTC_8x8" => (long)Math.Ceiling(width / 8.0) * (long)Math.Ceiling(height / 8.0) * 16,
            // ASTC 10×10: ~1.28 bpp
            "PF_ASTC_10x10" => (long)Math.Ceiling(width / 10.0) * (long)Math.Ceiling(height / 10.0) * 16,
            // ASTC 12×12: ~0.89 bpp
            "PF_ASTC_12x12" => (long)Math.Ceiling(width / 12.0) * (long)Math.Ceiling(height / 12.0) * 16,
            // Uncompressed 8-bit per channel
            "PF_G8" => (long)width * height,
            "PF_R8" => (long)width * height,
            "PF_R8G8" => (long)width * height * 2,
            "PF_B8G8R8A8" or "PF_R8G8B8A8" => (long)width * height * 4,
            // 16-bit half float
            "PF_R16F" => (long)width * height * 2,
            "PF_G16R16F" => (long)width * height * 4,
            "PF_FloatRGBA" or "PF_A16B16G16R16" => (long)width * height * 8,
            // 32-bit float
            "PF_R32_FLOAT" => (long)width * height * 4,
            "PF_A32B32G32R32F" => (long)width * height * 16,
            // Default: assume 4 bytes per texel
            _ => (long)width * height * 4,
        };
    }

    private static EGame ResolveGameVersion(string version)
    {
        if (GameVersionMap.TryGetValue(version, out var game))
            return game;
        return EGame.GAME_UE5_3;
    }
}
