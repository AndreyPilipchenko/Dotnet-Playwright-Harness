# C# build standards: playbook

Versions and commands here come from the harness's `docs/framework-matrix.md`, where each was
verified by spike. When a command you find online differs, the matrix wins: many examples
online show the VSTest form, which does not apply under Microsoft Testing Platform (MTP).

## 1. What the build settings do

`Directory.Build.props` applies to every project in the repository:

| Property | Value | Why |
|---|---|---|
| `TargetFramework` | `net10.0` unless `scaffold --target-framework` set another | One target for the framework and the tests |
| `Nullable` | `enable` | Null mistakes show up at build time, not as a flaky `NullReferenceException` |
| `TreatWarningsAsErrors` | `true` | A warning can't be ignored "for now" |
| `AnalysisMode` | `Recommended` | The .NET analyzers at their recommended set |
| `EnforceCodeStyleInBuild` | `true` | `.editorconfig` rules (naming, braces, file-scoped namespaces) fail the build, not only the IDE |
| `ManagePackageVersionsCentrally` | `true` | Versions live in `Directory.Packages.props` only |
| `ImplicitUsings` | `enable` | `System`, `System.IO`, `System.Linq`, `System.Threading.Tasks` and similar need no `using` |

`global.json` pins the SDK feature band with `rollForward: latestFeature` and selects the test
runner: `"test": { "runner": "Microsoft.Testing.Platform" }`. Every test project is
`OutputType=Exe`. The test-framework skill covers the runner property for each framework.

Framework-specific analyzers come in through packages: NUnit projects get `NUnit.Analyzers`.

## 2. Packages

### Adding one
1. Check that it isn't already on the list in `Directory.Packages.props`, and that no listed package does the job.
2. **Ask the user**, naming the package, its version, its license, and why the listed packages are not enough.
3. Once approved, add `<PackageVersion Include="…" Version="…" />` to the matching `ItemGroup` in `Directory.Packages.props`.
4. Add `<PackageReference Include="…" />`, without a version, to the project that needs it.
   - The framework project (`src/<Name>.Framework`) never gets a test-framework package. `check-conventions` reports one as `layer-boundary`.

### Checking for updates and vulnerabilities
Run from the repository root:

```powershell
dotnet package list --outdated
dotnet package list --vulnerable --include-transitive
dotnet package list --outdated --format json   # for a script to read
```

- **Never judge these by their exit code: it is 0 whether or not something is found.** Read the report.
  - Text output: "has no updates given the current sources" means nothing was found. Otherwise it shows a table with `Requested`, `Resolved` and `Latest` columns.
  - JSON output: a finding is any entry under `projects[].frameworks[].topLevelPackages[]`. A project without findings has no `frameworks` key.
- A vulnerable direct package already **fails the build**: restore-time NuGet audit reports it as an `NU19xx` warning (`NU1903` for a high-severity one), and warnings are errors. Fix it by updating the package. Never silence the code.
- The older `dotnet list package --outdated` form gives the same result.

### Updating one
- Change the version in `Directory.Packages.props` only.
- The four Playwright packages (`Microsoft.Playwright` and its NUnit, xUnit and MSTest adapters) move together, to the same version.
- After a Playwright update, reinstall the browser: `bin/<cfg>/<tfm>/playwright.ps1 install chromium`.
- A **major** version of NUnit, xUnit or MSTest needs the user's approval first. The Playwright adapters accept open-ended framework versions, so the build won't stop you, but a new major is only known to work after a spike.
- Build and run the full suite once before you finish.

## 3. When the build fails on a warning

1. Read the rule id (`CA…`, `IDE…`, `CS…`, `NUnit…`) and its message. Look the id up if it is unclear.
2. **Fix the code.** Common cases in test code:
   - `CS8618` (non-nullable field not set in the constructor): set it in the constructor. For a field set in setup, use `= null!;` on that field, as design-conventions §5 shows for `_orders`.
   - `CA2007` (`ConfigureAwait`) does not fire in the scaffolded build, which awaits without `ConfigureAwait`. If it ever appears, don't add `ConfigureAwait(false)` all over test code; ask the user.
   - `IDE` naming errors: rename to match `.editorconfig` (`_camelCase` private fields, PascalCase constants and static readonly fields, `Async` suffix).
3. If the warning is really wrong for this code, suppress it **at the narrowest scope, with a reason**:

```csharp
// On the one member or type that needs it, never assembly-wide:
[SuppressMessage("<Category>", "<RuleId>", Justification = "<why this rule is wrong for this code>")]
```

   - Never add `<NoWarn>` to `Directory.Build.props`, lower a severity in `.editorconfig`, or turn off `TreatWarningsAsErrors`. Those silence every future case too.
   - Never use `#pragma warning disable` without a matching `restore` and a comment saying why.

## 4. Changing the target framework or SDK

- The target framework is set once, in `Directory.Build.props`.
- Only `net10.0` with SDK 10.0.403 is verified (see the matrix). Another target is possible with `scaffold --target-framework`, but it is unverified until spiked.
  - In particular, xunit.v3 cannot run in VSTest mode on SDK 10. Never switch the runner back to VSTest to "fix" a test run.
- `global.json` keeps `rollForward: latestFeature`, so newer SDK 10 feature bands are used automatically.
