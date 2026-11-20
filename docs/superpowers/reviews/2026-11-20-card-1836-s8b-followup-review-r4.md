# CARD-1836 S8b follow-up review, ordinary round 3 (Final, blockers only)

Subject: origin/feat/card-task-ec86be3a at 98b35649d95a1c90770934eb41563bdc87353262. The Code owner is ec86be3a and the author is Grok.
Range reviewed: 38efdbde6c65ba1eed378868dd8b7cb47703c222..98b35649d95a1c90770934eb41563bdc87353262. That is red e754d392 and fix 98b35649.
Previous review: 70a0aa27 (`docs/superpowers/reviews/2026-11-20-card-1836-s8b-followup-review-r3.md` at f62cfa59). It raised B-1, empty List and ArrayList, and B-2, the dictionary key named Keys.
Reviewer task: 31d3d11d, on server2 Linux.

## Verdict

CLEAN. No blocker and no regression. Round 3's B-1 and B-2 are fixed and witnessed. Every advisory below already exists at 38efdbde, lies outside this diff, and is input to the compiled C# port that the header and plan now require before S6c/S6d.

## Evidence

- **Checkpoint run 20261120-064259-d969 at 98b35649, one run, GREEN exit 0. `validate` reports SOURCE VALID for 3 rows. dirty=0, sourceState=clean, buildSource=verified, slot=granted, unlisted: none.**
  - CP-1 `RcIncrementalShadowTests`: executed 12, passed 12, failed 0, skipped 0 (Min 12).
  - CP-2 lane guards: 81/81, 0 failed, 0 skipped (Min 78).
  - CP-3 `DerivedImpactIndexTests`: 27/27, 0 failed, 0 skipped (Min 27).
- **Unlisted build, named in the plan:** the `Antiphon.DerivedImpactIndex` driver for CP-3. It was built under one build-slot lease, held 5 s, with exit 0.
- **Red at the base:** the tip harness was run against the 38efdbde library. EmptyCollectionFailsClosed and DictionaryKeyShadowFailsClosed are both red with `pilot-count`. AbsentEvidenceKeepsCarriesUntilWired is green.
- **Mutants:** each was a scratch `git archive` copy with no build, run through pwsh.
  - `get_Keys()` reverted to `.Keys` in Get-RcShadowNames only: DictionaryKeyShadowFailsClosed goes red on the row flags.
  - The same revert in Test-RcShadowKey only: DictionaryKeyShadowFailsClosed goes red on the evidence-level forms.
  - The whole empty-collection change reverted, meaning Test-RcShadowEmptyCollection restored to the base bytes and the three new count checks removed: EmptyCollectionFailsClosed goes red.
  - The other cases stay green in all three mutants.
- **Coercion probe:** 142 new forms, with 7 clean controls that must stay valid.
  - At 98b35649, no form failed in the blocker direction inside the diff's family: empty or nested collections, the dictionary member shadows Keys, Count, Values, Item, Length, get_Keys, Contains and PSBase, OrderedDictionary with a `'0'` key or an integer key, SortedList, case-insensitive and ordinal Dictionary, SortedDictionary, ConcurrentDictionary, ReadOnlyDictionary, ExpandoObject, PSObject-wrapped keys and values, ScriptProperty and AliasProperty, HashSet, Enumerable.Empty, Queue, Stack, LinkedList, DBNull, AutomationNull, JSON numbers, JSON nulls inside arrays, `[[]]`, and nesting 200 to 300 levels deep.
  - The probe still found 9 valid-carry results and 1 over-invalidation. All of them are listed under A-1 to A-4. The base 38efdbde gives the same 10 plus 3 evidence-level Keys shadows, which the fix closes. So none of the 10 is a regression.
- **merge-tree** against master dab889e6: clean (tree f0775a43).
- **check-evidence-diff** over d663f5ee..98b35649: 8 commits, 0 entries, 0 violations.
- **Pins:** untouched. The diff changes only the plan, the library, the harness, and the TUnit wrapper.
- **Shadow flags:** trustGranted false, gateCredit false, effectiveMode Full, IncrementalValidation Shadow, and no trust record. Asserted in every probe form and in both new cases.

## Advisories for the C# port (they do not hold the land)

Each has Where, Failure, Why, and Fix. All of them reproduce at 38efdbde.

- **A-1 Case-variant field keys beside the exact key.**
  - **Where:** Get-RcShadowField, Test-RcShadowResultRow, and Test-RcShadowKey.
  - **Failure:** the evidence comes from `ConvertFrom-Json -AsHashtable`, which uses ordinal keys.
    - `{"outcome":"passed","Outcome":"failed",...}` keeps carries valid with `pilot-count`.
    - `"Provenance":"unverified"` beside `"provenance":"verified"` does the same.
    - Evidence-level `"Results":[failed]` beside a clean `"results"` does the same.
  - **Why:** round 2's variant rule covers only the two closed flags.
  - **Fix:** reject a case variant of any known name, at every level.
- **A-2 Duplicate JSON keys.**
  - **Where:** the caller's parse layer.
  - **Failure:** `"provenanceFailure":true,"provenanceFailure":false`, a duplicate `outcome`, or a duplicate `results` reaches the library as the last value. Both ConvertFrom-Json modes behave this way.
  - **Fix:** use a JSON reader that refuses duplicate properties.
- **A-3 Unknown or near-miss keys.**
  - **Where:** Test-RcShadowClosedFlag.
  - **Failure:** a BOM-prefixed `provenanceFailure` key, or one with a trailing space, holding true is ignored, and a missing flag counts as clean.
  - **Fix:** use a strict schema that rejects unknown keys on result rows.
- **A-4 ConvertFrom-Json DateTime coercion.**
  - **Failure:** an ISO-8601 chunk id becomes a DateTime, so a clean row fails closed as missing-outcome. This over-invalidates, which is the safe direction.
  - **Fix:** read ids as strings.
- **A-5 Count checks after enumeration are not witnessed.**
  - **Where:** the `$ids.Count -eq 0` and `$rows.Count -eq 0` checks in Get-RcShadowSampleEvidenceFailure.
  - **Failure:** no harness form needs these checks. The ICollection rule in Test-RcShadowEmptyCollection catches every witnessed form first. HashSet<T>, Enumerable.Empty, and ExpandoObject do depend on the checks. The probe shows those forms fail closed, but no case would catch the checks' removal.
  - **Fix:** in the port, use one emptiness rule over typed lists.
- **A-6 Remaining PowerShell coercion classes seen.** Strict types remove all of them.
  - `-is [pscustomobject]` is true for any PSObject-wrapped value.
  - ScriptProperty and AliasProperty values are computed on each read.
  - Non-string dictionary keys are skipped silently.
  - The reserved member names PSObject and pstypenames, and a BOM at the start of the text, make ConvertFrom-Json throw. That throw fails closed at the caller.
  - Absent evidence stays fail-open by design (F-1) and is not raised again here.

PCs stay pending for SourceLanding Mutation; mutation is operator-waived (2026-11-12).
