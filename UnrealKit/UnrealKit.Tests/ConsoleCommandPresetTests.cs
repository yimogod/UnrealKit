using UnrealKit.Core.CommandChannel;
using UnrealKit.Core.Projects;

namespace UnrealKit.Tests;

/// <summary>
/// 控制台预设指令的模型语义与 INI 往返、默认值合并。
/// </summary>
public sealed class ConsoleCommandPresetTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "UnrealKit.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void BuildCommand_Bool_AppendsZeroOrOne()
    {
        var preset = new ConsoleCommandPreset("showflag.Fog", ConsoleCommandKind.Bool, "Rendering", "showflag.Fog", null, null, "");

        Assert.Equal("showflag.Fog 1", preset.BuildCommand(boolValue: true));
        Assert.Equal("showflag.Fog 0", preset.BuildCommand(boolValue: false));
    }

    [Fact]
    public void BuildCommand_Value_UsesSuppliedValue()
    {
        var preset = new ConsoleCommandPreset("r.ScreenPercentage", ConsoleCommandKind.Value, "Rendering", "r.ScreenPercentage", null, "100", "");

        Assert.Equal("r.ScreenPercentage 80", preset.BuildCommand(value: "80"));
    }

    /// <summary>清空输入框不该发出一条缺参数的指令，回落到预设的默认值。</summary>
    [Fact]
    public void BuildCommand_Value_BlankFallsBackToDefaultValue()
    {
        var preset = new ConsoleCommandPreset("r.ScreenPercentage", ConsoleCommandKind.Value, "Rendering", "r.ScreenPercentage", null, "100", "");

        Assert.Equal("r.ScreenPercentage 100", preset.BuildCommand(value: "   "));
    }

    [Fact]
    public void BuildCommand_Action_ReturnsCommandText()
    {
        var preset = new ConsoleCommandPreset("stat unit", ConsoleCommandKind.Action, "Stats", null, "stat unit", null, "");

        Assert.Equal("stat unit", preset.BuildCommand());
    }

    [Fact]
    public void BuildCommand_MissingCvar_Throws()
    {
        var preset = new ConsoleCommandPreset("broken", ConsoleCommandKind.Bool, "Rendering", null, null, null, "");

        var exception = Assert.Throws<InvalidOperationException>(() => preset.BuildCommand(boolValue: true));
        Assert.Contains("Cvar", exception.Message);
    }

    [Fact]
    public void SupportsReadBack_OnlyBoolAndValue()
    {
        Assert.True(new ConsoleCommandPreset("a", ConsoleCommandKind.Bool, "g", "a", null, null, "").SupportsReadBack);
        Assert.True(new ConsoleCommandPreset("b", ConsoleCommandKind.Value, "g", "b", null, "0", "").SupportsReadBack);
        Assert.False(new ConsoleCommandPreset("c", ConsoleCommandKind.Action, "g", null, "c", null, "").SupportsReadBack);
    }

    [Fact]
    public void ResolveVariableType_MapsKindToVariableType()
    {
        Assert.Equal(ConsoleVariableType.Bool,
            new ConsoleCommandPreset("a", ConsoleCommandKind.Bool, "g", "a", null, null, "").ResolveVariableType());
        Assert.Equal(ConsoleVariableType.Number,
            new ConsoleCommandPreset("b", ConsoleCommandKind.Value, "g", "b", null, "0", "").ResolveVariableType());
    }

    [Fact]
    public void ResolveVariableType_Action_Throws()
    {
        var preset = new ConsoleCommandPreset("stat unit", ConsoleCommandKind.Action, "Stats", null, "stat unit", null, "");

        Assert.Throws<InvalidOperationException>(() => preset.ResolveVariableType());
    }

    [Fact]
    public void CreateDefaults_IncludesBuiltInPresets()
    {
        var settings = ProjectSettings.CreateDefaults("Sample");

        Assert.NotEmpty(settings.ConsoleCommandPresets);
        Assert.Contains(settings.ConsoleCommandPresets, preset => preset.Kind == ConsoleCommandKind.Bool);
        Assert.Contains(settings.ConsoleCommandPresets, preset => preset.Kind == ConsoleCommandKind.Value);
        Assert.Contains(settings.ConsoleCommandPresets, preset => preset.Kind == ConsoleCommandKind.Action);
    }

    /// <summary>内置默认里每条都得能合成指令，否则界面上点了就抛异常。</summary>
    [Fact]
    public void Defaults_AllBuildableAndWellFormed()
    {
        foreach (var preset in ConsoleCommandPresetDefaults.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.Group));
            Assert.False(string.IsNullOrWhiteSpace(preset.BuildCommand(true, preset.DefaultValue)));

            if (preset.Kind == ConsoleCommandKind.Value)
            {
                Assert.False(string.IsNullOrWhiteSpace(preset.DefaultValue));
            }
        }
    }

    [Fact]
    public async Task UpdateSettingsAsync_PersistsAllThreeKinds()
    {
        var service = new ProjectService();
        var created = await service.CreateProjectAsync(
            new CreateProjectRequest(Path.Combine(_temporaryDirectory, "PresetRoundTrip"), "PresetRoundTrip"));
        var settings = created.Project.Settings with
        {
            ConsoleCommandPresets =
            [
                new("showflag.Custom", ConsoleCommandKind.Bool, "Custom", "showflag.Custom", null, null, ""),
                new("r.Custom", ConsoleCommandKind.Value, "Custom", "r.Custom", null, "42", ""),
                new("custom action", ConsoleCommandKind.Action, "Custom", null, "custom action", null, "")
            ]
        };

        await service.UpdateSettingsAsync(created.Project, settings);
        var reopened = await service.OpenProjectAsync(created.Project.ProjectFilePath);

        var boolPreset = Single(reopened, "showflag.Custom");
        Assert.Equal(ConsoleCommandKind.Bool, boolPreset.Kind);
        Assert.Equal("Custom", boolPreset.Group);
        Assert.Equal("showflag.Custom", boolPreset.Cvar);

        var valuePreset = Single(reopened, "r.Custom");
        Assert.Equal(ConsoleCommandKind.Value, valuePreset.Kind);
        Assert.Equal("42", valuePreset.DefaultValue);

        var actionPreset = Single(reopened, "custom action");
        Assert.Equal(ConsoleCommandKind.Action, actionPreset.Kind);
        Assert.Equal("custom action", actionPreset.Command);
    }

    /// <summary>内置默认打底：配置里没写的预设仍然在，界面上不会因为改了一条就少一片。</summary>
    [Fact]
    public async Task OpenProjectAsync_KeepsBuiltInDefaultsAlongsideCustomPresets()
    {
        var service = new ProjectService();
        var created = await service.CreateProjectAsync(
            new CreateProjectRequest(Path.Combine(_temporaryDirectory, "PresetMerge"), "PresetMerge"));
        await AppendPresetLineAsync(created.Project, "MyCustom=Action|Custom|||my.custom.command");

        var reopened = await service.OpenProjectAsync(created.Project.ProjectFilePath);

        Assert.Contains(reopened.Settings.ConsoleCommandPresets, preset => preset.Name == "MyCustom");
        foreach (var builtIn in ConsoleCommandPresetDefaults.All)
        {
            Assert.Contains(reopened.Settings.ConsoleCommandPresets, preset => preset.Name == builtIn.Name);
        }
    }

    /// <summary>同名覆盖：配置改了默认值，读回来是配置的值，而不是内置默认。</summary>
    [Fact]
    public async Task OpenProjectAsync_ConfiguredPresetOverridesBuiltInByName()
    {
        var service = new ProjectService();
        var created = await service.CreateProjectAsync(
            new CreateProjectRequest(Path.Combine(_temporaryDirectory, "PresetOverride"), "PresetOverride"));
        var configPath = ConfigPath(created.Project);
        var config = await File.ReadAllTextAsync(configPath);
        await File.WriteAllTextAsync(configPath, config.Replace(
            "r.ScreenPercentage=Value|Rendering|r.ScreenPercentage|100|",
            "r.ScreenPercentage=Value|MyRendering|r.ScreenPercentage|75|"));

        var reopened = await service.OpenProjectAsync(created.Project.ProjectFilePath);

        var preset = Single(reopened, "r.ScreenPercentage");
        Assert.Equal("75", preset.DefaultValue);
        Assert.Equal("MyRendering", preset.Group);
        // 说明文本在 INI 里没有段位，覆盖时保留内置默认的说明。
        Assert.False(string.IsNullOrWhiteSpace(preset.Description));
    }

    [Fact]
    public async Task OpenProjectAsync_UnknownKind_FailsInsteadOfFallingBack()
    {
        var service = new ProjectService();
        var created = await service.CreateProjectAsync(
            new CreateProjectRequest(Path.Combine(_temporaryDirectory, "PresetBadKind"), "PresetBadKind"));
        await AppendPresetLineAsync(created.Project, "Broken=Boolean|Custom|some.cvar||");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => service.OpenProjectAsync(created.Project.ProjectFilePath));

        Assert.Contains("Broken", exception.Message);
        Assert.Contains("Boolean", exception.Message);
    }

    [Fact]
    public async Task OpenProjectAsync_BoolPresetWithoutCvar_Fails()
    {
        var service = new ProjectService();
        var created = await service.CreateProjectAsync(
            new CreateProjectRequest(Path.Combine(_temporaryDirectory, "PresetNoCvar"), "PresetNoCvar"));
        await AppendPresetLineAsync(created.Project, "Broken=Bool|Custom|||");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => service.OpenProjectAsync(created.Project.ProjectFilePath));

        Assert.Contains("Cvar", exception.Message);
    }

    [Fact]
    public async Task OpenProjectAsync_ActionPresetWithoutCommand_Fails()
    {
        var service = new ProjectService();
        var created = await service.CreateProjectAsync(
            new CreateProjectRequest(Path.Combine(_temporaryDirectory, "PresetNoCommand"), "PresetNoCommand"));
        await AppendPresetLineAsync(created.Project, "Broken=Action|Custom|||");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => service.OpenProjectAsync(created.Project.ProjectFilePath));

        Assert.Contains("Command", exception.Message);
    }

    private static ConsoleCommandPreset Single(UkitProject project, string name) =>
        Assert.Single(project.Settings.ConsoleCommandPresets, preset => preset.Name == name);

    private static string ConfigPath(UkitProject project) =>
        Path.Combine(project.RootDirectory, "Config", "DefaultGame.ini");

    private static async Task AppendPresetLineAsync(UkitProject project, string line)
    {
        var configPath = ConfigPath(project);
        var config = await File.ReadAllTextAsync(configPath);
        await File.WriteAllTextAsync(configPath, config.Replace(
            "[UnrealKit.ConsoleCommandPresets]",
            $"[UnrealKit.ConsoleCommandPresets]{Environment.NewLine}{line}"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, recursive: true);
    }
}
