---
name: validation-methods
description: Use when writing, naming or reviewing a Verify* method (an assertion) on a page, Api or Db class in a Playwright C# framework built with this harness - choosing the Expect matcher, checking text, counts, absence or a whole list, API status checks, database value checks, timeouts for slow pages, or moving an assertion out of a test.
---

# Validation methods

A validation checks one business condition and does nothing else. Every assertion in the
framework lives in a `Verify*` method on a business class, never in a test.

Read [design-conventions](../pom-architecture/references/design-conventions.md) first: §3
(validations come last), §4 law 6, §7 (lists and slow surfaces).

## 1. Name and place

- `Verify` + the business condition + `Async`: `VerifyErrorMessageAsync`, `VerifyOrderAbsentAsync`, `VerifySignedInAsAsync`.
- All validations sit last, under `// Validations`.
- One business condition per method. Several related `Expect` calls in one method are fine when they describe that one condition together.

## 2. GUI: web-first `Expect`

```csharp
public Task VerifyErrorMessageAsync(string expected) => Expect(ErrorMessage).ToHaveTextAsync(expected);

public Task VerifyOrderAbsentAsync(string id) => Expect(OrderRow(id)).ToHaveCountAsync(0);

public Task VerifyOrderStatusAsync(string id, string status) =>
    Expect(OrderStatusCell(id)).ToHaveTextAsync(status, new() { Timeout = Timeouts.SlowSurface });
```

- Use `using static Microsoft.Playwright.Assertions;` and `Expect(locator)`. These assertions retry until they pass or time out, so no wait is needed before them.
- **Text:** `ToHaveTextAsync` by default; it matches the whole text. Use `ToContainTextAsync` only when the element really holds more than the part you check.
- **Absence:** `ToHaveCountAsync(0)` on the locator that would match, or `ToBeHiddenAsync()`. Never check absence by catching a timeout.
- **A whole list:** `Expect(rows).ToHaveTextAsync(["A-1", "A-2"])` checks every row in order, with no loop.
- **"No row may be X":** one locator for the rows that would break the rule, with `ToHaveCountAsync(0)`. Seed the rows first, so the check doesn't pass on an empty table (§7).
- **Slow surfaces:** a named timeout from `Config/Timeouts.cs`, never a number. `[timeout-literal]`

## 3. API: status, then body

```csharp
public async Task VerifyOrderExistsAsync(string id) =>
    await Expect(await _request.GetAsync($"{OrdersEndpoint}/{id}")).ToBeOKAsync();
```

- `Expect(response).ToBeOKAsync()` checks for a 2xx status. `Expect(response).Not.ToBeOKAsync()` checks for a non-2xx status.
- To check the body, read it (`await response.JsonAsync()`) and compare the business fields with `Checks.Equal`, as for DB values (below).

## 4. Values with no web-first form (DB, parsed bodies)

```csharp
public async Task VerifyOrderCountAsync(string product, long expected) =>
    Checks.Equal(expected, await CountOrdersAsync(product), $"orders for {product}");
```

- The framework project cannot use a test framework's `Assert`. `Checks.Equal` in Utils throws `CheckFailedException` with the expected and actual values, and every framework reports that as a failure (design-conventions §4 law 6).

## 5. Validations only observe

- Expected values are **parameters**, fed from the test's JSON. Nothing expected is hard-coded inside the method.
- No clicks, fills or navigation inside a `Verify*` method. `[action-verify-separation]`
- No loops, branches or `try`. Use a collection assertion or split the method.
- Return `Task`. A validation returns nothing.
- **Pick the layer that owns the truth:** what the user sees goes in a page's `Verify*`; what the service stored goes in an Api or Db `Verify*`. A test can call both.

## 6. Moving an assertion out of a test

A test with `Expect(...)`, `Assert.That(...)` or `.Should()` fails `[no-assertion-in-test]`. Move
the check to the business class that owns the element or data, name it `Verify…Async`, pass the
expected value in, and call it from the test.

## Boundaries

- Locators the validation reads → element-locators
- Steps before the validation → action-methods
- Where expected values come from → test-data
- A test that fails because the product is wrong → design-conventions §6 (keep it red, `bug:<id>`)

## Review checklist

- [ ] Named `Verify…Async`, one business condition, placed under `// Validations`.
- [ ] Web-first `Expect` on the GUI; `ToBeOKAsync` for API status; `Checks.Equal` for other values.
- [ ] Expected values are parameters from test data.
- [ ] No actions, loops, branches, `try` or literal timeouts.
- [ ] Absence and "every row" checks are single assertions on seeded data.
