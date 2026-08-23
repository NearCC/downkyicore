using System.IO;
using DownKyi.Application.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

namespace DownKyi.Core.Storage.Uploader;

/// <summary>
/// 基于 JSON 文件的 <see cref="IUploaderRoutingToggleRepository"/> 实现。
/// - 文件路径：默认 <c>{Config}/uploader-routing-toggle.json</c>，可通过环境变量 <c>DOWNKYI_UPLOADER_TOGGLE_PATH</c> 覆盖。
/// - 原子写入：写临时文件 + rename。
/// - 线程安全：实例内 lock 序列化 Load+Save。
/// </summary>
public sealed class FileUploaderRoutingToggleRepository : IUploaderRoutingToggleRepository
{
    private const string DefaultRelativePath = "uploader-routing-toggle.json";
    private const string EnvironmentVariableOverride = "DOWNKYI_UPLOADER_TOGGLE_PATH";
    private const string TempFileSuffix = ".tmp";

    private readonly object _gate = new();
    private readonly ILogger _logger;
    private readonly Func<string> _filePathProvider;

    public FileUploaderRoutingToggleRepository()
        : this(ResolveDefaultFilePath, NullLogger.Instance)
    {
    }

    public FileUploaderRoutingToggleRepository(ILogger logger)
        : this(ResolveDefaultFilePath, logger)
    {
    }

    /// <summary>
    /// 测试或自定义路径场景使用：传入一个返回绝对路径的函数。
    /// </summary>
    public FileUploaderRoutingToggleRepository(Func<string> filePathProvider)
        : this(filePathProvider, NullLogger.Instance)
    {
    }

    public FileUploaderRoutingToggleRepository(Func<string> filePathProvider, ILogger logger)
    {
        _filePathProvider = filePathProvider ?? throw new ArgumentNullException(nameof(filePathProvider));
        _logger = logger ?? NullLogger.Instance;
    }

    public string FilePath => _filePathProvider();

    public UploaderRoutingToggle Load()
    {
        lock (_gate)
        {
            var path = FilePath;

            if (!File.Exists(path))
            {
                return UploaderRoutingToggle.Default;
            }

            try
            {
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return UploaderRoutingToggle.Default;
                }

                var payload = JsonConvert.DeserializeObject<ToggleFilePayload>(json);
                if (payload == null)
                {
                    return UploaderRoutingToggle.Default;
                }

                return new UploaderRoutingToggle(IsEnabled: payload.IsEnabled);
            }
            catch (JsonException ex)
            {
                _logger.LogWarningMessage(
                    $"Uploader routing toggle file at {path} is corrupt. Falling back to defaults.",
                    ex);
                return UploaderRoutingToggle.Default;
            }
            catch (IOException ex)
            {
                _logger.LogWarningMessage(
                    $"Failed to read uploader routing toggle file at {path}. Falling back to defaults.",
                    ex);
                return UploaderRoutingToggle.Default;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarningMessage(
                    $"Access denied when reading uploader routing toggle file at {path}. Falling back to defaults.",
                    ex);
                return UploaderRoutingToggle.Default;
            }
        }
    }

    public void Save(UploaderRoutingToggle toggle)
    {
        ArgumentNullException.ThrowIfNull(toggle);

        lock (_gate)
        {
            var path = FilePath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var payload = new ToggleFilePayload
            {
                IsEnabled = toggle.IsEnabled,
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

    private sealed class ToggleFilePayload
    {
        [JsonProperty("isEnabled")]
        public bool IsEnabled { get; set; }
    }
}
