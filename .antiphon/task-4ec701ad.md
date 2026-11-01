# Final Review 4ec701ad, round 3: failed-first + seed regroup (rc top-up 52 gate)

**Verdict: CLEAN. Route: land (merge to rc top-up 52 with 0dbadd9ef).**

Round 2's D-1 (`seed-plan-regrouped`) is fixed. A regrouped plan now keeps failed-first order. I proved it end to end with the real packer, a 1,000-case property test and eight mutants. The full plan sweep, the land graph, evidence-diff and merge-tree are all green. There are no behaviour defects. One new test-adequacy gap (A-9) and two order details (A-8, A-10) are advisories.

**Subject.** `origin/feat/card-task-f7273caf`:
- Report commit (reviewed tip): `3360b94d19ba57274e8d58d21e26941d6c915e43`.
- Code commit: `e0fca48698a4e0e17c646b69e28ebfc353a6b579`.
- Red-test commit: `9b7fae60b070caf75efd0bdcfd2e1c344264dc22`.
- Base: `d92701adb`.

The product delta is only the deletion of the regroup throw and its two helpers (`scripts/lib/nightly-stopped-run-seed.ps1`), plus comments. The tests are `scripts/test-nightly-stopped-seed.ps1` and one `[Test]` line in `RcStoppedRunSeedTests.cs`.

**Host.** server2/Linux. The standing caller authority covered everything I did, and nothing needed approval. I pushed nothing except this report on `feat/card-task-4ec701ad`, and did no restart.

## Defects

None.

## Requested verifications

### (1) End to end through the real packer

The probe is `scratchpad/e2e.ps1` with `probe-lib.ps1`. It loads the tip's real libs and goes through:
1. `ConvertTo-NightlyProfileLaunchGroups`
2. `Read-NightlyStoppedRunSeed` (a real envelope and hash)
3. `Resolve-NightlyPriorFailures`
4. `New-NightlyDispatchSchedule`

**Plans.**
- Old plan: 188 chunks (antiphon 142, e2e 37, agents-pty 6, markdown-pdf 3). It includes real oversized classes, so there are real `all-split-N` chunks that carry `nodes`.
- New plan: +151 methods, 4 new classes, 2 removed classes. The result is 190 chunks.
- Same chunk id with the same classes: 94. Same id with a changed class set: 94. New ids: 2.

**Seeds.** Two shapes, each with failed, unfinished, never-run and passed classes:
- "finished" (shaped like d47848cc): every chunk carries a timestamp, and 6% are unfinished because of not-executed rows.
- "stopped": each lane is cut at 70%, with one torn chunk and an untimed tail.

**Results.** Both shapes give `operator-seed-order` with 190/190 unique entries. Two calls give identical output.

| Seed | P0 | P1 | P2 |
|---|---:|---:|---:|
| finished | 40 | 29 | 121 |
| stopped | 31 | 70 | 89 |

- The priority sequence never decreases. Each chunk's `priorFailure` flag equals the class oracle.
- Within P1, never-run (untimed) chunks come before timed ones.
- **0 violations.**

### (2) Specific rules

| Rule | Result |
|---|---|
| Failed class split across new chunks | `Antiphon.E2E.LandT0901Tests` is split by the real packer into `e2e/all-split-1..10`. All 10 chunks are P0 and lead, in both shapes. |
| Mixed chunk (failed + passed classes) | 3 (finished) and 10 (stopped) mixed chunks lead at P0. None is demoted. |
| Old failed chunk id that now holds only non-failed classes | Does not lead: 2 and 5 cases, wronglyLeading 0. |

### (3) Identity and tamper (re-check)

**Falls back to the exact canonical order:**
- sha blank → `seed-candidate-missing`
- sha `XYZ` or 40 uppercase hex → `seed-candidate-malformed`
- run id blank → `seed-run-missing`
- marker with a trailing space or different case → `operator-seed-kind-foreign`
- marker missing → `previous-run-plan-digest-missing`
- normal record forged with seed shape → `previous-run-plan-digest-missing`

**Accepted (seed order):** a foreign but well-formed sha, and a foreign run id. Round 2 reached the same conclusion: there is nothing to compare them against, and the binding is contentHash plus candidate id at ingest.

**Refused at ingest:** sha or run id edited without a rehash → `seed-hash-mismatch`.

### (4) Property test and mutants

**Property test** (`scratchpad/prop.ps1`): 4 shards × 250 = **1,000 cases, 0 failures**. Seeds 20261101..20261104.
- Inputs: 1–4 random lanes, real-packer old and new plans (including oversized splits), random method additions and class add/remove, seed chunks dropped or seed-only, random started/completed/timestamps/failures. 35% of cases were corrupted with the 10 variants above.
- Coverage: 689 valid cases were checked against the oracle: permutation, determinism, failed-first, P1 before P2, untimed before timed, and `priorFailure` matching the oracle. 311 fallback cases each had to match the exact canonical order. There were 1,677 P0 split chunks and 641 P0 multi-class chunks.
- Reasons: operator-seed-order 689, seed-candidate-malformed 91, previous-run-plan-digest-missing 78, operator-seed-kind-foreign 58, seed-candidate-missing 45, seed-run-missing 38, seed-plan-unmapped 1.

**Mutants.** Each was one exact-once replacement in a git-archive copy. The worktree was never edited.

| Mutant | Committed SS cases | My e2e / property (40) |
|---|---|---|
| M1 P0 demoted for split (node-bearing) chunks, `:471` | **survived all 16 SS cases** (A-9) | killed / killed (5 fails) |
| M1b split class flags only its first chunk (`nightly-dispatch.ps1:103`) | killed (RealPackerRegroup) | killed / killed |
| M2 mixed chunk demoted (`priorFailure -and classes.Count -eq 1`) | killed (RealPackerRegroup) | killed / killed (20) |
| M3 never-seen class treated as passed | killed (RealPackerRegroup, FailedFirstMapping) | killed / killed (12) |
| M4 regroup check restored (the `d92701adb` file) | killed (RealPackerRegroup, DigestGuard) | killed / killed (27) |
| M5 failed-first sort reversed | killed (4 of 5 cases) | killed / killed (28) |
| M6 untimed sorts after timed | killed (RealPackerRegroup, FailedFirstMapping) | killed (stopped shape only) / killed (16) |
| M7 split-chunk node-UID coverage emptied (`nightly-failure-history.ps1`) | SS survived; **killed by FF EvidenceUnion.allSplits**, RefusesIncompleteEvidence, OrdinaryFailedChunk (in CP-1) | killed |

The tip control was green: 5/5 SS cases, e2e 0 failures, property 40/0.

### (5) Sweep

**Checkpoint run** `20261031-232557-c99d`.
- Plan: `docs/superpowers/plans/2026-10-31-card-1514-stopped-run-seed-layout-plan.md`, copied to `.antiphon/r4e-plan.md` with only `bin-d63-` changed to `bin-r4e-`.
- Flags: `--serial --row-timeout 90m --total-timeout 180m --expected-source-sha 3360b94d1…`.
- Result: **GREEN exit 0.** `validate`: `CHECKPOINT SOURCE VALID source=3360b94d19ba57274e8d58d21e26941d6c915e43 rows=6`. Every row is `sourceState=clean buildSource=verified dirty=0`.
- Wall 46m15s, 4 builds, `unlisted: none`. The tool deleted its outputs.

| Row | Executed / passed / failed / skipped |
|---|---|
| CP-1 seed + chain + neighbours | 130 / 130 / 0 / 0 |
| CP-2 CapFitsAdmittedPlannedCost | 1 / 1 / 0 / 0 |
| CP-3 LedgerAndReceiptBytesAreUnchanged | 1 / 1 / 0 / 0 |
| CP-4 runner witness | 5 / 5 / 0 / 0 |
| CP-5 pty witness | 3 / 3 / 0 / 0 |
| CP-6 agents-pty witness | 5 / 5 / 0 / 0 |

Inside CP-1, by TRX:
- RcStoppedRunSeedTests: 17 Passed, including `RealPackerRegroupKeepsFailedFirst` and `DigestGuardLeavesSeedOrder`.
- RcRosterRepairTests: 3 Passed.
- The census cases C1404_Group10MeasuredBudget, C1577_Group71MeasuredBudget, C1584_E2EExpandedBudget, C1410_MeasuredCostGroups and G2865_Group28Group65MeasuredBudget each passed.

**Other gates.**
- Land graph `-Check`, in a detached scratch worktree at the tip with a leased Release `bin-land-audit` build: `approved`, `count=69`, `digest=E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806`, phase pass2. No `-Update`.
- `check-evidence-diff`: `4390c697e..3360b94d1` gives commits=6, entries=2, violations=0. `e0d49d221..3360b94d1` gives commits=77, entries=18, violations=0.
- merge-tree onto `origin/rc/2026-10-27` (`e0d49d221`, an ancestor of the tip): exit 0, tree `8261b0478660475b46e11778caee8f1a706736cc`, equal to the tip's tree.
- Tip plus the tree-kill fix `0dbadd9ef`: merge-tree is clean, tree `6b0fa96a289c9b1c49bc901c42043de8fe6d2342`. 0dbadd9ef's base is `fc064a682`, not current rc, so landing picks it rather than merging.
- Windows CP-15/16 are still owed. PCs stay pending.

## The operator's ordering rule, for the seed we have

The rule, verbatim: "failed first, and the last thing we passed in each lane, so stuff which hasn't finished running in previous run starts run". Later: "Failures first / Oldest things which have not run".

**d47848cc finished.** Every started chunk therefore carries a timestamp, and there is no half-run lane tail. The seed path gives:
1. **P0:** every chunk that holds any of the failed classes (58 failed results, 31 causes), including regrouped, split and mixed chunks.
2. **P1:** chunks not fully completed or not fully seen, oldest first:
   - chunks with any non-terminal TRX row (`nightly-stopped-run-seed.ps1:360`), which is where the **63 not-executed results** go;
   - chunks that gained a class added since `cde8def41`.

   A chunk made only of brand-new classes has no timestamp and sorts first within P1.
3. **P2:** fully passed chunks, oldest first. Within a lane that is the old run order.

So for this seed: failures lead, then what has not run cleanly, then the rest. **No wrong order results.** A per-lane "resume after the last passed" would be a no-op here, because each lane's last passed chunk is its end.

The 63 not-executed results are P1 with a timestamp. The brief called them "never run, class 1". That is right about the priority, but they sort by timestamp, not as untimed. I could not name their classes: the d47848cc TRX and seed are on the desktop and not reachable from server2.

"Affected lanes only since last rc run" is lane selection. It is not part of this ordering path.

**An unfinished-run seed** (my "stopped" shape): P0 failed, then each lane's never-started tail in canonical order (antiphon tail, then e2e tail, and so on), then the torn chunk(s), chunks with new classes and other timed unfinished chunks, then P2. That differs from the normal-path per-lane rotation only in where the torn chunk sits. It runs after every lane's untouched tail instead of first. This is advisory A-8, not a defect, because the torn chunk may be the hang.

## Advisories (cards, none blocking)

- **A-8 (carried, card).** The seed path has no per-lane resume. A timestamped torn chunk runs after every never-started chunk in all lanes.
  - It does not matter for d47848cc.
  - For a future stopped-run seed, consider ordering untimed and torn chunks per lane, after that lane's last passed chunk, matching `Resolve-NightlyPreviousRunOrder`.
- **A-9 (new, test adequacy, card).** The committed split witness (`test-nightly-stopped-seed.ps1:534-560`) builds `split-a`/`split-b` with `nodes=@()`: each holds the whole class. The real packer emits `all-split-N` chunks with disjoint `nodes`.
  - M1 (P0 only for node-less chunks, inserted at `nightly-stopped-run-seed.ps1:471`) survives all 16 SS cases.
  - Behaviour is correct today: split chunks lead in my real-packer probe and in 1,677 P0 split chunks across the property run.
  - The shared class-to-chunk mapping is witnessed (M7 killed by FF EvidenceUnion.allSplits).
  - The seed-order code has no node-dependent branch, so this is a future-regression gap, not a missed branch.
  - **Fix:** give split-a/split-b disjoint `nodes` subsets of Beta, like real split chunks, or add one oversized class to the packer half.
- **A-10 (new, card).** A chunk with any NotExecuted row (for example a `[Skip]`) is never `completed`, so every seed puts it in P1, ahead of passed chunks. That is conservative, but it is permanent elevation for skip-bearing chunks. If the 63 are platform skips, those chunks always run early.

## Unlisted runs, with reasons

- Checkpoint tool build `tools/Antiphon.Checkpoints/bin-r4e-tool/` (leased, 6 s), needed to drive the plan.
- Land audit Release build plus `-Check` in a scratch detached worktree (leased, 7 s). The worktree has since been removed.
- pwsh-only scratch diagnostics with no dotnet driver: the e2e probe, the property test, mutants M1–M7 with a tip control through `test-nightly-stopped-seed.ps1` / `test-nightly-failed-first.ps1`, and the probe scripts.

All `bin-r4e-*` directories are deleted. No `-Update` was run, and the worktree source is unchanged.
