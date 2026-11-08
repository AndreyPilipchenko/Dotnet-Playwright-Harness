using Harness.Tools.Conventions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Harness.Tools.Traceability;

/// <summary>A test method that carries a <c>tc:</c> or <c>bug:</c> category.</summary>
internal sealed record TestLink(string Prefix, string Target, string File, int Line, string Method);

internal sealed record TraceabilityReport(
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<QaRecord> Records,
    IReadOnlyList<TestLink> Links,
    int TestFileCount)
{
    public IEnumerable<Finding> Failures => Findings.Where(f => f.Severity == Severity.Fail);

    public IEnumerable<Finding> Warnings => Findings.Where(f => f.Severity == Severity.Warn);

    public IEnumerable<QaRecord> OfKind(RecordKind kind) => Records.Where(r => r.Kind == kind);

    public IEnumerable<TestLink> LinksTo(string prefix, string id) => Links.Where(l => l.Prefix == prefix && l.Target == id);
}

/// <summary>The checks, by id, with the reason each one exists.</summary>
internal static class TraceabilityRules
{
    private static readonly Dictionary<string, (Severity Severity, string Why)> Rules = new(StringComparer.Ordinal)
    {
        ["record-format"] = (Severity.Fail, "A QA record needs front matter with its id, matching file name and required fields."),
        ["case-unknown-requirement"] = (Severity.Fail, "A test case names the requirement records it covers, and they must exist."),
        ["case-automated-without-test"] = (Severity.Fail, "A case marked 'automation: automated' needs a test with the category tc:<id>."),
        ["case-not-marked-automated"] = (Severity.Warn, "A test automates this case, so set 'automation: automated' on it."),
        ["test-unknown-case"] = (Severity.Fail, "A tc:<id> category must name an existing test case record."),
        ["test-unknown-bug"] = (Severity.Fail, "A bug:<id> category must name an existing bug report."),
        ["bug-unknown-case"] = (Severity.Fail, "A bug report's 'cases' must name existing test case records."),
        ["bug-closed-still-linked"] = (Severity.Warn, "The bug is closed; remove its bug:<id> category from the test so new failures are not mistaken for it."),
        ["requirement-without-cases"] = (Severity.Warn, "An accepted requirement has no test case yet."),
    };

    public static IEnumerable<(string Id, Severity Severity, string Why)> All => Rules.Select(r => (r.Key, r.Value.Severity, r.Value.Why));

    public static Finding Finding(string rule, string file, int line, string detail)
    {
        var (severity, why) = Rules[rule];
        return new Finding(rule, severity, file, line, why, detail);
    }
}

/// <summary>
/// Checks the chain requirement → test case → test → bug, from the Markdown records in
/// <c>docs/qa/</c> and the <c>tc:</c> and <c>bug:</c> categories on tests.
/// </summary>
internal static class TraceabilityChecker
{
    public static TraceabilityReport Run(string root)
    {
        var (records, findings) = RecordReader.Read(root);
        if (records.Count == 0 && findings.Count == 0)
        {
            throw new ConventionException($"No QA records found under {RecordReader.QaFolder}/ (requirements, test-cases, bug-reports).");
        }

        var (links, testFiles) = ReadTestLinks(root);
        var ids = records.ToDictionary(r => r.Id, StringComparer.Ordinal);

        foreach (var testCase in records.Where(r => r.Kind == RecordKind.TestCase))
        {
            var requirements = testCase.List("requirements");
            if (requirements.Count == 0)
            {
                findings.Add(TraceabilityRules.Finding("case-unknown-requirement", testCase.File, 1, "No requirement is listed."));
            }

            foreach (var missing in requirements.Where(id => !Is(ids, id, RecordKind.Requirement)))
            {
                findings.Add(TraceabilityRules.Finding("case-unknown-requirement", testCase.File, 1, $"{missing} does not exist."));
            }

            var automated = testCase.Field("automation") == "automated";
            var tested = links.Any(l => l.Prefix == "tc" && l.Target == testCase.Id);
            if (automated && !tested)
            {
                findings.Add(TraceabilityRules.Finding("case-automated-without-test", testCase.File, 1, $"No test has the category tc:{testCase.Id}."));
            }

            if (!automated && tested)
            {
                findings.Add(TraceabilityRules.Finding("case-not-marked-automated", testCase.File, 1, $"automation: {testCase.Field("automation")}"));
            }
        }

        foreach (var link in links)
        {
            if (link.Prefix == "tc")
            {
                if (!Is(ids, link.Target, RecordKind.TestCase))
                {
                    findings.Add(TraceabilityRules.Finding("test-unknown-case", link.File, link.Line, $"tc:{link.Target} in {link.Method}"));
                }
            }
            else if (!Is(ids, link.Target, RecordKind.BugReport))
            {
                findings.Add(TraceabilityRules.Finding("test-unknown-bug", link.File, link.Line, $"bug:{link.Target} in {link.Method}"));
            }
            else if (ids[link.Target].Field("status") != "open")
            {
                findings.Add(TraceabilityRules.Finding("bug-closed-still-linked", link.File, link.Line, $"bug:{link.Target} is {ids[link.Target].Field("status")}"));
            }
        }

        foreach (var bug in records.Where(r => r.Kind == RecordKind.BugReport))
        {
            foreach (var missing in bug.List("cases").Where(id => !Is(ids, id, RecordKind.TestCase)))
            {
                findings.Add(TraceabilityRules.Finding("bug-unknown-case", bug.File, 1, $"{missing} does not exist."));
            }
        }

        foreach (var requirement in records.Where(r => r.Kind == RecordKind.Requirement && r.Field("status") == "accepted"))
        {
            var covered = records.Any(r => r.Kind == RecordKind.TestCase && r.List("requirements").Contains(requirement.Id, StringComparer.Ordinal));
            if (!covered)
            {
                findings.Add(TraceabilityRules.Finding("requirement-without-cases", requirement.File, 1, requirement.Field("title")));
            }
        }

        var ordered = findings.OrderBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line).ToList();
        return new TraceabilityReport(ordered, records, links, testFiles);
    }

    private static bool Is(Dictionary<string, QaRecord> ids, string id, RecordKind kind) =>
        ids.TryGetValue(id, out var record) && record.Kind == kind;

    private static (List<TestLink> Links, int Files) ReadTestLinks(string root)
    {
        var links = new List<TestLink>();
        var tests = Path.Combine(root, "tests");
        if (!Directory.Exists(tests))
        {
            return (links, 0);
        }

        var files = 0;
        foreach (var path in Directory.GetFiles(tests, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var layer = LayerMap.Of(relative);
            if (layer is not (Layer.Tests or Layer.Setup))
            {
                continue;
            }

            files++;
            var source = new SourceFile(relative, layer, File.ReadAllText(path));
            foreach (var (category, attribute) in source.Categories())
            {
                var colon = category.IndexOf(':', StringComparison.Ordinal);
                var prefix = colon > 0 ? category[..colon] : string.Empty;
                if (prefix is "tc" or "bug")
                {
                    var method = attribute.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.Text
                        ?? attribute.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text
                        ?? string.Empty;
                    links.Add(new TestLink(prefix, category[(colon + 1)..], relative, SourceFile.LineOf(attribute), method));
                }
            }
        }

        return (links, files);
    }
}
