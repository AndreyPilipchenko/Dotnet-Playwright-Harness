using System.Text.Json.Nodes;
using Harness.Tools.Hooks;
using Harness.Tools.Installing;

namespace Harness.Tools.Tests.Installing;

public class InstallerTests
{
    private static string Template => Path.Combine(RepoPaths.Templates, "settings.harness.json");

    private static InstallResult Install(TempDir root, string? framework = "nunit", string? name = "Acme.Shop", string? tfm = null) =>
        Installer.Run(new InstallOptions(root.Path, framework, name, tfm, Template));

    private static JsonNode Settings(TempDir root) => JsonNode.Parse(File.ReadAllText(root.Combine(".claude", "settings.json")))!;

    private static JsonNode Project(TempDir root) => JsonNode.Parse(File.ReadAllText(root.Combine(".harness", "project.json")))!;

    /// <summary>Every command handler in a settings document, with its event and matcher.</summary>
    private static List<(string Event, string? Matcher, JsonObject Handler)> Handlers(JsonNode settings) =>
        [.. (settings["hooks"]?.AsObject() ?? [])
            .SelectMany(e => e.Value!.AsArray().SelectMany(g => g!["hooks"]!.AsArray()
                .Select(h => (e.Key, g["matcher"]?.GetValue<string>(), h!.AsObject()))))];

    private static bool IsHarness(JsonObject handler) => handler.ToJsonString().Contains("Harness.Tools.dll", StringComparison.Ordinal);

    // ── the template matches the tool ────────────────────────────────────

    [Fact]
    public void Template_SendsFailedShellCommands_ToPostShell()
    {
        // Claude Code fires PostToolUseFailure, not PostToolUse, for a command that exits non-zero.
        var groups = JsonNode.Parse(File.ReadAllText(Template))!["hooks"]!["PostToolUseFailure"]!.AsArray();

        var group = Assert.Single(groups);
        Assert.Equal("Bash|PowerShell", group!["matcher"]!.GetValue<string>());
        var handler = Assert.Single(group["hooks"]!.AsArray());
        Assert.Equal("post-shell", handler!["args"]![2]!.GetValue<string>());
    }

    [Fact]
    public void Template_HasAnExecFormHandler_ForEveryHookMode()
    {
        var handlers = Handlers(JsonNode.Parse(File.ReadAllText(Template))!);

        var modes = handlers.Select(h => h.Handler["args"]!.AsArray().Select(a => a!.GetValue<string>()).ToArray()).ToList();
        Assert.All(handlers, h => Assert.Equal("dotnet", h.Handler["command"]!.GetValue<string>()));
        Assert.All(modes, args =>
        {
            Assert.Equal("${CLAUDE_PROJECT_DIR}/.harness/tools/bin/Harness.Tools.dll", args[0]);
            Assert.Equal("hook", args[1]);
        });
        Assert.Equal(HookRunner.Modes.Order(StringComparer.Ordinal), modes.Select(a => a[2]).Distinct().Order(StringComparer.Ordinal));
    }

    // ── project.json ─────────────────────────────────────────────────────

    [Fact]
    public void FreshProject_GetsProjectJson()
    {
        using var root = new TempDir();

        Install(root, "XUnit", "Acme.Shop", "net9.0");

        var project = Project(root);
        Assert.Equal("xunit", project["framework"]!.GetValue<string>());
        Assert.Equal("Acme.Shop", project["name"]!.GetValue<string>());
        Assert.Equal("net9.0", project["targetFramework"]!.GetValue<string>());
    }

    [Fact]
    public void Rerun_WithoutOptions_UsesProjectJson_AndKeepsOtherKeys()
    {
        using var root = new TempDir();
        root.Write(".harness/project.json", """{ "framework": "mstest", "name": "Acme.Shop", "targetFramework": "net10.0", "conventions": { "categories": ["smoke"] } }""");

        Install(root, framework: null, name: null);

        var project = Project(root);
        Assert.Equal("mstest", project["framework"]!.GetValue<string>());
        Assert.Equal("smoke", project["conventions"]!["categories"]![0]!.GetValue<string>());
    }

    [Fact]
    public void ChangingTheFramework_OfAnInstalledProject_IsRefused()
    {
        using var root = new TempDir();
        root.Write(".harness/project.json", """{ "framework": "mstest", "name": "Acme.Shop" }""");

        var ex = Assert.Throws<InstallException>(() => Install(root, "nunit"));
        Assert.Contains("mstest", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FreshProject_WithoutAFramework_IsRefused()
    {
        using var root = new TempDir();

        Assert.Throws<InstallException>(() => Install(root, framework: null));
    }

    [Fact]
    public void InvalidName_IsRefused()
    {
        using var root = new TempDir();

        Assert.Throws<InstallException>(() => Install(root, name: "1-bad name"));
    }

    [Theory]
    [InlineData("acme-shop", "AcmeShop")]
    [InlineData("my shop.e2e tests", "MyShop.E2eTests")]
    [InlineData("2024-app", "_2024App")]
    [InlineData("---", "Tests")]
    public void DefaultName_ComesFromTheFolderName(string folder, string expected)
    {
        Assert.Equal(expected, Installer.DefaultName(folder));
    }

    // ── settings.json ────────────────────────────────────────────────────

    [Fact]
    public void FreshProject_GetsTheHarnessHooks_AndDenyList()
    {
        using var root = new TempDir();

        Install(root);

        var settings = Settings(root);
        Assert.Equal(8, Handlers(settings).Count(h => IsHarness(h.Handler)));
        Assert.Contains("Read(./.env)", settings["permissions"]!["deny"]!.AsArray().Select(d => d!.GetValue<string>()));
    }

    [Fact]
    public void UserEntries_ArePreserved()
    {
        using var root = new TempDir();
        root.Write(".claude/settings.json", """
            {
              "model": "opus",
              "permissions": { "allow": ["Bash(git status)"], "deny": ["Read(./secret.txt)", "Read(./.env)"] },
              "hooks": {
                "PreToolUse": [
                  { "matcher": "Bash", "hooks": [ { "type": "command", "command": "my-guard.exe" } ] }
                ],
                "Stop": [ { "hooks": [ { "type": "command", "command": "notify.exe" } ] } ]
              }
            }
            """);

        Install(root);

        var settings = Settings(root);
        Assert.Equal("opus", settings["model"]!.GetValue<string>());
        Assert.Equal("Bash(git status)", settings["permissions"]!["allow"]![0]!.GetValue<string>());
        var deny = settings["permissions"]!["deny"]!.AsArray().Select(d => d!.GetValue<string>()).ToList();
        Assert.Equal("Read(./secret.txt)", deny[0]);
        Assert.Single(deny, d => d == "Read(./.env)");
        var handlers = Handlers(settings);
        Assert.Contains(handlers, h => h.Event == "PreToolUse" && h.Handler["command"]!.GetValue<string>() == "my-guard.exe");
        Assert.Contains(handlers, h => h.Event == "Stop");
        Assert.Equal(8, handlers.Count(h => IsHarness(h.Handler)));
    }

    [Fact]
    public void Rerun_IsByteIdentical()
    {
        using var root = new TempDir();
        Install(root);
        var first = File.ReadAllText(root.Combine(".claude", "settings.json"));

        Install(root);

        Assert.Equal(first, File.ReadAllText(root.Combine(".claude", "settings.json")));
    }

    [Fact]
    public void StaleHarnessHandlers_AreReplaced_ButUserHandlersInTheSameGroupStay()
    {
        using var root = new TempDir();
        root.Write(".claude/settings.json", """
            {
              "hooks": {
                "PreToolUse": [
                  { "matcher": "Bash", "hooks": [
                    { "type": "command", "command": "dotnet .harness/tools/bin/Harness.Tools.dll hook old-mode" },
                    { "type": "command", "command": "my-guard.exe" }
                  ] }
                ],
                "Notification": [ { "hooks": [ { "type": "command", "command": "dotnet", "args": ["x/Harness.Tools.dll", "hook", "gone"] } ] } ]
              }
            }
            """);

        Install(root);

        var settings = Settings(root);
        var handlers = Handlers(settings);
        Assert.DoesNotContain(handlers, h => h.Handler.ToJsonString().Contains("old-mode", StringComparison.Ordinal));
        Assert.Null(settings["hooks"]!["Notification"]);
        Assert.Contains(handlers, h => h.Matcher == "Bash" && h.Handler["command"]!.GetValue<string>() == "my-guard.exe");
        Assert.Equal(8, handlers.Count(h => IsHarness(h.Handler)));
    }

    [Fact]
    public void MalformedSettings_AreAnError_AndLeftUntouched()
    {
        using var root = new TempDir();
        root.Write(".claude/settings.json", "{ not json");

        Assert.Throws<InstallException>(() => Install(root));
        Assert.Equal("{ not json", File.ReadAllText(root.Combine(".claude", "settings.json")));
        Assert.False(File.Exists(root.Combine(".harness", "project.json")));
    }

    // ── .harness/.gitignore ──────────────────────────────────────────────

    [Fact]
    public void HarnessGitignore_KeepsBuildOutputAndStateOut_WithoutDuplicates()
    {
        using var root = new TempDir();
        root.Write(".harness/.gitignore", "notes-local.md\nstate/\n");

        Install(root);
        Install(root);

        var lines = File.ReadAllLines(root.Combine(".harness", ".gitignore"));
        Assert.Equal("notes-local.md", lines[0]);
        Assert.Single(lines, l => l == "state/");
        Assert.Contains("tools/bin/", lines);
        Assert.Contains("tools/**/obj/", lines);
    }

    // ── command line ─────────────────────────────────────────────────────

    [Fact]
    public void Cli_Install_Exits0_AndReports()
    {
        using var root = new TempDir();
        var io = new Console3();

        var code = Cli.Run(["install", "--root", root.Path, "--framework", "nunit", "--name", "Acme.Shop", "--settings-template", Template], io.In, io.Out, io.Err);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("settings.json", io.Out.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_Install_BadFramework_Exits1()
    {
        using var root = new TempDir();
        var io = new Console3();

        var code = Cli.Run(["install", "--root", root.Path, "--framework", "jest", "--settings-template", Template], io.In, io.Out, io.Err);

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains("jest", io.Err.ToString(), StringComparison.Ordinal);
    }
}
