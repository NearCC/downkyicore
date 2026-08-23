using System;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using DownKyi.Application.Desktop;
using DownKyi.Core.Storage.Uploader;
using DownKyi.Utils;
using Newtonsoft.Json;

namespace DownKyi.ViewModels.Settings;

/// <summary>
/// Settings 页"UP 主"标签：承载"按 UP 主归类"功能的主开关。
/// 标签文本、提示均用中文字面量（避免依赖 i18n 资源 Key，减小 rebase 冲突面）。
/// </summary>
internal sealed class ViewUploaderViewModel : ViewModelBase
{
    public const string Tag = "PageSettingsUploader";

    private readonly IUploaderRoutingToggleRepository _toggleRepository;

    private bool _isUploaderRoutingEnabled;

    public bool IsUploaderRoutingEnabled
    {
        get => _isUploaderRoutingEnabled;
        set => SetProperty(ref _isUploaderRoutingEnabled, value);
    }

    public ViewUploaderViewModel(
        IDesktopInteractionContext desktopInteractions,
        IUploaderRoutingToggleRepository toggleRepository)
        : base(desktopInteractions)
    {
        _toggleRepository = toggleRepository ?? throw new ArgumentNullException(nameof(toggleRepository));
    }

    public override void OnNavigatedTo(AppNavigationContext navigationContext)
    {
        base.OnNavigatedTo(navigationContext);
        IsUploaderRoutingEnabled = _toggleRepository.Load().IsEnabled;
    }

    private RelayCommand? _toggleCommand;

    public RelayCommand ToggleCommand => _toggleCommand ??= new RelayCommand(ExecuteToggleCommand);

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
}
