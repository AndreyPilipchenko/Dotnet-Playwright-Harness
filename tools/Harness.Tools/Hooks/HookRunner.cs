using System.Text.Json;

namespace Harness.Tools.Hooks;

internal enum HookDecision
{
    Allow,
    Block,
}

/// <summary>What a hook handler gets: the parsed Claude Code payload and writers for its output.</summary>
internal sealed record HookInput(string Mode, JsonElement Payload, TextWriter Out, TextWriter Err);

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
        ["session-start"] = Allow,
        ["pre-shell"] = Allow,
        ["post-shell"] = Allow,
        ["post-edit"] = Allow,
        ["pre-write"] = Allow,
        ["pre-compact"] = Allow,
        ["session-end"] = Allow,
    };

    public static IReadOnlyCollection<string> Modes => DefaultHandlers.Keys;

    public static int Run(
        string? mode,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        IReadOnlyDictionary<string, HookHandler>? handlers = null)
    {
        try
        {
            handlers ??= DefaultHandlers;
            if (mode is null || !handlers.TryGetValue(mode, out var handler))
            {
                stderr.WriteLine($"harness hook: unknown mode '{mode}', allowing.");
                return AllowCode;
            }

            if (!TryReadPayload(stdin, out var payload))
            {
                return AllowCode;
            }

            // Buffer stdout so a handler that fails half-way leaves nothing behind.
            using var buffer = new StringWriter();
            var decision = handler(new HookInput(mode, payload, buffer, stderr));
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

    private static HookDecision Allow(HookInput input) => HookDecision.Allow;

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
