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
using DownKyi.Utils;
using Microsoft.Extensions.Logging;

namespace DownKyi.ViewModels.Dialogs;

/// <summary>
/// 同款下载弹窗，多出 1 个"子目录"输入框。
///
/// 与 <see cref="ViewDownloadSetterViewModel"/> 的区别：
/// - 复制了所有原行为（避免修改原 ViewModel，方便 rebase 上游）。
/// - 额外读取 <see cref="UploaderRoutingPreference"/> 作为子目录默认值。
/// - 用户点"下载"时，把 <see cref="SubFolder"/> 透传到 <c>parameters["subFolder"]</c>，
///   上游 <c>AddToDownloadService</c> 据此决定是否调用 resolver。
/// </summary>
internal class ViewDownloadSetterWithSubFolderViewModel : BaseDialogViewModel
{
    public const string Tag = "DialogDownloadSetterWithSubFolder";
    private readonly IUserNotificationService _notifications;
    private readonly IFilePickerService _filePickerService;
    private readonly ISettingsStore _settingsStore;
    private readonly IUploaderRoutingPreferenceRepository _preferenceRepository;
    private readonly ILogger<ViewDownloadSetterWithSubFolderViewModel> _logger;

    private const int MaxDirectoryListCount = 20;

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

    private string _subFolder = string.Empty;

    public string SubFolder
    {
        get => _subFolder;
        set => SetProperty(ref _subFolder, value);
    }

    #endregion

    public ViewDownloadSetterWithSubFolderViewModel(
        IUserNotificationService notifications,
        IFilePickerService filePickerService,
        ISettingsStore settingsStore,
        IUploaderRoutingPreferenceRepository preferenceRepository,
        ILogger<ViewDownloadSetterWithSubFolderViewModel> logger)
    {
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _filePickerService = filePickerService ?? throw new ArgumentNullException(nameof(filePickerService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _preferenceRepository = preferenceRepository ?? throw new ArgumentNullException(nameof(preferenceRepository));
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

        // 子目录默认值（仅在用户上次选过 Custom 策略时填充；ByUploader 不强制预设）。
        try
        {
            var pref = _preferenceRepository.Load();
            SubFolder = pref.LastStrategy == UploaderRoutingStrategy.Custom
                ? pref.LastCustomFolder
                : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            _logger.LogWarningMessage("Could not load uploader routing preference for sub-folder default.", e);
            SubFolder = string.Empty;
        }

        #endregion
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

        // 返回数据：原 5 项 + 新增 subFolder（trick: 即使用户没填，也传空串让上游明确知道本次没有自定义子目录）
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            { "directory", Directory },
            { "downloadAudio", DownloadAudio },
            { "downloadVideo", DownloadVideo },
            { "downloadDanmaku", DownloadDanmaku },
            { "downloadSubtitle", DownloadSubtitle },
            { "downloadCover", DownloadCover },
            { "subFolder", SubFolder ?? string.Empty }
        };

        CloseDialog(AppDialogOutcome.Accepted, parameters);
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
