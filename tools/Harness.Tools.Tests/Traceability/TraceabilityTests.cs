using System.Text.Json.Nodes;
using Harness.Tools.Traceability;

namespace Harness.Tools.Tests.Traceability;

public class TraceabilityTests
{
    private const string TestFile = "tests/Acme.Tests/Ui/OrdersTests.cs";

    private static string Req(string id, string status = "accepted") =>
        $"---\nid: {id}\ntitle: Orders can be deleted\nsource: docs/stories/orders.md\nstatus: {status}\n---\n\n## Statement\n\n> A customer can delete an order.\n";

    private static string Tc(string id, string requirements = "REQ-001", string automation = "automated", string priority = "high") =>
        $"---\nid: {id}\ntitle: Deleted order disappears\nrequirements: {requirements}\npriority: {priority}\nautomation: {automation}\n---\n\n## Steps\n";

    private static string Bug(string id, string status = "open", string cases = "TC-001") =>
        $"---\nid: {id}\ntitle: Order stays after delete\nstatus: {status}\nseverity: high\ncases: {cases}\n---\n";

    private static string Tests(params string[] categories) =>
        "public class OrdersTests\n{\n    [Test]\n    "
        + string.Join(" ", categories.Select(c => $"[Category(\"{c}\")]"))
        + "\n    public Task RemovesADeletedOrderAsync() => Task.CompletedTask;\n}\n";

    private static TempDir Project(
        string? req = null, string? tc = null, string? bug = null, string? tests = null)
    {
        var dir = new TempDir();
        dir.Write("docs/qa/requirements/REQ-001.md", req ?? Req("REQ-001"));
        dir.Write("docs/qa/test-cases/TC-001.md", tc ?? Tc("TC-001"));
        if (bug is not null)
        {
            dir.Write("docs/qa/bug-reports/BUG-001.md", bug);
        }

        dir.Write(TestFile, tests ?? Tests("smoke", "tc:TC-001"));
        return dir;
    }

    private static IReadOnlyList<Harness.Tools.Conventions.Finding> Check(TempDir dir) => TraceabilityChecker.Run(dir.Path).Findings;

    [Fact]
    public void ACompleteChain_IsClean()
    {
        using var dir = Project(bug: Bug("BUG-001"), tests: Tests("regression", "tc:TC-001", "bug:BUG-001"));

        Assert.Empty(Check(dir));
    }

    [Theory]
    [InlineData("---\ntitle: no id\n---\n")]
    [InlineData("no front matter at all\n")]
    [InlineData("---\nid: REQ-001\ntitle: x\nsource: y\nstatus: maybe\n---\n")]
    [InlineData("---\nid: REQ-002\ntitle: x\nsource: y\nstatus: accepted\n---\n")]
    public void MalformedRecord_IsReported(string text)
    {
        using var dir = Project(req: text);

        Assert.Contains(Check(dir), f => f.Rule == "record-format" && f.File == "docs/qa/requirements/REQ-001.md");
    }

    [Fact]
    public void DuplicateId_IsReported()
    {
        using var dir = Project();
        dir.Write("docs/qa/test-cases/TC-001-copy.md", Tc("TC-001"));

        Assert.Contains(Check(dir), f => f.Rule == "record-format" && f.File == "docs/qa/test-cases/TC-001-copy.md");
    }

    [Theory]
    [InlineData("REQ-009")]
    [InlineData("")]
    public void CaseWithAMissingRequirement_IsReported(string requirements)
    {
        using var dir = Project(tc: Tc("TC-001", requirements: requirements));

        Assert.Contains(Check(dir), f => f.Rule == "case-unknown-requirement" && f.File == "docs/qa/test-cases/TC-001.md");
    }

    [Fact]
    public void AutomatedCaseWithoutATest_IsReported()
    {
        using var dir = Project(tests: Tests("smoke"));

        var finding = Assert.Single(Check(dir), f => f.Rule == "case-automated-without-test");
        Assert.Equal(Harness.Tools.Conventions.Severity.Fail, finding.Severity);
    }

    [Fact]
    public void TestLinkedToAPlannedCase_IsAWarning()
    {
        using var dir = Project(tc: Tc("TC-001", automation: "planned"));

        var finding = Assert.Single(Check(dir), f => f.Rule == "case-not-marked-automated");
        Assert.Equal(Harness.Tools.Conventions.Severity.Warn, finding.Severity);
    }

    [Fact]
    public void TestLinkedToAnUnknownCase_IsReportedOnTheAttributeLine()
    {
        using var dir = Project(tests: Tests("smoke", "tc:TC-001", "tc:TC-404"));

        var finding = Assert.Single(Check(dir), f => f.Rule == "test-unknown-case");
        Assert.Equal(TestFile, finding.File);
        Assert.Equal(4, finding.Line);
    }

    [Fact]
    public void TestLinkedToAnUnknownBug_IsReported()
    {
        using var dir = Project(tests: Tests("smoke", "tc:TC-001", "bug:BUG-404"));

        Assert.Contains(Check(dir), f => f.Rule == "test-unknown-bug");
    }

    [Fact]
    public void BugLinkedToAnUnknownCase_IsReported()
    {
        using var dir = Project(bug: Bug("BUG-001", cases: "TC-404"));

        Assert.Contains(Check(dir), f => f.Rule == "bug-unknown-case" && f.File == "docs/qa/bug-reports/BUG-001.md");
    }

    [Theory]
    [InlineData("fixed")]
    [InlineData("wont-fix")]
    public void ClosedBugStillOnATest_IsAWarning(string status)
    {
        using var dir = Project(bug: Bug("BUG-001", status: status), tests: Tests("regression", "tc:TC-001", "bug:BUG-001"));

        var finding = Assert.Single(Check(dir), f => f.Rule == "bug-closed-still-linked");
        Assert.Equal(Harness.Tools.Conventions.Severity.Warn, finding.Severity);
    }

    [Fact]
    public void AcceptedRequirementWithoutCases_IsAWarning_ButAProposedOneIsNot()
    {
        using var dir = Project();
        dir.Write("docs/qa/requirements/REQ-002.md", Req("REQ-002"));
        dir.Write("docs/qa/requirements/REQ-003.md", Req("REQ-003", status: "proposed"));

        var finding = Assert.Single(Check(dir), f => f.Rule == "requirement-without-cases");
        Assert.Equal("docs/qa/requirements/REQ-002.md", finding.File);
    }

    [Fact]
    public void ManualOnlyCase_NeedsNoTest()
    {
        using var dir = Project(tc: Tc("TC-001", automation: "manual-only"), tests: Tests("smoke"));

        Assert.Empty(Check(dir));
    }

    [Fact]
    public void TestsInBuildOutput_AreIgnored()
    {
        using var dir = Project();
        dir.Write("tests/Acme.Tests/obj/Debug/Generated.cs", Tests("tc:TC-404"));

        Assert.Empty(Check(dir));
    }

    [Fact]
    public void NoQaRecords_CannotBeChecked()
    {
        using var dir = new TempDir();
        dir.Write(TestFile, Tests("smoke"));

        Assert.Throws<Harness.Tools.Conventions.ConventionException>(() => TraceabilityChecker.Run(dir.Path));
    }

    [Fact]
    public void Json_DescribesTheWholeChain()
    {
        using var dir = Project(bug: Bug("BUG-001"), tests: Tests("regression", "tc:TC-001", "bug:BUG-001"));
        var io = new Console3();

        var code = Cli.Run(["traceability", "--root", dir.Path, "--json"], io.In, io.Out, io.Err);

        Assert.Equal(ExitCodes.Ok, code);
        var json = JsonNode.Parse(io.Out.ToString())!;
        Assert.Equal("REQ-001", json["requirements"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("TC-001", json["requirements"]![0]!["cases"]![0]!.GetValue<string>());
        var test = json["cases"]![0]!["tests"]![0]!;
        Assert.Equal("RemovesADeletedOrderAsync", test["method"]!.GetValue<string>());
        Assert.Equal(TestFile, test["file"]!.GetValue<string>());
        Assert.Equal("open", json["bugs"]![0]!["status"]!.GetValue<string>());
        Assert.Equal("RemovesADeletedOrderAsync", json["bugs"]![0]!["tests"]![0]!["method"]!.GetValue<string>());
        Assert.Empty(json["fail"]!.AsArray());
    }

    [Fact]
    public void Cli_ExitsOne_OnAFailure_AndPrintsTheFinding()
    {
        using var dir = Project(tests: Tests("smoke"));
        var io = new Console3();

        var code = Cli.Run(["traceability", "--root", dir.Path], io.In, io.Out, io.Err);

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains("[case-automated-without-test]", io.Out.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_WarningsPass_UnlessFailOnWarn()
    {
        using var dir = Project(tc: Tc("TC-001", automation: "planned"));
        var io = new Console3();

        Assert.Equal(ExitCodes.Ok, Cli.Run(["traceability", "--root", dir.Path], io.In, io.Out, io.Err));
        Assert.Equal(ExitCodes.Failed, Cli.Run(["traceability", "--root", dir.Path, "--fail-on-warn"], io.In, io.Out, io.Err));
    }

    [Fact]
    public void Cli_NothingToCheck_ExitsTwo()
    {
        using var dir = new TempDir();
        var io = new Console3();

        Assert.Equal(ExitCodes.NotChecked, Cli.Run(["traceability", "--root", dir.Path], io.In, io.Out, io.Err));
    }
}
