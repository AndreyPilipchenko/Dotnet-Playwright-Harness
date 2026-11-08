---
name: test-case-design
description: Use when designing, writing, reviewing or prioritising test cases for a requirement or feature - TC records under docs/qa/test-cases/, steps and expected results, positive and negative cases, boundaries, coverage of acceptance criteria, choosing what to automate, or linking a test to its case with a tc:<id> category.
---

# Test case design

Turn accepted requirements into test cases a person could run and a test can automate. Every
expected result comes from the requirement; nothing is guessed.

Read [the playbook](references/playbook.md) before drafting: it has the quality considerations,
the rules for writing a case, priority and the independent assessment.

## 1. Start from requirements

- Read the requirement records in `docs/qa/requirements/`. Design only for `accepted` requirements. A `proposed` or `needs-decision` one gets its questions answered first (requirements-analysis).
- If there are no requirement records, run requirements-analysis first. Don't design from a story directly: the record is what the case cites.
- Read the existing cases (`traceability --json` lists them per requirement) so you extend coverage instead of repeating it.

## 2. Draft the cases

- Copy `assets/test-case-template.md` to `docs/qa/test-cases/TC-<n>.md`, numbered after the highest existing id. The file name equals the id.
- `requirements:` lists the requirement ids the case covers, comma separated. `## Coverage` names the acceptance criteria (`REQ-001 AC-2`).
- Set `priority` and `automation` as the playbook says. New cases start `planned` or `manual-only`.
- When an expected outcome is missing, stop and add an open question to the requirement. A case with a guessed result is worse than no case.

## 3. Review in chat

Show the user one table of the drafted cases before writing more:

| Id | Title | Requirements | Type | Priority | Automation |
|---|---|---|---|---|---|

Then the uncovered acceptance criteria, if any, and the questions raised. The user decides what to
keep, merge or drop.

## 4. Link the automated test

When a test automates a case (the automate-test work, through pom-architecture):

- The test gets `[Category("tc:TC-012")]` (NUnit), `[TestCategory("tc:TC-012")]` (MSTest) or `[Trait("Category", "tc:TC-012")]` (xUnit), next to its vocabulary category. `[category-vocabulary]`
- The case's `automation` becomes `automated` in the same change.
- One case may be automated by several tests (data rows count as one test). One test may automate several cases; give it one `tc:` category per case.

## 5. Check

Run `dotnet .harness/tools/bin/Harness.Tools.dll traceability`. It must report no failures:

| Rule | Means |
|---|---|
| `case-unknown-requirement` | `requirements:` names a record that doesn't exist |
| `case-automated-without-test` | `automated`, but no test carries `tc:<id>` |
| `test-unknown-case` | a test names a case that doesn't exist |
| `case-not-marked-automated` (warning) | a test exists, so set `automation: automated` |
| `requirement-without-cases` (warning) | an accepted requirement has no case yet |

## Boundaries

- Requirements and their questions → requirements-analysis
- Which cases to run for a release, and the exit criteria → test-planning
- Writing the test code → pom-architecture, then test-classes and test-methods
- A case that fails because the product is wrong → bug-report
