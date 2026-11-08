---
name: bug-report
description: Use when a test fails because the product is wrong, when asked to report, file, log or write up a bug or defect, when a known defect is fixed or won't be fixed, or when linking a failing test to its bug with a bug category - BUG records under docs/qa/bug-reports/ and an optional GitHub issue.
---

# Bug report

Record a confirmed product defect so anyone can reproduce it, and link the test that shows it.
The test stays red until the fix ships (design-conventions §6).

## 1. Confirm it's a product defect

Before writing a report, be sure the product is wrong, not the test:

- The expected behaviour is **documented**: a requirement record, an acceptance criterion, or the user confirms it. If it isn't, it's a question for requirements-analysis, not a bug.
- It reproduces: run the single test again (run-tests), and once by hand (execute-test) if it's intermittent.
- The test itself is sound: the locator finds the right element, the data is valid, the environment is up. A broken test is fixed, not reported.

## 2. Write the record

- Copy `assets/bug-report-template.md` to `docs/qa/bug-reports/BUG-<n>.md`, numbered after the highest existing id.
- `severity`: `critical` (data loss, security, no workaround on a core flow), `high` (a core flow broken, with a workaround), `medium` (a supported variant broken), `low` (cosmetic or rare).
- `cases`: the test cases that expose it, comma separated, or empty. `traceability` checks they exist.
- `issue`: the external issue URL or number, or empty.
- Steps start from a known state and are the shortest path. Expected cites the requirement. Actual quotes the exact message or value.
- Never paste credentials, tokens or personal data from logs or traces into the record.

## 3. Link the test

- Keep the test asserting the **documented** behaviour. Never change it to pass on the defect, and never skip it.
- Add `bug:BUG-<n>` next to its normal category: `[Category("bug:BUG-17")]` (NUnit), `[TestCategory(...)]` (MSTest) or `[Trait("Category", "bug:BUG-17")]` (xUnit).
- Run `dotnet .harness/tools/bin/Harness.Tools.dll traceability`: `test-unknown-bug` and `bug-unknown-case` must not appear.

## 4. Optional: a GitHub issue

Only when the user asks. Write the body (the record's sections, without the front matter) to a
temporary file, show the user the exact title and body, then run
`gh issue create --title "<title>" --body-file <that file>`. The pre-shell hook asks before any
`gh` write. Put the returned URL in `issue:`.

## 5. Close it

- **Fixed:** the test turns green on its own. Set `status: fixed` and remove the `bug:` category from the test in the same change; the `bug-closed-still-linked` warning says when one was missed.
- **Won't fix:** set `status: wont-fix` with the decision and who made it under Notes. Then the documented behaviour changed: update the requirement and the case (requirements-analysis, test-case-design) and the test, and remove the `bug:` category.

## Boundaries

- Deciding whether a failure is the product or the test → maintain-test, which uses the `failure-investigator` agent
- The test code → test-classes and test-methods
- Requirement or case changes after a won't-fix → requirements-analysis, test-case-design
