# Sign In Once Per Role Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Each role signs in once per run, through the one sign-in chosen for it when the automation
was designed: the API when the app has one, otherwise the login page. Its Playwright storage state is
reused by every UI test of that role. A `[SignedOut]` test still runs the login form,
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
- State files go under `AppContext.BaseDirectory/.auth/<run-id>/`, with a new GUID per test process.
  They are never written into `TestResults/`, `TRACE_DIR` or the repo tree.
- Storage state only. Session storage is not captured. Session-storage apps use the documented
  fallback.
- Credentials are named `<ROLE>_EMAIL` and `<ROLE>_PASSWORD`, read through `Secrets.Get`, which the
  project owns. The skeleton ships no `Secrets` class, so the template only mentions it in comments.
- The failure message, exact: `Role '<role>' could not sign in at the start of the run: <reason>`
- The framework project never references NUnit, xUnit or MSTest.
- A skeleton with no roles launches no browser in the hook, and its greeting test still passes.
- Facts go into templates and skills only after Task 1 records them in `docs/framework-matrix.md`.
- No commits, pushes or PRs. Each task ends with a suggested commit message.

## Review Focus

1. **An API role whose API sign-in fails** (wrong password, API down): its tests fail with the role
   message, which carries the API error. The login page is never opened, because there is no runtime
   fallback. Tested in Task 5.
2. **A test of a role that was never registered** (a typo in `Role`): it fails with a message naming
   the role, not an opaque Playwright error. Tested in Task 2 (the `PathFor` contract) and Task 5.
3. **`[SignedOut]` on a method in a class without a role:** a normal signed-out context. Tested in
   Task 5.
4. **A run where every role fails** (app down): the hook still lets API tests and the greeting test run.
   Tested in Task 5.
5. **Two runs at once, or one after another, in the same output folder:** neither uses or deletes the
   other's files, because each process has its own `.auth/<run-id>/`. Tested in Task 5.

---

### Task 1: Spike

**Files:**
- Modify: `docs/framework-matrix.md` (a new section, "Sign in once per role")

- [ ] **Step 1:** Scaffold NUnit, xUnit and MSTest scratch projects in the session scratchpad, never in
  this repo. Items 1 and 7's Toolshop half are done (2026-11-21, session-storage token). Items 2 and 7
  use a stand-in cookie and local-storage page.
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
  - `public sealed record Role(string Name, Func<IBrowserContext, Task> SignIn)`, with
    `public static Role ThroughApi(string name, Func<IBrowserContext, Task> signIn)` and
    `public static Role ThroughLoginPage(string name, Func<IPage, Task> signIn)`. The second wraps the
    page delegate so that it opens a page on the context. Exactly one sign-in per role, chosen at
    design time.
  - `[AttributeUsage(AttributeTargets.Method)] public sealed class SignedOutAttribute : Attribute`,
    in the same file.
  - `public static class SignedInSessions`:
    - `Task SaveAllAsync(IBrowser browser, BrowserNewContextOptions options, IReadOnlyList<Role> roles)`
    - `string PathFor(string role)`
    - `void DeleteAll()`

- [ ] **Step 1: Write the failing tests** in `ScaffolderTests`:
  - Add `"src/Acme.Shop.Framework/Utils/SignedInSessions.cs"` to `CommonFiles`.
  - `RealTemplates_SignedInSessions_KeepsStateUnderTheOutputFolder`: the rendered file contains
    `AppContext.BaseDirectory`, `".auth"`, `Guid.NewGuid()`, `StorageStateAsync` and
    `could not sign in at the start of the run`. It contains none of `.Result`, `.Wait()` or
    `GetResult()`.
- [ ] **Step 2:** Run `dotnet test --project tools/Harness.Tools.Tests/Harness.Tools.Tests.csproj --filter-class "Harness.Tools.Tests.ScaffolderTests"`.
  Expected: FAIL (the file is missing).
- [ ] **Step 3: Implement the file.**
  - `SaveAllAsync`:
    1. Use this process's run folder, `.auth/<run-id>/`, from a static GUID.
    2. For each role, open a context from `options` and run the role's one `SignIn`. Never try
       another method.
    3. Save `StorageStateAsync(new() { Path = PathFor-file })`.
    4. If the sign-in threw, record its message as the reason in a static per-role dictionary.
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
    commented example of a `Customer` constant and its entry. The example is `Role.ThroughApi` through
    an Api class, with a one-line note that an app without an API sign-in uses `Role.ThroughLoginPage`
    with `LoginPage.LoginAsync`. It reads `CUSTOMER_EMAIL` and `CUSTOMER_PASSWORD`.
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
    4. Disposes everything.
    5. Its teardown calls `DeleteAll()`.
  - `UiTest` reads `[SignedOut]` the way Task 1 item 8 recorded.
- [ ] **Step 4:** Run the class, then the full tool suite. Expected: all PASS.
- [ ] **Step 5:** Suggested commit message: `Sign in once per role in the skeleton: Roles, run-once hook, UiTest.Role`

### Task 4: Skills and docs

**Files:**
- Modify:
  - `.claude/skills/test-classes/SKILL.md`: choosing `Role`, `[SignedOut]` for the login-flow tests,
    and "login tests never set a role". Also the parallel-run rules from the spec:
    - read-only tests share a role
    - a test that changes account data uses its own API-created user or a dedicated role
    - sign-out, password change and token revocation are `[SignedOut]` and sign in themselves
    - token-rotating or one-session apps use the fallback
  - `.claude/skills/test-data/SKILL.md`: a per-test user created through the API, for tests that
    change account data.
  - `.claude/skills/utility-classes/SKILL.md`: `SignedInSessions` and `Roles`, the `.auth/` location,
    one sign-in per role (`ThroughApi` or `ThroughLoginPage`) with no runtime fallback, and the
    failed-role message.
  - `.claude/skills/automate-test/` (design stage) and `test-case-design` (preconditions): choosing the
    role's sign-in is a design decision. Prefer the API when the app has one, record the choice, and
    don't add a UI fallback. Add a link to the fallback
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
  - when storage state can't carry the session: session storage, one session per user, refresh-token
    rotation, a token that expires within the run
  - the API-token cookie form (`AddCookiesAsync`) and the init-script form (`AddInitScriptAsync`)
  - the worked example from the spike: a session-storage `auth-token` JWT from `POST /users/login`,
    without naming the app as a requirement

- [ ] **Step 1:** Make the edits, citing only facts recorded in Task 1.
- [ ] **Step 2:** Run `dotnet run --project tools/Harness.Tools/Harness.Tools.csproj -- validate-skills`
  and the full tool suite (SkillRoutingTests, DocsTests). Expected: `0 failures, 0 warnings`, all
  tests PASS.
- [ ] **Step 3:** Suggested commit message: `Document signing in once per role, and the session fallback`

### Task 5: End to end on a stand-in app

**Files:** none in the repo. This all happens in scratch projects in the session scratchpad.

The app is a stand-in, served by an `HttpListener` that the scratch project starts in its own run-once
code before the sign-in hook:
- `/login`: a form that sets a session cookie and a local-storage token
- `POST /api/login`: sets the same cookie, and returns 401 for a wrong password
- `/account`: shows "Signed in" only with the cookie and the token, otherwise "Signed out"

- [ ] **Step 1:** For each framework, scaffold, build with warnings as errors, and install Chromium.
  Run the greeting test with no roles. Expected: PASS, and no browser launched by the hook.
- [ ] **Step 2:** Add, in the scratch project only:
  - a `customer` role `ThroughApi` (`context.APIRequest` POST, which sets the cookie)
  - a `shopper` role `ThroughLoginPage`, with the same account
  - a `broken` role `ThroughApi` with a wrong password
  - test classes:
    - a `customer` test that opens a signed-in-only page
    - a `[SignedOut]` test in the same class that fills the login form
    - a `broken` test
    - a test with `Role => "typo"`
    - a `[SignedOut]` test in a class with no role
    - an API test

  Run the suite twice. Expected:
  - The `customer` and `shopper` tests pass signed in.
  - The `[SignedOut]` tests pass on the form.
  - The `broken` test fails with the exact role message, which carries the API error. No login page
    is opened for it.
  - The typo test fails naming `typo`.
  - The API and greeting tests pass.
  - Run the suite twice at the same time from the same output folder. Both pass, and each leaves no
    `.auth/<run-id>/` behind.
- [ ] **Step 3:** Run `check-conventions --root <scratch>` on the unmodified scaffold. Expected: clean.
- [ ] **Step 4:** Delete the scratch projects. Record the result in the framework-matrix section from
  Task 1.
- [ ] **Step 5:** Suggested commit message for the phase, and the PR description.
