using Lookout.Core;
using Lookout.Storage.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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

        // Register the concrete recorder so the flusher can inject it directly for Reader access.
        services.TryAddSingleton<ChannelLookoutRecorder>();
        services.TryAddSingleton<ILookoutRecorder>(sp => sp.GetRequiredService<ChannelLookoutRecorder>());

        // TryAdd so tests can override with AddSingleton after this call.
        services.TryAddSingleton<ILookoutStorage>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<LookoutOptions>>().Value;
            return new SqliteLookoutStorage(opts);
        });

        services.AddHostedService<LookoutFlusherHostedService>();

        return services;
    }
}
