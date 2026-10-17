using DownKyi.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace DownKyi.Composition;

/// <summary>
/// Registers the uploader-routing screens so the upstream interaction composition
/// root only needs a single feature call.
/// </summary>
internal static class UploaderRoutingUiComposition
{
    public static IServiceCollection AddUploaderRoutingUi(this IServiceCollection services)
    {
        services.AddTransient<ViewUploaderViewModel>();
        return services;
    }
}
