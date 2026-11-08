---
name: impact-analysis
description: Use when asked what a change affects or what to retest - a changed requirement, story or acceptance criterion, a pull request or diff of the application or the test framework, a renamed page or endpoint, or choosing the tests and test cases to run for a change.
---

# Impact analysis

From a change, find the requirements, test cases and tests it touches, and say what to update and
what to run. Report what the records and code show; mark anything inferred as inferred.

## 1. Identify the change

- **A requirement change:** the old and new source text (requirements-analysis §5 refreshes the records first).
- **An application change:** the diff, PR or list of changed screens and endpoints the user gives. Read it; don't ask for a summary you can derive.
- **A framework change:** `git diff --name-only <base>...HEAD` under `src/` and `tests/`.

## 2. Trace it

Use `dotnet .harness/tools/bin/Harness.Tools.dll traceability --json`: it maps each requirement to
its cases, and each case and bug to the tests (file, line, method) that carry `tc:` or `bug:`.

| Changed | Follow |
|---|---|
| A requirement | its `cases`, then each case's `tests` |
| A test case | its `tests`, and its `requirements` for context |
| A page, Api, Db or Helpers class | grep `tests/` for the class and its methods, then map those test methods back to cases through the JSON |
| A locator only | the page's `Verify*` and action methods that use it, then their callers |
| Test data | the paired `<Feature>Tests` class |

Code paths without a `tc:` link are still affected: list them as tests without a case.

## 3. Report

| Item | Kind | Why affected | Action |
|---|---|---|---|
| REQ-004 | requirement | AC-2 text changed | re-check; status back to `proposed` |
| TC-012 | test case | cites REQ-004 AC-2 | update expected result in step 3 |
| `OrdersTests.ShowsNewOrderWithItsStatusAsync` | test | `tc:TC-012` | update after TC-012; run |

Then:

- **Run:** the narrowest command that covers the affected tests, from the test-framework skill (a category filter, or `tc:` categories for the cases).
- **Not affected:** say what you checked and found unaffected, so the user can trust the gaps.
- **Inferred:** links found by reading code rather than records.

## 4. Apply

Change records and code only when the user agrees with the report. Then run `traceability` again;
it must have no failures.

## Boundaries

- Rewriting the requirements → requirements-analysis
- Rewriting the cases → test-case-design
- Updating the test code → pom-architecture and the specialist skills
