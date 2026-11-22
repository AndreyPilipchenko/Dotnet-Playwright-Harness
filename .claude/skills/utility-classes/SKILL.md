---
name: utility-classes
description: Use when deciding whether code belongs in Utils or Config, or when writing or reviewing a class there in a Playwright C# framework built with this harness - test data loading, secrets and environment variables, API request contexts, database connections, unique values, retries and polling, value checks, named timeouts, logging, signing in once per role and reusing the session (Roles.cs, SignedInSessions, [SignedOut]), or any try/catch or delay that seems necessary.
---

# Utility and config classes

Utils holds technical plumbing: *how* things are done reliably. Config holds named settings.
Neither knows anything about the application's business.

Read [design-conventions](../pom-architecture/references/design-conventions.md) first: §1 (the
two families, dependencies point down), §4 laws 3 and 7, §8 (extension members).

## 1. The litmus test

Put code in `Utils/` only if **all** of these hold:

1. It would work unchanged for a different application under test.
2. It names no page, endpoint, table, element or business term.
3. A business class or the test base class needs it.

Otherwise it belongs in a page, Api, Db or Helpers class. A sign-in flow is business, so it goes in
Helpers. Reading an environment variable, or polling until something is true, is plumbing.

## 2. What lives here

| Class | Job | Verified example |
|---|---|---|
| `Utils/TestData` | Load `<Feature>TestData.json` (from the template) | `TestData.Load<OrdersTestData>("Orders")` |
| `Utils/Secrets` | Read a credential from the environment; fail loudly if missing; remember it for masking (from the template) | `Secrets.Get("SHOP_PASSWORD")` |
| `Utils/Redaction` | Mask secrets as `***` in traces and browser diagnostics (from the template) | `Redaction.Remember(token)` |
| `Utils/BrowserDiagnostics` | Collect what the browser said during a UI test, for a failed test's TRX (from the template) | `BrowserDiagnostics.Attach(Context)` in `UiTest` |
| `Utils/ApiContext` | Create an `IAPIRequestContext` with `API_URL` (or `BASE_URL` when that is unset or empty) and a key header | used by `OrdersApi.CreateAsync` |
| `Utils/DbConnections` | Open a `DbConnection` from a connection string in the environment | used to build an `OrdersDb` |
| `Utils/UniqueData` | Values unique per test and run | `UniqueData.Email("buyer")` |
| `Utils/Checks` | Compare values that have no web-first assertion | `Checks.Equal(expected, actual, "orders for Pliers")` |
| `Utils/Retry` | Bounded polling for eventually consistent arrangement | below |
| `Utils/SignedInSessions` | Sign each role in once per run and serve its saved storage state; `Role`, `[SignedOut]` (from the template) | [signed-in sessions](references/signed-in-sessions.md) |
| `Utils/Extensions/` | Extension members on Playwright and .NET types | extension-classes |
| `Config/Timeouts` | Named time budgets for slow surfaces | `Timeouts.SlowSurface` |

- **Naming:** a noun for what it provides, usually a `static class`: `Secrets`, `Retry`, `UniqueData`. Never `<Noun>Actions`; that suffix means a business flow in Helpers.
- **Config** holds environment coordinates and named constants, never secrets. Each constant gets an XML doc comment saying why it has that value.

`ApiContext` is not in the template. Write it like this, so the key goes through `Secrets`:

```csharp
// src/<Name>.Framework/Utils/ApiContext.cs (technical: connection details from the environment)
public static class ApiContext
{
    public static Task<IAPIRequestContext> CreateAsync(IPlaywright playwright, string header, string variable)
    {
        ArgumentNullException.ThrowIfNull(playwright);
        var key = Secrets.Get(variable); // throws when unset, and masks the key in traces
        // API_URL when the API has its own origin; unset or empty (as in CI) falls back to BASE_URL.
        var apiUrl = Environment.GetEnvironmentVariable("API_URL");
        return playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = string.IsNullOrEmpty(apiUrl) ? Environment.GetEnvironmentVariable("BASE_URL") : apiUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { [header] = key },
        });
    }
}
```


## 3. The only place for `try`, delays and complexity

Utils is exempt from `[no-try-outside-utils]` and `[no-hard-wait]`. That is a responsibility, not a
loophole:

```csharp
public static async Task<T> UntilAsync<T>(Func<Task<T>> attempt, Func<T, bool> done, int attempts, TimeSpan delay)
{
    ArgumentNullException.ThrowIfNull(attempt);
    ArgumentNullException.ThrowIfNull(done);
    ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);

    for (var i = 1; ; i++)
    {
        var result = await attempt();
        if (done(result) || i == attempts)
        {
            return result;
        }

        await Task.Delay(delay);
    }
}
```

- **Bounded:** a fixed number of attempts, then return or throw.
- **For arrangement only:** waiting until a seeded record is searchable, for example. Never wrap a `Verify*` method or a whole test in a retry to make it pass. `Expect` already retries GUI checks; wrapping it hides a real defect (iron law 10).
- **Catch narrowly:** catch a specific exception type you can handle, and rethrow or wrap anything else. Never swallow an exception.
- **Never hide the real failure.** A helper that reports or cleans up must not throw over the original error.

## 4. Secrets and redaction

- Read credentials only through `Secrets.Get`, which throws a clear message naming the missing variable. It also hands each value to `Redaction`, so traces and browser diagnostics mask it. A project's own `Secrets` must keep the `Redaction.Remember(value)` call. `[secrets-remembered]`
- **A secret that doesn't come from `Secrets.Get`,** such as a generated password or a token an Api class got back, goes through `Redaction.Remember(value)` as soon as it exists. Values under 4 characters are ignored.
- **What masking can't reach:** text drawn in a trace's screenshots, and a secret the framework never saw. That includes one typed into an ordinary text field or printed on the page. A value typed into a `type="password"` field is masked even if no one remembered it. `secret-scan` is still the gate before anything is published (reporting).
- Never write a secret into an exception message, a log line or a report. Name the variable, not its value.
- Console output is allowed in Utils, but prefer the test framework's output. Console text is interleaved in parallel runs. `[no-console-output]`

## 5. Dependencies point down

- Utils and Config never use Pages, Apis, Dbs or Helpers. `[layer-boundary]`
- They never reference a test framework, and never load test data on behalf of a business class.
- Utils may use Config (for example a timeout), and Config uses nothing.

## 6. Resource lifecycle

A helper that opens a resource (a connection, a request context) hands it to its owner, the Api or
Db class. That class disposes it from the test's teardown. Utils never keeps static open
resources, which would leak across parallel tests.

## Boundaries

- Extension members → extension-classes
- Business flows across pages → page-classes (Helpers, `<Noun>Actions`)
- Api and Db classes that use these helpers → service-classes
- Package choices (loggers, DB providers) → csharp-standards

## Review checklist

- [ ] Passes the litmus test: no business names; reusable for another application.
- [ ] Named for what it provides; not `<Noun>Actions`.
- [ ] `try` catches a specific type and never swallows; retries are bounded and used only for arrangement.
- [ ] Secrets only through `Secrets`; never in messages, logs or Config.
- [ ] No dependency on business classes or a test framework; no static open resources.
