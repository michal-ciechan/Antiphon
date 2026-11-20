# CARD-1836 S8b follow-up review, round 3

Subject: `feat/card-task-970e959e` at `38efdbde6c65ba1eed378868dd8b7cb47703c222`. Reviewed range: `82faab82aa639f5cec688c491a965860925a46b0..38efdbde6c65ba1eed378868dd8b7cb47703c222`. It has two commits: red witnesses `75acc49af` and fix `38efdbde6`. Code owner: `970e959e-d489-4425-957d-964a6292c5c7` (Grok). Earlier owner: `2f6279b4-df69-4fa9-9f36-e92438298cf2`. Reviewer: Claude Opus (a different company). Round 2 was review `455131f23651c9670baecf5d470e1c7317bd3389`. This is round 3, the last round, and it reports blockers only.

Verdict: **BLOCKERS (2)**. Round 2's B-1 and B-2 are fixed as claimed. Two coercion routes still keep every carry valid even though the evidence holds a failure signal. B-1 below is a regression introduced by this diff. B-2 below predates this diff, and the new `Get-RcShadowNames` repeats it. Under the round rules, both go to the operator.

## Coercion attack summary

The probe script dot-sources `scripts/lib/rc-shadow-sample.ps1` and calls `Get-RcShadowTrustDecision -Carries @('c1') -CurrentSample roster`. It ran **144 forms**. One form is a mislabelled clean control. It covers:

- **Containers:** `ConvertFrom-Json` PSCustomObject, `ConvertFrom-Json -AsHashtable` (OrderedHashtable), hand-built hashtables, `[ordered]`, ordinal `Dictionary[string,object]`, PSCustomObject with Add-Member, and List/ArrayList.
- **Flag values:** `[false]`, `[true]`, `[]`, `[[false]]`, `[[[[false]]]]`, `null`, `0`, `"false"`, `{}`, `[null]`, `[0]`, a List of false, `bool[]`, a SwitchParameter false, and a PSObject-wrapped true. Each was tried for both flags.
- **Field values:** array, empty, null, number, and object values for outcome, provenance, and chunkId.
- **Identities:** whitespace ids, ids with embedded NUL, and Cyrillic lookalike ids.
- **Duplicates:** duplicate ids in sampled and duplicate rows in results.
- **Unwrapped containers:** results given as a single object, string, false, or 0, and sampled given as a scalar.
- **Flag-name case variants.**
- **Carries:** int, char, bool, hashtable, nested-array, and PSObject-wrapped-string entries.
- **Member-name shadowing:** dictionary keys named `Keys` (the reserved `PSObject` name cannot come from JSON).

| Lib | Wrong answers |
|---|---|
| tip `38efdbde6` | **10 / 144** (all listed below) |
| round-2 base `82faab82a` | 47 / 144 |
| master `7fcfbeaa3` | 135 / 144 |
| tip plus the fix sketch below | 0 / 144 |

Coercion candidates listed from a grep of the lib:

- No `Where-Object`, `Select-Object`, `ForEach-Object`, `[array]`, `-contains`, `-in`, `-match` or `switch` reads input.
- The only `-cmatch` is line 293, and it is guarded by `-is [string]`.
- The `[bool]$` hits at lines 277, 519 and 584 are parameters or outputs, not reads of input.
- The `.Count` uses are on library-owned `List` and `HashSet` objects.
- `@($x)` at lines 363 and 381 enumerates sampled and results. Scalar wrapping there is benign: a scalar row is still checked.
- `.Keys` at lines 30, 54, 240 and 443 is the live defect (B-2).
- The `return ,$value` comma returns at lines 34 and 43 are correct. They are also the cause of B-1, because an empty List no longer collapses to `$null`.

## B-1: an empty non-array collection for sampled or results now keeps carries (regression)

- **Where:** `scripts/lib/rc-shadow-sample.ps1:65-71` (`Test-RcShadowEmptyCollection`), used at `:360` and `:376`. Its effect lands at `:408-409`.
- **Failure:** `Get-RcShadowTrustDecision -Carries @('c1') -Evidence @{ results = [Collections.Generic.List[object]]::new() }` returns `carriesInvalidated=false` with reason `pilot-count`. The same holds for `@{ sampled = [List[string]]::new() }` and `@{ results = [Collections.ArrayList]::new() }`. On base `82faab82a` all three were `empty-results`, which is invalid.
- **Why:** before this diff, `Get-RcShadowField` unrolled an empty List to `$null`, so the `$null -eq` check caught it. Now the List comes back intact, `Test-RcShadowEmptyCollection` recognises only `System.Array`, the loop runs zero times, and the `ids=0, rows=0` branch returns `$null`. The plan defines a present but empty results key as a failure signal. It is reachable through a hand-built hashtable, which the witnesses themselves treat as a valid input form. JSON in both parse modes yields `object[]`, and that path stays correct.
- **Fix:** treat any non-string, non-dictionary `ICollection` whose `Count` is 0 as empty. More robust still: once a sampled or results key is present, return `empty-results` whenever the enumeration produced no ids or rows. Add hand-built List and ArrayList witnesses for both keys.

## B-2: a dictionary key named `Keys` hides flags and evidence keys (pre-existing; repeated in new code)

- **Where:** `@($Object.Keys)` at `:30` (`Get-RcShadowField`), `:54` (`Get-RcShadowNames`, new in this diff) and `:443` (`Test-RcShadowKey`).
- **Failure:**
  - **Row level:** a row parsed by `ConvertFrom-Json -AsHashtable` from `{"sampled":["c1"],"results":[{"chunkId":"c1","outcome":"passed","provenance":"verified","provenanceFailure":true,"Keys":["chunkId","outcome","provenance"]}]}` keeps carries valid. The same happens with `unexpectedSkip:true` and with the case variant `UnexpectedSkip:true`. A hand-built hashtable gives the same result.
  - **Evidence level:** `{"Keys":[],"sampled":["c1"],"results":[{"chunkId":"c1","outcome":"failed",...}]}` keeps carries valid. So does `{"Keys":"x", ... carried c1 row ...}`, because sampled and results become invisible and the evidence is read as absent.
  - **Cause, observed directly:** for an OrderedHashtable from `-AsHashtable`, `$h.Keys` returns the `Keys` entry's value (`chunkId`), while `$h.get_Keys()` returns the real key set.
- **Why:** PowerShell member access on an IDictionary resolves a matching key before the `Keys` property. The closed-flag rule and the case-variant rule from round 2 both read their names from that shadowed list. A `true` flag that is present in the evidence therefore reads as missing, which counts as clean. The same holds at the evidence level for a failed outcome. This is the same class of bug as round 2's B-1, a PowerShell coercion of input. `-AsHashtable` reaches it, and so does any caller that parses evidence that way.
- **Fix:** read keys with `$Object.get_Keys()` (or `$Object.PSBase.Keys`) in all three helpers. Add witnesses for a `Keys` entry at row level and at evidence level, using `-AsHashtable` and a hand-built hashtable.

## Fix sketch (scratch only, not committed)

The sketch applies two changes. Every `@($Object.Keys)` becomes `@($Object.get_Keys())`. `Test-RcShadowEmptyCollection` adds `if ($Value -is [Collections.IDictionary]) { return $false }; if ($Value -is [Collections.ICollection]) { return $Value.Count -eq 0 }`. With those changes the probe gives 0 wrong answers out of 144. The sketch was not run against the checkpoint rows.

## Claimed behaviour verified

- **Round 2's B-1 is fixed:**
  - `[false]`, `@($false)`, `[true]`, `[]`, `[[false]]` and `[[[[false]]]]` fail closed for both flags. This holds for JSON, `-AsHashtable`, hand-built hashtables, `bool[]`, a List, and a SwitchParameter.
  - An array outcome is `missing-outcome`, an array provenance is `provenance-failure`, and an array chunkId is `unreadable-chunk-id`.
- **Round 2's B-2 is fixed:** a chunkId that is missing, null, empty, whitespace, a number, an object, `true`, an array or a nested array is `unreadable-chunk-id`. This holds with sampled present and for a results-only row.
- **Round 2's advisories are fixed:**
  - A-1: a case-variant flag fails closed, including a variant set to false and an ordinal dictionary that holds both cases.
  - A-3: int, char, bool, hashtable and nested-array carries throw `shadow-carry-id`. A PSObject-wrapped string carry is accepted. A single-string `-Carries` binds as one carry.
- **Duplicates and identities:** duplicate sampled ids and duplicate results rows (passed+failed and passed+passed) fail closed. A carried row outside sampled fails closed. A NUL-suffixed id or a Cyrillic lookalike id is a different ordinal identity, so it is dropped when unsampled (by design) and checked when sampled.
- **Absent evidence:** it stays fail-open (F-1 / AbsentEvidenceKeepsCarriesUntilWired). That finding is not raised again.

## Mutation (scratch `git archive` copies of the tip, harness run directly, no build)

| Mutant | Case | Result |
|---|---|---|
| control (unmutated) | NonBoolean..., CarriedChunk..., AbsentEvidence..., EvidenceDerives... | 4/4 exit 0 |
| M1 `return ,$value` → `return $value` (both) | NonBooleanProvenanceFailureInvalidates | exit 1: `[false]` and the array outcome/provenance/chunkId witnesses left carries valid |
| M2 non-string chunkId → `continue` | CarriedChunkResultInvalidatesCarries | exit 1: every non-string-id witness left carries valid (empty and whitespace strings are still caught; the mutant skips only non-strings) |
| M3 remove the `shadow-carry-id` throw | CarriedChunkResultInvalidatesCarries | exit 1: integer and object carries were skipped |

The witnesses can fail. No PC was run; PCs stay pending.

## Checkpoint rows (run `20261120-054213-bbd6`, one checkpoint-tool run, validate `CHECKPOINT SOURCE VALID rows=3`)

| CP | Executed | Passed | Failed | Skipped | Min |
|---|---|---|---|---|---|
| CP-1 RcIncrementalShadowTests | 10 | 10 | 0 | 0 | 10 |
| CP-2 lane guards | 81 | 81 | 0 | 0 | 78 |
| CP-3 DerivedImpactIndexTests | 27 | 27 | 0 | 0 | 27 |

The tool's own summary was: verdict GREEN exit=0, `unlisted: none`, 1 build, wall 10m20s, `sourceState=clean buildSource=verified`. Every row's test count is nonzero and meets its Min.

## Other gates

- check-evidence-diff over `82faab82a..38efdbde6` found 2 commits and 0 violations. Over `d663f5eed..38efdbde6` it found 6 commits and 0 violations.
- `git merge-tree --write-tree origin/master(7fcfbeaa3) 38efdbde6` exits 0 with tree `e10c56c85`, so there are no conflicts.
- The scope gives no input wait, no migration, and no producer-to-recipient queue path. This is a shadow library with no production caller. Windows behaviour is UNOBSERVED, as the plan says.
- Load: the checkpoint `start` was issued with the 1-minute load at 20.95. It had been 15.29 just before. The build lease was granted at once.
