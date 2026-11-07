# Notices

The [MIT license](LICENSE) covers this repository. Parts of it are adapted from the projects below, which keep their own copyright. Where a file is adapted from one of them, this notice applies to that file.

## Playwright POM Harness

- Source: [`MahmoudElSharkawy/Playwright-Harness`](https://github.com/MahmoudElSharkawy/Playwright-Harness), package `playwright-pom-harness` 4.3.3, commit `651462a`.
- License: MIT, copyright (c) 2026 Playwright POM Harness contributors.
- Used for: the framework-convention and QA-process skills, the playbooks, the hook behaviour and the conventions checker rules. All of these are rewritten for C# and .NET.

## claude-code-harness

- Source: [`anudeeps28/claude-code-harness`](https://github.com/anudeeps28/claude-code-harness) 3.2.0, commit `4ba77a3`.
- License: MIT, copyright (c) 2026 Anudeep Sharma.
- Used for: the generalist process skills and agents (suite runner, coverage audit, loosened-test review, PR review loop, harness improvement) and the safety-check test cases that specify `hook pre-shell` and `hook pre-write`. Hooks are rewritten in C#.

## sample-playwright-framework

- Source: the author's own TypeScript project, under MIT, copyright (c) 2026 Andrey Pilipchenko.
- Used for: design references, reporting and CI scripts (rewritten in C#), and generalized lessons.
- Not used: its `docs/test-plan.md`, `docs/test-cases.md`, `docs/known-defects.md` and `docs/README.md`, which its own `NOTICE.md` excludes from MIT. No content about the application it tests is included here.

## cde-dotnetcc

- Source: `atherio-danp/cde-dotnetcc`, which has **no license**.
- Used for: ideas only (the implementation loop, the approved-package rule, the destructive-command gate, the source-of-truth order). Nothing is copied from it; everything here is written independently.

## Microsoft Playwright for .NET and dotnet/skills

- [`microsoft/playwright-dotnet`](https://github.com/microsoft/playwright-dotnet) (MIT) and [`dotnet/skills`](https://github.com/dotnet/skills) (MIT, copyright (c) .NET Foundation and Contributors).
- Used for: API facts and test-platform command facts, each checked by our own spikes (see `docs/framework-matrix.md`). No files are copied.

## Dependencies

NuGet and npm packages used by the harness or by generated projects keep their own licenses.
