---
name: scaffold-framework
description: Use when starting a Playwright C# test framework from nothing in a project where this harness is installed, when asked to "scaffold", "set up", "bootstrap" or "create the framework" (NUnit, xUnit or MSTest), or when adapting the scaffolded walking skeleton to the real application under test - its URL, its first page and its first real test.
---

# Scaffold the framework

Goal: a **walking skeleton** that builds with zero warnings, passes one test on Chromium,
writes a TRX report and is clean under `check-conventions`. Then it is adapted to the real app,
one page and one test at a time.

**Follow [the playbook](references/playbook.md) step by step.** Don't hand-write the project
files: `Harness.Tools scaffold` renders them from templates whose versions and settings were
verified by spike.

## Preconditions

- The harness is installed: `.harness/tools/bin/Harness.Tools.dll` and `.harness/project.json` exist. If not, ask the user to run `.harness/setup.ps1` (or the harness's `scripts/setup.ps1 -Target <project>`), then restart Claude Code.
- `.harness/project.json` names the framework (`nunit`, `xunit` or `mstest`) and the project name. Confirm both with the user before you scaffold; changing the framework later means porting every test.
- No `src/` or `tests/` folder exists yet. `scaffold` never overwrites a file, and stops before writing anything if one exists.

## Steps (details in the playbook)

1. Scaffold: `dotnet .harness/tools/bin/Harness.Tools.dll scaffold --framework <framework> --name <Name>`
2. Build: `dotnet build`, which must report 0 warnings.
3. Install the browser: `tests/<Name>.Tests/bin/Debug/<tfm>/playwright.ps1 install chromium`
4. Run: `dotnet test --project tests/<Name>.Tests/<Name>.Tests.csproj --report-trx --results-directory TestResults`
5. Check: `dotnet .harness/tools/bin/Harness.Tools.dll check-conventions`
6. Adapt to the app (playbook §3). The skeleton test uses `SetContentAsync` and needs no app; replace it once the first real test passes.

## Done when

- [ ] Build: 0 warnings, 0 errors.
- [ ] The test run exits 0, and a `.trx` file exists under `TestResults/`.
- [ ] `check-conventions` reports no findings.
- [ ] After adapting: `BASE_URL` is set outside source (environment or launch settings), the first real page follows design-conventions §3, and the sample `Greeting*` files are gone.
