using Lookout.Core.Capture;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lookout.EntityFrameworkCore;

/// <summary>Extension methods for integrating Lookout with EF Core.</summary>
public static class LookoutEntityFrameworkCoreExtensions
{
    /// <summary>
    /// Registers <see cref="LookoutDbCommandInterceptor"/> as a DI singleton so it can be
    /// resolved and wired into DbContext registrations via <see cref="UseLookout(DbContextOptionsBuilder, IServiceProvider)"/>.
    /// </summary>
    public static IServiceCollection AddEntityFrameworkCore(this IServiceCollection services)
    {
        services.TryAddSingleton<LookoutDbCommandInterceptor>();
        // Signal to provider-level subscribers (Npgsql ActivitySource) that the richer EF
        // interceptor is active so they skip double-capturing the same queries.
        EfCommandRegistry.EfInterceptorRegistered = true;
        return services;
    }

    /// <summary>
    /// Adds the <see cref="LookoutDbCommandInterceptor"/> to this <see cref="DbContextOptionsBuilder"/>
    /// by resolving it from <paramref name="serviceProvider"/>.
    /// Intended for use inside the <c>AddDbContext((sp, opts) =&gt; ...)</c> overload.
    /// </summary>
    public static DbContextOptionsBuilder UseLookout(
        this DbContextOptionsBuilder builder,
        IServiceProvider serviceProvider)
    {
        builder.AddInterceptors(
            serviceProvider.GetRequiredService<LookoutDbCommandInterceptor>());
        return builder;
    }

    /// <summary>
    /// Generic overload — preserves the typed builder so callers can continue chaining.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> UseLookout<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        IServiceProvider serviceProvider) where TContext : DbContext
    {
        builder.AddInterceptors(
            serviceProvider.GetRequiredService<LookoutDbCommandInterceptor>());
        return builder;
    }

    /// <summary>
    /// Adds the given <see cref="LookoutDbCommandInterceptor"/> to this
    /// <see cref="DbContextOptionsBuilder"/>.
    /// Intended for apps that build <see cref="DbContextOptions"/> manually.
    /// </summary>
    public static DbContextOptionsBuilder UseLookout(
        this DbContextOptionsBuilder builder,
        LookoutDbCommandInterceptor interceptor)
    {
        builder.AddInterceptors(interceptor);
        return builder;
    }

    /// <summary>
    /// Generic overload — preserves the typed builder so callers can continue chaining.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> UseLookout<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        LookoutDbCommandInterceptor interceptor) where TContext : DbContext
    {
        builder.AddInterceptors(interceptor);
        return builder;
    }
}
