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
        "resources/testData/GreetingTestData.json",
        "tests/Acme.Shop.Tests/Acme.Shop.Tests.csproj",
        "tests/Acme.Shop.Tests/Setup/UiTest.cs",
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

    private static TempDir FakeTemplates()
    {
        var dir = new TempDir();
        dir.Write("build/root.txt", "{{Name}} {{TargetFramework}} {{Framework}}");
        dir.Write("framework/fw.txt", "fw");
        dir.Write("nunit/n.txt", "n");
        return dir;
    }
}
