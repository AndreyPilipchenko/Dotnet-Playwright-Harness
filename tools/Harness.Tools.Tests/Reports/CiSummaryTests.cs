using System.Text.Json;
using Harness.Tools.Reports;

namespace Harness.Tools.Tests.Reports;

/// <summary>
/// <c>ci-summary</c> turns TRX into Markdown. It reports and never judges: the test step's exit code
/// is the verdict, so it exits 0 even for a failed or missing run.
/// </summary>
public class CiSummaryTests
{
    private static readonly TrxCase Passing = new("Passes");
    private static readonly TrxCase Failing = new("Fails", "Failed", "boom");
    private static readonly TrxCase KnownFailing = new("Known", "Failed", "known boom", Categories: ["bug:BUG-7", "tc:TC-1"]);

    private static (int Code, string Out, string Err) Run(Dictionary<string, string>? env, params string[] args)
    {
        var io = new Console3();
        var code = CiSummaryCommand.Run(args, io.Out, io.Err, name => env?.GetValueOrDefault(name));
        return (code, io.Out.ToString(), io.Err.ToString());
    }

    private static (int Code, string Out, string Err) Summarize(TempDir dir, params TrxCase[] tests)
    {
        dir.Write("r/a.trx", TrxFixture.Run(tests));
        return Run(null, "--results", dir.Combine("r"));
    }

    [Fact]
    public void NoResultsOption_IsAUsageError()
    {
        Assert.Equal(ExitCodes.Usage, Run(null).Code);
    }

    [Fact]
    public void UnknownOption_IsAUsageError()
    {
        Assert.Equal(ExitCodes.Usage, Run(null, "--results", "x", "--nope", "y").Code);
    }

    [Fact]
    public void UpdateBaselineWithoutABaseline_IsAUsageError()
    {
        Assert.Equal(ExitCodes.Usage, Run(null, "--results", "x", "--update-baseline").Code);
    }

    [Fact]
    public void IsRoutedFromTheCli()
    {
        var io = new Console3();

        Assert.Equal(ExitCodes.Usage, Cli.Run(["ci-summary"], io.In, io.Out, io.Err));
    }

    [Fact]
    public void Counts_EachOutcome_AndExitsZeroOnAFailedRun()
    {
        using var dir = new TempDir();

        var (code, output, _) = Summarize(dir, Passing, Failing, new TrxCase("Skipped", "NotExecuted", "not today"));

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("### ❌ Test results", output, StringComparison.Ordinal);
        Assert.Contains("| Passed | Failed | Skipped | Duration |", output, StringComparison.Ordinal);
        Assert.Contains("| 1 | 1 | 1 | 1m 05s |", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AllPassing_GetsTheTick_AndCustomHeading()
    {
        using var dir = new TempDir();
        dir.Write("r/a.trx", TrxFixture.Run(Passing));

        var (_, output, _) = Run(null, "--results", dir.Combine("r"), "--heading", "Smoke");

        Assert.Contains("### ✅ Smoke", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsASingleFile()
    {
        using var dir = new TempDir();
        dir.Write("a.trx", TrxFixture.Run(Passing));

        Assert.Contains("| 1 | 0 | 0 |", Run(null, "--results", dir.Combine("a.trx")).Out, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsEveryTrxUnderAFolder()
    {
        using var dir = new TempDir();
        dir.Write("r/a/one.trx", TrxFixture.Run(Passing));
        dir.Write("r/b/two.trx", TrxFixture.Run(new TrxCase("Other")));

        Assert.Contains("| 2 | 0 | 0 |", Run(null, "--results", dir.Combine("r")).Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Error")]
    [InlineData("Timeout")]
    [InlineData("Aborted")]
    public void EveryFailingOutcome_CountsAsFailed(string outcome)
    {
        using var dir = new TempDir();

        Assert.Contains("| 0 | 1 | 0 |", Summarize(dir, new TrxCase("X", outcome, "e")).Out, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedTest_IsNamedWithItsClass_AndItsError()
    {
        using var dir = new TempDir();

        var (_, output, _) = Summarize(dir, Failing);

        Assert.Contains("**Failed**", output, StringComparison.Ordinal);
        Assert.Contains("| Acme.Tests.Ui.TodosTests.Fails | boom |", output, StringComparison.Ordinal);
    }

    [Fact]
    public void XunitFullName_IsNotPrefixedTwice()
    {
        using var dir = new TempDir();

        var (_, output, _) = Summarize(dir, new TrxCase("Fails", "Failed", "boom", TestName: "Acme.Tests.Ui.TodosTests.Fails"));

        Assert.Contains("| Acme.Tests.Ui.TodosTests.Fails | boom |", output, StringComparison.Ordinal);
        Assert.DoesNotContain("TodosTests.Acme", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorExcerpt_DropsCallLogAndData_AndLocatorLines_AndAnsi()
    {
        using var dir = new TempDir();
        var message = @"\u001b[31mSystem.TimeoutException\u001b[39m" + " : Timeout 30000ms exceeded.\nLocator: getByRole('x')\nTimeout: 5000ms\nExpected: 1\nCall log:\n  - waiting for x\nData:\n  playwright.errorDetails: y";

        var (_, output, _) = Summarize(dir, new TrxCase("F", "Failed", message));

        Assert.Contains("System.TimeoutException : Timeout 30000ms exceeded. Expected: 1 |", output, StringComparison.Ordinal);
        Assert.DoesNotContain("waiting for", output, StringComparison.Ordinal);
        Assert.DoesNotContain("getByRole", output, StringComparison.Ordinal);
        Assert.DoesNotContain("errorDetails", output, StringComparison.Ordinal);
        Assert.DoesNotContain("u001b", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorExcerpt_DropsDataWhenThereIsNoCallLog()
    {
        using var dir = new TempDir();

        var (_, output, _) = Summarize(dir, new TrxCase("F", "Failed", "boom\nData:\n  x: y"));

        Assert.Contains("| boom |", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorExcerpt_IsCappedAt300Characters()
    {
        using var dir = new TempDir();

        var (_, output, _) = Summarize(dir, new TrxCase("F", "Failed", new string('e', 400)));

        Assert.Contains(new string('e', 299) + "…", output, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('e', 300), output, StringComparison.Ordinal);
    }

    [Fact]
    public void TableCells_AreEscaped()
    {
        using var dir = new TempDir();

        var (_, output, _) = Summarize(dir, new TrxCase("F", "Failed", "a|b <c> & d"));

        Assert.Contains("| a&#124;b &lt;c&gt; &amp; d |", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorThatHoldsASecret_IsWithheld()
    {
        using var dir = new TempDir();
        var token = "ghp_" + new string('a', 36);

        var (_, output, _) = Summarize(dir, new TrxCase("F", "Failed", $"sent {token}"));

        Assert.DoesNotContain(token, output, StringComparison.Ordinal);
        Assert.Contains("withheld", output, StringComparison.Ordinal);
    }

    [Fact]
    public void StackTraces_AreNotPublished()
    {
        using var dir = new TempDir();

        Assert.DoesNotContain("Secret.Stack", Summarize(dir, Failing).Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Skipped_ListsReasons_InACollapsedBlock()
    {
        using var dir = new TempDir();

        var (_, output, _) = Summarize(dir, new TrxCase("S", "NotExecuted", "not today"), new TrxCase("T", "NotExecuted"));

        Assert.Contains("<details><summary>Skipped (2)</summary>", output, StringComparison.Ordinal);
        Assert.Contains("| Acme.Tests.Ui.TodosTests.S | not today |", output, StringComparison.Ordinal);
        Assert.Contains("| Acme.Tests.Ui.TodosTests.T | not recorded |", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ZeroTests_IsAWarning_NotATick()
    {
        using var dir = new TempDir();

        var (_, output, _) = Summarize(dir);

        Assert.Contains("### ⚠️ Test results", output, StringComparison.Ordinal);
        Assert.Contains("No tests matched this selection.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingReport_WarnsAndExitsZero()
    {
        using var dir = new TempDir();

        var (code, output, _) = Run(null, "--results", dir.Combine("missing"));

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("### ⚠️ Test results", output, StringComparison.Ordinal);
        Assert.Contains("No TRX report at", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not xml")]
    [InlineData("<Other />")]
    [InlineData("<!DOCTYPE x [<!ENTITY a \"b\">]><TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\" />")]
    public void UnreadableReport_WarnsAndExitsZero(string content)
    {
        using var dir = new TempDir();
        dir.Write("r/a.trx", content);

        var (code, output, _) = Run(null, "--results", dir.Combine("r"));

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("Could not read", output, StringComparison.Ordinal);
    }

    [Fact]
    public void RunFailureThatNoTestShows_IsAWarning_WithTheRunnersText()
    {
        using var dir = new TempDir();
        dir.Write("r/a.trx", TrxFixture.CrashedRun("Test host process crashed", Passing));

        var (_, output, _) = Run(null, "--results", dir.Combine("r"));

        Assert.Contains("### ⚠️ Test results", output, StringComparison.Ordinal);
        Assert.Contains("Test host process crashed", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Note_IsPrinted()
    {
        using var dir = new TempDir();
        dir.Write("r/a.trx", TrxFixture.Run(Passing));

        Assert.Contains("nightly on staging", Run(null, "--results", dir.Combine("r"), "--note", "nightly on staging").Out, StringComparison.Ordinal);
    }

    [Fact]
    public void KnownFailures_AreListedApart_WithTheirBug()
    {
        using var dir = new TempDir();

        var (_, output, _) = Summarize(dir, Failing, KnownFailing);

        Assert.Contains("### ❌ Test results", output, StringComparison.Ordinal);
        Assert.Contains("**Known failures**", output, StringComparison.Ordinal);
        Assert.Contains("| Acme.Tests.Ui.TodosTests.Known | BUG-7 | known boom |", output, StringComparison.Ordinal);
        Assert.DoesNotContain("| Acme.Tests.Ui.TodosTests.Known | known boom |", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Baseline_SeparatesNewKnownFixedAndStillFailing()
    {
        using var dir = new TempDir();
        var cls = "Acme.Tests.Ui.TodosTests.";
        dir.Write("base.json", JsonSerializer.Serialize(new
        {
            runId = "41",
            savedAt = "2026-11-07T00:00:00Z",
            outcomes = new Dictionary<string, string>
            {
                [cls + "Still"] = "failed",
                [cls + "Healed"] = "failed",
                [cls + "New"] = "passed",
                [cls + "Known"] = "passed",
            },
        }));
        dir.Write("r/a.trx", TrxFixture.Run(
            new TrxCase("Still", "Failed", "still boom"),
            new TrxCase("Healed"),
            new TrxCase("New", "Failed", "new boom"),
            KnownFailing));

        var (_, output, _) = Run(null, "--results", dir.Combine("r"), "--baseline", dir.Combine("base.json"));

        Assert.Contains("**1 newly failing, 1 known, 1 fixed, 1 still failing**, compared with run 41", output, StringComparison.Ordinal);
        var newly = output.IndexOf("**Newly failing**", StringComparison.Ordinal);
        var known = output.IndexOf("**Known failures**", StringComparison.Ordinal);
        var fixedAt = output.IndexOf("**Fixed since the last run**", StringComparison.Ordinal);
        var still = output.IndexOf("<details><summary>Still failing (1)</summary>", StringComparison.Ordinal);
        Assert.True(newly >= 0 && newly < known && known < fixedAt && fixedAt < still, output);
        Assert.Contains("| " + cls + "New | new boom |", output, StringComparison.Ordinal);
        Assert.Contains("| " + cls + "Healed |", output, StringComparison.Ordinal);
        Assert.DoesNotContain("**Failed**", output, StringComparison.Ordinal);
    }

    [Fact]
    public void NoEarlierResults_ListsEveryFailureTogether()
    {
        using var dir = new TempDir();
        dir.Write("r/a.trx", TrxFixture.Run(Failing));

        var (_, output, _) = Run(null, "--results", dir.Combine("r"), "--baseline", dir.Combine("none.json"));

        Assert.Contains("No earlier results to compare with, so every failure is listed together.", output, StringComparison.Ordinal);
        Assert.Contains("**Failed**", output, StringComparison.Ordinal);
    }

    [Fact]
    public void BrokenBaseline_IsSaidPlainly_AndEveryFailureIsListed()
    {
        using var dir = new TempDir();
        dir.Write("base.json", "{ nope");
        dir.Write("r/a.trx", TrxFixture.Run(Failing));

        var (code, output, _) = Run(null, "--results", dir.Combine("r"), "--baseline", dir.Combine("base.json"));

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("Could not read the earlier results", output, StringComparison.Ordinal);
        Assert.Contains("**Failed**", output, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateBaseline_StoresNamesAndOutcomes_NeverErrorText_AndAddsRunId()
    {
        using var dir = new TempDir();
        dir.Write("r/a.trx", TrxFixture.Run(Passing, Failing));
        var env = new Dictionary<string, string> { ["GITHUB_RUN_ID"] = "77" };

        Run(env, "--results", dir.Combine("r"), "--baseline", dir.Combine("base.json"), "--update-baseline");

        var saved = File.ReadAllText(dir.Combine("base.json"));
        Assert.DoesNotContain("boom", saved, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(saved);
        Assert.Equal("77", json.RootElement.GetProperty("runId").GetString());
        Assert.True(json.RootElement.TryGetProperty("savedAt", out _));
        var outcomes = json.RootElement.GetProperty("outcomes");
        Assert.Equal("passed", outcomes.GetProperty("Acme.Tests.Ui.TodosTests.Passes").GetString());
        Assert.Equal("failed", outcomes.GetProperty("Acme.Tests.Ui.TodosTests.Fails").GetString());
    }

    [Fact]
    public void WithoutUpdateFlag_TheBaselineIsLeftAlone()
    {
        using var dir = new TempDir();
        dir.Write("r/a.trx", TrxFixture.Run(Passing));

        Run(null, "--results", dir.Combine("r"), "--baseline", dir.Combine("base.json"));

        Assert.False(File.Exists(dir.Combine("base.json")));
    }

    [Fact]
    public void UpdateBaseline_KeepsTheEarlierOutcome_OfATestThatDidNotRun()
    {
        using var dir = new TempDir();
        var cls = "Acme.Tests.Ui.TodosTests.";
        dir.Write("base.json", JsonSerializer.Serialize(new { runId = "1", savedAt = "x", outcomes = new Dictionary<string, string> { [cls + "Elsewhere"] = "failed", [cls + "Skipped"] = "failed" } }));
        dir.Write("r/a.trx", TrxFixture.Run(Passing, new TrxCase("Skipped", "NotExecuted")));

        Run(null, "--results", dir.Combine("r"), "--baseline", dir.Combine("base.json"), "--update-baseline");

        using var json = JsonDocument.Parse(File.ReadAllText(dir.Combine("base.json")));
        var outcomes = json.RootElement.GetProperty("outcomes");
        Assert.Equal("failed", outcomes.GetProperty(cls + "Elsewhere").GetString());
        Assert.Equal("failed", outcomes.GetProperty(cls + "Skipped").GetString());
        Assert.Equal("passed", outcomes.GetProperty(cls + "Passes").GetString());
    }

    [Fact]
    public void ATestThatFailsInAnyReport_IsStoredAsFailed()
    {
        using var dir = new TempDir();
        dir.Write("r/a/one.trx", TrxFixture.Run(new TrxCase("Both", "Failed", "e")));
        dir.Write("r/b/two.trx", TrxFixture.Run(new TrxCase("Both")));

        Run(null, "--results", dir.Combine("r"), "--baseline", dir.Combine("base.json"), "--update-baseline");

        using var json = JsonDocument.Parse(File.ReadAllText(dir.Combine("base.json")));
        Assert.Equal("failed", json.RootElement.GetProperty("outcomes").GetProperty("Acme.Tests.Ui.TodosTests.Both").GetString());
    }

    [Fact]
    public void ARunWithNoResults_SavesNothing()
    {
        using var dir = new TempDir();
        dir.Write("r/a.trx", TrxFixture.Run());
        var outputFile = dir.Combine("gh-output");
        var env = new Dictionary<string, string> { ["GITHUB_OUTPUT"] = outputFile };

        Run(env, "--results", dir.Combine("r"), "--baseline", dir.Combine("base.json"), "--update-baseline");

        Assert.False(File.Exists(dir.Combine("base.json")));
        Assert.False(File.Exists(outputFile));
    }

    [Fact]
    public void InGitHubActions_AppendsToTheStepSummary_AndFlagsTheBaseline()
    {
        using var dir = new TempDir();
        dir.Write("r/a.trx", TrxFixture.Run(Passing));
        dir.Write("summary.md", "earlier\n");
        var env = new Dictionary<string, string>
        {
            ["GITHUB_STEP_SUMMARY"] = dir.Combine("summary.md"),
            ["GITHUB_OUTPUT"] = dir.Combine("gh-output"),
        };

        var (_, output, _) = Run(env, "--results", dir.Combine("r"), "--baseline", dir.Combine("base.json"), "--update-baseline");

        Assert.Equal(string.Empty, output);
        var summary = File.ReadAllText(dir.Combine("summary.md"));
        Assert.StartsWith("earlier", summary, StringComparison.Ordinal);
        Assert.Contains("### ✅ Test results", summary, StringComparison.Ordinal);
        Assert.Contains("baseline-updated=true", File.ReadAllText(dir.Combine("gh-output")), StringComparison.Ordinal);
    }

    [Fact]
    public void OutputOption_WinsOverTheStepSummary()
    {
        using var dir = new TempDir();
        dir.Write("r/a.trx", TrxFixture.Run(Passing));
        var env = new Dictionary<string, string> { ["GITHUB_STEP_SUMMARY"] = dir.Combine("summary.md") };

        Run(env, "--results", dir.Combine("r"), "--output", dir.Combine("out.md"));

        Assert.True(File.Exists(dir.Combine("out.md")));
        Assert.False(File.Exists(dir.Combine("summary.md")));
    }

    [Fact]
    public void AnOutputFileThatCannotBeWritten_IsSaid_AndStillExitsZero()
    {
        using var dir = new TempDir();
        dir.Write("r/a.trx", TrxFixture.Run(Passing));
        Directory.CreateDirectory(dir.Combine("taken"));

        var (code, _, err) = Run(null, "--results", dir.Combine("r"), "--output", dir.Combine("taken"));

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("Could not write", err, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonSummary_HasCountsAndFailureNames_NeverErrorText()
    {
        using var dir = new TempDir();
        dir.Write("r/a.trx", TrxFixture.Run(Passing, Failing, KnownFailing));

        Run(null, "--results", dir.Combine("r"), "--json", dir.Combine("sub", "summary.json"));

        var text = File.ReadAllText(dir.Combine("sub", "summary.json"));
        Assert.DoesNotContain("boom", text, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(text);
        Assert.Equal("failed", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("passed").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("failed").GetInt32());
        var failures = json.RootElement.GetProperty("failures").EnumerateArray().ToList();
        Assert.Equal("Acme.Tests.Ui.TodosTests.Fails", failures[0].GetProperty("name").GetString());
        Assert.Equal("BUG-7", failures[1].GetProperty("bugs")[0].GetString());
    }
}
