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
            () => new ViewUploaderViewModel(
                new TestDesktopInteractionContext(),
                toggleRepository: null!,
                aliasRepository: new InMemoryUploaderAliasRepository()));
    }

    [Fact]
    public void CtorRejectsNullAliasRepository()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ViewUploaderViewModel(
                new TestDesktopInteractionContext(),
                new InMemoryUploaderRoutingToggleRepository(),
                aliasRepository: null!));
    }

    [Fact]
    public void OnNavigatedToLoadsExistingToggleState()
    {
        var toggle = new InMemoryUploaderRoutingToggleRepository(
            new UploaderRoutingToggle(IsEnabled: true));
        using var viewModel = new ViewUploaderViewModel(
            new TestDesktopInteractionContext(),
            toggle,
            new InMemoryUploaderAliasRepository());

        viewModel.OnNavigatedTo(NewContext());

        Assert.True(viewModel.IsUploaderRoutingEnabled);
    }

    [Fact]
    public void OnNavigatedToDefaultsToDisabledWhenRepositoryReturnsDefault()
    {
        var toggle = new InMemoryUploaderRoutingToggleRepository();
        using var viewModel = new ViewUploaderViewModel(
            new TestDesktopInteractionContext(),
            toggle,
            new InMemoryUploaderAliasRepository());

        viewModel.OnNavigatedTo(NewContext());

        Assert.False(viewModel.IsUploaderRoutingEnabled);
    }

    [Fact]
    public void ToggleCommandPersistsCurrentValue()
    {
        var toggle = new InMemoryUploaderRoutingToggleRepository();
        using var viewModel = new ViewUploaderViewModel(
            new TestDesktopInteractionContext(),
            toggle,
            new InMemoryUploaderAliasRepository())
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
        using var viewModel = new ViewUploaderViewModel(
            new TestDesktopInteractionContext(),
            toggle,
            new InMemoryUploaderAliasRepository())
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
        using var viewModel = new ViewUploaderViewModel(
            new TestDesktopInteractionContext(),
            toggle,
            new InMemoryUploaderAliasRepository())
        {
            IsUploaderRoutingEnabled = true
        };

        viewModel.ToggleCommand.Execute(null);

        Assert.False(viewModel.IsUploaderRoutingEnabled);
    }

    [Fact]
    public void OnNavigatedToLoadsExistingAliasesSortedByMid()
    {
        var alias = new InMemoryUploaderAliasRepository();
        alias.Save(new Dictionary<long, string>
        {
            [300] = "丙",
            [100] = "甲",
            [200] = "乙",
        });
        using var viewModel = new ViewUploaderViewModel(
            new TestDesktopInteractionContext(),
            new InMemoryUploaderRoutingToggleRepository(),
            alias);

        viewModel.OnNavigatedTo(NewContext());

        Assert.Equal(3, viewModel.Aliases.Count);
        Assert.Equal(100L, viewModel.Aliases[0].Mid);
        Assert.Equal(200L, viewModel.Aliases[1].Mid);
        Assert.Equal(300L, viewModel.Aliases[2].Mid);
    }

    [Fact]
    public void AddAliasCommandIsDisabledUntilInputsAreValid()
    {
        using var viewModel = new ViewUploaderViewModel(
            new TestDesktopInteractionContext(),
            new InMemoryUploaderRoutingToggleRepository(),
            new InMemoryUploaderAliasRepository())
        {
            NewMid = 0,
            NewFolderName = string.Empty
        };

        Assert.False(viewModel.AddAliasCommand.CanExecute(null));

        viewModel.NewMid = 1;
        Assert.False(viewModel.AddAliasCommand.CanExecute(null));

        viewModel.NewFolderName = "  ";
        Assert.False(viewModel.AddAliasCommand.CanExecute(null));

        viewModel.NewFolderName = "博主";
        Assert.True(viewModel.AddAliasCommand.CanExecute(null));
    }

    [Fact]
    public void AddAliasCommandPersistsAndAppendsEntry()
    {
        var alias = new InMemoryUploaderAliasRepository();
        using var viewModel = new ViewUploaderViewModel(
            new TestDesktopInteractionContext(),
            new InMemoryUploaderRoutingToggleRepository(),
            alias);

        viewModel.NewMid = 123;
        viewModel.NewFolderName = "  博主A  ";
        viewModel.AddAliasCommand.Execute(null);

        Assert.Equal("博主A", alias.Load()[123]);
        Assert.Single(viewModel.Aliases);
        Assert.Equal(123L, Assert.Single(viewModel.Aliases).Mid);
        Assert.Equal(0L, viewModel.NewMid);
        Assert.True(string.IsNullOrEmpty(viewModel.NewFolderName));
    }

    [Fact]
    public void AddAliasCommandTrimsFolderName()
    {
        var alias = new InMemoryUploaderAliasRepository();
        using var viewModel = new ViewUploaderViewModel(
            new TestDesktopInteractionContext(),
            new InMemoryUploaderRoutingToggleRepository(),
            alias);

        viewModel.NewMid = 7;
        viewModel.NewFolderName = "  spaced  ";
        viewModel.AddAliasCommand.Execute(null);

        Assert.Equal("spaced", alias.Load()[7]);
    }

    [Fact]
    public void RemoveAliasCommandDeletesEntryFromRepositoryAndCollection()
    {
        var alias = new InMemoryUploaderAliasRepository();
        alias.Save(new Dictionary<long, string>
        {
            [1] = "one",
            [2] = "two",
        });
        using var viewModel = new ViewUploaderViewModel(
            new TestDesktopInteractionContext(),
            new InMemoryUploaderRoutingToggleRepository(),
            alias);

        viewModel.OnNavigatedTo(NewContext());
        var target = viewModel.Aliases.Single(a => a.Mid == 1);

        viewModel.RemoveAliasCommand.Execute(target);

        Assert.False(alias.Load().ContainsKey(1));
        Assert.Single(viewModel.Aliases);
        Assert.Equal(2L, Assert.Single(viewModel.Aliases).Mid);
    }

    [Fact]
    public void RemoveAliasCommandIgnoresNonEntryParameters()
    {
        var alias = new InMemoryUploaderAliasRepository();
        alias.Save(new Dictionary<long, string> { [1] = "one" });
        using var viewModel = new ViewUploaderViewModel(
            new TestDesktopInteractionContext(),
            new InMemoryUploaderRoutingToggleRepository(),
            alias);

        viewModel.OnNavigatedTo(NewContext());

        viewModel.RemoveAliasCommand.Execute(null);
        viewModel.RemoveAliasCommand.Execute("not an entry");

        Assert.Single(viewModel.Aliases);
        Assert.Single(alias.Load());
    }

    [Fact]
    public void AliasFilePathExposesRepositoryPath()
    {
        var alias = new InMemoryUploaderAliasRepository("custom-path.json");
        using var viewModel = new ViewUploaderViewModel(
            new TestDesktopInteractionContext(),
            new InMemoryUploaderRoutingToggleRepository(),
            alias);

        Assert.Equal("custom-path.json", viewModel.AliasFilePath);
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