# Framework design conventions (C#)

The shared law for a Playwright + C# test framework built with this harness. Every
specialist skill builds on this file and must not contradict it. When a playbook and this
file disagree, this file wins. When this file and the project's code disagree on a style
detail, follow the file you are editing and raise the mismatch.

Rules marked with a rule id, such as `[no-hard-wait]`, are enforced by
`Harness.Tools check-conventions`. The others are checked in review.

Adapted from the Playwright POM Harness design conventions (MIT, see `.harness/NOTICE.md`),
rewritten for C# and .NET.

---

## 1. Architecture

```
src/<Name>.Framework/          framework-neutral: references Microsoft.Playwright, never a test framework
  Pages/     <Name>Page        one class per page or shared area (HeaderPage)
  Apis/      <Domain>Api       API business operations
  Dbs/       <Domain>Db        DB business operations
  Helpers/   <Noun>Actions     a business flow that spans several pages or services
  Utils/                       technical plumbing: TestData, retries, HTTP and DB connections, logging
    Extensions/  <Type>Extensions   extension members on Playwright and BCL types (§8)
  Config/                      environment coordinates and named Timeouts; never secrets
tests/<Name>.Tests/            the only project that references NUnit, xUnit or MSTest
  Setup/     UiTest            the one base class, wrapping the adapter's PageTest
  Ui/  Api/  <Feature>Tests    test classes
resources/testData/            <Feature>TestData.json, one per test class
```

- **Business family:** Pages, Apis, Dbs, Helpers. These describe *what* the user or system does.
- **Technical family:** Utils, Config. These describe *how* it is done reliably.
- **Dependencies point down only:** Tests → Helpers → Pages, Apis, Dbs → Utils, Config.
  - The framework project never uses a test framework or the tests' namespaces.
  - Utils and Config never use business classes. `[layer-boundary]`
- **No base page.** Pages compose what they need. A shared area such as a header or menu gets its own page class. Implementing an interface is fine. `[no-base-page]`
  - The only base class is `UiTest` in `tests/<Name>.Tests/Setup/`. It wraps the adapter's `PageTest`, one per framework, and sets context options in code.
- **Folder names are exactly the ones above:** `Apis`, not `Api`; `Pages`, not `PageObjects`. The checker picks a file's rules by its folder, so a misnamed folder silently escapes them.
- **Layers are created on first need.** Never scaffold empty folders. When the first test of a new kind arrives, create its canonical folder; don't park a DB class in `Apis/` because that folder exists.
- **Tests orchestrate; they never implement.** A test reads as a sequence of business calls. If it needs a locator, an HTTP call, a loop or an `if`, push that work down a layer. HTTP belongs to an `<Domain>Api` class. `[no-http-in-test]`

## 2. Naming

| Identifier | Rule | Examples |
|---|---|---|
| Class | PascalCase noun with its role suffix | `LoginPage`, `HeaderPage`, `OrdersApi`, `OrdersDb`, `CheckoutActions` |
| Method | PascalCase verb phrase. Anything returning `Task` or `ValueTask` ends in `Async` `[async-naming]` | `LoginAsync`, `CreateOrderAsync`, `VerifyErrorMessageAsync` |
| Validation method | Starts with `Verify` | `VerifyOrderStatusAsync` |
| Navigation | `NavigateAsync` on the page it opens. Tests never call `GotoAsync` `[no-navigation-in-test]`, and a page calls it only inside its own `NavigateAsync` `[page-goto-owner]` | `loginPage.NavigateAsync()` |
| Locator | Private property (or method returning `ILocator`), `<Name><Suffix>` from the closed list below `[locator-naming]` `[locator-visibility]` | `EmailInput`, `SignInButton`, `ErrorMessage`, `OrderRow(id)` |
| Private field | `_camelCase` | `_page`, `_request` |
| Constant | PascalCase | `Url`, `SlowSurface` |
| Interface | `I` prefix (C# idiom) | `IOrderSource` |
| Extension class and file | `<ExtendedType>Extensions` in `Utils/Extensions/<ExtendedType>Extensions.cs` `[extension-naming]` (§8) | `ApiResponseExtensions`, `LocatorExtensions` |
| Test class and file | `<Feature>Tests` in `<Feature>Tests.cs` `[test-class-naming]` | `LoginTests.cs` |
| Test method | What the system does, ending in `Async` | `ShowsErrorForWrongPasswordAsync` |
| Test data | `resources/testData/<Feature>TestData.json` (camelCase folder, never `test-data`) `[test-data-pairing]` `[test-data-folder]` | `LoginTestData.json` |
| Category | `smoke`, `regression`, or the project's list in `.harness/project.json`; plus the links `tc:<id>` (the test case it automates) and `bug:<id>` (a known defect) `[category-vocabulary]` | NUnit `[Category("tc:TC-012")]`, xUnit `[Trait("Category", "tc:TC-012")]`, MSTest `[TestCategory("tc:TC-012")]` |

**Locator suffixes (closed list):** `Button`, `Input`, `Link`, `Header`, `Heading`, `Text`,
`Image`, `Icon`, `Checkbox`, `Radio`, `Dropdown`, `List`, `Table`, `Row`, `Cell`, `Form`,
`Message`, `Tab`, `Card`, `Modal`, `Section`.

- Name the element kind, not its ARIA role. An alert is an `ErrorMessage`; a combobox is a `CountryDropdown`.
- `Input` covers every fillable control, including text areas.
- Adding a suffix changes this file and the checker together; never invent one in a single page.
- Declare each locator once, as a member. Actions use the member, never an inline `_page.GetBy…` or `_page.Locator(…)` in a method body. `[locator-in-method-body]`
- Prefer `GetByRole`, `GetByLabel` and `GetByTestId`. When an id is the only handle, use the CSS form `#email`, never the `id=` engine prefix. `[no-id-engine-prefix]`

## 3. Class anatomy

Every business class uses this order and the two section comments exactly as written (checked
in review). Members are separated by exactly one blank line. `[anatomy-spacing]`

1. Constants (`Url`), then private readonly fields (`_page`).
2. Constructor. It checks its arguments and stores them.
3. Locator properties, then locator methods (parameterized, returning `ILocator`).
4. `// Actions`. `NavigateAsync` comes first, then the other actions.
5. `// Validations`. Every `Verify*` method, always last.

```csharp
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Acme.Shop.Framework.Pages;

public sealed class LoginPage
{
    private const string Url = "/login";

    private readonly IPage _page;

    public LoginPage(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _page = page;
    }

    private ILocator EmailInput => _page.GetByLabel("Email");

    private ILocator PasswordInput => _page.GetByLabel("Password");

    private ILocator SignInButton => _page.GetByRole(AriaRole.Button, new() { Name = "Sign in" });

    private ILocator ErrorMessage => _page.GetByRole(AriaRole.Alert);

    // Actions

    public Task NavigateAsync() => _page.GotoAsync(Url);

    public async Task LoginAsync(string email, string password)
    {
        await EmailInput.FillAsync(email);
        await PasswordInput.FillAsync(password);
        await SignInButton.ClickAsync();
    }

    // Validations

    public Task VerifyErrorMessageAsync(string expected) => Expect(ErrorMessage).ToHaveTextAsync(expected);
}
```

The test that uses it, with its data. Only the attributes differ between frameworks; use the one
in `.harness/project.json`.

**NUnit**

```csharp
public class LoginTests : UiTest
{
    private static readonly LoginTestData Data = TestData.Load<LoginTestData>("Login");

    [Test]
    [Category("regression")]
    public async Task ShowsErrorForWrongPasswordAsync()
    {
        var loginPage = new LoginPage(Page);

        await loginPage.NavigateAsync();
        await loginPage.LoginAsync(Data.WrongPassword.Email, Data.WrongPassword.Password);
        await loginPage.VerifyErrorMessageAsync(Data.WrongPassword.Error);
    }
}

public sealed record LoginTestData(LoginCase WrongPassword);

public sealed record LoginCase(string Email, string Password, string Error);
```

**xUnit v3**

```csharp
public class LoginTests : UiTest
{
    private static readonly LoginTestData Data = TestData.Load<LoginTestData>("Login");

    [Fact]
    [Trait("Category", "regression")]
    public async Task ShowsErrorForWrongPasswordAsync()
    {
        var loginPage = new LoginPage(Page);

        await loginPage.NavigateAsync();
        await loginPage.LoginAsync(Data.WrongPassword.Email, Data.WrongPassword.Password);
        await loginPage.VerifyErrorMessageAsync(Data.WrongPassword.Error);
    }
}
```

**MSTest v4**

```csharp
[TestClass]
public class LoginTests : UiTest
{
    private static readonly LoginTestData Data = TestData.Load<LoginTestData>("Login");

    [TestMethod]
    [TestCategory("regression")]
    public async Task ShowsErrorForWrongPasswordAsync()
    {
        var loginPage = new LoginPage(Page);

        await loginPage.NavigateAsync();
        await loginPage.LoginAsync(Data.WrongPassword.Email, Data.WrongPassword.Password);
        await loginPage.VerifyErrorMessageAsync(Data.WrongPassword.Error);
    }
}
```

The data records are the same in all three, and so is the JSON:

```json
{ "wrongPassword": { "email": "qa@example.com", "password": "not-the-password", "error": "Invalid email or password" } }
```

The test-framework skill has the full attribute mapping.

## 4. Iron laws

1. **Separation.** Business code lives in Pages, Apis, Dbs and Helpers. Technical code lives in Utils.
2. **Encapsulation.** Locators are private and never leave their page. Tests and other classes use actions and `Verify*` methods only. `[locator-visibility]` `[no-locator-in-test]`
3. **No exception handling outside Utils.** Let failures fail: the Playwright error and the trace tell the story. `[no-try-outside-utils]`
4. **No logic in tests.** Tests have no `if`, `switch`, loops or `?:`. `[test-logic]`
   - Business classes keep logic minimal; prefer small intent-named methods over general branching.
   - A case that varies by data becomes one data row per case: `[TestCase]`, `[InlineData]` or `[DataRow]`, or a source method.
5. **Externalized data.** Test inputs and expected values come from the test class's own JSON, loaded with `TestData.Load<T>("<Feature>")`. `[test-data-source]`
   - Business classes take them as parameters and never load test data. `[no-test-data-in-business-layer]`
   - A page's own `Url` constant is structure, not data.
   - Externally provisioned credentials and API keys come from environment variables or user-secrets, never from source or JSON. `[secret-literal]`
   - Fixed synthetic values, including deliberately invalid passwords, belong in the JSON.
6. **Actions act; validations verify.**
   - Every assertion lives in a `Verify*` method on a business class, using web-first `Assertions.Expect`. `[no-assertion-in-test]` `[action-verify-separation]`
   - A value with no web-first form, such as a database count, is compared with `Checks.Equal(expected, actual, what)` from Utils. It throws `CheckFailedException`, which every test framework reports as a failure. The framework project never references a test framework's `Assert`.
   - Actions never assert, and `Verify*` methods never click or fill.
   - Each test has at least one `Verify*` call.
7. **No fixed waits.** `Thread.Sleep`, `Task.Delay` and `WaitForTimeoutAsync` are banned outside Utils. `[no-hard-wait]`
   - Wait for a condition: auto-waiting actions, `Expect(...)`, or `WaitForAsync`.
   - A slow surface gets a named timeout from `Config/Timeouts.cs`, never a number in place. `[timeout-literal]`
8. **Async all the way.** Never use `.Result`, `.Wait()`, `GetAwaiter().GetResult()` or `async void`. `[async-discipline]`
9. **Independence.** Every test runs alone, in parallel and repeatedly.
   - `PageTest` gives each test a fresh context.
   - Data is unique per test and per run.
   - Arrange state through Apis or Dbs whenever such a path exists; the GUI is for the behaviour under test.
   - Clean up what the test creates, in teardown, through the same lower layer.
   - Leave no handler behind. A lambda added to `Page.Dialog` can't be removed, so it handles every later dialog too. Use a named handler: add it, run the action, remove it. `[no-persistent-dialog-handler]`
     Without any handler, Playwright dismisses dialogs automatically.
10. **Tests are never weakened to go green.** No `Ignore`, `Explicit`, `Skip =`, `Assert.Ignore`, `Assert.Inconclusive`, or "until" dates. `[no-skipped-test]`
    - Never loosen an assertion, raise a timeout, add a retry or delete the check to hide a defect.
    - See §6 for what to do with a known product defect.
11. **Build only what the test needs.** Before adding a method, search Pages, Apis, Dbs and Helpers for one with the same intent, and reuse or extend it. One test does not justify a new abstraction.
12. **One framework, public APIs only.**
    - A project uses the test framework recorded in `.harness/project.json`. `[mixed-test-frameworks]`
    - Never use Playwright's internal namespaces. `[playwright-internal-api]`
    - No console output; it is lost in parallel runs. `[no-console-output]`

## 5. Arranging state through the API, and cleaning up

A test that needs data creates it through an `<Domain>Api` class and removes it afterwards.
It never relies on records that happen to exist.

- The Api class owns its request context. A Utils helper creates the context from environment variables, so a test only ever holds the business class.
- Arrangement fails loudly: a failed create throws with the status, it never asserts. Assertions belong in `Verify*` methods.

```csharp
// src/<Name>.Framework/Utils/ApiContext.cs (technical: connection details from the environment)
public static class ApiContext
{
    public static Task<IAPIRequestContext> CreateAsync(IPlaywright playwright, string header, string variable)
    {
        ArgumentNullException.ThrowIfNull(playwright);
        var key = Environment.GetEnvironmentVariable(variable)
            ?? throw new InvalidOperationException($"Set the {variable} environment variable.");
        return playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = Environment.GetEnvironmentVariable("BASE_URL"),
            ExtraHTTPHeaders = new Dictionary<string, string> { [header] = key },
        });
    }
}
```

```csharp
// src/<Name>.Framework/Apis/OrdersApi.cs (business: operations only, no test framework)
public sealed class OrdersApi : IAsyncDisposable
{
    private readonly IAPIRequestContext _request;
    private readonly List<string> _created = [];

    private OrdersApi(IAPIRequestContext request) => _request = request;

    public static async Task<OrdersApi> CreateAsync(IPlaywright playwright) =>
        new(await ApiContext.CreateAsync(playwright, "X-Api-Key", "SHOP_API_KEY"));

    // Actions

    public async Task<string> CreateOrderAsync(string product, int quantity)
    {
        var response = await _request.PostAsync("/api/orders", new() { DataObject = new { product, quantity } });
        var body = response.Ok ? await response.JsonAsync() : null;
        var id = body?.GetProperty("id").GetString()
            ?? throw new InvalidOperationException($"POST /api/orders failed with {response.Status}.");
        _created.Add(id);
        return id;
    }

    public async Task DeleteCreatedOrdersAsync()
    {
        foreach (var id in _created)
        {
            await _request.DeleteAsync($"/api/orders/{id}");
        }

        _created.Clear();
    }

    public ValueTask DisposeAsync() => _request.DisposeAsync();
}
```

The hooks in the test class, `tests/<Name>.Tests/Ui/OrdersTests.cs`:

**NUnit**

```csharp
private OrdersApi _orders = null!;

[SetUp]
public async Task CreateOrdersApiAsync() => _orders = await OrdersApi.CreateAsync(Playwright);

[TearDown]
public async Task CleanUpAsync()
{
    await _orders.DeleteCreatedOrdersAsync();
    await _orders.DisposeAsync();
}
```

**xUnit v3**: override the base hooks. Call `base.InitializeAsync()` first and `base.DisposeAsync()` last; `GC.SuppressFinalize(this)` satisfies CA1816.

```csharp
private OrdersApi _orders = null!;

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
```

**MSTest v4**

```csharp
private OrdersApi _orders = null!;

[TestInitialize]
public async Task CreateOrdersApiAsync() => _orders = await OrdersApi.CreateAsync(Playwright);

[TestCleanup]
public async Task CleanUpAsync()
{
    await _orders.DeleteCreatedOrdersAsync();
    await _orders.DisposeAsync();
}
```

- Hooks need no `try`: the framework runs teardown after a failure and reports both errors.
- Cleanup deletes only what this test created, and stays a business method on the Api class.

## 6. A known product defect

A test that fails because the product is wrong is **working**. Keep it, keep it red:

1. Write the test against the documented behaviour, exactly as if the product were fixed.
2. Add the category `bug:<id>` next to its normal category: `[Category("bug:BUG-17")]` (NUnit), `[Trait("Category", "bug:BUG-17")]` (xUnit) or `[TestCategory("bug:BUG-17")]` (MSTest).
3. The bug report is `docs/qa/bug-reports/BUG-<n>.md` (the bug-report skill), and `Harness.Tools traceability` checks that the category names it. Optionally link an external issue in the test's XML doc comment.
4. CI reports `bug:` failures separately as known failures, so new failures still stand out. When the fix ships, the test turns green on its own.

| Thought | Reality |
|---|---|
| "Skip it so CI stays green and useful" | CI already separates known failures by the `bug:` category. A skipped test cannot tell you the fix shipped. |
| "`Ignore` keeps it visible as skipped" | Skips are read as "nothing to see". The red test with a bug link is the visible record. |
| "`Ignore(Until = …)` means it can't be forgotten" | It hides the defect until a date someone guessed. Dates are not fixes. |
| "Mark it `Explicit` so it only runs on demand" | Then it never runs. Same as a skip. |
| "Assert the current broken behaviour for now" | That test passes on the defect and fails on the fix. It is wrong twice. |

## 7. Targeting one record in a list

When a test needs a specific row or card:

- Select it by an exact identity inside the row, such as an order id: `OrderRow(id) => OrdersTable.GetByRole(AriaRole.Row).Filter(new() { HasText = id })`.
  - Never use the first or nth row.
  - Duplicates need another known identifier from the same row.
- Prefer the page's own search when it really covers the identifier. One successful search on the first page proves nothing about other pages.
- Wait for the results that belong to the query, using `Expect`. An empty list during loading is not absence.
- An "every row" check is one validation that names the rows that must not exist, never a loop:
  a locator `UncheckedRowCheckbox => OrdersTable.GetByRole(AriaRole.Checkbox, new() { Checked = false })`
  and `Expect(UncheckedRowCheckbox).ToHaveCountAsync(0)`. Seed the rows it checks (§5) so the
  check is not passing on an empty table.
- A slow surface uses a named budget, which lives in Config:

```csharp
// src/<Name>.Framework/Config/Timeouts.cs
public static class Timeouts
{
    /// <summary>The orders table on the test environment can take 40 s to load.</summary>
    public const float SlowSurface = 60_000;
}

// In OrdersPage: locators are declared once, the validation uses them.
private ILocator OrderStatusCell(string id) => OrderRow(id).GetByRole(AriaRole.Cell).Last;

public Task VerifyOrderStatusAsync(string id, string status) =>
    Expect(OrderStatusCell(id)).ToHaveTextAsync(status, new() { Timeout = Timeouts.SlowSurface });
```

## 8. Extension members

Extension members add reusable technical helpers to types you don't own, such as
`IAPIResponse`, `ILocator` or `string`. They are Utils code: plumbing that makes business classes
shorter, never business behaviour of their own.

- **Where:** only in `src/<Name>.Framework/Utils/Extensions/`, never in Pages, Apis, Dbs, Helpers, Config or the tests project. `[extension-placement]`
- **Naming:** one `public static class <ExtendedType>Extensions` per extended type, in a file with the same name. `[extension-naming]`
  - Members follow §2: PascalCase, and `Async` on anything returning `Task` or `ValueTask`.
- **Form:** classic static methods with a `this` parameter are the default. They work for every target framework.
  - A C# 14 `extension(T receiver) { … }` block is allowed when you need an extension **property**. Only `net10.0` is verified.
  - Both forms are checked the same way.
- **What belongs in one:** a generic step that any page or Api class could use with its own arguments.
  - Raising a clear error from a failed response (the "fail loudly" rule from §5).
  - Reading a value out of a JSON body.
  - Replacing the text of any input.
- **What never belongs in one:**
  - **A specific element.** An extension that passes a literal label, name, test id or selector to `GetBy…` or `Locator` is a page member in disguise. It leaks a locator that iron law 2 keeps private. Declare it in its page. Generic helpers take the selector or role as a parameter, or filter by an argument. `[no-element-locator-extension]`
  - **A business step**, such as `page.LoginAsync(…)`. It belongs to the page or Api class that owns it.
  - **An assertion.** Checks live in `Verify*` methods (iron law 6). An extension that finds a problem throws. `[no-assertion-in-extension]`
  - **Test data, business types, or a test framework.** Utils sits below the business layer (§1).

```csharp
// src/<Name>.Framework/Utils/Extensions/ApiResponseExtensions.cs
public static class ApiResponseExtensions
{
    public static async Task<string> ReadIdAsync(this IAPIResponse response, string operation)
    {
        ArgumentNullException.ThrowIfNull(response);
        var body = response.Ok ? await response.JsonAsync() : null;
        return body?.GetProperty("id").GetString()
            ?? throw new InvalidOperationException($"{operation} failed with {response.Status}.");
    }
}
```

With it, `OrdersApi.CreateOrderAsync` in §5 shrinks to the request, `ReadIdAsync("POST /api/orders")`, and recording the id.

## 9. Mapping from the TypeScript conventions

These differences are settled. Do not reintroduce the TypeScript form.

| TypeScript / Java form | C# canon | Why |
|---|---|---|
| `email_input`, `login_button` | `EmailInput`, `SignInButton`, private properties | .NET naming; the suffix vocabulary is kept |
| `verify*()` returning `Promise<void>` | `Verify*Async()` returning `Task` | async naming; no fluent `return this` |
| `<Feature>TestJsonFile.json` | `<Feature>TestData.json` | Shorter, matches `TestData.Load` |
| `test.describe` and fixtures | A test class deriving from `UiTest` | One base class per framework wraps the adapter |
| Tags `@smoke` | Categories `smoke`, `regression`, `bug:<id>` | Framework category attributes drive `dotnet test` filters |
| `playwright.config.ts` | `UiTest.ContextOptions()` plus `BROWSER`, `HEADED` and `BASE_URL` environment variables | runsettings are ignored under Microsoft Testing Platform |
| An Allure step per method | Intent-named methods; traces and TRX as the baseline. Allure is optional (reporting skill) | Allure packages differ per framework and some are previews |
| `Expects.ts` assertion facade | `Assertions.Expect` inside `Verify*` methods | The method name carries the business meaning |
| `ApiActions` facade in utils | `<Domain>Api` classes over `IAPIRequestContext`; shared plumbing in Utils | Same split, .NET types |

## 10. Exemptions and legacy

- A deliberate exception carries `// conventions-ok: <reason>` on the line, or on the line above. The reason is required; reviewers read every one.
  - `secret-literal` and `playwright-internal-api` cannot be exempted.
- A project adopting the harness can record its existing findings once with `check-conventions --write-baseline`. They are then reported as legacy and never hide a new finding.
  - Fix legacy code when you touch it for another reason. Don't mass-rename working code.

## 11. Review checklist

- [ ] Each new class is in the right layer and folder; dependencies point down.
- [ ] Names follow §2, including the locator suffixes and `Async`.
- [ ] Anatomy follows §3: order, the two section comments, single blank lines.
- [ ] Routable pages have `NavigateAsync`; locators are private.
- [ ] Tests contain only business calls; every assertion is in a `Verify*` method; each test verifies at least once.
- [ ] Inputs and expected values come from the paired JSON; secrets come from the environment.
- [ ] No fixed waits; slow surfaces use named timeouts.
- [ ] Extension members are generic plumbing in `Utils/Extensions/` (§8): no specific elements, business steps or assertions.
- [ ] State is arranged and cleaned up through Apis or Dbs where possible; tests are independent and parallel-safe.
- [ ] Nothing is skipped or weakened; known product defects are red, linked and categorized `bug:<id>`.
- [ ] New methods were checked against existing ones; only what the tests need was built.
- [ ] `check-conventions` is clean, or each finding has a reviewed `conventions-ok` reason.
