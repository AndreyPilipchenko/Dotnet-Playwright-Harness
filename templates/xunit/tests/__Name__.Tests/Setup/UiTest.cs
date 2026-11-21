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

    private BrowserDiagnostics? _diagnostics;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _diagnostics = BrowserDiagnostics.Attach(Context);
        if (TraceFiles.Enabled)
        {
            await Context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true });
        }
    }

    // A failed test gets a masked trace when TRACE_DIR is set, then what the browser said in its TRX output.
    // The trace goes first: masking it also remembers any password typed into a password field.
    public override async ValueTask DisposeAsync()
    {
        var failed = TestContext.Current.TestState?.Result == TestResult.Failed;
        try
        {
            if (TraceFiles.Enabled)
            {
                var path = failed ? TraceFiles.PathFor(TestContext.Current.Test!.TestDisplayName) : null;
                await Context.Tracing.StopAsync(new() { Path = path });
                if (path is not null && TraceFiles.MaskOrRemove(path) is { } removed)
                {
                    TestContext.Current.TestOutputHelper?.WriteLine(removed);
                }
            }
        }
        finally
        {
            // Written even when stopping the trace throws, such as after a browser crash.
            if (failed)
            {
                foreach (var line in _diagnostics?.Report() ?? [])
                {
                    TestContext.Current.TestOutputHelper?.WriteLine(line);
                }
            }
        }

        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
