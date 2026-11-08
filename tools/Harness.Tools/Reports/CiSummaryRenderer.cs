using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Harness.Tools.Reports;

internal enum BaselineState
{
    NotRequested,
    NoEarlierResults,
    Broken,
    Available,
}

/// <summary>Renders a run as Markdown for a CI step summary.</summary>
internal static partial class CiSummaryRenderer
{
    private const int MaxExcerpt = 300;

    private const string Withheld = "(error text withheld: it looks like it holds a secret)";

    public static string Unavailable(string heading, string detail) => $"### ⚠️ {heading}\n\n{detail}\n";

    public static string Render(string heading, TrxRun run, string? note, BaselineState state, Baseline? baseline)
    {
        var failed = run.With(TrxOutcome.Failed).ToList();
        var skipped = run.With(TrxOutcome.Skipped).ToList();
        var passed = run.With(TrxOutcome.Passed).Count();
        var unexplained = run.RunProblem is not null && failed.Count == 0;

        var text = new StringBuilder();
        var icon = failed.Count > 0 ? "❌" : run.Tests.Count == 0 || unexplained ? "⚠️" : "✅";
        Line(text, $"### {icon} {heading}");
        Line(text);

        if (run.Tests.Count == 0)
        {
            Line(text, "No tests matched this selection.");
        }
        else
        {
            Line(text, "| Passed | Failed | Skipped | Duration |");
            Line(text, "|---:|---:|---:|---:|");
            Line(text, $"| {passed} | {failed.Count} | {skipped.Count} | {FormatDuration(run)} |");
        }

        Line(text);
        if (!string.IsNullOrWhiteSpace(note))
        {
            Line(text, note);
            Line(text);
        }

        if (unexplained)
        {
            Line(text, $"The test run reported a failure that no test shows: {Markdown.Cell(Excerpt(run.RunProblem) ?? "no details in the report")}");
            Line(text);
        }

        var known = failed.Where(t => t.Bugs.Count > 0).ToList();
        var unknown = failed.Where(t => t.Bugs.Count == 0).ToList();
        if (state == BaselineState.Available && run.Tests.Count > 0)
        {
            Compared(text, run, baseline!, known, unknown);
        }
        else
        {
            if (state != BaselineState.NotRequested && run.Tests.Count > 0)
            {
                Line(text, state == BaselineState.Broken
                    ? "Could not read the earlier results, so every failure is listed together."
                    : "No earlier results to compare with, so every failure is listed together.");
                Line(text);
            }

            ErrorTable(text, "**Failed**", unknown);
            KnownTable(text, known);
        }

        if (skipped.Count > 0)
        {
            Line(text, $"<details><summary>Skipped ({skipped.Count})</summary>");
            Line(text);
            Line(text, "| Test | Reason |");
            Line(text, "|---|---|");
            foreach (var test in skipped)
            {
                Line(text, $"| {Markdown.Cell(test.Name)} | {Markdown.Cell(Excerpt(test.Message) ?? "not recorded")} |");
            }

            Line(text);
            Line(text, "</details>");
            Line(text);
        }

        return text.ToString();
    }

    /// <summary>The outcome of each test that ran. A test that failed in any report counts as failed.</summary>
    public static Dictionary<string, string> Outcomes(TrxRun run) =>
        run.Tests.Where(t => t.Outcome != TrxOutcome.Skipped)
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Any(t => t.Outcome == TrxOutcome.Failed) ? Baseline.Failed : Baseline.Passed, StringComparer.Ordinal);

    private static void Compared(StringBuilder text, TrxRun run, Baseline baseline, List<TrxTest> known, List<TrxTest> unknown)
    {
        bool WasFailing(string name) => baseline.Outcomes.TryGetValue(name, out var earlier) && earlier == Baseline.Failed;

        var still = unknown.Where(t => WasFailing(t.Name)).ToList();
        var newly = unknown.Where(t => !WasFailing(t.Name)).ToList();
        var healed = Outcomes(run).Where(o => o.Value == Baseline.Passed && WasFailing(o.Key)).Select(o => o.Key).Order(StringComparer.Ordinal).ToList();

        var since = baseline.RunId is not null ? $"run {baseline.RunId}" : $"the results saved {baseline.SavedAt ?? "earlier"}";
        Line(text, $"**{newly.Count} newly failing, {known.Count} known, {healed.Count} fixed, {still.Count} still failing**, compared with {since}.");
        Line(text);

        ErrorTable(text, "**Newly failing**", newly);
        KnownTable(text, known);

        if (healed.Count > 0)
        {
            Line(text, "**Fixed since the last run**");
            Line(text);
            Line(text, "| Test |");
            Line(text, "|---|");
            foreach (var name in healed)
            {
                Line(text, $"| {Markdown.Cell(name)} |");
            }

            Line(text);
        }

        if (still.Count > 0)
        {
            Line(text, $"<details><summary>Still failing ({still.Count})</summary>");
            Line(text);
            ErrorRows(text, still);
            Line(text);
            Line(text, "</details>");
            Line(text);
        }
    }

    private static void ErrorTable(StringBuilder text, string title, List<TrxTest> tests)
    {
        if (tests.Count == 0)
        {
            return;
        }

        Line(text, title);
        Line(text);
        ErrorRows(text, tests);
        Line(text);
    }

    private static void ErrorRows(StringBuilder text, List<TrxTest> tests)
    {
        Line(text, "| Test | Error |");
        Line(text, "|---|---|");
        foreach (var test in tests)
        {
            Line(text, $"| {Markdown.Cell(test.Name)} | {Markdown.Cell(Excerpt(test.Message) ?? "no message")} |");
        }
    }

    private static void KnownTable(StringBuilder text, List<TrxTest> tests)
    {
        if (tests.Count == 0)
        {
            return;
        }

        Line(text, "**Known failures**");
        Line(text);
        Line(text, "| Test | Bug | Error |");
        Line(text, "|---|---|---|");
        foreach (var test in tests)
        {
            Line(text, $"| {Markdown.Cell(test.Name)} | {Markdown.Cell(string.Join(", ", test.Bugs))} | {Markdown.Cell(Excerpt(test.Message) ?? "no message")} |");
        }

        Line(text);
    }

    /// <summary>The first useful part of a message: no ANSI, no call log, no Playwright data, no locator lines.</summary>
    private static string? Excerpt(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var kept = new List<string>();
        foreach (var line in Ansi().Replace(message, string.Empty).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Call log:", StringComparison.Ordinal) || trimmed.StartsWith("Data:", StringComparison.Ordinal))
            {
                break;
            }

            if (!trimmed.StartsWith("Locator:", StringComparison.Ordinal) && !trimmed.StartsWith("Timeout:", StringComparison.Ordinal))
            {
                kept.Add(trimmed);
            }
        }

        var clean = Markdown.CollapseWhitespace(string.Join(' ', kept));
        if (ReportSecrets.Kinds(clean, []).Count > 0)
        {
            return Withheld;
        }

        return clean.Length > MaxExcerpt ? clean[..(MaxExcerpt - 1)] + "…" : clean;
    }

    private static string FormatDuration(TrxRun run)
    {
        var d = run.Duration;
        return d.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)d.TotalHours}h {d.Minutes:00}m")
            : d.TotalMinutes >= 1
                ? string.Create(CultureInfo.InvariantCulture, $"{(int)d.TotalMinutes}m {d.Seconds:00}s")
                : string.Create(CultureInfo.InvariantCulture, $"{d.TotalSeconds:0.0}s");
    }

    private static void Line(StringBuilder text, string line = "") => text.Append(line).Append('\n');

    // XML can't hold the escape character, so the TRX writer stores it as the text \u001b.
    [GeneratedRegex(@"\\u001[bB]\[[0-9;]*[A-Za-z]")]
    private static partial Regex Ansi();
}
