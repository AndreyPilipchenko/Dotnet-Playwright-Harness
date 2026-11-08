using System.Diagnostics;

namespace Harness.Tools.Scratch;

/// <summary>
/// Copies a git project's working files (tracked plus untracked, not ignored) to another folder.
/// Nothing under <c>.git/</c> is read or written except through <c>git ls-files</c>.
/// </summary>
internal static class ScratchCopy
{
    /// <summary>The project-relative paths git lists as tracked or untracked-but-not-ignored.</summary>
    public static IReadOnlyList<string> WorkingFiles(string root)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "-C", root, "ls-files", "-z", "--cached", "--others", "--exclude-standard" })
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start git.");
        var stderr = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git ls-files failed in {root}: {stderr.Result.Trim()}");
        }

        return [.. output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The top folder of the git repository that contains <paramref name="root"/>, or null when git
    /// can't say (not a repository, git missing). A root that is a subfolder of a repository still
    /// has the whole repository as its top level, which a copy must not be written into.
    /// </summary>
    public static string? TopLevel(string root)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "-C", root, "rev-parse", "--show-toplevel" })
        {
            start.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var stderr = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            _ = stderr.Result;
            return process.ExitCode == 0 && output.Length > 0 ? Path.GetFullPath(output) : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Copies the working files of <paramref name="root"/> to <paramref name="target"/>; returns the count.</summary>
    public static int Copy(string root, string target)
    {
        var files = WorkingFiles(root);
        Directory.CreateDirectory(target);
        var copied = 0;
        foreach (var relative in files)
        {
            var source = Path.Combine(root, relative);
            if (!File.Exists(source))
            {
                continue;
            }

            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
            copied++;
        }

        return copied;
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="root"/> or inside it.</summary>
    public static bool IsInside(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var full = WithSeparator(Path.GetFullPath(path));
        return full.StartsWith(WithSeparator(Path.GetFullPath(root)), comparison);
    }

    private static string WithSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
}
