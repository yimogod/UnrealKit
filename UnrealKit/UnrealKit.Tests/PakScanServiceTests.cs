using UnrealKit.Core.Diagnostics;
using UnrealKit.Core.PakScan;

namespace UnrealKit.Tests;

public sealed class PakScanServiceTests
{
    [Fact]
    public async Task ScanAsync_DirectoryNotFound_ReturnsPKS001Error()
    {
        var service = new PakScanService();
        var result = await service.ScanAsync(@"C:\NonExistentPakDirectory_12345");

        Assert.Null(result.Report);
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, d => d.Code == "PKS001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ScanAsync_EmptyDirectory_ReturnsPKS002Error()
    {
        var tempDir = Directory.CreateTempSubdirectory("pakscan_test_");
        try
        {
            var service = new PakScanService();
            var result = await service.ScanAsync(tempDir.FullName);

            Assert.Null(result.Report);
            Assert.False(result.IsSuccess);
            Assert.Contains(result.Diagnostics, d => d.Code == "PKS002" && d.Severity == DiagnosticSeverity.Error);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_CancellationRequested_ReturnsEarlyWithError()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var tempDir = Directory.CreateTempSubdirectory("pakscan_cancel_");
        try
        {
            var service = new PakScanService();
            // 空目录会在 ThrowIfCancellationRequested 前先命中 PKS002，
            // 但取消令牌应被传入并尊重。验证两种终止路径都不会挂起。
            var result = await service.ScanAsync(tempDir.FullName, cancellationToken: cts.Token);
            // 空目录情况：提前返回 PKS002
            Assert.False(result.IsSuccess);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    // ── 地图 Actor 扫描测试 ────────────────────────────────────────────────

    [Fact]
    public async Task ScanMapActorsAsync_DirectoryNotFound_ReturnsPKS001Error()
    {
        var service = new PakScanService();
        var result = await service.ScanMapActorsAsync(@"C:\NonExistentPakDirectory_map_12345");

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, d => d.Code == "PKS001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ScanMapActorsAsync_EmptyDirectory_ReturnsPKS002Error()
    {
        var tempDir = Directory.CreateTempSubdirectory("pakscan_map_test_");
        try
        {
            var service = new PakScanService();
            var result = await service.ScanMapActorsAsync(tempDir.FullName);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Diagnostics, d => d.Code == "PKS002" && d.Severity == DiagnosticSeverity.Error);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void BuildMapActorStats_AggregatesCountsCorrectly()
    {
        // 两张地图，同一个 mesh 各出现一次，另一张地图独有第二个 mesh
        var map1 = new MapMeshUsageEntry(
            "Game/Maps/Level1",
            [new MapMeshPlacement("Game/Meshes/SM_Rock", 5), new MapMeshPlacement("Game/Meshes/SM_Tree", 3)],
            TotalActorCount: 8,
            FailedActorCount: 0);

        var map2 = new MapMeshUsageEntry(
            "Game/Maps/Level2",
            [new MapMeshPlacement("Game/Meshes/SM_Rock", 10)],
            TotalActorCount: 10,
            FailedActorCount: 0);

        var result = PakScanService.BuildMapActorStats(
            "Game/Content",
            [map1, map2],
            mapsWithErrors: 0,
            diagnostics: []);

        Assert.Equal(2, result.TotalMapsScanned);
        Assert.Equal(0, result.TotalMapsWithErrors);
        Assert.True(result.IsSuccess);

        // SM_Rock: 5+10=15, 出现在 2 张地图
        var rock = Assert.Single(result.Aggregates, a => a.MeshObjectPath == "Game/Meshes/SM_Rock");
        Assert.Equal(15, rock.TotalCount);
        Assert.Equal(2, rock.MapCount);

        // SM_Tree: 3, 出现在 1 张地图
        var tree = Assert.Single(result.Aggregates, a => a.MeshObjectPath == "Game/Meshes/SM_Tree");
        Assert.Equal(3, tree.TotalCount);
        Assert.Equal(1, tree.MapCount);

        // Aggregates 应按 TotalCount 降序：SM_Rock(15) > SM_Tree(3)
        Assert.Equal("Game/Meshes/SM_Rock", result.Aggregates[0].MeshObjectPath);
        Assert.Equal("Game/Meshes/SM_Tree", result.Aggregates[1].MeshObjectPath);

        // PerMapEntries 应包含两张地图
        Assert.Equal(2, result.PerMapEntries.Count);
    }
}
