namespace DownKyi.Core.Storage.Uploader;

/// <summary>
/// UP 主别名映射表的持久化与查询抽象。
/// 所有方法都是线程安全的。
/// </summary>
public interface IUploaderAliasRepository
{
    /// <summary>
    /// 当前别名文件的绝对路径（用于诊断、调试、导出/导入 UI）。
    /// </summary>
    string FilePath { get; }

    /// <summary>
    /// 读取整张别名表。
    /// - 文件不存在 → 空字典
    /// - 文件存在但 JSON 损坏 → 空字典（错误通过 <see cref="Microsoft.Extensions.Logging.ILogger"/> 记录）
    /// </summary>
    IReadOnlyDictionary<long, string> Load();

    /// <summary>
    /// 整体覆盖写入。用于导入场景。
    /// 内部使用临时文件 + rename 实现原子写入，避免中途崩溃损坏现有文件。
    /// </summary>
    void Save(IReadOnlyDictionary<long, string> aliases);

    /// <summary>
    /// 新增或覆盖单条映射。
    /// 内部 Load → 合并 → 原子写回；并发调用是安全的（同一实例内串行化）。
    /// </summary>
    /// <param name="mid">UP 主 mid（必须 > 0）。</param>
    /// <param name="folderName">文件夹名（允许空白，调用方负责消毒）。</param>
    void Upsert(long mid, string folderName);

    /// <summary>
    /// 删除单条映射。mid 不存在时静默无操作（不抛异常）。
    /// </summary>
    /// <param name="mid">要移除的 UP 主 mid。</param>
    void Remove(long mid);
}
