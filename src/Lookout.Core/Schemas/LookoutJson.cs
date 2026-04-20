using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lookout.Core.Schemas;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> used by every capture type so that
/// on-disk <c>content_json</c> payloads and API responses stay consistent.
/// </summary>
public static class LookoutJson
{
    /// <summary>CamelCase property names, null values omitted.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };
        return o;
    }
}
