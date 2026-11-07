using System.Text.RegularExpressions;

namespace Harness.Tools.Hooks;

/// <summary>A suspected secret. <see cref="Description"/> never contains the value itself.</summary>
internal sealed record SecretFinding(string Rule, string Description, bool HighConfidence, int Line);

/// <summary>
/// Finds secrets in text. High-confidence findings are known token formats and real private keys.
/// Low-confidence findings are strong-looking string literals assigned to a password, secret, key or
/// token name. Weak test values (<c>welcome01</c>), placeholders and environment lookups are ignored.
/// </summary>
internal static partial class SecretScanner
{
    private sealed record Pattern(string Rule, string What, Regex Regex);

    private static readonly Pattern[] Known =
    [
        new("private-key", "a private key", PrivateKey()),
        new("github-token", "a GitHub token", GitHubToken()),
        new("aws-access-key", "an AWS access key", AwsKey()),
        new("slack-token", "a Slack token", SlackToken()),
        new("jwt", "a JSON Web Token", Jwt()),
        new("azure-storage-key", "an Azure storage account key", AzureStorageKey()),
        new("api-key-token", "an API key (sk-...)", ApiKeyToken()),
    ];

    private static readonly string[] PlaceholderMarks =
    [
        "${", "{{", "<", "example", "changeme", "change-me", "placeholder", "your-", "your_", "xxx", "***", "dummy", "redacted", "todo",
    ];

    public static IReadOnlyList<SecretFinding> Scan(string text)
    {
        var findings = new List<SecretFinding>();
        if (string.IsNullOrEmpty(text))
        {
            return findings;
        }

        foreach (var pattern in Known)
        {
            foreach (Match match in pattern.Regex.Matches(text))
            {
                var line = LineOf(text, match.Index);
                findings.Add(new(pattern.Rule, $"line {line} contains what looks like {pattern.What}", true, line));
            }
        }

        var highLines = findings.Select(f => f.Line).ToHashSet();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            if (highLines.Contains(lineNumber))
            {
                continue;
            }

            var value = AssignedValue(lines[i]);
            if (value is not null && IsStrong(value) && !IsPlaceholder(value))
            {
                findings.Add(new(
                    "secret-assignment",
                    $"line {lineNumber} assigns a literal that looks like a real secret to a password, secret, key or token name",
                    false,
                    lineNumber));
            }
        }

        return [.. findings.OrderBy(f => f.Line)];
    }

    private static string? AssignedValue(string line)
    {
        var quoted = QuotedAssignment().Match(line);
        if (quoted.Success)
        {
            return quoted.Groups[1].Value;
        }

        var connection = ConnectionStringPassword().Match(line);
        return connection.Success ? connection.Groups[1].Value : null;
    }

    private static bool IsStrong(string value)
    {
        var hasLower = value.Any(char.IsLower);
        var hasUpper = value.Any(char.IsUpper);
        var hasDigit = value.Any(char.IsDigit);
        var hasOther = value.Any(c => !char.IsLetterOrDigit(c));
        var classes = (hasLower ? 1 : 0) + (hasUpper ? 1 : 0) + (hasDigit ? 1 : 0) + (hasOther ? 1 : 0);
        var hasLetter = hasLower || hasUpper;
        return hasLetter && hasDigit && ((value.Length >= 12 && classes >= 3) || value.Length >= 24);
    }

    private static bool IsPlaceholder(string value) =>
        value[0] is '$' or '%' or '{' or '['
        || PlaceholderMarks.Any(m => value.Contains(m, StringComparison.OrdinalIgnoreCase));

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    [GeneratedRegex(@"-----BEGIN (?:RSA |EC |DSA |OPENSSH |ENCRYPTED )?PRIVATE KEY-----[ \t]*\r?\n\s*[A-Za-z0-9+/=]{40,}")]
    private static partial Regex PrivateKey();

    [GeneratedRegex(@"\bgh[pousr]_[A-Za-z0-9]{36,}\b|\bgithub_pat_[A-Za-z0-9_]{60,}")]
    private static partial Regex GitHubToken();

    [GeneratedRegex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b")]
    private static partial Regex AwsKey();

    [GeneratedRegex(@"\bxox[abprs]-[A-Za-z0-9-]{10,}")]
    private static partial Regex SlackToken();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"AccountKey=[A-Za-z0-9+/]{40,}={0,2}")]
    private static partial Regex AzureStorageKey();

    [GeneratedRegex(@"\bsk-(?:ant-|proj-)?[A-Za-z0-9_-]{32,}")]
    private static partial Regex ApiKeyToken();

    [GeneratedRegex(@"\b(?:password|passwd|pwd|secret|client[_-]?secret|api[_-]?key|access[_-]?token|refresh[_-]?token|auth[_-]?token|token)\b[""']?\s*(?:=>|[:=])\s*@?[""']([^""'\s]{8,})[""']", RegexOptions.IgnoreCase)]
    private static partial Regex QuotedAssignment();

    [GeneratedRegex(@"(?:^|[;""'\s])(?:password|pwd)=([^;""'\s]{8,})", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringPassword();
}
