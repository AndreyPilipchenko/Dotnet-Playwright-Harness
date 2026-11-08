using Harness.Tools.Reports;

namespace Harness.Tools.Tests.Reports;

/// <summary>
/// <c>dashboard</c> writes one static HTML page from TRX. It holds names and counts only, never error
/// text, and has no scripts. Like ci-summary it reports and never judges: failures still exit 0.
/// </summary>
public class DashboardTests
{
    private const string Bug7 = "---\nid: BUG-7\ntitle: Order stays after delete\nstatus: open\nseverity: high\ncases: TC-001\n---\n";

    private static readonly TrxCase Passing = new("Passes");
    private static readonly TrxCase Failing = new("Fails", "Failed", "boom");
    private static readonly TrxCase KnownFailing = new("Known", "Failed", "known boom", Categories: ["bug:BUG-7", "tc:TC-001"]);

    private static (int Code, string Out, string Err) Run(Dictionary<string, string>? env, params string[] args)
    {
        var io = new Console3();
        var code = DashboardCommand.Run(args, io.Out, io.Err, name => env?.GetValueOrDefault(name));
        return (code, io.Out.ToString(), io.Err.ToString());
    }

    private static string Page(TempDir dir, params TrxCase[] tests) => PageWith(dir, null, tests);

    private static string PageWith(TempDir dir, Dictionary<string, string>? env, params TrxCase[] tests)
    {
        dir.Write("r/a.trx", TrxFixture.Run(tests));
        var (code, _, err) = Run(env, "--run", "ui=" + dir.Combine("r"), "--root", dir.Path, "--out", dir.Combine("o", "index.html"));
        Assert.True(code == ExitCodes.Ok, err);
        return File.ReadAllText(dir.Combine("o", "index.html"));
    }

    private static string Tile(int value, string label) => $"<div class=\"tile\"><b>{value}</b><span>{label}</span></div>";

    [Fact]
    public void NoRun_IsAUsageError()
    {
        Assert.Equal(ExitCodes.Usage, Run(null).Code);
    }

    [Theory]
    [InlineData("noequals")]
    [InlineData("=path")]
    [InlineData("name=")]
    public void MalformedRun_IsAUsageError(string run)
    {
        Assert.Equal(ExitCodes.Usage, Run(null, "--run", run).Code);
    }

    [Fact]
    public void IsRoutedFromTheCli()
    {
        var io = new Console3();

        Assert.Equal(ExitCodes.Usage, Cli.Run(["dashboard"], io.In, io.Out, io.Err));
    }

    [Fact]
    public void NothingToShow_IsNotChecked_AndWritesNoPage()
    {
        using var dir = new TempDir();

        var (code, _, err) = Run(null, "--run", "ui=" + dir.Combine("nope"), "--root", dir.Path, "--out", dir.Combine("o.html"));

        Assert.Equal(ExitCodes.NotChecked, code);
        Assert.Contains("ui", err, StringComparison.Ordinal);
        Assert.False(File.Exists(dir.Combine("o.html")));
    }

    [Fact]
    public void DefaultOutput_IsUnderTestResults_InTheRoot()
    {
        using var dir = new TempDir();
        dir.Write("r/a.trx", TrxFixture.Run(Passing));

        var (code, output, _) = Run(null, "--run", "ui=" + dir.Combine("r"), "--root", dir.Path);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.True(File.Exists(dir.Combine("TestResults", "dashboard", "index.html")));
        Assert.Contains("1 test(s)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_IsStatic_WithAPolicyThatForbidsScripts()
    {
        using var dir = new TempDir();

        var page = Page(dir, Passing);

        Assert.StartsWith("<!DOCTYPE html>", page, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'; style-src 'unsafe-inline'", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailuresStillExitZero_AndTheTilesCountEachOutcome()
    {
        using var dir = new TempDir();

        var page = Page(dir, Passing, Failing, new TrxCase("Skipped", "NotExecuted"));

        Assert.Contains(Tile(3, "tests"), page, StringComparison.Ordinal);
        Assert.Contains(Tile(1, "passed"), page, StringComparison.Ordinal);
        Assert.Contains(Tile(1, "failed"), page, StringComparison.Ordinal);
        Assert.Contains(Tile(1, "skipped"), page, StringComparison.Ordinal);
    }

    [Fact]
    public void ATestInSeveralJobs_CountsOnce_AsFailedIfItFailedInAny_WhileEachJobCountsItsOwn()
    {
        using var dir = new TempDir();
        dir.Write("a/a.trx", TrxFixture.Run(new TrxCase("Both")));
        dir.Write("b/b.trx", TrxFixture.Run(new TrxCase("Both", "Failed", "e")));

        Run(null, "--run", "chromium=" + dir.Combine("a"), "--run", "firefox=" + dir.Combine("b"), "--root", dir.Path, "--out", dir.Combine("o.html"));

        var page = File.ReadAllText(dir.Combine("o.html"));
        Assert.Contains(Tile(1, "tests"), page, StringComparison.Ordinal);
        Assert.Contains(Tile(0, "passed"), page, StringComparison.Ordinal);
        Assert.Contains(Tile(1, "failed"), page, StringComparison.Ordinal);
        Assert.Contains("<td>chromium</td><td class=\"num\">1</td><td class=\"num\">1</td><td class=\"num\">0</td><td class=\"num\">0</td>", page, StringComparison.Ordinal);
        Assert.Contains("<td>firefox</td><td class=\"num\">1</td><td class=\"num\">0</td><td class=\"num\">1</td><td class=\"num\">0</td>", page, StringComparison.Ordinal);
    }

    [Fact]
    public void AJobWithoutResults_IsNamed_AndTheRestIsShown()
    {
        using var dir = new TempDir();
        dir.Write("a/a.trx", TrxFixture.Run(Passing));
        dir.Write("b/b.trx", "not xml");

        var (code, _, _) = Run(null, "--run", "good=" + dir.Combine("a"), "--run", "broken=" + dir.Combine("b"), "--run", "gone=" + dir.Combine("c"), "--root", dir.Path, "--out", dir.Combine("o.html"));

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("No results from: broken, gone.", File.ReadAllText(dir.Combine("o.html")), StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorText_NeverReachesThePage()
    {
        using var dir = new TempDir();

        Assert.DoesNotContain("boom", Page(dir, Failing), StringComparison.Ordinal);
    }

    [Fact]
    public void TestNames_AreEscaped()
    {
        using var dir = new TempDir();

        var page = Page(dir, new TrxCase("F(\"<img>\")", "Failed", "e"));

        Assert.DoesNotContain("<img>", page, StringComparison.Ordinal);
        Assert.Contains("&lt;img&gt;", page, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailureLinkedToABugRecord_ShowsItsTitleAndCase()
    {
        using var dir = new TempDir();
        dir.Write("docs/qa/bug-reports/BUG-7.md", Bug7);

        var page = Page(dir, KnownFailing);

        Assert.Contains("BUG-7: Order stays after delete", page, StringComparison.Ordinal);
        Assert.Contains("TC-001", page, StringComparison.Ordinal);
        Assert.Contains(Tile(1, "linked to a bug report"), page, StringComparison.Ordinal);
        Assert.Contains(Tile(0, "with no recorded reason"), page, StringComparison.Ordinal);
    }

    [Fact]
    public void ABugIdWithNoRecord_IsStillShown()
    {
        using var dir = new TempDir();

        Assert.Contains("BUG-7", Page(dir, KnownFailing), StringComparison.Ordinal);
    }

    [Fact]
    public void AFailureWithNoBug_IsCalledOut()
    {
        using var dir = new TempDir();

        var page = Page(dir, Failing);

        Assert.Contains(Tile(1, "with no recorded reason"), page, StringComparison.Ordinal);
        Assert.Contains("<strong>No recorded reason</strong>", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Areas_ComeFromTheTestClass_UnderTheirLayer()
    {
        using var dir = new TempDir();

        var page = Page(
            dir,
            Passing,
            new TrxCase("Bad", "Failed", "e", ClassName: "Acme.Tests.Api.OrdersTests"));

        Assert.Contains(">UI<", page, StringComparison.Ordinal);
        Assert.Contains(">Todos<", page, StringComparison.Ordinal);
        Assert.Contains(">API<", page, StringComparison.Ordinal);
        Assert.Contains(">Orders<", page, StringComparison.Ordinal);
        Assert.Contains("0 passed, 1 failed", page, StringComparison.Ordinal);
        Assert.True(page.IndexOf(">API<", StringComparison.Ordinal) < page.IndexOf(">UI<", StringComparison.Ordinal), "The layer with failures comes first.");
    }

    [Fact]
    public void Bars_AreScaledToTheWidestArea_WithALabelForEachSegment()
    {
        using var dir = new TempDir();

        var page = Page(
            dir,
            new TrxCase("A"),
            new TrxCase("B"),
            new TrxCase("C", ClassName: "Acme.Tests.Ui.CartTests"));

        Assert.Contains("width:50%", page, StringComparison.Ordinal);
        Assert.Contains("title=\"Cart: 1 passed\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void ZeroTests_SaysSo()
    {
        using var dir = new TempDir();

        Assert.Contains("No tests ran.", Page(dir), StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutGitHubVariables_ItIsALocalRun()
    {
        using var dir = new TempDir();

        Assert.Contains("Local run", Page(dir, Passing), StringComparison.Ordinal);
    }

    [Fact]
    public void InGitHubActions_LinksTheRunAndCommit_Escaped()
    {
        using var dir = new TempDir();
        var env = new Dictionary<string, string>
        {
            ["GITHUB_SERVER_URL"] = "https://github.example",
            ["GITHUB_REPOSITORY"] = "acme/<shop>",
            ["GITHUB_RUN_ID"] = "77",
            ["GITHUB_SHA"] = "abcdef1234567",
        };

        var page = PageWith(dir, env, Passing);

        Assert.Contains("https://github.example/acme/&lt;shop&gt;/actions/runs/77", page, StringComparison.Ordinal);
        Assert.Contains(">abcdef1<", page, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOutputThatCannotBeWritten_Fails()
    {
        using var dir = new TempDir();
        dir.Write("r/a.trx", TrxFixture.Run(Passing));
        Directory.CreateDirectory(dir.Combine("taken"));

        var (code, _, err) = Run(null, "--run", "ui=" + dir.Combine("r"), "--root", dir.Path, "--out", dir.Combine("taken"));

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains("Could not write", err, StringComparison.Ordinal);
    }
}
