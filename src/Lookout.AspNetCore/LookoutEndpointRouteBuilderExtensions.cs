using System.Text.Json;
using Lookout.AspNetCore.Api;
using Lookout.Core;
using Lookout.Core.Schemas;
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
        group.MapGet("/api/entries/{id:guid}", GetEntryAsync);
        group.MapGet("/api/requests/{id}", GetRequestEntriesAsync);

        endpoints.MapGet(pathPrefix, () => Results.Content("Lookout is running.", "text/plain"));

        return group;
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
        int? limit = null)
    {
        if (!TryParseStatus(status, out var statusMin, out var statusMax))
            return Results.Text(
                "invalid status filter; expected '500' or '400-499'",
                contentType: "text/plain",
                statusCode: StatusCodes.Status400BadRequest);

        var effectiveLimit = Math.Clamp(limit ?? 50, 1, 200);

        var query = new LookoutQuery(
            Type: type,
            Method: method,
            StatusMin: statusMin,
            StatusMax: statusMax,
            Path: path,
            Q: q,
            BeforeUnixMs: before,
            Limit: effectiveLimit);

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
