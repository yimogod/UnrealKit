# 第一阶段：核心可用版本详细 TODO

## 1. 目的与范围

本文把 `Doc/旧版Python性能检查工具功能分析.md` 中“第一阶段：核心可用版本”拆解为可执行的开发任务。该阶段的目标不是一次性复刻旧 Python 脚本的所有能力，而是交付一个可由 UE 工程师日常使用的 Windows 工具：

- 能创建/打开 `.ukit` 工程并保存项目配置。
- 能明确选择 ADB 设备，写入启动参数、启动游戏、抓取 Android 内存并拉取 Saved 数据。
- 能导入或选择采集数据，解析 Android meminfo 和 UE memreport。
- 能在 GUI 中浏览关键结果，并导出真实 CSV/TSV/XLSX。
- 能通过 CLI 完成同一批核心操作，并复用 GUI 的核心业务逻辑。

本阶段不实现：静态相机性能报告、基线差分、历史趋势、自动 UE 控制台命令、RenderDoc 集成和实际模型驱动的 Agent 分析。工程目录、Capture 清单和 Agent 报告预留规则必须先落实，但 Agent 执行功能可以后置。

## 2. 交付边界

### 2.1 支持的数据与旧工具兼容目标

| 类别 | 第一阶段必须支持 |
| --- | --- |
| Android | `adb devices`、设备序列号选择、`adb connect`、启动应用、`adb push/pull`、`dumpsys meminfo`。 |
| 启动参数 | 写入/推送/删除 `uecommandline.txt`，支持 LLM、LLM CSV、OpenGL、Vulkan、4 个 Trace 预设和自定义参数。 |
| 采集数据 | meminfo 文本、UE Saved 的 Logs/Screenshots/Profiling/GPUDumps，以及导入已有本地采集目录。 |
| Android 内存 | `TOTAL`、Native/Dalvik Heap、Gfx dev、`.so mmap`、GL/EGL mtrack、Unknown、Java Heap、Code、Graphics。 |
| UE MemReport 摘要 | Wwise、Lua、Texture Group、Texture Streaming、Shader、RHI、Font/FName、LLM Platform、LLM Full。 |
| UE MemReport 明细 | Render Target Pool、全部/NonStreaming/Uncompressed 纹理、StaticMesh、SkeletalMesh、Object Class、Actor。 |
| 导出 | CSV、TSV、真实 XLSX；保存输入来源、时间、工具版本、配置快照。 |

### 2.2 第一阶段验收路径

```text
创建或打开 .ukit 工程
  → 填写/加载项目配置
  → 枚举并选择 ADB 设备
  → 写入 -llm 并启动游戏
  → 创建新的 Capture，抓取 meminfo 并拉取 Saved
  → 在 Capture 中明确选择 meminfo 和 memreport 文件
  → 解析、查看摘要/纹理/对象表格
  → 导出真实 XLSX/CSV/TSV 到 Saved/Exports
  → 从 CLI 重复至少一次采集或离线解析流程
```

## 3. 实施顺序与里程碑

| 里程碑 | 依赖 | 完成结果 |
| --- | --- | --- |
| M0：工程骨架 | 无 | 可构建的 Core/CLI/Desktop/Tests 解决方案。 |
| M1：`.ukit` 与配置 | M0 | 可创建、打开、校验工程并读写配置。 |
| M2：ADB 基础设施 | M0、M1 | 可安全执行和记录所有需要的 ADB 操作。 |
| M3：Capture 采集归档 | M1、M2 | 拉取数据进入不可变的 Content Capture，并生成 Manifest。 |
| M4：解析器与领域结果 | M0 | 可解析 meminfo/memreport，并提供明确诊断。 |
| M5：导出与结果服务 | M4、M1 | 可导出真实 XLSX/CSV/TSV，并保留来源信息。 |
| M6：CLI | M1-M5 | 可用命令行完成项目、ADB、采集、解析、导出。 |
| M7：WPF GUI | M1-M5 | 可完成首阶段人工工作流。 |
| M8：测试、打包与验收 | M0-M7 | 样本测试、端到端验证、可分发构建。 |

## 4. M0：解决方案与基础设施

### TODO

- [ ] 创建 `UnrealKit.sln`。
- [ ] 创建 `UnrealKit.Core` 类库：仅包含领域模型、业务服务和抽象接口；不得引用 WPF、CLI 框架或特定 AI SDK。
- [ ] 创建 `UnrealKit.Cli` 控制台项目：引用 `UnrealKit.Core`。
- [ ] 创建 `UnrealKit.Desktop` WPF 项目：引用 `UnrealKit.Core`。
- [ ] 创建 `UnrealKit.Tests` 测试项目：引用 `UnrealKit.Core`，并准备测试样本目录。
- [ ] 统一目标 .NET 版本、Nullable、隐式 using、分析器和格式化规则。
- [ ] 添加版本信息服务：应用版本、Git commit（若构建时可用）、构建日期。
- [ ] 建立全局日志抽象：分级日志事件、时间、操作 ID、可选结构化属性；CLI、GUI、文件日志共用同一事件模型。
- [ ] 建立可取消操作模型：每个长操作接受 `CancellationToken`，并报告阶段、当前项、总项、消息和错误。
- [ ] 确定依赖并记录用途：CLI 参数库、INI 解析库、真实 XLSX 导出库、测试框架。优先选择成熟、许可兼容的库。

### 完成标准

- `dotnet build` 可构建全部项目。
- Core 不引用 Desktop/CLI，也不依赖 WPF 类型。
- Desktop 不在 code-behind 中承载业务逻辑；CLI 不实现业务逻辑。
- 任何后台操作都可传入取消令牌和日志接收器。

## 5. M1：`.ukit` 工程与配置

### 5.1 数据模型

- [ ] 定义 `UkitProjectDescriptor`：`FormatVersion`、`ProjectName`、`ContentRoot`、`ConfigRoot`、`SavedRoot`、`IntermediateRoot`。
- [ ] 定义 `ProjectSettings`：包名、UE 项目名、Activity、设备 Saved 根目录模板、本地工作目录、ADB 默认路径、启动参数预设、采集子目录和导出默认值。
- [ ] 定义 `ProjectConfigurationSnapshot`：每个 Capture 和导出结果均可保存当前配置快照。
- [ ] 定义 `ProjectValidationResult`：错误、警告、修复建议和关联路径。
- [ ] 定义明确的配置优先级：内置默认值 < `.ukit` < `Config/DefaultGame.ini` < CLI 显式参数/GUI 本次操作值。

### 5.2 工程创建、打开与校验

- [ ] 实现 `CreateProjectAsync`：校验工程名与目标目录，创建 `<Name>.ukit`、`Config/`、`Content/`、`Saved/`、`Intermediate/`。
- [ ] `.ukit` 使用 UTF-8 INI，至少写入 `FormatVersion=1`、工程名和四个根目录名。
- [ ] 创建 `Config/DefaultGame.ini` 模板，保留常用 UE 风格节名与字段说明；不写入密钥或敏感信息。
- [ ] 对非空目录、同名 `.ukit`、不可写目录提供拒绝或明确确认，不允许静默覆盖。
- [ ] 实现 `OpenProjectAsync`：定位 `.ukit`、加载配置、检查根目录和格式版本。
- [ ] 实现 `ValidateProjectAsync`：检查版本兼容性、路径合法性、配置必填项、目录读写权限与配置冲突。
- [ ] 实现最近工程记录；最近列表只记录 `.ukit` 路径，不复制工程配置。
- [ ] 为未来格式升级预留迁移接口；第一阶段至少能识别未知的新格式版本并拒绝以避免误写。

### 5.3 CLI 与 GUI 骨架

- [ ] CLI：`project create <directory> --name <name>`。
- [ ] CLI：`project info <project.ukit>`，可选 `--format json`。
- [ ] CLI：`project validate <project.ukit>`。
- [ ] GUI：创建工程对话框、打开工程、最近工程列表和工程信息页。

### 完成标准

- 新建工程生成约定的目录树和可读的 `.ukit`/INI 文件。
- 工程被手动修改后，校验结果能指出具体字段或路径问题。
- GUI 与 CLI 打开同一工程后读取相同配置。

## 6. M2：ADB 基础设施与启动参数

### 6.1 外部进程执行器

- [ ] 实现通用 `ProcessRunner`：使用 `ProcessStartInfo.ArgumentList`，禁止拼接 shell 命令。
- [ ] 捕获命令、工作目录、开始/结束时间、退出码、stdout、stderr、超时和取消状态。
- [ ] 支持流式输出，供 GUI 日志面板和 CLI 实时显示。
- [ ] 定义外部命令错误：找不到 adb、超时、取消、非零退出码、输出解析失败。
- [ ] 支持从设置、环境变量和 PATH 查找 adb；启动前提供诊断。

### 6.2 ADB 服务

- [ ] 定义 `AdbDevice`：序列号、状态、产品/型号/设备名、连接类型、原始行。
- [ ] 实现 `ListDevicesAsync` 并解析 `adb devices -l`。
- [ ] 明确拒绝设备选择歧义：一个命令需要序列号；未指定时仅在唯一可用设备的规则经调用方确认后使用，否则报错。
- [ ] 实现 `ConnectAsync(host:port)`、`DisconnectAsync(host:port)`、`TcpIpAsync(port)`、`StartServerAsync`、`KillServerAsync`。
- [ ] 实现 `StartApplicationAsync(serial, package, activity)`。
- [ ] 实现 `PushFileAsync`、`PullDirectoryAsync`、`DeleteRemoteFileAsync`、`RunDumpsysAsync`，所有设备操作携带 `-s <serial>`。
- [ ] 统一远端路径格式、引用规则和错误提示；不信任用户输入的参数作为命令片段。

### 6.3 启动参数服务

- [ ] 定义预设：LLM、LLM CSV、OpenGL、Vulkan、Trace Default、Trace All、Trace Network、Trace Memory、No Update。
- [ ] 保留旧工具的参数内容，作为默认预设；将预设名称、内容、描述、是否允许叠加放入项目配置。
- [ ] 实现写入本地临时 `uecommandline.txt`、推送到设备游戏根目录、删除设备文件。
- [ ] 提供自定义参数文本，显示最终将写入的完整内容。
- [ ] 推送、删除和启动应用在 GUI 中必须显示目标设备、远端路径和确认操作；CLI 必须通过显式参数执行。

### 完成标准

- 选择指定设备后能完成“推送 `-llm` → 启动应用 → 删除启动参数”。
- 任一 ADB 失败都保留可复制的命令、退出码和 stderr。
- GUI 长操作不冻结；用户取消后可得到明确的已取消状态。

## 7. M3：Capture 采集与 Content 归档

### 7.1 Capture 模型

- [ ] 定义 `CaptureRequest`：平台、Tag、日期、设备、采集类型、是否拉取各 Saved 子目录、项目配置快照。
- [ ] 定义 `CaptureMetadata`：CaptureId、开始/结束时间、实际 ADB 命令、设备信息、远端路径、文件记录、警告与失败步骤。
- [ ] 定义 `CaptureFileEntry`：相对路径、大小、最后修改时间、可选 SHA-256、来源类型。
- [ ] 实现唯一 `CaptureId` 生成：建议 `yyyyMMdd-HHmmss-<deviceSuffix>-<random>`。

### 7.2 采集服务

- [ ] 在 `Content/<Platform>/<Tag>/<YYYY-MM-DD>/<CaptureId>/` 创建新 Capture；默认不复用已有目录。
- [ ] 在 Capture 内保存 `MemInfo/meminfo_<timestamp>.txt`，内容来自 `adb shell dumpsys meminfo <package>`。
- [ ] 根据项目配置的 UE Saved 根路径，按需拉取到 Capture 内的 `Logs/`、`Screenshots/`、`Profiling/`、`GPUDumps/` 或 `Saved/`。
- [ ] 对每个子步骤报告进度：创建目录、meminfo、每个 pull、文件清单、写 Manifest。
- [ ] 即使部分拉取失败，也要写入 Manifest，并在 Capture 中标注不完整状态；不应伪装为完整成功。
- [ ] 默认不删除本地目录；如果用户选择覆盖/清理，GUI 必须显示绝对路径并明确确认。
- [ ] 实现已有本地采集资料导入：复制到新 Capture、生成 Manifest、保留来源路径；不得移动或改写导入原件。
- [ ] 计算文件清单，第一阶段默认记录文件大小和修改时间；对关键报告文件可选计算 SHA-256。

### 7.3 CLI 与 GUI

- [ ] CLI：`capture collect --project <.ukit> --device <serial> --platform Android --tag <tag>`，并提供 `--include`、`--output`、`--format json`。
- [ ] CLI：`capture import --project <.ukit> --source <directory> --platform <platform> --tag <tag>`。
- [ ] GUI：采集页提供 Platform、Tag、设备、Saved 子目录选择、目标 Capture 预览、执行日志、取消和完成后打开结果。
- [ ] GUI：所有覆盖/删除类动作需二次确认；默认行为是新建 Capture。

### 完成标准

- 一次完整采集会生成可追溯 Capture、原始数据目录和 `CaptureManifest.json`。
- Capture 中能找到 meminfo 与拉取的 Saved 子目录，Manifest 可描述任何失败步骤。
- 导入本地数据不会修改源目录。

## 8. M4：解析器与领域结果

### 8.1 共同解析基础

- [ ] 定义解析结果通用类型：`ParseResult<T>`、`ParseDiagnostic`、严重级别、行号、段落、原始片段引用。
- [ ] 解析器不修改输入文件；只从文件/流读取。
- [ ] 解析失败不得将未知项默认为零值；缺失或异常项必须以诊断呈现。
- [ ] 定义文件候选扫描服务：列出路径、文件名、大小、最后修改时间、推测类型；不自动选择“第一份”。
- [ ] 所有内存值明确保留原始单位及统一显示单位（建议 KiB/MiB），避免隐式换算。

### 8.2 Android meminfo 解析

- [ ] 定义 `AndroidMemInfoSnapshot` 及上述 11 个指标。
- [ ] 按旧工具兼容规则解析 `Applications Memory Usage` 表；记录每个指标的原始行号。
- [ ] 支持 PSS 列读取，并将“是否含 Swap”作为显式元数据或未来可配置策略。
- [ ] 输入不含标题、列不足、数字非法或指标缺失时输出明确诊断。
- [ ] 提供面向 UI 的摘要行模型和面向导出的平面数据模型。

### 8.3 UE MemReport 摘要解析

- [ ] 定义 `UnrealMemReportSummary`：按 Wwise、Lua、Texture Group、Streaming、Shader、RHI、Slate/Object、LLM Platform、LLM Full 分组。
- [ ] 将旧 Python 中的行尾统计标签整理为可维护的映射表，而不是大量散落的 `if`。
- [ ] 解析 MemReport 元信息（例如 Changelist）并校验文件类型。
- [ ] 对同名标签、多次出现标签和缺失标签定义明确策略，并记录诊断。
- [ ] 结果中标记哪些指标来自文件、哪些缺失、哪些无法解析。

### 8.4 UE MemReport 明细解析

- [ ] 实现 Render Target Pool 区段解析。
- [ ] 实现 `listtextures`、`listtextures nonstreaming`、`listtextures uncompressed` 解析与汇总行识别。
- [ ] 实现 StaticMesh、SkeletalMesh、Object Class、Actor 的对象清单解析。
- [ ] 对开始标记、结束标记、表头、汇总行均保留匹配位置；缺失任一必要标记时产生可读诊断。
- [ ] 定义强类型行模型，不在 Core 中保存制表符拼接字符串。
- [ ] 对不规则空格、空行、非数字列、未知列和重复区段添加容错及诊断测试。

### 8.5 解析工作流

- [ ] 实现 `AnalyzeCaptureAsync`：扫描 Capture 中候选 meminfo/memreport，返回候选列表但不擅自选择。
- [ ] 实现 `ParseSelectionAsync`：只解析调用方明确指定的文件，并生成带输入来源的结果对象。
- [ ] 将解析结果缓存为 `Saved/` 下的可再生数据；缓存失效条件包含输入文件信息、解析器版本和关键配置。

### 完成标准

- 使用脱敏样本可解析旧工具当前支持的摘要和主要明细。
- 缺失段落/格式错误可被 UI 和 CLI 精确展示，不产生静默零值。
- 多个候选文件的情况下，GUI/CLI 都要求显式选择。

## 9. M5：结果查看、导出与可追溯性

### 9.1 结果服务

- [ ] 定义 `AnalysisSession`：工程、Capture、选择的输入文件、解析器版本、结果对象、诊断和导出历史。
- [ ] 定义统一表格列元数据：列名、数据类型、单位、排序规则、是否默认显示。
- [ ] 提供摘要卡片数据：Android 总内存、主要 Heap、LLM Total、纹理/RT/对象主要汇总，以及数据完整性状态。
- [ ] 提供纹理、Render Target、Mesh、Actor、Object Class 等可排序行集合。

### 9.2 导出服务

- [ ] 实现 CSV 导出：UTF-8、稳定列顺序、正确引用/转义。
- [ ] 实现 TSV 导出：使用 `.tsv` 扩展名。
- [ ] 实现真实 XLSX 导出：至少一个工作簿，工作表分为 Metadata、AndroidMemInfo、MemReportSummary、RenderTargetPool、Textures、Meshes、Actors、ObjectClasses；缺失数据可省略工作表或注明原因。
- [ ] 每份导出包含 Metadata：工程、CaptureId、输入相对路径、输入文件时间/大小、解析时间、工具版本、配置快照、诊断摘要。
- [ ] 导出默认位置为 `<Project>/Saved/Exports/<AnalysisId>/`；`AnalysisId` 唯一且不会覆盖已有结果。
- [ ] GUI 导出前提供格式、目标目录、文件名预览；CLI 允许显式指定格式和输出目录。

### 9.3 GUI 结果浏览

- [ ] 创建概要页面：Capture 信息、输入选择、数据完整性、关键内存指标、解析警告。
- [ ] 创建可切换的表格视图：Android MemInfo、Summary、Textures、Render Target、StaticMesh、SkeletalMesh、Object Class、Actor。
- [ ] 支持列排序、基础筛选/搜索、数值格式化、单位显示和复制所选行。
- [ ] 提供输入文件、Capture 目录、导出目录的安全打开操作；不把“打开资源管理器”作为查看结果的替代方案。
- [ ] 解析或导出失败时保留错误面板和可复制日志。

### 完成标准

- 可从一次 Capture 中选择输入、解析并在 GUI 中查看结果。
- 导出文件可被 Excel 正常打开，且 `.xlsx` 为真实工作簿。
- 导出包含输入和配置追溯信息，并且不修改 `Content/` 原件。

## 10. M6：CLI 详细命令清单

CLI 命令名可以在实现时微调，但必须保持分组清晰、参数显式、退出码稳定。

| 命令 | 必需能力 |
| --- | --- |
| `project create` | 创建 `.ukit` 工程；支持名称、路径、可选初始配置。 |
| `project info` | 显示工程描述、有效配置、目录状态。 |
| `project validate` | 输出错误、警告与建议；支持 JSON。 |
| `adb devices` | 列出带详情的设备；支持 JSON。 |
| `adb connect` / `adb disconnect` | 明确 host:port 的连接管理。 |
| `app start` | 指定工程和设备序列号，启动配置应用。 |
| `commandline push` | 推送预设或自定义 UE 启动参数。 |
| `commandline delete` | 删除设备 `uecommandline.txt`。 |
| `capture collect` | 创建 Capture，抓取 meminfo 与选定 Saved 子目录。 |
| `capture import` | 导入本地数据到新 Capture。 |
| `capture list` | 列出工程内 Capture、状态、Tag、时间和设备。 |
| `parse candidates` | 列出指定 Capture 的 meminfo/memreport 候选项。 |
| `parse run` | 指定明确输入文件，解析并保存结果。 |
| `export run` | 指定解析结果或 Capture、格式与输出目录进行导出。 |

### CLI TODO

- [ ] 为每个命令定义必填参数、可选参数、默认值和冲突参数。
- [ ] 支持 `--project <path>`、`--device <serial>`、`--output <path>`、`--format text|json` 等一致参数。
- [ ] 将人类可读日志输出到 stderr 或可配置日志流；机器可读 JSON 输出保持纯净、稳定。
- [ ] 定义退出码：`0` 成功；参数/配置错误、外部命令错误、数据缺失/歧义、解析错误、导出错误、取消分别使用稳定非零码。
- [ ] 对任何破坏性操作提供 `--yes` 或显式确认机制；非交互模式未确认时拒绝执行。
- [ ] 为每个命令提供 `--help` 示例，包含典型 `.ukit`、Capture 和设备序列号用法。

### 完成标准

- CLI 能完成创建工程、列设备、采集、列候选、解析、导出的完整链路。
- CLI 与 GUI 使用同一个项目配置、同一解析结果和同一 Capture 目录规则。
- 自动化调用可通过 JSON、退出码和日志判断成功/失败。

## 11. M7：WPF GUI 详细任务

### 11.1 壳与导航

- [ ] 主窗口包含工程状态、当前工程路径、全局操作日志入口和后台任务状态。
- [ ] 导航至少包括：工程、设备、启动参数、采集、解析、结果、日志/设置。
- [ ] 未打开工程时禁用依赖工程的操作，并提供创建/打开引导。
- [ ] 关闭工程或应用时，对于未完成任务提供取消/等待确认。

### 11.2 工程与配置页面

- [ ] 编辑并校验包名、项目名、Activity、UE Saved 路径模板、本地工作目录、ADB 位置、默认 Platform/Tag。
- [ ] 显示配置来源与优先级，避免用户不知道字段来自 `.ukit`、INI 还是本次临时值。
- [ ] 支持保存 `DefaultGame.ini`，保存前验证路径、必填值和阈值。
- [ ] 显示工程目录状态与最近 Capture。

### 11.3 设备与启动参数页面

- [ ] 设备列表显示序列号、状态、型号/产品信息、连接类型，支持刷新与选择。
- [ ] 支持 Wi-Fi 连接地址输入、连接/断开、错误显示。
- [ ] 启动参数预设可查看、选择、组合（仅允许可叠加项）和编辑自定义文本。
- [ ] 推送、删除、启动按钮显示当前设备、应用包名、Activity、远端路径和最终参数。

### 11.4 采集页面

- [ ] 显示 Platform、Tag、设备、当前配置、将创建的 Capture 路径、要拉取的子目录。
- [ ] 提供“只抓 meminfo”“抓 meminfo + Selected Saved”“导入本地目录”操作。
- [ ] 长操作期间显示阶段、进度、当前 ADB 输出、取消按钮和结束状态。
- [ ] 完成后显示 CaptureManifest 摘要、文件数量、警告，并可转到解析页。

### 11.5 解析与结果页面

- [ ] 解析页列出候选 meminfo/memreport，按路径、时间、大小显示；要求用户各自选择。
- [ ] 解析后显示诊断摘要：成功、警告、错误、缺失段落。
- [ ] 结果页显示摘要卡片和表格；表格支持排序、搜索、选择和复制。
- [ ] 导出操作可选择 CSV/TSV/XLSX、默认或自定义输出目录，完成后显示结果文件。

### 完成标准

- 新用户可以无需手工输入命令完成第一阶段验收路径。
- 所有 ADB/I/O/解析/导出工作都在后台执行，GUI 持续可响应。
- 各页面错误信息可理解、可复制且指向具体操作/输入。

## 12. M8：测试、发布和验收

### 12.1 测试资产

- [ ] 收集并脱敏至少一份有效 Android meminfo 样本。
- [ ] 收集并脱敏至少一份完整 UE memreport 样本，覆盖摘要、RT Pool、三种纹理、四种对象区段。
- [ ] 收集格式不完整样本：缺少标题、缺少结束标签、非法数值、多份候选文件等。
- [ ] 将样本和预期结果放入测试项目，不向仓库提交敏感包名、设备 ID、用户路径或游戏资产名。

### 12.2 自动化测试

- [ ] 单元测试：`.ukit` 读写、INI 合并与优先级、工程校验、CaptureId/目录生成。
- [ ] 单元测试：ADB 输出解析、命令参数构造、错误/取消/超时映射。
- [ ] 单元测试：Android meminfo 成功和异常解析。
- [ ] 单元测试：MemReport 摘要、每类明细区段、缺失标记和诊断行号。
- [ ] 单元测试：CSV/TSV/XLSX 导出元数据、列顺序和工作表。
- [ ] 集成测试：使用假的 ADB 进程或可控命令模拟设备列表、dumpsys、pull 成功/失败。
- [ ] 端到端测试：从脱敏导入 Capture 到解析再导出，验证 `Content/` 文件未改变。

### 12.3 人工验收

- [ ] 在真实 Windows 环境检查 ADB 不在 PATH、无设备、多设备、离线设备、Wi-Fi 连接失败等场景。
- [ ] 在真实 Android 设备检查启动参数推送、应用启动、meminfo 抓取和 Saved 拉取。
- [ ] 用 Excel/LibreOffice 打开导出的 XLSX、CSV、TSV，确认字符编码、列、单位和 Metadata 正确。
- [ ] 检查取消长时间 pull 后 GUI 仍可操作，日志包含已完成与未完成步骤。
- [ ] 检查清理/覆盖操作需要确认，且默认采集不会删除历史 Capture。
- [ ] 检查 GUI 与 CLI 对同一 Capture 的解析、诊断和导出关键数值一致。

### 12.4 发布

- [ ] 选择并实现发布方式：Windows self-contained 发布包或安装包。
- [ ] 确保应用启动时能诊断缺少 adb，而非崩溃。
- [ ] 提供简短 README：安装、创建 `.ukit` 工程、配置 ADB、一次采集、一次解析、常用 CLI 命令。
- [ ] 提供变更记录，列出旧 Python 工具中第一阶段已兼容、暂未兼容和有意改变的行为。

## 13. 第一阶段 Definition of Done

以下条件全部满足时，第一阶段才算完成：

- [ ] Core、CLI、Desktop、Tests 工程存在并可构建；CLI/GUI 不重复业务逻辑。
- [ ] 可创建、打开、校验 `.ukit` 工程，且能通过 `Config/DefaultGame.ini` 保存项目默认配置。
- [ ] 用户可明确选择 ADB 设备，管理连接，推送/删除启动参数并启动应用。
- [ ] 可创建新 Capture，抓取 Android meminfo 和选定 Saved 内容，生成完整或带失败信息的 Manifest。
- [ ] 可导入本地采集目录，且不修改导入源和 `Content/` 原始数据。
- [ ] 可明确选择 meminfo/memreport，解析旧工具覆盖的核心摘要和明细，并输出可读诊断。
- [ ] GUI 可浏览关键结果与表格；CLI 可完成等价的项目、设备、采集、解析、导出操作。
- [ ] 可导出真实 XLSX、CSV、TSV，并保留来源、配置和版本信息。
- [ ] 核心解析、工程、采集归档和导出拥有脱敏样本自动化测试。
- [ ] 已验证无设备、多设备、外部命令失败、解析失败、用户取消和覆盖确认等关键异常路径。

## 14. 建议的实施批次

为了尽早获得可用反馈，建议按以下批次提交和验证：

1. **批次 A**：M0 + M1。先交付可创建的 `.ukit` 工程、配置读写、项目 CLI 和 GUI 空壳。
2. **批次 B**：M2。交付设备枚举、设备选择、启动参数推送/删除、应用启动和执行日志。
3. **批次 C**：M3。交付带 Manifest 的 Capture 采集/导入；此时即可真实归档测试数据。
4. **批次 D**：M4。先实现 Android meminfo 和 MemReport 摘要，再补全部纹理/对象明细。
5. **批次 E**：M5 + M6。交付结果表格、真实导出和端到端 CLI。
6. **批次 F**：M7 + M8。完善 WPF 工作流、样本测试、异常路径、发布和用户文档。

## 15. 需要在开工前确认的事项

以下项目不阻塞创建骨架，但会影响具体字段和兼容策略，应尽早由项目使用者确认：

- [ ] 当前 UE Android 项目的真实包名、项目名、Activity 与 Saved 路径是否仍沿用旧 Python 的规则。
- [ ] 是否要求 Android 11+ 的 scoped storage 或特殊 ADB 权限兼容策略。
- [ ] `memreport` 的 UE 版本、是否有项目自定义统计标签，以及需要支持的最小样本集合。
- [ ] 默认 `Platform`、`Tag` 的命名/校验规则，以及是否允许中文、空格和层级标签。
- [ ] Capture 是否需要默认计算 SHA-256，或仅在手动导入/归档时计算。
- [ ] 第一个真实 XLSX 库的选择与许可证要求。
- [ ] 是否需要将 `.ukit`/Config/Content 纳入版本控制，`Saved/Intermediate` 是否默认加入 `.gitignore`。
