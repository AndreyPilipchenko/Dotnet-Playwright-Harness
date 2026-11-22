# Signed-in sessions: sign in once per role

Tests that need a signed-in user don't each go through the login form. Each role signs in once per
run, its Playwright storage state is saved, and every UI test of that role starts in a new context
that is already signed in. Verified on NUnit, xUnit v3 and MSTest v4 (docs/framework-matrix.md, "Sign
in once per role").

## The parts

| File | Job |
|---|---|
| `src/<Name>.Framework/Utils/SignedInSessions.cs` | `Role`, `[SignedOut]`, and `SignedInSessions`, which signs in, saves and serves the state files |
| `tests/<Name>.Tests/Setup/Roles.cs` | The project's roles. It ships empty |
| `tests/<Name>.Tests/Setup/SignInOnce.cs` | The run-once hook. It signs in every role before the first test and deletes the sessions after the last |
| `tests/<Name>.Tests/Setup/UiTest.cs` | `protected virtual string? Role`. When it is set and the test lacks `[SignedOut]`, `ContextOptions()` loads that role's state |

## Add a role

```csharp
public const string Customer = "customer";

public static IReadOnlyList<Role> All { get; } =
[
    Role.ThroughApi(Customer, context => AuthApi.SignInAsync(
        context.APIRequest, Secrets.Get("CUSTOMER_EMAIL"), Secrets.Get("CUSTOMER_PASSWORD"))),
];
```

`AuthApi.SignInAsync(IAPIRequestContext request, string email, string password)` is a static method on
the project's Api class. It posts on the request object it is given and **never disposes it**,
because that object belongs to the browser context. Don't build it through `ApiContext`: a separate
request context keeps the cookies to itself, the sign-in seems to work, and the saved state is
empty.

A test class opts in with one line: `protected override string? Role => Roles.Customer;`.

- **Each role has exactly one sign-in, chosen when the automation is designed.** Use `Role.ThroughApi`
  when the application has an API sign-in. Use `Role.ThroughLoginPage` with `LoginPage.LoginAsync` only
  when it doesn't. Record the choice in the automation notes.
- **No runtime fallback.** If an API sign-in fails, it fails. It never switches to the login page,
  because that would hide a broken API.
- **API sign-in, cookie session:** a POST through `context.APIRequest` shares the context's cookies, so
  the saved state carries the session. This needs the sign-in API on the **same origin as the app**
  (`BASE_URL`). An API on its own origin (`API_URL`) sets cookies for that origin, not the app's.
  Throw from the sign-in when the response is not OK, for example
  `API sign-in for <email> returned 401.`, so the failure says why.
- **API sign-in, token in local storage:** get the token from the Api class (which calls
  `Redaction.Remember(token)`, so traces and diagnostics mask it), open a page on the app,
  write it with `page.EvaluateAsync("t => localStorage.setItem('<key>', t)", token)`, then let the state
  be saved. A token the app keeps in session storage can't be saved this way: use the fallback below.
  The local-storage form isn't spiked yet (docs/framework-matrix.md), so check it with a signed-in test
  that passes twice.
- **Credentials:** `<ROLE>_EMAIL` and `<ROLE>_PASSWORD`, read through `Secrets.Get`. Never in
  `Roles.cs` or the JSON.

## What a failure looks like

| Cause | The role's tests fail with |
|---|---|
| The sign-in threw | `Role 'broken' could not sign in at the start of the run: <the sign-in's first error line>`. The whole exception, with Playwright's call log, is the inner exception |
| Two roles whose names map to the same file (`a b` and `a_b`, or `Admin` and `admin`) | The run-once hook refuses before anyone signs in, naming both roles, so the whole run fails. No role can run as another |
| `Role` names a role that isn't in `Roles.cs`, or the run-once hook didn't run before the test | `Role 'typo' was never signed in. Either it is not in Setup/Roles.cs, or SignInOnce did not run before this test (…)`. On NUnit, the hook covers only the root test namespace and those below it |

Only that role's tests fail. API tests, `[SignedOut]` tests, tests without a role, and other roles
still run. A browser that won't launch fails the whole run.

## `[SignedOut]`

A method marked `[SignedOut]` gets a fresh, signed-out context, even in a class that has a role. Use
it for the login flow itself: a wrong password, sign-out, password reset. A login test never sets a
role.

It applies to every data row of a data-driven test. The check goes by method name, so on NUnit and
MSTest, overloads of one test must all have `[SignedOut]` or all lack it. If they disagree, each of
them fails with "The overloads of X disagree on [SignedOut]". Give the signed-out test its own name.
xUnit doesn't allow overloaded tests (xUnit1024).

## Parallel runs

- Every role signs in inside the run-once hook, before any test starts, so no two tests race to
  create a session. During the run, tests only read the state files.
- Each test process keeps its files in its own `.auth/<run-id>/` folder under the output folder. Two
  runs at once, or a retry, never share or delete each other's sessions.
- A run holds a `.lock` in its folder for as long as it runs. A killed run never deletes its folder,
  so the next run that signs in deletes it: any folder whose lock is free, and any folder with no lock
  that is more than a day old. Never delete a folder whose run is still going.
- **Read-only tests share a role.**
- **A test that changes the account's own data** (cart, profile, settings) uses its own user, created
  through the API for that test (test-data), or a role kept for that purpose. Otherwise parallel tests
  see each other's changes.
- **A test that signs out, changes the password or revokes tokens never uses a shared role.** It is
  `[SignedOut]` and signs in itself. Ending the shared session would sign out every parallel test of
  that role.

## When storage state can't carry the session

Storage state saves cookies and local storage, not session storage. Don't use a saved role when:
- the application keeps its sign-in in **session storage**
- it allows **one active session per user**
- it **rotates refresh tokens** on use
- its token **expires within a run**

Sign in per test through the API instead, and put the token into the test's context before the first
navigation:
- a cookie: `await Context.AddCookiesAsync([new() { Name = "sid", Value = token, Url = baseUrl }]);`
- browser storage: `await Context.AddInitScriptAsync($"window.sessionStorage.setItem('auth-token', '{token}');");`

Do this in the class's per-test setup (test-classes), with the token from the project's Api class.
**A worked example from the spike:** an application whose `POST /users/login` returns an
`access_token` JWT and whose UI keeps it in session storage under `auth-token`. Its saved state came
back empty, and the init script signed in.

## Projects scaffolded before this

Render the three new files with `scaffold --only` (harness-setup playbook §2), then edit the
project's own `UiTest.cs` to match the template:
- `BaseContextOptions()`
- `Role`
- `ContextOptions()` with `StorageStatePath`
- the `[SignedOut]` check

Check for an existing run-once hook first. MSTest allows one `[AssemblyInitialize]` per assembly, so
put the sign-in into the existing one instead of adding `SignInOnce.cs`.

## Never

- Publish or upload `.auth/` files. They hold tokens (reporting).
- Block on async code in `ContextOptions()` to sign in lazily. The conventions forbid it, and the
  run-once hook makes it unnecessary.
