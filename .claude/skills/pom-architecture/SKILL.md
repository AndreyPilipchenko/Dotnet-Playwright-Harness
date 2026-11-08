---
name: pom-architecture
description: Use when designing, structuring, extending or reviewing a Playwright C# test framework built with this harness (NUnit, xUnit or MSTest) - deciding where new code belongs, naming pages, locators, tests or data files, adding tests, seeding data, handling a known product defect, or asked to "automate" or "write tests for" a feature. The entry point for every framework task.
---

# POM architecture (C#)

The framework is a Page Object Model **without base pages**, split into a business family
(Pages, Apis, Dbs, Helpers) and a technical family (Utils, Config). Tests only orchestrate
business calls.

**Before writing or reviewing framework code, read
[design-conventions.md](references/design-conventions.md).** It is the single canonical source:
layout, naming, class anatomy, the iron laws, API seeding, known defects and the review
checklist. Where a project file and this skill disagree on style, follow the file you edit
and raise the mismatch.

## Layer map

| Path | Holds | Never holds |
|---|---|---|
| `src/<Name>.Framework/Pages/` | `<Name>Page`: private locators, actions, `Verify*` | test-framework types, test data loading |
| `src/<Name>.Framework/Apis/`, `Dbs/` | `<Domain>Api`, `<Domain>Db`: business operations | assertions in actions |
| `src/<Name>.Framework/Helpers/` | `<Noun>Actions`: flows across pages or services | locators of their own |
| `src/<Name>.Framework/Utils/`, `Config/` | `TestData`, `ApiContext`, retries, `Timeouts` | business classes |
| `src/<Name>.Framework/Utils/Extensions/` | `<Type>Extensions`: generic extension members | specific elements, business steps, assertions |
| `tests/<Name>.Tests/Setup/` | `UiTest`, the only base class | tests |
| `tests/<Name>.Tests/Ui/`, `Api/` | `<Feature>Tests`: business calls only | locators, `Expect`, HTTP, `if`/loops, `try`, skips |
| `resources/testData/` | `<Feature>TestData.json` | secrets |

## Routing

| Working on | Skill |
|---|---|
| Choosing, naming or repairing a locator; strict-mode errors; upload inputs | element-locators |
| Creating or extending a page class; shared areas such as a header; NavigateAsync; composition | page-classes |
| A business step on a page, Api, Db or Helpers class; dialogs; uploads | action-methods |
| A `Verify*` method: Expect matchers, absence, lists, API status, DB values, timeouts | validation-methods |
| The paired JSON file, its records, keyed cases, enums, secrets, unique values, upload files | test-data |
| A `<Feature>Tests` class: layout, setup and teardown of services, categories | test-classes |
| A single test: what its body may contain, data rows, independence | test-methods |
| An `<Domain>Api` or `<Domain>Db` class: requests, queries, seeding, cleanup | service-classes |
| Whether code belongs in Utils or Config; secrets, retries, checks, connections, timeouts | utility-classes |
| An extension method or C# 14 extension block | extension-classes |
| Starting a framework from nothing, or adapting the skeleton to an application | scaffold-framework |
| Test attributes, categories, data rows, hooks, parallelism or `dotnet test` commands for NUnit, xUnit or MSTest | test-framework |
| Build settings, analyzers, nullable, async rules, packages and versions | csharp-standards |

## Before you finish

Run `dotnet .harness/tools/bin/Harness.Tools.dll check-conventions` and fix every finding.
An exception you mean to keep needs `// conventions-ok: <reason>`. Then walk the review checklist
at the end of design-conventions.md.
