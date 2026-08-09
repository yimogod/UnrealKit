# CLI 约定

`UnrealKit.Cli` 只负责命令解析、输出格式化和退出码。所有业务逻辑位于 `UnrealKit.Core`。

## 通用原则

- CLI 与 GUI 使用相同的配置模型和 Core 服务，不重复实现 ADB 调用、解析或导出。
- 命令支持显式选择工程配置、ADB 设备序列号、输入路径和输出路径。
- 成功返回退出码 `0`；参数错误、设备错误、解析失败或导出失败返回非零退出码。
- 日志可读且适合复制；为自动化调用提供 `--format json` 机器可读输出。
- 不依赖「默认第一台设备」或「目录中第一份报告」等隐式选择。歧义输入必须报错或要求显式选择。

## 命令结构

顶层动词：`project`、`adb`、`app`、`commandline`、`capture`、`parse`、`export`。

```text
unrealkit project create <dir> --name <name>
unrealkit project info <project.ukit> [--format json]
unrealkit project validate <project.ukit>

unrealkit adb version [--adb-path <path>]
unrealkit adb devices [--adb-path <path>]
unrealkit adb connect <host:port> [--adb-path <path>]
unrealkit adb disconnect <host:port> [--adb-path <path>]

unrealkit app start --project <project.ukit> --device <serial> [--adb-path <path>]

unrealkit commandline push --project <project.ukit> --device <serial>
                           [--preset <name>] [--custom <args>] [--remote-path <path>] [--adb-path <path>]
unrealkit commandline delete --project <project.ukit> --device <serial>
                             [--remote-path <path>] [--adb-path <path>]

unrealkit capture run --project <project.ukit> --device <serial|auto>
                      [--tag <tag>] [--skip-saved] [--format text|json] [--adb-path <path>]
unrealkit capture import --project <project.ukit> --source <directory>
                         [--platform <platform>] [--tag <tag>] [--capture-id <id>]
unrealkit capture list <...>
unrealkit capture info <...>

unrealkit parse meminfo --input <file> [--format text|json]
unrealkit parse memreport --input <file> [--format text|json]
unrealkit parse static-camera --input <log> --screenshots <dir> [--format json]
unrealkit parse capture-list --project <project.ukit> [--platform <platform>] [--tag <tag>]
unrealkit parse capture-files --capture-dir <path>
unrealkit parse capture-meminfo --project <project.ukit> --capture <capture-id>
                                [--file <filename>] [--analysis-id <id>]

unrealkit export meminfo --input <file> --output <file.csv|file.tsv|file.xlsx>
                         [--include-details] [--capture-id <id>]
unrealkit export memreport --input <file> --output <file.csv|file.tsv|file.xlsx>
                           [--include-details] [--capture-id <id>]
```

新增子命令时保持「动词 + 名词」结构，并同步更新 `README.md` 的 CLI 参考与本文。

## 参数约定

- `--project` 接受 `.ukit` 文件路径，不接受工程目录。
- `--device` 接受 ADB 序列号；`capture run` 额外接受 `auto`，且仅在恰好一台设备在线时成立，否则报错而非任选。
- `--adb-path` 为最高优先级的 adb 来源，解析顺序见 `Doc/设备操作与文件安全.md`。
- `--format` 默认为 `text`；`json` 输出必须是单个可解析的 JSON 文档，不与人类可读日志混排。
- 输出文件的扩展名决定格式，规则见 `Doc/解析导出与诊断.md`。

## 退出码

| 场景 | 退出码 |
| --- | --- |
| 成功 | `0` |
| 参数缺失、未知子命令、参数值非法 | 非零 |
| 工程校验失败（含 Error 级诊断） | 非零 |
| 设备未找到、adb 未解析、外部命令失败 | 非零 |
| 解析或导出失败 | 非零 |

解析产生 Warning 级诊断但结果可用时返回 `0`，并在输出中列出诊断。区分「失败」与「带警告成功」，不要把警告升级为失败。

## 验证

修改 CLI 后至少验证三条路径：成功、参数错误、外部命令失败。
