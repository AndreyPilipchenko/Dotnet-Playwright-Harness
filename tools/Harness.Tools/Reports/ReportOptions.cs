namespace Harness.Tools.Reports;

/// <summary>Parses the options of a report command. A value option may repeat; a flag takes no value.</summary>
internal static class ReportOptions
{
    public static bool TryParse(
        string[] args,
        string[] valued,
        string[] flags,
        out Dictionary<string, List<string>> values,
        out string error)
    {
        values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        error = string.Empty;
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (flags.Contains(key, StringComparer.Ordinal))
            {
                values[key] = [];
                continue;
            }

            if (!valued.Contains(key, StringComparer.Ordinal))
            {
                error = $"Unknown option '{key}'.";
                return false;
            }

            if (++i >= args.Length)
            {
                error = $"Option '{key}' needs a value.";
                return false;
            }

            if (!values.TryGetValue(key, out var list))
            {
                values[key] = list = [];
            }

            list.Add(args[i]);
        }

        return true;
    }

    public static string? Single(this Dictionary<string, List<string>> values, string key) =>
        values.TryGetValue(key, out var list) && list.Count > 0 ? list[^1] : null;
}
