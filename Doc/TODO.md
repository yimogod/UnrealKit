# UnrealKit 第一阶段核心可用版本 TODO

最后更新：2026-08-09

本文合并自 Doc/开发进度记录.md 和 Doc/第一阶段核心可用版本详细TODO.md，并根据项目实际代码状态更新。实现范围以旧版工具核心能力为准，详见 Doc/旧版Python性能检查工具功能分析.md。

## 目标

交付一个可用的 Windows 桌面工具，供 UE 工程师日常使用：

- 创建/打开 .ukit 工程并保存项目配置。
- 明确选择 ADB 设备，写入启动参数、启动游戏、抓取 Android 内存并拉取 Saved 数据。
- 导入或选择采集数据，解析 Android meminfo 和 UE memreport。
- 在 GUI 中浏览关键结果，并导出真实 CSV/TSV/XLSX。
- 通过 CLI 完成同一批核心操作，复用 GUI 的核心业务逻辑。

## 当前状态

- 实现目录：UnrealKit/
- 解决方案：UnrealKit/UnrealKit.sln
- 目标框架：.NET 9；桌面端为 WPF（Windows）
- 当前阶段：M2 收尾 / M3 Capture 采集完善
- 构建：0 警告，0 错误
- 测试：57 项全部通过

## 里程碑进度总览

| 里程碑 | 状态 | 说明 |
| --- | --- | --- |
| M0：工程骨架 | ✅ 完成 | 可构建的 Core/CLI/Desktop/Tests 解决方案 |
| M1：.ukit 与配置 | ✅ 完成 | 可创建、打开、校验工程并读写配置 |
| M2：ADB 基础设施 | ✅ 完成 | ADB 设备、进程执行器、启动参数闭环 |
| M3：Capture 采集归档 | 🔶 部分完成 | 本地导入已实现；ADB 采集运行待完善 |
| M4：解析器与领域结果 | ✅ 完成 | Android meminfo + UE memreport 解析 |
| M5：导出与结果服务 | 🔶 部分完成 | TSV/CSV/XLSX meminfo 导出已完成；MemReport 导出待完善 |
| M6：CLI | 🔶 部分完成 | 项目/设备/启动参数/capture import/export 已覆盖 |
| M7：WPF GUI | 🔶 部分完成 | 导航壳、设备页、启动参数页闭环完成；其余页面待接入 |
| M8：测试、打包与验收 | 🔶 部分完成 | 单元测试覆盖 Core 主要模块；端到端验收待进行 |

---

## M0：解决方案与基础架构 ✅

- [x] 创建 UnrealKit.sln。
- [x] 创建 UnrealKit.Core 类库：领域模型、业务服务和抽象接口；不引用 WPF、CLI 框架或特定 AI SDK。
- [x] 创建 UnrealKit.Cli 控制台项目：引用 UnrealKit.Core。
- [x] 创建 UnrealKit.Desktop WPF 项目：引用 UnrealKit.Core。
- [x] 创建 UnrealKit.Tests 测试项目：引用 UnrealKit.Core，使用 xUnit。
- [x] Directory.Build.props 统一启用 Nullable、隐式 using、最新语言版本和警告视为错误。
- [x] 共享操作基础设施：OperationInfrastructure.cs（OperationProgress、LogEvent、IOperationLogger）。
- [x] 应用版本信息：AppVersionInfo.cs（版本、构建时间、可选 Git commit）。
- [x] .gitignore 适配 Unreal Engine 与 .NET/WPF。

## M1：.ukit 工程与配置 ✅

### 工程模型

- [x] 实现 ProjectModels.cs、IniDocument.cs、IProjectService.cs、ProjectService.cs。
- [x] 支持创建、打开、校验 .ukit 工程（UTF-8 INI，格式版本 1）。
- [x] 创建工程生成：<Name>.ukit、Config/DefaultGame.ini、Content/、Saved/、Intermediate/。
- [x] 非空目标目录被拒绝，避免静默覆盖。
- [x] 校验报告格式版本、必需目录、工程名称和缺失配置的结构化诊断。
- [x] 配置优先级：内置默认值 < .ukit 描述符 < Config/DefaultGame.ini < 显式 CLI/GUI 参数。

### CLI

- [x] project create <directory> --name <name>
- [x] project info <project.ukit> [--format json]
- [x] project validate <project.ukit>

## M2：ADB 基础设施 ✅

### 安全进程执行器

- [x] ProcessRunner 使用 ProcessStartInfo.ArgumentList，不通过拼接传递参数。
- [x] 记录/返回：退出码、stdout、stderr、开始/结束时间和耗时。
- [x] 支持取消令牌、超时、超时/取消时终止整个进程树、进度与结构化日志。

### ADB 设备与服务

- [x] db devices -l 解析支持制表符与空格分隔，识别：序列号、device/offline/unauthorized、产品、型号、设备名和 USB/Wi-Fi 类型。
- [x] 已实现：版本检查、db connect/disconnect、设备枚举、Wi-Fi 连接（含端口默认）。
- [x] AdbPathResolver 支持路径配置与诊断。
- [x] CLI device list --project <project.ukit> 列出设备和版本信息。
- [x] CLI device connect --address <host:port> [--project <project.ukit>]
- [x] 设备自动选择策略：--device auto 在仅有单台 device 状态设备时自动选择。

### 启动参数与命令

- [x] LaunchParameterService 支持预设：LLM、LLM CSV、OpenGL、Vulkan、No Update、Trace 和自定义参数。
- [x] CLI pp start --project <project.ukit> --device <serial|auto> [--preset <name>] [--params <args>]
- [x] CLI commandline push --project <project.ukit> --device <serial|auto> [--params <args>]
- [x] CLI commandline delete --project <project.ukit> --device <serial|auto>
- [x] 确保 EnsureOnlyOptions 支持 --include-details 等无值标志选项。

### WPF 设备与启动参数页

- [x] 设备页已接入 IAdbService：刷新设备列表、显示序列号/状态/型号、显式选择设备、Wi-Fi 连接和错误状态。
- [x] 启动参数页已读取项目预设并预览最终 uecommandline.txt 内容及远端路径。
- [x] 推送、启动、删除操作均使用选定设备和项目包名/Activity；页面显示设备、包名、Activity 和远端路径，删除前弹出确认框。
- [x] ADB 调用复用 ProcessRunner，通过异步命令、IOperationProgress、stdout/stderr 流式日志绑定 WPF 状态栏和日志列表。
- [x] 新增 DesktopShellViewModelTests，使用模拟 ADB 验证推送→启动→删除闭环及删除拒绝路径。
- [x] WPF ShellViewModel.UpdateDevices 自动预选：刷新后若仅有一台可用设备自动预选。

## M3：Capture 采集归档 🔶

### 本地导入 ✅

- [x] CaptureModels.cs 新增 CaptureImportRequest 模型（Platform、SourceDirectory、Tag、CaptureId）。
- [x] ICaptureService 新增 ImportAsync(CaptureImportRequest) 方法。
- [x] CaptureService.ImportAsync：将本地目录完整复制到 Content/<Platform>/<Tag>/<date>/<CaptureId>/，计算 SHA-256 文件清单，生成 CaptureManifest.json；不修改导入源目录。
- [x] CLI capture import --project <project.ukit> --source <directory> [--platform <platform>] [--tag <tag>] [--capture-id <id>]

### ADB 采集运行（待完成）

- [ ] CaptureService.CaptureAsync：通过 ADB 创建新 Capture，抓取 dumpsys meminfo 并拉取 Saved（Logs/Screenshots/Profiling/GPUDumps）。
- [ ] 采集完成后自动生成 CaptureManifest.json（CaptureId、平台、标签、时间、项目配置快照、ADB 设备序列号/型号、文件清单和校验信息）。
- [ ] 支持 --include-saved、--include-screenshots 等细粒度控制拉取内容。
- [ ] 对已存在的 Capture 默认只读；重新拉取创建新 Capture 或由用户明确选择覆盖。
- [ ] CLI capture run --project <project.ukit> --device <serial|auto> [--tag <tag>] [--include-saved] [--include-screenshots]
- [ ] WPF 采集页：选择 Tag、选择拉取内容、显示进度和日志、完成后导航到结果页。

## M4：解析器与领域结果 ✅

### Android meminfo 解析

- [x] AndroidMemInfoModels.cs、IAndroidMemInfoParser.cs、AndroidMemInfoParser.cs
- [x] 解析 TOTAL、Native/Dalvik Heap、Gfx dev、.so mmap、GL/EGL mtrack、Unknown、Java Heap、Code、Graphics 等关键指标。
- [x] --include-details 输出全部 PSS 详细条目、Dalvik 和 Objects 明细。
- [x] 解析失败时给出具体缺失段落、行号或格式原因，不静默以零值替代。
- [x] 样例数据：7 个 meminfo 脱敏样本（完整、OEM 详细、OEM 重排 PSS、重复节、损坏详细、缺少 TOTAL、截断节）。

### UE MemReport 解析

- [x] UnrealMemReportModels.cs、IUnrealMemReportParser.cs、UnrealMemReportParser.cs
- [x] 摘要：Wwise、Lua、Texture Group、Texture Streaming、Shader、RHI、Font/FName、LLM Platform、LLM Full。
- [x] 明细：Render Target Pool、全部 NonStreaming/Uncompressed 纹理、StaticMesh、SkeletalMesh、Object Class、Actor。
- [x] 样例数据：1 个 memreport 完整明细样本。

## M5：导出与结果服务 🔶

### TSV/CSV 导出 ✅

- [x] MemInfoExportService：CSV、TSV 文本导出，带 Metadata。
- [x] CLI export meminfo --project <project.ukit> --input <file> --format csv|tsv [--include-details]

### XLSX 导出 ✅

- [x] 引入 ClosedXML NuGet 依赖（v0.104.2，MIT 许可）到 UnrealKit.Core。
- [x] XlsxMemInfoExportService 与 IXlsxMemInfoExportService。
- [x] XLSX 工作簿结构：
  - **Metadata** 工作表：输入文件、Capture ID、解析时间、工具版本、Git commit、进程名/ID。
  - **AndroidMemInfo** 工作表：Java/Native Heap、Code、Stack、Graphics、Private/System、TOTAL PSS 指标。
  - **PSS Details** 工作表（--include-details）：全部详细 PSS 条目含 Total/Private Dirty/Clean/Swap/RSS/Heap。
  - **Dalvik** 工作表（--include-details）：Dalvik 条目含 PSS。
  - **Objects** 工作表（--include-details）：对象条目含计数。
  - **Diagnostics** 工作表：解析诊断含严重级别、代码、消息、行号、建议修复。
- [x] CLI export meminfo --output <file.xlsx> 自动识别 .xlsx 扩展名并路由到 XLSX 服务。

### 待完成

- [ ] MemReport 导出（TSV/CSV/XLSX）—摘要 + 明细工作表。
- [ ] 导出时保存输入来源、时间、工具版本和配置快照（MemReport 同 meminfo 标准）。
- [ ] CLI export memreport --project <project.ukit> --input <file> --format csv|tsv|xlsx [--include-details]
- [ ] 多文件批量导出支持。

## M6：CLI 全覆盖 🔶

### 已实现 ✅

- [x] project create、project info、project validate
- [x] device list、device connect
- [x] pp start
- [x] commandline push、commandline delete
- [x] capture import
- [x] export meminfo（CSV/TSV/XLSX）
- [x] --device auto 自动设备选择策略
- [x] 退出码规范：成功 0，参数/设备/解析/导出失败非零。
- [x] 歧义输入必须报错（多设备时不隐式选择）。

### 待完成

- [ ] capture run（依赖 M3 ADB 采集完成）
- [ ] capture list / capture info：列举和查看已有 Capture 详情。
- [ ] export memreport（依赖 M5 MemReport 导出完成）
- [ ] parse meminfo / parse memreport：独立解析命令，不依赖导出。
- [ ] 全局 --format json 支持机器可读输出（部分命令已支持）。
- [ ] 非交互式、可审计的 Agent 分析 CLI 入口（依赖 Agent 适配层）。

## M7：WPF GUI 完善 🔶

### 已完成 ✅

- [x] 导航壳（MainWindow + ShellViewModel）：工程、设备、启动参数、采集、解析、结果、日志与设置页面。
- [x] 设备页面：完整 ADB 设备管理闭环。
- [x] 启动参数页面：预设选择、参数预览、推送/启动/删除闭环。
- [x] DesktopOperationServices：GUI 的 ADB 操作适配层。

### 待完成

- [ ] 工程页面：创建工程、打开工程、最近工程、工程信息展示与校验结果。
- [ ] 采集页面：Tag 选择、拉取内容选择、采集进度与日志、完成后自动跳转。
- [ ] 解析页面：Capture 文件选择（meminfo/memreport）、解析执行、诊断展示。
- [ ] 结果页面：摘要/明细数据表格浏览、列排序、过滤。
- [ ] 导出页面：格式选择（CSV/TSV/XLSX）、IncludeDetails 开关、导出进度。
- [ ] 日志与设置页面：操作日志查看、项目配置编辑、ADB 路径配置。
- [ ] 业务逻辑保持在 ViewModel/Core 中，不在 code-behind 中实现业务流程。
- [ ] GUI 操作无阻塞：所有长时操作使用异步 API，保持 UI 响应。

## M8：测试、打包与验收 🔶

### 测试 ✅

- [x] 解析器测试：7 个 meminfo 脱敏样本 + 1 个 memreport 金样测试。
- [x] ProjectServiceTests：创建、打开、校验、非空目录拒绝。
- [x] AdbServiceTests、AdbPathResolverTests：ADB 服务与路径诊断。
- [x] LaunchParameterServiceTests：启动参数预设与自定义参数。
- [x] ProcessRunnerTests：进程执行、超时、取消。
- [x] CaptureServiceTests：本地导入与 Manifest 生成。
- [x] MemInfoExportServiceTests：CSV/TSV 导出验证。
- [x] DesktopShellViewModelTests：设备自动选择、推送→启动→删除闭环。

### 待完成

- [ ] Capture 采集运行（ADB）的集成测试。
- [ ] XLSX 导出的自动化单元测试（格式与内容校验）。
- [ ] MemReport 解析与导出的单元测试。
- [ ] 端到端验收：创建工程 → ADB 采集 → 解析 → 导出 XLSX/CSV/TSV 完整链路。
- [ ] 无设备、多设备、外部命令失败、解析失败、用户取消和覆盖确认等异常路径验证。
- [ ] 检查取消长时间拉取后 GUI 仍可操作，日志包含已完成与未完成步骤。
- [ ] 检查清理/覆盖操作需要确认，默认采集不删除历史 Capture。
- [ ] 检查 GUI 与 CLI 对同一 Capture 的解析、诊断和导出关键数值一致。

### 发布

- [ ] 选择并实现发布方式：Windows self-contained 发布包或安装包。
- [ ] 确保应用启动时能诊断缺少 adb，而非崩溃。
- [ ] 提供简短 README：安装、创建 .ukit 工程、配置 ADB、一次采集、一次解析、常用 CLI 命令。
- [ ] 提供变更记录，列出旧 Python 工具中第一阶段已兼容、暂未兼容和有意改变的行为。

---

## 第一阶段 Definition of Done

以下条件全部满足时，第一阶段才算完成：

- [x] Core、CLI、Desktop、Tests 工程存在且可构建；CLI/GUI 不重复业务逻辑。
- [x] 可创建、打开、校验 .ukit 工程，且能通过 Config/DefaultGame.ini 保存项目默认配置。
- [x] 用户可明确选择 ADB 设备，管理连接，推送/删除启动参数并启动应用。
- [ ] 可创建新 Capture，抓取 Android meminfo 和选定 Saved 内容，生成完整或带失败信息的 Manifest。
- [x] 可导入本地采集目录，且不修改导入源和 Content/ 原始数据。
- [x] 可明确选择 meminfo/memreport，解析旧工具覆盖的核心摘要和明细，并输出可读诊断。
- [ ] GUI 可浏览关键结果与表格；CLI 可完成等价的项目、设备、采集、解析、导出操作。
- [x] 可导出真实 XLSX、CSV、TSV，并保留来源、配置和版本信息。
- [x] 核心解析、工程、采集归档和导出拥有脱敏样本自动化测试。
- [ ] 已验证无设备、多设备、外部命令失败、解析失败、用户取消和覆盖确认等关键异常路径。

---

## 已知限制

- ProcessRunner 当前在进程结束后一次性读取 stdout/stderr；TODO 中要求的"流式输出到 GUI/CLI"尚未实现。
- ADB 路径当前只接受构造参数或 PATH 中的 db；尚未实现从项目设置、环境变量和 PATH 的完整优先级诊断。
- CaptureService.CaptureAsync（基于 ADB 的真实采集）尚未完成，目前仅有本地目录导入。
- WPF 的工程、采集、解析、结果、日志和设置页面仍为导航/占位壳，未接入对应服务。
- MemReport 导出（XLSX/CSV/TSV）尚未实现。
- CLI 部分命令（capture run、capture list、parse memreport 等）尚未实现。
- Agent 分析能力尚未开始；模型适配层和提供方接口已预留但未实现。
- 静态相机性能报告、基线差分、历史趋势和 RenderDoc 集成不在第一阶段范围内。

---

## 建议实施批次

1. **批次 A**：M0 + M1 ✅ — .ukit 工程、配置读写、项目 CLI 和 GUI 空壳。
2. **批次 B**：M2 ✅ — 设备枚举、设备选择、启动参数推送/删除、应用启动和执行日志。
3. **批次 C**：M3 🔶 — 带 Manifest 的 Capture 采集/导入；可开始真实归档测试数据。
4. **批次 D**：M4 ✅ — Android meminfo 和 MemReport 摘要，再补全纹理/对象明细。
5. **批次 E**：M5 + M6 🔶 — 结果表格、真实导出和端到端 CLI。
6. **批次 F**：M7 + M8 🔶 — 完善 WPF 工作流、样本测试、异常路径、发布和用户文档。