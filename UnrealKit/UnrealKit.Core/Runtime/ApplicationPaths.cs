namespace UnrealKit.Core.Runtime;

public static class ApplicationPaths
{
    public static string AppDir => AppContext.BaseDirectory;

    /// <summary>
    /// 软件自身的配置目录，与随程序分发的 <c>BaseGame.ini</c> 同级。
    /// 存放跨工程、属于「这台机器上的 UnrealKit」的设置（例如上次打开的工程），
    /// 与工程内的 <c>Config/</c> 分开——后者跟着工程走，换工程就换一份。
    /// </summary>
    public static string AppConfigDir => Path.Combine(AppDir, "Config");
}
