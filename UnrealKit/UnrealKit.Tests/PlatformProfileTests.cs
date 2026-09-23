using UnrealKit.Core.Projects;

namespace UnrealKit.Tests;

public sealed class PlatformProfileTests
{
    [Fact]
    public void AndroidProfile_Resolve_ExpandsTemplatePlaceholders()
    {
        var profile = AndroidPlatformProfile.CreateDefaults() with { PackageName = "com.example.game" };

        var target = profile.Resolve("Sample");

        Assert.Equal(TargetPlatform.Android, target.Platform);
        Assert.Equal(DevicePathStyle.Unix, target.PathStyle);
        Assert.Equal("com.example.game", target.ProcessIdentity);
        Assert.Equal("/sdcard/Android/data/com.example.game/files/UnrealGame/Sample/Sample", target.GameRootPath);
        Assert.Equal("/sdcard/Android/data/com.example.game/files/UnrealGame/Sample/Sample/Saved", target.SavedRootPath);
    }

    [Fact]
    public void AndroidProfile_Resolve_DerivesSavedRootFromGameRoot()
    {
        // Saved 目录不单独配置：改了 Game 目录，Saved 必须跟着走，
        // 否则采集会在旧路径下拉到空目录却报告成功。
        var profile = AndroidPlatformProfile.CreateDefaults() with
        {
            PackageName = "com.example.game",
            GameRoot = "/sdcard/Custom/{PackageName}/{UnrealProjectName}"
        };

        var target = profile.Resolve("Sample");

        Assert.Equal("/sdcard/Custom/com.example.game/Sample", target.GameRootPath);
        Assert.Equal("/sdcard/Custom/com.example.game/Sample/Saved", target.SavedRootPath);
    }

    [Fact]
    public void AndroidProfile_Resolve_MissingPackageName_ThrowsNamingTheField()
    {
        // 包名缺失时展开出的路径含字面量 {PackageName}，采集会拉到不存在的目录。
        var exception = Assert.Throws<InvalidOperationException>(
            () => AndroidPlatformProfile.CreateDefaults().Resolve("Sample"));

        Assert.Contains("PackageName", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Win64Profile_Resolve_DerivesProcessNameFromExecutable()
    {
        var packageDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(packageDir, "MyGame.exe"), string.Empty);
            var profile = new Win64PlatformProfile("MyGame.exe", GameRoot: packageDir);

            var target = profile.Resolve("MyGame");

            Assert.Equal(TargetPlatform.Win64, target.Platform);
            Assert.Equal(DevicePathStyle.Windows, target.PathStyle);
            Assert.Equal("MyGame", target.ProcessIdentity);
            Assert.Equal(Path.Combine(packageDir, "MyGame.exe"), target.LaunchTarget);
            Assert.Null(target.LaunchActivity);
            Assert.Equal(Path.Combine(packageDir, "Saved"), target.SavedRootPath);
        }
        finally
        {
            if (Directory.Exists(packageDir)) Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public void Win64Profile_Resolve_NoPackageDirectory_Throws()
    {
        // 没有运行时构建包目录（用户尚未在「安装包」页选择/下载）必须报错，
        // 不能默默拿一个猜测路径去启动——那会启动错误的 exe 却报告成功。
        var profile = new Win64PlatformProfile("MyGame.exe", GameRoot: null);

        Assert.Throws<InvalidOperationException>(() => profile.Resolve("MyGame"));
    }

    [Fact]
    public void Win64Profile_Resolve_ExecutableNotFoundInPackageDirectory_Throws()
    {
        // exe 只在包目录第一层找，不递归、不跨层猜测：找不到就是配置或下载有问题。
        var packageDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var profile = new Win64PlatformProfile("MyGame.exe", GameRoot: packageDir);

            var exception = Assert.Throws<InvalidOperationException>(() => profile.Resolve("MyGame"));
            Assert.Contains("MyGame.exe", exception.Message);
        }
        finally
        {
            if (Directory.Exists(packageDir)) Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public void AndroidProfile_Validate_RejectsWindowsStyleTemplate()
    {
        var profile = AndroidPlatformProfile.CreateDefaults() with { GameRoot = @"C:\sdcard\Game" };

        Assert.Throws<ArgumentException>(() => profile.Validate());
    }

    [Fact]
    public void Win64Profile_Validate_AllowsEmptyFieldsForPartialConfiguration()
    {
        // 保存一份填了一半的配置是合法的：完整性在 Resolve 时才要求，
        // 否则用户无法先存下工程再回来补可执行文件名。
        Win64PlatformProfile.CreateDefaults().Validate();
    }

    [Fact]
    public void Win64Profile_Validate_RejectsPackageNameWithPathSeparator()
    {
        // PackageName 只存文件名：路径由运行时的构建包目录决定，配置里混入路径会造成
        // 两处来源打架。
        var profile = new Win64PlatformProfile(@"Sub\MyGame.exe");

        Assert.Throws<ArgumentException>(() => profile.Validate());
    }

    [Fact]
    public void CombineDevicePath_UsesPlatformSeparator()
    {
        var packageDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(packageDir, "MyGame.exe"), string.Empty);

            var android = AndroidPlatformProfile.CreateDefaults() with { PackageName = "com.example.game" };
            var win64 = new Win64PlatformProfile("MyGame.exe", GameRoot: packageDir);

            var androidTarget = android.Resolve("Sample");
            var win64Target = win64.Resolve("MyGame");

            // 在 Windows 主机上用 Path.Combine 拼 Android 路径会写入反斜杠，设备端无法识别。
            Assert.Equal(
                "/sdcard/Android/data/com.example.game/files/UnrealGame/Sample/Sample/uecommandline.txt",
                androidTarget.CombineDevicePath(androidTarget.GameRootPath, "uecommandline.txt"));
            Assert.Equal(
                Path.Combine(packageDir, "uecommandline.txt"),
                win64Target.CombineDevicePath(win64Target.GameRootPath, "uecommandline.txt"));
        }
        finally
        {
            if (Directory.Exists(packageDir)) Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public void AndroidProfile_Defaults_EmptyFtpPath()
    {
        // FtpPath 是可空可选项：默认空串，表示该平台未配置 FTP 父目录。
        Assert.Equal(string.Empty, AndroidPlatformProfile.CreateDefaults().FtpPath);
        Assert.Equal(string.Empty, Win64PlatformProfile.CreateDefaults().FtpPath);
    }

    [Fact]
    public void ProfileFor_CoversEveryDeclaredPlatform()
    {
        // 新增平台时若忘记在 ProjectSettings 上补 profile 属性，此测试失败。
        var settings = ProjectSettings.CreateDefaults("Sample");

        foreach (var platform in Enum.GetValues<TargetPlatform>())
        {
            Assert.NotNull(settings.ProfileFor(platform));
        }
    }
}
