# Firefox and WebKit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Firefox and WebKit are verified on all three frameworks on Windows and Linux. A project's
nightly CI runs the browsers the user chose, asked by ci-pipeline and rendered by
`scaffold-ci --browsers`.

**Architecture:**
- A spike decides what may be claimed.
- `Scaffolder.RunCi` gets a `Browsers` list, which renders the `{{Browsers}}` and `{{SmokeBrowser}}`
  tokens into the nightly matrix and the smoke job.
- The repo's CI runs the skeleton job as framework × browser.
- The skills ask the user and document installation.

**Tech Stack:** C# net10.0 `Harness.Tools`, xUnit v3, GitHub Actions YAML templates, Docker
(`mcr.microsoft.com/dotnet/sdk:10.0`) for the Linux spike.

**Spec:** `docs/superpowers/specs/2026-11-21-firefox-webkit-design.md`

## Global Constraints

- Browser names, exact: `chromium`, `firefox`, `webkit`. `--browsers` defaults to `chromium`.
- `--browsers` errors (unknown, duplicate, empty) are usage errors that write nothing.
- A browser is claimed only where the spike ran it. The matrix records each OS × framework × browser
  result.
- Smoke runs one job, on the first browser in the list.
- Nightly matrix: `fail-fast: false`, one results artifact per browser (`results-regression-<browser>`).
- No commits, pushes or PRs. Each task ends with a suggested commit message, and the phase ends with
  a PR description.

## Review Focus

1. **`--browsers " firefox , webkit "`** (spaces, mixed case): trimmed and lower-cased, not rejected.
   Tested in Task 2.
2. **The default output:** a project that runs `scaffold-ci` without `--browsers` gets one nightly
   Chromium job with today's artifact name changed only by the suffix. The `reports` job still finds
   it. Tested in Task 2.
3. **Inputs reach shells only through the environment** (the existing test): the matrix value must
   not appear inline in `run:` blocks. Covered by the existing
   `Inputs_ReachShellsThroughTheEnvironment_NeverInline`.
4. **The dashboard command with one `--run` per browser:** it has to stay a single command, built
   from the downloaded folders. Tested in Task 2 (the rendered text) and in the Linux spike (the
   command form).
5. **WebKit on Linux needs system libraries:** `--with-deps` must stay on the install step. Tested in
   Task 2.

---

### Task 1: Spike

**Files:**
- Modify: `docs/framework-matrix.md` (a new section, "Firefox and WebKit")

- [ ] **Step 1, Windows:** scaffold nunit, xunit and mstest into the scratchpad and build each. Run
  `playwright.ps1 install firefox webkit`. Then, for each framework and each of firefox and webkit, run
  `$env:BROWSER='<b>'; dotnet test --project tests/<Name>.Tests/<Name>.Tests.csproj --no-build`.
  Record the counts.
- [ ] **Step 2, Linux:** in `mcr.microsoft.com/dotnet/sdk:10.0` (pwsh is included), with the three
  scaffolded projects copied into the container:
  - `playwright.ps1 install --with-deps chromium firefox webkit`
  - build, then run each framework × browser
  - run one `dashboard` with two `--run` arguments, to confirm the command form
- [ ] **Step 3:** Record the installed browser builds and every result. **Stop and bring it to the
  user** if a browser fails on an OS.
- [ ] **Step 4:** Suggested commit message: `Record the Firefox and WebKit spike`

### Task 2: `scaffold-ci --browsers` and the templates

**Files:**
- Modify:
  - `tools/Harness.Tools/Scaffolding/Scaffolder.cs` (`RunCi` tokens, browser validation)
  - `tools/Harness.Tools/Cli.cs` (the `--browsers` option and usage)
  - `templates/ci/dot.github/workflows/nightly.yml`
  - `templates/ci/dot.github/workflows/smoke.yml`
- Test: `tools/Harness.Tools.Tests/Scaffolding/ScaffoldCiTests.cs`

**Interfaces:**
- Produces:
  - `ScaffoldOptions.Browsers` (`IReadOnlyList<string>?`, init). Null means `["chromium"]`.
  - `Scaffolder.ParseBrowsers(string list)`, returning a normalized `IReadOnlyList<string>`, or
    throwing `ScaffoldException`.
  - Tokens: `{{Browsers}}` (`chromium, firefox`) and `{{SmokeBrowser}}`.

- [ ] **Step 1: Write the failing tests:**
  - `Default_RendersAChromiumOnlyNightlyMatrix`
  - `Browsers_RenderTheMatrixInOrder` (`firefox,webkit` gives `browser: [firefox, webkit]`)
  - `Browsers_AreTrimmedAndLowerCased`
  - `Smoke_RunsOnTheFirstBrowser`
  - `[Theory] BadBrowsers_AreAUsageError_AndWriteNothing("edge" | "chromium,chromium" | "" | ",")`
  - `Nightly_IsAFailSlowMatrix_WithOneArtifactPerBrowser` (`fail-fast: false`,
    `results-regression-${{ matrix.browser }}`)
  - `Reports_PassOneRunPerBrowser`
  - `Install_KeepsWithDeps`
- [ ] **Step 2:** Run `--filter-class "Harness.Tools.Tests.Scaffolding.ScaffoldCiTests"`. Expected:
  FAIL.
- [ ] **Step 3: Implement.**
  - The reports job downloads with `pattern: results-regression-*` and `merge-multiple: false`, then
    builds `--run` arguments in a bash loop over the downloaded folders. No `${{ }}` inside `run:`.
- [ ] **Step 4:** Run the class, then the full suite. Expected: PASS.
- [ ] **Step 5:** Suggested commit message: `scaffold-ci --browsers: a nightly browser matrix from the user's choice`

### Task 3: The repo's CI

**Files:**
- Modify: `.github/workflows/ci.yml` (the skeleton job's matrix becomes framework × browser, and the
  install step installs `matrix.browser`)

- [ ] **Step 1:** Edit. Keep the job names unique: `skeleton (<framework>, <browser>)`.
- [ ] **Step 2:** Check that the YAML parses (`ConvertFrom-Yaml` isn't available, so use the
  repo's existing approach, or `dotnet` with YamlDotNet if a test exists). The user's push is the
  real run.
- [ ] **Step 3:** Suggested commit message: `CI: run the skeleton on Chromium, Firefox and WebKit`

### Task 4: Skills and docs

**Files:**
- Modify:
  - `.claude/skills/ci-pipeline/SKILL.md`: before `scaffold-ci`, ask which browsers nightly covers,
    with the cost, and pass `--browsers`; smoke uses the first; drop the "only Chromium" line.
  - `run-tests`, `test-framework`, `scaffold-framework` (and its playbook) and the harness-setup
    playbook: install `firefox` or `webkit` the same way as Chromium, `--with-deps` on Linux.
  - `README.md` ("Only Chromium is verified")
  - `docs/porting-notes.md` (remove the item)
  - the "Not verified" lines in `test-framework/references/playbook.md`
- [ ] **Step 1:** Make the edits, citing Task 1's facts only.
- [ ] **Step 2:** `validate-skills` and the full suite. Expected: clean.
- [ ] **Step 3:** The final commit message and the PR description.
