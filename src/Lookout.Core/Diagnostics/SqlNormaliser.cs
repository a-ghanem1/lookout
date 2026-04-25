using System.Text.RegularExpressions;

namespace Lookout.Core.Diagnostics;

/// <summary>
/// Produces a stable "shape key" from a SQL string suitable for grouping structurally
/// identical queries (e.g. N+1 detection) regardless of parameter values or whitespace.
/// </summary>
public static class SqlNormaliser
{
    // Step 1 — quoted string literals ('...', handles '' escaping inside)
    private static readonly Regex StringLiteral =
        new(@"'(?:[^']|'')*'", RegexOptions.Compiled);

    // Step 2 — parameterised placeholders: @name, @p0, $1, :name, ?
    private static readonly Regex ParamPlaceholder =
        new(@"@\w+|\$\d+|:\w+|\?", RegexOptions.Compiled);

    // Step 3 — numeric literals (integers and decimals)
    private static readonly Regex NumericLiteral =
        new(@"\b\d+(?:\.\d+)?\b", RegexOptions.Compiled);

    // Step 4 — runs of whitespace
    private static readonly Regex WhitespaceRun =
        new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Normalises <paramref name="sql"/> into a deterministic shape key.
    /// <list type="bullet">
    ///   <item>Replaces string literals and parameter placeholders with <c>?</c>.</item>
    ///   <item>Replaces numeric literals with <c>?</c>.</item>
    ///   <item>Collapses whitespace to a single space and trims.</item>
    ///   <item>Case-folds the result to uppercase.</item>
    /// </list>
    /// Identical structural queries produce identical shape keys.
    /// </summary>
    public static string Normalise(string sql)
    {
        if (string.IsNullOrEmpty(sql))
            return string.Empty;

        // Order matters: replace string literals first so their contents are not
        // mis-identified as parameters or numbers.
        var result = StringLiteral.Replace(sql, "?");
        result = ParamPlaceholder.Replace(result, "?");
        result = NumericLiteral.Replace(result, "?");
        result = WhitespaceRun.Replace(result, " ").Trim();
        return result.ToUpperInvariant();
    }
}
