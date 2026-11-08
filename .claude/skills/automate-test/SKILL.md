---
name: automate-test
description: Use when asked to automate a designed test case, or a set of them, in a Playwright C# framework built with this harness - turning TC records into tests by observing the page, writing the code through the test-author agent, an independent review, breaking each new test once to prove it can fail, and two scoped green runs.
---

# Automate a test case

A designed test case (`docs/qa/test-cases/TC-<n>.md`) goes in. A test comes out that proves the
case's expected results, has been **seen to fail** when one of them is false, and has passed
**twice** in scoped runs. The procedure for each stage is in [the playbook](references/playbook.md).

**No test case, no automation.** Given a story, a ticket or a description, run
requirements-analysis and test-case-design first, then come back with the TC ids.

## The stages

Keep the working notes for each case in `.harness/state/automation/<TC-id>.md`, copied from
`assets/automation-notes-template.md`. That folder is gitignored. The notes hold the stage reached,
the review, the break result, the runs and the repair count.

1. [Source](references/playbook.md#1-source): read the TC and its REQ. The steps and expected results are the oracle. A gap goes back to test-case-design.
2. [Refine](references/playbook.md#2-refine): make the steps executable without changing what they prove.
3. [Observe](references/playbook.md#3-observe): see the real page with playwright-cli ([observe](references/observe.md)), or a probe test without it.
4. [Author](references/playbook.md#4-author): the `test-author` agent writes the code. Its gate is a clean build, `check-conventions` and `traceability`.
5. [Review](references/playbook.md#5-review): framework-review. Fresh agents try to refute the work and list every relaxed check, and `findings-verifier` checks the findings you'd act on (none, no verifier).
6. [Break it once](references/playbook.md#6-break-it-once): in a scratch copy, make one expected result false and watch the test go red.
7. [Verify](references/playbook.md#7-verify): two scoped runs through `suite-runner`, both green (run-tests).
8. [Repair](references/playbook.md#8-repair): at most three rounds per batch. Every change sends the case back through review, break and verify.
9. [Finish](references/playbook.md#9-finish): mark the case `automation: automated`, run `traceability`, and report.

## Who does each stage

You are the coordinator. Dispatch these agents with the Agent tool (`subagent_type` as shown),
and act on their reports. The general rules are in pom-architecture, under "Agents".

| Stage | Done by | `subagent_type` |
|---|---|---|
| 1–3 Source, refine, observe | you | none |
| 4 Author, and every repair | the author | `test-author` |
| 5 Review, through framework-review | a fresh reviewer, and the relaxed-check list | `framework-reviewer`, `loosened-reviewer` |
| 5 Check the findings you'd act on | the verifier | `findings-verifier` |
| 6 Break it once: you make the edit in the scratch copy; the agent runs it | the runner | `suite-runner` |
| 7 Verify: both runs in one command | the runner | `suite-runner` |
| 8 A failed run: diagnose before repairing | the investigator | `failure-investigator` |
| 9 Finish | you | none |

Report one line per case at the end:

```text
- TC-012: OrdersTests.RemovesADeletedOrderAsync; broke: red; runs: pass, pass
- TC-013: blocked at observe: the delete button needs an admin account (next: ask for a test admin)
```

## Gates you don't argue with

| Thought | Reality |
|---|---|
| "The test passed, so it works" | A test you never saw fail may assert nothing. Stage 6 is required. |
| "This expected result is awkward; I'll check something close to it" | Rewording or dropping an expected result changes what the case proves. Send it back to test-case-design. |
| "It fails because the product is wrong; I'll assert what it does now" | Keep the documented expectation. It stays red with `bug:<id>` (bug-report). |
| "Flaky, add a retry or a longer timeout" | That hides a defect (design-conventions §4 law 10). Diagnose it with maintain-test. |
| "I wrote it, I can review it" | The author never reviews their own work. Use a fresh subagent. |
| "Let me paste the test output here" | Runs go through `suite-runner`; only counts and first error lines come back. |
| "Third repair didn't work, one more" | After three rounds, stop, record it and ask the user. |

## Boundaries

- Where code goes, and every class and method rule → pom-architecture and the specialist skills it routes to
- Running tests and the green rule → run-tests
- A failing or flaky test that already exists → maintain-test
- A product defect found on the way → bug-report
- Running the case by hand, without code → execute-test
