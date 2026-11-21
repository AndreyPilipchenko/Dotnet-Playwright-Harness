using {{Name}}.Framework.Utils;
using {{Name}}.Tests.Setup;
using Microsoft.Playwright;

// In the root test namespace: a [SetUpFixture] covers only its own namespace and those below it.
namespace {{Name}}.Tests;

/// <summary>
/// Signs in every role in Setup/Roles.cs once, before the first test, and deletes the saved sessions
/// after the last. With no roles it does nothing, so no browser is started.
/// </summary>
[SetUpFixture]
public sealed class SignInOnce
{
    [OneTimeSetUp]
    public async Task SignInAllRolesAsync()
    {
        if (Roles.All.Count == 0)
        {
            return;
        }

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright[Environment.GetEnvironmentVariable("BROWSER") ?? "chromium"]
            .LaunchAsync(new() { Headless = Environment.GetEnvironmentVariable("HEADED") != "1" });
        await SignedInSessions.SaveAllAsync(browser, UiTest.BaseContextOptions(), Roles.All);
    }

    [OneTimeTearDown]
    public void DeleteSessions() => SignedInSessions.DeleteAll();
}
