using System.Text.RegularExpressions;

namespace Harness.Tools.Hooks;

/// <summary>Small, conservative helpers for reading shell command lines (bash, PowerShell, cmd).</summary>
internal static partial class ShellText
{
    private static readonly char[] QuoteChars = ['"', '\'', ')', ';', '`'];

    /// <summary>Splits a command line into the commands chained by <c>&amp;&amp;</c>, <c>||</c>, <c>;</c>, <c>|</c> or newlines.</summary>
    public static string[] Segments(string command) => SegmentSeparators().Split(command);

    /// <summary>
    /// Like <see cref="Segments"/>, but also splits at sub-shells and parentheses, so a command
    /// hidden in <c>$(...)</c> or backticks is looked at on its own.
    /// </summary>
    public static string[] DeepSegments(string command) => DeepSeparators().Split(command);

    /// <summary>Whitespace tokens with surrounding quotes and closing brackets removed; empty tokens dropped.</summary>
    public static List<string> Tokens(string segment) =>
        segment.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim(QuoteChars))
            .Where(t => t.Length > 0)
            .ToList();

    /// <summary>The command name without a directory or <c>.exe</c>, for example <c>/bin/rm</c> becomes <c>rm</c>.</summary>
    public static string CommandName(string token)
    {
        var name = token[(token.LastIndexOfAny(['/', '\\']) + 1)..];
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    /// <summary>
    /// True when a segment recursively deletes a filesystem root, a drive, the home folder,
    /// the current or parent folder, or everything matched by <c>*</c>.
    /// </summary>
    public static bool DeletesARoot(string command)
    {
        foreach (var segment in DeepSegments(command))
        {
            var tokens = Tokens(segment);
            if (tokens.Count > 0 && tokens[0].Equals("sudo", StringComparison.OrdinalIgnoreCase))
            {
                tokens.RemoveAt(0);
            }

            if (tokens.Count < 2)
            {
                continue;
            }

            var name = CommandName(tokens[0]);
            var args = tokens.Skip(1).ToList();
            bool recursive;
            IEnumerable<string> targets;

            if (name.Equals("rm", StringComparison.OrdinalIgnoreCase))
            {
                // bash: -r, -R, -rf, --recursive. PowerShell's rm alias: -r, -Recurse.
                recursive = args.Any(a =>
                    (a.StartsWith('-') && !a.StartsWith("--", StringComparison.Ordinal) && a.Contains('r', StringComparison.OrdinalIgnoreCase))
                    || a.Equals("--recursive", StringComparison.OrdinalIgnoreCase));
                targets = args.Where(a => !a.StartsWith('-'));
            }
            else if (IsWindowsDelete(name))
            {
                recursive = args.Any(a =>
                    a.StartsWith("-r", StringComparison.OrdinalIgnoreCase) || a.Equals("/s", StringComparison.OrdinalIgnoreCase));
                targets = args.Where(a => !a.StartsWith('-') && !CmdSwitch().IsMatch(a));
            }
            else
            {
                continue;
            }

            if (recursive && targets.Any(IsRootTarget))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWindowsDelete(string name) =>
        name.Equals("remove-item", StringComparison.OrdinalIgnoreCase)
        || name.Equals("ri", StringComparison.OrdinalIgnoreCase)
        || name.Equals("rmdir", StringComparison.OrdinalIgnoreCase)
        || name.Equals("rd", StringComparison.OrdinalIgnoreCase)
        || name.Equals("del", StringComparison.OrdinalIgnoreCase)
        || name.Equals("erase", StringComparison.OrdinalIgnoreCase);

    private static bool IsRootTarget(string target)
    {
        var t = target.TrimEnd('*').TrimEnd('/', '\\');
        return t.Length == 0
            || t is "~" or "." or ".."
            || t.Equals("$HOME", StringComparison.OrdinalIgnoreCase)
            || t.Equals("${HOME}", StringComparison.OrdinalIgnoreCase)
            || t.Equals("$env:USERPROFILE", StringComparison.OrdinalIgnoreCase)
            || t.Equals("$env:HOME", StringComparison.OrdinalIgnoreCase)
            || t.Equals("$env:HOMEDRIVE", StringComparison.OrdinalIgnoreCase)
            || t.Equals("$env:SystemDrive", StringComparison.OrdinalIgnoreCase)
            || DriveRoot().IsMatch(t);
    }

    [GeneratedRegex(@"&&|\|\||;|\||\r?\n")]
    private static partial Regex SegmentSeparators();

    [GeneratedRegex(@"&&|\|\||;|\||\r?\n|\$\(|`|\(")]
    private static partial Regex DeepSeparators();

    [GeneratedRegex(@"^/[a-zA-Z]$")]
    private static partial Regex CmdSwitch();

    [GeneratedRegex(@"^[a-zA-Z]:$")]
    private static partial Regex DriveRoot();
}
