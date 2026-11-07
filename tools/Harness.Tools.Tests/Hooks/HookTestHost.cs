using System.Text.Json;
using System.Text.Json.Nodes;
using Harness.Tools.Hooks;

namespace Harness.Tools.Tests.Hooks;

/// <summary>Runs hook modes against a temporary project root with injected services.</summary>
internal sealed class HookTestHost : IDisposable
{
    private readonly Dictionary<string, string> _env = new(StringComparer.Ordinal);

    public HookTestHost()
    {
        _env["CLAUDE_PROJECT_DIR"] = Root.Path;
    }

    public TempDir Root { get; } = new();

    public TempDir Home { get; } = new();

    public string? Branch { get; set; } = "feature/x";

    public DateTimeOffset Now { get; set; } = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    public void SetEnv(string name, string value) => _env[name] = value;

    public HookServices Services => new(
        name => _env.GetValueOrDefault(name),
        _ => Branch,
        () => Now,
        Home.Path);

    public HookResult Run(string mode, object payload)
    {
        var json = payload as string ?? JsonSerializer.Serialize(payload);
        var io = Console3.WithInput(json);
        var code = HookRunner.Run(mode, io.In, io.Out, io.Err, Services);
        return new HookResult(code, io.Out.ToString(), io.Err.ToString());
    }

    public HookResult Shell(string command, string session = "s1", string tool = "Bash") =>
        Run("pre-shell", new { session_id = session, tool_name = tool, tool_input = new { command } });

    public HookResult AfterShell(string command, string output, string session = "s1") =>
        Run("post-shell", new { session_id = session, tool_name = "Bash", tool_input = new { command }, tool_response = new { stdout = output, stderr = "" } });

    public HookResult AfterEdit(string relativePath, string session = "s1") =>
        Run("post-edit", new { session_id = session, tool_name = "Edit", tool_input = new { file_path = Root.Combine(relativePath) } });

    public string StateFile(string name) => Root.Combine(".harness", "state", name);

    public void Dispose()
    {
        Root.Dispose();
        Home.Dispose();
    }
}

internal sealed record HookResult(int Code, string Out, string Err)
{
    public string? PermissionDecision =>
        string.IsNullOrWhiteSpace(Out)
            ? null
            : JsonNode.Parse(Out)?["hookSpecificOutput"]?["permissionDecision"]?.GetValue<string>();

    public string? PermissionReason =>
        string.IsNullOrWhiteSpace(Out)
            ? null
            : JsonNode.Parse(Out)?["hookSpecificOutput"]?["permissionDecisionReason"]?.GetValue<string>();
}
