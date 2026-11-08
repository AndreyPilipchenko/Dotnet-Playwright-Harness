# Dotnet Playwright Harness

A [Claude Code](https://claude.com/claude-code) harness for building Playwright + C#/.NET test frameworks. It supports NUnit, xUnit v3 and MSTest v4.

> **Work in progress.** The skills, agents, hooks and templates are still changing. Commands, package versions and file layouts may change without notice, and there is no stable release yet.

## What it contains

- **Skills and agents** (`.claude/`): conventions for page, Api and test classes, locators, test data and assertions, plus a QA lifecycle (requirements, test cases, test plans, bug reports, traceability).
- **`Harness.Tools`** (`tools/Harness.Tools/`): a C# console app with the Claude Code hooks, `scaffold`, `check-conventions`, `install` and the traceability check.
- **Templates** (`templates/`): rendered by `scaffold` into a walking skeleton for the chosen test framework.
- **Docs** (`docs/`): `framework-matrix.md` records the versions and commands verified by spikes. `external-tools.md` records which external tools were vetted.

This repository is not a test project. `setup.ps1` copies the harness into your project.

## Quick start

You need the .NET SDK 10 and PowerShell (Windows PowerShell 5.1 or PowerShell 7).

```powershell
scripts/setup.ps1 -Target C:\src\shop-tests -Framework nunit -Name Acme.Shop
```

The script copies the payload, builds the tool, and merges the harness hooks into the project's `.claude/settings.json`. Use `scripts/update.ps1` to refresh the harness later. Files you changed locally are never overwritten.

## Contributing

Read [CLAUDE.md](CLAUDE.md) for the layout, commands and rules.

## License

[MIT](LICENSE). Parts are adapted from other MIT projects; see [NOTICE.md](NOTICE.md).
