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
            GameRootTemplate = "/sdcard/Custom/{PackageName}/{UnrealProjectName}"
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
        var profile = new Win64PlatformProfile(@"C:\Builds\MyGame\MyGame.exe", @"C:\Builds\MyGame");

        var target = profile.Resolve("MyGame");

        Assert.Equal(TargetPlatform.Win64, target.Platform);
        Assert.Equal(DevicePathStyle.Windows, target.PathStyle);
        Assert.Equal("MyGame", target.ProcessIdentity);
        Assert.Equal(@"C:\Builds\MyGame\MyGame.exe", target.LaunchTarget);
        Assert.Null(target.LaunchActivity);
        Assert.Equal(@"C:\Builds\MyGame\MyGame\Saved", target.SavedRootPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"Builds\MyGame")]
    public void Win64Profile_Resolve_NonAbsoluteWorkingDirectory_Throws(string workingDirectory)
    {
        // 相对路径会按当前进程工作目录解析，GUI 与 CLI 下指向不同位置。
        var profile = new Win64PlatformProfile(@"C:\Builds\MyGame\MyGame.exe", workingDirectory);

        Assert.ThrowsAny<Exception>(() => profile.Resolve("MyGame"));
    }

    [Fact]
    public void AndroidProfile_Validate_RejectsWindowsStyleTemplate()
    {
        var profile = AndroidPlatformProfile.CreateDefaults() with { GameRootTemplate = @"C:\sdcard\Game" };

        Assert.Throws<ArgumentException>(() => profile.Validate());
    }

    [Fact]
    public void Win64Profile_Validate_AllowsEmptyFieldsForPartialConfiguration()
    {
        // 保存一份填了一半的配置是合法的：完整性在 Resolve 时才要求，
        // 否则用户无法先存下工作目录再回来补可执行文件。
        Win64PlatformProfile.CreateDefaults().Validate();
    }

    [Fact]
    public void CombineDevicePath_UsesPlatformSeparator()
    {
        var android = AndroidPlatformProfile.CreateDefaults() with { PackageName = "com.example.game" };
        var win64 = new Win64PlatformProfile(@"C:\Builds\MyGame\MyGame.exe", @"C:\Builds\MyGame");

        var androidTarget = android.Resolve("Sample");
        var win64Target = win64.Resolve("MyGame");

        // 在 Windows 主机上用 Path.Combine 拼 Android 路径会写入反斜杠，设备端无法识别。
        Assert.Equal(
            "/sdcard/Android/data/com.example.game/files/UnrealGame/Sample/Sample/uecommandline.txt",
            androidTarget.CombineDevicePath(androidTarget.GameRootPath, "uecommandline.txt"));
        Assert.Equal(
            @"C:\Builds\MyGame\MyGame\uecommandline.txt",
            win64Target.CombineDevicePath(win64Target.GameRootPath, "uecommandline.txt"));
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
