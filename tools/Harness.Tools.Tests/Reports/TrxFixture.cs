using System.Globalization;
using System.Security;
using System.Text;

namespace Harness.Tools.Tests.Reports;

/// <summary>One test in a fixture. <see cref="TestName"/> overrides the short name, as xUnit v3 does.</summary>
internal sealed record TrxCase(
    string Method,
    string Outcome = "Passed",
    string? Message = null,
    string? TestName = null,
    string[]? Categories = null,
    string ClassName = "Acme.Tests.Ui.TodosTests");

/// <summary>Writes TRX in the shape recorded from real NUnit, xUnit v3 and MSTest v4 runs (docs/framework-matrix.md).</summary>
internal static class TrxFixture
{
    public static string Run(params TrxCase[] tests) => Build("Completed", null, tests);

    public static string CrashedRun(string runInfo, params TrxCase[] tests) => Build("Failed", runInfo, tests);

    private static string Build(string outcome, string? runInfo, TrxCase[] tests)
    {
        var results = new StringBuilder();
        var definitions = new StringBuilder();
        for (var i = 0; i < tests.Length; i++)
        {
            var test = tests[i];
            var id = Guid.NewGuid().ToString();
            var name = SecurityElement.Escape(test.TestName ?? test.Method);
            var output = test.Message is null
                ? string.Empty
                : $"<Output><ErrorInfo><Message>{SecurityElement.Escape(test.Message)}</Message><StackTrace>   at Secret.Stack.Frame()</StackTrace></ErrorInfo></Output>";
            results.Append(CultureInfo.InvariantCulture, $"""<UnitTestResult executionId="{Guid.NewGuid()}" testId="{id}" testName="{name}" duration="00:00:01.5000000" outcome="{test.Outcome}">{output}</UnitTestResult>""");

            var categories = test.Categories is null
                ? string.Empty
                : "<TestCategory>" + string.Concat(test.Categories.Select(c => $"""<TestCategoryItem TestCategory="{c}" />""")) + "</TestCategory>";
            definitions.Append(CultureInfo.InvariantCulture, $"""<UnitTest name="{name}" storage="a.dll" id="{id}">{categories}<TestMethod className="{test.ClassName}" name="{SecurityElement.Escape(test.Method)}" /></UnitTest>""");
        }

        var infos = runInfo is null
            ? string.Empty
            : $"""<RunInfos><RunInfo outcome="Error"><Text>{SecurityElement.Escape(runInfo)}</Text></RunInfo></RunInfos>""";

        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun id="{Guid.NewGuid()}" name="run" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Times creation="2026-11-08T10:00:00.000Z" queuing="2026-11-08T10:00:00.000Z" start="2026-11-08T10:00:00.000Z" finish="2026-11-08T10:01:05.000Z" />
              <Results>{results}</Results>
              <TestDefinitions>{definitions}</TestDefinitions>
              <ResultSummary outcome="{outcome}"><Counters total="{tests.Length}" />{infos}</ResultSummary>
            </TestRun>
            """;
    }
}
