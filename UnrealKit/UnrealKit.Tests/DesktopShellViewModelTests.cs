using UnrealKit.Core.Adb;
using UnrealKit.Core.Devices;
using UnrealKit.Core.Operations;
using UnrealKit.Core.Processes;
using UnrealKit.Core.Projects;
using UnrealKit.Core.Runtime;
using UnrealKit.Desktop.Models;
using UnrealKit.Desktop.Services;
using UnrealKit.Desktop.ViewModels;

namespace UnrealKit.Tests;

public sealed class DesktopShellViewModelTests
{
    [Fact]
    public async Task DeviceAndLaunchParameterWorkflow_UsesSelectedDeviceAndShowsTarget()
    {
        var adb = new RecordingAdbService();
        var project = CreateProject();
        var confirmation = new RecordingConfirmationService(true);
        var viewModel = new ShellViewModel(new StaticProjectService(project), new StaticAdbServiceFactory(adb), confirmation, new FakeEditorSettingStore(), new FakeUserSettingStore())
        {
            ProjectFilePath = project.ProjectFilePath,
            CustomLaunchArguments = "-trace=memory"
        };

        await ((AsyncDelegateCommand)viewModel.OpenProjectCommand).ExecuteAsync();
        await ((AsyncDelegateCommand)viewModel.RefreshDevicesCommand).ExecuteAsync();
        viewModel.SelectedDevice = viewModel.Devices.First(d => d.Platform == "Android");
        Assert.NotNull(viewModel.SelectedDevice);
        Assert.Equal("R58M123ABC", viewModel.SelectedDevice?.Id);
        await ((AsyncDelegateCommand)viewModel.PushLaunchParametersCommand).ExecuteAsync();
        await ((AsyncDelegateCommand)viewModel.StartApplicationCommand).ExecuteAsync();
        await ((AsyncDelegateCommand)viewModel.DeleteLaunchParametersCommand).ExecuteAsync();

        Assert.Equal("R58M123ABC", viewModel.SelectedDevice?.Id);
        Assert.Contains("R58M123ABC", viewModel.LaunchOperationSummary);
        Assert.Contains("com.example.game", viewModel.LaunchOperationSummary);
        Assert.Contains("uecommandline.txt", viewModel.LaunchParameterPreview);
        Assert.Equal("R58M123ABC", adb.PushSerialNumber);
        Assert.Contains("-trace=memory", adb.PushedContent);
        Assert.Equal(("R58M123ABC", "com.example.game", "com.example.game.MainActivity"), adb.StartRequest);
        Assert.Equal(("R58M123ABC", "com.example.game"), adb.ForceStopRequest);
        // 启动前先尝试停止旧实例：stop 必须排在 start 之前。
        Assert.Equal(["stop", "start"], adb.OperationOrder);
        Assert.Equal("R58M123ABC", adb.DeleteSerialNumber);
        Assert.True(confirmation.WasAsked);
        Assert.NotEmpty(viewModel.OperationLogs);
    }

    [Fact]
    public async Task OperationLogs_CarryTimestampAndCategory_AndClearCommandEmptiesThem()
    {
        var adb = new RecordingAdbService();
        var project = CreateProject();
        var viewModel = new ShellViewModel(new StaticProjectService(project), new StaticAdbServiceFactory(adb), new RecordingConfirmationService(true), new FakeEditorSettingStore(), new FakeUserSettingStore())
        {
            ProjectFilePath = project.ProjectFilePath
        };

        await ((AsyncDelegateCommand)viewModel.OpenProjectCommand).ExecuteAsync();

        Assert.NotEmpty(viewModel.OperationLogs);
        Assert.True(viewModel.HasOperationLogs);
        // 时间戳与分类由 AddOperationLog 统一生成，调用方不再自带前缀。
        Assert.All(viewModel.OperationLogs, entry =>
        {
            Assert.NotEqual(default, entry.Timestamp);
            Assert.False(string.IsNullOrWhiteSpace(entry.Category));
            Assert.DoesNotContain('[', entry.Message);
        });

        var saved = viewModel.OperationLogs[0].ToString();
        Assert.Contains($"[{viewModel.OperationLogs[0].Category}]", saved);

        Assert.True(viewModel.ClearOperationLogsCommand.CanExecute(null));
        viewModel.ClearOperationLogsCommand.Execute(null);

        Assert.Empty(viewModel.OperationLogs);
        Assert.False(viewModel.HasOperationLogs);
        Assert.False(viewModel.ClearOperationLogsCommand.CanExecute(null));
    }

    [Fact]
    public async Task SaveOperationLogs_WritesOneLinePerEntry()
    {
        var adb = new RecordingAdbService();
        var project = CreateProject();
        var viewModel = new ShellViewModel(new StaticProjectService(project), new StaticAdbServiceFactory(adb), new RecordingConfirmationService(true), new FakeEditorSettingStore(), new FakeUserSettingStore())
        {
            ProjectFilePath = project.ProjectFilePath
        };

        await ((AsyncDelegateCommand)viewModel.OpenProjectCommand).ExecuteAsync();
        var expected = viewModel.OperationLogs.Count;
        Assert.True(expected > 0);

        var outputPath = Path.Combine(Path.GetTempPath(), $"ukit-log-test-{Guid.NewGuid():N}.txt");
        try
        {
            await viewModel.SaveOperationLogsAsync(outputPath);
            var lines = await File.ReadAllLinesAsync(outputPath);
            Assert.Equal(expected, lines.Length);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task ParseMemReport_PopulatesMetricsAndLeavesBusyCleared()
    {
        var viewModel = CreateViewModel();
        viewModel.MemReportInputPath = TestDataPath("MemReport", "complete-details.memreport");

        Assert.True(viewModel.ParseMemReportCommand.CanExecute(null));
        await ((AsyncDelegateCommand)viewModel.ParseMemReportCommand).ExecuteAsync();

        Assert.NotEmpty(viewModel.MemReportMetrics);
        Assert.Contains("Changelist", viewModel.MemReportParseDescription);
        // 嵌套复用 core 方法后，外层解析结束必须把忙碌状态清掉。
        Assert.False(viewModel.IsBusy);
    }

    [Theory]
    [InlineData("MemInfo", "complete-meminfo.txt", ".tsv")]
    [InlineData("MemInfo", "complete-meminfo.txt", ".xlsx")]
    [InlineData("MemReport", "complete-details.memreport", ".tsv")]
    [InlineData("MemReport", "complete-details.memreport", ".xlsx")]
    public async Task ExportCaptureData_WritesFileForEachInputAndFormat(string folder, string sample, string extension)
    {
        var viewModel = CreateViewModel();
        var outputPath = Path.Combine(Path.GetTempPath(), $"ukit-export-{Guid.NewGuid():N}{extension}");

        viewModel.ExportInputPath = TestDataPath(folder, sample);
        viewModel.ExportOutputPath = outputPath;

        Assert.True(viewModel.ExportCaptureDataCommand.CanExecute(null));
        try
        {
            await ((AsyncDelegateCommand)viewModel.ExportCaptureDataCommand).ExecuteAsync();

            Assert.True(File.Exists(outputPath), $"未生成导出文件：{viewModel.ExportProgress}");
            Assert.Contains("Exported to", viewModel.ExportProgress);
            Assert.False(viewModel.IsBusy);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public void ExportCommand_StaysDisabledUntilBothPathsProvided()
    {
        var viewModel = CreateViewModel();
        Assert.False(viewModel.ExportCaptureDataCommand.CanExecute(null));

        viewModel.ExportInputPath = TestDataPath("MemInfo", "complete-meminfo.txt");
        Assert.False(viewModel.ExportCaptureDataCommand.CanExecute(null));

        viewModel.ExportOutputPath = Path.Combine(Path.GetTempPath(), "ukit-unused.tsv");
        Assert.True(viewModel.ExportCaptureDataCommand.CanExecute(null));
    }

    private static ShellViewModel CreateViewModel()
    {
        var project = CreateProject();
        return new ShellViewModel(
            new StaticProjectService(project),
            new StaticAdbServiceFactory(new RecordingAdbService()),
            new RecordingConfirmationService(true), new FakeEditorSettingStore(), new FakeUserSettingStore());
    }

    [Fact]
    public async Task OpenProject_RecordsProjectAsLastOpened()
    {
        var project = CreateProject();
        var store = new FakeEditorSettingStore();
        var viewModel = new ShellViewModel(
            new StaticProjectService(project),
            new StaticAdbServiceFactory(new RecordingAdbService()),
            new RecordingConfirmationService(true),
            store)
        {
            ProjectFilePath = project.ProjectFilePath
        };

        await ((AsyncDelegateCommand)viewModel.OpenProjectCommand).ExecuteAsync();

        Assert.Equal(project.ProjectFilePath, store.Saved);
    }

    [Fact]
    public async Task RestoreLastProject_OpensRecordedProject()
    {
        var projectFilePath = Path.Combine(Path.GetTempPath(), $"ukit-restore-{Guid.NewGuid():N}.ukit");
        await File.WriteAllTextAsync(projectFilePath, "[UnrealKit.Project]\nProjectName=Sample\n");
        try
        {
            var project = CreateProject() with { ProjectFilePath = projectFilePath };
            var confirmation = new RecordingConfirmationService(true);
            var viewModel = new ShellViewModel(
                new StaticProjectService(project),
                new StaticAdbServiceFactory(new RecordingAdbService()),
                confirmation,
                new FakeEditorSettingStore(projectFilePath));

            await viewModel.RestoreLastProjectAsync();

            Assert.Equal(projectFilePath, viewModel.ProjectFilePath);
            Assert.Contains("已打开工程", viewModel.StatusMessage, StringComparison.Ordinal);
            Assert.Null(confirmation.UnavailableProjectFilePath);
        }
        finally
        {
            if (File.Exists(projectFilePath)) File.Delete(projectFilePath);
        }
    }

    [Fact]
    public async Task RestoreLastProject_AlertsWhenRecordedProjectIsMissing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"ukit-missing-{Guid.NewGuid():N}.ukit");
        var confirmation = new RecordingConfirmationService(true);
        var viewModel = new ShellViewModel(
            new StaticProjectService(CreateProject()),
            new StaticAdbServiceFactory(new RecordingAdbService()),
            confirmation,
            new FakeEditorSettingStore(missingPath));

        await viewModel.RestoreLastProjectAsync();

        // 提示用户后不留下一个打不开的路径：状态回到「未打开工程」，由用户新建或手动打开。
        Assert.Equal(missingPath, confirmation.UnavailableProjectFilePath);
        Assert.Equal("当前工程：未打开", viewModel.ProjectTitle);
        Assert.Equal(string.Empty, viewModel.ProjectFilePath);
    }

    [Fact]
    public async Task RestoreLastProject_AlertsWhenRecordedProjectFailsToOpen()
    {
        var projectFilePath = Path.Combine(Path.GetTempPath(), $"ukit-broken-{Guid.NewGuid():N}.ukit");
        await File.WriteAllTextAsync(projectFilePath, "not a descriptor");
        try
        {
            var confirmation = new RecordingConfirmationService(true);
            var viewModel = new ShellViewModel(
                new FailingProjectService("工程描述文件缺少 ProjectName。"),
                new StaticAdbServiceFactory(new RecordingAdbService()),
                confirmation,
                new FakeEditorSettingStore(projectFilePath));

            await viewModel.RestoreLastProjectAsync();

            Assert.Equal(projectFilePath, confirmation.UnavailableProjectFilePath);
            Assert.Contains("ProjectName", confirmation.UnavailableReason ?? string.Empty, StringComparison.Ordinal);
            Assert.Equal("当前工程：未打开", viewModel.ProjectTitle);
        }
        finally
        {
            if (File.Exists(projectFilePath)) File.Delete(projectFilePath);
        }
    }

    [Fact]
    public async Task RestoreLastProject_DoesNothingWithoutRecord()
    {
        var confirmation = new RecordingConfirmationService(true);
        var viewModel = new ShellViewModel(
            new StaticProjectService(CreateProject()),
            new StaticAdbServiceFactory(new RecordingAdbService()),
            confirmation,
            new FakeEditorSettingStore());

        await viewModel.RestoreLastProjectAsync();

        Assert.Null(confirmation.UnavailableProjectFilePath);
        Assert.Equal("当前工程：未打开", viewModel.ProjectTitle);
        Assert.Empty(viewModel.OperationLogs);
    }

    private static string TestDataPath(string folder, string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", folder, fileName);

    [Fact]
    public async Task DeleteLaunchParameters_DoesNotCallAdbWhenUserDeclines()
    {
        var adb = new RecordingAdbService();
        var project = CreateProject();
        var confirmation = new RecordingConfirmationService(false);
        var viewModel = new ShellViewModel(new StaticProjectService(project), new StaticAdbServiceFactory(adb), confirmation, new FakeEditorSettingStore(), new FakeUserSettingStore())
        {
            ProjectFilePath = project.ProjectFilePath
        };

        await ((AsyncDelegateCommand)viewModel.OpenProjectCommand).ExecuteAsync();
        await ((AsyncDelegateCommand)viewModel.RefreshDevicesCommand).ExecuteAsync();
        viewModel.SelectedDevice = viewModel.Devices.First(d => d.Platform == "Android");
        await ((AsyncDelegateCommand)viewModel.DeleteLaunchParametersCommand).ExecuteAsync();

        Assert.True(confirmation.WasAsked);
        Assert.Null(adb.DeleteSerialNumber);
        Assert.Equal("已取消删除设备启动参数。", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ShowDeviceIpAddresses_LogsEveryInterfaceAndSummarizesWiFi()
    {
        var adb = new RecordingAdbService();
        var viewModel = await CreateViewModelWithSelectedAndroidDeviceAsync(adb);

        await ((AsyncDelegateCommand)viewModel.ShowDeviceIpAddressesCommand).ExecuteAsync();

        Assert.Equal("R58M123ABC", adb.IpQuerySerialNumber);

        // 每个接口各自成一条日志，蜂窝地址不因摘要只显示 WiFi 而丢失。
        var logged = viewModel.OperationLogs.Where(entry => entry.Category == "DeviceIp").Select(entry => entry.Message).ToArray();
        Assert.Equal(2, logged.Length);
        Assert.Contains(logged, message => message.Contains("wlan0 192.168.1.23/24", StringComparison.Ordinal));
        Assert.Contains(logged, message => message.Contains("rmnet_data0 10.148.22.7/30", StringComparison.Ordinal));

        // 摘要取 WiFi，那是同网段连这台手机要用的地址。
        Assert.Equal("wlan0 192.168.1.23/24", viewModel.SelectedDeviceIpSummary);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task ShowDeviceIpAddresses_ReportsUnavailableInsteadOfClaimingAnAddress()
    {
        var adb = new RecordingAdbService();
        adb.IpAddresses.Clear();
        var viewModel = await CreateViewModelWithSelectedAndroidDeviceAsync(adb);

        await ((AsyncDelegateCommand)viewModel.ShowDeviceIpAddressesCommand).ExecuteAsync();

        Assert.Equal("未查到 IPv4 地址。", viewModel.SelectedDeviceIpSummary);
        // 消息里带上尝试过的命令，用户才能区分「设备没联网」和「查询没跑起来」。
        Assert.Contains("ip -f inet addr", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShowDeviceIpAddressesCommand_RequiresAvailableAndroidDevice()
    {
        var adb = new RecordingAdbService();
        var project = CreateProject();
        var viewModel = new ShellViewModel(new StaticProjectService(project), new StaticAdbServiceFactory(adb), new RecordingConfirmationService(true), new FakeEditorSettingStore(), new FakeUserSettingStore())
        {
            ProjectFilePath = project.ProjectFilePath
        };

        Assert.False(viewModel.ShowDeviceIpAddressesCommand.CanExecute(null));

        await ((AsyncDelegateCommand)viewModel.OpenProjectCommand).ExecuteAsync();
        await ((AsyncDelegateCommand)viewModel.RefreshDevicesCommand).ExecuteAsync();

        // Win64 本机地址不经 ADB shell，这条命令对它无意义。
        viewModel.SelectedDevice = viewModel.Devices.First(device => device.Platform == "Win64");
        Assert.False(viewModel.ShowDeviceIpAddressesCommand.CanExecute(null));

        viewModel.SelectedDevice = viewModel.Devices.First(device => device.Platform == "Android");
        Assert.True(viewModel.ShowDeviceIpAddressesCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectedDeviceIpSummary_ResetsWhenDeviceChanges()
    {
        var adb = new RecordingAdbService();
        var viewModel = await CreateViewModelWithSelectedAndroidDeviceAsync(adb);
        await ((AsyncDelegateCommand)viewModel.ShowDeviceIpAddressesCommand).ExecuteAsync();
        Assert.Contains("192.168.1.23", viewModel.SelectedDeviceIpSummary, StringComparison.Ordinal);

        // 换设备后仍显示上一台的地址会被读成当前设备的。
        viewModel.SelectedDevice = viewModel.Devices.First(device => device.Platform == "Win64");

        Assert.DoesNotContain("192.168.1.23", viewModel.SelectedDeviceIpSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Devices_ShowConfiguredAliasAlongsideDeviceId()
    {
        // 设备 id 与别名各占一列：id 是所有操作的依据，别名只用于辨认，
        // 因此配了别名也不能把 id 换掉。
        var adb = new RecordingAdbService();
        var project = CreateProject() with
        {
            Settings = CreateProject().Settings with
            {
                DeviceAliases = DeviceAliasMap.Create(new Dictionary<string, string> { ["R58M123ABC"] = "测试机A" })
            }
        };
        var viewModel = new ShellViewModel(new StaticProjectService(project), new StaticAdbServiceFactory(adb), new RecordingConfirmationService(true), new FakeEditorSettingStore(), new FakeUserSettingStore())
        {
            ProjectFilePath = project.ProjectFilePath
        };

        await ((AsyncDelegateCommand)viewModel.OpenProjectCommand).ExecuteAsync();
        await ((AsyncDelegateCommand)viewModel.RefreshDevicesCommand).ExecuteAsync();

        var android = viewModel.Devices.First(device => device.Platform == "Android");
        Assert.Equal("R58M123ABC", android.Id);
        Assert.Equal("测试机A", android.Alias);
        Assert.True(android.HasAlias);

        // 没配别名的设备别名列为空，不回填型号或 id 冒充别名。
        var win64 = viewModel.Devices.First(device => device.Platform == "Win64");
        Assert.Null(win64.Alias);
        Assert.False(win64.HasAlias);
        Assert.Equal(win64.Name, win64.DisplayLabel);

        viewModel.SelectedDevice = android;
        Assert.Contains("R58M123ABC", viewModel.SelectedDeviceDescription, StringComparison.Ordinal);
        Assert.Contains("测试机A", viewModel.SelectedDeviceDescription, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Devices_WithoutConfiguredAliases_ListDevicesWithoutAlias()
    {
        // 未配置别名的工程设备列表照常可用：别名是附加信息，不是列出设备的前提。
        var adb = new RecordingAdbService();
        var project = CreateProject();
        var viewModel = new ShellViewModel(new StaticProjectService(project), new StaticAdbServiceFactory(adb), new RecordingConfirmationService(true), new FakeEditorSettingStore(), new FakeUserSettingStore())
        {
            ProjectFilePath = project.ProjectFilePath
        };

        await ((AsyncDelegateCommand)viewModel.OpenProjectCommand).ExecuteAsync();
        await ((AsyncDelegateCommand)viewModel.RefreshDevicesCommand).ExecuteAsync();

        Assert.NotEmpty(viewModel.Devices);
        Assert.All(viewModel.Devices, device => Assert.False(device.HasAlias));
    }

    [Fact]
    public async Task PlatformScope_DefaultsToAll_AndListsEveryPlatform()
    {
        // 默认不过滤：隐式默认某个平台会让另一个平台的设备静默消失。
        var viewModel = await CreateRefreshedViewModelAsync();

        Assert.True(viewModel.PlatformScope.IsAll);
        Assert.Contains(viewModel.ScopedDevices, device => device.Platform == "Android");
        Assert.Contains(viewModel.ScopedDevices, device => device.Platform == "Win64");
        Assert.Equal(viewModel.Devices.Count, viewModel.ScopedDevices.Count);
    }

    [Fact]
    public async Task PlatformScope_FiltersScopedDevicesButKeepsFullList()
    {
        // Devices 保留全量、ScopedDevices 过滤：两者都需要，
        // 才能区分「该平台没有设备」与「有设备但被作用域挡住」。
        var viewModel = await CreateRefreshedViewModelAsync();

        viewModel.PlatformScope = PlatformScope.For(TargetPlatform.Win64);

        Assert.All(viewModel.ScopedDevices, device => Assert.Equal("Win64", device.Platform));
        Assert.Contains(viewModel.Devices, device => device.Platform == "Android");
        Assert.Contains("Win64", viewModel.PlatformScopeDescription, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformScope_ClearsSelectedDeviceThatFallsOutsideScope()
    {
        // 已选设备落到作用域外必须清空：留着它会让后续操作打到用户以为已排除的平台。
        var viewModel = await CreateRefreshedViewModelAsync();
        viewModel.SelectedDevice = viewModel.Devices.First(device => device.Platform == "Android");

        viewModel.PlatformScope = PlatformScope.For(TargetPlatform.Win64);

        Assert.NotEqual("Android", viewModel.SelectedDevice?.Platform);
    }

    [Fact]
    public async Task PlatformScope_KeepsSelectedDeviceThatStaysInScope()
    {
        // 已选设备仍在作用域内时保持选中：换作用域不该打断正在进行的工作。
        var viewModel = await CreateRefreshedViewModelAsync();
        var android = viewModel.Devices.First(device => device.Platform == "Android");
        viewModel.SelectedDevice = android;

        viewModel.PlatformScope = PlatformScope.For(TargetPlatform.Android);

        Assert.Same(android, viewModel.SelectedDevice);
    }

    [Fact]
    public async Task PlatformScope_IsPersistedToOpenProject()
    {
        var project = CreateProject();
        var store = new FakeUserSettingStore();
        var viewModel = new ShellViewModel(
            new StaticProjectService(project),
            new StaticAdbServiceFactory(new RecordingAdbService()),
            new RecordingConfirmationService(true),
            userSettingStore: store)
        {
            ProjectFilePath = project.ProjectFilePath
        };
        await ((AsyncDelegateCommand)viewModel.OpenProjectCommand).ExecuteAsync();

        viewModel.PlatformScope = PlatformScope.For(TargetPlatform.Win64);

        Assert.Equal(PlatformScope.For(TargetPlatform.Win64), store.SavedScope);
        Assert.Equal(project.ProjectFilePath, store.SavedForProject?.ProjectFilePath);
    }

    [Fact]
    public void PlatformScope_IsNotPersistedWithoutOpenProject()
    {
        // 未打开工程时无处可记：作用域记在工程的 Config/UserSetting.ini 里。
        var store = new FakeUserSettingStore();
        var viewModel = new ShellViewModel(
            new StaticProjectService(CreateProject()),
            new StaticAdbServiceFactory(new RecordingAdbService()),
            new RecordingConfirmationService(true),
            userSettingStore: store);

        viewModel.PlatformScope = PlatformScope.For(TargetPlatform.Win64);

        Assert.Equal(PlatformScope.For(TargetPlatform.Win64), viewModel.PlatformScope);
        Assert.Null(store.SavedScope);
    }

    [Fact]
    public async Task OpenProject_RestoresScopeRecordedInThatProject()
    {
        var project = CreateProject();
        var viewModel = new ShellViewModel(
            new StaticProjectService(project),
            new StaticAdbServiceFactory(new RecordingAdbService()),
            new RecordingConfirmationService(true),
            userSettingStore: new FakeUserSettingStore(PlatformScope.For(TargetPlatform.Win64)))
        {
            ProjectFilePath = project.ProjectFilePath
        };

        await ((AsyncDelegateCommand)viewModel.OpenProjectCommand).ExecuteAsync();

        Assert.Equal(PlatformScope.For(TargetPlatform.Win64), viewModel.PlatformScope);
    }

    [Fact]
    public async Task OpenProject_KeepsCurrentScopeWhenProjectHasNoRecord()
    {
        // 没有记录时保留当前选择：把「记录缺失」当成「记录为全部」会重置用户刚选的平台。
        var project = CreateProject();
        var viewModel = new ShellViewModel(
            new StaticProjectService(project),
            new StaticAdbServiceFactory(new RecordingAdbService()),
            new RecordingConfirmationService(true),
            userSettingStore: new FakeUserSettingStore())
        {
            ProjectFilePath = project.ProjectFilePath,
            PlatformScope = PlatformScope.For(TargetPlatform.Android)
        };

        await ((AsyncDelegateCommand)viewModel.OpenProjectCommand).ExecuteAsync();

        Assert.Equal(PlatformScope.For(TargetPlatform.Android), viewModel.PlatformScope);
    }

    [Fact]
    public void UserSettingFilePath_SitsBesideDefaultGameIniWithoutReplacingIt()
    {
        // 作用域不该写进可版本化的 DefaultGame.ini，因此两者同目录但分文件。
        var project = CreateProject();

        Assert.Equal(Path.Combine(project.ConfigDir, "UserSetting.ini"), project.UserSettingFilePath);
        Assert.NotEqual(project.ConfigFilePath, project.UserSettingFilePath);
    }

    /// <summary>
    /// 打开工程并刷新一次设备，得到同时含 Android 与 Win64 的设备列表。
    /// </summary>
    private static async Task<ShellViewModel> CreateRefreshedViewModelAsync()
    {
        var project = CreateProject();
        var viewModel = new ShellViewModel(
            new StaticProjectService(project),
            new StaticAdbServiceFactory(new RecordingAdbService()),
            new RecordingConfirmationService(true), new FakeEditorSettingStore(), new FakeUserSettingStore())
        {
            ProjectFilePath = project.ProjectFilePath
        };

        await ((AsyncDelegateCommand)viewModel.OpenProjectCommand).ExecuteAsync();
        await ((AsyncDelegateCommand)viewModel.RefreshDevicesCommand).ExecuteAsync();
        return viewModel;
    }

    private static async Task<ShellViewModel> CreateViewModelWithSelectedAndroidDeviceAsync(RecordingAdbService adb)
    {
        var project = CreateProject();
        var viewModel = new ShellViewModel(new StaticProjectService(project), new StaticAdbServiceFactory(adb), new RecordingConfirmationService(true), new FakeEditorSettingStore(), new FakeUserSettingStore())
        {
            ProjectFilePath = project.ProjectFilePath
        };

        await ((AsyncDelegateCommand)viewModel.OpenProjectCommand).ExecuteAsync();
        await ((AsyncDelegateCommand)viewModel.RefreshDevicesCommand).ExecuteAsync();
        viewModel.SelectedDevice = viewModel.Devices.First(device => device.Platform == "Android");
        return viewModel;
    }

    private static UkitProject CreateProject()
    {
        var settings = ProjectSettings.CreateDefaults("Sample") with
        {
            Android = AndroidPlatformProfile.CreateDefaults() with { PackageName = "com.example.game", Activity = "com.example.game.MainActivity" }
        };
        return new UkitProject("C:\\Projects\\Sample\\Sample.ukit", "C:\\Projects\\Sample", UkitProjectDescriptor.CreateDefault("Sample"), settings);
    }

    private sealed class StaticProjectService(UkitProject project) : IProjectService
    {
        public Task<ProjectCreateResult> CreateProjectAsync(CreateProjectRequest request, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UkitProject> OpenProjectAsync(string projectFilePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(project);
        public Task<UkitProject> UpdateSettingsAsync(UkitProject updatedProject, ProjectSettings settings, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(updatedProject with { Settings = settings });
        public Task<ProjectValidationResult> ValidateProjectAsync(string projectFilePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(new ProjectValidationResult([]));
    }

    private sealed class FailingProjectService(string message) : IProjectService
    {
        public Task<ProjectCreateResult> CreateProjectAsync(CreateProjectRequest request, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UkitProject> OpenProjectAsync(string projectFilePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => throw new InvalidDataException(message);
        public Task<UkitProject> UpdateSettingsAsync(UkitProject updatedProject, ProjectSettings settings, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProjectValidationResult> ValidateProjectAsync(string projectFilePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(new ProjectValidationResult([]));
    }

    private sealed class StaticAdbServiceFactory(IAdbService adb) : IDesktopAdbServiceFactory
    {
        public IAdbService Create(ProjectSettings? settings, IProgress<ProcessOutput>? output) => new OutputForwardingAdbService(adb, output);
        public AdbPathResolution Resolve(ProjectSettings? settings) => new(null, []);
    }

    private sealed class RecordingConfirmationService(bool response) : IUserConfirmationService
    {
        public bool WasAsked { get; private set; }
        public Task<bool> ConfirmDeleteLaunchParametersAsync(LaunchOperationTarget target)
        {
            WasAsked = true;
            return Task.FromResult(response);
        }

        public string? UnavailableProjectFilePath { get; private set; }
        public string? UnavailableReason { get; private set; }

        public Task NotifyLastProjectUnavailableAsync(string projectFilePath, string reason)
        {
            UnavailableProjectFilePath = projectFilePath;
            UnavailableReason = reason;
            return Task.CompletedTask;
        }

        public Task<bool> ConfirmInstallApplicationAsync(string deviceId, string localApplicationPath) =>
            Task.FromResult(response);
    }

    private sealed class FakeEditorSettingStore(string? lastProjectFilePath = null) : IEditorSettingStore
    {
        public string? Saved { get; private set; }

        public Task<string?> TryGetLastProjectFilePathAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(lastProjectFilePath);

        public Task SaveLastProjectFilePathAsync(string projectFilePath, CancellationToken cancellationToken = default)
        {
            Saved = projectFilePath;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUserSettingStore(PlatformScope? platformScope = null) : IUserSettingStore
    {
        public PlatformScope? SavedScope { get; private set; }

        public UkitProject? SavedForProject { get; private set; }

        public Task<PlatformScope?> TryGetPlatformScopeAsync(UkitProject project, CancellationToken cancellationToken = default) =>
            Task.FromResult(platformScope);

        public Task SavePlatformScopeAsync(UkitProject project, PlatformScope scope, CancellationToken cancellationToken = default)
        {
            SavedForProject = project;
            SavedScope = scope;
            return Task.CompletedTask;
        }
    }

    private sealed class OutputForwardingAdbService(IAdbService inner, IProgress<ProcessOutput>? output) : IAdbService
    {
        private static ProcessExecutionResult Success => new(0, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        private void Write(string text) => output?.Report(new ProcessOutput(DateTimeOffset.UtcNow, ProcessOutputStream.StandardOutput, text));
        public Task<ProcessExecutionResult> GetVersionAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.GetVersionAsync(progress, cancellationToken);
        public async Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) { Write("adb devices -l"); return await inner.ListDevicesAsync(progress, cancellationToken); }
        public Task<ProcessExecutionResult> StartServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.StartServerAsync(progress, cancellationToken);
        public Task<ProcessExecutionResult> KillServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.KillServerAsync(progress, cancellationToken);
        public Task<ProcessExecutionResult> ConnectAsync(string endpoint, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.ConnectAsync(endpoint, progress, cancellationToken);
        public Task<ProcessExecutionResult> DisconnectAsync(string endpoint, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.DisconnectAsync(endpoint, progress, cancellationToken);
        public Task<ProcessExecutionResult> TcpIpAsync(string serialNumber, int port, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.TcpIpAsync(serialNumber, port, progress, cancellationToken);
        public async Task<ProcessExecutionResult> StartApplicationAsync(string serialNumber, string packageName, string activityName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) { Write("am start"); return await inner.StartApplicationAsync(serialNumber, packageName, activityName, progress, cancellationToken); }
        public async Task<ProcessExecutionResult> PushFileAsync(string serialNumber, string localPath, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) { Write("adb push"); return await inner.PushFileAsync(serialNumber, localPath, remotePath, progress, cancellationToken); }
        public Task<ProcessExecutionResult> PullDirectoryAsync(string serialNumber, string remotePath, string localDirectory, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.PullDirectoryAsync(serialNumber, remotePath, localDirectory, progress, cancellationToken);
        public async Task<ProcessExecutionResult> DeleteRemoteFileAsync(string serialNumber, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) { Write("adb shell rm"); return await inner.DeleteRemoteFileAsync(serialNumber, remotePath, progress, cancellationToken); }
        public Task<ProcessExecutionResult> RunDumpsysAsync(string serialNumber, string packageName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.RunDumpsysAsync(serialNumber, packageName, progress, cancellationToken);
        public Task<ProcessExecutionResult> InstallApkAsync(string serialNumber, string localApkPath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.InstallApkAsync(serialNumber, localApkPath, progress, cancellationToken);
        public Task<ProcessExecutionResult> ForceStopApplicationAsync(string serialNumber, string packageName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.ForceStopApplicationAsync(serialNumber, packageName, progress, cancellationToken);
        public Task<ProcessExecutionResult> ForwardTcpAsync(string serialNumber, int hostPort, int devicePort, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.ForwardTcpAsync(serialNumber, hostPort, devicePort, progress, cancellationToken);
        public IAsyncEnumerable<string> StreamLogcatAsync(string serialNumber, string? filter = null, CancellationToken cancellationToken = default) => inner.StreamLogcatAsync(serialNumber, filter, cancellationToken);
        public Task<IReadOnlyList<DeviceIpAddress>> GetIpAddressesAsync(string serialNumber, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.GetIpAddressesAsync(serialNumber, progress, cancellationToken);
    }

    private sealed class RecordingAdbService : IAdbService
    {
        private static ProcessExecutionResult Success => new(0, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        public string? PushSerialNumber { get; private set; }
        public string? PushedContent { get; private set; }
        public string? DeleteSerialNumber { get; private set; }
        public (string SerialNumber, string PackageName, string ActivityName)? StartRequest { get; private set; }
        public (string SerialNumber, string PackageName)? ForceStopRequest { get; private set; }
        /// <summary>按调用先后记录 stop/start，验证「先关后启」的顺序。</summary>
        public List<string> OperationOrder { get; } = [];
        public Task<ProcessExecutionResult> GetVersionAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AdbDevice>>([new("R58M123ABC", AdbDeviceStatus.Device, null, "Pixel", null, AdbConnectionType.Usb, "R58M123ABC device model:Pixel")]);
        public Task<ProcessExecutionResult> StartServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> KillServerAsync(IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> ConnectAsync(string endpoint, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> DisconnectAsync(string endpoint, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> TcpIpAsync(string serialNumber, int port, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> StartApplicationAsync(string serialNumber, string packageName, string activityName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) { StartRequest = (serialNumber, packageName, activityName); OperationOrder.Add("start"); return Task.FromResult(Success); }
        public async Task<ProcessExecutionResult> PushFileAsync(string serialNumber, string localPath, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) { PushSerialNumber = serialNumber; PushedContent = await File.ReadAllTextAsync(localPath, cancellationToken); return Success; }
        public Task<ProcessExecutionResult> PullDirectoryAsync(string serialNumber, string remotePath, string localDirectory, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> DeleteRemoteFileAsync(string serialNumber, string remotePath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) { DeleteSerialNumber = serialNumber; return Task.FromResult(Success); }
        public Task<ProcessExecutionResult> RunDumpsysAsync(string serialNumber, string packageName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> InstallApkAsync(string serialNumber, string localApkPath, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public Task<ProcessExecutionResult> ForceStopApplicationAsync(string serialNumber, string packageName, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) { ForceStopRequest = (serialNumber, packageName); OperationOrder.Add("stop"); return Task.FromResult(new ProcessExecutionResult(0, string.Empty, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)); }
        public Task<ProcessExecutionResult> ForwardTcpAsync(string serialNumber, int hostPort, int devicePort, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(Success);
        public async IAsyncEnumerable<string> StreamLogcatAsync(string serialNumber, string? filter = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { await System.Threading.Tasks.Task.CompletedTask; yield break; }
        /// <summary>置空表示设备未联网，与真实服务一致地抛异常而不是返回空列表。</summary>
        public List<DeviceIpAddress> IpAddresses { get; } =
        [
            new("wlan0", "192.168.1.23", 24, DeviceNetworkInterfaceKind.WiFi),
            new("rmnet_data0", "10.148.22.7", 30, DeviceNetworkInterfaceKind.Cellular)
        ];

        public string? IpQuerySerialNumber { get; private set; }

        public Task<IReadOnlyList<DeviceIpAddress>> GetIpAddressesAsync(string serialNumber, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            IpQuerySerialNumber = serialNumber;
            return IpAddresses.Count == 0
                ? throw new AdbDeviceAddressUnavailableException(serialNumber, [$"adb -s {serialNumber} shell ip -f inet addr"])
                : Task.FromResult<IReadOnlyList<DeviceIpAddress>>(IpAddresses);
        }
    }
}
