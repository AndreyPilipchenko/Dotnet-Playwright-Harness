using System.Text.Json;
using System.Text.Json.Nodes;

namespace Harness.Tools.Conventions;

/// <summary>The <c>check-conventions</c> command line.</summary>
internal static class ConventionCommand
{
    public const string Usage = """
          check-conventions    Check the project's C# files against the harness conventions:
                                 --root <dir>          project root (default: current directory)
                                 --files <f> [<f>...]  check only these files
                                 --changed             check only files changed since --base-ref
                                 --base-ref <ref>      with --changed (default HEAD)
                                 --json                machine-readable output
                                 --fail-on-warn        exit 1 on new warnings too
                                 --write-baseline      record current findings as legacy (full scan only)
                                 --list-rules          print the rules
                               Exit: 0 clean, 1 new failures, 2 nothing could be checked, 64 bad arguments.
        """;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private sealed class Options
    {
        public string Root { get; set; } = Directory.GetCurrentDirectory();

        public List<string>? Files { get; set; }

        public bool Changed { get; set; }

        public string? BaseRef { get; set; }

        public bool Json { get; set; }

        public bool FailOnWarn { get; set; }

        public bool WriteBaseline { get; set; }

        public bool ListRules { get; set; }
    }

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (!TryParse(args, out var options, out var error))
        {
            stderr.WriteLine(error);
            stderr.WriteLine(Usage);
            return ExitCodes.Usage;
        }

        if (options.ListRules)
        {
            foreach (var rule in ConventionRules.All)
            {
                stdout.WriteLine($"{rule.Id,-32} {rule.Severity.ToString().ToUpperInvariant(),-5} {rule.Why}");
            }

            return ExitCodes.Ok;
        }

        try
        {
            var report = ConventionChecker.Run(options.Root, options.Files, options.Changed ? options.BaseRef ?? "HEAD" : null);

            if (options.WriteBaseline)
            {
                var path = ConventionChecker.WriteBaseline(options.Root, report);
                stdout.WriteLine($"Recorded {report.Findings.Count} findings as legacy in {path}.");
                return ExitCodes.Ok;
            }

            if (options.Json)
            {
                WriteJson(report, stdout);
            }
            else
            {
                WriteText(report, options, stdout);
            }

            var failed = report.NewFailures.Any() || (options.FailOnWarn && report.NewWarnings.Any());
            return failed ? ExitCodes.Failed : ExitCodes.Ok;
        }
        catch (ConventionException ex)
        {
            stderr.WriteLine("check-conventions: " + ex.Message);
            return ExitCodes.NotChecked;
        }
    }

    private static void WriteText(ConventionReport report, Options options, TextWriter stdout)
    {
        if (report.FileCount == 0)
        {
            stdout.WriteLine($"No C# files changed since {options.BaseRef ?? "HEAD"}; nothing to check.");
            return;
        }

        foreach (var f in report.NewFailures.Concat(report.NewWarnings))
        {
            var label = f.Severity == Severity.Fail ? "FAIL" : "WARN";
            stdout.WriteLine($"{label}  {f.File}:{f.Line}  [{f.Rule}] {f.Why}");
            if (!string.IsNullOrEmpty(f.Excerpt))
            {
                stdout.WriteLine($"      {f.Excerpt}");
            }
        }

        stdout.WriteLine(
            $"Checked {report.FileCount} files: {report.NewFailures.Count()} failures, {report.NewWarnings.Count()} warnings, {report.LegacyCount} legacy.");
    }

    private static void WriteJson(ConventionReport report, TextWriter stdout)
    {
        static JsonArray ToArray(IEnumerable<Finding> findings) =>
            [.. findings.Select(f => (JsonNode)new JsonObject
            {
                ["rule"] = f.Rule,
                ["file"] = f.File,
                ["line"] = f.Line,
                ["why"] = f.Why,
                ["excerpt"] = f.Excerpt,
            })];

        var json = new JsonObject
        {
            ["files"] = report.FileCount,
            ["fullScan"] = report.FullScan,
            ["fail"] = ToArray(report.NewFailures),
            ["warn"] = ToArray(report.NewWarnings),
            ["legacy"] = report.LegacyCount,
        };
        stdout.WriteLine(json.ToJsonString(Indented));
    }

    private static bool TryParse(string[] args, out Options options, out string error)
    {
        options = new Options();
        error = string.Empty;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--root" or "--base-ref":
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        error = $"Option '{args[i]}' needs a value.";
                        return false;
                    }

                    if (args[i] == "--root")
                    {
                        options.Root = args[++i];
                    }
                    else
                    {
                        options.BaseRef = args[++i];
                    }

                    break;
                case "--files":
                    options.Files ??= [];
                    while (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        options.Files.Add(args[++i]);
                    }

                    if (options.Files.Count == 0)
                    {
                        error = "Option '--files' needs at least one file.";
                        return false;
                    }

                    break;
                case "--changed":
                    options.Changed = true;
                    break;
                case "--json":
                    options.Json = true;
                    break;
                case "--fail-on-warn":
                    options.FailOnWarn = true;
                    break;
                case "--write-baseline":
                    options.WriteBaseline = true;
                    break;
                case "--list-rules":
                    options.ListRules = true;
                    break;
                default:
                    error = $"Unknown option '{args[i]}'.";
                    return false;
            }
        }

        error = options switch
        {
            { Files: not null, Changed: true } => "Use --files or --changed, not both.",
            { BaseRef: not null, Changed: false } => "--base-ref needs --changed.",
            { WriteBaseline: true, Files: not null } or { WriteBaseline: true, Changed: true } => "--write-baseline needs a full scan (no --files or --changed).",
            _ => string.Empty,
        };
        return error.Length == 0;
    }
}
