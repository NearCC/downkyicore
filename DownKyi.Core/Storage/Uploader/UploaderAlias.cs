namespace DownKyi.Core.Storage.Uploader;

/// <summary>
/// 一个 UP 主 mid 与其文件夹名的映射条目。
/// </summary>
/// <param name="Mid">B 站 UP 主 ID（永不变）。</param>
/// <param name="FolderName">下载时使用的子目录名。</param>
public sealed record UploaderAlias(
    long Mid,
    string FolderName);
