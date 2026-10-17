using System;
using System.Collections.Generic;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DownKyi.Application.Desktop;
using DownKyi.Application.Diagnostics;
using DownKyi.Core.Storage.Uploader;
using DownKyi.Desktop.Services.Uploader;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace DownKyi.ViewModels.Dialogs;

/// <summary>
/// Optional panel hosted by the upstream download settings dialog. It only owns
/// the uploader-routing choice; everything else in the dialog stays upstream.
/// </summary>
internal sealed class UploaderRoutingPanelViewModel : ObservableObject
{
    private readonly IUploaderRoutingPreferenceRepository _preferenceRepository;
    private readonly IUploaderAliasRepository _aliasRepository;
    private readonly IUserNotificationService _notifications;
    private readonly ILogger _logger;
    private readonly long _ownerMid;
    private readonly string _ownerName;

    private UploaderRoutingStrategy _strategy = UploaderRoutingStrategy.ByUploader;
    private string _subFolder = string.Empty;
    private ResolutionStatus _resolutionStatus = ResolutionStatus.WillCreateAlias;

    public UploaderRoutingPanelViewModel(
        long ownerMid,
        string ownerName,
        IUploaderRoutingPreferenceRepository preferenceRepository,
        IUploaderAliasRepository aliasRepository,
        IUserNotificationService notifications,
        ILogger logger)
    {
        _ownerMid = ownerMid > 0 ? ownerMid : 0L;
        _ownerName = ownerName ?? string.Empty;
        _preferenceRepository = preferenceRepository
            ?? throw new ArgumentNullException(nameof(preferenceRepository));
        _aliasRepository = aliasRepository ?? throw new ArgumentNullException(nameof(aliasRepository));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        LoadPreference();
        ApplyStrategyDefaults();
    }

    public IReadOnlyList<UploaderRoutingStrategy> StrategyChoices { get; } =
        new[] { UploaderRoutingStrategy.ByUploader, UploaderRoutingStrategy.Custom };

    public UploaderRoutingStrategy Strategy
    {
        get => _strategy;
        set
        {
            if (!SetProperty(ref _strategy, value))
            {
                return;
            }

            ApplyStrategyDefaultsOnStrategySwitch();
            PersistPreference();
            OnPropertyChanged(nameof(IsByUploader));
            OnPropertyChanged(nameof(IsCustom));
            OnPropertyChanged(nameof(IsUpdateAliasEnabled));
            UpdateAliasCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Text box value: user input for Custom, resolved folder for ByUploader.
    /// </summary>
    public string SubFolder
    {
        get => _subFolder;
        set
        {
            if (!SetProperty(ref _subFolder, value))
            {
                return;
            }

            UpdateAliasCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsUpdateAliasEnabled));
            if (Strategy == UploaderRoutingStrategy.Custom)
            {
                PersistPreference();
            }
        }
    }

    public ResolutionStatus ResolutionStatus
    {
        get => _resolutionStatus;
        private set
        {
            if (!SetProperty(ref _resolutionStatus, value))
            {
                return;
            }

            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsByUploader));
            OnPropertyChanged(nameof(IsCustom));
            OnPropertyChanged(nameof(IsUpdateAliasEnabled));
            UpdateAliasCommand.NotifyCanExecuteChanged();
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

    public RelayCommand UpdateAliasCommand =>
        _updateAliasCommand ??= new RelayCommand(ExecuteUpdateAlias, CanExecuteUpdateAlias);

    private RelayCommand? _updateAliasCommand;

    internal void PersistCurrentPreference()
    {
        PersistPreference();
    }

    private void LoadPreference()
    {
        try
        {
            var preference = _preferenceRepository.Load();
            _strategy = preference.LastStrategy;
            _subFolder = preference.LastStrategy == UploaderRoutingStrategy.Custom
                ? preference.LastCustomFolder
                : string.Empty;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarningMessage("Could not load uploader routing preference.", exception);
            _strategy = UploaderRoutingStrategy.ByUploader;
            _subFolder = string.Empty;
        }
    }

    private void ApplyStrategyDefaults()
    {
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
        if (Strategy == UploaderRoutingStrategy.ByUploader)
        {
            RecomputeByUploaderStatus();
        }
    }

    private void ApplyByUploaderDefaults()
    {
        if (_ownerMid > 0)
        {
            var aliases = LoadAliasesBestEffort("sub-folder default");
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
            ResolutionStatus = string.IsNullOrEmpty(_ownerName)
                ? ResolutionStatus.NoUploader
                : ResolutionStatus.WillCreateAlias;
            return;
        }

        var aliases = LoadAliasesBestEffort("status check");
        ResolutionStatus = aliases.ContainsKey(_ownerMid)
            ? ResolutionStatus.MatchedAlias
            : ResolutionStatus.WillCreateAlias;
    }

    private void ApplyCustomDefaultsFromPreference()
    {
        try
        {
            var preference = _preferenceRepository.Load();
            SubFolder = preference.LastCustomFolder ?? string.Empty;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarningMessage(
                "Could not load uploader routing preference for Custom default.",
                exception);
            SubFolder = string.Empty;
        }

        ResolutionStatus = ResolutionStatus.WillCreateAlias;
    }

    private IReadOnlyDictionary<long, string> LoadAliasesBestEffort(string purpose)
    {
        try
        {
            return _aliasRepository.Load();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarningMessage(
                $"Could not load uploader aliases for {purpose}.",
                exception);
            return new Dictionary<long, string>();
        }
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
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarningMessage("Failed to persist uploader routing preference.", exception);
        }
    }

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
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarningMessage("Failed to upsert uploader alias from dialog.", exception);
            _notifications.Show("更新别名映射失败");
        }
    }
}
