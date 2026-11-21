using {{Name}}.Framework.Utils;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;
using Xunit;

namespace {{Name}}.Tests.Setup;

/// <summary>
/// The base class for UI tests, next to ApiTest for API tests. The browser is chosen by the BROWSER and HEADED environment
/// variables, because runsettings are ignored under Microsoft Testing Platform. Context options
/// are set here, in code. With TRACE_DIR set, a failed test leaves a Playwright trace there.
/// A class that overrides <see cref="Role"/> starts each test already signed in as that role.
/// </summary>
public abstract class UiTest : PageTest
{
    /// <summary>The role from Roles.cs that this class's tests run as, or null for signed out.</summary>
    protected virtual string? Role => null;

    /// <summary>The context options every UI test starts from. SignInOnce uses them too, so a saved session matches the context that loads it.</summary>
    public static BrowserNewContextOptions BaseContextOptions() => new()
    {
        Locale = "en-US",
        ViewportSize = new() { Width = 1280, Height = 720 },
        BaseURL = Environment.GetEnvironmentVariable("BASE_URL"),
    };

    public override BrowserNewContextOptions ContextOptions()
    {
        var options = BaseContextOptions();
        if (Role is { } role && !CurrentTestIsSignedOut())
        {
            options.StorageStatePath = SignedInSessions.PathFor(role);
        }

        return options;
    }

    private bool CurrentTestIsSignedOut() =>
        TestContext.Current.TestMethod?.MethodName is { } name
        && (GetType().GetMethod(name)?.IsDefined(typeof(SignedOutAttribute), inherit: false) ?? false);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        if (TraceFiles.Enabled)
        {
            await Context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true });
        }
    }

    public override async ValueTask DisposeAsync()
    {
        if (TraceFiles.Enabled)
        {
            var failed = TestContext.Current.TestState?.Result == TestResult.Failed;
            await Context.Tracing.StopAsync(new() { Path = failed ? TraceFiles.PathFor(TestContext.Current.Test!.TestDisplayName) : null });
        }

        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
