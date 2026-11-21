# Review Follow-ups Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the four deferred review minors of #25.

**Architecture:**
- Two `check-conventions` rules in `tools/Harness.Tools/Conventions/ConventionRules.cs`.
- One template class, `templates/framework/src/__Name__.Framework/Utils/Redaction.cs`.
  - Its behaviour is proven by probes in a scratch xUnit project scaffolded from the templates.
  - Its presence is pinned by `ScaffolderTests`.

**Tech Stack:** .NET 10, Roslyn, System.Text.Json, xUnit v3

**Spec:** docs/superpowers/specs/2026-11-21-review-followups-design.md

## Global Constraints

- Test-first. Warnings are errors.
- Rule findings never show a secret value.
- Templates contain only `{{Name}}` placeholders.
- Scripts run on PS 5.1 and 7.
- Never commit.

## Review Focus

1. **A secret-named field whose value is an empty array or object, or holds only numbers or booleans:** no finding.
2. **A nested secret-named field inside an already flagged object:** exactly one finding, for the outer field.
3. **A `Secrets.cs` that declares `partial class Secrets` and calls `Remember` in a sibling file:** no finding. With no call in any part: one finding.
4. **A JSON field value ending in an escaped backslash (`"password":"ab\\"`):** fully masked, and the masked line still parses.
5. **A trace with a binary entry between text entries:** the binary entry is copied byte-for-byte after streaming.

---

### Task 1: `[secret-in-test-data]` arrays and objects, and the docs

**Files:**
- Modify: `tools/Harness.Tools/Conventions/ConventionRules.cs` (`SecretInTestData`)
- Test: `tools/Harness.Tools.Tests/Conventions/ConventionRuleTests.cs`
- Docs:
  - `.claude/skills/harness-setup/references/playbook.md`
  - `.claude/skills/test-data/SKILL.md`

- [ ] **Step 1: Write the failing tests.**
  - Extend Theory `SecretInTestData_ASecretNamedFieldWithAValue_IsRejected_WithoutShowingIt` with two cases:
    - `{ "password": ["Pa55word!"] }` (line 1)
    - a three-line file whose `"token": { "value": "abc1" }` sits on line 2 (line 2)
  - Extend `SecretInTestData_EmptyOrHonestlyNamedValues_AreFine` with three cases:
    - `{ "password": [] }`
    - `{ "token": {} }`
    - `{ "secret": [1, true, null] }`
  - New Fact `SecretInTestData_ANestedSecretInsideAFlaggedObject_IsOneFinding`, with the content `{ "secret": { "password": "x1" } }`, asserts a single finding.
- [ ] **Step 2: Run them.** Expected: the new array and object cases FAIL.
- [ ] **Step 3: Implement the check.** On a `StartArray` or `StartObject` value, walk the tokens to the matching end with `reader.CurrentDepth`. Flag once if any `String` token (not a property name) is non-empty. Consuming the value also stops nested names from being reported twice.
- [ ] **Step 4: Run them.** Expected: PASS. Then run the full suite.
- [ ] **Step 5: Docs.**
  - harness-setup playbook: the bullet on renaming secret-named fields says that this rule and `[secret-literal]` can't be exempted or baselined, that `--write-baseline` refuses while a finding from either is open, and gives the names `candidatePassword` and `tooShortPassword`.
  - test-data skill: one line saying that data rows for password-rule cases name the field for what it holds.

### Task 2: `[secrets-remembered]` syntax check

**Files:**
- Modify: `ConventionRules.cs` (`SecretsRemembered`)
- Test: `ConventionRuleTests.cs`

- [ ] **Step 1: Write the failing tests.** Theory `SecretsRemembered_LooksForARealCall`, checking `FindingsFor(SecretsPath)` for the `secrets-remembered` count:

  | Case | Expected findings |
  |---|---|
  | `// Redaction.Remember(value);` in a comment, no real call | 1 |
  | `using static Acme.Framework.Utils.Redaction;` with `Remember(value);` | 0 |
  | `Utils.Redaction.Remember(value);` (qualified) | 0 |

  Fact `SecretsRemembered_APartialSecretsWithTheCallInASiblingFile_IsFine`:
  - Write `Secrets.cs` as `public static partial class Secrets { public static string Get(string n) => Read(n); }`.
  - Write `src/Acme.Framework/Utils/Secrets.Remember.cs` with a partial part that calls `Redaction.Remember(v)`.
  - Expect 0 findings.

  Fact `SecretsRemembered_APartialSecretsWithNoCallAnywhere_IsRejected`: expect 1 finding.
- [ ] **Step 2: Run them.** Expected: FAIL for the comment case, the `using static` case and the sibling case.
- [ ] **Step 3: Implement `HasRememberCall(SyntaxNode)`.** It is true for an `InvocationExpressionSyntax` whose expression is either:
  - a `MemberAccessExpressionSyntax` named `Remember` whose target's last identifier is `Redaction`
  - an `IdentifierNameSyntax` `Remember`, when the compilation unit has a `using static` ending in `Redaction`

  If `Secrets` has the `partial` modifier, also parse the other `*.cs` files in the same folder with `CSharpSyntaxTree.ParseText` that declare `partial class Secrets`.
- [ ] **Step 4: Run them.** Expected: PASS. Then run the full suite.

### Task 3: `Redaction` streaming and the JSON field scanner

**Files:**
- Modify: `templates/framework/src/__Name__.Framework/Utils/Redaction.cs`
- Test: `tools/Harness.Tools.Tests/ScaffolderTests.cs` (`RealTemplates_Redaction_MasksTheSpikedForms`)
- Probes: a scratch xUnit skeleton in the scratchpad

- [ ] **Step 1: Write the failing tests.**
  - In ScaffolderTests, add the expected strings `"EndOfValue("` and `"RememberPasswordFields(ReadText("` to the Redaction case.
  - Probes, in the scratch skeleton's tests:

    | Probe | Input | Expected |
    |---|---|---|
    | `AnEscapedQuoteInsideAValueIsMaskedToo` | `"password":"ab\"cd"` | `"password":"***"` |
    | `AnEscapedQuoteInANestedBodyIsMaskedToo` | `\"password\":\"ab\\\"cd\"` | `\"password\":\"***\"` |
    | `AValueEndingInABackslashIsMaskedWhole` | `{"password":"ab\\"}` | `{"password":"***"}`, which still parses |
    | `ABinaryEntryIsCopiedUnchanged` | zip with `trace.trace`, then `a.jpeg` (`FF D8 00 01`), then `trace.network` | the jpeg bytes are equal and both text entries are masked |

- [ ] **Step 2: Run them.** Expected: ScaffolderTests and the three escaped-quote probes FAIL. The binary probe may already pass; it pins the streaming rewrite.
- [ ] **Step 3: Implement.**
  - `JsonField` becomes a prefix-only regex: `(?<esc>\\*)"(?:names)\k<esc>"\s*:\s*\k<esc>"`.
  - `MaskJsonFields(text)` loops over the matches and calls `EndOfValue(text, start, n)`. That applies the spec's `r ≡ n (mod 2(n+1))` rule and returns -1 when there is no end, in which case the text is left as it is. Values are replaced through a `StringBuilder`.
  - `MaskZip` makes two passes over `ZipFile.OpenRead`: `RememberPasswordFields(ReadText(entry))` per entry, then mask and write per entry.
- [ ] **Step 4: Run them.** Expected: all probes and the earlier 19 probes PASS, and the tool suite passes.

### Task 4: Verify

- [ ] Run validate-skills, the full tool suite and `dotnet build Harness.slnx` with 0 warnings.
- [ ] Scaffold fresh nunit, xunit and mstest skeletons. Each must build with 0 warnings, pass its greeting test and pass check-conventions.
- [ ] Final review by a fresh reviewer.
