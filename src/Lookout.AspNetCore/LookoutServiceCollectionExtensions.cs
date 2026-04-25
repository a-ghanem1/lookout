using Lookout.AspNetCore.Capture;
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

        // Register the concrete type so the retention service can inject it directly.
        // TryAdd so tests can substitute ILookoutStorage without affecting the pruner.
        services.TryAddSingleton<SqliteLookoutStorage>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<LookoutOptions>>().Value;
            return new SqliteLookoutStorage(opts);
        });
        services.TryAddSingleton<ILookoutStorage>(sp => sp.GetRequiredService<SqliteLookoutStorage>());

        services.TryAddSingleton<LookoutMountInfo>();

        services.AddHostedService<LookoutFlusherHostedService>();
        services.AddHostedService<LookoutRetentionHostedService>();
        services.AddHostedService<AdoNetDiagnosticSubscriber>();

        return services;
    }
}
