using DownKyi.Core.Storage.Uploader;

namespace DownKyi.Desktop.Services.Uploader;

/// <summary>
/// 纯函数：根据路由策略 + 视频 UP 主信息 + 别名表，算出"应该用哪个子目录"与"状态"。
/// 不触碰文件系统、不调用 UI、不读写别名表——这些由调用方决定。
/// </summary>
public sealed class DownloadSubFolderResolver
{
    /// <summary>
    /// 解析单个任务的子目录路由。
    /// </summary>
    public static ResolverResult Resolve(ResolverInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        return inputs.Strategy switch
        {
            UploaderRoutingStrategy.Custom => ResolveCustom(inputs),
            UploaderRoutingStrategy.ByUploader => ResolveByUploader(inputs),
            _ => throw new ArgumentOutOfRangeException(nameof(inputs), inputs.Strategy, "Unknown routing strategy."),
        };
    }

    private static ResolverResult ResolveCustom(ResolverInputs inputs)
    {
        var folder = inputs.CustomFolder?.Trim() ?? string.Empty;
        // Custom 不查别名表，Preview 始终为 null。
        return new ResolverResult(Preview: null, SubFolder: folder);
    }

    private static ResolverResult ResolveByUploader(ResolverInputs inputs)
    {
        var mid = inputs.OwnerMid ?? -1;

        // 用户在文本框里有值（含 trim 后非空）—— 让 UI 说了算；状态仍按 mid 正确显示。
        if (!string.IsNullOrWhiteSpace(inputs.ResolvedFolderName))
        {
            var preview = ComputePreview(mid, inputs.OwnerName ?? string.Empty, inputs.Aliases);
            return new ResolverResult(preview, inputs.ResolvedFolderName!.Trim());
        }

        if (mid <= 0)
        {
            return new ResolverResult(
                Preview: new ResolverPreview(Mid: 0, InitialFolderName: string.Empty, Status: ResolutionStatus.NoUploader),
                SubFolder: string.Empty);
        }

        // 否则按 resolver 自动解析。
        if (inputs.Aliases.TryGetValue(mid, out var aliasName))
        {
            return new ResolverResult(
                Preview: new ResolverPreview(Mid: mid, InitialFolderName: aliasName, Status: ResolutionStatus.MatchedAlias),
                SubFolder: aliasName);
        }

        var ownerName = inputs.OwnerName ?? string.Empty;
        return new ResolverResult(
            Preview: new ResolverPreview(Mid: mid, InitialFolderName: ownerName, Status: ResolutionStatus.WillCreateAlias),
            SubFolder: ownerName);
    }

    /// <summary>
    /// 计算预览（用于"用户编辑了文本框"的场景）：仍然查映射以正确显示状态，
    /// 但 SubFolder 由调用方（UI 传入的 ResolvedFolderName）决定。
    /// </summary>
    private static ResolverPreview ComputePreview(
        long mid,
        string ownerName,
        IReadOnlyDictionary<long, string> aliases)
    {
        if (mid <= 0)
        {
            return new ResolverPreview(Mid: 0, InitialFolderName: string.Empty, Status: ResolutionStatus.NoUploader);
        }

        if (aliases.TryGetValue(mid, out var aliasName))
        {
            return new ResolverPreview(Mid: mid, InitialFolderName: aliasName, Status: ResolutionStatus.MatchedAlias);
        }

        return new ResolverPreview(Mid: mid, InitialFolderName: ownerName, Status: ResolutionStatus.WillCreateAlias);
    }
}
