using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lookout.Hangfire;

/// <summary>
/// Extension methods for registering Lookout's Hangfire capture pipeline.
/// </summary>
public static class HangfireServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Lookout Hangfire client and server filters so that background-job
    /// enqueues and executions are captured in the Lookout diagnostics dashboard.
    /// </summary>
    /// <remarks>
    /// Call <see cref="AddLookoutHangfire"/> <em>after</em> <c>AddLookout()</c>. If
    /// <c>AddLookout()</c> has not been called first, filter installation is skipped and
    /// a warning is logged at startup.
    /// </remarks>
    public static IServiceCollection AddLookoutHangfire(this IServiceCollection services)
    {
        services.TryAddSingleton<LookoutHangfireClientFilter>();
        services.TryAddSingleton<LookoutHangfireServerFilter>();
        services.AddHostedService<LookoutHangfireFilterInstaller>();
        return services;
    }
}
