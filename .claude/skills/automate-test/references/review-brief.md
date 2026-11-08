# Review brief: try to refute the new tests

Give this brief to a **fresh** general-purpose subagent: never the agent or session that wrote
the code. Fill in the bracketed parts. In phase 5c, the `framework-reviewer` agent and the
framework-review skill replace this brief.

---

You are reviewing tests that automate designed test cases in a Playwright C# framework. Your job
is to **refute** the claim that they are correct. Assume something is wrong and look for it. You
edit nothing.

**Inputs:**
- The test cases: [paths to `docs/qa/test-cases/TC-*.md`]
- The automation notes (refinement and observations): [paths to `.harness/state/automation/TC-*.md`]
- The changed files: [list]
- The author's gate results: [build, check-conventions, traceability output]
- The rules: `.claude/skills/pom-architecture/references/design-conventions.md`, and the review checklist at the end of each specialist skill that the changes touch (page-classes, element-locators, action-methods, validation-methods, test-classes, test-methods, test-data, service-classes).

**Check, for every test case:**

1. **Each expected result is proven.** Find the `Verify*` call that proves it and read that method. Does it really check the expected result, with the same value and the same scope? Or does it check something weaker: existence instead of text, contains instead of equals, any row instead of that row?
2. **Nothing changed meaning.** Compare the steps and expected results in the TC with the test's calls. Look for a dropped step, a reworded or softened expectation, or an added check the case doesn't ask for.
3. **No escape hatches:** skips, retries, raised or literal timeouts, `try`/`catch`, `if` in tests, sleeps, a `bug:` category without an open BUG record.
4. **Reuse:** an existing page, Api, action or `Verify*` method that already does this, duplicated under a new name.
5. **Locators:** priority role, then label, then test id. A specific record is targeted by its own identity, not by "first row".
6. **Data and independence:** inputs and expected values come from the paired JSON. Unique values come from `UniqueData`. Secrets come from `Secrets.Get`. The test creates what it needs and teardown deletes it. It could run in parallel and repeatedly.
7. **Links:** each test has `tc:<id>` for its case and a vocabulary category.

**Report:** start with `STATUS: DONE` (or `BLOCKED` or `NEEDS_CONTEXT`), then one line per
finding, most serious first:

```text
F1 | blocking: yes | src/Acme.Shop.Framework/Pages/OrdersPage.cs:48 | VerifyOrderAbsentAsync checks the count of all rows, not the deleted order | TC-012 step 3 expects "the order no longer appears"; the method uses ToHaveCountAsync(0) on OrderRow without the id
```

`blocking: yes` when the test could pass while the expected result is false, or when it breaks a
design-convention law. Otherwise `no`. If you find nothing, say `No findings` and list what you
checked.
