# Sign in once per role (design)

## Intent

UI tests that need a signed-in user shouldn't each go through the login form. The user wants all of
these, and also sees it as standard practice:
- faster runs
- fewer flaky sign-ins
- clean handling of apps with several roles

Each role (customer, admin, …) signs in **once per run**. Playwright's storage state for that role is
saved, and every test of that role starts in a new context that is already signed in. This closes the
sign-in half of the porting-notes item "Guidance on signing in once per role and reusing the session".

## Goals, non-goals, anti-goals

- **Goals:**
  - A test class says in one line which role it runs as. With no role, nothing changes.
  - Roles sign in before the first test, credentials from `Secrets.Get`. **Each role has exactly one
    sign-in, chosen when the automation is designed** (user's decision): the API, through the
    project's Api class, when the app offers an API sign-in; otherwise the UI, through
    `LoginPage.LoginAsync`. There is no fallback at run time. A role designed to sign in through the
    API fails its tests when the API sign-in fails, and never switches to the login page.
  - A test in a class that has a role can still run the **regular login flow**, by overriding the
    role for that test (user's decision).
  - The same shape works on NUnit, xUnit v3 and MSTest v4.
  - When storage state can't carry the session, the skills give a fallback.
- **Non-goals:**
  - a browser diagnostics helper (the other half of the porting-notes item)
  - a convention rule that forces tests to use a role
  - signing in to more than one application
- **Anti-goals:**
  - blocking on async code (`check-conventions` already forbids `.Result`, `.Wait()` and `GetResult()`)
  - saved sessions with tokens ending up in git, CI artifacts or reports
  - tests that check sign-in itself (wrong password, sign-out) using a saved session
  - a skeleton that fails for a project that has no roles yet

## Approaches

**A. Sign in each role at the start of the run (chosen by the user).** Each framework's run-once hook
starts a browser, signs in each role and saves its storage state. `UiTest` points `StorageStatePath`
at the role's file.
- Gains: no parallel race, no blocking waits.
- Costs: one hook per framework, and every listed role signs in even when the run uses only some of
  them.

**B. Sign in on first use, behind a lock per role.** Dropped. Playwright for .NET reads
`ContextOptions()` synchronously, so this needs sync-over-async, which the conventions forbid.

**Fallback (chosen by the user, as guidance):** for apps where storage state doesn't carry the session:
- the token is in **session storage**, which storage state doesn't save
- the app allows **one active session per user**
- the token **expires within a run**

In those cases, sign in through the API (an Api class) and put the token into each new context: a
cookie through `Context.AddCookiesAsync`, or storage through `Context.AddInitScriptAsync`, which runs
before any page script.

## Design

**Framework-neutral code (`templates/framework/…/Utils/SignedInSessions.cs`):**
- It knows the roles and how each one signs in. A role is a name plus **exactly one** async sign-in,
  fixed when the automation is designed:
  - **API sign-in (preferred when the app has one):** it works on the role's `IBrowserContext`. For
    an app that uses cookies, it posts the login through `context.APIRequest`, which shares the
    context's cookies. For an app that keeps a token in the browser, it gets the token from the
    project's Api class (for example `AuthApi.SignInAsync`) and writes it into the app's origin.
  - **UI sign-in (when the app has no API sign-in):** it works on an `IPage`, using
    `LoginPage.LoginAsync`.

  If the role's sign-in fails, the role fails, as described below. It never tries the other method.
  Choosing between the two is part of automation design. automate-test, and test-case-design where
  it covers preconditions, say to prefer the API and to record the choice. The project registers its
  roles in one place, `Setup/Roles.cs` in the test project (see the next point).
- `SaveAllAsync(IBrowser browser, BrowserNewContextOptions options)` signs in each role in its own
  context, using the same base options as `UiTest`. It saves storage state to `PathFor(role)`, then
  closes the context. **A role that fails to sign in fails only its own tests (user's decision).**
  `SaveAllAsync` records the reason per role and carries on with the others, and never throws for one
  role. When a test of that role builds its context, it fails at once with "Role '<role>' could not
  sign in at the start of the run: <reason>". API tests, login-form tests and other roles still run.
  Anything wrong outside a single role, such as a browser that won't launch, does fail the whole run.
- `PathFor(role)` points into `.auth/<run-id>/` under the test output folder
  (`AppContext.BaseDirectory`). The run id is a new GUID per test process. The folder is under `bin/`,
  so it is gitignored and is never part of `TestResults/` or `TRACE_DIR`.
- `DeleteAll()` removes this run's folder at the end of the run. It never touches another run's.
- **Storage state only (user's decision, 2026-11-21 spike):** the saved session is Playwright's storage
  state, which holds cookies and local storage. An app that keeps its sign-in in session storage
  (Toolshop does) uses the fallback below, built by the project. The scaffold doesn't try to capture
  session storage. The design follows test-design and automation guidelines, not one app's behaviour.

**Parallel runs (user's request):**
- Every role signs in inside the run-once hook, before the first test, so no test ever races to create
  a session. During the run, tests only read the state files, and many contexts can open the same
  file at once.
- One run's folder per process (`.auth/<run-id>/`): two `dotnet test` runs from the same output folder,
  or a CI retry, never delete or reuse each other's sessions. A file from an earlier run is never
  used.
- Under Microsoft Testing Platform each test project is its own process with its own output folder,
  so projects never share sessions.
- The guidance (test-classes, test-data) covers sharing one account across parallel tests:
  - **Read-only tests share the role.**
  - **A test that changes the account's own data** (cart, profile, settings) uses its own user, created
    through the API for that test, or a role kept for that purpose. Otherwise parallel tests see each
    other's changes.
  - **A test that signs out, changes the password or revokes tokens never uses a shared role.** It is
    `[SignedOut]` and signs in itself, because ending the shared session would sign out every
    parallel test of that role.
  - **An app that rotates refresh tokens on use, or allows one session per user,** can't share a saved
    session across parallel contexts. It uses the fallback, signing in per test through the API.

**Test project (`templates/<fw>/tests/__Name__.Tests/Setup/`):**
- `Roles.cs`: the project's role list. It ships empty, with a comment showing one entry. With no roles,
  the run-once hook launches no browser.
- The run-once hook, one file per framework:
  - NUnit: a `[SetUpFixture]` in the root test namespace (`[OneTimeSetUp]` / `[OneTimeTearDown]`).
  - xUnit v3: an assembly fixture (`[assembly: AssemblyFixture(...)]`, `IAsyncLifetime`).
  - MSTest: `[AssemblyInitialize]` / `[AssemblyCleanup]`.

  Each one creates Playwright, launches the browser named by `BROWSER` (as `UiTest` does), calls
  `SaveAllAsync`, and disposes everything.
- `UiTest` gets `protected virtual string? Role => null;`. `ContextOptions()` sets
  `StorageStatePath = SignedInSessions.PathFor(Role)` when `Role` is not null. A test class opts in
  with `protected override string? Role => Roles.Customer;`.
- **Per-test override (user's decision):** a test method marked `[SignedOut]` gets a fresh, signed-out
  context even inside a role class, so it can run the regular login flow. `UiTest` reads the
  attribute from the current test method through each framework's test context (builder's call: an
  attribute keeps the test body free of setup code). The class-level `Role` stays overridable as
  above. A method-level `[SignInAs(role)]` that switches to another role is left out until someone
  needs it.
- `UiTest` and the hook share the base context options (locale, viewport, `BASE_URL`), so a saved
  session matches the context it is used in.

**Projects scaffolded before this change (user's decision: documented steps):** new scaffolds get the
feature built in. For an existing project, the harness-setup playbook's §2 section "Files a newer
harness adds to the skeleton", and the skill that covers roles, give the steps:
- `scaffold --only` for each new file: `Setup/Roles.cs`, the hook file and `Utils/SignedInSessions.cs`
- the small edit to the project's own `UiTest.cs`: the `Role` member, `StorageStatePath`, and the
  shared options
- a check for a run-once hook the project already has. MSTest allows only one `[AssemblyInitialize]`
  per assembly, so the role sign-in goes into the existing one instead of a second file. On NUnit and
  xUnit a second hook is allowed but should be checked for order dependence.

Updates still never edit project code.

**Skills:**
- test-classes: choosing a role. Login tests never set one.
- utility-classes: `SignedInSessions` and `Roles`, and where the files go.
- action-methods and page-classes: the role's sign-in delegate reuses `LoginPage.LoginAsync`.
- service-classes: the API sign-in for the fallback.
- test-framework: the three run-once hooks.
- scaffold-framework: adding the first role when adapting the skeleton.
- reporting: `.auth/` files hold tokens, so never publish them.
- pom-architecture: a routing line for "sign in once / reuse the session".
- A short **fallback** section, in utility-classes or a reference it links: when to use it, and the
  cookie and init-script forms.

**Docs:**
- porting-notes: narrow the item to the diagnostics helper.
- framework-matrix: the spike.
- routing-guide: if DocsTests needs it.

## Spike first ("facts come from spikes")

Against Toolshop (`sprint5-with-bugs`, running locally), record in `docs/framework-matrix.md`:

1. Where Toolshop keeps its sign-in: cookie, local storage or session storage.
2. That a storage-state file saved after a UI sign-in opens a new context already signed in.
3. That each framework's run-once hook runs before the first test, also when tests run in parallel,
   and that the teardown runs. On xUnit v3, check that `AssemblyFixture` exists in the pinned
   version.
4. What happens when `StorageStatePath` names a file that doesn't exist. This decides whether a
   missing role fails loudly or silently.
5. That throwing from `ContextOptions()`, for a role that failed to sign in, fails only that test and
   shows the message on each framework, and doesn't stop the run.
6. That `scaffold --only` renders each new file on its own, the way Setup/ApiTest.cs does.
7. That an API sign-in through `context.APIRequest` sets the context's cookies, so the saved state
   signs in a new context. Checked on the stand-in app. Toolshop's API login and session-storage
   token are recorded as the fallback's worked example (done 2026-11-21).
8. That `UiTest` can see the current test method's `[SignedOut]` attribute inside `ContextOptions()`,
   on each framework. If one framework can't, the fallback is a `protected virtual bool SignedOut`
   the test class sets per test. That is a design change, so it comes back to the user.

Done on 2026-11-21: Toolshop keeps the token in session storage. The user chose storage state only,
with Toolshop as the fallback's worked example.

## Tests

- Tool tests on the real templates (Scaffolder tests):
  - each framework renders `Roles.cs` and its hook
  - `UiTest` has the `Role` member
  - no unknown placeholders
- The end-to-end scaffold check builds all three frameworks with warnings as errors. The skeleton's
  greeting test still passes with no roles.
- `check-conventions` is clean on the rendered skeleton.
- A scratch-copy check on each framework against a **small stand-in app served from the scratch
  project** by a local `HttpListener`, which keeps its sign-in in a cookie and in local storage. A
  route would have to be added to every context, so a listener is simpler. This checks the
  feature against the behaviour it supports, not against Toolshop. Toolshop, which uses session
  storage, is the worked example for the fallback, verified in the spike. The check covers:
  - one role with good credentials and one with a wrong password
  - an API role's test and a UI role's test both pass signed in
  - an API role whose API sign-in fails makes its tests fail with the role message, and the login page
    is never opened
  - a `[SignedOut]` test in the same class runs the login form
  - the bad role's test fails with the role message
  - the greeting test still passes
- Optional and live, after the user confirms: one pilot project gets a customer role, and a test that
  opens a signed-in-only page passes without touching the login form.

## Builder's calls (made while writing, check these)

- **Credentials:** per role, `<ROLE>_EMAIL` and `<ROLE>_PASSWORD`, read through `Secrets.Get`, for
  example `CUSTOMER_EMAIL`. The `Roles.cs` comment, harness-setup §3 and ci-pipeline (which secrets to
  pass into the workflow) name the pattern. The `ci/` templates don't change, because the project adds
  its own secrets to the workflow.
- **Passing roles in:** the framework project can't reference the test project, so the hook passes
  `Roles` into `SignedInSessions`.
- **Shared context options:** move into one static member that `UiTest` and the hook both use.
- **NUnit hook location:** the `[SetUpFixture]` sits in the root `{{Name}}.Tests` namespace, not
  `.Setup`, so it covers every test.
- **Missing state file:** if spike 4 shows Playwright accepts a missing `StorageStatePath` without
  failing, `PathFor` checks for the file and throws, naming the role.

## Left to the builder

- exact type and member names beyond those above
- the comment text in `Roles.cs`
- how the role delegate is typed (`Func<IPage, Task>` or a small record)
- the wording of the skill sections
