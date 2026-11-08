using System.IO.Compression;

namespace Harness.Tools.Reports;

/// <summary>
/// Fails when reports about to be published hold secret-shaped text. Prints the file and the kind of
/// secret, never the value. Text inside an image goes unnoticed, and the output says what wasn't read.
/// </summary>
internal static class SecretScanCommand
{
    public const string Usage = """
          secret-scan          Fail if a report about to be published holds a token or password.
                               Prints the file and the kind, never the value. Exit 1 found, 2 not checked:
                                 --path <file-or-folder>           (required)
                                 --forbid-env <NAME,NAME>          (also search for these variables' values)
        """;

    /// <summary>Shorter values match ordinary words, so they can't be searched for.</summary>
    private const int MinValueLength = 8;

    private const long MaxZipEntryBytes = 64 * 1024 * 1024;

    private static readonly HashSet<string> ReportExtensions = new(
        [".html", ".htm", ".trx", ".md", ".json", ".xml", ".txt", ".log", ".csv"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> MediaExtensions = new(
        [".png", ".jpg", ".jpeg", ".gif", ".webp", ".webm", ".mp4", ".woff", ".woff2", ".ico"],
        StringComparer.OrdinalIgnoreCase);

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, Func<string, string?> env)
    {
        if (!ReportOptions.TryParse(args, ["--path", "--forbid-env"], [], out var options, out var error))
        {
            stderr.WriteLine(error);
            return ExitCodes.Usage;
        }

        if (options.Single("--path") is not { } path)
        {
            stderr.WriteLine("secret-scan needs --path.");
            return ExitCodes.Usage;
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            stderr.WriteLine($"Not checked: {path} does not exist.");
            return ExitCodes.NotChecked;
        }

        var forbidden = ForbiddenValues(options.Single("--forbid-env"), stderr, env);
        var scan = new Scan(forbidden, stderr);
        var baseFolder = File.Exists(path) ? Path.GetDirectoryName(Path.GetFullPath(path))! : path;
        var files = File.Exists(path) ? [path] : Directory.GetFiles(path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        foreach (var file in files)
        {
            scan.File(file, Path.GetRelativePath(baseFolder, file));
        }

        if (scan.Found > 0)
        {
            stderr.WriteLine($"Found secrets in {scan.Found} place(s). Do not publish these reports.");
            return ExitCodes.Failed;
        }

        if (scan.Unreadable > 0)
        {
            stderr.WriteLine($"Not checked: {scan.Unreadable} file(s) could not be read.");
            return ExitCodes.NotChecked;
        }

        if (scan.Read == 0)
        {
            stderr.WriteLine($"Not checked: No report files in {path}.");
            return ExitCodes.NotChecked;
        }

        stdout.WriteLine($"No secrets found in {scan.Read} report file(s). {scan.NotRead} other file(s) were not read, and text inside an image goes unnoticed.");
        return ExitCodes.Ok;
    }

    private static List<ForbiddenValue> ForbiddenValues(string? names, TextWriter stderr, Func<string, string?> env)
    {
        var values = new List<ForbiddenValue>();
        foreach (var name in (names ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var value = env(name);
            if (string.IsNullOrEmpty(value))
            {
                stderr.WriteLine($"Not searching for {name}: it is not set.");
            }
            else if (value.Length < MinValueLength)
            {
                stderr.WriteLine($"Not searching for {name}: it is under {MinValueLength} characters.");
            }
            else
            {
                values.Add(ReportSecrets.Forbidden("env:" + name, value));
            }
        }

        return values;
    }

    private sealed class Scan(IReadOnlyList<ForbiddenValue> forbidden, TextWriter stderr)
    {
        public int Found { get; private set; }

        public int Read { get; private set; }

        public int NotRead { get; private set; }

        public int Unreadable { get; private set; }

        public void File(string file, string label)
        {
            var extension = Path.GetExtension(file);
            try
            {
                if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    Zip(file, label);
                }
                else if (ReportExtensions.Contains(extension))
                {
                    Text(System.IO.File.ReadAllText(file), label);
                }
                else
                {
                    NotRead++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Unreadable++;
                stderr.WriteLine($"Could not read {label}: {ex.GetType().Name}.");
            }
        }

        private void Zip(string file, string label)
        {
            using var zip = ZipFile.OpenRead(file);
            foreach (var entry in zip.Entries.Where(e => e.Name.Length > 0))
            {
                if (MediaExtensions.Contains(Path.GetExtension(entry.Name)) || entry.Length > MaxZipEntryBytes)
                {
                    NotRead++;
                    continue;
                }

                using var reader = new StreamReader(entry.Open());
                Text(reader.ReadToEnd(), $"{label}!{entry.FullName}");
            }
        }

        private void Text(string text, string label)
        {
            Read++;
            foreach (var kind in ReportSecrets.Kinds(text, forbidden).Order(StringComparer.Ordinal))
            {
                Found++;
                stderr.WriteLine($"{label}: {kind}");
            }
        }
    }
}
