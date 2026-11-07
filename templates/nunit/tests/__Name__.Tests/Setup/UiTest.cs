using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

[assembly: Parallelizable(ParallelScope.Fixtures)]

namespace {{Name}}.Tests.Setup;

/// <summary>
/// The only base class for UI tests. The browser is chosen by the BROWSER and HEADED environment
/// variables, because runsettings are ignored under Microsoft Testing Platform. Context options
/// are set here, in code.
/// </summary>
public abstract class UiTest : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        Locale = "en-US",
        ViewportSize = new() { Width = 1280, Height = 720 },
        BaseURL = Environment.GetEnvironmentVariable("BASE_URL"),
    };
}
