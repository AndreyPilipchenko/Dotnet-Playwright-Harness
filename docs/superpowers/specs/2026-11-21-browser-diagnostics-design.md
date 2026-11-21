# Browser diagnostics in the TRX, and secrets masked in traces (design)

## Intent

When a UI test fails, the people and agents who diagnose it see only the assertion message and
stack trace in the TRX. What the browser said is invisible to maintain-test and the
failure-investigator agent: console errors, uncaught page errors, failed requests and HTTP error
responses. The only place that holds it is a Playwright trace, which needs `TRACE_DIR` and the trace
viewer.

This change does two things:
- It puts a short, capped list of browser diagnostics into the TRX of each failed UI test.
- **User's requirement:** secrets such as passwords and tokens are masked or removed from traces and
  from these diagnostics.

It closes the porting-notes item "A browser diagnostics helper".

## Goals, non-goals, anti-goals

- **Goals:**
  - A failed UI test's TRX holds a "Browser diagnostics" block, on NUnit, xUnit v3 and MSTest v4,
    with no setting to turn it on. A passing test writes nothing.
  - Every trace that `UiTest` saves has its text masked before the test ends. That covers typed
    values, headers, cookies, bodies and secret-named fields.
  - The diagnostics lines are masked the same way.
  - maintain-test, failure-investigator and reporting read and describe the block.
- **Non-goals:**
  - masking pixels in screenshots
  - masking the assertion message or stack trace (`secret-scan` still guards the TRX)
  - diagnostics for `ApiTest`, whose failures already carry the response in the assertion
  - a new report page
- **Anti-goals:**
  - an unmasked trace left on disk because masking failed
  - any secret value in an exception, a log or the TRX
  - diagnostics so long that they bury the assertion message

## Approaches

1. **Mask in the test run, as each file is written (chosen by the user).** New `Utils/Secrets`
   (which remembers the values it hands out) and `Utils/Redaction` in the skeleton. `UiTest` masks the
   diagnostics before writing them and rewrites the trace zip's text entries right after
   `StopAsync`. `secret-scan` stays the final check before CI publishes anything.
2. Mask in the tool, with a `secret-scan --redact` step in CI. Dropped: local traces would stay raw,
   and they're the ones people share by hand.
3. Both. Dropped: the patterns would live in two places for little gain.

## Design

### `Utils/Secrets` (new in the skeleton)

- `public static string Get(string name)`:
  - Reads the environment variable.
  - Throws `InvalidOperationException` with `"Set the <name> environment variable."` when it is
    missing or empty. The message names the variable, never a value.
  - Calls `Redaction.Remember(value)` before returning.
- **Older projects** that already have their own `Utils/Secrets.cs` keep it and add the one
  `Redaction.Remember` call. `scaffold --only` never overwrites a file.

### `Utils/Redaction` (new)

- `public static void Remember(string value)`:
  - Adds a value to mask, such as a token an Api class got back from a sign-in.
  - Values shorter than 4 characters are ignored, because masking them would wreck ordinary text.
  - Thread-safe, because tests run in parallel.
- `public static string Mask(string text)` replaces each of these with `***`:
  - every remembered value, as an exact substring, longest first
  - the values of headers named `authorization`, `proxy-authorization`, `cookie`, `set-cookie` and
    `x-api-key`, in the trace's `{"name":…,"value":…}` form and in `Name: value` text
  - every cookie `value` in the trace's network entries
  - `Bearer <token>`
  - JSON-like fields and `key=value` query fields whose name is in the secret-name list
    (`password|passwd|pwd|secret|client_secret|api_key|apikey|token|access_token|refresh_token|id_token|auth_token|authorization|cookie|set-cookie`,
    the same list `secret-scan` uses)
  - JWT-shaped strings (`eyJ…\.eyJ…\.…`)
  - The exact forms come from the spike (below), not from guesses.
- `public static void MaskZip(string path)`: rewrites each text entry of a zip with `Mask`.
  - An entry is text if it decodes as strict UTF-8 with no NUL character. Images and other binary
    entries are copied as they are.
  - It writes to a temp file next to the zip, then replaces the zip.
- **On any failure the unmasked trace is deleted** (builder's call: an unmasked trace must never
  remain), and `UiTest` writes `Trace removed: it could not be masked (<exception type>).` to the
  test output. There's no fallback to keeping the trace.

### `Utils/BrowserDiagnostics` (new)

- `public static BrowserDiagnostics Attach(IBrowserContext context)` subscribes to the context-level
  events, so pages the test opens itself are covered as well:
  - console messages of type `error` and `warning`
  - uncaught page errors
  - failed requests
  - responses with status 400 and above
- `public IReadOnlyList<string> Lines`: one line each, such as `console.error: …`,
  `pageerror: …`, `requestfailed: GET <url> (<failure>)` or `response 500: POST <url>`.
  - Masked, and each cut to 300 characters.
  - Capped at **50 lines**, followed by `… and <n> more`.
  - Kept in the order the events arrived. Thread-safe.
- Builder's call: query strings are kept in URLs, after masking, because they often explain a 4xx.

### `UiTest` (all three frameworks)

- At setup: `_diagnostics = BrowserDiagnostics.Attach(Context)`.
- At teardown, when the test failed and there is at least one line:
  - It writes `Browser diagnostics (<n>):` and then the lines to the framework's test output, which
    the spike must show lands in the TRX:
    - NUnit `TestContext.Out`
    - xUnit v3 `TestContext.Current.TestOutputHelper`
    - MSTest `TestContext.WriteLine`
  - With `TRACE_DIR` set, it saves the trace as today, then calls `Redaction.MaskZip`.
- A passing test writes and keeps nothing, as today.

### Skills and docs

- **utility-classes:**
  - the Secrets and Redaction rows
  - §4: read credentials through `Secrets.Get`, which remembers them; `Redaction.Remember` for
    tokens the app hands back
  - what is masked, and the screenshot limit
- **maintain-test and failure-investigator:** read the "Browser diagnostics" block in the failed
  test's TRX output.
- **reporting:** traces are masked when written; screenshots aren't; `secret-scan` is still the check
  before anything is published.
- **harness-setup §2:** steps for older projects.
- **porting-notes:** remove the item.
- **framework-matrix:** the spike.

## The spike first (Task 1)

On Windows, Chromium, all three frameworks, against a stand-in page served by `SetContentAsync` and
routes (no real app):
- A form fills a password from an env var through `Secrets.Get`.
- A route answers a POST with `Set-Cookie: sid=…` and a JSON body `{"access_token": "eyJ…"}`.
- The page logs a console error and throws.
- One request returns 500 and another fails.

Record:
- where each framework's test output lands in the TRX (`<StdOut>` or otherwise)
- the exact forms of headers, cookies, the fill value and bodies inside `trace.trace`,
  `trace.network` and `resources/`
- that a trace masked by rewriting its entries still opens in `playwright.ps1 show-trace`, checked by
  loading the viewer page and seeing the actions listed
- that the `IBrowserContext` event names exist in 1.64.1

If masking by rewriting entries breaks the viewer, stop and bring it back to the user.

## Tests

- **`Harness.Tools.Tests`** (the template files, as today):
  - the new files render
  - `UiTest` wires `Attach`, the output writer and `MaskZip` on each framework
  - no `.Result`, `.Wait()` or `Console.`
- **End to end on a scaffolded skeleton** with a failing sample:
  - the TRX holds the block
  - the masked trace holds none of the seeded secrets
  - `secret-scan` on the results folder passes
  - a deliberately unmaskable zip is deleted
- `check-conventions` stays clean on the skeleton.

## Left to the builder

The exact regex forms (from the spike), the line wording, file layout and test names.
