# Sign In Once Per Role Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Each role signs in once per run, API first and the login page as the fallback. Its Playwright
storage state is reused by every UI test of that role. A `[SignedOut]` test still runs the login form,
and a role that fails to sign in fails only its own tests.

**Architecture:**
- The framework-neutral `Utils/SignedInSessions.cs` (with `Role` and `SignedOutAttribute`) signs in,
  saves and serves the state files.
- Each framework's run-once hook, `Setup/SignInOnce.cs`, calls it before the first test.
- `UiTest` picks the state file from the class's `Role`, unless the test method has `[SignedOut]`.
- Roles are registered in the test project's `Setup/Roles.cs`, which ships empty.

**Tech Stack:** C# templates (net10.0, Microsoft.Playwright 1.64.1, NUnit 5, xUnit v3, MSTest v4),
xUnit v3 tool tests, Markdown skills.

**Spec:** `docs/superpowers/specs/2026-11-21-sign-in-reuse-design.md`

## Global Constraints

- No blocking on async code (`.Result`, `.Wait()`, `GetResult()`). `check-conventions` must stay clean
  on the rendered skeleton.
- State files go under `AppContext.BaseDirectory/.auth/`. They are never written into `TestResults/`,
  `TRACE_DIR` or the repo tree.
- Credentials are named `<ROLE>_EMAIL` and `<ROLE>_PASSWORD`, read through `Secrets.Get`, which the
  project owns. The skeleton ships no `Secrets` class, so the template only mentions it in comments.
- The failure message, exact: `Role '<role>' could not sign in at the start of the run: <reason>`
- The framework project never references NUnit, xUnit or MSTest.
- A skeleton with no roles launches no browser in the hook, and its greeting test still passes.
- Facts go into templates and skills only after Task 1 records them in `docs/framework-matrix.md`.
- No commits, pushes or PRs. Each task ends with a suggested commit message.

## Review Focus

1. **A role whose API sign-in throws but has a UI sign-in:** it signs in through the UI, and the API
   error shows in the run output and isn't swallowed. Tested in Task 5.
2. **A test of a role that was never registered** (a typo in `Role`): it fails with a message naming
   the role, not an opaque Playwright error. Tested in Task 2 (the `PathFor` contract) and Task 5.
3. **`[SignedOut]` on a method in a class without a role:** a normal signed-out context. Tested in
   Task 5.
4. **A run where every role fails** (app down): the hook still lets API tests and the greeting test run.
   Tested in Task 5.
5. **A re-run in the same output folder:** an old `.auth/` file from a previous run is never used
   when this run's sign-in failed. `SaveAllAsync` deletes the folder first. Tested in Task 5.

---

### Task 1: Spike

**Files:**
- Modify: `docs/framework-matrix.md` (a new section, "Sign in once per role")

- [ ] **Step 1:** Scaffold NUnit, xUnit and MSTest scratch projects in the session scratchpad (never in
  this repo), with `BASE_URL=http://localhost:4200` and the API at `http://localhost:8091`. Toolshop
  must be running; if it isn't, stop and ask the user.
- [ ] **Step 2:** Settle each of the spec's spike items 1–8 with a throwaway probe, and record the
  answer:
  1. Toolshop's sign-in: the API login endpoint, its request and response, and where the app keeps
     the token (the key, if it is local storage).
  2. Storage state saved after a UI sign-in signs a new context in.
  3. Each framework's run-once hook runs before the first test under parallel runs, and its teardown
     runs. On xUnit v3 4.0.2, `AssemblyFixture` exists.
  4. What a missing `StorageStatePath` file does.
  5. An exception thrown from `ContextOptions()` fails only that test, with its message, on each
     framework.
  6. `scaffold --only` renders a single `Setup/` file.
  7. A state saved after an API sign-in signs a new context in.
  8. Inside `ContextOptions()`, how `UiTest` reads the current test method's attribute on each
     framework (NUnit `TestContext.CurrentContext.Test.MethodName`, xUnit
     `TestContext.Current.TestMethod`, MSTest `TestContext.TestName`).
- [ ] **Step 3:** Also record where each framework's run-once hook can write a line that shows in the
  run output: NUnit `TestContext.Progress`, xUnit v3 `IMessageSink` or `TestContext.Current`, MSTest
  the `AssemblyInitialize` `TestContext`.
- [ ] **Step 4:** Delete the scratch projects with `scratch-copy --remove` or a plain removal inside
  the scratchpad.
- **Stop and bring it to the user** if either of these happens:
  - item 1 shows a session-storage token
  - item 8 fails on any framework (the spec's `protected virtual bool SignedOut` fallback is a design
    change)
- [ ] **Step 5:** Suggested commit message: `Record the sign-in-reuse spike`

### Task 2: Framework-neutral sign-in code

**Files:**
- Create: `templates/framework/src/__Name__.Framework/Utils/SignedInSessions.cs`
- Test: `tools/Harness.Tools.Tests/ScaffolderTests.cs`

**Interfaces:**
- Produces (namespace `{{Name}}.Framework.Utils`):
  - `public sealed record Role(string Name, Func<IBrowserContext, Task>? ApiSignIn = null, Func<IPage, Task>? UiSignIn = null)`.
    The constructor throws `ArgumentException` when both sign-ins are null.
  - `[AttributeUsage(AttributeTargets.Method)] public sealed class SignedOutAttribute : Attribute`,
    in the same file.
  - `public static class SignedInSessions`:
    - `Task<IReadOnlyList<string>> SaveAllAsync(IBrowser browser, BrowserNewContextOptions options, IReadOnlyList<Role> roles)`
      returns warnings, one per API sign-in that failed before a UI sign-in succeeded.
    - `string PathFor(string role)`
    - `void DeleteAll()`

- [ ] **Step 1: Write the failing tests** in `ScaffolderTests`:
  - Add `"src/Acme.Shop.Framework/Utils/SignedInSessions.cs"` to `CommonFiles`.
  - `RealTemplates_SignedInSessions_KeepsStateUnderTheOutputFolder`: the rendered file contains
    `AppContext.BaseDirectory`, `".auth"`, `StorageStateAsync` and
    `could not sign in at the start of the run`. It contains none of `.Result`, `.Wait()` or
    `GetResult()`.
- [ ] **Step 2:** Run `dotnet test --project tools/Harness.Tools.Tests/Harness.Tools.Tests.csproj --filter-class "Harness.Tools.Tests.ScaffolderTests"`.
  Expected: FAIL (the file is missing).
- [ ] **Step 3: Implement the file.**
  - `SaveAllAsync`:
    1. Delete `.auth/` first.
    2. For each role, open a context from `options`. Try `ApiSignIn` if it is set. If it is null or
       throws, run `UiSignIn` on a new page, if one is set.
    3. Save `StorageStateAsync(new() { Path = PathFor-file })`.
    4. If the role still failed, record the reason in a static per-role dictionary.
    5. Always close the context.
  - `PathFor(role)` throws `InvalidOperationException` in three cases:
    - a recorded failure: the exact Global Constraints message
    - an unregistered role: "Role '<role>' is not in Setup/Roles.cs"
    - a missing file, if Task 1 item 4 showed Playwright accepts a missing file silently
  - The catch is allowed because the file is in `Utils/` (the `no-try-outside-utils` rule).
- [ ] **Step 4:** Run the same command. Expected: PASS.
- [ ] **Step 5:** Suggested commit message: `Add SignedInSessions to the skeleton's framework layer`

### Task 3: Test project: roles, run-once hook, UiTest

**Files:**
- Create:
  - `templates/<fw>/tests/__Name__.Tests/Setup/Roles.cs`
  - `templates/<fw>/tests/__Name__.Tests/Setup/SignInOnce.cs`

  for each of `nunit`, `xunit` and `mstest`.
- Modify: `templates/<fw>/tests/__Name__.Tests/Setup/UiTest.cs` (×3)
- Test: `tools/Harness.Tools.Tests/ScaffolderTests.cs`

**Interfaces:**
- Consumes: Task 2's `Role`, `SignedOutAttribute` and `SignedInSessions`.
- Produces:
  - `public static class Roles { public static IReadOnlyList<Role> All { get; } = []; }`, with a
    commented example of a `Customer` constant and its `Role` entry (`ApiSignIn` through an Api class,
    `UiSignIn` through `LoginPage.LoginAsync`, `CUSTOMER_EMAIL` and `CUSTOMER_PASSWORD`).
  - `UiTest`:
    - `public static BrowserNewContextOptions BaseContextOptions()` (locale, viewport, `BASE_URL`)
    - `protected virtual string? Role => null;`
    - `ContextOptions()` returns the base options, with
      `StorageStatePath = SignedInSessions.PathFor(Role)` when `Role` is set and the current method
      lacks `[SignedOut]`

- [ ] **Step 1: Write the failing tests:**
  - Add `"tests/Acme.Shop.Tests/Setup/Roles.cs"` and `"tests/Acme.Shop.Tests/Setup/SignInOnce.cs"` to
    `CommonFiles`.
  - `[Theory] nunit "[SetUpFixture]" / xunit "AssemblyFixture" / mstest "[AssemblyInitialize]"`
    `RealTemplates_SignInOnce_UsesTheFrameworksRunOnceHook(framework, marker)`: the hook contains the
    marker, `SignedInSessions.SaveAllAsync`, `Roles.All` and `DeleteAll`. For nunit, it also contains
    `namespace Acme.Shop.Tests;`.
  - `[Theory ×3] RealTemplates_UiTest_ChoosesTheRolesState(framework)`: `UiTest.cs` contains
    `protected virtual string? Role`, `SignedInSessions.PathFor(Role)`, `SignedOutAttribute` and
    `BaseContextOptions()`.
  - `[Theory ×3] RealTemplates_Roles_ShipsEmpty(framework)`: `Roles.cs` contains `All { get; } = [];`.
    Every line mentioning `new Role(` starts with `//`.
  - `[Theory] ScaffoldOnly_RendersEachSignInFile("Setup/Roles.cs" | "Setup/SignInOnce.cs" | "Utils/SignedInSessions.cs")`:
    `--only` on a fresh folder writes exactly that one file.
- [ ] **Step 2:** Run the `ScaffolderTests` class. Expected: FAIL.
- [ ] **Step 3: Implement** from Task 1's recorded facts:
  - Each hook:
    1. Returns at once when `Roles.All` is empty.
    2. Otherwise creates Playwright and launches the browser named by `BROWSER` (default `chromium`,
       as in `UiTest`).
    3. Calls `SaveAllAsync(browser, UiTest.BaseContextOptions(), Roles.All)`.
    4. Writes each warning to the run output that Task 1 Step 3 found.
    5. Disposes everything.
    6. Its teardown calls `DeleteAll()`.
  - `UiTest` reads `[SignedOut]` the way Task 1 item 8 recorded.
- [ ] **Step 4:** Run the class, then the full tool suite. Expected: all PASS.
- [ ] **Step 5:** Suggested commit message: `Sign in once per role in the skeleton: Roles, run-once hook, UiTest.Role`

### Task 4: Skills and docs

**Files:**
- Modify:
  - `.claude/skills/test-classes/SKILL.md`: choosing `Role`, `[SignedOut]` for the login-flow tests,
    and "login tests never set a role".
  - `.claude/skills/utility-classes/SKILL.md`: `SignedInSessions` and `Roles`, the `.auth/` location,
    API first and UI as the fallback, and the failed-role message. Add a link to the fallback
    reference.
  - `.claude/skills/service-classes/SKILL.md`: the role's API sign-in through an Api class.
  - `.claude/skills/action-methods/SKILL.md` or `page-classes/SKILL.md`: the UI sign-in reuses
    `LoginPage.LoginAsync`. One line, in whichever one already covers `LoginAsync`.
  - `.claude/skills/test-framework/SKILL.md`: the three run-once hooks, and MSTest's single
    `[AssemblyInitialize]`.
  - `.claude/skills/scaffold-framework/references/…`: adding the first role when adapting the skeleton.
  - `.claude/skills/harness-setup/references/playbook.md`:
    - §2: older projects use `scaffold --only` for the three files, plus the `UiTest` edit and the
      existing-hook check.
    - §3: the `<ROLE>_EMAIL` and `<ROLE>_PASSWORD` names.
  - `.claude/skills/ci-pipeline/SKILL.md`: pass the role secrets into the workflow.
  - `.claude/skills/reporting/SKILL.md`: `.auth/` files hold tokens and are never published.
  - `.claude/skills/pom-architecture/SKILL.md`: a Routing row, "Signing in once per role, reusing
    the session | utility-classes".
  - `docs/porting-notes.md`: narrow the item to the browser diagnostics helper.
- Create: `.claude/skills/utility-classes/references/session-fallback.md`
  - when storage state can't carry the session: session storage, one session per user, a token
    that expires within the run
  - the API-token cookie form (`AddCookiesAsync`) and the init-script form (`AddInitScriptAsync`)

- [ ] **Step 1:** Make the edits, citing only facts recorded in Task 1.
- [ ] **Step 2:** Run `dotnet run --project tools/Harness.Tools/Harness.Tools.csproj -- validate-skills`
  and the full tool suite (SkillRoutingTests, DocsTests). Expected: `0 failures, 0 warnings`, all
  tests PASS.
- [ ] **Step 3:** Suggested commit message: `Document signing in once per role, and the session fallback`

### Task 5: End to end against Toolshop

**Files:** none in the repo. This all happens in scratch projects in the session scratchpad.

- [ ] **Step 1:** For each framework, scaffold, build with warnings as errors, and install Chromium.
  Run the greeting test with no roles. Expected: PASS, and no browser launched by the hook.
- [ ] **Step 2:** Add, in the scratch project only:
  - a `customer` role with an API sign-in and a UI sign-in, built on Task 1's facts
  - a `broken` role whose sign-ins use a wrong password, and whose API sign-in throws
  - a UI-only role
  - test classes:
    - a `customer` test that opens a signed-in-only page
    - a `[SignedOut]` test in the same class that fills the login form
    - a `broken` test
    - a test with `Role => "typo"`
    - a `[SignedOut]` test in a class with no role
    - an API test

  Run the suite twice. Expected:
  - The customer tests pass, both through the API and with only the UI sign-in.
  - The `[SignedOut]` tests pass on the form.
  - The `broken` test fails with the exact role message.
  - The typo test fails naming `typo`.
  - The API and greeting tests pass.
  - The run output shows the warning for the API sign-in that fell back.
  - The second run never uses a state file from the first.
- [ ] **Step 3:** Run `check-conventions --root <scratch>` on the unmodified scaffold. Expected: clean.
- [ ] **Step 4:** Delete the scratch projects. Record the result in the framework-matrix section from
  Task 1.
- [ ] **Step 5:** Suggested commit message for the phase, and the PR description.
