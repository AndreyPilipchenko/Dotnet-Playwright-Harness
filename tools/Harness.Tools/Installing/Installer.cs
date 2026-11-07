using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harness.Tools.Scaffolding;

namespace Harness.Tools.Installing;

/// <param name="Root">The consumer project root.</param>
/// <param name="Framework">nunit, xunit or mstest; null to keep the one in project.json.</param>
/// <param name="Name">Project name; null to keep project.json's, or derive one from the folder name.</param>
/// <param name="TargetFramework">null to keep project.json's, or net10.0.</param>
/// <param name="SettingsTemplate">The harness hook and permission block (templates/settings.harness.json).</param>
internal sealed record InstallOptions(string Root, string? Framework, string? Name, string? TargetFramework, string SettingsTemplate);

internal sealed record InstallResult(string ProjectJson, string Settings, int HarnessHandlers, int UserHandlers);

internal sealed class InstallException(string message) : Exception(message);

/// <summary>
/// Records the project settings in <c>.harness/project.json</c> and merges the harness hooks and
/// deny rules into <c>.claude/settings.json</c>. Safe to re-run: harness-owned hook handlers (those
/// that run Harness.Tools.dll) are replaced, everything else is kept, and nothing is written when an
/// input is invalid.
/// </summary>
internal static partial class Installer
{
    public const string DefaultTargetFramework = "net10.0";
    private const string HarnessMarker = "Harness.Tools.dll";

    private static readonly string[] GitignoreLines = ["tools/bin/", "tools/**/bin/", "tools/**/obj/", "state/"];
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static InstallResult Run(InstallOptions options)
    {
        var root = Path.GetFullPath(options.Root);
        var projectPath = Path.Combine(root, ".harness", "project.json");
        var settingsPath = Path.Combine(root, ".claude", "settings.json");

        // Read and validate everything before writing anything.
        var project = ReadObject(projectPath) ?? [];
        var recorded = project["framework"]?.GetValue<string>();
        var framework = options.Framework ?? recorded
            ?? throw new InstallException("No framework given and none recorded in .harness/project.json. Pass nunit, xunit or mstest.");
        if (recorded is not null && options.Framework is not null && !string.Equals(recorded, options.Framework, StringComparison.OrdinalIgnoreCase))
        {
            throw new InstallException(
                $".harness/project.json records the framework '{recorded}'. Re-run with that framework; switching frameworks is a migration, not a setup option.");
        }

        var name = options.Name ?? project["name"]?.GetValue<string>() ?? DefaultName(Path.GetFileName(root));
        var tfm = options.TargetFramework ?? project["targetFramework"]?.GetValue<string>() ?? DefaultTargetFramework;
        try
        {
            framework = Scaffolder.Validate(framework, name, tfm);
        }
        catch (ScaffoldException ex)
        {
            throw new InstallException(ex.Message);
        }

        var template = ReadObject(options.SettingsTemplate)
            ?? throw new InstallException($"The harness settings template {options.SettingsTemplate} does not exist.");
        var settings = ReadObject(settingsPath) ?? [];
        var userHandlers = Merge(settings, template);

        project["framework"] = framework;
        project["name"] = name;
        project["targetFramework"] = tfm;

        Write(projectPath, project.ToJsonString(Indented));
        Write(settingsPath, settings.ToJsonString(Indented));
        UpdateGitignore(Path.Combine(root, ".harness", ".gitignore"));

        return new InstallResult(projectPath, settingsPath, CountHandlers(template), userHandlers);
    }

    /// <summary>A valid dotted C# name from a folder name: <c>my-shop.e2e</c> becomes <c>MyShop.E2e</c>.</summary>
    public static string DefaultName(string folder)
    {
        var segments = folder.Split('.')
            .Select(segment => string.Concat(NonWordCharacters().Split(segment)
                .Where(w => w.Length > 0)
                .Select(w => char.ToUpperInvariant(w[0]) + w[1..])))
            .Where(s => s.Length > 0)
            .Select(s => char.IsAsciiDigit(s[0]) ? "_" + s : s)
            .ToList();
        return segments.Count == 0 ? "Tests" : string.Join('.', segments);
    }

    /// <summary>Removes harness handlers from <paramref name="settings"/>, then adds the template's. Returns the user handler count.</summary>
    private static int Merge(JsonObject settings, JsonObject template)
    {
        var hooks = settings["hooks"] as JsonObject;
        if (settings["hooks"] is not null && hooks is null)
        {
            throw new InstallException(".claude/settings.json has a \"hooks\" value that is not an object.");
        }

        hooks ??= [];
        foreach (var eventName in hooks.Select(e => e.Key).ToList())
        {
            if (hooks[eventName] is not JsonArray groups)
            {
                continue;
            }

            foreach (var group in groups.OfType<JsonObject>().ToList())
            {
                if (group["hooks"] is JsonArray handlers)
                {
                    foreach (var handler in handlers.Where(IsHarness).ToList())
                    {
                        handlers.Remove(handler);
                    }

                    if (handlers.Count == 0)
                    {
                        groups.Remove(group);
                    }
                }
            }

            if (groups.Count == 0)
            {
                hooks.Remove(eventName);
            }
        }

        var userHandlers = CountHandlers(hooks);
        foreach (var (eventName, groups) in template["hooks"]?.AsObject() ?? [])
        {
            if (hooks[eventName] is not JsonArray target)
            {
                target = [];
                hooks[eventName] = target;
            }

            foreach (var group in groups!.AsArray())
            {
                target.Add(group!.DeepClone());
            }
        }

        if (hooks.Count == 0)
        {
            settings.Remove("hooks");
        }
        else if (hooks.Parent is null)
        {
            settings["hooks"] = hooks;
        }

        MergeDeny(settings, template);
        return userHandlers;
    }

    private static void MergeDeny(JsonObject settings, JsonObject template)
    {
        var wanted = template["permissions"]?["deny"]?.AsArray().Select(d => d!.GetValue<string>()).ToList() ?? [];
        if (wanted.Count == 0)
        {
            return;
        }

        if (settings["permissions"] is not JsonObject permissions)
        {
            if (settings["permissions"] is not null)
            {
                throw new InstallException(".claude/settings.json has a \"permissions\" value that is not an object.");
            }

            permissions = [];
            settings["permissions"] = permissions;
        }

        if (permissions["deny"] is not JsonArray deny)
        {
            deny = [];
            permissions["deny"] = deny;
        }

        var existing = deny.Select(d => d?.ToString()).ToHashSet(StringComparer.Ordinal);
        foreach (var rule in wanted.Where(existing.Add))
        {
            deny.Add(rule);
        }
    }

    private static bool IsHarness(JsonNode? handler) =>
        handler is JsonObject && handler.ToJsonString().Contains(HarnessMarker, StringComparison.Ordinal);

    private static int CountHandlers(JsonObject? container)
    {
        var hooks = container?["hooks"] as JsonObject ?? container;
        return hooks?.Sum(e => (e.Value as JsonArray)?.OfType<JsonObject>().Sum(g => (g["hooks"] as JsonArray)?.Count ?? 0) ?? 0) ?? 0;
    }

    private static void UpdateGitignore(string path)
    {
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        var missing = GitignoreLines.Where(l => !lines.Contains(l, StringComparer.Ordinal)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        if (lines.Count == 0)
        {
            lines.Add("# Harness build output and local state. The tool source under tools/ is committed.");
        }

        lines.AddRange(missing);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines, Utf8NoBom);
    }

    private static JsonObject? ReadObject(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path), documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                ?? throw new InstallException($"{path} does not contain a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new InstallException($"{path} is not valid JSON ({ex.Message}). Fix it by hand; it was not changed.");
        }
    }

    private static void Write(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json + Environment.NewLine, Utf8NoBom);
    }

    [System.Text.RegularExpressions.GeneratedRegex("[^A-Za-z0-9_]+")]
    private static partial System.Text.RegularExpressions.Regex NonWordCharacters();
}
