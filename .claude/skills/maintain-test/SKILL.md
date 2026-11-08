---
name: maintain-test
description: Use when a test fails, is flaky, broke after an application or framework change, or fails only in CI in a Playwright C# framework built with this harness - diagnosing why with evidence and falsifiable hypotheses, and, when asked, fixing a test or framework defect and re-verifying it. Product defects stay red with a bug report.
---

# Maintain a test

Find out **why** a test fails before anyone changes it. Then fix it only if the test or the
framework is wrong. The procedure is in [the playbook](references/playbook.md).

## Choose the outcome from the request

| The request says | Outcome | Stop when |
|---|---|---|
| "why does it fail", "diagnose", "investigate", "what's wrong" | **Diagnose only** | The assessment is written, with the classification, evidence, uncertainty and next actions. Change no code, test case or bug record. |
| "fix", "repair", "make it pass again", "diagnose and fix" | **Diagnose and fix** | The diagnosis is done, a supported fix has passed review, break-it-once and two green runs, or the cause is outside the test and handed to its owner. |
| "don't run anything", "evidence only" | Either outcome, without running tests | Proposed checks are labelled `not run`. |

Not sure which? Diagnose only, and offer the fix.

## The steps

1. **Assess:** copy `assets/assessment-template.md` to `.harness/state/maintenance/<test-or-TC>-<n>.md` (gitignored, a new file each time). Fill in scope and evidence. See the [evidence sources](references/playbook.md#2-evidence).
2. **Diagnose:** dispatch the `failure-investigator` agent. Run [controlled experiments](references/playbook.md#4-controlled-experiments) when the evidence doesn't settle it, changing one factor at a time.
3. **Classify** with evidence: one of the six causes below. Record mixed causes separately.
4. **Fix (diagnose and fix only):** follow the route for the cause.

| Cause | Route |
|---|---|
| Test or framework defect: a locator, wait, data, cleanup or code error | Dispatch `test-author` with the diagnosis, and write the code through pom-architecture's conventions. Then run automate-test's [review](../automate-test/references/playbook.md#5-review), [break it once](../automate-test/references/playbook.md#6-break-it-once) and [verify](../automate-test/references/playbook.md#7-verify). |
| Product defect | Don't change the test. Write the record and add `bug:<id>` (bug-report). It stays red until the fix ships. |
| Accepted requirement change | requirements-analysis and test-case-design update the REQ and the TC first, then automate-test re-automates. |
| Environment or dependency: app down, test data missing, a credential expired, a CI image | Name the owner and the exact condition to fix. No code change. |
| Intermittent | Keep diagnosing until it has a cause. "Flaky" isn't a cause, and a single later pass doesn't prove a flake. |
| Unknown | Record what's known and the next check. Don't guess a fix. |

## Who does what

You coordinate. The general rules are in pom-architecture, under "Agents".

| Job | `subagent_type` |
|---|---|
| A scoped diagnostic run, or the verification runs after a fix | `suite-runner` |
| Classification and ranked hypotheses; again after three failed attempts | `failure-investigator` |
| The fix itself, for a test or framework defect | `test-author`, briefed with the diagnosis and the hypothesis being tested |
| Review, verify the findings, break it once | as in automate-test's "Who does each stage" |
| Reading TRX and CI artifacts, controlled experiments, the assessment | you |

## Rules

- **Three attempts, then rethink.** After three fix attempts that didn't turn it green, stop. Dispatch `failure-investigator` again with what was tried. Never try a fourth variant of the same idea.
- **Never weaken to go green:** no skip, retry, raised timeout, loosened assertion or deleted check (design-conventions §4 law 10).
- **Never rerun an unchanged failed command.** The pre-shell hook blocks it until a source, test or data file is edited. A new results folder doesn't count as a change. A diagnostic run must change one real factor: a narrower filter (that one test alone), `$env:BROWSER` or `$env:HEADED`. Planned repeats go in one `;` command. Record it as an experiment. Never look for a variant just to get past the block.
- **A diagnostic pass is not verification.** Only the two runs after the final fix count.
- Changing what a test expects needs an accepted requirement change, with its source. A new observation is not acceptance.

## Boundaries

- "What does this change affect, what should we rerun?" with no failure to fix → impact-analysis
- Writing the bug record and its `gh` issue → bug-report
- New automation for a case that has none → automate-test
- Running tests and the green rule → run-tests
