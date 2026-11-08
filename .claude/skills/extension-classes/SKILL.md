---
name: extension-classes
description: Use when adding, naming or reviewing an extension method or C# 14 extension block in a Playwright C# framework built with this harness - helpers on IAPIResponse, ILocator, IPage or string, choosing between the classic this-parameter form and an extension block, or when an extension was placed in a page, test or helper and must move or be rewritten.
---

# Extension classes

Extension members add **generic technical helpers** to types you don't own. In C# they are also the
easiest way around the page object model: `page.SaveButton()` on `IPage` hands a test a locator. So
the rules are strict, and four of them are enforced.

Read [design-conventions](../pom-architecture/references/design-conventions.md) §8 first. It is
the canonical rule; this skill shows how to apply it.

## 1. Where and how they are named

- **Only** in `src/<Name>.Framework/Utils/Extensions/`. Never in Pages, Apis, Dbs, Helpers, Config, other Utils files, or the tests project. `[extension-placement]`
- One `public static class <ExtendedType>Extensions` per extended type, in a file of the same name: `ApiResponseExtensions.cs`, `LocatorExtensions.cs`. `[extension-naming]`
- Members follow normal naming: PascalCase, `Async` on anything returning a task. `[async-naming]`

## 2. Choose the form

**Default: classic static methods with `this`.** They work on every target framework.

```csharp
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

**A C# 14 `extension` block when you need an extension property.** Only `net10.0` is verified for
this form.

```csharp
public static class LocatorExtensions
{
    extension(ILocator locator)
    {
        /// <summary>The rows inside a table or grid locator.</summary>
        public ILocator Rows => locator.GetByRole(AriaRole.Row);

        public async Task ReplaceTextAsync(string value)
        {
            await locator.ClearAsync();
            await locator.FillAsync(value);
        }
    }
}
```

Both forms are checked the same way. Don't mix forms within one class.

## 3. The test: generic or specific?

Ask: *would this helper make sense, unchanged, on any page or any API of any application?*

| Generic: allowed | Specific: belongs in a business class |
|---|---|
| `response.ReadIdAsync("POST /api/orders")` | `page.LoginAsync(email, password)`: a business step, so it goes on `LoginPage` |
| `table.Rows`, `input.ReplaceTextAsync(value)` | `page.SaveButton()`: names one element, so it is a private locator on its page |
| `row.Filter(new() { HasText = id })` wrapped as `RowWith(id)` | `page.GetByLabel("Email")` inside an extension `[no-element-locator-extension]` |
| throwing when a response is not OK | `Expect(locator).ToBeVisibleAsync()` inside an extension `[no-assertion-in-extension]` |

- **A string literal in a locator call means specific.** Generic helpers take the selector, role name or text as a parameter. A role on its own (`AriaRole.Row`) is fine.
- A rare generic filter written as a string selector, such as `Locator("visible=true")`, gets a reasoned exemption: `// conventions-ok: generic visibility filter, no element named`.
- **Never assert.** If a helper finds a problem, it throws with a clear message. Checks belong in `Verify*` methods.
- **Never load test data or use business types.** Extensions are Utils code, and Utils sits below the business layer.

## 4. Fixing a misplaced extension

| Found | Do |
|---|---|
| An extension in `Pages/` or `Helpers/` that performs a business step | Make it an action on the page or Helpers class that owns the step; delete the extension |
| An extension that returns a specific element's locator | Make it a private locator on its page, plus the action or `Verify*` that uses it |
| A generic helper in the wrong folder or file | Move it to `Utils/Extensions/<ExtendedType>Extensions.cs` |
| An extension that asserts | Make it a `Verify*` method on the business class, or make it throw if it was really arrangement |
| Extensions in the tests project | Move generic ones to `Utils/Extensions/`, and business ones to their class |

## Boundaries

- Other Utils helpers (`Secrets`, `Retry`, `Checks`) → utility-classes
- Locators and where they live → element-locators, page-classes
- Assertions → validation-methods

## Review checklist

- [ ] In `Utils/Extensions/`, in a `<ExtendedType>Extensions` class with a matching file name.
- [ ] Classic form by default; a block only for extension properties.
- [ ] Generic: no literal labels, names or selectors, and no business steps.
- [ ] No assertions; failures throw with a clear message.
- [ ] No test data, business types or test framework.
