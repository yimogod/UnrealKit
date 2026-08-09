# UnrealKit 第二阶段 TODO

基于 旧版Python性能检查工具功能分析.md 第二阶段 + 第三阶段能力。

最后更新：2026-08-09 | 框架 .NET 9 / WPF | P1-P5 Core+CLI 完成，P1-P4 RenderDoc CLI 完成，P5 Agent 模板完成

---

## 第二阶段目标

在第一阶段核心能力（工程管理、ADB、采集、解析、导出、CLI/GUI）基础上，增加：

---

## 待完成

### P1：静态相机性能模块

- [x] StaticCameraPerfParser：解析 `!!!Do Perf Start!!!` / `!!!Do Perf End!!!` / `PointNum:` 标记
- [x] 14 行数据结构配置化（标签、阈值不可硬编码 → `StaticCameraPerfConfig`）
- [x] 截图数量校验：截图数 vs 相机记录数一致性检查
- [x] DC warning/error 分档（旧脚本同值 500 是已知缺陷，修复 → 400/500）
- [ ] HTML 报告生成：日志 + 截图 + 阈值标记（后续迭代）
- [x] CLI：`parse static-camera --input <log> --screenshots <dir> [--format json]`
- [ ] WPF：静态相机解析页（日志选择、截图预览、报告查看）（后续迭代）

### P2：基线差分

- [x] BaselineService：双 Capture / 双文件加载，逐指标差分（meminfo / memreport / 静态相机三种来源）
- [x] 区分"基线"和"当前"，标注单位、正负方向、缺失项（`MetricDirection` / `MetricDiffStatus` 四态）
- [x] CLI：`analyze diff --baseline <id> --current <id> [--metrics <list>] [--only-changed] [--format json]`
- [ ] WPF：差分结果表格（基线列、当前列、差值列、方向指示）（后续迭代）

### P3：历史趋势

- [x] TrendService：按 Tag/平台/设备/日期聚合多 Capture 的指标序列
- [x] 导出趋势 CSV/TSV/XLSX（真实多工作表 Excel）
- [x] CLI：`analyze trend --project <project.ukit> [--tag <tag>] [--from <date>] [--to <date>] [--metrics <list>] [--output <file>] [--include-points]`
- [ ] WPF：趋势图表（折线图、可选指标、时间范围滑块）（后续迭代）

### P4：RenderDoc 集成

- [ ] RenderDoc Python 脚本保留为独立能力，不做 C# 重写
- [x] RenderDoc 适配层：通过 CLI 调用 Python 脚本、管理输出目录
- [ ] WPF：RenderDoc 页（脚本路径配置、参数设置、输出浏览）（后续迭代）

### P5：Agent 分析


- [x] 项目创建时自动写入 AGENTS.md 宪章与 .codex/skills/ukit-analyze 分析技能
- [x] Agent 自行选择 LLM（Claude/Codex），打开项目目录即可按宪章+Skill 执行分析
- [x] 分析报告保存到 Saved/Analysis/<analysis-id>/（由 Agent 管理）
- [ ] 后续可扩展更多分析 Skill 模板

---

## 下一步优先级

1. ~~**P1 静态相机**~~ -- Core 解析器 + CLI 已完成（HTML 报告、WPF 页面后续迭代）
2. ~~**P2 基线差分**~~ -- BaselineService + nalyze diff 已完成（WPF 差分页后续迭代）
3. ~~**P3 历史趋势**~~ -- TrendService + 趋势导出 + nalyze trend 已完成（WPF 趋势图表后续迭代）
4. ~~**P4 RenderDoc**~~ -- Core RenderDocService + CLI enderdoc run 已完成（WPF 页后续迭代）
5. ~~**P5 Agent 分析**~~ -- 项目模板（AGENTS.md + Skill）已完成，Agent 自行选择 LLM 分析
6. **WPF 页面补齐** -- 静态相机、差分、趋势、RenderDoc 页面一批补齐

WPF 页面（静态相机、差分、趋势）作为一批后续迭代统一补齐，Core 与 CLI 已先行落地。
