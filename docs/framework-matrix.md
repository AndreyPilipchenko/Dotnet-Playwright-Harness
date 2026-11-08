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

## Scoped runs (verified 2026-11-08)

Spiked on projects scaffolded from the templates, with a test class holding three tests: one with the categories `smoke` and `tc:TC-001`, one with `smoke` only, and one skipped (`[Ignore]` for NUnit and MSTest, `[Fact(Skip = …)]` for xUnit). Each run used `--no-build --report-trx --results-directory artifacts/test-results/<label>`.

| Purpose | NUnit / MSTest | xUnit v3 |
|---|---|---|
| One linked test case | `--filter "TestCategory=tc:TC-001"`: the colon needs no escaping; 1 test ran | `--filter-trait "Category=tc:TC-001"`: 1 test ran |
| One test by method name | `--filter "Name=UnlinkedPasses"`: 1 test ran | `--filter-method "Acme.Spike.Tests.Ui.SpikeTests.UnlinkedPasses"` (full name): 1 test ran |
| Tests by part of the full name | `--filter "FullyQualifiedName~SpikeTests.LinkedPasses"`: 1 test ran | `--filter-method "*LinkedPasses"` is a wildcard: it ran **2** tests, `LinkedPasses` and `UnlinkedPasses`. Use the full name |

What the runs showed:

- **No matching test:** exit code **8**, `Zero tests ran` on the module line and as the summary (`Test run summary: Zero tests ran`, `error: 1`, `total: 0`). Same on all three. `hook post-shell` does not count it as a failure. A skill must not count it as a pass either.
- **Skipped tests:** the run still ends `Test run summary: Passed!` with exit code **0**, and only the `skipped: 1` line shows the skip. A skipped test is visible only in that line.
- **Separate results folders:** two runs of the same project with different `--results-directory` values each wrote their own `<project>_<tfm>_<arch>.trx`. The pre-shell rerun block ignores `--results-directory`, `--report-trx`, `--no-build` and `--no-restore` when it compares commands, but counts `BROWSER` and `HEADED` (`TestRuns.Fingerprints`). A new folder doesn't make a failed run new. Two planned runs therefore go in one command joined with `;`. Only runs inside the project root count: the hook reads the payload's `cwd` and follows `cd`, `Set-Location` and `Push-Location` segments, so a run in a scratch copy outside the project (break it once) is neither recorded nor blocked. This is covered by unit tests; it has not been observed live, because the `cwd` payload field comes from the Claude Code hook documentation.
- `total` counts every test that ran, including the scaffold's own `GreetingTests` when the filter selects it.

## Test attributes and lifecycle (verified 2026-11-08)

Spiked on projects scaffolded from the templates, with warnings as errors, Chromium, native MTP. Each spike had a two-row data-driven test, an async per-test setup and teardown that creates and disposes an `IAPIRequestContext` from the adapter's `Playwright` property, and a class marked to run without parallelism. All four tests passed on each framework, and the category filter selected exactly the two data rows.

| | NUnit | xUnit v3 | MSTest v4 |
|---|---|---|---|
| Test class | plain class deriving from `UiTest` | same | `[TestClass]` needed on every test class |
| Test | `[Test]` | `[Fact]` | `[TestMethod]` |
| Data rows | `[TestCase(a, b)]` on the method, no `[Test]` needed | `[Theory]` + `[InlineData(a, b)]` | `[TestMethod]` + `[DataRow(a, b)]` |
| Category | `[Category("x")]` | `[Trait("Category", "x")]` | `[TestCategory("x")]` |
| Per-test async setup / teardown | `[SetUp]` / `[TearDown]` on `public async Task` methods | `public override async ValueTask InitializeAsync()` calling `base.InitializeAsync()` first; `DisposeAsync()` override calling `base.DisposeAsync()` last | `[TestInitialize]` / `[TestCleanup]` on `public async Task` methods |
| Run a class alone | `[NonParallelizable]` | `[Collection("Serial")]` + a `[CollectionDefinition("Serial", DisableParallelization = true)]` class | `[DoNotParallelize]` |
| Template default parallelism | `[assembly: Parallelizable(ParallelScope.Fixtures)]` | xUnit default (by class) | `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]` |

Analyzer errors seen under the strict build (xUnit only):

- `CA1816` on a `DisposeAsync()` override: add `GC.SuppressFinalize(this);`.
- `CA1711` on a collection definition class named `…Collection`: name it differently, for example `SerialGroup`.

Not verified: that the "run alone" markers really serialize execution (only compile and pass were checked), class-level setup hooks (`[OneTimeSetUp]`, `IClassFixture`, `[ClassInitialize]`), and source-method data (`TestCaseSource`, `MemberData`, `DynamicData`).

## Extension members (verified 2026-11-08)

Spiked on a scaffolded NUnit project (net10.0, `LangVersion=latest`, `AnalysisMode=Recommended`, warnings as errors), in `src/<Name>.Framework/Utils/Extensions/`:

| Form | Build | `check-conventions` | Runtime |
|---|---|---|---|
| Classic: `public static class XExtensions` with `this` methods (`EnsureOk`, `ReadIdAsync` on `IAPIResponse`) | 0 warnings | Parsed; Utils rules apply | not run (no server in the spike) |
| C# 14 block: `extension(ILocator locator) { … }` with a method and a property | 0 warnings | Parsed; Utils rules apply | Called from a page and a probe test: passed |

- The checker (Roslyn `Microsoft.CodeAnalysis.CSharp` 5.9.0, default parse options) sees members inside an `extension` block. A badly named `Task` method was reported as `async-naming` in both forms.
- `Utils/Extensions/` maps to the Utils layer with no change to `LayerMap`.
- Not verified: the C# 14 block form when `--target-framework` is not `net10.0`.

## Playwright and data patterns used by the specialist skills (verified 2026-11-08)

All passed on a scaffolded NUnit project (Chromium, warnings as errors):

| Pattern | Verified behaviour |
|---|---|
| Dialog handled for one action | A named handler `static void AcceptDialog(object? sender, IDialog dialog) => _ = dialog.AcceptAsync();` added with `Page.Dialog +=` and removed with `-=` after the click: the `confirm()` returned true. After removal, the next dialog was dismissed automatically (`confirm()` returned false) |
| Upload | `SetInputFilesAsync(path)` on a hidden `<input type="file">` (`display:none`) set the file. It worked through a CSS selector and also through `GetByLabel("Invoice")` with the input wrapped in its `<label>` |
| Binary fixtures | `<None Include="..\..\resources\files\**\*" LinkBase="files" CopyToOutputDirectory="PreserveNewest" />` in the test csproj copies them to `files/` next to the binaries; read with `Path.Combine(AppContext.BaseDirectory, "files", …)` |
| API response assertion | `Expect(response).ToBeOKAsync()` and `Expect(response).Not.ToBeOKAsync()` on an `IAPIResponse` (live GET to playwright.dev, 200 and 404) |
| Collection assertions | `Expect(rows).ToHaveTextAsync(["A-1", "A-2"])`; `Expect(GetByRole(AriaRole.Checkbox, new() { Checked = false })).ToHaveCountAsync(0)`; `Filter(new() { HasText = … })` plus `ToHaveCountAsync(1)` |
| Strict mode | Clicking `GetByRole(AriaRole.Button)` on a page with two buttons threw `PlaywrightException: Error: strict mode violation: GetByRole(AriaRole.Button) resolved to 2 elements:` |
| Keyed cases in test data | A record with `IReadOnlyDictionary<string, OrderCase> Orders`, loaded with the template's `TestData.Load<T>`, gave each case by key (`Data.Orders["express"]`) |
| Enum in test data | `[JsonConverter(typeof(JsonStringEnumConverter<T>))]` on the enum type: `"Express"` and `"express"` both deserialize with the template's `TestData` options. No change to `TestData.cs` is needed |

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
- **Package checks under CPM** (2026-11-08, SDK 10.0.403, on a scaffolded NUnit project, from the repo root): `dotnet package list --outdated` and `dotnet package list --vulnerable --include-transitive` both work. The older `dotnet list package …` form gives the same output. `--format json` gives machine-readable output.
  - When nothing is found, they print "has no updates given the current sources" or "has no vulnerable packages…" and exit 0. They also exit 0 when an update is found, so read the output; the exit code says nothing.
  - Pinning `NUnit.Analyzers` back to 4.10.0 was reported as `Requested 4.10.0, Latest 4.15.0`.
  - The "Not found at the sources" message seen during Phase 0 did not reproduce.
  - **JSON shape** (`--outdated --format json`): `projects[].frameworks[].topLevelPackages[]` with `id`, `requestedVersion`, `resolvedVersion`, `latestVersion`. A project with no findings has a `path` and no `frameworks` key.
  - **The build is the vulnerability gate.** With `TreatWarningsAsErrors`, restore-time NuGet audit fails the build on a vulnerable direct package. Pinning `System.Text.Json` 8.0.4 gave `error NU1903: Warning As Error: … known high severity vulnerability`. `package list --vulnerable` then fails too (exit 1), because restore fails.
  - Not verified: NuGet audit for transitive-only vulnerabilities, and the JSON shape of a `--vulnerable` finding.
  - Also seen: `NU1510` (a reference to a package the SDK already provides, here `System.Text.Json`) is an error under warnings as errors.
- **xunit.v3** does not add a global `using Xunit;`. The template must add it, either in code or with `<Using Include="Xunit" />`.

## Claude Code hooks (verified live, Claude Code 2.1.319, Windows 11, 2026-11-08)

The harness hooks in `templates/settings.harness.json` use the **exec form**: `"command": "dotnet"` with `"args": ["${CLAUDE_PROJECT_DIR}/.harness/tools/bin/Harness.Tools.dll", "hook", "<mode>"]`. No shell is involved, so the same entry works whether Claude Code would use Git Bash or PowerShell. `dotnet.exe` is a real executable, which exec form requires on Windows.

Checked in a scratch project installed with `setup.ps1` and run through `claude -p`:

| Hook | Evidence |
|---|---|
| `session-start` | The "Harness reminders:" text reached the model as context. This also shows that **`${CLAUDE_PROJECT_DIR}` is expanded inside `args`**, which the hooks docs do not state |
| `pre-shell` | `git push origin main` was blocked. Claude saw `BLOCKED by harness pre-shell (push-default-branch): …`, and `hook-events.jsonl` recorded the deny |
| `pre-write` | A write containing a `ghp_…` token was blocked (`github-token`) and the file was not created |
| `post-edit` | Writing a page that calls `Thread.Sleep` gave Claude the `no-hard-wait` finding as feedback ("hook blocking error" on PostToolUse); the file was kept |
| `session-end` | `sessions.jsonl` got one row per session, with deny and ask counts |

`post-shell` and `pre-compact` were not triggered live; their behaviour is covered by unit tests.

Other facts found on the way:

- In `--allowedTools` and permission rules, **file allow rules must be written as `Edit(path)`**. `Write(path)` is not matched by file permission checks, and Claude Code says so.
- A nested `claude -p` session starts in the user's default permission mode (here, plan mode), so live tests pass `--permission-mode default` with scoped `--allowedTools`. `bypassPermissions` is not needed.
- PostToolUse exit 2 shows stderr to Claude and does not undo the tool call. This matches the docs.

## Probe test and scratch copy (verified 2026-11-08)

Used by automate-test when playwright-cli isn't available. On an NUnit project scaffolded from the templates, with `BASE_URL=https://demo.playwright.dev`:

- `Harness.Tools scratch-copy --to <new folder>` copied the 127 tracked and untracked files. It did not copy `bin/`, `obj/` or `.harness/state/`. The copy built with 0 warnings.
- `scratch-copy` refuses (exit 64, nothing copied) a target that is the project root, inside it, inside the git repository that contains it (also when `--root` is a subfolder), or already exists.
- A probe test in the copy ran `Page.GotoAsync("/todomvc/")`, `await Page.Locator("body").AriaSnapshotAsync()` and `GetByRole(…).CountAsync()`, and wrote them to a file with `File.WriteAllTextAsync`. The run passed, and the file held the accessibility tree in the same YAML form as a playwright-cli snapshot, without refs.
