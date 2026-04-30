namespace Lookout.AspNetCore.Api;

/// <summary>Per-section entry counts returned by <c>GET /lookout/api/entries/counts</c>.</summary>
public sealed record EntryCounts(
    long Requests,
    long Queries,
    long Exceptions,
    long Logs,
    long Cache,
    long HttpClients,
    long Jobs,
    long Dump);
