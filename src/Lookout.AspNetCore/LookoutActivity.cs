using System.Diagnostics;

namespace Lookout.AspNetCore;

/// <summary>
/// Dedicated <see cref="ActivitySource"/> used by Lookout to ensure an <see cref="Activity"/>
/// is current for every captured request so <c>Activity.Current.RootId</c> can serve as
/// the correlation key.
/// </summary>
internal static class LookoutActivity
{
    public const string SourceName = "Lookout.AspNetCore";

    public static readonly ActivitySource Source = new(SourceName);

    static LookoutActivity()
    {
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = s => s.Name == SourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
        });
    }
}
