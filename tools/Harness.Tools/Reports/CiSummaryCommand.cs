using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;

namespace Harness.Tools.Reports;

internal static class CiFiles
{
    public static void EnsureFolder(string file)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(file));
        if (folder is not null)
        {
            Directory.CreateDirectory(folder);
        }
    }
}

/// <summary>
/// Turns TRX into a Markdown summary, optionally against the previous run. It reports and never
/// judges: the test step's exit code is the verdict, so a failed or missing run still exits 0.
/// </summary>
internal static class CiSummaryCommand
{
    public const string Usage = """
          ci-summary           Summarize TRX as Markdown, to $GITHUB_STEP_SUMMARY when set. Always exits 0
                               unless the arguments are wrong: the test step gives the verdict.
                                 --results <file-or-folder>        (required, every *.trx below it)
                                 --heading <text>                  (default "Test results")
                                 --note <text>                     (printed under the counts)
                                 --baseline <file>                 (compare with earlier outcomes)
                                 --update-baseline                 (save this run's outcomes there)
                                 --json <file>                     (counts and failing names, no error text)
                                 --output <file>                   (append here instead)
        """;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, Func<string, string?> env)
    {
        if (!ReportOptions.TryParse(
            args,
            ["--results", "--heading", "--note", "--baseline", "--json", "--output"],
            ["--update-baseline"],
            out var options,
            out var error))
        {
            stderr.WriteLine(error);
            return ExitCodes.Usage;
        }

        if (options.Single("--results") is not { } results)
        {
            stderr.WriteLine("ci-summary needs --results.");
            return ExitCodes.Usage;
        }

        var baselinePath = options.Single("--baseline");
        var update = options.ContainsKey("--update-baseline");
        if (update && baselinePath is null)
        {
            stderr.WriteLine("--update-baseline needs --baseline.");
            return ExitCodes.Usage;
        }

        var heading = options.Single("--heading") ?? "Test results";
        try
        {
            var markdown = Summarize(results, heading, options.Single("--note"), baselinePath, update, options.Single("--json"), env);
            Deliver(markdown, options.Single("--output") ?? env("GITHUB_STEP_SUMMARY"), stdout);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"Could not write the summary: {ex.Message}");
        }

        return ExitCodes.Ok;
    }

    private static string Summarize(string results, string heading, string? note, string? baselinePath, bool update, string? jsonPath, Func<string, string?> env)
    {
        var files = File.Exists(results) ? [results]
            : Directory.Exists(results) ? Directory.GetFiles(results, "*.trx", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray()
            : [];
        if (files.Length == 0)
        {
            return CiSummaryRenderer.Unavailable(heading, $"No TRX report at {results}.");
        }

        TrxRun run;
        try
        {
            run = TrxReader.Read(files);
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return CiSummaryRenderer.Unavailable(heading, $"Could not read the TRX report at {results}.");
        }

        Baseline? baseline = null;
        var state = BaselineState.NotRequested;
        if (baselinePath is not null)
        {
            baseline = Baseline.TryRead(baselinePath, out var broken);
            state = broken ? BaselineState.Broken
                : baseline is null || baseline.Outcomes.Count == 0 ? BaselineState.NoEarlierResults
                : BaselineState.Available;
        }

        var markdown = CiSummaryRenderer.Render(heading, run, note, state, baseline);

        var current = CiSummaryRenderer.Outcomes(run);
        if (update && current.Count > 0)
        {
            Baseline.Save(baselinePath!, env("GITHUB_RUN_ID"), baseline, current);
            if (env("GITHUB_OUTPUT") is { Length: > 0 } output)
            {
                File.AppendAllText(output, "baseline-updated=true\n");
            }
        }

        if (jsonPath is not null)
        {
            WriteJson(jsonPath, heading, run);
        }

        return markdown;
    }

    private static void Deliver(string markdown, string? file, TextWriter stdout)
    {
        if (string.IsNullOrEmpty(file))
        {
            stdout.Write(markdown);
            return;
        }

        CiFiles.EnsureFolder(file);
        File.AppendAllText(file, markdown);
    }

    private static void WriteJson(string path, string heading, TrxRun run)
    {
        var failed = run.With(TrxOutcome.Failed).ToList();
        var status = failed.Count > 0 ? "failed"
            : run.Tests.Count == 0 ? "no-tests"
            : run.RunProblem is not null ? "run-problem"
            : "passed";

        var failures = new JsonArray();
        foreach (var test in failed)
        {
            failures.Add(new JsonObject
            {
                ["name"] = test.Name,
                ["bugs"] = new JsonArray([.. test.Bugs.Select(b => (JsonNode?)JsonValue.Create(b))]),
            });
        }

        var root = new JsonObject
        {
            ["heading"] = heading,
            ["status"] = status,
            ["total"] = run.Tests.Count,
            ["passed"] = run.With(TrxOutcome.Passed).Count(),
            ["failed"] = failed.Count,
            ["skipped"] = run.With(TrxOutcome.Skipped).Count(),
            ["durationSeconds"] = Math.Round(run.Duration.TotalSeconds, 1),
            ["failures"] = failures,
        };

        CiFiles.EnsureFolder(path);
        File.WriteAllText(path, root.ToJsonString(Indented));
    }
}
