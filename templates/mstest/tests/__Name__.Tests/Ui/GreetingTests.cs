using {{Name}}.Framework.Pages;
using {{Name}}.Framework.Utils;
using {{Name}}.Tests.Setup;

namespace {{Name}}.Tests.Ui;

[TestClass]
public class GreetingTests : UiTest
{
    private static readonly GreetingTestData Data = TestData.Load<GreetingTestData>("Greeting");

    [TestMethod]
    [TestCategory("smoke")]
    public async Task ShowsGreetingAsync()
    {
        var greetingPage = new GreetingPage(Page);

        await greetingPage.NavigateAsync();
        await greetingPage.VerifyGreetingAsync(Data.Greeting);
    }
}

public sealed record GreetingTestData(string Greeting);
