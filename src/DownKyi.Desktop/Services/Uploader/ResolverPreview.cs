namespace DownKyi.Desktop.Services.Uploader;

/// <summary>
/// Resolver 给 UI 的预览信息。Custom 策略下为 null。
/// </summary>
/// <param name="Mid">关联的 UP 主 mid（&gt; 0）。</param>
/// <param name="InitialFolderName">初始文件夹名（别名或当前名字）。</param>
/// <param name="Status">命中映射 / 将要新建 / 无 UP 主。</param>
public sealed record ResolverPreview(
    long Mid,
    string InitialFolderName,
    ResolutionStatus Status);
