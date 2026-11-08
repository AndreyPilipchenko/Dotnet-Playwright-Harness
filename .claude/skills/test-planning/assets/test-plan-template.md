# Test plan: <release, feature or sprint>

- **Owner:** <role>
- **Date:** <yyyy-mm-dd>
- **Requirements:** REQ-001, REQ-002

## Scope

| In scope | Out of scope (and why) |
|---|---|
| <feature or requirement> | <feature, and the reason: not changed, covered elsewhere, not testable here> |

## Risks

| Risk | Likelihood | Impact | Mitigation (which cases, which level) |
|---|---|---|---|
| <what could go wrong for the business> | High/Medium/Low | High/Medium/Low | TC-001, TC-004; API checks for data integrity |

## Approach

| Level | What | How it runs |
|---|---|---|
| UI | <flows> | `dotnet test --project tests/<Name>.Tests/<Name>.Tests.csproj` with the category filter of the test-framework skill |
| API | <endpoints> | <same project, Api/ tests> |
| Manual | <`manual-only` cases> | <who runs them, where results go> |

## Environments and data

- <environment, BASE_URL variable, accounts by role (never the secrets themselves), seeded data>

## Entry criteria

- <the build is deployed; the accepted requirements are listed above; blockers named>

## Exit criteria

- <every high-priority case has run; no open bug of severity critical or high without a decision; traceability has no failures>

## Open items

- <decisions or dependencies still missing>
