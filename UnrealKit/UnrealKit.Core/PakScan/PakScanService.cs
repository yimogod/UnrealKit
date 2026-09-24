using System.Collections.Concurrent;
using System.Threading.Channels;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Exports.Component.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.WorldPartition;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
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

        // 快速校验在 UI 线程做（纯内存/目录检查，不阻塞）
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

        // 用无界 Channel 把扫描结果从后台线程传回迭代器（UI 线程消费）
        var channel = Channel.CreateUnbounded<PakScanEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        // 后台任务：Initialize / MountAsync / 逐资产解析，全部在线程池执行
        var scanTask = Task.Run(async () =>
        {
            try
            {
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
                    if (oodleDiag is not null)
                        await channel.Writer.WriteAsync(new PakScanDiagnosticEntry(oodleDiag), cancellationToken);
                }

                var provider = new DefaultFileProvider(
                    pakDirectory,
                    SearchOption.AllDirectories,
                    new VersionContainer(game),
                    StringComparer.OrdinalIgnoreCase);

                // Initialize 是同步 CPU 密集操作，在线程池上跑
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
                    if (aesDiag is not null)
                        await channel.Writer.WriteAsync(new PakScanDiagnosticEntry(aesDiag), cancellationToken);
                }

                await provider.MountAsync();
                provider.PostMount();

                // 无条件应用项目 DefaultGame.ini 的显式覆盖，优先级高于 pak 内 DefaultEngine.ini
                foreach (var kv in config.VersionOverrides)
                    provider.Versions[kv.Key] = kv.Value;

                var allPaths = provider.Files.Keys
                    .Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                    .Where(p => !config.ExcludeEnginePaths || !p.Contains("/Engine/", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var totalCount = allPaths.Count;
                var scannedCount = 0;
                var textureCount = 0;
                var staticMeshCount = 0;
                var skeletalMeshCount = 0;
                var materialCount = 0;
                var materialInstanceCount = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                await channel.Writer.WriteAsync(new PakScanStartEntry(totalCount), cancellationToken);

                Dictionary<string, string> TempDict = new Dictionary<string, string>();
                var textureUsage = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                foreach (var path in allPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    scannedCount++;

                    try
                    {
                        var objectPath = path[..^".uasset".Length];
                        var chunkId = ExtractPakChunkId(provider, path);

                        // 先读 package header 拿类名，只对目标类型做完整反序列化；
                        // IoPackage 或失败时 className 为 null，退回盲试。
                        var className = GetExportClassName(provider, path);
                        if(className == null)TempDict.Add(path, "Null");
                        else TempDict.Add(path, className);
                        

                        if (className == "Texture2D")
                        {
                            if (provider.TryLoadPackageObject<UTexture2D>(objectPath, out var tex) && tex is not null)
                            {
                                var texEntry = BuildTextureEntry(tex, objectPath, config, chunkId, out var diag);
                                if (diag is not null)
                                    await channel.Writer.WriteAsync(new PakScanDiagnosticEntry(diag), cancellationToken);
                                await channel.Writer.WriteAsync(new PakScanTextureFound(texEntry), cancellationToken);
                                textureCount++;
                            }
                        }
                        else if (className == "SkeletalMesh")
                        {
                            // SkeletalMesh 先于 StaticMesh，因为 SkeletalMesh 继承自 StaticMesh
                            if (provider.TryLoadPackageObject<USkeletalMesh>(objectPath, out var skm) && skm is not null)
                            {
                                await channel.Writer.WriteAsync(new PakScanSkeletalMeshFound(BuildSkeletalMeshEntry(skm, objectPath, chunkId)), cancellationToken);
                                skeletalMeshCount++;
                            }
                        }
                        else if (className == "StaticMesh")
                        {
                            if (provider.TryLoadPackageObject<UStaticMesh>(objectPath, out var sm) && sm is not null)
                            {
                                await channel.Writer.WriteAsync(new PakScanStaticMeshFound(BuildStaticMeshEntry(sm, objectPath, chunkId)), cancellationToken);
                                staticMeshCount++;
                            }
                        }
                        else if (className == "Material")
                        {
                            if (provider.TryLoadPackageObject<UMaterial>(objectPath, out var mat) && mat is not null)
                            {
                                await channel.Writer.WriteAsync(new PakScanMaterialFound(BuildMaterialEntry(mat, objectPath, chunkId)), cancellationToken);
                                AccumulateTextureUsage(provider, mat, textureUsage);
                                materialCount++;
                            }
                        }
                        else if (className == "MaterialInstanceConstant")
                        {
                            if (provider.TryLoadPackageObject<UMaterialInstanceConstant>(objectPath, out var mi) && mi is not null)
                            {
                                await channel.Writer.WriteAsync(new PakScanMaterialInstanceFound(BuildMaterialInstanceEntry(mi, objectPath, chunkId)), cancellationToken);
                                AccumulateMaterialInstanceTextureUsage(provider, mi, textureUsage);
                                materialInstanceCount++;
                            }
                        }
                        else if (className is null)
                        {
                            // IoPackage 或 header 解析失败，退回盲试；SkeletalMesh 仍先于 StaticMesh
                            if (provider.TryLoadPackageObject<UTexture2D>(objectPath, out var tex) && tex is not null)
                            {
                                var texEntry = BuildTextureEntry(tex, objectPath, config, chunkId, out var diag);
                                if (diag is not null)
                                    await channel.Writer.WriteAsync(new PakScanDiagnosticEntry(diag), cancellationToken);
                                await channel.Writer.WriteAsync(new PakScanTextureFound(texEntry), cancellationToken);
                                textureCount++;
                            }
                            else if (provider.TryLoadPackageObject<USkeletalMesh>(objectPath, out var skm) && skm is not null)
                            {
                                await channel.Writer.WriteAsync(new PakScanSkeletalMeshFound(BuildSkeletalMeshEntry(skm, objectPath, chunkId)), cancellationToken);
                                skeletalMeshCount++;
                            }
                            else if (provider.TryLoadPackageObject<UStaticMesh>(objectPath, out var sm) && sm is not null)
                            {
                                await channel.Writer.WriteAsync(new PakScanStaticMeshFound(BuildStaticMeshEntry(sm, objectPath, chunkId)), cancellationToken);
                                staticMeshCount++;
                            }
                            else if (provider.TryLoadPackageObject<UMaterial>(objectPath, out var mat) && mat is not null)
                            {
                                await channel.Writer.WriteAsync(new PakScanMaterialFound(BuildMaterialEntry(mat, objectPath, chunkId)), cancellationToken);
                                AccumulateTextureUsage(provider, mat, textureUsage);
                                materialCount++;
                            }
                            else if (provider.TryLoadPackageObject<UMaterialInstanceConstant>(objectPath, out var mi) && mi is not null)
                            {
                                await channel.Writer.WriteAsync(new PakScanMaterialInstanceFound(BuildMaterialInstanceEntry(mi, objectPath, chunkId)), cancellationToken);
                                AccumulateMaterialInstanceTextureUsage(provider, mi, textureUsage);
                                materialInstanceCount++;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        await channel.Writer.WriteAsync(new PakScanDiagnosticEntry(new Diagnostic(DiagnosticSeverity.Warning, "PKS005",
                            $"资产反序列化失败，已跳过：{path} — {ex.Message}", path)), cancellationToken);
                    }

                    await channel.Writer.WriteAsync(new PakScanProgressEntry(scannedCount, totalCount, path), cancellationToken);
                }

                sw.Stop();

                await channel.Writer.WriteAsync(new PakScanTextureUsageReadyEntry(textureUsage), cancellationToken);

                await channel.Writer.WriteAsync(new PakScanDiagnosticEntry(new Diagnostic(DiagnosticSeverity.Information, "PKS006",
                    $"扫描完成：{textureCount} 个 Texture2D，{staticMeshCount} 个 StaticMesh，{skeletalMeshCount} 个 SkeletalMesh，{materialCount} 个 Material，{materialInstanceCount} 个 MaterialInstance，共扫描 {scannedCount} 个资产，耗时 {FormatElapsed(sw.Elapsed)}")), cancellationToken);

                await channel.Writer.WriteAsync(new PakScanCompleteEntry(scannedCount, textureCount, staticMeshCount, skeletalMeshCount, materialCount, materialInstanceCount, sw.Elapsed), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 取消时正常退出，不写入错误
            }
            catch (Exception ex)
            {
                await channel.Writer.WriteAsync(new PakScanDiagnosticEntry(new Diagnostic(DiagnosticSeverity.Error, "PKS008",
                    $"扫描过程中发生未预期错误：{ex.Message}")), CancellationToken.None);
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, cancellationToken);

        // UI 线程侧：从 Channel 读取结果并逐条 yield
        await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken))
            yield return entry;

        // 等待后台任务结束，传播非取消异常
        await scanTask;
    }

    public async Task<PakScanResult> ScanAsync(
        string pakDirectory,
        PakScanConfig? config = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<Diagnostic>();
        var textures           = new List<PakTextureEntry>();
        var staticMeshes       = new List<PakMeshEntry>();
        var skeletalMeshes     = new List<PakMeshEntry>();
        var materials          = new List<PakMaterialEntry>();
        var materialInstances  = new List<PakMaterialInstanceEntry>();
        IReadOnlyDictionary<string, List<string>> textureUsage = new Dictionary<string, List<string>>();
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

                case PakScanMaterialFound mat:
                    materials.Add(mat.Material);
                    break;

                case PakScanMaterialInstanceFound mi:
                    materialInstances.Add(mi.MaterialInstance);
                    break;

                case PakScanTextureUsageReadyEntry u:
                    textureUsage = u.Usage;
                    break;

                case PakScanDiagnosticEntry d:
                    diagnostics.Add(d.Diagnostic);
                    break;

                case PakScanCompleteEntry c:
                    progress?.Report(new OperationProgress("pakScan", "Done", c.TotalScanned, c.TotalScanned,
                        $"扫描完成：{c.TextureCount} 个纹理，{c.StaticMeshCount} 个 StaticMesh，{c.SkeletalMeshCount} 个 SkeletalMesh，{c.MaterialCount} 个 Material，{c.MaterialInstanceCount} 个 MaterialInstance，共 {c.TotalScanned} 个资产，耗时 {FormatElapsed(c.Elapsed)}"));
                    break;
            }
        }

        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return new PakScanResult(pakDirectory, null, diagnostics);

        textures = textures.Select(t => t with
        {
            UsedByMaterialNames = textureUsage.TryGetValue(t.ObjectPath, out var names)
                ? names
                : []
        }).ToList();

        var totalAssets = textures.Count + staticMeshes.Count + skeletalMeshes.Count + materials.Count + materialInstances.Count;
        var report = new PakScanReport(pakDirectory, totalAssets,
            textures.Count, textures, staticMeshes.Count, staticMeshes, skeletalMeshes.Count, skeletalMeshes,
            materials.Count, materials, materialInstances.Count, materialInstances);
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
                if (dto == null || dto.LODs.Count == 0) return null;
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

    // "pakchunk5-Android_ASTCClient.pak" → "5"；无法识别时返回空字符串。
    private static string ExtractPakChunkId(DefaultFileProvider provider, string assetPath)
    {
        if (!provider.Files.TryGetValue(assetPath, out var gameFile))
            return string.Empty;
        if (gameFile is not CUE4Parse.UE4.VirtualFileSystem.VfsEntry vfsEntry)
            return string.Empty;

        var vfsName = vfsEntry.Vfs.Name; // e.g. "pakchunk5-Android_ASTCClient.pak"
        var fileName = System.IO.Path.GetFileNameWithoutExtension(vfsName); // "pakchunk5-Android_ASTCClient"
        var firstSegment = fileName.Split('-')[0]; // "pakchunk5"
        const string prefix = "pakchunk";
        if (firstSegment.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return firstSegment[prefix.Length..]; // "5"
        return string.Empty;
    }

    private static PakTextureEntry BuildTextureEntry(
        UTexture2D tex,
        string objectPath,
        PakScanConfig config,
        string pakChunkId,
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
            estimatedBytes,
            pakChunkId,
            []);
    }

    private static void AccumulateTextureUsage(DefaultFileProvider provider, UMaterial mat, Dictionary<string, List<string>> textureUsage)
    {
        foreach (var tex in mat.ReferencedTextures)
        {
            // tex.Owner.Name 是 UE 虚拟路径（/Game/... 或 /Engine/...），需要 FixPath 转换成
            // 与扫描主循环里 objectPath 同源的挂载相对路径（XGame/Content/... 等），否则字典查找永远不命中。
            var ownerName = tex?.Owner?.Name.ToString();
            if (string.IsNullOrEmpty(ownerName)) continue; // Owner 未解析，跳过而不报错
            var fixedPath = provider.FixPath(ownerName);
            var texPath = fixedPath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                ? fixedPath[..^".uasset".Length]
                : fixedPath;
            if (!textureUsage.TryGetValue(texPath, out var list))
                textureUsage[texPath] = list = [];
            if (!list.Contains(mat.Name))
                list.Add(mat.Name);
        }
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

    private static PakMeshEntry BuildStaticMeshEntry(UStaticMesh sm, string objectPath, string pakChunkId)
    {
        // Win64 Nanite IoPackage: RenderData is null because geometry is stored in Nanite-private
        // bulk format that CUE4Parse cannot decode. Return -1 as "N/A" sentinel for geometry fields.
        bool hasRenderData = sm.RenderData?.LODs?.Length > 0;
        int lodCount = hasRenderData ? sm.RenderData!.LODs!.Length : -1;

        // Win64 IoPackage: sm.StaticMaterials typed field stays empty even when the property tag is
        // present; TryGetValue reads directly from the serialized property bag and works for both.
        int materialCount = sm.StaticMaterials?.Length > 0
            ? sm.StaticMaterials.Length
            : sm.Materials?.Length > 0
                ? sm.Materials.Length
                : (sm.TryGetValue<FStaticMaterial[]>(out var rawMats, "StaticMaterials") ? rawMats?.Length ?? 0 : 0);

        if (!hasRenderData)
            return new PakMeshEntry(sm.Name, objectPath, PakMeshKind.StaticMesh, -1, materialCount, 0, -1, -1, pakChunkId);

        var lod0 = sm.RenderData!.LODs![0];
        // NumVertices 只在 UE3 路径被赋值；UE5 cooked build 用 PositionVertexBuffer.NumVertices，
        // 再 fallback 到 Sections 的 MaxVertexIndex 推算（non-inlined bulk data 时 PositionVertexBuffer 为 null）。
        int vertexCount = lod0.PositionVertexBuffer?.NumVertices
            ?? (lod0.Sections?.Length > 0 ? lod0.Sections.Max(s => s.MaxVertexIndex) + 1 : 0);
        // IndexBuffer.Buffer 在 non-inlined bulk data 时为 null；Sections.NumTriangles 是直接序列化字段，始终可靠。
        int triangleCount = lod0.Sections?.Sum(s => s.NumTriangles)
            ?? (lod0.IndexBuffer?.Buffer?.Length ?? 0) / 3;
        return new PakMeshEntry(sm.Name, objectPath, PakMeshKind.StaticMesh, lodCount, materialCount, 0, vertexCount, triangleCount, pakChunkId);
    }

    private static PakMeshEntry BuildSkeletalMeshEntry(USkeletalMesh skm, string objectPath, string pakChunkId)
    {
        int lodCount = skm.LODModels?.Length ?? 0;
        int materialCount = skm.SkeletalMaterials?.Length ?? skm.Materials?.Length ?? 0;
        int boneCount = skm.ReferenceSkeleton?.FinalRefBoneInfo?.Length ?? 0;
        var lod0 = skm.LODModels?.Length > 0 ? skm.LODModels[0] : null;
        int vertexCount = lod0?.NumVertices ?? 0;
        int triangleCount = lod0?.Sections?.Sum(s => (int)s.NumTriangles) ?? 0;
        return new PakMeshEntry(skm.Name, objectPath, PakMeshKind.SkeletalMesh, lodCount, materialCount, boneCount, vertexCount, triangleCount, pakChunkId);
    }

    private static PakMaterialEntry BuildMaterialEntry(UMaterial mat, string objectPath, string pakChunkId) =>
        new(mat.Name, objectPath,
            mat.BlendMode.ToString(), mat.ShadingModel.ToString(),
            mat.ReferencedTextures.Count, mat.TwoSided, pakChunkId);

    private static PakMaterialInstanceEntry BuildMaterialInstanceEntry(UMaterialInstanceConstant mi, string objectPath, string pakChunkId)
    {
        var parentName = mi.Parent?.Name ?? string.Empty;
        return new PakMaterialInstanceEntry(mi.Name, objectPath, parentName, mi.TextureParameterValues.Length, pakChunkId);
    }

    private static void AccumulateMaterialInstanceTextureUsage(DefaultFileProvider provider, UMaterialInstanceConstant mi, Dictionary<string, List<string>> textureUsage)
    {
        foreach (var param in mi.TextureParameterValues)
        {
            if (param.ParameterValue is null || param.ParameterValue.IsNull) continue;
            var tex = param.ParameterValue.Load<UTexture>();
            if (tex is null) continue;
            var ownerName = tex.Owner?.Name.ToString();
            if (string.IsNullOrEmpty(ownerName)) continue;
            var fixedPath = provider.FixPath(ownerName);
            var texPath = fixedPath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                ? fixedPath[..^".uasset".Length]
                : fixedPath;
            if (!textureUsage.TryGetValue(texPath, out var list))
                textureUsage[texPath] = list = [];
            if (!list.Contains(mi.Name))
                list.Add(mi.Name);
        }
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

    // ── 地图 Actor 扫描 ───────────────────────────────────────────────────────

    public async IAsyncEnumerable<PakScanEntry> ScanMapActorsStreamAsync(
        string pakDirectory,
        PakScanConfig? config = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
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

        var channel = Channel.CreateUnbounded<PakScanEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        var scanTask = Task.Run(async () =>
        {
            try
            {
                DefaultFileProvider provider;
                if (_provider is not null)
                {
                    provider = _provider;
                }
                else
                {
                    var game = ResolveGameVersion(config.GameVersion);

                    if (!string.IsNullOrWhiteSpace(config.OodleDllPath) && File.Exists(config.OodleDllPath))
                    {
                        try { OodleHelper.Initialize(config.OodleDllPath); }
                        catch { /* 忽略，Oodle 不影响 .umap 解析主流程 */ }
                    }

                    provider = new DefaultFileProvider(
                        pakDirectory,
                        SearchOption.AllDirectories,
                        new VersionContainer(game),
                        StringComparer.OrdinalIgnoreCase);

                    provider.Initialize();
                    _provider = provider;

                    if (!string.IsNullOrWhiteSpace(config.AesKey))
                    {
                        try
                        {
                            var key = new FAesKey(config.AesKey);
                            await provider.SubmitKeyAsync(new FGuid(), key);
                        }
                        catch (Exception ex)
                        {
                            await channel.Writer.WriteAsync(new PakScanDiagnosticEntry(new Diagnostic(
                                DiagnosticSeverity.Warning, "PKS004",
                                $"AES 密钥提交失败，加密资产将无法读取：{ex.Message}",
                                SuggestedFix: "请确认密钥格式为十六进制字符串")), cancellationToken);
                        }
                    }

                    await provider.MountAsync();
                }

                var umapPaths = provider.Files.Keys
                    .Where(p => p.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
                    .Where(p => !config.ExcludeEnginePaths || !p.Contains("/Engine/", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (umapPaths.Count == 0)
                {
                    await channel.Writer.WriteAsync(new PakScanDiagnosticEntry(new Diagnostic(
                        DiagnosticSeverity.Error, "PKS010",
                        $"目录中未找到任何 .umap 文件：{pakDirectory}", pakDirectory,
                        "请确认 pak 包含地图文件，或关闭 ExcludeEnginePaths 选项")), cancellationToken);
                    return;
                }

                // 第一遍：收集所有 LevelInstance 引用关系，被引用的子关卡不作为独立 entry 输出
                var levelInstanceRefs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                var referencedLevels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var umapPath in umapPaths)
                {
                    var mapObjectPath = umapPath[..^".umap".Length];
                    var refs = CollectLevelInstanceRefs(provider, umapPath);
                    if (refs.Count > 0)
                    {
                        levelInstanceRefs[mapObjectPath] = refs;
                        foreach (var r in refs)
                            referencedLevels.Add(r);
                    }
                }

                // 补充 World Partition streaming cells：路径含 _Generated_ 的 umap
                // 是其父目录名对应 map 的子关卡（WP chain 在 cooked build 里无法从 umap 直接读取）
                const string generatedMarker = "/_Generated_/";
                foreach (var umapPath in umapPaths)
                {
                    var mapObjectPath = umapPath[..^".umap".Length];
                    var idx = mapObjectPath.IndexOf(generatedMarker, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) continue;

                    // 父 map objectPath = _Generated_ 之前的部分
                    var parentObjectPath = mapObjectPath[..idx];
                    referencedLevels.Add(mapObjectPath);
                    if (!levelInstanceRefs.TryGetValue(parentObjectPath, out var list))
                    {
                        list = new List<string>();
                        levelInstanceRefs[parentObjectPath] = list;
                    }
                    list.Add(mapObjectPath);
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var diagnostics = new List<Diagnostic>();
                var perMapEntries = new List<MapMeshUsageEntry>(umapPaths.Count);
                // 被引用的子关卡单独扫描，供 ActorLocal 视图使用；不进入 perMapEntries
                var referencedLevelEntries = new List<MapMeshUsageEntry>();
                int scanned = 0;
                int mapsWithErrors = 0;

                await channel.Writer.WriteAsync(new PakMapScanStartEntry(umapPaths.Count), cancellationToken);

                foreach (var umapPath in umapPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var mapObjectPath = umapPath[..^".umap".Length];
                    bool isReferenced = referencedLevels.Contains(mapObjectPath);

                    MapMeshUsageEntry entry;

                    try
                    {
                        // 子关卡只扫描自身 actor，不再递归展开（它就是被展开的那一层）
                        entry = isReferenced
                            ? ScanSingleMap(provider, umapPath, mapObjectPath)
                            : ScanSingleMap(provider, umapPath, mapObjectPath, levelInstanceRefs, referencedLevels);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        mapsWithErrors++;
                        scanned++;
                        var diag = new Diagnostic(DiagnosticSeverity.Warning, "PKS009",
                            $"地图解析失败，已跳过：{umapPath} — [{ex.GetType().Name}] {ex.Message}\n{ex.StackTrace?.Split('\n').FirstOrDefault()}", umapPath);
                        diagnostics.Add(diag);
                        await channel.Writer.WriteAsync(new PakScanDiagnosticEntry(diag), cancellationToken);
                        await channel.Writer.WriteAsync(
                            new PakMapScanProgressEntry(scanned, umapPaths.Count, umapPath, 0), cancellationToken);
                        continue;
                    }

                    if (isReferenced)
                    {
                        referencedLevelEntries.Add(entry);
                    }
                    else
                    {
                        perMapEntries.Add(entry);
                        await channel.Writer.WriteAsync(new PakMapMeshUsageFound(entry), cancellationToken);
                    }

                    scanned++;
                    await channel.Writer.WriteAsync(
                        new PakMapScanProgressEntry(scanned, umapPaths.Count, umapPath, entry.Placements.Count),
                        cancellationToken);
                }

                sw.Stop();
                var result = BuildMapActorStats(pakDirectory, perMapEntries, referencedLevelEntries, referencedLevels, mapsWithErrors, diagnostics);
                await channel.Writer.WriteAsync(new PakMapScanCompleteEntry(result, sw.Elapsed), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex)
            {
                await channel.Writer.WriteAsync(new PakScanDiagnosticEntry(new Diagnostic(
                    DiagnosticSeverity.Error, "PKS008",
                    $"地图扫描过程中发生未预期错误：{ex.Message}")), CancellationToken.None);
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, cancellationToken);

        await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken))
            yield return entry;

        await scanTask;
    }

    public async Task<MapActorScanResult> ScanMapActorsAsync(
        string pakDirectory,
        PakScanConfig? config = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        MapActorScanResult? finalResult = null;
        var earlyDiags = new List<Diagnostic>();

        await foreach (var entry in ScanMapActorsStreamAsync(pakDirectory, config, cancellationToken))
        {
            switch (entry)
            {
                case PakMapScanStartEntry s:
                    progress?.Report(new OperationProgress("pakMapScan", "MapScan", 0, s.TotalMaps,
                        $"开始地图扫描，共 {s.TotalMaps} 个…"));
                    break;

                case PakMapScanProgressEntry p:
                    progress?.Report(new OperationProgress("pakMapScan", "MapScan", p.Scanned, p.Total,
                        $"地图扫描中… {p.Scanned}/{p.Total}  {p.MapPath}"));
                    break;

                case PakMapMeshUsageFound:
                    break;

                case PakMapScanCompleteEntry c:
                    finalResult = c.Result;
                    progress?.Report(new OperationProgress("pakMapScan", "Done",
                        c.Result.TotalMapsScanned, c.Result.TotalMapsScanned,
                        $"地图扫描完成：{c.Result.TotalMapsScanned} 张地图，{c.Result.Aggregates.Count} 个 Mesh，耗时 {FormatElapsed(c.Elapsed)}"));
                    break;

                case PakScanDiagnosticEntry d:
                    earlyDiags.Add(d.Diagnostic);
                    break;
            }
        }

        // 早期错误（PKS001/PKS002/PKS010）时 PakMapScanCompleteEntry 不会发出
        return finalResult ?? new MapActorScanResult(pakDirectory, [], [], [], 0, 0, earlyDiags);
    }

    private static MapMeshUsageEntry ScanSingleMap(
        DefaultFileProvider provider,
        string umapPath,
        string mapObjectPath,
        IReadOnlyDictionary<string, List<string>>? levelInstanceRefs = null,
        IReadOnlySet<string>? referencedLevels = null)
    {
        IPackage pkg;
        try { pkg = provider.LoadPackage(umapPath); }
        catch (Exception ex)
        {
            // 展开完整异常链以拿到根本原因
            var chain = new System.Text.StringBuilder();
            var e = ex;
            while (e is not null)
            {
                chain.Append($"[{e.GetType().Name}] {e.Message}; ");
                e = e.InnerException;
            }
            throw new InvalidOperationException(
                $"无法加载地图包：{umapPath} — {chain}", ex);
        }

        var world = pkg.GetExports().OfType<UWorld>().FirstOrDefault()
            ?? throw new InvalidOperationException($"地图包内未找到 UWorld 导出：{umapPath}");

        var level = world.PersistentLevel.Load<ULevel>();
        if (level?.Actors is null)
            return new MapMeshUsageEntry(mapObjectPath, [], 0, 0);

        var meshCounts = new Dictionary<string, int>(512, StringComparer.OrdinalIgnoreCase);
        int totalActors = 0;
        int failedActors = 0;

        foreach (var actorPtr in level.Actors)
        {
            if (actorPtr is null || actorPtr.IsNull) continue;

            // 廉价类名检查，不加载 export 内容
            var exportTypeName = actorPtr.ResolvedObject?.Class?.Name.Text;
            if (!string.Equals(exportTypeName, "StaticMeshActor", StringComparison.Ordinal))
                continue;

            UObject? actorObj;
            try
            {
                actorObj = actorPtr.Load<UObject>();
                if (actorObj is null) continue;
            }
            catch
            {
                failedActors++;
                continue;
            }

            totalActors++;

            try
            {
                // 取 StaticMeshComponent 子对象引用
                if (!actorObj.TryGetValue<FPackageIndex>(out var smCompIdx, "StaticMeshComponent")
                    || smCompIdx is null || smCompIdx.IsNull)
                    continue;

                var smComp = smCompIdx.Load<UStaticMeshComponent>();
                if (smComp is null) continue;

                // GetStaticMesh() 从属性表读取，不加载 UStaticMesh 本体
                var meshIdx = smComp.GetStaticMesh();
                if (meshIdx is null || meshIdx.IsNull) continue;

                var meshPath = BuildMeshObjectPath(meshIdx);
                if (meshPath is null) continue;

                meshCounts.TryGetValue(meshPath, out var prev);
                meshCounts[meshPath] = prev + 1;
            }
            catch
            {
                failedActors++;
            }
        }

        // 第二遍：统计 PackedLevelActor / PackedLevelInstance 内的 ISM 组件
        // 这类 actor 是 LevelInstance 的打包变体，无独立子关卡 .umap，
        // 所有 StaticMesh 放置已合并为 UInstancedStaticMeshComponent export，
        // 存在于本 .umap 中，outer chain 指向对应的 PackedLevelActor。
        ScanPackedLevelActorISMs(pkg, level.Actors, meshCounts, ref failedActors);

        // 第三遍：扫描 World Partition 外部 actor 包
        // UE5 World Partition 地图把绝大多数 actor 存为独立 .uasset，
        // 路径格式：<mapObjectPath>/__ExternalActors__/**/*.uasset
        ScanExternalActors(provider, mapObjectPath, meshCounts, ref totalActors, ref failedActors);

        // 第四遍：展开非 packed ALevelInstance 引用的子关卡，按引用次数累加
        // 子关卡本身不作为独立 entry 输出（已在调用方过滤），所以这里是唯一计数入口
        if (levelInstanceRefs is not null && levelInstanceRefs.TryGetValue(mapObjectPath, out var childRefs))
        {
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { mapObjectPath };
            foreach (var childObjectPath in childRefs)
            {
                MergeLevelInstanceMeshCounts(
                    provider, childObjectPath, meshCounts, levelInstanceRefs, referencedLevels, visiting);
            }
        }

        var placements = meshCounts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new MapMeshPlacement(kv.Key, kv.Value))
            .ToList();

        return new MapMeshUsageEntry(mapObjectPath, placements, totalActors, failedActors);
    }

    /// <summary>
    /// 扫描 UE5 World Partition 地图的外部 actor 包。
    /// 路径格式：{mapObjectPath}/__ExternalActors__/**/*.uasset
    /// 每个文件是一个独立 actor 包，直接检查其 export 类型和 StaticMeshComponent。
    /// </summary>
    private static void ScanExternalActors(
        DefaultFileProvider provider,
        string mapObjectPath,
        Dictionary<string, int> meshCounts,
        ref int totalActors,
        ref int failedActors)
    {
        // 构造外部 actor 目录前缀，例如 "Game/Maps/MyMap/__ExternalActors__/"
        var prefix = mapObjectPath + "/__ExternalActors__/";

        // 从 provider.Files.Keys 筛选属于本地图的外部 actor 文件
        var externalActorPaths = provider.Files.Keys
            .Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                     && p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (externalActorPaths.Count == 0) return;

        foreach (var actorAssetPath in externalActorPaths)
        {
            try
            {
                if (!provider.TryLoadPackage(actorAssetPath, out var actorPkg) || actorPkg is null)
                    continue;

                // 每个外部 actor 包通常只有一个 actor export
                foreach (var export in actorPkg.GetExports())
                {
                    var typeName = export.ExportType;
                    if (!string.Equals(typeName, "StaticMeshActor", StringComparison.Ordinal))
                        continue;

                    totalActors++;

                    if (!export.TryGetValue<FPackageIndex>(out var smCompIdx, "StaticMeshComponent")
                        || smCompIdx is null || smCompIdx.IsNull)
                        continue;

                    var smComp = smCompIdx.Load<UStaticMeshComponent>();
                    if (smComp is null) continue;

                    var meshIdx = smComp.GetStaticMesh();
                    if (meshIdx is null || meshIdx.IsNull) continue;

                    var meshPath = BuildMeshObjectPath(meshIdx);
                    if (meshPath is null) continue;

                    meshCounts.TryGetValue(meshPath, out var prev);
                    meshCounts[meshPath] = prev + 1;
                }
            }
            catch
            {
                failedActors++;
            }
        }
    }

    /// <summary>
    /// 统计地图中 PackedLevelActor / PackedLevelInstance 携带的 ISM 组件放置数。
    /// 这类 actor 是 LevelInstance 的打包变体，无独立子关卡 .umap；
    /// 所有 StaticMesh 放置合并为 UInstancedStaticMeshComponent export，
    /// 直接存在于父 .umap，outer chain 指向对应的 PackedLevelActor。
    /// </summary>
    private static void ScanPackedLevelActorISMs(
        CUE4Parse.UE4.Assets.IPackage pkg,
        FPackageIndex?[] levelActors,
        Dictionary<string, int> meshCounts,
        ref int failedActors)
    {
        // 快速检查：关卡里有没有 PackedLevelActor，没有就直接返回
        bool hasPackedActors = false;
        foreach (var actorPtr in levelActors)
        {
            if (actorPtr is null || actorPtr.IsNull) continue;
            var t = actorPtr.ResolvedObject?.Class?.Name.Text;
            if (t is "PackedLevelActor" or "APackedLevelActor"
                  or "PackedLevelInstance" or "APackedLevelInstance")
            {
                hasPackedActors = true;
                break;
            }
        }
        if (!hasPackedActors) return;

        // 遍历包内所有 export，找 UInstancedStaticMeshComponent，
        // 通过 UObject.Outer（ResolvedObject）向上追溯 outer chain 判断归属
        foreach (var export in pkg.GetExports())
        {
            if (export is not UInstancedStaticMeshComponent ismComp) continue;

            bool ownedByPackedActor = false;
            var outer = ismComp.Outer;           // ResolvedObject?
            while (outer is not null)
            {
                var cls = outer.Class?.Name.Text;
                if (cls is "PackedLevelActor" or "APackedLevelActor"
                        or "PackedLevelInstance" or "APackedLevelInstance")
                {
                    ownedByPackedActor = true;
                    break;
                }
                outer = outer.Outer;
            }
            if (!ownedByPackedActor) continue;

            try
            {
                var meshIdx = ismComp.GetStaticMesh();
                if (meshIdx is null || meshIdx.IsNull) continue;

                var meshPath = BuildMeshObjectPath(meshIdx);
                if (meshPath is null) continue;

                // PerInstanceSMData 是反序列化字段，直接读取；fallback 为 1
                var instanceCount = ismComp.PerInstanceSMData?.Length ?? 1;
                if (instanceCount <= 0) instanceCount = 1;

                meshCounts.TryGetValue(meshPath, out var prev);
                meshCounts[meshPath] = prev + instanceCount;
            }
            catch
            {
                failedActors++;
            }
        }
    }

    /// <summary>
    /// 轻量扫描一个 .umap，返回其中所有非 packed ALevelInstance actor 引用的子关卡 objectPath 列表。
    /// 同一子关卡被引用多次则重复出现（代表多个放置实例）。
    /// </summary>
    private static List<string> CollectLevelInstanceRefs(DefaultFileProvider provider, string umapPath)
    {
        var result = new List<string>();
        try
        {
            if (!provider.TryLoadPackage(umapPath, out var pkg) || pkg is null)
                return result;

            var world = pkg.GetExports().OfType<UWorld>().FirstOrDefault();
            if (world is null) return result;

            // ── 链路一：ALevelInstance actor（非 World Partition 场景）──────────────
            var level = world.PersistentLevel.Load<ULevel>();
            if (level?.Actors is not null)
            {
                foreach (var actorPtr in level.Actors)
                {
                    if (actorPtr is null || actorPtr.IsNull) continue;

                    var typeName = actorPtr.ResolvedObject?.Class?.Name.Text;
                    if (typeName is not ("LevelInstance" or "ALevelInstance"
                        or "LevelStreamingLevelInstanceEditor" or "LevelInstanceActor"))
                        continue;

                    UObject? actorObj;
                    try { actorObj = actorPtr.Load<UObject>(); }
                    catch { continue; }
                    if (actorObj is null) continue;

                    // cooked pak 里属性名是 CookedWorldAsset，编辑器包里是 WorldAsset，两个都试
                    if (!actorObj.TryGetValue<FSoftObjectPath>(out var worldAsset, "CookedWorldAsset") &&
                        !actorObj.TryGetValue<FSoftObjectPath>(out worldAsset, "WorldAsset"))
                        continue;

                    var normalized = NormalizeSoftPath(provider, worldAsset.AssetPathName.Text);
                    if (!string.IsNullOrEmpty(normalized))
                        result.Add(normalized);
                }
            }

            // ── 链路二：World Partition RuntimeHash（Spatial Hash 和 HashSet 两种）──
            var wpIndex = world.GetOrDefault<FPackageIndex>("WorldPartition");
            if (wpIndex is null || wpIndex.IsNull) return result;

            UWorldPartition? wp;
            try { wp = wpIndex.Load<UWorldPartition>(); }
            catch { return result; }
            if (wp?.RuntimeHash is null || wp.RuntimeHash.IsNull) return result;

            CUE4Parse.UE4.Assets.Exports.UObject? runtimeHashObj;
            try { runtimeHashObj = wp.RuntimeHash.Load(); }
            catch { return result; }

            // 收集所有 streaming cell 的 FPackageIndex
            var cellIndices = new List<FPackageIndex>();

            if (runtimeHashObj is UWorldPartitionRuntimeSpatialHash spatialHash)
            {
                foreach (var grid in spatialHash.StreamingGrids)
                    foreach (var gridLevel in grid.GridLevels)
                        foreach (var layerCell in gridLevel.LayerCells)
                            cellIndices.AddRange(layerCell.GridCells);
            }
            else if (runtimeHashObj is UWorldPartitionRuntimeHashSet hashSet)
            {
                foreach (var streamingData in hashSet.RuntimeStreamingData)
                {
                    cellIndices.AddRange(streamingData.SpatiallyLoadedCells);
                    cellIndices.AddRange(streamingData.NonSpatiallyLoadedCells);
                }
            }

            foreach (var cellIndex in cellIndices)
            {
                if (cellIndex.IsNull) continue;
                try
                {
                    if (cellIndex.Load() is not UWorldPartitionRuntimeLevelStreamingCell cell) continue;
                    if (cell.LevelStreaming is null || cell.LevelStreaming.IsNull) continue;
                    if (cell.LevelStreaming.Load() is not ULevelStreaming streaming) continue;
                    if (streaming.WorldAsset is null) continue;

                    var normalized = NormalizeSoftPath(provider, streaming.WorldAsset.Value.AssetPathName.Text);
                    if (!string.IsNullOrEmpty(normalized))
                        result.Add(normalized);
                }
                catch { /* 单个 cell 失败不影响整体 */ }
            }
        }
        catch
        {
            // 收集阶段失败不影响主扫描
        }
        return result;
    }

    /// visiting 用于检测循环引用；referencedLevels 用于判断子关卡是否已被收录（避免遗漏
    /// 仅被间接引用的层级）。
    /// </summary>
    private static void MergeLevelInstanceMeshCounts(
        DefaultFileProvider provider,
        string childObjectPath,
        Dictionary<string, int> meshCounts,
        IReadOnlyDictionary<string, List<string>> levelInstanceRefs,
        IReadOnlySet<string>? referencedLevels,
        HashSet<string> visiting)
    {
        if (!visiting.Add(childObjectPath))
            return; // 循环引用，跳过

        try
        {
            var childUmapPath = childObjectPath + ".umap";
            if (!provider.Files.ContainsKey(childUmapPath))
                return;

            if (!provider.TryLoadPackage(childUmapPath, out var pkg) || pkg is null)
                return;

            var world = pkg.GetExports().OfType<UWorld>().FirstOrDefault();
            if (world is null) return;

            var level = world.PersistentLevel.Load<ULevel>();
            if (level?.Actors is null) return;

            // 统计子关卡自己的 StaticMeshActor
            foreach (var actorPtr in level.Actors)
            {
                if (actorPtr is null || actorPtr.IsNull) continue;

                var exportTypeName = actorPtr.ResolvedObject?.Class?.Name.Text;
                if (!string.Equals(exportTypeName, "StaticMeshActor", StringComparison.Ordinal))
                    continue;

                UObject? actorObj;
                try { actorObj = actorPtr.Load<UObject>(); }
                catch { continue; }
                if (actorObj is null) continue;

                try
                {
                    if (!actorObj.TryGetValue<FPackageIndex>(out var smCompIdx, "StaticMeshComponent")
                        || smCompIdx is null || smCompIdx.IsNull)
                        continue;

                    var smComp = smCompIdx.Load<UStaticMeshComponent>();
                    if (smComp is null) continue;

                    var meshIdx = smComp.GetStaticMesh();
                    if (meshIdx is null || meshIdx.IsNull) continue;

                    var meshPath = BuildMeshObjectPath(meshIdx);
                    if (meshPath is null) continue;

                    meshCounts.TryGetValue(meshPath, out var prev);
                    meshCounts[meshPath] = prev + 1;
                }
                catch { }
            }

            // 子关卡里的 PackedLevelActor ISM
            var dummyFailed = 0;
            ScanPackedLevelActorISMs(pkg, level.Actors, meshCounts, ref dummyFailed);

            // 子关卡里的 World Partition 外部 actor
            var dummyTotal = 0;
            ScanExternalActors(provider, childObjectPath, meshCounts, ref dummyTotal, ref dummyFailed);

            // 递归展开子关卡自己的 LevelInstance 引用
            if (levelInstanceRefs.TryGetValue(childObjectPath, out var grandChildRefs))
            {
                foreach (var grandChild in grandChildRefs)
                {
                    MergeLevelInstanceMeshCounts(
                        provider, grandChild, meshCounts, levelInstanceRefs, referencedLevels, visiting);
                }
            }
        }
        catch
        {
            // 子关卡解析失败不影响父关卡
        }
        finally
        {
            visiting.Remove(childObjectPath);
        }
    }

    private static string? BuildMeshObjectPath(FPackageIndex meshIdx)
    {
        var resolved = meshIdx.ResolvedObject;
        if (resolved is null) return null;

        // 尝试直接用 ToString()；CUE4Parse 对 import/export 的默认 ToString 已给出
        // 形如 "Game/Meshes/SM_Rock.SM_Rock" 的路径，与 PakMeshEntry.ObjectPath 格式一致。
        // 若格式不匹配则退回手动 outer chain 拼接。
        var str = resolved.ToString();
        if (!string.IsNullOrEmpty(str) && str.Contains('/'))
            return str;

        // 手动拼接：walk outer chain
        var segments = new List<string>(4);
        var cur = resolved;
        while (cur is not null)
        {
            segments.Add(cur.Name.Text);
            cur = cur.Outer;
        }
        segments.Reverse();
        return segments.Count > 0 ? string.Join('/', segments) : null;
    }

    internal static MapActorScanResult BuildMapActorStats(
        string inputDirectory,
        IReadOnlyList<MapMeshUsageEntry> perMapEntries,
        IReadOnlyList<MapMeshUsageEntry> referencedLevelEntries,
        IReadOnlySet<string> referencedLevels,
        int mapsWithErrors,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        var crossMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var meshMapCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapEntry in perMapEntries)
        {
            foreach (var placement in mapEntry.Placements)
            {
                crossMap.TryGetValue(placement.MeshObjectPath, out var prevTotal);
                crossMap[placement.MeshObjectPath] = prevTotal + placement.Count;

                meshMapCount.TryGetValue(placement.MeshObjectPath, out var prevMaps);
                meshMapCount[placement.MeshObjectPath] = prevMaps + 1;
            }
        }

        var aggregates = crossMap
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new MapMeshAggregate(
                kv.Key,
                kv.Value,
                meshMapCount.GetValueOrDefault(kv.Key)))
            .ToList();

        return new MapActorScanResult(
            inputDirectory,
            perMapEntries,
            referencedLevelEntries,
            aggregates,
            perMapEntries.Count + mapsWithErrors,
            mapsWithErrors,
            diagnostics);
    }

    /// <summary>
    /// 将 FSoftObjectPath 的 AssetPathName 文本规范化为 provider.Files.Keys 格式（无扩展名 objectPath）。
    /// </summary>
    private static string NormalizeSoftPath(DefaultFileProvider provider, string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return string.Empty;

        if (assetPath.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
        {
            var fixed1 = provider.FixPath(assetPath);
            if (fixed1.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
                fixed1 = fixed1[..^".umap".Length];
            return fixed1;
        }

        // FSoftObjectPath 典型格式：/Game/Maps/LA.LA（PackageName.ObjectName）
        var dotIdx = assetPath.LastIndexOf('.');
        var slashIdx = assetPath.LastIndexOf('/');
        if (dotIdx > slashIdx)
            assetPath = assetPath[..dotIdx];

        var fixed2 = provider.FixPath(assetPath + ".umap");
        if (fixed2.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
            fixed2 = fixed2[..^".umap".Length];
        return fixed2;
    }
}

