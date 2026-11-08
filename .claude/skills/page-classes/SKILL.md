---
name: page-classes
description: Use when creating, splitting, extending or reviewing a page class in a Playwright C# test framework built with this harness - deciding whether a new page class is needed, modelling a shared area such as a header or menu, member order and section comments, constructors, NavigateAsync, or replacing an inherited base page with composition.
---

# Page classes

A page class is the only place that knows how one page, or one shared area, is built. Tests and
helpers talk to it through business actions and `Verify*` methods.

Read [design-conventions](../pom-architecture/references/design-conventions.md) first: §1 (no
base page, layers), §2 (naming), §3 (class anatomy), §4 laws 2, 6 and 11.

## 1. One class per page, and one per shared area

- One class per routable page: `LoginPage`, `OrdersPage`.
- One class per area that appears on many pages: `HeaderPage`, `CartDrawerPage`. Several pages use it; none inherits it.
- **Create a class only when a current test needs it** (iron law 11). First search `Pages/` for one that already covers the area, and extend it.
- One public class per file, and the file name equals the class name.

## 2. Fixed anatomy

```csharp
public sealed class OrdersPage
{
    private const string Url = "/orders";

    private readonly IPage _page;

    public OrdersPage(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _page = page;
    }

    private ILocator OrdersTable => _page.GetByRole(AriaRole.Table, new() { Name = "Orders" });

    private ILocator OrderRow(string id) => OrdersTable.GetByRole(AriaRole.Row).Filter(new() { HasText = id });

    private ILocator OrderStatusCell(string id) => OrderRow(id).GetByRole(AriaRole.Cell).Last;

    // Actions

    public Task NavigateAsync() => _page.GotoAsync(Url);

    // Validations

    public Task VerifyOrderStatusAsync(string id, string status) =>
        Expect(OrderStatusCell(id)).ToHaveTextAsync(status, new() { Timeout = Timeouts.SlowSurface });
}
```

The order is: constants, fields, constructor, locator properties, locator methods,
`// Actions` (`NavigateAsync` first), `// Validations`. Members are separated by exactly one blank
line. `[anatomy-spacing]`

- **`sealed`**, with a constructor that only checks and stores its arguments. It never navigates, waits or reads data.
- **Routable pages have `NavigateAsync`,** which goes to the page's own relative `Url`. `BASE_URL` comes from `UiTest`. Only `NavigateAsync` calls `GotoAsync`. `[page-goto-owner]` A page you reach only by clicking, like `HeaderPage`, has no `NavigateAsync`.
- **Methods return `Task`,** or `Task<T>` when the test really uses the value. Never return `this` or another page object. Tests construct the pages they use: `new OrdersPage(Page)`.

## 3. Compose, never inherit

```csharp
public sealed class CheckoutActions
{
    private readonly LoginPage _loginPage;

    private readonly HeaderPage _headerPage;

    public CheckoutActions(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _loginPage = new LoginPage(page);
        _headerPage = new HeaderPage(page);
    }
}
```

- There is no `BasePage`. A page that needs the header's behaviour holds a `HeaderPage`, or the test uses both. `[no-base-page]`
- A flow across pages belongs in `Helpers/<Noun>Actions`, which composes pages and has no locators of its own.
- Implementing an interface is fine. A shared base class is not.

## 4. Business services, not element wrappers

- Expose what a user does: `LoginAsync(email, password)`, `DeleteOrderAsync(id)`. Don't expose `ClickSignInAsync`, `FillEmailAsync` and `FillPasswordAsync` for a test to string together.
- A small page still gets the full anatomy. A page with one locator and one action is fine.
- No `try`/`catch`, no branching on page state, no loops. `[no-try-outside-utils]`
- No dead members. Delete a locator or method once nothing uses it.

## 5. Legacy pages

When you touch an existing page that breaks these rules, fix what you touch and leave the rest
for later; `check-conventions --write-baseline` records the rest (design-conventions §10). Never
rewrite a working page in passing.

## Boundaries

- Choosing and naming each locator → element-locators
- What goes inside an action → action-methods
- What goes inside a `Verify*` method → validation-methods
- API and DB classes → service-classes

## Review checklist

- [ ] The class is needed by a current test, and no existing page covers the area.
- [ ] `sealed`; the anatomy order holds, with both section comments and single blank lines.
- [ ] Routable pages have `NavigateAsync`; `GotoAsync` appears nowhere else.
- [ ] No base page; shared areas are composed.
- [ ] Methods are business services returning `Task` or a consumed `Task<T>`.
- [ ] No `try`, no branching, no dead members.
