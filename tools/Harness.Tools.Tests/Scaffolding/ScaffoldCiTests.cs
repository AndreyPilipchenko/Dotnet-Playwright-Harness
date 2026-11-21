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

    private static string Nightly(TempDir dir) => File.ReadAllText(dir.Combine(".github/workflows/nightly.yml"));

    private static string Smoke(TempDir dir) => File.ReadAllText(dir.Combine(".github/workflows/smoke.yml"));

    [Fact]
    public void Default_RendersAChromiumOnlyNightlyMatrix()
    {
        using var dir = Project();

        var (code, _, err) = Scaffold(dir);

        Assert.True(code == ExitCodes.Ok, err);
        Assert.Contains("browser: [chromium]", Nightly(dir), StringComparison.Ordinal);
        Assert.Contains("BROWSER: chromium", Smoke(dir), StringComparison.Ordinal);
    }

    [Fact]
    public void Browsers_RenderTheMatrixInOrder()
    {
        using var dir = Project();

        var (code, _, err) = Run("--root", dir.Path, "--templates", Templates, "--browsers", "webkit,firefox");

        Assert.True(code == ExitCodes.Ok, err);
        Assert.Contains("browser: [webkit, firefox]", Nightly(dir), StringComparison.Ordinal);
    }

    [Fact]
    public void Browsers_AreTrimmedAndLowerCased()
    {
        using var dir = Project();

        var (code, _, err) = Run("--root", dir.Path, "--templates", Templates, "--browsers", " Firefox , WEBKIT ");

        Assert.True(code == ExitCodes.Ok, err);
        Assert.Contains("browser: [firefox, webkit]", Nightly(dir), StringComparison.Ordinal);
    }

    [Fact]
    public void Smoke_RunsOnTheFirstBrowser()
    {
        using var dir = Project();

        Run("--root", dir.Path, "--templates", Templates, "--browsers", "firefox,chromium");

        var smoke = Smoke(dir);
        Assert.Contains("BROWSER: firefox", smoke, StringComparison.Ordinal);
        Assert.Contains("browser: firefox", smoke, StringComparison.Ordinal);
        Assert.DoesNotContain("chromium", smoke, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("edge")]
    [InlineData("chromium,chromium")]
    [InlineData("Chromium, chromium")]
    [InlineData("")]
    [InlineData(",")]
    [InlineData("chromium,,firefox")]
    public void BadBrowsers_AreAUsageError_AndWriteNothing(string browsers)
    {
        using var dir = Project();

        var (code, _, err) = Run("--root", dir.Path, "--templates", Templates, "--browsers", browsers);

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Contains("chromium, firefox, webkit", err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(dir.Combine(".github")));
    }

    [Fact]
    public void Nightly_IsAFailSlowMatrix_WithOneArtifactPerBrowser()
    {
        using var dir = Project();
        Run("--root", dir.Path, "--templates", Templates, "--browsers", "chromium,firefox,webkit");

        var nightly = Nightly(dir);

        Assert.Contains("fail-fast: false", nightly, StringComparison.Ordinal);
        Assert.Contains("BROWSER: ${{ matrix.browser }}", nightly, StringComparison.Ordinal);
        Assert.Contains("browser: ${{ matrix.browser }}", nightly, StringComparison.Ordinal);
        Assert.Contains("label: regression-${{ matrix.browser }}", nightly, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_PassOneRunPerChosenBrowser_EvenOneWithoutResults()
    {
        // Review finding: looping over the downloaded folders dropped a browser whose job failed before uploading.
        // Looping over the chosen browsers keeps it, and the dashboard says "No results from: regression-<browser>".
        using var dir = Project();
        Run("--root", dir.Path, "--templates", Templates, "--browsers", "chromium,webkit");

        var nightly = Nightly(dir).ReplaceLineEndings("\n");

        Assert.Contains("pattern: results-regression-*", nightly, StringComparison.Ordinal);
        Assert.Contains("for browser in chromium webkit; do", nightly, StringComparison.Ordinal);
        Assert.Contains("runs+=(--run \"regression-$browser=artifacts/results-regression-$browser\")", nightly, StringComparison.Ordinal);
        Assert.DoesNotContain("artifacts/results-regression-*/", nightly, StringComparison.Ordinal);
        Assert.Contains("dashboard \"${runs[@]}\" --out site/index.html", nightly, StringComparison.Ordinal);
        Assert.Contains("tracker --results artifacts --out site/tracker.html", nightly, StringComparison.Ordinal);
        Assert.DoesNotContain("name: results-regression\n", nightly, StringComparison.Ordinal);
    }

    [Fact]
    public void Install_KeepsWithDeps()
    {
        using var dir = Project();
        Scaffold(dir);

        var setup = File.ReadAllText(dir.Combine(".github/actions/setup-harness/action.yml"));

        Assert.Contains("playwright.ps1 install --with-deps $env:PLAYWRIGHT_BROWSER", setup, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"(?m)^\s*(?:- )?uses:\s*(\S+)")]
    private static partial Regex UsesLine();
}
