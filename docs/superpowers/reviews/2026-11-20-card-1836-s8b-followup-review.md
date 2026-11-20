# CARD-1836 S8b follow-up review (round 1, Final)

Subject: `feat/card-task-bdc2e824` at `ccf2dbb0a77d1f291dc8303fc92506afcc7c6ff4`. It has two commits on base `d663f5eed4d22e69752963878602ab1b269e7b2f`: the red witnesses `7288542f4` and the fix `ccf2dbb0a`. The original Code task is `bdc2e824-9d04-4689-9a09-68860816ef60`. The reviewer is Claude Opus and the author is Grok.

Verdict: **BLOCKERS (2)**. In each case a failure signal is present in the sample evidence, but every carry stays valid. A-1, A-2 and A-3 are implemented correctly, and every witness can fail. Shadow safety has not changed. Fail-open on absent evidence is reported separately as item F-1. It is advisory, with a required wiring precondition.

## Blockers

- **D-1. A non-boolean `provenanceFailure` is ignored.**
  - Where: `Test-RcShadowResultRow` in `scripts/lib/rc-shadow-sample.ps1`.
  - Failure: a sampled row has `outcome=passed`, `provenance=verified`, and `provenanceFailure` set to `"true"` or `1`. The row passes, so `carriesInvalidated=false` and the reason is `pilot-count`. This happens with JSON evidence too.
  - Why: the code fails closed only on `provenanceFailure -is [bool] -and $true`. The sibling `unexpectedSkip` is stricter: any present non-boolean value is a failure (`"true"` gives `unexpected-skip`). So a provenance-failure flag that is present in the evidence does not invalidate the carries. That breaks claim A-4.
  - Fix: mirror the `unexpectedSkip` rule. A present `provenanceFailure` that is not exactly boolean `false` is `provenance-failure`. Add `Assert-DerivedFailure` cases for `"true"` and `1`.
- **D-2. A failed result for a carried chunk is ignored when `sampled` is present.**
  - Where: `Get-RcShadowSampleEvidenceFailure`.
  - Failure: the call has `-Carries c1,c2` and evidence `sampled=[s1]`. The results are `s1` passed/verified and `c2` failed/verified. The decision is `carriesInvalidated=false`, reason `pilot-count`. Carry `c2` stays valid even though the evidence records its failure.
  - Why: when `sampled` is present, the function reads only the rows whose id is in `sampled` and drops every other row. When `sampled` is absent, it checks every row. Dropping dirty-chunk rows is correct, because D-8 sends an ordinary dirty failure to closure. A row for an id in `-Carries` is different: a carry is not launched, so a result for it is a roster contradiction, and a failed one is a direct failure signal against that carry.
  - Fix: pass the carry ids into the derivation. Any result row whose `chunkId` is in `-Carries` gives `sample-failed`, with kind `provenance-failure` or a new kind such as `carried-result`. Add a witness for it.

## F-1. Fail-open on absent evidence (advisory, with a required wiring precondition)

- With `-Evidence $null`, `@{}`, a string, or keys in another case (`Sampled`/`Results`), the derivation returns `$null`. A nonempty `-Carries` then stays valid. This holds even with `-CurrentSample roster` or `executed`. The new comment "Missing ... sample evidence fails closed" overstates this: it is true only once a `sampled` or `results` key exists.
- A pilot record without `cchunk` and `stratumCount` falls back to the floor of 30. If the real Cchunk was 6000, D-8 requires N=300. A record with `n=30` and `nExecuted=30` still counts. Every genuine proposal records both fields, so this only matters when the record is incomplete.
- Not a blocker. No production caller exists: only `scripts/test-rc-shadow-sample.ps1` dot-sources the library. `trustGranted`, `gateCredit`, `effectiveMode` and `incrementalValidation` are hard-coded literals, so no input can grant trust, and a carry never skips execution in Shadow.
- Precondition for S6c/S6d (the dispatch and closure wiring): before this library feeds any carry decision, absent or unrecognised sample evidence must invalidate every carry when Carries is nonempty or CurrentSample is `roster` or `executed`. A pilot without `cchunk` and `stratumCount` must be ineligible. Each rule needs a producer-to-recipient witness on real scheduler output.
- V-11 impact if this is fixed in the library now: the `roster` and `unknown` assertions in `MismatchInvalidatesCarries` pin exact reason strings and pass `New-Evidence`, which has no sample keys. They would gain `sample-failed` and turn red, so their expected reasons or fixtures must change with the fix. `NoCarryAndOnePilotDoNotGrantTrust` passes no carries and would not change.

## Verified claims

- **A-1:** `Get-RcShadowSampleCount` computes the ceiling exactly, as `Floor((C*5+99)/100)` in `decimal`. It does not use floating-point. Then it applies `min(C, max(30, ·))` and raises the result to the stratum count, capped at C.
  - Probe results: C=0→0, 1→1, 29→29, 30→30, 599→30, 600→30, 601→31, 620→31, 621→32, 6000→300, 6001→301. With 40 strata: 599 and 600→40, 29→29.
  - This matches D-8 in `2026-11-12-card-1836-incremental-activation-plan.md`.
  - Pilot eligibility requires a whole-number `n` and `nExecuted` (`int` or `long` in 0..int.Max) with planned ≥ minimum, executed ≥ planned, and executed ≥ minimum. A `cchunk` without `stratumCount` is refused. Doubles, strings, negative numbers, cchunk=0 and n=299 at C=6000 are refused. A JSON round-trip of a valid pilot stays eligible.
- **A-2:** the check is `-is [string]` plus `-cmatch '\A[0-9a-f]{64}\z'` on both sides. These are refused: empty, missing, uppercase, 63 or 65 characters, a leading space, and a trailing newline.
- **A-3:** only a boolean `$true` is carryable. An absent value or `$null` gives `false`. The string `'true'`, `1`, `0` and `'false'` throw `shadow-chunk-carryable`.
- **A-4, when a sampled key is present:** these all give `sample-failed` for every carry:
  - an empty or null `sampled` or `results` (including JSON `[]` and `null`), and sampled ids with no rows
  - a missing, empty, null or numeric outcome
  - an outcome in another case or with whitespace (`Passed`, `PASSED`, ` passed`, `Skipped`)
  - `provenance` other than exactly `verified`
  - a non-boolean `unexpectedSkip`
  - duplicate sampled ids, duplicate rows for one chunk, and a `chunkId` in another case
  - a `results` object that is not an array, and a scalar `sampled`

  A caller `-SampleFailure` takes precedence and is the preserved failure object. It adds `sample-failed` only once.

## Witnesses and mutants

Each case was run directly through `pwsh scripts/test-rc-shadow-sample.ps1 -Case <name>` from `git archive` scratch copies:

- At `7288542f4`, V-11 passes 3/3. All four new cases fail, for their stated reasons:
  - one sample, 1 of 30, planned below the floor, and 30 at C=601 are counted, with `observed=2`
  - an empty or uppercase digest matches
  - an unknown chunk enters Cchunk (`cchunk=1`, then `2`)
  - every A-4 row leaves carries valid
- At `ccf2dbb0a`, all 7 cases pass.

Each mutant was applied to a scratch copy of the tip library. All 7 cases were run against each mutant, and every mutant was killed:

| Mutant | Red case |
|---|---|
| M1: empty digest matches (`(...)?`) | EmptyDigestIsUnproven |
| M2a: missing outcome treated as passed | EvidenceDerivesSampleFailure |
| M2b: absent result for a sampled chunk ignored | EvidenceDerivesSampleFailure |
| M3: eligibility reduced to `nExecuted >= 1` | OneExecutedSampleIsNotAPilot |
| M4: null carryable treated as true | MissingCarryableIsDirty |
| M5: evidence derivation removed | EvidenceDerivesSampleFailure |
| M6: case-insensitive digest | EmptyDigestIsUnproven |

The scratch copies are under the session scratchpad. No repository file was mutated.

## Checkpoints, guards and land checks

Checkpoint run `20261120-033623-b594` on the plan `2026-11-20-card-1836-s8b-followup-checkpoints.md` at `ccf2dbb0a`: verdict GREEN, and `validate --expected-source-sha` printed `CHECKPOINT SOURCE VALID rows=3`.

| Row | Executed | Passed | Failed | Skipped | Time | Notes |
|---|---|---|---|---|---|---|
| CP-1 RcIncrementalShadowTests | 7 | 7 | 0 | 0 | 32 s | `buildSource=verified` |
| CP-2 guard batch | 81 | 81 | 0 | 0 | 87 s | Plan minimum is 78 |
| CP-3 DerivedImpactIndexTests | 27 | 27 | 0 | 0 | 194 s | |

The CP-2 guard batch is the policy, guard, registry, tripwire, NativeLinux, mechanical contract, platform guidance, layering and lane-category guards.

- `check-evidence-diff` over d663f5eed..ccf2dbb0a: commits=2, entries=0, violations=0.
- `git merge-tree --write-tree origin/master` at `d36d95412` with the subject: clean (tree `bb665194d`). The 31 commits that landed on master since the base do not touch the four subject files or the dot-sourced dependencies (`rc-forced-full.ps1`, `c487-harness.ps1`, `release-gate.ps1`).
- No PC is defined. Mutation is operator-waived, and the mutants above are review evidence only. Every PC stays pending.
- Windows behaviour is unobserved.

## Advisories (to cards, never hold the land)

- **A-5.** `$matches` is used as a local list name inside `Get-RcShadowSampleEvidenceFailure`. That name shadows the automatic regex variable. The code is safe today because nothing in that scope uses `-match`, but the next edit could break it. Rename it to `$hits`.
- **A-6.** The library never binds the evidence's `sampled` set to the proposal's `selected` roster. Detecting a short or substituted roster depends entirely on the caller's `-RosterChanged`. S6c must compare them and must witness that comparison.
- **A-7.** In results-only mode (no `sampled` key), every row is treated as a sample row. So an ordinary dirty-chunk failure becomes `sample-failed`. That is over-strict, not unsafe. S6c should always emit `sampled`.
