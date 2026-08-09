# UnrealKit 第一阶段（完成记录）

最后更新： | 框架 .NET 9 / WPF | 72 测试通过 | 0 警告 0 错误

实现范围以 旧版Python性能检查工具功能分析.md 中第一阶段核心能力为准，开发约定见仓库根 CLAUDE.md。

---

## 进度总览

| 里程碑 | 状态 | 说明 |
| --- | --- | --- |
| M0：工程骨架 | ✅ | Core / CLI / Desktop / Tests 可构建方案 |
| M1：.ukit 与配置 | ✅ | 创建、打开、校验工程；Config/DefaultGame.ini 读写 |
| M2：ADB 基础设施 | ✅ | ProcessRunner 参数化调用+流式输出、设备枚举/Wi-Fi 连接、启动参数推送/启动闭环 |
| M3：Capture 采集归档 | ✅ | 实时采集 + 导入均完成；支持 --skip-saved 跳过 Saved 拉取 |
| M4：解析器 | ✅ | Android meminfo + UE memreport 摘要与明细 |
| M5：导出 | ✅ | meminfo + memreport CSV/TSV/XLSX 导出全部完成 |
| M6：CLI | ✅ | capture run/list/info 全覆盖；export meminfo/memreport；--format json 全覆盖 |
| M7：WPF GUI | ✅ | 工程/设备/启动参数/采集/解析/结果/导出/日志与设置 8 页完成 |
| M8：测试与验收 | ✅ | 72 单元测试覆盖；READMe + CHANGELOG + 发布脚本；ProcessRunner 流式输出已确认实现 |

---

## 已完成 -> M8 新增

- [x] XLSX 导出自动化单元测试（MemInfo + MemReport）
- [x] MemReport 导出单元测试（CSV/TSV）
- [x] 端到端：解析 → 导出 XLSX/CSV/TSV 全链路
- [x] README.md 全面更新
- [x] CHANGELOG.md 发布记录
- [x] Script\Publish-Shipping.bat 自包含发布脚本
- [x] ProcessRunner 流式输出确认实现（逐行读取 + IProgress<ProcessOutput>）

---

## 已知限制

| 限制 | 影响范围 |
| --- | --- |
| ADB 路径仅支持构造参数或 PATH | 多 adb 版本共存时可能选错（可通过项目配置指定） |
| Agent 分析未开始 | 适配层接口预留但未接入 |
| 第一阶段不含：静态相机报告、基线差分、历史趋势、RenderDoc | 后续迭代 |

---

## 下一步：第二阶段

1. 静态相机性能日志解析与 HTML 报告
2. 基线差分（Baselining）：双文件对比
3. 历史趋势分析
4. RenderDoc Python API 集成（保留为独立能力）
