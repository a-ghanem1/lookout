using Lookout.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lookout.AspNetCore;

/// <summary>Extension methods for registering Lookout services.</summary>
public static class LookoutServiceCollectionExtensions
{
    /// <summary>Registers Lookout services with the DI container.</summary>
    public static IServiceCollection AddLookout(
        this IServiceCollection services,
        Action<LookoutOptions>? configure = null)
    {
        services.AddLogging();
        services.AddOptions<LookoutOptions>();
        if (configure is not null)
            services.Configure(configure);

        // Register the concrete type so the flusher (M2.4) can inject it directly for Reader access.
        services.TryAddSingleton<ChannelLookoutRecorder>();
        services.TryAddSingleton<ILookoutRecorder>(sp => sp.GetRequiredService<ChannelLookoutRecorder>());

        return services;
    }
}
