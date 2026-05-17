using Lookout.Core;
using Lookout.Core.Capture;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lookout.AspNetCore;

/// <summary>Extension methods for adding Lookout middleware to the pipeline.</summary>
public static class LookoutApplicationBuilderExtensions
{
    /// <summary>Adds the Lookout middleware to the request pipeline.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the current environment is not in <see cref="LookoutOptions.AllowInEnvironments"/>
    /// and <see cref="LookoutOptions.AllowInProduction"/> is false.
    /// </exception>
    public static IApplicationBuilder UseLookout(this IApplicationBuilder app)
    {
        var env = app.ApplicationServices.GetRequiredService<IHostEnvironment>();
        var options = app.ApplicationServices.GetRequiredService<IOptions<LookoutOptions>>().Value;
        var logger = app.ApplicationServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Lookout.AspNetCore");

        if (options.AllowInProduction)
        {
            logger.LogWarning(
                "Lookout is running with AllowInProduction = true in the '{EnvironmentName}' environment. " +
                "This bypasses the dev-only safety rail — do not use in production workloads.",
                env.EnvironmentName);
        }
        else
        {
            var permitted = options.AllowInEnvironments
                .Any(e => string.Equals(e, env.EnvironmentName, StringComparison.OrdinalIgnoreCase));

            if (!permitted)
            {
                var allowed = string.Join(", ", options.AllowInEnvironments.Select(e => $"'{e}'"));
                throw new InvalidOperationException(
                    $"Lookout cannot run in the '{env.EnvironmentName}' environment. " +
                    $"Permitted environments (LookoutOptions.AllowInEnvironments): {allowed}. " +
                    $"To allow this environment, add '{env.EnvironmentName}' to AllowInEnvironments, " +
                    $"or set AllowInProduction = true to bypass the safety rail entirely " +
                    $"(not recommended — logs a startup warning).");
            }
        }

        // Register the non-loopback address warning. Runs after the server starts
        // listening so IServerAddressesFeature is fully populated (Kestrel). Falls back
        // to IConfiguration["urls"] in tests where TestServer does not bind real sockets.
        var lifetime = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
        var serverFeatures = app.ServerFeatures;
        lifetime.ApplicationStarted.Register(() =>
        {
            var efActive = EfCommandRegistry.EfInterceptorRegistered;
            var n1Threshold = options.Ef.N1DetectionMinOccurrences;
            // LogDebug: framework startup diagnostics don't belong in the developer's dashboard
            // (LookoutLoggerProvider captures >= Information by default), but remain visible to
            // anyone who enables debug logging for the "Lookout.AspNetCore" category.
            logger.LogDebug(
                "Lookout running at /lookout. EF Core capture: {EfStatus}. " +
                "N+1 detection: {N1Status}.",
                efActive ? "active" : "inactive — install Lookout.EntityFrameworkCore",
                efActive ? $"active (threshold: {n1Threshold} identical queries)" : "inactive");

            if (options.AllowNonLoopback) return;

            var addresses = serverFeatures.Get<IServerAddressesFeature>()?.Addresses;
            IEnumerable<string> toCheck = addresses?.Count > 0
                ? addresses
                : GetConfiguredUrls(app.ApplicationServices);

            foreach (var address in toCheck)
            {
                if (!IsLoopbackAddress(address))
                {
                    logger.LogWarning(
                        "Lookout is bound to a non-loopback address ({Address}). " +
                        "The dashboard is accessible to other machines on the network. " +
                        "Set AllowNonLoopback = true to suppress this warning.",
                        address);
                    break;
                }
            }
        });

        app.UseMiddleware<LookoutRequestMiddleware>();
        return app;
    }

    private static IEnumerable<string> GetConfiguredUrls(IServiceProvider services)
    {
        var config = services.GetService<IConfiguration>();
        var urls = config?["urls"] ?? string.Empty;
        return urls.Split(';', StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool IsLoopbackAddress(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return true;
        var host = uri.Host;
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host == "127.0.0.1"
            || host == "::1"
            || host == "[::1]";
    }
}
