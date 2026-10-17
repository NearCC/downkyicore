using DownKyi.Core.Storage.Uploader;

namespace DownKyi.Services.Uploader;

/// <summary>
/// Values accepted by the uploader-routing panel in the upstream download dialog.
/// </summary>
internal sealed record UploaderRoutingSelection(
    UploaderRoutingStrategy Strategy,
    string? SubFolder);
