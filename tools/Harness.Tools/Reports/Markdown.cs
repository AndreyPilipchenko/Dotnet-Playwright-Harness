using System.Text.RegularExpressions;

namespace Harness.Tools.Reports;

internal static partial class Markdown
{
    /// <summary>Makes text safe for one table cell: entities escaped, whitespace on one line.</summary>
    public static string Cell(string text) =>
        Whitespace().Replace(text, " ").Trim()
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("|", "&#124;", StringComparison.Ordinal);

    public static string CollapseWhitespace(string text) => Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
