using Harness.Tools.Hooks;

namespace Harness.Tools.Tests.Hooks;

/// <summary>
/// Secret-looking values are assembled at run time, so this file never contains one and the
/// harness's own pre-write hook does not trip on it.
/// </summary>
public class SecretScannerTests
{
    private static readonly string Base64Line = new string('A', 30) + new string('b', 30) + "0123456789+/";
    private static readonly string Pem = "-----BEGIN RSA " + "PRIVATE KEY-----\n" + Base64Line + "\n-----END RSA PRIVATE KEY-----";
    private static readonly string OpenSsh = "-----BEGIN OPENSSH " + "PRIVATE KEY-----\n" + Base64Line + "\n-----END OPENSSH PRIVATE KEY-----";
    private static readonly string GitHubToken = "gh" + "p_" + new string('a', 20) + new string('7', 16);
    private static readonly string GitHubPat = "github" + "_pat_" + new string('B', 30) + "_" + new string('c', 40);
    private static readonly string AwsKey = "AK" + "IA" + "ABCDEFGHIJ234567";
    private static readonly string SlackToken = "xo" + "xb-" + "1234567890-abcdefghij";
    private static readonly string Jwt = "ey" + "J" + new string('h', 20) + ".ey" + "J" + new string('p', 20) + "." + new string('s', 20);
    private static readonly string StorageKey = "Account" + "Key=" + new string('Q', 40) + "abc123+/==";
    private static readonly string ApiKey = "sk" + "-ant-" + new string('k', 20) + new string('9', 20);
    private static readonly string StrongValue = "ABC123abc456" + "def789ghijklmnop=";

    public static TheoryData<string, string> HighConfidence => new()
    {
        { Pem, "private-key" },
        { "key: |\n  " + OpenSsh, "private-key" },
        { "token = \"" + GitHubToken + "\"", "github-token" },
        { GitHubPat, "github-token" },
        { "aws_access_key_id=" + AwsKey, "aws-access-key" },
        { SlackToken, "slack-token" },
        { "Authorization: Bearer " + Jwt, "jwt" },
        { "DefaultEndpointsProtocol=https;AccountName=a;" + StorageKey + ";", "azure-storage-key" },
        { "export KEY=" + ApiKey, "api-key-token" },
    };

    public static TheoryData<string> Heuristic =>
    [
        "{ \"password\": \"" + StrongValue + "\" }",
        "private const string ApiKey = \"" + StrongValue + "\";",
        "var clientSecret = @\"" + StrongValue + "\";",
        "Password => \"" + StrongValue + "\";",
        "Server=tcp:db.example.com;Database=app;User ID=admin;Pass" + "word=" + StrongValue + ";",
        "access_token: '" + StrongValue + "'",
    ];

    public static TheoryData<string> Clean =>
    [
        "public Task OpenAsync() => _page.SetContentAsync(Content);",
        "private ILocator PasswordInput => _page.GetByLabel(\"Password\");",
        "public Task VerifyPasswordErrorMessageIsDisplayedAsync() => Task.CompletedTask;",
        "var password = Environment.GetEnvironmentVariable(\"APP_PASSWORD\");",
        "Password = Environment.GetEnvironmentVariable(\"APP_PASSWORD\") ?? string.Empty,",
        "{ \"password\": \"wrong-password\" }",
        "{ \"password\": \"welcome01\" }",
        "{ \"password\": \"${APP_PASSWORD}\" }",
        "{ \"apiKey\": \"<your-api-key>\" }",
        "{ \"token\": \"{{TOKEN}}\" }",
        "{ \"buildHash\": \"a1b2c3d4e5f6a7b8c9d0a1b2c3d4e5f6a7b8c9d0\" }",
        "-----BEGIN RSA " + "PRIVATE KEY-----\nMIIEp...etc\n-----END RSA PRIVATE KEY-----",
        "Never commit a line starting with -----BEGIN PRIVATE KEY-----.",
        "",
    ];

    [Theory]
    [MemberData(nameof(HighConfidence))]
    public void FindsKnownSecretFormats(string text, string rule)
    {
        var findings = SecretScanner.Scan(text);

        var finding = Assert.Single(findings, f => f.HighConfidence);
        Assert.Equal(rule, finding.Rule);
    }

    [Theory]
    [MemberData(nameof(Heuristic))]
    public void FlagsStrongLiteralsAssignedToSecretNames_AsLowConfidence(string text)
    {
        var findings = SecretScanner.Scan(text);

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.False(f.HighConfidence));
    }

    [Theory]
    [MemberData(nameof(Clean))]
    public void IgnoresOrdinaryCode_PlaceholdersAndWeakTestValues(string text)
    {
        Assert.Empty(SecretScanner.Scan(text));
    }

    [Fact]
    public void ReportsTheLineNumber()
    {
        var findings = SecretScanner.Scan("line one\nline two\ntoken = \"" + GitHubToken + "\"\n");

        Assert.Equal(3, Assert.Single(findings).Line);
    }

    [Fact]
    public void NeverEchoesTheSecretValue()
    {
        var finding = Assert.Single(SecretScanner.Scan("token = \"" + GitHubToken + "\""));

        Assert.DoesNotContain(GitHubToken, finding.Description, StringComparison.Ordinal);
    }
}
