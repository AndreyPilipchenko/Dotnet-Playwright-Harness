using System.Text.Json;
using System.Text.Json.Nodes;

namespace Harness.Tools.Reports;

/// <summary>
/// Reads the JSON of <c>dotnet package list</c>. That command exits 0 with findings and 1 for reasons
/// unrelated to them, so only the JSON counts. A report that proves nothing is "not checked".
/// </summary>
internal static class PackageReportCommand
{
    public const string Usage = """
          package-report       Fail on findings in 'dotnet package list --outdated|--vulnerable --format json'
                               output. Exit 1 findings, 2 not checked:
                                 --input <file>                    (required, repeat for several)
        """;

    private sealed record Finding(string Project, string Framework, string Package, string Scope, string Current, string What);

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (!ReportOptions.TryParse(args, ["--input"], [], out var options, out var error))
        {
            stderr.WriteLine(error);
            return ExitCodes.Usage;
        }

        if (!options.TryGetValue("--input", out var inputs))
        {
            stderr.WriteLine("package-report needs --input.");
            return ExitCodes.Usage;
        }

        var findings = new List<Finding>();
        var notChecked = 0;
        foreach (var input in inputs)
        {
            if (Read(input, findings, stderr) is { } reason)
            {
                notChecked++;
                stderr.WriteLine($"Not checked: {input}: {reason}");
            }
        }

        if (findings.Count > 0)
        {
            stdout.WriteLine("| Project | Framework | Package | Scope | Current | Finding |");
            stdout.WriteLine("|---|---|---|---|---|---|");
            foreach (var f in findings)
            {
                stdout.WriteLine($"| {Markdown.Cell(f.Project)} | {Markdown.Cell(f.Framework)} | {Markdown.Cell(f.Package)} | {f.Scope} | {Markdown.Cell(f.Current)} | {Markdown.Cell(f.What)} |");
            }

            stdout.WriteLine();
            stdout.WriteLine($"{findings.Count} finding(s) in {inputs.Count} report(s).");
            return ExitCodes.Failed;
        }

        if (notChecked > 0)
        {
            return ExitCodes.NotChecked;
        }

        stdout.WriteLine($"No findings in {inputs.Count} report(s).");
        return ExitCodes.Ok;
    }

    /// <summary>Adds the report's findings. Returns why it proves nothing, or null when it was read.</summary>
    private static string? Read(string input, List<Finding> findings, TextWriter stderr)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(File.ReadAllText(input));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return "it is missing or is not JSON, so the listing probably failed.";
        }

        if (parsed is not JsonObject root || root["projects"] is not JsonArray projects)
        {
            return "it is not 'dotnet package list' JSON.";
        }

        var parameters = Text(root["parameters"]);
        if (!parameters.Contains("--outdated", StringComparison.Ordinal) && !parameters.Contains("--vulnerable", StringComparison.Ordinal))
        {
            return "it is not a list made with --outdated or --vulnerable.";
        }

        var failedProblems = 0;
        foreach (var problem in (root["problems"] as JsonArray ?? []).OfType<JsonObject>())
        {
            stderr.WriteLine($"{Text(problem["level"])}: {Text(problem["text"])}");
            if (string.Equals(Text(problem["level"]), "error", StringComparison.OrdinalIgnoreCase))
            {
                failedProblems++;
            }
        }

        if (failedProblems > 0)
        {
            return $"the listing reported {failedProblems} error(s).";
        }

        if (projects.Count == 0)
        {
            return "it lists no projects.";
        }

        foreach (var project in projects.OfType<JsonObject>())
        {
            var name = Path.GetFileName(Text(project["path"]));
            foreach (var framework in (project["frameworks"] as JsonArray ?? []).OfType<JsonObject>())
            {
                AddPackages(framework["topLevelPackages"], "direct", name, Text(framework["framework"]), findings);
                AddPackages(framework["transitivePackages"], "transitive", name, Text(framework["framework"]), findings);
            }
        }

        return null;
    }

    private static void AddPackages(JsonNode? packages, string scope, string project, string framework, List<Finding> findings)
    {
        foreach (var package in (packages as JsonArray ?? []).OfType<JsonObject>())
        {
            var current = Text(package["resolvedVersion"]);
            var vulnerabilities = (package["vulnerabilities"] as JsonArray ?? []).OfType<JsonObject>().ToList();
            var what = vulnerabilities.Count > 0
                ? vulnerabilities.Select(v => $"vulnerable: {Text(v["severity"])} {Text(v["advisoryurl"])}")
                : [$"outdated: latest {Text(package["latestVersion"])}"];

            findings.AddRange(what.Select(w => new Finding(project, framework, Text(package["id"]), scope, current, w)));
        }
    }

    private static string Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;
}
