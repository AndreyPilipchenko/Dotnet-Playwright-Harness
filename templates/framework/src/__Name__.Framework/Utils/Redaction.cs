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
        text = JsonField().Replace(text, "${pre}" + Hidden);
        return QueryField().Replace(text, "${pre}" + Hidden);
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
            using (var source = ZipFile.OpenRead(path))
            using (var target = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                var entries = source.Entries.Select(e => (Entry: e, Bytes: ReadAll(e))).ToList();
                var texts = entries.Select(e => AsText(e.Bytes)).ToList();

                // A value typed into a password field is a secret however the test got it, so it is
                // remembered before anything is masked, and then hidden in every entry.
                foreach (var text in texts.OfType<string>())
                {
                    RememberPasswordFields(text);
                }

                for (var i = 0; i < entries.Count; i++)
                {
                    var name = entries[i].Entry.FullName;
                    var masked = texts[i] is { } text ? MaskEntry(name, text) : null;
                    var copy = target.CreateEntry(name, CompressionLevel.Optimal);
                    copy.LastWriteTime = entries[i].Entry.LastWriteTime;
                    using var output = copy.Open();
                    output.Write(masked is null ? entries[i].Bytes : StrictUtf8.GetBytes(masked));
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

    // "password":"…" in bodies, and \"access_token\":\"…\" inside an escaped body.
    [GeneratedRegex(@"(?<pre>\\*""(?:" + SecretNames + @")\\*""\s*:\s*\\*"")(?:[^""\\]|\\[^""])*(?=\\*"")", RegexOptions.IgnoreCase)]
    private static partial Regex JsonField();

    // ?api_key=… in URLs, and password=… in form bodies.
    [GeneratedRegex(@"(?<pre>(?:^|[?&\s""'])(?:" + SecretNames + @")=)[^&#\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex QueryField();
}
