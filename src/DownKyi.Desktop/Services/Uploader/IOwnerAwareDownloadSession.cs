using DownKyi.Services.Download;

namespace DownKyi.Services.Uploader;

/// <summary>
/// Feature-owned opt-in hook for add-to-download sessions that need the uploader
/// identity before the directory dialog opens.
/// </summary>
internal interface IOwnerAwareDownloadSession
{
    /// <summary>
    /// Provides the uploader identity when it is already known (video detail flow).
    /// </summary>
    void SetOwner(long ownerMid, string ownerName);

    /// <summary>
    /// Provides the content source so the feature can resolve the uploader itself
    /// (batch download flow).
    /// </summary>
    void SetOwnerSource(string source);
}

/// <summary>
/// Keeps the uploader-routing feature from changing the upstream session contract:
/// call sites use these extensions, and sessions that do not opt in are unaffected.
/// </summary>
internal static class OwnerAwareDownloadSessionExtensions
{
    public static void SetOwnerIfSupported(
        this IAddToDownloadSession session,
        long ownerMid,
        string ownerName)
    {
        if (session is IOwnerAwareDownloadSession ownerAware)
        {
            ownerAware.SetOwner(ownerMid, ownerName);
        }
    }

    public static void SetOwnerSourceIfSupported(
        this IAddToDownloadSession session,
        string source)
    {
        if (session is IOwnerAwareDownloadSession ownerAware)
        {
            ownerAware.SetOwnerSource(source);
        }
    }
}
