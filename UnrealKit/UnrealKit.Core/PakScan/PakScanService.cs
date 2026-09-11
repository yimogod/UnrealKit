using System.Collections.Concurrent;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
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

        if (!string.IsNullOrWhiteSpace(config.OodleDllPath))
        {
            if (!File.Exists(config.OodleDllPath))
            {
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "PKS007",
                    $"指定的 Oodle DLL 不存在，Oodle 压缩资产将无法解压：{config.OodleDllPath}",
                    SuggestedFix: "请确认路径指向 oo2core_9_win64.dll 或同类文件"));
            }
            else
            {
                try { OodleHelper.Initialize(config.OodleDllPath); }
                catch (Exception ex)
                {
                    diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "PKS007",
                        $"Oodle DLL 加载失败，Oodle 压缩资产将无法解压：{ex.Message}"));
                }
            }
        }

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

        await provider.MountAsync();

        var allPaths = provider.Files.Keys
            .Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .Where(p => !config.ExcludeEnginePaths || !p.Contains("/Engine/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var totalCount = allPaths.Count;
        var scannedCount = 0;
        var textures = new ConcurrentBag<PakTextureEntry>();
        var meshes = new ConcurrentBag<PakMeshEntry>();
        var concurrentDiags = new ConcurrentBag<Diagnostic>();

        progress?.Report(new OperationProgress("pakScan", "Scan", 0, totalCount, $"开始扫描，共 {totalCount} 个资产…"));

        var parallelism = config.MaxDegreeOfParallelism > 0
            ? config.MaxDegreeOfParallelism
            : Math.Max(1, Environment.ProcessorCount / 2);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = parallelism,
            CancellationToken = cancellationToken,
        };

        // 进度节流：每 200ms 上报一次，避免 UI 线程被高频回调淹没（几万资产并发上报会卡界面）
        long lastReportTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        void ReportProgress(int current, string currentPath)
        {
            if (progress is null) return;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var last = Interlocked.Read(ref lastReportTick);
            if (now - last < 200) return;
            if (Interlocked.CompareExchange(ref lastReportTick, now, last) != last) return;
            progress.Report(new OperationProgress("pakScan", "Scan", current, totalCount,
                $"[{current}/{totalCount}] {currentPath}"));
        }

        // FileProviderDictionary 内部全用 ConcurrentDictionary，LoadPackage 每次新建独立 Package，
        // 没有共享可变状态，TryLoadPackageObject 可以安全并发调用。
        await Parallel.ForEachAsync(allPaths, parallelOptions, (path, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var objectPath = path[..^".uasset".Length];

                if (provider.TryLoadPackageObject<UTexture2D>(objectPath, out var tex) && tex is not null)
                {
                    var localDiags = new List<Diagnostic>();
                    textures.Add(BuildTextureEntry(tex, objectPath, config, localDiags));
                    foreach (var d in localDiags)
                        concurrentDiags.Add(d);
                }
                else if (provider.TryLoadPackageObject<UStaticMesh>(objectPath, out var sm) && sm is not null)
                {
                    meshes.Add(BuildStaticMeshEntry(sm, objectPath));
                }
                else if (provider.TryLoadPackageObject<USkeletalMesh>(objectPath, out var skm) && skm is not null)
                {
                    meshes.Add(BuildSkeletalMeshEntry(skm, objectPath));
                }
            }
            catch (Exception ex)
            {
                concurrentDiags.Add(new Diagnostic(DiagnosticSeverity.Warning, "PKS005",
                    $"资产反序列化失败，已跳过：{path} — {ex.Message}", path));
            }

            var current = Interlocked.Increment(ref scannedCount);
            ReportProgress(current, path);
            return ValueTask.CompletedTask;
        });

        foreach (var d in concurrentDiags)
            diagnostics.Add(d);

        var textureList = textures.ToList();
        var meshList = meshes.ToList();
        var staticMeshCount = meshList.Count(m => m.Kind == PakMeshKind.StaticMesh);
        var skeletalMeshCount = meshList.Count(m => m.Kind == PakMeshKind.SkeletalMesh);

        progress?.Report(new OperationProgress("pakScan", "Done", totalCount, totalCount,
            $"扫描完成：{textureList.Count} 个纹理，{staticMeshCount} 个 StaticMesh，{skeletalMeshCount} 个 SkeletalMesh，共 {scannedCount} 个资产"));

        diagnostics.Add(new Diagnostic(DiagnosticSeverity.Information, "PKS006",
            $"扫描完成：{textureList.Count} 个 Texture2D，{staticMeshCount} 个 StaticMesh，{skeletalMeshCount} 个 SkeletalMesh，共扫描 {scannedCount} 个资产"));

        var report = new PakScanReport(pakDirectory, scannedCount, textureList.Count, textureList,
            staticMeshCount, skeletalMeshCount, meshList);
        return new PakScanResult(pakDirectory, report, diagnostics);
    }

    private static PakTextureEntry BuildTextureEntry(
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

    private static PakMeshEntry BuildStaticMeshEntry(UStaticMesh sm, string objectPath)
    {
        int lodCount = sm.RenderData?.LODs?.Length ?? 0;
        int materialCount = sm.StaticMaterials?.Length ?? sm.Materials?.Length ?? 0;
        return new PakMeshEntry(sm.Name, objectPath, PakMeshKind.StaticMesh, lodCount, materialCount, 0);
    }

    private static PakMeshEntry BuildSkeletalMeshEntry(USkeletalMesh skm, string objectPath)
    {
        int lodCount = skm.LODModels?.Length ?? 0;
        int materialCount = skm.SkeletalMaterials?.Length ?? skm.Materials?.Length ?? 0;
        int boneCount = skm.ReferenceSkeleton?.FinalRefBoneInfo?.Length ?? 0;
        return new PakMeshEntry(skm.Name, objectPath, PakMeshKind.SkeletalMesh, lodCount, materialCount, boneCount);
    }

    private static EGame ResolveGameVersion(string version)
    {
        if (GameVersionMap.TryGetValue(version, out var game))
            return game;
        return EGame.GAME_UE5_3;
    }
}
