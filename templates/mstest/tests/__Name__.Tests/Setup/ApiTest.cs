using Microsoft.Playwright.MSTest;

namespace {{Name}}.Tests.Setup;

/// <summary>
/// The only base class for API tests. It starts no browser and no page: tests reach the service
/// through Api classes built on the inherited Playwright instance. UI tests derive from UiTest.
/// </summary>
public abstract class ApiTest : PlaywrightTest
{
}
