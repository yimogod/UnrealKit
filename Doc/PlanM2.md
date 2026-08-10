# UnrealKit 第二阶段 TODO

基于 旧版Python性能检查工具功能分析.md 第二阶段 + 第三阶段能力。

最后更新：2026-08-09 | P1-P6 全部完成（含 Win64 设备支持）

---

## 第二阶段目标

在第一阶段核心能力（工程管理、ADB、采集、解析、导出、CLI/GUI）基础上，增加：

---

## 完成情况

### P1：静态相机性能模块 ✅

- [x] StaticCameraPerfParser：解析 `!!!Do Perf Start!!!` / `!!!Do Perf End!!!` / `PointNum:` 标记
- [x] 14 行数据结构配置化（标签、阈值不可硬编码 → `StaticCameraPerfConfig`）
- [x] 截图数量校验：截图数 vs 相机记录数一致性检查
- [x] DC warning/error 分档（旧脚本同值 500 是已知缺陷，修复 → 400/500）
- [x] HTML 报告生成：日志 + 截图 + 阈值标记（`StaticCameraHtmlReportService`）
- [x] CLI：`parse static-camera --input <log> [--screenshots <dir>] [--format json] [--html-output <path>]`
- [x] WPF：静态相机解析页 + "生成 HTML 报告…"按钮

### P2：基线差分 ✅

- [x] BaselineService：双 Capture / 双文件加载，逐指标差分（meminfo / memreport / 静态相机三种来源）
- [x] 区分"基线"和"当前"，标注单位、正负方向、缺失项（`MetricDirection` / `MetricDiffStatus` 四态）
- [x] CLI：`analyze diff --baseline <id> --current <id> [--metrics <list>] [--only-changed] [--format json]`
- [x] WPF：差分结果表格（基线列、当前列、差值列、方向指示）

### P3：历史趋势 ✅

- [x] TrendService：按 Tag/平台/设备/日期聚合多 Capture 的指标序列
- [x] 导出趋势 CSV/TSV/XLSX（真实多工作表 Excel）
- [x] CLI：`analyze trend --project <project.ukit> [--tag <tag>] [--from <date>] [--to <date>] [--metrics <list>] [--output <file>] [--include-points]`
- [x] WPF：趋势概览（采集列表 + 指标汇总表）+ Chart 折线图（纯 Canvas 渲染，零外部依赖）

### P4：RenderDoc 集成

- [ ] RenderDoc Python 脚本保留为独立能力，不做 C# 重写
- [x] RenderDoc 适配层：通过 CLI 调用 Python 脚本、管理输出目录
- [x] WPF：RenderDoc 页基础 UI（脚本路径配置、参数设置、执行、诊断结果）
- [ ] WPF 增强（后续可选）

### P5：Agent 分析 ✅

- [x] 项目创建时自动写入 AGENTS.md 宪章与 .codex/skills/ukit-analyze 分析技能
- [x] Agent 自行选择 LLM（Claude/Codex），打开项目目录即可按宪章+Skill 执行分析
- [x] 分析报告保存到 Saved/Analysis/<analysis-id>/（由 Agent 管理）
- [ ] 后续可扩展更多分析 Skill 模板（后续可选）

---


---

## P6：Win64 设备支持 ✅

- [x] `Win64DeviceService`：实现 `IDeviceService`，通过 `System.Diagnostics.Process` 采集 Windows 进程内存（`CaptureMemoryAsync`）
- [x] `Win64MemInfoParser`：对应 `AndroidMemInfoParser`，解析 `CaptureMemoryAsync` 输出的结构化文本
- [x] `Win64Device`：实现 `IDevice`（`Id="localhost"`, `Platform="Win64"`）
- [x] `PullDirectoryAsync` / `PushFileAsync` / `DeleteRemoteFileAsync`：映射为本地文件系统操作
- [x] `StartApplicationAsync`：通过 `IProcessRunner` 启动本机可执行文件
- [x] `.ukit` 工程创建支持 `--platform Win64` 参数
- [x] `Platform` / `Win64Executable` / `Win64WorkingDirectory` 持久化到 `DefaultGame.ini`
- [x] CLI：`devices` 命令同时列出 Win64 本地主机与 ADB 设备
- [x] CLI：`project create` 用法更新（含 `--platform` 参数）
- [x] 测试：`Win64DeviceServiceTests`（2 个）、`Win64MemInfoParserTests`（5 个）、`ProjectServiceTests` Win64 平台持久化（1 个）
- [x] 构建 0 警告 0 错误，140/140 测试通过

### 新增文件
| 文件 | 说明 |
|------|------|
| `UnrealKit.Core/Devices/Win64DeviceService.cs` | IDeviceService 的 Win64 实现 + Win64Device 类 |
| `UnrealKit.Core/Parsing/Win64MemInfoModels.cs` | Win64MemInfoCounters / Win64MemInfoReport / Win64MemInfoParseResult |
| `UnrealKit.Core/Parsing/IWin64MemInfoParser.cs` | 解析器接口 |
| `UnrealKit.Core/Parsing/Win64MemInfoParser.cs` | Win64 meminfo 文本解析器 |
| `UnrealKit.Tests/Win64DeviceServiceTests.cs` | 设备服务测试 |
| `UnrealKit.Tests/Win64MemInfoParserTests.cs` | 解析器测试 |

### 修改文件
| 文件 | 变更 |
|------|------|
| `UnrealKit.Core/Projects/ProjectService.cs` | WriteSettingsAsync / ReadSettingsAsync 加入 Platform/Win64 字段 |
| `UnrealKit.Cli/Program.cs` | 新增 devices 命令；project create 支持 --platform；新增 GetPositionalArgument |
| `UnrealKit.Tests/ProjectServiceTests.cs` | 新增 Win64 平台持久化测试 |
## 下一步优先级
1. ✅ **P1 静态相机** -- Core 解析器 + CLI + WPF 页面 + HTML 报告全部完成
2. ✅ **P2 基线差分** -- BaselineService + CLI + WPF 差分页已完成
3. ✅ **P3 历史趋势** -- TrendService + CLI + WPF 趋势页 + 折线图已完成
4. ✅ **P4 RenderDoc** -- Core RenderDocService + CLI renderdoc run 已完成
5. ✅ **P5 Agent 分析** -- 项目模板（AGENTS.md + Skill）已完成
6. ✅ **P6 Win64 设备** -- Win64DeviceService + Win64MemInfoParser + CLI devices 命令 + --platform Win64 参数 全部完成
7. **后续可选** -- RenderDoc WPF 页增强、Desktop 端 Win64 设备 UI 集成、更多 Skill 模板
