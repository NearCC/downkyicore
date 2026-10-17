using System.Threading;
using System.Threading.Tasks;
using DownKyi.Presentation;
using DownKyi.Services.Uploader;
using DownKyi.ViewModels.Dialogs;

namespace DownKyi.Services.Download;

internal sealed partial class AddToDownloadService : IOwnerAwareDownloadSession
{
    private UploaderRoutingService? _uploaderRouting;
    private long _pendingOwnerMid;
    private string _pendingOwnerName = string.Empty;
    private string? _pendingOwnerSource;

    public void SetOwner(long ownerMid, string ownerName)
    {
        _pendingOwnerMid = ownerMid;
        _pendingOwnerName = ownerName ?? string.Empty;
        _pendingOwnerSource = null;
    }

    public void SetOwnerSource(string source)
    {
        _pendingOwnerMid = 0L;
        _pendingOwnerName = string.Empty;
        _pendingOwnerSource = source;
    }

    internal void ConfigureUploaderRouting(UploaderRoutingService uploaderRouting)
    {
        _uploaderRouting = uploaderRouting;
    }

    /// <summary>
    /// Builds the panel that the upstream download settings dialog will host.
    /// Returns null when routing is disabled, leaving the dialog untouched.
    /// </summary>
    private async Task<UploaderRoutingPanelViewModel?> TryPrepareUploaderRoutingAsync(
        CancellationToken cancellationToken)
    {
        if (_uploaderRouting is not { IsEnabled: true } uploaderRouting)
        {
            return null;
        }

        try
        {
            var (ownerMid, ownerName) = _pendingOwnerMid > 0
                ? (_pendingOwnerMid, _pendingOwnerName)
                : await uploaderRouting
                    .ResolveOwnerAsync(_pendingOwnerSource, cancellationToken)
                    .ConfigureAwait(true);
            return uploaderRouting.CreateDialogExtension(ownerMid, ownerName);
        }
        finally
        {
            _pendingOwnerMid = 0L;
            _pendingOwnerName = string.Empty;
            _pendingOwnerSource = null;
        }
    }

    private void TryCompleteUploaderRouting(bool accepted)
    {
        _uploaderRouting?.CompleteDialogExtension(accepted);
    }

    private string ResolveUploaderDownloadDirectory(string directory, VideoInfoView video)
    {
        return UploaderSubFolderPath.Apply(directory, _uploaderRouting?.ResolveSubFolder(video));
    }
}
