---
name: csharp-standards
description: Use when changing build settings, analyzers, nullable or warning configuration, .editorconfig, global.json, NuGet packages or their versions in a Playwright C# test framework built with this harness, when a build fails on a warning or analyzer rule, or when checking packages for updates or vulnerabilities.
---

# C# build standards

The build is strict on purpose: warnings are errors, analyzers run in `Recommended` mode and
code style is enforced in the build. A warning you see is a defect to fix, not noise to silence.

**Read [the playbook](references/playbook.md) before changing a build file or a package.**
Code conventions (layers, naming, async discipline) are in the pom-architecture skill's
`design-conventions.md`; this skill does not repeat them.

## The files

| File | Owns | Change it when |
|---|---|---|
| `Directory.Build.props` | Target framework, nullable, warnings as errors, analyzers, CPM switch | Almost never; a change affects every project |
| `Directory.Packages.props` | The approved package list and every version | A package is approved and added, or a version is updated |
| `global.json` | SDK version with roll-forward, and the test runner (`Microsoft.Testing.Platform`) | The SDK band changes |
| `.editorconfig` | Formatting and naming rules, as build warnings | A team style decision is made |
| `*.csproj` | Package references **without versions**, project references, `OutputType=Exe` for tests | A project needs an approved package |

## Hard rules

- **No new package without the user's approval.** The approved list is `Directory.Packages.props`. Ask first, then add it there with a version, and reference it from the csproj without one.
- **Versions live only in `Directory.Packages.props`.** A `Version=` on a `PackageReference` breaks central package management.
- **Fix warnings; don't suppress them.** Never lower `TreatWarningsAsErrors`, `AnalysisMode` or an `.editorconfig` severity to get a build through. A suppression is a last resort with a written reason (playbook §3), and reviewers list every one.
- **No runsettings.** They are ignored under Microsoft Testing Platform. The browser comes from `BROWSER` and `HEADED`, and context options are set in `UiTest`.

## Before you finish

`dotnet build` passes with zero warnings, and `dotnet .harness/tools/bin/Harness.Tools.dll check-conventions` is clean.
