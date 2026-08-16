namespace DownKyi.Core.Storage.Uploader;

/// <summary>
/// 路由偏好持久化与查询抽象。
/// 偏好是单值对象（不是集合），不存在 Upsert 之类的合并操作。
/// 所有方法线程安全。
/// </summary>
public interface IUploaderRoutingPreferenceRepository
{
    /// <summary>
    /// 当前偏好文件的绝对路径（用于诊断、调试、设置页展示）。
    /// </summary>
    string FilePath { get; }

    /// <summary>
    /// 读取偏好。
    /// - 文件不存在 → <see cref="UploaderRoutingPreference.Default"/>
    /// - 文件存在但 JSON 损坏 → <see cref="UploaderRoutingPreference.Default"/> + 错误日志
    /// </summary>
    UploaderRoutingPreference Load();

    /// <summary>
    /// 写入偏好。原子写入（临时文件 + rename）。
    /// </summary>
    void Save(UploaderRoutingPreference preference);
}
