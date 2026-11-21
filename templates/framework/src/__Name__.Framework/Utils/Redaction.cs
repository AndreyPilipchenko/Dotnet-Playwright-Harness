using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace {{Name}}.Framework.Utils;

/// <summary>
/// Masks secrets as <c>***</c> in browser diagnostics and in Playwright traces, before either leaves
/// the test. It masks values handed out by <see cref="Secrets"/> or passed to <see cref="Remember"/>,
/// plus the forms a trace holds them in: header and cookie values, bearer tokens, JWTs, and
/// secret-named JSON and query fields (docs/framework-matrix.md, "Browser diagnostics and masked
/// traces"). Screenshots inside a trace are images and are left as they are.
/// </summary>
public static partial class Redaction
{
    private const string Hidden = "***";

    // The same names secret-scan looks for.
    private const string SecretNames =
        "password|passwd|pwd|current_password|new_password|password_confirmation|secret|client_secret|api_key|apikey|x-api-key"
        + "|token|access_token|refresh_token|id_token|auth_token|authorization|cookie|set-cookie";

    private const string HeaderNames = "authorization|proxy-authorization|cookie|set-cookie|x-api-key";

    private static readonly ConcurrentDictionary<string, byte> Remembered = new(StringComparer.Ordinal);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Adds a value to mask, such as a token the app returned. Values shorter than 4 characters are ignored.</summary>
    public static void Remember(string value)
    {
        if (value is { Length: >= 4 })
        {
            Remembered.TryAdd(value, 0);
        }
    }

    /// <summary>Returns the text with every known secret form replaced by <c>***</c>.</summary>
    public static string Mask(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Whole values only, as secret-scan matches them, so a remembered "1234" never lands inside
        // 12345.6 or a sha1. Longest first, so a value that contains another leaves no visible tail.
        var forms = Remembered.Keys.SelectMany(Forms).Distinct(StringComparer.Ordinal).OrderByDescending(v => v.Length).Select(Regex.Escape).ToList();
        if (forms.Count > 0)
        {
            text = Regex.Replace(text, @"(?<![\w-])(?:" + string.Join('|', forms) + @")(?![\w-])", Hidden, RegexOptions.CultureInvariant);
        }

        text = NameValuePair().Replace(text, "${pre}" + Hidden);
        text = CookieList().Replace(text, list => CookieValue().Replace(list.Value, "${pre}" + Hidden));
        text = HeaderLine().Replace(text, "${pre}" + Hidden);
        text = BearerToken().Replace(text, "${pre}" + Hidden);
        text = Jwt().Replace(text, Hidden);
        text = MaskJsonFields(text);
        return QueryField().Replace(text, "${pre}" + Hidden);
    }

    // "password":"…" in bodies, and \"access_token\":\"…\" inside an escaped body. The value may hold
    // escaped quotes, so its end is found by EndOfValue rather than by the regex.
    private static string MaskJsonFields(string text)
    {
        var result = new StringBuilder(text.Length);
        var copied = 0;
        foreach (Match field in JsonField().Matches(text))
        {
            var start = field.Index + field.Length;
            // A field that starts inside an earlier masked value still masks its own value.
            if (start < copied || EndOfValue(text, start, field.Groups["esc"].Length) is not (>= 0 and var end))
            {
                continue;
            }

            result.Append(text, copied, start - copied).Append(Hidden);
            copied = end;
        }

        return result.Append(text, copied, text.Length - copied).ToString();
    }

    // A string nested n levels deep in JSON strings opens and closes with a quote after 0, 1, 3 or 7
    // backslashes, and a literal backslash in it is n+1 backslashes. So a quote after r backslashes is
    // part of the value when r ≡ 2n+1 (mod 2(n+1)), a quote nested deeper. Any other quote ends it:
    // this value's closing quote when r ≡ n, or a shallower string's when the value was never closed.
    // The value then ends after the literal backslashes, leaving the quote's own r mod (n+1). A raw
    // newline or the end of the text ends it too, so a broken value is masked, never shown.
    // Returns -1 only when n is not a nesting level.
    private static int EndOfValue(string text, int start, int n)
    {
        if ((n & (n + 1)) != 0)
        {
            return -1;
        }

        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                return i > start && text[i - 1] == '\r' ? i - 1 : i;
            }

            if (text[i] != '\\' && text[i] != '"')
            {
                continue;
            }

            var run = 0;
            while (i + run < text.Length && text[i + run] == '\\')
            {
                run++;
            }

            if (i + run < text.Length && text[i + run] == '"' && run % (2 * (n + 1)) != (2 * n) + 1)
            {
                return i + run - (run % (n + 1));
            }

            i += Math.Max(run, 1) - 1;
            if (run > 0 && i + 1 < text.Length && text[i + 1] == '"')
            {
                i++;
            }
        }

        return text.Length;
    }

    /// <summary>
    /// Rewrites every text entry of a zip, such as a Playwright trace, through <see cref="Mask"/>.
    /// Binary entries are copied unchanged. Throws if the zip can't be read or written; the caller
    /// must then delete it, because an unmasked trace must not remain.
    /// </summary>
    public static void MaskZip(string path)
    {
        var temp = path + ".masking";
        try
        {
            // Two passes, one entry at a time, so a large trace is never held in memory whole.
            using (var source = ZipFile.OpenRead(path))
            using (var target = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                // A value typed into a password field is a secret however the test got it, so it is
                // remembered before anything is masked, and then hidden in every entry. DOM snapshots
                // live only in *.trace entries, so screenshots and resources are not read twice.
                foreach (var entry in source.Entries.Where(e => e.FullName.EndsWith(".trace", StringComparison.Ordinal)))
                {
                    RememberPasswordFields(ReadText(entry) ?? string.Empty);
                }

                foreach (var entry in source.Entries)
                {
                    var bytes = ReadAll(entry);
                    var masked = AsText(bytes) is { } text ? MaskEntry(entry.FullName, text) : null;
                    var copy = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                    copy.LastWriteTime = entry.LastWriteTime;
                    using var output = copy.Open();
                    output.Write(masked is null ? bytes : StrictUtf8.GetBytes(masked));
                }
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    // trace.trace and trace.network hold one JSON object per line. A masked line that no longer parses
    // would break the viewer without a word, so it fails the masking instead, and the trace is deleted.
    private static string MaskEntry(string name, string text)
    {
        var masked = Mask(text);
        if (!name.EndsWith(".trace", StringComparison.Ordinal) && !name.EndsWith(".network", StringComparison.Ordinal))
        {
            return masked;
        }

        var before = text.Split('\n');
        var after = masked.Split('\n');
        for (var i = 0; i < after.Length; i++)
        {
            if (before.Length != after.Length || (after[i] != before[i] && Parses(before[i]) && !Parses(after[i])))
            {
                throw new InvalidDataException($"Masking broke the JSON of {name}, line {i + 1}.");
            }
        }

        return masked;
    }

    private static bool Parses(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // A trace's DOM snapshot keeps an input's typed value next to its attributes:
    // {"__playwright_value_":"…","id":"pw","type":"password"}. The value is JSON-escaped there.
    private static void RememberPasswordFields(string text)
    {
        foreach (Match element in PasswordElement().Matches(text))
        {
            foreach (Match value in TypedValue().Matches(element.Value))
            {
                Remember(Unescape(value.Groups["value"].Value));
            }
        }
    }

    private static string Unescape(string value) => value.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);

    // A remembered value as it appears raw, inside one JSON string, and inside a JSON string within a JSON string.
    private static IEnumerable<string> Forms(string value)
    {
        var once = Escape(value);
        return [value, once, Escape(once)];
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var input = entry.Open();
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string? ReadText(ZipArchiveEntry entry) => AsText(ReadAll(entry));

    // Text is strict UTF-8 with no NUL character; anything else (screenshots) is binary.
    private static string? AsText(byte[] bytes)
    {
        try
        {
            var text = StrictUtf8.GetString(bytes);
            return text.Contains('\0', StringComparison.Ordinal) ? null : text;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    // One element's attribute object in a DOM snapshot, when it is a password field.
    [GeneratedRegex(@"\{[^{}]*""type""\s*:\s*""password""[^{}]*\}", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordElement();

    [GeneratedRegex(@"""(?:__playwright_value_|value)""\s*:\s*""(?<value>(?:[^""\\]|\\.)*)""")]
    private static partial Regex TypedValue();

    // {"name":"Authorization","value":"…"} in trace headers, and {"name":"api_key","value":"…"} in query strings.
    [GeneratedRegex(@"(?<pre>""name""\s*:\s*""(?:" + HeaderNames + "|" + SecretNames + @")""\s*,\s*""value""\s*:\s*"")(?:[^""\\]|\\.)*(?="")", RegexOptions.IgnoreCase)]
    private static partial Regex NameValuePair();

    // "cookies":[{"name":"sid","value":"…"}] in trace.network: every cookie value is a session.
    [GeneratedRegex(@"""cookies""\s*:\s*\[[^\]]*\]")]
    private static partial Regex CookieList();

    [GeneratedRegex(@"(?<pre>""value""\s*:\s*"")(?:[^""\\]|\\.)*(?="")")]
    private static partial Regex CookieValue();

    // Authorization: … as a plain header line.
    [GeneratedRegex(@"(?<pre>^[ \t]*(?:" + HeaderNames + @")[ \t]*:[ \t]*)[^\r\n]+", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex HeaderLine();

    // The digit tells a token from prose such as "Bearer authentication".
    [GeneratedRegex(@"(?<pre>\bbearer\s+)(?=[\w.~+/=-]*\d)[\w.~+/=-]{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"eyJ[\w-]{5,}\.eyJ[\w-]{5,}\.[\w-]*")]
    private static partial Regex Jwt();

    // Up to the opening quote of a secret-named field's value; esc is the backslashes before each quote.
    // esc is capped at 15 backslashes (four levels deep), so each position tries at most 16 and a long
    // run of backslashes stays linear. Any shorter tail of a longer run is still tried, so a nested field
    // after literal backslashes (C:\\\"password\":\"…) is found.
    [GeneratedRegex(@"(?<esc>\\{0,15})""(?:" + SecretNames + @")\k<esc>""\s*:\s*\k<esc>""", RegexOptions.IgnoreCase)]
    private static partial Regex JsonField();

    // ?api_key=… in URLs, and password=… in form bodies.
    [GeneratedRegex(@"(?<pre>(?:^|[?&\s""'])(?:" + SecretNames + @")=)[^&#\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex QueryField();
}
