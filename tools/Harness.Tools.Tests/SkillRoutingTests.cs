using System.Text.RegularExpressions;

namespace Harness.Tools.Tests;

/// <summary>
/// Every skill in the payload has valid frontmatter, and the pom-architecture router names every
/// other skill, so a framework task always reaches the right specialist.
/// </summary>
public partial class SkillRoutingTests
{
    private const string Router = "pom-architecture";

    private static string SkillsRoot => Path.Combine(RepoPaths.Root, ".claude", "skills");

    public static TheoryData<string> Skills() => [.. SkillNames()];

    public static TheoryData<string> RoutedSkills() => [.. SkillNames().Where(s => s != Router)];

    private static IEnumerable<string> SkillNames() =>
        Directory.GetDirectories(SkillsRoot).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(Skills))]
    public void Skill_HasFrontmatter_WithItsFolderNameAndADescription(string skill)
    {
        var text = File.ReadAllText(Path.Combine(SkillsRoot, skill, "SKILL.md"));
        var frontmatter = Frontmatter().Match(text);

        Assert.True(frontmatter.Success, $"{skill}/SKILL.md has no frontmatter");
        Assert.Matches($"(?m)^name: {Regex.Escape(skill)}\\s*$", frontmatter.Value);
        Assert.Matches("(?m)^description: Use when .{40,}$", frontmatter.Value);
    }

    [Theory]
    [MemberData(nameof(RoutedSkills))]
    public void Skill_IsRoutedFromPomArchitecture(string skill)
    {
        var router = File.ReadAllText(Path.Combine(SkillsRoot, Router, "SKILL.md"));

        Assert.Matches($"(?m)^\\|.*\\| {Regex.Escape(skill)} \\|\\s*$", router);
    }

    [GeneratedRegex(@"\A---\r?\n(.*?)\r?\n---\r?\n", RegexOptions.Singleline)]
    private static partial Regex Frontmatter();
}
