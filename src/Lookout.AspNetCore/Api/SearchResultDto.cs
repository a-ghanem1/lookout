namespace Lookout.AspNetCore.Api;

/// <summary>A ranked FTS5 search hit returned from <c>GET /lookout/api/search</c>.</summary>
public sealed record SearchResultDto(
    Guid Id,
    string Type,
    long Timestamp,
    string? RequestId,
    string Snippet);
