using System.IO;
using System.Reflection;
using System.Text;
using Scriban;
using Scriban.Runtime;
using UnrealKit.Core.Diagnostics;

namespace UnrealKit.Cli;

// ── 列类型 ───────────────────────────────────────────────────────────────────

internal enum HtmlColumnType
{
    Text,
    Number,
    /// <summary>字节数（long），渲染为 KB/MB/GB + 比例条形图。</summary>
    Bytes,
    /// <summary>枚举值，渲染为彩色标签。</summary>
    Tag,
    /// <summary>文件/资产路径，截断 + tooltip，弱化颜色。</summary>
    Path,
}

// ── 列定义 ───────────────────────────────────────────────────────────────────

internal sealed record HtmlColumn(
    string Key,
    string Title,
    HtmlColumnType Type = HtmlColumnType.Text,
    bool Sortable = true,
    bool DefaultSort = false,
    bool DefaultSortDesc = true);

// ── 工具栏过滤器 ─────────────────────────────────────────────────────────────

internal sealed record HtmlFilterOption(string Label, string Value);

internal sealed record HtmlFilter(
    string Id,
    string Label,
    string FilterColumn,
    string? EnumFromColumn = null,
    IReadOnlyList<HtmlFilterOption>? FixedOptions = null,
    HtmlFilterMode FilterMode = HtmlFilterMode.Exact,
    string? FilterColumn2 = null);

internal enum HtmlFilterMode
{
    Exact,
    NumberAtLeast,
    NumberAtLeastEither,
    NumberAtMost,
}

// ── 元信息行 ─────────────────────────────────────────────────────────────────

internal sealed record HtmlMetaItem(string Label, string Value);

// ── 主报告构造器 ──────────────────────────────────────────────────────────────

internal static class HtmlTableReport
{
    private static readonly Template _template = LoadTemplate();

    private static Template LoadTemplate()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .First(n => n.EndsWith("HtmlTableReport.sbnhtml", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var src = reader.ReadToEnd();
        var tpl = Template.Parse(src);
        if (tpl.HasErrors)
            throw new InvalidOperationException(
                "HtmlTableReport.sbnhtml parse error: " + string.Join("; ", tpl.Messages));
        return tpl;
    }

    internal static string Build(
        string title,
        IReadOnlyList<HtmlColumn> columns,
        IReadOnlyList<object?[]> rows,
        IReadOnlyList<HtmlMetaItem>? meta = null,
        IReadOnlyList<HtmlFilter>? filters = null,
        IReadOnlyList<Diagnostic>? diagnostics = null,
        string searchPlaceholder = "搜索…",
        string? uniqueCountKey = null)
    {
        var defaultSortCol = columns.FirstOrDefault(c => c.DefaultSort) ?? columns[0];
        var defaultSortDir = defaultSortCol.DefaultSortDesc ? -1 : 1;
        var bytesColKey    = columns.FirstOrDefault(c => c.Type == HtmlColumnType.Bytes)?.Key;

        var model = new
        {
            title              = HtmlEscape(title),
            search_placeholder = HtmlEscape(searchPlaceholder),
            meta_html          = BuildMetaHtml(meta),
            filter_html        = BuildFilterHtml(filters),
            thead_html         = BuildTheadHtml(columns),
            diag_html          = BuildDiagHtml(diagnostics),
            data_json          = SerializeRows(columns, rows),
            cols_json          = SerializeColDefs(columns),
            bytes_key_json     = bytesColKey != null ? $"\"{JsEscape(bytesColKey)}\"" : "null",
            col_count          = columns.Count,
            sort_key_json      = $"\"{JsEscape(defaultSortCol.Key)}\"",
            sort_dir           = defaultSortDir,
            filter_logic_js    = BuildFilterLogicJs(filters, columns),
            enum_fill_js       = BuildEnumFillJs(filters),
            uniq_key_json      = uniqueCountKey != null ? $"\"{JsEscape(uniqueCountKey)}\"" : "null",
        };

        var ctx = new TemplateContext { LimitToString = 0 };
        ctx.PushGlobal(new ScriptObject());
        var scriptObj = new ScriptObject();
        scriptObj.Import(model, renamer: member => member.Name);
        ctx.PushGlobal(scriptObj);
        return _template.Render(ctx);
    }

    internal static void WriteAndOpen(string html, string path)
    {
        File.WriteAllText(path, html, Encoding.UTF8);
        Console.WriteLine($"HTML report: {path}");
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { }
    }

    // ── 转义工具 ──────────────────────────────────────────────────────────────

    internal static string HtmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    internal static string JsEscape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"")
         .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

    internal static string JsString(string? s) => $"\"{JsEscape(s ?? "")}\"";

    // ── JSON 序列化 ───────────────────────────────────────────────────────────

    private static string SerializeRows(IReadOnlyList<HtmlColumn> cols, IReadOnlyList<object?[]> rows)
    {
        var sb = new StringBuilder(rows.Count * 120);
        sb.Append('[');
        for (var r = 0; r < rows.Count; r++)
        {
            if (r > 0) sb.Append(',');
            sb.Append('{');
            var row = rows[r];
            for (var c = 0; c < cols.Count; c++)
            {
                if (c > 0) sb.Append(',');
                sb.Append('"').Append(JsEscape(cols[c].Key)).Append("\":");
                var val = c < row.Length ? row[c] : null;
                if (val is long l)        sb.Append(l);
                else if (val is int i)    sb.Append(i);
                else if (val is double d) sb.Append(d.ToString(System.Globalization.CultureInfo.InvariantCulture));
                else if (val is float f)  sb.Append(f.ToString(System.Globalization.CultureInfo.InvariantCulture));
                else if (val is bool b)   sb.Append(b ? "true" : "false");
                else                      sb.Append(JsString(val?.ToString()));
            }
            sb.Append('}');
        }
        sb.Append(']');
        return sb.ToString();
    }

    // cols_json: [{k:"n",t:0},{k:"sz",t:2},...]  t: 0=text 1=number 2=bytes 3=tag 4=path
    private static string SerializeColDefs(IReadOnlyList<HtmlColumn> cols)
    {
        var sb = new StringBuilder("[");
        for (var i = 0; i < cols.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var t = cols[i].Type switch
            {
                HtmlColumnType.Number => 1,
                HtmlColumnType.Bytes  => 2,
                HtmlColumnType.Tag    => 3,
                HtmlColumnType.Path   => 4,
                _                     => 0,
            };
            sb.Append("{k:\"").Append(JsEscape(cols[i].Key)).Append("\",t:").Append(t).Append('}');
        }
        sb.Append(']');
        return sb.ToString();
    }

    // ── HTML 片段 ─────────────────────────────────────────────────────────────

    private static string BuildMetaHtml(IReadOnlyList<HtmlMetaItem>? meta) =>
        meta is { Count: > 0 }
            ? string.Join(" &nbsp;·&nbsp; ", meta.Select(
                m => $"<span>{HtmlEscape(m.Label)}：<strong>{HtmlEscape(m.Value)}</strong></span>"))
            : "";

    private static string BuildDiagHtml(IReadOnlyList<Diagnostic>? diagnostics)
    {
        if (diagnostics is null or { Count: 0 }) return "";
        return string.Join("", diagnostics.Select(d =>
            $"<div class=\"diag diag-{d.Severity.ToString().ToLowerInvariant()}\">" +
            $"[{d.Severity}] {HtmlEscape(d.Code ?? "")} — {HtmlEscape(d.Message ?? "")}</div>"));
    }

    private static string BuildFilterHtml(IReadOnlyList<HtmlFilter>? filters)
    {
        if (filters is null or { Count: 0 }) return "";
        var sb = new StringBuilder();
        foreach (var f in filters)
        {
            if (f.FilterMode is HtmlFilterMode.NumberAtLeast or HtmlFilterMode.NumberAtMost)
            {
                // 数字输入框，不用 <select>
                var placeholder = HtmlEscape(f.Label);
                sb.Append($"<label style=\"display:inline-flex;align-items:center;gap:4px;\">" +
                          $"{placeholder}：" +
                          $"<input id=\"{HtmlEscape(f.Id)}\" type=\"number\" min=\"0\" placeholder=\"–\" " +
                          $"style=\"width:72px;\" oninput=\"onFilter()\">" +
                          $"</label>\n");
            }
            else
            {
                sb.Append($"<select id=\"{HtmlEscape(f.Id)}\" onchange=\"onFilter()\">");
                sb.Append($"<option value=\"\">{HtmlEscape(f.Label)}</option>");
                if (f.FixedOptions is { Count: > 0 })
                    foreach (var opt in f.FixedOptions)
                        sb.Append($"<option value=\"{HtmlEscape(opt.Value)}\">{HtmlEscape(opt.Label)}</option>");
                sb.Append("</select>\n");
            }
        }
        return sb.ToString();
    }

    private static string BuildTheadHtml(IReadOnlyList<HtmlColumn> cols)
    {
        var sb = new StringBuilder();
        foreach (var col in cols)
        {
            if (col.Sortable)
                sb.Append($"<th class=\"sortable\" data-k=\"{HtmlEscape(col.Key)}\" " +
                          $"onclick=\"sort('{HtmlEscape(col.Key)}')\">" +
                          $"{HtmlEscape(col.Title)}<span class=\"si\"></span></th>\n");
            else
                sb.Append($"<th>{HtmlEscape(col.Title)}</th>\n");
        }
        return sb.ToString();
    }

    // ── JS 片段 ───────────────────────────────────────────────────────────────

    private static string BuildEnumFillJs(IReadOnlyList<HtmlFilter>? filters)
    {
        if (filters is null or { Count: 0 }) return "";
        var sb = new StringBuilder();
        foreach (var f in filters.Where(f => f.EnumFromColumn is not null))
        {
            var key = JsEscape(f.EnumFromColumn!);
            var id  = JsEscape(f.Id);
            sb.Append($"(function(){{var sel=document.getElementById('{id}');" +
                      $"[...new Set(RAW.map(function(r){{return r.{key};}}))]" +
                      $".sort().forEach(function(v){{var o=document.createElement('option');" +
                      $"o.value=o.textContent=v;sel.appendChild(o);}});}})();\n");
        }
        return sb.ToString();
    }

    private static string BuildFilterLogicJs(IReadOnlyList<HtmlFilter>? filters, IReadOnlyList<HtmlColumn> cols)
    {
        var sb = new StringBuilder();

        var searchKeys = cols
            .Where(c => c.Type is HtmlColumnType.Text or HtmlColumnType.Path)
            .Select(c => $"String(r.{JsEscape(c.Key)}).indexOf(q)>=0")
            .ToArray();
        var searchExpr = searchKeys.Length > 0 ? string.Join("||", searchKeys) : "true";

        sb.AppendLine($"  flt=RAW.filter(function(r){{");
        sb.AppendLine($"    if(q&&!({searchExpr}))return false;");

        if (filters is { Count: > 0 })
        {
            foreach (var f in filters)
            {
                var id  = JsEscape(f.Id);
                var col = JsEscape(f.FilterColumn);
                switch (f.FilterMode)
                {
                    case HtmlFilterMode.Exact:
                        sb.AppendLine($"    var _f{id}=document.getElementById('{id}').value;" +
                                      $"if(_f{id}&&r.{col}!==_f{id})return false;");
                        break;
                    case HtmlFilterMode.NumberAtLeast:
                        sb.AppendLine($"    var _f{id}=parseInt(document.getElementById('{id}').value||'0')||0;" +
                                      $"if(_f{id}&&r.{col}<_f{id})return false;");
                        break;
                    case HtmlFilterMode.NumberAtLeastEither:
                        var col2 = JsEscape(f.FilterColumn2 ?? f.FilterColumn);
                        sb.AppendLine($"    var _f{id}=parseInt(document.getElementById('{id}').value||'0')||0;" +
                                      $"if(_f{id}&&r.{col}<_f{id}&&r.{col2}<_f{id})return false;");
                        break;
                    case HtmlFilterMode.NumberAtMost:
                        sb.AppendLine($"    var _f{id}=document.getElementById('{id}').value;" +
                                      $"if(_f{id}!==''&&_f{id}!==null){{var _n{id}=parseInt(_f{id});if(!isNaN(_n{id})&&r.{col}>_n{id})return false;}}");
                        break;
                }
            }
        }

        sb.AppendLine("    return true;");
        sb.AppendLine("  });");
        return sb.ToString();
    }
}

// ── 数据特定构造器 ─────────────────────────────────────────────────────────────

internal static class PakScanHtmlBuilder
{
    internal static string Build(UnrealKit.Core.PakScan.PakScanResult result)
    {
        var report   = result.Report;
        var textures = report?.Textures ?? [];
        var scanDir  = report?.InputDirectory ?? result.InputPath;

        HtmlColumn[] columns =
        [
            new("n",   "名称",     HtmlColumnType.Text),
            new("x",   "宽",       HtmlColumnType.Number),
            new("y",   "高",       HtmlColumnType.Number),
            new("fmt", "格式",     HtmlColumnType.Tag),
            new("grp", "LodGroup", HtmlColumnType.Tag),
            new("mip", "Mips",     HtmlColumnType.Number),
            new("lod", "LodBias",  HtmlColumnType.Number),
            new("sz",  "估算大小", HtmlColumnType.Bytes, DefaultSort: true, DefaultSortDesc: true),
            new("p",   "路径",     HtmlColumnType.Path,  Sortable: false),
        ];

        HtmlFilter[] filters =
        [
            new("_grp", "全部 LodGroup", "grp", EnumFromColumn: "grp"),
            new("_fmt", "全部格式",       "fmt", EnumFromColumn: "fmt"),
            new("_sz",  "全部尺寸", "x",
                FilterMode: HtmlFilterMode.NumberAtLeastEither,
                FilterColumn2: "y",
                FixedOptions:
                [
                    new("≥ 4096", "4096"),
                    new("≥ 2048", "2048"),
                    new("≥ 1024", "1024"),
                    new("≥ 512",  "512"),
                ]),
        ];

        var rows = textures.Select(t => new object?[]
        {
            t.Name, (int)t.SizeX, (int)t.SizeY, t.PixelFormat,
            t.LodGroup, (int)t.NumMips, (int)t.LodBias,
            t.EstimatedSizeBytes, t.ObjectPath,
        }).ToArray();

        var meta = new HtmlMetaItem[]
        {
            new("目录",        scanDir),
            new("扫描资产",    (report?.TotalAssetsScanned ?? 0).ToString()),
            new("Texture2D",   textures.Count.ToString()),
            new("StaticMesh",  (report?.StaticMeshCount  ?? 0).ToString()),
            new("SkeletalMesh",(report?.SkeletalMeshCount ?? 0).ToString()),
            new("生成时间",    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
        };

        var title = $"PakScan — Textures — {Path.GetFileName(scanDir.TrimEnd('/', '\\'))}";
        return HtmlTableReport.Build(title, columns, rows, meta, filters,
            result.Diagnostics, searchPlaceholder: "搜索名称 / 路径 / 格式…");
    }
}

// ── PakScan StaticMesh HTML 构造器 ────────────────────────────────────────────

internal static class PakScanStaticMeshHtmlBuilder
{
    internal static string Build(UnrealKit.Core.PakScan.PakScanResult result)
    {
        var report  = result.Report;
        var meshes  = report?.StaticMeshes ?? [];
        var scanDir = report?.InputDirectory ?? result.InputPath;

        HtmlColumn[] columns =
        [
            new("n",   "名称",    HtmlColumnType.Text),
            new("lod", "LOD 数",  HtmlColumnType.Number, DefaultSort: true, DefaultSortDesc: true),
            new("mat", "材质数",  HtmlColumnType.Number),
            new("vtx", "顶点数",  HtmlColumnType.Number),
            new("tri", "三角形数", HtmlColumnType.Number),
            new("p",   "路径",    HtmlColumnType.Path, Sortable: false),
        ];

        var rows = meshes.Select(m => new object?[]
        {
            m.Name, m.LodCount, m.MaterialCount, m.VertexCount, m.TriangleCount, m.ObjectPath,
        }).ToArray();

        var meta = new HtmlMetaItem[]
        {
            new("目录",       scanDir),
            new("StaticMesh", (report?.StaticMeshCount ?? 0).ToString()),
            new("生成时间",   DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
        };

        var title = $"PakScan — StaticMesh — {Path.GetFileName(scanDir.TrimEnd('/', '\\'))}";
        return HtmlTableReport.Build(title, columns, rows, meta, null,
            result.Diagnostics, searchPlaceholder: "搜索名称 / 路径…");
    }
}

// ── PakScan SkeletalMesh HTML 构造器 ──────────────────────────────────────────

internal static class PakScanSkeletalMeshHtmlBuilder
{
    internal static string Build(UnrealKit.Core.PakScan.PakScanResult result)
    {
        var report  = result.Report;
        var meshes  = report?.SkeletalMeshes ?? [];
        var scanDir = report?.InputDirectory ?? result.InputPath;

        HtmlColumn[] columns =
        [
            new("n",   "名称",    HtmlColumnType.Text),
            new("lod", "LOD 数",  HtmlColumnType.Number, DefaultSort: true, DefaultSortDesc: true),
            new("mat", "材质数",  HtmlColumnType.Number),
            new("bon", "骨骼数",  HtmlColumnType.Number),
            new("vtx", "顶点数",  HtmlColumnType.Number),
            new("tri", "三角形数", HtmlColumnType.Number),
            new("p",   "路径",    HtmlColumnType.Path, Sortable: false),
        ];

        var rows = meshes.Select(m => new object?[]
        {
            m.Name, m.LodCount, m.MaterialCount, m.BoneCount, m.VertexCount, m.TriangleCount, m.ObjectPath,
        }).ToArray();

        var meta = new HtmlMetaItem[]
        {
            new("目录",         scanDir),
            new("SkeletalMesh", (report?.SkeletalMeshCount ?? 0).ToString()),
            new("生成时间",     DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
        };

        var title = $"PakScan — SkeletalMesh — {Path.GetFileName(scanDir.TrimEnd('/', '\\'))}";
        return HtmlTableReport.Build(title, columns, rows, meta, null,
            result.Diagnostics, searchPlaceholder: "搜索名称 / 路径…");
    }
}

// ── PakScan Material HTML 构造器 ──────────────────────────────────────────────

internal static class PakScanMaterialHtmlBuilder
{
    internal static string Build(UnrealKit.Core.PakScan.PakScanResult result)
    {
        var report    = result.Report;
        var materials = report?.Materials ?? [];
        var scanDir   = report?.InputDirectory ?? result.InputPath;

        HtmlColumn[] columns =
        [
            new("n",   "名称",         HtmlColumnType.Text),
            new("bm",  "BlendMode",    HtmlColumnType.Tag),
            new("sm",  "ShadingModel", HtmlColumnType.Tag),
            new("tx",  "纹理引用数",    HtmlColumnType.Number, DefaultSort: true, DefaultSortDesc: true),
            new("ts",  "TwoSided",     HtmlColumnType.Tag),
            new("p",   "路径",         HtmlColumnType.Path, Sortable: false),
        ];

        var rows = materials.Select(m => new object?[]
        {
            m.Name, m.BlendMode, m.ShadingModel,
            m.ReferencedTextureCount, m.TwoSided.ToString(), m.ObjectPath,
        }).ToArray();

        var meta = new HtmlMetaItem[]
        {
            new("目录",     scanDir),
            new("Material", (report?.MaterialCount ?? 0).ToString()),
            new("生成时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
        };

        var title = $"PakScan — Material — {Path.GetFileName(scanDir.TrimEnd('/', '\\'))}";
        return HtmlTableReport.Build(title, columns, rows, meta, null,
            result.Diagnostics, searchPlaceholder: "搜索名称 / 路径…");
    }
}

// ── PakScan CSV 构造器 ────────────────────────────────────────────────────────

internal static class PakScanCsvBuilder
{
    private static readonly CsvColumn[] TexColumns =
    [
        new("Name",               "Name"),
        new("SizeX",              "SizeX"),
        new("SizeY",              "SizeY"),
        new("PixelFormat",        "PixelFormat"),
        new("LodGroup",           "LodGroup"),
        new("NumMips",            "NumMips"),
        new("LodBias",            "LodBias"),
        new("EstimatedSizeBytes", "EstimatedSizeBytes"),
        new("ObjectPath",         "ObjectPath"),
    ];

    private static readonly CsvColumn[] StaticMeshColumns =
    [
        new("Name",          "Name"),
        new("LodCount",      "LodCount"),
        new("MaterialCount", "MaterialCount"),
        new("VertexCount",   "VertexCount"),
        new("TriangleCount", "TriangleCount"),
        new("ObjectPath",    "ObjectPath"),
    ];

    private static readonly CsvColumn[] SkeletalMeshColumns =
    [
        new("Name",          "Name"),
        new("LodCount",      "LodCount"),
        new("MaterialCount", "MaterialCount"),
        new("BoneCount",     "BoneCount"),
        new("VertexCount",   "VertexCount"),
        new("TriangleCount", "TriangleCount"),
        new("ObjectPath",    "ObjectPath"),
    ];

    internal static string Build(UnrealKit.Core.PakScan.PakScanResult result)
    {
        var textures = result.Report?.Textures ?? [];
        var texRows = textures.Select(t => new object?[]
        {
            t.Name, t.SizeX, t.SizeY, t.PixelFormat,
            t.LodGroup, t.NumMips, t.LodBias,
            t.EstimatedSizeBytes, t.ObjectPath,
        }).ToArray();
        return CsvTableReport.Build(TexColumns, texRows);
    }

    internal static string BuildStaticMeshCsv(UnrealKit.Core.PakScan.PakScanResult result)
    {
        var meshes = result.Report?.StaticMeshes ?? [];
        var rows = meshes.Select(m => new object?[]
        {
            m.Name, m.LodCount, m.MaterialCount, m.VertexCount, m.TriangleCount, m.ObjectPath,
        }).ToArray();
        return CsvTableReport.Build(StaticMeshColumns, rows);
    }

    internal static string BuildSkeletalMeshCsv(UnrealKit.Core.PakScan.PakScanResult result)
    {
        var meshes = result.Report?.SkeletalMeshes ?? [];
        var rows = meshes.Select(m => new object?[]
        {
            m.Name, m.LodCount, m.MaterialCount, m.BoneCount, m.VertexCount, m.TriangleCount, m.ObjectPath,
        }).ToArray();
        return CsvTableReport.Build(SkeletalMeshColumns, rows);
    }

    private static readonly CsvColumn[] MaterialColumns =
    [
        new("Name",                   "Name"),
        new("BlendMode",              "BlendMode"),
        new("ShadingModel",           "ShadingModel"),
        new("ReferencedTextureCount", "ReferencedTextureCount"),
        new("TwoSided",               "TwoSided"),
        new("ObjectPath",             "ObjectPath"),
    ];

    internal static string BuildMaterialCsv(UnrealKit.Core.PakScan.PakScanResult result)
    {
        var materials = result.Report?.Materials ?? [];
        var rows = materials.Select(m => new object?[]
        {
            m.Name, m.BlendMode, m.ShadingModel,
            m.ReferencedTextureCount, m.TwoSided, m.ObjectPath,
        }).ToArray();
        return CsvTableReport.Build(MaterialColumns, rows);
    }
}

// ── 地图 Actor 统计 HTML 构造器 ───────────────────────────────────────────────

internal static class MapMeshPlacementsHtmlBuilder
{
    internal static string Build(UnrealKit.Core.PakScan.MapActorScanResult result)
    {
        HtmlColumn[] columns =
        [
            new("map", "地图",     HtmlColumnType.Text),
            new("n",   "Mesh 名", HtmlColumnType.Text, DefaultSort: false),
            new("cnt", "放置次数", HtmlColumnType.Number, DefaultSort: true, DefaultSortDesc: true),
            new("mp",  "地图路径", HtmlColumnType.Path, Sortable: false),
            new("p",   "Mesh 路径", HtmlColumnType.Path, Sortable: false),
        ];

        var rows = result.PerMapEntries
            .SelectMany(e => e.Placements.Select(p => new object?[]
            {
                Path.GetFileNameWithoutExtension(e.MapObjectPath),
                Path.GetFileNameWithoutExtension(p.MeshObjectPath),
                p.Count,
                e.MapObjectPath,
                p.MeshObjectPath,
            }))
            .ToArray();

        var meta = new HtmlMetaItem[]
        {
            new("目录",          result.InputDirectory),
            new("地图总数",       result.TotalMapsScanned.ToString()),
            new("独立 Mesh 数",   result.Aggregates.Count.ToString()),
            new("生成时间",       DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
        };

        return HtmlTableReport.Build(
            $"地图 Actor 统计 — 按地图 — {Path.GetFileName(result.InputDirectory.TrimEnd('/', '\\'))}",
            columns, rows, meta,
            [new HtmlFilter("fCntMax", "放置次数 ≤", "cnt", FilterMode: HtmlFilterMode.NumberAtMost)],
            result.Diagnostics, searchPlaceholder: "搜索地图 / Mesh 名…",
            uniqueCountKey: "p");
    }
}

internal static class MapMeshAggregatesHtmlBuilder
{
    internal static string Build(UnrealKit.Core.PakScan.MapActorScanResult result)
    {
        HtmlColumn[] columns =
        [
            new("n",   "Mesh 名",  HtmlColumnType.Text),
            new("tot", "总放置次数", HtmlColumnType.Number, DefaultSort: true, DefaultSortDesc: true),
            new("mc",  "出现地图数", HtmlColumnType.Number),
            new("p",   "路径",      HtmlColumnType.Path, Sortable: false),
        ];

        var rows = result.Aggregates.Select(a => new object?[]
        {
            Path.GetFileNameWithoutExtension(a.MeshObjectPath),
            a.TotalCount,
            a.MapCount,
            a.MeshObjectPath,
        }).ToArray();

        var meta = new HtmlMetaItem[]
        {
            new("目录",         result.InputDirectory),
            new("独立 Mesh 数",  result.Aggregates.Count.ToString()),
            new("生成时间",      DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
        };

        return HtmlTableReport.Build(
            $"地图 Actor 统计 — 汇总 — {Path.GetFileName(result.InputDirectory.TrimEnd('/', '\\'))}",
            columns, rows, meta,
            [new HtmlFilter("fTotMax", "总次数 ≤", "tot", FilterMode: HtmlFilterMode.NumberAtMost)],
            result.Diagnostics, searchPlaceholder: "搜索 Mesh 名 / 路径…",
            uniqueCountKey: "p");
    }
}
