# Review a7108dd6: CARD-1545 S2b history and reuse policy (round 1 of 3, diff-only)

**Verdict: CLEAN, no defects.** Subject `feat/card-task-5b21d0be` tip `2db78f306716a62279880a030f4263911f877dbb`.
The tested code is `5fd6ec3f2160535be8398c63332cdb8e38655b89`; the tip adds only `.antiphon/task-5b21d0be.md`.
Range reviewed: `740e2a6a2391fec61d52f143f9086d73f2f9782c..5fd6ec3f2160535be8398c63332cdb8e38655b89`
(8 files, +925/-35). Original Code task and landing owner: 5b21d0be.

## Scope gap: the Unit lane did not run

The verification profile's ordinary scope includes the whole Unit lane. The Code task deferred it,
and this review did not run it either. The S2b-group rerun was a second checkpoint run, and with it
this review was already past the 75-minute budget; the Unit lane takes about 67 minutes serial on
server2. The ordinary scope is therefore **Interim**.

The risk is low but not zero. The change is PowerShell plus one new Integration class and seven
rows added to the existing attestation class. No production C#, csproj, policy or shared helper
changed. Repository census guards in the Unit lane could still see the new script or test class.

## Verify priorities

### 1. Fail-safe / no credit: holds

- **No gate credit anywhere.** Every `Get-LiveLaneState` result is fixed at `gateCredit=$false`,
  `effectiveMode='Full'`, `activation='shadow-only'` and `executionEvidence=$false`. The history
  status document and the INVALID error document carry the same fixed values.
- **SATISFIED is allowed only by design.** It is reachable only with `-ReusePolicy Bounded24h`.
  The design allows this (V-16 `same-line-ancestor`, V-19 `equal-all`), and even then there is no
  credit.
- **Each history fault keeps the lane DUE or refuses with exit 2:**
  - An unreadable, linked or oversized record, an unknown `kind`, an invalid receipt, an invalid
    use record or a malformed revocation each gives `history-invalid`.
  - Another lane's receipt gives `foreign-lane`.
  - The same `runId` with different bytes throws `contradictory-receipts` (exit 2).
  - A duplicated identical receipt is harmless.
  - A future-dated receipt beyond the 120 s skew gives `clock-skew` under Bounded24h, and is DUE
    under AlwaysRerun anyway.
  - A dirty receipt with the same fingerprint gives `source-dirty`.
  - A newer RED, INCONCLUSIVE or INVALID receipt gives `newer-*`.
- **Identity and lineage are checked before freshness.** A receipt from a different
  repository, ref, host, account, sha (not an ancestor or missing) or nonce never satisfies.
- **No quarantine rule: acceptable.** One bad file blocks only reuse, which is off by default.
- **The reuse switch is a CLI parameter only.** It is set in `live-lane-status.ps1:7` and
  `Get-LiveLaneState` `live-lane-receipt.ps1:212`, both defaulting to `AlwaysRerun`, and checked
  against a closed set.
  - The context JSON has a strict key set with no policy key.
  - History records carry no policy.
  - The new code reads no environment variable (checked with grep).

### 2. Secrets: holds

- Error tokens are fixed.
- An unknown uid becomes `row-<n>` and `roster-unexpected`.
- An unknown field is named only when it is a plain identifier.
- An invalid record's reasons never reach the status output.
- A use record holds digests, ids and zeros only.
- Receipts are credential-scanned case-insensitively.

### 3. Advisories A-1..A-6: real

| Check | Setup | Result |
|---|---|---|
| A-1..A-5 red | Attestation and fingerprint libraries reverted to `ffac5193`, with the 2db78f30 harness and dll | `LiveLaneAttestationTests`: 3 of 6 methods failed. There were **exactly 6 FAIL rows**, the advisory rows: `linked-document`, `size-cap`, `roster-bound`, `binding-missing`, `probe-time-token` and `credential-case`. |
| A-6 mutant | Both `-cne $executedFact` halves dropped | `ExternalEpochIsNotInferred`: 13 passed, 1 failed. The only FAIL is `executed-host-account`, as the Code task reported. |

The code was restored after both runs.

### 4. Witnesses: green, and the mutants go red

**Checkpoint runs** (the checkpoint tool at `2db78f306`, unedited lines):

```text
CHECKPOINT CP-8 commit=2db78f306716a62279880a030f4263911f877dbb build=ok filter=/*/Antiphon.Tests.Scripts/LiveLaneFingerprintTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-a7108dd6/.antiphon/checkpoints/20261102-141238-b97d/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=2db78f306716a62279880a030f4263911f877dbb sourceState=clean buildSource=verified
CHECKPOINT CP-22 commit=2db78f306716a62279880a030f4263911f877dbb build=ok filter=/*/Antiphon.Tests.Scripts/LiveLaneAttestationTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-a7108dd6/.antiphon/checkpoints/20261102-141238-b97d/rows/CP-22/run.trx slot=granted waited=165s dirty=0 source=2db78f306716a62279880a030f4263911f877dbb sourceState=clean buildSource=verified
CHECKPOINT CP-31 commit=2db78f306716a62279880a030f4263911f877dbb build=reused filter=/*/Antiphon.Tests.Scripts/LiveLaneFingerprintTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-a7108dd6/.antiphon/checkpoints/20261102-141238-b97d/rows/CP-31/run.trx slot=granted waited=90s dirty=0 source=2db78f306716a62279880a030f4263911f877dbb sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=2db78f306716a62279880a030f4263911f877dbb build=ok filter=/*/Antiphon.Tests.Scripts/LiveLaneReceiptTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-a7108dd6/.antiphon/checkpoints/20261102-151024-4e7e/rows/CP-9/run.trx slot=granted waited=90s dirty=0 source=2db78f306716a62279880a030f4263911f877dbb sourceState=clean buildSource=verified
CHECKPOINT CP-32 commit=2db78f306716a62279880a030f4263911f877dbb build=reused filter=/*/Antiphon.Tests.Scripts/LiveLaneFingerprintTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-a7108dd6/.antiphon/checkpoints/20261102-151024-4e7e/rows/CP-32/run.trx slot=granted waited=285s dirty=0 source=2db78f306716a62279880a030f4263911f877dbb sourceState=clean buildSource=verified
CHECKPOINT CP-33 commit=2db78f306716a62279880a030f4263911f877dbb build=reused filter=/*/Antiphon.Tests.Scripts/LiveLaneAttestationTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-a7108dd6/.antiphon/checkpoints/20261102-151024-4e7e/rows/CP-33/run.trx slot=granted waited=555s dirty=0 source=2db78f306716a62279880a030f4263911f877dbb sourceState=clean buildSource=verified
```

- **Run `20261102-151024-4e7e`** (`--after S2b`): GREEN exit 0. Validation printed
  `CHECKPOINT SOURCE VALID source=2db78f30… rows=3`. The fresh TRX names the 6 receipt methods, all
  Passed.
- **Run `20261102-141238-b97d`** (all six rows, serial): exit 5, `hard-wall-deadline-exceeded`.
  - CP-8, CP-22 and CP-31 completed green; their TRX are fresh, with the exact 7, 6 and 7 methods
    Passed.
  - CP-9, CP-32 and CP-33 never got a test slot before the 55 m wall. The host slots were
    saturated (4/4), partly by this review's own mutant leases.
  - Validation refused the run (`cleanup_refused`, outputs kept). Every class it covers is proven
    again in the validated S2b run.

**Mutants.** I ran them through `build-slot`, with one lease per run, on a copy of the CP-8 build
in a scratch worktree at `2db78f30`. The baseline was `LiveLaneReceiptTests` 6/6 green.

| Mutant | Result |
|---|---|
| M1a: the `Get-LiveLaneState` default set to `Bounded24h` | Red, exactly `ReuseIsNotExecution/policy-default`. |
| M1b: the `live-lane-status.ps1` default set to `Bounded24h` | Red, exactly `policy-default`. |
| M2: the foreign-repository and foreign-ref checks removed | Red, exactly `LineageAndHostBound/foreign-repo` and `other-line`. |

All the mutants were restored.

**Not re-verified here.** I did not re-run the "52 red at the stub 8f212505" claim. The witnesses
are judged adequate by reading them and by the mutants above.

**Row timeouts.** The 8 and 10 minute pins hide no hang. The fingerprint class measured 171 to
217 s, the receipt class 60 s and the attestation class 28 to 38 s. Every row finished inside its
pin.

### 5. Master land shape: clean

- I cherry-picked `740e2a6a2..2db78f306` (6 commits) onto `origin/master` `43ae7371d` in a scratch
  worktree. The picks were clean.
- The result tree `76712dce9…` equals `git merge-tree --write-tree --merge-base 740e2a6a2 origin/master 2db78f306`.
- The S2b files are byte-identical to the branch.
- The plain merge-tree onto master shows the expected add/add on the S2a files, because S2a landed
  rebased (`e18a1177d`, `e5d111209`).

### 6. Land graph, evidence diff and rc

- **Land graph.** `audit-land-verification-graph.ps1 -Check` at `2db78f30` gave **approved,
  count=69, digest D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE**.
  - The first attempt refused because evaluating `Antiphon.sln` took longer than the 5 s limit
    under host load. The retry passed.
  - `-Update` was not run.
- **Evidence diff.** `check-evidence-diff -BaseRef 740e2a6a2… -HeadRef 2db78f306…` gave
  commits=6, violations=0.
- **rc.** `merge-tree --merge-base 740e2a6a2` onto `origin/rc/2026-11-01` `10a826b9d` conflicts,
  as expected: rc has no S2a files. The feature is master-only.

## Advisories (no Review rerun needed)

- **Inconsistent invalid-record handling.** In `live-lane-receipt.ps1:243-244`, a receipt is
  ignored silently when its reasons include `roster-changed` and its fingerprint digest differs
  from the candidate's, even when it is also invalid for other reasons (issuer, tamper, lane). This
  is inconsistent with the `history-invalid` rule. It cannot grant anything, because a receipt
  with another fingerprint is never selected.
- **runId echoed without a character class.** `selected.runId` is echoed in the status output.
  It is a free string of up to 256 characters with no character class, while field names are
  restricted. Consider a token pattern for it.
- **History records can be skipped silently.** The history reader uses `GetFiles('*.json')`,
  which is case-sensitive on Linux and not recursive. A record named `*.JSON` is skipped silently.
  The S2c producer should pin the name, or the reader should refuse files that do not match.
- **Unreachable default.** The default at `live-lane-receipt.ps1:333` (`Get-LiveLaneHistoryStatus`)
  cannot be reached from the CLI, because the status script always passes `-ReusePolicy`. No
  witness covers it.
- **Quarantine.** Fail-closed with no quarantine is fine while shadow-only. S2c should give the
  operator a quarantine or revocation path rather than file deletion.
- **Unbound advisory PCs.** The Code report lists unbound candidate PCs for A-1..A-6. They need a
  mutations amendment before SourceLanding Mutation. PC-1..PC-130 stay pending.

## Cleanup

- Deleted the kept `bin-live-fingerprint/` and `bin-live-attestation/`. The tool deleted
  `bin-live-receipt/`.
- Removed the scratch worktrees `lg` and `cpick`.
- Checkpoint evidence stays ignored under `.antiphon/checkpoints/`.
