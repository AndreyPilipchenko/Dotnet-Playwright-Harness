using System.Text;

namespace Harness.Tools.Tests.Reports;

/// <summary>
/// <c>package-report</c> reads the JSON of <c>dotnet package list</c>, whose exit code says nothing
/// about findings. A finding fails; a report that cannot prove anything is "not checked".
/// </summary>
public class PackageReportTests
{
    private const string Clean = """{ "version":1, "parameters":"--outdated", "sources":[], "projects":[{ "path":"C:/r/a.csproj" }] }""";

    private const string Outdated = """
        { "version":1, "parameters":"--outdated", "projects":[{ "path":"C:/r/a.csproj", "frameworks":[{ "framework":"net10.0",
          "topLevelPackages":[{ "id":"NUnit.Analyzers", "requestedVersion":"4.10.0", "resolvedVersion":"4.10.0", "latestVersion":"4.15.0" }] }] }] }
        """;

    private const string Vulnerable = """
        { "version":1, "parameters":"--vulnerable --include-transitive", "projects":[{ "path":"C:/r/a.csproj", "frameworks":[{ "framework":"net10.0",
          "topLevelPackages":[{ "id":"System.Text.Json", "requestedVersion":"8.0.4", "resolvedVersion":"8.0.4",
            "vulnerabilities":[{ "severity":"High", "advisoryurl":"https://github.com/advisories/GHSA-1" }] }],
          "transitivePackages":[{ "id":"Deep.Pkg", "resolvedVersion":"1.0.0",
            "vulnerabilities":[{ "severity":"Low", "advisoryurl":"https://github.com/advisories/GHSA-2" }] }] }] }] }
        """;

    private const string RestoreProblem = """
        { "version":1, "parameters":"--outdated", "problems":[{ "project":"C:/r/a.csproj", "level":"error", "text":"No assets file was found" }], "sources":[], "projects":[{ "path":"C:/r/a.csproj" }] }
        """;

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var io = new Console3();
        var code = Cli.Run(["package-report", .. args], io.In, io.Out, io.Err);
        return (code, io.Out.ToString(), io.Err.ToString());
    }

    private static (int Code, string Out, string Err) Report(TempDir dir, string json)
    {
        dir.Write("p.json", json);
        return Run("--input", dir.Combine("p.json"));
    }

    [Fact]
    public void NoInput_IsAUsageError()
    {
        Assert.Equal(ExitCodes.Usage, Run().Code);
    }

    [Fact]
    public void NoFindings_Passes()
    {
        using var dir = new TempDir();

        var (code, output, _) = Report(dir, Clean);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("No findings", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Outdated_Fails_WithCurrentAndLatest()
    {
        using var dir = new TempDir();

        var (code, output, _) = Report(dir, Outdated);

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains("| a.csproj | net10.0 | NUnit.Analyzers | direct | 4.10.0 | outdated: latest 4.15.0 |", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Vulnerable_Fails_WithSeverityAndAdvisory_ForDirectAndTransitive()
    {
        using var dir = new TempDir();

        var (code, output, _) = Report(dir, Vulnerable);

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains("| a.csproj | net10.0 | System.Text.Json | direct | 8.0.4 | vulnerable: High https://github.com/advisories/GHSA-1 |", output, StringComparison.Ordinal);
        Assert.Contains("| a.csproj | net10.0 | Deep.Pkg | transitive | 1.0.0 | vulnerable: Low https://github.com/advisories/GHSA-2 |", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SeveralInputs_AreAllRead()
    {
        using var dir = new TempDir();
        dir.Write("o.json", Outdated);
        dir.Write("v.json", Vulnerable);

        var (_, output, _) = Run("--input", dir.Combine("o.json"), "--input", dir.Combine("v.json"));

        Assert.Contains("NUnit.Analyzers", output, StringComparison.Ordinal);
        Assert.Contains("System.Text.Json", output, StringComparison.Ordinal);
        Assert.Contains("3 finding(s)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Utf16File_FromWindowsPowerShellRedirect_IsRead()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("p.json"), Outdated, Encoding.Unicode);

        Assert.Equal(ExitCodes.Failed, Run("--input", dir.Combine("p.json")).Code);
    }

    [Fact]
    public void MissingFile_IsNotChecked()
    {
        using var dir = new TempDir();

        Assert.Equal(ExitCodes.NotChecked, Run("--input", dir.Combine("nope.json")).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("error NU1903: Warning As Error")]
    [InlineData("[]")]
    [InlineData("""{ "version":1 }""")]
    [InlineData("""{ "version":1, "parameters":"--outdated", "projects":[] }""")]
    public void UnusableReport_IsNotChecked_NeverAPass(string content)
    {
        using var dir = new TempDir();

        Assert.Equal(ExitCodes.NotChecked, Report(dir, content).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--deprecated")]
    public void ReportThatIsNotAnOutdatedOrVulnerableList_IsNotChecked(string parameters)
    {
        using var dir = new TempDir();

        var (code, _, err) = Report(dir, Clean.Replace("--outdated", parameters, StringComparison.Ordinal));

        Assert.Equal(ExitCodes.NotChecked, code);
        Assert.Contains("--outdated", err, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreProblem_IsNotChecked_AndSaysWhy()
    {
        using var dir = new TempDir();

        var (code, _, err) = Report(dir, RestoreProblem);

        Assert.Equal(ExitCodes.NotChecked, code);
        Assert.Contains("No assets file was found", err, StringComparison.Ordinal);
    }

    [Fact]
    public void ProblemWithAWarningLevel_DoesNotHideACleanResult()
    {
        using var dir = new TempDir();

        var (code, _, err) = Report(dir, RestoreProblem.Replace("\"error\"", "\"warning\"", StringComparison.Ordinal));

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("No assets file was found", err, StringComparison.Ordinal);
    }

    [Fact]
    public void AFindingInOneReport_BeatsNotCheckedInAnother()
    {
        using var dir = new TempDir();
        dir.Write("o.json", Outdated);

        Assert.Equal(ExitCodes.Failed, Run("--input", dir.Combine("o.json"), "--input", dir.Combine("nope.json")).Code);
    }
}
