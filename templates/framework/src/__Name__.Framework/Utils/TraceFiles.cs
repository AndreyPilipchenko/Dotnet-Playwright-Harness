namespace {{Name}}.Framework.Utils;

/// <summary>
/// Where a failed test's Playwright trace goes. Traces are off unless the TRACE_DIR environment
/// variable names a folder, because recording one costs time on every test.
/// </summary>
public static class TraceFiles
{
    private static string? Folder => Environment.GetEnvironmentVariable("TRACE_DIR");

    public static bool Enabled => !string.IsNullOrEmpty(Folder);

    public static string PathFor(string testName)
    {
        var safeName = string.Concat(testName.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
        return Path.Combine(Folder!, safeName + ".zip");
    }
}
