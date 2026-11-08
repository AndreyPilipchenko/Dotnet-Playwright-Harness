using System.Text.RegularExpressions;
using Harness.Tools.Conventions;

namespace Harness.Tools.Traceability;

/// <summary>One kind of QA record: where it lives, its id prefix, and the fields it must carry.</summary>
internal sealed record RecordKind(string Folder, string Prefix, IReadOnlyDictionary<string, string[]?> Fields)
{
    public static readonly RecordKind Requirement = new("requirements", "REQ", new Dictionary<string, string[]?>
    {
        ["title"] = null,
        ["source"] = null,
        ["status"] = ["accepted", "proposed", "needs-decision"],
    });

    public static readonly RecordKind TestCase = new("test-cases", "TC", new Dictionary<string, string[]?>
    {
        ["title"] = null,
        ["requirements"] = null,
        ["priority"] = ["high", "medium", "low"],
        ["automation"] = ["planned", "automated", "manual-only"],
    });

    public static readonly RecordKind BugReport = new("bug-reports", "BUG", new Dictionary<string, string[]?>
    {
        ["title"] = null,
        ["status"] = ["open", "fixed", "wont-fix"],
        ["severity"] = ["critical", "high", "medium", "low"],
    });

    public static readonly IReadOnlyList<RecordKind> All = [Requirement, TestCase, BugReport];
}

/// <summary>A parsed record: its front matter fields, keyed by name.</summary>
internal sealed record QaRecord(RecordKind Kind, string Id, string File, IReadOnlyDictionary<string, string> Fields)
{
    public string Field(string name) => Fields.TryGetValue(name, out var value) ? value : string.Empty;

    /// <summary>A comma-separated list field, such as <c>requirements: REQ-001, REQ-002</c>.</summary>
    public IReadOnlyList<string> List(string name) =>
        [.. Field(name).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}

/// <summary>Reads <c>docs/qa/&lt;kind&gt;/*.md</c> and reports records that cannot be used.</summary>
internal static class RecordReader
{
    public const string QaFolder = "docs/qa";

    public static (List<QaRecord> Records, List<Finding> Findings) Read(string root)
    {
        var records = new List<QaRecord>();
        var findings = new List<Finding>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var kind in RecordKind.All)
        {
            var folder = Path.Combine(root, QaFolder, kind.Folder);
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var path in Directory.GetFiles(folder, "*.md").Order(StringComparer.Ordinal))
            {
                var file = QaFolder + "/" + kind.Folder + "/" + Path.GetFileName(path);
                var problem = Parse(kind, path, file, out var record);
                if (problem is null && !seen.Add(record!.Id))
                {
                    problem = $"Another record already has the id {record.Id}.";
                }

                if (problem is null)
                {
                    records.Add(record!);
                }
                else
                {
                    findings.Add(TraceabilityRules.Finding("record-format", file, 1, problem));
                }
            }
        }

        return (records, findings);
    }

    private static string? Parse(RecordKind kind, string path, string file, out QaRecord? record)
    {
        record = null;
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return "The record has no front matter (a block between --- lines at the top).";
        }

        var end = Array.FindIndex(lines, 1, l => l.Trim() == "---");
        if (end < 0)
        {
            return "The front matter is not closed with a --- line.";
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines[1..end])
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                fields[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }

        var id = fields.GetValueOrDefault("id") ?? string.Empty;
        if (!Regex.IsMatch(id, "^" + kind.Prefix + "-[0-9]+$"))
        {
            return $"The id must look like {kind.Prefix}-001.";
        }

        if (id != Path.GetFileNameWithoutExtension(path))
        {
            return $"The file name must be the id: {id}.md.";
        }

        foreach (var (name, allowed) in kind.Fields)
        {
            if (!fields.TryGetValue(name, out var value))
            {
                return $"The front matter has no '{name}' field.";
            }

            if (allowed is not null && !allowed.Contains(value, StringComparer.Ordinal))
            {
                return $"'{name}' must be one of: {string.Join(", ", allowed)}.";
            }
        }

        record = new QaRecord(kind, id, file, fields);
        return null;
    }
}
