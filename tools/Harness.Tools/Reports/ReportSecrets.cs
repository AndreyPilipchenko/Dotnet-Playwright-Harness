using System.Text;
using System.Text.RegularExpressions;
using Harness.Tools.Hooks;

namespace Harness.Tools.Reports;

/// <summary>A value that must not appear in a report, known by the variable that holds it.</summary>
internal sealed record ForbiddenValue(string Kind, Regex Regex);

/// <summary>
/// Names the kinds of secret in report text, including inside base64, where HTML reports keep their
/// attachments. Returns kinds only, never a value.
/// </summary>
internal static partial class ReportSecrets
{
    private const int MaxDecodeDepth = 2;

    private const string SecretNames =
        "password|passwd|pwd|current_password|new_password|password_confirmation|secret|client_secret|api_key|apikey|x-api-key"
        + "|token|access_token|refresh_token|id_token|auth_token|authorization|cookie|set-cookie";

    public static ForbiddenValue Forbidden(string kind, string value) =>
        new(kind, new Regex($@"(?<![\w-]){Regex.Escape(value)}(?![\w-])", RegexOptions.CultureInvariant));

    public static IReadOnlySet<string> Kinds(string text, IReadOnlyList<ForbiddenValue> forbidden)
    {
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        AddKinds(text, forbidden, kinds, 0);
        return kinds;
    }

    private static void AddKinds(string text, IReadOnlyList<ForbiddenValue> forbidden, HashSet<string> kinds, int depth)
    {
        foreach (var finding in SecretScanner.Scan(text))
        {
            kinds.Add(finding.Rule);
        }

        if (BearerToken().IsMatch(text))
        {
            kinds.Add("bearer-token");
        }

        if (PasswordHash().IsMatch(text))
        {
            kinds.Add("password-hash");
        }

        if (HasUnmaskedField(JsonField(), text) || HasUnmaskedField(QueryField(), text))
        {
            kinds.Add("secret-field");
        }

        foreach (var value in forbidden.Where(v => v.Regex.IsMatch(text)))
        {
            kinds.Add(value.Kind);
        }

        if (depth == MaxDecodeDepth)
        {
            return;
        }

        foreach (Match blob in Blob().Matches(text))
        {
            if (TryDecode(blob.Value) is { } decoded)
            {
                AddKinds(decoded, forbidden, kinds, depth + 1);
            }
        }
    }

    private static bool HasUnmaskedField(Regex field, string text) =>
        field.Matches(text).Any(m => !Masked().IsMatch(m.Groups[1].Value));

    private static string? TryDecode(string blob)
    {
        var body = blob.TrimEnd('=');
        if (body.Length % 4 == 1)
        {
            body = body[..^1];
        }

        var padded = body.PadRight(body.Length + ((4 - (body.Length % 4)) % 4), '=');
        var buffer = new byte[(padded.Length / 4) * 3];
        if (!Convert.TryFromBase64String(padded, buffer, out var written))
        {
            return null;
        }

        var isPng = written >= 4 && buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47;
        return isPng ? null : Encoding.UTF8.GetString(buffer, 0, written);
    }

    // The digit tells a token from prose such as "Bearer authentication".
    [GeneratedRegex(@"bearer\s+(?!\*)(?=[\w.-]*\d)[\w.-]{12,}", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"\$2[aby]\$\d\d\$[./A-Za-z0-9]{50,}")]
    private static partial Regex PasswordHash();

    [GeneratedRegex(@"\\*""(?:" + SecretNames + @")\\*""\s*:\s*\\*""([^""\\]{3,})", RegexOptions.IgnoreCase)]
    private static partial Regex JsonField();

    [GeneratedRegex(@"(?<=^|[?&\s""'])(?:" + SecretNames + @")=([\w%~.+-]{3,})(?=[&\s""'<>]|$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex QueryField();

    [GeneratedRegex(@"^(?:(?:bearer\s+)?\*+|(?:%2A)+|\[.*|REDACTED.*|null|none|undefined|true|false)$", RegexOptions.IgnoreCase)]
    private static partial Regex Masked();

    // Twelve characters of base64 hold eight bytes, the shortest value searched for.
    [GeneratedRegex(@"[A-Za-z0-9+/=]{12,}")]
    private static partial Regex Blob();
}
