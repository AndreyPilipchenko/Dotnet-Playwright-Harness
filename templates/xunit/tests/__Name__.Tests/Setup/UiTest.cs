using {{Name}}.Framework.Utils;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;
using Xunit;

namespace {{Name}}.Tests.Setup;

/// <summary>
/// The only base class for UI tests. The browser is chosen by the BROWSER and HEADED environment
/// variables, because runsettings are ignored under Microsoft Testing Platform. Context options
/// are set here, in code. With TRACE_DIR set, a failed test leaves a Playwright trace there.
/// </summary>
public abstract class UiTest : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        Locale = "en-US",
        ViewportSize = new() { Width = 1280, Height = 720 },
        BaseURL = Environment.GetEnvironmentVariable("BASE_URL"),
    };

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
