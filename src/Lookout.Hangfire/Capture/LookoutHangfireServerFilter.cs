using Hangfire.Server;

namespace Lookout.Hangfire;

/// <summary>Server-side Hangfire filter — captures job execution lifecycle. Implemented in M8.3.</summary>
internal sealed class LookoutHangfireServerFilter : IServerFilter
{
    public void OnPerforming(PerformingContext filterContext) { }
    public void OnPerformed(PerformedContext filterContext) { }
}
