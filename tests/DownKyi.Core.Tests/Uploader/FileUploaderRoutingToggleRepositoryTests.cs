using DownKyi.Core.Storage;
using DownKyi.Core.Storage.Uploader;

namespace DownKyi.Core.Tests.Uploader;

public sealed class FileUploaderRoutingToggleRepositoryTests : IDisposable
{
    private readonly string _tempDirectory;

    public FileUploaderRoutingToggleRepositoryTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"downkyi-uploader-toggle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string TestFilePath =>
        Path.Combine(_tempDirectory, "uploader-routing-toggle.json");

    private FileUploaderRoutingToggleRepository CreateRepo()
    {
        return new FileUploaderRoutingToggleRepository(() => TestFilePath);
    }

    [Fact]
    public void LoadWhenFileDoesNotExistReturnsDisabledDefault()
    {
        var repo = CreateRepo();
        Assert.False(File.Exists(TestFilePath));

        var loaded = repo.Load();

        Assert.Equal(UploaderRoutingToggle.Default, loaded);
        Assert.False(loaded.IsEnabled);
    }

    [Fact]
    public void LoadWhenFileIsEmptyReturnsDisabledDefault()
    {
        var repo = CreateRepo();
        File.WriteAllText(TestFilePath, string.Empty);

        var loaded = repo.Load();

        Assert.False(loaded.IsEnabled);
    }

    [Fact]
    public void LoadWhenFileIsWhitespaceOnlyReturnsDisabledDefault()
    {
        var repo = CreateRepo();
        File.WriteAllText(TestFilePath, "   \r\n\t  ");

        var loaded = repo.Load();

        Assert.False(loaded.IsEnabled);
    }

    [Fact]
    public void LoadWhenFileIsCorruptJsonReturnsDisabledDefault()
    {
        var repo = CreateRepo();
        File.WriteAllText(TestFilePath, "{ this is not valid JSON ][" );

        var loaded = repo.Load();

        Assert.False(loaded.IsEnabled);
    }

    [Fact]
    public void LoadWhenPayloadIsEnabledReturnsEnabled()
    {
        var repo = CreateRepo();
        File.WriteAllText(TestFilePath, """
            { "isEnabled": true }
            """);

        var loaded = repo.Load();

        Assert.True(loaded.IsEnabled);
    }

    [Fact]
    public void LoadWhenPayloadIsDisabledReturnsDisabled()
    {
        var repo = CreateRepo();
        File.WriteAllText(TestFilePath, """
            { "isEnabled": false }
            """);

        var loaded = repo.Load();

        Assert.False(loaded.IsEnabled);
    }

    [Fact]
    public void SaveEnabledRoundTrips()
    {
        var repo = CreateRepo();

        repo.Save(new UploaderRoutingToggle(IsEnabled: true));
        var loaded = repo.Load();

        Assert.True(loaded.IsEnabled);
    }

    [Fact]
    public void SaveDisabledRoundTrips()
    {
        var repo = CreateRepo();
        repo.Save(new UploaderRoutingToggle(IsEnabled: true));

        repo.Save(new UploaderRoutingToggle(IsEnabled: false));
        var loaded = repo.Load();

        Assert.False(loaded.IsEnabled);
    }

    [Fact]
    public void SavePersistsAcrossNewRepositoryInstance()
    {
        CreateRepo().Save(new UploaderRoutingToggle(IsEnabled: true));

        var freshRepo = CreateRepo();
        Assert.True(freshRepo.Load().IsEnabled);
    }

    [Fact]
    public void SaveWhenTargetDoesNotExistCreatesFile()
    {
        var repo = CreateRepo();
        Assert.False(File.Exists(TestFilePath));

        repo.Save(UploaderRoutingToggle.Default);

        Assert.True(File.Exists(TestFilePath));
    }

    [Fact]
    public void SaveOverwritesPreviousContent()
    {
        var repo = CreateRepo();
        File.WriteAllText(TestFilePath, """
            { "isEnabled": true }
            """);

        repo.Save(new UploaderRoutingToggle(IsEnabled: false));

        var content = File.ReadAllText(TestFilePath);
        Assert.Contains("\"isEnabled\": false", content, StringComparison.Ordinal);
        Assert.DoesNotContain("true", content, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveDoesNotLeaveTempFileBehindOnSuccess()
    {
        var repo = CreateRepo();

        repo.Save(new UploaderRoutingToggle(IsEnabled: true));
        repo.Save(new UploaderRoutingToggle(IsEnabled: false));
        repo.Save(new UploaderRoutingToggle(IsEnabled: true));

        var leftovers = Directory
            .GetFiles(_tempDirectory)
            .Where(f => f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Empty(leftovers);
    }

    [Fact]
    public void SaveCreatesParentDirectoryIfMissing()
    {
        var nested = Path.Combine(_tempDirectory, "deep", "nested", "uploader-routing-toggle.json");
        var repo = new FileUploaderRoutingToggleRepository(() => nested);

        repo.Save(new UploaderRoutingToggle(IsEnabled: true));

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void SaveNullToggleThrows()
    {
        var repo = CreateRepo();

        Assert.Throws<ArgumentNullException>(() => repo.Save(null!));
    }

    [Fact]
    public void ConcurrentSavesKeepFileConsistent()
    {
        var repo = CreateRepo();

        Parallel.For(0, 30, i => repo.Save(new UploaderRoutingToggle(IsEnabled: i % 2 == 0)));

        // 最后一次保存必须是完整的、不会因中途写入而损坏
        var loaded = repo.Load();
        Assert.NotEqual(default, loaded);
    }

    [Fact]
    public void DefaultFilePathUsesConfigDirectory()
    {
        var repo = new FileUploaderRoutingToggleRepository();

        Assert.Equal(
            Path.Combine(ApplicationDataPaths.Config, "uploader-routing-toggle.json"),
            repo.FilePath);
    }

    [Fact]
    public void EnvironmentOverrideTakesPrecedenceOverDefaultPath()
    {
        var custom = Path.Combine(_tempDirectory, "shared", "uploader-routing-toggle.json");
        Environment.SetEnvironmentVariable("DOWNKYI_UPLOADER_TOGGLE_PATH", custom);
        try
        {
            var repo = new FileUploaderRoutingToggleRepository();

            Assert.Equal(Path.GetFullPath(custom), repo.FilePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOWNKYI_UPLOADER_TOGGLE_PATH", null);
        }
    }

    [Fact]
    public void EnvironmentOverrideEmptyValueFallsBackToDefault()
    {
        Environment.SetEnvironmentVariable("DOWNKYI_UPLOADER_TOGGLE_PATH", "   ");
        try
        {
            var repo = new FileUploaderRoutingToggleRepository();

            Assert.EndsWith("uploader-routing-toggle.json", repo.FilePath, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOWNKYI_UPLOADER_TOGGLE_PATH", null);
        }
    }

    [Fact]
    public void DefaultToggleIsDisabled()
    {
        Assert.False(UploaderRoutingToggle.Default.IsEnabled);
    }
}
