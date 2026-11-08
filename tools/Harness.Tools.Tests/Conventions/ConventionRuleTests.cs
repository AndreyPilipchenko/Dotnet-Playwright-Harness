using Harness.Tools.Conventions;
using static Harness.Tools.Tests.Conventions.ConventionHost;

namespace Harness.Tools.Tests.Conventions;

/// <summary>
/// One bad and at least one good fixture per rule. In a bad fixture, the line marked
/// <c>// &lt;-</c> is where the finding must be reported.
/// </summary>
public class ConventionRuleTests
{
    private static readonly string StrongValue = "ABC123abc456" + "def789ghijklmnop=";

    public static TheoryData<string, string, string> Bad => new()
    {
        // no-skipped-test: per framework
        { "no-skipped-test", TestPath, "public class LoginTests\n{\n    [Test, Ignore(\"flaky\")] // <-\n    public Task LogsInAsync() => Task.CompletedTask;\n}" },
        { "no-skipped-test", TestPath, "public class LoginTests\n{\n    [Test, Explicit] // <-\n    public Task LogsInAsync() => Task.CompletedTask;\n}" },
        { "no-skipped-test", TestPath, "public class LoginTests\n{\n    [Fact(Skip = \"later\")] // <-\n    public Task LogsInAsync() => Task.CompletedTask;\n}" },
        { "no-skipped-test", TestPath, "[TestClass]\npublic class LoginTests\n{\n    [TestMethod, Ignore] // <-\n    public Task LogsInAsync() => Task.CompletedTask;\n}" },
        { "no-skipped-test", TestPath, "public class LoginTests\n{\n    [Test]\n    public void LogsIn()\n    {\n        Assert.Ignore(\"x\"); // <-\n    }\n}" },
        { "no-skipped-test", TestPath, "public class LoginTests\n{\n    [Fact]\n    public void LogsIn()\n    {\n        Assert.Skip(\"x\"); // <-\n    }\n}" },

        { "no-hard-wait", PagePath, "class LoginPage\n{\n    public async Task SaveAsync()\n    {\n        await _page.WaitForTimeoutAsync(500); // <-\n    }\n}" },
        { "no-hard-wait", TestPath, "class LoginTests\n{\n    void X()\n    {\n        Thread.Sleep(100); // <-\n    }\n}" },
        { "no-hard-wait", ApiPath, "class UsersApi\n{\n    async Task XAsync()\n    {\n        await Task.Delay(100); // <-\n    }\n}" },

        { "no-try-outside-utils", PagePath, "class LoginPage\n{\n    void X()\n    {\n        try // <-\n        {\n        }\n        catch\n        {\n        }\n    }\n}" },

        { "playwright-internal-api", PagePath, "using Microsoft.Playwright.Core; // <-\nclass LoginPage { }" },

        { "no-assertion-in-test", TestPath, "class LoginTests\n{\n    async Task XAsync()\n    {\n        await Expect(page.Heading).ToBeVisibleAsync(); // <-\n    }\n}" },
        { "no-assertion-in-test", TestPath, "class LoginTests\n{\n    void X()\n    {\n        Assert.That(x, Is.EqualTo(1)); // <-\n    }\n}" },
        { "no-assertion-in-test", TestPath, "class LoginTests\n{\n    void X()\n    {\n        Assert.Equal(1, x); // <-\n    }\n}" },
        { "no-assertion-in-test", TestPath, "class LoginTests\n{\n    void X()\n    {\n        Assert.AreEqual(1, x); // <-\n    }\n}" },
        { "no-assertion-in-test", TestPath, "class LoginTests\n{\n    void X()\n    {\n        x.Should().Be(1); // <-\n    }\n}" },

        { "no-locator-in-test", TestPath, "class LoginTests\n{\n    async Task XAsync()\n    {\n        await Page.GetByRole(AriaRole.Button).ClickAsync(); // <-\n    }\n}" },
        { "no-locator-in-test", TestPath, "class LoginTests\n{\n    async Task XAsync()\n    {\n        await Page.Locator(\"#save\").ClickAsync(); // <-\n    }\n}" },

        { "no-navigation-in-test", TestPath, "class LoginTests\n{\n    async Task XAsync()\n    {\n        await Page.GotoAsync(\"/login\"); // <-\n    }\n}" },

        { "page-goto-owner", PagePath, "class LoginPage\n{\n    public async Task OpenSettingsAsync()\n    {\n        await _page.GotoAsync(\"/settings\"); // <-\n    }\n}" },

        { "no-http-in-test", TestPath, "class LoginTests\n{\n    void X()\n    {\n        using var client = new HttpClient(); // <-\n    }\n}" },
        { "no-http-in-test", TestPath, "class LoginTests\n{\n    async Task XAsync()\n    {\n        var api = await Playwright.APIRequest.NewContextAsync(); // <-\n    }\n}" },

        { "no-id-engine-prefix", PagePath, "class LoginPage\n{\n    private ILocator EmailInput => _page.Locator(\"id=email\"); // <-\n}" },

        { "no-console-output", TestPath, "class LoginTests\n{\n    void X()\n    {\n        Console.WriteLine(\"x\"); // <-\n    }\n}" },
        { "no-console-output", PagePath, "class LoginPage\n{\n    void X()\n    {\n        Debug.WriteLine(\"x\"); // <-\n    }\n}" },

        { "no-persistent-dialog-handler", PagePath, "class LoginPage\n{\n    void X()\n    {\n        _page.Dialog += async (_, dialog) => await dialog.AcceptAsync(); // <-\n    }\n}" },

        { "locator-in-method-body", PagePath, "class LoginPage\n{\n    public Task SaveAsync() => _page.GetByRole(AriaRole.Button).ClickAsync(); // <-\n}" },

        { "timeout-literal", PagePath, "class LoginPage\n{\n    public Task VerifySavedAsync() =>\n        Expect(SavedMessage).ToBeVisibleAsync(new() { Timeout = 60000 }); // <-\n}" },

        { "secret-literal", PagePath, "class LoginPage\n{\n    private const string Password = \"" + StrongValue + "\"; // <-\n}" },

        { "test-data-pairing", TestPath, "public class LoginTests // <-\n{\n    [Test]\n    public Task LogsInAsync() => Task.CompletedTask;\n}" },

        { "test-data-source", TestPath, "public class LoginTests\n{\n    private static readonly LoginTestData Data = TestData.Load<LoginTestData>(\"Checkout\"); // <-\n}" },
        { "test-data-source", TestPath, "public class LoginTests\n{\n    void X()\n    {\n        var json = File.ReadAllText(\"other.json\"); // <-\n    }\n}" },

        { "no-test-data-in-business-layer", PagePath, "class LoginPage\n{\n    void X()\n    {\n        var data = TestData.Load<LoginTestData>(\"Login\"); // <-\n    }\n}" },

        { "test-class-naming", "tests/Acme.Tests/Ui/Login.cs", "public class Login // <-\n{\n    [Test]\n    public Task LogsInAsync() => Task.CompletedTask;\n}" },

        { "category-vocabulary", TestPath, "public class LoginTests\n{\n    [Test, Category(\"slow\")] // <-\n    public Task LogsInAsync() => Task.CompletedTask;\n}" },
        { "category-vocabulary", TestPath, "public class LoginTests\n{\n    [Fact, Trait(\"Category\", \"wip\")] // <-\n    public Task LogsInAsync() => Task.CompletedTask;\n}" },
        { "category-vocabulary", TestPath, "[TestClass]\npublic class LoginTests\n{\n    [TestMethod, TestCategory(\"nightly\")] // <-\n    public Task LogsInAsync() => Task.CompletedTask;\n}" },

        { "locator-naming", PagePath, "class LoginPage\n{\n    private ILocator Email => _page.GetByLabel(\"Email\"); // <-\n}" },
        { "locator-naming", PagePath, "class LoginPage\n{\n    private ILocator emailInput => _page.GetByLabel(\"Email\"); // <-\n}" },
        { "locator-naming", PagePath, "class LoginPage\n{\n    private ILocator RowFor(string name) => _page.GetByRole(AriaRole.Row); // <-\n}" },

        { "locator-visibility", PagePath, "class LoginPage\n{\n    public ILocator EmailInput => _page.GetByLabel(\"Email\"); // <-\n}" },

        { "layer-boundary", PagePath, "using NUnit.Framework; // <-\nclass LoginPage { }" },
        { "layer-boundary", PagePath, "using Acme.Tests.Setup; // <-\nclass LoginPage { }" },
        { "layer-boundary", UtilPath, "using Acme.Framework.Pages; // <-\nclass Waits { }" },

        { "no-base-page", PagePath, "public sealed class LoginPage : BasePage // <-\n{\n}" },
        { "no-base-page", PagePath, "public abstract class PageBase // <-\n{\n}" },

        { "test-logic", TestPath, "class LoginTests\n{\n    void X()\n    {\n        if (x) // <-\n        {\n        }\n    }\n}" },
        { "test-logic", TestPath, "class LoginTests\n{\n    void X()\n    {\n        foreach (var u in users) // <-\n        {\n        }\n    }\n}" },
        { "test-logic", TestPath, "class LoginTests\n{\n    void X()\n    {\n        var y = x ? 1 : 2; // <-\n    }\n}" },

        { "async-discipline", PagePath, "class LoginPage\n{\n    string X() => _page.TitleAsync().Result; // <-\n}" },
        { "async-discipline", PagePath, "class LoginPage\n{\n    void X()\n    {\n        _page.ReloadAsync().Wait(); // <-\n    }\n}" },
        { "async-discipline", PagePath, "class LoginPage\n{\n    string X() => _page.TitleAsync().GetAwaiter().GetResult(); // <-\n}" },
        { "async-discipline", PagePath, "class LoginPage\n{\n    public async void SaveAsync() // <-\n    {\n    }\n}" },

        { "async-naming", PagePath, "class LoginPage\n{\n    public Task Save() => Task.CompletedTask; // <-\n}" },
        { "async-naming", TestPath, "class LoginTests\n{\n    [Test]\n    public async Task LogsIn() // <-\n    {\n    }\n}" },

        { "action-verify-separation", PagePath, "class LoginPage\n{\n    public async Task VerifySavedAsync()\n    {\n        await SaveButton.ClickAsync(); // <-\n    }\n}" },
        { "action-verify-separation", PagePath, "class LoginPage\n{\n    public async Task SaveAsync()\n    {\n        await Expect(SaveButton).ToBeEnabledAsync(); // <-\n    }\n}" },

        { "mixed-test-frameworks", TestPath, "public class LoginTests\n{\n    [Test]\n    public Task AAsync() => Task.CompletedTask;\n\n    [Fact] // <-\n    public Task BAsync() => Task.CompletedTask;\n}" },

        { "category-vocabulary", TestPath, "public class LoginTests\n{\n    [Test, Category(\"tc:TC 12\")] // <-\n    public Task AAsync() => Task.CompletedTask;\n}" },

        // extension rules: classic `this` methods and C# 14 extension blocks
        { "extension-placement", PagePath, "static class PageExtensions\n{\n    public static Task OpenHomeAsync(this IPage page) => page.GotoAsync(\"/\"); // <-\n}" },
        { "extension-placement", TestPath, "static class LocatorExtensions\n{\n    extension(ILocator locator) // <-\n    {\n        public Task ReplaceAsync(string v) => locator.FillAsync(v);\n    }\n}" },
        { "extension-placement", UtilPath, "static class Waits\n{\n    public static Task SettleAsync(this IPage page) => page.WaitForLoadStateAsync(); // <-\n}" },
        { "extension-placement", SetupPath,"static class PageExtensions\n{\n    public static Task ReloadAsync(this IPage page) => page.ReloadAsync(); // <-\n}" },
        { "extension-naming", ExtensionPath, "static class LocatorHelpers // <-\n{\n    public static Task ReplaceAsync(this ILocator l, string v) => l.FillAsync(v);\n}" },
        { "extension-naming", UtilPath, "static class LocatorExtensions // <-\n{\n    extension(ILocator locator)\n    {\n        public Task ReplaceAsync(string v) => locator.FillAsync(v);\n    }\n}" },
        { "no-assertion-in-extension", ExtensionPath, "static class LocatorExtensions\n{\n    public static Task ShouldBeShownAsync(this ILocator l) => Expect(l).ToBeVisibleAsync(); // <-\n}" },
        { "no-assertion-in-extension", ExtensionPath, "static class LocatorExtensions\n{\n    extension(IAPIResponse response)\n    {\n        public void MustBeOk() => Assert.That(response.Ok); // <-\n    }\n}" },
        { "no-element-locator-extension", ExtensionPath, "static class LocatorExtensions\n{\n    public static ILocator SaveButton(this IPage page) =>\n        page.GetByRole(AriaRole.Button, new() { Name = \"Save\" }); // <-\n}" },
        { "no-element-locator-extension", ExtensionPath, "static class LocatorExtensions\n{\n    extension(IPage page)\n    {\n        public async Task LoginAsync(string email)\n        {\n            await page.GetByLabel(\"Email\").FillAsync(email); // <-\n        }\n    }\n}" },
    };

    public static TheoryData<string, string, string> Good => new()
    {
        { "no-skipped-test", TestPath, "public class LoginTests\n{\n    [Test, Category(\"smoke\")]\n    public Task LogsInAsync() => Task.CompletedTask;\n}" },
        { "no-skipped-test", TestPath, "public class LoginTests\n{\n    [Fact]\n    public Task LogsInAsync() => Task.CompletedTask;\n}" },
        { "no-hard-wait", UtilPath, "static class Retry\n{\n    static async Task XAsync()\n    {\n        await Task.Delay(100);\n    }\n}" },
        { "no-try-outside-utils", UtilPath, "static class Retry\n{\n    static void X()\n    {\n        try\n        {\n        }\n        catch (IOException)\n        {\n        }\n    }\n}" },
        { "playwright-internal-api", PagePath, "using Microsoft.Playwright;\nclass LoginPage { }" },
        { "no-assertion-in-test", TestPath, "class LoginTests\n{\n    async Task XAsync()\n    {\n        await loginPage.VerifyErrorAsync(Data.Error);\n    }\n}" },
        { "no-assertion-in-test", PagePath, "class LoginPage\n{\n    public Task VerifyErrorAsync(string e) => Expect(ErrorMessage).ToHaveTextAsync(e);\n}" },
        { "no-locator-in-test", TestPath, "class LoginTests\n{\n    async Task XAsync()\n    {\n        var loginPage = new LoginPage(Page);\n        await loginPage.NavigateAsync();\n    }\n}" },
        { "no-locator-in-test", SetupPath, "abstract class UiTest\n{\n    async Task XAsync()\n    {\n        await Page.Locator(\"body\").WaitForAsync();\n    }\n}" },
        { "page-goto-owner", PagePath, "class LoginPage\n{\n    private const string Url = \"/login\";\n\n    public Task NavigateAsync() => _page.GotoAsync(Url);\n}" },
        { "no-http-in-test", ApiPath, "class UsersApi\n{\n    private readonly HttpClient _client;\n}" },
        { "no-id-engine-prefix", PagePath, "class LoginPage\n{\n    private ILocator EmailInput => _page.Locator(\"#email\");\n}" },
        { "no-console-output", UtilPath, "static class Log\n{\n    static void X() => Console.WriteLine(\"x\");\n}" },
        { "no-persistent-dialog-handler", PagePath, "class LoginPage\n{\n    void X()\n    {\n        _page.Dialog += Accept;\n    }\n}" },
        { "locator-in-method-body", PagePath, "class LoginPage\n{\n    private ILocator SaveButton => _page.GetByRole(AriaRole.Button);\n\n    private ILocator ProductRow(string name) => _page.GetByRole(AriaRole.Row, new() { Name = name });\n\n    public Task SaveAsync() => SaveButton.ClickAsync();\n}" },
        { "timeout-literal", PagePath, "class LoginPage\n{\n    public Task VerifySavedAsync() =>\n        Expect(SavedMessage).ToBeVisibleAsync(new() { Timeout = Timeouts.SlowSurface });\n}" },
        { "secret-literal", PagePath, "class LoginPage\n{\n    private static readonly string Password = Environment.GetEnvironmentVariable(\"APP_PASSWORD\") ?? string.Empty;\n}" },
        { "test-data-source", TestPath, "public class LoginTests\n{\n    private static readonly LoginTestData Data = TestData.Load<LoginTestData>(\"Login\");\n}" },
        { "test-class-naming", TestPath, "public class LoginTests\n{\n    [Test]\n    public Task LogsInAsync() => Task.CompletedTask;\n}" },
        { "category-vocabulary", TestPath, "public class LoginTests\n{\n    [Test, Category(\"smoke\")]\n    public Task AAsync() => Task.CompletedTask;\n\n    [Test, Category(\"regression\"), Category(\"bug:BUG-12\")]\n    public Task BAsync() => Task.CompletedTask;\n}" },
        { "category-vocabulary", TestPath, "public class LoginTests\n{\n    [Test, Category(\"smoke\"), Category(\"tc:TC-012\")]\n    public Task AAsync() => Task.CompletedTask;\n\n    [Fact, Trait(\"Category\", \"tc:TC-013\")]\n    public Task BAsync() => Task.CompletedTask;\n}" },
        { "locator-naming", PagePath, "class LoginPage\n{\n    private ILocator EmailInput => _page.GetByLabel(\"Email\");\n\n    private ILocator ProductRow(string name) => _page.GetByRole(AriaRole.Row);\n}" },
        { "layer-boundary", PagePath, "using Acme.Framework.Utils;\nusing Microsoft.Playwright;\nclass LoginPage { }" },
        { "no-base-page", PagePath, "public sealed class LoginPage : IDisposable\n{\n}" },
        { "test-logic", SetupPath, "abstract class UiTest\n{\n    void X()\n    {\n        if (x)\n        {\n        }\n    }\n}" },
        { "test-logic", TestPath, "class LoginTests\n{\n    void X()\n    {\n        var y = x ?? \"default\";\n    }\n}" },
        { "async-discipline", PagePath, "class LoginPage\n{\n    void X()\n    {\n        _page.Dialog += async (_, d) => await d.DismissAsync();\n        var r = response.Result;\n    }\n}" },
        { "async-naming", SetupPath, "abstract class UiTest : PageTest\n{\n    public override Task Setup() => Task.CompletedTask;\n}" },
        { "action-verify-separation", PagePath, "class LoginPage\n{\n    public Task VerifySavedAsync() => Expect(SavedMessage).ToBeVisibleAsync();\n\n    public Task SaveAsync() => SaveButton.ClickAsync();\n}" },
        { "mixed-test-frameworks", TestPath, "[TestClass]\npublic class LoginTests\n{\n    [TestMethod, TestCategory(\"smoke\")]\n    public Task AAsync() => Task.CompletedTask;\n}" },
        { "extension-placement", ExtensionPath, "public static class LocatorExtensions\n{\n    public static Task ReplaceTextAsync(this ILocator locator, string value) => locator.FillAsync(value);\n}" },
        { "extension-placement", ExtensionPath, "public static class LocatorExtensions\n{\n    extension(ILocator locator)\n    {\n        public Task ReplaceTextAsync(string value) => locator.FillAsync(value);\n    }\n}" },
        { "extension-placement", PagePath, "class LoginPage\n{\n    public Task SaveAsync(string value) => SaveButton.ClickAsync();\n}" },
        { "extension-naming", ExtensionPath, "public static class LocatorExtensions\n{\n    public static Task ReplaceTextAsync(this ILocator locator, string value) => locator.FillAsync(value);\n}" },
        { "extension-naming", UtilPath, "static class Waits\n{\n    static Task XAsync() => Task.CompletedTask;\n}" },
        { "no-assertion-in-extension", ExtensionPath, "public static class LocatorExtensions\n{\n    extension(IAPIResponse response)\n    {\n        public void EnsureOk(string operation)\n        {\n            _ = response.Ok ? 0 : throw new InvalidOperationException($\"{operation} failed with {response.Status}.\");\n        }\n    }\n}" },
        { "no-element-locator-extension", ExtensionPath, "public static class LocatorExtensions\n{\n    public static ILocator RowWith(this ILocator table, string text) =>\n        table.GetByRole(AriaRole.Row).Filter(new() { HasText = text });\n}" },
        { "no-element-locator-extension", PagePath, "class LoginPage\n{\n    private ILocator SaveButton => _page.GetByRole(AriaRole.Button, new() { Name = \"Save\" });\n}" },
    };

    [Theory]
    [MemberData(nameof(Bad))]
    public void BadFixture_IsReported_OnTheMarkedLine(string rule, string path, string code)
    {
        using var host = new ConventionHost().Write(path, code);
        var expectedLine = Array.FindIndex(code.Split('\n'), l => l.Contains("// <-", StringComparison.Ordinal)) + 1;

        var findings = host.FindingsFor(path);

        Assert.Contains(findings, f => f.Rule == rule && f.Line == expectedLine);
    }

    [Theory]
    [MemberData(nameof(Good))]
    public void GoodFixture_IsNotReported(string rule, string path, string code)
    {
        using var host = new ConventionHost()
            .Write(path, code)
            .Write("resources/testData/LoginTestData.json", "{}");

        Assert.DoesNotContain(host.FindingsFor(path), f => f.Rule == rule);
    }

    [Fact]
    public void TestDataPairing_IsSatisfiedByThePairedJson()
    {
        using var host = new ConventionHost()
            .Write(TestPath, "public class LoginTests\n{\n    [Test]\n    public Task LogsInAsync() => Task.CompletedTask;\n}")
            .Write("resources/testData/LoginTestData.json", "{}");

        Assert.DoesNotContain(host.FindingsFor(TestPath), f => f.Rule == "test-data-pairing");
    }

    [Fact]
    public void AnatomySpacing_ReportsADoubleBlankLineInsideAClass()
    {
        using var host = new ConventionHost().Write(PagePath, "class LoginPage\n{\n    private int _a;\n\n\n    private int _b;\n}\n");

        Assert.Contains(host.FindingsFor(PagePath), f => f.Rule == "anatomy-spacing" && f.Line == 5);
    }

    [Fact]
    public void AnatomySpacing_AllowsSingleBlankLines_AndBlankLinesOutsideClasses()
    {
        using var host = new ConventionHost().Write(PagePath, "using X;\n\n\nnamespace A;\n\nclass LoginPage\n{\n    private int _a;\n\n    private int _b;\n}\n");

        Assert.DoesNotContain(host.FindingsFor(PagePath), f => f.Rule == "anatomy-spacing");
    }

    [Fact]
    public void LayerBoundary_RejectsATestFrameworkPackage_InAFrameworkProject()
    {
        using var host = new ConventionHost()
            .Write(PagePath, "class LoginPage { }")
            .Write("src/Acme.Framework/Acme.Framework.csproj", "<Project>\n  <ItemGroup>\n    <PackageReference Include=\"NUnit\" />\n  </ItemGroup>\n</Project>");

        var finding = Assert.Single(host.Check().Findings, f => f.Rule == "layer-boundary");
        Assert.Equal("src/Acme.Framework/Acme.Framework.csproj", finding.File);
        Assert.Equal(3, finding.Line);
    }

    [Fact]
    public void TestDataFolder_InKebabCase_IsRejected()
    {
        using var host = new ConventionHost().Write(PagePath, "class LoginPage { }").Write("resources/test-data/x.json", "{}");

        Assert.Contains(host.Check().Findings, f => f.Rule == "test-data-folder");
    }

    [Fact]
    public void MixedTestFrameworks_ReportsAFileThatDisagreesWithProjectJson()
    {
        using var host = new ConventionHost()
            .Write(".harness/project.json", "{ \"framework\": \"xunit\" }")
            .Write(TestPath, "public class LoginTests\n{\n    [Test] // <-\n    public Task AAsync() => Task.CompletedTask;\n}");

        Assert.Contains(host.FindingsFor(TestPath), f => f.Rule == "mixed-test-frameworks" && f.Line == 3);
    }

    [Fact]
    public void CategoryVocabulary_ComesFromProjectJson_WhenSet()
    {
        using var host = new ConventionHost()
            .Write(".harness/project.json", "{ \"conventions\": { \"categories\": [\"smoke\", \"nightly\"] } }")
            .Write(TestPath, "public class LoginTests\n{\n    [Test, Category(\"nightly\")]\n    public Task AAsync() => Task.CompletedTask;\n\n    [Test, Category(\"regression\")]\n    public Task BAsync() => Task.CompletedTask;\n}");

        var findings = host.FindingsFor(TestPath).Where(f => f.Rule == "category-vocabulary").ToList();

        Assert.Equal(6, Assert.Single(findings).Line);
    }

    [Fact]
    public void SecretLiteral_NeverEchoesTheValue()
    {
        using var host = new ConventionHost().Write(PagePath, "class LoginPage\n{\n    private const string Password = \"" + StrongValue + "\";\n}");

        var finding = Assert.Single(host.FindingsFor(PagePath), f => f.Rule == "secret-literal");
        Assert.DoesNotContain(StrongValue, finding.Excerpt, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRule_HasAnIdSeverityAndReason()
    {
        Assert.All(ConventionRules.All, r =>
        {
            Assert.Matches("^[a-z]+(-[a-z]+)*$", r.Id);
            Assert.False(string.IsNullOrWhiteSpace(r.Why));
        });
        Assert.Equal(ConventionRules.All.Count, ConventionRules.All.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public void EveryFileRule_HasABadFixture()
    {
        var covered = Bad.Cast<ITheoryDataRow>().Select(row => (string)row.GetData()[0]!).ToHashSet();
        covered.UnionWith(["anatomy-spacing"]);

        var missing = ConventionRules.All.Where(r => r.Kind == RuleKind.File).Select(r => r.Id).Where(id => !covered.Contains(id));

        Assert.Empty(missing);
    }
}
