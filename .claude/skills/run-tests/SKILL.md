---
name: run-tests
description: Use when running, scoping or reading the result of tests in a Playwright C# framework built with this harness - running one test case, one test or a category, deciding whether a run counts as green, reading exit codes and skipped or zero-test runs, keeping test output out of the conversation, or a second run that the pre-shell hook blocks.
---

# Running tests

Every run goes through the `suite-runner` agent, except the one reproduction run that
`failure-investigator` makes itself. It runs the command, reads the output and returns
only the counts and the first error line per failing test, so the logs stay out of this
conversation. The commands below are from `docs/framework-matrix.md` (verified on all three
frameworks). Check `.harness/project.json` for the framework first.

## Commands (repository root)

Base command; add one filter from the table:

```powershell
dotnet test --project tests/<Name>.Tests/<Name>.Tests.csproj --report-trx --results-directory TestResults/<label>
```

| Run | NUnit / MSTest | xUnit v3 |
|---|---|---|
| Everything | (no filter) | (no filter) |
| One category | `--filter "TestCategory=smoke"` | `--filter-trait "Category=smoke"` |
| One test case | `--filter "TestCategory=tc:TC-012"` | `--filter-trait "Category=tc:TC-012"` |
| One test method | `--filter "Name=RemovesADeletedOrderAsync"` | `--filter-method "<Namespace>.<Class>.RemovesADeletedOrderAsync"` |
| Part of a name | `--filter "FullyQualifiedName~OrdersTests"` | not used: `--filter-method "*X"` also matches every name ending in X |

- Add `--no-build` when the brief already ran `dotnet build`.
- Browser: the `BROWSER` (`chromium`, `firefox`, `webkit`) and `HEADED` environment variables. After a build or a Playwright update, install the browser once with `tests/<Name>.Tests/bin/Debug/<tfm>/playwright.ps1 install chromium`.

## Exit codes and what counts as green

| Exit | Meaning |
|---|---|
| 0 | Passed, **but** check `skipped:`. A skipped test still gives `Passed!` and 0 |
| 2 | Test failures |
| 5 | Invalid command line: fix the command, not the test |
| 8 | `Zero tests ran`: the filter matched nothing |

A run is **green** only when all of these hold:

1. Exit code 0.
2. `skipped: 0`.
3. `total:` equals the number of tests you meant to select. Count them first from the source: data rows count once each.

A `no tests` run (exit 8) is never green. Most often it is a mistyped id or a missing category.

## Run labels

Give every run its own label: `--results-directory TestResults/<label>`, such as
`verify-TC-012-1` and `verify-TC-012-2`.

Each run keeps its own TRX file as evidence.

**The rerun block.** The pre-shell hook blocks a `dotnet test` command that already failed until
a source, test or data file is edited. It compares the command **without** `--results-directory`,
`--report-trx`, `--no-build` and `--no-restore`, so a new label doesn't make a failed run new. A
blocked rerun means: diagnose first (maintain-test). Never look for a variant of the command that
gets past it.

**Two planned runs** (automate-test verification) go in **one** shell command, each with its own
label, so the second isn't a rerun of a failure:

```powershell
dotnet test --project <csproj> --no-build --report-trx --results-directory TestResults/verify-TC-012-1 --filter "TestCategory=tc:TC-012"; dotnet test --project <csproj> --no-build --report-trx --results-directory TestResults/verify-TC-012-2 --filter "TestCategory=tc:TC-012"
```

## Briefing `suite-runner`

Give it:

- the work folder (the repository root, or a scratch copy)
- the exact commands in order, such as `dotnet build` and then the test command
- the expected test count for each run

It returns a `STATUS:` line, then per command the exit code, the summary line, the counts and the
TRX path, and per failing test its name and first error line. Read the counts against the green
rule above yourself; don't take "passed" on trust.

## Boundaries

- Attributes, categories, data rows, hooks → test-framework
- A failing or flaky test → maintain-test
- Automating a test case, including its two verification runs → automate-test
