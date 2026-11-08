---
name: element-locators
description: Use when choosing, naming, writing, repairing or reviewing a Playwright locator in a C# page class built with this harness - selector priority, the locator suffix list, parameterized locator methods for rows and cards, strict-mode "resolved to N elements" errors, flaky or broken selectors, and file-upload inputs.
---

# Element locators

Locators are the most fragile part of a framework: a bad one passes today and breaks on the next
markup change. This skill covers how a locator is **chosen**, **named** and **declared**.

Read [design-conventions](../pom-architecture/references/design-conventions.md) first: §2
(naming and the closed suffix list), §4 law 2 (locators stay private), and §7 (targeting one
record in a list).

## 1. Declare once, privately, with a suffix from the closed list

```csharp
private ILocator EmailInput => _page.GetByLabel("Email");

private ILocator SignInButton => _page.GetByRole(AriaRole.Button, new() { Name = "Sign in" });

private ILocator OrderRow(string id) => OrdersTable.GetByRole(AriaRole.Row).Filter(new() { HasText = id });
```

- A locator is a `private` expression-bodied property, or a `private` method returning `ILocator` when it takes a parameter. `[locator-visibility]`
- The name is `<Purpose><Suffix>` and the suffix comes from §2's closed list: `EmailInput`, `OrdersTable`, `ErrorMessage`. `[locator-naming]`
- Name the element, not the gesture: `SignInButton`, not `ClickSignIn`.
- Actions and validations use the member. They never build a locator inline. `[locator-in-method-body]`

## 2. Choose the selector by priority

Work down the list and stop at the first one the element supports:

| # | Use | Form |
|---|---|---|
| 1 | Role with accessible name | `GetByRole(AriaRole.Button, new() { Name = "Save" })` |
| 2 | Label of a form control | `GetByLabel("Email")` |
| 3 | A test id the team owns | `GetByTestId("account-name")` |
| 4 | Placeholder, alt text or title, when that is the only accessible handle | `GetByPlaceholder`, `GetByAltText`, `GetByTitle` |
| 5 | Stable id or attribute in CSS | `Locator("#email")`, `Locator("[name=email]")` |
| 6 | Short relative CSS, as a last resort | `Locator("nav a[href='/orders']")` |

- A jump down the list needs a reason a reviewer would accept: the element has no role or label, and no test id was added yet. If the markup can't support a good locator, ask the team for a `data-testid` rather than writing a fragile selector.
- Ids use the CSS `#` form, never the `id=` engine prefix. `[no-id-engine-prefix]`
- **Never:** XPath copied from devtools, absolute paths (`/html/body/…`), styling class chains (`.btn.btn-primary.col-md-3`), or chains of more than four steps.

## 3. Scope, then filter; don't index

- Scope to a container first (`HeaderSection.GetByRole(...)`, `OrdersTable.GetByRole(...)`), then use a short selector inside it.
- To pick one record, filter by an identity it carries: `.Filter(new() { HasText = id })`. Never use `.First`, `.Nth(n)` or `:nth-child` to mean "the row I created". §7 covers this in full.
- Position is acceptable only where it *is* the identity in fixed markup: the last cell of a row is its status column, as in `OrderRow(id).GetByRole(AriaRole.Cell).Last`.
- **Strict mode:** an action on a locator that matches several elements throws `strict mode violation: … resolved to N elements`. That error is the design telling you the locator is too broad. Make it unique; don't add `.First` to silence it.

## 4. Text

- **Accessible names are fine:** the `Name` in `GetByRole` and the text in `GetByLabel` are what users see and what assistive tech uses.
- **Don't locate by copy that changes:** a marketing sentence, a status message or a count. Locate the element structurally and assert its text in a `Verify*` method, with the expected value from test data: `ErrorMessage => _page.GetByRole(AriaRole.Alert)` plus `VerifyErrorMessageAsync(expected)`.
- `Filter(new() { HasText = id })` with an identity passed in as a parameter is the right way to pick a row.

## 5. End on the element you act on

- Click the `<a>`, `<button>` or `<input>`, not a styling `<span>` or `<u>` inside it. Role locators do this for you.
- **File uploads** target the `<input type="file">` itself, even when it is hidden behind a styled button. `SetInputFilesAsync` works on hidden inputs, and `GetByLabel` finds them when a `<label>` wraps the input:

```csharp
private ILocator InvoiceInput => _page.GetByLabel("Invoice");
```

## Boundaries

- Where locators sit in the class, and how many pages to create → page-classes
- What actions do with a locator → action-methods
- Assertions on a located element → validation-methods
- An extension that builds a locator for a specific element is never allowed (design-conventions §8) → extension-classes

## Review checklist

- [ ] Private, declared once, named `<Purpose><Suffix>` from the closed list.
- [ ] As high on the priority list as the markup allows; any lower choice has a reason.
- [ ] Scoped to a container; rows and cards picked by identity, not position.
- [ ] No `id=` prefix, devtools XPath, absolute path, styling chains or long chains.
- [ ] No `.First` or `.Nth()` added to silence a strict-mode error.
- [ ] Text that changes is asserted, not used to locate.
