namespace Lookout.AspNetCore;

/// <summary>Options for configuring Lookout.</summary>
public sealed class LookoutOptions
{
    /// <summary>
    /// Environments in which Lookout is permitted to run.
    /// Reserved — not yet wired. Default: ["Development"].
    /// </summary>
    public IList<string> AllowInEnvironments { get; set; } = ["Development"];

    /// <summary>
    /// When true, Lookout is allowed to run in any environment, including Production.
    /// Reserved — not yet wired. Intended as an explicit opt-in escape hatch.
    /// </summary>
    public bool AllowInProduction { get; set; }
}
