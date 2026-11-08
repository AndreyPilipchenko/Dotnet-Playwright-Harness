# Harness setup: playbook

The harness is copied into a project and committed there: tools under `.harness/tools/`, templates,
and the Claude Code payload under `.claude/`. The tool's build output and `.harness/state/` are
gitignored. Both scripts work on Windows PowerShell 5.1 and PowerShell 7, and need the .NET 10 SDK.

## 1. Install

From a clone of the harness repository:

```powershell
scripts/setup.ps1 -Target C:\src\shop-tests -Framework nunit -Name Acme.Shop
```

| Parameter | Meaning |
|---|---|
| `-Target` | The project root. Default: the current directory |
| `-Framework` | `nunit`, `xunit` or `mstest`. Needed on the first install; later runs use `.harness/project.json` |
| `-Name` | The project name used by scaffold. Default: from `project.json`, or the folder name |
| `-TargetFramework` | Default: from `project.json`, or `net10.0` |

Confirm the framework and the name with the user first: changing the framework later means porting
every test.

The script copies the payload, builds the tool, writes `.harness/project.json` and merges the harness
hooks into `.claude/settings.json`. Your own settings entries are kept.

**After cloning a project** where the harness is already committed, run `.harness/setup.ps1`. It only
builds the tool and re-merges the hooks. Until it runs, the hooks fail open and guard nothing.

Then restart Claude Code, and go to scaffold-framework if there is no framework yet.

## 2. Update

```powershell
.harness/update.ps1 -Source C:\src\dotnet-playwright-harness
```

Or `scripts/update.ps1 -Target <project>` from the harness repository.

| A payload file is | What the script does |
|---|---|
| Unchanged since the last install | Replaces it with the new version |
| Changed locally, and the harness changed it too | Keeps yours, writes the new one next to it as `<file>.harness-new`, and lists a conflict |
| No longer shipped, unchanged | Removes it |
| No longer shipped, changed locally | Keeps it, with a warning |

**Conflicts.** For each `.harness-new` file, compare it with your file and show the user the
differences. Keep what they decide, then delete the `.harness-new` file. Your merged file still counts
as changed locally, so a later update that touches it reports a conflict again.

## 3. Configure

- **Application URL:** tests read the `BASE_URL` environment variable (`Setup/UiTest.cs`). Ask for the test environment's URL. Never assume that production is acceptable.
- **Secrets:** the framework reads them with `Secrets.Get("<NAME>")`, from the environment, and fails loudly when one is missing. Give the user the names to set. The harness denies reading `.env` files.
- **Browser:** `BROWSER` (`chromium`, `firefox` or `webkit`) and `HEADED=1`. Install it once after a build: `tests/<Name>.Tests/bin/Debug/<tfm>/playwright.ps1 install chromium`.
- **Page observation:** automate-test and execute-test copy `cli.config.json` with the app's origin when they need it. Nothing to do here.

## 4. Check readiness

Report each item as ready, missing (with what to do), or not configured. A missing optional tool is
not a failure.

| Item | Check | Needed for |
|---|---|---|
| .NET SDK 10 or later | `dotnet --version` | everything |
| The tool is built | `.harness/tools/bin/Harness.Tools.dll` exists | hooks, checks. If not: `.harness/setup.ps1` |
| Project settings | `.harness/project.json` exists | scaffold, skills |
| Hooks are merged | `.claude/settings.json` mentions `Harness.Tools.dll` | safety rules |
| Framework exists | `src/` and `tests/` exist | everything after scaffold |
| `BASE_URL` | `$env:BASE_URL` is set | tests against an application |
| GitHub CLI (optional) | `gh auth status` | CI and pull-request skills |
| Node and npx (optional) | `npx --yes @playwright/cli@0.1.23 --version` | execute-test, page observation |

Check whether a secret is set without printing it: `[string]::IsNullOrEmpty($env:NAME)`.

## 5. Roll back

There is no uninstall command, because the install is ordinary files in git.

1. If the install or update is committed: `git revert <that commit>`. The user decides and runs it.
2. If it isn't committed: show `git status --short`. Undoing it discards work, so confirm with the user which files to restore.
3. Restart Claude Code.
