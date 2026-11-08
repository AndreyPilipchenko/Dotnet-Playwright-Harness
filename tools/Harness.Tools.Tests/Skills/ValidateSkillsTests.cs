namespace Harness.Tools.Tests.Skills;

/// <summary>
/// <c>validate-skills</c> checks the payload: frontmatter, links, the routing table, and terms left
/// over from the TypeScript and Azure DevOps sources. A consumer's own skills are left alone.
/// </summary>
public class ValidateSkillsTests
{
    private const string Good = "Use when working on the thing that this skill is about, in enough words.";

    private static string Skill(string name, string body = "Body.\n", string? description = null) =>
        $"---\nname: {name}\ndescription: {description ?? Good}\n---\n\n# Title\n\n{body}";

    private static string Router(params string[] skills) =>
        Skill("pom-architecture", "## Routing\n\n| Working on | Skill |\n|---|---|\n"
            + string.Concat(skills.Select(s => $"| Something | {s} |\n"))
            + "\n## Agents\n\n| Job | `subagent_type` |\n|---|---|\n| Run | `suite-runner` |\n\n## End\n");

    private static string Agent(string name) =>
        $"---\nname: {name}\ndescription: Does a particular job for the main session, with enough words.\ntools: Read\n---\n\nBody.\n";

    /// <summary>A valid payload: a router, skill "alpha" that names the agent, and agent "suite-runner".</summary>
    private static TempDir Payload()
    {
        var dir = new TempDir();
        dir.Write(".claude/skills/pom-architecture/SKILL.md", Router("alpha"));
        dir.Write(".claude/skills/alpha/SKILL.md", Skill("alpha", "Use `suite-runner` here.\n"));
        dir.Write(".claude/agents/suite-runner.md", Agent("suite-runner"));
        return dir;
    }

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var io = new Console3();
        var code = Cli.Run(["validate-skills", .. args], io.In, io.Out, io.Err);
        return (code, io.Out.ToString(), io.Err.ToString());
    }

    private static (int Code, string Out, string Err) Check(TempDir dir) => Run("--root", dir.Path);

    private static void AssertFails(TempDir dir, string rule, string fragment)
    {
        var (code, output, _) = Check(dir);

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains($"[{rule}]", output, StringComparison.Ordinal);
        Assert.Contains(fragment, output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRealPayload_IsClean()
    {
        var (code, output, _) = Run("--root", RepoPaths.Root);

        Assert.True(code == ExitCodes.Ok, output);
    }

    [Fact]
    public void UnknownOption_IsAUsageError()
    {
        Assert.Equal(ExitCodes.Usage, Run("--nope").Code);
    }

    [Fact]
    public void NoSkills_IsNotChecked()
    {
        using var dir = new TempDir();

        var (code, _, err) = Check(dir);

        Assert.Equal(ExitCodes.NotChecked, code);
        Assert.Contains("No skills", err, StringComparison.Ordinal);
    }

    [Fact]
    public void AValidPayload_Passes_AndSaysWhatItChecked()
    {
        using var dir = Payload();

        var (code, output, _) = Check(dir);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("Checked 2 skills and 1 agents: 0 failures, 0 warnings.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingSkillFile_Fails()
    {
        using var dir = Payload();
        dir.Write(".claude/skills/beta/notes.md", "x");

        AssertFails(dir, "frontmatter", "beta");
    }

    [Theory]
    [InlineData("no frontmatter at all\n")]
    [InlineData("---\nname: other\ndescription: Use when something long enough is the case here.\n---\n")]
    [InlineData("---\nname: alpha\ndescription: Does a thing.\n---\n")]
    [InlineData("---\nname: alpha\n---\n")]
    public void BadSkillFrontmatter_Fails(string content)
    {
        using var dir = Payload();
        dir.Write(".claude/skills/alpha/SKILL.md", content + "`suite-runner`\n");

        AssertFails(dir, "frontmatter", "alpha/SKILL.md");
    }

    [Theory]
    [InlineData("---\nname: other\ndescription: Long enough to pass.\ntools: Read\n---\n")]
    [InlineData("---\nname: suite-runner\ntools: Read\n---\n")]
    [InlineData("---\nname: suite-runner\ndescription: Long enough to pass.\n---\n")]
    public void BadAgentFrontmatter_Fails(string content)
    {
        using var dir = Payload();
        dir.Write(".claude/agents/suite-runner.md", content);

        AssertFails(dir, "frontmatter", "suite-runner.md");
    }

    [Theory]
    [InlineData("Use when a <repository> is involved in this long enough sentence.")]
    [InlineData("Use when linking with a tc:<id> category in this long enough sentence.")]
    public void AnXmlLikeTagInADescription_Fails(string description)
    {
        using var dir = Payload();
        dir.Write(".claude/skills/alpha/SKILL.md", Skill("alpha", "`suite-runner`\n", description));

        AssertFails(dir, "description-tag", "alpha/SKILL.md");
    }

    [Theory]
    [InlineData("Use when linking with a tc: category in this long enough sentence.")]
    [InlineData("Use when a comment # starts part way through this long enough sentence.")]
    public void ADescriptionThatBreaksYaml_Fails(string description)
    {
        using var dir = Payload();
        dir.Write(".claude/skills/alpha/SKILL.md", Skill("alpha", "`suite-runner`\n", description));

        AssertFails(dir, "description-yaml", "alpha/SKILL.md");
    }

    [Fact]
    public void AnUnroutedSkill_Fails()
    {
        using var dir = Payload();
        dir.Write(".claude/skills/beta/SKILL.md", Skill("beta"));

        AssertFails(dir, "not-routed", "beta");
    }

    [Fact]
    public void AMissingRouter_Fails()
    {
        using var dir = new TempDir();
        dir.Write(".claude/skills/alpha/SKILL.md", Skill("alpha"));

        AssertFails(dir, "not-routed", "pom-architecture");
    }

    [Fact]
    public void AnAgentNoSkillNames_Fails()
    {
        using var dir = Payload();
        dir.Write(".claude/agents/lonely.md", Agent("lonely"));

        AssertFails(dir, "agent-unnamed", "lonely");
    }

    [Fact]
    public void TheRouterNamingAMissingAgent_Fails()
    {
        using var dir = Payload();
        File.Delete(dir.Combine(".claude", "agents", "suite-runner.md"));

        AssertFails(dir, "agent-table", "suite-runner");
    }

    [Fact]
    public void ABrokenLink_Fails_WithItsLine()
    {
        using var dir = Payload();
        dir.Write(".claude/skills/alpha/SKILL.md", Skill("alpha", "`suite-runner`\n\nSee [x](references/gone.md).\n"));

        AssertFails(dir, "broken-link", "alpha/SKILL.md:10");
    }

    [Fact]
    public void ABrokenLinkInAReferenceFile_Fails()
    {
        using var dir = Payload();
        dir.Write(".claude/skills/alpha/references/a.md", "[x](../nope.md)\n");

        AssertFails(dir, "broken-link", "references/a.md");
    }

    [Fact]
    public void GoodLinks_RemoteLinks_AndLinksInCode_Pass()
    {
        using var dir = Payload();
        dir.Write(".claude/skills/alpha/references/a.md", "# A\n\n## 2. The evidence\n");
        dir.Write(
            ".claude/skills/alpha/SKILL.md",
            Skill("alpha", "`suite-runner` [a](references/a.md) [b](references/a.md#2-the-evidence) [c](https://example.com/x) [d](#title)\n\n`[e](gone.md)`\n\n```text\n[f](gone.md)\n```\n"));

        Assert.Equal(ExitCodes.Ok, Check(dir).Code);
    }

    [Fact]
    public void ABrokenAnchor_Fails()
    {
        using var dir = Payload();
        dir.Write(".claude/skills/alpha/references/a.md", "# A\n");
        dir.Write(".claude/skills/alpha/SKILL.md", Skill("alpha", "`suite-runner` [a](references/a.md#nope)\n"));

        AssertFails(dir, "broken-link", "#nope");
    }

    [Theory]
    [InlineData("Run `LoginTests.spec.ts` now.", ".spec.ts")]
    [InlineData("Use test.describe here.", "test.describe")]
    [InlineData("Run npx playwright test.", "npx")]
    [InlineData("File it in ADO first.", "ADO")]
    [InlineData("File it in Azure DevOps first.", "Azure DevOps")]
    [InlineData("Replace {{value}} here.", "{{")]
    public void LeftoverTerms_Fail(string line, string term)
    {
        using var dir = Payload();
        dir.Write(".claude/skills/alpha/references/a.md", line + "\n");

        AssertFails(dir, "leftover", term);
    }

    [Theory]
    [InlineData("Query through ADO.NET here.")]
    [InlineData("Set `${{ secrets.BASE_URL }}` in the workflow.")]
    [InlineData("The Playwright MCP server starts with npx @playwright/mcp.")]
    [InlineData("Avoid the shadow of a tornado and of ADOPT.")]
    [InlineData("Check `npx --yes @playwright/cli@0.1.23 --version` first.")]
    [InlineData("It needs Node and npx).")]
    [InlineData("A template row links [TC](../test-cases/TC-<n>.md).")]
    public void AllowedTerms_Pass(string line)
    {
        using var dir = Payload();
        dir.Write(".claude/skills/alpha/references/a.md", line + "\n");

        Assert.Equal(ExitCodes.Ok, Check(dir).Code);
    }

    [Fact]
    public void AVeryLongSkill_IsAWarning_NotAFailure()
    {
        using var dir = Payload();
        dir.Write(".claude/skills/alpha/SKILL.md", Skill("alpha", "`suite-runner` " + new string('x', 20000) + "\n"));

        var (code, output, _) = Check(dir);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("WARN", output, StringComparison.Ordinal);
        Assert.Contains("[size]", output, StringComparison.Ordinal);
    }

    [Fact]
    public void InAProject_OnlyTheHarnessPayloadIsChecked()
    {
        using var dir = Payload();
        dir.Write(".claude/skills/mine/SKILL.md", "my own skill, with npx and no frontmatter\n");
        dir.Write(".claude/agents/my-agent.md", "no frontmatter\n");
        dir.Write(".harness/manifest.json", """
            { "files": {
              ".claude/skills/pom-architecture/SKILL.md": "a",
              ".claude/skills/alpha/SKILL.md": "b",
              ".claude/agents/suite-runner.md": "c" } }
            """);

        var (code, output, _) = Check(dir);

        Assert.True(code == ExitCodes.Ok, output);
        Assert.Contains("Checked 2 skills and 1 agents", output, StringComparison.Ordinal);
    }

    [Fact]
    public void InAProject_AHarnessSkillIsStillChecked()
    {
        using var dir = Payload();
        dir.Write(".claude/skills/alpha/references/a.md", "Run npx here.\n");
        dir.Write(".harness/manifest.json", """{ "files": { ".claude/skills/alpha/SKILL.md": "b" } }""");

        AssertFails(dir, "leftover", "npx");
    }
}
