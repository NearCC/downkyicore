namespace DownKyi.Core.Storage.Uploader;

/// <summary>
/// 持久化"按 UP 主归类"功能的主开关状态。
/// 实现可以是文件、注册表、内存等任意后端。
/// </summary>
public interface IUploaderRoutingToggleRepository
{
    /// <summary>
    /// 加载当前开关状态。文件不存在或解析失败时，返回 <see cref="UploaderRoutingToggle.Default"/>。
    /// </summary>
    UploaderRoutingToggle Load();

    /// <summary>
    /// 原子写入开关状态。出现任何错误都应该向上抛出。
    /// </summary>
    void Save(UploaderRoutingToggle toggle);
}
