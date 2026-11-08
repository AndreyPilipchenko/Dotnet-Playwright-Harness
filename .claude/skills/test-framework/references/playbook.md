# Test framework: playbook

Everything here was verified by spike and is recorded in the harness's
`docs/framework-matrix.md`, with versions. Patterns marked **unverified** compile in principle
but have not been run. Check them in a scratch copy before relying on them, and record what
you find.

Design rules (no logic in tests, assertions only in `Verify*`, data from the paired JSON) are in
the pom-architecture skill's `design-conventions.md`. The examples below are trimmed to the
framework mechanics.

## 1. The base class

`tests/<Name>.Tests/Setup/UiTest.cs` is the only base class. It derives from the adapter's
`PageTest` (`Microsoft.Playwright.NUnit`, `.Xunit.v3` or `.MSTest`), which gives each test a
fresh browser context and a `Page`. The adapter also exposes `Playwright` (for API request
contexts), `Browser` and `Context`.

Change context options here, in `ContextOptions()`, never in runsettings:

```csharp
public override BrowserNewContextOptions ContextOptions() => new()
{
    Locale = "en-US",
    ViewportSize = new() { Width = 1280, Height = 720 },
    BaseURL = Environment.GetEnvironmentVariable("BASE_URL"),
};
```

## 2. NUnit

```csharp
public class OrdersTests : UiTest
{
    private OrdersApi _orders = null!;

    [SetUp]
    public async Task CreateOrdersApiAsync() => _orders = await OrdersApi.CreateAsync(Playwright);

    [TearDown]
    public async Task CleanUpAsync()
    {
        await _orders.DeleteCreatedOrdersAsync();
        await _orders.DisposeAsync();
    }

    [TestCase("standard")]
    [TestCase("express")]
    [Category("regression")]
    public async Task ShowsShippingOptionAsync(string option) { /* business calls */ }
}
```

- `[TestCase]` alone makes a test; don't add `[Test]` next to it.
- Parallelism: the template sets `[assembly: Parallelizable(ParallelScope.Fixtures)]` in `UiTest.cs`, so classes run in parallel and the tests in one class run in order. Mark a class `[NonParallelizable]` to run it alone.
- `NUnit.Analyzers` is on the approved list and runs in the build.

## 3. xUnit v3

```csharp
public class OrdersTests : UiTest
{
    private OrdersApi _orders = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();   // first: creates Page and Playwright
        _orders = await OrdersApi.CreateAsync(Playwright);
    }

    public override async ValueTask DisposeAsync()
    {
        await _orders.DeleteCreatedOrdersAsync();
        await _orders.DisposeAsync();
        await base.DisposeAsync();      // last
        GC.SuppressFinalize(this);      // CA1816 requires it under the strict build
    }

    [Theory]
    [InlineData("standard")]
    [InlineData("express")]
    [Trait("Category", "regression")]
    public async Task ShowsShippingOptionAsync(string option) { /* business calls */ }
}
```

- xunit.v3 adds no global `using Xunit;`. The template's test files have it; add it to each new test file.
- xUnit creates a new class instance per test, so per-test state lives in fields set in `InitializeAsync`.
- Parallelism: classes run in parallel by default, and the tests in one class run in order. To run a class alone, put it in a collection with parallelism off:

```csharp
[CollectionDefinition("Serial", DisableParallelization = true)]
public sealed class SerialGroup;   // not "...Collection": CA1711 rejects that suffix

[Collection("Serial")]
public class CheckoutLimitsTests : UiTest { … }
```

- The filter is `--filter-trait "Category=x"`, not `--filter`.

## 4. MSTest v4

```csharp
[TestClass]
public class OrdersTests : UiTest
{
    private OrdersApi _orders = null!;

    [TestInitialize]
    public async Task CreateOrdersApiAsync() => _orders = await OrdersApi.CreateAsync(Playwright);

    [TestCleanup]
    public async Task CleanUpAsync()
    {
        await _orders.DeleteCreatedOrdersAsync();
        await _orders.DisposeAsync();
    }

    [TestMethod]
    [DataRow("standard")]
    [DataRow("express")]
    [TestCategory("regression")]
    public async Task ShowsShippingOptionAsync(string option) { /* business calls */ }
}
```

- Every test class needs `[TestClass]`; a class without it is not discovered.
- Data rows go on a `[TestMethod]`.
- Parallelism: the template sets `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]` in `UiTest.cs`, so individual tests run in parallel. Tests in one class must not share state through fields set once. Mark a class `[DoNotParallelize]` to run it alone.

## 5. A known product defect

Add `bug:<id>` next to the normal category, in the framework's own attribute:
`[Category("bug:BUG-17")]`, `[Trait("Category", "bug:BUG-17")]` or `[TestCategory("bug:BUG-17")]`.
The test stays red; never skip it (design-conventions §6). Skip attributes and properties
(`[Ignore]`, `[Explicit]`, `Skip =`, `Assert.Ignore`, `Assert.Inconclusive`) fail
`check-conventions` as `no-skipped-test`.

## 6. Reading a run

- Exit code 2 means test failures. 5 means the command line is wrong: an unknown option, `--filter` on xUnit, `--settings` on xUnit, or `--report-trx` without the TRX package.
- The summary is a block at the end: `Test run summary: Failed!`, then `total:`, `failed:`, `succeeded:`, `skipped:`.
- Each failing module prints `failed with N error(s)`, followed by the Playwright message (`Locator expected to have text …`) and the aria snapshot of the element.
- The TRX file goes to the `--results-directory`. Playwright traces, when the base class turns them on, go wherever the base class writes them.
- Don't rerun an unchanged failing command. The harness blocks that until a file has been edited. Read the failure first.

## 7. Porting a test between frameworks

Map each attribute with the quick map in `SKILL.md` and move per-test setup into the target's
hook form (§2–§4). Page, Api and Db classes don't change: they never reference a test
framework. A project still uses only one framework, so porting means porting all of it, and
updating `.harness/project.json` and the test project's packages, with the user's approval.

## 8. Not verified yet

Spike these and record them in the matrix before you rely on them:

- That `[NonParallelizable]`, `[Collection]` with parallelism off and `[DoNotParallelize]` really serialize. So far only compiling and passing was checked.
- Class-level setup: `[OneTimeSetUp]`, `IClassFixture<T>`, `[ClassInitialize]`.
- Data from a source method: `TestCaseSource`, `MemberData`, `DynamicData`.
- Firefox, WebKit, Linux, and SDK versions other than 10.
