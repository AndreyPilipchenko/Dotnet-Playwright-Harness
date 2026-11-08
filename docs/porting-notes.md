# Porting notes

This harness is a C#/.NET rewrite of ideas from TypeScript and generalist harnesses. These notes say what was changed, what was left out and why. Licenses and sources are in [NOTICE.md](../NOTICE.md).

## What changed form

- **Conventions:** `design-conventions.md` section 9 maps each TypeScript form to its C# form. Those differences are settled.
- **Scripts:** every hook and report script is now a command of the C# tool `Harness.Tools`. Only PowerShell and .NET are needed to run the harness.
- **Trackers:** Azure DevOps steps became Markdown records under `docs/qa/`, with optional GitHub issues through `gh`.
- **State checks:** the source's `traceability-*` and `verify-state-*` checks became one `traceability` command.
- **Merged skills:** `debug` went into `maintain-test` and `failure-investigator`, because they do the same job. `evaluate` went into `framework-review` for the same reason.

## Left out

| Source | Left out | Why |
|---|---|---|
| claude-code-harness | `implement`, `plan-features` | Very large, and they overlap with `automate-test`, `run-impl-loop` and our plan format |
| claude-code-harness | Sprint and story machinery, worktree and wave rules | They manage product delivery, not a test framework |
| claude-code-harness | PRD, architecture, decision, frontend design and deploy skills | They are about building products |
| claude-code-harness | Azure DevOps and Todoist trackers, Azure safety rules | The harness uses local records and GitHub |
| playwright-pom-harness | TypeScript test generation, `playwright.config.ts`, `@smoke` tags, fixtures | Replaced by the C# forms, and runsettings are ignored under Microsoft Testing Platform |
| playwright-pom-harness | An Allure step on every method | Allure packages differ per framework and two are previews. TRX and traces are the baseline, and Allure stays optional |
| sample framework | Its test plan, test cases, known defects and recorded API traffic | They are products of using a harness, not part of one, and are about one application |
| Playwright | Planner, generator and healer agents | Built on `@playwright/test`, with no C# form. `test-planning`, `automate-test` and `maintain-test` cover the same ideas |
| cde-dotnetcc | All text and code | It has no license, so only ideas were used |

## Rejected tools

External tools were tested before any decision. The evidence is in [external-tools.md](external-tools.md), and MCP servers are covered in [mcp.md](mcp.md).

- **`dotnet-test` plugin:** the idea only. Its `run-tests` skill would compete with ours, and its agents are written for GitHub Copilot.
- **NuGet MCP:** a 35,000-character schema in every session, and a solver error when tried.
- **GitHub MCP:** `gh` does the same with no context cost, and hooks can see it.

## Planned but not built

The original plan listed these, and they don't exist in the harness today:

- Path-scoped rules under `.claude/rules/`. The conventions live in skills and in `check-conventions`.
- A `CLAUDE.md` template for consumer projects, with a routing table, branch names and the source-of-truth order. The `session-start` hook gives reminders, but a project gets no `CLAUDE.md` from the harness.
- Guidance on signing in once per role and reusing the session, and a browser diagnostics helper. Traces of failed tests cover part of the second.
- Saved Claude Code workflows. The tool wasn't available when vetted, so the main session drives the loops.
- Firefox and WebKit in CI, because they aren't verified.
