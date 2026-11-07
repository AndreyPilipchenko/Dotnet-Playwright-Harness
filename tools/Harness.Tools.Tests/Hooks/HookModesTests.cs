using System.Text.Json.Nodes;

namespace Harness.Tools.Tests.Hooks;

public sealed class HookModesTests : IDisposable
{
    private const string TestCommand = "dotnet test --project tests/Acme.Tests/Acme.Tests.csproj";
    private const string FailedOutput = "Test run summary: Failed!\n  total: 2\n  failed: 1\n  succeeded: 1";
    private const string PassedOutput = "Test run summary: Passed!\n  total: 2\n  failed: 0\n  succeeded: 2";

    private static readonly string StrongValue = "ABC123abc456" + "def789ghijklmnop=";
    private static readonly string GitHubToken = "gh" + "p_" + new string('a', 20) + new string('7', 16);

    private readonly HookTestHost _host = new();

    public void Dispose() => _host.Dispose();

    // ── pre-shell ────────────────────────────────────────────────────────

    [Fact]
    public void PreShell_Deny_Exits2_WithReasonOnStderr_AndNoStdout()
    {
        var r = _host.Shell("npm publish");

        Assert.Equal(2, r.Code);
        Assert.Contains("BLOCKED", r.Err, StringComparison.Ordinal);
        Assert.Equal(string.Empty, r.Out);
    }

    [Fact]
    public void PreShell_Ask_WritesPermissionPrompt()
    {
        var r = _host.Shell("git reset --hard");

        Assert.Equal(0, r.Code);
        Assert.Equal("ask", r.PermissionDecision);
        Assert.Contains("reset --hard", r.PermissionReason, StringComparison.Ordinal);
        Assert.Equal("PreToolUse", JsonNode.Parse(r.Out)!["hookSpecificOutput"]!["hookEventName"]!.GetValue<string>());
    }

    [Fact]
    public void PreShell_ChecksThePowerShellTool()
    {
        Assert.Equal(2, _host.Shell("Remove-Item -Recurse -Force C:\\", tool: "PowerShell").Code);
    }

    [Fact]
    public void PreShell_Allow_IsSilent()
    {
        var r = _host.Shell("git status");

        Assert.Equal(0, r.Code);
        Assert.Equal(string.Empty, r.Out);
        Assert.Equal(string.Empty, r.Err);
    }

    [Fact]
    public void PreShell_PushToDefault_UsesTheCurrentBranch()
    {
        _host.Branch = "main";

        Assert.Equal(2, _host.Shell("git push").Code);
    }

    [Fact]
    public void PreShell_OptOutEnvironmentVariable_LiftsCommitPrompt()
    {
        _host.SetEnv("HARNESS_ALLOW_GIT_COMMIT_PUSH", "1");

        var r = _host.Shell("git commit -m x");

        Assert.Equal(0, r.Code);
        Assert.Equal(string.Empty, r.Out);
    }

    [Fact]
    public void PreShell_RecordsDenyAndAskEvents()
    {
        _host.Shell("npm publish");
        _host.Shell("git reset --hard");
        _host.Shell("git status");

        var lines = File.ReadAllLines(_host.StateFile("hook-events.jsonl"));
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"decision\":\"deny\"", lines[0], StringComparison.Ordinal);
        Assert.Contains("\"rule\":\"git-reset-hard\"", lines[1], StringComparison.Ordinal);
        Assert.All(lines, l => Assert.Contains("\"session_id\":\"s1\"", l, StringComparison.Ordinal));
    }

    // ── rerun breaker ────────────────────────────────────────────────────

    [Fact]
    public void Rerun_OfAFailedTestCommand_WithoutAnEdit_IsBlocked()
    {
        _host.AfterShell(TestCommand, FailedOutput);

        var r = _host.Shell(TestCommand);

        Assert.Equal(2, r.Code);
        Assert.Contains("already failed", r.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Rerun_AfterEditingCode_IsAllowed()
    {
        _host.AfterShell(TestCommand, FailedOutput);
        _host.AfterEdit("tests/Acme.Tests/Ui/LoginTests.cs");

        Assert.Equal(0, _host.Shell(TestCommand).Code);
    }

    [Theory]
    [InlineData("docs/notes.md")]
    [InlineData(".harness/state/plan.json")]
    [InlineData(".claude/skills/x/SKILL.md")]
    [InlineData("README.md")]
    public void Rerun_AfterEditingBookkeepingFiles_IsStillBlocked(string path)
    {
        _host.AfterShell(TestCommand, FailedOutput);
        _host.AfterEdit(path);

        Assert.Equal(2, _host.Shell(TestCommand).Code);
    }

    [Fact]
    public void Rerun_WithDifferentArguments_IsAllowed()
    {
        _host.AfterShell(TestCommand, FailedOutput);

        Assert.Equal(0, _host.Shell(TestCommand + " --filter TestCategory=smoke").Code);
    }

    [Fact]
    public void Rerun_AfterThatCommandPasses_IsAllowed()
    {
        _host.AfterShell(TestCommand, FailedOutput);
        _host.AfterEdit("src/Acme.Framework/Pages/LoginPage.cs");
        _host.AfterShell(TestCommand, PassedOutput);

        Assert.Equal(0, _host.Shell(TestCommand).Code);
    }

    [Fact]
    public void Rerun_IsTrackedPerSession()
    {
        _host.AfterShell(TestCommand, FailedOutput, session: "s1");

        Assert.Equal(0, _host.Shell(TestCommand, session: "s2").Code);
    }

    [Fact]
    public void PostShell_IsSilent()
    {
        var r = _host.AfterShell(TestCommand, FailedOutput);

        Assert.Equal(0, r.Code);
        Assert.Equal(string.Empty, r.Out);
    }

    // ── pre-write ────────────────────────────────────────────────────────

    private HookResult Write(string path, string content, string tool = "Write") =>
        _host.Run("pre-write", new { session_id = "s1", tool_name = tool, tool_input = new { file_path = _host.Root.Combine(path), content } });

    [Fact]
    public void PreWrite_KnownSecretFormat_IsBlocked_WithoutEchoingIt()
    {
        var r = Write("src/Acme.Framework/Config/Settings.cs", "var t = \"" + GitHubToken + "\";");

        Assert.Equal(2, r.Code);
        Assert.Contains("github-token", r.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(GitHubToken, r.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void PreWrite_KnownSecretFormat_IsBlocked_EvenInDocs()
    {
        Assert.Equal(2, Write("docs/setup.md", "token " + GitHubToken).Code);
    }

    [Fact]
    public void PreWrite_SecretLookingAssignment_Asks()
    {
        var r = Write("resources/testData/LoginTestData.json", "{ \"password\": \"" + StrongValue + "\" }");

        Assert.Equal(0, r.Code);
        Assert.Equal("ask", r.PermissionDecision);
    }

    [Theory]
    [InlineData("docs/configuration.md")]
    [InlineData("README.md")]
    [InlineData("notes/guide.txt")]
    public void PreWrite_SecretLookingAssignment_InDocs_IsAllowed(string path)
    {
        var r = Write(path, "{ \"password\": \"" + StrongValue + "\" }");

        Assert.Equal(0, r.Code);
        Assert.Equal(string.Empty, r.Out);
    }

    [Fact]
    public void PreWrite_ChecksEditNewString()
    {
        var r = _host.Run("pre-write", new
        {
            session_id = "s1",
            tool_name = "Edit",
            tool_input = new { file_path = _host.Root.Combine("src/a.cs"), old_string = "x", new_string = "var t = \"" + GitHubToken + "\";" },
        });

        Assert.Equal(2, r.Code);
    }

    [Fact]
    public void PreWrite_ChecksMultiEditEdits()
    {
        var r = _host.Run("pre-write", new
        {
            session_id = "s1",
            tool_name = "MultiEdit",
            tool_input = new
            {
                file_path = _host.Root.Combine("src/a.cs"),
                edits = new[] { new { old_string = "a", new_string = "b" }, new { old_string = "c", new_string = GitHubToken } },
            },
        });

        Assert.Equal(2, r.Code);
    }

    [Fact]
    public void PreWrite_OrdinaryCode_IsSilent()
    {
        var r = Write("src/a.cs", "private ILocator PasswordInput => _page.GetByLabel(\"Password\");");

        Assert.Equal(0, r.Code);
        Assert.Equal(string.Empty, r.Out);
    }

    [Fact]
    public void PreWrite_ACommandNamedInATestFile_IsAllowed()
    {
        Assert.Equal(0, Write("tests/a.cs", "Assert.Equal(2, Run(\"rm -rf /\"));").Code);
    }

    // ── session-start ────────────────────────────────────────────────────

    private HookResult Start() => _host.Run("session-start", new { session_id = "s1", source = "startup" });

    [Fact]
    public void SessionStart_InjectsReminders()
    {
        var r = Start();

        Assert.Equal(0, r.Code);
        Assert.Contains("CLAUDE.md", r.Out, StringComparison.Ordinal);
        Assert.Contains("branch", r.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionStart_WarnsWhenSetupHasNotRun()
    {
        Directory.CreateDirectory(_host.Root.Combine(".harness"));

        Assert.Contains("setup", Start().Out, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SessionStart_IsQuietAboutSetup_WhenProjectJsonExists()
    {
        _host.Root.Write(".harness/project.json", "{}");

        Assert.DoesNotContain("setup.ps1", Start().Out, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionStart_WarnsAboutAUserSkillThatShadowsADifferentProjectSkill()
    {
        _host.Root.Write(".claude/skills/automate-test/SKILL.md", "project version");
        _host.Home.Write(".claude/skills/automate-test/SKILL.md", "older user version");

        var r = Start();

        Assert.Contains("automate-test", r.Out, StringComparison.Ordinal);
        Assert.Contains("shadow", r.Out, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SessionStart_DoesNotNameIdenticalShadowedSkills()
    {
        _host.Root.Write(".claude/skills/automate-test/SKILL.md", "same");
        _host.Home.Write(".claude/skills/automate-test/SKILL.md", "same");

        Assert.DoesNotContain("automate-test", Start().Out, StringComparison.Ordinal);
    }

    // ── pre-compact and session-end ──────────────────────────────────────

    [Fact]
    public void PreCompact_AppendsAMarker_AndRemindsToSaveState()
    {
        var r = _host.Run("pre-compact", new { session_id = "s1", trigger = "auto" });

        Assert.Equal(0, r.Code);
        Assert.Contains("session-notes.md", r.Out, StringComparison.Ordinal);
        Assert.Contains("2026-11-08", File.ReadAllText(_host.StateFile("session-notes.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void SessionEnd_AppendsASessionLogLine_WithThisSessionsDecisions()
    {
        _host.Shell("npm publish", session: "s1");
        _host.Shell("git reset --hard", session: "s1");
        _host.Shell("npm publish", session: "other");

        var r = _host.Run("session-end", new { session_id = "s1", reason = "clear" });

        Assert.Equal(0, r.Code);
        var entry = JsonNode.Parse(File.ReadAllLines(_host.StateFile("sessions.jsonl")).Single())!;
        Assert.Equal("s1", entry["session_id"]!.GetValue<string>());
        Assert.Equal("clear", entry["reason"]!.GetValue<string>());
        Assert.Equal("feature/x", entry["branch"]!.GetValue<string>());
        Assert.Equal(1, entry["denied"]!.GetValue<int>());
        Assert.Equal(1, entry["asked"]!.GetValue<int>());
    }
}
