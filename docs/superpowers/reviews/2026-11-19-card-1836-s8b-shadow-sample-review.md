# CARD-1836 S8b shadow sample: Review round 1 (Final)

Subject: `feat/card-task-ad11c378` at `5acf597f121ef3cb53371d193337393627a8d76d`, base `9bcd61113f631ec58f158ff3fb637736fb786fd4` (4 commits).
Original Code/landing owner: `ad11c378-16c7-4e0d-bffd-04e440b33d63`. Reviewer task `3ae1939a`.

Verdict: **CLEAN**. No blockers. Advisories A-1 to A-4 go to cards and do not hold the land.

## Shadow safety

- `scripts/lib/rc-shadow-sample.ps1` is dot-sourced only by `scripts/test-rc-shadow-sample.ps1`. No production script, server, tool or client file references it, the `antiphon-rc-shadow-proposal-v1` format, or any trust record field.
- Every result object sets `trustGranted=false`, `gateCredit=false`, `effectiveMode=Full`, `incrementalValidation=Shadow` and `trustRecordWritten=false` as literals. No input changes them, including two eligible pilots plus an operator record (`trust-transition-disabled`).
- The only writer is `Write-RcShadowProposalFile`. It refuses paths inside the checkout and reparse points on the path, writes with `CreateNew` (write-once), and reads the bytes back. `TrustRecordPath` is never opened.
- The sampled roster is never used to skip anything. There is no caller. The off switch (unchanged `Full/Shadow/ChangeAware` ValidateSets; `Get-ReleaseGateCreditVerdict` refusing `Incremental`) is asserted in `SampleSizeStrataAndReplay`.

## Sampling soundness

- N follows D-8: `min(Cchunk, max(30, ceil(0.05*Cchunk)))` in integer arithmetic, then raised to the number of nonempty `(suite,boundary)` strata. Witness rows: 1->1, 30->30, 600->30, 601->31, 1000->50, and 32 chunks in 32 strata -> all 32.
- One chunk is picked from each stratum first, then the rest are filled. A probe with 40 chunks in one stratum and 1 chunk in another picks the single chunk every time.
- Rank is `SHA-256(seed, purpose, suite, boundary, id)` with an ordinal sort, tie broken by id. It does not use `Get-Random`. Input order does not matter: the witness replays the reversed input. The file has `#requires -Version 7.0`, so PS 5.1 cannot load it and cannot diverge from PS 7. SHA-256, UTF-8 and ordinal comparison are the same on every platform. Windows execution was not observed.
- Bias probe: 400 random seeds over a 40-chunk stratum. Each chunk was picked 261 to 307 times; 290 was expected. Duration and pass fields are dropped before ranking.
- A missing seed uses a 32-byte CSPRNG value. A supplied seed must be 64 lowercase hex characters.

## Mismatch and trust

- A contract mismatch (the S8a forced-full `contract-*` reasons), a sample failure or a changed roster invalidates every carry passed in. Carries are sorted, the failure object is returned as-is, and no expansion to Full occurs. This is D-8's "every carry authorized by the suspect contract".
- A pilot is counted only if it is a Windows cut with `sample=executed`, `shadowPilot=true`, no discrepancy or open failure, current receipts, no overdue Full, and a matching contract digest. Pilots are deduplicated by `cutId`. Fewer than two gives `pilot-count`. Two without an operator record give `operator-trust-required`. With a record the result is still `trust-transition-disabled`.

## Evidence

- Checkpoint run `20261119-223257-e0b4` (plan `docs/superpowers/plans/2026-11-19-card-1836-s8b-checkpoints.md`, all 4 rows) at `5acf597f1`:

| Row | Executed / passed | Failed / skipped |
|---|---|---|
| CP-11 RcIncrementalShadowTests | 3/3 | 0/0 |
| CP-90 guard union | 165/165 | 0/0 |
| CP-91 Pty ProcessSpawnLimitTests | 2/2 | 0/0 |
| CP-92 SessionRunner ProcessSpawnLimitTests | 2/2 | 0/0 |

  Every row reports `sourceState=clean buildSource=verified`. `validate` printed `CHECKPOINT SOURCE VALID source=5acf597f121ef3cb53371d193337393627a8d76d rows=4`. The tool ran no unlisted build or test, and the verdict was GREEN.
- Red at `ec01d5fc2`: all three cases fail on outcome assertions, not harness errors (sample refused, trust granted, reasons `unspecified`). Tip: 3/3 pass.
- Mutants, each run on a scratch copy of the tip:

| Mutant | Caught by |
|---|---|
| M1: sample failure dropped, so a mismatch counts as a match | `MismatchInvalidatesCarries` |
| M2: per-process random salt in the rank, so the sample is not deterministic | `SampleSizeStrataAndReplay` (pinned roster) |
| M3: trust granted at two pilots | `NoCarryAndOnePilotDoNotGrantTrust` |
| M4: platform and sample-state checks removed | `NoCarryAndOnePilotDoNotGrantTrust` (`linux observed=2`) |

  Each mutant failed only its intended witness. The PCs stay pending for SourceLanding Mutation.
- `check-evidence-diff`: commits=4, violations=0.
- `git merge-tree` against master `0bcf3d996`: one conflict, in the generated `tests/derived-impact-index.json`. The other four files merge clean. Per `docs/orchestration-loop.md` (regenerate before DerivedImpactIndexTests), the Merge regenerates the file and does not hand-merge it.

## Advisories (cards, not blockers)

- **A-1** Where: `Test-RcShadowPilotEligible` (`nExecuted -lt 1`). Failure: a cut that ran 1 of 30 planned samples counts toward the two pilots. Why: D-8 requires "N executed samples" per cut, and the pilot record does not carry its planned N. Fix: add `n` to the pilot record and require `nExecuted -ge n` (and `n -ge 30` or the bound plan's N). This does not grant trust today because the transition is disabled.
- **A-2** Where: same function (`$digest -cne $ContractDigest`). Failure: an empty `ContractDigest` matches a pilot whose digest is empty. Fix: require a 64-hex digest on both sides.
- **A-3** Where: `ConvertTo-RcShadowChunk` (`completelyCarryable` absent counts as true). Failure: chunks of unknown carryability enter Cchunk, so sample slots go to chunks that run anyway and fewer carries are actually sampled. Why: "unknown means dirty". Fix: require an explicit bool, or default it to false.
- **A-4** Where: `Get-RcShadowTrustDecision`. Failure: with no contracts and no `SampleFailure`, the result is `carriesInvalidated=false`. The D-8 triggers "unexpected skip, missing outcome, provenance failure" are not inputs; the caller supplies the failure. Fix: when S6c/S6d wire this in, derive `SampleFailure` fail-closed from the executed roster, treating a missing or empty outcome as a failure. Add a witness that an absent sample result invalidates carries.

The plan specifies PowerShell for this slice, not C#, so no direction advisory is recorded.
