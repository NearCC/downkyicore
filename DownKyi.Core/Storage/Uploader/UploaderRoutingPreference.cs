namespace DownKyi.Core.Storage.Uploader;

/// <summary>
/// 用户上次的子目录路由选择 + 自定义文本 + 勾选项。
/// 在打开下载对话框时作为默认值。
/// </summary>
public sealed record UploaderRoutingPreference(
    UploaderRoutingStrategy LastStrategy,
    string LastCustomFolder,
    bool LastPersistAlias)
{
    /// <summary>
    /// 当文件不存在或解析失败时使用的默认值。
    /// </summary>
    public static UploaderRoutingPreference Default { get; } = new(
        LastStrategy: UploaderRoutingStrategy.ByUploader,
        LastCustomFolder: string.Empty,
        LastPersistAlias: true);
}
