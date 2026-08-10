# UnrealKit 第三阶段 TODO

基于 UE `-message` 机制讨论，为 UE Android 应用增加控制台指令下发、序列编排与 Capture 集成能力。

最后更新：2026-08-09 | 框架 .NET 9 / WPF

---

## 第三阶段目标

在第二阶段能力（静态相机、基线差分、历史趋势、RenderDoc、Agent 分析）基础上，增加 UE 控制台指令通道，实现性能测试编排：

1. **指令下发**：向运行中的 UE Android 应用发送控制台指令（类比 UnrealFrontend + `-message` 的发现-控制体验）
2. **指令序列**：时序编排（指令 → 等待 → 标记），支持工程预设和 CLI 内联
3. **条件执行**：基于设备反馈（logcat 回读）的条件分支
4. **Capture 集成**：采集流程的 Pre/Post 指令钩子

---

## 机制选型

| 层级 | 机制 | 能力 | 优先级 |
| --- | --- | --- | --- |
| L1：ADB Broadcast | `adb shell am broadcast -a android.intent.action.RUN -e cmd "..."` | 单向指令下发，零 UE 端配置 | **P1 首选** |
| L2：ADB Broadcast + logcat 回读 | L1 + 并行读 logcat 抓 UE 日志行 | 可解析输出做阈值判断 | P2 |
| L3：TCP 端口转发 | `adb forward` + TCP 双向通信 | 完整命令回显，交互式调试 | P3（远期） |
| ~~L4：UDP Messaging~~ | 需 UE 端启用 UdpMessaging + 同 WiFi | 完美对齐 `-message` 语义但实现成本过高 | **不纳入** |

---

## 完成情况

### P1：基础指令下发 + CLI ✅/⬜

- [x] `IAdbService` 新增 `SendConsoleCommandAsync(serialNumber, command, ...)`
- [x] CLI：`app console send --device <serial> [--cmd <command>] [--adb-path <path>]`
- [x] 工程配置：`.ukit` 中 `PackageName` 字段用于构造 `am broadcast` 的 `-n` 参数（如需指定包名）
- [x] `AdbService` 中 `ValidatePackageName` 和 `RunDeviceCommandAsync` 复用现有基础设施

### P2：指令序列模型 + 序列执行器

- [x] `UnrealKit.Core.Console` 命名空间
  - [x] `ConsoleCommandModels`：单条指令、序列定义、执行结果模型
  - [x] `IConsoleCommandService` / `ConsoleCommandService`：发送单条 + 批量指令
  - [x] `ICommandSequenceRunner` / `CommandSequenceRunner`：时序编排（指令 + 延迟 + 标记）
  - [x] `CommandSequenceModels`：序列定义结构（Commands / Wait / Tag / Group）
- [x] 工程配置：`.ukit` `[ConsoleSequences]` section 定义可复用序列
- [x] CLI：`app console run --project <project.ukit> --device <serial> [--sequence <name>] [--cmds "cmd1;wait 2;cmd2"]`
- [x] 日志输出：每条指令发出后的时间戳、设备、内容、结果（L1 为"已发送"）

### P3：logcat 回读 + 条件执行

- [x] `IAdbService` 新增 `StreamLogcatAsync(serialNumber, filter, ...)` 返回可取消的流
- [x] `ICommandSequenceRunner` 支持条件表达式：
  - `logcat_contains:"pattern" -> action`
  - Action 类型：`send:cmd`、`capture:tag`、`fail:message`、`retry`
- [x] 超时：单条指令的超时 + 序列级超时
- [x] 取消：`CancellationToken` 贯穿全链路

### P4：Capture 流程集成

- [x] `CaptureService` 新增 `PreCaptureCommands` / `PostCaptureCommands` 钩子
- [x] 采集链路：
  ```
  PreCaptureCommands → dumpsys → Pull → PostCaptureCommands
  ```
- [x] 具体场景：
  - [x] 采集前后设/还原 cvar（`r.ScreenPercentage 100` → 采集 → 还原 `r.ScreenPercentage 50`）
  - [x] 批量采集切场景（采集 1 → `openlevel Map2` → 采集 2 → ...）
  - [x] 采集前触发 UE 内置报告（`memreport -full` → pull 报告文件）
- [x] CLI：`capture run` 自动使用工程预设的 Pre/Post 序列
- [x] `CaptureManifest.json` 记录 Pre/Post 序列执行结果（扩展字段，遵循稳定契约第 4 条）

### P5：WPF 控制台页面

- [x] 设备控制台页：设备选择 + 指令输入 + 发送按钮 + 输出回显
- [x] 序列配置页：可视化编辑 `.ukit` 的 `[ConsoleSequences]`
- [x] Capture 页集成：Pre/Post 序列选择下拉框
- [x] 取消支持：长耗时序列允许中止

### P6：TCP 双向控制台（远期 P3）

- [x] `TcpConsoleService`：ADB forward + TCP 连接管理
- [x] 命令回显解析
- [x] CLI + WPF 适配

---

## 架构约束

遵循根 AGENTS.md 核心不变项：

1. **单向依赖**：`Console` 模块在 Core 层，不引用 WPF / CLI 框架
2. **逻辑不重复**：指令下发、序列执行、logcat 回读只在 Core 实现一次；GUI 和 CLI 都是适配层
3. **原始数据只读**：指令序列的执行日志写入 `Saved/`，不修改 `Content/`
4. **无隐式选择**：设备必须显式指定，不取"默认第一台"
5. **失败要具体**：指令发送失败给出 ADB 退出码 + stderr，序列执行失败指明第几步、什么原因
6. **参数化调用外部命令**：使用 `ProcessStartInfo.ArgumentList`，禁止字符串拼接 shell 命令
7. **破坏性操作先确认**：（本模块无破坏性操作，指令下发不影响设备文件系统）
8. **配置不硬编码**：序列定义在 `.ukit` 中，不在代码中硬编码
9. **扩展名不说谎**：序列执行日志用 `.log` 或 `.json`，非 `.xlsx`
10. **不阻塞 UI 线程**：指令发送、logcat 回读均异步，支持取消

---

## 下一步优先级

1. ⬜ **P1 基础指令下发** -- `SendConsoleCommandAsync` + CLI `app console send`
2. ⬜ **P2 指令序列** -- 序列模型 + Runner + `.ukit` 预设 + CLI 内联
3. ⬜ **P3 logcat 条件执行** -- 回读流 + 条件表达式 + 超时/取消
4. ⬜ **P4 Capture 集成** -- Pre/Post 钩子全覆盖
5. ⬜ **P5 WPF** -- 控制台页 + 序列配置页
6. ⬜ **P6 TCP 双向** -- 远期
