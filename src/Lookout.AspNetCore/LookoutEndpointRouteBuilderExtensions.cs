using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Lookout.AspNetCore;

/// <summary>Extension methods for mapping Lookout dashboard endpoints.</summary>
public static class LookoutEndpointRouteBuilderExtensions
{
    /// <summary>Maps the Lookout dashboard at <paramref name="pathPrefix"/>.</summary>
    public static IEndpointConventionBuilder MapLookout(
        this IEndpointRouteBuilder endpoints,
        string pathPrefix = "/lookout")
    {
        var mount = endpoints.ServiceProvider.GetRequiredService<LookoutMountInfo>();
        mount.PathPrefix = pathPrefix;

        return endpoints.MapGet(pathPrefix, () => Results.Content("Lookout is running.", "text/plain"));
    }
}
