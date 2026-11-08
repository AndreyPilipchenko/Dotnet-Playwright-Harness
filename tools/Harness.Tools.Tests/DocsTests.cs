using System.Text.RegularExpressions;

namespace Harness.Tools.Tests;

/// <summary>
/// The routing guide is the first thing a person reads, so it has to name everything the payload
/// ships, and the links in the top-level documents have to lead somewhere.
/// </summary>
public partial class DocsTests
{
    private static string ClaudeRoot => Path.Combine(RepoPaths.Root, ".claude");

    private static string Guide => File.ReadAllText(Path.Combine(RepoPaths.Root, "docs", "routing-guide.md"));

    public static TheoryData<string> Skills() =>
        [.. Directory.GetDirectories(Path.Combine(ClaudeRoot, "skills")).Select(d => Path.GetFileName(d)!)];

    public static TheoryData<string> Agents() =>
        [.. Directory.GetFiles(Path.Combine(ClaudeRoot, "agents"), "*.md").Select(f => Path.GetFileNameWithoutExtension(f)!)];

    public static TheoryData<string> Documents() =>
        ["README.md", "docs/routing-guide.md", "docs/porting-notes.md", "docs/mcp.md"];

    [Theory]
    [MemberData(nameof(Skills))]
    public void RoutingGuide_NamesEverySkill(string skill) =>
        Assert.Contains($"`{skill}`", Guide, StringComparison.Ordinal);

    [Theory]
    [MemberData(nameof(Agents))]
    public void RoutingGuide_NamesEveryAgent(string agent) =>
        Assert.Contains($"`{agent}`", Guide, StringComparison.Ordinal);

    [Theory]
    [MemberData(nameof(Documents))]
    public void Document_RelativeLinks_Resolve(string document)
    {
        var path = Path.Combine(RepoPaths.Root, document);
        var folder = Path.GetDirectoryName(path)!;

        var broken = RelativeLink().Matches(File.ReadAllText(path))
            .Select(m => m.Groups[1].Value)
            .Where(target => !File.Exists(Path.Combine(folder, target)) && !Directory.Exists(Path.Combine(folder, target)))
            .ToList();

        Assert.True(broken.Count == 0, $"{document} links to missing files: {string.Join(", ", broken)}");
    }

    [GeneratedRegex(@"\]\((?!https?:|#)([^)#\s]+)")]
    private static partial Regex RelativeLink();
}
