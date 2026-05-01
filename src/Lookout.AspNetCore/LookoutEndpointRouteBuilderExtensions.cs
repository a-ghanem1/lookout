using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Lookout.AspNetCore.Api;
using Lookout.Core;
using Lookout.Core.Schemas;
using Lookout.Dashboard;
using Lookout.Storage.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Lookout.AspNetCore;

/// <summary>Extension methods for mapping Lookout dashboard endpoints.</summary>
public static class LookoutEndpointRouteBuilderExtensions
{
    /// <summary>Maps the Lookout dashboard and JSON API at <paramref name="pathPrefix"/>.</summary>
    public static IEndpointConventionBuilder MapLookout(
        this IEndpointRouteBuilder endpoints,
        string pathPrefix = "/lookout")
    {
        var mount = endpoints.ServiceProvider.GetRequiredService<LookoutMountInfo>();
        mount.PathPrefix = pathPrefix;

        var group = endpoints.MapGroup(pathPrefix);

        group.MapGet("/api/entries", ListEntriesAsync);
        group.MapGet("/api/entries/counts", GetEntryCountsAsync);
        group.MapGet("/api/entries/cache/summary", GetCacheSummaryAsync);
        group.MapGet("/api/entries/logs/histogram", GetLogHistogramAsync);
        group.MapGet("/api/entries/cache/by-key", GetCacheByKeyAsync);
        group.MapGet("/api/entries/{id:guid}", GetEntryAsync);
        group.MapGet("/api/requests/{id}", GetRequestEntriesAsync);
        group.MapGet("/api/search", SearchEntriesAsync);
        group.MapGet("/api/host", GetHostInfoAsync);
        group.MapDelete("/api/entries", DeleteAllEntriesAsync);

        // Dashboard asset handler. The `/api/` routes above win by template specificity;
        // anything else under /lookout falls through to the embedded Vite bundle, with
        // SPA fallback to index.html for unknown paths. The bare-prefix route ensures
        // `GET /lookout` (no trailing slash) still hits the index.
        endpoints.MapGet(pathPrefix, (HttpContext ctx) => ServeAssetAsync(ctx, string.Empty, mount));
        group.MapGet("/{*path}", (HttpContext ctx, string? path) => ServeAssetAsync(ctx, path ?? string.Empty, mount));

        return group;
    }

    private static Task ServeAssetAsync(HttpContext ctx, string path, LookoutMountInfo mount)
    {
        var relative = (path ?? string.Empty).Trim('/');
        if (relative.Length == 0)
            return WriteIndexAsync(ctx, mount);

        // Stop obvious traversal; resource lookup already normalizes slashes, but a hit on
        // the assembly with '..' in the logical path should never happen — play safe.
        if (relative.Contains("..", StringComparison.Ordinal))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        return WriteAssetAsync(ctx, relative, mount);
    }

    private static async Task WriteAssetAsync(HttpContext ctx, string relative, LookoutMountInfo mount)
    {
        await using var stream = DashboardAssets.Open(relative);
        if (stream is not null)
        {
            ctx.Response.ContentType = GuessContentType(relative);
            ctx.Response.Headers.CacheControl = "no-cache";
            await stream.CopyToAsync(ctx.Response.Body, ctx.RequestAborted).ConfigureAwait(false);
            return;
        }

        // SPA fallback — unknown sub-paths serve the index so the hash router can handle them.
        await WriteIndexAsync(ctx, mount);
    }

    private static async Task WriteIndexAsync(HttpContext ctx, LookoutMountInfo mount)
    {
        await using var indexStream = DashboardAssets.Open("index.html");
        if (indexStream is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Set the CSRF double-submit cookie. HttpOnly=false is intentional: the React
        // client reads the token via document.cookie and attaches it as the
        // X-Lookout-Csrf-Token header on mutating requests.
        ctx.Response.Cookies.Append("__lookout-csrf", mount.CsrfToken, new CookieOptions
        {
            SameSite = SameSiteMode.Strict,
            HttpOnly = false,
            Path = mount.PathPrefix.TrimEnd('/'),
            IsEssential = true,
        });

        // Inject <base href> so the browser resolves Vite's relative asset URLs
        // (and our fetch('api/…') calls) against the mount prefix, regardless of
        // whether the current URL is `/lookout`, `/lookout/`, or `/lookout/requests/x`.
        using var reader = new StreamReader(indexStream, Encoding.UTF8);
        var html = await reader.ReadToEndAsync(ctx.RequestAborted).ConfigureAwait(false);
        var injected = InjectBaseHref(html, mount.PathPrefix);

        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache";
        await ctx.Response.WriteAsync(injected, ctx.RequestAborted).ConfigureAwait(false);
    }

    internal static string InjectBaseHref(string html, string pathPrefix)
    {
        var normalized = pathPrefix.EndsWith('/') ? pathPrefix : pathPrefix + "/";
        var tag = $"<base href=\"{normalized}\">";
        var headIdx = html.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
        if (headIdx < 0) return html;
        var insertAt = headIdx + "<head>".Length;
        return html.Insert(insertAt, tag);
    }

    private static string GuessContentType(string path)
    {
        var dot = path.LastIndexOf('.');
        var ext = dot < 0 ? string.Empty : path.Substring(dot).ToLowerInvariant();
        return ext switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" or ".mjs" => "application/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".map" => "application/json; charset=utf-8",
            ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream",
        };
    }

    private static async Task<IResult> GetEntryCountsAsync(
        HttpContext ctx,
        SqliteLookoutStorage storage)
    {
        var counts = await storage.GetCountsAsync(ctx.RequestAborted).ConfigureAwait(false);
        ctx.Response.Headers.CacheControl = "no-store";
        return Json(new EntryCounts(
            counts.Requests,
            counts.Queries,
            counts.Exceptions,
            counts.Logs,
            counts.Cache,
            counts.HttpClients,
            counts.Jobs,
            counts.Dump));
    }

    private static async Task<IResult> GetCacheSummaryAsync(
        HttpContext ctx,
        SqliteLookoutStorage storage,
        long? from = null,
        long? to = null)
    {
        var (hits, misses, sets, removes) = await storage
            .GetCacheSummaryAsync(from, to, ctx.RequestAborted).ConfigureAwait(false);
        var total = hits + misses;
        var hitRatio = total > 0 ? (double)hits / total : 0.0;
        ctx.Response.Headers.CacheControl = "no-store";
        return Json(new CacheSummary(hits, misses, sets, removes, hitRatio));
    }

    private static async Task<IResult> ListEntriesAsync(
        HttpContext ctx,
        SqliteLookoutStorage storage,
        string? type = null,
        string? method = null,
        string? status = null,
        string? path = null,
        string? q = null,
        long? before = null,
        int? limit = null,
        string? sort = null,
        double? min_duration_ms = null,
        double? max_duration_ms = null,
        string? host = null,
        bool? errors_only = null,
        string? min_level = null,
        bool? handled = null)
    {
        if (!TryParseStatus(status, out var statusMin, out var statusMax))
            return Results.Text(
                "invalid status filter; expected '500' or '400-499'",
                contentType: "text/plain",
                statusCode: StatusCodes.Status400BadRequest);

        var effectiveLimit = Math.Clamp(limit ?? 50, 1, 200);

        // Parse comma-separated type list (e.g. "ef,sql") for multi-type filtering
        string? singleType = null;
        IReadOnlyList<string>? typeIn = null;
        if (!string.IsNullOrEmpty(type))
        {
            var parts = type.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 1) singleType = parts[0];
            else typeIn = parts;
        }

        // Parse multi-valued tag filters: ?tag=key:value&tag=key2:value2
        var tagValues = ctx.Request.Query["tag"];
        List<(string Key, string Value)>? tags = null;
        foreach (var tv in tagValues)
        {
            if (string.IsNullOrEmpty(tv)) continue;
            var colon = tv.IndexOf(':');
            if (colon < 1) continue;
            tags ??= [];
            tags.Add((tv[..colon], tv[(colon + 1)..]));
        }

        var query = new LookoutQuery(
            Type: singleType,
            TypeIn: typeIn,
            Method: method,
            StatusMin: statusMin,
            StatusMax: statusMax,
            Path: path,
            Q: q,
            BeforeUnixMs: before,
            Limit: effectiveLimit,
            Sort: sort,
            MinDurationMs: min_duration_ms,
            MaxDurationMs: max_duration_ms,
            UrlHost: host,
            ErrorsOnly: errors_only,
            MinLevel: min_level,
            Handled: handled,
            Tags: tags);

        var entries = await storage.QueryAsync(query, ctx.RequestAborted).ConfigureAwait(false);
        var dtos = entries.Select(EntryDto.From).ToArray();
        var nextBefore = dtos.Length == effectiveLimit ? dtos[^1].Timestamp : (long?)null;

        return Json(new EntryListResponse(dtos, nextBefore));
    }

    private static async Task<IResult> GetEntryAsync(
        HttpContext ctx,
        SqliteLookoutStorage storage,
        Guid id)
    {
        var entry = await storage.GetByIdAsync(id, ctx.RequestAborted).ConfigureAwait(false);
        return entry is null ? Results.NotFound() : Json(EntryDto.From(entry));
    }

    private static async Task<IResult> GetRequestEntriesAsync(
        HttpContext ctx,
        SqliteLookoutStorage storage,
        string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Results.NotFound();

        var entries = await storage.GetByRequestIdAsync(id, ctx.RequestAborted).ConfigureAwait(false);
        if (entries.Count == 0)
            return Results.NotFound();

        var dtos = entries.Select(EntryDto.From).ToArray();
        return Json(dtos);
    }

    private static async Task<IResult> SearchEntriesAsync(
        HttpContext ctx,
        SqliteLookoutStorage storage,
        string? q = null,
        int? limit = null)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Json(Array.Empty<SearchResultDto>());

        var effectiveLimit = Math.Clamp(limit ?? 50, 1, 100);
        ctx.Response.Headers["Cache-Control"] = "no-store";

        IReadOnlyList<SearchResult> results;
        try
        {
            results = await storage.SearchWithSnippetAsync(q, effectiveLimit, ctx.RequestAborted)
                .ConfigureAwait(false);
        }
        catch
        {
            results = Array.Empty<SearchResult>();
        }

        var dtos = results.Select(r => new SearchResultDto(
            r.Id, r.Type, r.Timestamp.ToUnixTimeMilliseconds(), r.RequestId, r.Snippet)).ToArray();
        return Json(dtos);
    }

    private static async Task<IResult> GetLogHistogramAsync(
        HttpContext ctx,
        SqliteLookoutStorage storage,
        int? bucket_count = null)
    {
        var effectiveBuckets = Math.Clamp(bucket_count ?? 12, 2, 60);
        ctx.Response.Headers["Cache-Control"] = "no-store";

        var buckets = await storage.GetLogHistogramAsync(effectiveBuckets, ctx.RequestAborted)
            .ConfigureAwait(false);

        var dtos = buckets.Select(b => new
        {
            from = b.From,
            to = b.To,
            byLevel = new
            {
                trace = b.Trace,
                debug = b.Debug,
                information = b.Information,
                warning = b.Warning,
                error = b.Error,
                critical = b.Critical,
            },
        }).ToArray();

        return Json(dtos);
    }

    private static async Task<IResult> GetCacheByKeyAsync(
        HttpContext ctx,
        SqliteLookoutStorage storage,
        int? limit = null)
    {
        var effectiveLimit = Math.Clamp(limit ?? 10, 1, 50);
        ctx.Response.Headers["Cache-Control"] = "no-store";

        var stats = await storage.GetCacheByKeyAsync(effectiveLimit, ctx.RequestAborted)
            .ConfigureAwait(false);

        return Json(stats.Select(s => new { key = s.Key, hits = s.Hits, misses = s.Misses, sets = s.Sets, hitRatio = s.HitRatio }).ToArray());
    }

    private static IResult GetHostInfoAsync()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos"
            : "linux";
        return Json(new { os });
    }

    private static async Task<IResult> DeleteAllEntriesAsync(
        HttpContext ctx,
        SqliteLookoutStorage storage,
        LookoutMountInfo mount)
    {
        if (!IsSameOrigin(ctx) || !IsValidCsrf(ctx, mount))
            return Results.Json(new { error = "csrf" }, statusCode: StatusCodes.Status403Forbidden);

        var deleted = await storage.DeleteAllAsync(ctx.RequestAborted).ConfigureAwait(false);
        return Json(new { deleted });
    }

    private static bool IsSameOrigin(HttpContext ctx)
    {
        var origin = ctx.Request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrEmpty(origin)) return false;
        var host = ctx.Request.Host.ToString();
        if (Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
            return string.Equals(originUri.Authority, host, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private static bool IsValidCsrf(HttpContext ctx, LookoutMountInfo mount)
    {
        var header = ctx.Request.Headers["X-Lookout-Csrf-Token"].FirstOrDefault();
        return !string.IsNullOrEmpty(header)
            && string.Equals(header, mount.CsrfToken, StringComparison.Ordinal);
    }

    private static IResult Json<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, LookoutJson.Options);
        return Results.Bytes(bytes, "application/json; charset=utf-8");
    }

    private static bool TryParseStatus(string? value, out int? min, out int? max)
    {
        min = null;
        max = null;
        if (string.IsNullOrWhiteSpace(value)) return true;

        var dash = value.IndexOf('-');
        if (dash < 0)
        {
            if (!int.TryParse(value, out var exact)) return false;
            min = max = exact;
            return true;
        }

        var left = value.AsSpan(0, dash).Trim();
        var right = value.AsSpan(dash + 1).Trim();
        if (!int.TryParse(left, out var lo) || !int.TryParse(right, out var hi)) return false;
        if (hi < lo) return false;
        min = lo;
        max = hi;
        return true;
    }
}
