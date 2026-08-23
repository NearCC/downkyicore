using DownKyi.Application.Desktop;
using DownKyi.Core.Storage.Uploader;
using DownKyi.ViewModels.Settings;
using System.IO;

namespace DownKyi.Tests;

public sealed class ViewUploaderViewModelTests
{
    [Fact]
    public void CtorRejectsNullToggleRepository()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ViewUploaderViewModel(new TestDesktopInteractionContext(), toggleRepository: null!));
    }

    [Fact]
    public void OnNavigatedToLoadsExistingToggleState()
    {
        var toggle = new InMemoryUploaderRoutingToggleRepository(
            new UploaderRoutingToggle(IsEnabled: true));
        using var viewModel = new ViewUploaderViewModel(new TestDesktopInteractionContext(), toggle);

        viewModel.OnNavigatedTo(NewContext());

        Assert.True(viewModel.IsUploaderRoutingEnabled);
    }

    [Fact]
    public void OnNavigatedToDefaultsToDisabledWhenRepositoryReturnsDefault()
    {
        var toggle = new InMemoryUploaderRoutingToggleRepository();
        using var viewModel = new ViewUploaderViewModel(new TestDesktopInteractionContext(), toggle);

        viewModel.OnNavigatedTo(NewContext());

        Assert.False(viewModel.IsUploaderRoutingEnabled);
    }

    [Fact]
    public void ToggleCommandPersistsCurrentValue()
    {
        var toggle = new InMemoryUploaderRoutingToggleRepository();
        using var viewModel = new ViewUploaderViewModel(new TestDesktopInteractionContext(), toggle)
        {
            IsUploaderRoutingEnabled = true
        };

        viewModel.ToggleCommand.Execute(null);

        Assert.True(toggle.Load().IsEnabled);
    }

    [Fact]
    public void ToggleCommandPersistsFalseAfterDisabling()
    {
        var toggle = new InMemoryUploaderRoutingToggleRepository(
            new UploaderRoutingToggle(IsEnabled: true));
        using var viewModel = new ViewUploaderViewModel(new TestDesktopInteractionContext(), toggle)
        {
            IsUploaderRoutingEnabled = false
        };

        viewModel.ToggleCommand.Execute(null);

        Assert.False(toggle.Load().IsEnabled);
    }

    [Fact]
    public void ToggleCommandRollsBackValueWhenRepositorySaveThrows()
    {
        var toggle = new ThrowingToggleRepository();
        using var viewModel = new ViewUploaderViewModel(new TestDesktopInteractionContext(), toggle)
        {
            IsUploaderRoutingEnabled = true
        };

        viewModel.ToggleCommand.Execute(null);

        Assert.False(viewModel.IsUploaderRoutingEnabled);
    }

    private static AppNavigationContext NewContext() =>
        new(
            Region: AppNavigationRegion.Settings,
            Route: AppRoute.SettingsUploader,
            ParentRoute: AppRoute.Settings,
            Parameter: null,
            Parameters: new AppNavigationParameters());

    private sealed class ThrowingToggleRepository : IUploaderRoutingToggleRepository
    {
        public ThrowingToggleRepository()
        {
            FilePath = $"<throwing-toggle>:{Guid.NewGuid():N}";
        }

        public string FilePath { get; }

        public UploaderRoutingToggle Load() => UploaderRoutingToggle.Default;

        public void Save(UploaderRoutingToggle toggle) =>
            throw new IOException("simulated disk full");
    }
}
