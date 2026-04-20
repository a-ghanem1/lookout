using System.Diagnostics;
using System.Text.Json;
using Lookout.Core;
using Lookout.Core.Schemas;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lookout.AspNetCore;

/// <summary>
/// Captures a <see cref="LookoutEntry"/> of type <c>http</c> for every request that is not
/// skipped by <see cref="LookoutOptions.SkipPaths"/> or the dashboard mount prefix.
/// </summary>
internal sealed class LookoutRequestMiddleware
{
    private const string RedactionMask = "***";

    private readonly RequestDelegate _next;
    private readonly ILookoutRecorder _recorder;
    private readonly LookoutOptions _options;
    private readonly LookoutMountInfo _mount;
    private readonly ILogger<LookoutRequestMiddleware> _logger;

    public LookoutRequestMiddleware(
        RequestDelegate next,
        ILookoutRecorder recorder,
        IOptions<LookoutOptions> options,
        LookoutMountInfo mount,
        ILogger<LookoutRequestMiddleware> logger)
    {
        _next = next;
        _recorder = recorder;
        _options = options.Value;
        _mount = mount;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (ShouldSkip(path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        Activity? startedActivity = null;
        if (Activity.Current is null)
            startedActivity = LookoutActivity.Source.StartActivity("Lookout.HttpRequest");

        var requestId = Activity.Current?.RootId;

        // Snapshot request headers up-front: by the time we record in finally, the
        // request object may have been recycled or modified by downstream middleware.
        var requestHeaders = SnapshotHeaders(context.Request.Headers);

        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            var durationMs = ElapsedMs(startTimestamp);
            try
            {
                Capture(context, path, durationMs, requestId, requestHeaders);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lookout HTTP capture failed for {Method} {Path}.",
                    context.Request.Method, path);
            }
            startedActivity?.Dispose();
        }
    }

    private bool ShouldSkip(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (IsUnderMountPrefix(path)) return true;
        return _options.SkipPaths.Contains(path);
    }

    private bool IsUnderMountPrefix(string path)
    {
        var prefix = _mount.PathPrefix;
        if (string.IsNullOrEmpty(prefix)) return false;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        // Exact match or immediately followed by '/': both count as "under" the prefix.
        return path.Length == prefix.Length || path[prefix.Length] == '/';
    }

    private void Capture(
        HttpContext context,
        string path,
        double durationMs,
        string? requestId,
        IReadOnlyDictionary<string, string> requestHeaders)
    {
        var req = context.Request;
        var res = context.Response;

        var responseHeaders = SnapshotHeaders(res.Headers);

        var redactedRequestHeaders = RedactHeaders(requestHeaders);
        var redactedResponseHeaders = RedactHeaders(responseHeaders);

        var user = ResolveUser(context);

        var content = new HttpEntryContent(
            Method: req.Method,
            Path: path,
            QueryString: req.QueryString.Value ?? string.Empty,
            StatusCode: res.StatusCode,
            DurationMs: durationMs,
            RequestHeaders: redactedRequestHeaders,
            ResponseHeaders: redactedResponseHeaders,
            RequestBody: null,
            ResponseBody: null,
            User: user);

        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["http.method"] = req.Method,
            ["http.path"] = path,
            ["http.status"] = res.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrEmpty(user))
            tags["http.user"] = user!;

        var entry = new LookoutEntry(
            Id: Guid.NewGuid(),
            Type: "http",
            Timestamp: DateTimeOffset.UtcNow,
            RequestId: requestId,
            DurationMs: durationMs,
            Tags: tags,
            Content: JsonSerializer.Serialize(content, LookoutJson.Options));

        _recorder.Record(entry);
    }

    private static Dictionary<string, string> SnapshotHeaders(IHeaderDictionary headers)
    {
        var dict = new Dictionary<string, string>(headers.Count, StringComparer.Ordinal);
        foreach (var kvp in headers)
            dict[kvp.Key] = kvp.Value.ToString();
        return dict;
    }

    private IReadOnlyDictionary<string, string> RedactHeaders(IReadOnlyDictionary<string, string> headers)
    {
        var set = _options.Redaction.Headers;
        var dict = new Dictionary<string, string>(headers.Count, StringComparer.Ordinal);
        foreach (var kvp in headers)
            dict[kvp.Key] = set.Contains(kvp.Key) ? RedactionMask : kvp.Value;
        return dict;
    }

    private string? ResolveUser(HttpContext context)
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated != true) return null;

        if (!string.IsNullOrEmpty(_options.UserClaimType))
        {
            var claim = user.FindFirst(_options.UserClaimType);
            return string.IsNullOrEmpty(claim?.Value) ? null : claim!.Value;
        }

        var name = user.Identity.Name;
        return string.IsNullOrEmpty(name) ? null : name;
    }

    private static double ElapsedMs(long startTimestamp)
    {
        var ticks = Stopwatch.GetTimestamp() - startTimestamp;
        return ticks * 1000.0 / Stopwatch.Frequency;
    }
}
