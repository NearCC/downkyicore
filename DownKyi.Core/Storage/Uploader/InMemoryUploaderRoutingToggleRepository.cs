namespace DownKyi.Core.Storage.Uploader;

/// <summary>
/// 进程内的 <see cref="IUploaderRoutingToggleRepository"/> 实现，仅用于测试。
/// 不会触碰磁盘；<see cref="FilePath"/> 返回一个稳定的标记字符串而不是真实路径。
/// </summary>
public sealed class InMemoryUploaderRoutingToggleRepository : IUploaderRoutingToggleRepository
{
    private readonly object _gate = new();

    public InMemoryUploaderRoutingToggleRepository(UploaderRoutingToggle? initial = null)
    {
        FilePath = $"<in-memory-toggle>:{Guid.NewGuid():N}";
        Current = initial ?? UploaderRoutingToggle.Default;
    }

    public string FilePath { get; }

    public UploaderRoutingToggle Current { get; private set; }

    public UploaderRoutingToggle Load()
    {
        lock (_gate)
        {
            return Current;
        }
    }

    public void Save(UploaderRoutingToggle toggle)
    {
        ArgumentNullException.ThrowIfNull(toggle);
        lock (_gate)
        {
            Current = toggle;
        }
    }
}
