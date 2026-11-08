using System.Text.RegularExpressions;

namespace Harness.Tools.Tests.Scaffolding;

/// <summary>
/// <c>scaffold-ci</c> renders the GitHub Actions templates into a project, only when asked, because
/// a workflow starts running and using minutes as soon as it is pushed.
/// </summary>
public partial class ScaffoldCiTests
{
    private static readonly string[] Files =
    [
        ".github/actions/run-tests/action.yml",
        ".github/actions/setup-harness/action.yml",
        ".github/workflows/nightly.yml",
        ".github/workflows/smoke.yml",
        ".github/workflows/static-checks.yml",
    ];

    private static string Templates => Path.Combine(RepoPaths.Root, "templates");

    private static TempDir Project(string framework = "nunit")
    {
        var dir = new TempDir();
        dir.Write(".harness/project.json", $$"""{ "framework": "{{framework}}", "name": "Acme.Shop", "targetFramework": "net10.0" }""");
        return dir;
    }

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var io = new Console3();
        var code = Cli.Run(["scaffold-ci", .. args], io.In, io.Out, io.Err);
        return (code, io.Out.ToString(), io.Err.ToString());
    }

    private static (int Code, string Out, string Err) Scaffold(TempDir dir) => Run("--root", dir.Path, "--templates", Templates);

    private static string AllText(TempDir dir) => string.Concat(Files.Select(f => File.ReadAllText(dir.Combine(f))));

    [Fact]
    public void UnknownOption_IsAUsageError()
    {
        Assert.Equal(ExitCodes.Usage, Run("--nope", "x").Code);
    }

    [Fact]
    public void WithoutAnInstalledHarness_ItSaysSo_AndWritesNothing()
    {
        using var dir = new TempDir();

        var (code, _, err) = Scaffold(dir);

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains("setup", err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(dir.Combine(".github")));
    }

    [Theory]
    [InlineData("{ nope")]
    [InlineData("""{ "name": "Acme.Shop" }""")]
    public void ABrokenProjectFile_IsSaid(string content)
    {
        using var dir = new TempDir();
        dir.Write(".harness/project.json", content);

        Assert.Equal(ExitCodes.Failed, Scaffold(dir).Code);
    }

    [Theory]
    [InlineData("nunit")]
    [InlineData("xunit")]
    [InlineData("mstest")]
    public void WritesEveryFile_NamingThem(string framework)
    {
        using var dir = Project(framework);

        var (code, output, err) = Scaffold(dir);

        Assert.True(code == ExitCodes.Ok, err);
        Assert.All(Files, f =>
        {
            Assert.True(File.Exists(dir.Combine(f)), f);
            Assert.Contains(f, output, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("nunit", "filter-option: --filter", "filter-value: TestCategory=smoke")]
    [InlineData("mstest", "filter-option: --filter", "filter-value: TestCategory=smoke")]
    [InlineData("xunit", "filter-option: --filter-trait", "filter-value: Category=smoke")]
    public void TheSmokeFilter_FitsTheFramework(string framework, string option, string value)
    {
        using var dir = Project(framework);
        Scaffold(dir);

        var smoke = File.ReadAllText(dir.Combine(".github/workflows/smoke.yml"));

        Assert.Contains(option + "\n", smoke.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains(value, smoke, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectValues_AreFilledIn_AndGitHubExpressionsAreKept()
    {
        using var dir = Project();
        Scaffold(dir);

        var text = AllText(dir);

        Assert.Contains("tests/Acme.Shop.Tests/Acme.Shop.Tests.csproj", text, StringComparison.Ordinal);
        Assert.Contains("tests/Acme.Shop.Tests/bin/Release/net10.0/playwright.ps1", text, StringComparison.Ordinal);
        Assert.Contains("${{ inputs.label }}", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?<!\$)\{\{", text);
    }

    [Fact]
    public void ExistingFiles_AreNeverOverwritten()
    {
        using var dir = Project();
        dir.Write(".github/workflows/smoke.yml", "mine");

        var (code, _, err) = Scaffold(dir);

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains("smoke.yml", err, StringComparison.Ordinal);
        Assert.Equal("mine", File.ReadAllText(dir.Combine(".github/workflows/smoke.yml")));
        Assert.False(File.Exists(dir.Combine(".github/workflows/nightly.yml")));
    }

    [Fact]
    public void Actions_ArePinned_AndComeOnlyFromGitHub()
    {
        using var dir = Project();
        Scaffold(dir);

        var uses = UsesLine().Matches(AllText(dir)).Select(m => m.Groups[1].Value).ToList();

        Assert.NotEmpty(uses);
        Assert.All(uses, u => Assert.Matches(@"^(\./\.github/actions/[a-z-]+|actions/[a-z/-]+@v\d+)$", u));
    }

    [Fact]
    public void EveryWorkflow_HasLeastPrivilege_AndATimeout()
    {
        using var dir = Project();
        Scaffold(dir);

        Assert.All(Files.Where(f => f.Contains("workflows", StringComparison.Ordinal)), f =>
        {
            var text = File.ReadAllText(dir.Combine(f));
            Assert.Contains("permissions:", text, StringComparison.Ordinal);
            Assert.Contains("contents: read", text, StringComparison.Ordinal);
            Assert.Contains("timeout-minutes:", text, StringComparison.Ordinal);
            Assert.Contains("runs-on: ubuntu-latest", text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Inputs_ReachShellsThroughTheEnvironment_NeverInline()
    {
        using var dir = Project();
        Scaffold(dir);

        foreach (var file in Files)
        {
            var inRun = false;
            var indent = 0;
            foreach (var line in File.ReadAllLines(dir.Combine(file)))
            {
                var trimmed = line.TrimStart();
                var current = line.Length - trimmed.Length;
                if (inRun && trimmed.Length > 0 && current <= indent)
                {
                    inRun = false;
                }

                if (inRun || trimmed.StartsWith("run:", StringComparison.Ordinal) || trimmed.StartsWith("- run:", StringComparison.Ordinal))
                {
                    Assert.False(line.Contains("${{", StringComparison.Ordinal), $"{file}: {line}");
                }

                if (trimmed.StartsWith("run: |", StringComparison.Ordinal))
                {
                    inRun = true;
                    indent = current;
                }
            }
        }
    }

    [Fact]
    public void TheTestRun_ChecksForSecretsBeforeUploading_AndStillReportsTheExitCode()
    {
        using var dir = Project();
        Scaffold(dir);

        var action = File.ReadAllText(dir.Combine(".github/actions/run-tests/action.yml"));
        var scan = action.IndexOf("secret-scan", StringComparison.Ordinal);
        var upload = action.IndexOf("actions/upload-artifact", StringComparison.Ordinal);
        var verdict = action.LastIndexOf("exit \"$TEST_EXIT\"", StringComparison.Ordinal);

        Assert.True(scan > 0 && scan < upload && upload < verdict, action);
        Assert.Contains("--report-trx", action, StringComparison.Ordinal);
        Assert.Contains("TRACE_DIR", action, StringComparison.Ordinal);
        Assert.Contains("ci-summary", action, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"(?m)^\s*(?:- )?uses:\s*(\S+)")]
    private static partial Regex UsesLine();
}
