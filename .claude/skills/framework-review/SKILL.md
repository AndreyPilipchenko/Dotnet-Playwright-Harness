---
name: framework-review
description: Use when reviewing test or framework changes against this harness's conventions in a Playwright C# framework - "review this diff", "run framework review", the review stage of automate-test, maintain-test or run-impl-loop, or any check before a commit. Runs the mechanical checks, walks the checklist by layer, lists every relaxed check, and returns findings with evidence and an APPROVE or CHANGES-REQUIRED verdict.
---

# Framework review

Turn the review checklist into a verdict that can be acted on. It answers "does this follow the
conventions, and does each test prove its case?". For hunting logic bugs in general code, use a
general code review instead.

Two rules come first:

1. **The reviewer is never the author.** Always dispatch a fresh agent. If no agent is available, say so and report the review as **not done**.
2. **A finding that can't name its evidence isn't a finding.** Each one has a `file:line`, the rule it breaks and what goes wrong. Unproven suspicions go under "Questions". An empty list is a valid result.

## Steps

You coordinate. The general rules are in pom-architecture, under "Agents".

| Step | Job | `subagent_type` |
|---|---|---|
| 1 | Review the scope, and return the verdict block ([procedure](references/procedure.md)) | `framework-reviewer` |
| 2 | List every relaxed check. Can run alongside step 1, since both only read | `loosened-reviewer` |
| 3 | Check the findings you'd act on: every blocking one, and the others you plan to fix | `findings-verifier` |
| 4 | Decide, and record | you |

**Brief for steps 1 and 2:** the work folder and the scope (a base ref, default `main`, or a list of files). For step 1 also give the TC and notes files, or the `impl` notes file, the author's gate results, and any facts about the app that can't be seen in code.

## Decide

| Result | What happens |
|---|---|
| CONFIRMED and blocking, or a blocking relaxed check | **CHANGES-REQUIRED.** It goes back to the author (`test-author` or `framework-implementer`). Never fix it in the reviewing context |
| UNCERTAIN and blocking | Read the code yourself and decide. Note why |
| REFUTED | Record it and drop it |
| Not blocking | Fix it if it's cheap. Otherwise list it in the report |
| A check failed: build, `check-conventions` or `traceability` | Always **CHANGES-REQUIRED**, whatever else was found |

- **A blocking finding stays open** until it's fixed, or the **user** accepts it in writing. Note who accepted it. An agent never accepts on the user's behalf.
- **The ship test,** before calling anything a follow-up: with this left undone, could a test pass while its expected result is false, or would the framework misbehave for the project's real inputs? If yes, it can't wait, whatever severity it was given.
- **A real deferral is written down:** the reason, the owner and what brings it back, in the report or a BUG record. "Follow-up" on its own isn't a deferral.
- After a fix, the review runs again, scoped to what changed since the last verdict.

## The class ledger

Each CONFIRMED finding has a `class` slug, such as `first-row-targeting`. Reviews teach the checker
this way instead of repeating themselves.

1. Create `.harness/review/class-ledger.md` from [the template](assets/class-ledger-template.md) if it doesn't exist. It is committed, so keep it free of credentials and customer data.
2. Add one row per CONFIRMED finding. You write it: the reviewer edits nothing.
3. **When a class reaches three rows,** add a line under "Rule proposals": a concrete `check-conventions` rule with a passing and a failing example, or "cannot be mechanised because <reason>". A proposal is not permission. Building the rule is a normal reviewed change, and it needs the user's approval because it changes the harness.

Never add to `.harness/conventions-baseline.json` to hide a new finding.

## Boundaries

- Fixing what the review found → the specialist skill that owns the rule (pom-architecture routes to it)
- Why a test fails → maintain-test
- Whether a document is fully tested → coverage-audit
- Comments from reviewers on a pull request → pr-review-loop
- Turning repeated classes into proposed harness changes → improve-harness
