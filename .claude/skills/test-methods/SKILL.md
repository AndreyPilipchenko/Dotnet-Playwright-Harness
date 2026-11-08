---
name: test-methods
description: Use when writing, naming or reviewing a single test method in a Playwright C# framework built with this harness - what its body may contain, its name, data rows for variants, using values returned by an Api action, making it independent and parallel-safe, or removing logic, locators and assertions from a test.
---

# Test methods

A test reads like a short script of business steps that ends in at least one check. Everything
else lives one layer down.

Read [design-conventions](../pom-architecture/references/design-conventions.md) first: §4 laws
4–6, 9 and 10, §6 (known defects).

## 1. One test, one behaviour

Use the samples for the framework in `.harness/project.json`. Only the attributes differ; the body
is the same in all three.

**NUnit**

```csharp
[Test]
[Category("smoke")]
public async Task RemovesADeletedOrderAsync()
{
    var id = await _orders.CreateOrderAsync(Data.Orders["standard"].Product, 1);
    var checkout = new CheckoutActions(Page);
    var ordersPage = new OrdersPage(Page);

    await checkout.SignInAndOpenOrdersAsync(Data.User.Email, Secrets.Get("SHOP_PASSWORD"));
    await ordersPage.DeleteOrderAsync(id);
    await ordersPage.VerifyOrderAbsentAsync(id);
}
```

**xUnit v3**

```csharp
[Fact]
[Trait("Category", "smoke")]
public async Task RemovesADeletedOrderAsync()
{
    var id = await _orders.CreateOrderAsync(Data.Orders["standard"].Product, 1);
    var checkout = new CheckoutActions(Page);
    var ordersPage = new OrdersPage(Page);

    await checkout.SignInAndOpenOrdersAsync(Data.User.Email, Secrets.Get("SHOP_PASSWORD"));
    await ordersPage.DeleteOrderAsync(id);
    await ordersPage.VerifyOrderAbsentAsync(id);
}
```

**MSTest v4** (the class around it carries `[TestClass]`, see test-classes)

```csharp
[TestMethod]
[TestCategory("smoke")]
public async Task RemovesADeletedOrderAsync()
{
    var id = await _orders.CreateOrderAsync(Data.Orders["standard"].Product, 1);
    var checkout = new CheckoutActions(Page);
    var ordersPage = new OrdersPage(Page);

    await checkout.SignInAndOpenOrdersAsync(Data.User.Email, Secrets.Get("SHOP_PASSWORD"));
    await ordersPage.DeleteOrderAsync(id);
    await ordersPage.VerifyOrderAbsentAsync(id);
}
```

- **Name** what the system does, ending in `Async`: `RemovesADeletedOrderAsync`, `ShowsErrorForWrongPasswordAsync`. The name is the test's documentation; a reader shouldn't need the body to know what fails.
- **Shape:** arrange (seed through an Api, create page objects), then act, then verify. A blank line between arrange and the rest is enough; no comments are needed.
- **One behaviour:** if the name needs "and", it is probably two tests.

## 2. The body is business calls only

Allowed: `new SomePage(Page)`, calls to actions and `Verify*` methods, `Data.…` reads, `Secrets.Get`,
`UniqueData.…`, and local variables holding values returned by an action.

Not allowed, each reported by `check-conventions`:

| In a test | Move it to |
|---|---|
| `Page.GetByRole(…)`, `Page.Locator(…)` `[no-locator-in-test]` | a locator and an action on the page class |
| `Expect(…)`, `Assert.…`, `.Should()` `[no-assertion-in-test]` | a `Verify*` method |
| `Page.GotoAsync(…)` `[no-navigation-in-test]` | the page's `NavigateAsync` |
| `HttpClient`, `APIRequest`, `FetchAsync` `[no-http-in-test]` | an `<Domain>Api` class |
| `if`, `switch`, loops, `?:` `[test-logic]` | data rows, or a method one layer down |
| a `??` fallback (not reported, but still logic) | the data, or `Secrets.Get`, which fails loudly instead of falling back |
| `try`/`catch` `[no-try-outside-utils]` | nowhere; let it fail |
| `Thread.Sleep`, `Task.Delay` `[no-hard-wait]` | an auto-waiting action or a `Verify*` |
| literal inputs or expected values | the paired JSON (test-data) |

## 3. At least one `Verify*`

Every test calls at least one `Verify*` method. A test that only acts proves nothing: it passes
when the product ignores the action.

## 4. Variants are data rows

**NUnit**

```csharp
[TestCase("standard")]
[TestCase("express")]
[Category("regression")]
public async Task ShowsNewOrderWithItsStatusAsync(string shipping)
{
    var order = Data.Orders[shipping];
    var id = await _orders.CreateOrderAsync(order.Product, order.Quantity);
    var checkout = new CheckoutActions(Page);
    var ordersPage = new OrdersPage(Page);

    await checkout.SignInAndOpenOrdersAsync(Data.User.Email, Secrets.Get("SHOP_PASSWORD"));
    await ordersPage.VerifyOrderStatusAsync(id, order.Status);
}
```

**xUnit v3**

```csharp
[Theory]
[InlineData("standard")]
[InlineData("express")]
[Trait("Category", "regression")]
public async Task ShowsNewOrderWithItsStatusAsync(string shipping)
{
    var order = Data.Orders[shipping];
    var id = await _orders.CreateOrderAsync(order.Product, order.Quantity);
    var checkout = new CheckoutActions(Page);
    var ordersPage = new OrdersPage(Page);

    await checkout.SignInAndOpenOrdersAsync(Data.User.Email, Secrets.Get("SHOP_PASSWORD"));
    await ordersPage.VerifyOrderStatusAsync(id, order.Status);
}
```

**MSTest v4**

```csharp
[TestMethod]
[DataRow("standard")]
[DataRow("express")]
[TestCategory("regression")]
public async Task ShowsNewOrderWithItsStatusAsync(string shipping)
{
    var order = Data.Orders[shipping];
    var id = await _orders.CreateOrderAsync(order.Product, order.Quantity);
    var checkout = new CheckoutActions(Page);
    var ordersPage = new OrdersPage(Page);

    await checkout.SignInAndOpenOrdersAsync(Data.User.Email, Secrets.Get("SHOP_PASSWORD"));
    await ordersPage.VerifyOrderStatusAsync(id, order.Status);
}
```

- Same steps, different data: one method with a row per case. The row holds the case key; the values stay in the JSON (test-framework has the attributes).
- Different steps: separate tests, kept next to each other in the class.

## 5. Independent and parallel-safe

- Each test runs alone, in any order, in parallel, and repeatedly. It never depends on another test's data or outcome.
- It creates what it needs (through an Api) and the class teardown removes it. Values that must be unique come from `UniqueData`.
- Use a returned value only when a later call needs it, like `id` above. Don't capture values just to look at them.

## 6. When the product is wrong

The test asserts documented behaviour and stays red, with a `bug:<id>` category and a linked bug
report. Never skip it, loosen it or assert the broken behaviour (design-conventions §6).

## Boundaries

- The class around the tests (data field, hooks, categories) → test-classes
- Attributes per framework → test-framework
- Where values come from → test-data

## Review checklist

- [ ] The name states the behaviour and ends in `Async`.
- [ ] The body has business calls only: no locators, assertions, navigation, HTTP, logic, `try` or waits.
- [ ] At least one `Verify*` call.
- [ ] Variants are data rows keyed into the JSON.
- [ ] It creates its own data and is safe to run in parallel and repeatedly.
- [ ] Known defects stay red with `bug:<id>`.
