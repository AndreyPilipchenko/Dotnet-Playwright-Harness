# Maintain-test playbook

Adapted from the playwright-pom-harness maintenance procedure and its controlled-experiment
guidance (MIT), and from the claude-code-harness debug workflow (MIT). See NOTICE.md.

## 1. Scope

Record in the assessment:

- The test's full name, its `tc:` and `bug:` categories, and the test case it automates.
- The commit (`git rev-parse --short HEAD`) and any uncommitted changes to the files involved (`git status --short`).
- The environment (`BASE_URL`, browser) and the application build, if known.
- The requested outcome and any restriction, such as "evidence only".

## 2. Evidence

Read existing evidence before running anything.

| Source | How | Don't |
|---|---|---|
| A local TRX file | Read `TestResults/<label>/*.trx`: the test's outcome, `<Message>` and the first lines of `<StackTrace>` | Paste the whole file into the conversation |
| A new local run | Scoped to that one test, with its own label (run-tests), through `suite-runner` | Rerun a command that just failed: the hook blocks it even with a new label. Read its TRX instead |
| CI | `gh run list --json databaseId,workflowName,conclusion,headBranch`, then `gh run view <id> --json jobs`, then `gh run download <id> -n <results artifact> -D TestResults/ci/<id>` (gitignored), then read the TRX there. Parse with `ConvertFrom-Json`, never `--jq` | Use `gh run view --log-failed` into the conversation (it can be over 1 MB). If you need a log, have `suite-runner` extract only the failing step |
| The code | The test, its page, Api and Helpers methods, the paired JSON, and `git log -p --follow` on them since the last green run | Read the whole framework |

Record each piece of evidence with its path or run id. Note any that's missing or stale, such as
TRX from another commit.

## 3. Diagnosis

Dispatch the `failure-investigator` agent with:

- the scoped run command
- the error (the first error line and the assertion message)
- what was being attempted, and any fix attempts so far
- `git diff` of the relevant files
- the file paths

It returns a classification and 3–5 ranked hypotheses. Each one has a claim, one change that
tests it, the expected result, and the evidence that would disprove it. Copy them into the
assessment. Test them in order, cheapest decisive check first.

Typical causes for Playwright tests, to check before guessing:

- **Locator:** the role or name changed, strict mode now matches two elements, or the element moved into a frame or dialog (element-locators).
- **Timing:** an action ran before the app was ready, or a check reads a list while it is still loading. The fix is a web-first wait on the right condition, never a sleep.
- **Data:** a value isn't unique per run, a record was left by another test, the seed failed silently, or the expected value changed in the JSON.
- **Cleanup:** an earlier test's teardown didn't run or didn't delete, so this test sees extra rows.
- **Environment:** `BASE_URL`, an expired secret, the app down, a browser not installed.

## 4. Controlled experiments

Use them when the evidence leaves two or more causes open, typically for intermittent or CI-only
failures. Record each one in the assessment's experiment table:

| Fact | What to write |
|---|---|
| Baseline | The exact test, conditions and failure signature (first error line) |
| Hypotheses | The suspected cause and the plausible alternatives, with the facts for and against each |
| Prediction | What each hypothesis predicts, and the result that would disprove the suspected one |
| Check | **One** factor changed, everything else held the same, and the attempts left |
| Observation | The actual result, or `not run` |
| Interpretation | Which hypotheses gained or lost support, and what's still unknown |

- Change one factor at a time: the browser, `HEADED`, run alone vs. with its class, local vs. CI, a fresh data value. A pass after changing two factors proves nothing about either.
- Set a bound before the first experiment, such as 5 runs of that one test. When it's reached, stop and record the uncertainty and the next action.
- Repeats of the same run go in **one** `;` command, each with its own `--results-directory`. After a failed run the pre-shell hook blocks an identical command, so separate repeat commands would be refused. Or change a real factor between them (the filter, `BROWSER`, `HEADED`).
- Count what you saw honestly: "failed 2 of 10 runs alone, 0 of 10 with `HEADED=1`". Keep the first failure even when later runs pass.
- A run with a repeat or loop flag is an experiment, never a green run.

## 5. Fix and re-verify (diagnose and fix only)

- Fix only a diagnosed test or framework defect, at its cause: the locator in the page class, the wait in the `Verify*` method, the data in the JSON, the cleanup in the Api class.
- Then follow automate-test stages 5–7, in full: framework-review, break it once in a scratch copy, and the two scoped runs in one command (automate-test stage 7). Count the round in the case's automation notes, if there is one.
- **Three attempts:** when three fixes in a row haven't turned it green, stop. Dispatch `failure-investigator` again with all three attempts and their results. The next fix must test a new hypothesis.
- A product defect goes to bug-report. An accepted requirement change goes to requirements-analysis and test-case-design.

## 6. Record the outcome

Finish the assessment: the classification and why, what was changed, the run labels and their
results, and anything still open with its owner. Never edit an old assessment after its evidence
changed; start a new one. Report to the user:

```text
OrdersTests.RemovesADeletedOrderAsync: test defect (strict mode: two "Delete" buttons after the redesign)
fixed in OrdersPage.DeleteButton(id), scoped to the row; broke: red; runs: pass, pass
assessment: .harness/state/maintenance/OrdersTests.RemovesADeletedOrderAsync-1.md
```
