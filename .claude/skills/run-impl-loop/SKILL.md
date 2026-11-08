---
name: run-impl-loop
description: Use when making a change to a Playwright C# framework built with this harness that no test case drives - a new utility, config or extension member, a shared page area, a build setting, a refactor - or when asked to "implement", "add support for" or "refactor" framework code. Runs the loop analyze, implement, validate, test, review, fix, summarize, with a fresh agent for each job.
---

# The implementation loop

A change is finished when its criteria are proven and someone else has reviewed it. "It builds" and
"I checked it myself" don't count.

## Is this the right skill?

| The request | Go to |
|---|---|
| A designed test case to automate | automate-test |
| A failing or flaky test | maintain-test |
| A story with no test cases yet | requirements-analysis, then test-case-design |
| A framework that doesn't exist yet | scaffold-framework |
| Anything else that changes framework code | here |

## The loop

You coordinate. The general rules are in pom-architecture, under "Agents".

| Stage | Done by | `subagent_type` |
|---|---|---|
| 1 Analyze | you | none |
| 2 Implement, and every repair | the implementer | `framework-implementer` |
| 3 Validate | the validator | `validator` |
| 4 Test | the runner | `suite-runner` |
| 5 Review | framework-review | `framework-reviewer`, `loosened-reviewer`, `findings-verifier` |
| 6 Fix | back to stage 2 | |
| 7 Summarize | you | none |

### 1. Analyze

Copy [the template](assets/impl-notes-template.md) to `.harness/state/impl/<slug>.md` (gitignored), and fill it in:

- **The request,** in the user's own words.
- **Acceptance criteria:** numbered, each one something a reader could check. If you can't write them, ask the user. Don't guess.
- **Planned files,** found by reading the code first. Where sources disagree, the order is: the user's request, then `CLAUDE.md`, then design-conventions.md, then `docs/framework-matrix.md`, then the existing code.
- **Tests affected:** through impact-analysis for anything beyond a single class.
- **Packages:** a new package needs the user's approval first (csharp-standards). Write down who approved it.

A destructive or wide change (deleting a class, renaming across the framework) needs the user's
agreement before stage 2.

### 2. Implement

Dispatch `framework-implementer` with the notes file. Its gate is a clean build, `check-conventions`
and `traceability`. A failing gate goes back to it and doesn't count as a repair round.

### 3. Validate

Dispatch `validator` with the notes file and the base ref. FAIL on any criterion goes to stage 6.
NOT-PROVEN names a run that stage 4 must include.

### 4. Test

Run the tests affected through `suite-runner` (run-tests). They must meet the green rule. Include what
the validator marked NOT-PROVEN. If no test exercises the change, say so in the summary: a criterion
with no test is not proven. Propose a test case through test-case-design if the behaviour matters.

### 5. Review

Run framework-review on the diff. CHANGES-REQUIRED goes to stage 6.

### 6. Fix

- At most **three rounds**. Count them in the notes (`Repairs (n of 3)`).
- Each round goes back through stages 3, 4 and 5. Earlier results no longer count.
- A failed test is diagnosed before it's repaired: the `failure-investigator` route in maintain-test.
- Never repair by weakening a check, adding a retry or skip, or raising a timeout.
- **After the third round,** stop. Mark it blocked in the notes with the evidence, and ask the user.

### 7. Summarize

Report one line per criterion, with its evidence, then the test result, the verdict, and anything
deferred (with reason, owner and re-check). Never commit. Suggest a commit message.

## Gates you don't argue with

| Thought | Reality |
|---|---|
| "It's a small change, skip the review" | Size is not evidence. Small changes break shared classes. |
| "While I'm here, I'll also tidy this" | Do what the criteria say. Anything else is another change. |
| "This package would make it easy" | Ask first. |
| "The test is out of date, I'll update it" | That is a requirement change or a defect. Route it through maintain-test. |

## Boundaries

- Where code goes, and every class and method rule → pom-architecture and the specialist skills it routes to
- What a change affects → impact-analysis
- Running tests and the green rule → run-tests
