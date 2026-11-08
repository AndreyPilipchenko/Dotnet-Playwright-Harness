---
name: action-methods
description: Use when writing, naming or reviewing an action method (a business step such as LoginAsync, CreateOrderAsync or UploadInvoiceAsync) on a page, Api, Db or Helpers class in a Playwright C# framework built with this harness - its granularity, parameters, return value, native dialogs, file uploads, or reuse of an existing action.
---

# Action methods

An action performs one user-visible business step. It acts and never checks.

Read [design-conventions](../pom-architecture/references/design-conventions.md) first: §2
(naming), §3 (anatomy), §4 laws 4, 6, 7, 8 and 11.

## 1. Name the business intent

- PascalCase verb phrase ending in `Async` when it returns `Task`: `LoginAsync`, `DeleteOrderAsync`, `SignInAndOpenOrdersAsync`. `[async-naming]`
- The name says what the user achieves, not which controls are touched. `LoginAsync`, not `FillFormAndClickAsync`.
- `NavigateAsync` is the one fixed name. It goes first under `// Actions`.

## 2. One action, one business step

```csharp
public async Task LoginAsync(string email, string password)
{
    await EmailInput.FillAsync(email);
    await PasswordInput.FillAsync(password);
    await SignInButton.ClickAsync();
}
```

- **Granularity:** one step a user would name. Filling a form and submitting it is one action; filling each field separately is not.
- **Reuse first:** before writing an action, search Pages, Apis, Dbs and Helpers for one with the same intent, and reuse or extend it (iron law 11). Duplicate intent is the defect, even if the code differs.
- **A flow across pages** is an action on a `Helpers/<Noun>Actions` class that calls page actions in order.

## 3. Act, don't check

- No `Expect`, `Assert` or `Checks` in an action. `[action-verify-separation]` Playwright's actions already wait for the element to be actionable.
- No fixed waits. `[no-hard-wait]` If the next step depends on something loading, the next locator's auto-wait, or a `Verify*` call in the test, covers it.
- No `try`/`catch`, no branching on page state, no loops over elements. A loop is fine in an Api class over data the class owns, such as deleting the ids it created.

## 4. Inputs and outputs

- **Business data arrives as parameters,** from the test's JSON. Nothing is hard-coded inside the action except the page's own structure (its `Url`, its locators).
- **Four parameters at most.** A larger form takes one record: `FillAddressAsync(Address address)`. Define the record next to the test data shape, or in the framework if several tests share it.
- **Return `Task`,** or `Task<T>` when a later call needs the value, such as `CreateOrderAsync` returning the new id. Never return `this` or a page object.

## 5. Native dialogs

A `confirm()` or `alert()` is handled inside the action that triggers it, by a named handler that
is added, then removed. Without a handler, Playwright dismisses dialogs automatically.

```csharp
public async Task DeleteOrderAsync(string id)
{
    _page.Dialog += AcceptDialog;
    await DeleteButton(id).ClickAsync();
    _page.Dialog -= AcceptDialog;
}

private static void AcceptDialog(object? sender, IDialog dialog) => _ = dialog.AcceptAsync();
```

- Never add a lambda to `Page.Dialog`. It can't be removed, and it would handle every later dialog too. `[no-persistent-dialog-handler]` An `async` lambda is also `async void`. `[async-discipline]`

## 6. File uploads

```csharp
public async Task UploadInvoiceAsync(string path)
{
    await InvoiceInput.SetInputFilesAsync(path);
    await UploadButton.ClickAsync();
}
```

- The path comes from the test (test-data covers where binary files live). The locator targets the `<input type="file">` (element-locators §5).

## Boundaries

- Checks after an action → validation-methods
- Locators the action uses → element-locators
- API and DB actions (requests, queries, cleanup) → service-classes
- Retry or polling helpers → utility-classes

## Review checklist

- [ ] Named for the business intent, `Async`, and not a duplicate of an existing action.
- [ ] One user-visible step; flows across pages live in Helpers.
- [ ] No assertions, fixed waits, `try`, branching or loops over elements.
- [ ] Data arrives as parameters (at most 4, otherwise a record); nothing business-specific is hard-coded.
- [ ] Returns `Task`, or `Task<T>` only when the value is used.
- [ ] Dialogs use a named handler that is removed after the action.
