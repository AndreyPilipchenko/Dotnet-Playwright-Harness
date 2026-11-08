# Assessment: <test full name or TC id> (<n>)

Kept in `.harness/state/maintenance/` (gitignored). A new file each time the evidence changes; old ones are kept.

Requested outcome: diagnose only | diagnose and fix
Restrictions: <none, or "evidence only: no runs">

## Scope

| | |
|---|---|
| Test | <namespace.class.method>, categories <smoke, tc:TC-012> |
| Test case | <TC-012, or none> |
| Commit | <short hash>, uncommitted changes: <none, or files> |
| Environment | BASE_URL <…>, browser <…>, app build <… or unknown> |

## Evidence

| What | Where | Notes |
|---|---|---|
| <TRX, CI run, local run> | <path or run id> | <first error line; stale or missing?> |

## Hypotheses

<!-- From failure-investigator: claim, one change, expected result, disconfirming evidence, confidence. -->

## Experiments

| # | Check (one factor) | Prediction | Observation | Interpretation |
|---|---|---|---|---|

## Diagnosis

Cause: product defect | test or framework defect | intermittent | environment | requirement change | unknown

<!-- Why, with the evidence. Mixed causes separately. Confidence, and what's still unknown. -->

## Resolution

<!-- Fix and where; or the owner and condition; review, break and run labels with results; open items. Attempts: <n> of 3. -->
