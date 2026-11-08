# Test case design playbook

Adapted from the test case design method of Playwright POM Harness (MIT, see `NOTICE.md`),
rewritten for the QA records of this harness.

## Quality considerations

These are prompts for thinking, **not a checklist**. Use one only where the requirement gives a
reason to, and never add a case just because an area exists.

| Area | Ask |
|---|---|
| Boundaries | Does the source state a limit, a range or a size? Test on, just inside and just outside it. |
| Equivalence classes | Do several inputs behave the same? One case per class is enough. |
| State and transitions | Does the record or screen move through states? Cover the stated transitions and one invalid one. |
| Rules and combinations | Do two conditions combine into one outcome? A decision table finds the cases. |
| Errors | Does the source state what happens on invalid input or a failed dependency? |
| Roles and permissions | Does the source say who may and may not do it? |
| Data integrity | Is something saved, changed or deleted? Check it through the Api or Db too. |
| Accessibility, security, performance | Only where the source sets a target or a rule. |

## Writing a case

- **One purpose per case.** If the title needs "and", split it.
- **Steps are actions; results are observations.** Each expected result is something a tester (or a `Verify*` method) can see: a text, a status, a row, a value. Not "works correctly".
- **Expected results come from the source.** If the source does not state the outcome, do not guess one: raise a question on the requirement instead, and leave the case out until it is answered.
- **Data is concrete** but never a real secret. A value that must be unique per run says so; the test-data skill turns it into `UniqueData`.
- **Prerequisites are state, not steps.** "An order in status Shipped exists" is a prerequisite; the test seeds it through an Api or Db class.
- **Negative cases** state the expected rejection (the message, the status code, the unchanged record).

## Priority

| Priority | When |
|---|---|
| high | A core flow or a High-impact rule: without it the feature has no value, or data or security are at risk |
| medium | A supported variant or a stated error behaviour |
| low | A rare variant, cosmetic, or already covered elsewhere at a lower level |

## Automation

- `planned`: worth automating, not automated yet. The default.
- `automated`: a test carries `[Category("tc:<id>")]`. Set it only when that test exists; `traceability` fails otherwise.
- `manual-only`: say why in the case (visual judgement, hardware, a third party that can't be driven).

## Independent assessment

After drafting, read the set once as a reviewer who didn't write it:

1. Does every acceptance criterion of each accepted requirement have at least one case?
2. Does any case test something the source does not state? Remove it, or raise a question.
3. Are two cases testing the same class of input? Merge them.
4. Can each case run alone, in any order?

**Zero new cases is a valid result** when the existing ones already cover the requirement.
