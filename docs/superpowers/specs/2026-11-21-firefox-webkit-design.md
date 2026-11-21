# Firefox and WebKit (design)

## Intent

Only Chromium is verified today. The framework matrix records only Chromium, the generated CI
hard-codes `BROWSER: chromium`, and ci-pipeline and the README say so. This verifies Firefox and WebKit
on all three frameworks, on Windows and Linux, and lets a project's CI run them. Closes porting-notes
"Firefox and WebKit in CI, because they aren't verified".

## Goals, non-goals, anti-goals

- **Goals:**
  - Firefox and WebKit verified by spike on NUnit, xUnit v3 and MSTest v4, on Windows and on Linux,
    and recorded in `docs/framework-matrix.md`.
  - **The harness asks the user which browsers the nightly run covers, then generates the CI from
    the answer** (user's decision). The ci-pipeline skill asks. `scaffold-ci` takes the answer as an
    option.
  - The repo's own CI proves the claim: the skeleton runs on all three browsers for all three
    frameworks.
- **Non-goals:** branded channels (Chrome, Edge), mobile emulation, browser-specific test code, a
  browser matrix for local runs.
- **Anti-goals:**
  - claiming a browser works anywhere it wasn't run
  - silently running fewer browsers than the user chose
  - making the smoke run slower

## Approaches

1. **Ask, then generate (chosen by the user).** ci-pipeline asks which browsers nightly covers.
   `scaffold-ci --browsers chromium,firefox,webkit` renders a nightly job matrix over the answer.
2. Always run all three browsers every night. Dropped: it costs three times the CI minutes whether
   the project needs it or not.
3. Chromium by default, with the others behind a repository variable. Dropped: cross-browser
   breakage would only show up when someone remembers to turn it on.

## Design

- **Spike first.** On Windows (directly) and Linux (a .NET 10 SDK container in Docker, which mirrors
  `ubuntu-latest`), for each framework:
  - scaffold
  - `playwright.ps1 install --with-deps firefox webkit`
  - run the skeleton's greeting test with `BROWSER=firefox` and with `BROWSER=webkit`

  Record each result, the browser builds Playwright 1.64.1 installs, and anything that differs from
  Chromium. A browser that fails somewhere is recorded as not verified there, and comes back to the
  user before it is offered.
- **`scaffold-ci --browsers <list>`:**
  - Allowed values: `chromium`, `firefox`, `webkit`, comma-separated, at least one, no duplicates.
    Anything else is a usage error that writes nothing.
  - The default is `chromium`, so non-interactive callers get today's output.
  - The order is kept as given.
- **The nightly template:** the `regression` job becomes a matrix over `{{Browsers}}`, with
  `fail-fast: false`, so one browser failing doesn't cancel the others.
  - Each job installs its own browser (`setup-harness` with `browser: ${{ matrix.browser }}`) and sets
    `BROWSER`.
  - Each job uploads `results-regression-<browser>`.
  - The `reports` job downloads them all and passes one `--run regression-<browser>=…` per browser to
    `dashboard`, so the page shows them side by side.
- **Smoke** runs on the first browser in the list (builder's call: the user's first choice is the one
  they care about most), so it stays one fast job.
- **The ci-pipeline skill:** before running `scaffold-ci`, ask which browsers nightly should cover,
  showing the cost (one nightly job per browser), and pass the answer. To change the answer later,
  re-run `scaffold-ci` into a scratch folder and compare, the same way as today.
- **The repo's own CI:** the skeleton job's matrix becomes framework × browser (9 jobs). Each one
  installs only its own browser (builder's call).
- **Skills and docs:**
  - run-tests, test-framework, harness-setup and scaffold-framework: installing and choosing Firefox
    and WebKit
  - ci-pipeline
  - README ("Only Chromium is verified")
  - porting-notes
  - framework-matrix

## Tests

- `ScaffoldCiTests`:
  - the default renders a Chromium-only matrix
  - `--browsers firefox,webkit` renders both, in that order
  - smoke uses the first browser
  - an unknown or duplicate browser, or an empty list, is a usage error that writes nothing
  - no placeholders are left
- The generated YAML stays valid. The existing tests parse it.
- The repo CI's 9 jobs are the Linux end-to-end proof once the user pushes. The Docker spike runs first,
  so nothing depends on that push.

## Left to the builder

The option's error text, the YAML layout, and the skill's wording of the question.
