using DownKyi.Core.Storage;
using DownKyi.Core.Storage.Uploader;

namespace DownKyi.Core.Tests.Uploader;

public sealed class FileUploaderAliasRepositoryTests : IDisposable
{
    private readonly string _tempDirectory;

    public FileUploaderAliasRepositoryTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"downkyi-uploader-aliases-{Guid.NewGuid():N}");
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
        Path.Combine(_tempDirectory, "uploader-aliases.json");

    private FileUploaderAliasRepository CreateRepo()
    {
        return new FileUploaderAliasRepository(() => TestFilePath);
    }

    [Fact]
    public void LoadWhenFileDoesNotExistReturnsEmptyDictionary()
    {
        var repo = CreateRepo();
        Assert.False(File.Exists(TestFilePath));

        var result = repo.Load();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void LoadWhenFileIsEmptyReturnsEmptyDictionary()
    {
        var repo = CreateRepo();
        File.WriteAllText(TestFilePath, string.Empty);

        var result = repo.Load();

        Assert.Empty(result);
    }

    [Fact]
    public void LoadWhenFileIsWhitespaceOnlyReturnsEmptyDictionary()
    {
        var repo = CreateRepo();
        File.WriteAllText(TestFilePath, "   \r\n\t  ");

        var result = repo.Load();

        Assert.Empty(result);
    }

    [Fact]
    public void LoadWhenFileIsCorruptJsonReturnsEmptyDictionary()
    {
        var repo = CreateRepo();
        File.WriteAllText(TestFilePath, "{ this is not valid JSON ][");

        var result = repo.Load();

        Assert.Empty(result);
    }

    [Fact]
    public void LoadWhenFileHasValidEntriesReturnsDictionary()
    {
        var repo = CreateRepo();
        File.WriteAllText(TestFilePath, """
            {
              "aliases": [
                { "mid": 123, "folderName": "博主A" },
                { "mid": 456, "folderName": "博主B" }
              ]
            }
            """);

        var result = repo.Load();

        Assert.Equal(2, result.Count);
        Assert.Equal("博主A", result[123]);
        Assert.Equal("博主B", result[456]);
    }

    [Fact]
    public void LoadWhenEntriesAreInvalidAreSkippedButOthersKept()
    {
        var repo = CreateRepo();
        File.WriteAllText(TestFilePath, """
            {
              "aliases": [
                { "mid": 0, "folderName": "invalid-mid" },
                { "mid": -5, "folderName": "negative-mid" },
                { "mid": 1, "folderName": "" },
                { "mid": 2, "folderName": "valid" }
              ]
            }
            """);

        var result = repo.Load();

        Assert.Single(result);
        Assert.Equal("valid", result[2]);
    }

    [Fact]
    public void SaveRoundTripsAllEntries()
    {
        var repo = CreateRepo();
        var input = new Dictionary<long, string>
        {
            [100] = "博主甲",
            [200] = "博主乙",
            [300] = "博主丙",
        };

        repo.Save(input);
        var output = repo.Load();

        Assert.Equal(input, output);
    }

    [Fact]
    public void SaveWhenTargetDoesNotExistCreatesFile()
    {
        var repo = CreateRepo();
        Assert.False(File.Exists(TestFilePath));

        repo.Save(new Dictionary<long, string> { [1] = "only" });

        Assert.True(File.Exists(TestFilePath));
    }

    [Fact]
    public void SaveWhenTargetExistsOverwritesAndDoesNotKeepStaleContent()
    {
        var repo = CreateRepo();
        File.WriteAllText(TestFilePath, """
            { "aliases": [ { "mid": 999, "folderName": "stale" } ] }
            """);

        repo.Save(new Dictionary<long, string> { [1] = "fresh" });

        var content = File.ReadAllText(TestFilePath);
        Assert.DoesNotContain("stale", content, StringComparison.Ordinal);
        Assert.Contains("fresh", content, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveDoesNotLeaveTempFileBehindOnSuccess()
    {
        var repo = CreateRepo();

        repo.Save(new Dictionary<long, string> { [1] = "x" });
        repo.Save(new Dictionary<long, string> { [2] = "y" });
        repo.Save(new Dictionary<long, string> { [3] = "z" });

        var leftovers = Directory
            .GetFiles(_tempDirectory)
            .Where(f => f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Empty(leftovers);
    }

    [Fact]
    public void SaveCreatesParentDirectoryIfMissing()
    {
        var nested = Path.Combine(_tempDirectory, "deep", "nested", "uploader-aliases.json");
        var repo = new FileUploaderAliasRepository(() => nested);

        repo.Save(new Dictionary<long, string> { [1] = "x" });

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void UpsertAddsNewEntry()
    {
        var repo = CreateRepo();

        repo.Upsert(123, "博主A");

        Assert.Equal("博主A", repo.Load()[123]);
    }

    [Fact]
    public void UpsertOverwritesExistingEntry()
    {
        var repo = CreateRepo();
        repo.Upsert(123, "旧名字");

        repo.Upsert(123, "新名字");

        var loaded = repo.Load();
        Assert.Single(loaded);
        Assert.Equal("新名字", loaded[123]);
    }

    [Fact]
    public void UpsertKeepsOtherEntriesIntact()
    {
        var repo = CreateRepo();
        repo.Save(new Dictionary<long, string>
        {
            [1] = "one",
            [2] = "two",
            [3] = "three",
        });

        repo.Upsert(2, "TWO_UPDATED");

        var loaded = repo.Load();
        Assert.Equal(3, loaded.Count);
        Assert.Equal("one", loaded[1]);
        Assert.Equal("TWO_UPDATED", loaded[2]);
        Assert.Equal("three", loaded[3]);
    }

    [Fact]
    public void UpsertWhenMidIsZeroOrNegativeThrows()
    {
        var repo = CreateRepo();

        Assert.Throws<ArgumentOutOfRangeException>(() => repo.Upsert(0, "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => repo.Upsert(-1, "x"));
    }

    [Fact]
    public void UpsertPersistsAcrossNewRepositoryInstance()
    {
        CreateRepo().Upsert(42, "持久化测试");

        var freshRepo = CreateRepo();
        Assert.Equal("持久化测试", freshRepo.Load()[42]);
    }

    [Fact]
    public void MultipleSequentialUpsertsAllEntriesSurvive()
    {
        var repo = CreateRepo();

        for (long mid = 1; mid <= 50; mid++)
        {
            repo.Upsert(mid, $"博主{mid}");
        }

        var loaded = repo.Load();
        Assert.Equal(50, loaded.Count);
        for (long mid = 1; mid <= 50; mid++)
        {
            Assert.Equal($"博主{mid}", loaded[mid]);
        }
    }

    [Fact]
    public void UpsertIsSerializedUnderConcurrentCalls()
    {
        var repo = CreateRepo();

        Parallel.For(1, 30, mid => repo.Upsert(mid, $"博主{mid}"));

        var loaded = repo.Load();
        Assert.Equal(29, loaded.Count);
        foreach (var (mid, name) in loaded)
        {
            Assert.Equal($"博主{mid}", name);
        }
    }

    [Fact]
    public void DefaultFilePathUsesConfigDirectory()
    {
        var repo = new FileUploaderAliasRepository();

        Assert.Equal(
            Path.Combine(ApplicationDataPaths.Config, "uploader-aliases.json"),
            repo.FilePath);
    }

    [Fact]
    public void EnvironmentOverrideTakesPrecedenceOverDefaultPath()
    {
        var custom = Path.Combine(_tempDirectory, "shared", "uploader-aliases.json");
        Environment.SetEnvironmentVariable("DOWNKYI_UPLOADER_ALIAS_PATH", custom);
        try
        {
            var repo = new FileUploaderAliasRepository();

            Assert.Equal(Path.GetFullPath(custom), repo.FilePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOWNKYI_UPLOADER_ALIAS_PATH", null);
        }
    }

    [Fact]
    public void EnvironmentOverrideEmptyValueFallsBackToDefault()
    {
        Environment.SetEnvironmentVariable("DOWNKYI_UPLOADER_ALIAS_PATH", "   ");
        try
        {
            var repo = new FileUploaderAliasRepository();

            Assert.EndsWith("uploader-aliases.json", repo.FilePath, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOWNKYI_UPLOADER_ALIAS_PATH", null);
        }
    }

    [Fact]
    public void LoadFilePathIsStableAcrossCalls()
    {
        var repo = CreateRepo();

        Assert.Equal(repo.FilePath, repo.FilePath);
    }
}
