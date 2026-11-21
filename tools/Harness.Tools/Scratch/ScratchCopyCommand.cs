namespace Harness.Tools.Scratch;

/// <summary>The <c>scratch-copy</c> command line.</summary>
internal static class ScratchCopyCommand
{
    public const string Usage = """
          scratch-copy         Copy the project's working files (tracked and untracked, not ignored) to a
                               new folder outside it, for example to break a test once without touching the repo:
                                 --to <dir>            the new folder (required; must not exist, must be outside --root and its git repository)
                                 --root <dir>          project root, a git repository (default: current directory)
                                 --remove <dir>        instead of copying, delete a folder this command made (it holds .harness-scratch-copy)
                               Exit: 0 copied or removed, 1 git or delete failed, 64 bad arguments or an unmarked folder.
        """;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var root = Directory.GetCurrentDirectory();
        string? target = null;
        string? remove = null;
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
                case "--remove" when i + 1 < args.Length:
                    remove = args[++i];
                    break;
                default:
                    stderr.WriteLine($"Unknown or incomplete option '{args[i]}'.");
                    stderr.WriteLine(Usage);
                    return ExitCodes.Usage;
            }
        }

        if (remove is not null)
        {
            if (target is not null)
            {
                stderr.WriteLine("Pass either --to or --remove, not both.");
                return ExitCodes.Usage;
            }

            return Remove(Path.GetFullPath(remove), stdout, stderr);
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

    private static int Remove(string folder, TextWriter stdout, TextWriter stderr)
    {
        if (!Directory.Exists(folder))
        {
            stdout.WriteLine($"{folder} does not exist; nothing to remove.");
            return ExitCodes.Ok;
        }

        if (!ScratchCopy.IsMarked(folder))
        {
            stderr.WriteLine($"{folder} has no {ScratchCopy.MarkerFile}, so scratch-copy did not make it. Not removed.");
            return ExitCodes.Usage;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(folder, recursive: true);
            stdout.WriteLine($"Removed {folder}.");
            return ExitCodes.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine(ex.Message);
            return ExitCodes.Failed;
        }
    }
}
