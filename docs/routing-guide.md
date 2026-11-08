# Routing guide

You don't need to name a skill. Say what you want in plain words, and Claude Code picks the skill from its description. `pom-architecture` is the entry point for framework work and routes to the rest. Naming a skill (`/automate-test`) also works.

Agents are started by the skills, not by you. They load when Claude Code starts, so restart it after `setup.ps1` or `update.ps1`.

## Getting started

| You say | Skill |
|---|---|
| "Install, update or repair the harness", "the hooks are missing" | `harness-setup` |
| "Create the framework", "scaffold NUnit for this app" | `scaffold-framework` |
| "Add CI", "generate the GitHub workflows" | `ci-pipeline` |

## From a requirement to a verified test

| You say | Skill | Agents it starts |
|---|---|---|
| "Analyze this story", "what's missing from this spec" | `requirements-analysis` | |
| "Plan the testing for this release" | `test-planning` | |
| "Write test cases for REQ-12" | `test-case-design` | |
| "Automate TC-7" | `automate-test` | `test-author`, `framework-reviewer`, `loosened-reviewer`, `findings-verifier`, `suite-runner`, `failure-investigator` |
| "Run TC-7 by hand in the browser" | `execute-test` | |
| "What does this change affect, what should I rerun" | `impact-analysis` | |
| "Is this document fully tested" | `coverage-audit` | `coverage-auditor` |
| "What's our coverage", "QA status for the release" | `plan-tracker` | |

## Running and keeping tests healthy

| You say | Skill | Agents it starts |
|---|---|---|
| "Run the smoke tests", "is this run green" | `run-tests` | `suite-runner` |
| "This test fails", "it's flaky", "it only fails in CI" | `maintain-test` | `failure-investigator`, `test-author`, `suite-runner` |
| "File a bug for this failure" | `bug-report` | |
| "Summarize the run", "make the results page", "check the reports for secrets" | `reporting` | |
| "What did last night's CI run do" | `ci-pipeline` | |

## Changing the framework

| You say | Skill | Agents it starts |
|---|---|---|
| "Add a retry helper", "refactor the config", any change no test case drives | `run-impl-loop` | `framework-implementer`, `validator`, `framework-reviewer`, `suite-runner` |
| "Review this diff" | `framework-review` | `framework-reviewer`, `loosened-reviewer`, `findings-verifier` |
| "Handle the PR comments" | `pr-review-loop` | |
| "What keeps going wrong", "improve the harness" | `improve-harness` | |

## Convention skills

These load while code is being written or reviewed. You rarely ask for them directly.

| Working on | Skill |
|---|---|
| Where code belongs, naming, the review checklist | `pom-architecture` |
| Page classes, shared areas, navigation | `page-classes` |
| Locators and strict-mode errors | `element-locators` |
| Business steps on a page, Api, Db or helper | `action-methods` |
| `Verify*` methods | `validation-methods` |
| Test classes | `test-classes` |
| A single test method | `test-methods` |
| JSON test data and secrets | `test-data` |
| `<Domain>Api` and `<Domain>Db` classes | `service-classes` |
| Utils and Config | `utility-classes` |
| Extension members | `extension-classes` |
| Attributes, filters and the `dotnet test` command per framework | `test-framework` |
| Build settings, analyzers and packages | `csharp-standards` |

## What the hooks do on their own

The hooks run without being asked. Until `setup.ps1` has built the tool, they do nothing and block nothing.

- Blocked: a push to the default branch, a write that looks like a hard-coded secret, catastrophic commands, and repeating a failed `dotnet test` with no edit in between.
- Asks first: destructive commands, and `gh` actions that change something, such as `gh workflow run` or `gh pr merge`.
- Warns: convention findings in an edited `.cs` file.

Hooks don't see MCP calls, so see [mcp.md](mcp.md) before adding a server.
