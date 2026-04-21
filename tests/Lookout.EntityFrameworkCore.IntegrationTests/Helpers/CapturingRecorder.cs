using Lookout.Core;

namespace Lookout.EntityFrameworkCore.IntegrationTests.Helpers;

internal sealed class CapturingRecorder : ILookoutRecorder
{
    private readonly List<LookoutEntry> _entries = new();

    public IReadOnlyList<LookoutEntry> Entries
    {
        get { lock (_entries) return _entries.ToList(); }
    }

    public void Record(LookoutEntry entry) { lock (_entries) _entries.Add(entry); }

    public void Clear() { lock (_entries) _entries.Clear(); }
}
