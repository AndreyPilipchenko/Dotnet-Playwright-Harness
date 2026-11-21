# Consumer CLAUDE.md Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every project with the harness gets the harness's standing instructions through one `@` import
line in its own `CLAUDE.md`, which `install` adds and updates keep current.

**Architecture:** The text is a static payload file, `templates/CLAUDE.harness.md`. Setup already copies
it to `.harness/templates/` and tracks it in the manifest. `Installer.Run` gains one step that makes sure
the root `CLAUDE.md` contains `@.harness/templates/CLAUDE.harness.md`, without touching any other text.

**Tech Stack:** C# (net10.0) `Harness.Tools`, xUnit v3 tests, PowerShell 5.1/7 `Test-Setup.ps1`.

**Spec:** `docs/superpowers/specs/2026-11-21-consumer-claude-md-design.md`

## Global Constraints

- Import line, exact: `@.harness/templates/CLAUDE.harness.md`
- The template has no placeholders, and it points to pom-architecture's Routing table instead of copying it.
- The team's `CLAUDE.md` text is never rewritten. Only the one line is appended.
- Test-first for `Harness.Tools`. Warnings are errors.
- No commits, pushes or PRs. Each task ends with a suggested commit message, and the phase ends with a PR description.
- A fact goes into the template only after it is recorded in `docs/framework-matrix.md` (Task 1).

## Review Focus

1. **No trailing newline:** `CLAUDE.md` ends without a newline. The import must start on its own line and not be glued to the last word. Tested in Task 3.
2. **CRLF file:** the appended lines use CRLF too, so the file doesn't end up with mixed line endings. Tested in Task 3.
3. **UTF-8 BOM:** a file with a BOM keeps it. Tested in Task 3.
4. **Line already present but indented, or with trailing spaces:** this counts as present, with no duplicate. Tested in Task 3.
5. **Re-run through `update.ps1`:** the line appears exactly once after setup plus update. Tested in Task 4.

---

### Task 1: Spike, does Claude Code follow the import?

**Files:**
- Modify: `docs/framework-matrix.md` (a new short "Consumer CLAUDE.md import" section)

- [ ] **Step 1:** In the session scratchpad, make a folder with `git init`. Add `CLAUDE.md` containing only the import line, and `.harness/templates/CLAUDE.harness.md` containing `The harness codeword is LANTERN-42.`
- [ ] **Step 2:** From that folder, run `claude -p "What is the harness codeword? Answer with the word only." --model haiku --setting-sources project,local --strict-mcp-config --mcp-config '{"mcpServers":{}}' --output-format json`.
  Expected: the result is `LANTERN-42`, with no permission denial or prompt about imports.
- [ ] **Step 3:** Record the CLI version (`claude --version`), the command and the result in `docs/framework-matrix.md`.
  **If it fails**, stop and bring the fallback to the user: a marked block that `install` rewrites. That is a design change.

### Task 2: The template and its content test

**Files:**
- Create: `templates/CLAUDE.harness.md`
- Test: `tools/Harness.Tools.Tests/Installing/ClaudeMdTemplateTests.cs`

**Interfaces:**
- Produces: `templates/CLAUDE.harness.md`, which `RepoPaths.Templates` resolves.

- [ ] **Step 1: Write the failing tests** in `ClaudeMdTemplateTests`:
  - `Template_NamesTheSetupScript_AndTheEntryPoint`: the text contains `.harness/setup.ps1`, `pom-architecture` and `Routing`.
  - `Template_NamesTheBranchPrefixes`: it contains `automation/`, `harness/`, `feature/` and `fix/`.
  - `Template_HasNoPlaceholders`: it does not match `\{\{\w+\}\}` or `__Name__`.
  - `Template_DoesNotCopyTheRoutingTable`: it has no line matching `^\| .* \| [a-z-]+ \|$` that names a skill.
- [ ] **Step 2:** Run `dotnet test --project tools/Harness.Tools.Tests/Harness.Tools.Tests.csproj --filter-class "Harness.Tools.Tests.Installing.ClaudeMdTemplateTests"`. Expected: FAIL (file missing).
- [ ] **Step 3:** Write `templates/CLAUDE.harness.md`, about 40 lines, with these sections:
  - **Before anything else:** if `.harness/tools/bin/Harness.Tools.dll` is missing, run `.harness/setup.ps1`. Until then, the hooks and checks guard nothing.
  - **Where to start:** the pom-architecture skill and its Routing table.
  - **Git:** the four branch prefixes, never push to the default branch, and commit, push or PR only when the user asks.
  - **Tests:** a test is green only after the scoped runs in run-tests. No skips. A test of a known product defect stays red with `bug:<id>`. Run `check-conventions` before committing.
  - **Source of truth, in order:** code, then runtime evidence (runs, traces, the app), then docs, then AI output.
- [ ] **Step 4:** Run the same command. Expected: PASS.
- [ ] **Step 5:** Suggested commit message: `Add the consumer CLAUDE.harness.md template`

### Task 3: `install` adds the import line

**Files:**
- Modify: `tools/Harness.Tools/Installing/Installer.cs`
- Create: `tools/Harness.Tools/Installing/ClaudeMd.cs`
- Test: `tools/Harness.Tools.Tests/Installing/ClaudeMdTests.cs`

**Interfaces:**
- Produces:
  - `internal enum ClaudeMdChange { Created, Appended, Unchanged }`
  - `internal static class ClaudeMd { public const string ImportLine = "@.harness/templates/CLAUDE.harness.md"; public static ClaudeMdChange Ensure(string path); }`
  - `InstallResult` gets a new last positional member, `ClaudeMdChange ClaudeMd`.

- [ ] **Step 1: Write the failing tests** in `ClaudeMdTests`. Each one works on a `TempDir`, and the file is `CLAUDE.md` in its root.
  - `NoFile_IsCreated_WithTheImportLine`: returns `Created`. The file has a `#` heading line and then the import line.
  - `FileWithoutTheLine_GetsItAppended_AndKeepsItsText`: `"# Team\n\nOur rules.\n"` returns `Appended`. The file becomes `"# Team\n\nOur rules.\n\n@.harness/templates/CLAUDE.harness.md\n"`.
  - `NoTrailingNewline_StartsTheImportOnItsOwnLine`: `"Our rules."` becomes `"Our rules.\n\n@…\n"`.
  - `CrlfFile_StaysCrlf`: `"A\r\nB\r\n"` becomes `"A\r\nB\r\n\r\n@…\r\n"`.
  - `BomFile_KeepsItsBom`: the first three bytes are still `EF BB BF`.
  - `LineAlreadyThere_EvenIndented_IsUnchanged`: `"x\n  @.harness/templates/CLAUDE.harness.md  \n"` returns `Unchanged`, and the bytes are identical.
  - `Rerun_IsByteIdentical`: `Ensure` twice. The second call returns `Unchanged`.
  - `Install_ReportsTheClaudeMdChange`: `Installer.Run` on a fresh `TempDir` gives `result.ClaudeMd == Created` and the file exists.
- [ ] **Step 2:** Run with `--filter-class "Harness.Tools.Tests.Installing.ClaudeMdTests"`. Expected: build FAIL (`ClaudeMd` not defined).
- [ ] **Step 3: Implement `ClaudeMd.Ensure(string path)`:**
  - Read the bytes and detect the BOM. Use the first newline sequence in the text (`\r\n` or `\n`), and `\n` when there is none.
  - A line counts as present when its trimmed form equals `ImportLine`.
  - Write with the same BOM state. Change nothing when the line is present.
  - Call it from `Installer.Run` after `UpdateGitignore`, with `Path.Combine(root, "CLAUDE.md")`, and pass the result into `InstallResult`.
- [ ] **Step 4:** Run the class, then the whole suite. Expected: all PASS. The existing `InstallerTests` still pass, including `Rerun_IsByteIdentical`.
- [ ] **Step 5:** Suggested commit message: `install: add the harness import line to the project's CLAUDE.md`

### Task 4: CLI output and the setup scripts check

**Files:**
- Modify: `tools/Harness.Tools/Cli.cs` (`RunInstall` and the usage text)
- Modify: `tools/Harness.Tools.Tests/Installing/InstallerTests.cs` (`Cli_Install_Exits0_AndReports`)
- Modify: `scripts/tests/Test-Setup.ps1`

**Interfaces:**
- Consumes: `InstallResult.ClaudeMd` from Task 3.

- [ ] **Step 1: Extend `Cli_Install_Exits0_AndReports`** to assert that stdout contains `CLAUDE.md: created`. Run it. Expected: FAIL.
- [ ] **Step 2:** In `RunInstall`, print one line after the Hooks line: `CLAUDE.md: created`, `CLAUDE.md: harness import line added` or `CLAUDE.md: already imports the harness`. In the usage line for `install`, add "and adds the harness import line to CLAUDE.md". Run it. Expected: PASS.
- [ ] **Step 3: Add checks to `Test-Setup.ps1`:**
  - after "First install": `Check 'CLAUDE.md imports the harness once'`. Exactly one line equals `@.harness/templates/CLAUDE.harness.md`, and `.harness\templates\CLAUDE.harness.md` exists.
  - after the update block: the same check, and the `CLAUDE.md` text is unchanged since the first install.
- [ ] **Step 4:** Run `powershell -NoProfile -NonInteractive -File scripts/tests/Test-Setup.ps1` and `pwsh -NoProfile -NonInteractive -File scripts/tests/Test-Setup.ps1`. Expected: `All checks passed.` on both.
- [ ] **Step 5:** Suggested commit message: `Report the CLAUDE.md change in install, and check it in Test-Setup`

### Task 5: Docs

**Files:**
- Modify:
  - `docs/porting-notes.md` (remove the consumer `CLAUDE.md` bullet from "Planned but not built")
  - `README.md` (one line in the setup paragraph)
  - `.claude/skills/harness-setup/references/playbook.md` (§1 and §2: what `install` does to `CLAUDE.md`; §5: rolling back deletes the line)
  - `CLAUDE.md` of this repo (Layout: `templates/CLAUDE.harness.md` is not a scaffold layer, like `settings.harness.json`)

- [ ] **Step 1:** Make the edits.
- [ ] **Step 2:** Run `dotnet build Harness.slnx` (0 warnings), the full tool tests, and `dotnet run --project tools/Harness.Tools/Harness.Tools.csproj --no-build -- validate-skills`. Expected: all pass and `0 failures, 0 warnings`.
- [ ] **Step 3: End to end:**
  - Scaffold into a scratch folder.
  - `git init` it, run `scripts/setup.ps1 -Target <dir> -Framework xunit -Name Acme.Shop`, and confirm that `CLAUDE.md` holds the import line once.
  - Run `check-conventions --root <dir>`. Expected: clean.
- [ ] **Step 4:** Give the suggested commit message and the PR description for the phase.
