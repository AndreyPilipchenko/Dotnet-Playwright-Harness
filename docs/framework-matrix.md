# Framework matrix

These facts are **verified by spike** on 2026-11-07 with these versions:

- Windows 11
- .NET SDK 10.0.403
- Microsoft.Playwright 1.64.1
- Chromium headless shell 156.0.8078.4

Each spike ran two tests against `Page.SetContentAsync`, one passing and one failing on purpose. Each spike used a web-first `Expect` assertion, a category or trait filter, and TRX output. Anything not listed here has not been verified.

Not verified yet:

- Linux
- SDK 8 and 9
- Firefox and WebKit
- parallel execution at scale

## Packages (pinned in `templates/build/Directory.Packages.props`)

| | NUnit | xUnit | MSTest |
|---|---|---|---|
| Runner | `NUnit` 5.0.0 + `NUnit3TestAdapter` 6.3.1 + `NUnit.Analyzers` 4.15.0 | `xunit.v3` 4.0.2 (+ `xunit.runner.visualstudio` 4.0.1 for IDE discovery) | `MSTest` 4.5.2 |
| Playwright adapter | `Microsoft.Playwright.NUnit` 1.64.1 | `Microsoft.Playwright.Xunit.v3` 1.64.1 | `Microsoft.Playwright.MSTest.v4` 1.64.1 |
| Shared | `Microsoft.NET.Test.Sdk` 18.11.0, `Microsoft.Testing.Extensions.TrxReport` 2.5.2 | same | same |
| Allure | `Allure.NUnit` 2.15.0 (stable). Needs `[AllureNUnit]` on the class | `Allure.Xunit.v3` 3.0.0-preview.1, **experimental** | `Allure.TestingPlatform` 3.0.0-preview.1, **experimental** |

The Playwright adapters accept open-ended versions of the test frameworks (for example `NUnit >= 3.13.2`), so newer major versions of a framework must be re-checked by spike.

## Test platform: native Microsoft Testing Platform (MTP) for all three

The template default is **native MTP command mode**. It is selected in `global.json`:

```json
{ "test": { "runner": "Microsoft.Testing.Platform" } }
```

Each test project then sets `OutputType=Exe` and its framework's runner property:

| | NUnit | xUnit | MSTest |
|---|---|---|---|
| Runner property | `EnableNUnitRunner=true` | `UseMicrosoftTestingPlatformRunner=true`. Not needed to run tests, but Allure needs it; without it Allure only warns with `ALLURE001` and writes nothing | `EnableMSTestRunner=true` |

**Why not VSTest mode:** xunit.v3 4.x **cannot run in VSTest mode on SDK 10**. The build stops with this error from `Microsoft.Testing.Platform.MSBuild`:

> Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later.

NUnit and MSTest do still run in VSTest mode. Using native MTP for all three gives a single command shape.

## Commands (native MTP, SDK 10)

| Purpose | NUnit / MSTest | xUnit v3 |
|---|---|---|
| Run all + TRX | `dotnet test --project <csproj> --report-trx --results-directory TestResults` | same |
| Category filter | `--filter "TestCategory=smoke"` | `--filter-trait "Category=smoke"` |
| Skip build | `--no-build` | same |

Notes:

- `--report-trx` needs `Microsoft.Testing.Extensions.TrxReport`. Without it, xUnit only offers `--report-xunit-trx`, and `--report-trx` fails with exit code 5.
- A positional project path is a VSTest-mode form. In MTP mode, use `--project`.

## Output that `hook post-shell` and `ci-summary` must parse

| | Native MTP | VSTest (legacy projects) |
|---|---|---|
| Exit codes | 0 = pass, **2 = test failures**, **5 = invalid command line**, others are listed at aka.ms/testingplatform/exitcodes | 0 = pass, 1 = failures |
| Summary | Multi-line block: `Test run summary: Failed!` / `total: 2` / `failed: 1` / `succeeded: 1` / `skipped: 0` | One line: `Failed!  - Failed: 1, Passed: 1, Skipped: 0, Total: 2` |
| Failure detail | `failed with 1 error(s)` per module, then the Playwright message (`Locator expected to have text …`) with the aria snapshot | `Failed <Test> [2 s]` + `Error Message:` |

## Allure output location

| | Location | Template action |
|---|---|---|
| NUnit (2.15.0) | `bin/<cfg>/<tfm>/allure-results` | Set `directory` in `allureConfig.json` so results go to a stable path |
| xUnit / MSTest (3.0.0-preview.1) | `<results-directory>/allure-results` | None |

The xUnit preview reports the test name as the fully qualified method name (`SpikeXunit.LoginTests.ShowsGreetingAsync`).

## Browser and launch configuration under MTP

**The `.runsettings` `<Playwright>` section does not work under native MTP.** It was tested with `BrowserName=webkit`, where WebKit was not installed, so a working setting would make the tests fail:

| Framework | Result with `--settings probe.runsettings` |
|---|---|
| NUnit, MSTest | **The setting is ignored without any message.** Tests still ran on Chromium and passed |
| xUnit | `--settings` is rejected (exit code 5) |

**The `BROWSER` environment variable works on all three:** each failed with `Executable doesn't exist … webkit-2370`. That is why templates configure the browser this way:

- Use the `BROWSER` and `HEADED` environment variables, set by CI, the local run script or `launchSettings`.
- Set context and launch options in code, by overriding `ContextOptions()` in the single base class for each framework.
- Do not use runsettings.

Many online examples, Context7's included, show the VSTest form `dotnet test -- Playwright.BrowserName=…`. That form does not apply here, and skills must say so.

## Other verified facts

- **Browser install** works through `bin/<cfg>/<tfm>/playwright.ps1 install chromium` on **Windows PowerShell 5.1** as well as on pwsh 7. The script's `pwsh` shebang does not matter when it is called as `powershell.exe -File`. The C# fallback planned for this case is not needed.
- **xunit.v3** does not add a global `using Xunit;`. The template must add it, either in code or with `<Using Include="Xunit" />`.
