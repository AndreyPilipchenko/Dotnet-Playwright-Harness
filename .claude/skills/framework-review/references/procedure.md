# Framework review: procedure

For the `framework-reviewer` agent. Adapted from the Playwright POM Harness (MIT, see NOTICE.md).

Run every command from the work folder. Your output is the verdict block in section 7.

## 1. Scope

The files the brief lists, or everything changed since the base ref, **including untracked files**.
New code is often not added to git yet, and a plain diff doesn't show it.

```powershell
git diff --name-only <base>
git ls-files --others --exclude-standard
```

Note the branch with `git branch --show-current`. A change directly on the default branch is a finding.

## 2. Mechanical pass, first

```powershell
dotnet .harness/tools/bin/Harness.Tools.dll check-conventions --changed --base-ref <base>
dotnet .harness/tools/bin/Harness.Tools.dll traceability
```

`check-conventions` exits 0 when clean, 1 for new failures, and 2 when nothing could be checked. Each
new failure is a confirmed finding: use the `file:line` it names, and don't argue with it. Exit 2 means
you stop and report `STATUS: BLOCKED`, unless the scope has no C# files.

## 3. Sort the files by layer

| Path | Read the review checklist of |
|---|---|
| `tests/<Name>.Tests/Ui/`, `Api/` | test-classes, test-methods |
| `src/<Name>.Framework/Pages/` | page-classes, element-locators, action-methods, validation-methods |
| `src/<Name>.Framework/Apis/`, `Dbs/` | service-classes |
| `src/<Name>.Framework/Helpers/` | action-methods |
| `src/<Name>.Framework/Utils/`, `Config/` | utility-classes |
| `src/<Name>.Framework/Utils/Extensions/` | extension-classes |
| `resources/testData/` | test-data |
| `*.csproj`, `Directory.*.props`, `.editorconfig`, `global.json` | csharp-standards |

Anything else (`.claude/`, `docs/`, scripts) gets a read for consistency only.

## 4. Walk the checklist

Take design-conventions §11, box by box, for the layers in the diff. When in doubt, open the owning
skill's checklist and judge by that, not by memory. Record each box as PASS, FAIL or N/A, with
`file:line` for each FAIL.

Then check these, which a linter can't:

- **Reuse:** for each new public action or `Verify*` method, search `Pages`, `Apis`, `Dbs` and `Helpers` for one with the same intent, under any name. A duplicate is blocking (class `duplicate-action-method`).
- **Exemptions:** read every new `// conventions-ok:`. Is the reason real?
- **Targeting one record:** design-conventions §7. Is it found by its own identity, and not by "first row"?
- **State:** design-conventions §5. What the test creates, it deletes, also when it fails partway.
- **Secrets:** nothing in source, JSON, test names or messages.

## 5. For each test case in the brief

1. **Each expected result is proven.** Find the `Verify*` call that proves it and read that method. Does it check the same value at the same scope? Or something weaker: existence instead of text, contains instead of equals, any row instead of that row?
2. **Nothing changed meaning.** Compare the TC's steps and expected results with the test's calls. Look for a dropped step, a softened expectation, or a check the case doesn't ask for.
3. **No escape hatches:** skips, retries, raised or literal timeouts, `try`/`catch`, `if` in tests, sleeps, or a `bug:` category without an open BUG record.
4. **Locators:** role, then label, then test id, as the notes observed them.
5. **Data and independence:** inputs and expected values come from the paired JSON, unique values from `UniqueData`, secrets from `Secrets.Get`. Could it run in parallel, and repeatedly?
6. **Links:** `tc:<id>` for its case, and a vocabulary category.

`blocking: yes` when the test could pass while the expected result is false, or when it breaks a
design-conventions law. Otherwise `no`.

## 6. For a framework change in the brief

Compare the diff with the notes file: does it do what the acceptance criteria ask, and only that? A
change with no criterion behind it is a finding.

## 7. The verdict block

```text
FRAMEWORK REVIEW: <scope> on <date>
Reviewer: independent agent
Mechanical pass: <n> failures, <n> warnings (new)
Boxes: <n> PASS, <n> FAIL, <n> N/A

F1 | blocking: yes | src/Acme.Shop.Framework/Pages/OrdersPage.cs:48 | VerifyOrderAbsentAsync checks the count of all rows, not the deleted order | TC-012 step 3 expects "the order no longer appears"; the method uses ToHaveCountAsync(0) on OrderRow without the id | rule: validation-methods | class: absence-not-scoped-to-record

Questions (unproven): <suspicions that need the author's answer, or none>
Checked and found sound: <one line per area>

VERDICT: APPROVE | CHANGES-REQUIRED
```

Any blocking finding means CHANGES-REQUIRED. Order the findings most serious first. Never fix anything.
