using System.Globalization;
using System.Text;
using Harness.Tools.Traceability;
using static Harness.Tools.Reports.HtmlReport;

namespace Harness.Tools.Reports;

/// <summary>
/// Writes one static HTML page from the TRX of a run's jobs. A test in several jobs counts once, as
/// failed if it failed in any. The page holds names and counts only, never error text.
/// </summary>
internal static class DashboardCommand
{
    public const string Usage = """
          dashboard            Write a static HTML results page. Failures still exit 0; exit 2 if no job has results:
                                 --run <job>=<file-or-folder>      (required, repeat for each job)
                                 --out <file>                      (default <root>/TestResults/dashboard/index.html)
                                 --root <dir>                      (project with docs/qa/, default: current directory)
        """;

    private sealed record Job(string Name, TrxRun? Run);

    private sealed record Area(string Layer, string Name, int Passed, int Failed);

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, Func<string, string?> env)
    {
        if (!ReportOptions.TryParse(args, ["--run", "--out", "--root"], [], out var options, out var error))
        {
            stderr.WriteLine(error);
            return ExitCodes.Usage;
        }

        if (!options.TryGetValue("--run", out var runs))
        {
            stderr.WriteLine("dashboard needs at least one --run <job>=<file-or-folder>.");
            return ExitCodes.Usage;
        }

        var jobs = new List<Job>();
        foreach (var run in runs)
        {
            var equals = run.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0 || equals == run.Length - 1)
            {
                stderr.WriteLine($"Expected <job>=<file-or-folder>, got '{run}'.");
                return ExitCodes.Usage;
            }

            jobs.Add(new Job(run[..equals], TrxReader.TryRead(run[(equals + 1)..])));
        }

        if (jobs.All(j => j.Run is null))
        {
            stderr.WriteLine($"Not checked: no results from {string.Join(", ", jobs.Select(j => j.Name))}.");
            return ExitCodes.NotChecked;
        }

        var root = options.Single("--root") ?? Directory.GetCurrentDirectory();
        var output = options.Single("--out") ?? Path.Combine(root, "TestResults", "dashboard", "index.html");
        var tests = Merge(jobs);
        try
        {
            HtmlReport.Write(output, Render(jobs, tests, BugRecords(root), env));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"Could not write {output}: {ex.Message}");
            return ExitCodes.Failed;
        }

        stdout.WriteLine($"Wrote {output}: {tests.Count} test(s).");
        return ExitCodes.Ok;
    }

    private static Dictionary<string, QaRecord> BugRecords(string root) =>
        RecordReader.Read(root).Records.Where(r => r.Kind == RecordKind.BugReport).ToDictionary(r => r.Id, StringComparer.Ordinal);

    private static List<TrxTest> Merge(List<Job> jobs) =>
        [.. jobs.Where(j => j.Run is not null).SelectMany(j => j.Run!.Tests)
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .Select(g => g.First() with
            {
                Outcome = g.Any(t => t.Outcome == TrxOutcome.Failed) ? TrxOutcome.Failed
                    : g.Any(t => t.Outcome == TrxOutcome.Passed) ? TrxOutcome.Passed
                    : TrxOutcome.Skipped,
                Bugs = [.. g.SelectMany(t => t.Bugs).Distinct(StringComparer.Ordinal)],
                Cases = [.. g.SelectMany(t => t.Cases).Distinct(StringComparer.Ordinal)],
            })];

    private static (string Layer, string Area) Place(TrxTest test)
    {
        var parts = (test.ClassName ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return ("Other", parts.Length == 1 ? AreaName(parts[0]) : "Other");
        }

        var layer = parts[^2] switch
        {
            "Ui" => "UI",
            "Api" => "API",
            var other => other,
        };
        return (layer, AreaName(parts[^1]));
    }

    private static string AreaName(string className) =>
        className.Length > 5 && className.EndsWith("Tests", StringComparison.Ordinal) ? className[..^5] : className;

    private static string Render(List<Job> jobs, List<TrxTest> tests, Dictionary<string, QaRecord> bugs, Func<string, string?> env)
    {
        var failed = tests.Where(t => t.Outcome == TrxOutcome.Failed).ToList();
        var unexplained = failed.Where(t => t.Bugs.Count == 0).ToList();

        var body = new StringBuilder();
        body.Append("<h1>Test results</h1>\n<p class=\"muted\">").Append(Source(env)).Append("</p>\n");

        var missing = jobs.Where(j => j.Run is null).Select(j => E(j.Name)).ToList();
        if (missing.Count > 0)
        {
            body.Append(CultureInfo.InvariantCulture, $"<p><strong>No results from: {string.Join(", ", missing)}.</strong> The counts below leave those out.</p>\n");
        }

        body.Append("<div class=\"tiles\">")
            .Append(Tile(Count(tests.Count), "tests"))
            .Append(Tile(Count(tests.Count(t => t.Outcome == TrxOutcome.Passed)), "passed"))
            .Append(Tile(Count(failed.Count), "failed"))
            .Append(Tile(Count(tests.Count(t => t.Outcome == TrxOutcome.Skipped)), "skipped"))
            .Append("</div>\n");

        if (tests.Count == 0)
        {
            body.Append("<p>No tests ran.</p>\n");
        }

        body.Append("<h2>Results by job</h2>\n").Append(JobTable(jobs, tests.Count));

        if (failed.Count > 0)
        {
            body.Append("<h2>Why they failed</h2>\n<div class=\"tiles\">")
                .Append(Tile(Count(failed.Count - unexplained.Count), "linked to a bug report"))
                .Append(Tile(Count(unexplained.Count), "with no recorded reason"))
                .Append("</div>\n<p class=\"muted\">A reason is what the project's records say about that test. It doesn't prove why this run failed.</p>\n");
            if (unexplained.Count > 0)
            {
                body.Append("<h3>No recorded reason</h3>\n").Append(FailureTable(unexplained, bugs));
            }
        }

        if (tests.Count > 0)
        {
            var areas = Areas(tests);
            body.Append("<h2>Tests by area</h2>\n").Append(AreaChart(areas));
            if (failed.Count > 0)
            {
                body.Append(CultureInfo.InvariantCulture, $"<h2>All failures: {failed.Count}</h2>\n");
                foreach (var area in areas.Where(a => a.Failed > 0))
                {
                    var inArea = failed.Where(t => Place(t) == (area.Layer, area.Name)).ToList();
                    body.Append(CultureInfo.InvariantCulture, $"<details><summary>{E(area.Layer)} / {E(area.Name)}: {inArea.Count} failure(s)</summary>")
                        .Append(FailureTable(inArea, bugs)).Append("</details>\n");
                }
            }
        }

        return Page("Test results", body.ToString());
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Source(Func<string, string?> env)
    {
        var parts = new List<string>();
        if (env("GITHUB_REPOSITORY") is { Length: > 0 } repository)
        {
            var url = $"{env("GITHUB_SERVER_URL") ?? "https://github.com"}/{repository}";
            if (env("GITHUB_RUN_ID") is { Length: > 0 } runId)
            {
                parts.Add($"<a href=\"{E($"{url}/actions/runs/{runId}")}\">workflow run</a>");
            }

            if (env("GITHUB_SHA") is { Length: > 0 } commit)
            {
                parts.Add($"commit <a href=\"{E($"{url}/commit/{commit}")}\">{E(commit[..Math.Min(7, commit.Length)])}</a>");
            }
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : "Local run";
    }

    private static string JobTable(List<Job> jobs, int testCount)
    {
        var read = jobs.Where(j => j.Run is not null).ToList();
        var rows = read.Select(j =>
        {
            var run = j.Run!;
            return $"<tr><td>{E(j.Name)}</td><td class=\"num\">{run.Tests.Count}</td><td class=\"num\">{run.With(TrxOutcome.Passed).Count()}</td>"
                + $"<td class=\"num\">{run.With(TrxOutcome.Failed).Count()}</td><td class=\"num\">{run.With(TrxOutcome.Skipped).Count()}</td></tr>";
        });

        var entries = read.Sum(j => j.Run!.Tests.Count);
        var note = entries == testCount
            ? string.Empty
            : $"<p class=\"muted\">The jobs add up to {entries}, not {testCount}, because some tests ran in more than one job. The totals above count each test once.</p>\n";
        return Table(["Job", "#Tests", "#Passed", "#Failed", "#Skipped"], rows) + "\n" + note;
    }

    private static string FailureTable(List<TrxTest> tests, Dictionary<string, QaRecord> bugs)
    {
        var rows = tests.Select(t =>
            $"<tr><td>{E(t.Name)}</td><td>{E(t.Cases.Count > 0 ? string.Join(", ", t.Cases) : "-")}</td><td>{Reason(t, bugs)}</td></tr>");
        return Table(["Test", "Case", "Recorded reason"], rows);
    }

    private static string Reason(TrxTest test, Dictionary<string, QaRecord> bugs)
    {
        if (test.Bugs.Count == 0)
        {
            return "<strong>No recorded reason</strong>";
        }

        return string.Join("<br>", test.Bugs.Select(id =>
        {
            if (!bugs.TryGetValue(id, out var bug))
            {
                return E(id);
            }

            var closed = bug.Field("status") == "open" ? string.Empty : $" ({bug.Field("status")})";
            return E($"{id}: {bug.Field("title")}{closed}");
        }));
    }

    private static List<Area> Areas(List<TrxTest> tests) =>
        [.. tests.Where(t => t.Outcome != TrxOutcome.Skipped)
            .GroupBy(Place)
            .Select(g => new Area(g.Key.Layer, g.Key.Area, g.Count(t => t.Outcome == TrxOutcome.Passed), g.Count(t => t.Outcome == TrxOutcome.Failed)))
            .OrderByDescending(a => a.Failed)
            .ThenBy(a => a.Name, StringComparer.Ordinal)];

    private static string AreaChart(List<Area> areas)
    {
        if (areas.Count == 0)
        {
            return "<p>Every test was skipped.</p>\n";
        }

        var widest = areas.Max(a => a.Passed + a.Failed);
        var chart = new StringBuilder("<div class=\"legend\"><span><i class=\"key passed\"></i>Passed</span><span><i class=\"key failed\"></i>Failed</span></div>\n");

        var layers = areas.GroupBy(a => a.Layer, StringComparer.Ordinal)
            .OrderByDescending(l => l.Sum(a => a.Failed))
            .ThenBy(l => l.Key, StringComparer.Ordinal);
        foreach (var layer in layers)
        {
            chart.Append(CultureInfo.InvariantCulture, $"<div class=\"layer\"><span>{E(layer.Key)}</span><span>{layer.Sum(a => a.Passed)} passed, {layer.Sum(a => a.Failed)} failed</span></div>\n");
            foreach (var area in layer)
            {
                var label = $"{area.Passed} passed, {area.Failed} failed";
                chart.Append(CultureInfo.InvariantCulture, $"<div class=\"row\"><span class=\"name\">{E(area.Name)}</span><span class=\"track\" role=\"img\" aria-label=\"{label}\">")
                    .Append(Segment(area, "passed", area.Passed, widest))
                    .Append(Segment(area, "failed", area.Failed, widest))
                    .Append(CultureInfo.InvariantCulture, $"</span><span class=\"counts\">{label}</span></div>\n");
            }
        }

        return chart.ToString();
    }

    private static string Segment(Area area, string kind, int count, int widest)
    {
        if (count == 0)
        {
            return string.Empty;
        }

        var width = (count * 100.0 / widest).ToString("0.##", CultureInfo.InvariantCulture);
        return $"<span class=\"bar {kind}\" style=\"width:{width}%\" title=\"{E(area.Name)}: {count} {kind}\"></span>";
    }
}
