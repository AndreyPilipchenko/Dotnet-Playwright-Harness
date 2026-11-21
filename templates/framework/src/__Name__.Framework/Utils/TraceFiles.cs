namespace {{Name}}.Framework.Utils;

/// <summary>
/// Where a failed test's Playwright trace goes. Traces are off unless the TRACE_DIR environment
/// variable names a folder, because recording one costs time on every test. A saved trace is
/// masked at once (<see cref="Redaction"/>); one that can't be masked is deleted.
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

    /// <summary>Masks the saved trace. Returns null when it was masked, or the line to write to the test output when it had to be deleted.</summary>
    public static string? MaskOrRemove(string path)
    {
        try
        {
            Redaction.MaskZip(path);
            return null;
        }
        catch (Exception ex)
        {
            // Any failure means the trace may hold secrets, so it goes, whatever the cause.
            try
            {
                File.Delete(path);
            }
            catch (Exception deleteError) when (deleteError is IOException or UnauthorizedAccessException)
            {
                return $"Trace {path} was NOT masked and could NOT be deleted ({ex.GetType().Name}, then {deleteError.GetType().Name}). Delete it before sharing anything from this run.";
            }

            return $"Trace removed: it could not be masked ({ex.GetType().Name}).";
        }
    }
}
