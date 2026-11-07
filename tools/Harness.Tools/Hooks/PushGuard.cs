using System.Text.RegularExpressions;

namespace Harness.Tools.Hooks;

/// <summary>
/// Detects a <c>git push</c> that would land on main or master. <c>push</c> must be git's
/// subcommand, so <c>git stash push</c> never matches. Only the destination side of a
/// <c>src:dst</c> refspec counts. A bare push (or <c>HEAD</c>) uses the current branch, except
/// with <c>git -C &lt;dir&gt;</c>, which pushes another repository.
/// Behaviour follows pom-harness's guard (MIT), rewritten in C#.
/// </summary>
internal static partial class PushGuard
{
    private static readonly char[] TrimChars = ['"', '\'', ')', '`'];

    public static bool TargetsDefaultBranch(string command, Func<string?> currentBranch)
    {
        string? branch = null;
        var branchKnown = false;
        bool CurrentIsDefault()
        {
            if (!branchKnown)
            {
                branch = currentBranch();
                branchKnown = true;
            }

            return branch is not null && IsDefault(branch);
        }

        foreach (var segment in ShellText.Segments(command))
        {
            foreach (Match match in GitPush().Matches(segment))
            {
                var otherRepo = OtherRepo().IsMatch(match.Groups["opts"].Value);
                var args = match.Groups["rest"].Value
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim(TrimChars))
                    .Where(t => t.Length > 0 && !t.StartsWith('-'))
                    .ToList();
                var refspecs = args.Skip(1).ToList(); // args[0] is the remote

                if (refspecs.Count == 0)
                {
                    if (!otherRepo && CurrentIsDefault())
                    {
                        return true;
                    }

                    continue;
                }

                foreach (var refspec in refspecs)
                {
                    var destination = refspec.TrimStart('+');
                    var colon = destination.LastIndexOf(':');
                    if (colon >= 0)
                    {
                        destination = destination[(colon + 1)..];
                    }

                    if (destination == "HEAD")
                    {
                        if (!otherRepo && CurrentIsDefault())
                        {
                            return true;
                        }

                        continue;
                    }

                    if (IsDefault(destination))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool IsDefault(string name) => DefaultBranch().IsMatch(name);

    [GeneratedRegex(@"\bgit(?:\.exe)?\s+(?<opts>(?:-C\s+\S+\s+|-{1,2}[^\s=]+(?:=\S+)?\s+)*)push\b(?<rest>.*)$")]
    private static partial Regex GitPush();

    [GeneratedRegex(@"(?:^|\s)-C\s")]
    private static partial Regex OtherRepo();

    [GeneratedRegex(@"^(?:refs/heads/)?(?:main|master)$", RegexOptions.IgnoreCase)]
    private static partial Regex DefaultBranch();
}
