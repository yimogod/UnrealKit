namespace UnrealKit.Cli;

/// <summary>顶层用法总览。各子命令的详细用法由对应命令类在参数错误时输出。</summary>
internal static class CliUsage
{
    internal static void Print()
    {
        Console.WriteLine("UnrealKit CLI");
        Console.WriteLine("  unrealkit project create <directory> --name <name> [--platform Android|Win64]");
        Console.WriteLine("  unrealkit project info <project.ukit> [--format json]");
        Console.WriteLine("  unrealkit project validate <project.ukit>");
        Console.WriteLine("  unrealkit devices [--project <project.ukit>] [--adb-path <path>]");
        Console.WriteLine("  unrealkit adb version [--adb-path <path>]");
        Console.WriteLine("  unrealkit adb devices [--adb-path <path>]");
        Console.WriteLine("  unrealkit adb connect <host:port> [--adb-path <path>]");
        Console.WriteLine("  unrealkit adb disconnect <host:port> [--adb-path <path>]");
        Console.WriteLine("  unrealkit adb ip <serial> [--adb-path <path>]");
        Console.WriteLine("  unrealkit app start --project <project.ukit> --device <serial> [--adb-path <path>]");
        Console.WriteLine("  unrealkit app console send --project <project.ukit> --device <serial> --cmd <command> [--adb-path <path>]");
        Console.WriteLine("  unrealkit app console run --project <project.ukit> --device <serial> [--sequence <name>] [--cmds <inline>] [--adb-path <path>]");
        Console.WriteLine("  unrealkit commandline push --project <project.ukit> --device <serial> [--preset <name>] [--custom <arguments>] [--adb-path <path>]");
        Console.WriteLine("  unrealkit commandline delete --project <project.ukit> --device <serial> [--adb-path <path>]");
        Console.WriteLine("  unrealkit capture run --project <project.ukit> --device <serial>|auto [--tag <tag>] [--format text|json] [--skip-saved] [--adb-path <path>]");
        Console.WriteLine("  unrealkit capture import --project <project.ukit> --source <directory> [--platform <platform>] [--tag <tag>] [--capture-id <id>]");
        Console.WriteLine("  unrealkit parse meminfo --input <meminfo.txt> [--format text|json]");
        Console.WriteLine("  unrealkit parse win64-meminfo --input <meminfo.txt> [--format text|json]");
        Console.WriteLine("  unrealkit parse memreport --input <memreport.txt> [--format text|json]");
        Console.WriteLine("  unrealkit parse capture-list --project <project.ukit> [--platform <platform>] [--tag <tag>]");
        Console.WriteLine("  unrealkit parse capture-files --capture-dir <path>");
        Console.WriteLine("  unrealkit parse capture-meminfo --project <project.ukit> --capture <capture-id> [--file <filename>] [--analysis-id <id>]");
        Console.WriteLine("  unrealkit parse static-camera --input <log> [--screenshots <dir>] [--format json] [--html-output <path>]");
        Console.WriteLine("  unrealkit export meminfo --input <meminfo.txt> --output <results.csv|results.tsv|results.xlsx> [--include-details] [--capture-id <capture-id>]");
        Console.WriteLine("  unrealkit export memreport --input <memreport.txt> --output <results.csv|results.tsv|results.xlsx> [--include-details] [--capture-id <capture-id>]");
        Console.WriteLine("  unrealkit analyze diff --baseline <file> --current <file> [--source meminfo|win64-meminfo|memreport|static-camera] [--metrics <list>] [--only-changed] [--format text|json]");
        Console.WriteLine("  unrealkit analyze diff --project <project.ukit> --baseline <capture-id> --current <capture-id> [--baseline-file <filename>] [--current-file <filename>] [--source <source>] [--metrics <list>] [--only-changed] [--format text|json]");
        Console.WriteLine("  unrealkit analyze trend --project <project.ukit> [--source <source>] [--platform <platform>] [--tag <tag>] [--device <serial>] [--from <yyyy-MM-dd>] [--to <yyyy-MM-dd>] [--metrics <list>] [--file <filename>] [--output <file.csv|file.tsv|file.xlsx>] [--include-points] [--format text|json]");
        Console.WriteLine("  unrealkit renderdoc run --python <python.exe> --script <script.py> [--args <space-separated args>] [--output <dir>] [--workdir <dir>] [--format text|json]");
        Console.WriteLine("  unrealkit download --project <project.ukit> --platform <Android|Win64> [--format text|json]");
        Console.WriteLine("  unrealkit download install --project <project.ukit> --device <serial> --apk <path> [--adb-path <path>]");
    }
}
