using System.Text;
using System.Text.Json;

namespace Lookout.Core;

/// <summary>Applies default redaction rules to a <see cref="LookoutEntry"/> before storage.</summary>
public static class RedactionPipeline
{
    private const string Mask = "***";

    /// <summary>
    /// Returns the entry unchanged if no redaction is needed; otherwise returns a new entry
    /// with matching tag values and top-level JSON content values replaced with <c>***</c>.
    /// </summary>
    public static LookoutEntry Apply(LookoutEntry entry, RedactionOptions options)
    {
        var newTags = RedactTags(entry.Tags, options);
        var newContent = RedactContent(entry.Content, options);

        if (ReferenceEquals(newTags, entry.Tags) && ReferenceEquals(newContent, entry.Content))
            return entry;

        return entry with { Tags = newTags, Content = newContent };
    }

    private static IReadOnlyDictionary<string, string> RedactTags(
        IReadOnlyDictionary<string, string> tags, RedactionOptions options)
    {
        Dictionary<string, string>? result = null;
        foreach (var kvp in tags)
        {
            if (!NeedsRedaction(kvp.Key, options)) continue;
            result ??= tags.ToDictionary(p => p.Key, p => p.Value);
            result[kvp.Key] = Mask;
        }
        return result ?? tags;
    }

    private static string RedactContent(string content, RedactionOptions options)
    {
        if (content.Length < 3) return content;
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return content;

            var modified = false;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (NeedsRedaction(prop.Name, options)) { modified = true; break; }
            }
            if (!modified) return content;

            using var ms = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms);
            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                writer.WritePropertyName(prop.Name);
                if (NeedsRedaction(prop.Name, options))
                    writer.WriteStringValue(Mask);
                else
                    prop.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
            writer.Flush();
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            return content;
        }
    }

    private static bool NeedsRedaction(string key, RedactionOptions options) =>
        options.Headers.Contains(key) ||
        options.QueryParams.Contains(key) ||
        options.FormFields.Contains(key);
}
