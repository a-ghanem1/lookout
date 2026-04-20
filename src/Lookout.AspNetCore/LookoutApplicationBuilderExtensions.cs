using Microsoft.AspNetCore.Builder;
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

        if (options.AllowInProduction)
        {
            app.ApplicationServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Lookout.AspNetCore")
                .LogWarning(
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

        return app;
    }
}
