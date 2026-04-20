namespace Lookout.Core;

/// <summary>Abstraction over the persistence layer consumed by the background flusher.</summary>
public interface ILookoutStorage
{
    Task WriteAsync(IReadOnlyList<LookoutEntry> entries, CancellationToken ct = default);
}
