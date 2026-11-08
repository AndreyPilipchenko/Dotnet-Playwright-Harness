---
name: coverage-audit
description: Use when asked whether a requirements document, specification or story is fully tested in a Playwright C# framework built with this harness - "audit coverage", "is this spec fully covered", "check the document against the tests", or a closing check before a release. A fresh agent compares each requirement line with the tests and marks it Built, Partly or Missing with file evidence.
---

# Coverage audit

A document is covered when its lines are proven by tests, not when its records say so. Each test case
can pass while the document as a whole loses requirements between them. This check reads the document
against the code.

For counts from the records (how many cases are automated, what's open), use plan-tracker. The two
answer different questions, and a disagreement between them is worth reporting.

## Steps

**1. Number the lines.** Read the source document. List each requirement line, verbatim, as
`[L<n>] <line>`. Headings and background are not lines, but they give the lines their meaning. When a
sentence holds several requirements, split it, keeping the words. Show the list to the user if the
document is ambiguous about what counts as a requirement.

**2. Dispatch the reader.** `subagent_type: coverage-auditor`, with the document's path, the lines and
the work folder. **Give it nothing else:** no REQ or TC records, no plans, no PR. An agent handed the
plans checks the plans.

**3. Write it down.** Save the report as `docs/qa/coverage-audits/<yyyy-mm-dd>-<slug>.md`, from
[the template](assets/coverage-audit-template.md). It has no front matter, and `traceability` ignores it.

**4. Turn the gaps into decisions.** Show each Partly or Missing line, with its evidence and what's
missing. The user chooses, per line, because this decides what the document delivers:

| Choice | What happens |
|---|---|
| New work (recommended) | requirements-analysis and test-case-design add it, and automate-test automates it |
| Out of scope | The row becomes `Out of scope: <reason> (accepted by <who>, <date>)` |

The document is **covered** only when every line is Built or accepted as out of scope.

## Rules

- The reader gets the document and the code, never the records or plans.
- Every line gets a mark and evidence. Partly is never rounded up to Built.
- A gap becomes new work or an accepted out-of-scope row. It is never dropped silently, and never your decision.
- Read-only on code. This skill writes only the audit file.

## Boundaries

- Counts and status from the records → plan-tracker
- What a change affects → impact-analysis
- Writing the missing cases → test-case-design
