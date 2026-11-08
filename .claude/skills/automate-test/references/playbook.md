# Automate-test playbook

Adapted from the playwright-pom-harness automation pipeline (MIT, see NOTICE.md). Its engine
(source fingerprints, candidates, receipts) is replaced by the notes file, agents, `dotnet test`,
`check-conventions` and `traceability`.

The tool is `dotnet .harness/tools/bin/Harness.Tools.dll` in a consumer project. Paths below are
from the repository root.

## 1. Source

- **In:** one or more TC ids. Read each `docs/qa/test-cases/TC-<n>.md` and the requirements it names.
- **Check:** `automation` is `planned` (or `automated` when re-automating). Every step has an action and an observable expected result. The prerequisites and test data are concrete.
- **Out:** a notes file per case, from `assets/automation-notes-template.md`, with the TC's steps copied into `Source` unchanged.
- **Gate:** if an expected result is missing, vague ("works correctly") or contradicts the requirement, stop for that case. Write it up as a question through test-case-design. Never invent an expected result.

## 2. Refine

Rewrite the steps so they can be executed, without changing what the case covers or proves.
Write the result under `Refinement` in the notes, never into the TC.

**Allowed:**
- Add implicit steps: open the app, sign in as a test user, dismiss a cookie banner.
- Turn a prerequisite into setup through an Api or Db class (design-conventions §5), when one exists or can be added.
- Split a compound step, and make a vague action concrete ("choose a product" → the product named in the test data).
- Bind data to the paired JSON, a `UniqueData` value or a secret name (test-data).
- For each expected result, note whether the UI or an API check proves it best.

**Forbidden:**
- Dropping, weakening or rewording an expected result.
- Adding checks the case doesn't ask for.
- Inventing data that must be real: an account, a price, a message text.

Anything that can't be resolved goes in the notes as a blocker, with the question to ask.

## 3. Observe

See the real application before writing code. Follow [observe](observe.md).

- **Record in the notes:** for each step, the element's role and accessible name (or label, or test id), the URL after it, any API call it made, and the exact text of each expected result as the page shows it.
- **A reliable failure is evidence, not a new expectation.** When the app doesn't do what the case expects, keep the case's expectation. Note what you saw, and carry on: the test will be red. Draft the BUG record now through bug-report (a file only; filing a GitHub issue still needs the user), so the test can carry `bug:<id>` for an open record before stages 6 and 7. See [a case that must be red](#a-case-that-must-be-red).
- **Cleanup:** note what the case creates, and how the test can delete it through an Api class.

## 4. Author

Dispatch the `test-author` agent with:

- the TC file and the notes file (refinement and observations)
- the framework from `.harness/project.json`
- the existing classes it may reuse: list `src/<Name>.Framework/Pages`, `Apis`, `Dbs` and `Helpers`

It writes the code through pom-architecture. Each test carries `tc:<id>` plus a vocabulary
category. It returns the files it changed, the mapping from each step and expected result to a
method, and its gate results:

```powershell
dotnet build                                                   # 0 warnings, 0 errors
dotnet .harness/tools/bin/Harness.Tools.dll check-conventions  # exit 0
dotnet .harness/tools/bin/Harness.Tools.dll traceability       # no failures; case-not-marked-automated is expected until stage 9
```

Record the files and the mapping in the notes. A failing gate goes back to the author, and doesn't
use a repair round.

## 5. Review

1. Dispatch a **fresh** subagent (`subagent_type: general-purpose`), never the author, with [the review brief](review-brief.md), the TC, the notes and the changed files. Fill in the brief's bracketed parts, and add any facts about the app it can't see in code, such as "the filter links are also list items".
2. Give `findings-verifier` (`subagent_type: findings-verifier`) the findings you would act on: every blocking one, and the non-blocking ones you plan to fix. Include the same files. It answers CONFIRMED, REFUTED or UNCERTAIN per finding, with evidence. Non-blocking findings you won't fix go straight into the report.
3. **CONFIRMED and blocking:** repair (stage 8). **UNCERTAIN and blocking:** read the code yourself and decide; note why. **REFUTED:** record and drop. **Not blocking:** fix it if it's cheap; otherwise list it in the report.
4. A blocking finding stays open until it's fixed, or until the **user** accepts it in writing. Note who accepted it. An agent never accepts on the user's behalf.

## 6. Break it once

This proves each new test can fail. Do it after the review is clean, for every new or changed test.

1. `dotnet .harness/tools/bin/Harness.Tools.dll scratch-copy --to <scratchpad>\break-<TC-id>`. The folder must be new and outside the repository.
2. In the copy, use Edit to change the **one** thing that makes an expected result true. Usually that is the expected value in the copy's `resources/testData/<Feature>TestData.json`, such as `"0 items left"` → `"9 items left"`. If the value isn't data, change the `Verify*` target in the copy instead.
3. Through `suite-runner`, with the work folder set to the copy, run `dotnet build` and then only that test (run-tests), labelled `break-<TC-id>`.
4. **Expect red on that assertion:** exit 2, and the first error line names the changed value. Record `broke: red` in the notes.
   - **Still green:** the test doesn't check that result. Record `broke: green` and go back to Author. This is a blocking finding.
   - **Red for another reason** (build error, a different assertion): the break was wrong. Fix the break and try again.
5. Delete the scratch folder. The repository was never touched, so nothing needs restoring.

For a case with several expected results, break the one most likely to be checked weakly: a text, a count, an absence. Break more than one when the review doubted them.

The break run happens in the scratch copy, which is outside the project, so the pre-shell rerun
hook doesn't count it: it neither blocks a second break attempt nor the stage 7 runs. That works
because the hook reads the folder the run happens in. `suite-runner` starts each command with
`Set-Location <work folder>` for that reason. Don't run the break from the project root with a path
to the copy.

### A case that must be red

When Observe showed that the app doesn't meet an expected result, the test is meant to fail on that
assertion (`bug:<id>`, open BUG record):

- **Stage 6:** a red run on the defect assertion is the evidence that the test can fail. Don't break that result. Break any other expected result of the case as usual.
- **Stage 7:** both runs must be red, with the same first error line, on the defect's assertion. A different failure, or a pass, is not verified.
- If the defect is fixed later, bug-report removes `bug:<id>` and the test must go green.

## 7. Verify

Two scoped runs, after the last change to any file. Run them through `suite-runner` with the
repository root as the work folder. The two runs are **one** shell command, joined with `;`. The
pre-shell hook ignores the results folder when it compares commands, so as two separate commands,
a red first run (a known defect) would block the second (run-tests):

```powershell
dotnet build
dotnet test --project tests/<Name>.Tests/<Name>.Tests.csproj --no-build --report-trx --results-directory TestResults/verify-TC-012-1 --filter "TestCategory=tc:TC-012"; dotnet test --project tests/<Name>.Tests/<Name>.Tests.csproj --no-build --report-trx --results-directory TestResults/verify-TC-012-2 --filter "TestCategory=tc:TC-012"
```

If the hook blocks this command, a run with the same selection already failed and nothing was
edited since. Don't verify yet: diagnose it (stage 8).

(xUnit: `--filter-trait "Category=tc:TC-012"`.) Give `suite-runner` the expected test count: one
per test method, plus one per extra data row.

A run counts when it meets the green rule in run-tests (exit 0, `skipped: 0`, the expected
`total:`), with one exception. A test **may** fail when it carries `bug:<id>` for an **open**
BUG record and its first error line is that defect's assertion (master plan call 13). Both runs
must agree. A pass in one run and a failure in the other is not verified: investigate it with
maintain-test and don't count either run.

## 8. Repair

- A repair is any change after the first review: a CONFIRMED finding, a `broke: green`, or a failed verification run.
- At most **three rounds per batch** of cases. Count them in the notes (`Repairs (n of 3)`). A fix that only gets the author's own gate passing isn't a round.
- Every round sends the case back through stages 5, 6 and 7. Earlier review results, breaks and green runs no longer count.
- Diagnose before repairing a failed run: the failure-investigator route in maintain-test. Never repair by weakening an expectation, adding a retry or skip, or raising a timeout.
- **After the third round:** stop. Mark the case blocked in the notes with the evidence, and ask the user how to proceed.

## 9. Finish

1. For each verified case, set `automation: automated` in the TC file.
2. Run `traceability`. It must report no failures, and no `case-not-marked-automated` for these cases.
3. Report one line per case (see SKILL.md), with blocked cases and their next action.
4. **Post-run bug review:** for every red test, or every expectation the app didn't meet during Observe, classify the cause: product, test, environment or unknown. Draft a bug record through bug-report for product defects. Show the drafts to the user before filing anything, and never run `gh issue create` unasked. A failed assertion alone doesn't prove a product defect: check the expectation is documented and the test is sound.
5. Never commit. Suggest a commit message, such as `Automate TC-012 (orders: delete removes the order)`.
