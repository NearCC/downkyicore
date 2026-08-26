using System;
using System.Collections.Generic;
using System.IO;
using DownKyi.Core.Storage.Uploader;
using Newtonsoft.Json;

namespace DownKyi.Desktop.Services.Uploader;

/// <summary>
/// 路由层：从弹窗 result 字典 + 当前视频 UP 主信息 + 别名表，
/// 调用 <see cref="DownloadSubFolderResolver"/> 算出最终子目录。
/// 不读不写别名表（写表是 UI「更新映射」按钮的事）。
/// </summary>
internal sealed class SubFolderRouteResolver
{
    private readonly IUploaderAliasRepository _aliasRepository;

    public SubFolderRouteResolver(IUploaderAliasRepository aliasRepository)
    {
        _aliasRepository = aliasRepository ?? throw new ArgumentNullException(nameof(aliasRepository));
    }

    public string? ResolveFromDialogResult(
        IReadOnlyDictionary<string, object?> parameters,
        long ownerMid,
        string ownerName)
    {
        // 弹窗里用户选择的策略；缺省视为 ByUploader（与 VM 默认一致）。
        var strategy = parameters.TryGetValue("strategy", out var strategyValue)
            && strategyValue is UploaderRoutingStrategy s
            ? s
            : UploaderRoutingStrategy.ByUploader;

        // 弹窗文本框里用户填的最终值（空白视为未填）。
        var rawSubFolder = parameters.TryGetValue("subFolder", out var value) ? value as string : null;
        var userFolder = string.IsNullOrWhiteSpace(rawSubFolder) ? null : rawSubFolder.Trim();

        var aliases = LoadAliasesBestEffort();

        var inputs = new ResolverInputs(
            Strategy: strategy,
            CustomFolder: userFolder,
            ResolvedFolderName: userFolder,
            OwnerMid: ownerMid,
            OwnerName: ownerName ?? string.Empty,
            Aliases: aliases);
        var result = DownloadSubFolderResolver.Resolve(inputs);
        return result.SubFolder.Length == 0 ? null : result.SubFolder;
    }

    private IReadOnlyDictionary<long, string> LoadAliasesBestEffort()
    {
        try
        {
            return _aliasRepository.Load();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // 别名表损坏也不影响下载主流程，吞掉异常返回空表。
            return new Dictionary<long, string>();
        }
    }
}
