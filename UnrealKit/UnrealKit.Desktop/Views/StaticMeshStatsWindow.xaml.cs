using System.Windows;
using UnrealKit.Desktop.Models;

namespace UnrealKit.Desktop.Views;

public partial class StaticMeshStatsWindow : Window
{
    private static StaticMeshStatsWindow? _instance;

    public static bool IsOpen => _instance is not null;

    private StaticMeshStatsWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
    }

    public static void Show(
        Window owner,
        PakScanStaticMeshOption mesh,
        IReadOnlyList<PakScanMaterialOption> materials,
        IReadOnlyList<PakScanMaterialInstanceOption> matInstances,
        IReadOnlyList<PakScanTextureOption> textures)
    {
        if (_instance is null)
        {
            _instance = new StaticMeshStatsWindow(owner);
            _instance.Closed += (_, _) => _instance = null;
            _instance.Show();
        }
        else
        {
            if (_instance.WindowState == WindowState.Minimized)
                _instance.WindowState = WindowState.Normal;
            _instance.Activate();
        }

        _instance.Update(mesh, materials, matInstances, textures);
    }

    private void Update(
        PakScanStaticMeshOption mesh,
        IReadOnlyList<PakScanMaterialOption> materials,
        IReadOnlyList<PakScanMaterialInstanceOption> matInstances,
        IReadOnlyList<PakScanTextureOption> textures)
    {
        Title = $"StaticMesh 资产统计 — {mesh.Name}";
        TitleText.Text = mesh.Name;
        PathText.Text  = mesh.Path;

        LodCountText.Text      = mesh.LodCount;
        MaterialCountText.Text = mesh.MaterialCount;
        VertexCountText.Text   = mesh.VertexCount;
        TriangleCountText.Text = mesh.TriangleCount;
        ChunkText.Text         = mesh.PakChunkId;

        MatHeaderText.Text     = $"Material（{materials.Count}）";
        MatInstHeaderText.Text = $"MatInstance（{matInstances.Count}）";
        TexHeaderText.Text     = $"Texture（{textures.Count}）";
        StatsCountText.Text    = $"M:{materials.Count}  MI:{matInstances.Count}  T:{textures.Count}";

        MaterialsGrid.ItemsSource   = materials;
        MatInstancesGrid.ItemsSource = matInstances;
        TexturesGrid.ItemsSource    = textures.Select(t => new TextureRow(t)).ToList();

        FooterText.Text = mesh.Path;
    }
}

/// <summary>为 Texture DataGrid 添加 Resolution 展示列的包装行。</summary>
internal sealed class TextureRow(PakScanTextureOption t)
{
    public string Name           => t.Name;
    public string Resolution     => $"{t.SizeX}×{t.SizeY}";
    public string Format         => t.Format;
    public string EstimatedSizeMB => t.EstimatedSizeMB;
    public string PakChunkId     => t.PakChunkId;
    public string Path           => t.Path;
}
