# UnrealKit 开发约定

UnrealKit 是面向 Unreal Engine Android 性能数据采集与分析的桌面工具，同时提供 WPF 图形界面（日常设备操作、采集、查看、导出）和命令行界面（自动化、批处理、CI）。

技术栈：.NET 9 / C#，WPF（Windows），ClosedXML 写出真实 XLSX。当前 79 个测试全部通过。

## 详细约定

本文只保留跨领域的高频规则，细节按领域拆分在 `Doc/` 下：

| 文档 | 内容 |
| --- | --- |
| [Doc/架构与分层约定.md](Doc/架构与分层约定.md) | 技术基线、解决方案结构、各层职责、诊断与进度契约、代码风格 |
| [Doc/工程格式与配置.md](Doc/工程格式与配置.md) | `.ukit` 格式、目录约定、`ProjectSettings` 字段、配置优先级、兼容性 |
| [Doc/采集归档与Agent分析.md](Doc/采集归档与Agent分析.md) | Capture 目录规则、`CaptureManifest` 字段、Agent 分析边界 |
| [Doc/CLI约定.md](Doc/CLI约定.md) | 命令结构、参数约定、退出码 |
| [Doc/设备操作与文件安全.md](Doc/设备操作与文件安全.md) | 参数化进程调用、ADB 路径解析、设备选择、破坏性操作确认 |
| [Doc/解析导出与诊断.md](Doc/解析导出与诊断.md) | 解析原则、诊断码分域、解析器现状、导出格式与列名契约 |
| [Doc/测试与质量约定.md](Doc/测试与质量约定.md) | 测试布局、金样测试、覆盖要求、验证流程 |
| [Doc/旧版Python性能检查工具功能分析.md](Doc/旧版Python性能检查工具功能分析.md) | 旧工具功能分析，兼容性判断依据 |
| [Doc/PlanM1.md](Doc/PlanM1.md) / [Doc/PlanM2.md](Doc/PlanM2.md) | 第一阶段完成记录 / 第二阶段计划 |

实现新功能前，先确认是否需要兼容旧工具的工作流、输入格式或输出数据。

## 核心不变式

违反以下任一条视为设计错误，不是风格偏好。

1. **单向依赖**：`Cli → Core`、`Desktop → Core`。`UnrealKit.Core` 不得引用 WPF、命令行框架或任何视图类型。
2. **逻辑不重复**：ADB 调用、文件解析、导出逻辑只在 Core 实现一次；GUI 与 CLI 都是适配层。
3. **原始数据只读**：`Content/` 是采集归档的权威来源。解析、导出、分析都不得修改、重命名或覆盖其中的原件；派生结果写入 `Saved/`。
4. **无隐式选择**：不取「默认第一台设备」「目录中第一份报告」。歧义输入必须报错或要求显式选择。
5. **失败要具体**：解析失败给出缺失段落、行号和原因，不静默以零值替代。
6. **参数化调用外部命令**：使用 `ProcessStartInfo.ArgumentList`，禁止字符串拼接 shell 命令。
7. **破坏性操作先确认**：覆盖、删除、清空目录前展示完整目标路径并要求确认；默认使用带时间戳的新输出目录。
8. **配置不硬编码**：包名、路径、阈值、预设走可版本化配置文件，GUI 与 CLI 共享同一模型。
9. **扩展名不说谎**：`.xlsx` 只能是真实 XLSX 工作簿；制表符文本用 `.tsv`/`.txt`，逗号分隔用 `.csv`。
10. **不阻塞 UI 线程**：长耗时 ADB、I/O、导出一律异步，并支持取消。

## 配置优先级

```text
内置默认值  <  .ukit 描述符  <  Config/DefaultGame.ini  <  显式 CLI 参数或 GUI 本次操作值
```

ADB 路径解析顺序：`--adb-path` < 工程配置 `AdbPath` < 环境变量（`ADB_PATH`、`ANDROID_SDK_ROOT`、`ANDROID_HOME`）< `PATH`。解析过程的每一步尝试都要保留，用于失败时的可操作提示。

## 稳定契约

以下标识一经发布即不可随意变更，修改等同破坏性变更，须在 `CHANGELOG.md` 中说明：

- 诊断码：`UKIT*`（工程）、`AMI*`（Android meminfo）、`UMR*`（UE memreport）、`SCP*`（静态相机）。新增向后追加，不复用、不改语义。
- 导出列名与 XLSX 工作表名。
- `.ukit` 的 `FormatVersion`；提升版本必须同时提供迁移路径。
- `CaptureManifest.json` 字段；扩展时新增可空字段，保证既有归档仍可反序列化。

## 功能优先级

第一阶段核心能力已完成（工程管理、ADB、采集归档、解析、导出、CLI/GUI 全覆盖），记录见 `Doc/PlanM1.md`。

第二阶段按序推进（`Doc/PlanM2.md`）：静态相机 HTML 报告与 WPF 页面 → 基线差分 → 历史趋势 → RenderDoc 集成 → Agent 分析。RenderDoc Python 脚本保留为独立能力，不做 C# 重写。

## 交付要求

- 构建必须无警告（`TreatWarningsAsErrors=true`），不用 `#pragma warning disable` 掩盖问题。
- 修改 Core 后运行相关单元测试；修改 CLI 后验证成功、参数错误、外部命令失败三条路径。
- 新增解析规则时同时添加成功样本和格式异常样本。
- 不修改与当前任务无关的测试失败，在交付说明中单独指出。
- 改动保持小而聚焦；新增依赖须说明用途。
- 批处理脚本使用原生 bat 语法，不使用 PowerShell 语法。
- 新增 CLI 子命令时同步更新 `README.md` 的 CLI 参考和 `Doc/CLI约定.md`。
