using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Application.Bilibili;
using DownKyi.Application.Desktop;
using DownKyi.Application.Diagnostics;
using DownKyi.Core.BiliApi;
using DownKyi.Core.BiliApi.Sign;
using DownKyi.Core.Settings;
using DownKyi.Core.Storage.Uploader;
using DownKyi.Desktop.Services.Uploader;
using DownKyi.Presentation;
using DownKyi.Services.Video;
using DownKyi.ViewModels.Dialogs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace DownKyi.Services.Uploader;

/// <summary>
/// Owns the uploader routing extension that is hosted by the upstream download
/// settings dialog, and remembers the accepted values until the download task
/// drafts are created.
/// </summary>
internal sealed class UploaderRoutingService
{
    private readonly SubFolderRouteResolver _routeResolver;
    private readonly IUploaderRoutingToggleRepository _toggleRepository;
    private readonly IUploaderRoutingPreferenceRepository _preferenceRepository;
    private readonly IUploaderAliasRepository _aliasRepository;
    private readonly ISettingsStore _settingsStore;
    private readonly IVideoTagProvider _tagProvider;
    private readonly IWbiKeyProvider _wbiKeyProvider;
    private readonly IBilibiliApiClient _client;
    private readonly IUserNotificationService _notifications;
    private readonly ILogger _logger;
    private UploaderRoutingPanelViewModel? _pendingPanel;
    private UploaderRoutingSelection? _pendingSelection;

    public UploaderRoutingService(
        SubFolderRouteResolver routeResolver,
        IUploaderRoutingToggleRepository toggleRepository,
        IUploaderRoutingPreferenceRepository preferenceRepository,
        IUploaderAliasRepository aliasRepository,
        ISettingsStore settingsStore,
        IVideoTagProvider tagProvider,
        IWbiKeyProvider wbiKeyProvider,
        IBilibiliApiClient client,
        IUserNotificationService notifications,
        ILogger logger)
    {
        _routeResolver = routeResolver ?? throw new ArgumentNullException(nameof(routeResolver));
        _toggleRepository = toggleRepository ?? throw new ArgumentNullException(nameof(toggleRepository));
        _preferenceRepository = preferenceRepository
            ?? throw new ArgumentNullException(nameof(preferenceRepository));
        _aliasRepository = aliasRepository ?? throw new ArgumentNullException(nameof(aliasRepository));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _tagProvider = tagProvider ?? throw new ArgumentNullException(nameof(tagProvider));
        _wbiKeyProvider = wbiKeyProvider ?? throw new ArgumentNullException(nameof(wbiKeyProvider));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsEnabled
    {
        get
        {
            try
            {
                return _toggleRepository.Load().IsEnabled;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.LogWarningMessage("Could not load the uploader routing toggle.", exception);
                return false;
            }
        }
    }

    /// <summary>
    /// Resolves the uploader of a batch item. Failures only mean the routing panel
    /// opens without an uploader; they never block the download flow.
    /// </summary>
    public async Task<(long OwnerMid, string OwnerName)> ResolveOwnerAsync(
        string? source,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return (0L, string.Empty);
        }

        try
        {
            var infoService = await VideoInfoService.CreateAsync(
                source,
                _settingsStore,
                _tagProvider,
                _wbiKeyProvider,
                _client,
                cancellationToken).ConfigureAwait(false);
            var videoView = infoService.GetVideoView(cancellationToken);
            return videoView is { UpperMid: > 0 }
                ? (videoView.UpperMid, videoView.UpName ?? string.Empty)
                : (0L, string.Empty);
        }
        catch (Exception exception) when (
            exception is BilibiliApiResponseException or HttpRequestException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarningMessage(
                "The uploader identity could not be resolved before opening the routing dialog.",
                exception);
            return (0L, string.Empty);
        }
    }

    /// <summary>
    /// Creates the panel hosted by the upstream download settings dialog.
    /// </summary>
    public UploaderRoutingPanelViewModel CreateDialogExtension(long ownerMid, string ownerName)
    {
        _pendingSelection = null;
        _pendingPanel = new UploaderRoutingPanelViewModel(
            ownerMid,
            ownerName,
            _preferenceRepository,
            _aliasRepository,
            _notifications,
            _logger);
        return _pendingPanel;
    }

    /// <summary>
    /// Captures the panel values after the upstream dialog closes. Only an accepted
    /// dialog produces a routing selection.
    /// </summary>
    public void CompleteDialogExtension(bool accepted)
    {
        var panel = _pendingPanel;
        _pendingPanel = null;
        if (!accepted || panel is null)
        {
            _pendingSelection = null;
            return;
        }

        panel.PersistCurrentPreference();
        var subFolder = string.IsNullOrWhiteSpace(panel.SubFolder)
            ? null
            : panel.SubFolder.Trim();
        _pendingSelection = new UploaderRoutingSelection(panel.Strategy, subFolder);
    }

    public string? ResolveSubFolder(VideoInfoView video)
    {
        return _pendingSelection is { } selection
            ? _routeResolver.Resolve(
                selection.Strategy,
                selection.SubFolder,
                video.UpperMid,
                video.UpName ?? string.Empty)
            : null;
    }
}
