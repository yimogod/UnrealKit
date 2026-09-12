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
using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Formats.Meshes;
using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Textures;
using UnrealKit.Core.Diagnostics;
using UnrealKit.Core.Operations;

namespace UnrealKit.Core.PakScan;

public sealed class PakScanService : IPakScanService
{
    private DefaultFileProvider? _provider;

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

    public async IAsyncEnumerable<PakScanEntry> ScanStreamAsync(
        string pakDirectory,
        PakScanConfig? config = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _provider = null;
        config ??= PakScanConfig.Default;

        if (!Directory.Exists(pakDirectory))
        {
            yield return new PakScanDiagnosticEntry(new Diagnostic(DiagnosticSeverity.Error, "PKS001",
                $"Pak 目录不存在：{pakDirectory}", pakDirectory));
            yield break;
        }

        var hasPakFiles = Directory.EnumerateFiles(pakDirectory, "*.pak", SearchOption.AllDirectories).Any()
            || Directory.EnumerateFiles(pakDirectory, "*.utoc", SearchOption.AllDirectories).Any()
            || Directory.EnumerateFiles(pakDirectory, "*.ucas", SearchOption.AllDirectories).Any();

        if (!hasPakFiles)
        {
            yield return new PakScanDiagnosticEntry(new Diagnostic(DiagnosticSeverity.Error, "PKS002",
                $"目录中未找到任何 .pak / .utoc / .ucas 文件：{pakDirectory}", pakDirectory,
                "请确认路径指向包含游戏包文件的目录"));
            yield break;
        }

        var game = ResolveGameVersion(config.GameVersion);

        if (!string.IsNullOrWhiteSpace(config.OodleDllPath))
        {
            Diagnostic? oodleDiag = null;
            if (!File.Exists(config.OodleDllPath))
            {
                oodleDiag = new Diagnostic(DiagnosticSeverity.Warning, "PKS007",
                    $"指定的 Oodle DLL 不存在，Oodle 压缩资产将无法解压：{config.OodleDllPath}",
                    SuggestedFix: "请确认路径指向 oo2core_9_win64.dll 或同类文件");
            }
            else
            {
                try { OodleHelper.Initialize(config.OodleDllPath); }
                catch (Exception ex)
                {
                    oodleDiag = new Diagnostic(DiagnosticSeverity.Warning, "PKS007",
                        $"Oodle DLL 加载失败，Oodle 压缩资产将无法解压：{ex.Message}");
                }
            }
            if (oodleDiag is not null) yield return new PakScanDiagnosticEntry(oodleDiag);
        }

        var provider = new DefaultFileProvider(
            pakDirectory,
            SearchOption.AllDirectories,
            new VersionContainer(game),
            StringComparer.OrdinalIgnoreCase);

        provider.Initialize();
        _provider = provider;

        if (!string.IsNullOrWhiteSpace(config.AesKey))
        {
            Diagnostic? aesDiag = null;
            try
            {
                var key = new FAesKey(config.AesKey);
                await provider.SubmitKeyAsync(new FGuid(), key);
            }
            catch (Exception ex)
            {
                aesDiag = new Diagnostic(DiagnosticSeverity.Warning, "PKS004",
                    $"AES 密钥提交失败，加密资产将无法读取：{ex.Message}",
                    SuggestedFix: "请确认密钥格式为十六进制字符串，例如 0x1A2B3C4D...");
            }
            if (aesDiag is not null) yield return new PakScanDiagnosticEntry(aesDiag);
        }

        await provider.MountAsync();

        var allPaths = provider.Files.Keys
            .Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .Where(p => !config.ExcludeEnginePaths || !p.Contains("/Engine/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var totalCount = allPaths.Count;
        var scannedCount = 0;
        var textureCount = 0;
        var staticMeshCount = 0;
        var skeletalMeshCount = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        yield return new PakScanStartEntry(totalCount);

        foreach (var path in allPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scannedCount++;

            // yield 不能出现在 try/catch 内，用局部列表暂存本次迭代产生的条目，在外部 yield
            var pendingEntries = new List<PakScanEntry>();

            try
            {
                var objectPath = path[..^".uasset".Length];

                // 先读 package header 拿类名，只对目标类型做完整反序列化；
                // IoPackage 或失败时 className 为 null，退回盲试。
                var className = GetExportClassName(provider, path);
                if (className == "Texture2D")
                {
                    if (provider.TryLoadPackageObject<UTexture2D>(objectPath, out var tex) && tex is not null)
                    {
                        var texEntry = BuildTextureEntry(tex, objectPath, config, out var diag);
                        if (diag is not null) pendingEntries.Add(new PakScanDiagnosticEntry(diag));
                        pendingEntries.Add(new PakScanTextureFound(texEntry));
                        textureCount++;
                    }
                }
                else if (className == "SkeletalMesh")
                {
                    // SkeletalMesh 先于 StaticMesh，因为 SkeletalMesh 继承自 StaticMesh
                    if (provider.TryLoadPackageObject<USkeletalMesh>(objectPath, out var skm) && skm is not null)
                    {
                        pendingEntries.Add(new PakScanSkeletalMeshFound(BuildSkeletalMeshEntry(skm, objectPath)));
                        skeletalMeshCount++;
                    }
                }
                else if (className == "StaticMesh")
                {
                    if (provider.TryLoadPackageObject<UStaticMesh>(objectPath, out var sm) && sm is not null)
                    {
                        pendingEntries.Add(new PakScanStaticMeshFound(BuildStaticMeshEntry(sm, objectPath)));
                        staticMeshCount++;
                    }
                }
                else if (className is null)
                {
                    // IoPackage 或 header 解析失败，退回盲试；SkeletalMesh 仍先于 StaticMesh
                    if (provider.TryLoadPackageObject<UTexture2D>(objectPath, out var tex) && tex is not null)
                    {
                        var texEntry = BuildTextureEntry(tex, objectPath, config, out var diag);
                        if (diag is not null) pendingEntries.Add(new PakScanDiagnosticEntry(diag));
                        pendingEntries.Add(new PakScanTextureFound(texEntry));
                        textureCount++;
                    }
                    else if (provider.TryLoadPackageObject<USkeletalMesh>(objectPath, out var skm) && skm is not null)
                    {
                        pendingEntries.Add(new PakScanSkeletalMeshFound(BuildSkeletalMeshEntry(skm, objectPath)));
                        skeletalMeshCount++;
                    }
                    else if (provider.TryLoadPackageObject<UStaticMesh>(objectPath, out var sm) && sm is not null)
                    {
                        pendingEntries.Add(new PakScanStaticMeshFound(BuildStaticMeshEntry(sm, objectPath)));
                        staticMeshCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                pendingEntries.Add(new PakScanDiagnosticEntry(new Diagnostic(DiagnosticSeverity.Warning, "PKS005",
                    $"资产反序列化失败，已跳过：{path} — {ex.Message}", path)));
            }

            foreach (var e in pendingEntries)
                yield return e;

            yield return new PakScanProgressEntry(scannedCount, totalCount, path);

            // 每个资产处理完后让出一次，保证取消令牌和进度回调能及时响应
            await Task.Yield();
        }

        sw.Stop();
        yield return new PakScanDiagnosticEntry(new Diagnostic(DiagnosticSeverity.Information, "PKS006",
            $"扫描完成：{textureCount} 个 Texture2D，{staticMeshCount} 个 StaticMesh，{skeletalMeshCount} 个 SkeletalMesh，共扫描 {scannedCount} 个资产，耗时 {FormatElapsed(sw.Elapsed)}"));

        yield return new PakScanCompleteEntry(scannedCount, textureCount, staticMeshCount, skeletalMeshCount, sw.Elapsed);
    }

    public async Task<PakScanResult> ScanAsync(
        string pakDirectory,
        PakScanConfig? config = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<Diagnostic>();
        var textures       = new List<PakTextureEntry>();
        var staticMeshes   = new List<PakMeshEntry>();
        var skeletalMeshes = new List<PakMeshEntry>();
        int totalCount = 0;

        await foreach (var entry in ScanStreamAsync(pakDirectory, config, cancellationToken))
        {
            switch (entry)
            {
                case PakScanStartEntry s:
                    totalCount = s.TotalAssets;
                    progress?.Report(new OperationProgress("pakScan", "Scan", 0, totalCount,
                        $"开始扫描，共 {totalCount} 个资产…"));
                    break;

                case PakScanProgressEntry p:
                    progress?.Report(new OperationProgress("pakScan", "Scan", p.Scanned, p.Total,
                        $"扫描中… {p.Scanned}/{p.Total}  {p.CurrentAsset}"));
                    break;

                case PakScanTextureFound t:
                    textures.Add(t.Texture);
                    break;

                case PakScanStaticMeshFound sm:
                    staticMeshes.Add(sm.Mesh);
                    break;

                case PakScanSkeletalMeshFound skm:
                    skeletalMeshes.Add(skm.Mesh);
                    break;

                case PakScanDiagnosticEntry d:
                    diagnostics.Add(d.Diagnostic);
                    break;

                case PakScanCompleteEntry c:
                    progress?.Report(new OperationProgress("pakScan", "Done", c.TotalScanned, c.TotalScanned,
                        $"扫描完成：{c.TextureCount} 个纹理，{c.StaticMeshCount} 个 StaticMesh，{c.SkeletalMeshCount} 个 SkeletalMesh，共 {c.TotalScanned} 个资产，耗时 {FormatElapsed(c.Elapsed)}"));
                    break;
            }
        }

        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return new PakScanResult(pakDirectory, null, diagnostics);

        var report = new PakScanReport(pakDirectory, textures.Count + staticMeshes.Count + skeletalMeshes.Count,
            textures.Count, textures, staticMeshes.Count, staticMeshes, skeletalMeshes.Count, skeletalMeshes);
        return new PakScanResult(pakDirectory, report, diagnostics);
    }

    public Task<byte[]?> DecodeTexturePngAsync(string objectPath, CancellationToken cancellationToken = default)
    {
        var provider = _provider;
        if (provider is null) return Task.FromResult<byte[]?>(null);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!provider.TryLoadPackageObject<UTexture2D>(objectPath, out var tex) || tex is null)
                return null;
            cancellationToken.ThrowIfCancellationRequested();
            var ctex = tex.Decode();
            if (ctex is null) return null;
            return ctex.Encode(ETextureFormat.Png, saveHdrAsHdr: false, out _);
        }, cancellationToken);
    }

    public Task<byte[]?> ExportMeshGlbAsync(string objectPath, CancellationToken cancellationToken = default)
    {
        var provider = _provider;
        if (provider is null) return Task.FromResult<byte[]?>(null);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = new ExportOptions(EMeshFormat.Gltf2, naniteMeshFormat: ENaniteMeshFormat.NoNanite, meshQuality: EMeshQuality.Highest);

            if (provider.TryLoadPackageObject<UStaticMesh>(objectPath, out var sm) && sm is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var dto = new StaticMeshDto(sm, options.MeshQuality, options.NaniteMeshFormat);
                if (dto.LODs.Count == 0) return null;
                var files = new GltfMeshFormat().BuildStaticMesh(sm.Name, objectPath, options, dto);
                return files.FirstOrDefault(f => f.Extension == "glb").Data;
            }

            if (provider.TryLoadPackageObject<USkeletalMesh>(objectPath, out var skm) && skm is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var dto = new SkeletalMeshDto(skm, options.MeshQuality, options.NaniteMeshFormat, exportMorphTarget: false);
                if (dto.LODs.Count == 0) return null;
                var files = new GltfMeshFormat().BuildSkeletalMesh(skm.Name, objectPath, options, dto);
                return files.FirstOrDefault(f => f.Extension == "glb").Data;
            }

            return null;
        }, cancellationToken);
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalMinutes >= 1
            ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s"
            : $"{elapsed.TotalSeconds:F1}s";

    private static PakTextureEntry BuildTextureEntry(
        UTexture2D tex,
        string objectPath,
        PakScanConfig config,
        out Diagnostic? largeSizeWarning)
    {
        var platData = tex.PlatformData;
        int sizeX = platData?.SizeX ?? (int)tex.ImportedSize.X;
        int sizeY = platData?.SizeY ?? (int)tex.ImportedSize.Y;
        string pixelFormat = platData?.PixelFormat ?? "Unknown";
        int numMips = platData?.Mips?.Length ?? 0;

        int lodBias = ReadIntProperty(tex.Properties, "LODBias");
        string lodGroup = ReadEnumProperty(tex.Properties, "LODGroup");

        long estimatedBytes = EstimateSize(sizeX, sizeY, pixelFormat);

        largeSizeWarning = estimatedBytes > config.LargeSizeWarningBytes
            ? new Diagnostic(DiagnosticSeverity.Warning, "PKS007",
                $"大尺寸纹理：{tex.Name} ({sizeX}x{sizeY} {pixelFormat}, 估算 {estimatedBytes / 1024 / 1024} MB)",
                objectPath)
            : null;

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
        var lod0 = sm.RenderData?.LODs?.Length > 0 ? sm.RenderData.LODs[0] : null;
        int vertexCount = lod0?.NumVertices ?? 0;
        int triangleCount = (lod0?.IndexBuffer?.Buffer?.Length ?? 0) / 3;
        return new PakMeshEntry(sm.Name, objectPath, PakMeshKind.StaticMesh, lodCount, materialCount, 0, vertexCount, triangleCount);
    }

    private static PakMeshEntry BuildSkeletalMeshEntry(USkeletalMesh skm, string objectPath)
    {
        int lodCount = skm.LODModels?.Length ?? 0;
        int materialCount = skm.SkeletalMaterials?.Length ?? skm.Materials?.Length ?? 0;
        int boneCount = skm.ReferenceSkeleton?.FinalRefBoneInfo?.Length ?? 0;
        var lod0 = skm.LODModels?.Length > 0 ? skm.LODModels[0] : null;
        int vertexCount = lod0?.NumVertices ?? 0;
        int triangleCount = lod0?.Sections?.Sum(s => (int)s.NumTriangles) ?? 0;
        return new PakMeshEntry(skm.Name, objectPath, PakMeshKind.SkeletalMesh, lodCount, materialCount, boneCount, vertexCount, triangleCount);
    }

    // 读 package header 拿第一个 export 的类名，不触发 export 内容反序列化。
    // 对于 IoPackage（.utoc/.ucas）无法轻量读取类名，返回 null 退回全量路径。
    private static string? GetExportClassName(DefaultFileProvider provider, string path)
    {
        try
        {
            if (!provider.TryLoadPackage(path, out var pkg) || pkg is null)
                return null;

            if (pkg is CUE4Parse.UE4.Assets.Package uassetPkg)
            {
                if (uassetPkg.ExportMap.Length == 0) return null;
                var classIndex = uassetPkg.ExportMap[0].ClassIndex;
                if (classIndex.IsImport)
                {
                    var importIdx = -classIndex.Index - 1;
                    if (importIdx < uassetPkg.ImportMap.Length)
                        return uassetPkg.ImportMap[importIdx].ClassName.Text;
                }
                return null;
            }

            // IoPackage：无法轻量读取类名，返回 null
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static EGame ResolveGameVersion(string version)
    {
        if (GameVersionMap.TryGetValue(version, out var game))
            return game;
        return EGame.GAME_UE5_3;
    }
}
