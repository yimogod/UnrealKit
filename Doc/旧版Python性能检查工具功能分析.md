# 旧版 Python 性能检查工具功能分析

## 分析范围

- 源码目录：`E:\ProjectDev\ProjectX\Script\Python`
- 分析日期：2026-08-04
- 目的：梳理旧 Python 工具已实现的功能、数据流、产物和限制，为 .NET GUI 重构确定范围。

> 源码的部分中文注释和控制台字符串存在编码显示异常；本文按实际代码逻辑归纳。

## 总体结论

旧工具是一组以命令行为主的 UE Android 性能辅助脚本，核心覆盖：

1. ADB 设备连接、应用启动、启动参数写入、游戏数据拉取。
2. Android `dumpsys meminfo` 抓取与解析。
3. UE `memreport` 的离线解析与表格导出。
4. 项目定制的静态相机性能日志/截图 HTML 报告。
5. 一组独立运行于 RenderDoc Python 环境的帧分析脚本。

## 入口和模块关系

主入口 `entry.py` 支持以下命令：

| 命令 | 模块 | 功能 |
| --- | --- | --- |
| `html` | `Profile/export_static_cam_html.py` | 静态相机性能日志与截图报表。 |
| `adb` | `Android/adb_main.py` | ADB、启动参数、内存/Saved 数据抓取。 |
| `pip` | `Pipeline/pip_main.py` | 内存测试流程编排。 |
| `android_mem_info` | `Profile/android_mem_info.py` | Android 内存文本解析。 |
| `ue_mem_report` | `Profile/mem_report.py` | UE MemReport 解析。 |
| `info` | `Tool/usefull_info.py` | UE、Perfetto、ADB 命令参考。 |

```text
entry.py
├─ Android/adb_main.py ── ADBProxy ── adb 命令行
├─ Pipeline/pip_main.py ── adb_main + pip_mem
│  └─ Pipeline/pip_mem.py ── android_mem_info + mem_report
├─ Profile/android_mem_info.py
├─ Profile/mem_report.py ── mem_util.py
├─ Profile/export_static_cam_html.py
└─ Tool/usefull_info.py

RenderDoc/*.py（独立脚本，未由 entry.py 调用）
```

## 硬编码配置与依赖

`Android/adb_const.py` 将下列配置写死在源码中：

- 包名：`com.lootergames.projectx`
- 启动 Activity：`com.epicgames.unreal.SplashActivity`
- UE 项目：`XGame`
- 设备 Saved 根目录：`/sdcard/Android/data/<package>/files/UnrealGame/<project>/<project>/Saved`
- 本地临时目录：`./AAsset/Temp`
- 本地 Saved 目录：`./AAsset/Saved`

因此旧工具默认依赖 Windows、可执行的 `adb`、可连接 Android 设备、符合该目录规则的 UE Android 包。MemReport 和静态性能解析依赖固定文本格式；RenderDoc 脚本依赖 RenderDoc Python API 与 UI/Replay 环境。

## ADB 与 Android 设备操作

`Android/adb_class.py` 的 `ADBProxy` 是对 ADB 的薄封装：

| 能力 | 实际命令/行为 |
| --- | --- |
| 查询版本、列出设备 | `adb version`、`adb devices` |
| 管理 Server | `adb start-server`、`adb kill-server`、重启 Server |
| Wi-Fi 连接 | `adb tcpip 5555`、`adb connect <ip>`、`adb disconnect <ip>` |
| 启动应用 | `adb shell am start -n <package>/<activity>` |
| 文件操作 | `adb pull`、`adb push`、`adb shell rm` |
| 系统信息 | `adb shell dumpsys <command>`，返回逐行输出 |

`Android/adb_main.py` 在此基础上实现：

- `meminfo`：执行 `dumpsys meminfo <package>`，保存为 `AAsset/Temp/meminfo_<timestamp>.txt`，并打开目录。
- `pull_saved`：清空本地 `AAsset/Saved`，依次拉取 `Logs`、`Screenshots`、`Profiling`、`GPUDumps`，随后打开目录。
- `pull_log`：只拉取日志目录。
- `del_ue_cmd`：删除设备端 `uecommandline.txt`。
- `run_cmd_at_first`：写入本地 `uecommandline.txt` 并推送到游戏根目录。
- `gl`、`vulkan`：写入 `-OpenGLES`、`-vulkan`。
- `llm`、`llmcsv`：写入 `-llm`、`-llmcsv`。
- `push_trace_default`、`push_trace_all`、`push_trace_net`、`push_trace_mem`：写入不同 UE Insights Trace 通道组合。
- `push_noupdate`：写入阻止游戏更新的启动参数；`run`：按配置启动应用。

## 内存测试工作流

`Pipeline/pip_main.py` 将手动测试串为三个流程：

1. `pip mem_pre`：推送 `-llm`，等待 5 秒，启动游戏，并提示测试人员在设备侧手动执行 `memreport -full`。
2. `pip mem`：抓取 Android meminfo，等待 2 秒，拉取 Saved；归档数据和清理旧 memreport 仍是人工步骤。
3. `pip mem_export <目录>`：清空并创建 `<目录>/Export`，选取第一份 `meminfo_*.txt` 和第一份 `.memreport`，执行全部解析，再打开导出目录。

该工具不能自动执行游戏内 UE 控制台命令，`memreport -full` 必须由测试人员在合适场景手动触发。

## Android `dumpsys meminfo` 解析

`Profile/android_mem_info.py` 只接受首行含 `Applications Memory Usage` 的文本，解析指标为：

- `TOTAL`、`Native Heap`、`Dalvik Heap`、`Gfx dev`、`.so mmap`。
- `GL mtrack`、`EGL mtrack`、`Unknown`。
- `Java Heap`、`Code`、`Graphics`。

当前主要读取 PSS 列，而非 PSS + Swap。代码内部有两次快照差分函数，但标准命令流程只输出单次结果。产物名 `mem_ainfo.xlsx` 实际是 UTF-8 制表符文本，并不是真实 XLSX 文件。

## UE MemReport 离线解析

`Profile/mem_report.py` 是核心离线解析模块。它按固定行标签和区段标记读取 MemReport，再生成可由 Excel 打开的制表符文本。

### 内存摘要

从 `Changelist:` 开头的 MemReport 中提取：

- Wwise：`SoundEngine Reserved`、`SoundBank`；Lua：`Lua Memory`。
- 纹理组：16Bit、Pixels2D、UI、Effects、Weapon、Character、World，以及对应 NormalMap。
- Texture Streaming：Average Required PoolSize、Wanted Mips、NonStreaming Mips。
- Shader、RHI Buffer/Texture/Render Target、Font、FName。
- LLM Platform：FMalloc、Overhead、Tracked Total、Total、Vulkan Driver GPU/CPU、Staging、FrameTemp、Shader、RT、Texture、Buffer 等。
- LLM Full：GPUScene、UI、引擎初始化、Physics、Navigation、Niagara、Material、Mesh、Texture、Animation、UObject、RHI、Audio、Tracked/Untracked/Total 等。

输出是 `mem_aummary.xlsx`、`mem_aummary.txt`；`aummary` 为原代码拼写错误，`.xlsx` 实际仍是制表符文本。类内已有摘要差分方法，但没有命令行入口来选择两份报告并输出对比。

### 资源明细导出

| 类别 | 主要输出 |
| --- | --- |
| Render Target Pool | `mem_rt_pool.xlsx`、`mem_rt_pool.txt` |
| `listtextures` | `mem_texture_all.xlsx/.txt`、汇总 txt |
| `listtextures nonstreaming` | `mem_texture_nonstreaming.xlsx/.txt`、汇总 xlsx/txt |
| `listtextures uncompressed` | `mem_texture_uncompressed.xlsx/.txt`、汇总 xlsx/txt |
| `StaticMesh` | `mem_obj_mesh.xlsx/.txt`、汇总 txt |
| `SkeletalMesh` | `mem_obj_skeletal.xlsx/.txt`、汇总 txt |
| Object Class | `mem_obj_class.xlsx/.txt`、汇总 txt |
| Actor | `mem_obj_actor.xlsx/.txt`、汇总 xlsx/txt |

纹理和对象明细由 `Profile/mem_util.py` 依据开始标记、结束标记和汇总标签切分，再转为制表符列。

## 静态相机性能快照报告

`Profile/export_static_cam_html.py` 面向项目内专用日志埋点，不是泛用 UE 性能日志解析器。

输入：

- `./Asset/Saved/Logs/XGame.log`
- `./Asset/Saved/Screenshots/*.png`

处理过程：

1. 解析 OS、设备名称、GPU 厂商、Vulkan 可用性和版本。
2. 截取 `!!!Do Perf Start!!!` 至 `!!!Do Perf End!!!` 之间的 `Perf:` 行。
3. 从 `PointNum:` 获取相机数量。
4. 每个相机按固定 14 行格式解析名称、Frame/Game/Draw/RHI/GPU 时间、内存、DC、三角形数。
5. 日志中断时，按已成功解析的相机数继续并标记数据不完整。
6. 按文件名排序，取最新的 `相机数 × 11` 张 PNG，每 11 张关联一个相机。
7. 计算所有相机的平均指标，导出 `perf.html` 与 `perf.xlsx`；后者仍是制表符文本。

当前展示阈值：帧时间超过 `33.4 ms` 标红；DC warning 与 error 都是 `500`，所以 warning 区间不可达；三角形数超过 `500,000` 标黄、超过 `700,000` 标红。HTML 中的“简单结论”是固定文字，不会根据数据自动生成。

## 工具命令与 RenderDoc 脚本

- `Tool/tool.py`：写入覆盖 CPU/GPU/内存/音频通道的完整 UE Trace 参数。
- `Tool/usefull_info.py`：打印 UE 地图、Perfetto、无线 ADB 等手工命令参考。
- `Utility/io_utility.py`：目录创建/清空、文件枚举、文本/CSV 读写、打开资源管理器等基础工具。

`RenderDoc` 目录不被 `entry.py` 调用，应视为 GUI 的可选高级模块：

| 脚本 | 功能 |
| --- | --- |
| `rd_export_duration.py` | 多次抓取目标 Pass 的 `EventGPUDuration`，按 5 次平均导出耗时和占比。 |
| `rd_detailInfo.py` | 导出 Action、GPU 耗时、索引数。 |
| `rd_saveTextures.py` | 按 draw call 导出颜色、深度、VS/PS/CS 输入纹理 JPG。 |
| `rd_saveSceneColor.py` | 导出 SceneColor 或 BackBuffer JPG。 |
| `rd_frame_info.py` | 统计目标 Pass draw call、三角形、网格材质，可导出 BackBuffer。 |
| `rd_frame_statistics.py` | 分析移动端 BasePass，导出输入/输出纹理和 `base_pass.html`。 |
| `frame_statistics_rd.py` | 扩展 PreZ、ShadowDepth、阴影 Atlas 分析。 |
| `frame_statistics_copy_gbufferA.py` | 筛选并复制 `GBufferA` 附件图。 |

这些脚本硬编码 `D:/RenderDoc` 或 `D:/renderdoc` 输出路径，部分流程会清空输出目录。

## 数据流汇总

### Android 内存测试

```text
选择项目配置（包名、项目名、路径）
  → 连接 ADB 设备
  → 写入 uecommandline.txt（例如 -llm）
  → 启动游戏
  → 人工执行 memreport -full
  → dumpsys meminfo <package>
  → 拉取 Saved/Logs、Screenshots、Profiling、GPUDumps
  → 选择归档目录
  → 解析 meminfo + memreport
  → Export/ 下生成可由 Excel 打开的制表符文本
```

### 静态性能

```text
XGame.log + Screenshots/*.png
  → 从特定 Perf 埋点解析相机性能
  → 与按名称排序的最新截图分组关联
  → 计算平均值、应用显示阈值
  → perf.html + perf.xlsx（制表符文本）
```

### RenderDoc 帧分析

```text
RenderDoc 捕获 + ReplayController
  → 定位 Scene / MobileSceneRender / 目标 Pass
  → 收集 counters、action、资源、绑定输入和输出附件
  → 导出 JPG、HTML 或 TXT
```

## 旧实现限制与 .NET GUI 重构重点

1. **配置硬编码**：包名、项目名、Activity、设备 IP、本地/远端路径、RenderDoc 目录都写死。GUI 应支持项目配置持久化与最近使用配置。
2. **ADB 可观测性不足**：使用字符串拼接，未校验退出码，缺少完整 stdout/stderr、超时、取消和进度。新实现应使用参数化进程调用和结构化日志。
3. **伪 XLSX**：现有 `.xlsx` 文件并非工作簿。GUI 应输出真实 XLSX，或明确使用 `.csv/.tsv`，不再使用误导性扩展名。
4. **固定格式脆弱**：meminfo、memreport、静态相机日志均依赖固定标签和列位置。解析失败时应显示缺失区段、原始行位置和兼容性错误，不能静默填零。
5. **多文件选择不可控**：旧流程只拿第一份 meminfo/memreport。GUI 应列出候选项的路径、时间、大小，让用户明确选择。
6. **差分能力未暴露**：旧代码已有基础差值逻辑。GUI 应优先支持“基线 vs 当前”的摘要、纹理、对象差异。
7. **静态性能高度项目定制**：日志标签、截图数、阈值和结论模板都应配置化，并先确认当前 UE 埋点仍兼容。
8. **截图关联存在风险**：旧代码假设截图数量足够且能被 11 整除。GUI 应校验缺失和多余图片。
9. **DC 阈值逻辑问题**：warning 与 error 同为 500。GUI 应校验 warning 必须小于 error。
10. **清空目录有风险**：`pull_saved` 与部分 RenderDoc 流程会删除内容。执行前应显示目标目录、要求确认，建议使用时间戳输出目录或备份。
11. **缺少测试资产**：未见样例文件和自动化测试。重构前应收集脱敏 meminfo、memreport、日志样本，为解析器建立金样测试。
12. **RenderDoc 建议后置**：运行环境与普通桌面程序不同。建议先实现 Android/内存/日志 GUI，再研究 CLI、插件或脚本桥接。

## 建议的 GUI 分期

### 第一阶段：核心可用版本

- 项目配置：包名、项目名、Activity、设备 Saved 根目录、本地工作目录。
- 设备页：扫描/选择 ADB 设备、连接状态、启动游戏、Wi-Fi 连接。
- 启动参数页：LLM、OpenGL、Vulkan、Trace 预设及自定义参数。
- 采集页：抓取 `dumpsys meminfo`、拉取 Saved、选择输出目录、显示进度与执行日志。
- 解析页：选择 meminfo/memreport，显示摘要、纹理和对象表，并导出结果。
- 结果浏览：可排序表格、关键摘要与导出位置，而不是只打开资源管理器。

### 第二阶段：分析效率

- 基线与当前的双文件差分。
- 以场景、设备、版本、日期归档的历史趋势。
- 多工作表真实 Excel 导出。
- 可配置的静态相机性能报告与自动结论。

### 第三阶段：高级能力

- 通过项目已有远程通道自动执行 UE 控制台命令。
- RenderDoc 外部分析集成、帧捕获批处理与结果浏览。
- 按设备/场景阈值生成性能门禁报告。

## 推荐模块边界

```text
UnrealKit.Core
├─ ProjectConfiguration       # 项目、路径、阈值、预设
├─ Adb                        # adb 发现、设备、进程、文件传输
├─ Collection                 # meminfo、Saved、日志采集工作流
├─ Parsing
│  ├─ AndroidMemInfoParser
│  ├─ UnrealMemReportParser
│  └─ StaticCameraPerfParser
├─ Analysis                   # 汇总、差分、诊断规则
└─ Export                     # CSV/TSV/XLSX/HTML

UnrealKit.Desktop
├─ 配置与设备界面
├─ 采集工作流界面
├─ 解析、表格与对比结果界面
└─ 日志、进度、取消与错误展示

UnrealKit.RenderDoc（可选，后续）
└─ RenderDoc 集成适配层
```

## 重构验收清单

- 无需修改源码即可切换包名、项目名、Activity 和路径。
- 可选择指定 ADB 设备，而不是依赖默认设备。
- 每条 ADB 操作均展示完整命令、结果、失败原因和可复制日志。
- 采集前明确提示将写入/清空的目录。
- 多份 meminfo/memreport 必须显式选择，不默认取第一份。
- 解析结果可在 GUI 查看，并能导出真实 CSV/TSV/XLSX。
- 异常格式有明确诊断，不输出误导性零值。
- 至少支持一组基线/当前摘要差分。
- 静态相机性能可以校验日志记录与截图数量的一致性。
- 核心解析器使用真实脱敏样本进行自动化测试。

## 源文件索引

| 文件 | 主要职责 |
| --- | --- |
| `entry.py` | 顶级命令分发。 |
| `Android/adb_const.py` | 项目与路径硬编码配置。 |
| `Android/adb_class.py` | ADB 命令封装。 |
| `Android/adb_main.py` | ADB 命令、启动参数、Saved/内存抓取。 |
| `Pipeline/pip_main.py` | 内存测试工作流入口。 |
| `Pipeline/pip_mem.py` | 归档目录的 meminfo/memreport 批量导出。 |
| `Profile/android_mem_info.py` | Android `dumpsys meminfo` 解析。 |
| `Profile/mem_report.py` | UE MemReport 摘要、纹理、对象解析。 |
| `Profile/mem_util.py` | MemReport 区段切分与表格转换。 |
| `Profile/export_static_cam_html.py` | 静态相机日志/截图性能 HTML 报告。 |
| `Tool/usefull_info.py` | 常用命令参考。 |
| `Utility/io_utility.py` | 文件、目录、文本读写工具。 |
| `RenderDoc/*.py` | 独立 GPU 帧分析脚本。 |
