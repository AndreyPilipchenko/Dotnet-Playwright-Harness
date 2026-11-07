using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace {{Name}}.Framework.Pages;

/// <summary>
/// Walking-skeleton page that proves the layers connect without an application.
/// The scaffold-framework skill replaces it with a page of the application under test.
/// </summary>
public sealed class GreetingPage
{
    private const string Content = "<main><h1>Hello</h1></main>";

    private readonly IPage _page;

    public GreetingPage(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _page = page;
    }

    private ILocator GreetingHeading => _page.GetByRole(AriaRole.Heading, new() { Level = 1 });

    public Task NavigateAsync() => _page.SetContentAsync(Content);

    public Task VerifyGreetingAsync(string expected) => Expect(GreetingHeading).ToHaveTextAsync(expected);
}
