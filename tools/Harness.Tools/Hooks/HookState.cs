using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Harness.Tools.Hooks;

/// <summary>Rerun-breaker state for one session.</summary>
internal sealed record RerunLedger(string? LastFailFingerprint, bool EditsSinceFail);

/// <summary>
/// Files under <c>.harness/state/</c>. Every read and write is best effort: a state problem must
/// never turn a hook decision into a crash, which the runner would turn into "allow".
/// </summary>
internal sealed class HookState(string projectRoot)
{
    private const long MaxLogBytes = 5 * 1024 * 1024;

    public string Directory { get; } = Path.Combine(projectRoot, ".harness", "state");

    public RerunLedger ReadLedger(string sessionId)
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(LedgerPath(sessionId)));
            return new RerunLedger(
                node?["lastFailFingerprint"]?.GetValue<string>(),
                node?["editsSinceFail"]?.GetValue<bool>() ?? false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new RerunLedger(null, false);
        }
    }

    public void WriteLedger(string sessionId, RerunLedger ledger)
    {
        var node = new JsonObject
        {
            ["lastFailFingerprint"] = ledger.LastFailFingerprint,
            ["editsSinceFail"] = ledger.EditsSinceFail,
        };
        TryWrite(LedgerPath(sessionId), path => File.WriteAllText(path, node.ToJsonString()));
    }

    public void AppendEvent(string sessionId, string mode, string decision, string rule, DateTimeOffset now) =>
        AppendJsonLine("hook-events.jsonl", new JsonObject
        {
            ["ts"] = now.ToString("O"),
            ["session_id"] = sessionId,
            ["mode"] = mode,
            ["decision"] = decision,
            ["rule"] = rule,
        });

    /// <summary>How many deny and ask decisions this session made.</summary>
    public (int Denied, int Asked) CountEvents(string sessionId)
    {
        int denied = 0, asked = 0;
        try
        {
            foreach (var line in File.ReadLines(Path.Combine(Directory, "hook-events.jsonl")))
            {
                var node = TryParse(line);
                if (node?["session_id"]?.GetValue<string>() != sessionId)
                {
                    continue;
                }

                switch (node["decision"]?.GetValue<string>())
                {
                    case "deny":
                        denied++;
                        break;
                    case "ask":
                        asked++;
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No log yet.
        }

        return (denied, asked);
    }

    public void AppendJsonLine(string fileName, JsonObject entry) =>
        TryWrite(Path.Combine(Directory, fileName), path =>
        {
            if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
            {
                File.Move(path, Path.ChangeExtension(path, ".1.jsonl"), overwrite: true);
            }

            File.AppendAllText(path, entry.ToJsonString() + "\n");
        });

    public void AppendText(string fileName, string text) =>
        TryWrite(Path.Combine(Directory, fileName), path => File.AppendAllText(path, text));

    private string LedgerPath(string sessionId)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId)))[..24];
        return Path.Combine(Directory, "hooks", hash + ".json");
    }

    private static JsonNode? TryParse(string line)
    {
        try
        {
            return JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void TryWrite(string path, Action<string> write)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            write(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: losing a log line is better than losing the decision.
        }
    }
}
