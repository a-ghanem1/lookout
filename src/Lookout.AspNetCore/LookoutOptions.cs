namespace Lookout.AspNetCore;

/// <summary>Options for configuring Lookout.</summary>
public sealed class LookoutOptions
{
    /// <summary>
    /// Environments in which Lookout is permitted to run. Default: ["Development"].
    /// </summary>
    public IList<string> AllowInEnvironments { get; set; } = ["Development"];

    /// <summary>
    /// When true, Lookout is allowed to run in any environment, including Production.
    /// Intended as an explicit opt-in escape hatch — logs a startup warning when used.
    /// </summary>
    public bool AllowInProduction { get; set; }
}
