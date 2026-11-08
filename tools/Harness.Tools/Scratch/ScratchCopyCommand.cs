namespace Harness.Tools.Scratch;

/// <summary>The <c>scratch-copy</c> command line.</summary>
internal static class ScratchCopyCommand
{
    public const string Usage = """
          scratch-copy         Copy the project's working files (tracked and untracked, not ignored) to a
                               new folder outside it, for example to break a test once without touching the repo:
                                 --to <dir>            the new folder (required; must not exist, must be outside --root and its git repository)
                                 --root <dir>          project root, a git repository (default: current directory)
                               Exit: 0 copied, 1 git failed or not a repository, 64 bad arguments.
        """;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var root = Directory.GetCurrentDirectory();
        string? target = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--root" when i + 1 < args.Length:
                    root = args[++i];
                    break;
                case "--to" when i + 1 < args.Length:
                    target = args[++i];
                    break;
                default:
                    stderr.WriteLine($"Unknown or incomplete option '{args[i]}'.");
                    stderr.WriteLine(Usage);
                    return ExitCodes.Usage;
            }
        }

        if (target is null)
        {
            stderr.WriteLine("Pass --to <dir>, the new folder to copy into.");
            return ExitCodes.Usage;
        }

        root = Path.GetFullPath(root);
        target = Path.GetFullPath(target);
        if (ScratchCopy.IsInside(target, root))
        {
            stderr.WriteLine($"--to must be outside the project root {root}.");
            return ExitCodes.Usage;
        }

        if (ScratchCopy.TopLevel(root) is { } repository && ScratchCopy.IsInside(target, repository))
        {
            stderr.WriteLine($"--to must be outside the repository {repository}.");
            return ExitCodes.Usage;
        }

        if (Directory.Exists(target) || File.Exists(target))
        {
            stderr.WriteLine($"{target} already exists. Pass a new folder.");
            return ExitCodes.Usage;
        }

        try
        {
            var count = ScratchCopy.Copy(root, target);
            stdout.WriteLine($"Copied {count} files to {target}.");
            return ExitCodes.Ok;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            stderr.WriteLine(ex.Message);
            return ExitCodes.Failed;
        }
    }
}
