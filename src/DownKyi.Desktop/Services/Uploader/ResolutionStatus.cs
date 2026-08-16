namespace DownKyi.Desktop.Services.Uploader;

/// <summary>
/// Resolver 算出的"状态"。用于 UI 显示与日志，不影响路径。
/// </summary>
public enum ResolutionStatus
{
    /// <summary>
    /// 没有可用 UP 主（mid ≤ 0）。
    /// </summary>
    NoUploader,

    /// <summary>
    /// 在别名表里命中现有映射。
    /// </summary>
    MatchedAlias,

    /// <summary>
    /// 没有命中，将要新建（仅当调用方允许持久化时才会真正写入）。
    /// </summary>
    WillCreateAlias,
}
