namespace Lookout.Core;

/// <summary>
/// Records a diagnostic entry into the capture pipeline.
/// Implementations must be fire-and-forget: <see cref="Record"/> must never block the caller.
/// </summary>
public interface ILookoutRecorder
{
    /// <summary>Enqueues <paramref name="entry"/> for asynchronous persistence. Never blocks.</summary>
    void Record(LookoutEntry entry);
}
