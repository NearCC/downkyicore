namespace DownKyi.Desktop.Services.Uploader;

/// <summary>
/// 下载对话框中的子目录路由策略。
/// </summary>
public enum UploaderRoutingStrategy
{
    /// <summary>
    /// 用户输入任意子目录名（不查别名）。
    /// </summary>
    Custom = 0,

    /// <summary>
    /// 按视频主 UP 主（Owner）解析别名。
    /// </summary>
    ByUploader = 1,
}
