using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Harness.Tools.Conventions;

/// <summary>
/// Runs the convention rules over a project. A run that cannot check what was asked (no files,
/// a file outside the layers, a broken baseline or project.json) throws <see cref="ConventionException"/>
/// instead of passing.
/// </summary>
internal static class ConventionChecker
{
    public const string BaselineRelativePath = ".harness/conventions-baseline.json";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <param name="root">The project root (where src/ and tests/ live).</param>
    /// <param name="files">Project-relative files to check; null for a full scan.</param>
    /// <param name="changedSince">Check only C# files changed since the merge base with this ref (plus untracked files).</param>
    public static ConventionReport Run(string root, IReadOnlyList<string>? files, string? changedSince = null)
    {
        root = Path.GetFullPath(root);
        var context = LoadContext(root);
        var fullScan = files is null && changedSince is null;

        var targets = files is not null ? Explicit(root, files)
            : changedSince is not null ? Changed(root, changedSince)
            : Everything(root);

        var findings = new List<Finding>();
        var applications = 0;
        foreach (var relative in targets)
        {
            var layer = LayerMap.Of(relative);
            var source = new SourceFile(relative, layer, File.ReadAllText(Path.Combine(root, relative)));
            foreach (var rule in ConventionRules.All.Where(r => r.AppliesTo(layer)))
            {
                applications++;
                foreach (var hit in rule.File!(source, context))
                {
                    if (rule.Exemptable && source.IsExempt(hit.Line))
                    {
                        continue;
                    }

                    findings.Add(new Finding(rule.Id, hit.Severity ?? rule.Severity, relative, hit.Line, rule.Why, hit.Excerpt ?? source.ExcerptAt(hit.Line)));
                }
            }
        }

        if (fullScan)
        {
            foreach (var rule in ConventionRules.All.Where(r => r.Repository is not null))
            {
                applications++;
                findings.AddRange(rule.Repository!(rule, context));
            }
        }

        var unique = findings
            .DistinctBy(f => (f.Rule, f.File, f.Line))
            .OrderBy(f => f.File, StringComparer.Ordinal)
            .ThenBy(f => f.Line)
            .ThenBy(f => f.Rule, StringComparer.Ordinal)
            .Select(f => f with { Fingerprint = Fingerprint(f) })
            .ToList();

        return new ConventionReport(ApplyBaseline(root, unique), targets.Count, applications, fullScan);
    }

    /// <summary>Records every current finding as legacy. Only a full scan can be a baseline, and secrets never are.</summary>
    public static string WriteBaseline(string root, ConventionReport report)
    {
        if (!report.FullScan)
        {
            throw new ConventionException("A baseline must come from a full scan, not from --files or --changed.");
        }

        var refused = report.Findings.Where(f => !ConventionRules.ById(f.Rule).Baselinable).ToList();
        if (refused.Count > 0)
        {
            var where = string.Join(", ", refused.Select(f => $"{f.File}:{f.Line} [{f.Rule}]"));
            throw new ConventionException($"These findings cannot be baselined and must be fixed first: {where}.");
        }

        var entries = new JsonArray();
        foreach (var f in report.Findings)
        {
            entries.Add(new JsonObject { ["rule"] = f.Rule, ["file"] = f.File, ["line"] = f.Line, ["fingerprint"] = f.Fingerprint });
        }

        var document = new JsonObject
        {
            ["note"] = "Known convention findings, reported as legacy. Fix them over time; never add new ones by hand.",
            ["entries"] = entries,
        };

        var path = Path.Combine(Path.GetFullPath(root), BaselineRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, document.ToJsonString(Indented) + Environment.NewLine);
        return path;
    }

    // ── scope ────────────────────────────────────────────────────────────

    private static List<string> Explicit(string root, IReadOnlyList<string> files)
    {
        if (files.Count == 0)
        {
            throw new ConventionException("No files were given to check.");
        }

        var result = new List<string>();
        foreach (var file in files)
        {
            var full = Path.GetFullPath(Path.Combine(root, file));
            var relative = Path.GetRelativePath(root, full).Replace('\\', '/');
            if (relative.StartsWith("../", StringComparison.Ordinal) || relative == ".." || Path.IsPathRooted(relative))
            {
                throw new ConventionException($"'{file}' is outside the project root {root}.");
            }

            if (LayerMap.Of(relative) == Layer.None)
            {
                throw new ConventionException($"'{relative}' is not a checked C# file (only .cs files under src/ or tests/, outside bin/ and obj/).");
            }

            if (!File.Exists(full))
            {
                throw new ConventionException($"'{relative}' does not exist.");
            }

            result.Add(relative);
        }

        return [.. result.Distinct(StringComparer.Ordinal)];
    }

    private static List<string> Everything(string root)
    {
        var result = new List<string>();
        foreach (var top in new[] { "src", "tests" })
        {
            var folder = Path.Combine(root, top);
            if (Directory.Exists(folder))
            {
                result.AddRange(Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                    .Where(f => LayerMap.Of(f) != Layer.None));
            }
        }

        if (result.Count == 0)
        {
            throw new ConventionException($"Found no C# files under src/ or tests/ in {root}. Nothing was checked; pass --root <project root>.");
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static List<string> Changed(string root, string baseRef)
    {
        var mergeBase = Git(root, "merge-base", baseRef, "HEAD").Trim();
        var changed = Git(root, "-c", "core.quotepath=off", "diff", "--name-only", "--relative", "--diff-filter=ACMR", mergeBase)
            + Git(root, "-c", "core.quotepath=off", "ls-files", "--others", "--exclude-standard");

        return [.. changed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => f.Replace('\\', '/'))
            .Where(f => LayerMap.Of(f) != Layer.None && File.Exists(Path.Combine(root, f)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    private static string Git(string root, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(start) ?? throw new ConventionException("Could not start git.");
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new ConventionException($"git {string.Join(' ', args)} failed: {error.Result.Trim()}");
            }

            return output;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new ConventionException("Could not run git: " + ex.Message);
        }
    }

    // ── project settings ─────────────────────────────────────────────────

    private static RuleContext LoadContext(string root)
    {
        var path = Path.Combine(root, ".harness", "project.json");
        if (!File.Exists(path))
        {
            return new RuleContext(root, null, ConventionRules.DefaultCategories);
        }

        try
        {
            var json = JsonNode.Parse(File.ReadAllText(path));
            var framework = json?["framework"]?.GetValue<string>();
            var categories = json?["conventions"]?["categories"]?.AsArray().Select(c => c!.GetValue<string>()).ToList();
            return new RuleContext(root, framework, categories is { Count: > 0 } ? categories : ConventionRules.DefaultCategories);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NullReferenceException)
        {
            throw new ConventionException($"{path} is not valid: {ex.Message}");
        }
    }

    // ── baseline ─────────────────────────────────────────────────────────

    private static string Fingerprint(Finding f)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{f.Rule}|{f.File}|{f.Excerpt.Trim()}"));
        return Convert.ToHexStringLower(bytes)[..32];
    }

    /// <summary>
    /// Marks findings that match a baseline entry as legacy. Entries match by fingerprint (rule, file and
    /// line text), not by line number, so unrelated edits that move a line keep it legacy.
    /// </summary>
    private static List<Finding> ApplyBaseline(string root, List<Finding> findings)
    {
        var path = Path.Combine(root, BaselineRelativePath);
        if (!File.Exists(path))
        {
            return findings;
        }

        var known = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            var entries = JsonNode.Parse(File.ReadAllText(path))?["entries"]?.AsArray()
                ?? throw new ConventionException($"{path} has no \"entries\" array.");
            foreach (var entry in entries)
            {
                var fingerprint = entry?["fingerprint"]?.GetValue<string>();
                if (entry?["rule"]?.GetValue<string>() is null || entry["file"]?.GetValue<string>() is null || string.IsNullOrEmpty(fingerprint))
                {
                    throw new ConventionException($"{path} has an entry without rule, file and fingerprint. Regenerate it with --write-baseline.");
                }

                known[fingerprint] = known.GetValueOrDefault(fingerprint) + 1;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new ConventionException($"{path} is not valid: {ex.Message}");
        }

        var result = new List<Finding>(findings.Count);
        foreach (var finding in findings)
        {
            if (ConventionRules.ById(finding.Rule).Baselinable && known.GetValueOrDefault(finding.Fingerprint) > 0)
            {
                known[finding.Fingerprint]--;
                result.Add(finding with { Legacy = true });
            }
            else
            {
                result.Add(finding);
            }
        }

        return result;
    }
}
