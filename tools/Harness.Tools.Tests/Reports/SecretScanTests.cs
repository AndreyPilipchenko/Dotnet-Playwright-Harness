using System.IO.Compression;
using System.Text;
using Harness.Tools.Reports;

namespace Harness.Tools.Tests.Reports;

/// <summary>
/// <c>secret-scan</c> fails when a report about to be published holds secret-shaped text. It names the
/// file and the kind of secret and never the value. No files to read is "not checked", never a pass.
/// </summary>
public class SecretScanTests
{
    private static readonly string GitHubToken = "ghp_" + new string('a', 36);
    private static readonly string Jwt = "eyJ" + new string('a', 20) + ".eyJ" + new string('b', 20) + "." + new string('c', 20);

    private static (int Code, string Out, string Err) Run(Dictionary<string, string>? env, params string[] args)
    {
        var io = new Console3();
        var code = SecretScanCommand.Run(args, io.Out, io.Err, name => env?.GetValueOrDefault(name));
        return (code, io.Out.ToString(), io.Err.ToString());
    }

    private static (int Code, string Out, string Err) Scan(TempDir dir, string file, string content)
    {
        dir.Write("site/" + file, content);
        return Run(null, "--path", dir.Combine("site"));
    }

    private static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void NoPathOption_IsAUsageError()
    {
        Assert.Equal(ExitCodes.Usage, Run(null).Code);
    }

    [Fact]
    public void IsRoutedFromTheCli()
    {
        var io = new Console3();

        Assert.Equal(ExitCodes.Usage, Cli.Run(["secret-scan"], io.In, io.Out, io.Err));
    }

    [Fact]
    public void MissingFolder_IsNotChecked()
    {
        using var dir = new TempDir();

        Assert.Equal(ExitCodes.NotChecked, Run(null, "--path", dir.Combine("nope")).Code);
    }

    [Fact]
    public void FolderWithNoReportFiles_IsNotChecked()
    {
        using var dir = new TempDir();

        var (code, _, err) = Scan(dir, "shot.png", "binary");

        Assert.Equal(ExitCodes.NotChecked, code);
        Assert.Contains("No report files", err, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanReport_Passes_AndSaysWhatItDidNotRead()
    {
        using var dir = new TempDir();
        dir.Write("site/shot.png", "binary");

        var (code, output, _) = Scan(dir, "index.html", "<p>All good, uses Bearer authentication</p>");

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("No secrets found in 1 report file(s)", output, StringComparison.Ordinal);
        Assert.Contains("1 other file(s) were not read", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("index.html")]
    [InlineData("index.htm")]
    [InlineData("a.trx")]
    [InlineData("a.md")]
    [InlineData("a.json")]
    [InlineData("a.xml")]
    [InlineData("a.txt")]
    [InlineData("a.log")]
    [InlineData("a.csv")]
    [InlineData("deep/er/a.html")]
    public void ScansEveryReportFileType(string file)
    {
        using var dir = new TempDir();

        var (code, _, err) = Scan(dir, file, $"x {GitHubToken} y");

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains("github-token", err, StringComparison.Ordinal);
    }

    [Fact]
    public void Finding_NamesFileAndKind_NeverTheValue()
    {
        using var dir = new TempDir();

        var (_, output, err) = Scan(dir, "a.html", GitHubToken);

        Assert.Contains("a.html", err, StringComparison.Ordinal);
        Assert.DoesNotContain(GitHubToken, output + err, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsASingleFile()
    {
        using var dir = new TempDir();
        dir.Write("a.md", GitHubToken);

        Assert.Equal(ExitCodes.Failed, Run(null, "--path", dir.Combine("a.md")).Code);
    }

    [Theory]
    [InlineData("Authorization: Bearer abc123def456ghi789", "bearer-token")]
    [InlineData("""{"password":"hunter22"}""", "secret-field")]
    [InlineData("""{\"access_token\":\"abcdef\"}""", "secret-field")]
    [InlineData("""<a href="/x?token=abc123&y=1">""", "secret-field")]
    [InlineData("new_password=abc123 ", "secret-field")]
    [InlineData("$2b$12$aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "password-hash")]
    public void FindsReportShapes(string content, string kind)
    {
        using var dir = new TempDir();

        var (code, _, err) = Scan(dir, "a.html", content);

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains(kind, err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"password":"***"}""")]
    [InlineData("""{"password":"[REDACTED]"}""")]
    [InlineData("""{"password":"REDACTED"}""")]
    [InlineData("""{"token":null}""")]
    [InlineData("""{"token":"none"}""")]
    [InlineData("""{"authorization":"Bearer ***"}""")]
    [InlineData("""<a href="/x?token=%2A%2A%2A">""")]
    [InlineData("Uses Bearer authentication for every request")]
    public void MaskedValuesAndProse_Pass(string content)
    {
        using var dir = new TempDir();

        Assert.Equal(ExitCodes.Ok, Scan(dir, "a.html", content).Code);
    }

    [Fact]
    public void FindsASecretInsideBase64()
    {
        using var dir = new TempDir();

        var (code, _, err) = Scan(dir, "a.html", $"<img src=\"data:text/plain;base64,{Base64(GitHubToken)}\">");

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains("github-token", err, StringComparison.Ordinal);
    }

    [Fact]
    public void FindsASecretInsideBase64WithoutPadding()
    {
        using var dir = new TempDir();
        var encoded = Base64($"xx {GitHubToken}").TrimEnd('=');

        Assert.Equal(ExitCodes.Failed, Scan(dir, "a.html", encoded).Code);
    }

    [Fact]
    public void FindsASecretInsideDoubleBase64()
    {
        using var dir = new TempDir();

        Assert.Equal(ExitCodes.Failed, Scan(dir, "a.html", Base64(Base64(GitHubToken))).Code);
    }

    [Fact]
    public void SkipsDecodedPngs()
    {
        using var dir = new TempDir();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 }.Concat(Encoding.UTF8.GetBytes(GitHubToken)).ToArray();

        Assert.Equal(ExitCodes.Ok, Scan(dir, "a.html", Convert.ToBase64String(png)).Code);
    }

    [Fact]
    public void FindsASecretInsideAZip_AsATraceWouldHoldIt()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("site"));
        using (var zip = ZipFile.Open(dir.Combine("site", "trace.zip"), ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("trace.network").Open());
            writer.Write($"{{\"value\":\"{Jwt}\"}}");
        }

        var (code, _, err) = Run(null, "--path", dir.Combine("site"));

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains("trace.zip", err, StringComparison.Ordinal);
        Assert.Contains("trace.network", err, StringComparison.Ordinal);
    }

    [Fact]
    public void BrokenZip_IsNotChecked_NeverAPass()
    {
        using var dir = new TempDir();

        var (code, _, err) = Scan(dir, "trace.zip", "this is not a zip");

        Assert.Equal(ExitCodes.NotChecked, code);
        Assert.Contains("trace.zip", err, StringComparison.Ordinal);
    }

    [Fact]
    public void ForbiddenEnvironmentValue_IsFound_ByVariableName_NeverByValue()
    {
        using var dir = new TempDir();
        dir.Write("site/a.html", "<p>user typed s3cretvalue here</p>");
        var env = new Dictionary<string, string> { ["APP_ADMIN_PASSWORD"] = "s3cretvalue" };

        var (code, output, err) = Run(env, "--path", dir.Combine("site"), "--forbid-env", "APP_ADMIN_PASSWORD");

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains("env:APP_ADMIN_PASSWORD", err, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cretvalue", output + err, StringComparison.Ordinal);
    }

    [Fact]
    public void ForbiddenValue_IsFoundInsideBase64()
    {
        using var dir = new TempDir();
        dir.Write("site/a.html", Base64("password is s3cretvalue!"));
        var env = new Dictionary<string, string> { ["A"] = "s3cretvalue" };

        Assert.Equal(ExitCodes.Failed, Run(env, "--path", dir.Combine("site"), "--forbid-env", "A").Code);
    }

    [Fact]
    public void ForbiddenValue_MatchesWholeWordsOnly()
    {
        using var dir = new TempDir();
        dir.Write("site/a.html", "xs3cretvaluex and s3cretvalue-more");
        var env = new Dictionary<string, string> { ["A"] = "s3cretvalue" };

        Assert.Equal(ExitCodes.Ok, Run(env, "--path", dir.Combine("site"), "--forbid-env", "A").Code);
    }

    [Fact]
    public void ShortOrUnsetForbiddenValue_WarnsWithoutTheValue_AndIsNotSearched()
    {
        using var dir = new TempDir();
        dir.Write("site/a.html", "<p>admin1 here</p>");
        var env = new Dictionary<string, string> { ["SHORT"] = "admin1" };

        var (code, output, err) = Run(env, "--path", dir.Combine("site"), "--forbid-env", "SHORT,UNSET");

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("SHORT", err, StringComparison.Ordinal);
        Assert.Contains("UNSET", err, StringComparison.Ordinal);
        Assert.DoesNotContain("admin1", output + err, StringComparison.Ordinal);
    }
}
