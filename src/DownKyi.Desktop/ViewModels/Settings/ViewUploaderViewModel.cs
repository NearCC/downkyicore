using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DownKyi.Application.Desktop;
using DownKyi.Core.Storage.Uploader;
using DownKyi.Utils;
using Newtonsoft.Json;

namespace DownKyi.ViewModels.Settings;

/// <summary>
/// Settings 页"UP 主"标签：承载"按 UP 主归类"功能的主开关，以及别名表的手动维护 UI。
/// 标签文本、提示均用中文字面量（避免依赖 i18n 资源 Key，减小 rebase 冲突面）。
/// </summary>
internal sealed class ViewUploaderViewModel : ViewModelBase
{
    public const string Tag = "PageSettingsUploader";

    private readonly IUploaderRoutingToggleRepository _toggleRepository;
    private readonly IUploaderAliasRepository _aliasRepository;

    private bool _isUploaderRoutingEnabled;

    public bool IsUploaderRoutingEnabled
    {
        get => _isUploaderRoutingEnabled;
        set => SetProperty(ref _isUploaderRoutingEnabled, value);
    }

    public ObservableCollection<UploaderAliasEntry> Aliases { get; } = new();

    private long _newMid;

    public long NewMid
    {
        get => _newMid;
        set
        {
            if (SetProperty(ref _newMid, value))
            {
                AddAliasCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private string? _newFolderName;

    public string? NewFolderName
    {
        get => _newFolderName;
        set
        {
            if (SetProperty(ref _newFolderName, value))
            {
                AddAliasCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string AliasFilePath => _aliasRepository.FilePath;

    public ViewUploaderViewModel(
        IDesktopInteractionContext desktopInteractions,
        IUploaderRoutingToggleRepository toggleRepository,
        IUploaderAliasRepository aliasRepository)
        : base(desktopInteractions)
    {
        _toggleRepository = toggleRepository ?? throw new ArgumentNullException(nameof(toggleRepository));
        _aliasRepository = aliasRepository ?? throw new ArgumentNullException(nameof(aliasRepository));
        Aliases.CollectionChanged += OnAliasesChanged;
    }

    public override void OnNavigatedTo(AppNavigationContext navigationContext)
    {
        base.OnNavigatedTo(navigationContext);
        IsUploaderRoutingEnabled = _toggleRepository.Load().IsEnabled;
        ReloadAliases();
    }

    public override void OnNavigatedFrom(AppNavigationContext navigationContext)
    {
        Aliases.CollectionChanged -= OnAliasesChanged;
        base.OnNavigatedFrom(navigationContext);
    }

    private RelayCommand? _toggleCommand;

    public RelayCommand ToggleCommand => _toggleCommand ??= new RelayCommand(ExecuteToggleCommand);

    private RelayCommand? _addAliasCommand;

    public RelayCommand AddAliasCommand => _addAliasCommand ??= new RelayCommand(ExecuteAddAlias, CanExecuteAddAlias);

    private RelayCommand<object>? _removeAliasCommand;

    public RelayCommand<object> RemoveAliasCommand => _removeAliasCommand ??=
        new RelayCommand<object>(ExecuteRemoveAlias);

    private void ExecuteToggleCommand()
    {
        var target = new UploaderRoutingToggle(IsEnabled: IsUploaderRoutingEnabled);
        try
        {
            _toggleRepository.Save(target);
            Notifications.Show(DictionaryResource.GetString("TipSettingUpdated"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            IsUploaderRoutingEnabled = !IsUploaderRoutingEnabled;
            Notifications.Show(DictionaryResource.GetString("TipSettingFailed"));
        }
    }

    private bool CanExecuteAddAlias()
    {
        return NewMid > 0 && !string.IsNullOrWhiteSpace(NewFolderName);
    }

    private void ExecuteAddAlias()
    {
        var folder = NewFolderName!.Trim();
        var mid = NewMid;
        try
        {
            _aliasRepository.Upsert(mid, folder);
            NewMid = 0;
            NewFolderName = string.Empty;
            ReloadAliases();
            Notifications.Show(DictionaryResource.GetString("TipSettingUpdated"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            Notifications.Show(DictionaryResource.GetString("TipSettingFailed"));
        }
    }

    private void ExecuteRemoveAlias(object? parameter)
    {
        if (parameter is not UploaderAliasEntry entry)
        {
            return;
        }

        try
        {
            _aliasRepository.Remove(entry.Mid);
            Aliases.Remove(entry);
            Notifications.Show(DictionaryResource.GetString("TipSettingUpdated"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            Notifications.Show(DictionaryResource.GetString("TipSettingFailed"));
        }
    }

    private void ReloadAliases()
    {
        IReadOnlyDictionary<long, string> snapshot;
        try
        {
            snapshot = _aliasRepository.Load();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            Notifications.Show(DictionaryResource.GetString("TipSettingFailed"));
            return;
        }

        Aliases.Clear();
        foreach (var kvp in snapshot.OrderBy(kvp => kvp.Key))
        {
            Aliases.Add(new UploaderAliasEntry(kvp.Key, kvp.Value));
        }
    }

    private void OnAliasesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        AddAliasCommand.NotifyCanExecuteChanged();
    }
}

internal sealed class UploaderAliasEntry : ObservableObject
{
    public UploaderAliasEntry(long mid, string folderName)
    {
        _mid = mid;
        _folderName = folderName;
    }

    private long _mid;

    public long Mid
    {
        get => _mid;
        set => SetProperty(ref _mid, value);
    }

    private string _folderName;

    public string FolderName
    {
        get => _folderName;
        set => SetProperty(ref _folderName, value);
    }
}