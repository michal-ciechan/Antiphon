# Review CARD-1728 round 2 (task 3d2eb2ca): SCOPE line repair, diff only

Subject: origin/feat/card-task-b1ceb641 tip 5acde1e2b7ad10791cabea5732f6b9a22cc84506 (Grok Code b1ceb641-7452-4315-be8b-d9812d4818a4; red 8bf38064ed712a9f7084411d0df0a61a9ba76312, fix 5acde1e2b). Previously reviewed SHA ecfb032da692304543eab6b2f4d3db2547c6d7b9 (round 1, task f31c70a6). Reviewed range ecfb032da..5acde1e2b: 7 files, +11/-8. Branch base c7216a06f55fba7dc255702c99217248e1b4a20e. Master 6d5531762633dbb9077739cb451386c6c2131255.
Reviewed on runner mirror /work/worktrees/task-3d2eb2ca (branch feat/card-task-3d2eb2ca at the same tip). Read-only on the subject; the one disk mutant was restored and the tree verified clean.

## Verdict: clean. Round-1 B-1 is fixed, A-1 applied, no regression in the diff.

### Check 1: B-1 fixed
- `server/Bundles/stage-code.md` line 3 now reads `SCOPE: Closed rows plus the named affected integration classes; the Unit lane is nightly/rc. Unit omits delivery, landing, leases, persistence. ...`. The ROUND clause (`closed rows, named classes, not the whole lane`) is unchanged, so the bundle carries one ordinary-scope statement.
- File: 2,444 bytes, 0 non-ASCII bytes, 0 CR (was 2,408). Live caps hold and were run green: `TaskPlatformGuidanceTests` 2,480 (trimmed chars) and `InstructionBundleTests` / `ScopedVerificationInstructionTests` 2,500 (composed text).
- `CheckpointRepeatDocumentationTests` bound 2410 -> 2450: f55d60754 introduced `ShouldBeLessThan(2410, "stage-code-net-shorter-than-code-base")` as a one-time no-growth pin when the repeat template was added (file went 2,410 -> 2,370 in that commit). It is not the argv-headroom safety limit; those are the 2,480 and 2,500 caps above, which the diff does not touch. The pin had 1 byte of headroom at ecfb032da (2,408), so the +36-byte sentence could not fit without cutting other pinned text. 2,450 keeps a tight pin (6 bytes) under the real cap, and the comment records the measurement. Justified; not a loosening of a safety limit.

### Check 2: re-pinned tests and witness
- `ScopedVerificationInstructionTests` C487_G127 (line 19) and C487_G129 (line 36), and `VerificationRoundInstructionTests` line 25, assert `ShouldContain` of the exact new sentence on `InstructionBundleComposer.Compose(ForDelegate(Worker, Code)).Text`, which composes the embedded bundle. G127 keeps `ShouldNotContain("skip the Unit lane")`. Reverting the sentence removes the asserted literal, so each is red by construction. The embedded-bundle (rebuild) mutant stays pending for method-scoped SourceLanding Mutation, as the Code handoff states.
- Witness `PerTaskFinalUnitLaneWitnessTests`: `RequiringPhrases` gains `Unit plus the named affected integration classes`; the scan covers `server/Bundles/stage-code.md` (first TaskFacing path, read from disk). Mutant run with the old sentence restored on disk (no rebuild): 1 failed, 0 passed, single hit `server/Bundles/stage-code.md: Unit plus the named affected integration classes`, lease 2bf91612 waited 0s held 8s. Restored with `git checkout -- server/Bundles/stage-code.md`; `git status --short` empty; file back at 2,444 bytes; HEAD 5acde1e2b.
- stage-review.md A-1: `SCOPE: Re-run the claimed rows and named class checks in one checkpoint-tool run; PCs may remain pending. ...`; the rest of the SCOPE line and the ROUND line (`Final Review reruns the complete ordinary scope, including Interim deferrals`) are unchanged, so Review still reruns claimed rows and named classes. Not weakened. File 2,557 bytes (was 2,563), ASCII, under the 2,580 / 2,600 caps. Pinned verbatim in `TaskPlatformGuidanceTests` lines 97 and 126, both updated.

### Check 3: no regression in the diff
- Only the two bundles and five test files change. `orchestrator.md` and its SHA-256 pin are untouched. A repo-wide grep for the two old sentences finds them only in historical plans and investigations and one retained task report, none on the witness's TaskFacing list (consistent with round 1 item 1).
- Evidence-diff `scripts/check-evidence-diff.ps1 -Repository . -BaseRef c7216a06f... -HeadRef 5acde1e2b...`: commits=4 entries=0 violations=0 (after `git fetch --unshallow`; the mirror was shallow and first returned `shallow_or_unknown_history`). `git merge-tree --write-tree origin/master HEAD` clean, tree e9e620cff8e5dfc816f4db3a4626e09f87e73f17. `git diff --check` clean.
- Code report (task API; the branch carries no `.antiphon/task-b1ceb641.md`): the red commit 8bf38064e added the phrase to the witness while stage-code still had it (1 failed), the fix 5acde1e2b rewords and re-pins; every unlisted run carries a reason and a lease (red 98a9fb7a, fix build 5f9df11c, guards 1797d739, mutant 6b773d70, restore 5088c987). No `### Checkpoints` table exists for this card (`docs/investigations/2026-11-07-debug-unit-shard-completion.md` carries CHECKPOINT lines only), so the selection to match is Code's 18 classes: the round-1 14 plus `ScopedVerificationInstructionTests`, `CheckpointRepeatDocumentationTests`, `CheckpointManifestDocumentationTests`, `StandingPipelinePolicyDocumentationTests`.

## Runs (HEAD 5acde1e2b, tree clean; scripts/build-slot.ps1, native output, UseAppHost=false, TEMP=/tmp)
| Run | Lease | Result |
|---|---|---|
| `dotnet build tests/Antiphon.Tests` | 3b460c65 waited 75s held 484s | 0 errors, 1,804 warnings |
| Green, 16 classes (round-1 14 + ScopedVerificationInstructionTests + CheckpointRepeatDocumentationTests) | 22d1c407 waited 210s held 211s | 192 passed, 0 failed, 0 skipped, 3m26s; TRX run 80add097 |
| Green, CheckpointManifestDocumentationTests + StandingPipelinePolicyDocumentationTests | 258f53f1 waited 15s held 8s | 20 passed, 0 failed, 0 skipped |
| Witness on the SCOPE disk mutant | 2bf91612 waited 0s held 8s | 1 failed (expected), file restored |
| Checkpoint tool CP-1, same 18 classes, cold `bin-r3d2eb2ca/` | build lease 6d0a27be waited 135s held 489s; row lease faf27fb7 waited 15s held 170s | executed=212 passed=212 failed=0 skipped=0, dirty=0, sourceState=clean, buildSource=verified, run 20261107-135122-795a, wall 15m13s; `validate --evidence report.json --expected-source-sha 5acde1e2b...` and `scripts/validate-checkpoint-receipt.ps1` both print CHECKPOINT SOURCE VALID source=5acde1e2b7ad10791cabea5732f6b9a22cc84506 rows=1; outputs deleted bin-r3d2eb2ca/ |

Per class (green runs): C1729LayeringGuardTests 9, CheckpointRepeatDocumentationTests 1, InstructionBundleTests 78, MechanicalPcContractTests 11, MechanicalPcRehearsalTests 10, NativeLinuxPolicyWitnessTests 3, PerTaskFinalUnitLaneWitnessTests 1, RepairSourceDocumentationTests 3, ScopedVerificationInstructionTests 17, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, TaskPlatformGuidanceTests 5, TestClassificationGuardTests 1, TestClassificationPolicyTests 39, VerificationRoundBriefTests 1, VerificationRoundInstructionTests 6, CheckpointManifestDocumentationTests + StandingPipelinePolicyDocumentationTests 20. Total 212, matching Code's 212/0.

PC-1 (formatter sentence `ordinary scope: the whole Unit lane`) and the stage-code SCOPE embedded-bundle mutant stay pending for method-scoped SourceLanding Mutation.

Budget note: the brief asked for one leased run (40 minutes). The checkpoint-tool row was added so the evidence block can assert `reviewedSourceClean: true` under the CARD-0835 rule (validated receipt for the reviewed SHA), which the leased runs alone cannot satisfy; it took the review past the budget.

Logs and TRX (ephemeral scratchpad): /tmp/claude-1654/-work-worktrees-task-3d2eb2ca/3eb1b154-95e2-4cb7-ae27-c9144c0e0f16/scratchpad/{logs,trx}. Checkpoint evidence: /work/worktrees/task-3d2eb2ca/.antiphon/checkpoints/20261107-135122-795a (report.json, report.md, rows/CP-1/run.trx; plan extract .antiphon/review-3d2eb2ca-plan.md).

## Checkpoint receipt (unedited tool output)

```
source: 5acde1e2b7ad10791cabea5732f6b9a22cc84506 state=clean buildSource=verified
CHECKPOINT CP-1 commit=5acde1e2b7ad10791cabea5732f6b9a22cc84506 build=ok filter=/*/*/(PerTaskFinalUnitLaneWitnessTests*)|(VerificationRoundBriefTests*)|(VerificationRoundInstructionTests*)|(ScopedVerificationInstructionTests*)|(TaskPlatformGuidanceTests*)|(InstructionBundleTests*)|(CheckpointRepeatDocumentationTests*)|(CheckpointManifestDocumentationTests*)|(StandingPipelinePolicyDocumentationTests*)|(RepairSourceDocumentationTests*)|(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContract*)|(MechanicalPcRehearsal*)|(C1729LayeringGuardTests*)/* executed=212 passed=212 failed=0 skipped=0 trx=/work/worktrees/task-3d2eb2ca/.antiphon/checkpoints/20261107-135122-795a/rows/CP-1/run.trx slot=granted waited=15s dirty=0 source=5acde1e2b7ad10791cabea5732f6b9a22cc84506 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=15s executed=170.138s wall=185.174s
PHASES CP-1 slotWait=150s build=506.7762179s startup=18.4636777s testsWall=150.263579s teardown=0.5192997s hostWall=169.2466814s
unlisted: none (the tool ran no other build or test command)
wall: 15m13s  sequential-equivalent: 3m05s  builds: 1  max-concurrent-builds: 1  rows: 1 green 0 red 0 skipped
outputs: deleted bin-r3d2eb2ca/
verdict: GREEN exit=0
```
