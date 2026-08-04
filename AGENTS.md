# UnrealKit 开发约定

## 项目目标

UnrealKit 是面向 Unreal Engine Android 性能数据采集与分析的桌面工具。它需要同时提供：

- 图形界面，服务日常设备操作、数据采集、查看和导出。
- 命令行界面，服务自动化、批处理、CI 和高级用户。

旧版 Python 工具的分析见 `Doc/旧版Python性能检查工具功能分析.md`。实现新功能前，先确认是否需要兼容其中的旧工作流、输入格式或输出数据。

## 技术方向

- 主体使用 **.NET / C#** 重写，不新增 Python GUI 作为主应用。
- 首个 GUI 使用 **WPF**，以 Windows 为目标平台。
- GUI 和 CLI 必须复用同一套核心业务逻辑；不得在两个入口重复实现 ADB 调用、文件解析或导出逻辑。
- RenderDoc Python 脚本可暂时保留为独立的后续集成能力；不要为了首版强行把 RenderDoc Python API 迁移到 C#。

## 建议的解决方案结构

新建项目时按职责拆分，保持依赖单向：

```text
UnrealKit.Core       # 领域模型、配置、ADB、采集、解析、分析、导出
UnrealKit.Cli        # CLI 参数绑定和控制台呈现；引用 Core
UnrealKit.Desktop    # WPF 视图与 ViewModel；引用 Core
UnrealKit.Tests      # Core 的单元测试与解析样本测试
```

- `UnrealKit.Core` 不得依赖 WPF、命令行框架或具体视图类型。
- `UnrealKit.Cli` 只负责命令参数、输出格式和进程退出码，不承载业务规则。
- `UnrealKit.Desktop` 只负责交互、展示、进度和用户确认，不直接拼接 ADB 命令或解析文本。
- 优先将可测试的流程实现为 Core 中的服务，GUI/CLI 只作为适配层。

## 功能优先级

首版优先实现旧工具的核心能力：

1. 项目配置：包名、项目名、Activity、设备 Saved 路径、本地工作目录、阈值和启动参数预设。
2. ADB：设备枚举与选择、Wi-Fi 连接、应用启动、推送/删除 `uecommandline.txt`、抓取 `dumpsys meminfo`、拉取 Saved 数据。
3. 离线解析：Android meminfo、UE memreport、纹理/Render Target/对象明细。
4. 结果查看和真实 CSV/TSV/XLSX 导出。
5. CLI 覆盖核心采集、解析和导出能力。

静态相机性能 HTML 报告、基线差分、历史趋势和 RenderDoc 集成可在核心流程稳定后迭代。

## `.ukit` 工程

UnrealKit 必须支持创建和打开 UE 风格的分析工程。工程描述文件扩展名为 `.ukit`，其内容是 UTF-8 INI 文本，而不是二进制格式。

### 工程目录约定

```text
<ProjectRoot>/
├─ <ProjectName>.ukit          # 必需：工程描述符、格式版本、根目录约定
├─ Config/                     # 可选：UE 风格的可版本化默认配置
│  └─ DefaultGame.ini          # 项目采集、路径、阈值和分析默认值
├─ Content/                    # 受工程管理的原始采集数据；不修改原件
│  └─ <Platform>/<Tag>/<YYYY-MM-DD>/<CaptureId>/
│     ├─ CaptureManifest.json  # 本次采集的来源、设备、时间、文件清单、校验信息
│     ├─ MemInfo/
│     ├─ Saved/
│     ├─ Logs/
│     ├─ Screenshots/
│     ├─ Profiling/
│     └─ GPUDumps/
├─ Saved/                      # 可重新生成的派生数据，不作为原始采集来源
│  ├─ Exports/
│  ├─ Analysis/
│  ├─ Reports/
│  └─ Logs/
└─ Intermediate/               # 可删除的缓存、解压和临时处理文件
```

- `Content/` 是采集数据的权威存档。导入、拉取或解析时不得修改其中的原始文件。
- `Platform` 例如 `Android`；`Tag` 是用户定义的场景、版本、测试批次或性能标签；日期必须使用 ISO 格式 `YYYY-MM-DD`。
- `CaptureId` 必须唯一，建议使用 `yyyyMMdd-HHmmss` 加设备或随机后缀，避免同日多次采集冲突。
- `Saved/`、`Intermediate/` 是派生或可再生数据；清理它们不得删除 `Content/` 中的原始采集数据。
- 工程创建时应只生成必要的空目录和 `.ukit`/默认配置，不创建虚假的采集数据。

### `.ukit` 与 `Config/` 职责

- `.ukit` 是必需的工程描述符，保存工程名称、格式版本和固定根目录名；它应保持小、稳定、便于识别工程。
- `Config/DefaultGame.ini` 是可选的项目默认配置，采用 UE 工程师熟悉的命名。它用于包名、Activity、Saved 路径模板、ADB 默认值、采集预设、阈值、标签规则和 Agent 分析预设。
- 首版允许将最少的创建参数写入 `.ukit`；新增可调业务配置时，优先放入 `Config/DefaultGame.ini`，不要无限扩展 `.ukit`。
- 配置优先级必须明确并在 GUI/CLI 中可见：内置默认值 < `.ukit` 描述符 < `Config/DefaultGame.ini` < 显式 CLI 参数或 GUI 本次操作值。
- 项目配置中不得存储密钥、令牌或敏感个人信息；这些信息应由系统安全存储或运行时环境提供。

建议的 `.ukit` 最小内容：

```ini
[UnrealKit.Project]
FormatVersion=1
ProjectName=ExamplePerformance
ContentRoot=Content
ConfigRoot=Config
SavedRoot=Saved
IntermediateRoot=Intermediate
```

建议的 `Config/DefaultGame.ini` 节名使用 `[/Script/UnrealKit.*]` 或 `[UnrealKit.*]`，并保持字段稳定、可读和可手动编辑。

### 工程管理入口

- GUI 必须提供创建工程、打开工程、最近工程和工程信息页面。
- CLI 必须至少提供等价的 `project create`、`project info` 和 `project validate` 能力。
- 创建工程时必须校验工程名、目标目录和 `.ukit` 文件名；不应在非空目录中静默覆盖已有文件。
- 打开工程时必须校验格式版本、必需目录和配置错误，并给出可操作的迁移或修复提示。

## 采集归档与 Agent 分析

每次工具拉取或导入的数据都必须归属到一个明确的 `Content/<Platform>/<Tag>/<YYYY-MM-DD>/<CaptureId>/` 采集目录。不得将不同设备、场景或采集批次的文件混入同一 Capture。

- 采集完成后创建 `CaptureManifest.json`，至少记录 CaptureId、平台、标签、采集开始/结束时间、项目配置快照、ADB 设备序列号/型号（如可用）、输入文件列表、文件大小和校验信息。
- 工具应记录采集时实际使用的包名、Activity、远端路径、CLI 参数和启动参数，便于复现分析结论。
- 对已经存在的 Capture，默认只读；重新拉取应创建新的 Capture 或由用户明确选择覆盖。
- 解析后的表格、真实 XLSX、HTML 和中间数据必须输出到 `Saved/Exports/` 或 `Saved/Reports/`，不得写回或重命名 `Content/` 原件。

Agent 分析是工程的派生能力，不是原始数据的替代品：

- Agent 必须基于用户明确选择的 Capture、文件或解析结果执行分析；不得因打开工程而自动发送或分析全部数据。
- 分析前应展示输入范围、将使用的规则/提示词、可能的外部服务，以及数据是否会离开本机；涉及外部模型时必须得到用户明确确认。
- Agent 输入优先使用 Core 解析后的强类型数据和受控摘要；只有在需要定位问题时才引用原始日志片段，并限制范围。
- Agent 报告保存到 `Saved/Analysis/<AnalysisId>/`，至少包含报告正文、输入 CaptureId、输入文件/结果清单、工具版本、分析规则或提示词版本、模型/提供方标识、执行时间和警告项。
- Agent 的结论必须区分“事实/测量值”“基于规则的判断”“推断或建议”；对缺失数据、格式异常和不确定性必须明确说明。
- Agent 只能生成分析、建议和派生报告；除非用户确认，不得删除、覆盖、上传或修改工程的原始采集数据与配置。
- Agent 提供方必须通过可替换的适配层接入，不能让 `UnrealKit.Core` 直接依赖某个模型 SDK 或服务商。
- CLI 应支持非交互式、可审计的 Agent 分析：指定 Capture、分析预设、输出目录和机器可读结果；任何需要外网的调用仍需由显式开关授权。

## CLI 约定

- CLI 应与 GUI 使用相同配置模型和 Core 服务。
- 命令应支持显式选择项目配置、ADB 设备序列号、输入路径和输出路径。
- 成功返回退出码 `0`；参数、设备、解析或导出失败返回非零退出码。
- 日志应可读且适合复制；为自动化调用提供可选的 JSON 或机器可读输出。
- 不依赖“默认第一台设备”或“目录中第一份报告”等隐式选择；歧义输入必须报错或要求显式选择。

## ADB 和文件操作安全

- 使用 `ProcessStartInfo.ArgumentList` 等参数化方式调用 ADB；禁止通过未经处理的字符串拼接 shell 命令。
- 每次外部命令执行都要捕获退出码、标准输出和标准错误，并支持超时和取消。
- GUI 中执行会覆盖、删除或清空目录的操作前，必须向用户明确显示目标路径并要求确认。
- 默认优先使用带时间戳的新输出目录，避免无提示清空历史采集数据。
- 支持按 ADB 序列号明确选择设备；所有设备相关命令应携带选择结果。

## 解析和导出约定

- 旧 Python 解析器依赖固定文本标签和行格式。新解析器必须在失败时给出具体缺失段落、行号或格式原因，不能静默以零值替代。
- 对同目��下的多份 `meminfo_*.txt` 或 `.memreport`，必须列出候选文件信息并由调用方明确选择。
- `.xlsx` 扩展名只能用于真实 XLSX 工作簿；制表符文本必须使用 `.tsv` 或 `.txt`，CSV 使用 `.csv`。
- 导出结果应保存输入来源、解析时间、工具版本和关键配置，确保可追溯。
- 差分功能应区分“基线”和“当前”，并明确单位、正负方向和缺失项。

## 静态相机性能模块

- 旧版依赖 `!!!Do Perf Start!!!`、`!!!Do Perf End!!!`、`PointNum:` 和固定 14 行数据结构；这些标签必须配置化，不要散落硬编码在解析器中。
- 截图数量、每相机截图数量、帧时间/DC/三角形阈值均应可配置。
- 配置校验必须保证 warning 小于 error；旧脚本中 DC warning/error 同为 `500` 是已知缺陷，不应继承。
- 截图与相机记录匹配前必须校验数量和顺序；不匹配时产生可见诊断。

## 配置与兼容性

- 项目配置不得继续硬编码在源码中；使用可读、可版本化的配置文件，并让 GUI 与 CLI 共享。
- 默认兼容旧工具涉及的 Android UE Saved 路径规则，但允许项目配置覆盖。
- 兼容旧数据输入时，保留原始文件，不在解析阶段修改输入。
- 修改旧行为前，更新 `Doc/旧版Python性能检查工具功能分析.md` 或新增迁移说明，明确兼容性变化。

## 测试与验证

- 为 Android meminfo、UE memreport、静态相机日志解析器提供脱敏样本和金样测试。
- 新增解析规则时，至少添加一个成功样本和一个格式异常样本。
- 修改 Core 后优先运行相关单元测试；修改 CLI 后验证成功、参数错误和外部命令失败路径。
- GUI 代码应将业务逻辑保持在可测试的 ViewModel/Core 中，避免在 code-behind 中实现业务流程。
- 不要修改或“修复”与当前任务无关的测试失败；应在交付说明中单独指出。

## 代码风格

- 使用异步 API 执行长时间 ADB、I/O 和导出工作；GUI 不得阻塞 UI 线程。
- 公共 API、配置字段、用户可见错误和导出列名要有清晰、稳定的命名。
- 优先使用强类型数据模型，不在业务层传递无结构的字典或制表符字符串。
- 保持改动小而聚焦；未经明确要求，不修改旧 Python 工具的行为。
- 任何新增依赖必须说明用途，并优先选用维护活跃、许可兼容的库。
