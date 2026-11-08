using System.Text.Json;

namespace Harness.Tools.Tests.Reports;

/// <summary>
/// <c>tracker</c> writes an HTML plan report derived from the QA records each time, optionally with
/// the latest results and a history of snapshots. It reports problems in the records but never fails on them.
/// </summary>
public class TrackerTests
{
    private const string Class = "Acme.Tests.Ui.OrdersTests";

    private static string Req(string id, string status = "accepted") =>
        $"---\nid: {id}\ntitle: Requirement {id}\nsource: docs/stories/orders.md\nstatus: {status}\n---\n";

    private static string Tc(string id, string automation = "automated", string priority = "high", string requirements = "REQ-001") =>
        $"---\nid: {id}\ntitle: Case {id}\nrequirements: {requirements}\npriority: {priority}\nautomation: {automation}\n---\n";

    private static string Bug(string id, string status = "open") =>
        $"---\nid: {id}\ntitle: Bug {id}\nstatus: {status}\nseverity: high\ncases: TC-001\nissue: 42\n---\n";

    private static string TestSource(params string[] categories) =>
        "public class OrdersTests\n{\n    [Test]\n    "
        + string.Join(" ", categories.Select(c => $"[Category(\"{c}\")]"))
        + "\n    public Task A() => Task.CompletedTask;\n}\n";

    private static TempDir Project()
    {
        var dir = new TempDir();
        dir.Write("docs/qa/requirements/REQ-001.md", Req("REQ-001"));
        dir.Write("docs/qa/test-cases/TC-001.md", Tc("TC-001"));
        dir.Write("tests/Acme.Tests/Ui/OrdersTests.cs", TestSource("tc:TC-001"));
        return dir;
    }

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var io = new Console3();
        var code = Cli.Run(["tracker", .. args], io.In, io.Out, io.Err);
        return (code, io.Out.ToString(), io.Err.ToString());
    }

    private static string Page(TempDir dir, params string[] extra)
    {
        var (code, _, err) = Run(["--root", dir.Path, "--out", dir.Combine("o.html"), .. extra]);
        Assert.True(code == ExitCodes.Ok, err);
        return File.ReadAllText(dir.Combine("o.html"));
    }

    private static string Tile(string value, string label) => $"<div class=\"tile\"><b>{value}</b><span>{label}</span></div>";

    private static string Results(TempDir dir, params TrxCase[] tests)
    {
        dir.Write("r/a.trx", TrxFixture.Run(tests));
        return dir.Combine("r");
    }

    [Fact]
    public void UnknownOption_IsAUsageError()
    {
        Assert.Equal(ExitCodes.Usage, Run("--nope", "x").Code);
    }

    [Fact]
    public void RecordWithoutHistory_IsAUsageError()
    {
        Assert.Equal(ExitCodes.Usage, Run("--record").Code);
    }

    [Fact]
    public void NoRecords_IsNotChecked_AndWritesNoPage()
    {
        using var dir = new TempDir();

        var (code, _, err) = Run("--root", dir.Path, "--out", dir.Combine("o.html"));

        Assert.Equal(ExitCodes.NotChecked, code);
        Assert.Contains("No QA records", err, StringComparison.Ordinal);
        Assert.False(File.Exists(dir.Combine("o.html")));
    }

    [Fact]
    public void DefaultOutput_IsUnderTestResults_InTheRoot()
    {
        using var dir = Project();

        var (code, output, _) = Run("--root", dir.Path);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.True(File.Exists(dir.Combine("TestResults", "tracker", "index.html")));
        Assert.Contains("index.html", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_IsStatic()
    {
        using var dir = Project();

        var page = Page(dir);

        Assert.Contains("default-src 'none'; style-src 'unsafe-inline'", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tiles_CountCoverageAutomationAndOpenBugs()
    {
        using var dir = Project();
        dir.Write("docs/qa/requirements/REQ-002.md", Req("REQ-002"));
        dir.Write("docs/qa/requirements/REQ-003.md", Req("REQ-003", "proposed"));
        dir.Write("docs/qa/test-cases/TC-002.md", Tc("TC-002", "planned"));
        dir.Write("docs/qa/test-cases/TC-003.md", Tc("TC-003", "manual-only"));
        dir.Write("docs/qa/bug-reports/BUG-001.md", Bug("BUG-001"));
        dir.Write("docs/qa/bug-reports/BUG-002.md", Bug("BUG-002", "fixed"));

        var page = Page(dir);

        Assert.Contains(Tile("1 of 2", "accepted requirements with a test case"), page, StringComparison.Ordinal);
        Assert.Contains(Tile("1 of 2", "automatable cases automated"), page, StringComparison.Ordinal);
        Assert.Contains(Tile("1", "open bugs"), page, StringComparison.Ordinal);
    }

    [Fact]
    public void Gaps_NameUncoveredRequirementsAndHighPriorityCases_ButNotManualOnes()
    {
        using var dir = Project();
        dir.Write("docs/qa/requirements/REQ-002.md", Req("REQ-002"));
        dir.Write("docs/qa/test-cases/TC-002.md", Tc("TC-002", "planned"));
        dir.Write("docs/qa/test-cases/TC-003.md", Tc("TC-003", "planned", "low"));
        dir.Write("docs/qa/test-cases/TC-004.md", Tc("TC-004", "manual-only"));

        var page = Page(dir);
        var gaps = page[page.IndexOf("<h2>Gaps</h2>", StringComparison.Ordinal)..page.IndexOf("<h2>Requirements</h2>", StringComparison.Ordinal)];

        Assert.Contains("REQ-002", gaps, StringComparison.Ordinal);
        Assert.Contains("TC-002", gaps, StringComparison.Ordinal);
        Assert.DoesNotContain("TC-003", gaps, StringComparison.Ordinal);
        Assert.DoesNotContain("TC-004", gaps, StringComparison.Ordinal);
    }

    [Fact]
    public void NoGaps_SaysSo()
    {
        using var dir = Project();

        Assert.Contains("No gaps.", Page(dir), StringComparison.Ordinal);
    }

    [Fact]
    public void BrokenLinks_AreShownFirst_AndStillExitZero()
    {
        using var dir = Project();
        dir.Write("docs/qa/test-cases/TC-002.md", Tc("TC-002", requirements: "REQ-404"));

        var page = Page(dir);

        Assert.Contains("REQ-404 does not exist.", page, StringComparison.Ordinal);
        Assert.Contains("can't be trusted", page, StringComparison.Ordinal);
        Assert.True(page.IndexOf("can't be trusted", StringComparison.Ordinal) < page.IndexOf("class=\"tiles\"", StringComparison.Ordinal));
    }

    [Fact]
    public void RecordText_IsEscaped()
    {
        using var dir = Project();
        dir.Write("docs/qa/requirements/REQ-002.md", Req("REQ-002").Replace("Requirement REQ-002", "<img src=x>", StringComparison.Ordinal));

        var page = Page(dir);

        Assert.DoesNotContain("<img", page, StringComparison.Ordinal);
        Assert.Contains("&lt;img src=x&gt;", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Bugs_ListOnlyOpenOnes_WithSeverityAndIssue()
    {
        using var dir = Project();
        dir.Write("docs/qa/bug-reports/BUG-001.md", Bug("BUG-001"));
        dir.Write("docs/qa/bug-reports/BUG-002.md", Bug("BUG-002", "fixed"));

        var page = Page(dir);

        Assert.Contains("<td>BUG-001</td><td>Bug BUG-001</td><td>high</td><td>TC-001</td><td>42</td>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Bug BUG-002", page, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutResults_ThereIsNoResultColumn()
    {
        using var dir = Project();

        var page = Page(dir);

        Assert.DoesNotContain("<th>Last result</th>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("automated cases passing", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Passed", "passed")]
    [InlineData("Failed", "failed")]
    [InlineData("NotExecuted", "skipped")]
    public void Results_AreJoinedToCasesByCategory(string outcome, string shown)
    {
        using var dir = Project();
        var results = Results(dir, new TrxCase("A", outcome, Categories: ["tc:TC-001"], ClassName: Class));

        var page = Page(dir, "--results", results);

        Assert.Contains($"<td>{shown}</td>", page, StringComparison.Ordinal);
    }

    [Fact]
    public void ACaseWithSeveralTests_FailsIfAnyFailed()
    {
        using var dir = Project();
        var results = Results(
            dir,
            new TrxCase("A", Categories: ["tc:TC-001"], ClassName: Class),
            new TrxCase("B", "Failed", "e", Categories: ["tc:TC-001"], ClassName: Class));

        var page = Page(dir, "--results", results);

        Assert.Contains("<td>failed</td>", page, StringComparison.Ordinal);
        Assert.Contains(Tile("0 of 1", "automated cases passing"), page, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAutomatedCaseThatDidNotRun_IsSaid_AndIsNotPassing()
    {
        using var dir = Project();
        var results = Results(dir, new TrxCase("Other", ClassName: Class));

        var page = Page(dir, "--results", results);

        Assert.Contains("<td>not run</td>", page, StringComparison.Ordinal);
        Assert.Contains(Tile("0 of 1", "automated cases passing"), page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("bad")]
    public void ResultsThatCannotBeRead_AreSaid_AndThePageIsStillWritten(string folder)
    {
        using var dir = Project();
        dir.Write("bad/a.trx", "not xml");

        var (code, _, err) = Run("--root", dir.Path, "--out", dir.Combine("o.html"), "--results", dir.Combine(folder));

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("No readable TRX", err, StringComparison.Ordinal);
        Assert.Contains("No readable TRX", File.ReadAllText(dir.Combine("o.html")), StringComparison.Ordinal);
    }

    [Fact]
    public void Record_AppendsASnapshot_WithCountsOnly()
    {
        using var dir = Project();
        var history = dir.Combine("docs", "qa", "history.jsonl");
        var results = Results(dir, new TrxCase("A", Categories: ["tc:TC-001"], ClassName: Class));

        var page = Page(dir, "--results", results, "--history", history, "--record");

        var line = Assert.Single(File.ReadAllLines(history));
        using var json = JsonDocument.Parse(line);
        var at = json.RootElement.GetProperty("at").GetString()!;
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", at);
        Assert.Equal(1, json.RootElement.GetProperty("accepted").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("covered").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("automated").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("passing").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("failing").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("openBugs").GetInt32());
        Assert.Contains($"<td>{at}</td>", page, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutResults_ASnapshotLeavesTheResultCountsEmpty()
    {
        using var dir = Project();
        var history = dir.Combine("h.jsonl");

        Page(dir, "--history", history, "--record");

        using var json = JsonDocument.Parse(File.ReadAllText(history));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("passing").ValueKind);
    }

    [Fact]
    public void HistoryWithoutRecord_IsRead_NeverWritten_NewestFirst_AndTheLastLineOfADayWins()
    {
        using var dir = Project();
        var lines = new[]
        {
            """{"at":"2026-01-01","accepted":1,"covered":0,"automated":0,"passing":null,"failing":null,"openBugs":3}""",
            """{"at":"2026-01-02","accepted":2,"covered":1,"automated":1,"passing":1,"failing":0,"openBugs":2}""",
            """{"at":"2026-01-01","accepted":1,"covered":1,"automated":0,"passing":null,"failing":null,"openBugs":9}""",
        };
        dir.Write("h.jsonl", string.Join('\n', lines));

        var page = Page(dir, "--history", dir.Combine("h.jsonl"));

        Assert.Equal(string.Join('\n', lines), File.ReadAllText(dir.Combine("h.jsonl")));
        Assert.Contains("<td>2026-01-01</td><td class=\"num\">1 of 1</td><td class=\"num\">0</td><td class=\"num\">-</td><td class=\"num\">-</td><td class=\"num\">9</td>", page, StringComparison.Ordinal);
        Assert.True(page.IndexOf("2026-01-02", StringComparison.Ordinal) < page.IndexOf("2026-01-01", StringComparison.Ordinal));
    }

    [Fact]
    public void BrokenHistoryLines_AreSkippedAndCounted()
    {
        using var dir = Project();
        dir.Write("h.jsonl", "nope\n[1]\n{\"at\":\"not a date\"}\n" + """{"at":"2026-01-02","accepted":2,"covered":1,"automated":1,"openBugs":2}""");

        var (code, _, err) = Run("--root", dir.Path, "--out", dir.Combine("o.html"), "--history", dir.Combine("h.jsonl"));

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("Skipped 3 unreadable history line(s)", err, StringComparison.Ordinal);
        Assert.Contains("<td>2026-01-02</td>", File.ReadAllText(dir.Combine("o.html")), StringComparison.Ordinal);
    }

    [Fact]
    public void AnOutputThatCannotBeWritten_Fails()
    {
        using var dir = Project();
        Directory.CreateDirectory(dir.Combine("taken"));

        var (code, _, err) = Run("--root", dir.Path, "--out", dir.Combine("taken"));

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains("Could not write", err, StringComparison.Ordinal);
    }
}
