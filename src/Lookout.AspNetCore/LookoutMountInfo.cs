namespace Lookout.AspNetCore;

/// <summary>
/// Carries the dashboard mount prefix from <c>MapLookout</c> to
/// <see cref="LookoutRequestMiddleware"/> so the middleware can self-skip requests
/// hitting the dashboard. Singleton; mutated once at startup.
/// </summary>
internal sealed class LookoutMountInfo
{
    public string PathPrefix { get; set; } = "/lookout";
}
