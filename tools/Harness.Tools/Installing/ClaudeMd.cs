using System.Text;

namespace Harness.Tools.Installing;

internal enum ClaudeMdChange
{
    Created,
    Appended,
    Unchanged,

    /// <summary>The file is not UTF-8 (for example UTF-16 from Windows PowerShell 5.1), so it was left alone.</summary>
    NotUtf8,

    /// <summary>The file could not be read or written (read-only, locked, a folder), so it was left alone.</summary>
    WriteFailed,
}

/// <summary>
/// Makes sure the project's <c>CLAUDE.md</c> imports the harness instructions. The team's own text is
/// never rewritten: the import line is appended, in the file's own line endings and BOM state. A file
/// that is not UTF-8, or cannot be written, is left alone and reported, so install still finishes.
/// </summary>
internal static class ClaudeMd
{
    public const string ImportLine = "@.harness/templates/CLAUDE.harness.md";

    private const string Heading = "# Project instructions";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static ClaudeMdChange Ensure(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, $"{Heading}\n\n{ImportLine}\n", StrictUtf8);
                return ClaudeMdChange.Created;
            }

            var bytes = File.ReadAllBytes(path);
            var bom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
            if (!TryDecode(bytes, bom ? 3 : 0, out var text))
            {
                return ClaudeMdChange.NotUtf8;
            }

            if (HasImport(text))
            {
                return ClaudeMdChange.Unchanged;
            }

            File.WriteAllText(path, text + Addition(text), new UTF8Encoding(encoderShouldEmitUTF8Identifier: bom));
            return ClaudeMdChange.Appended;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ClaudeMdChange.WriteFailed;
        }
    }

    public static bool HasImport(string text) => text.Split('\n').Any(line => line.Trim() == ImportLine);

    /// <summary>Whether the project's CLAUDE.md imports the harness instructions; false when it can't be read.</summary>
    public static bool Imports(string path)
    {
        try
        {
            return File.Exists(path) && HasImport(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryDecode(byte[] bytes, int start, out string text)
    {
        text = string.Empty;
        try
        {
            text = StrictUtf8.GetString(bytes, start, bytes.Length - start);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        // UTF-16 or UTF-32 decodes as valid UTF-8 full of NUL characters, with or without a BOM.
        return !text.Contains('\0', StringComparison.Ordinal);
    }

    private static string Addition(string text)
    {
        var newline = NewlineOf(text);
        var addition = new StringBuilder();
        if (text.Length > 0)
        {
            if (!text.EndsWith('\n'))
            {
                addition.Append(newline);
            }

            if (!text.EndsWith(newline + newline, StringComparison.Ordinal))
            {
                addition.Append(newline);
            }
        }

        return addition.Append(ImportLine).Append(newline).ToString();
    }

    private static string NewlineOf(string text)
    {
        var index = text.IndexOf('\n', StringComparison.Ordinal);
        return index > 0 && text[index - 1] == '\r' ? "\r\n" : "\n";
    }
}
