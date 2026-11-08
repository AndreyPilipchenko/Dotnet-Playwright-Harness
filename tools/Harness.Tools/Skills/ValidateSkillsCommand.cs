using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Harness.Tools.Conventions;

namespace Harness.Tools.Skills;

/// <summary>
/// Checks the skills and agents: frontmatter, links, the routing table, and terms left over from
/// the TypeScript and Azure DevOps sources. In a project, only files the harness installed count.
/// </summary>
internal static partial class ValidateSkillsCommand
{
    public const string Usage = """
          validate-skills      Check the skills and agents: frontmatter, links, routing, leftover terms:
                                 --root <dir>          project root (default: current directory)
                               Exit: 0 clean, 1 failures, 2 no skills found, 64 bad arguments.
        """;

    private const string Router = "pom-architecture";

    /// <summary>Past this many estimated tokens a skill costs more than it tends to give back.</summary>
    private const int SizeWarningTokens = 4000;

    private const int CharactersPerToken = 4;

    private static readonly (string Term, Regex Pattern)[] Leftovers =
    [
        (".spec.ts", SpecFile()),
        ("test.describe", TestDescribe()),
        ("npx", Npx()),
        ("ADO", Ado()),
        ("Azure DevOps", AzureDevOps()),
        ("{{", Mustache()),
    ];

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var root = Directory.GetCurrentDirectory();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--root" && i + 1 < args.Length)
            {
                root = args[++i];
            }
            else
            {
                stderr.WriteLine($"Unknown or incomplete option '{args[i]}'.");
                stderr.WriteLine(Usage);
                return ExitCodes.Usage;
            }
        }

        var payload = Payload.Read(root);
        if (payload.Skills.Count == 0)
        {
            stderr.WriteLine($"validate-skills: No skills found under {Path.Combine(root, ".claude", "skills")}.");
            return ExitCodes.NotChecked;
        }

        var findings = Check(payload);
        foreach (var finding in findings)
        {
            stdout.WriteLine($"{(finding.Severity == Severity.Fail ? "FAIL" : "WARN")}  {finding.File}:{finding.Line}  [{finding.Rule}] {finding.Why}");
            stdout.WriteLine($"      {finding.Excerpt}");
        }

        var failures = findings.Count(f => f.Severity == Severity.Fail);
        stdout.WriteLine($"Checked {payload.Skills.Count} skills and {payload.Agents.Count} agents: {failures} failures, {findings.Count - failures} warnings.");
        return failures > 0 ? ExitCodes.Failed : ExitCodes.Ok;
    }

    private static Finding Fail(string rule, string file, int line, string why, string excerpt) =>
        new(rule, Severity.Fail, file, line, why, excerpt);

    private static List<Finding> Check(Payload payload)
    {
        var findings = new List<Finding>();
        foreach (var skill in payload.Skills)
        {
            CheckSkill(payload, skill, findings);
        }

        foreach (var agent in payload.Agents)
        {
            CheckAgent(payload, agent, findings);
        }

        CheckRouterAgents(payload, findings);

        foreach (var file in payload.Documents)
        {
            CheckDocument(payload, file, findings);
        }

        return [.. findings.OrderBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line)];
    }

    private static void CheckSkill(Payload payload, string skill, List<Finding> findings)
    {
        const string Why = "A skill needs SKILL.md with frontmatter: its folder name and a 'Use when' description.";
        var relative = $".claude/skills/{skill}/SKILL.md";
        var path = Path.Combine(payload.Root, relative);
        if (!File.Exists(path))
        {
            findings.Add(Fail("frontmatter", relative, 1, Why, $"{skill} has no SKILL.md."));
            return;
        }

        var text = File.ReadAllText(path);
        var frontmatter = Frontmatter().Match(text);
        if (!frontmatter.Success)
        {
            findings.Add(Fail("frontmatter", relative, 1, Why, "There is no frontmatter."));
        }
        else
        {
            if (Field(frontmatter.Value, "name") != skill)
            {
                findings.Add(Fail("frontmatter", relative, 2, Why, $"name must be {skill}."));
            }

            var description = Field(frontmatter.Value, "description");
            if (description is null || !UseWhen().IsMatch(description))
            {
                findings.Add(Fail("frontmatter", relative, 3, Why, "The description must start with 'Use when' and say enough to route on."));
            }

            AddDescriptionTag(description, relative, findings);
        }

        if (skill != Router && !IsRouted(payload, skill))
        {
            findings.Add(Fail("not-routed", relative, 1, "pom-architecture must route to every skill, so a task reaches it.", $"{skill} has no row in the routing tables."));
        }

        if (skill == Router || payload.RouterText is not null)
        {
            var tokens = text.Length / CharactersPerToken;
            if (tokens > SizeWarningTokens)
            {
                findings.Add(new Finding("size", Severity.Warn, relative, 1, $"SKILL.md is over about {SizeWarningTokens} tokens; move detail into references/.", $"About {tokens} tokens."));
            }
        }

        if (payload.RouterText is null && payload.Skills[0] == skill)
        {
            findings.Add(Fail("not-routed", $".claude/skills/{Router}/SKILL.md", 1, "pom-architecture must route to every skill, so a task reaches it.", $"{Router} is missing."));
        }
    }

    private static bool IsRouted(Payload payload, string skill) =>
        payload.RouterText is { } router && Regex.IsMatch(router, $@"(?m)^\|.*\| {Regex.Escape(skill)} \|\s*$");

    private static void CheckAgent(Payload payload, string agent, List<Finding> findings)
    {
        const string Why = "An agent needs frontmatter: its file name, a description and its tools.";
        var relative = $".claude/agents/{agent}.md";
        var frontmatter = Frontmatter().Match(File.ReadAllText(Path.Combine(payload.Root, relative)));
        if (!frontmatter.Success)
        {
            findings.Add(Fail("frontmatter", relative, 1, Why, "There is no frontmatter."));
        }
        else
        {
            var description = Field(frontmatter.Value, "description");
            if (Field(frontmatter.Value, "name") != agent)
            {
                findings.Add(Fail("frontmatter", relative, 2, Why, $"name must be {agent}."));
            }

            if (description is null || description.Length < 40)
            {
                findings.Add(Fail("frontmatter", relative, 3, Why, "The description is missing or too short to route on."));
            }

            if (string.IsNullOrEmpty(Field(frontmatter.Value, "tools")))
            {
                findings.Add(Fail("frontmatter", relative, 1, Why, "tools is missing."));
            }

            AddDescriptionTag(description, relative, findings);
        }

        var mention = $"`{agent}`";
        var named = payload.Documents.Where(d => d.StartsWith(".claude/skills/", StringComparison.Ordinal))
            .Any(d => File.ReadAllText(Path.Combine(payload.Root, d)).Contains(mention, StringComparison.Ordinal));
        if (!named)
        {
            findings.Add(Fail("agent-unnamed", relative, 1, "A skill must say when to use each agent.", $"No skill names {mention}."));
        }
    }

    private static void AddDescriptionTag(string? description, string file, List<Finding> findings)
    {
        if (description is null)
        {
            return;
        }

        if (DescriptionTag().Match(description) is { Success: true } tag)
        {
            findings.Add(Fail("description-tag", file, 3, "Skill surfaces can reject a description holding XML-like tags.", tag.Value));
        }

        var quoted = description.StartsWith('"') || description.StartsWith('\'');
        if (!quoted && (description.Contains(": ", StringComparison.Ordinal) || description.Contains(" #", StringComparison.Ordinal)))
        {
            findings.Add(Fail("description-yaml", file, 3, "A colon and space, or a space and #, breaks an unquoted YAML value, and the description is lost.", "Reword it, or quote the whole value."));
        }
    }

    private static void CheckRouterAgents(Payload payload, List<Finding> findings)
    {
        if (payload.RouterText is null || !payload.Skills.Contains(Router, StringComparer.Ordinal))
        {
            return;
        }

        var section = RouterAgents().Match(payload.RouterText).Groups[1].Value;
        foreach (Match row in AgentColumn().Matches(section))
        {
            var name = row.Groups[1].Value;
            if (name != "general-purpose" && !File.Exists(Path.Combine(payload.Root, ".claude", "agents", name + ".md")))
            {
                findings.Add(Fail("agent-table", $".claude/skills/{Router}/SKILL.md", 1, "The Agents table must name only agents that exist.", $"`{name}` is not in .claude/agents."));
            }
        }
    }

    private static void CheckDocument(Payload payload, string relative, List<Finding> findings)
    {
        var path = Path.Combine(payload.Root, relative);
        var lines = File.ReadAllLines(path);
        var fenced = false;
        for (var i = 0; i < lines.Length; i++)
        {
            foreach (var (term, pattern) in Leftovers)
            {
                if (pattern.IsMatch(lines[i]) && !IsAllowedNpx(term, lines[i]))
                {
                    findings.Add(Fail("leftover", relative, i + 1, "This term comes from the TypeScript or Azure DevOps sources.", term));
                }
            }

            if (IsFence(lines[i]))
            {
                fenced = !fenced;
                continue;
            }

            if (!fenced)
            {
                foreach (Match link in Link().Matches(InlineCode().Replace(lines[i], string.Empty)))
                {
                    CheckLink(path, relative, i + 1, link.Groups[1].Value, findings);
                }
            }
        }
    }

    private static bool IsAllowedNpx(string term, string line) =>
        term == "npx"
        && (line.Contains("mcp", StringComparison.OrdinalIgnoreCase) || line.Contains("@playwright/cli", StringComparison.Ordinal));

    private static bool IsFence(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal);
    }

    private static void CheckLink(string path, string relative, int line, string target, List<Finding> findings)
    {
        // A '<' means a placeholder in a template, filled in when the record is written.
        if (target.Contains("://", StringComparison.Ordinal) || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || target.Contains('<', StringComparison.Ordinal))
        {
            return;
        }

        const string Why = "A link must point at a file and heading that exist.";
        var hash = target.IndexOf('#', StringComparison.Ordinal);
        var file = hash >= 0 ? target[..hash] : target;
        var anchor = hash >= 0 ? target[(hash + 1)..] : null;

        var resolved = file.Length == 0 ? path : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, Uri.UnescapeDataString(file)));
        if (!Path.Exists(resolved))
        {
            findings.Add(Fail("broken-link", relative, line, Why, target));
            return;
        }

        if (anchor is not null && resolved.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !Anchors(resolved).Contains(anchor))
        {
            findings.Add(Fail("broken-link", relative, line, Why, target));
        }
    }

    /// <summary>The heading anchors of a Markdown file, as GitHub makes them.</summary>
    private static HashSet<string> Anchors(string path)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var fenced = false;
        foreach (var line in File.ReadAllLines(path))
        {
            if (IsFence(line))
            {
                fenced = !fenced;
                continue;
            }

            if (fenced || Heading().Match(line) is not { Success: true } heading)
            {
                continue;
            }

            var slug = NotSlug().Replace(heading.Groups[1].Value.Trim().Replace("`", string.Empty, StringComparison.Ordinal).ToLowerInvariant(), string.Empty).Replace(' ', '-');
            var count = seen.GetValueOrDefault(slug);
            seen[slug] = count + 1;
            anchors.Add(count == 0 ? slug : $"{slug}-{count}");
        }

        return anchors;
    }

    private static string? Field(string frontmatter, string name)
    {
        var match = Regex.Match(frontmatter, $@"(?m)^{Regex.Escape(name)}:[ \t]*(.*?)\s*$");
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"\.spec\.ts\b")]
    private static partial Regex SpecFile();

    [GeneratedRegex(@"\btest\.describe\b")]
    private static partial Regex TestDescribe();

    [GeneratedRegex(@"\bnpx\s")]
    private static partial Regex Npx();

    [GeneratedRegex(@"\bADO\b(?!\.NET)")]
    private static partial Regex Ado();

    [GeneratedRegex(@"\bAzure DevOps\b")]
    private static partial Regex AzureDevOps();

    [GeneratedRegex(@"(?<!\$)\{\{")]
    private static partial Regex Mustache();

    [GeneratedRegex(@"\A---\r?\n(.*?)\r?\n---\r?\n", RegexOptions.Singleline)]
    private static partial Regex Frontmatter();

    [GeneratedRegex(@"^Use when .{40,}$")]
    private static partial Regex UseWhen();

    [GeneratedRegex(@"</?[A-Za-z][^>]*>")]
    private static partial Regex DescriptionTag();

    [GeneratedRegex(@"(?ms)^## Agents\s*$(.*?)^## ")]
    private static partial Regex RouterAgents();

    [GeneratedRegex(@"(?m)^\|[^|\n]+\| `([a-z-]+)`")]
    private static partial Regex AgentColumn();

    [GeneratedRegex(@"!?\[[^\]]*\]\(([^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"`[^`]*`")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"^#{1,6}\s+(.*?)\s*#*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"[^\p{L}\p{N} _-]")]
    private static partial Regex NotSlug();

    /// <summary>The skills and agents to check. A manifest means a project: only what the harness installed counts.</summary>
    private sealed record Payload(string Root, IReadOnlyList<string> Skills, IReadOnlyList<string> Agents, IReadOnlyList<string> Documents, string? RouterText)
    {
        public static Payload Read(string root)
        {
            var installed = Installed(root);
            bool InScope(string relative) => installed is null || installed.Contains(relative);

            var skillsRoot = Path.Combine(root, ".claude", "skills");
            var skills = Directory.Exists(skillsRoot)
                ? Directory.GetDirectories(skillsRoot).Select(d => Path.GetFileName(d)!).Where(s => InScope($".claude/skills/{s}/SKILL.md")).Order(StringComparer.Ordinal).ToList()
                : [];

            var agentsRoot = Path.Combine(root, ".claude", "agents");
            var agents = Directory.Exists(agentsRoot)
                ? Directory.GetFiles(agentsRoot, "*.md").Select(f => Path.GetFileNameWithoutExtension(f)!).Where(a => InScope($".claude/agents/{a}.md")).Order(StringComparer.Ordinal).ToList()
                : [];

            var documents = skills
                .SelectMany(s => Directory.GetFiles(Path.Combine(skillsRoot, s), "*.md", SearchOption.AllDirectories))
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                .Concat(agents.Select(a => $".claude/agents/{a}.md"))
                .Order(StringComparer.Ordinal)
                .ToList();

            var router = Path.Combine(skillsRoot, Router, "SKILL.md");
            return new Payload(root, skills, agents, documents, File.Exists(router) ? File.ReadAllText(router) : null);
        }

        private static HashSet<string>? Installed(string root)
        {
            var path = Path.Combine(root, ".harness", "manifest.json");
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonNode.Parse(File.ReadAllText(path))?["files"] is JsonObject files
                    ? [.. files.Select(f => f.Key)]
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
