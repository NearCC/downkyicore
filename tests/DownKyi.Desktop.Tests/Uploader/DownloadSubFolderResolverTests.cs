using DownKyi.Desktop.Services.Uploader;

namespace DownKyi.Desktop.Tests.Uploader;

public sealed class DownloadSubFolderResolverTests
{
    private static Dictionary<long, string> Aliases(
        params (long Mid, string Name)[] entries)
    {
        var dict = new Dictionary<long, string>();
        foreach (var (mid, name) in entries)
        {
            dict[mid] = name;
        }
        return dict;
    }

    private static Dictionary<long, string> OneAlias(long mid, string name)
    {
        return new Dictionary<long, string> { [mid] = name };
    }

    private static ResolverInputs CustomInputs(
        string? customFolder,
        Dictionary<long, string>? aliases = null)
    {
        return new ResolverInputs(
            Strategy: UploaderRoutingStrategy.Custom,
            CustomFolder: customFolder,
            ResolvedFolderName: null,
            OwnerMid: 12345,
            OwnerName: "博主A",
            Aliases: aliases ?? Aliases());
    }

    private static ResolverInputs ByUploaderInputs(
        long? ownerMid,
        string? ownerName,
        Dictionary<long, string>? aliases = null,
        string? resolvedFolderName = null)
    {
        return new ResolverInputs(
            Strategy: UploaderRoutingStrategy.ByUploader,
            CustomFolder: null,
            ResolvedFolderName: resolvedFolderName,
            OwnerMid: ownerMid,
            OwnerName: ownerName,
            Aliases: aliases ?? Aliases());
    }

    // ===== Custom 策略 =====

    [Fact]
    public void CustomWithNonEmptyFolderReturnsTrimmed()
    {
        var result = DownloadSubFolderResolver.Resolve(CustomInputs("  我的文件夹  "));

        Assert.Null(result.Preview);
        Assert.Equal("我的文件夹", result.SubFolder);
    }

    [Fact]
    public void CustomWithEmptyFolderReturnsEmptySubFolder()
    {
        var result = DownloadSubFolderResolver.Resolve(CustomInputs(""));

        Assert.Null(result.Preview);
        Assert.Equal(string.Empty, result.SubFolder);
    }

    [Fact]
    public void CustomWithNullFolderReturnsEmptySubFolder()
    {
        var result = DownloadSubFolderResolver.Resolve(CustomInputs(null));

        Assert.Null(result.Preview);
        Assert.Equal(string.Empty, result.SubFolder);
    }

    [Fact]
    public void CustomWithWhitespaceOnlyFolderReturnsEmptySubFolder()
    {
        var result = DownloadSubFolderResolver.Resolve(CustomInputs("   \t\n  "));

        Assert.Null(result.Preview);
        Assert.Equal(string.Empty, result.SubFolder);
    }

    [Fact]
    public void CustomIgnoresAliasTable()
    {
        var aliases = Aliases((12345, "已存在的别名"));

        var result = DownloadSubFolderResolver.Resolve(CustomInputs("我的文件夹", aliases));

        Assert.Null(result.Preview);
        Assert.Equal("我的文件夹", result.SubFolder);
    }

    // ===== ByUploader 策略：自动解析（无 ResolvedFolderName） =====

    [Fact]
    public void ByUploaderMatchedAliasUsesAliasName()
    {
        var aliases = Aliases((12345, "博主A-官方"));

        var result = DownloadSubFolderResolver.Resolve(ByUploaderInputs(12345, "博主A", aliases));

        Assert.NotNull(result.Preview);
        Assert.Equal(12345, result.Preview!.Mid);
        Assert.Equal("博主A-官方", result.Preview.InitialFolderName);
        Assert.Equal(ResolutionStatus.MatchedAlias, result.Preview.Status);
        Assert.Equal("博主A-官方", result.SubFolder);
    }

    [Fact]
    public void ByUploaderUnmatchedAliasUsesOwnerNameAndWillCreate()
    {
        var aliases = Aliases((99999, "别人的别名"));

        var result = DownloadSubFolderResolver.Resolve(ByUploaderInputs(12345, "博主A", aliases));

        Assert.NotNull(result.Preview);
        Assert.Equal(12345, result.Preview!.Mid);
        Assert.Equal("博主A", result.Preview.InitialFolderName);
        Assert.Equal(ResolutionStatus.WillCreateAlias, result.Preview.Status);
        Assert.Equal("博主A", result.SubFolder);
    }

    [Fact]
    public void ByUploaderEmptyAliasTableUsesOwnerNameAndWillCreate()
    {
        var result = DownloadSubFolderResolver.Resolve(ByUploaderInputs(12345, "博主A", Aliases()));

        Assert.NotNull(result.Preview);
        Assert.Equal(ResolutionStatus.WillCreateAlias, result.Preview!.Status);
        Assert.Equal("博主A", result.SubFolder);
    }

    [Fact]
    public void ByUploaderOwnerMidZeroReturnsNoUploader()
    {
        var aliases = Aliases((12345, "已存在的别名"));

        var result = DownloadSubFolderResolver.Resolve(ByUploaderInputs(0, "博主A", aliases));

        Assert.NotNull(result.Preview);
        Assert.Equal(ResolutionStatus.NoUploader, result.Preview!.Status);
        Assert.Equal(string.Empty, result.SubFolder);
    }

    [Fact]
    public void ByUploaderOwnerMidNegativeReturnsNoUploader()
    {
        var result = DownloadSubFolderResolver.Resolve(ByUploaderInputs(-1, "博主A"));

        Assert.NotNull(result.Preview);
        Assert.Equal(ResolutionStatus.NoUploader, result.Preview!.Status);
        Assert.Equal(string.Empty, result.SubFolder);
    }

    [Fact]
    public void ByUploaderOwnerMidNullReturnsNoUploader()
    {
        var result = DownloadSubFolderResolver.Resolve(ByUploaderInputs(null, "博主A"));

        Assert.NotNull(result.Preview);
        Assert.Equal(ResolutionStatus.NoUploader, result.Preview!.Status);
        Assert.Equal(string.Empty, result.SubFolder);
    }

    [Fact]
    public void ByUploaderOwnerNameNullWhenUnmatchedReturnsEmptySubFolder()
    {
        var result = DownloadSubFolderResolver.Resolve(ByUploaderInputs(12345, null));

        Assert.NotNull(result.Preview);
        Assert.Equal(ResolutionStatus.WillCreateAlias, result.Preview!.Status);
        Assert.Equal(string.Empty, result.SubFolder);
    }

    // ===== ByUploader 策略：用户编辑了文本框（ResolvedFolderName 非空） =====

    [Fact]
    public void ByUploaderResolvedFolderNameOverridesAliasName()
    {
        var aliases = Aliases((12345, "博主A-官方"));

        var result = DownloadSubFolderResolver.Resolve(ByUploaderInputs(
            ownerMid: 12345,
            ownerName: "博主A",
            aliases: aliases,
            resolvedFolderName: "用户改的名"));

        Assert.NotNull(result.Preview);
        Assert.Equal(ResolutionStatus.MatchedAlias, result.Preview!.Status);
        Assert.Equal("博主A-官方", result.Preview.InitialFolderName);
        Assert.Equal("用户改的名", result.SubFolder);
    }

    [Fact]
    public void ByUploaderResolvedFolderNameOverridesOwnerNameWhenUnmatched()
    {
        var result = DownloadSubFolderResolver.Resolve(ByUploaderInputs(
            ownerMid: 12345,
            ownerName: "博主A",
            aliases: Aliases(),
            resolvedFolderName: "用户改的名"));

        Assert.NotNull(result.Preview);
        Assert.Equal(ResolutionStatus.WillCreateAlias, result.Preview!.Status);
        Assert.Equal("博主A", result.Preview.InitialFolderName);
        Assert.Equal("用户改的名", result.SubFolder);
    }

    [Fact]
    public void ByUploaderResolvedFolderNameIsTrimmed()
    {
        var result = DownloadSubFolderResolver.Resolve(ByUploaderInputs(
            ownerMid: 12345,
            ownerName: "博主A",
            resolvedFolderName: "  用户改的名  "));

        Assert.Equal("用户改的名", result.SubFolder);
    }

    [Fact]
    public void ByUploaderEmptyResolvedFolderNameFallsThroughToAutoResolve()
    {
        var aliases = Aliases((12345, "博主A-官方"));

        var result = DownloadSubFolderResolver.Resolve(ByUploaderInputs(
            ownerMid: 12345,
            ownerName: "博主A",
            aliases: aliases,
            resolvedFolderName: ""));

        Assert.NotNull(result.Preview);
        Assert.Equal(ResolutionStatus.MatchedAlias, result.Preview!.Status);
        Assert.Equal("博主A-官方", result.SubFolder);
    }

    [Fact]
    public void ByUploaderWhitespaceOnlyResolvedFolderNameFallsThroughToAutoResolve()
    {
        var aliases = Aliases((12345, "博主A-官方"));

        var result = DownloadSubFolderResolver.Resolve(ByUploaderInputs(
            ownerMid: 12345,
            ownerName: "博主A",
            aliases: aliases,
            resolvedFolderName: "   "));

        Assert.Equal("博主A-官方", result.SubFolder);
    }

    [Fact]
    public void ByUploaderResolvedFolderNameStillProducesPreviewForNoUploader()
    {
        // 罕见情况：UI 有文本框但 OwnerMid ≤ 0。Preview 应为 NoUploader，SubFolder 为用户输入。
        var result = DownloadSubFolderResolver.Resolve(ByUploaderInputs(
            ownerMid: -1,
            ownerName: "博主A",
            resolvedFolderName: "用户输入的"));

        Assert.NotNull(result.Preview);
        Assert.Equal(ResolutionStatus.NoUploader, result.Preview!.Status);
        Assert.Equal("用户输入的", result.SubFolder);
    }

    // ===== 边界与健壮性 =====

    [Fact]
    public void NullInputsThrows()
    {
        Assert.Throws<ArgumentNullException>(() => DownloadSubFolderResolver.Resolve(null!));
    }

    [Fact]
    public void CustomStrategyDoesNotConsultOwnerFields()
    {
        var result = DownloadSubFolderResolver.Resolve(new ResolverInputs(
            Strategy: UploaderRoutingStrategy.Custom,
            CustomFolder: "我的文件夹",
            ResolvedFolderName: null,
            OwnerMid: null,
            OwnerName: null,
            Aliases: OneAlias(12345, "别名")));

        Assert.Null(result.Preview);
        Assert.Equal("我的文件夹", result.SubFolder);
    }

    [Fact]
    public void ResolverIsStatelessAcrossInvocations()
    {
        var aliases = Aliases((1, "first"));

        var first = DownloadSubFolderResolver.Resolve(ByUploaderInputs(1, "first", aliases));
        var second = DownloadSubFolderResolver.Resolve(ByUploaderInputs(2, "second", aliases));

        Assert.Equal("first", first.SubFolder);
        Assert.Equal("second", second.SubFolder);
        Assert.Equal(ResolutionStatus.MatchedAlias, first.Preview!.Status);
        Assert.Equal(ResolutionStatus.WillCreateAlias, second.Preview!.Status);
    }
}
