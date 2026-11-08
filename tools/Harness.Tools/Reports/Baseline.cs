using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Harness.Tools.Reports;

/// <summary>The outcome of each test in earlier runs. Names and outcomes only, never error text.</summary>
internal sealed record Baseline(string? RunId, string? SavedAt, IReadOnlyDictionary<string, string> Outcomes)
{
    public const string Failed = "failed";

    public const string Passed = "passed";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Null when the file is missing. <paramref name="broken"/> is true when it can't be read.</summary>
    public static Baseline? TryRead(string path, out bool broken)
    {
        broken = false;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject root && root["outcomes"] is JsonObject outcomes)
            {
                return new Baseline(
                    Text(root["runId"]),
                    Text(root["savedAt"]),
                    outcomes.Where(p => Text(p.Value) is not null).ToDictionary(p => p.Key, p => Text(p.Value)!, StringComparer.Ordinal));
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Reported below as a baseline that can't be read.
        }

        broken = true;
        return null;
    }

    /// <summary>Saves the merged outcomes: a test that didn't run keeps its earlier outcome.</summary>
    public static void Save(string path, string? runId, Baseline? earlier, IReadOnlyDictionary<string, string> current)
    {
        var merged = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, outcome) in earlier?.Outcomes ?? new Dictionary<string, string>())
        {
            merged[name] = outcome;
        }

        foreach (var (name, outcome) in current)
        {
            merged[name] = outcome;
        }

        var outcomes = new JsonObject();
        foreach (var (name, outcome) in merged)
        {
            outcomes[name] = outcome;
        }

        var root = new JsonObject
        {
            ["runId"] = runId,
            ["savedAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["outcomes"] = outcomes,
        };

        CiFiles.EnsureFolder(path);
        File.WriteAllText(path, root.ToJsonString(Indented));
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
