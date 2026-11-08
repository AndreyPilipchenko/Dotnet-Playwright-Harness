using System.Text.Json;
using System.Text.Json.Nodes;
using Harness.Tools.Conventions;

namespace Harness.Tools.Traceability;

/// <summary>The <c>traceability</c> command line.</summary>
internal static class TraceabilityCommand
{
    public const string Usage = """
          traceability         Check requirement -> test case -> test -> bug links (docs/qa/ and tc:/bug: categories):
                                 --root <dir>          project root (default: current directory)
                                 --json                the whole chain plus findings, machine-readable
                                 --fail-on-warn        exit 1 on warnings too
                                 --list-rules          print the checks
                               Exit: 0 clean, 1 failures, 2 no QA records found, 64 bad arguments.
        """;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var root = Directory.GetCurrentDirectory();
        bool json = false, failOnWarn = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--root" when i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal):
                    root = args[++i];
                    break;
                case "--json":
                    json = true;
                    break;
                case "--fail-on-warn":
                    failOnWarn = true;
                    break;
                case "--list-rules":
                    foreach (var (id, severity, why) in TraceabilityRules.All)
                    {
                        stdout.WriteLine($"{id,-30} {severity.ToString().ToUpperInvariant(),-5} {why}");
                    }

                    return ExitCodes.Ok;
                default:
                    stderr.WriteLine($"Unknown or incomplete option '{args[i]}'.");
                    stderr.WriteLine(Usage);
                    return ExitCodes.Usage;
            }
        }

        try
        {
            var report = TraceabilityChecker.Run(root);
            if (json)
            {
                stdout.WriteLine(ToJson(report).ToJsonString(Indented));
            }
            else
            {
                WriteText(report, stdout);
            }

            var failed = report.Failures.Any() || (failOnWarn && report.Warnings.Any());
            return failed ? ExitCodes.Failed : ExitCodes.Ok;
        }
        catch (ConventionException ex)
        {
            stderr.WriteLine("traceability: " + ex.Message);
            return ExitCodes.NotChecked;
        }
    }

    private static void WriteText(TraceabilityReport report, TextWriter stdout)
    {
        foreach (var f in report.Failures.Concat(report.Warnings))
        {
            var label = f.Severity == Severity.Fail ? "FAIL" : "WARN";
            stdout.WriteLine($"{label}  {f.File}:{f.Line}  [{f.Rule}] {f.Why}");
            stdout.WriteLine($"      {f.Excerpt}");
        }

        stdout.WriteLine(
            $"Checked {report.Records.Count} records and {report.TestFileCount} test files: "
            + $"{report.Failures.Count()} failures, {report.Warnings.Count()} warnings.");
    }

    private static JsonObject ToJson(TraceabilityReport report)
    {
        JsonArray Tests(string prefix, string id) =>
            [.. report.LinksTo(prefix, id).Select(l => (JsonNode)new JsonObject { ["file"] = l.File, ["line"] = l.Line, ["method"] = l.Method })];

        static JsonArray Strings(IEnumerable<string> values) => [.. values.Select(v => (JsonNode)JsonValue.Create(v))];

        static JsonArray Findings(IEnumerable<Finding> findings) =>
            [.. findings.Select(f => (JsonNode)new JsonObject
            {
                ["rule"] = f.Rule,
                ["file"] = f.File,
                ["line"] = f.Line,
                ["why"] = f.Why,
                ["detail"] = f.Excerpt,
            })];

        var cases = report.OfKind(RecordKind.TestCase).ToList();
        return new JsonObject
        {
            ["requirements"] = new JsonArray([.. report.OfKind(RecordKind.Requirement).Select(r => (JsonNode)new JsonObject
            {
                ["id"] = r.Id,
                ["title"] = r.Field("title"),
                ["status"] = r.Field("status"),
                ["file"] = r.File,
                ["cases"] = Strings(cases.Where(c => c.List("requirements").Contains(r.Id, StringComparer.Ordinal)).Select(c => c.Id)),
            })]),
            ["cases"] = new JsonArray([.. cases.Select(c => (JsonNode)new JsonObject
            {
                ["id"] = c.Id,
                ["title"] = c.Field("title"),
                ["priority"] = c.Field("priority"),
                ["automation"] = c.Field("automation"),
                ["requirements"] = Strings(c.List("requirements")),
                ["file"] = c.File,
                ["tests"] = Tests("tc", c.Id),
            })]),
            ["bugs"] = new JsonArray([.. report.OfKind(RecordKind.BugReport).Select(b => (JsonNode)new JsonObject
            {
                ["id"] = b.Id,
                ["title"] = b.Field("title"),
                ["status"] = b.Field("status"),
                ["severity"] = b.Field("severity"),
                ["issue"] = b.Field("issue"),
                ["cases"] = Strings(b.List("cases")),
                ["file"] = b.File,
                ["tests"] = Tests("bug", b.Id),
            })]),
            ["fail"] = Findings(report.Failures),
            ["warn"] = Findings(report.Warnings),
        };
    }
}
