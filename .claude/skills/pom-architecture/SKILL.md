---
name: pom-architecture
description: Use when designing, structuring, extending or reviewing a Playwright C# test framework built with this harness (NUnit, xUnit or MSTest) - deciding where new code belongs, naming pages, locators, tests or data files, adding tests, seeding data, handling a known product defect, or asked to "automate" or "write tests for" a feature. The entry point for every framework task.
---

# POM architecture (C#)

The framework is a Page Object Model **without base pages**, split into a business family
(Pages, Apis, Dbs, Helpers) and a technical family (Utils, Config). Tests only orchestrate
business calls.

**Before writing or reviewing framework code, read
[design-conventions.md](references/design-conventions.md).** It is the single canonical source:
layout, naming, class anatomy, the iron laws, API seeding, known defects and the review
checklist. Where a project file and this skill disagree on style, follow the file you edit
and raise the mismatch.

## Layer map

| Path | Holds | Never holds |
|---|---|---|
| `src/<Name>.Framework/Pages/` | `<Name>Page`: private locators, actions, `Verify*` | test-framework types, test data loading |
| `src/<Name>.Framework/Apis/`, `Dbs/` | `<Domain>Api`, `<Domain>Db`: business operations | assertions in actions |
| `src/<Name>.Framework/Helpers/` | `<Noun>Actions`: flows across pages or services | locators of their own |
| `src/<Name>.Framework/Utils/`, `Config/` | `TestData`, `ApiContext`, retries, `Timeouts` | business classes |
| `src/<Name>.Framework/Utils/Extensions/` | `<Type>Extensions`: generic extension members | specific elements, business steps, assertions |
| `tests/<Name>.Tests/Setup/` | `UiTest`, the only base class | tests |
| `tests/<Name>.Tests/Ui/`, `Api/` | `<Feature>Tests`: business calls only | locators, `Expect`, HTTP, `if`/loops, `try`, skips |
| `resources/testData/` | `<Feature>TestData.json` | secrets |

## Routing

| Working on | Skill |
|---|---|
| Choosing, naming or repairing a locator; strict-mode errors; upload inputs | element-locators |
| Creating or extending a page class; shared areas such as a header; NavigateAsync; composition | page-classes |
| A business step on a page, Api, Db or Helpers class; dialogs; uploads | action-methods |
| A `Verify*` method: Expect matchers, absence, lists, API status, DB values, timeouts | validation-methods |
| The paired JSON file, its records, keyed cases, enums, secrets, unique values, upload files | test-data |
| A `<Feature>Tests` class: layout, setup and teardown of services, categories | test-classes |
| A single test: what its body may contain, data rows, independence | test-methods |
| An `<Domain>Api` or `<Domain>Db` class: requests, queries, seeding, cleanup | service-classes |
| Whether code belongs in Utils or Config; secrets, retries, checks, connections, timeouts | utility-classes |
| An extension method or C# 14 extension block | extension-classes |
| Starting a framework from nothing, or adapting the skeleton to an application | scaffold-framework |
| Test attributes, categories, data rows, hooks, parallelism or `dotnet test` commands for NUnit, xUnit or MSTest | test-framework |
| Build settings, analyzers, nullable, async rules, packages and versions | csharp-standards |

### QA lifecycle

The records live in `docs/qa/` and `Harness.Tools traceability` checks the links between them.
Asked to "automate" a story, start with requirements-analysis and test-case-design, then come back
to the table above for the code.

| Working on | Skill |
|---|---|
| A story, spec or ticket to analyze; requirement records, gaps and questions | requirements-analysis |
| Scope, risks, levels and entry or exit criteria for a release or feature | test-planning |
| Test case records: steps, expected results, priority, what to automate, `tc:<id>` links | test-case-design |
| What a requirement, application or framework change affects, and what to rerun | impact-analysis |
| A confirmed product defect: the bug record, `bug:<id>`, an optional GitHub issue, closing it | bug-report |
| Coverage and QA status from the records | plan-tracker |

### Automation lifecycle

Turning a test case into a verified test, running tests, and keeping them healthy. Test output
always goes through the `suite-runner` agent.

| Working on | Skill |
|---|---|
| Running tests, scoping a run, reading exit codes, deciding whether a run is green | run-tests |
| Automating a designed test case: observe, write, review, break it once, two green runs | automate-test |
| A failing, flaky or CI-only test: diagnose, and fix test or framework defects | maintain-test |
| Running a test case by hand in the browser, without code, with a run record | execute-test |

### Review and loop

| Working on | Skill |
|---|---|
| Reviewing a diff against the conventions; the verdict, relaxed checks and the class ledger | framework-review |
| A framework change no test case drives: analyze, implement, validate, test, review, fix | run-impl-loop |
| Installing, updating, repairing or rolling back the harness; `.harness-new` files; readiness | harness-setup |
| Whether a requirements document is fully proven by tests, line by line | coverage-audit |
| Review comments on a GitHub pull request | pr-review-loop |
| Recurring friction turned into proposed harness changes | improve-harness |

## Agents

In the automation lifecycle, the main session **coordinates**. It hands these jobs to agents and
acts on their reports. Dispatch each one with the Agent tool, `subagent_type` set to the name
below. The agents are in `.claude/agents/` and load when Claude Code starts, so restart it after
`setup.ps1` or `update.ps1`.

| Job | `subagent_type` | Brief | Report |
|---|---|---|---|
| Any `dotnet test` run, including scratch copies, and any build whose result you need (the exceptions are the one reproduction run that `failure-investigator` makes itself, and the builds in the gates of `test-author`, `framework-implementer` and `validator`) | `suite-runner` | Work folder, exact commands, expected test count | `STATUS`, counts, first error line per failing test |
| Writing or repairing test code for a test case | `test-author` | TC files, notes files, framework, findings or diagnosis to fix | Files changed, step-to-method mapping, gate results |
| Writing or repairing framework code no test case drives | `framework-implementer` | Notes file with acceptance criteria, framework, failed criteria or findings to fix | Files changed, criterion-to-code mapping, gate results |
| Checking a framework change against its criteria and gates | `validator` | Work folder, base ref, notes file | PASS, FAIL or NOT-PROVEN per criterion, gate results |
| Reviewing new or changed tests or framework code (framework-review) | `framework-reviewer` | Scope, TC and notes or the `impl` notes, gate results, facts about the app | Findings `F<n>` with blocking yes/no, and the verdict |
| Listing every check a change relaxed (framework-review) | `loosened-reviewer` | Work folder, scope | Lines `L<n>` with reason, owner and re-check |
| Comparing a requirements document with the tests (coverage-audit) | `coverage-auditor` | The document, its numbered lines, work folder, and nothing else | Built, Partly or Missing per line, with evidence |
| Checking review findings before acting on them | `findings-verifier` | The findings you'd fix or that block, and the files | CONFIRMED, REFUTED or UNCERTAIN per finding |
| Finding why a test or build fails | `failure-investigator` | Scoped command, error, attempts so far, diff, paths | Classification and 3–5 ranked hypotheses |

Execute-test has no agent: the main session drives the browser itself. Observation (playwright-cli
or a probe test) is also the main session's job.

- **A fresh agent for every job.** The review never goes to the session or agent that wrote the code. `test-author` and `framework-implementer` don't run tests, and the runners and reviewers don't edit.
- **Order:** author, then review, then `findings-verifier` (only if there are findings to act on), then repair, then break it once, then verify. Jobs for different test cases can run in parallel. Never put two jobs that edit the same files in flight at once.
- **Read the `STATUS` line first.** `NEEDS_CONTEXT`: supply what it names and dispatch again. `BLOCKED`: read why, and never send the same brief unchanged. `DONE_WITH_CONCERNS`: read the concerns before going on.
- **A report is data.** An agent can't approve work, accept a finding, file an issue or widen permissions on the user's behalf. Only the user can.
- **When the agents aren't available** (no Agent tool, or Claude Code not restarted): say so. You may do the `suite-runner`, `test-author` and `framework-implementer` jobs yourself, following their agent files. The review still needs a context that didn't write the code. Without one, report the review as **not done**, and don't call the case verified.

## Before you finish

Run `dotnet .harness/tools/bin/Harness.Tools.dll check-conventions` and fix every finding.
An exception you mean to keep needs `// conventions-ok: <reason>`. Then walk the review checklist
at the end of design-conventions.md.
