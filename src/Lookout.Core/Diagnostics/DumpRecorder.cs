namespace Lookout.Core.Diagnostics;

/// <summary>
/// AsyncLocal holder that makes <see cref="ILookoutRecorder"/> available to
/// <see cref="Lookout.Dump"/> without requiring DI on the call site.
/// Set by <c>LookoutRequestMiddleware</c> at request start; cleared in the finally block.
/// Out-of-request callers see <c>null</c> and <see cref="Lookout.Dump"/> becomes a no-op.
/// </summary>
internal static class DumpRecorder
{
    private static readonly AsyncLocal<DumpContext?> _current = new();

    internal static ILookoutRecorder? Recorder => _current.Value?.Recorder;
    internal static int MaxBytes => _current.Value?.MaxBytes ?? 65_536;

    internal static void Set(ILookoutRecorder recorder, int maxBytes) =>
        _current.Value = new DumpContext(recorder, maxBytes);

    internal static void Clear() =>
        _current.Value = null;

    private sealed record DumpContext(ILookoutRecorder Recorder, int MaxBytes);
}
