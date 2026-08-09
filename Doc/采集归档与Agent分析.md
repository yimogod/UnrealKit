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

## CaptureManifest

`CaptureManifest`（`UnrealKit.Core.Capture.CaptureModels`）当前记录：

| 字段 | 说明 |
| --- | --- |
| `CaptureId` | 采集唯一标识 |
| `Platform` / `Tag` | 归档路径维度 |
| `StartedAt` / `CompletedAt` | 采集起止时间（`DateTimeOffset`） |
| `ProjectConfiguration` | 采集时的工程配置快照（描述符 + 设置 + 快照时间） |
| `DeviceSerialNumber` / `DeviceModel` / `DeviceStatus` | 设备标识与状态 |
| `PackageName` | 实际使用的包名 |
| `DeviceSavedDirectory` | 实际拉取的设备端 Saved 路径 |
| `InputFiles` | 每个文件的相对路径、字节大小、SHA-256 |

扩展 Manifest 时新增可空字段，避免破坏既有归档的反序列化；同时更新 `CaptureServiceTests` 的金样断言。

## 采集请求

- `CaptureRequest(Project, Device, Tag, CaptureId?, SkipSaved)` — 实时采集；`SkipSaved` 跳过设备 Saved 拉取，适合只需 `dumpsys meminfo` 的快速采样。
- `CaptureImportRequest(Project, SourceDirectory, Platform, Tag, CaptureId?)` — 从本地目录导入既有数据，同样生成 Manifest 与校验信息。
- `CapturePlan` 在实际写入前确定 `CaptureId`、目标目录和设备源路径；GUI 应在确认阶段展示计划路径。

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

当前状态：`CaptureAnalysisService` 提供本地采集分析；LLM 适配层尚未实现，属 `Doc/PlanM2.md` 的 P5 项。
