---
name: plan-tracker
description: Use when asked for test coverage, QA status, progress or a traceability report - how many requirements have test cases, how many cases are automated, which accepted requirements or high-priority cases have no coverage, which bugs are open, or a status summary for a release or stand-up.
---

# Plan tracker

Report QA status from the records. The committed Markdown records are the source of truth; the
status is derived from them every time, never kept in a separate file.

## 1. Read the chain

```powershell
$qa = dotnet .harness/tools/bin/Harness.Tools.dll traceability --json | Out-String | ConvertFrom-Json
```

`Out-String` joins the lines first, so this works on Windows PowerShell 5.1 as well as pwsh 7.

| Key | Holds |
|---|---|
| `requirements[]` | `id`, `title`, `status`, `file`, `cases[]` |
| `cases[]` | `id`, `title`, `priority`, `automation`, `requirements[]`, `file`, `tests[]` (`file`, `line`, `method`) |
| `bugs[]` | `id`, `title`, `status`, `severity`, `issue`, `cases[]`, `file`, `tests[]` |
| `fail[]`, `warn[]` | findings: `rule`, `file`, `line`, `why`, `detail` |

Exit 2 means there are no records under `docs/qa/` yet: say so and point to requirements-analysis.
If `fail[]` is not empty, report those first: the numbers below can't be trusted until the links
are fixed.

## 2. Summarise

Count from the JSON; don't estimate.

| Measure | From |
|---|---|
| Requirements by status | `requirements[].status` |
| Accepted requirements with no case | `status == accepted` and `cases` empty |
| Cases by automation | `cases[].automation` |
| High-priority cases not automated | `priority == high` and `automation == planned` |
| Open bugs by severity | `bugs[]` with `status == open` |

Then list the gaps by name, highest priority first, each with the skill that closes it:
test-case-design for a requirement without cases, pom-architecture for a case to automate,
bug-report for a bug to close.

## 3. Scope it

When the user names a release or plan, read `docs/qa/test-plans/<name>.md` and report only its
requirements, and its exit criteria as met or not met.

## 4. As a page

For something to share, `dotnet .harness/tools/bin/Harness.Tools.dll tracker` writes the same status
as HTML under `TestResults/tracker/`. Add `--results TestResults/<label>` for each case's last result.
See reporting.

## Boundaries

- This skill reads; it changes no records. Fixing a finding goes to the skill named in the gap list.
- Test run results (pass, fail, known failures) come from the TRX files, not from the records.
