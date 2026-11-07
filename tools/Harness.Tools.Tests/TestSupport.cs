namespace Harness.Tools.Tests;

/// <summary>A temporary directory that is deleted when the test ends.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "harness-tools-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Write(string relativePath, string content)
    {
        var full = Combine(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a locked file must not fail the test.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal static class RepoPaths
{
    /// <summary>The repository's real templates folder, found by walking up from the test binaries.</summary>
    public static string Templates =>
        TemplateLocator.Find(AppContext.BaseDirectory)
        ?? throw new InvalidOperationException("templates/ folder not found above " + AppContext.BaseDirectory);
}

/// <summary>Captures what a CLI or hook call writes.</summary>
internal sealed class Console3
{
    public StringReader In { get; init; } = new(string.Empty);

    public StringWriter Out { get; } = new();

    public StringWriter Err { get; } = new();

    public static Console3 WithInput(string stdin) => new() { In = new StringReader(stdin) };
}
