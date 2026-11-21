# Playwright C# harness

These instructions come from the harness installed under `.harness/`. The project's own `CLAUDE.md`
imports them. Harness updates refresh this file, so put project rules in `CLAUDE.md` itself, not here.

## Before anything else

If `.harness/tools/bin/Harness.Tools.dll` is missing, for example in a fresh clone, run
`.harness/setup.ps1` first. Until it has run, the hooks and checks fail open and guard nothing.

## Where to start

Start every framework task with the pom-architecture skill. Its Routing table names the skill to use
before any file changes: test cases, automation, failing tests, reviews, reports and setup.

## Git

- Work on a named branch: `automation/`, `harness/`, `feature/` or `fix/`.
- Never push to the default branch.
- Commit, push or open a pull request only when the user asks.

## Tests

- A test counts as green only after the scoped runs described in the run-tests skill. One passing run
  by hand is not enough.
- No skipped or ignored tests, and no retries that hide a failure.
- A test of a known product defect stays red and carries `bug:<id>` for an open BUG record (bug-report).
  Never change its expectation to match what the product does now.
- Before committing, run `dotnet .harness/tools/bin/Harness.Tools.dll check-conventions` and fix what
  it reports.

## Source of truth

When sources disagree, trust them in this order:

1. The code.
2. Runtime evidence: test runs, traces, the application itself.
3. Documentation: requirements, test cases, notes.
4. AI output, including earlier answers in this conversation.
