# UnrealKit 第一阶段 TODO

最后更新： | 框架 .NET 9 / WPF | 57 测试通过 | 0 警告 0 错误

实现范围以 旧版Python性能检查工具功能分析.md 中第一阶段核心能力为准，开发约定见仓库根 AGENTS.md。

---

## 进度总览

| 里程碑 | 状态 | 说明 |
| --- | --- | --- |
| M0：工程骨架 | ✅ | Core / CLI / Desktop / Tests 可构建方案 |
| M1：.ukit 与配置 | ✅ | 创建、打开、校验工程；Config/DefaultGame.ini 读写 |
| M2：ADB 基础设施 | ✅ | ProcessRunner、设备枚举/Wi-Fi 连接、启动参数推送/启动闭环 |
| M3：Capture 采集归档 | ✅ | 实时采集 + 导入均完成；支持 --skip-saved 跳过 Saved 拉取 |
| M4：解析器 | ✅ | Android meminfo + UE memreport 摘要与明细 |
| M5：导出 | ✅ | meminfo + memreport XLSX/CSV/TSV 导出全部完成 |
| M6：CLI | ✅ | capture run/list/info 全覆盖；export meminfo/memreport；--format json 全覆盖 |
| M7：WPF GUI | ✅ | 工程/设备/启动参数/采集/解析/结果/导出/日志与设置 8 页完成 |
| M8：测试与验收 | 🔶 | Core 模块已覆盖单元测试；端到端、异常路径、发布待进行 |

---

## 已完成的模块

**M0 工程骨架**：四项目方案（Core/Cli/Desktop/Tests），Directory.Build.props 统一 Nullable/隐式 using/最新 C#，共享 OperationInfrastructure 和 AppVersionInfo。

**M1 工程与配置**：ProjectService 支持创建/打开/校验 .ukit（UTF-8 INI v1），自动生成 Config/DefaultGame.ini 和 Content//Saved//Intermediate/ 目录，非空目录拒绝覆盖。配置优先级：内置默认值 < .ukit < Config/DefaultGame.ini < CLI/GUI 显式参数。

**M2 ADB 基础设施**：ProcessRunner 参数化调用（ArgumentList），支持超时、取消、进程树终止。AdbService 覆盖 devices -l 解析、版本检查、Wi-Fi 连接、--device auto 自动选择。LaunchParameterService 支持 LLM/LLM CSV/OpenGL/Vulkan/Trace/No Update 预设和自定义参数。WPF 设备页与启动参数页已完成完整推送→启动→删除闭环，含确认弹窗和状态栏日志。

**M4 解析器**：AndroidMemInfoParser 和 UnrealMemReportParser，解析失败输出具体行号与缺失原因。附带 7 个 meminfo 脱敏样本 + 1 个 memreport 金样。

---

## 待完成

### M3：Capture 实时采集

- [x] CaptureService.CaptureAsync：ADB 抓取 dumpsys meminfo + 可选拉取 Saved（支持 --skip-saved）
- [x] 采集后自动生成 CaptureManifest.json（设备序列号/型号、配置快照、文件清单 SHA-256）
- [x] CLI capture run --device <serial|auto> [--tag <tag>] [--skip-saved] [--format text|json]
- [x] 已有 Capture 默认只读；重新拉取创建新 Capture 或由用户明确覆盖

### M5：导出补全

- [x] MemReport XLSX/CSV/TSV 导出（Textures/RenderTargets/Objects 明细 + Metadata + Diagnostics）
- [x] CLI export memreport --input <file> --output <output> [--include-details] [--capture-id <id>]

### M6：CLI 补全

- [x] capture run（支持 --skip-saved、--format json）
- [x] capture list / capture info：列举和查看已有 Capture（含 --format json）
- [x] parse meminfo / parse memreport：独立解析命令
- [x] 全局 --format json 机器可读输出全覆盖（capture/parse/export 各大子命令）

### M7：WPF 页面补全

- [x] 工程页：创建/打开工程、工程信息与校验结果
- [x] 采集页：Tag 选择、拉取内容预览、进度与日志
- [x] 解析页：meminfo + memreport 文件选择、解析执行、诊断展示
- [x] 结果页：Capture 浏览、文件列表、meminfo 摘要展示
- [x] 导出页：输入/输出选择、IncludeDetails 开关、CSV/TSV/XLSX 导出进度
- [x] 日志/设置页：操作日志查看、项目配置编辑、ADB 路径配置
- [x] 所有长时操作使用异步 API，不阻塞 UI

### M8：测试与验收

- [ ] Capture 实时采集（ADB）集成测试
- [ ] XLSX 导出自动化单元测试
- [ ] MemReport 解析与导出单元测试
- [ ] 端到端：创建工程 → ADB 采集 → 解析 → 导出 XLSX/CSV/TSV
- [ ] 异常路径：无设备、多设备、命令失败、解析失败、用户取消、覆盖确认
- [ ] 发布：self-contained 包、adb 缺失诊断、README、变更记录

---

## 已知限制

| 限制 | 影响范围 |
| --- | --- |
| ProcessRunner 进程结束后一次性读取 stdout/stderr | 长时 ADB 操作日志不实时 |
| ADB 路径仅支持构造参数或 PATH，无完整优先级诊断 | 多 adb 版本共存时可能选错 |
| CaptureService.CaptureAsync 未实现 | 无法从设备实时采集 |
| MemReport 导出未实现 | 仅 meminfo 可导出 XLSX |
| WPF 仅设备/启动参数页闭环 | 其余 5 页为占位壳 |
| Agent 分析未开始 | 适配层接口预留但未接入 |
| 第一阶段不含：静态相机报告、基线差分、历史趋势、RenderDoc | 后续迭代 |

---

## 下一步优先级

1. **M3 CaptureAsync** — 打通 ADB 实时采集 → Capture 归档完整链路
2. **M7 WPF 工程/采集/解析/结果页** — 让 GUI 具备核心工作流
3. **M5 MemReport 导出 + M6 CLI 补全** — 补齐命令行能力
4. **M8 端到端验证 + 发布** — 可分发版本