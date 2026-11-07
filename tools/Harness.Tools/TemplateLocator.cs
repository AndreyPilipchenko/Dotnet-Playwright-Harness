namespace Harness.Tools;

/// <summary>
/// Finds the templates folder by walking up from a start folder. Works for both layouts:
/// this repository (<c>templates/</c> next to <c>tools/</c>) and a consumer project
/// (<c>.harness/templates/</c> next to <c>.harness/tools/</c>).
/// </summary>
internal static class TemplateLocator
{
    public static string? Find(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "templates");
            if (Directory.Exists(Path.Combine(candidate, "build")))
            {
                return candidate;
            }
        }

        return null;
    }
}
