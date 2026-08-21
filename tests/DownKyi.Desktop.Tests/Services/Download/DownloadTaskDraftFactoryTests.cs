using System.Collections.Generic;
using System.Collections.Immutable;
using DownKyi.Core.BiliApi.Models;
using DownKyi.Core.BiliApi.VideoStream.Models;
using DownKyi.Core.FileName;
using DownKyi.Core.Settings;
using DownKyi.Presentation;
using DownKyi.Services.Download;

namespace DownKyi.Desktop.Tests.Services.Download;

public sealed class DownloadTaskDraftFactoryTests
{
    private const string RootDirectory = @"D:\BilibiliDownload";

    private static ApplicationSettings BuildSettings()
    {
        return new ApplicationSettings(
            Video: new VideoApplicationSettings(
                VideoCodecs: 7,
                Quality: 120,
                AudioQuality: 30280,
                VideoParseType: 0,
                IsTranscodingFlvToMp4: AllowStatus.Yes,
                IsTranscodingAacToMp3: AllowStatus.Yes,
                FfmpegHardwareAcceleration: FfmpegHardwareAcceleration.Auto,
                FfmpegMaxParallelJobs: 1,
                SaveVideoRootPath: RootDirectory,
                HistoryVideoRootPaths: ImmutableArray<string>.Empty,
                IsUseSaveVideoRootPath: AllowStatus.Yes,
                Content: new VideoContentApplicationSettings(
                    true, true, true, true, true, false),
                FileNameParts: ImmutableArray.Create(
                    FileNamePart.MainTitle,
                    FileNamePart.Hyphen,
                    FileNamePart.PageTitle),
                FileNamePartTimeFormat: "yyyy-MM-dd",
                OrderFormat: OrderFormat.Natural),
            Basic: new BasicApplicationSettings(
                ThemeMode.Default,
                AfterDownloadOperation.None,
                AllowStatus.Yes,
                AllowStatus.No,
                ParseScope.None,
                AllowStatus.No,
                DownloadFinishedSort.DownloadAsc,
                RepeatDownloadStrategy.Ask,
                false),
            Danmaku: default!,
            About: default!,
            Network: default!,
            User: default!,
            Window: default!,
            SchemaVersion: 1);
    }

    private static VideoInfoView BuildVideo()
    {
        return new VideoInfoView
        {
            Title = "测试视频",
            VideoZone = "知识>科学",
            TypeId = 0,
        };
    }

    private static VideoSection BuildSection(int pageCount = 1)
    {
        var pages = new List<VideoPage>();
        for (var i = 0; i < pageCount; i++)
        {
            pages.Add(new VideoPage
            {
                Name = $"第{i + 1}集",
                AudioQualityFormat = "高清 1080P",
                PublishTime = "2024-01-01",
                Avid = 1,
                Bvid = "BV1xx",
                Cid = 100 + i,
                Order = i + 1,
                Owner = new VideoOwner
                {
                    Name = "UP主A",
                    Face = string.Empty,
                    Mid = 12345,
                },
            });
        }

        return new VideoSection
        {
            Id = 0,
            Title = "默认合集",
            VideoPages = pages,
        };
    }

    private static VideoQuality BuildQuality()
    {
        return new VideoQuality
        {
            Quality = 120,
            QualityFormat = "1080P 高清",
            SelectedVideoCodec = "AVC",
        };
    }

    [Fact]
    public void BuildFilePathWithNullSubFolderMatchesBaseline()
    {
        var path = DownloadTaskDraftFactory.BuildFilePath(
            directory: RootDirectory,
            video: BuildVideo(),
            section: BuildSection(),
            sectionCount: 1,
            page: BuildSection().VideoPages[0],
            videoQuality: BuildQuality(),
            settings: BuildSettings(),
            subFolder: null);

        Assert.StartsWith(RootDirectory, path, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.DirectorySeparatorChar + "null", path, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFilePathWithEmptySubFolderMatchesBaseline()
    {
        var pathWithEmpty = DownloadTaskDraftFactory.BuildFilePath(
            directory: RootDirectory,
            video: BuildVideo(),
            section: BuildSection(),
            sectionCount: 1,
            page: BuildSection().VideoPages[0],
            videoQuality: BuildQuality(),
            settings: BuildSettings(),
            subFolder: string.Empty);

        var pathWithNull = DownloadTaskDraftFactory.BuildFilePath(
            directory: RootDirectory,
            video: BuildVideo(),
            section: BuildSection(),
            sectionCount: 1,
            page: BuildSection().VideoPages[0],
            videoQuality: BuildQuality(),
            settings: BuildSettings(),
            subFolder: null);

        Assert.Equal(pathWithNull, pathWithEmpty);
    }

    [Fact]
    public void BuildFilePathWithWhitespaceSubFolderMatchesBaseline()
    {
        var pathWithWs = DownloadTaskDraftFactory.BuildFilePath(
            directory: RootDirectory,
            video: BuildVideo(),
            section: BuildSection(),
            sectionCount: 1,
            page: BuildSection().VideoPages[0],
            videoQuality: BuildQuality(),
            settings: BuildSettings(),
            subFolder: "   ");

        var pathWithNull = DownloadTaskDraftFactory.BuildFilePath(
            directory: RootDirectory,
            video: BuildVideo(),
            section: BuildSection(),
            sectionCount: 1,
            page: BuildSection().VideoPages[0],
            videoQuality: BuildQuality(),
            settings: BuildSettings(),
            subFolder: null);

        Assert.Equal(pathWithNull, pathWithWs);
    }

    [Fact]
    public void BuildFilePathWithSubFolderInsertsSegmentBeforeTemplate()
    {
        var subFolder = "博主A";

        var pathWithSub = DownloadTaskDraftFactory.BuildFilePath(
            directory: RootDirectory,
            video: BuildVideo(),
            section: BuildSection(),
            sectionCount: 1,
            page: BuildSection().VideoPages[0],
            videoQuality: BuildQuality(),
            settings: BuildSettings(),
            subFolder: subFolder);

        var pathWithoutSub = DownloadTaskDraftFactory.BuildFilePath(
            directory: RootDirectory,
            video: BuildVideo(),
            section: BuildSection(),
            sectionCount: 1,
            page: BuildSection().VideoPages[0],
            videoQuality: BuildQuality(),
            settings: BuildSettings(),
            subFolder: null);

        var prefixWithSub = Path.Combine(RootDirectory, subFolder) + Path.DirectorySeparatorChar;
        Assert.StartsWith(prefixWithSub, pathWithSub, StringComparison.OrdinalIgnoreCase);

        // 去掉 subFolder 段后，剩下的部分应该和没传 subFolder 的一样。
        var pathWithoutPrefix = Path.Combine(RootDirectory, subFolder) + Path.DirectorySeparatorChar;
        Assert.Equal(
            pathWithoutSub.AsSpan(RootDirectory.Length + 1),
            pathWithSub.AsSpan(pathWithoutPrefix.Length));
    }

    [Fact]
    public void BuildFilePathWithTrailingSlashTrimsIt()
    {
        var subFolder = "博主A/";

        var path = DownloadTaskDraftFactory.BuildFilePath(
            directory: RootDirectory,
            video: BuildVideo(),
            section: BuildSection(),
            sectionCount: 1,
            page: BuildSection().VideoPages[0],
            videoQuality: BuildQuality(),
            settings: BuildSettings(),
            subFolder: subFolder);

        var doubleSep = new string(Path.DirectorySeparatorChar, 2);
        Assert.DoesNotContain(doubleSep, path, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFilePathWithLeadingAndTrailingWhitespaceTrimsBoth()
    {
        var path = DownloadTaskDraftFactory.BuildFilePath(
            directory: RootDirectory,
            video: BuildVideo(),
            section: BuildSection(),
            sectionCount: 1,
            page: BuildSection().VideoPages[0],
            videoQuality: BuildQuality(),
            settings: BuildSettings(),
            subFolder: "  博主A  ");

        var normalized = Path.Combine(RootDirectory, "博主A");
        Assert.StartsWith(normalized, path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFilePathWithMultiSectionAndSubFolderWorks()
    {
        var path = DownloadTaskDraftFactory.BuildFilePath(
            directory: RootDirectory,
            video: BuildVideo(),
            section: BuildSection(pageCount: 3),
            sectionCount: 3,
            page: BuildSection(pageCount: 3).VideoPages[1],
            videoQuality: BuildQuality(),
            settings: BuildSettings(),
            subFolder: "博主B");

        Assert.StartsWith(
            Path.Combine(RootDirectory, "博主B") + Path.DirectorySeparatorChar,
            path,
            StringComparison.OrdinalIgnoreCase);
    }
}