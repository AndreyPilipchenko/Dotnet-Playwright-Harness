using {{Name}}.Framework.Utils;
using Microsoft.Playwright;

namespace {{Name}}.Tests.Setup;

/// <summary>
/// Signs in every role in Roles.cs once, before the first test, and deletes the saved sessions after
/// the last. With no roles it does nothing, so no browser is started. MSTest allows one
/// [AssemblyInitialize] per assembly: add other run-once setup here, not in a second one.
/// </summary>
[TestClass]
public static class SignInOnce
{
    [AssemblyInitialize]
    public static async Task SignInAllRolesAsync(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Roles.All.Count == 0)
        {
            return;
        }

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright[Environment.GetEnvironmentVariable("BROWSER") ?? "chromium"]
            .LaunchAsync(new() { Headless = Environment.GetEnvironmentVariable("HEADED") != "1" });
        await SignedInSessions.SaveAllAsync(browser, UiTest.BaseContextOptions(), Roles.All);
    }

    [AssemblyCleanup]
    public static void DeleteSessions() => SignedInSessions.DeleteAll();
}
