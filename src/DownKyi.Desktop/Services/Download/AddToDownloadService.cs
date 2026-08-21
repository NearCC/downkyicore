using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using DownKyi.Application.Bilibili;
using DownKyi.Application.Desktop;
using DownKyi.Application.Diagnostics;
using DownKyi.Core.BiliApi.Sign;
using DownKyi.Core.BiliApi.VideoStream;
using DownKyi.Core.Settings;
using DownKyi.Core.Storage.Uploader;
using DownKyi.Desktop.Services.Uploader;
using UploaderRoutingStrategy = DownKyi.Core.Storage.Uploader.UploaderRoutingStrategy;
using DownKyi.Presentation;
using DownKyi.Services.Video;
using DownKyi.Utils;
using Microsoft.Extensions.Logging;

namespace DownKyi.Services.Download;

/// <summary>
/// Owns one add-to-download session from media selection through queue admission.
/// </summary>
internal sealed class AddToDownloadService : IAddToDownloadSession
{
    private readonly DownloadTaskAdmissionService _admission;
    private readonly DownloadDuplicatePolicy _duplicatePolicy;
    private readonly DownloadMovieMetadataBuilder _metadataBuilder;
    private readonly ISettingsStore _settingsStore;
    private readonly IAppDialogService _dialogService;
    private readonly ILogger<AddToDownloadService> _logger;
    private readonly DownloadSubFolderResolver _subFolderResolver;
    private readonly IUploaderAliasRepository _aliasRepository;
    private IInfoService _videoInfoService = null!;
    private VideoInfoView? _videoInfoView;
    private IList<VideoSection>? _videoSections;
    private DownloadContentSelection _downloadContent = DownloadContentSelection.All;

    public AddToDownloadService(
        PlayStreamType streamType,
        DownloadTaskAdmissionService admission,
        DownloadDuplicatePolicy duplicatePolicy,
        DownloadMovieMetadataBuilder metadataBuilder,
        ISettingsStore settingsStore,
        IVideoTagProvider tagProvider,
        IWbiKeyProvider wbiKeyProvider,
        IBilibiliApiClient client,
        IAppDialogService dialogService,
        ILogger<AddToDownloadService> logger,
        DownloadSubFolderResolver subFolderResolver,
        IUploaderAliasRepository aliasRepository)
    {
        _admission = admission ?? throw new ArgumentNullException(nameof(admission));
        _duplicatePolicy = duplicatePolicy ?? throw new ArgumentNullException(nameof(duplicatePolicy));
        _metadataBuilder = metadataBuilder ?? throw new ArgumentNullException(nameof(metadataBuilder));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _subFolderResolver = subFolderResolver ?? throw new ArgumentNullException(nameof(subFolderResolver));
        _aliasRepository = aliasRepository ?? throw new ArgumentNullException(nameof(aliasRepository));
        ArgumentNullException.ThrowIfNull(tagProvider);
        ArgumentNullException.ThrowIfNull(wbiKeyProvider);
        ArgumentNullException.ThrowIfNull(client);

        switch (streamType)
        {
            case PlayStreamType.Video:
                _videoInfoService = new VideoInfoService(
                    settingsStore,
                    tagProvider,
                    wbiKeyProvider,
                    client);
                break;
            case PlayStreamType.Bangumi:
                _videoInfoService = new BangumiInfoService(settingsStore, client);
                break;
            case PlayStreamType.Cheese:
                _videoInfoService = new CheeseInfoService(settingsStore, client);
                break;
        }
    }

    public void SetVideoInfoService(IInfoService videoInfoService)
    {
        _videoInfoService = videoInfoService;
    }

    public void GetVideo(VideoInfoView videoInfoView, IList<VideoSection> videoSections)
    {
        _videoInfoView = videoInfoView;
        _videoSections = videoSections;
    }

    public void GetVideo()
    {
        _videoInfoView = _videoInfoService.GetVideoView();
        if (_videoInfoView == null)
        {
            _logger.LogDebugMessage("VideoInfoView is null.");
            return;
        }

        _videoSections = _videoInfoService.GetVideoSections(true);
        if (_videoSections == null)
        {
            _logger.LogDebugMessage("Video sections do not exist.");
            _videoSections =
            [
                new VideoSection
                {
                    Id = 0,
                    Title = "default",
                    IsSelected = true,
                    VideoPages = _videoInfoService.GetVideoPages() ?? new List<VideoPage>()
                }
            ];
        }

        foreach (var section in _videoSections)
        {
            foreach (var item in section.VideoPages)
            {
                item.IsSelected = true;
            }
        }
    }

    public async Task ParseVideoAsync(
        IInfoService videoInfoService,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(videoInfoService);

        if (_videoSections == null)
        {
            return;
        }

        var settings = _settingsStore.Current;
        foreach (var section in _videoSections)
        {
            foreach (var page in section.VideoPages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var playUrl = await videoInfoService
                    .GetVideoStreamAsync(page, cancellationToken)
                    .ConfigureAwait(false);
                VideoPagePlaybackMapper.ApplyPlayUrl(playUrl, page, settings);
            }
        }
    }

    public async Task<string?> SetDirectory(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = string.Empty;
        var videoSettings = _settingsStore.Current.Video;
        if (videoSettings.IsUseSaveVideoRootPath == AllowStatus.Yes)
        {
            _downloadContent = DownloadContentSelection.From(videoSettings.Content);
            directory = videoSettings.SaveVideoRootPath;
        }
        else
        {
            var result = await _dialogService.ShowAsync(
                new AppDialogRequest(AppDialog.DownloadSettings),
                cancellationToken).ConfigureAwait(true);
            if (result.Outcome == AppDialogOutcome.Accepted)
            {
                directory = result.Parameters.TryGetValue("directory", out var directoryValue)
                    ? directoryValue as string ?? string.Empty
                    : string.Empty;
                _downloadContent = new DownloadContentSelection(
                    GetBoolean(result.Parameters, "downloadAudio"),
                    GetBoolean(result.Parameters, "downloadVideo"),
                    GetBoolean(result.Parameters, "downloadDanmaku"),
                    GetBoolean(result.Parameters, "downloadSubtitle"),
                    GetBoolean(result.Parameters, "downloadCover"));
            }
        }

        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        if (!Directory.Exists(Directory.GetDirectoryRoot(directory)))
        {
            var alert = new AlertService(_dialogService);
            await alert
                .ShowError(DictionaryResource.GetString("DriveNotFound"), cancellationToken)
                .ConfigureAwait(true);
            return null;
        }

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
    }

    public async Task<int> AddToDownload(
        string? directory,
        bool isAll = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(directory) || _videoSections == null || _videoInfoView == null)
        {
            return -1;
        }

        var settings = _settingsStore.Current;
        var addedCount = 0;

        // 一次性解析子目录：当前按主 UP 主自动分配。后续 UI 阶段会按用户策略切换。
        var subFolder = ResolveSubFolderFor(_videoInfoView, settings, cancellationToken);

        foreach (var section in _videoSections)
        {
            foreach (var page in section.VideoPages)
            {
                if ((!isAll && !page.IsSelected) || page.PlayUrl == null)
                {
                    continue;
                }

                var retry = 0;
                while (page.VideoQuality == null && retry < 5)
                {
                    var playUrl = await _videoInfoService
                        .GetVideoStreamAsync(page, cancellationToken)
                        .ConfigureAwait(false);
                    VideoPagePlaybackMapper.ApplyPlayUrl(playUrl, page, settings);
                    retry++;
                }

                if (page.VideoQuality == null)
                {
                    continue;
                }

                var videoQuality = page.VideoQuality;
                if (await _duplicatePolicy
                    .ShouldSkipAsync(
                        page,
                        videoQuality,
                        settings.Basic.RepeatDownloadStrategy,
                        cancellationToken)
                    .ConfigureAwait(true))
                {
                    continue;
                }

                var downloadingItem = DownloadTaskDraftFactory.Create(
                    directory,
                    _videoInfoView,
                    section,
                    _videoSections.Count,
                    page,
                    videoQuality,
                    settings,
                    _downloadContent,
                    subFolder);
                if (settings.Video.Content.GenerateMovieMetadata && _downloadContent.Video)
                {
                    downloadingItem.Metadata = await _metadataBuilder
                        .BuildAsync(_videoInfoView, page, cancellationToken)
                        .ConfigureAwait(true);
                }

                await _admission
                    .AdmitAsync(
                        downloadingItem,
                        settings.Basic.RepeatFileAutoAddNumberSuffix,
                        cancellationToken)
                    .ConfigureAwait(true);
                addedCount++;
            }
        }

        return addedCount;
    }

    private static bool GetBoolean(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        return parameters.TryGetValue(key, out var value) && value is true;
    }

    private string? ResolveSubFolderFor(
        VideoInfoView video,
        ApplicationSettings settings,
        CancellationToken cancellationToken)
    {
        // TODO: Phase 7 UI 完成时，根据用户策略（Custom / ByUploader + 别名映射）切换。
        // 当前阶段：先按主 UP 主解析；别名表为空、Owner 无效时返回 null（保持现有行为）。
        var aliases = LoadAliasesBestEffort(settings);
        var inputs = new ResolverInputs(
            Strategy: UploaderRoutingStrategy.ByUploader,
            CustomFolder: null,
            ResolvedFolderName: null,
            OwnerMid: video.UpperMid,
            OwnerName: video.UpName,
            Aliases: aliases);
        var result = DownloadSubFolderResolver.Resolve(inputs);
        return result.SubFolder.Length == 0 ? null : result.SubFolder;
    }

    private IReadOnlyDictionary<long, string> LoadAliasesBestEffort(ApplicationSettings settings)
    {
        try
        {
            return _aliasRepository.Load();
        }
        catch (IOException)
        {
            // 别名表损坏也不影响下载主流程，吞掉异常返回空表。
            return new Dictionary<long, string>();
        }
        catch (JsonException)
        {
            // 别名表损坏也不影响下载主流程，吞掉异常返回空表。
            return new Dictionary<long, string>();
        }
        catch (UnauthorizedAccessException)
        {
            // 别名表损坏也不影响下载主流程，吞掉异常返回空表。
            return new Dictionary<long, string>();
        }
    }
}
