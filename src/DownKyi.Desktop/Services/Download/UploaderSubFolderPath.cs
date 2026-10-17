using System.IO;

namespace DownKyi.Services.Download;

/// <summary>
/// Combines the selected root directory with the uploader sub-folder.
/// Applied before calling the upstream draft factory, so the upstream
/// file-path logic stays untouched.
/// </summary>
internal static class UploaderSubFolderPath
{
    public static string Apply(string directory, string? subFolder)
    {
        return string.IsNullOrWhiteSpace(subFolder)
            ? directory
            : Path.Combine(directory, subFolder.Trim());
    }
}
