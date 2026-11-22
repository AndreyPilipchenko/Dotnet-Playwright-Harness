using Harness.Tools.Scaffolding;

namespace Harness.Tools.Tests;

public class ScaffolderTests
{
    private const string Name = "Acme.Shop";

    private static readonly string[] CommonFiles =
    [
        "global.json",
        "Directory.Build.props",
        "Directory.Packages.props",
        ".editorconfig",
        ".gitignore",
        "Acme.Shop.slnx",
        "src/Acme.Shop.Framework/Acme.Shop.Framework.csproj",
        "src/Acme.Shop.Framework/Pages/GreetingPage.cs",
        "src/Acme.Shop.Framework/Utils/TestData.cs",
        "src/Acme.Shop.Framework/Utils/SignedInSessions.cs",
        "src/Acme.Shop.Framework/Utils/Secrets.cs",
        "src/Acme.Shop.Framework/Utils/Redaction.cs",
        "src/Acme.Shop.Framework/Utils/BrowserDiagnostics.cs",
        "resources/testData/GreetingTestData.json",
        "tests/Acme.Shop.Tests/Acme.Shop.Tests.csproj",
        "tests/Acme.Shop.Tests/Setup/UiTest.cs",
        "tests/Acme.Shop.Tests/Setup/ApiTest.cs",
        "tests/Acme.Shop.Tests/Setup/Roles.cs",
        "tests/Acme.Shop.Tests/Setup/SignInOnce.cs",
        "tests/Acme.Shop.Tests/Ui/GreetingTests.cs",
    ];

    private static ScaffoldOptions Options(string framework, string output, string? templates = null, string name = Name, string tfm = "net10.0") =>
        new(framework, name, tfm, output, templates ?? RepoPaths.Templates);

    [Theory]
    [InlineData("nunit", "EnableNUnitRunner", "Microsoft.Playwright.NUnit")]
    [InlineData("xunit", "UseMicrosoftTestingPlatformRunner", "Microsoft.Playwright.Xunit.v3")]
    [InlineData("mstest", "EnableMSTestRunner", "Microsoft.Playwright.MSTest.v4")]
    public void RealTemplates_RenderACompleteSkeleton(string framework, string runnerProperty, string adapter)
    {
        using var dir = new TempDir();

        var written = Scaffolder.Run(Options(framework, dir.Path));

        foreach (var file in CommonFiles)
        {
            Assert.True(File.Exists(dir.Combine(file)), $"missing {file}");
        }

        Assert.Equal(written.Count, Directory.GetFiles(dir.Path, "*", SearchOption.AllDirectories).Length);
        var csproj = File.ReadAllText(dir.Combine("tests/Acme.Shop.Tests/Acme.Shop.Tests.csproj"));
        Assert.Contains($"<{runnerProperty}>true</{runnerProperty}>", csproj, StringComparison.Ordinal);
        Assert.Contains(adapter, csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void RealTemplates_SignedInSessions_KeepsStateUnderTheOutputFolder()
    {
        using var dir = new TempDir();
        Scaffolder.Run(Options("nunit", dir.Path));

        var text = File.ReadAllText(dir.Combine("src/Acme.Shop.Framework/Utils/SignedInSessions.cs"));

        Assert.All(
            ["AppContext.BaseDirectory", "\".auth\"", "Guid.NewGuid()", "StorageStateAsync", "could not sign in at the start of the run", "ThroughApi", "ThroughLoginPage", "class SignedOutAttribute"],
            s => Assert.Contains(s, text, StringComparison.Ordinal));
        Assert.All([".Result", ".Wait()", "GetResult()"], s => Assert.DoesNotContain(s, text, StringComparison.Ordinal));
    }

    [Fact]
    public void RealTemplates_Secrets_RemembersWhatItReturns()
    {
        using var dir = new TempDir();
        Scaffolder.Run(Options("nunit", dir.Path));

        var text = File.ReadAllText(dir.Combine("src/Acme.Shop.Framework/Utils/Secrets.cs"));

        Assert.Contains("public static string Get(string name)", text, StringComparison.Ordinal);
        Assert.Contains("Redaction.Remember(value)", text, StringComparison.Ordinal);
        Assert.Contains("$\"Set the {name} environment variable.\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Console.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RealTemplates_Redaction_MasksTheSpikedForms()
    {
        // The forms recorded in docs/framework-matrix.md, "Browser diagnostics and masked traces".
        using var dir = new TempDir();
        Scaffolder.Run(Options("nunit", dir.Path));

        var text = File.ReadAllText(dir.Combine("src/Acme.Shop.Framework/Utils/Redaction.cs"));

        Assert.All(
            ["public static void Remember(string value)", "public static string Mask(string text)", "public static void MaskZip(string path)",
             "authorization|proxy-authorization|cookie|set-cookie|x-api-key", "access_token", "\"***\"", "eyJ", "\"cookies\"", "throwOnInvalidBytes: true",
             // A value typed into a password field is masked even when no one remembered it (side-agent finding).
             "__playwright_value_", "\"\"password\"\"",
             // Review findings: whole-value matches only, and a masked JSON line that no longer parses fails the masking.
             "(?<![\\w-])", "Masking broke the JSON of",
             // Review follow-ups: a scanner finds where a field's value ends, and the zip is read one entry at a time.
             "EndOfValue(", "RememberPasswordFields(ReadText(",
             // Masking hardening: pass 1 reads only the entries that hold DOM snapshots.
             "EndsWith(\".trace\", StringComparison.Ordinal)))"],
            s => Assert.Contains(s, text, StringComparison.Ordinal));
        Assert.All(["Console.", ".Result", ".Wait()"], s => Assert.DoesNotContain(s, text, StringComparison.Ordinal));
    }

    [Fact]
    public void RealTemplates_BrowserDiagnostics_IsCappedAndMasked()
    {
        using var dir = new TempDir();
        Scaffolder.Run(Options("nunit", dir.Path));

        var text = File.ReadAllText(dir.Combine("src/Acme.Shop.Framework/Utils/BrowserDiagnostics.cs"));

        Assert.All(
            ["public static BrowserDiagnostics Attach(IBrowserContext context)", "MaxLines = 50", "MaxLength = 300", "Redaction.Mask(",
             "context.Console", "context.WebError", "context.RequestFailed", "context.Response", "\"warning\"", "Browser diagnostics (", "more"],
            s => Assert.Contains(s, text, StringComparison.Ordinal));
        Assert.All(["Console.Write", ".Result", ".Wait()"], s => Assert.DoesNotContain(s, text, StringComparison.Ordinal));
    }

    [Fact]
    public void RealTemplates_TraceFiles_MasksOrRemovesTheTrace()
    {
        // An unmasked trace must never remain: if masking fails, the trace is deleted and the test output says so.
        using var dir = new TempDir();
        Scaffolder.Run(Options("nunit", dir.Path));

        var text = File.ReadAllText(dir.Combine("src/Acme.Shop.Framework/Utils/TraceFiles.cs"));

        Assert.All(
            ["public static string? MaskOrRemove(string path)", "Redaction.MaskZip(path)", "File.Delete(path)", "Trace removed: it could not be masked (",
             // Review finding: a failed delete must not escape teardown silently.
             "was NOT masked and could NOT be deleted"],
            s => Assert.Contains(s, text, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("nunit", "TestContext.Out.WriteLine")]
    [InlineData("xunit", "TestContext.Current.TestOutputHelper?.WriteLine")]
    [InlineData("mstest", "TestContext.WriteLine")]
    public void RealTemplates_UiTest_WritesDiagnosticsAndMasksTheTrace(string framework, string writer)
    {
        using var dir = new TempDir();
        Scaffolder.Run(Options(framework, dir.Path));

        var text = File.ReadAllText(dir.Combine("tests/Acme.Shop.Tests/Setup/UiTest.cs"));

        Assert.All(
            ["BrowserDiagnostics.Attach(Context)", ".Report()", "TraceFiles.MaskOrRemove(", writer],
            s => Assert.Contains(s, text, StringComparison.Ordinal));

        // Review finding: a crashed context throws from Tracing.StopAsync; the diagnostics are still written.
        var finallyAt = text.IndexOf("finally", StringComparison.Ordinal);
        Assert.True(finallyAt > text.IndexOf("Tracing.StopAsync", StringComparison.Ordinal) && finallyAt < text.IndexOf(".Report()", StringComparison.Ordinal), text);
        Assert.DoesNotContain("static BrowserDiagnostics", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RealTemplates_SignedInSessions_KeepsTheWholeFailure_AndRefusesCollidingRoleNames()
    {
        // Review findings: the failure kept only its first line, and "a b" / "a_b" / "Admin" / "admin" shared one file.
        using var dir = new TempDir();
        Scaffolder.Run(Options("nunit", dir.Path));

        var text = File.ReadAllText(dir.Combine("src/Acme.Shop.Framework/Utils/SignedInSessions.cs"));

        Assert.Contains("ConcurrentDictionary<string, Exception?>", text, StringComparison.Ordinal);
        Assert.Contains("ex.GetType().Name", text, StringComparison.Ordinal);
        Assert.Contains("could not sign in at the start of the run: {Reason(failure)}\", failure)", text, StringComparison.Ordinal);
        Assert.Contains("StringComparer.OrdinalIgnoreCase", text, StringComparison.Ordinal);
        Assert.Contains("share the file name", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nunit")]
    [InlineData("xunit")]
    [InlineData("mstest")]
    public void RealTemplates_Roles_ApiExampleSignsInOnTheContextsOwnRequest(string framework)
    {
        // Review finding: an Api class built through ApiContext posts on a separate request context, so its cookies never reach the browser context.
        using var dir = new TempDir();
        Scaffolder.Run(Options(framework, dir.Path));

        var text = File.ReadAllText(dir.Combine("tests/Acme.Shop.Tests/Setup/Roles.cs"));

        Assert.Contains("AuthApi.SignInAsync(context.APIRequest,", text, StringComparison.Ordinal);
        Assert.DoesNotContain("new AuthApi(", text, StringComparison.Ordinal);
        Assert.Contains("same origin", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nunit", "[SetUpFixture]")]
    [InlineData("xunit", "AssemblyFixture")]
    [InlineData("mstest", "[AssemblyInitialize]")]
    public void RealTemplates_SignInOnce_UsesTheFrameworksRunOnceHook(string framework, string marker)
    {
        using var dir = new TempDir();
        Scaffolder.Run(Options(framework, dir.Path));

        var text = File.ReadAllText(dir.Combine("tests/Acme.Shop.Tests/Setup/SignInOnce.cs"));

        Assert.All([marker, "SignedInSessions.SaveAllAsync", "Roles.All", "DeleteAll", "UiTest.BaseContextOptions()"], s => Assert.Contains(s, text, StringComparison.Ordinal));
        if (framework == "nunit")
        {
            // A [SetUpFixture] covers only its own namespace and those below it.
            Assert.Contains("namespace Acme.Shop.Tests;", text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("nunit")]
    [InlineData("xunit")]
    [InlineData("mstest")]
    public void RealTemplates_UiTest_ChoosesTheRolesState(string framework)
    {
        using var dir = new TempDir();
        Scaffolder.Run(Options(framework, dir.Path));

        var text = File.ReadAllText(dir.Combine("tests/Acme.Shop.Tests/Setup/UiTest.cs"));

        Assert.All(["protected virtual string? Role", "SignedInSessions.PathFor(", "SignedOutAttribute", "public static BrowserNewContextOptions BaseContextOptions()"], s => Assert.Contains(s, text, StringComparison.Ordinal));
    }

    // NUnit and MSTest allow overloaded tests, and GetMethod(name) throws on them (spike 2026-11-22).
    // xUnit makes overloads a build error (xUnit1024), so its lookup by name stays.
    [Theory]
    [InlineData("nunit")]
    [InlineData("mstest")]
    public void RealTemplates_UiTest_OverloadsMustAgreeOnSignedOut(string framework)
    {
        using var dir = new TempDir();
        Scaffolder.Run(Options(framework, dir.Path));

        var text = File.ReadAllText(dir.Combine("tests/Acme.Shop.Tests/Setup/UiTest.cs"));

        Assert.DoesNotContain("GetMethod(name)", text, StringComparison.Ordinal);
        Assert.Contains("disagree on [SignedOut]", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nunit")]
    [InlineData("xunit")]
    [InlineData("mstest")]
    public void RealTemplates_Roles_ShipsEmpty(string framework)
    {
        using var dir = new TempDir();
        Scaffolder.Run(Options(framework, dir.Path));

        var lines = File.ReadAllLines(dir.Combine("tests/Acme.Shop.Tests/Setup/Roles.cs"));

        Assert.Contains(lines, l => l.Contains("All { get; } = [];", StringComparison.Ordinal));
        Assert.All(lines.Where(l => l.Contains("Role.Through", StringComparison.Ordinal)), l => Assert.StartsWith("//", l.TrimStart(), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Setup/Roles.cs", "tests/Acme.Shop.Tests/Setup/Roles.cs")]
    [InlineData("Setup/SignInOnce.cs", "tests/Acme.Shop.Tests/Setup/SignInOnce.cs")]
    [InlineData("Utils/SignedInSessions.cs", "src/Acme.Shop.Framework/Utils/SignedInSessions.cs")]
    public void ScaffoldOnly_RendersEachSignInFile(string only, string expected)
    {
        // Projects scaffolded before sign-in reuse add these files one at a time.
        using var dir = new TempDir();

        var written = Scaffolder.Run(Options("mstest", dir.Path) with { Only = only });

        Assert.Equal([expected], written);
    }

    [Theory]
    [InlineData("nunit")]
    [InlineData("xunit")]
    [InlineData("mstest")]
    public void RealTemplates_LeaveNoPlaceholders(string framework)
    {
        using var dir = new TempDir();

        Scaffolder.Run(Options(framework, dir.Path));

        foreach (var file in Directory.GetFiles(dir.Path, "*", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("{{", text, StringComparison.Ordinal);
            Assert.DoesNotContain("__Name__", file, StringComparison.Ordinal);
        }

        var page = File.ReadAllText(dir.Combine("src/Acme.Shop.Framework/Pages/GreetingPage.cs"));
        Assert.Contains("namespace Acme.Shop.Framework.Pages;", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TargetFramework_IsWrittenToDirectoryBuildProps()
    {
        using var dir = new TempDir();

        Scaffolder.Run(Options("nunit", dir.Path, tfm: "net9.0"));

        var props = File.ReadAllText(dir.Combine("Directory.Build.props"));
        Assert.Contains("<TargetFramework>net9.0</TargetFramework>", props, StringComparison.Ordinal);
    }

    [Fact]
    public void FrameworkName_IsCaseInsensitive()
    {
        using var dir = new TempDir();

        Scaffolder.Run(Options("NUnit", dir.Path));

        Assert.True(File.Exists(dir.Combine("tests/Acme.Shop.Tests/Setup/UiTest.cs")));
    }

    [Theory]
    [InlineData("junit")]
    [InlineData("")]
    public void UnknownFramework_IsRejected(string framework)
    {
        using var dir = new TempDir();

        var ex = Assert.Throws<ScaffoldException>(() => Scaffolder.Run(Options(framework, dir.Path)));

        Assert.Contains("nunit, xunit, mstest", ex.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1Shop")]
    [InlineData("Acme..Shop")]
    [InlineData("Acme Shop")]
    [InlineData("Acme/Shop")]
    [InlineData(".Shop")]
    [InlineData("Shop.")]
    public void InvalidName_IsRejected(string name)
    {
        using var dir = new TempDir();

        Assert.Throws<ScaffoldException>(() => Scaffolder.Run(Options("nunit", dir.Path, name: name)));
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    [Theory]
    [InlineData("net10")]
    [InlineData("netstandard2.0")]
    [InlineData("net10.0-windows")]
    [InlineData("")]
    public void InvalidTargetFramework_IsRejected(string tfm)
    {
        using var dir = new TempDir();

        Assert.Throws<ScaffoldException>(() => Scaffolder.Run(Options("nunit", dir.Path, tfm: tfm)));
    }

    [Fact]
    public void ExistingFile_IsNeverOverwritten_AndNothingIsWritten()
    {
        using var dir = new TempDir();
        dir.Write("global.json", "mine");

        var ex = Assert.Throws<ScaffoldException>(() => Scaffolder.Run(Options("nunit", dir.Path)));

        Assert.Contains("global.json", ex.Message, StringComparison.Ordinal);
        Assert.Equal("mine", File.ReadAllText(dir.Combine("global.json")));
        Assert.Single(Directory.GetFileSystemEntries(dir.Path));
    }

    [Fact]
    public void UnrelatedExistingFiles_AreKept()
    {
        using var dir = new TempDir();
        dir.Write("README.md", "hello");

        Scaffolder.Run(Options("nunit", dir.Path));

        Assert.Equal("hello", File.ReadAllText(dir.Combine("README.md")));
    }

    [Fact]
    public void UnknownPlaceholder_IsATemplateError_AndNothingIsWritten()
    {
        using var templates = FakeTemplates();
        templates.Write("nunit/bad.txt", "{{Nmae}}");
        using var output = new TempDir();

        var ex = Assert.Throws<ScaffoldException>(() => Scaffolder.Run(Options("nunit", output.Path, templates.Path)));

        Assert.Contains("{{Nmae}}", ex.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(output.Path));
    }

    [Fact]
    public void SamePathInTwoLayers_IsATemplateError()
    {
        using var templates = FakeTemplates();
        templates.Write("build/same.txt", "a");
        templates.Write("nunit/same.txt", "b");
        using var output = new TempDir();

        var ex = Assert.Throws<ScaffoldException>(() => Scaffolder.Run(Options("nunit", output.Path, templates.Path)));

        Assert.Contains("same.txt", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DotPrefixAndNameTokens_AreAppliedToPaths()
    {
        using var templates = FakeTemplates();
        templates.Write("build/dot.gitignore", "bin/");
        templates.Write("nunit/tests/__Name__.Tests/__Name__.Tests.csproj", "<Project />");
        using var output = new TempDir();

        Scaffolder.Run(Options("nunit", output.Path, templates.Path));

        Assert.Equal("bin/", File.ReadAllText(output.Combine(".gitignore")));
        Assert.True(File.Exists(output.Combine("tests/Acme.Shop.Tests/Acme.Shop.Tests.csproj")));
    }

    [Fact]
    public void DotPrefix_IsAppliedToFolders()
    {
        using var templates = FakeTemplates();
        templates.Write("nunit/dot.agents/notes.md", "x");
        using var output = new TempDir();

        Scaffolder.Run(Options("nunit", output.Path, templates.Path));

        Assert.True(File.Exists(output.Combine(".agents/notes.md")));
    }

    [Theory]
    [InlineData("nunit", "--filter \"TestCategory=smoke\"")]
    [InlineData("xunit", "--filter-trait \"Category=smoke\"")]
    [InlineData("mstest", "--filter \"TestCategory=smoke\"")]
    public void RealTemplates_WriteTheDotnetTestOverlay_WithTheFrameworksFilter(string framework, string filter)
    {
        using var dir = new TempDir();

        Scaffolder.Run(Options(framework, dir.Path));

        var overlay = File.ReadAllText(dir.Combine(".agents/skill-overlays/dotnet-test/run-tests.md"));
        Assert.Contains("core: dotnet-test/run-tests", overlay, StringComparison.Ordinal);
        Assert.Contains("--project tests/Acme.Shop.Tests/Acme.Shop.Tests.csproj", overlay, StringComparison.Ordinal);
        Assert.Contains(filter, overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingTemplateLayer_IsReported()
    {
        using var templates = new TempDir();
        templates.Write("build/x.txt", "x");
        using var output = new TempDir();

        var ex = Assert.Throws<ScaffoldException>(() => Scaffolder.Run(Options("nunit", output.Path, templates.Path)));

        Assert.Contains("framework", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_Scaffold_WritesFiles_AndListsThem()
    {
        using var dir = new TempDir();
        var io = new Console3();

        var code = Cli.Run(
            ["scaffold", "--framework", "xunit", "--name", Name, "--output", dir.Path, "--templates", RepoPaths.Templates],
            io.In, io.Out, io.Err);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.True(File.Exists(dir.Combine("tests/Acme.Shop.Tests/Ui/GreetingTests.cs")));
        Assert.Contains("GreetingTests.cs", io.Out.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("scaffold")]
    [InlineData("scaffold", "--name", "X")]
    [InlineData("scaffold", "--framework", "nunit", "--name")]
    [InlineData("scaffold", "--framework", "nunit", "--name", "X", "--bogus", "1")]
    public void Cli_Scaffold_BadArguments_ReturnUsageCode(params string[] args)
    {
        var io = new Console3();

        var code = Cli.Run(args, io.In, io.Out, io.Err);

        Assert.Equal(ExitCodes.Usage, code);
    }

    [Fact]
    public void Cli_Scaffold_UserError_ReturnsFailed_WithMessage()
    {
        using var dir = new TempDir();
        var io = new Console3();

        var code = Cli.Run(
            ["scaffold", "--framework", "nunit", "--name", "1Bad", "--output", dir.Path, "--templates", RepoPaths.Templates],
            io.In, io.Out, io.Err);

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains("1Bad", io.Err.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nunit")]
    [InlineData("xunit")]
    [InlineData("mstest")]
    public void Only_AddsOneFile_ToAnExistingProject(string framework)
    {
        using var dir = new TempDir();
        dir.Write("tests/Acme.Shop.Tests/Setup/UiTest.cs", "// the project's own");

        var written = Scaffolder.Run(Options(framework, dir.Path) with { Only = "Setup/ApiTest.cs" });

        Assert.Equal(["tests/Acme.Shop.Tests/Setup/ApiTest.cs"], written);
        Assert.Equal(2, Directory.GetFiles(dir.Path, "*", SearchOption.AllDirectories).Length);
        var content = File.ReadAllText(dir.Combine("tests/Acme.Shop.Tests/Setup/ApiTest.cs"));
        Assert.Contains("namespace Acme.Shop.Tests", content, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_AcceptsTheFullOutputPath_AndBackslashes()
    {
        using var dir = new TempDir();

        var written = Scaffolder.Run(Options("nunit", dir.Path) with { Only = @"tests\Acme.Shop.Tests\Setup\ApiTest.cs" });

        Assert.Equal(["tests/Acme.Shop.Tests/Setup/ApiTest.cs"], written);
    }

    [Fact]
    public void Only_MatchesWholeSegments()
    {
        using var dir = new TempDir();

        var ex = Assert.Throws<ScaffoldException>(() => Scaffolder.Run(Options("nunit", dir.Path) with { Only = "piTest.cs" }));

        Assert.Contains("piTest.cs", ex.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(dir.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Only_NeverOverwrites()
    {
        using var dir = new TempDir();
        dir.Write("tests/Acme.Shop.Tests/Setup/ApiTest.cs", "// mine");

        Assert.Throws<ScaffoldException>(() => Scaffolder.Run(Options("nunit", dir.Path) with { Only = "Setup/ApiTest.cs" }));

        Assert.Equal("// mine", File.ReadAllText(dir.Combine("tests/Acme.Shop.Tests/Setup/ApiTest.cs")));
    }

    [Fact]
    public void Cli_Scaffold_Only_WritesOneFile()
    {
        using var dir = new TempDir();
        var io = new Console3();

        var code = Cli.Run(
            ["scaffold", "--framework", "mstest", "--name", Name, "--output", dir.Path, "--templates", RepoPaths.Templates, "--only", "Setup/ApiTest.cs"],
            io.In, io.Out, io.Err);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Single(Directory.GetFiles(dir.Path, "*", SearchOption.AllDirectories));
        Assert.Contains("Setup/ApiTest.cs", io.Out.ToString(), StringComparison.Ordinal);
    }

    private static TempDir FakeTemplates()
    {
        var dir = new TempDir();
        dir.Write("build/root.txt", "{{Name}} {{TargetFramework}} {{Framework}}");
        dir.Write("framework/fw.txt", "fw");
        dir.Write("nunit/n.txt", "n");
        return dir;
    }
}
