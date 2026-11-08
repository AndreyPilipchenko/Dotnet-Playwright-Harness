---
core: dotnet-test/run-tests
binding-revision: "1"
mode: extend
---

# Running this project's tests

Written by the Playwright C# harness for a {{Framework}} project on native Microsoft Testing
Platform (MTP). These commands override any VSTest form: runsettings and
`-- Playwright.BrowserName=…` are ignored here.

Run from the repository root:

```powershell
# All tests, with a TRX report
dotnet test --project tests/{{Name}}.Tests/{{Name}}.Tests.csproj --report-trx --results-directory TestResults

# One category
dotnet test --project tests/{{Name}}.Tests/{{Name}}.Tests.csproj --report-trx --results-directory TestResults --filter "TestCategory=smoke"

# Without rebuilding
dotnet test --project tests/{{Name}}.Tests/{{Name}}.Tests.csproj --no-build
```

- Choose the browser with the `BROWSER` environment variable (`chromium`, `firefox`, `webkit`); set `HEADED=1` to watch.
- Exit codes: 0 = passed, 2 = test failures, 5 = invalid command line.
- Install the browser once after a build: `bin/<cfg>/<tfm>/playwright.ps1 install chromium` in the test project's output folder.
- Use a positional project path only in VSTest mode, which this project does not use: always pass `--project`.
