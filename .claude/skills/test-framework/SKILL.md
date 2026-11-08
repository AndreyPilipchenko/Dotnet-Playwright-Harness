---
name: test-framework
description: Use when writing or changing test attributes, categories, data-driven rows, per-test setup and teardown, parallelism, or the dotnet test command line in a Playwright C# test framework built with this harness on NUnit, xUnit v3 or MSTest v4 - including filtering by category, reading exit codes, choosing the browser, or porting a test between frameworks.
---

# Test framework (NUnit, xUnit v3, MSTest v4)

A project uses **one** test framework: the one in `.harness/project.json`. Check it before you
write an attribute. `check-conventions` reports a mix as `mixed-test-frameworks`.

All three run on **native Microsoft Testing Platform (MTP)**. Many online examples show the
VSTest form (`dotnet test <path> -- Playwright.BrowserName=…`, `.runsettings`). It does not
apply here: runsettings are silently ignored (NUnit, MSTest) or rejected (xUnit).

**For hooks, parallelism, analyzer pitfalls and porting, read [the playbook](references/playbook.md).**

## Quick map

| | NUnit | xUnit v3 | MSTest v4 |
|---|---|---|---|
| Test class | derives from `UiTest` | derives from `UiTest` | `[TestClass]`, derives from `UiTest` |
| Test | `[Test]` | `[Fact]` | `[TestMethod]` |
| Data rows | `[TestCase(a, b)]` | `[Theory]` + `[InlineData(a, b)]` | `[TestMethod]` + `[DataRow(a, b)]` |
| Category | `[Category("smoke")]` | `[Trait("Category", "smoke")]` | `[TestCategory("smoke")]` |
| Per-test setup | `[SetUp]` async `Task` | `InitializeAsync()` override | `[TestInitialize]` async `Task` |
| Run a class alone | `[NonParallelizable]` | `[Collection("Serial")]` | `[DoNotParallelize]` |

Categories are `smoke`, `regression`, `bug:<id>`, or the project's own list in `.harness/project.json`.

## Commands (repository root)

```powershell
dotnet test --project tests/<Name>.Tests/<Name>.Tests.csproj --report-trx --results-directory TestResults
```

| Add | NUnit / MSTest | xUnit v3 |
|---|---|---|
| One category | `--filter "TestCategory=smoke"` | `--filter-trait "Category=smoke"` |
| Skip the build | `--no-build` | `--no-build` |

- **Exit codes:** 0 = passed, **2 = test failures**, **5 = invalid command line**. A 5 means fix the command, not the test.
- **Browser:** the `BROWSER` environment variable (`chromium`, `firefox`, `webkit`) and `HEADED`. Context options (base URL, viewport, locale) are set in code in `tests/<Name>.Tests/Setup/UiTest.cs`.
- After a build or a Playwright update, install the browser once: `tests/<Name>.Tests/bin/Debug/<tfm>/playwright.ps1 install chromium`.
