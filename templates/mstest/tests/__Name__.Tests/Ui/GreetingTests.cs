using {{Name}}.Framework.Pages;
using {{Name}}.Tests.Setup;

namespace {{Name}}.Tests.Ui;

[TestClass]
public class GreetingTests : UiTest
{
    [TestMethod]
    [TestCategory("smoke")]
    public async Task ShowsGreetingAsync()
    {
        var greetingPage = new GreetingPage(Page);

        await greetingPage.OpenAsync();
        await greetingPage.VerifyGreetingAsync("Hello");
    }
}
