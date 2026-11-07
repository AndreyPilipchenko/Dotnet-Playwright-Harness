using Harness.Tools.Conventions;
using Harness.Tools.Hooks;
using Harness.Tools.Scaffolding;

namespace Harness.Tools;

/// <summary>Routes the first argument to a command.</summary>
internal static class Cli
{
    private static readonly string UsageText = $$"""
        Usage: Harness.Tools <command> [options]

        Commands:
          hook <mode>          Claude Code hook. Modes: session-start, pre-shell, post-shell,
                               post-edit, pre-write, pre-compact, session-end. Always fails open.
          scaffold             Render the walking skeleton:
                                 --framework <nunit|xunit|mstest>  (required)
                                 --name <Name>                     (required, e.g. Acme.Shop)
                                 --target-framework <tfm>          (default net10.0)
                                 --output <dir>                    (default: current directory)
                                 --templates <dir>                 (default: found above the tool)
        {{ConventionCommand.Usage.TrimEnd()}}
          traceability         Planned
          ci-summary           Planned
          dashboard            Planned
          secret-scan          Planned
          tracker              Planned
          validate-skills      Planned
        """;

    private static readonly string[] PlannedCommands =
    [
        "traceability", "ci-summary", "dashboard", "secret-scan", "tracker", "validate-skills",
    ];

    public static int Run(IReadOnlyList<string> args, TextReader stdin, TextWriter stdout, TextWriter stderr)
    {
        if (args.Count == 0)
        {
            stderr.WriteLine(UsageText);
            return ExitCodes.Usage;
        }

        var command = args[0];
        var rest = args.Skip(1).ToArray();

        switch (command)
        {
            case "--help" or "-h" or "help":
                stdout.WriteLine(UsageText);
                return ExitCodes.Ok;
            case "hook":
                return HookRunner.Run(rest.FirstOrDefault(), stdin, stdout, stderr);
            case "check-conventions":
                return ConventionCommand.Run(rest, stdout, stderr);
            case "scaffold":
                return RunScaffold(rest, stdout, stderr);
            default:
                if (PlannedCommands.Contains(command, StringComparer.Ordinal))
                {
                    stderr.WriteLine($"'{command}' is not implemented yet.");
                    return ExitCodes.Failed;
                }

                stderr.WriteLine($"Unknown command '{command}'.");
                stderr.WriteLine(UsageText);
                return ExitCodes.Usage;
        }
    }

    private static int RunScaffold(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (!TryParseOptions(args, ["--framework", "--name", "--target-framework", "--output", "--templates"], out var values, out var error))
        {
            stderr.WriteLine(error);
            return ExitCodes.Usage;
        }

        if (!values.TryGetValue("--framework", out var framework) || !values.TryGetValue("--name", out var name))
        {
            stderr.WriteLine("scaffold needs --framework and --name.");
            return ExitCodes.Usage;
        }

        var templates = values.GetValueOrDefault("--templates") ?? TemplateLocator.Find(AppContext.BaseDirectory);
        if (templates is null)
        {
            stderr.WriteLine("No templates/ folder found above the tool. Pass --templates <dir>.");
            return ExitCodes.Failed;
        }

        var options = new ScaffoldOptions(
            framework,
            name,
            values.GetValueOrDefault("--target-framework") ?? "net10.0",
            Path.GetFullPath(values.GetValueOrDefault("--output") ?? Directory.GetCurrentDirectory()),
            templates);

        try
        {
            var written = Scaffolder.Run(options);
            stdout.WriteLine($"Scaffolded {written.Count} files into {options.OutputDirectory}:");
            foreach (var file in written)
            {
                stdout.WriteLine("  " + file);
            }

            return ExitCodes.Ok;
        }
        catch (ScaffoldException ex)
        {
            stderr.WriteLine(ex.Message);
            return ExitCodes.Failed;
        }
    }

    /// <summary>Parses <c>--key value</c> pairs. Every key must be known and have a value.</summary>
    private static bool TryParseOptions(
        string[] args,
        string[] known,
        out Dictionary<string, string> values,
        out string error)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        error = string.Empty;
        for (var i = 0; i < args.Length; i += 2)
        {
            var key = args[i];
            if (!known.Contains(key, StringComparer.Ordinal))
            {
                error = $"Unknown option '{key}'.";
                return false;
            }

            if (i + 1 >= args.Length)
            {
                error = $"Option '{key}' needs a value.";
                return false;
            }

            values[key] = args[i + 1];
        }

        return true;
    }
}
