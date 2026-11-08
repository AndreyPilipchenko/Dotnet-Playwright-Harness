using System.Text.RegularExpressions;
using Harness.Tools.Conventions;

namespace Harness.Tools.Tests.Conventions;

/// <summary>
/// Keeps the canonical design-conventions.md and the checker in step: every rule is explained
/// in the doc, and every rule id the doc cites is a real rule.
/// </summary>
public partial class ConventionsDocTests
{
    private static string DocText() => File.ReadAllText(Path.Combine(
        RepoPaths.Root, ".claude", "skills", "pom-architecture", "references", "design-conventions.md"));

    private static HashSet<string> CitedIds() =>
        [.. RuleCitation().Matches(DocText()).Select(m => m.Groups[1].Value)];

    [Fact]
    public void EveryRuleIsCitedInTheDoc()
    {
        var cited = CitedIds();

        var missing = ConventionRules.All.Select(r => r.Id).Where(id => !cited.Contains(id)).Order();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryCitedIdIsARule()
    {
        var known = ConventionRules.All.Select(r => r.Id).ToHashSet();

        var unknown = CitedIds().Where(id => !known.Contains(id)).Order();

        Assert.Empty(unknown);
    }

    // A rule citation is a backticked, bracketed kebab-case id such as `[no-hard-wait]`.
    [GeneratedRegex(@"`\[([a-z]+(?:-[a-z]+)+)\]`")]
    private static partial Regex RuleCitation();
}
