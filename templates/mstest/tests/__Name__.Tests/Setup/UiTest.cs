using {{Name}}.Framework.Utils;
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

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

    [TestInitialize]
    public async Task StartTraceAsync()
    {
        if (TraceFiles.Enabled)
        {
            await Context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true });
        }
    }

    [TestCleanup]
    public async Task StopTraceAsync()
    {
        if (TraceFiles.Enabled)
        {
            var failed = TestContext.CurrentTestOutcome == UnitTestOutcome.Failed;
            await Context.Tracing.StopAsync(new() { Path = failed ? TraceFiles.PathFor($"{TestContext.FullyQualifiedTestClassName}.{TestContext.TestName}") : null });
        }
    }
}
