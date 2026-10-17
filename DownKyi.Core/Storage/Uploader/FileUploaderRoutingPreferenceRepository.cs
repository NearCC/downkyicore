using DownKyi.Application.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

namespace DownKyi.Core.Storage.Uploader;

/// <summary>
/// 基于 JSON 文件的 <see cref="IUploaderRoutingPreferenceRepository"/> 实现。
/// - 文件路径：默认 <c>{Config}/uploader-routing-prefs.json</c>，可通过环境变量 <c>DOWNKYI_UPLOADER_ROUTING_PREFS_PATH</c> 覆盖。
/// - 原子写入：写临时文件 + rename。
/// - 线程安全：实例内 lock 序列化 Load+Save。
/// </summary>
public sealed class FileUploaderRoutingPreferenceRepository : IUploaderRoutingPreferenceRepository
{
    private const string DefaultRelativePath = "uploader-routing-prefs.json";
    private const string EnvironmentVariableOverride = "DOWNKYI_UPLOADER_ROUTING_PREFS_PATH";
    private const string TempFileSuffix = ".tmp";

    private readonly object _gate = new();
    private readonly ILogger _logger;
    private readonly Func<string> _filePathProvider;

    public FileUploaderRoutingPreferenceRepository()
        : this(ResolveDefaultFilePath, NullLogger.Instance)
    {
    }

    public FileUploaderRoutingPreferenceRepository(ILogger logger)
        : this(ResolveDefaultFilePath, logger)
    {
    }

    /// <summary>
    /// 测试或自定义路径场景使用：传入一个返回绝对路径的函数。
    /// </summary>
    public FileUploaderRoutingPreferenceRepository(Func<string> filePathProvider)
        : this(filePathProvider, NullLogger.Instance)
    {
    }

    public FileUploaderRoutingPreferenceRepository(Func<string> filePathProvider, ILogger logger)
    {
        _filePathProvider = filePathProvider ?? throw new ArgumentNullException(nameof(filePathProvider));
        _logger = logger ?? NullLogger.Instance;
    }

    public string FilePath => _filePathProvider();

    public UploaderRoutingPreference Load()
    {
        lock (_gate)
        {
            return LoadUnsafe();
        }
    }

    public void Save(UploaderRoutingPreference preference)
    {
        ArgumentNullException.ThrowIfNull(preference);

        lock (_gate)
        {
            WriteAtomically(preference);
        }
    }

    private UploaderRoutingPreference LoadUnsafe()
    {
        var path = FilePath;

        if (!File.Exists(path))
        {
            return UploaderRoutingPreference.Default;
        }

        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return UploaderRoutingPreference.Default;
            }

            var payload = JsonConvert.DeserializeObject<PreferenceFilePayload>(json);
            if (payload == null)
            {
                return UploaderRoutingPreference.Default;
            }

            // 非法枚举值回退到默认值（不抛异常）
            var strategy = Enum.IsDefined(payload.LastStrategy)
                ? payload.LastStrategy
                : UploaderRoutingPreference.Default.LastStrategy;

            return new UploaderRoutingPreference(
                LastStrategy: strategy,
                LastCustomFolder: payload.LastCustomFolder ?? string.Empty,
                LastPersistAlias: payload.LastPersistAlias);
        }
        catch (JsonException ex)
        {
            _logger.LogWarningMessage(
                $"Uploader routing preference file at {path} is corrupt. Falling back to defaults.",
                ex);
            return UploaderRoutingPreference.Default;
        }
        catch (IOException ex)
        {
            _logger.LogWarningMessage(
                $"Failed to read uploader routing preference file at {path}. Falling back to defaults.",
                ex);
            return UploaderRoutingPreference.Default;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarningMessage(
                $"Access denied when reading uploader routing preference file at {path}. Falling back to defaults.",
                ex);
            return UploaderRoutingPreference.Default;
        }
    }

    private void WriteAtomically(UploaderRoutingPreference preference)
    {
        var path = FilePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new PreferenceFilePayload
        {
            LastStrategy = preference.LastStrategy,
            LastCustomFolder = preference.LastCustomFolder ?? string.Empty,
            LastPersistAlias = preference.LastPersistAlias,
        };

        var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
        var tempPath = path + TempFileSuffix;

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

    private sealed class PreferenceFilePayload
    {
        [JsonProperty("lastStrategy")]
        public UploaderRoutingStrategy LastStrategy { get; set; }

        [JsonProperty("lastCustomFolder")]
        public string? LastCustomFolder { get; set; }

        [JsonProperty("lastPersistAlias")]
        public bool LastPersistAlias { get; set; }
    }
}
