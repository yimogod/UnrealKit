# UnrealKit 第二阶段 TODO

基于 旧版Python性能检查工具功能分析.md 第二阶段 + 第三阶段能力。

最后更新： | 框架 .NET 9 / WPF | 72 测试通过

---

## 第二阶段目标

在第一阶段核心能力（工程管理、ADB、采集、解析、导出、CLI/GUI）基础上，增加：

---

## 待完成

### P1：静态相机性能模块

- [ ] StaticCameraPerfParser：解析 `!!!Do Perf Start!!!` / `!!!Do Perf End!!!` / `PointNum:` 标记
- [ ] 14 行数据结构配置化（标签、阈值不可硬编码）
- [ ] 截图数量校验：截图数 vs 相机记录数一致性检查
- [ ] DC warning/error 分档（旧脚本同值 500 是已知缺陷，修复）
- [ ] HTML 报告生成：日志 + 截图 + 阈值标记
- [ ] CLI：`parse static-camera --input <log> --screenshots <dir> [--config <ini>]`
- [ ] WPF：静态相机解析页（日志选择、截图预览、报告查看）

### P2：基线差分

- [ ] BaselineService：双 Capture / 双文件加载，逐指标差分
- [ ] 区分"基线"和"当前"，标注单位、正负方向、缺失项
- [ ] CLI：`analyze diff --baseline <id> --current <id> [--metrics <list>]`
- [ ] WPF：差分结果表格（基线列、当前列、差值列、方向指示）

### P3：历史趋势

- [ ] TrendService：按 Tag/场景/设备/日期聚合多 Capture 的指标序列
- [ ] 导出趋势 CSV/TSV/XLSX（真实多工作表 Excel）
- [ ] CLI：`analyze trend --project <project.ukit> [--tag <tag>] [--from <date>] [--to <date>]`
- [ ] WPF：趋势图表（折线图、可选指标、时间范围滑块）

### P4：RenderDoc 集成

- [ ] RenderDoc Python 脚本保留为独立能力，不做 C# 重写
- [ ] RenderDoc 适配层：通过 CLI 调用 Python 脚本、管理输出目录
- [ ] WPF：RenderDoc 页（脚本路径配置、参数设置、输出浏览）

### P5：Agent 分析

- [ ] AgentAnalysisService：基于捕获的强类型数据 + 受控摘要执行分析
- [ ] 可替换的 LLM 适配层（不在 Core 直接依赖模型 SDK）
- [ ] CLI：非交互式分析 (`analyze agent --capture <id> --preset <name> --output <dir>`)
- [ ] WPF：Agent 分析页（Capture 选择、提示词预览、外部服务确认、报告查看）
- [ ] Agent 报告保存到 `Saved/Analysis/<AnalysisId>/`

---

## 下一步优先级

1. **P1 静态相机** — 旧工具中唯一未迁移的核心解析能力
2. **P2 基线差分** — 建立对比分析基础
3. **P3 历史趋势** — 在基线基础上扩展时间维度
4. **P4 RenderDoc** — 独立能力集成
5. **P5 Agent 分析** — 最高层分析能力
