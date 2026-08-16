namespace DownKyi.Desktop.Services.Uploader;

/// <summary>
/// Resolver 的输出。
/// </summary>
/// <param name="Preview">
/// Custom 策略下为 null。
/// ByUploader 策略下总是非 null（即便状态是 NoUploader）。
/// </param>
/// <param name="SubFolder">最终写入路径的子目录（可能为空）。</param>
public sealed record ResolverResult(
    ResolverPreview? Preview,
    string SubFolder);
