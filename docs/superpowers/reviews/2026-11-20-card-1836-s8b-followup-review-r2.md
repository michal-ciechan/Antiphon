# CARD-1836 S8b follow-up review (round 2, Final, blockers only)

Subject: `feat/card-task-2f6279b4` at `82faab82aa639f5cec688c491a965860925a46b0`. The reviewed range is `ccf2dbb0a77d1f291dc8303fc92506afcc7c6ff4..82faab82aa639f5cec688c491a965860925a46b0`. It has two commits: red witnesses `62d2271e1` and fix `82faab82a`. The Code owner is `2f6279b4-df69-4fa9-9f36-e92438298cf2` (Grok). The reviewer is Claude Opus. Round 1 is review `7d08422440fbc6682f603daaa436f52b98219727`.

Verdict: **BLOCKERS (2)**. Round-1 D-1 and D-2 are fixed for the exact forms they named. Every new witness can fail. Shadow safety has not changed. Two inputs remain that slip past the new rules. In each one, a failure signal, or a non-boolean-false flag, leaves every carry valid. Both inputs pass through lines this diff changed.

## Blockers

- **B-1. A single-element array `[false]` counts as a clean flag.**
  - Where: `Test-RcShadowClosedFlag` reads its value through `Get-RcShadowField`, in `scripts/lib/rc-shadow-sample.ps1`.
  - Failure: a sampled row has `outcome=passed`, `provenance=verified`, and `provenanceFailure` set to JSON `[false]`. `unexpectedSkip: [false]` behaves the same way, and so does `@($false)` in a hashtable. Each one gives `carriesInvalidated=false` and reason `pilot-count`. This holds for `ConvertFrom-Json` and for `-AsHashtable`. A two-element array `[true,false]` and an empty array `[]` do fail closed.
  - Why: `Get-RcShadowField` returns the value with `return $Object[$key]`. The PowerShell pipeline unrolls a one-element array into its element, so `-is [bool]` sees boolean `false`. The plan's D-1 says "a present provenanceFailure that is not boolean false is provenance-failure". An array is not boolean false, so this is a coercion that turns a non-boolean-false value into a pass. The same unrolling also lets `outcome: ["passed"]`, `provenance: ["verified"]` and `chunkId: ["c1"]` through. Those three were already lax before this diff.
  - Fix: read the flag without unrolling. Either make `Get-RcShadowField` return `,$value`, or index the dictionary or property directly inside `Test-RcShadowClosedFlag`. Add JSON `[false]` and `@($false)` cases for both flags to `NonBooleanProvenanceFailureInvalidates`. If `Get-RcShadowField` itself changes, apply the same strict type checks to `outcome`, `provenance` and `chunkId`.
- **B-2. A failed row with a missing or non-string `chunkId` is dropped once `sampled` is present.**
  - Where: the new carried-result loop (`if ($rowId -isnot [string] -or -not $carried.Contains($rowId)) { continue }`) and the per-id match loop in `Get-RcShadowSampleEvidenceFailure`.
  - Failure: the call has `-Carries c1,c2` and evidence `sampled=[s1]`. The results are `s1` passed/verified, plus one row with `outcome=failed` (or `provenanceFailure=true`) whose `chunkId` is absent, `null`, `""`, a number, an object, or a multi-element array. Each one gives `carriesInvalidated=false` and reason `pilot-count`. The same row is rejected when `sampled` is absent, because then every row is checked.
  - Why: D-2's rule is "a carry is not launched, so a row for it is a contradiction", and "a dirty unsampled row that is not a carry stays dropped". A row whose id cannot be read cannot be shown to be "not a carry". Unknown has to be treated as dirty, so dropping a failure signal here is fail-open. The base `ccf2dbb0a` already dropped these rows. The new loop explicitly `continue`s past them, so the gap is in the D-2 fix itself.
  - Fix: once `sampled` or `results` is present, any result row whose `chunkId` is not a non-empty string fails closed. Use `missing-outcome` or a new kind such as `unidentified-result`. Add a witness to `CarriedChunkResultInvalidatesCarries` covering a missing, `null`, numeric and object `chunkId`, using JSON evidence.

## Verified

- Closed-flag rule (`scripts/lib/rc-shadow-sample.ps1` `Test-RcShadowClosedFlag`): a boolean `false` is clean, whether it is a hashtable value, a PSObject-wrapped value, or parsed by JSON into a pso or a hashtable. Each of these fails closed for both flags: the strings `'false'`, `'False'`, `'0'`, `''`, `' '` and `'$false'`; the numbers `0` and `0.0`; `null`, `DBNull` and `AutomationNull`; an empty array; an object; a `[switch]` false; and the JSON forms `"false"`, `0`, `""`, `null`, `[]` and `{}`. `-is [bool]` is used explicitly, and no `-eq` or `-not` coercion path remains. The only exception is B-1.
- Carried rows: a failed or passed row for carry `c2` gives `carried-result` and invalidates `c1|c2`, both with `sampled=[s1]` and in results-only mode. An empty `sampled` gives `empty-results`. A carry id listed in `sampled` follows the ordinary rules: passed is clean, failed is `sample-outcome`, and a duplicate row is `provenance-failure`. That case is reachable and sound. `New-RcShadowProposal` draws the sample from the carryable population (`completelyCarryable`), so a sampled id is a carry that was actually launched. PSObject-wrapped and JSON carry and row ids match.
- Id identity is ordinal everywhere (proposal, roster, carries). `C2` and ` c2` are different chunk ids from `c2`. When unsampled, they are dropped as dirty rows. That is consistent with the design and is not a defect.
- Red witness: at `62d2271e1` (with the lib at `ccf2dbb0a`), `NonBooleanProvenanceFailureInvalidates` and `CarriedChunkResultInvalidatesCarries` exit 1 (FAIL), and `AbsentEvidenceKeepsCarriesUntilWired` exits 0, as the plan claims.
- Review mutants (scratch copies, harness run directly, no build). Mutation is operator-waived, so these are review evidence only:
  - M1, string flag passes: `NonBooleanProvenanceFailureInvalidates` goes red ("string provenanceFailure left carries valid"). The other 4 cases tested stay green.
  - M2, a passed carried row passes: `CarriedChunkResultInvalidatesCarries` goes red ("passed carried row left carries valid").
  - M3, null treated as false: `NonBooleanProvenanceFailureInvalidates` goes red ("null provenanceFailure left carries valid").
  - All mutants were restored, and `cmp` confirms the copy matches the clean lib.
- Shadow invariants are unchanged: `trustGranted`, `gateCredit`, `effectiveMode` and `incrementalValidation` are still literals, no trust record is written, and there is still no production caller.

## Checkpoint evidence (run `20261120-043851-0dd0`, source `82faab82a`, sourceState=clean, buildSource=verified)

| CP | Executed | Passed | Failed | Skipped |
|---|---|---|---|---|
| CP-1 RcIncrementalShadowTests | 10 | 10 | 0 | 0 |
| CP-2 lane guards | 81 | 81 | 0 | 0 |
| CP-3 DerivedImpactIndexTests | 27 | 27 | 0 | 0 |

`validate` gives `CHECKPOINT SOURCE VALID source=82faab82aa639f5cec688c491a965860925a46b0 rows=3`. The tool reports `unlisted: none`. There were two out-of-tool builds, each under its own build-slot lease: the checkpoint tool (15 s), and the derived-index driver that the plan names before CP-3 (2 s, incremental). CP-1 confirms all 10 method names by TRX identity. CP-2 has 81 tests against the plan's minimum of 78.

- `check-evidence-diff` over `ccf2dbb0a..82faab82a` gives commits=2, entries=0, violations=0.
- `git merge-tree --write-tree origin/master(e0f381c48) 82faab82a` is clean (tree `8639af5d8`).
- Pins: the branch touches only the two CARD-1836 plan docs, `scripts/lib/rc-shadow-sample.ps1`, `scripts/test-rc-shadow-sample.ps1` and `RcIncrementalShadowTests.cs`. Master's `tests/test-execution-policy.json` (`8c376b03…`), the live-lane protected inputs (`c0d2a723…` digest set) and `tests/derived-impact-index.json` are untouched by the branch. The index is keyed by class, and no class or script file was added.

## Advisories (to cards, never hold the land)

- **A-1.** A flag key in the wrong case (`ProvenanceFailure: true`) is ignored as absent. An ordinal dictionary holding both `provenanceFailure=false` and `ProvenanceFailure=true` reads as clean. This is consistent with the ordinal key contract and the documented F-1 fail-open. S6b/S6c should reject unknown or case-variant row keys.
- **A-2.** Exact duplicate JSON keys (`"provenanceFailure":true,"provenanceFailure":false`) collapse to the last value inside `ConvertFrom-Json`, before the library sees them. The S6b/S6c evidence reader should parse strictly and refuse duplicate keys.
- **A-3.** A non-string carry entry (for example the integer `2`) is silently left out of the carried set, and `Get-RcShadowCarryIds` throws only on the invalidate path. Validate `-Carries` up front.
- Round-1 A-5 (`$matches` shadowing), A-6 (sampled vs selected binding) and A-7 (results-only over-strictness) still stand as filed.

No PC is defined. Every PC stays pending for SourceLanding Mutation. Windows behaviour is unobserved.
