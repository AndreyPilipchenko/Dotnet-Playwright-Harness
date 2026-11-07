namespace Harness.Tools.Conventions;

/// <summary>Where a rule fired. <see cref="Severity"/> overrides the rule's own severity when set.</summary>
internal sealed record RuleHit(int Line, string? Excerpt = null, Severity? Severity = null);

/// <summary>What the rules may know about the project besides the file in hand.</summary>
internal sealed record RuleContext(string Root, string? Framework, IReadOnlyList<string> Categories);

internal delegate IEnumerable<RuleHit> FileCheck(SourceFile file, RuleContext context);

internal delegate IEnumerable<Finding> RepositoryCheck(ConventionRule rule, RuleContext context);

/// <summary>
/// One convention. A rule has a file check (run on each C# file in <see cref="Layers"/>), a repository
/// check (run once on a full scan), or both. <see cref="Exemptable"/> rules honour
/// <c>// conventions-ok: &lt;reason&gt;</c>; <see cref="Baselinable"/> rules may be recorded as legacy.
/// </summary>
internal sealed record ConventionRule(
    string Id,
    Severity Severity,
    string Why,
    IReadOnlyCollection<Layer> Layers,
    FileCheck? File,
    RepositoryCheck? Repository = null,
    bool Exemptable = true,
    bool Baselinable = true)
{
    public RuleKind Kind => File is null ? RuleKind.Repository : RuleKind.File;

    public bool AppliesTo(Layer layer) => File is not null && Layers.Contains(layer);
}
