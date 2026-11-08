# Dotnet Playwright Harness

A [Claude Code](https://claude.com/claude-code) harness for building Playwright + C#/.NET test frameworks. It supports NUnit, xUnit v3 and MSTest v4.

> **Work in progress.** The skills, agents, hooks and templates are still changing. Commands, package versions and file layouts may change without notice, and there is no stable release yet.

## What it contains

- **Skills and agents** (`.claude/`): 32 skills and 9 agents. They cover conventions for page, Api and test classes, locators, test data and assertions, a QA lifecycle (requirements, test cases, test plans, bug reports, traceability), automating and maintaining tests, reviews, reporting and CI.
- **`Harness.Tools`** (`tools/Harness.Tools/`): a C# console app with the Claude Code hooks and the commands below.
- **Templates** (`templates/`): rendered into a walking skeleton for the chosen test framework, and optionally into GitHub Actions workflows.
- **Docs** (`docs/`): see [Documentation](#documentation).

| Command | Does |
|---|---|
| `scaffold`, `scaffold-ci` | Render the walking skeleton, and the GitHub Actions workflows |
| `check-conventions`, `validate-skills` | Check the C# code, and the skills and agents |
| `traceability`, `tracker` | Check the links from requirement to test to bug, and report them as a page |
| `ci-summary`, `dashboard` | Turn TRX results into a Markdown summary and an HTML page |
| `secret-scan`, `package-report` | Fail on secrets in reports, and on vulnerable or outdated packages |
| `scratch-copy` | Copy a project to a throwaway folder, for example to break a test once |

This repository is not a test project. `setup.ps1` copies the harness into your project.

## Quick start

You need the .NET SDK 10 and PowerShell (Windows PowerShell 5.1 or PowerShell 7).

```powershell
scripts/setup.ps1 -Target C:\src\shop-tests -Framework nunit -Name Acme.Shop
```

The script copies the payload, builds the tool, and merges the harness hooks into the project's `.claude/settings.json`. Use `scripts/update.ps1` to refresh the harness later. Files you changed locally are never overwritten.

Then start Claude Code in that project and ask for what you want, for example "create the framework for https://your-app.example". After cloning a project that already has the harness, run `.harness/setup.ps1`. Until then the hooks guard nothing.

## What has been verified

The repository's CI builds the tool and runs its tests on Ubuntu and Windows, checks the setup scripts on both PowerShell editions, and generates and runs a skeleton for each framework on Ubuntu. Only Chromium is verified. Allure on xUnit and MSTest uses preview packages.

The workflows that `scaffold-ci` generates have been run command by command on Windows, but not yet on GitHub Actions. A full pilot on a real application has not been done yet.

## Documentation

- [Routing guide](docs/routing-guide.md): what to ask for, and which skill and agents handle it.
- [Framework matrix](docs/framework-matrix.md): the versions, commands and behaviour verified by spikes.
- [External tools](docs/external-tools.md) and [MCP servers](docs/mcp.md): what was vetted, what was adopted, and the safety rules.
- [Porting notes](docs/porting-notes.md): what was left out of the source harnesses, and why.

## Contributing

Read [CLAUDE.md](CLAUDE.md) for the layout, commands and rules.

## License

[MIT](LICENSE). Parts are adapted from other MIT projects; see [NOTICE.md](NOTICE.md).
