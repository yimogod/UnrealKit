using UnrealKit.Core.Projects;

namespace UnrealKit.Core.Runtime;

/// <summary>
/// 工程内的用户设置：使用过程中在某个工程里做出的界面选择，目前是平台作用域。
///
/// 落在工程的 <c>Config/UserSetting.ini</c> 而不是软件目录：这些选择是「在这个工程里
/// 看哪个平台」，换工程就该换一份。放在软件级会让上一个工程的作用域跟到下一个工程，
/// 把另一个平台的设备与归档静默藏起来。
///
/// 也不写进 <c>Config/DefaultGame.ini</c>：那是可版本化的工程配置，不该因为「谁上次看的是
/// 哪个平台」产生 diff。<c>UserSetting.ini</c> 属于个人状态，建议在版本控制中忽略。
/// </summary>
public interface IUserSettingStore
{
    /// <summary>
    /// 读取该工程上次选择的平台作用域。没有记录、文件不可读或取值无法识别时返回 <c>null</c>，
    /// 由调用方保留当前作用域——不把「记录缺失」当成「记录为全部」，
    /// 那会在打开工程时把用户刚选的平台重置掉。
    /// </summary>
    Task<PlatformScope?> TryGetPlatformScopeAsync(UkitProject project, CancellationToken cancellationToken = default);

    /// <summary>记录该工程当前的平台作用域，覆盖上一条记录。</summary>
    Task SavePlatformScopeAsync(UkitProject project, PlatformScope scope, CancellationToken cancellationToken = default);
}

/// <summary>
/// 基于 INI 的工程内用户设置实现，写入 <see cref="UkitProject.UserSettingFilePath"/>。
/// </summary>
public sealed class UserSettingStore : IUserSettingStore
{
    private const string ScopeSection = "UnrealKit.Scope";
    private const string PlatformScopeKey = "Platform";

    public async Task<PlatformScope?> TryGetPlatformScopeAsync(UkitProject project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var value = await IniStateFile.TryReadValueAsync(
            project.UserSettingFilePath, ScopeSection, PlatformScopeKey, cancellationToken);

        // 无法识别的取值当作没有记录而不是抛出：状态文件是便利设施，
        // 手工改坏它不该让应用起不来，也不该让界面聚焦到用户没选过的平台。
        return PlatformScope.TryParse(value, out var scope) ? scope : null;
    }

    public Task SavePlatformScopeAsync(UkitProject project, PlatformScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(scope);
        return IniStateFile.WriteValueAsync(
            project.UserSettingFilePath, ScopeSection, PlatformScopeKey, scope.Name, cancellationToken);
    }
}
