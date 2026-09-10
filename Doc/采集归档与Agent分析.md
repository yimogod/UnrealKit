# 采集归档与 Agent 分析

## 采集归档

每次工具拉取或导入的数据都必须归属到一个明确的采集目录：

```text
Content/<Platform>/<Tag>/<YYYY-MM-DD>/<CaptureId>/
```

不得将不同设备、场景或采集批次的文件混入同一 Capture。

- 采集完成后写入 `CaptureManifest.json`。
- 工具应记录采集时实际使用的包名、Activity、远端路径、CLI 参数和启动参数，便于复现分析结论。
- 已存在的 Capture 默认只读；重新拉取应创建新的 Capture，或由用户明确选择覆盖。
- 解析后的表格、XLSX、HTML 和中间数据输出到 `Saved/Exports/` 或 `Saved/Reports/`，不得写回或重命名 `Content/` 原件。

### 列出归档

`CaptureAnalysisService.ListCaptureDirectoriesAsync` 的 `platform` 参数为 `null` 时枚举 `Content/` 下的**全部**平台目录，不得回退到某个默认平台——被跳过的平台目录既不显示也不报错，读起来就是「从未采集过」。平台名取自目录名本身，不与 `TargetPlatform` 枚举比对，未纳入枚举的平台目录同样列出。

结果按采集日期倒序，同日期以 `CaptureId` 作为稳定次序：目录枚举顺序由文件系统决定，仅按日期排会让「最近一份」在两次刷新之间跳动。

按 `CaptureId` 定位归档时命中多份必须报错并列出候选路径（跨平台查找使同一 ID 可能出现在多个平台目录下），不取第一个。

## CaptureManifest

`CaptureManifest`（`UnrealKit.Core.Capture.CaptureModels`）当前记录：

| 字段 | 说明 |
| --- | --- |
| `CaptureId` | 采集唯一标识 |
| `Platform` / `Tag` | 归档路径维度 |
| `StartedAt` / `CompletedAt` | 采集起止时间（`DateTimeOffset`） |
| `ProjectConfiguration` | 采集时的工程配置快照（描述符 + 设置 + 快照时间） |
| `DeviceSerialNumber` / `DeviceModel` / `DeviceStatus` | 设备标识与状态 |
| `ResolvedTarget` | 本次采集实际用到的平台落地值（`PlatformTarget`：进程标识、启动目标、已展开的设备端路径）。导入的归档没有涉及设备，该字段为 `null` |
| `DeviceSavedDirectory` | 实际拉取的设备端 Saved 路径 |
| `InputFiles` | 每个文件的相对路径、字节大小、SHA-256 |

`ProjectConfiguration` 是采集时的整份工程配置，含全部已配置平台；`ResolvedTarget` 指明本次用的是哪一个、模板展开成了什么。读者据此还原采集上下文，不必自己重新展开模板去猜。

扩展 Manifest 时新增可空字段，避免破坏既有归档的反序列化；同时更新 `CaptureServiceTests` 的金样断言。

## 采集请求

- `CaptureRequest(Project, Device, Tag, CaptureId?, SkipSaved)` — 实时采集；`SkipSaved` 跳过设备 Saved 拉取，适合只需 `dumpsys meminfo` 的快速采样。
- `CaptureImportRequest(Project, SourceDirectory, Platform, Tag, CaptureId?)` — 从本地目录导入既有数据，同样生成 Manifest 与校验信息。
- `CapturePlan` 在实际写入前确定 `CaptureId`、目标目录和设备源路径；GUI 应在确认阶段展示计划路径。

## Pak 资产扫描

`PakScanService` 对 APK 或 `.pak` 文件做离线资产扫描，结果写入 `Saved/PakScan/<Platform>/<yyyyMMdd-HHmmss>-<source>/`：

| 文件 | 说明 |
| --- | --- |
| `scan_manifest.json` | 扫描来源（pak 路径或 APK 路径）、工具版本、执行时间 |
| `assets.json` | 扫描到的资产列表（强类型结构化数据） |
| `report.html` | 可选的 HTML 摘要报告 |

`<source>` 取自 `Intermediate/Download/<Platform>/<subdir>/` 对应的子目录名（即 FTP 下载版本号），保持可追溯。如果扫描的不是下载产物而是用户手动指定的文件，`<source>` 取文件名（不含扩展名）。

若某次采集需要关联其资产扫描结果，在对应 `CaptureManifest.json` 中通过可空字段 `LinkedPakScanId` 记录扫描目录名（即时间戳+source 部分）；`Content/` 目录本身不存放资产扫描数据。

## 临时数据（Scratch）

`Saved/Scratch/<yyyyMMdd-HHmmss>/` 用于不需要完整归档流程的临时操作：随手拿一份 memreport、拉一段运行日志、做一次快速验证等。

- 每次操作独立一个时间戳目录，不同操作的文件不混放。
- 无 `CaptureManifest.json`，来源不保证完整可溯源。
- 整个 `Scratch/` 目录可随时清除，不影响 `Content/` 中的任何归档。
- 与 `Saved/DeviceSaved/` 的区别：`DeviceSaved/` 来源明确（知道是哪台设备、哪个时间、哪个范围拉的），适合需要保留一段时间的设备快照；`Scratch/` 是随手操作，生命周期更短。

## 下载设备 Saved

`SavedDownloadService`（`SavedDownloadRequest` / `SavedDownloadPlan` / `SavedDownloadResult`）把设备上的 UE Saved 数据取回本地，供用户直接翻看日志、截图、Profiling 文件。GUI 在「采集归档」页有两个按钮，下载完成后都会打开落地目录：

| 按钮 | `SavedDownloadScope` | 设备端源目录 |
| --- | --- | --- |
| 下载 Saved | `All` | `PlatformTarget.SavedRootPath` |
| 下载 Log | `Logs` | 其下的 `Logs` 子目录 |

两个范围共用同一条落地流程，只有源目录与提示文字不同。分两个入口是因为排查问题时通常只看日志，而完整 Saved 可能很大（含 Profiling、截图）。

范围用枚举而不是让调用方传自由的子目录名：子目录名是 UE 的固定布局，可自由填写的路径会让「取回 Logs」和「取回一个拼错的名字」无法区分，后者会以「设备上没有该目录」的形式失败，读起来像设备的问题。

它与采集刻意分开，不是采集的简化版：

- 落地在 `Saved/DeviceSaved/<Platform>/<yyyyMMdd-HHmmss>-<设备 id>-<Saved|Logs>/`，不写 `Content/`。下载不生成 `CaptureManifest.json`，来源无从追溯，混进归档会让 `Content/` 里出现无法溯源的数据。
- 每次下载进一个新目录，目标目录已存在时报错而不是覆盖或合并——覆盖会静默抹掉两次取回之间的差异。目录名同时含时间戳、设备 id 与范围三者：同一天多台设备各取一次要靠设备 id 区分，同一秒对同一设备既取 Saved 又取 Logs 要靠范围区分，少任何一项都可能撞名。
- 落地目录直接就是所取范围的内容，不多包一层同名子目录：取 Logs 得到的是 `Game.log` 等文件本身，而不是 `Logs/Game.log`。
- 先落 `Intermediate/SavedDownloadStaging/` 暂存再整体 `Directory.Move`。中途失败或取消留下的是暂存目录，不是一个看起来完整、实则只有一半文件的下载结果。
- 拉取报告成功但本地没有内容时报错，指出设备端路径与可能原因，不产出空目录——「设备上没有该目录」和「取回成功但没数据」必须可区分。
- 类内无平台分支：设备端路径由 `PlatformTarget` 提供，子目录用 `PlatformTarget.CombineDevicePath` 拼接（按平台风格选分隔符，不用 `Path.Combine`——它在 Windows 主机上会给 Android 路径写入反斜杠），拉取动作委托 `IDeviceService.PullDirectoryAsync`。平台不支持 `DeviceCapability.PullDirectory` 时抛 `DeviceCapabilityNotSupportedException`。

## Agent 分析

Agent 分析是工程的派生能力，不是原始数据的替代品。

### 输入与授权

- 必须基于用户明确选择的 Capture、文件或解析结果执行；不得因打开工程而自动发送或分析全部数据。
- 分析前展示输入范围、将使用的规则/提示词、可能的外部服务，以及数据是否会离开本机。涉及外部模型时必须获得用户明确确认。
- 输入优先使用 Core 解析后的强类型数据和受控摘要；只有在需要定位问题时才引用原始日志片段，并限制范围。

### 输出

报告保存到 `Saved/Analysis/<AnalysisId>/`，至少包含：

- 报告正文
- 输入 CaptureId 与输入文件/结果清单
- 工具版本、分析规则或提示词版本
- 模型/提供方标识
- 执行时间与警告项

结论必须区分「事实/测量值」「基于规则的判断」「推断或建议」。对缺失数据、格式异常和不确定性必须明确说明。

### 边界

- Agent 只能生成分析、建议和派生报告；除非用户确认，不得删除、覆盖、上传或修改工程的原始采集数据与配置。
- Agent 提供方必须通过可替换的适配层接入，`UnrealKit.Core` 不得直接依赖某个模型 SDK 或服务商。
- CLI 应支持非交互式、可审计的分析：指定 Capture、分析预设、输出目录和机器可读结果；任何需要外网的调用仍需显式开关授权。

当前状态：`CaptureAnalysisService` 提供本地采集分析；LLM 适配层接口已预留，具体提供方通过可替换适配层接入。
