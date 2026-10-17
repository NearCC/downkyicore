namespace DownKyi.Core.Storage.Uploader;

/// <summary>
/// 主开关：是否启用"按 UP 主归类到子目录"功能。
///
/// 与 <see cref="UploaderRoutingPreference"/> 的区别：
/// - 本类只承载一个布尔开关，用于在调用下载入口时分支（启用时打开新弹窗、调用 resolver）。
/// - <see cref="UploaderRoutingPreference"/> 承载弹窗内部的细粒度状态（策略、自定义文本等），新弹窗打开时读取。
/// </summary>
public sealed record UploaderRoutingToggle(bool IsEnabled)
{
    /// <summary>
    /// 当文件不存在或解析失败时使用的默认值。默认关闭，保证现有用户行为零变化。
    /// </summary>
    public static UploaderRoutingToggle Default { get; } = new(IsEnabled: false);
}
