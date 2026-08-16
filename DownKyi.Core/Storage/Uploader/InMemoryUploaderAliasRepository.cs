namespace DownKyi.Core.Storage.Uploader;

/// <summary>
/// 进程内的 <see cref="IUploaderAliasRepository"/> 实现，仅用于测试。
/// 不会触碰磁盘；<see cref="FilePath"/> 返回一个稳定的标记字符串而不是真实路径。
/// </summary>
public sealed class InMemoryUploaderAliasRepository : IUploaderAliasRepository
{
    private readonly object _gate = new();

    public InMemoryUploaderAliasRepository(string? filePath = null)
    {
        FilePath = filePath ?? $"<in-memory>:{Guid.NewGuid():N}";
    }

    public string FilePath { get; }

    private Dictionary<long, string> Aliases { get; } = new();

    public IReadOnlyDictionary<long, string> Load()
    {
        lock (_gate)
        {
            return new Dictionary<long, string>(Aliases);
        }
    }

    public void Save(IReadOnlyDictionary<long, string> aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);

        lock (_gate)
        {
            Aliases.Clear();
            foreach (var kvp in aliases)
            {
                if (kvp.Key <= 0 || string.IsNullOrEmpty(kvp.Value))
                {
                    continue;
                }

                Aliases[kvp.Key] = kvp.Value;
            }
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
            Aliases[mid] = folderName;
        }
    }
}
