using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DownKyi.Application.Desktop;
using DownKyi.Application.Diagnostics;
using DownKyi.Commands;
using DownKyi.Core.Settings;
using DownKyi.Core.Storage.Uploader;
using DownKyi.Core.Utils;
using DownKyi.Images;
using DownKyi.Desktop.Services.Uploader;
using DownKyi.Utils;
using Microsoft.Extensions.Logging;

namespace DownKyi.ViewModels.Dialogs;

/// <summary>
/// 同款下载弹窗 + 归类策略面板：
/// - 顶部"位置"= 下载根目录（原版行为）；
/// - 下方"归类方式"：下拉选择 Custom / ByUploader；
///   - Custom → 文本框 = 用户本次输入的子目录；
///   - ByUploader → 文本框预填别名（或 UP 主昵称），下方有匹配状态 + "更新映射"按钮。
///
/// 与 <see cref="ViewDownloadSetterViewModel"/> 的区别：
/// - 复制了所有原行为（避免修改原 ViewModel，方便 rebase 上游）。
/// - 弹窗打开时根据 <see cref="UploaderRoutingPreference"/> 恢复上次策略与文本；
/// - 接受时把 strategy + subFolder 两个键透传到 parameters，供 <c>AddToDownloadService</c> 决定路由。
/// </summary>
internal class ViewDownloadSetterWithSubFolderViewModel : BaseDialogViewModel
{
    public const string Tag = "DialogDownloadSetterWithSubFolder";

    private readonly IUserNotificationService _notifications;
    private readonly IFilePickerService _filePickerService;
    private readonly ISettingsStore _settingsStore;
    private readonly IUploaderRoutingPreferenceRepository _preferenceRepository;
    private readonly IUploaderAliasRepository _aliasRepository;
    private readonly ILogger<ViewDownloadSetterWithSubFolderViewModel> _logger;

    private const int MaxDirectoryListCount = 20;

    private long _ownerMid;
    private string _ownerName = string.Empty;

    private bool HasOwner => _ownerMid > 0;

    #region 页面属性申明

    private VectorImage _cloudDownloadIcon = null!;

    public VectorImage CloudDownloadIcon
    {
        get => _cloudDownloadIcon;
        set => SetProperty(ref _cloudDownloadIcon, value);
    }

    private VectorImage _folderIcon = null!;

    public VectorImage FolderIcon
    {
        get => _folderIcon;
        set => SetProperty(ref _folderIcon, value);
    }

    private bool _isDefaultDownloadDirectory;

    public bool IsDefaultDownloadDirectory
    {
        get => _isDefaultDownloadDirectory;
        set => SetProperty(ref _isDefaultDownloadDirectory, value);
    }

    public ObservableCollection<string> DirectoryList { get; private set; }

    private string _directory = string.Empty;

    public string Directory
    {
        get => _directory;
        set
        {
            SetProperty(ref _directory, value);

            if (string.IsNullOrEmpty(_directory) || !Path.IsPathFullyQualified(_directory))
            {
                return;
            }

            DriveName = Path.GetPathRoot(_directory) ?? _directory;
            try
            {
                DriveNameFreeSpace = Format.FormatFileSize(HardDisk.GetHardDiskFreeSpace(_directory));
            }
            catch (Exception e) when (e is DriveNotFoundException or IOException or UnauthorizedAccessException)
            {
                DriveNameFreeSpace = Format.FormatFileSize(0);
                _logger.LogErrorMessage("Available download disk space could not be read.", e);
            }
        }
    }

    private string _driveName = string.Empty;

    public string DriveName
    {
        get => _driveName;
        set => SetProperty(ref _driveName, value);
    }

    private string _driveNameFreeSpace = string.Empty;

    public string DriveNameFreeSpace
    {
        get => _driveNameFreeSpace;
        set => SetProperty(ref _driveNameFreeSpace, value);
    }

    private bool _downloadAll;

    public bool DownloadAll
    {
        get => _downloadAll;
        set => SetProperty(ref _downloadAll, value);
    }

    private bool _downloadAudio;

    public bool DownloadAudio
    {
        get => _downloadAudio;
        set => SetProperty(ref _downloadAudio, value);
    }

    private bool _downloadVideo;

    public bool DownloadVideo
    {
        get => _downloadVideo;
        set => SetProperty(ref _downloadVideo, value);
    }

    private bool _downloadDanmaku;

    public bool DownloadDanmaku
    {
        get => _downloadDanmaku;
        set => SetProperty(ref _downloadDanmaku, value);
    }

    private bool _downloadSubtitle;

    public bool DownloadSubtitle
    {
        get => _downloadSubtitle;
        set => SetProperty(ref _downloadSubtitle, value);
    }

    private bool _downloadCover;

    public bool DownloadCover
    {
        get => _downloadCover;
        set => SetProperty(ref _downloadCover, value);
    }

    #endregion

    #region 策略面板属性

    private static readonly IReadOnlyList<UploaderRoutingStrategy> StrategyChoicesList =
        new[] { UploaderRoutingStrategy.ByUploader, UploaderRoutingStrategy.Custom };

    public IReadOnlyList<UploaderRoutingStrategy> StrategyChoices
    {
        get
        {
            // Touch instance field to satisfy CA1822 (XAML binding needs instance property).
            _ = _strategy;
            return StrategyChoicesList;
        }
    }

    private UploaderRoutingStrategy _strategy = UploaderRoutingStrategy.ByUploader;

    public UploaderRoutingStrategy Strategy
    {
        get => _strategy;
        set
        {
            if (SetProperty(ref _strategy, value))
            {
                ApplyStrategyDefaultsOnStrategySwitch();
                PersistPreference();
                UpdateAliasCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private string _subFolder = string.Empty;

    /// <summary>
    /// 文本框值：Custom 策略 = 用户输入；ByUploader 策略 = 解析结果（用户可改）。
    /// 提交时透传为 <c>subFolder</c> 参数。
    /// </summary>
    public string SubFolder
    {
        get => _subFolder;
        set
        {
            if (SetProperty(ref _subFolder, value))
            {
                UpdateAliasCommand.NotifyCanExecuteChanged();
                if (Strategy == UploaderRoutingStrategy.Custom)
                {
                    PersistPreference();
                }
            }
        }
    }

    private ResolutionStatus _resolutionStatus = ResolutionStatus.WillCreateAlias;

    public ResolutionStatus ResolutionStatus
    {
        get => _resolutionStatus;
        private set
        {
            if (SetProperty(ref _resolutionStatus, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsByUploader));
                OnPropertyChanged(nameof(IsCustom));
                OnPropertyChanged(nameof(IsUpdateAliasEnabled));
                UpdateAliasCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusText => ResolutionStatus switch
    {
        ResolutionStatus.MatchedAlias => "已匹配别名（修改后请点右侧按钮更新映射）",
        ResolutionStatus.WillCreateAlias => "未匹配别名，本次不会写入映射",
        ResolutionStatus.NoUploader => "视频没有 UP 主信息",
        _ => string.Empty,
    };

    public bool IsByUploader => Strategy == UploaderRoutingStrategy.ByUploader;

    public bool IsCustom => Strategy == UploaderRoutingStrategy.Custom;

    public bool IsUpdateAliasEnabled =>
        Strategy == UploaderRoutingStrategy.ByUploader
        && _ownerMid > 0
        && !string.IsNullOrWhiteSpace(SubFolder);

    #endregion

    /// <summary>
    /// 弹窗打开时由 <c>AvaloniaDialogService</c> 调用，从 <see cref="AppDialogRequest.Parameters"/>
    /// 读取当前视频的 UP 主 mid / name。
    /// </summary>
    public override void OnDialogOpened(AppDialogRequest request)
    {
        base.OnDialogOpened(request);
        if (request.Parameters == null)
        {
            return;
        }

        var ownerMid = request.Parameters.TryGetValue("ownerMid", out var midValue) && midValue is long mid
            ? mid
            : 0L;
        var ownerName = request.Parameters.TryGetValue("ownerName", out var nameValue) ? nameValue as string : null;
        SetOwner(ownerMid, ownerName ?? string.Empty);
    }

    public ViewDownloadSetterWithSubFolderViewModel(
        IUserNotificationService notifications,
        IFilePickerService filePickerService,
        ISettingsStore settingsStore,
        IUploaderRoutingPreferenceRepository preferenceRepository,
        IUploaderAliasRepository aliasRepository,
        ILogger<ViewDownloadSetterWithSubFolderViewModel> logger)
    {
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _filePickerService = filePickerService ?? throw new ArgumentNullException(nameof(filePickerService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _preferenceRepository = preferenceRepository ?? throw new ArgumentNullException(nameof(preferenceRepository));
        _aliasRepository = aliasRepository ?? throw new ArgumentNullException(nameof(aliasRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        #region 属性初始化

        Title = DictionaryResource.GetString("DownloadSetter");

        CloudDownloadIcon = NormalIcon.Instance().CloudDownload;
        CloudDownloadIcon.Fill = DictionaryResource.GetColor("ColorPrimary");

        FolderIcon = NormalIcon.Instance().Folder;
        FolderIcon.Fill = DictionaryResource.GetColor("ColorPrimary");

        // 下载内容
        var videoSettings = _settingsStore.Current.Video;
        var videoContent = videoSettings.Content;

        DownloadAudio = videoContent.DownloadAudio;
        DownloadVideo = videoContent.DownloadVideo;
        DownloadDanmaku = videoContent.DownloadDanmaku;
        DownloadSubtitle = videoContent.DownloadSubtitle;
        DownloadCover = videoContent.DownloadCover;

        if (DownloadAudio && DownloadVideo && DownloadDanmaku && DownloadSubtitle && DownloadCover)
        {
            DownloadAll = true;
        }
        else
        {
            DownloadAll = false;
        }

        // 历史下载目录
        DirectoryList = new ObservableCollection<string>(videoSettings.HistoryVideoRootPaths);
        var directory = videoSettings.SaveVideoRootPath;
        if (!DirectoryList.Contains(directory))
        {
            ListHelper.InsertUnique(DirectoryList, directory, 0);
        }

        Directory = directory;

        // 是否使用默认下载目录
        IsDefaultDownloadDirectory = videoSettings.IsUseSaveVideoRootPath == AllowStatus.Yes;

        // 策略默认值（从偏好文件读取）。
        try
        {
            var pref = _preferenceRepository.Load();
            _strategy = pref.LastStrategy;
            _subFolder = pref.LastStrategy == UploaderRoutingStrategy.Custom
                ? pref.LastCustomFolder
                : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            _logger.LogWarningMessage("Could not load uploader routing preference.", e);
            _strategy = UploaderRoutingStrategy.ByUploader;
            _subFolder = string.Empty;
        }

        #endregion
    }

    /// <summary>
    /// 弹窗打开时由 <c>AddToDownloadService</c> 调用，把当前视频的 UP 主信息传进来。
    /// </summary>
    public void SetOwner(long ownerMid, string ownerName)
    {
        _ownerMid = ownerMid;
        _ownerName = ownerName ?? string.Empty;
        ApplyStrategyDefaults();
    }

    private void ApplyStrategyDefaults()
    {
        // 强制按新策略重算默认值（覆盖用户输入）—— 仅用于视频变化场景（OnDialogOpened / SetOwner）。
        if (Strategy == UploaderRoutingStrategy.ByUploader)
        {
            ApplyByUploaderDefaults();
        }
        else
        {
            ApplyCustomDefaultsFromPreference();
        }
    }

    private void ApplyStrategyDefaultsOnStrategySwitch()
    {
        // 用户手动切换策略时不要覆盖已经输入的文本框值；
        // 只切换 ByUploader 时的状态（MatchedAlias / WillCreateAlias 等）。
        if (Strategy == UploaderRoutingStrategy.ByUploader)
        {
            RecomputeByUploaderStatus();
        }
    }

    private void ApplyByUploaderDefaults()
    {
        if (_ownerMid > 0)
        {
            IReadOnlyDictionary<long, string> aliases;
            try
            {
                aliases = _aliasRepository.Load();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
            {
                _logger.LogWarningMessage("Could not load uploader aliases for sub-folder default.", e);
                aliases = new Dictionary<long, string>();
            }

            if (aliases.TryGetValue(_ownerMid, out var existing))
            {
                SubFolder = existing;
                ResolutionStatus = ResolutionStatus.MatchedAlias;
            }
            else
            {
                SubFolder = _ownerName;
                ResolutionStatus = ResolutionStatus.WillCreateAlias;
            }
        }
        else if (!string.IsNullOrEmpty(_ownerName))
        {
            // mid 缺失但昵称有：仍按昵称预填，禁用"更新映射"。
            SubFolder = _ownerName;
            ResolutionStatus = ResolutionStatus.WillCreateAlias;
        }
        else
        {
            SubFolder = string.Empty;
            ResolutionStatus = ResolutionStatus.NoUploader;
        }
    }

    private void RecomputeByUploaderStatus()
    {
        if (_ownerMid <= 0)
        {
            // 没有 mid 时禁用"更新映射"（无法写别名表），但有昵称仍视为可用。
            ResolutionStatus = string.IsNullOrEmpty(_ownerName)
                ? ResolutionStatus.NoUploader
                : ResolutionStatus.WillCreateAlias;
            return;
        }

        IReadOnlyDictionary<long, string> aliases;
        try
        {
            aliases = _aliasRepository.Load();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            _logger.LogWarningMessage("Could not load uploader aliases for status check.", e);
            aliases = new Dictionary<long, string>();
        }

        ResolutionStatus = aliases.ContainsKey(_ownerMid)
            ? ResolutionStatus.MatchedAlias
            : ResolutionStatus.WillCreateAlias;
    }

    private void ApplyCustomDefaultsFromPreference()
    {
        try
        {
            var pref = _preferenceRepository.Load();
            SubFolder = pref.LastCustomFolder ?? string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            _logger.LogWarningMessage("Could not load uploader routing preference for Custom default.", e);
            SubFolder = string.Empty;
        }

        ResolutionStatus = ResolutionStatus.WillCreateAlias;
    }

    private void PersistPreference()
    {
        try
        {
            _preferenceRepository.Save(new UploaderRoutingPreference(
                LastStrategy: Strategy,
                LastCustomFolder: Strategy == UploaderRoutingStrategy.Custom ? SubFolder : string.Empty,
                LastPersistAlias: true));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            _logger.LogWarningMessage("Failed to persist uploader routing preference.", e);
        }
    }

    #region 命令申明

    // 浏览文件夹事件
    private DownKyiAsyncDelegateCommand? _browseCommand;

    public DownKyiAsyncDelegateCommand BrowseCommand => _browseCommand ??= new DownKyiAsyncDelegateCommand(ExecuteBrowseCommand, _logger);

    private async Task ExecuteBrowseCommand()
    {
        var directory = await SetDirectory().ConfigureAwait(true);

        if (directory == null)
        {
            _notifications.Show(DictionaryResource.GetString("WarningNullDirectory"));
        }
        else
        {
            ListHelper.InsertUnique(DirectoryList, directory, 0);
            Directory = directory;

            if (DirectoryList.Count > MaxDirectoryListCount)
            {
                DirectoryList.RemoveAt(MaxDirectoryListCount);
            }
        }
    }

    private RelayCommand? _downloadAllCommand;

    public RelayCommand DownloadAllCommand => _downloadAllCommand ??= new RelayCommand(ExecuteDownloadAllCommand);

    private void ExecuteDownloadAllCommand()
    {
        if (DownloadAll)
        {
            DownloadAudio = true;
            DownloadVideo = true;
            DownloadDanmaku = true;
            DownloadSubtitle = true;
            DownloadCover = true;
        }
        else
        {
            DownloadAudio = false;
            DownloadVideo = false;
            DownloadDanmaku = false;
            DownloadSubtitle = false;
            DownloadCover = false;
        }

        SetVideoContent();
    }

    private RelayCommand? _downloadAudioCommand;

    public RelayCommand DownloadAudioCommand => _downloadAudioCommand ??= new RelayCommand(ExecuteDownloadAudioCommand);

    private void ExecuteDownloadAudioCommand()
    {
        if (!DownloadAudio)
        {
            DownloadAll = false;
        }

        if (DownloadAudio && DownloadVideo && DownloadDanmaku && DownloadSubtitle && DownloadCover)
        {
            DownloadAll = true;
        }

        SetVideoContent();
    }

    private RelayCommand? _downloadVideoCommand;

    public RelayCommand DownloadVideoCommand => _downloadVideoCommand ??= new RelayCommand(ExecuteDownloadVideoCommand);

    private void ExecuteDownloadVideoCommand()
    {
        if (!DownloadVideo)
        {
            DownloadAll = false;
        }

        if (DownloadAudio && DownloadVideo && DownloadDanmaku && DownloadSubtitle && DownloadCover)
        {
            DownloadAll = true;
        }

        SetVideoContent();
    }

    private RelayCommand? _downloadDanmakuCommand;

    public RelayCommand DownloadDanmakuCommand => _downloadDanmakuCommand ??= new RelayCommand(ExecuteDownloadDanmakuCommand);

    private void ExecuteDownloadDanmakuCommand()
    {
        if (!DownloadDanmaku)
        {
            DownloadAll = false;
        }

        if (DownloadAudio && DownloadVideo && DownloadDanmaku && DownloadSubtitle && DownloadCover)
        {
            DownloadAll = true;
        }

        SetVideoContent();
    }

    private RelayCommand? _downloadSubtitleCommand;

    public RelayCommand DownloadSubtitleCommand => _downloadSubtitleCommand ??= new RelayCommand(ExecuteDownloadSubtitleCommand);

    private void ExecuteDownloadSubtitleCommand()
    {
        if (!DownloadSubtitle)
        {
            DownloadAll = false;
        }

        if (DownloadAudio && DownloadVideo && DownloadDanmaku && DownloadSubtitle && DownloadCover)
        {
            DownloadAll = true;
        }

        SetVideoContent();
    }

    private RelayCommand? _downloadCoverCommand;

    public RelayCommand DownloadCoverCommand => _downloadCoverCommand ??= new RelayCommand(ExecuteDownloadCoverCommand);

    private void ExecuteDownloadCoverCommand()
    {
        if (!DownloadCover)
        {
            DownloadAll = false;
        }

        if (DownloadAudio && DownloadVideo && DownloadDanmaku && DownloadSubtitle && DownloadCover)
        {
            DownloadAll = true;
        }

        SetVideoContent();
    }

    private RelayCommand? _downloadCommand;

    public RelayCommand DownloadCommand => _downloadCommand ??= new RelayCommand(ExecuteDownloadCommand);

    private void ExecuteDownloadCommand()
    {
        if (string.IsNullOrEmpty(Directory))
        {
            return;
        }

        // 将Directory移动到第一项
        ListHelper.InsertUnique(DirectoryList, Directory, 0, ref _directory);

        // 将更新后的目录设置一次写入
        _settingsStore.Update(settings => settings with
        {
            Video = settings.Video with
            {
                IsUseSaveVideoRootPath = IsDefaultDownloadDirectory ? AllowStatus.Yes : AllowStatus.No,
                SaveVideoRootPath = Directory,
                HistoryVideoRootPaths = DirectoryList.ToImmutableArray()
            }
        });

        // 同步保存一次偏好（带最新 CustomFolder）。
        PersistPreference();

        // 返回数据：原 5 项 + 新增 strategy + subFolder
        var subFolderToSend = string.IsNullOrWhiteSpace(SubFolder) ? null : SubFolder.Trim();
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            { "directory", Directory },
            { "downloadAudio", DownloadAudio },
            { "downloadVideo", DownloadVideo },
            { "downloadDanmaku", DownloadDanmaku },
            { "downloadSubtitle", DownloadSubtitle },
            { "downloadCover", DownloadCover },
            { "strategy", Strategy },
            { "subFolder", subFolderToSend }
        };

        CloseDialog(AppDialogOutcome.Accepted, parameters);
    }

    private RelayCommand? _updateAliasCommand;

    public RelayCommand UpdateAliasCommand => _updateAliasCommand ??= new RelayCommand(ExecuteUpdateAlias, CanExecuteUpdateAlias);

    private bool CanExecuteUpdateAlias()
    {
        return IsUpdateAliasEnabled;
    }

    private void ExecuteUpdateAlias()
    {
        if (_ownerMid <= 0 || string.IsNullOrWhiteSpace(SubFolder))
        {
            return;
        }

        var folder = SubFolder.Trim();
        try
        {
            _aliasRepository.Upsert(_ownerMid, folder);
            SubFolder = folder;
            ResolutionStatus = ResolutionStatus.MatchedAlias;
            _notifications.Show("已更新别名映射");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            _logger.LogWarningMessage("Failed to upsert uploader alias from dialog.", e);
            _notifications.Show("更新别名映射失败");
        }
    }

    #endregion

    private void SetVideoContent()
    {
        _settingsStore.Update(settings => settings with
        {
            Video = settings.Video with
            {
                Content = settings.Video.Content with
                {
                    DownloadAudio = DownloadAudio,
                    DownloadVideo = DownloadVideo,
                    DownloadDanmaku = DownloadDanmaku,
                    DownloadSubtitle = DownloadSubtitle,
                    DownloadCover = DownloadCover
                }
            }
        });
    }

    private async Task<string?> SetDirectory()
    {
        return await _filePickerService.SelectFolderAsync().ConfigureAwait(true);
    }
}