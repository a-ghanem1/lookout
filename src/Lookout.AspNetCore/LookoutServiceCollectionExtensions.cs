using Lookout.AspNetCore.Capture;
using Lookout.AspNetCore.Capture.Cache;
using Lookout.AspNetCore.Capture.Exceptions;
using Lookout.AspNetCore.Capture.Http;
using Lookout.AspNetCore.Capture.Logging;
using Lookout.Core;
using Lookout.Storage.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Lookout.AspNetCore;

/// <summary>
/// Extension methods for registering Lookout services.
/// </summary>
/// <remarks>
/// Registration order: call <c>AddMemoryCache()</c> (or equivalent) <em>before</em>
/// <c>AddLookout()</c> for cache capture to take effect. If no <c>IMemoryCache</c>
/// registration is found, cache capture is skipped silently — no error.
/// </remarks>
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

        // Exception capture — IExceptionHandler (requires UseExceptionHandler() in pipeline) +
        // DiagnosticListener fallback for exceptions that bypass UseExceptionHandler().
        // Ours is registered first so it runs before consumer-registered handlers; it always
        // returns false to keep the host's own error handling intact.
        services.AddSingleton<Microsoft.AspNetCore.Diagnostics.IExceptionHandler, LookoutExceptionHandler>();
        services.AddHostedService<UnhandledExceptionDiagnosticSubscriber>();

        // Log capture — additive provider; all other configured providers still receive every call.
        services.AddSingleton<ILoggerProvider, LookoutLoggerProvider>();

        // Outbound HttpClient capture — auto-wired into every named/typed client.
        // Consumers must call AddHttpClient() before or after AddLookout(); order does not matter
        // because ConfigureAll defers until the handler chain is first built.
        services.AddTransient<LookoutHttpClientHandler>();
        services.ConfigureAll<HttpClientFactoryOptions>(o =>
            o.HttpMessageHandlerBuilderActions.Add(b =>
                b.AdditionalHandlers.Add(b.Services.GetRequiredService<LookoutHttpClientHandler>())));

        // IMemoryCache capture — decorates the last IMemoryCache registration found.
        // No-op when AddMemoryCache() has not been called before AddLookout().
        DecorateMemoryCache(services);

        // IDistributedCache capture — decorates the last IDistributedCache registration found.
        // No-op when AddDistributedMemoryCache() (or equivalent) has not been called before AddLookout().
        DecorateDistributedCache(services);

        return services;
    }

    private static void DecorateMemoryCache(IServiceCollection services)
    {
        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(IMemoryCache));
        if (descriptor is null) return;

        services.Remove(descriptor);
        services.Add(ServiceDescriptor.Singleton<IMemoryCache>(sp =>
        {
            var inner = (IMemoryCache)CreateFromDescriptor(sp, descriptor);
            return new LookoutMemoryCacheDecorator(
                inner,
                sp.GetRequiredService<ILookoutRecorder>(),
                sp.GetRequiredService<IOptions<LookoutOptions>>());
        }));
    }

    private static void DecorateDistributedCache(IServiceCollection services)
    {
        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(IDistributedCache));
        if (descriptor is null) return;

        services.Remove(descriptor);
        services.Add(ServiceDescriptor.Singleton<IDistributedCache>(sp =>
        {
            var inner = (IDistributedCache)CreateFromDescriptor(sp, descriptor);
            return new LookoutDistributedCacheDecorator(
                inner,
                sp.GetRequiredService<ILookoutRecorder>(),
                sp.GetRequiredService<IOptions<LookoutOptions>>());
        }));
    }

    private static object CreateFromDescriptor(IServiceProvider sp, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is not null)
            return descriptor.ImplementationInstance;
        if (descriptor.ImplementationFactory is not null)
            return descriptor.ImplementationFactory(sp);
        return ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);
    }
}
