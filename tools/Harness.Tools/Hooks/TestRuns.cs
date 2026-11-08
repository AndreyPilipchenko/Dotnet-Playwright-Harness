using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Harness.Tools.Hooks;

/// <summary>Recognises <c>dotnet test</c> commands and failed runs in their output.</summary>
internal static partial class TestRuns
{
    /// <summary>
    /// A stable hash of the first <c>dotnet test</c> part of a command. Null when there is none.
    /// See <see cref="Fingerprints"/> for what is ignored.
    /// </summary>
    public static string? Fingerprint(string command) => Fingerprints(command) is [var first, ..] ? first : null;

    /// <summary>
    /// A stable hash of each <c>dotnet test</c> part of a command, in order. Whitespace, output
    /// redirection, anything chained or piped around it, and options that don't change which tests
    /// run (<c>--results-directory</c>, <c>--report-trx</c>, <c>--no-build</c>, <c>--no-restore</c>)
    /// are ignored, so a new results folder does not make a failed run look new.
    /// </summary>
    public static IReadOnlyList<string> Fingerprints(string command) => Fingerprints(command, null, null);

    /// <summary>
    /// Like <see cref="Fingerprints(string)"/>, but only for runs that happen inside
    /// <paramref name="projectRoot"/>. The run's folder starts at <paramref name="workingFolder"/>
    /// (the hook payload's <c>cwd</c>, when known) and follows <c>cd</c>, <c>Set-Location</c> and
    /// <c>Push-Location</c> segments. A run in a scratch copy outside the project, such as break-it-once,
    /// is left out, so it neither blocks nor is blocked by the project's own runs. An unknown folder
    /// counts as inside.
    /// </summary>
    public static IReadOnlyList<string> Fingerprints(string command, string? workingFolder, string? projectRoot)
    {
        var settings = RunSettings(command);
        var fingerprints = new List<string>();
        var folder = workingFolder;
        foreach (var segment in ShellText.Segments(command))
        {
            if (folder is not null && ChangeDirectory().Match(segment) is { Success: true } cd)
            {
                folder = Resolve(folder, cd.Groups["path"].Value) ?? folder;
                continue;
            }

            var match = DotnetTest().Match(segment);
            if (!match.Success)
            {
                continue;
            }

            if (folder is not null && projectRoot is not null && !IsInside(folder, projectRoot))
            {
                continue;
            }

            var text = Redirection().Replace(match.Value, string.Empty);
            text = ResultsDirectory().Replace(text, string.Empty);
            text = SelectionNeutralFlag().Replace(text, string.Empty);
            text = Whitespace().Replace(text, " ").Trim() + settings;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            fingerprints.Add(Convert.ToHexStringLower(hash)[..16]);
        }

        return fingerprints;
    }

    /// <summary>
    /// The <c>BROWSER</c> and <c>HEADED</c> values the command sets (PowerShell <c>$env:X = 'v'</c> or a
    /// POSIX <c>X=v</c> prefix), as a suffix such as <c>|BROWSER=firefox</c>. They change how the tests
    /// run, so a diagnostic run in another browser is a different run. Other variables don't count.
    /// </summary>
    private static string RunSettings(string command)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in RunSetting().Matches(command))
        {
            values[match.Groups["name"].Value.ToUpperInvariant()] = match.Groups["value"].Value.ToLowerInvariant();
        }

        return string.Concat(values.Select(kv => $"|{kv.Key}={kv.Value}"));
    }

    /// <summary>The folder a <c>cd</c> target leads to, or null when it can't be read (a variable, <c>-</c>, a bad path).</summary>
    private static string? Resolve(string from, string target)
    {
        target = target.Trim().Trim('"', '\'');
        if (target.Length == 0 || target == "-" || target.Contains('$', StringComparison.Ordinal) || target.Contains('%', StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(target, from);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsInside(string folder, string root)
    {
        var relative = Path.GetRelativePath(root, folder);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith("../", StringComparison.Ordinal);
    }

    /// <summary>
    /// True when output shows failed or aborted tests (native MTP or VSTest summary) or a failed
    /// build. Only summary lines are read, so a test named "ShowsFailedLogin" does not count.
    /// </summary>
    public static bool LooksFailed(string output) =>
        !string.IsNullOrEmpty(output) && Failure().IsMatch(output);

    [GeneratedRegex(@"^\s*(?:cd|chdir|sl|Set-Location|Push-Location)\s+(?:-(?:Path|LiteralPath)\s+)?(?<path>""[^""]*""|'[^']*'|\S+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ChangeDirectory();

    [GeneratedRegex(@"\bdotnet\s+test\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex DotnetTest();

    [GeneratedRegex(@"\s+[\d*]?>>?\s*(?:&\d|\S+)")]
    private static partial Regex Redirection();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex("""\s--results-directory(?:=|\s+)(?:"[^"]*"|'[^']*'|\S+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ResultsDirectory();

    [GeneratedRegex(@"\s--(?:report-trx|no-build|no-restore)(?=\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex SelectionNeutralFlag();

    [GeneratedRegex("""(?:\$env:|(?<![\w$:]))(?<name>BROWSER|HEADED)\s*=\s*["']?(?<value>[\w.-]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex RunSetting();

    [GeneratedRegex(@"Test run summary:\s*(?:Failed|Aborted)|^\s*failed:\s*[1-9]|^\s*Failed!\s+-\s+Failed:\s+[1-9]|\bBuild FAILED\b|\berror (?:CS|MSB|NU|NETSDK)\d{3,5}\b", RegexOptions.Multiline)]
    private static partial Regex Failure();
}
