using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Harness.Tools.Hooks;

/// <summary>Recognises <c>dotnet test</c> commands and failed runs in their output.</summary>
internal static partial class TestRuns
{
    /// <summary>
    /// A stable hash of the <c>dotnet test</c> part of a command, ignoring whitespace, output
    /// redirection, and anything chained before or piped after it. Null when there is none.
    /// </summary>
    public static string? Fingerprint(string command)
    {
        foreach (var segment in ShellText.Segments(command))
        {
            var match = DotnetTest().Match(segment);
            if (!match.Success)
            {
                continue;
            }

            var text = Redirection().Replace(match.Value, string.Empty);
            text = Whitespace().Replace(text, " ").Trim();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            return Convert.ToHexStringLower(hash)[..16];
        }

        return null;
    }

    /// <summary>
    /// True when output shows failed or aborted tests (native MTP or VSTest summary) or a failed
    /// build. Only summary lines are read, so a test named "ShowsFailedLogin" does not count.
    /// </summary>
    public static bool LooksFailed(string output) =>
        !string.IsNullOrEmpty(output) && Failure().IsMatch(output);

    [GeneratedRegex(@"\bdotnet\s+test\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex DotnetTest();

    [GeneratedRegex(@"\s+[\d*]?>>?\s*(?:&\d|\S+)")]
    private static partial Regex Redirection();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"Test run summary:\s*(?:Failed|Aborted)|^\s*failed:\s*[1-9]|^\s*Failed!\s+-\s+Failed:\s+[1-9]|\bBuild FAILED\b|\berror (?:CS|MSB|NU|NETSDK)\d{3,5}\b", RegexOptions.Multiline)]
    private static partial Regex Failure();
}
