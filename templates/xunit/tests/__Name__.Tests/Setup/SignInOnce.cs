using {{Name}}.Framework.Utils;
using Microsoft.Playwright;
using Xunit;

[assembly: AssemblyFixture(typeof({{Name}}.Tests.Setup.SignInOnce))]

namespace {{Name}}.Tests.Setup;

/// <summary>
/// Signs in every role in Roles.cs once, before the first test, and deletes the saved sessions after
/// the last. With no roles it does nothing, so no browser is started.
/// </summary>
public sealed class SignInOnce : IAsyncLifetime
{
    public async ValueTask InitializeAsync()
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

    public ValueTask DisposeAsync()
    {
        SignedInSessions.DeleteAll();
        return ValueTask.CompletedTask;
    }
}
