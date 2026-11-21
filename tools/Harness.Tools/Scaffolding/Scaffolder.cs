using System.Text;
using System.Text.RegularExpressions;

namespace Harness.Tools.Scaffolding;

internal sealed record ScaffoldOptions(
    string Framework,
    string Name,
    string TargetFramework,
    string OutputDirectory,
    string TemplatesDirectory)
{
    /// <summary>
    /// Render just this file: its output path, or the end of it in whole segments (<c>Setup/ApiTest.cs</c>).
    /// Lets an installed project add a file that a newer harness introduced.
    /// </summary>
    public string? Only { get; init; }
}

/// <summary>A problem the user or the template author must fix. Nothing was written.</summary>
internal sealed class ScaffoldException(string message) : Exception(message);

/// <summary>
/// Renders the walking skeleton from three template layers: <c>build</c> (repo root files),
/// <c>framework</c> (the framework-neutral project) and the chosen test framework.
/// Path segments may contain <c>__Name__</c>; a file or folder name starting with <c>dot.</c>
/// gets a leading dot instead. File contents may contain <c>{{Name}}</c>, <c>{{TargetFramework}}</c> and
/// <c>{{Framework}}</c>. Everything is rendered in memory and checked before the first write,
/// and existing files are never overwritten.
/// </summary>
internal static partial class Scaffolder
{
    public static readonly IReadOnlyList<string> Frameworks = ["nunit", "xunit", "mstest"];

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Checks the project settings shared by scaffold and install; returns the framework in lower case.</summary>
    public static string Validate(string framework, string name, string targetFramework)
    {
        var normalized = framework.ToLowerInvariant();
        if (!Frameworks.Contains(normalized))
        {
            throw new ScaffoldException(
                $"Unknown framework '{framework}'. Use one of: {string.Join(", ", Frameworks)}.");
        }

        if (!NamePattern().IsMatch(name))
        {
            throw new ScaffoldException(
                $"'{name}' is not a valid project name. Use dotted C# identifiers, for example Acme.Shop.");
        }

        if (!TargetFrameworkPattern().IsMatch(targetFramework))
        {
            throw new ScaffoldException(
                $"'{targetFramework}' is not a supported target framework. Use the form net10.0.");
        }

        return normalized;
    }

    public static IReadOnlyList<string> Run(ScaffoldOptions options)
    {
        var framework = Validate(options.Framework, options.Name, options.TargetFramework);

        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Name"] = options.Name,
            ["TargetFramework"] = options.TargetFramework,
            ["Framework"] = framework,
        };

        var plan = Render(options.TemplatesDirectory, ["build", "framework", framework], tokens);
        return Write(options.Only is { } only ? Select(plan, only) : plan, options.OutputDirectory);
    }

    private static SortedDictionary<string, string> Select(SortedDictionary<string, string> plan, string only)
    {
        var wanted = only.Replace('\\', '/').Trim('/');
        var matches = plan
            .Where(p => p.Key.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                || p.Key.EndsWith("/" + wanted, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count != 1)
        {
            throw new ScaffoldException(matches.Count == 0
                ? $"No template renders '{only}'. Give the file's path in the project, for example Setup/ApiTest.cs."
                : $"'{only}' matches more than one file: {string.Join(", ", matches.Select(m => m.Key))}. Give more of the path.");
        }

        return new SortedDictionary<string, string>(matches.ToDictionary(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Renders the GitHub Actions templates. Separate from the skeleton: a workflow runs as soon as it is pushed.</summary>
    public static IReadOnlyList<string> RunCi(ScaffoldOptions options)
    {
        var framework = Validate(options.Framework, options.Name, options.TargetFramework);
        var xunit = framework == "xunit";

        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Name"] = options.Name,
            ["TargetFramework"] = options.TargetFramework,
            ["Framework"] = framework,
            ["FilterOption"] = xunit ? "--filter-trait" : "--filter",
            ["SmokeFilter"] = xunit ? "Category=smoke" : "TestCategory=smoke",
        };

        return Write(Render(options.TemplatesDirectory, ["ci"], tokens), options.OutputDirectory);
    }

    private static IReadOnlyList<string> Write(SortedDictionary<string, string> plan, string outputDirectory)
    {
        var conflicts = plan.Keys
            .Where(rel => Path.Exists(Path.Combine(outputDirectory, rel)))
            .ToList();
        if (conflicts.Count > 0)
        {
            throw new ScaffoldException(
                "These files already exist, so nothing was written: " + string.Join(", ", conflicts));
        }

        foreach (var (rel, content) in plan)
        {
            var target = Path.Combine(outputDirectory, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content, Utf8NoBom);
        }

        return [.. plan.Keys];
    }

    private static SortedDictionary<string, string> Render(
        string templatesDirectory,
        string[] layers,
        Dictionary<string, string> tokens)
    {
        var plan = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        foreach (var layer in layers)
        {
            var layerDir = Path.Combine(templatesDirectory, layer);
            if (!Directory.Exists(layerDir))
            {
                throw new ScaffoldException($"Template layer '{layer}' not found in {templatesDirectory}.");
            }

            foreach (var file in Directory.GetFiles(layerDir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var rel = MapPath(Path.GetRelativePath(layerDir, file), tokens["Name"]);
                if (!plan.TryAdd(rel, string.Empty))
                {
                    throw new ScaffoldException($"Template path '{rel}' appears in more than one layer.");
                }

                plan[rel] = Placeholder().Replace(File.ReadAllText(file), match =>
                {
                    if (tokens.TryGetValue(match.Groups[1].Value, out var value))
                    {
                        return value;
                    }

                    errors.Add($"{layer}/{Path.GetRelativePath(layerDir, file).Replace('\\', '/')}: {match.Value}");
                    return match.Value;
                });
            }
        }

        if (errors.Count > 0)
        {
            throw new ScaffoldException("Unknown placeholders in templates: " + string.Join("; ", errors));
        }

        return plan;
    }

    private static string MapPath(string relative, string name)
    {
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Select(s => s.Replace("__Name__", name, StringComparison.Ordinal))
            .Select(s => s.StartsWith("dot.", StringComparison.Ordinal) ? "." + s["dot.".Length..] : s);

        return string.Join('/', segments);
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^net\d+\.\d+$")]
    private static partial Regex TargetFrameworkPattern();

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex Placeholder();
}
