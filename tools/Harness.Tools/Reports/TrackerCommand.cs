using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Harness.Tools.Conventions;
using Harness.Tools.Traceability;
using static Harness.Tools.Reports.HtmlReport;

namespace Harness.Tools.Reports;

/// <summary>
/// Writes an HTML plan report. The status is derived from the committed QA records each time; only
/// the dated snapshots in the optional history file are kept.
/// </summary>
internal static partial class TrackerCommand
{
    public const string Usage = """
          tracker              Write an HTML plan report from docs/qa/. Problems in the records are shown,
                               not failed on; exit 2 if there are no records:
                                 --root <dir>                      (default: current directory)
                                 --out <file>                      (default <root>/TestResults/tracker/index.html)
                                 --results <file-or-folder>        (TRX, adds each case's last result)
                                 --history <file>                  (dated snapshots, shown newest first)
                                 --record                          (add today's snapshot to the history)
        """;

    private const int MaxHistoryRows = 30;

    private sealed record Snapshot(string At, int Accepted, int Covered, int Automated, int? Passing, int? Failing, int OpenBugs);

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (!ReportOptions.TryParse(args, ["--root", "--out", "--results", "--history"], ["--record"], out var options, out var error))
        {
            stderr.WriteLine(error);
            return ExitCodes.Usage;
        }

        var historyPath = options.Single("--history");
        var record = options.ContainsKey("--record");
        if (record && historyPath is null)
        {
            stderr.WriteLine("--record needs --history.");
            return ExitCodes.Usage;
        }

        var root = options.Single("--root") ?? Directory.GetCurrentDirectory();
        TraceabilityReport report;
        try
        {
            report = TraceabilityChecker.Run(root);
        }
        catch (ConventionException ex)
        {
            stderr.WriteLine("tracker: " + ex.Message);
            return ExitCodes.NotChecked;
        }

        var resultsPath = options.Single("--results");
        var run = resultsPath is null ? null : TrxReader.TryRead(resultsPath);
        var unreadable = resultsPath is not null && run is null ? $"No readable TRX at {resultsPath}." : null;
        if (unreadable is not null)
        {
            stderr.WriteLine(unreadable);
        }

        var today = CurrentSnapshot(report, run);
        var history = historyPath is null ? [] : ReadHistory(historyPath, stderr);
        if (record)
        {
            history.Add(today);
        }

        var output = options.Single("--out") ?? Path.Combine(root, "TestResults", "tracker", "index.html");
        try
        {
            HtmlReport.Write(output, Render(report, run, unreadable, historyPath is null ? null : history));
            if (record)
            {
                Append(historyPath!, today);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"Could not write {output}: {ex.Message}");
            return ExitCodes.Failed;
        }

        stdout.WriteLine($"Wrote {output}.");
        return ExitCodes.Ok;
    }

    private static string? ResultOf(QaRecord testCase, TrxRun run)
    {
        var tests = run.Tests.Where(t => t.Cases.Contains(testCase.Id, StringComparer.Ordinal)).ToList();
        return tests.Count == 0
            ? testCase.Field("automation") == "automated" ? "not run" : null
            : tests.Any(t => t.Outcome == TrxOutcome.Failed) ? "failed"
            : tests.Any(t => t.Outcome == TrxOutcome.Passed) ? "passed"
            : "skipped";
    }

    private static bool HasCases(TraceabilityReport report, QaRecord requirement) =>
        report.OfKind(RecordKind.TestCase).Any(c => c.List("requirements").Contains(requirement.Id, StringComparer.Ordinal));

    private static List<QaRecord> Accepted(TraceabilityReport report) =>
        [.. report.OfKind(RecordKind.Requirement).Where(r => r.Field("status") == "accepted")];

    private static List<QaRecord> Automated(TraceabilityReport report) =>
        [.. report.OfKind(RecordKind.TestCase).Where(c => c.Field("automation") == "automated")];

    private static List<QaRecord> OpenBugs(TraceabilityReport report) =>
        [.. report.OfKind(RecordKind.BugReport).Where(b => b.Field("status") == "open")];

    private static Snapshot CurrentSnapshot(TraceabilityReport report, TrxRun? run)
    {
        var accepted = Accepted(report);
        var automated = Automated(report);
        return new Snapshot(
            DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            accepted.Count,
            accepted.Count(r => HasCases(report, r)),
            automated.Count,
            run is null ? null : automated.Count(c => ResultOf(c, run) == "passed"),
            run is null ? null : automated.Count(c => ResultOf(c, run) == "failed"),
            OpenBugs(report).Count);
    }

    private static List<Snapshot> ReadHistory(string path, TextWriter stderr)
    {
        var snapshots = new List<Snapshot>();
        if (!File.Exists(path))
        {
            return snapshots;
        }

        var broken = 0;
        foreach (var line in File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            if (Parse(line) is { } snapshot)
            {
                snapshots.Add(snapshot);
            }
            else
            {
                broken++;
            }
        }

        if (broken > 0)
        {
            stderr.WriteLine($"Skipped {broken} unreadable history line(s) in {path}.");
        }

        return snapshots;
    }

    private static Snapshot? Parse(string line)
    {
        try
        {
            if (JsonNode.Parse(line) is not JsonObject o
                || o["at"]?.GetValue<string>() is not { } at
                || !DateOnly().IsMatch(at)
                || Number(o["accepted"]) is not { } accepted
                || Number(o["covered"]) is not { } covered
                || Number(o["automated"]) is not { } automated
                || Number(o["openBugs"]) is not { } openBugs)
            {
                return null;
            }

            return new Snapshot(at, accepted, covered, automated, Number(o["passing"]), Number(o["failing"]), openBugs);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static int? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    private static void Append(string path, Snapshot snapshot)
    {
        var line = new JsonObject
        {
            ["at"] = snapshot.At,
            ["accepted"] = snapshot.Accepted,
            ["covered"] = snapshot.Covered,
            ["automated"] = snapshot.Automated,
            ["passing"] = snapshot.Passing,
            ["failing"] = snapshot.Failing,
            ["openBugs"] = snapshot.OpenBugs,
        }.ToJsonString();

        CiFiles.EnsureFolder(path);
        var existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        var separator = existing.Length > 0 && !existing.EndsWith('\n') ? "\n" : string.Empty;
        File.AppendAllText(path, separator + line + "\n");
    }

    private static string Render(TraceabilityReport report, TrxRun? run, string? unreadable, List<Snapshot>? history)
    {
        var accepted = Accepted(report);
        var cases = report.OfKind(RecordKind.TestCase).ToList();
        var automated = Automated(report);
        var automatable = cases.Count(c => c.Field("automation") != "manual-only");
        var bugs = OpenBugs(report);

        var body = new StringBuilder("<h1>Test plan status</h1>\n");
        var failures = report.Failures.ToList();
        if (failures.Count > 0)
        {
            body.Append(CultureInfo.InvariantCulture, $"<p><strong>{failures.Count} problem(s) in the records, so the numbers below can't be trusted until they are fixed.</strong></p>\n")
                .Append(Table(["File", "Problem"], failures.Select(f => $"<tr><td>{E(f.File)}</td><td>{E(f.Excerpt)} {E(f.Why)}</td></tr>")));
        }

        if (unreadable is not null)
        {
            body.Append("<p><strong>").Append(E(unreadable)).Append("</strong> Last results are left out.</p>\n");
        }

        body.Append("<div class=\"tiles\">")
            .Append(Tile($"{accepted.Count(r => HasCases(report, r))} of {accepted.Count}", "accepted requirements with a test case"))
            .Append(Tile($"{automated.Count} of {automatable}", "automatable cases automated"));
        if (run is not null)
        {
            body.Append(Tile($"{automated.Count(c => ResultOf(c, run) == "passed")} of {automated.Count}", "automated cases passing"));
        }

        body.Append(Tile(bugs.Count.ToString(CultureInfo.InvariantCulture), "open bugs")).Append("</div>\n");

        body.Append("<h2>Gaps</h2>\n").Append(Gaps(report, accepted, cases));

        body.Append("<h2>Requirements</h2>\n").Append(Table(
            ["ID", "Title", "Status", "Cases"],
            report.OfKind(RecordKind.Requirement).Select(r =>
                $"<tr><td>{E(r.Id)}</td><td>{E(r.Field("title"))}</td><td>{E(r.Field("status"))}</td>"
                + $"<td>{E(string.Join(", ", cases.Where(c => c.List("requirements").Contains(r.Id, StringComparer.Ordinal)).Select(c => c.Id)))}</td></tr>")));

        string[] caseHeaders = run is null
            ? ["ID", "Title", "Priority", "Automation", "Requirements", "#Tests"]
            : ["ID", "Title", "Priority", "Automation", "Requirements", "#Tests", "Last result"];
        body.Append("<h2>Test cases</h2>\n").Append(Table(caseHeaders, cases.Select(c =>
        {
            var result = run is null ? string.Empty : $"<td>{E(ResultOf(c, run) ?? "-")}</td>";
            return $"<tr><td>{E(c.Id)}</td><td>{E(c.Field("title"))}</td><td>{E(c.Field("priority"))}</td><td>{E(c.Field("automation"))}</td>"
                + $"<td>{E(c.Field("requirements"))}</td><td class=\"num\">{report.LinksTo("tc", c.Id).Count()}</td>{result}</tr>";
        })));

        body.Append("<h2>Open bugs</h2>\n");
        body.Append(bugs.Count == 0
            ? "<p>None.</p>\n"
            : Table(["ID", "Title", "Severity", "Cases", "Issue"], bugs.Select(b =>
                $"<tr><td>{E(b.Id)}</td><td>{E(b.Field("title"))}</td><td>{E(b.Field("severity"))}</td><td>{E(string.Join(", ", b.List("cases")))}</td><td>{E(b.Field("issue"))}</td></tr>")));

        if (history is not null)
        {
            body.Append("<h2>History</h2>\n").Append(HistoryTable(history));
        }

        var warnings = report.Warnings.ToList();
        if (warnings.Count > 0)
        {
            body.Append(CultureInfo.InvariantCulture, $"<details><summary>Warnings ({warnings.Count})</summary>")
                .Append(Table(["File", "Warning"], warnings.Select(f => $"<tr><td>{E(f.File)}</td><td>{E(f.Excerpt)} {E(f.Why)}</td></tr>")))
                .Append("</details>\n");
        }

        return Page("Test plan status", body.ToString());
    }

    private static string Gaps(TraceabilityReport report, List<QaRecord> accepted, List<QaRecord> cases)
    {
        var uncovered = accepted.Where(r => !HasCases(report, r)).ToList();
        var waiting = cases.Where(c => c.Field("priority") == "high" && c.Field("automation") == "planned").ToList();
        if (uncovered.Count == 0 && waiting.Count == 0)
        {
            return "<p>No gaps.</p>\n";
        }

        var rows = uncovered.Select(r => $"<tr><td>{E(r.Id)}</td><td>{E(r.Field("title"))}</td><td>Accepted, with no test case</td></tr>")
            .Concat(waiting.Select(c => $"<tr><td>{E(c.Id)}</td><td>{E(c.Field("title"))}</td><td>High priority, not automated</td></tr>"));
        return Table(["ID", "Title", "Gap"], rows);
    }

    private static string HistoryTable(List<Snapshot> history)
    {
        if (history.Count == 0)
        {
            return "<p>No snapshots yet.</p>\n";
        }

        static string Optional(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

        var days = new Dictionary<string, Snapshot>(StringComparer.Ordinal);
        foreach (var snapshot in history)
        {
            days[snapshot.At] = snapshot;
        }

        var rows = days.Values.OrderByDescending(s => s.At, StringComparer.Ordinal).Take(MaxHistoryRows).Select(s =>
            $"<tr><td>{E(s.At)}</td><td class=\"num\">{s.Covered} of {s.Accepted}</td><td class=\"num\">{s.Automated}</td>"
            + $"<td class=\"num\">{Optional(s.Passing)}</td><td class=\"num\">{Optional(s.Failing)}</td><td class=\"num\">{s.OpenBugs}</td></tr>");
        return Table(["Date", "#Accepted with a case", "#Automated", "#Passing", "#Failing", "#Open bugs"], rows);
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex DateOnly();
}
