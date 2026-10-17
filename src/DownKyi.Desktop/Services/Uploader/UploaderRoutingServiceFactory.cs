using System;
using DownKyi.Application.Bilibili;
using DownKyi.Application.Desktop;
using DownKyi.Core.BiliApi.Sign;
using DownKyi.Core.Settings;
using DownKyi.Core.Storage.Uploader;
using DownKyi.Desktop.Services.Uploader;
using DownKyi.Services.Video;
using Microsoft.Extensions.Logging;

namespace DownKyi.Services.Uploader;

internal interface IUploaderRoutingServiceFactory
{
    UploaderRoutingService Create();
}

/// <summary>
/// Builds one routing helper per add-to-download session, keeping the upstream
/// factory free of the feature's dependency list.
/// </summary>
internal sealed class UploaderRoutingServiceFactory : IUploaderRoutingServiceFactory
{
    private readonly IUploaderAliasRepository _aliasRepository;
    private readonly IUploaderRoutingPreferenceRepository _preferenceRepository;
    private readonly IUploaderRoutingToggleRepository _toggleRepository;
    private readonly ISettingsStore _settingsStore;
    private readonly IVideoTagProvider _tagProvider;
    private readonly IWbiKeyProvider _wbiKeyProvider;
    private readonly IBilibiliApiClient _client;
    private readonly IUserNotificationService _notifications;
    private readonly ILogger<UploaderRoutingService> _logger;

    public UploaderRoutingServiceFactory(
        IUploaderAliasRepository aliasRepository,
        IUploaderRoutingPreferenceRepository preferenceRepository,
        IUploaderRoutingToggleRepository toggleRepository,
        ISettingsStore settingsStore,
        IVideoTagProvider tagProvider,
        IWbiKeyProvider wbiKeyProvider,
        IBilibiliApiClient client,
        IUserNotificationService notifications,
        ILogger<UploaderRoutingService> logger)
    {
        _aliasRepository = aliasRepository ?? throw new ArgumentNullException(nameof(aliasRepository));
        _preferenceRepository = preferenceRepository
            ?? throw new ArgumentNullException(nameof(preferenceRepository));
        _toggleRepository = toggleRepository ?? throw new ArgumentNullException(nameof(toggleRepository));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _tagProvider = tagProvider ?? throw new ArgumentNullException(nameof(tagProvider));
        _wbiKeyProvider = wbiKeyProvider ?? throw new ArgumentNullException(nameof(wbiKeyProvider));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public UploaderRoutingService Create()
    {
        return new UploaderRoutingService(
            new SubFolderRouteResolver(_aliasRepository),
            _toggleRepository,
            _preferenceRepository,
            _aliasRepository,
            _settingsStore,
            _tagProvider,
            _wbiKeyProvider,
            _client,
            _notifications,
            _logger);
    }
}
