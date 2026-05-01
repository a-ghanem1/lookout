using System.Security.Cryptography;

namespace Lookout.AspNetCore;

/// <summary>
/// Carries the dashboard mount prefix from <c>MapLookout</c> to
/// <see cref="LookoutRequestMiddleware"/> so the middleware can self-skip requests
/// hitting the dashboard. Singleton; mutated once at startup.
/// </summary>
internal sealed class LookoutMountInfo
{
    public string PathPrefix { get; set; } = "/lookout";

    /// <summary>
    /// Stable CSRF token generated once per server lifetime. The server sets it as the
    /// <c>__lookout-csrf</c> cookie (HttpOnly=false) on every dashboard load so the
    /// React client can read it via <c>document.cookie</c> and attach it as the
    /// <c>X-Lookout-Csrf-Token</c> header on mutating requests.
    /// </summary>
    public string CsrfToken { get; } =
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}
