using System.Diagnostics;
using System.Text;
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
    private const string TruncationMarker = "\n…[lookout:truncated]";

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
        var requestContentType = context.Request.ContentType;

        string? requestBody = null;
        var requestBodyTruncated = false;
        if (_options.CaptureRequestBody &&
            MatchesCapturedType(requestContentType, _options.CapturedContentTypes))
        {
            try
            {
                context.Request.EnableBuffering();
                (requestBody, requestBodyTruncated) = await ReadRequestBodyAsync(
                    context.Request.Body, _options.MaxBodyBytes, context.RequestAborted)
                    .ConfigureAwait(false);
                context.Request.Body.Position = 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lookout failed to buffer request body for {Method} {Path}.",
                    context.Request.Method, path);
                requestBody = null;
            }
        }

        Stream? originalResponseBody = null;
        CapturingResponseStream? responseCapture = null;
        if (_options.CaptureResponseBody)
        {
            originalResponseBody = context.Response.Body;
            responseCapture = new CapturingResponseStream(
                originalResponseBody, context.Response, _options.MaxBodyBytes, _options.CapturedContentTypes);
            context.Response.Body = responseCapture;
        }

        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            if (originalResponseBody is not null)
                context.Response.Body = originalResponseBody;

            var durationMs = ElapsedMs(startTimestamp);
            try
            {
                Capture(
                    context, path, durationMs, requestId, requestHeaders,
                    requestContentType, requestBody, requestBodyTruncated,
                    responseCapture);
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
        IReadOnlyDictionary<string, string> requestHeaders,
        string? requestContentType,
        string? requestBody,
        bool requestBodyTruncated,
        CapturingResponseStream? responseCapture)
    {
        var req = context.Request;
        var res = context.Response;

        var responseHeaders = SnapshotHeaders(res.Headers);

        var redactedRequestHeaders = RedactHeaders(requestHeaders);
        var redactedResponseHeaders = RedactHeaders(responseHeaders);

        var finalRequestBody = FinalizeBody(
            requestBody, requestBodyTruncated, requestContentType);

        string? finalResponseBody = null;
        if (responseCapture is not null)
        {
            var responseText = responseCapture.GetCapturedText();
            finalResponseBody = FinalizeBody(
                responseText, responseCapture.Truncated, res.ContentType);
        }

        var user = ResolveUser(context);

        var content = new HttpEntryContent(
            Method: req.Method,
            Path: path,
            QueryString: req.QueryString.Value ?? string.Empty,
            StatusCode: res.StatusCode,
            DurationMs: durationMs,
            RequestHeaders: redactedRequestHeaders,
            ResponseHeaders: redactedResponseHeaders,
            RequestBody: finalRequestBody,
            ResponseBody: finalResponseBody,
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

    private string? FinalizeBody(string? body, bool truncated, string? contentType)
    {
        if (body is null) return null;

        var normalized = NormalizeContentType(contentType);
        var redacted = body;
        if (string.Equals(normalized, "application/json", StringComparison.OrdinalIgnoreCase))
            redacted = RedactionPipeline.RedactJsonBody(body, _options.Redaction);
        else if (string.Equals(normalized, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            redacted = RedactionPipeline.RedactFormBody(body, _options.Redaction);

        return truncated ? redacted + TruncationMarker : redacted;
    }

    private static async Task<(string Text, bool Truncated)> ReadRequestBodyAsync(
        Stream body, int maxBytes, CancellationToken cancellationToken)
    {
        // Read up to maxBytes+1 so we can distinguish "exactly at the limit" from "overflowed".
        var limit = maxBytes + 1;
        var buffer = new byte[Math.Min(8192, limit)];
        using var ms = new MemoryStream();

        var totalRead = 0;
        while (totalRead < limit)
        {
            var toRead = Math.Min(buffer.Length, limit - totalRead);
            var read = await body.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0) break;
            ms.Write(buffer, 0, read);
            totalRead += read;
        }

        var truncated = totalRead > maxBytes;
        var bytes = truncated ? ms.ToArray().AsSpan(0, maxBytes).ToArray() : ms.ToArray();
        return (Encoding.UTF8.GetString(bytes), truncated);
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

    private static string? NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return null;
        var semi = contentType.IndexOf(';');
        var bare = semi >= 0 ? contentType.Substring(0, semi) : contentType;
        return bare.Trim();
    }

    private static bool MatchesCapturedType(string? contentType, HashSet<string> set)
    {
        var normalized = NormalizeContentType(contentType);
        if (normalized is null) return false;
        if (set.Contains(normalized)) return true;

        var slash = normalized.IndexOf('/');
        if (slash <= 0) return false;
        var wildcard = normalized.Substring(0, slash) + "/*";
        return set.Contains(wildcard);
    }

    /// <summary>
    /// Wraps <see cref="HttpResponse.Body"/> so outbound writes pass through untouched while a
    /// size-capped, content-type-gated copy is retained for <see cref="LookoutEntry"/> capture.
    /// </summary>
    private sealed class CapturingResponseStream : Stream
    {
        private readonly Stream _inner;
        private readonly HttpResponse _response;
        private readonly int _maxBytes;
        private readonly HashSet<string> _capturedContentTypes;

        private MemoryStream? _buffer;
        private bool _decided;
        private bool _capture;

        public bool Truncated { get; private set; }

        public CapturingResponseStream(
            Stream inner,
            HttpResponse response,
            int maxBytes,
            HashSet<string> capturedContentTypes)
        {
            _inner = inner;
            _response = response;
            _maxBytes = maxBytes;
            _capturedContentTypes = capturedContentTypes;
        }

        public string? GetCapturedText()
        {
            if (_buffer is null) return null;
            return Encoding.UTF8.GetString(_buffer.GetBuffer(), 0, (int)_buffer.Length);
        }

        private void DecideCapture()
        {
            if (_decided) return;
            _decided = true;
            if (MatchesCapturedType(_response.ContentType, _capturedContentTypes))
            {
                _capture = true;
                _buffer = new MemoryStream();
            }
        }

        private void CopyToBuffer(ReadOnlySpan<byte> span)
        {
            if (!_capture || _buffer is null) return;
            if (_buffer.Length >= _maxBytes)
            {
                Truncated = true;
                return;
            }
            var remaining = _maxBytes - (int)_buffer.Length;
            if (span.Length <= remaining)
            {
                _buffer.Write(span);
            }
            else
            {
                _buffer.Write(span.Slice(0, remaining));
                Truncated = true;
            }
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
            DecideCapture();
            CopyToBuffer(buffer.AsSpan(offset, count));
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            DecideCapture();
            CopyToBuffer(buffer.AsSpan(offset, count));
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            DecideCapture();
            CopyToBuffer(buffer.Span);
        }
    }
}
