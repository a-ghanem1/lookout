using Microsoft.Extensions.DependencyInjection;

namespace Lookout.AspNetCore;

/// <summary>Extension methods for registering Lookout services.</summary>
public static class LookoutServiceCollectionExtensions
{
    /// <summary>Registers Lookout services with the DI container.</summary>
    public static IServiceCollection AddLookout(
        this IServiceCollection services,
        Action<LookoutOptions>? configure = null)
    {
        services.AddOptions<LookoutOptions>();
        if (configure is not null)
            services.Configure(configure);
        return services;
    }
}
