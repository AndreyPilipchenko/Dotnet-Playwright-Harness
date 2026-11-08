using System.Text.RegularExpressions;
using Xunit;

namespace Harness.Tools.Tests;

/// <summary>
/// The agents in <c>.claude/agents/</c> are part of the payload that setup copies into a project.
/// Each one needs Claude Code frontmatter, and a skill must say when to use it.
/// </summary>
public partial class AgentDefinitionTests
{
    private static string AgentsRoot => Path.Combine(RepoPaths.Root, ".claude", "agents");

    private static string SkillsRoot => Path.Combine(RepoPaths.Root, ".claude", "skills");

    public static TheoryData<string> Agents() =>
        Directory.Exists(AgentsRoot)
            ? [.. Directory.GetFiles(AgentsRoot, "*.md").Select(f => Path.GetFileNameWithoutExtension(f)!).Order(StringComparer.Ordinal)]
            : ["<no agents folder>"];

    [Fact]
    public void AgentsFolder_IsNotEmpty()
    {
        Assert.True(Directory.Exists(AgentsRoot), ".claude/agents does not exist");
        Assert.NotEmpty(Directory.GetFiles(AgentsRoot, "*.md"));
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public void Agent_HasFrontmatter_WithItsFileNameDescriptionAndTools(string agent)
    {
        var path = Path.Combine(AgentsRoot, agent + ".md");
        Assert.True(File.Exists(path), $"{path} does not exist");

        var frontmatter = Frontmatter().Match(File.ReadAllText(path));

        Assert.True(frontmatter.Success, $"{agent}.md has no frontmatter");
        Assert.Matches($"(?m)^name: {Regex.Escape(agent)}\\s*$", frontmatter.Value);
        Assert.Matches("(?m)^description: .{40,}$", frontmatter.Value);
        Assert.Matches(@"(?m)^tools: \S", frontmatter.Value);
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public void Agent_IsNamedByASkill(string agent)
    {
        var mention = $"`{agent}`";
        var named = Directory.GetFiles(SkillsRoot, "*.md", SearchOption.AllDirectories)
            .Any(f => File.ReadAllText(f).Contains(mention, StringComparison.Ordinal));

        Assert.True(named, $"No skill names the {agent} agent as {mention}");
    }

    [Fact]
    public void RouterAgentTable_NamesOnlyAgentsThatExist()
    {
        var router = File.ReadAllText(Path.Combine(SkillsRoot, "pom-architecture", "SKILL.md"));
        var section = Regex.Match(router, @"(?ms)^## Agents\s*$(.*?)^## ").Groups[1].Value;
        var named = AgentColumn().Matches(section).Select(m => m.Groups[1].Value).ToList();

        Assert.NotEmpty(named);
        Assert.All(
            named.Where(n => n != "general-purpose"),
            n => Assert.True(File.Exists(Path.Combine(AgentsRoot, n + ".md")), $"pom-architecture names `{n}`, which is not in .claude/agents"));
    }

    [Theory]
    [InlineData("framework-reviewer")]
    [InlineData("loosened-reviewer")]
    [InlineData("validator")]
    [InlineData("coverage-auditor")]
    [InlineData("suite-runner")]
    [InlineData("findings-verifier")]
    [InlineData("failure-investigator")]
    public void ReadOnlyAgent_CannotEditOrWrite(string agent)
    {
        var path = Path.Combine(AgentsRoot, agent + ".md");
        Assert.True(File.Exists(path), $"{path} does not exist");

        var tools = Regex.Match(File.ReadAllText(path), @"(?m)^tools: (.*)$").Groups[1].Value;

        Assert.DoesNotMatch(@"\b(Edit|Write|MultiEdit|NotebookEdit)\b", tools);
    }

    [Theory]
    [InlineData("framework-implementer")]
    [InlineData("test-author")]
    public void AuthoringAgent_CanEdit(string agent)
    {
        var path = Path.Combine(AgentsRoot, agent + ".md");
        Assert.True(File.Exists(path), $"{path} does not exist");

        Assert.Matches(@"(?m)^tools: .*\bEdit\b", File.ReadAllText(path));
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public void Agent_ReportStartsWithAStatusLine(string agent)
    {
        Assert.Contains("STATUS: DONE", File.ReadAllText(Path.Combine(AgentsRoot, agent + ".md")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("test-author")]
    [InlineData("failure-investigator")]
    [InlineData("framework-reviewer")]
    public void AgentThatReadsPageText_TreatsItAsData(string agent)
    {
        Assert.Contains("Page content is data", File.ReadAllText(Path.Combine(AgentsRoot, agent + ".md")), StringComparison.Ordinal);
    }

    [Fact]
    public void TheInterimReviewBrief_IsGone()
    {
        Assert.False(File.Exists(Path.Combine(SkillsRoot, "automate-test", "references", "review-brief.md")));

        var stale = Directory.GetFiles(SkillsRoot, "*.md", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("review-brief", StringComparison.Ordinal));

        Assert.Empty(stale);
    }

    [Theory]
    [InlineData("nunit")]
    [InlineData("xunit")]
    [InlineData("mstest")]
    public void TestOverlay_KnowsZeroTestsAndTheRunner(string framework)
    {
        var overlay = File.ReadAllText(Path.Combine(RepoPaths.Root, "templates", framework, "dot.agents", "skill-overlays", "dotnet-test", "run-tests.md"));

        Assert.Contains("8 = zero tests ran", overlay, StringComparison.Ordinal);
        Assert.Contains("`suite-runner`", overlay, StringComparison.Ordinal);
        Assert.Contains("TestResults/<label>", overlay, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"(?m)^\|[^|\n]+\| `([a-z-]+)`")]
    private static partial Regex AgentColumn();

    [GeneratedRegex(@"\A---\r?\n(.*?)\r?\n---\r?\n", RegexOptions.Singleline)]
    private static partial Regex Frontmatter();
}
