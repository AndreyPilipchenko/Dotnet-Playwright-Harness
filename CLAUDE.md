# Working on the harness

This repo is a Claude Code harness for building Playwright + C#/.NET test frameworks (NUnit, xUnit or MSTest). It is **not** a test project itself: `setup.ps1` copies the harness into a consumer project, and `Harness.Tools scaffold` renders a walking skeleton there.

## Layout

- `tools/Harness.Tools/`: C# console app (net10.0): hooks, `scaffold`, `check-conventions`, `install`, and later reports. It is self-contained (`tools/Directory.*.props`), because its source is copied into consumer projects under `.harness/tools/`.
- `tools/Harness.Tools.Tests/`: xUnit v3 tests. They use the real `templates/` folder.
- `templates/`: rendered by `scaffold` in three layers: `build/` (repo root files), `framework/` (the framework-neutral `src/<Name>.Framework`), then `nunit/`, `xunit/` or `mstest/`. Placeholders are `__Name__` in paths, a `dot.` prefix for dotfiles, and `{{Name}}`, `{{TargetFramework}}`, `{{Framework}}` in contents. Unknown placeholders fail the scaffold.
- `templates/settings.harness.json`: the hook and deny block that `install` merges into a project's `.claude/settings.json`. It is not a scaffold layer.
- `scripts/`: `setup.ps1` and `update.ps1` copy the payload into a project (tracked by `.harness/manifest.json`, so local changes are never overwritten), build the tool and run `install`. `scripts/tests/Test-Setup.ps1` checks both scripts end to end.
- `docs/`: `framework-matrix.md` (versions and behaviour verified by spike) and `external-tools.md` (vetting record). Treat both as the source of truth for package versions and commands.

## Commands

```powershell
dotnet build Harness.slnx
dotnet test --project tools/Harness.Tools.Tests/Harness.Tools.Tests.csproj --report-trx --results-directory artifacts/test-results
```

Check the setup scripts on both editions:

```powershell
powershell -NoProfile -NonInteractive -File scripts/tests/Test-Setup.ps1
pwsh -NoProfile -NonInteractive -File scripts/tests/Test-Setup.ps1
```

End-to-end check of the templates: scaffold into a scratch folder (never into this repo), build, install Chromium with `bin/<cfg>/<tfm>/playwright.ps1 install chromium`, then run `dotnet test --project tests/<Name>.Tests/<Name>.Tests.csproj --report-trx`.

## Rules

- **Tooling language:** C#/.NET and PowerShell. Scripts must run on Windows PowerShell 5.1 and pwsh 7. Use `.js` only where a tool requires it.
- **Test-first** for `Harness.Tools`: write the failing test, then the code. Warnings are errors.
- **Hooks fail open:** any internal error, unknown mode or malformed input exits 0 with empty stdout. Only a deliberate block exits 2.
- **Facts come from spikes:** a version, command or flag goes into templates or skills only after it is verified and recorded in `docs/framework-matrix.md`. Online examples often show the VSTest form, which does not apply under MTP.
- **Licensing:** see `NOTICE.md`. Never copy from `cde-dotnetcc` (it has no license). Content adapted from MIT sources keeps its notice.
- **Git:** work on `harness/*` branches. Commit, push or open a PR only when the user asks.
