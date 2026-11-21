# Browser Diagnostics and Masked Traces Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A failed UI test's TRX holds a masked "Browser diagnostics" block, and every trace `UiTest`
saves is masked before the test ends.

**Architecture:**
- Three new framework-neutral Utils: `Secrets`, `Redaction` and `BrowserDiagnostics`.
- `UiTest` on each framework attaches the diagnostics at setup. At teardown, when the test failed,
  it writes the lines to the framework's test output and masks the trace zip.
- A spike fixes the facts first.

**Tech Stack:** Microsoft.Playwright 1.64.1, NUnit 5, xUnit v3, MSTest v4 under MTP,
`System.IO.Compression`, `System.Text.RegularExpressions`; `Harness.Tools.Tests` (xUnit v3).

**Spec:** `docs/superpowers/specs/2026-11-21-browser-diagnostics-design.md`

## Global Constraints

- **Mask token:** `***`. Values shorter than 4 characters are not remembered.
- **Diagnostics:**
  - At most 50 lines, then `… and <n> more`. Each line is cut to 300 characters.
  - Console types `error` and `warning`, page errors, failed requests, responses `>= 400`.
- **Header names:** `authorization`, `proxy-authorization`, `cookie`, `set-cookie`, `x-api-key`.
  Secret field names are the `ReportSecrets.SecretNames` list.
- **A trace that can't be masked is deleted.** The test output then says
  `Trace removed: it could not be masked (<type>).`
- **`Secrets.Get`'s missing-variable message:** `Set the <name> environment variable.`
- **Code rules:**
  - Utils never reference a test framework.
  - No `Console.`, `.Result`, `.Wait()` or `GetResult()`.
  - Warnings are errors.
- No commits, pushes or PRs. Each task gives a suggested commit message, and the phase gives a PR
  description.

## Review Focus

1. **A secret that spans the 300-character cut:** mask before cutting, never after. Tested in
   Task 2: a line with the secret at position 295.
2. **Overlapping remembered values** (`abcd`, `abcdef`): longest first, so no tail is left.
   Tested in Task 2.
3. **A zip entry that is binary but valid UTF-8 by chance:** a strict decode plus a NUL check. A
   JPEG must come back byte-for-byte. Tested in Task 2 with a real screencast jpeg from the spike,
   or one made with a NUL byte.
4. **Teardown after the context is already closed or crashed:** the diagnostics write must not
   throw over the real failure. Tested in Task 4 end to end, where the browser keeps running; the
   rest is reasoned in review.
5. **Parallel tests:** one `BrowserDiagnostics` per test instance, never static. `Redaction`'s set is
   shared on purpose. Tested in Task 3 (the template is checked for no `static` diagnostics field).

---

### Task 1: Spike

**Files:**
- Modify: `docs/framework-matrix.md` (new section "Browser diagnostics and masked traces")

- [ ] **Step 1:** In the scratchpad, scaffold nunit, xunit and mstest. In each, add a failing probe
  test:
  - routes serve a page that logs `console.error`, throws, fetches a 500 and an aborted URL, and
    posts a login form
  - the password comes from an env var
  - the POST's answer has `Set-Cookie: sid=<value>` and a JSON body with `access_token` set to a
    JWT-shaped value
  - the probe writes a marker line through the framework's test output
- [ ] **Step 2:** Run each with `TRACE_DIR` and `--report-trx`. Record:
  - where the marker lands in the TRX
  - that the context events (`Console`, `WebError`, `RequestFailed`, `Response`) fire
  - the exact JSON forms in `trace.trace`, `trace.network` and `resources/` of the fill value, the
    headers, the cookie and the body
- [ ] **Step 3:** Mask a trace by rewriting its entries (a throwaway script). Then open it with
  `playwright.ps1 show-trace --port <p>` and load the viewer page with Playwright. Expected: the
  action list shows the probe's actions. **Stop and ask the user** if it does not.
- [ ] **Step 4:** Record everything in the matrix. Suggested commit message:
  `Record the browser-diagnostics and trace-masking spike`

### Task 2: `Secrets` and `Redaction`

**Files:**
- Create:
  - `templates/framework/src/__Name__.Framework/Utils/Secrets.cs`
  - `templates/framework/src/__Name__.Framework/Utils/Redaction.cs`
- Test: `tools/Harness.Tools.Tests/ScaffolderTests.cs` (template content), plus a behaviour test
  project in the scratchpad.

**Interfaces (produced):**
- `Secrets.Get(string name) -> string`
- `Redaction.Remember(string value)`
- `Redaction.Mask(string text) -> string`
- `Redaction.MaskZip(string path)`, which throws on failure. The caller deletes the zip.

- [ ] **Step 1: Write the failing template tests:**
  - `RealTemplates_Secrets_RemembersWhatItReturns`: contains `Redaction.Remember(` and the exact
    message, and no `Console.`
  - `RealTemplates_Redaction_MasksTheSpikedForms`: each header name, `***`, the secret-name list,
    and a JWT pattern
  - add both paths to `CommonFiles`
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3: Implement**, using the forms recorded in Task 1.
- [ ] **Step 4: Behaviour check.** Scaffold, then add a scratch xUnit test file inside the scaffolded
  test project that calls `Mask` and `MaskZip`. It covers the five spike forms, Review Focus 1–3,
  and a corrupt zip that throws. Run it, then delete it. Record the results in the ledger.
- [ ] **Step 5:** Full suite green. Suggested commit message:
  `Add Secrets and Redaction to the skeleton`

### Task 3: `BrowserDiagnostics` and `UiTest`

**Files:**
- Create: `templates/framework/src/__Name__.Framework/Utils/BrowserDiagnostics.cs`
- Modify: `templates/{nunit,xunit,mstest}/tests/__Name__.Tests/Setup/UiTest.cs`
- Test: `tools/Harness.Tools.Tests/ScaffolderTests.cs`

**Interfaces:**
- Consumes: Task 2's `Redaction.Mask` and `Redaction.MaskZip`.
- Produces: `BrowserDiagnostics.Attach(IBrowserContext) -> BrowserDiagnostics` and `Lines`.

- [ ] **Step 1: Write the failing tests:**
  - `RealTemplates_UiTest_WritesDiagnosticsAndMasksTheTrace(framework, outputCall)`, where
    `outputCall` is the spiked writer per framework. It asserts `BrowserDiagnostics.Attach(Context)`,
    `Redaction.MaskZip(`, the removed-trace message, and no `static BrowserDiagnostics`.
  - `RealTemplates_BrowserDiagnostics_IsCappedAndMasked`: `50`, `300`, `Redaction.Mask(`, and the
    four event names.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Scaffold all three. Each must build with 0 warnings, pass its greeting test and
  pass `check-conventions` cleanly. Then run the full suite. Suggested commit message:
  `Write browser diagnostics into the TRX and mask traces as they are saved`

### Task 4: End to end, skills and docs

- [ ] **Step 1:** On each framework, run the Task 1 probe against the real skeleton code. Expected:
  - the TRX holds `Browser diagnostics (n):` with masked lines
  - the masked trace's text holds none of the seeded secrets, checked with
    `Select-String -SimpleMatch` over the extracted entries
  - `secret-scan --path <results> --forbid-env <PASSWORD_VAR>` exits 0
  - the masked trace opens in the viewer (as in Task 1)

  Record the results in the matrix.
- [ ] **Step 2:** Update the skills and docs:
  - utility-classes: the table rows and §4
  - maintain-test playbook §2 and `failure-investigator.md`: read the block in `<StdOut>`, or
    wherever the spike puts it
  - reporting: the traces section and the screenshot limit
  - harness-setup §2: older projects
  - porting-notes
  - the `Roles.cs` comment, if needed
- [ ] **Step 3:** validate-skills and the full suite. Expected: clean. Then the final commit message
  and the PR description.
