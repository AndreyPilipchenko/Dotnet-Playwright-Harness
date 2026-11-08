using System.Diagnostics;

namespace Harness.Tools.Tests.Scratch;

/// <summary>
/// <c>scratch-copy</c> copies a project's working files (tracked plus untracked, not ignored) to a
/// folder outside it, so a test can be broken there without touching the repository.
/// </summary>
public class ScratchCopyTests
{
    private static (int Code, Console3 Io) Run(params string[] args)
    {
        var io = new Console3();
        var code = Cli.Run(args, io.In, io.Out, io.Err);
        return (code, io);
    }

    private static void Git(string root, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)} failed: {process.StandardError.ReadToEnd()}");
    }

    /// <summary>A repository with a committed a.cs and .gitignore of bin/.</summary>
    private static TempDir Repository()
    {
        var dir = new TempDir();
        dir.Write("project/.gitignore", "bin/\n");
        dir.Write("project/src/a.cs", "class A { }\n");
        var root = dir.Combine("project");
        Git(root, "init", "--quiet");
        Git(root, "add", ".");
        Git(root, "-c", "user.name=t", "-c", "user.email=t@example.com", "commit", "--quiet", "-m", "init");
        return dir;
    }

    [Fact]
    public void CopiesTrackedAndUntrackedFiles_ButNotIgnoredOnes()
    {
        using var dir = Repository();
        dir.Write("project/src/b.cs", "class B { }\n");
        dir.Write("project/bin/x.dll", "binary");
        var target = dir.Combine("copy");

        var (code, io) = Run("scratch-copy", "--root", dir.Combine("project"), "--to", target);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.True(File.Exists(Path.Combine(target, "src", "a.cs")));
        Assert.True(File.Exists(Path.Combine(target, "src", "b.cs")));
        Assert.True(File.Exists(Path.Combine(target, ".gitignore")));
        Assert.False(Directory.Exists(Path.Combine(target, "bin")));
        Assert.False(Directory.Exists(Path.Combine(target, ".git")));
        Assert.Contains("Copied 3 files to ", io.Out.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SkipsTrackedFilesDeletedFromDisk()
    {
        using var dir = Repository();
        File.Delete(dir.Combine("project", "src", "a.cs"));
        var target = dir.Combine("copy");

        var (code, io) = Run("scratch-copy", "--root", dir.Combine("project"), "--to", target);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.False(File.Exists(Path.Combine(target, "src", "a.cs")));
        Assert.Contains("Copied 1 files to ", io.Out.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesATargetInsideTheRoot()
    {
        using var dir = Repository();
        var target = dir.Combine("project", "scratch");

        var (code, io) = Run("scratch-copy", "--root", dir.Combine("project"), "--to", target);

        Assert.Equal(ExitCodes.Usage, code);
        Assert.False(Directory.Exists(target));
        Assert.Contains("outside", io.Err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesTheRootItself()
    {
        using var dir = Repository();

        var (code, _) = Run("scratch-copy", "--root", dir.Combine("project"), "--to", dir.Combine("project"));

        Assert.Equal(ExitCodes.Usage, code);
    }

    [Fact]
    public void RefusesATargetElsewhereInTheRepository_WhenTheRootIsASubfolder()
    {
        using var dir = Repository();
        var target = dir.Combine("project", "scratch-new");

        var (code, _) = Run("scratch-copy", "--root", dir.Combine("project", "src"), "--to", target);

        Assert.Equal(ExitCodes.Usage, code);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void RefusesAnExistingTarget()
    {
        using var dir = Repository();
        var target = dir.Combine("copy");
        Directory.CreateDirectory(target);

        var (code, io) = Run("scratch-copy", "--root", dir.Combine("project"), "--to", target);

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Empty(Directory.GetFileSystemEntries(target));
        Assert.Contains("already exists", io.Err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void FailsOutsideAGitRepository()
    {
        using var dir = new TempDir();
        dir.Write("plain/a.cs", "class A { }\n");

        var (code, io) = Run("scratch-copy", "--root", dir.Combine("plain"), "--to", dir.Combine("copy"));

        Assert.Equal(ExitCodes.Failed, code);
        Assert.False(Directory.Exists(dir.Combine("copy")));
        Assert.NotEmpty(io.Err.ToString());
    }

    [Fact]
    public void RequiresTheToOption()
    {
        var (code, io) = Run("scratch-copy");

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Contains("--to", io.Err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void IsListedInTheUsage()
    {
        var (_, io) = Run("--help");

        Assert.Contains("scratch-copy", io.Out.ToString(), StringComparison.Ordinal);
    }
}
