using DownKyi.Application.Desktop;
using DownKyi.Core.Storage.Uploader;
using DownKyi.Desktop.Services.Uploader;
using DownKyi.ViewModels.Dialogs;
using Microsoft.Extensions.Logging.Abstractions;

namespace DownKyi.Tests.Dialogs;

public sealed class ViewDownloadSetterWithSubFolderViewModelTests : IDisposable
{
    private readonly string _directory;

    public ViewDownloadSetterWithSubFolderViewModelTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"vds-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private DownKyi.Core.Settings.SettingsStore CreateSettingsStore()
    {
        return new DownKyi.Core.Settings.SettingsStore(Path.Combine(_directory, "settings.json"));
    }

    private ViewDownloadSetterWithSubFolderViewModel CreateViewModel(
        IUploaderRoutingPreferenceRepository? prefs = null,
        IUploaderAliasRepository? aliases = null,
        DownKyi.Core.Settings.SettingsStore? settings = null)
    {
#pragma warning disable CA2000
        var ownedStore = settings ?? CreateSettingsStore();
#pragma warning restore CA2000
        return new ViewDownloadSetterWithSubFolderViewModel(
            new TestDesktopInteractionContext().Notifications,
            new StubFilePickerService(),
            ownedStore,
            prefs ?? new InMemoryUploaderRoutingPreferenceRepository(),
            aliases ?? new InMemoryUploaderAliasRepository(),
            NullLogger<ViewDownloadSetterWithSubFolderViewModel>.Instance);
    }

    private static AppDialogRequest NewRequest(long ownerMid, string ownerName) =>
        new(
            Dialog: AppDialog.DownloadSettingsWithSubFolder,
            Parameters: new Dictionary<string, object?>
            {
                ["ownerMid"] = ownerMid,
                ["ownerName"] = ownerName
            });

    [Fact]
    public void CtorRejectsNullPreferenceRepository()
    {
        using var settings = CreateSettingsStore();
        Assert.Throws<ArgumentNullException>(
            () => new ViewDownloadSetterWithSubFolderViewModel(
                new TestDesktopInteractionContext().Notifications,
                new StubFilePickerService(),
                settings,
                preferenceRepository: null!,
                aliasRepository: new InMemoryUploaderAliasRepository(),
                NullLogger<ViewDownloadSetterWithSubFolderViewModel>.Instance));
    }

    [Fact]
    public void CtorRejectsNullAliasRepository()
    {
        using var settings = CreateSettingsStore();
        Assert.Throws<ArgumentNullException>(
            () => new ViewDownloadSetterWithSubFolderViewModel(
                new TestDesktopInteractionContext().Notifications,
                new StubFilePickerService(),
                settings,
                new InMemoryUploaderRoutingPreferenceRepository(),
                aliasRepository: null!,
                NullLogger<ViewDownloadSetterWithSubFolderViewModel>.Instance));
    }

    [Fact]
    public void DefaultStrategyIsByUploaderWhenNoPreferenceRecorded()
    {
        using var settings = CreateSettingsStore();
        var viewModel = CreateViewModel(settings: settings);

        Assert.Equal(UploaderRoutingStrategy.ByUploader, viewModel.Strategy);
        Assert.Equal(string.Empty, viewModel.SubFolder);
        Assert.False(viewModel.IsCustom);
        Assert.True(viewModel.IsByUploader);
    }

    [Fact]
    public void DefaultStrategyRestoresLastChoiceFromPreferenceFile()
    {
        using var settings = CreateSettingsStore();
        var prefs = new InMemoryUploaderRoutingPreferenceRepository();
        prefs.Save(new UploaderRoutingPreference(
            LastStrategy: UploaderRoutingStrategy.Custom,
            LastCustomFolder: "上次",
            LastPersistAlias: true));
        var viewModel = CreateViewModel(prefs: prefs, settings: settings);

        Assert.Equal(UploaderRoutingStrategy.Custom, viewModel.Strategy);
        Assert.Equal("上次", viewModel.SubFolder);
        Assert.True(viewModel.IsCustom);
    }

    [Fact]
    public void OnDialogOpenedByUploaderPrefillsAliasFolderName()
    {
        using var settings = CreateSettingsStore();
        var aliases = new InMemoryUploaderAliasRepository();
        aliases.Save(new Dictionary<long, string>
        {
            [42] = "博主A",
            [99] = "博主B"
        });

        var viewModel = CreateViewModel(aliases: aliases, settings: settings);

        viewModel.OnDialogOpened(NewRequest(ownerMid: 42, ownerName: "博主A-原昵称"));

        Assert.Equal(UploaderRoutingStrategy.ByUploader, viewModel.Strategy);
        Assert.Equal("博主A", viewModel.SubFolder);
        Assert.Equal(ResolutionStatus.MatchedAlias, viewModel.ResolutionStatus);
        Assert.Contains("已匹配", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void OnDialogOpenedByUploaderPrefillsOwnerNameWhenNoAlias()
    {
        using var settings = CreateSettingsStore();
        var viewModel = CreateViewModel(settings: settings);

        viewModel.OnDialogOpened(NewRequest(ownerMid: 7, ownerName: "新UP主"));

        Assert.Equal("新UP主", viewModel.SubFolder);
        Assert.Equal(ResolutionStatus.WillCreateAlias, viewModel.ResolutionStatus);
        Assert.Contains("未匹配", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void OnDialogOpenedByUploaderWithNoUploaderMarksStatusAsNoUploader()
    {
        using var settings = CreateSettingsStore();
        var viewModel = CreateViewModel(settings: settings);

        viewModel.OnDialogOpened(NewRequest(ownerMid: 0, ownerName: string.Empty));

        Assert.Equal(string.Empty, viewModel.SubFolder);
        Assert.Equal(ResolutionStatus.NoUploader, viewModel.ResolutionStatus);
        Assert.False(viewModel.UpdateAliasCommand.CanExecute(null));
    }

    [Fact]
    public void OnDialogOpenedByUploaderWithMissingMidButNamePrefillsName()
    {
        using var settings = CreateSettingsStore();
        var viewModel = CreateViewModel(settings: settings);

        // 上游 B 站 API 在某些边界情况下把 Owner.Mid 写成 -1 但 Owner.Name 正常。
        viewModel.OnDialogOpened(NewRequest(ownerMid: -1, ownerName: "昵称"));

        Assert.Equal("昵称", viewModel.SubFolder);
        Assert.Equal(ResolutionStatus.WillCreateAlias, viewModel.ResolutionStatus);
        // 没有 mid 时不能更新别名表。
        Assert.False(viewModel.UpdateAliasCommand.CanExecute(null));
    }

    [Fact]
    public void SwitchingToCustomStrategyHidesUpdateAliasButton()
    {
        using var settings = CreateSettingsStore();
        var aliases = new InMemoryUploaderAliasRepository();
        aliases.Upsert(1, "x");
        var viewModel = CreateViewModel(aliases: aliases, settings: settings);
        viewModel.OnDialogOpened(NewRequest(ownerMid: 1, ownerName: "某UP主"));

        Assert.True(viewModel.UpdateAliasCommand.CanExecute(null));
        viewModel.Strategy = UploaderRoutingStrategy.Custom;

        Assert.False(viewModel.UpdateAliasCommand.CanExecute(null));
        Assert.False(viewModel.IsByUploader);
        Assert.True(viewModel.IsCustom);
    }

    [Fact]
    public void UpdateAliasCommandWritesToRepositoryAndUpdatesStatus()
    {
        using var settings = CreateSettingsStore();
        var aliases = new InMemoryUploaderAliasRepository();
        var viewModel = CreateViewModel(aliases: aliases, settings: settings);

        viewModel.OnDialogOpened(NewRequest(ownerMid: 5, ownerName: "原昵称"));
        Assert.Equal(ResolutionStatus.WillCreateAlias, viewModel.ResolutionStatus);

        viewModel.SubFolder = "新文件夹";
        viewModel.UpdateAliasCommand.Execute(null);

        Assert.Equal("新文件夹", aliases.Load()[5]);
        Assert.Equal(ResolutionStatus.MatchedAlias, viewModel.ResolutionStatus);
    }

    [Fact]
    public void UpdateAliasCommandTrimsFolderName()
    {
        using var settings = CreateSettingsStore();
        var aliases = new InMemoryUploaderAliasRepository();
        var viewModel = CreateViewModel(aliases: aliases, settings: settings);

        viewModel.OnDialogOpened(NewRequest(ownerMid: 5, ownerName: "原昵称"));
        viewModel.SubFolder = "  带空格  ";
        viewModel.UpdateAliasCommand.Execute(null);

        Assert.Equal("带空格", aliases.Load()[5]);
        Assert.Equal("带空格", viewModel.SubFolder);
    }

    [Fact]
    public void UpdateAliasCommandIsDisabledWhenSubFolderIsBlank()
    {
        using var settings = CreateSettingsStore();
        var viewModel = CreateViewModel(settings: settings);
        viewModel.OnDialogOpened(NewRequest(ownerMid: 5, ownerName: "原昵称"));

        viewModel.SubFolder = "  ";
        Assert.False(viewModel.UpdateAliasCommand.CanExecute(null));
    }

    [Fact]
    public void EditingSubFolderNotifiesUpdateAliasCommand()
    {
        using var settings = CreateSettingsStore();
        var viewModel = CreateViewModel(settings: settings);
        viewModel.OnDialogOpened(NewRequest(ownerMid: 5, ownerName: "原昵称"));

        // 刚打开时按 mid/name 预填，所以一开始应该是可执行的。
        Assert.True(viewModel.UpdateAliasCommand.CanExecute(null));

        // 清空 SubFolder 后必须立刻变成不可执行（命令要随属性变化重新查询）。
        viewModel.SubFolder = string.Empty;
        Assert.False(viewModel.UpdateAliasCommand.CanExecute(null));

        viewModel.SubFolder = "  ";
        Assert.False(viewModel.UpdateAliasCommand.CanExecute(null));

        viewModel.SubFolder = "新名字";
        Assert.True(viewModel.UpdateAliasCommand.CanExecute(null));
    }

    [Fact]
    public void SwitchingStrategyNotifiesUpdateAliasCommand()
    {
        using var settings = CreateSettingsStore();
        var aliases = new InMemoryUploaderAliasRepository();
        aliases.Upsert(1, "原映射");
        var viewModel = CreateViewModel(aliases: aliases, settings: settings);
        viewModel.OnDialogOpened(NewRequest(ownerMid: 1, ownerName: "原昵称"));

        Assert.True(viewModel.UpdateAliasCommand.CanExecute(null));
        viewModel.Strategy = UploaderRoutingStrategy.Custom;
        // 切到 Custom 后不应再可执行。
        Assert.False(viewModel.UpdateAliasCommand.CanExecute(null));
    }

    [Fact]
    public void SwitchingStrategyPersistsPreference()
    {
        using var settings = CreateSettingsStore();
        var prefs = new InMemoryUploaderRoutingPreferenceRepository();
        var viewModel = CreateViewModel(prefs: prefs, settings: settings);
        viewModel.OnDialogOpened(NewRequest(ownerMid: 0, ownerName: string.Empty));

        viewModel.SubFolder = "hello";
        viewModel.Strategy = UploaderRoutingStrategy.Custom;

        var saved = prefs.Load();
        Assert.Equal(UploaderRoutingStrategy.Custom, saved.LastStrategy);
        Assert.Equal("hello", saved.LastCustomFolder);
    }

    [Fact]
    public void StrategyChoicesContainsBothStrategies()
    {
        using var settings = CreateSettingsStore();
        var viewModel = CreateViewModel(settings: settings);

        Assert.Contains(UploaderRoutingStrategy.ByUploader, viewModel.StrategyChoices);
        Assert.Contains(UploaderRoutingStrategy.Custom, viewModel.StrategyChoices);
    }

    private sealed class StubFilePickerService : DownKyi.Application.Desktop.IFilePickerService
    {
        public Task<string?> SelectFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string?> SelectVideoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> SelectVideosAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}