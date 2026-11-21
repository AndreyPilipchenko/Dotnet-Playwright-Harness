using System.Text.RegularExpressions;

namespace Harness.Tools.Tests.Installing;

/// <summary>The text that a consumer project's CLAUDE.md imports (templates/CLAUDE.harness.md).</summary>
public class ClaudeMdTemplateTests
{
    private static string Text => File.ReadAllText(Path.Combine(RepoPaths.Templates, "CLAUDE.harness.md"));

    [Fact]
    public void Template_NamesTheSetupScript_AndTheEntryPoint()
    {
        var text = Text;

        Assert.Contains(".harness/setup.ps1", text, StringComparison.Ordinal);
        Assert.Contains("pom-architecture", text, StringComparison.Ordinal);
        Assert.Contains("Routing", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Template_NamesTheBranchPrefixes()
    {
        var text = Text;

        Assert.All(["automation/", "harness/", "feature/", "fix/"], prefix => Assert.Contains(prefix, text, StringComparison.Ordinal));
    }

    [Fact]
    public void Template_HasNoPlaceholders()
    {
        var text = Text;

        Assert.DoesNotMatch(new Regex(@"\{\{\w+\}\}"), text);
        Assert.DoesNotContain("__Name__", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Template_DoesNotCopyTheRoutingTable()
    {
        // pom-architecture owns the routing table; a copy here would drift from it.
        var skills = Directory.GetDirectories(Path.Combine(RepoPaths.Root, ".claude", "skills")).Select(Path.GetFileName).ToList();
        var tableRows = Text.Split('\n').Where(l => l.TrimStart().StartsWith('|')).ToList();

        Assert.DoesNotContain(tableRows, row => skills.Any(s => row.Contains(s!, StringComparison.Ordinal)));
    }
}
