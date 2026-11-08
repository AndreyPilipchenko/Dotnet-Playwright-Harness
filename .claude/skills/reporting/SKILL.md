---
name: reporting
description: Use when producing or reading test reports in a Playwright C# framework built with this harness - TRX results, a Markdown summary, the HTML results page or plan report, Playwright traces of failed tests, optional Allure output, or checking that reports hold no tokens or passwords before they are published or shared.
---

# Reporting

Every run leaves TRX in its own results folder, `TestResults/<label>/`. The other reports are built
from that folder and from `docs/qa/`. A report says what happened; the exit code of the test run
gives the verdict, so read [the exit codes](../run-tests/SKILL.md) first.

All commands start with `dotnet .harness/tools/bin/Harness.Tools.dll`.

| You want | Command | Exit |
|---|---|---|
| A Markdown summary of a run | `ci-summary --results TestResults/<label>` | 0, unless the arguments are wrong |
| To compare with earlier runs | add `--baseline <file>`, and `--update-baseline` to save this run | 0 |
| An HTML results page | `dashboard --run <job>=TestResults/<label>` (repeat per job) | 0, or 2 if no job has results |
| An HTML plan report | `tracker --results TestResults/<label>` | 0, or 2 if there are no QA records |
| A dated progress history | add `--history docs/qa/history.jsonl --record` to `tracker` | 0 |
| To check reports before sharing | `secret-scan --path <folder>` | 0 clean, 1 found, 2 not checked |

`dashboard` and `tracker` write under `TestResults/` by default, which git ignores. Add `--out` to
choose another file.

## Reading the summary

- **Known failures** carry a `bug:<id>` category. They stay red and are listed apart, so a new failure still stands out.
- With a baseline, each failure is newly failing or still failing, and a test that now passes is listed as fixed. A test that didn't run keeps its earlier outcome.
- A warning instead of a tick means no tests ran, or the run itself reported a failure that no test shows. Neither is a pass.
- An error replaced by "withheld" looked like it held a secret. Read the TRX locally.

## Traces of failed tests

Set `TRACE_DIR` to a folder and each failed test leaves a Playwright trace there, named after the
test. A passing test leaves nothing. It is off by default, because recording costs time on every test.
The base class `UiTest` does this through `TraceFiles` in `Utils`.

```powershell
$env:TRACE_DIR = "$PWD/TestResults/<label>/traces"
dotnet test --project tests/<Name>.Tests/<Name>.Tests.csproj --report-trx --results-directory TestResults/<label>
```

Open one with `bin/<cfg>/<tfm>/playwright.ps1 show-trace <file>` in the test project's output folder.

## Before anything is shared or published

Run `secret-scan` on the folder. It reads HTML, TRX, Markdown, JSON, XML, text, logs and CSV, inside
base64 and zip files too, so a trace counts. It prints the file and the kind of secret, never the value.

- Add `--forbid-env NAME,NAME` to also search for the values of those variables, such as a test account's password. A value under 8 characters can't be searched for, and the command says so.
- **Text inside a screenshot or video goes unnoticed.** Don't publish those from a run that signs in with a real account.
- A finding means the report doesn't leave the machine or the run. Remove the secret at its source; never print it to confirm it.

The dashboard holds test names and counts only. TRX and traces hold error text and page content.

## Allure (optional)

Traces and TRX are the baseline, and the skeleton has no Allure. Adding it is a new package, so
follow the approval steps in csharp-standards first. From `docs/framework-matrix.md`:

| | Package | Note |
|---|---|---|
| NUnit | `Allure.NUnit` 2.15.0 | Needs `[AllureNUnit]` on the class, and `directory` set in `allureConfig.json` |
| xUnit | `Allure.Xunit.v3` 3.0.0-preview.1 | Experimental. Results go to `<results-directory>/allure-results` |
| MSTest | `Allure.TestingPlatform` 3.0.0-preview.1 | Experimental. Results go to `<results-directory>/allure-results` |

## Boundaries

- Test output goes through the `suite-runner` agent. Read summaries and TRX, not raw logs.
- This skill reports. Diagnosing a failure is maintain-test, and coverage questions are plan-tracker.
