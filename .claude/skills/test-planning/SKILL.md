---
name: test-planning
description: Use when planning testing for a release, feature, sprint or project - writing a test plan under docs/qa/test-plans/, deciding scope and what is out of scope, risks and their mitigation, test levels (UI, API, manual), environments and data, or entry and exit criteria.
---

# Test planning

A test plan says what will be tested, what won't and why, where the risk is, and when testing is
done. It cites requirement and test case ids; it doesn't repeat them.

## 1. Gather

- The requirement records for the scope (`docs/qa/requirements/`). If there are none, run requirements-analysis first.
- The existing cases and their automation state: `dotnet .harness/tools/bin/Harness.Tools.dll traceability --json`.
- Open bugs (`docs/qa/bug-reports/`, `status: open`) that touch the scope.
- What the user tells you about the release: dates, environments, who tests manually. Ask only for what you can't read.

## 2. Write the plan

Copy `assets/test-plan-template.md` to `docs/qa/test-plans/<name>.md` (a short kebab-case name,
such as `checkout-v2.md`). The plan is not a traceability record, so it has no front matter.

- **Scope:** every in-scope item names its requirements. Every out-of-scope item gives a reason.
- **Risks:** business risks, rated by evidence from the requirements and open bugs, each mapped to the cases or level that mitigate it. A risk with no mitigation is an open item, not a hidden gap.
- **Approach:** which cases run automated (by category filter), which are `manual-only`. Commands come from the test-framework skill, never from memory.
- **Environments and data:** name the variables and roles; never write a secret into the plan.
- **Exit criteria:** measurable, and checkable from the records: case status, open bug severity, `traceability` clean.

## 3. Review in chat

Show the scope table, the risks ordered by impact, and the coverage gaps (in-scope requirements
with no cases, high-priority cases still `planned`). The user decides; the plan records the
decision.

## 4. Keep it current

When scope or requirements change, update the plan in the same change and say what moved. A plan
that no longer matches the records is worse than none. impact-analysis tells you which cases a
change touches.

## Boundaries

- Requirement records and questions → requirements-analysis
- Designing the cases the plan cites → test-case-design
- What a code or requirement change affects → impact-analysis
- Coverage numbers across all records → plan-tracker
