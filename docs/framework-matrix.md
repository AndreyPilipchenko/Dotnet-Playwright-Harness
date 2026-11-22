# Framework matrix

These facts are **verified by spike** on 2026-11-07 with these versions:

- Windows 11
- .NET SDK 10.0.403
- Microsoft.Playwright 1.64.1
- Chromium headless shell 156.0.8078.4

Each spike ran two tests against `Page.SetContentAsync`, one passing and one failing on purpose. Each spike used a web-first `Expect` assertion, a category or trait filter, and TRX output. Anything not listed here has not been verified.

Not verified yet:

- SDK 8 and 9
- parallel execution at scale

Firefox and WebKit, and the skeleton on Linux, were verified later. See "Firefox and WebKit" below and "GitHub Actions".

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

**TRX shape** (2026-11-08, spiked on projects scaffolded for each framework, each with a passing, a failing and a skipped test; `ci-summary` was run on the results):

- `UnitTestResult` has `testId`, `testName`, `duration` and `outcome`. A failure holds `Output/ErrorInfo/Message` and `StackTrace`. `testId` joins to `TestDefinitions/UnitTest/@id`, which holds `TestCategory/TestCategoryItem/@TestCategory` and `TestMethod/@className`.
- A skip is `outcome="NotExecuted"` on all three, and `ResultSummary/Counters` counts it under `notExecuted`. NUnit and MSTest put the reason in `Message`. xUnit v3 leaves it out.
- `testName` is the short method name on NUnit and MSTest, and the full name on xUnit v3. The class comes from `className`.
- `TestCategoryItem` holds `bug:<id>` and `tc:<id>` on all three, xUnit `Trait("Category", …)` included.
- A Playwright failure's `Message` continues with `Call log:` and `Data:` blocks after the first line.
- The escape character can't be stored in XML, so a message holds it as the six characters `\u001b` (seen on NUnit; the TRX writer is the platform's, not the framework's).
- `ResultSummary/@outcome` is `Failed` for a failed run, and a `RunInfo` with `outcome="Error"` carries the exit-code text.

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
- **Separate results folders:** two runs of the same project with different `--results-directory` values each wrote their own `<project>_<tfm>_<arch>.trx`. The pre-shell rerun block ignores `--results-directory`, `--report-trx`, `--no-build` and `--no-restore` when it compares commands, but counts `BROWSER`, `HEADED`, `BASE_URL` and `API_URL` set in the same command (`TestRuns.Fingerprints`; the two URLs added after the Phase 7c pilot, where an inline `BASE_URL` fix was blocked). A new folder doesn't make a failed run new. Two planned runs therefore go in one command joined with `;`. Only runs inside the project root count: the hook reads the payload's `cwd` and follows `cd`, `Set-Location` and `Push-Location` segments, so a run in a scratch copy outside the project (break it once) is neither recorded nor blocked. This is covered by unit tests; it has not been observed live, because the `cwd` payload field comes from the Claude Code hook documentation.
- `total` counts every test that ran, including the scaffold's own `GreetingTests` when the filter selects it.

## Test attributes and lifecycle (verified 2026-11-08)

Spiked on projects scaffolded from the templates, with warnings as errors, Chromium, native MTP. Each spike had a two-row data-driven test, an async per-test setup and teardown that creates and disposes an `IAPIRequestContext` from the adapter's `Playwright` property, and a class marked to run without parallelism. All four tests passed on each framework, and the category filter selected exactly the two data rows.

| | NUnit | xUnit v3 | MSTest v4 |
|---|---|---|---|
| Test class | plain class deriving from `UiTest` (`ApiTest` for API-only) | same | `[TestClass]` needed on every test class |
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

Rechecked on the scaffolded skeleton of each framework on 2026-11-08: adding the package, and `[AllureNUnit]` for NUnit, built with warnings as errors, the sample test passed, and one result file appeared at the location above.

## Setup end to end (verified 2026-11-08, Windows)

For each of NUnit, xUnit and MSTest, on Windows PowerShell 5.1 and pwsh 7: `setup.ps1`, `scaffold`, build with warnings as errors, `playwright.ps1 install chromium`, the sample test, TRX written, `ci-summary` showing 1 passed, and `check-conventions` clean. An added `Thread.Sleep` made `check-conventions` exit 1, and removing it made the check clean again.

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
  - **JSON shape of a `--vulnerable` finding** (2026-11-08, SDK 10.0.403, `System.Text.Json` 8.0.4 without warnings as errors): the same envelope as `--outdated`, with `id`, `requestedVersion`, `resolvedVersion` and `vulnerabilities[]` of `severity` and `advisoryurl`. The command exits 0 with the finding.
  - **A listing that could not run** (no restore assets, `--no-restore`): exit 1, and the JSON still has a `path` with no `frameworks`, which alone looks clean. The cause is in a top-level `problems[]` with `project`, `level` and `text`, so `package-report` treats a `problems` entry of level `error` as "not checked".
  - Not verified: NuGet audit for transitive-only vulnerabilities, and the shape of `transitivePackages` entries in a real finding (`package-report` assumes the same fields as `topLevelPackages`, without `requestedVersion`).
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

`pre-compact` was triggered live in the Phase 7c pilot (see "Acceptance pilot" below).

**Second live pass (2026-11-08), `claude -p` on a project made with `setup.ps1`:**

| Check | Result |
|---|---|
| A file that only names `git push origin main` | Written and read, not blocked |
| `git stash push`, `git stash pop` | Allowed |
| `git push origin main` | Blocked (`push-default-branch`) |
| A failing `dotnet test`, repeated unchanged | The repeat is blocked (`test-rerun-unchanged`) |
| The same command after editing a `.cs` file | Allowed |
| `Remove-Item -Recurse -Force <dir>` | Asks (`delete-files`). With no one to answer under `-p`, it isn't run. A plain single-file `Remove-Item` doesn't ask |
| `Read` on `.env` | Denied by the permission rule |
| Tool DLL missing | Work continues. `dotnet` exits 1, not 2, and Claude Code prints a hook error |

- **Claude Code fires `PostToolUseFailure`, not `PostToolUse`, for a shell command that exits non-zero.** The payload has no `tool_response`, and the output is in `error`. `post-shell` needs both events, or a failed test run is never recorded.
- Hook time over 25 runs on Windows, median: `pre-write` 47 ms, `pre-shell` 62 ms, `post-edit` 139 ms.

Other facts found on the way:

- In `--allowedTools` and permission rules, **file allow rules must be written as `Edit(path)`**. `Write(path)` is not matched by file permission checks, and Claude Code says so.
- A nested `claude -p` session starts in the user's default permission mode (here, plan mode), so live tests pass `--permission-mode default` with scoped `--allowedTools`. `bypassPermissions` is not needed.
- PostToolUse exit 2 shows stderr to Claude and does not undo the tool call. This matches the docs.

## Consumer CLAUDE.md import (verified 2026-11-21, Windows 11, Claude Code 2.1.330)

The setup for the check was a scratch folder with `git init`:
- `CLAUDE.md` held only `@.harness/templates/CLAUDE.harness.md`.
- That file held `Project rule: end every reply with the line "-- shop-tests".`

The command was `claude -p "Say hello in three words." --model haiku --disallowedTools "Read,Glob,Grep,Bash,PowerShell"`, run in a separate terminal.

The reply ended with `-- shop-tests`, and no approval prompt appeared. File tools were disallowed, so the rule could only have arrived through the import. **A relative `@` import from the root `CLAUDE.md` into `.harness/` loads in a headless run.**

`claude -p` sessions started from inside another Claude Code session loaded no `CLAUDE.md` at all, not even plain text. So run this check from an ordinary terminal.

## Sign in once per role (spike 2026-11-21, Windows, Chromium, Microsoft.Playwright 1.64.1, NUnit 5)

Run against Toolshop (`sprint5-with-bugs`; UI `http://localhost:4200`, API `http://localhost:8091`):

| Question | Result |
|---|---|
| API sign-in | `POST /users/login` with `{"email","password"}` returns `access_token` (a 337-character JWT), `token_type: bearer` and `expires_in: 15600000` |
| Where the UI keeps the sign-in | **Session storage**, key `auth-token`, holding the same JWT the API returns. No cookies, nothing in local storage |
| Storage state after a UI sign-in | `StorageStateAsync` wrote `{"cookies":[],"origins":[]}`. A new context opened with it was sent back to `/#/auth/login`. **Storage state doesn't carry a session-storage sign-in** |
| API token through an init script | `context.AddInitScriptAsync("window.sessionStorage.setItem('auth-token', '<jwt>');")` before the first navigation: `/#/account` opened signed in ("My account") |
| `StorageStatePath` naming a missing file | `NewContextAsync` throws `PlaywrightException: The specified storage state file does not exist: <path>` |
| NUnit 5 `[Order]` | Obsolete (CS0618, an error under warnings-as-errors). Use `DependsOnTest` / `DependsOnFixture` |
| Cookie and local-storage state (stand-in page through `RouteAsync`) | A cookie set by `document.cookie` and a `localStorage` item, saved with `StorageStateAsync`, were both present in a new context opened with `StorageStatePath` (`sid=abc|t1`) |

Run-once hooks and per-test context options, checked on scaffolded projects. Each had three probe tests:
one with a method attribute, one plain, and one whose `ContextOptions()` throws.

| | NUnit 5 | xUnit v3 4.0.2 | MSTest v4 |
|---|---|---|---|
| Run-once hook | `[SetUpFixture]` in the root test namespace, `[OneTimeSetUp]` / `[OneTimeTearDown]` | `[assembly: AssemblyFixture(typeof(T))]`, `T : IAsyncLifetime` | `[AssemblyInitialize] static async Task X(TestContext)` / `[AssemblyCleanup]` on a `[TestClass] public static class` |
| Hook order | An async setup (with a 500 ms delay) finished before the first `ContextOptions()`. Teardown ran after the last test | The same, also with a second test class running in parallel | The same |
| Current test method inside `ContextOptions()` | `TestContext.CurrentContext.Test.MethodName` | `TestContext.Current.TestMethod?.MethodName` | `TestContext.TestName` |
| Method attribute | `GetType().GetMethod(name)?.IsDefined(typeof(A), false)` was `true` only on the marked test, on all three frameworks | | |
| `ContextOptions()` throws | Only that test failed, as "SetUp : System.InvalidOperationException : <message>". The others passed | Only that test failed: "XunitException: System.InvalidOperationException : <message>" | Only that test failed: "Initialization method …ContextSetup threw exception. System.InvalidOperationException: <message>" |

**`[SignedOut]` with data rows and overloads (verified 2026-11-22).** This was spiked with a probe role
whose sign-in only adds a cookie, so each test could check its own context:
- **Data rows:** on all three frameworks, both rows of a two-row data-driven test got the right
  state, with and without `[SignedOut]`. The method name the framework gives has no arguments in it.
- **Overloads, before the fix:** on NUnit and MSTest, `GetMethod(name)` threw
  `AmbiguousMatchException` in setup for both overloads, even when they agreed.
- **xUnit:** overloaded tests are a build error there (xUnit1024).
- **Overloads, after the fix:** the NUnit and MSTest `UiTest` now look at every method of that name.
  A pair that agrees passed, and a pair that disagrees failed with "The overloads of X disagree on
  [SignedOut]".

**End to end (verified 2026-11-21, all three frameworks).** Each skeleton was scaffolded from the
templates, with `Roles.cs`, `SignInOnce.cs`, `UiTest.Role` and `[SignedOut]`. It ran against a
stand-in app, a PowerShell `HttpListener` with a cookie session and a local-storage token, using
`BASE_URL` for the app's address.

- **The roles:**
  - `customer` signed in with `Role.ThroughApi`, a POST through `context.APIRequest`. That request sets
    the context's cookies, so the saved state signs in.
  - `shopper` signed in with `Role.ThroughLoginPage`.
  - `broken` used `ThroughApi` with a wrong password (the API answered 401).
- **The result:** 8 tests ran and 6 passed on every framework:
  - the `customer` test
  - the `shopper` test, with its local-storage token restored
  - a `[SignedOut]` test inside the `customer` class, which saw "Signed out" and signed in through the
    form
  - a `[SignedOut]` test in a class with no role
  - an API test
  - the greeting test
- **The two intended failures:**
  - the `broken` test: "Role 'broken' could not sign in at the start of the run: API sign-in for
    broken@x returned 401."
  - a test with `Role => "typo"`: "Role 'typo' is not in Setup/Roles.cs, so it was never signed in."
- **What the app's request log showed:**
  - The `broken` role made one API call and never opened the login page. There is no runtime
    fallback.
  - `/login` was opened only by the `shopper` sign-in and by the form test.
- **Two `dotnet test` runs at the same time** from the same output folder each gave 8 tests with 6
  passed. Neither left an `.auth/<run-id>/` folder behind.
- **A skeleton with no roles** builds with 0 warnings, its greeting test passes, and `check-conventions`
  reports 0 failures.

## Traces of failed tests (verified 2026-11-08, Windows, Chromium)

`UiTest` starts tracing when `TRACE_DIR` is set, and saves the zip only when the test failed. A project scaffolded from each template, with one passing and one failing test, left a zip for the failing test only.

| | Hooks | Outcome read from |
|---|---|---|
| NUnit | `[SetUp]`, `[TearDown]` | `TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed` |
| xUnit v3 | override `InitializeAsync`, `DisposeAsync` | `TestContext.Current.TestState?.Result == TestResult.Failed`. It is set by the time `DisposeAsync` runs. Needs `using Xunit;` |
| MSTest | `[TestInitialize]`, `[TestCleanup]` | `TestContext.CurrentTestOutcome == UnitTestOutcome.Failed` |

- `playwright.ps1 show-trace <file>` is listed in the CLI's help. The viewer itself was not opened.
- A fake `ghp_` token placed in page content was found by `secret-scan` inside the trace zip (`<test>.zip!trace.trace: github-token`, exit 1).

## Browser diagnostics and masked traces (spike 2026-11-21, Windows, Chromium, Microsoft.Playwright 1.64.1)

The probe was a failing test on each framework's skeleton (`UiTest` with `TRACE_DIR`, `--report-trx`). Context routes served a fake app on `https://app.test/`:

- a `console.error` and a `console.warn`
- a thrown error
- a 500 response and an aborted request
- a password typed from an env var
- a POST with `Authorization: Bearer …` and `?api_key=…`, answered with `Set-Cookie: sid=…` and a JSON `access_token` (JWT-shaped)
- a follow-up request that sent the cookie

**Test output in the TRX:** a line written during the test lands in `<UnitTestResult><Output><StdOut>`, next to `<ErrorInfo>`.

| | Writer | `<StdOut>` |
|---|---|---|
| NUnit | `TestContext.Out.WriteLine` | the lines as written |
| xUnit v3 | `TestContext.Current.TestOutputHelper?.WriteLine` | the lines as written |
| MSTest | `TestContext.WriteLine` (the `PageTest` property) | preceded by a blank line and `TestContext Messages:` |

**Context events** (`IBrowserContext`) all compile and fire, on pages the test opens through `Page`:

| Event | Fired for |
|---|---|
| `Console` | `IConsoleMessage.Type` is `error` or `warning` (not `warn`). A failed load adds its own `console.error: Failed to load resource: …` |
| `WebError` | `IWebError.Error` is the message plus its stack, over several lines |
| `RequestFailed` | `Failure` is, for example, `net::ERR_FAILED` for an aborted route |
| `Response` | Every response. Status 500 from a route fulfil is included |

**Where secrets sit in a trace zip.** The entries are `trace.trace`, `trace.network`, `trace.stacks`, `resources/<sha1>.<ext>` and `screencast/*.jpeg`.

- **The typed value:**
  - `"method":"fill","params":{…,"value":"<value>"}`
  - a log line `"message":"  fill(\"<value>\")"`
  - the DOM snapshot attribute `"__playwright_value_":"<value>"`, even for `type="password"`
- **Headers** are `{"name":"Authorization","value":"Bearer …"}` and `{"name":"Cookie","value":"sid=…"}`. Their case is as sent. They appear in `trace.network`, and in `trace.trace` for a route's fulfil params (`{"name":"set-cookie","value":"sid=…; Path=/"}`).
- **Cookies:** `"cookies":[{"name":"sid","value":"…"}]` in `trace.network`.
- **Query:** in the URL (`?api_key=…`) and as `"queryString":[{"name":"api_key","value":"…"}]`.
- **Bodies:**
  - request and response bodies are separate `resources/<sha1>.json` files (`{"password":"…"}`, `{"access_token":"…"}`)
  - a fulfilled body also appears escaped in `trace.trace` (`\"access_token\":\"…\"`)
  - page HTML and scripts are `resources/<sha1>.html`, which held `'Bearer …'` and `api_key=…`
- **Rewriting the text entries masks them, and the viewer still works:**
  - Replacing each secret with `***` in every non-JPEG entry left none of the 5 planted values.
  - `playwright.ps1 show-trace --port <p> <zip>` served it, and a screenshot of the page showed every action, with `Fill "***"`. The snapshot rendered, and the Network (5), Console (5) and Errors (1) tabs were present.
  - The viewer doesn't check that a resource still matches its sha1 name.
  - `--port` also opens the viewer in a browser tab.
- Screenshots (`screencast/*.jpeg`) are images, and they were left alone. The password field rendered as dots.

**End to end with the skeleton's code** (`Secrets`, `Redaction`, `BrowserDiagnostics`, `TraceFiles.MaskOrRemove`, `UiTest`). On each framework the same probe ran with `TRACE_DIR` set. It read the password with `Environment.GetEnvironmentVariable`, not `Secrets.Get`, so nothing remembered it. A 500 request carried `?token=…`.

| | NUnit | xUnit v3 | MSTest |
|---|---|---|---|
| Run | 2 tests, probe failed, exit 2 | same | same |
| TRX `<StdOut>` | `Browser diagnostics (7):` and 7 lines | same | same, after `TestContext Messages:` |
| Planted secrets left in the trace's text entries (5 planted) | 0 | 0 | 0 |
| `secret-scan --path TestResults --forbid-env PROBE_PASSWORD` | exit 0 | exit 0 | exit 0 |

- **The 7 lines:**
  - `console.error: boom-console`
  - `console.warning: warn-x`
  - `pageerror: Error: boom-page at https://app.test/:5:26` (the stack, collapsed onto one line)
  - `response 500: GET https://app.test/api/fail500?token=***`
  - two `console.error: Failed to load resource: …` lines
  - `requestfailed: GET https://app.test/api/abort (net::ERR_FAILED)`
- **The password was masked although nothing remembered it.** `MaskZip` remembers values typed into `type="password"` fields, from the snapshot's `__playwright_value_`, before it masks.
- **Control:** `secret-scan` on the unmasked spike trace exits 1, finding `bearer-token`, `env:PROBE_PASSWORD`, `jwt` and `secret-field`.
- A corrupt zip passed to `TraceFiles.MaskOrRemove` was deleted, and it returned `Trace removed: it could not be masked (…)`.
- The masked MSTest trace opened in `show-trace`. It showed `Fill "***"`, the snapshot, and the Network, Console and Errors tabs.
- **Repeated after the review fixes** on fresh skeletons, with the same results on all three frameworks. The fixes were:
  - remembered values masked as whole values only
  - a masked JSON line that no longer parses fails the masking
  - the diagnostics written in a `finally`

  Every masked `trace.trace` and `trace.network` line still parsed, so no trace was deleted.
- **Not verified:** Firefox and WebKit, and a context that crashed before teardown. The `finally` is reasoned, not run.
- **Where the typed value lives (2026-11-21, NUnit skeleton, Chromium):** a failed probe filled a `type=password` field and a text field, then failed, with `TRACE_DIR` set. In the saved zip, `__playwright_value_` appeared only in `trace.trace` (9 times). It was absent from `trace.network`, `trace.stacks` and `screencast/*.jpeg`, and the probe's password was in no entry after masking. So `MaskZip`'s first pass, which remembers typed passwords, reads only `*.trace` entries.

## GitHub Actions (checked 2026-11-08)

Latest releases, read with `gh` from each repository. The templates pin the major version.

| Action | Latest | Pinned |
|---|---|---|
| `actions/checkout` | 7.0.1 | `@v7` |
| `actions/setup-dotnet` | 6.1.0 | `@v6` |
| `actions/upload-artifact` | 7.0.2 | `@v7` |
| `actions/download-artifact` | 8.0.2 | `@v8` |
| `actions/cache` (`restore`, `save`) | 6.1.0 | `@v6` |

- `playwright.ps1 install --with-deps` is listed in the CLI's help (`install --help`). It installs system packages, so CI uses it and local runs don't.
- Every shell command in the rendered workflows passed on Windows for all three frameworks, in a project made with `setup.ps1`, `scaffold` and `scaffold-ci`.
- This repository's own workflow passed on GitHub Actions (run 42502518073): tool build, tests and `validate-skills` on `ubuntu-latest` and `windows-latest`, `Test-Setup` on both PowerShell editions, and each framework's skeleton scaffolded, built and run on `ubuntu-latest`. So Linux and `install --with-deps chromium` are verified for the skeleton.
- **Not verified:** the workflows that `scaffold-ci` generates on GitHub Actions (including an empty `BASE_URL` on Linux and the baseline cache), and `actionlint`. All 16 YAML files parse. Firefox and WebKit: see the next section.

## Firefox and WebKit (spike 2026-11-21, Microsoft.Playwright 1.64.1)

Browser builds installed by `playwright.ps1 install`, the same on both systems:

- Firefox 157.0 (playwright firefox v1555)
- WebKit 27.2 (playwright webkit v2370)
- Chromium: Chrome for Testing and the headless shell 156.0.8078.4 (v1248)

Each framework's skeleton was scaffolded, built with 0 warnings, and its greeting test run with `BROWSER` set to each browser. The greeting test uses `SetContentAsync`, the context options from `UiTest` and a web-first `Expect`.

| | Windows 11 (host, SDK 10.0.403) | Linux: Ubuntu 24.04.5 in `mcr.microsoft.com/dotnet/sdk:10.0` (SDK 10.0.404, pwsh 7.6.10) |
|---|---|---|
| Install | `install firefox webkit` | `install --with-deps chromium firefox webkit`, as root, exit 0 |
| NUnit: chromium, firefox, webkit | 1/1 passed each, exit 0 | 1/1 passed each, exit 0 |
| xUnit v3: chromium, firefox, webkit | 1/1 passed each, exit 0 | 1/1 passed each, exit 0 |
| MSTest v4: chromium, firefox, webkit | 1/1 passed each, exit 0 | 1/1 passed each, exit 0 |

- **Traces (Windows):** with the expected greeting changed so that the test fails, and `TRACE_DIR` set, each framework left one trace zip on Firefox and on WebKit, exit 2.
  - Both browsers recorded the same actions and 4 DOM snapshots.
  - WebKit recorded 1 screencast frame against Firefox's 31, because the page doesn't change after it loads. So its zip is 3–4 KB against 45–51 KB.
- **Nightly reports (Linux):**
  - `dashboard --run regression-chromium=… --run regression-firefox=… --run regression-webkit=…` exited 0, and the page names each run.
  - The `scaffold-ci` nightly loop builds one `--run regression-<browser>=artifacts/results-regression-<browser>` per chosen browser. A browser with no results folder is still passed, and the dashboard lists it under "No results from" (an existing behaviour of `dashboard`; it fails only when every run is empty).
- **On the runner:** the repository's CI skeleton matrix passed on `ubuntu-latest` in run 44879879401 (2026-11-21, commit 48be1eb). That covers all 9 jobs, framework × browser, each scaffolded, built, installed with `--with-deps` and run.
- **Not verified:** real navigation on Firefox and WebKit (the greeting test sets content), Windows `--with-deps`, macOS, and branded channels.

## Probe test and scratch copy (verified 2026-11-08)

Used by automate-test when playwright-cli isn't available. On an NUnit project scaffolded from the templates, with `BASE_URL=https://demo.playwright.dev`:

- `Harness.Tools scratch-copy --to <new folder>` copied the 127 tracked and untracked files. It did not copy `bin/`, `obj/` or `.harness/state/`. The copy built with 0 warnings.
- `scratch-copy` refuses (exit 64, nothing copied) a target that is the project root, inside it, inside the git repository that contains it (also when `--root` is a subfolder), or already exists.
- A probe test in the copy ran `Page.GotoAsync("/todomvc/")`, `await Page.Locator("body").AriaSnapshotAsync()` and `GetByRole(…).CountAsync()`, and wrote them to a file with `File.WriteAllTextAsync`. The run passed, and the file held the accessibility tree in the same YAML form as a playwright-cli snapshot, without refs.

## Acceptance pilot (verified 2026-11-19 and 2026-11-20, Windows, Claude Code 2.1.329)

Master plan Verification item 5. Three scratch projects (NUnit, xUnit v3, MSTest v4), each made with `setup.ps1` and `scaffold`, run against Toolshop (`sprint5-with-bugs`; UI `http://localhost:4200`, API `http://localhost:8091`). Every stage was a fresh `claude -p` session (`--setting-sources project,local`, `acceptEdits`, empty `--strict-mcp-config` unless noted). The prompt was phrased the way a user would put it and never named a skill. The story was a six-criterion sign-in and product-search requirement with one vague criterion ("results appear quickly").

| Stage | NUnit | xUnit | MSTest |
|---|---|---|---|
| 1 Set up the framework | scaffold-framework; login test green ×2, break red | scaffold-framework; login test green ×2 | scaffold-framework; smoke test green ×2 |
| 2 Analyze the requirement | requirements-analysis; 7 REQ, 22 questions | requirements-analysis; 7 REQ, 18 questions | requirements-analysis; 7 REQ, 18 questions |
| 3 Design test cases | test-case-design; 7 TC | test-case-design; 9 TC | test-case-design; 9 TC |
| 4 Automate one UI and one API case | automate-test (test-author, framework-reviewer, loosened-reviewer, suite-runner); both broke red, then green ×2; resumed an interrupted session's partial work | the same, with the Playwright, Learn and Context7 MCP servers connected (no MCP tool was called); both broke red, green ×2 | the same, plus findings-verifier and 2 repair rounds; both broke red, green ×2 |
| 5 Review the changes | framework-review; CHANGES-REQUIRED (a case-insensitive check the TC doesn't allow, confirmed by findings-verifier) | framework-review; APPROVE | framework-review; APPROVE; first `class-ledger.md` entry |
| 6 Failing test after a commit | maintain-test (failure-investigator): planted locator typo classified as a test defect, fix proposed, no bug | not run | maintain-test: planted TC/data rename classified as an unconfirmed requirement change, no bug |
| 7 Is the requirement covered? | coverage-audit (coverage-auditor): 2 of 10 lines Built | coverage-audit: 4 of 10 Built | coverage-audit: 2 of 10 Built; noticed the conflicting product name |

- **bug-report** was checked on its own (xUnit): a real `sprint5-with-bugs` defect (the Home nav link routes to `#/contact`) was reproduced in the browser and written as `BUG-001`, with no commit and no issue created.
- **`pre-compact` (live):** `/compact` sent with `--continue` to a finished session fired `PreCompact`, which appended "Context compacted at …" to `.harness/state/session-notes.md` and showed its reminder. `SessionStart:compact` re-injected the harness reminders.
- **Without the MCP servers** every stage finished; with them (xUnit stage 4) nothing changed.
- **The `dotnet-test` overlay:** see `external-tools.md`.

Fixes made because of the pilot:

| Finding | Fix |
|---|---|
| `setup.ps1` hint printed `--framework <framework>` | prints the recorded values |
| scaffold playbook said "observe the page" with no pointer | links automate-test's `observe.md` |
| session-start pointed to a CLAUDE.md that consumer projects don't get | points to pom-architecture's Routing table |
| break-it-once and probe copies were left behind: `Remove-Item -Recurse` outside the project is denied under `-p` (all three runs) | `scratch-copy --remove <dir>` deletes only folders holding the `.harness-scratch-copy` marker (exit 64 otherwise); checked on a built copy |
| an API on its own origin had no convention: sessions invented `API_URL` and `API_BASE_URL` | `ApiContext` reads `API_URL`, falling back to `BASE_URL` when it is unset or empty; harness-setup, service-classes, utility-classes, ci-pipeline and the smoke and nightly templates name it |
| the rerun guard blocked `$env:BASE_URL = '…'; dotnet test …` after a run failed for want of `BASE_URL` | `BASE_URL` and `API_URL` set in the command count as run settings, like `BROWSER` and `HEADED` |

**Fixed (api-test-base):** API-only test classes derive from the new browserless `ApiTest` (adapter `PlaywrightTest`), so they start no browser. Spiked on all three frameworks with Chromium not installed: a probe test in `Api/` calling `Playwright.APIRequest.NewContextAsync()` built with warnings as errors and passed on NUnit, xUnit v3 and MSTest (`--filter "Name=..."` on NUnit and MSTest, `--filter-class` on xUnit). `ApiTest` needs no code of its own; the assembly-level parallelism attributes in `UiTest.cs` still apply.

**Follow-up sessions (2026-11-21, Claude Code 2.1.330, same setup):**

| Session | Prompt | Result |
|---|---|---|
| A (xUnit) | A test fails after a planted product defect (a product description changed) | maintain-test with failure-investigator classified it as a product defect. It handed over to bug-report in the same session (`BUG-002`), the test got the `bug:BUG-002` trait and stayed red, and traceability was clean. This is stage 6 on xUnit |
| B (NUnit) | Add an API test base to an older project, and prove a test can still fail | `scaffold --only Setup/ApiTest.cs` added the missing file. The test was broken inside the project and restored, not in a scratch copy, so routing was fixed (`automate-test`, break it once alone) |
| C (MSTest) | The same as B, after the routing fix | `scaffold --only` again. The break ran in `scratch-copy --to …`, went red on the changed value, and `scratch-copy --remove` deleted the copy in the live session. The project was never edited, then green ×2 |

| P8 (NUnit, Sonnet, no `BASE_URL` in the environment) | 1: "Run the login tests". 2, continuing the session: "The app runs at http://localhost:4200 …" | The first run failed with "Cannot navigate to invalid URL". The rerun `$env:BASE_URL = '…'; $env:API_URL = '…'; dotnet test …` was allowed and passed. That inline setting was the only difference the guard counts, since the results folder and the chained `"EXIT $LASTEXITCODE"` are ignored. Before the rerun, the session had already said that setting `BASE_URL` in the command makes it a different run |

Every acceptance item has now been exercised in a live session.
