using Microsoft.AspNetCore.Builder;

namespace Lookout.AspNetCore;

/// <summary>Extension methods for adding Lookout middleware to the pipeline.</summary>
public static class LookoutApplicationBuilderExtensions
{
    /// <summary>Adds the Lookout middleware to the request pipeline.</summary>
    public static IApplicationBuilder UseLookout(this IApplicationBuilder app)
    {
        // No-op stub — dev-only enforcement and capture pipeline wired in M1.3/Week 2
        return app;
    }
}
