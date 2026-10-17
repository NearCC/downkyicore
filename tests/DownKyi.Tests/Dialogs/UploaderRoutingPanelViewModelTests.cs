using DownKyi.Application.Desktop;
using DownKyi.Core.Settings;
using DownKyi.Core.Storage.Uploader;
using DownKyi.Desktop.Services.Uploader;
using DownKyi.Presentation;
using DownKyi.Services.Download;
using DownKyi.Services.Uploader;
using DownKyi.Services.Video;
using DownKyi.ViewModels.Dialogs;
using Microsoft.Extensions.Logging.Abstractions;

namespace DownKyi.Tests.Dialogs;

public sealed class UploaderRoutingPanelViewModelTests
{
    private static UploaderRoutingPanelViewModel CreateViewModel(
        long ownerMid = 0L,
        string ownerName = "",
        IUploaderRoutingPreferenceRepository? prefs = null,
        IUploaderAliasRepository? aliases = null)
    {
        return new UploaderRoutingPanelViewModel(
            ownerMid,
            ownerName,
            prefs ?? new InMemoryUploaderRoutingPreferenceRepository(),
            aliases ?? new InMemoryUploaderAliasRepository(),
            new TestDesktopInteractionContext().Notifications,
            NullLogger.Instance);
    }

    [Fact]
    public void CtorRejectsNullPreferenceRepository()
    {
        Assert.Throws<ArgumentNullException>(
            () => new UploaderRoutingPanelViewModel(
                0L,
                string.Empty,
                preferenceRepository: null!,
                new InMemoryUploaderAliasRepository(),
                new TestDesktopInteractionContext().Notifications,
                NullLogger.Instance));
    }

    [Fact]
    public void CtorRejectsNullAliasRepository()
    {
        Assert.Throws<ArgumentNullException>(
            () => new UploaderRoutingPanelViewModel(
                0L,
                string.Empty,
                new InMemoryUploaderRoutingPreferenceRepository(),
                aliasRepository: null!,
                new TestDesktopInteractionContext().Notifications,
                NullLogger.Instance));
    }

    [Fact]
    public void CtorRejectsNullNotificationService()
    {
        Assert.Throws<ArgumentNullException>(
            () => new UploaderRoutingPanelViewModel(
                0L,
                string.Empty,
                new InMemoryUploaderRoutingPreferenceRepository(),
                new InMemoryUploaderAliasRepository(),
                notifications: null!,
                NullLogger.Instance));
    }

    [Fact]
    public void DefaultStrategyIsByUploaderWhenNoPreferenceRecorded()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(UploaderRoutingStrategy.ByUploader, viewModel.Strategy);
        Assert.Equal(string.Empty, viewModel.SubFolder);
        Assert.False(viewModel.IsCustom);
        Assert.True(viewModel.IsByUploader);
    }

    [Fact]
    public void DefaultStrategyRestoresLastChoiceFromPreferenceFile()
    {
        var prefs = new InMemoryUploaderRoutingPreferenceRepository();
        prefs.Save(new UploaderRoutingPreference(
            LastStrategy: UploaderRoutingStrategy.Custom,
            LastCustomFolder: "上次",
            LastPersistAlias: true));
        var viewModel = CreateViewModel(prefs: prefs);

        Assert.Equal(UploaderRoutingStrategy.Custom, viewModel.Strategy);
        Assert.Equal("上次", viewModel.SubFolder);
        Assert.True(viewModel.IsCustom);
    }

    [Fact]
    public void ByUploaderPrefillsAliasFolderName()
    {
        var aliases = new InMemoryUploaderAliasRepository();
        aliases.Save(new Dictionary<long, string>
        {
            [42] = "博主A",
            [99] = "博主B"
        });

        var viewModel = CreateViewModel(ownerMid: 42, ownerName: "博主A-原昵称", aliases: aliases);

        Assert.Equal(UploaderRoutingStrategy.ByUploader, viewModel.Strategy);
        Assert.Equal("博主A", viewModel.SubFolder);
        Assert.Equal(ResolutionStatus.MatchedAlias, viewModel.ResolutionStatus);
        Assert.Contains("已匹配", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void ByUploaderPrefillsOwnerNameWhenNoAlias()
    {
        var viewModel = CreateViewModel(ownerMid: 7, ownerName: "新UP主");

        Assert.Equal("新UP主", viewModel.SubFolder);
        Assert.Equal(ResolutionStatus.WillCreateAlias, viewModel.ResolutionStatus);
        Assert.Contains("未匹配", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void ByUploaderWithNoUploaderMarksStatusAsNoUploader()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(string.Empty, viewModel.SubFolder);
        Assert.Equal(ResolutionStatus.NoUploader, viewModel.ResolutionStatus);
        Assert.False(viewModel.UpdateAliasCommand.CanExecute(null));
    }

    [Fact]
    public void ByUploaderWithMissingMidButNamePrefillsName()
    {
        var viewModel = CreateViewModel(ownerMid: -1, ownerName: "昵称");

        Assert.Equal("昵称", viewModel.SubFolder);
        Assert.Equal(ResolutionStatus.WillCreateAlias, viewModel.ResolutionStatus);
        Assert.False(viewModel.UpdateAliasCommand.CanExecute(null));
    }

    [Fact]
    public void SwitchingToCustomStrategyHidesUpdateAliasButton()
    {
        var aliases = new InMemoryUploaderAliasRepository();
        aliases.Upsert(1, "x");
        var viewModel = CreateViewModel(ownerMid: 1, ownerName: "某UP主", aliases: aliases);

        Assert.True(viewModel.UpdateAliasCommand.CanExecute(null));
        viewModel.Strategy = UploaderRoutingStrategy.Custom;

        Assert.False(viewModel.UpdateAliasCommand.CanExecute(null));
        Assert.False(viewModel.IsByUploader);
        Assert.True(viewModel.IsCustom);
    }

    [Fact]
    public void UpdateAliasCommandWritesToRepositoryAndUpdatesStatus()
    {
        var aliases = new InMemoryUploaderAliasRepository();
        var viewModel = CreateViewModel(ownerMid: 5, ownerName: "原昵称", aliases: aliases);

        Assert.Equal(ResolutionStatus.WillCreateAlias, viewModel.ResolutionStatus);

        viewModel.SubFolder = "新文件夹";
        viewModel.UpdateAliasCommand.Execute(null);

        Assert.Equal("新文件夹", aliases.Load()[5]);
        Assert.Equal(ResolutionStatus.MatchedAlias, viewModel.ResolutionStatus);
    }

    [Fact]
    public void UpdateAliasCommandTrimsFolderName()
    {
        var aliases = new InMemoryUploaderAliasRepository();
        var viewModel = CreateViewModel(ownerMid: 5, ownerName: "原昵称", aliases: aliases);

        viewModel.SubFolder = "  带空格  ";
        viewModel.UpdateAliasCommand.Execute(null);

        Assert.Equal("带空格", aliases.Load()[5]);
        Assert.Equal("带空格", viewModel.SubFolder);
    }

    [Fact]
    public void UpdateAliasCommandIsDisabledWhenSubFolderIsBlank()
    {
        var viewModel = CreateViewModel(ownerMid: 5, ownerName: "原昵称");

        viewModel.SubFolder = "  ";
        Assert.False(viewModel.UpdateAliasCommand.CanExecute(null));
    }

    [Fact]
    public void EditingSubFolderNotifiesUpdateAliasCommand()
    {
        var viewModel = CreateViewModel(ownerMid: 5, ownerName: "原昵称");

        Assert.True(viewModel.UpdateAliasCommand.CanExecute(null));

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
        var aliases = new InMemoryUploaderAliasRepository();
        aliases.Upsert(1, "原映射");
        var viewModel = CreateViewModel(ownerMid: 1, ownerName: "原昵称", aliases: aliases);

        Assert.True(viewModel.UpdateAliasCommand.CanExecute(null));
        viewModel.Strategy = UploaderRoutingStrategy.Custom;
        Assert.False(viewModel.UpdateAliasCommand.CanExecute(null));
    }

    [Fact]
    public void SwitchingStrategyPersistsPreference()
    {
        var prefs = new InMemoryUploaderRoutingPreferenceRepository();
        var viewModel = CreateViewModel(prefs: prefs);

        viewModel.SubFolder = "hello";
        viewModel.Strategy = UploaderRoutingStrategy.Custom;

        var saved = prefs.Load();
        Assert.Equal(UploaderRoutingStrategy.Custom, saved.LastStrategy);
        Assert.Equal("hello", saved.LastCustomFolder);
    }

    [Fact]
    public void StrategyChoicesContainsBothStrategies()
    {
        var viewModel = CreateViewModel();

        Assert.Contains(UploaderRoutingStrategy.ByUploader, viewModel.StrategyChoices);
        Assert.Contains(UploaderRoutingStrategy.Custom, viewModel.StrategyChoices);
    }

    [Fact]
    public void UpstreamDialogHostsPanelWithoutLosingSubtitleSelection()
    {
        using var settings = new TestSettingsStore();
        var interaction = new TestDesktopInteractionContext();
        var panel = CreateViewModel(ownerMid: 5, ownerName: "UP主");
        var viewModel = new ViewDownloadSetterViewModel(
            interaction.Notifications,
            new StubFilePickerService(),
            settings.Store,
            NullLogger<ViewDownloadSetterViewModel>.Instance)
        {
            Directory = Path.GetTempPath(),
            DownloadSubtitle = true
        };

        viewModel.OnDialogOpened(DownloadSettingsDialog.CreateRequest(
        [
            new(11, "zh", "中文", 0, "//zh"),
            new(22, "en", "English", 1, "//en")
        ]) with { ExtensionContent = panel });

        Assert.Same(panel, viewModel.ExtensionContent);
        Assert.True(viewModel.HasExtensionContent);

        viewModel.SubtitleTracks[1].IsSelected = false;
        viewModel.SubtitleTracks[0].IsDefault = true;
        AppDialogResult? result = null;
        viewModel.CloseRequested += (_, value) => result = value;

        viewModel.DownloadCommand.Execute(null);

        var selection = DownloadSettingsDialog.DecodeResult(Assert.IsType<AppDialogResult>(result));
        Assert.True(selection.HasValue);
        Assert.Equal(
            [11L],
            selection.Value.RequestedContent.SelectedSubtitleTrackIds.GetValueOrDefault().ToArray());
        Assert.Equal(11L, selection.Value.RequestedContent.DefaultSubtitleTrackId);
    }

    [Fact]
    public void AcceptedExtensionRoutesTheDownloadWithThePanelValues()
    {
        using var settings = new TestSettingsStore();
        var service = CreateRoutingService(settings.Store);
        var panel = service.CreateDialogExtension(ownerMid: 42, ownerName: "当前昵称");
        panel.SubFolder = "自定义目录";

        service.CompleteDialogExtension(accepted: true);

        var video = new VideoInfoView { UpperMid = 42, UpName = "当前昵称" };
        Assert.Equal("自定义目录", service.ResolveSubFolder(video));
    }

    [Fact]
    public void CanceledExtensionLeavesTheDownloadUnrouted()
    {
        using var settings = new TestSettingsStore();
        var service = CreateRoutingService(settings.Store);
        var panel = service.CreateDialogExtension(ownerMid: 42, ownerName: "当前昵称");
        panel.SubFolder = "自定义目录";

        service.CompleteDialogExtension(accepted: false);

        var video = new VideoInfoView { UpperMid = 42, UpName = "当前昵称" };
        Assert.Null(service.ResolveSubFolder(video));
    }

    [Fact]
    public void CreatingANewExtensionClearsThePreviousSelection()
    {
        using var settings = new TestSettingsStore();
        var service = CreateRoutingService(settings.Store);
        var first = service.CreateDialogExtension(ownerMid: 42, ownerName: "当前昵称");
        first.SubFolder = "自定义目录";
        service.CompleteDialogExtension(accepted: true);

        service.CreateDialogExtension(ownerMid: 43, ownerName: "另一个 UP 主");

        var video = new VideoInfoView { UpperMid = 42, UpName = "当前昵称" };
        Assert.Null(service.ResolveSubFolder(video));
    }

    private static UploaderRoutingService CreateRoutingService(ISettingsStore settingsStore)
    {
        var aliases = new InMemoryUploaderAliasRepository();
        return new UploaderRoutingService(
            new SubFolderRouteResolver(aliases),
            new InMemoryUploaderRoutingToggleRepository(),
            new InMemoryUploaderRoutingPreferenceRepository(),
            aliases,
            settingsStore,
            new StubVideoTagProvider(),
            new TestWbiKeyProvider(),
            new TestBilibiliApiClient(),
            new TestDesktopInteractionContext().Notifications,
            NullLogger.Instance);
    }

    private sealed class StubVideoTagProvider : IVideoTagProvider
    {
        public Task<IReadOnlyList<string>> GetTagsAsync(
            string bvid,
            long cid,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
    }

    private sealed class StubFilePickerService : IFilePickerService
    {
        public Task<string?> SelectFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string?> SelectVideoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> SelectVideosAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}
