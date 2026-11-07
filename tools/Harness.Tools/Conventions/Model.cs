namespace Harness.Tools.Conventions;

internal enum Severity
{
    Fail,
    Warn,
}

internal enum RuleKind
{
    /// <summary>Checks one C# file at a time.</summary>
    File,

    /// <summary>Checks the project as a whole (folders, project files).</summary>
    Repository,
}

/// <summary>Where a file sits in the generated project layout.</summary>
internal enum Layer
{
    None,
    Tests,
    Setup,
    Pages,
    Apis,
    Dbs,
    Helpers,
    Utils,
    Config,

    /// <summary>Any other file under <c>src/</c>.</summary>
    Framework,
}

internal sealed record Finding(string Rule, Severity Severity, string File, int Line, string Why, string Excerpt)
{
    public string Fingerprint { get; init; } = string.Empty;

    /// <summary>Recorded in the baseline: reported, but does not fail the run.</summary>
    public bool Legacy { get; init; }
}

internal sealed record ConventionReport(IReadOnlyList<Finding> Findings, int FileCount, int RuleApplications, bool FullScan)
{
    public IEnumerable<Finding> NewFailures => Findings.Where(f => !f.Legacy && f.Severity == Severity.Fail);

    public IEnumerable<Finding> NewWarnings => Findings.Where(f => !f.Legacy && f.Severity == Severity.Warn);

    public int LegacyCount => Findings.Count(f => f.Legacy);
}

/// <summary>The check could not run as asked. This is never a pass.</summary>
internal sealed class ConventionException(string message) : Exception(message);
