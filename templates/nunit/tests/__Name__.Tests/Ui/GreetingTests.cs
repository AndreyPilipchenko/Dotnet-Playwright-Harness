using {{Name}}.Framework.Pages;
using {{Name}}.Tests.Setup;

namespace {{Name}}.Tests.Ui;

public class GreetingTests : UiTest
{
    [Test]
    [Category("smoke")]
    public async Task ShowsGreetingAsync()
    {
        var greetingPage = new GreetingPage(Page);

        await greetingPage.OpenAsync();
        await greetingPage.VerifyGreetingAsync("Hello");
    }
}
