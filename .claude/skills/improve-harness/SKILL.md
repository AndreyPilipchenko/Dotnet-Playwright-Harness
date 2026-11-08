---
name: improve-harness
description: Use when asked to review how this Playwright C# harness has been working in a project and propose improvements - "improve the harness", "what keeps going wrong", "turn these review findings into rules", or when a class in the review ledger reaches three entries. Finds recurring friction with evidence and writes exact proposed edits. Never applies them.
---

# Improve the harness

The harness should learn from its own use. Find **recurring** friction, cite it, and propose exact
edits. **You write a proposal. You never edit a skill, agent, rule or tool here.** The user decides what lands.

## 1. Choose the window

Default: the last 14 days, or since the date in `.harness/state/last-retro.txt` if that is later. The
user can name another. State the window before doing anything else.

## 2. Read the evidence

| Source | What it shows |
|---|---|
| `.harness/review/class-ledger.md` | Confirmed review findings, by class |
| `.harness/state/hook-events.jsonl` | Hook denials and prompts, by rule |
| `.harness/state/automation/*.md` | Repair rounds, `broke: green`, blocked cases |
| `.harness/state/maintenance/*.md` | Causes and attempts |
| `.harness/state/impl/*.md` | Repair rounds and failed criteria |
| `.harness/conventions-baseline.json`, `// conventions-ok:` | Exemptions that are growing |

If there is nothing in the window, say so and stop.

## 3. Find what recurs

A single occurrence is an anomaly. Discard it.

| Pattern | Threshold | Propose a change to |
|---|---|---|
| The same finding class | 3 rows | A `check-conventions` rule, or the owning skill's checklist |
| The same hook rule denied | 3 times | The skill that led there, or the rule if it was a false alarm |
| Repairs used up, or `broke: green` | 2 cases | `automate-test`, or the `test-author` brief |
| The same cause in maintenance | 2 assessments | The owning skill, or a named timeout or helper |
| Refuted findings of one kind | 2 | `framework-review/references/procedure.md` |
| The same exemption reason | 3 | The rule that keeps being exempted |

## 4. Write the proposal

If nothing qualifies, say so and stop, writing nothing.

Otherwise write `.harness/state/improve-harness-<yyyy-mm-dd>.md`:

```markdown
# Retro: <start> to <end>

## Pattern 1: <one line>
- Evidence: `<file:line>`: "<short quote>" (one line each, at least the threshold's count)
- Hypothesis: <one sentence>

## Change 1: <one line>
- Target: `<path>`
- Why: pattern 1
- Before: <exact existing text>
- After: <replacement>

## For the user, not harness changes
## Considered and dropped
```

Read each target first. **Quote the existing text exactly.** If you can't quote it, you can't propose
the change. Every pattern cites a real `file:line`. If you can't cite one, drop the pattern.

## 5. Finish

Tell the user the file and the number of proposals. After they have reviewed it, write today's date to
`.harness/state/last-retro.txt`. A change to `.claude/` in a project shows as a local change on the
next update, so a fix that helps every project belongs in the harness repository.

## Boundaries

- Reviewing one change → framework-review
- Building a proposed `check-conventions` rule → a normal, reviewed change, once the user approves it
