# Review follow-ups for browser diagnostics (short design)

Approved 2026-11-21. Closes the four minors the final review of #25 deferred.

## Why

Each one is a gap in the secret safeguards a user relies on: a secret in test data that the rule doesn't see, a rule that gives false results, a masking step that can strain memory, and a masked field that leaves part of its value visible.

## Approaches

Each item had one sensible fix. The only real choice was the escape hatch for `[secret-in-test-data]`:
- **Keep it strict, like `[secret-literal]` (chosen).** A made-up value passes once its field is named for what it is.
- Allow `// conventions-ok`-style exemptions or a baseline. Rejected: JSON has no comments, and a baseline would let a real credential sit in the repository.

## What changes

1. **`[secret-in-test-data]` checks arrays and objects.** A secret-named field whose array or object holds a non-empty string anywhere inside it is one finding, at that field's line. The value is never shown. The rule stays without exemption and baseline. The harness-setup playbook and the test-data skill say so, including that `--write-baseline` refuses while a finding is open, and give the naming fix (`candidatePassword`, `tooShortPassword`).
2. **`[secrets-remembered]` looks for a real call.**
   - **What counts:** a syntax-tree call, either `Redaction.Remember(…)` (qualified or not) or `Remember(…)` imported with `using static`. A call inside a comment doesn't count.
   - **Partial classes:** when `Secrets` is `partial`, its other parts in `.cs` files in the same folder count too.
3. **`MaskZip` streams.**
   - **Pass 1:** each text entry is read and its password-field values remembered, then the entry is dropped.
   - **Pass 2:** each entry is read, masked and written.

   No pass holds more than one entry.
4. **The end of a JSON field's value is found by a scanner, not the regex.**
   - **Nesting depth:** the prefix regex captures the backslashes before the opening quote. That run's length `n` (0, 1, 3, 7) gives the nesting depth.
   - **Where the value ends:** at the first quote whose preceding backslash run `r` satisfies `r ≡ n (mod 2(n+1))`. Any other quote belongs to the value.
   - **If it ends in the wrong place:** the existing check that a masked trace line still parses fails the masking, and the trace is deleted.

## Non-goals

No browser behaviour changes, so there is no end-to-end browser run. Firefox and WebKit stay unverified for masking.

## Left to the builder

Helper names, and how test fixtures are laid out.
