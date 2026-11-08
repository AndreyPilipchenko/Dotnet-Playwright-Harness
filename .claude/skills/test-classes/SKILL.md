---
name: test-classes
description: Use when creating, naming, structuring or reviewing a test class (a FeatureTests file) in a Playwright C# framework built with this harness on NUnit, xUnit or MSTest - its base class, data field, per-test setup and teardown of Api or Db classes, categories, where tests and hooks sit in the file, or splitting a large test class.
---

# Test classes

A test class groups the tests of one feature. It derives from `UiTest`, loads its own data once,
sets up the services its tests need, and cleans up after each test.

Read [design-conventions](../pom-architecture/references/design-conventions.md) first: §1, §2
(test naming), §4 laws 4, 5, 9 and 10, §5 (setup and cleanup). The test-framework skill has the
hook attributes for each framework.

## 1. Name and file

- `<Feature>Tests` in `tests/<Name>.Tests/Ui/<Feature>Tests.cs` (or `Api/` for API-only tests), paired with `resources/testData/<Feature>TestData.json`. `[test-class-naming]` `[test-data-pairing]`
- One test class per file. The data records it uses may follow it in the same file.
- It derives from `UiTest`, the only base class. Never create a second base class or an intermediate one.
- MSTest needs `[TestClass]` on every test class. NUnit and xUnit need nothing.

## 2. Layout

The canonical order, shown for each framework (all three are from verified examples). Use the one
in `.harness/project.json`.

**NUnit**

```csharp
public class OrdersTests : UiTest
{
    private static readonly OrdersTestData Data = TestData.Load<OrdersTestData>("Orders");

    private OrdersApi _orders = null!;

    [TestCase("standard")]
    [TestCase("express")]
    [Category("regression")]
    public async Task ShowsNewOrderWithItsStatusAsync(string shipping) { /* see test-methods */ }

    [Test]
    [Category("smoke")]
    public async Task RemovesADeletedOrderAsync() { /* see test-methods */ }

    [SetUp]
    public async Task CreateOrdersApiAsync() => _orders = await OrdersApi.CreateAsync(Playwright);

    [TearDown]
    public async Task CleanUpAsync()
    {
        await _orders.DeleteCreatedOrdersAsync();
        await _orders.DisposeAsync();
    }
}
```

**xUnit v3**: the hooks are overrides of the base class's `InitializeAsync` and `DisposeAsync`.

```csharp
public class OrdersTests : UiTest
{
    private static readonly OrdersTestData Data = TestData.Load<OrdersTestData>("Orders");

    private OrdersApi _orders = null!;

    [Theory]
    [InlineData("standard")]
    [InlineData("express")]
    [Trait("Category", "regression")]
    public async Task ShowsNewOrderWithItsStatusAsync(string shipping) { /* see test-methods */ }

    [Fact]
    [Trait("Category", "smoke")]
    public async Task RemovesADeletedOrderAsync() { /* see test-methods */ }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _orders = await OrdersApi.CreateAsync(Playwright);
    }

    public override async ValueTask DisposeAsync()
    {
        await _orders.DeleteCreatedOrdersAsync();
        await _orders.DisposeAsync();
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
```

**MSTest v4**: `[TestClass]` on the class.

```csharp
[TestClass]
public class OrdersTests : UiTest
{
    private static readonly OrdersTestData Data = TestData.Load<OrdersTestData>("Orders");

    private OrdersApi _orders = null!;

    [TestMethod]
    [DataRow("standard")]
    [DataRow("express")]
    [TestCategory("regression")]
    public async Task ShowsNewOrderWithItsStatusAsync(string shipping) { /* see test-methods */ }

    [TestMethod]
    [TestCategory("smoke")]
    public async Task RemovesADeletedOrderAsync() { /* see test-methods */ }

    [TestInitialize]
    public async Task CreateOrdersApiAsync() => _orders = await OrdersApi.CreateAsync(Playwright);

    [TestCleanup]
    public async Task CleanUpAsync()
    {
        await _orders.DeleteCreatedOrdersAsync();
        await _orders.DisposeAsync();
    }
}
```

1. The `Data` field: `static readonly`, loaded from the paired file.
2. Per-test service fields, in layer order (Apis, then Dbs, then Helpers): `private OrdersApi _orders = null!;`.
3. The tests.
4. The hooks, at the bottom: setup, then teardown. In xUnit these are the `InitializeAsync` and `DisposeAsync` overrides.

## 3. Hooks

- **Per-test setup** creates the Api and Db objects the tests use, from the adapter's `Playwright` property. Page objects are created inside each test from `Page`, because each test gets a fresh page.
- **Per-test teardown** first deletes what the test created, through the same Api or Db class, then disposes it. Teardown runs after a failure too, so it needs no `try`.
- Hook forms per framework (`[SetUp]`, `InitializeAsync`, `[TestInitialize]`) are in the test-framework skill.
- **xUnit order:** `InitializeAsync` calls `base.InitializeAsync()` first, before it uses `Playwright`. `DisposeAsync` cleans up first, calls `base.DisposeAsync()` last, then `GC.SuppressFinalize(this)`, which analyzer rule CA1816 requires (docs/framework-matrix.md).
- Class-level setup (`[OneTimeSetUp]`, `IClassFixture<T>`, `[ClassInitialize]`) is not verified yet. Use per-test setup until it has been spiked and recorded in the framework matrix.

## 4. Categories

- Every test has at least one category from the project vocabulary: `smoke`, `regression`, or the list in `.harness/project.json`. `[category-vocabulary]`
- A test that automates a designed test case also gets `tc:<id>`, such as `[Category("tc:TC-012")]`. `Harness.Tools traceability` checks the link (test-case-design).
- A test of a confirmed product defect also gets `bug:<id>`, and stays red (design-conventions §6).
- CI picks tests by category, so a test without one is left out of the filtered runs.

## 5. What a test class never contains

- Locators, `Expect`/`Assert`, HTTP calls, `GotoAsync`, loops, `if`, `try`, console output. `[no-locator-in-test]` `[no-assertion-in-test]` `[no-http-in-test]` `[no-navigation-in-test]` `[test-logic]` `[no-try-outside-utils]` `[no-console-output]`
- Skips of any kind: `Ignore`, `Explicit`, `Skip =`, commented-out tests. `[no-skipped-test]` A test that can't run in an environment is a CI category choice, not a skip.
- Extension classes or helper methods. Shared steps go down a layer. `[extension-placement]`
- Attributes from a second test framework. `[mixed-test-frameworks]`

## 6. Splitting

Split a class when it covers two features, or when its data file holds unrelated groups. Each new
class gets its own `<Feature>Tests` name and data file. Don't split by test type (UI vs API) inside
one feature unless the setup differs.

## Boundaries

- The body of each test → test-methods
- Attributes, hooks and parallelism per framework → test-framework
- The data file and its records → test-data
- The Api and Db classes it sets up → service-classes

## Review checklist

- [ ] `<Feature>Tests` in a matching file, derives from `UiTest`, paired data file exists.
- [ ] Order: data field, service fields, tests, hooks.
- [ ] Setup creates services; teardown cleans up what tests created, then disposes.
- [ ] Every test has a vocabulary category; known defects carry `bug:<id>`.
- [ ] No locators, assertions, HTTP, logic, skips or second framework in the class.
