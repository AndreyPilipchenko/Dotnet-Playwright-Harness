---
name: ci-pipeline
description: Use when adding, adapting or reading continuous integration for a Playwright C# framework built with this harness - generating the GitHub Actions workflows, static checks, smoke and nightly runs, the application URL and secrets in CI, or finding out what a CI run did from its results artifacts.
---

# CI pipeline (GitHub Actions)

## 1. Generate the workflows

**Ask the user first.** A workflow starts running, and using minutes, as soon as it is pushed.

```powershell
dotnet .harness/tools/bin/Harness.Tools.dll scaffold-ci
```

It reads the framework and name from `.harness/project.json`, and never overwrites an existing file.

| File under `.github/` | Runs | Does |
|---|---|---|
| `workflows/static-checks.yml` | Pull requests, by hand | Build with warnings as errors, check-conventions, traceability, validate-skills, vulnerable packages |
| `workflows/smoke.yml` | Pushes to `main`, by hand | The `smoke` category on Chromium |
| `workflows/nightly.yml` | 02:00 UTC, by hand | Every test on Chromium, an outdated-package report, then the HTML reports |
| `actions/setup-harness` | Called by the jobs | .NET from `global.json`, the harness tool from source, the build, the browser |
| `actions/run-tests` | Called by the jobs | Tests, summary, secret check, results artifact, then the run's verdict |

## 2. Adapt it

- **The application:** set the repository variable `BASE_URL`. The default branch is assumed to be `main`, so change `smoke.yml` if yours isn't.
- **Secrets:** create them in the repository settings, then map each one onto the `uses: ./.github/actions/run-tests` step under `env:` as `${{ secrets.NAME }}`. Pass its name in `forbid-env`, so the results are searched for its value before they are published. Never read, print or ask for a value.
- **Another category:** `filter-option` and `filter-value` take the framework's filter, as in test-framework.
- **Reports stay private to the repository's readers.** They are uploaded as artifacts for 14 days. Publishing them to a site is a separate decision for the user, and `secret-scan` must pass first.
- **Only Chromium on Linux runners is set up.** Firefox, WebKit and the Linux results are not yet recorded as verified in `docs/framework-matrix.md`, so treat a first run there as a test of the pipeline.
- `.github/workflows/` changes are ordinary changes: the user commits and pushes them.

## 3. Read a run

Read the results artifact, never raw logs. Use `--json` with `ConvertFrom-Json`, because `--jq`
breaks on Windows PowerShell 5.1.

```powershell
gh run list --json databaseId,workflowName,conclusion,headBranch | ConvertFrom-Json
gh run view <id> --json jobs | ConvertFrom-Json
gh run download <id> -n results-<label> -D TestResults/ci/<id>
dotnet .harness/tools/bin/Harness.Tools.dll ci-summary --results TestResults/ci/<id>
```

Each results artifact holds the TRX, `summary.json` (counts and failing test names) and, for failed
tests, Playwright traces. See reporting for what they mean. Hand a failure to maintain-test.

`gh run view --log-failed` can return over 1 MB, so never send it to the conversation. If a log is
really needed, have the `suite-runner` agent extract only the failing step.

## 4. What the steps mean

- A failing test doesn't stop the job early. The summary, the secret check and the upload run first, and the last step gives the test run's exit code.
- Results that hold a secret fail the job **before** they are uploaded.
- `package-report` reads the JSON of `dotnet package list`, because that command's exit code says nothing about findings. A vulnerable package fails static checks. An outdated one is only reported.
- The baseline of earlier outcomes lives in the Actions cache, one per label. If the cache is evicted, the next summary simply lists every failure together.

## Boundaries

- Running, re-running or watching a workflow (`gh workflow run`, `gh run rerun`, `gh run watch`) only when the user asks. `hook pre-shell` asks before the first two.
- Never weaken a check to make CI green: no skips, no `continue-on-error` on a test or check step. A red run is a finding for maintain-test.
