using System.Diagnostics;

namespace Harness.Tools.Hooks;

/// <summary>Everything a hook reads from outside its input, so tests can replace it.</summary>
internal sealed record HookServices(
    Func<string, string?> GetEnv,
    Func<string, string?> CurrentBranch,
    Func<DateTimeOffset> Now,
    string? UserHome)
{
    public static HookServices System { get; } = new(
        Environment.GetEnvironmentVariable,
        Git.CurrentBranch,
        () => DateTimeOffset.UtcNow,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
}

/// <summary>Per-call context: where the project is and which session is calling.</summary>
internal sealed record HookContext(HookServices Services, string ProjectRoot, string SessionId)
{
    public HookState State { get; } = new(ProjectRoot);
}

internal static class Git
{
    /// <summary>The checked-out branch, or null when it cannot be read within a few seconds.</summary>
    public static string? CurrentBranch(string directory)
    {
        try
        {
            var start = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("-C");
            start.ArgumentList.Add(directory);
            start.ArgumentList.Add("branch");
            start.ArgumentList.Add("--show-current");

            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(3000))
            {
                process.Kill();
                return null;
            }

            var branch = output.GetAwaiter().GetResult().Trim();
            return process.ExitCode == 0 && branch.Length > 0 ? branch : null;
        }
#pragma warning disable CA1031 // No git, or not a repository: the branch is simply unknown.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }
}
