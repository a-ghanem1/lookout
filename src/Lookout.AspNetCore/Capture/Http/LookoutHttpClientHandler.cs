using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lookout.Core;
using Lookout.Core.Schemas;
using Microsoft.Extensions.Options;

namespace Lookout.AspNetCore.Capture.Http;

/// <summary>
/// Captures every outbound <see cref="HttpClient"/> request as a <c>http-out</c>
/// <see cref="LookoutEntry"/>, correlated to the parent request via
/// <see cref="Activity.Current"/>. Registered automatically by <c>AddLookout()</c>
/// for all named and typed clients — no per-client wiring required.
/// </summary>
public sealed class LookoutHttpClientHandler : DelegatingHandler
{
    private const string RedactionMask = "***";
    private const string TruncationMarker = "\n…[lookout:truncated]";

    private readonly ILookoutRecorder _recorder;
    private readonly LookoutOptions _options;

    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new Dictionary<string, string>(0, StringComparer.Ordinal);

    public LookoutHttpClientHandler(ILookoutRecorder recorder, IOptions<LookoutOptions> options)
    {
        _recorder = recorder;
        _options = options.Value;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!_options.Http.CaptureOutbound)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var startTimestamp = Stopwatch.GetTimestamp();

        var url = RedactUrl(request.RequestUri, _options.Redaction.QueryParams);
        var method = request.Method.Method;
        var requestHeaders = SnapshotAndRedactHeaders(request.Headers, _options.Redaction.Headers);

        string? requestBody = null;
        if (_options.Http.CaptureOutboundRequestBody && request.Content is not null)
        {
            requestBody = await CaptureBodyAsync(request.Content, _options, cancellationToken)
                .ConfigureAwait(false);
        }

        HttpResponseMessage? response;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RecordEntry(
                method, url, statusCode: null, ElapsedMs(startTimestamp),
                requestHeaders, responseHeaders: EmptyHeaders,
                requestBody, responseBody: null,
                errorType: ex.GetType().FullName, errorMessage: ex.Message);
            throw;
        }

        var durationMs = ElapsedMs(startTimestamp);
        var responseHeaders = SnapshotAndRedactHeaders(response.Headers, _options.Redaction.Headers);

        string? responseBody = null;
        if (_options.Http.CaptureOutboundResponseBody && response.Content is not null)
        {
            responseBody = await CaptureBodyAsync(response.Content, _options, cancellationToken)
                .ConfigureAwait(false);
        }

        RecordEntry(
            method, url, (int)response.StatusCode, durationMs,
            requestHeaders, responseHeaders,
            requestBody, responseBody,
            errorType: null, errorMessage: null);

        return response;
    }

    private void RecordEntry(
        string method,
        string url,
        int? statusCode,
        double durationMs,
        IReadOnlyDictionary<string, string> requestHeaders,
        IReadOnlyDictionary<string, string> responseHeaders,
        string? requestBody,
        string? responseBody,
        string? errorType,
        string? errorMessage)
    {
        var content = new OutboundHttpEntryContent(
            Method: method,
            Url: url,
            StatusCode: statusCode,
            DurationMs: durationMs,
            RequestHeaders: requestHeaders,
            ResponseHeaders: responseHeaders,
            RequestBody: requestBody,
            ResponseBody: responseBody,
            ErrorType: errorType,
            ErrorMessage: errorMessage);

        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["http.method"] = method,
            ["http.out"] = "true",
        };

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            tags["http.url.host"] = uri.Host;
            tags["http.url.path"] = uri.AbsolutePath;
        }

        if (statusCode.HasValue)
            tags["http.status"] = statusCode.Value.ToString(CultureInfo.InvariantCulture);

        if (errorType is not null)
            tags["http.error"] = errorType;

        var entry = new LookoutEntry(
            Id: Guid.NewGuid(),
            Type: "http-out",
            Timestamp: DateTimeOffset.UtcNow,
            RequestId: Activity.Current?.RootId,
            DurationMs: durationMs,
            Tags: tags,
            Content: JsonSerializer.Serialize(content, LookoutJson.Options));

        _recorder.Record(entry);
    }

    private static string RedactUrl(Uri? uri, HashSet<string> sensitiveParams)
    {
        if (uri is null) return string.Empty;

        var query = uri.Query;
        if (string.IsNullOrEmpty(query) || sensitiveParams.Count == 0)
            return uri.GetLeftPart(UriPartial.Path) + query;

        var sb = new StringBuilder();
        var raw = query.TrimStart('?');
        var first = true;
        foreach (var segment in raw.Split('&'))
        {
            if (segment.Length == 0) continue;
            sb.Append(first ? '?' : '&');
            first = false;

            var eq = segment.IndexOf('=');
            if (eq < 0)
            {
                sb.Append(segment);
                continue;
            }

            string name;
            try { name = Uri.UnescapeDataString(segment.Substring(0, eq)); }
            catch { name = segment.Substring(0, eq); }

            sb.Append(segment, 0, eq + 1);
            sb.Append(sensitiveParams.Contains(name) ? RedactionMask : segment.Substring(eq + 1));
        }

        return uri.GetLeftPart(UriPartial.Path) + sb;
    }

    private static IReadOnlyDictionary<string, string> SnapshotAndRedactHeaders(
        HttpHeaders headers, HashSet<string> sensitive)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kvp in headers)
            dict[kvp.Key] = sensitive.Contains(kvp.Key) ? RedactionMask : string.Join(", ", kvp.Value);
        return dict;
    }

    private static async Task<string?> CaptureBodyAsync(
        HttpContent content, LookoutOptions opts, CancellationToken cancellationToken)
    {
        var contentType = content.Headers.ContentType?.MediaType;
        if (!MatchesCapturedType(contentType, opts.CapturedContentTypes))
            return null;

        byte[] bytes;
        try
        {
            bytes = await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        if (bytes.Length == 0) return null;

        var maxBytes = opts.Http.OutboundBodyMaxBytes;
        var truncated = bytes.Length > maxBytes;
        var text = Encoding.UTF8.GetString(bytes, 0, truncated ? maxBytes : bytes.Length);
        return truncated ? text + TruncationMarker : text;
    }

    private static bool MatchesCapturedType(string? contentType, HashSet<string> set)
    {
        if (contentType is null) return false;
        var semi = contentType.IndexOf(';');
        var bare = (semi >= 0 ? contentType.Substring(0, semi) : contentType).Trim();
        if (set.Contains(bare)) return true;
        var slash = bare.IndexOf('/');
        if (slash <= 0) return false;
        return set.Contains(bare.Substring(0, slash) + "/*");
    }

    private static double ElapsedMs(long startTimestamp)
    {
        var ticks = Stopwatch.GetTimestamp() - startTimestamp;
        return ticks * 1000.0 / Stopwatch.Frequency;
    }
}
