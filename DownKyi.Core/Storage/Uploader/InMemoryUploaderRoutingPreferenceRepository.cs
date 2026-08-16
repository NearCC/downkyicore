namespace DownKyi.Core.Storage.Uploader;

/// <summary>
/// 进程内的 <see cref="IUploaderRoutingPreferenceRepository"/> 实现，仅用于测试。
/// 不会触碰磁盘；<see cref="FilePath"/> 返回一个稳定的标记字符串而不是真实路径。
/// </summary>
public sealed class InMemoryUploaderRoutingPreferenceRepository : IUploaderRoutingPreferenceRepository
{
    private readonly object _gate = new();
    private UploaderRoutingPreference _preference = UploaderRoutingPreference.Default;

    public InMemoryUploaderRoutingPreferenceRepository(string? filePath = null)
    {
        FilePath = filePath ?? $"<in-memory>:{Guid.NewGuid():N}";
    }

    public string FilePath { get; }

    public UploaderRoutingPreference Load()
    {
        lock (_gate)
        {
            return _preference;
        }
    }

    public void Save(UploaderRoutingPreference preference)
    {
        ArgumentNullException.ThrowIfNull(preference);

        lock (_gate)
        {
            _preference = preference;
        }
    }
}
