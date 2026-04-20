using System.Text.Json;
using Lookout.Core;
using Lookout.Core.Schemas;

namespace Lookout.AspNetCore.Api;

/// <summary>Wire format for a <see cref="LookoutEntry"/> returned from the JSON API.</summary>
public sealed record EntryDto(
    Guid Id,
    string Type,
    long Timestamp,
    string? RequestId,
    double? DurationMs,
    IReadOnlyDictionary<string, string> Tags,
    JsonElement Content)
{
    public static EntryDto From(LookoutEntry entry)
    {
        JsonElement content;
        try
        {
            using var doc = JsonDocument.Parse(entry.Content);
            content = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            content = JsonSerializer.SerializeToElement(entry.Content, LookoutJson.Options);
        }

        return new EntryDto(
            Id: entry.Id,
            Type: entry.Type,
            Timestamp: entry.Timestamp.ToUnixTimeMilliseconds(),
            RequestId: entry.RequestId,
            DurationMs: entry.DurationMs,
            Tags: entry.Tags,
            Content: content);
    }
}
