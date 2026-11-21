# Consumer CLAUDE.md (design)

## Intent

A project that uses the harness gets standing instructions that Claude Code reads even when the hooks
are silent. The main case is a fresh clone: `.harness/tools/bin` is gitignored, so until someone runs
`.harness/setup.ps1` the hooks fail open and the session-start reminders never show. A `CLAUDE.md` is
read anyway, so it can say "run setup first". This closes the "Planned but not built" item in
`docs/porting-notes.md` and master plan items 12 and 167.

## Goals, non-goals, anti-goals

- **Goals:**
  - The project's `CLAUDE.md` carries:
    - the setup-first note
    - the entry point (pom-architecture's Routing table)
    - branch prefixes (`automation/`, `harness/`, `feature/`, `fix/`)
    - no direct pushes to the default branch, and commit, push or PR only when the user asks
    - the test policy
    - the source-of-truth order: code, then runtime evidence, then docs, then AI output
  - Updates refresh the harness part without touching the team's own text.
- **Non-goals:** a full copy of the routing table, framework-specific text, or a `CLAUDE.md` for this
  repo (it has its own).
- **Anti-goals:**
  - overwriting or rewriting a team's existing `CLAUDE.md`
  - a second routing table that drifts from pom-architecture
  - blocking `scaffold` in a project that already has a `CLAUDE.md`

## Approaches

1. **Harness file plus one import line (chosen by the user).** The harness text lives in a tracked
   payload file, and `install` puts one `@` import line into the project's `CLAUDE.md`. Updates flow
   through the manifest, and the team's text is never edited.
2. Ship `.claude/CLAUDE.md` as payload. Simpler, but it would also load when working on this repo, and
   it conflicts with a team's own `.claude/CLAUDE.md`.
3. A scaffold template. It is written once and never updated, it blocks `scaffold` when the file
   exists, and it skips projects that install without scaffolding. Dropped.

## Design

- **Source:** `templates/CLAUDE.harness.md`. Like `templates/settings.harness.json`, it is not a scaffold
  layer. Setup already copies `templates/` to `.harness/templates/` and tracks it in the manifest, so no
  copy logic changes. The text is static, with no placeholders: the framework and name live in
  `.harness/project.json`.
- **Import line:** `@.harness/templates/CLAUDE.harness.md`. `install` (C#, test-first) makes sure the
  project's root `CLAUDE.md` contains it:
  - No file: create one with a short heading and the line.
  - File without the line: append a blank line and the line, keeping the existing encoding and line
    endings.
  - Line already there: no change.
  - The install output says which of the three happened.
- **Content** (about 40 lines): it points to pom-architecture rather than copying the table. A test
  checks that it names `pom-architecture` and `.harness/setup.ps1`.
- **Spike first** ("facts come from spikes"): confirm in one `claude -p` session in a scratch project
  that Claude Code follows a relative `@` import from the root `CLAUDE.md` into `.harness/`, with no
  approval prompt that would block a headless run. Record the result in `docs/framework-matrix.md`. If
  imports don't work, fall back to a marked block (`<!-- harness:begin -->` … `<!-- harness:end -->`)
  that `install` rewrites. That is a design change, so it comes back to the user.
- **Roll back:** harness-setup §5 says to delete the import line.
- **Docs:**
  - porting-notes: remove the item
  - README: one line
  - harness-setup playbook §1/§2: what `install` does to `CLAUDE.md`
  - framework-matrix: the spike
- **Tests:**
  - `Installer` tests: create, append, idempotent re-run, CRLF and BOM kept
  - a content test on the template
  - `Test-Setup.ps1`: after setup, `CLAUDE.md` has the line exactly once, also after `update.ps1`

## Left to the builder

The exact wording of the template, the heading of a newly created `CLAUDE.md`, and the test names.
