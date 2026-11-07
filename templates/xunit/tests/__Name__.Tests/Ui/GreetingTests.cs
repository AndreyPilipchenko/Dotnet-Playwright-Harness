using {{Name}}.Framework.Pages;
using {{Name}}.Tests.Setup;
using Xunit;

namespace {{Name}}.Tests.Ui;

public class GreetingTests : UiTest
{
    [Fact]
    [Trait("Category", "smoke")]
    public async Task ShowsGreetingAsync()
    {
        var greetingPage = new GreetingPage(Page);

        await greetingPage.OpenAsync();
        await greetingPage.VerifyGreetingAsync("Hello");
    }
}
