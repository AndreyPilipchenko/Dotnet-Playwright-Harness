using System.Text.RegularExpressions;

namespace Harness.Tools.Hooks;

internal enum Verdict
{
    Allow,
    Ask,
    Deny,
}

internal sealed record ShellDecision(Verdict Verdict, string Rule, string Reason)
{
    public static ShellDecision Allowed { get; } = new(Verdict.Allow, string.Empty, string.Empty);
}

/// <summary>
/// Decides whether a shell command may run. Deny is for catastrophic or irreversible commands;
/// ask is for destructive or outward-facing ones that are sometimes right, so a person signs off.
/// Every deny rule is checked before any ask rule. Rules read both the raw command and a form with
/// git's <c>-C</c>/<c>-c</c> option values removed, so neither form can hide a command.
/// </summary>
internal static partial class ShellRules
{
    private sealed record Rule(string Id, Regex Pattern, string Reason);

    private static readonly Rule[] DenyRules =
    [
        new("pipe-to-shell", PipeToShell(), "runs code downloaded from the network"),
        new("publish-package", Publish(), "publishes a package, which cannot be undone"),
        new("sql-destroy", SqlDestroy(), "DROP or TRUNCATE destroys database objects or all their rows"),
        new("disk-format", DiskFormat(), "formats or overwrites a disk"),
    ];

    private static readonly Rule[] AskRules =
    [
        new("git-reset-hard", GitResetHard(), "git reset --hard discards uncommitted work"),
        new("git-discard-changes", GitDiscard(), "this git checkout/restore discards uncommitted changes"),
        new("git-clean", GitClean(), "git clean -f deletes untracked files permanently"),
        new("git-branch-force-delete", GitBranchForceDelete(), "force-deletes a branch without checking that it is merged"),
        new("git-push-force", GitPushForce(), "force push rewrites the remote branch"),
        new("git-stash-drop", GitStashDrop(), "drops stashed changes"),
        new("git-commit", GitCommit(), "git commit: the harness commits only when the user asks"),
        new("git-push", GitPush(), "git push: the harness pushes only when the user asks"),
        new("delete-files", DeleteFiles(), "recursive or forced delete"),
        new("kill-process", KillProcess(), "force-kills a process"),
        new("env-dump", EnvDump(), "prints environment variables, which may contain secrets"),
        new("secret-in-command", SecretInCommand(), "the command line mentions a password, token or key"),
        new("sql-delete", SqlDelete(), "DELETE FROM removes database rows"),
        new("gh-write", GhWrite(), "this gh command changes GitHub (runs, PRs, issues, releases, secrets or the repo)"),
    ];

    public static ShellDecision Evaluate(string command, Func<string?> currentBranch, bool allowCommitPush)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return ShellDecision.Allowed;
        }

        var judged = GitGlobalOptions().Replace(command, "git ");
        string[] texts = judged == command ? [command] : [judged, command];

        if (ShellText.DeletesARoot(command))
        {
            return new(Verdict.Deny, "delete-root", "recursively deletes a root, drive, home or whole working folder");
        }

        if (PushGuard.TargetsDefaultBranch(command, currentBranch))
        {
            return new(Verdict.Deny, "push-default-branch",
                "pushes to main/master. Push a named branch (automation/, harness/, feature/, fix/) and open a pull request");
        }

        foreach (var rule in DenyRules)
        {
            if (texts.Any(rule.Pattern.IsMatch))
            {
                return new(Verdict.Deny, rule.Id, rule.Reason);
            }
        }

        foreach (var rule in AskRules)
        {
            if (allowCommitPush && rule.Id is "git-commit" or "git-push")
            {
                continue;
            }

            if (texts.Any(rule.Pattern.IsMatch))
            {
                return new(Verdict.Ask, rule.Id, rule.Reason);
            }
        }

        return ShellDecision.Allowed;
    }

    private const RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [GeneratedRegex(@"\bgit((?:\s+-[Cc]\s+(?:""[^""]*""|'[^']*'|\S+))+)\s+(?=\S)")]
    private static partial Regex GitGlobalOptions();

    [GeneratedRegex(@"\b(?:curl|wget|iwr|irm|invoke-webrequest|invoke-restmethod)\b.*\|\s*(?:sudo\s+)?(?:ba|z|da)?sh\b|\b(?:curl|wget|iwr|irm|invoke-webrequest|invoke-restmethod)\b.*\|\s*(?:iex|invoke-expression)\b|\b(?:iex|invoke-expression)\b.*\b(?:downloadstring|invoke-webrequest|invoke-restmethod|iwr|irm|net\.webclient)\b", I)]
    private static partial Regex PipeToShell();

    [GeneratedRegex(@"\bdotnet\s+nuget\s+push\b|\bnpm\s+publish\b", I)]
    private static partial Regex Publish();

    [GeneratedRegex(@"\bdrop\s+(?:table|database|schema|index|view|procedure)\b|\btruncate\s+table\b", I)]
    private static partial Regex SqlDestroy();

    [GeneratedRegex(@"\bformat\s+[a-z]:|\bformat-volume\b|\bmkfs(?:\.\w+)?\b|\bdiskpart\b|\bdd\s+.*\bof=/dev/", I)]
    private static partial Regex DiskFormat();

    [GeneratedRegex(@"\bgit\s+reset\b.*\s--hard\b", I)]
    private static partial Regex GitResetHard();

    [GeneratedRegex(@"\bgit\s+checkout\s+(?:\S+\s+)?--(?:\s|$)|\bgit\s+checkout\s+\.(?:\s|$)|\bgit\s+restore\s+(?:.*\s)?\.(?:\s|$)", I)]
    private static partial Regex GitDiscard();

    [GeneratedRegex(@"\bgit\s+clean\b.*\s(?:-[a-z]*f|--force\b)", I)]
    private static partial Regex GitClean();

    // Case-sensitive on purpose: -D force-deletes, -d does not.
    [GeneratedRegex(@"\bgit\s+branch\b(?:(?=.*\s-\w*D\b)|(?=.*\s(?:--delete\b|-[a-z]*d[a-z]*\b))(?=.*\s(?:--force\b|-[a-z]*f[a-z]*\b)))")]
    private static partial Regex GitBranchForceDelete();

    [GeneratedRegex(@"\bgit\s+push\b.*(?:\s--force(?:-with-lease)?\b|\s-f\b|\s\+\S)", I)]
    private static partial Regex GitPushForce();

    [GeneratedRegex(@"\bgit\s+stash\s+(?:drop|clear)\b", I)]
    private static partial Regex GitStashDrop();

    [GeneratedRegex(@"\bgit\s+commit\b", I)]
    private static partial Regex GitCommit();

    [GeneratedRegex(@"\bgit\s+push\b", I)]
    private static partial Regex GitPush();

    [GeneratedRegex(@"\brm\s+(?:[^|;&]*\s)?-[a-z]*[rf]|\bremove-item\b.*\s-(?:r|rec\w*|fo|for\w*)\b|\b(?:del|erase)\b.*\s/[fs]\b|\b(?:rmdir|rd)\b.*\s/s\b", I)]
    private static partial Regex DeleteFiles();

    [GeneratedRegex(@"\btaskkill\b.*\s/f\b|\bkill\s+-(?:9|kill|sigkill)\b|\bstop-process\b.*\s-fo\w*", I)]
    private static partial Regex KillProcess();

    [GeneratedRegex(@"\bprintenv\b|\b(?:gci|get-childitem|dir|ls)\s+env:", I)]
    private static partial Regex EnvDump();

    [GeneratedRegex(@"\b(?:curl|wget|iwr|irm|invoke-webrequest|invoke-restmethod)\b.*\b(?:password|passwd|secret|api.?key|token)\b|\b(?:echo|write-host|write-output|printf)\b.*\b(?:password|secret|api.?key|token|connection.?string)\b", I)]
    private static partial Regex SecretInCommand();

    [GeneratedRegex(@"\bdelete\s+from\b", I)]
    private static partial Regex SqlDelete();

    [GeneratedRegex(@"\bgh\s+(?:workflow\s+(?:run|enable|disable)|run\s+(?:rerun|cancel|delete)|pr\s+(?:create|merge|comment|close|reopen|edit|review|ready)|issue\s+(?:create|close|reopen|comment|edit|delete|transfer)|release\s+(?:create|delete|edit|upload|delete-asset)|repo\s+(?:create|delete|edit|rename|archive|fork|sync)|(?:secret|variable)\s+(?:set|delete|remove))\b|\bgh\s+api\b.*(?:\s-X\s*(?:POST|PUT|PATCH|DELETE)\b|\s--method[\s=]+(?:POST|PUT|PATCH|DELETE)\b|\s-f\s|\s--(?:field|raw-field|input)\b)", I)]
    private static partial Regex GhWrite();
}
