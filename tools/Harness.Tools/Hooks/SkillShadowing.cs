namespace Harness.Tools.Hooks;

/// <summary>
/// Claude Code resolves a skill by name, and a user-level skill (<c>~/.claude/skills/&lt;name&gt;</c>)
/// wins over the project's. When the two differ, invoking the skill silently runs the user copy.
/// The idea comes from claude-code-harness (MIT); this is a C# rewrite.
/// </summary>
internal static class SkillShadowing
{
    public static string? Warning(string projectSkills, string userSkills)
    {
        var projectNames = SkillNames(projectSkills);
        if (projectNames.Count == 0)
        {
            return null;
        }

        var userNames = SkillNames(userSkills);
        var shadowed = projectNames.Where(userNames.Contains).Order(StringComparer.Ordinal).ToList();
        var divergent = shadowed
            .Where(name => !SameContent(Path.Combine(projectSkills, name, "SKILL.md"), Path.Combine(userSkills, name, "SKILL.md")))
            .ToList();
        if (divergent.Count == 0)
        {
            return null;
        }

        var identical = shadowed.Count - divergent.Count;
        return "Skill shadowing: these user-level skills in ~/.claude/skills differ from this project's and take precedence over them: "
            + string.Join(", ", divergent)
            + ". Invoking one runs the user copy, not the project copy. Update or remove the user copies, or read the project SKILL.md directly."
            + (identical > 0 ? $" ({identical} more shadowed skill(s) are identical, which is harmless.)" : string.Empty);
    }

    private static HashSet<string> SkillNames(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory)
                .Where(d => File.Exists(Path.Combine(d, "SKILL.md")))
                .Select(Path.GetFileName)
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Unreadable files count as different: a missed warning costs more than an extra one.</summary>
    private static bool SameContent(string a, string b)
    {
        try
        {
            return File.ReadAllText(a) == File.ReadAllText(b);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
