using DownKyi.Application.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

namespace DownKyi.Core.Storage.Uploader;

/// <summary>
/// 基于 JSON 文件的 <see cref="IUploaderAliasRepository"/> 实现。
/// - 文件路径：默认 <c>{Config}/uploader-aliases.json</c>，可通过环境变量 <c>DOWNKYI_UPLOADER_ALIAS_PATH</c> 覆盖。
/// - 原子写入：写临时文件 + rename，避免中途崩溃损坏现有别名表。
/// - 线程安全：实例内 lock 序列化 Load+Save/Upsert。
/// </summary>
public sealed class FileUploaderAliasRepository : IUploaderAliasRepository
{
    private const string DefaultRelativePath = "uploader-aliases.json";
    private const string EnvironmentVariableOverride = "DOWNKYI_UPLOADER_ALIAS_PATH";
    private const string TempFileSuffix = ".tmp";

    private readonly object _gate = new();
    private readonly ILogger _logger;
    private readonly Func<string> _filePathProvider;

    public FileUploaderAliasRepository()
        : this(ResolveDefaultFilePath, NullLogger.Instance)
    {
    }

    public FileUploaderAliasRepository(ILogger logger)
        : this(ResolveDefaultFilePath, logger)
    {
    }

    /// <summary>
    /// 测试或自定义路径场景使用：传入一个返回绝对路径的函数。
    /// </summary>
    public FileUploaderAliasRepository(Func<string> filePathProvider)
        : this(filePathProvider, NullLogger.Instance)
    {
    }

    public FileUploaderAliasRepository(Func<string> filePathProvider, ILogger logger)
    {
        _filePathProvider = filePathProvider ?? throw new ArgumentNullException(nameof(filePathProvider));
        _logger = logger ?? NullLogger.Instance;
    }

    public string FilePath => _filePathProvider();

    public IReadOnlyDictionary<long, string> Load()
    {
        lock (_gate)
        {
            return LoadUnsafe();
        }
    }

    public void Save(IReadOnlyDictionary<long, string> aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);

        lock (_gate)
        {
            WriteAtomically(aliases);
        }
    }

    public void Upsert(long mid, string folderName)
    {
        if (mid <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mid), mid, "mid must be > 0.");
        }

        ArgumentNullException.ThrowIfNull(folderName);

        lock (_gate)
        {
            var current = LoadUnsafe();
            var updated = new Dictionary<long, string>(current)
            {
                [mid] = folderName,
            };
            WriteAtomically(updated);
        }
    }

    public void Remove(long mid)
    {
        if (mid <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mid), mid, "mid must be > 0.");
        }

        lock (_gate)
        {
            var current = LoadUnsafe();
            if (!current.ContainsKey(mid))
            {
                return;
            }

            var updated = new Dictionary<long, string>(current);
            updated.Remove(mid);
            WriteAtomically(updated);
        }
    }

    private Dictionary<long, string> LoadUnsafe()
    {
        var path = FilePath;

        if (!File.Exists(path))
        {
            return new Dictionary<long, string>();
        }

        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<long, string>();
            }

            var payload = JsonConvert.DeserializeObject<AliasFilePayload>(json);
            if (payload?.Aliases == null)
            {
                return new Dictionary<long, string>();
            }

            var result = new Dictionary<long, string>();
            foreach (var entry in payload.Aliases)
            {
                if (entry.Mid <= 0 || string.IsNullOrEmpty(entry.FolderName))
                {
                    continue;
                }

                result[entry.Mid] = entry.FolderName;
            }

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarningMessage(
                $"Uploader alias file at {path} is corrupt. Treating as empty table.",
                ex);
            return new Dictionary<long, string>();
        }
        catch (IOException ex)
        {
            _logger.LogWarningMessage(
                $"Failed to read uploader alias file at {path}. Treating as empty table.",
                ex);
            return new Dictionary<long, string>();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarningMessage(
                $"Access denied when reading uploader alias file at {path}. Treating as empty table.",
                ex);
            return new Dictionary<long, string>();
        }
    }

    private void WriteAtomically(IReadOnlyDictionary<long, string> aliases)
    {
        var path = FilePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new AliasFilePayload
        {
            Aliases = aliases
                .Where(kvp => kvp.Key > 0 && !string.IsNullOrEmpty(kvp.Value))
                .Select(kvp => new UploaderAlias(kvp.Key, kvp.Value))
                .ToArray(),
        };

        var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
        var tempPath = path + TempFileSuffix;

        // Write to temp file first; File.Move with overwrite is atomic on POSIX
        // and best-effort atomic on Windows when the target exists.
        File.WriteAllText(tempPath, json);

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    private static string ResolveDefaultFilePath()
    {
        var overridePath = Environment.GetEnvironmentVariable(EnvironmentVariableOverride);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        return Path.Combine(ApplicationDataPaths.Config, DefaultRelativePath);
    }

    private sealed class AliasFilePayload
    {
        [JsonProperty("aliases")]
        public UploaderAlias[]? Aliases { get; set; }
    }
}
