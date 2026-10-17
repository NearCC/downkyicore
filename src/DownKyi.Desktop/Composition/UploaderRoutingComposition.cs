using DownKyi.Core.Storage.Uploader;
using DownKyi.Services.Uploader;
using Microsoft.Extensions.DependencyInjection;

namespace DownKyi.Composition;

/// <summary>
/// Registers the uploader-routing storage feature so the upstream composition
/// root only needs a single feature call.
/// </summary>
internal static class UploaderRoutingComposition
{
    public static IServiceCollection AddUploaderRouting(this IServiceCollection services)
    {
        services.AddSingleton<IUploaderAliasRepository, FileUploaderAliasRepository>();
        services.AddSingleton<IUploaderRoutingPreferenceRepository, FileUploaderRoutingPreferenceRepository>();
        services.AddSingleton<IUploaderRoutingToggleRepository, FileUploaderRoutingToggleRepository>();
        services.AddSingleton<IUploaderRoutingServiceFactory, UploaderRoutingServiceFactory>();
        return services;
    }
}
