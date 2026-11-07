using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harness.Tools.Conventions;

namespace Harness.Tools.Hooks;

/// <summary>
/// The hook modes. Protocol: a deny exits 2 with the reason on stderr; an ask writes Claude Code's
/// <c>permissionDecision: ask</c> JSON to stdout and exits 0; for SessionStart, stdout is added to
/// the conversation as context.
/// </summary>
internal static class HookModes
{
    public const string AllowCommitPushVariable = "HARNESS_ALLOW_GIT_COMMIT_PUSH";

    private static readonly string[] DocExtensions = [".md", ".mdx", ".rst", ".txt"];
    private static readonly string[] BookkeepingFolders = [".harness/", ".claude/", "docs/", "TestResults/", "../"];

    public static HookDecision PreShell(HookInput input)
    {
        var command = String(input.Payload, "tool_input", "command");
        if (string.IsNullOrWhiteSpace(command))
        {
            return HookDecision.Allow;
        }

        var ctx = input.Context;
        var allowCommitPush = ctx.Services.GetEnv(AllowCommitPushVariable) == "1";
        var decision = ShellRules.Evaluate(command, () => ctx.Services.CurrentBranch(ctx.ProjectRoot), allowCommitPush);

        if (decision.Verdict == Verdict.Deny)
        {
            return Deny(input, decision.Rule, decision.Reason);
        }

        var fingerprint = TestRuns.Fingerprint(command);
        if (fingerprint is not null)
        {
            var ledger = ctx.State.ReadLedger(ctx.SessionId);
            if (ledger.LastFailFingerprint == fingerprint && !ledger.EditsSinceFail)
            {
                return Deny(input, "test-rerun-unchanged",
                    "this exact dotnet test command already failed and nothing has been edited since. " +
                    "Fix the code or data, or classify the failure (script defect, product defect or environment) " +
                    "instead of rerunning it unchanged. To rerun on purpose, for example for a suspected flaky test, " +
                    "change the command, such as adding a filter for that one test");
            }
        }

        if (decision.Verdict == Verdict.Ask)
        {
            Ask(input, decision.Rule, decision.Reason);
        }

        return HookDecision.Allow;
    }

    public static HookDecision PostShell(HookInput input)
    {
        var fingerprint = TestRuns.Fingerprint(String(input.Payload, "tool_input", "command") ?? string.Empty);
        if (fingerprint is null)
        {
            return HookDecision.Allow;
        }

        var ctx = input.Context;
        var ledger = ctx.State.ReadLedger(ctx.SessionId);
        if (TestRuns.LooksFailed(ResponseText(input.Payload)))
        {
            ctx.State.WriteLedger(ctx.SessionId, new RerunLedger(fingerprint, EditsSinceFail: false));
        }
        else if (ledger.LastFailFingerprint == fingerprint)
        {
            ctx.State.WriteLedger(ctx.SessionId, new RerunLedger(null, false));
        }

        return HookDecision.Allow;
    }

    public static HookDecision PostEdit(HookInput input)
    {
        var path = String(input.Payload, "tool_input", "file_path") ?? String(input.Payload, "tool_input", "notebook_path");
        if (string.IsNullOrEmpty(path) || !ChangesBehaviour(input.Context.ProjectRoot, path))
        {
            return HookDecision.Allow;
        }

        var ctx = input.Context;
        var ledger = ctx.State.ReadLedger(ctx.SessionId);
        if (ledger.LastFailFingerprint is not null && !ledger.EditsSinceFail)
        {
            ctx.State.WriteLedger(ctx.SessionId, ledger with { EditsSinceFail = true });
        }

        return LintConventions(input, path);
    }

    /// <summary>
    /// Runs the convention rules on the edited file. New findings go back to Claude as feedback
    /// (exit 2 after the edit, which keeps the edit); legacy findings stay quiet. Warn-only: any
    /// problem running the check allows silently.
    /// </summary>
    private static HookDecision LintConventions(HookInput input, string path)
    {
        var root = input.Context.ProjectRoot;
        var full = Path.GetFullPath(Path.Combine(root, path));
        var relative = Path.GetRelativePath(root, full).Replace('\\', '/');
        if (LayerMap.Of(relative) == Layer.None || !File.Exists(full))
        {
            return HookDecision.Allow;
        }

        ConventionReport report;
        try
        {
            report = ConventionChecker.Run(root, [relative]);
        }
        catch (ConventionException)
        {
            return HookDecision.Allow;
        }

        var fresh = report.Findings.Where(f => !f.Legacy).ToList();
        if (fresh.Count == 0)
        {
            return HookDecision.Allow;
        }

        input.Context.State.AppendEvent(input.Context.SessionId, input.Mode, "feedback", "conventions", input.Context.Services.Now());
        input.Err.WriteLine(
            $"Harness convention check (warn-only, the edit was kept): {relative} has {fresh.Count} finding(s). " +
            "Fix them now, or mark a deliberate exception with '// conventions-ok: <reason>'.");
        foreach (var f in fresh)
        {
            input.Err.WriteLine($"  {f.Severity.ToString().ToUpperInvariant()}  line {f.Line}  [{f.Rule}] {f.Why}");
        }

        return HookDecision.Block;
    }

    public static HookDecision PreWrite(HookInput input)
    {
        var path = String(input.Payload, "tool_input", "file_path") ?? String(input.Payload, "tool_input", "notebook_path") ?? string.Empty;
        var findings = WrittenText(input.Payload).SelectMany(SecretScanner.Scan).ToList();

        var high = findings.FirstOrDefault(f => f.HighConfidence);
        if (high is not null)
        {
            return Deny(input, high.Rule,
                high.Description + ". Read secrets from environment variables or user-secrets instead of writing them into files");
        }

        var low = findings.FirstOrDefault();
        if (low is not null && !IsDocument(path))
        {
            Ask(input, low.Rule,
                low.Description + ". Confirm it is a throwaway test value, or read it from an environment variable");
        }

        return HookDecision.Allow;
    }

    public static HookDecision SessionStart(HookInput input)
    {
        var root = input.Context.ProjectRoot;
        var text = new StringBuilder()
            .AppendLine("Harness reminders:")
            .AppendLine("- Route each framework task through the intent table in CLAUDE.md, and use the skill it names before changing files.")
            .AppendLine("- Work on a named branch (automation/, harness/, feature/, fix/). Never push to the default branch. Commit, push or open a PR only when the user asks.")
            .AppendLine("- Before committing, run Harness.Tools check-conventions and fix what it reports.");

        if (Directory.Exists(Path.Combine(root, ".harness")) && !File.Exists(Path.Combine(root, ".harness", "project.json")))
        {
            text.AppendLine("- Harness setup has not finished here (.harness/project.json is missing). Run .harness/setup.ps1 so the hooks and checks work.");
        }

        var home = input.Context.Services.UserHome;
        if (!string.IsNullOrEmpty(home))
        {
            var warning = SkillShadowing.Warning(Path.Combine(root, ".claude", "skills"), Path.Combine(home, ".claude", "skills"));
            if (warning is not null)
            {
                text.Append("- ").AppendLine(warning);
            }
        }

        input.Out.Write(text.ToString());
        return HookDecision.Allow;
    }

    public static HookDecision PreCompact(HookInput input)
    {
        var ctx = input.Context;
        var stamp = ctx.Services.Now().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        ctx.State.AppendText("session-notes.md",
            $"\n## Context compacted at {stamp} (session {ctx.SessionId})\n" +
            "If you are resuming after this point: check git status, re-read the plan and this file, " +
            "and confirm with the user which task to continue.\n");

        input.Out.WriteLine(
            "Context is about to be compacted. Write to .harness/state/session-notes.md now: the task in progress " +
            "(file, what is done, what is not), any uncommitted changes, and the latest test results.");
        return HookDecision.Allow;
    }

    public static HookDecision SessionEnd(HookInput input)
    {
        var ctx = input.Context;
        var (denied, asked) = ctx.State.CountEvents(ctx.SessionId);
        ctx.State.AppendJsonLine("sessions.jsonl", new JsonObject
        {
            ["ts"] = ctx.Services.Now().ToString("O"),
            ["session_id"] = ctx.SessionId,
            ["reason"] = String(input.Payload, "reason") ?? "unknown",
            ["branch"] = ctx.Services.CurrentBranch(ctx.ProjectRoot) ?? "unknown",
            ["denied"] = denied,
            ["asked"] = asked,
        });
        return HookDecision.Allow;
    }

    private static HookDecision Deny(HookInput input, string rule, string reason)
    {
        input.Context.State.AppendEvent(input.Context.SessionId, input.Mode, "deny", rule, input.Context.Services.Now());
        input.Err.WriteLine($"BLOCKED by harness {input.Mode} ({rule}): {reason}.");
        return HookDecision.Block;
    }

    private static void Ask(HookInput input, string rule, string reason)
    {
        input.Context.State.AppendEvent(input.Context.SessionId, input.Mode, "ask", rule, input.Context.Services.Now());
        var output = new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = "PreToolUse",
                ["permissionDecision"] = "ask",
                ["permissionDecisionReason"] = $"Harness ({rule}): {reason}.",
            },
        };
        input.Out.Write(output.ToJsonString());
    }

    private static bool ChangesBehaviour(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return !BookkeepingFolders.Any(f => relative.StartsWith(f, StringComparison.OrdinalIgnoreCase))
            && !DocExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsDocument(string path) =>
        DocExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
        || path.Replace('\\', '/').Contains("/docs/", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> WrittenText(JsonElement payload)
    {
        foreach (var name in new[] { "content", "new_string", "new_source" })
        {
            var text = String(payload, "tool_input", name);
            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }

        if (payload.TryGetProperty("tool_input", out var toolInput)
            && toolInput.ValueKind == JsonValueKind.Object
            && toolInput.TryGetProperty("edits", out var edits)
            && edits.ValueKind == JsonValueKind.Array)
        {
            foreach (var edit in edits.EnumerateArray())
            {
                var text = String(edit, "new_string");
                if (!string.IsNullOrEmpty(text))
                {
                    yield return text;
                }
            }
        }
    }

    /// <summary>The text fields of tool_response (stdout, stderr and so on), or the response itself when it is a string.</summary>
    private static string ResponseText(JsonElement payload)
    {
        if (!payload.TryGetProperty("tool_response", out var response))
        {
            return string.Empty;
        }

        return response.ValueKind switch
        {
            JsonValueKind.String => response.GetString() ?? string.Empty,
            JsonValueKind.Object => string.Join('\n', response.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.String)
                .Select(p => p.Value.GetString())),
            _ => string.Empty,
        };
    }

    /// <summary>Reads a string at a property path, or null when any step is missing or not the right kind.</summary>
    internal static string? String(JsonElement element, params string[] path)
    {
        foreach (var name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
            {
                return null;
            }
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }
}
