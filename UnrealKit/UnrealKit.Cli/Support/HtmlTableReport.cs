using System.Text;
using UnrealKit.Core.Diagnostics;

namespace UnrealKit.Cli;

// ── 列类型 ───────────────────────────────────────────────────────────────────

/// <summary>列的数据类型，控制排序规则和单元格渲染方式。</summary>
internal enum HtmlColumnType
{
    /// <summary>普通文字，字符串排序。</summary>
    Text,
    /// <summary>整数或小数，数值排序。</summary>
    Number,
    /// <summary>字节数（long），数值排序，渲染为 KB/MB/GB + 比例条形图。</summary>
    Bytes,
    /// <summary>枚举值，字符串排序，渲染为彩色标签。</summary>
    Tag,
    /// <summary>文件/资产路径，字符串排序，截断 + tooltip，弱化颜色。</summary>
    Path,
}

// ── 列定义 ───────────────────────────────────────────────────────────────────

/// <param name="Key">JS 对象中的字段名（短 key，如 "n"、"sz"）。</param>
/// <param name="Title">表头显示名。</param>
/// <param name="Type">渲染与排序规则。</param>
/// <param name="Sortable">是否允许点击排序，默认 true。</param>
/// <param name="DefaultSort">是否作为初始排序列，多列定义时取第一个。</param>
/// <param name="DefaultSortDesc">初始排序方向，默认降序（true）。</param>
internal sealed record HtmlColumn(
    string Key,
    string Title,
    HtmlColumnType Type = HtmlColumnType.Text,
    bool Sortable = true,
    bool DefaultSort = false,
    bool DefaultSortDesc = true);

// ── 工具栏过滤器 ─────────────────────────────────────────────────────────────

/// <summary>工具栏下拉过滤器的一条选项。</summary>
internal sealed record HtmlFilterOption(string Label, string Value);

/// <summary>
/// 工具栏上的下拉过滤器。
/// <para>当 <see cref="EnumFromColumn"/> 不为空时，运行时自动从数据中枚举唯一值填充选项；
/// 也可通过 <see cref="FixedOptions"/> 提供固定选项列表（两者互斥）。</para>
/// </summary>
/// <param name="Id">HTML 元素 id。</param>
/// <param name="Label">全选项的占位文字，如"全部 LodGroup"。</param>
/// <param name="FilterColumn">过滤目标列的 Key。</param>
/// <param name="EnumFromColumn">从哪一列枚举唯一值，通常与 FilterColumn 相同；为 null 时使用 FixedOptions。</param>
/// <param name="FixedOptions">固定选项列表，EnumFromColumn 为 null 时生效。</param>
/// <param name="FilterMode">过滤逻辑，默认 Exact（精确匹配列值）。</param>
/// <param name="FilterColumn2">仅用于 <see cref="HtmlFilterMode.NumberAtLeastEither"/>：第二个比较列的 Key（如高度列 "y"）。</param>
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
    /// <summary>过滤值必须与列值完全相等（字符串）。</summary>
    Exact,
    /// <summary>列值（数值）必须 ≥ 过滤值，用于尺寸筛选等场景。</summary>
    NumberAtLeast,
    /// <summary>过滤值与另一列同时满足 NumberAtLeast（如宽或高任意一边 ≥ 阈值）。</summary>
    NumberAtLeastEither,
}

// ── 元信息行 ─────────────────────────────────────────────────────────────────

/// <summary>页面顶部 meta 栏中的一个键值对。</summary>
internal sealed record HtmlMetaItem(string Label, string Value);

// ── 主报告构造器 ──────────────────────────────────────────────────────────────

/// <summary>
/// 通用 HTML 表格报告。接受列定义 + 行数据，生成自包含的单文件 HTML：
/// 全文搜索、下拉过滤、点击排序、分页、暗色模式、诊断区。
/// </summary>
internal static class HtmlTableReport
{
    /// <param name="title">页面 &lt;title&gt; 和 &lt;h1&gt;。</param>
    /// <param name="columns">列定义，顺序即表格列顺序。</param>
    /// <param name="rows">行数据，每行是与 columns 等长的值数组；数值列传 long/int/double，其余传 string。</param>
    /// <param name="meta">顶部 meta 栏，按顺序渲染。</param>
    /// <param name="filters">工具栏过滤器列表，按顺序渲染在搜索框之后。</param>
    /// <param name="diagnostics">诊断信息，渲染在页面底部。</param>
    /// <param name="searchPlaceholder">搜索框占位文字。</param>
    internal static string Build(
        string title,
        IReadOnlyList<HtmlColumn> columns,
        IReadOnlyList<object?[]> rows,
        IReadOnlyList<HtmlMetaItem>? meta = null,
        IReadOnlyList<HtmlFilter>? filters = null,
        IReadOnlyList<Diagnostic>? diagnostics = null,
        string searchPlaceholder = "搜索…")
    {
        var colCount = columns.Count;
        var defaultSortCol = columns.FirstOrDefault(c => c.DefaultSort) ?? columns[0];
        var defaultSortDir = defaultSortCol.DefaultSortDesc ? -1 : 1;

        // 找出 Bytes 列用于 bar 计算（取第一个）
        var bytesColKey = columns.FirstOrDefault(c => c.Type == HtmlColumnType.Bytes)?.Key;

        // 序列化行数据为 JS 数组
        var dataJson = SerializeRows(columns, rows);

        // 诊断 HTML
        var diagHtml = BuildDiagHtml(diagnostics);

        // meta 行 HTML
        var metaHtml = meta is { Count: > 0 }
            ? string.Join(" &nbsp;·&nbsp; ", meta.Select(m => $"<span>{HtmlEscape(m.Label)}：<strong>{HtmlEscape(m.Value)}</strong></span>"))
            : "";

        // 工具栏过滤器 HTML（静态部分，枚举型在 JS 里填充）
        var filterHtml = BuildFilterHtml(filters);

        // 表头 HTML
        var theadHtml = BuildTheadHtml(columns);

        // JS 过滤逻辑
        var filterLogicJs = BuildFilterLogicJs(filters, columns);

        // JS 枚举填充
        var enumFillJs = BuildEnumFillJs(filters, columns);

        // JS 行渲染
        var renderRowJs = BuildRenderRowJs(columns, colCount, bytesColKey);

        var sb = new StringBuilder(1024 * 64);
        sb.Append($$"""
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>{{HtmlEscape(title)}}</title>
<style>
:root{
  --bg:#f5f5f5;--surface:#fff;--border:#ddd;--text:#222;--muted:#666;
  --accent:#0078d4;--warn:#d97706;--err:#dc2626;
  --row-even:#fafafa;--row-hover:#e8f0fe;--tag-bg:#e0e7ff;--tag-text:#3730a3;
}
@media(prefers-color-scheme:dark){
  :root:not([data-theme="light"]){
    --bg:#1a1a1a;--surface:#242424;--border:#3a3a3a;--text:#e8e8e8;--muted:#999;
    --accent:#4ea8de;--warn:#fbbf24;--err:#f87171;
    --row-even:#2a2a2a;--row-hover:#1e3a5f;--tag-bg:#312e81;--tag-text:#a5b4fc;
  }
}
*,::before,::after{box-sizing:border-box;margin:0;padding:0}
body{font-family:system-ui,-apple-system,sans-serif;font-size:13px;background:var(--bg);color:var(--text);padding:16px}
h1{font-size:16px;font-weight:600;margin-bottom:4px;word-break:break-all}
.meta{color:var(--muted);font-size:12px;margin-bottom:12px}
.toolbar{display:flex;flex-wrap:wrap;gap:8px;margin-bottom:10px;align-items:center}
.toolbar input,.toolbar select{background:var(--surface);color:var(--text);border:1px solid var(--border);
  border-radius:4px;padding:4px 8px;font-size:12px;outline:none}
.toolbar input:focus,.toolbar select:focus{border-color:var(--accent)}
.toolbar input[type=search]{width:240px}
.toolbar .spacer{flex:1}
.count{font-size:12px;color:var(--muted);white-space:nowrap}
.table-wrap{overflow-x:auto;border:1px solid var(--border);border-radius:6px}
table{width:100%;border-collapse:collapse;background:var(--surface)}
thead{position:sticky;top:0;z-index:1}
th{background:var(--surface);border-bottom:2px solid var(--border);padding:6px 8px;
  text-align:left;font-size:12px;font-weight:600;user-select:none;white-space:nowrap}
th.sortable{cursor:pointer}
th.sortable:hover{color:var(--accent)}
th .si{font-size:10px;margin-left:3px;opacity:.4}
th.asc .si::after{content:'▲';opacity:1}
th.desc .si::after{content:'▼';opacity:1}
th.sortable:not(.asc):not(.desc) .si::after{content:'⇅'}
td{padding:5px 8px;border-bottom:1px solid var(--border);font-size:12px;vertical-align:middle}
tr:nth-child(even) td{background:var(--row-even)}
tr:hover td{background:var(--row-hover)}
.c-text{max-width:220px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.c-path{max-width:320px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:var(--muted);font-size:11px}
.tag{display:inline-block;padding:1px 6px;border-radius:10px;font-size:11px;
  background:var(--tag-bg);color:var(--tag-text);white-space:nowrap}
.bar-cell{display:flex;align-items:center;gap:6px}
.bar{height:8px;border-radius:2px;background:var(--accent);opacity:.6;min-width:2px}
.nowrap{white-space:nowrap}
.pager{display:flex;align-items:center;gap:6px;margin-top:10px;justify-content:flex-end}
.pager button{background:var(--surface);color:var(--text);border:1px solid var(--border);
  border-radius:4px;padding:3px 10px;font-size:12px;cursor:pointer}
.pager button:hover:not(:disabled){border-color:var(--accent);color:var(--accent)}
.pager button:disabled{opacity:.4;cursor:default}
.pager .pi{font-size:12px;color:var(--muted)}
.diagnostics{margin-top:12px}
.diag{font-size:12px;padding:4px 8px;border-radius:4px;margin-bottom:4px;border-left:3px solid transparent}
.diag-warning{background:#fffbeb;border-color:var(--warn);color:#92400e}
.diag-error{background:#fef2f2;border-color:var(--err);color:#7f1d1d}
.diag-info{background:#eff6ff;border-color:var(--accent);color:#1e40af}
@media(prefers-color-scheme:dark){
  :root:not([data-theme="light"]) .diag-warning{background:#2d2007;color:#fde68a}
  :root:not([data-theme="light"]) .diag-error{background:#2d0707;color:#fca5a5}
  :root:not([data-theme="light"]) .diag-info{background:#07192d;color:#93c5fd}
}
.empty{text-align:center;color:var(--muted);padding:32px}
</style>
</head>
<body>
<h1>{{HtmlEscape(title)}}</h1>
""");

        if (metaHtml.Length > 0)
            sb.Append($"<div class=\"meta\">{metaHtml}</div>\n");

        sb.Append($$"""
<div class="toolbar">
  <input type="search" id="_search" placeholder="{{HtmlEscape(searchPlaceholder)}}" oninput="onFilter()">
{{filterHtml}}  <span class="spacer"></span>
  <span class="count" id="_count"></span>
  <select id="_ps" onchange="onPsChange()">
    <option value="100">100 条/页</option>
    <option value="250" selected>250 条/页</option>
    <option value="500">500 条/页</option>
    <option value="1000">1000 条/页</option>
  </select>
</div>
<div class="table-wrap">
<table>
  <thead><tr>
{{theadHtml}}  </tr></thead>
  <tbody id="_tbody"></tbody>
</table>
</div>
<div class="pager">
  <button id="_bf" onclick="go(1)">«</button>
  <button id="_bp" onclick="go(cur-1)">‹</button>
  <span class="pi" id="_pi"></span>
  <button id="_bn" onclick="go(cur+1)">›</button>
  <button id="_bl" onclick="go(tp)">»</button>
</div>
""");

        if (diagHtml.Length > 0)
            sb.Append($"<div class=\"diagnostics\">{diagHtml}</div>\n");

        sb.Append($$"""
<script>
const RAW={{dataJson}};
let flt=RAW.slice(),sortK='{{HtmlEscape(defaultSortCol.Key)}}',sortD={{(defaultSortDir == -1 ? "-1" : "1")}},cur=1,ps=250,tp=1;
{{(bytesColKey != null ? $"let maxB=RAW.reduce((m,r)=>Math.max(m,r.{bytesColKey}||0),0)||1;" : "")}}
function fmtB(b){{if(b===0)return'0 B';const u=['B','KB','MB','GB'];let i=0;while(b>=1024&&i<u.length-1){{b/=1024;i++;}}return b.toFixed(i>0?1:0)+' '+u[i];}}
function esc(s){{return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');}}
function render(){{
  const tbody=document.getElementById('_tbody');
  const s=(cur-1)*ps,e=Math.min(s+ps,flt.length);
  if(!flt.length){{tbody.innerHTML='<tr><td colspan="{{colCount}}" class="empty">没有匹配的数据</td></tr>';return;}}
  const rows=[];
  for(let i=s;i<e;i++){{const r=flt[i];rows.push({{renderRowJs}});}}
  tbody.innerHTML=rows.join('');
}}
function updPager(){{
  tp=Math.max(1,Math.ceil(flt.length/ps));
  document.getElementById('_pi').textContent=`第 ${cur} / ${tp} 页`;
  ['_bf','_bp'].forEach(id=>document.getElementById(id).disabled=cur<=1);
  ['_bn','_bl'].forEach(id=>document.getElementById(id).disabled=cur>=tp);
  document.getElementById('_count').textContent=`共 ${flt.length.toLocaleString()} 条`;
}}
function go(p){{p=Math.max(1,Math.min(p,tp));if(p===cur)return;cur=p;render();updPager();}}
function sort(k){{if(sortK===k)sortD*=-1;else{{sortK=k;sortD=-1;}}
  document.querySelectorAll('th[data-k]').forEach(th=>{{th.classList.remove('asc','desc');if(th.dataset.k===k)th.classList.add(sortD===1?'asc':'desc');}});
  flt.sort((a,b)=>{{const av=a[sortK],bv=b[sortK];return(typeof av==='number'?(av-bv):String(av).localeCompare(String(bv)))*sortD;}});
  cur=1;render();updPager();
}}
function onFilter(){{
  const q=(document.getElementById('_search').value||'').toLowerCase();
{{filterLogicJs}}  flt.sort((a,b)=>{{const av=a[sortK],bv=b[sortK];return(typeof av==='number'?(av-bv):String(av).localeCompare(String(bv)))*sortD;}});
  cur=1;updPager();render();
}}
function onPsChange(){{ps=parseInt(document.getElementById('_ps').value);cur=1;updPager();render();}}
// 枚举填充
{{enumFillJs}}
// 初始化
sort('{{HtmlEscape(defaultSortCol.Key)}}');
</script>
</body>
</html>
""");

        return sb.ToString();
    }

    /// <summary>写文件并在默认浏览器打开。</summary>
    internal static void WriteAndOpen(string html, string path)
    {
        File.WriteAllText(path, html, Encoding.UTF8);
        Console.WriteLine($"HTML report: {path}");
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { /* 打开失败不影响导出结果 */ }
    }

    // ── 内部工具 ──────────────────────────────────────────────────────────────

    internal static string HtmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    internal static string JsEscape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

    internal static string JsString(string? s) => $"\"{JsEscape(s ?? "")}\"";

    // ── JSON 行序列化 ─────────────────────────────────────────────────────────

    private static string SerializeRows(IReadOnlyList<HtmlColumn> cols, IReadOnlyList<object?[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append('[');
        for (var r = 0; r < rows.Count; r++)
        {
            if (r > 0) sb.Append(',');
            sb.Append('{');
            var row = rows[r];
            for (var c = 0; c < cols.Count; c++)
            {
                if (c > 0) sb.Append(',');
                sb.Append($"\"{JsEscape(cols[c].Key)}\":");
                var val = c < row.Length ? row[c] : null;
                if (val is long l) sb.Append(l);
                else if (val is int i) sb.Append(i);
                else if (val is double d) sb.Append(d.ToString(System.Globalization.CultureInfo.InvariantCulture));
                else if (val is float f) sb.Append(f.ToString(System.Globalization.CultureInfo.InvariantCulture));
                else if (val is bool b) sb.Append(b ? "true" : "false");
                else sb.Append(JsString(val?.ToString()));
            }
            sb.Append('}');
        }
        sb.Append(']');
        return sb.ToString();
    }

    // ── HTML 片段构造 ─────────────────────────────────────────────────────────

    private static string BuildDiagHtml(IReadOnlyList<Diagnostic>? diagnostics)
    {
        if (diagnostics is null or { Count: 0 }) return "";
        return string.Join("", diagnostics.Select(d =>
            $"<div class=\"diag diag-{d.Severity.ToString().ToLowerInvariant()}\">[{d.Severity}] {HtmlEscape(d.Code ?? "")} — {HtmlEscape(d.Message ?? "")}</div>"));
    }

    private static string BuildFilterHtml(IReadOnlyList<HtmlFilter>? filters)
    {
        if (filters is null or { Count: 0 }) return "";
        var sb = new StringBuilder();
        foreach (var f in filters)
        {
            sb.Append($"  <select id=\"{HtmlEscape(f.Id)}\" onchange=\"onFilter()\"><option value=\"\">{HtmlEscape(f.Label)}</option>");
            if (f.FixedOptions is { Count: > 0 })
            {
                foreach (var opt in f.FixedOptions)
                    sb.Append($"<option value=\"{HtmlEscape(opt.Value)}\">{HtmlEscape(opt.Label)}</option>");
            }
            sb.AppendLine("</select>");
        }
        return sb.ToString();
    }

    private static string BuildTheadHtml(IReadOnlyList<HtmlColumn> cols)
    {
        var sb = new StringBuilder();
        foreach (var col in cols)
        {
            if (col.Sortable)
                sb.AppendLine($"    <th class=\"sortable\" data-k=\"{HtmlEscape(col.Key)}\" onclick=\"sort('{HtmlEscape(col.Key)}')\">{HtmlEscape(col.Title)}<span class=\"si\"></span></th>");
            else
                sb.AppendLine($"    <th>{HtmlEscape(col.Title)}</th>");
        }
        return sb.ToString();
    }

    // ── JS 片段构造 ───────────────────────────────────────────────────────────

    private static string BuildEnumFillJs(IReadOnlyList<HtmlFilter>? filters, IReadOnlyList<HtmlColumn> cols)
    {
        if (filters is null or { Count: 0 }) return "";
        var sb = new StringBuilder();
        foreach (var f in filters.Where(f => f.EnumFromColumn is not null))
        {
            var key = JsEscape(f.EnumFromColumn!);
            var id = JsEscape(f.Id);
            sb.AppendLine($"(()=>{{const sel=document.getElementById('{id}');[...new Set(RAW.map(r=>r.{key}))].sort().forEach(v=>{{const o=document.createElement('option');o.value=o.textContent=v;sel.appendChild(o);}});}})();");
        }
        return sb.ToString();
    }

    private static string BuildFilterLogicJs(IReadOnlyList<HtmlFilter>? filters, IReadOnlyList<HtmlColumn> cols)
    {
        var sb = new StringBuilder();

        // 收集搜索列（Text + Path 类型的 key）
        var searchKeys = cols
            .Where(c => c.Type is HtmlColumnType.Text or HtmlColumnType.Path or HtmlColumnType.Tag)
            .Select(c => $"r.{JsEscape(c.Key)}.toLowerCase().includes(q)")
            .ToArray();
        var searchExpr = searchKeys.Length > 0
            ? string.Join("||", searchKeys)
            : "true";

        sb.AppendLine($"  flt=RAW.filter(r=>{{");
        sb.AppendLine($"    if(q&&!({searchExpr}))return false;");

        if (filters is { Count: > 0 })
        {
            foreach (var f in filters)
            {
                var id = JsEscape(f.Id);
                var col = JsEscape(f.FilterColumn);
                switch (f.FilterMode)
                {
                    case HtmlFilterMode.Exact:
                        sb.AppendLine($"    const _f{id}=document.getElementById('{id}').value;if(_f{id}&&r.{col}!==_f{id})return false;");
                        break;
                    case HtmlFilterMode.NumberAtLeast:
                        sb.AppendLine($"    const _f{id}=parseInt(document.getElementById('{id}').value||'0')||0;if(_f{id}&&r.{col}<_f{id})return false;");
                        break;
                    case HtmlFilterMode.NumberAtLeastEither:
                        var otherCol = JsEscape(f.FilterColumn2 ?? f.FilterColumn);
                        sb.AppendLine($"    const _f{id}=parseInt(document.getElementById('{id}').value||'0')||0;if(_f{id}&&r.{col}<_f{id}&&r.{otherCol}<_f{id})return false;");
                        break;
                }
            }
        }

        sb.AppendLine("    return true;");
        sb.AppendLine("  });");
        return sb.ToString();
    }

    private static string BuildRenderRowJs(IReadOnlyList<HtmlColumn> cols, int colCount, string? bytesColKey)
    {
        // 构造一个 JS 模板字符串表达式，每列按类型渲染
        var cells = new StringBuilder();
        foreach (var col in cols)
        {
            var k = JsEscape(col.Key);
            switch (col.Type)
            {
                case HtmlColumnType.Text:
                    cells.Append($"+'<td class=\"c-text\" title=\"'+esc(r.{k})+'\">'+ esc(r.{k})+'</td>'");
                    break;
                case HtmlColumnType.Number:
                    cells.Append($"+'<td>'+r.{k}+'</td>'");
                    break;
                case HtmlColumnType.Tag:
                    cells.Append($"+'<td><span class=\"tag\">'+esc(r.{k})+'</span></td>'");
                    break;
                case HtmlColumnType.Path:
                    cells.Append($"+'<td class=\"c-path\" title=\"'+esc(r.{k})+'\">'+ esc(r.{k})+'</td>'");
                    break;
                case HtmlColumnType.Bytes:
                    if (bytesColKey != null && bytesColKey == col.Key)
                        cells.Append($"+'<td><div class=\"bar-cell\"><div class=\"bar\" style=\"width:'+Math.max(2,Math.round(r.{k}/maxB*80))+'px\"></div><span class=\"nowrap\">'+fmtB(r.{k})+'</span></div></td>'");
                    else
                        cells.Append($"+'<td class=\"nowrap\">'+fmtB(r.{k})+'</td>'");
                    break;
            }
        }
        return $"'<tr>'{cells}'</tr>'";
    }
}

// ── 数据特定构造器 ─────────────────────────────────────────────────────────────

/// <summary>PakScan Texture 报告的 HTML 构造，CLI 与 Desktop 共用同一份组装逻辑。</summary>
internal static class PakScanHtmlBuilder
{
    internal static string Build(UnrealKit.Core.PakScan.PakScanResult result)
    {
        var report = result.Report;
        var textures = report?.Textures ?? [];
        var scanDir = report?.InputDirectory ?? result.InputPath;

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
            new("目录",      scanDir),
            new("扫描资产",  (report?.TotalAssetsScanned ?? 0).ToString()),
            new("Texture2D", textures.Count.ToString()),
            new("生成时间",  DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
        };

        var title = $"PakScan — Texture Report — {Path.GetFileName(scanDir.TrimEnd('/', '\\'))}";
        return HtmlTableReport.Build(title, columns, rows, meta, filters,
            result.Diagnostics, searchPlaceholder: "搜索名称 / 路径 / 格式…");
    }
}
