using System.Diagnostics;
using System.Text.Json.Nodes;
using Harness.Tools.Conventions;
using Harness.Tools.Scaffolding;
using Harness.Tools.Tests.Hooks;
using static Harness.Tools.Tests.Conventions.ConventionHost;

namespace Harness.Tools.Tests.Conventions;

public class ConventionCheckerTests
{
    private const string HardWait = "class LoginPage\n{\n    async Task XAsync()\n    {\n        await Task.Delay(100);{0}\n    }\n}";

    private static string WithWait(string comment) => HardWait.Replace("{0}", comment, StringComparison.Ordinal);

    // ── exemptions ───────────────────────────────────────────────────────

    [Fact]
    public void Exemption_OnTheSameLine_WithAReason_Suppresses()
    {
        using var host = new ConventionHost().Write(PagePath, WithWait(" // conventions-ok: the widget animates for 100 ms"));

        Assert.DoesNotContain(host.FindingsFor(PagePath), f => f.Rule == "no-hard-wait");
    }

    [Fact]
    public void Exemption_OnTheLineAbove_Suppresses()
    {
        using var host = new ConventionHost().Write(PagePath,
            "class LoginPage\n{\n    async Task XAsync()\n    {\n        // conventions-ok: the widget animates for 100 ms\n        await Task.Delay(100);\n    }\n}");

        Assert.DoesNotContain(host.FindingsFor(PagePath), f => f.Rule == "no-hard-wait");
    }

    [Fact]
    public void Exemption_WithoutAReason_DoesNotSuppress()
    {
        using var host = new ConventionHost().Write(PagePath, WithWait(" // conventions-ok"));

        Assert.Contains(host.FindingsFor(PagePath), f => f.Rule == "no-hard-wait");
    }

    [Fact]
    public void Exemption_NeverSuppressesASecret()
    {
        var strong = "ABC123abc456" + "def789ghijklmnop=";
        using var host = new ConventionHost().Write(PagePath,
            "class LoginPage\n{\n    private const string Password = \"" + strong + "\"; // conventions-ok: test only\n}");

        Assert.Contains(host.FindingsFor(PagePath), f => f.Rule == "secret-literal");
    }

    // ── baseline ─────────────────────────────────────────────────────────

    [Fact]
    public void Baseline_TurnsKnownFindingsIntoLegacy_ButNotNewOnes()
    {
        using var host = new ConventionHost().Write(PagePath, WithWait(string.Empty));
        ConventionChecker.WriteBaseline(host.Root.Path, host.Check());
        Assert.True(File.Exists(host.Root.Combine(".harness", "conventions-baseline.json")));

        var report = host.Check();
        Assert.All(report.Findings.Where(f => f.Rule == "no-hard-wait"), f => Assert.True(f.Legacy));

        host.Write(PagePath, "class LoginPage\n{\n    async Task XAsync()\n    {\n        await Task.Delay(100);\n        await Task.Delay(200);\n    }\n}");
        Assert.Contains(host.Check().Findings, f => f.Rule == "no-hard-wait" && !f.Legacy && f.Line == 6);
    }

    [Fact]
    public void Baseline_RefusesToRecordSecrets()
    {
        var strong = "ABC123abc456" + "def789ghijklmnop=";
        using var host = new ConventionHost().Write(PagePath, "class LoginPage\n{\n    private const string Password = \"" + strong + "\";\n}");

        Assert.Throws<ConventionException>(() => ConventionChecker.WriteBaseline(host.Root.Path, host.Check()));
    }

    [Fact]
    public void Baseline_RefusesAPartialScan()
    {
        using var host = new ConventionHost().Write(PagePath, WithWait(string.Empty));

        Assert.Throws<ConventionException>(() => ConventionChecker.WriteBaseline(host.Root.Path, host.Check(PagePath)));
    }

    [Fact]
    public void Baseline_WithMalformedEntries_IsAnError()
    {
        using var host = new ConventionHost()
            .Write(PagePath, WithWait(string.Empty))
            .Write(".harness/conventions-baseline.json", "{ \"entries\": [ { \"rule\": \"x\" } ] }");

        Assert.Throws<ConventionException>(() => host.Check());
    }

    // ── scope ────────────────────────────────────────────────────────────

    [Fact]
    public void ZeroFiles_IsAnError_NotAPass()
    {
        using var host = new ConventionHost().Write("README.md", "x");

        Assert.Throws<ConventionException>(() => host.Check());
    }

    [Fact]
    public void Files_OutsideTheRoot_OrOutsideALayer_AreRejected()
    {
        using var host = new ConventionHost().Write(PagePath, "class LoginPage { }").Write("tools/x.cs", "class X { }");

        Assert.Throws<ConventionException>(() => host.Check("tools/x.cs"));
        Assert.Throws<ConventionException>(() => host.Check("../elsewhere/x.cs"));
    }

    [Fact]
    public void FullScan_SkipsBuildOutput()
    {
        using var host = new ConventionHost()
            .Write(PagePath, "class LoginPage { }")
            .Write("src/Acme.Framework/obj/Debug/Generated.cs", "class G { void X() { Thread.Sleep(1); } }");

        Assert.DoesNotContain(host.Check().Findings, f => f.File.Contains("/obj/", StringComparison.Ordinal));
    }

    [Fact]
    public void Changed_ChecksOnlyFilesChangedSinceTheBase()
    {
        using var host = new ConventionHost()
            .Write(PagePath, "class LoginPage { }")
            .Write(ApiPath, "class UsersApi { }");
        Git(host.Root.Path, "init", "-q");
        Git(host.Root.Path, "add", ".");
        Git(host.Root.Path, "-c", "user.email=t@example.com", "-c", "user.name=t", "commit", "-q", "-m", "base");
        host.Write(ApiPath, "class UsersApi\n{\n    void X() => Thread.Sleep(1);\n}");

        var report = ConventionChecker.Run(host.Root.Path, files: null, changedSince: "HEAD");

        Assert.Equal(1, report.FileCount);
        Assert.Contains(report.Findings, f => f.File == ApiPath && f.Rule == "no-hard-wait");
    }

    // ── command line ─────────────────────────────────────────────────────

    private static (int Code, string Out, string Err) Cli(params string[] args)
    {
        var io = new Console3();
        var code = Harness.Tools.Cli.Run(["check-conventions", .. args], io.In, io.Out, io.Err);
        return (code, io.Out.ToString(), io.Err.ToString());
    }

    [Fact]
    public void Cli_NewFailure_Exits1_AndPrintsIt()
    {
        using var host = new ConventionHost().Write(PagePath, WithWait(string.Empty));

        var (code, output, _) = Cli("--root", host.Root.Path);

        Assert.Equal(1, code);
        Assert.Contains("FAIL  " + PagePath + ":5  [no-hard-wait]", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_OnlyWarnings_Exits0_UnlessFailOnWarn()
    {
        using var host = new ConventionHost().Write(TestPath, "class LoginTests\n{\n    async Task XAsync() => await Page.GotoAsync(\"/\");\n}");

        Assert.Equal(0, Cli("--root", host.Root.Path, "--files", TestPath).Code);
        Assert.Equal(1, Cli("--root", host.Root.Path, "--files", TestPath, "--fail-on-warn").Code);
    }

    [Fact]
    public void Cli_Json_ListsFailuresAndWarnings()
    {
        using var host = new ConventionHost().Write(PagePath, WithWait(string.Empty));

        var (_, output, _) = Cli("--root", host.Root.Path, "--json");

        var json = JsonNode.Parse(output)!;
        Assert.Equal(1, json["files"]!.GetValue<int>());
        Assert.Equal("no-hard-wait", json["fail"]![0]!["rule"]!.GetValue<string>());
        Assert.NotNull(json["warn"]);
    }

    [Fact]
    public void Cli_ZeroFiles_Exits2()
    {
        using var host = new ConventionHost().Write("README.md", "x");

        var (code, _, err) = Cli("--root", host.Root.Path);

        Assert.Equal(2, code);
        Assert.Contains("no C# files", err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("--root")]
    [InlineData("--files", "a.cs", "--changed")]
    [InlineData("--base-ref", "main")]
    public void Cli_BadArguments_ExitUsage(params string[] args)
    {
        Assert.Equal(ExitCodes.Usage, Cli(args).Code);
    }

    [Fact]
    public void Cli_ListRules_PrintsEveryRule()
    {
        var (code, output, _) = Cli("--list-rules");

        Assert.Equal(0, code);
        Assert.All(ConventionRules.All, r => Assert.Contains(r.Id, output, StringComparison.Ordinal));
    }

    [Fact]
    public void Cli_WriteBaseline_ThenRun_ReportsLegacyOnly()
    {
        using var host = new ConventionHost().Write(PagePath, WithWait(string.Empty));

        Assert.Equal(0, Cli("--root", host.Root.Path, "--write-baseline").Code);
        var (code, output, _) = Cli("--root", host.Root.Path);

        Assert.Equal(0, code);
        Assert.Contains("1 legacy", output, StringComparison.Ordinal);
    }

    // ── the scaffolded skeleton is clean ─────────────────────────────────

    [Theory]
    [InlineData("nunit")]
    [InlineData("xunit")]
    [InlineData("mstest")]
    public void ScaffoldedSkeleton_HasNoFindings(string framework)
    {
        using var dir = new TempDir();
        Scaffolder.Run(new ScaffoldOptions(framework, "Acme.Shop", "net10.0", dir.Path, RepoPaths.Templates));
        dir.Write(".harness/project.json", $"{{ \"framework\": \"{framework}\" }}");

        var report = ConventionChecker.Run(dir.Path, files: null);

        Assert.Empty(report.Findings.Select(f => $"{f.File}:{f.Line} {f.Rule}"));
        Assert.True(report.FileCount >= 4);
    }

    // ── post-edit hook ───────────────────────────────────────────────────

    [Fact]
    public void PostEdit_OnAViolatingFile_GivesFeedback_WithExit2()
    {
        using var hooks = new HookTestHost();
        hooks.Root.Write(PagePath, WithWait(string.Empty));

        var r = hooks.AfterEdit(PagePath);

        Assert.Equal(2, r.Code);
        Assert.Contains("no-hard-wait", r.Err, StringComparison.Ordinal);
        Assert.Contains("warn-only", r.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void PostEdit_OnACleanFile_IsSilent()
    {
        using var hooks = new HookTestHost();
        hooks.Root.Write(PagePath, "namespace Acme.Framework.Pages;\n\npublic sealed class LoginPage\n{\n}\n");

        var r = hooks.AfterEdit(PagePath);

        Assert.Equal(0, r.Code);
        Assert.Equal(string.Empty, r.Err);
    }

    [Theory]
    [InlineData("docs/x.cs")]
    [InlineData("src/Acme.Framework/Pages/LoginPage.json")]
    public void PostEdit_OutsideALayerOrNotCSharp_IsSilent(string path)
    {
        using var hooks = new HookTestHost();
        hooks.Root.Write(path, "class X { void Y() => Thread.Sleep(1); }");

        var r = hooks.AfterEdit(path);

        Assert.Equal(0, r.Code);
    }

    [Fact]
    public void PostEdit_SkipsBaselinedFindings()
    {
        using var hooks = new HookTestHost();
        hooks.Root.Write(PagePath, WithWait(string.Empty));
        ConventionChecker.WriteBaseline(hooks.Root.Path, ConventionChecker.Run(hooks.Root.Path, files: null));

        Assert.Equal(0, hooks.AfterEdit(PagePath).Code);
    }

    private static void Git(string directory, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
