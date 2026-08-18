namespace UnrealKit.Core.Runtime;

public static class ApplicationPaths
{
    public static string AppDir => AppContext.BaseDirectory;

    /// <summary>
    /// 用户级状态目录。存放跨工程、跟随当前用户的状态（例如上次打开的工程），
    /// 与随程序分发的 <see cref="AppDir"/> 分开——程序目录可能只读或被整体替换。
    /// </summary>
    public static string UserStateDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UnrealKit");
}
