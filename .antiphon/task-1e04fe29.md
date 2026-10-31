# Final Review 1e04fe29, round 2: failed-first + stopped-run seed (rc top-up 52 gate)

**Verdict: NOT clean. Route: code.** The marker fix itself works. A valid stopped-run seed now keeps failed chunks first through the digest guard, and every corrupted seed falls back to the complete original order. But the new chunk-membership check that replaced the digest (`seed-plan-regrouped`) throws away the whole seed order when any chunk with the same id now holds a different set of classes. Chunks are bin-packed by per-method cost, so the test changes between the stopped run's sha and top-up 52 will regroup chunks. The fresh Windows run would then silently get canonical order. That is the same outcome as round 1's D-1, reached by a different path (D-1 below, HIGH). There are also two Low witness gaps.

**Subject.** `origin/feat/card-task-91dd821a`. The report commit is `d92701adb882bb15606e1d5ccffcae459d066891`, the code commit is `d1e41d83e45d53803d622d51fcc0ce6d98362a1d` and the red-test commit is `ba665eccf8c1db45af59a8dd6585cb165a96b4d3`. It sits on combined `4390c697e2a7255587264514260d9903fea8d719`, which is on rc `e0d49d221513b467f8c853f47587e688e04255e2`. I reviewed the full tree at `d92701adb`. The scripts are identical to `d1e41d83e`, because the last commit only adds the `.antiphon` report.

**Host.** server2/Linux, with the build slot 4/4 occupied for most of the review. Standing caller authority covered this review, and nothing needed approval. I pushed nothing except this report on `feat/card-task-1e04fe29`.

## Defects

### D-1 (HIGH): `seed-plan-regrouped` discards failed-first order when the plan regroups, which top-up 52 will do

- **Where:** `scripts/lib/nightly-stopped-run-seed.ps1:469-474`, in `Resolve-NightlyStoppedSeedOrder`, added by `d1e41d83e`. The code is `foreach($entry in $Entries){ ... if(-not (Test-NightlySameClassSet $same[0].classes (Get-NightlySeedEntryClasses $entry))){throw 'seed-plan-regrouped'} }`. The catch then returns `entries=@($Entries)`, which is canonical order. All priorFailure partitioning is lost too.
- **Committed pin:** `scripts/test-nightly-stopped-seed.ps1:420` (`DigestGuardLeavesSeedOrder`) asserts `regroup.orderingReason -ceq 'seed-plan-regrouped'`.
- **Why it fires in the real run:**
  - Antiphon chunks are `all-group-N`, built by `ConvertTo-NightlyProfileLaunchGroups` (`nightly-tests-impl.ps1:385-415`). It packs first-fit, largest-first, by `Seconds = per-node cost x node count`, and numbers the bins globally across namespaces.
  - Adding test methods changes class costs. A new class enters some bin and shifts the bins and indexes after it.
  - From the stopped run's sha `cde8def41` to this tip, 151 `[Test]` methods were added across 80 test files. `RcStoppedRunSeedTests` did not exist at `cde8def41`.
- **Witness 1, packer census:**
  - I counted `[Test]`+`[Arguments(` per Antiphon.Tests class at both shas and fed the counts into the real `ConvertTo-NightlyProfileLaunchGroups` with the real cost table.
  - Result: 262 chunks at the old sha and 263 at the new one. 223 chunk ids keep their classes, **39 chunk ids have different class sets**, and 1 id is new.
  - Any one of those 39 fires the throw.
  - Node counts are approximate (no TUnit discovery), but the mechanism does not depend on the exact counts.
- **Witness 2, end to end** (real builder → real ingest → `Resolve-NightlyPriorFailures` → `New-NightlyDispatchSchedule`):
  - Valid seed: correct, failed first.
  - Same seed with one new class added to `antiphon/all-group-90`: `seed-plan-regrouped`, canonical order, failed chunks not first.
  - Same seed with one class moved from group-30 to group-31: same result.
  - The same probe on the seed-only branch `9418c99f2` keeps the failed-first order for both regroup cases. The seed order maps by class, not by chunk id, so it was already robust to regrouping. **`d1e41d83e` introduced this regression.**
- **Why it matters:** the operator's top-up 52 Windows Full run with the d47848cc seed would be accepted. It would log `orderingReason=seed-plan-regrouped` and run canonical order. The failed chunks are not first, and the day is lost. Nothing refuses.
- **Fix (Code, owner 91dd821a):**
  - Delete the `seed-plan-regrouped` throw. Keep the sha/run-id checks. A duplicate suite+chunk is already refused by `Assert-NightlyStoppedSeedContent`.
  - The seed path never uses chunk-id positions, so the digest's job (protecting lane rotation by chunk id) does not apply to it. If a signal is wanted, record regrouped chunk ids as an annotation, not as a fallback.
  - Change line 420 so a regrouped plan expects `operator-seed-order*`, a permutation, and the failed class's new chunk first.
  - Add a witness that regroups through the real packer: add one node to a class and show failed-first survives.
  - Then re-run CP-1.

### D-2 (Low, test adequacy): no witness for the normal-shape guard (`$normal`)

- **Where:** `scripts/lib/nightly-dispatch.ps1:9,13`, the `$seedShape=(-not $normal) -and ...` term.
- **Failure:** mutant M2 dropped `(-not $normal) -and`, which accepts the marker on a normal record. It **survived** all four of `DigestGuardLeavesSeedOrder`, `FailedFirstMapping`, `ScheduleConservesAllChunks` and `InvalidSeedFallsBack`. The committed forged record (`test-nightly-stopped-seed.ps1:398`) has no `chunks`/`failedTests` arrays, so it is never seed-shaped and the `$normal` term is never decisive.
- The behaviour is correct today. In my probe, a normal record with marker, chunks and failedTests gives `previous-run-plan-digest-missing`.
- **Fix:** give the forged normal record `chunks=@(...)` and `failedTests=@(...)` arrays, and expect `previous-run-order` / `previous-run-plan-digest-missing`.

### D-3 (Low, test adequacy): "original order on fallback" is witnessed only without priority members

- **Where:** the seed catch `nightly-stopped-run-seed.ps1:501` and the foreign/missing-marker branches `nightly-dispatch.ps1:26-27`.
- **Failure:** mutants M6a and M6c returned `@($Entries|Sort-Object priorFailure -Descending)`, a partitioned order instead of the original, on those fallbacks. Both **survived**.
- Every fallback assertion builds its schedule without `-PriorityMembers`. `InvalidSeedFallsBack`'s unmapped plan resolves no members, so priorFailure is false everywhere and the sort is the identity.
- M6b, the same mutation on the digest-guard fallback, was killed.
- The behaviour is correct today: my end-to-end probe passes the real members and gets exact canonical order on every fallback.
- **Fix:** build the fallback cases through `Schedule $inputRecord` (with members) and assert the exact canonical sequence.

Owner for D-1 through D-3: 91dd821a Code (the original Code landing owner).

## Requested verifications

### (1) End to end, real layout

The probe script is `scratchpad/p/e2e.ps1`, run against the tip scripts. Layout:
- `logs/2026-10-30-0824-d47848cc`, a run directory with no owner.json, beside `logs/timing-d47848cce896471db1cc8ca346707cad`.
- A producer-shaped `summary.json`: `timingRoot` before an invalid bulk `timingChunks`, plus a decoy `selection.timingChunks`.
- 123 planned chunks: antiphon 105, e2e 10 (`all-split-N`), agents-pty 5, markdown-pdf 3, with 2 classes each.
- 91 TRX holding 637 test results. There is one TRX per started chunk, so it is not 840. Only started chunks have a TRX.
- Failed chunks are deliberately not first: antiphon 37, 52 and 79; e2e 3; agents-pty 4.
- Torn chunks (started, no completedAt, open TRX): antiphon 81 and e2e 5. Everything after them never started. markdown-pdf never started.

Export took 3-8 s. The seed has 123 chunks: 89 completed and 5 failed tests.

**Valid seed:** reason `operator-seed-order`, permutation 123/123.
- Order: the 5 failed chunks first (`ant/37 52 79, e2e/3, age/4`, all `priorFailure`).
- Then the never-started chunks in canonical order: ant 82..105, e2e 6..10, mdpdf 1..3. This is where each lane left off.
- Then the torn chunks: ant/81, e2e/5.
- Then the 84 passed chunks.
- Plainly: there is no per-lane rotation. Unfinished chunks with no timestamp come first in canonical order. Torn chunks have a timestamp, so they run after every never-started chunk (A-8).

**Corruptions.** Every case was a permutation. No refusal and no throw anywhere.

| Variant | Result |
|---|---|
| sourceSha malformed (rehashed) | `seed-schema`, canonical |
| sourceSha a different valid 40-hex (rehashed) | **accepted**, seed order. Nothing exists to compare it against: sourceSha is the stopped run's own sha. The binding is the contentHash plus the operator's `-SeedSourceCandidateId`. |
| sourceSha edited without rehash | `seed-hash-mismatch`, canonical |
| sourceRunId a different value (rehashed) | **accepted**, seed order. Same reason: there is no comparand. |
| sourceRunId blank | `seed-schema`, canonical |
| Seed gains a chunk the plan does not have | accepted. The unknown chunk is ignored and order is unchanged. |
| Seed drops a chunk | accepted. That plan chunk becomes never-run (class 1). All 123 conserved. |
| Plan has a chunk the seed never saw | accepted. It is placed in the never-run class, 124/124. |
| Plan lost a chunk the seed names | accepted, 122/122 |
| **Plan regrouped (class added or moved)** | **`seed-plan-regrouped`, canonical (D-1)** |
| Marker with trailing space / different case | `seed-schema` at ingest (envelope check), canonical. At schedule level: `operator-seed-kind-foreign`, canonical. |
| Marker missing | ingest routes the file as normal history: `history-schema: missing sourceAttempt`, canonical. At schedule level: `previous-run-plan-digest-missing`, canonical. |
| Marker forged on a normal record (with chunks/failedTests) | `previous-run-plan-digest-missing`, canonical |
| createdAtUtc 8 d / 13.9 d old | **accepted**, seed order |
| createdAtUtc or sourceObservedAtUtc 14.1 d old | `seed-stale`, canonical |

**The 7-day rule, plainly:** it does not apply to a seed. The 7-day bound is for normal history (`history-stale`) and dispatchProgress (`previous-run-stale`). A seed has its own 14-day window on both `createdAtUtc` (export time) and `sourceObservedAtUtc` (the latest evidence timestamp), with 5 min of future skew (`nightly-failure-history.ps1:719-721`). A seed whose last evidence is 2026-10-31 10:24Z works until about **2026-11-14 10:24Z**. After that the whole run falls back to canonical with a warning (`seed-stale`). It does not refuse.

### (2) Is the replacement check sufficient?

For identity, yes, and the identity check is close to vacuous: sha and run id are only format and presence checks. The contentHash plus the candidate id does the real binding at ingest.

For membership, the check is wrong-headed (D-1). The class-based mapping already conserves every chunk:
- New chunks become never-run.
- Seed-only chunks are ignored.
- Ordering stays a permutation, as shown above and by the property test.

The regroup throw protects nothing and kills the feature.

### (3) Forged marker, worst case

The worst case is **order only**:
- `Resolve-NightlyStoppedSeedOrder` builds `$ranked` with exactly one row per plan entry and sorts it.
- `New-NightlyDispatchSchedule` rechecks permutation (`dispatch-not-permutation`) after any order.
- A seed gives no execution credit. `Resolve-NightlyPriorFailures` refuses a seed in `PriorFailureSweep` with `seed-ordering-only`.
- A marker forged on a normal record stays under the digest guard.

**Property test** (`scratchpad/p/prop.ps1`, seed 20261031): **1,200 cases, 0 failures.**
- Inputs: random 1-4 suites x 1-15 chunks x 1-3 classes; random seeds with missing chunks, extra chunks, regrouping, random started/completed/timestamps, random failed tests; markers exact / trailing space / case / other / missing; sha and run id valid / blank / malformed; 10% forged-normal records.
- Checked: a permutation, deterministic across two calls, exactly canonical on every fallback, priorFailure-first under `operator-seed-order`, never `operator-seed-order` for a non-exact marker or a normal record, and no throw.
- Mix: operator-seed-order 601, operator-seed-kind-foreign 151, previous-run-plan-digest-missing 163, seed-plan-regrouped 72, seed-run-missing 88, seed-candidate-malformed 66, seed-candidate-missing 59.

### (4) Mutants

Scratch `git archive` copies of the tip. Exact-once string replacement, run through `scripts/test-nightly-stopped-seed.ps1 -Case ...`. The worktree was never edited. Controls were green: DigestGuardLeavesSeedOrder, FailedFirstMapping, InvalidSeedFallsBack, ScheduleConservesAllChunks, SummaryPrefixTamper, WindowsAncestorSwapRefused, ScaleExportBound (31 s).

| Mutant | Result |
|---|---|
| M1 marker check dropped (any seed shape → operator-seed) | killed (DigestGuardLeavesSeedOrder) |
| M2 marker accepted on a normal record | **survived 4/4 (D-2)** |
| M3 foreign marker accepted | killed (DigestGuardLeavesSeedOrder; Assert-NightlyStoppedSeedContent also refuses, as `seed-schema`) |
| M4a seed sha check skipped / M4b run-id check skipped | killed / killed |
| M5 unfinished class dropped | killed (FailedFirstMapping, ScheduleConservesAllChunks) |
| M6a seed fallback not original order | **survived 4/4 (D-3)** |
| M6b digest fallback not original order | killed |
| M6c foreign-marker fallback not original order | **survived (D-3)** |
| D-2 witness: SM2 per-call every-ancestor `Get-Item` | killed: ScaleExportBound did not finish within 300 s (control 31 s). The C# 180 s and harness 150 s bounds turn it red. |
| D-3 witness: SM7 prefix recheck removed | killed (SummaryPrefixTamper) |
| D-4 witness: forced-Windows identity with inode 0 | killed (WindowsAncestorSwapRefused) |

### (5) Sweep

Checkpoint run `20261031-203235-7a20`:
- Plan: `docs/superpowers/plans/2026-10-31-card-1514-stopped-run-seed-layout-plan.md`, copied to `.antiphon/r1e-plan.md` with only the bin names changed (`bin-d63-` → `bin-r1e-`).
- Flags: `--serial --row-timeout 90m --total-timeout 180m --expected-source-sha d92701adb882bb15606e1d5ccffcae459d066891`.
- Verdict **GREEN exit 0**. `validate --expected-source-sha d92701adb...` returned `CHECKPOINT SOURCE VALID rows=6`. Every row was `sourceState=clean buildSource=verified`.
- Wall 88m53s, most of it build-slot waits of 90-645 s per row. Execution 2678 s. 4 builds. `unlisted: none`. The tool deleted the `bin-r1e-*` outputs.

| Row | Executed / passed / failed / skipped | Note |
|---|---|---|
| CP-1 seed + chain + neighbours (RcStoppedRunSeedTests 16, RcFailedFirstPlanner/Execution incl. chain CP-6/CP-7, RcRosterRepairTests, C1514LandingSplitBudgetTests = census C1404/C1410/C1577/C1584/G2865, Antiphon.Tests NativeLinuxPolicyWitness, ...) | 129 / 129 / 0 / 0 | build 372 s, tests 1859 s. Green includes line 420's pin of the D-1 behaviour. |
| CP-2 CapFitsAdmittedPlannedCost (chain CP-5) | 1 / 1 / 0 / 0 | |
| CP-3 LedgerAndReceiptBytesAreUnchanged (chain CP-8) | 1 / 1 / 0 / 0 | |
| CP-4 session-runner ProcessSpawnLimit + NativeLinuxPolicyWitness | 5 / 5 / 0 / 0 | |
| CP-5 pty-host NativeLinuxPolicyWitness | 3 / 3 / 0 / 0 | |
| CP-6 agents-pty ProcessSpawnLimit + NativeLinuxPolicyWitness | 5 / 5 / 0 / 0 | |

The diff touches only scripts, tests and `.antiphon`. No product, csproj or props file changed, so the Unit lane is not affected and this sweep is the full ordinary scope.

Other gates:
- Land graph `-Check` was run in a detached scratch worktree at `d92701adb`, using a Release `bin-land-audit` build. Result: `digest=E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806 count=69`, `approved`. No `-Update`.
- `check-evidence-diff.ps1`:
  - `4390c697e..d92701adb`: commits=3, entries=1, violations=0.
  - `e0d49d221..d92701adb`: commits=74, entries=17, violations=0.
- merge-tree onto `origin/rc/2026-10-27`: rc is still `e0d49d221`, which is an ancestor of the tip, so the merge is a fast-forward. merge-tree is clean, tree `29ae7616b54ba53687fa58f19026e8c5f2ef0991`.
- Windows CP-15/16 remain owed. The D-4 `GetFileInformationByHandle` path has never executed. Its first execution is the operator's Windows seed export, and it fails closed.

## Advisories

- **A-1 (orphan TRX refuses the export).** I agree with leaving it. It fails closed, and d47848cc finished, so nothing appends.
- **A-2 (prefix-close heuristic).** I agree with leaving it. The producer always writes the string `timingRoot` right before `timingChunks`, and the failure mode is closed.
- **A-6 (no Windows lane).** Still open. The Windows file-index branch and backslash `timingRoot` acceptance have never executed. The operator should treat the first Windows export as that test: a refusal there costs minutes, not a wrong order.
- **A-7 (reviewer process notes).** Nothing to act on.
- **A-8 (new, order detail).** Torn (interrupted) chunks run after every never-started chunk, because they carry a timestamp. For d47848cc, the chunk that was running at the stop goes after the untouched tail. That is defensible, because it may be the hang, but it is not "resume after the last passed".

## Unlisted runs, with reasons

- Checkpoint tool build `tools/Antiphon.Checkpoints/bin-r1e-tool/` (lease, 19 s), needed to drive the plan.
- Land audit build plus `-Check` in a scratch worktree. The first lease timed out after 20 min waiting at 4/4 slots. The second succeeded (19 s).
- pwsh-only diagnostics on scratch copies, with no dotnet driver: the packer census, the end-to-end probe, the property test, and 11 mutants plus 2 controls through `test-nightly-stopped-seed.ps1`.

PCs stay pending.
