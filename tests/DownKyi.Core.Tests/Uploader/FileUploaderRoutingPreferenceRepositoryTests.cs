using DownKyi.Core.Storage;
using DownKyi.Core.Storage.Uploader;

namespace DownKyi.Core.Tests.Uploader;

public sealed class FileUploaderRoutingPreferenceRepositoryTests : IDisposable
{
    private readonly string _tempDirectory;

    public FileUploaderRoutingPreferenceRepositoryTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"downkyi-uploader-routing-prefs-{Guid.NewGuid():N}");
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
        Path.Combine(_tempDirectory, "uploader-routing-prefs.json");

    private FileUploaderRoutingPreferenceRepository CreateRepo()
    {
        return new FileUploaderRoutingPreferenceRepository(() => TestFilePath);
    }

    [Fact]
    public void LoadWhenFileDoesNotExistReturnsDefault()
    {
        var repo = CreateRepo();
        Assert.False(File.Exists(TestFilePath));

        var loaded = repo.Load();

        Assert.Equal(UploaderRoutingPreference.Default, loaded);
        Assert.Equal(UploaderRoutingStrategy.ByUploader, loaded.LastStrategy);
        Assert.Equal(string.Empty, loaded.LastCustomFolder);
        Assert.True(loaded.LastPersistAlias);
    }

    [Fact]
    public void LoadWhenFileIsEmptyReturnsDefault()
    {
        var repo = CreateRepo();
        File.WriteAllText(TestFilePath, string.Empty);

        var loaded = repo.Load();

        Assert.Equal(UploaderRoutingPreference.Default, loaded);
    }

    [Fact]
    public void LoadWhenFileIsWhitespaceOnlyReturnsDefault()
    {
        var repo = CreateRepo();
        File.WriteAllText(TestFilePath, "   \r\n\t  ");

        var loaded = repo.Load();

        Assert.Equal(UploaderRoutingPreference.Default, loaded);
    }

    [Fact]
    public void LoadWhenFileIsCorruptJsonReturnsDefault()
    {
        var repo = CreateRepo();
        File.WriteAllText(TestFilePath, "{ this is not valid JSON ][");

        var loaded = repo.Load();

        Assert.Equal(UploaderRoutingPreference.Default, loaded);
    }

    [Fact]
    public void LoadWhenFileHasValidPayloadReturnsIt()
    {
        var repo = CreateRepo();
        File.WriteAllText(TestFilePath, """
            {
              "lastStrategy": 1,
              "lastCustomFolder": "我的文件夹",
              "lastPersistAlias": false
            }
            """);

        var loaded = repo.Load();

        Assert.Equal(UploaderRoutingStrategy.ByUploader, loaded.LastStrategy);
        Assert.Equal("我的文件夹", loaded.LastCustomFolder);
        Assert.False(loaded.LastPersistAlias);
    }

    [Fact]
    public void LoadWhenStrategyIsCustomReturnsIt()
    {
        var repo = CreateRepo();
        File.WriteAllText(TestFilePath, """
            {
              "lastStrategy": 0,
              "lastCustomFolder": "anime",
              "lastPersistAlias": true
            }
            """);

        var loaded = repo.Load();

        Assert.Equal(UploaderRoutingStrategy.Custom, loaded.LastStrategy);
        Assert.Equal("anime", loaded.LastCustomFolder);
        Assert.True(loaded.LastPersistAlias);
    }

    [Fact]
    public void LoadWhenStrategyIsOutOfRangeFallsBackToDefault()
    {
        var repo = CreateRepo();
        File.WriteAllText(TestFilePath, """
            {
              "lastStrategy": 999,
              "lastCustomFolder": "x",
              "lastPersistAlias": true
            }
            """);

        var loaded = repo.Load();

        // 非法枚举值 → 回退到默认
        Assert.Equal(UploaderRoutingPreference.Default.LastStrategy, loaded.LastStrategy);
        // 但其他字段仍保留
        Assert.Equal("x", loaded.LastCustomFolder);
    }

    [Fact]
    public void LoadWhenCustomFolderIsNullStoresEmptyString()
    {
        var repo = CreateRepo();
        File.WriteAllText(TestFilePath, """
            {
              "lastStrategy": 1,
              "lastCustomFolder": null,
              "lastPersistAlias": true
            }
            """);

        var loaded = repo.Load();

        Assert.Equal(string.Empty, loaded.LastCustomFolder);
    }

    [Fact]
    public void SaveRoundTripsAllFields()
    {
        var repo = CreateRepo();
        var input = new UploaderRoutingPreference(
            LastStrategy: UploaderRoutingStrategy.Custom,
            LastCustomFolder: "影视飓风",
            LastPersistAlias: false);

        repo.Save(input);
        var loaded = repo.Load();

        Assert.Equal(input, loaded);
    }

    [Fact]
    public void SavePersistsAcrossNewRepositoryInstance()
    {
        CreateRepo().Save(new UploaderRoutingPreference(
            UploaderRoutingStrategy.ByUploader,
            "持久化测试",
            true));

        var freshRepo = CreateRepo();
        var loaded = freshRepo.Load();

        Assert.Equal(UploaderRoutingStrategy.ByUploader, loaded.LastStrategy);
        Assert.Equal("持久化测试", loaded.LastCustomFolder);
        Assert.True(loaded.LastPersistAlias);
    }

    [Fact]
    public void SaveWhenTargetDoesNotExistCreatesFile()
    {
        var repo = CreateRepo();
        Assert.False(File.Exists(TestFilePath));

        repo.Save(UploaderRoutingPreference.Default);

        Assert.True(File.Exists(TestFilePath));
    }

    [Fact]
    public void SaveOverwritesPreviousContent()
    {
        var repo = CreateRepo();
        repo.Save(new UploaderRoutingPreference(
            UploaderRoutingStrategy.Custom, "old", true));

        repo.Save(new UploaderRoutingPreference(
            UploaderRoutingStrategy.ByUploader, "new", false));

        var loaded = repo.Load();
        Assert.Equal(UploaderRoutingStrategy.ByUploader, loaded.LastStrategy);
        Assert.Equal("new", loaded.LastCustomFolder);
        Assert.False(loaded.LastPersistAlias);
    }

    [Fact]
    public void SaveDoesNotLeaveTempFileBehindOnSuccess()
    {
        var repo = CreateRepo();

        repo.Save(UploaderRoutingPreference.Default);
        repo.Save(UploaderRoutingPreference.Default);
        repo.Save(UploaderRoutingPreference.Default);

        var leftovers = Directory
            .GetFiles(_tempDirectory)
            .Where(f => f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Empty(leftovers);
    }

    [Fact]
    public void SaveCreatesParentDirectoryIfMissing()
    {
        var nested = Path.Combine(_tempDirectory, "deep", "nested", "uploader-routing-prefs.json");
        var repo = new FileUploaderRoutingPreferenceRepository(() => nested);

        repo.Save(UploaderRoutingPreference.Default);

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void SaveNullPreferenceThrows()
    {
        var repo = CreateRepo();

        Assert.Throws<ArgumentNullException>(() => repo.Save(null!));
    }

    [Fact]
    public void ConcurrentSavesKeepFileConsistent()
    {
        var repo = CreateRepo();

        Parallel.For(0, 30, i =>
        {
            var pref = new UploaderRoutingPreference(
                i % 2 == 0 ? UploaderRoutingStrategy.Custom : UploaderRoutingStrategy.ByUploader,
                $"folder-{i}",
                i % 3 == 0);
            repo.Save(pref);
        });

        // 最后一次保存的结果必须是完整的、不会因中途写入而损坏
        var loaded = repo.Load();
        Assert.NotEqual(default, loaded);
        Assert.NotEmpty(loaded.LastCustomFolder);
    }

    [Fact]
    public void DefaultFilePathUsesConfigDirectory()
    {
        var repo = new FileUploaderRoutingPreferenceRepository();

        Assert.Equal(
            Path.Combine(ApplicationDataPaths.Config, "uploader-routing-prefs.json"),
            repo.FilePath);
    }

    [Fact]
    public void EnvironmentOverrideTakesPrecedenceOverDefaultPath()
    {
        var custom = Path.Combine(_tempDirectory, "shared", "uploader-routing-prefs.json");
        Environment.SetEnvironmentVariable("DOWNKYI_UPLOADER_ROUTING_PREFS_PATH", custom);
        try
        {
            var repo = new FileUploaderRoutingPreferenceRepository();

            Assert.Equal(Path.GetFullPath(custom), repo.FilePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOWNKYI_UPLOADER_ROUTING_PREFS_PATH", null);
        }
    }

    [Fact]
    public void EnvironmentOverrideEmptyValueFallsBackToDefault()
    {
        Environment.SetEnvironmentVariable("DOWNKYI_UPLOADER_ROUTING_PREFS_PATH", "   ");
        try
        {
            var repo = new FileUploaderRoutingPreferenceRepository();

            Assert.EndsWith("uploader-routing-prefs.json", repo.FilePath, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOWNKYI_UPLOADER_ROUTING_PREFS_PATH", null);
        }
    }

    [Fact]
    public void DefaultPreferenceIsByUploaderWithPersistAliasTrue()
    {
        Assert.Equal(UploaderRoutingStrategy.ByUploader, UploaderRoutingPreference.Default.LastStrategy);
        Assert.Equal(string.Empty, UploaderRoutingPreference.Default.LastCustomFolder);
        Assert.True(UploaderRoutingPreference.Default.LastPersistAlias);
    }
}
