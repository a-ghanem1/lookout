namespace Lookout.AspNetCore.Api;

/// <summary>Paginated response envelope for <c>GET /lookout/api/entries</c>.</summary>
/// <param name="Entries">Entries ordered newest first.</param>
/// <param name="NextBefore">
/// Cursor to pass as <c>before</c> on the next request, or <c>null</c> when the page did not
/// reach <c>limit</c>. Set to the timestamp (unix ms) of the last entry in <see cref="Entries"/>.
/// </param>
public sealed record EntryListResponse(
    IReadOnlyList<EntryDto> Entries,
    long? NextBefore);
