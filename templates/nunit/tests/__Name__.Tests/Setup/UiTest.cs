using {{Name}}.Framework.Utils;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework.Interfaces;

[assembly: Parallelizable(ParallelScope.Fixtures)]

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
        TestContext.CurrentContext.Test.MethodName is { } name && IsSignedOut(name);

    // Overloaded tests share a name, and the name is all we have here, so they must agree on [SignedOut].
    private bool IsSignedOut(string name)
    {
        var marks = GetType().GetMethods().Where(m => m.Name == name)
            .Select(m => m.IsDefined(typeof(SignedOutAttribute), inherit: false)).Distinct().ToList();
        return marks.Count <= 1
            ? marks.SingleOrDefault()
            : throw new InvalidOperationException($"The overloads of {name} disagree on [SignedOut]. Give the signed-out test a name of its own.");
    }

    private BrowserDiagnostics? _diagnostics;

    [SetUp]
    public async Task StartTraceAsync()
    {
        _diagnostics = BrowserDiagnostics.Attach(Context);
        if (TraceFiles.Enabled)
        {
            await Context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true });
        }
    }

    // A failed test gets a masked trace when TRACE_DIR is set, then what the browser said in its TRX output.
    // The trace goes first: masking it also remembers any password typed into a password field.
    [TearDown]
    public async Task StopTraceAsync()
    {
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            if (TraceFiles.Enabled)
            {
                var path = failed ? TraceFiles.PathFor(TestContext.CurrentContext.Test.FullName) : null;
                await Context.Tracing.StopAsync(new() { Path = path });
                if (path is not null && TraceFiles.MaskOrRemove(path) is { } removed)
                {
                    TestContext.Out.WriteLine(removed);
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
                    TestContext.Out.WriteLine(line);
                }
            }
        }
    }
}
