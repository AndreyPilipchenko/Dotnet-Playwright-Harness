using System.Text.Json;

namespace Harness.Tools.Hooks;

internal enum HookDecision
{
    Allow,
    Block,
}

/// <summary>What a hook handler gets: the parsed Claude Code payload, writers for its output, and its context.</summary>
internal sealed record HookInput(string Mode, JsonElement Payload, TextWriter Out, TextWriter Err, HookContext Context);

internal delegate HookDecision HookHandler(HookInput input);

/// <summary>
/// Runs one hook mode. The hook must never get in the way of work because of its own bugs:
/// any internal error, unknown mode or malformed input allows the action (exit 0) and leaves
/// stdout empty. Only a handler's deliberate <see cref="HookDecision.Block"/> returns exit 2.
/// </summary>
internal static class HookRunner
{
    public const int AllowCode = 0;
    public const int BlockCode = 2;

    private static readonly Dictionary<string, HookHandler> DefaultHandlers = new(StringComparer.Ordinal)
    {
        ["session-start"] = HookModes.SessionStart,
        ["pre-shell"] = HookModes.PreShell,
        ["post-shell"] = HookModes.PostShell,
        ["post-edit"] = HookModes.PostEdit,
        ["pre-write"] = HookModes.PreWrite,
        ["pre-compact"] = HookModes.PreCompact,
        ["session-end"] = HookModes.SessionEnd,
    };

    public static IReadOnlyCollection<string> Modes => DefaultHandlers.Keys;

    public static int Run(
        string? mode,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        HookServices? services = null,
        IReadOnlyDictionary<string, HookHandler>? handlers = null)
    {
        try
        {
            handlers ??= DefaultHandlers;
            services ??= HookServices.System;
            if (mode is null || !handlers.TryGetValue(mode, out var handler))
            {
                stderr.WriteLine($"harness hook: unknown mode '{mode}', allowing.");
                return AllowCode;
            }

            if (!TryReadPayload(stdin, out var payload))
            {
                return AllowCode;
            }

            var context = new HookContext(
                services,
                ProjectRoot(services, payload),
                HookModes.String(payload, "session_id") ?? "unknown");

            // Buffer stdout so a handler that fails half-way leaves nothing behind.
            using var buffer = new StringWriter();
            var decision = handler(new HookInput(mode, payload, buffer, stderr, context));
            stdout.Write(buffer.ToString());
            return decision == HookDecision.Block ? BlockCode : AllowCode;
        }
#pragma warning disable CA1031 // Fail open is the point: no exception may block the user's action.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            stderr.WriteLine($"harness hook: internal error in '{mode}', allowing: {ex.Message}");
            return AllowCode;
        }
    }

    /// <summary>CLAUDE_PROJECT_DIR (set by Claude Code for hooks), else the event's cwd, else the process's.</summary>
    private static string ProjectRoot(HookServices services, JsonElement payload)
    {
        var fromEnv = services.GetEnv("CLAUDE_PROJECT_DIR");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        return HookModes.String(payload, "cwd") ?? Directory.GetCurrentDirectory();
    }

    private static bool TryReadPayload(TextReader stdin, out JsonElement payload)
    {
        payload = default;
        var text = stdin.ReadToEnd();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            payload = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
