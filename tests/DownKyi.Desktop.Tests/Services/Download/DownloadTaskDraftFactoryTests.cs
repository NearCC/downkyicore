using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using DownKyi.Core.BiliApi.Models;
using DownKyi.Core.BiliApi.VideoStream.Models;
using DownKyi.Core.FileName;
using DownKyi.Core.Settings;
using DownKyi.Domain.Downloads;
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

    [Fact]
    public void ApplyWithNullOrEmptySubFolderKeepsTheRootDirectory()
    {
        Assert.Equal(RootDirectory, UploaderSubFolderPath.Apply(RootDirectory, null));
        Assert.Equal(RootDirectory, UploaderSubFolderPath.Apply(RootDirectory, string.Empty));
        Assert.Equal(RootDirectory, UploaderSubFolderPath.Apply(RootDirectory, "   "));
    }

    [Fact]
    public void ApplyWithSubFolderCombinesAndTrimsTheSegment()
    {
        var expected = Path.Combine(RootDirectory, "博主A");

        Assert.Equal(expected, UploaderSubFolderPath.Apply(RootDirectory, "博主A"));
        Assert.Equal(expected, UploaderSubFolderPath.Apply(RootDirectory, "  博主A  "));
    }

    [Fact]
    public void DraftFactoryPlacesTheFileUnderTheAppliedSubFolder()
    {
        var downloadDirectory = UploaderSubFolderPath.Apply(RootDirectory, "博主A");
        var section = BuildSection();

        var item = DownloadTaskDraftFactory.Create(
            downloadDirectory,
            BuildVideo(),
            section,
            sectionCount: 1,
            page: section.VideoPages[0],
            videoQuality: null,
            audioQualityFormat: string.Empty,
            availableMedia: new DownloadMediaCapabilities(DownloadMediaOutputModes.None),
            settings: BuildSettings(),
            content: DownloadContentSelection.None with { Cover = true });

        Assert.StartsWith(
            Path.Combine(RootDirectory, "博主A") + Path.DirectorySeparatorChar,
            item.DownloadBase.FilePath,
            StringComparison.OrdinalIgnoreCase);
    }
}
