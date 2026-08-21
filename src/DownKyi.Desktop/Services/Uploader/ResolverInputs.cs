using DownKyi.Core.Storage.Uploader;

namespace DownKyi.Desktop.Services.Uploader;

/// <summary>
/// Resolver 的输入。包含 UI 端的选择、视频的 UP 主信息，以及当前别名表。
/// </summary>
/// <param name="Strategy">路由策略（Custom / ByUploader）。</param>
/// <param name="CustomFolder">Custom 策略下用户输入的子目录名（允许空，空时落到根目录）。</param>
/// <param name="ResolvedFolderName">
/// ByUploader 策略下 UI 计算好的最终文件夹名（含用户编辑过的值）。
/// 为空时 resolver 自行按别名表 + OwnerName 解析。
/// </param>
/// <param name="OwnerMid">视频主 UP 主 mid。无 UP 主时为 null 或 ≤ 0。</param>
/// <param name="OwnerName">视频主 UP 主当前名字（用于未命中映射时的回退）。</param>
/// <param name="Aliases">当前别名表（mid → 文件夹名）。</param>
public sealed record ResolverInputs(
    UploaderRoutingStrategy Strategy,
    string? CustomFolder,
    string? ResolvedFolderName,
    long? OwnerMid,
    string? OwnerName,
    IReadOnlyDictionary<long, string> Aliases);
