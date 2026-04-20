namespace Lookout.Core;

/// <summary>Redaction configuration applied to captured entries before storage.</summary>
public sealed class RedactionOptions
{
    /// <summary>
    /// Header names whose values are replaced with <c>***</c> before storage.
    /// Comparison is case-insensitive.
    /// </summary>
    public HashSet<string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Cookie", "Set-Cookie", "X-Api-Key",
    };

    /// <summary>
    /// Query-parameter names whose values are replaced with <c>***</c> before storage.
    /// Comparison is case-insensitive.
    /// </summary>
    public HashSet<string> QueryParams { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "token", "access_token", "refresh_token",
        "secret", "api_key", "apikey",
    };

    /// <summary>
    /// Form-field names whose values are replaced with <c>***</c> before storage.
    /// Comparison is case-insensitive.
    /// </summary>
    public HashSet<string> FormFields { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "token", "access_token", "refresh_token",
        "secret", "api_key", "apikey",
    };
}
