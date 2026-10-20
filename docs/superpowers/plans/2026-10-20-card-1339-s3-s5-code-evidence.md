# CARD-1339 S3-S5 Code evidence

S3-S5 implementation is pushed, but Final verification is incomplete because CP-11 and CP-12 were stopped for the task budget without a completed TRX. New long-lived tags use the attempt's UTC day; promotion requires current seven-script authority and one published reservation; fresh and resumed promotion verify final-tag persistence. Master-only. Restart: none.

Original Code task / landing owner: b44512b9-1427-4cd2-b385-14d29c07ab2c.
Branch: feat/card-task-b44512b9.
Worktree: /work/worktrees/task-b44512b9.
Task base / reviewed S1-S2 tip: f4be7e5494a30a6b32cbac80d5904c75ae3bf072.
Production/test tip used for the Final attempts: 41b8288f68cec30c8f5033a66d80038f6e1808ed.
The following report commit does not alter production/test files or the executed checkpoint manifest; its full SHA is in the caller report.

Plan: docs/superpowers/plans/2026-10-19-card-1339-release-tag-allocator-plan.md.
Unbound Mutation candidate: docs/superpowers/mutations/2026-10-20-card-1339-release-tag-allocator-candidate.md.
Final profile: whole Unit lane, all methods in the four named affected integration classes, all ordinary V/R and local fixture acceptance.

## Behavior and tests

- The publisher samples UTC once per attempt. New long-lived reservations use that day; retries retain their reserved tag across midnight. Legacy stamp allocation and candidate identity/cutUtc stay intact.
- Promotion joins the existing authority and publication gates before preview, creation or recovery. The promoter is the seventh required script hash, with publisher-equivalent Repository/API resolution.
- Promotion requires one correctly typed, published candidate/SHA/pre-tag row with release identity and a consistent final name. The recorder revalidates the same identity, writes atomically, and reads it back. A false, throwing or silently ineffective write returns promotion-journal-write-failed with neither Promoted nor Resumed.
- Real disposable Git recipients prove target A while checkout HEAD is B, annotated final identity, conflicting-tag refusal, same-tag recovery and preservation of pre-release state. File-backed release/card adapters and owned local transports keep all tag/publish/promote operations away from live origin, GitHub and deployment.
- Added exact wrappers: C1339_AllocationUtcDay (5 harness rows), C1339_PromoteAuthorityHash (5), C1339_PromoteReservationAndTarget (22). Upgraded adjacent successful promotion fixtures to current authority and published reservations.

## Historical failures and repairs

CP-5 failed at G-33 utc-day (and midnight wiring), proving the old line-date behavior. CP-6 passed both selected methods. Its first run ended cleanup_refused after retaining CP-5's output: build slot waited 90s and row slot waited 15s. Removed the owned stale output and reran CP-6 exactly once; that run passed and cleaned its output.

CP-7 failed at G-38 promotion-hash-required, with invalid fresh/resumed authority accepted. CP-8 then passed all three required methods.

CP-9 failed decisively at G-43 reservation-journal before the production fix. Missing/empty proof was accepted, including same-final recovery. Historical limitation: after nine harness assertions, a fixture attempted to assign an absent finalTag property and stopped at G-52. That fixture assignment was repaired with Add-Member; this red is not claimed as proof of later guards.

CP-10 executed two methods and both failed. The new unrelated-row oracle sent a deserialized DateTime to a recursive canonical serializer and exhausted the unchanged five-minute harness budget; the adjacent legacy trace oracle also confused an explicit positive refspec with empty-source deletion. The repair uses bounded JSON serialization and retains explicit negative controls for deletion and force-push syntax. No timeout was widened, safety assertion removed, or retry added to hide a failure.

A source inspection then found a real S5 defect: PowerShell unwraps one-element JSON root arrays. CP-13 committed and executed the new wrapped-journal witness before its fix; G-45 failed because promotion was accepted. The induced write also made subsequent no-write assertions fail. All 22 rows completed, including the persistence and recovery cases, in about 91s. The fix checks the raw JSON object boundary before conversion. CP-14 reran both failed CP-10 methods and passed 2/2, with 30 harness rows. These are ordinary defect witnesses, not deliberate Mutation cycles.

Expected-red outputs were removed only after their executors ended, before subsequent builds captured output ownership. No source was edited during any long run. CP-1..CP-4 belong to the reviewed S1-S2 predecessor; this task rechecks their methods in the Final full-class selection rather than rerunning those historical red rows.

## Scope, platform and execution

Read current GET /api/runner-defaults and GET /api/session-runners; no runner or platform was pinned and no fleet location was embedded. Every checkpoint used the committed HEAD as --expected-source-sha and --row-timeout 40m, then wait until terminal (not 75). Final CP-11/CP-12 ran serially. No full-assembly run was made: the declared boundary is the complete Unit lane plus the four affected script integration classes. The plan's 22-minute Final estimate was an estimate, not an outcome.

The only bootstrap build outside a checkpoint row was the plan-documented tool build:
`pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1339-tool-build -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-card1339-tool/ --nologo`.
It passed (4 warnings, 0 errors), slot=granted waited=0s, approximately 7s held. Tool launchers used this output with --no-build; their executors acquired build and test slots. The checkpoint reports list no unlisted build/test driver. Static parsing/ASCII validation passed for all six changed PowerShell files. Receipt/TRX inspection and duration reporting are read-only evidence checks, not additional test runs. During the quiet Unit run, dotnet-stack 10.0.750501 was installed only under /tmp/c1339-b44512b9-diagnostics and sampled the owned test PID twice; this diagnostic was not a build/test driver. It showed waiting managed frames while changing child processes demonstrated continued progress. The ignored samples are .antiphon/c1339-unit-stack.txt and .antiphon/c1339-unit-stack-2.txt.

No loaded repetitions or flake retries were run. The only unchanged successful selection repeated before Final was the explicitly required CP-6 cleanup recovery. Historical outcomes and costs are retained below.

## Operational limits and next stage

Adding the seventh script hash intentionally invalidates old six-file authorities. Recut/requalify through the existing process; do not patch old authority digests. Master landing does not update rc tooling; a separately commissioned and reviewed backport is required before using these changes there. No restart, deployment or live publication occurred.

The Final gate is incomplete: CP-11 and CP-12 need completed runs with adequate time, plus native execution for platform-guarded Unit cases. Next: Code for that missing execution, then initial Review. The caller lands the original Code task only after Review, then commissions method-scoped SourceLanding Mutation. No Mutation control is discharged by ordinary green.

## Incomplete Final Unit gate

CP-11 was started with CP-12 serially in run 20261020-005604-743b on the committed production/test tip. Its isolated build passed in 227.1309101s with verified source provenance. Build and row slots were granted, waited=0s. The run started at 2026-10-20T00:56:08.4226159Z; the Unit driver acquired its slot at 01:00:08.9912052Z.

At 2026-10-20T01:18:10.1345152Z, after roughly 22 minutes of checkpoint wall time and 18 minutes of Unit execution, the owner-checked checkpoint stop command terminated the run to preserve the task's 120-minute budget for the affected integration classes and final reporting. The final wait returned exit=6 (executor ended without phase=done), as expected after the deliberate stop. The executor/driver/test PIDs were confirmed gone. No source was edited; the owned bin-c1339-final-unit output was then removed.

**No completed Unit TRX exists. Executed/passed/failed/skipped totals are unknown, not zero and not passed.** Console output showed Windows-only skips, including delivery/argv/path and native file-sharing/custody cases. The two stack samples and changing owned child processes demonstrated progress; this report does not classify the run as hung or blame an inherited defect. There is no assertion-red attribution from this incomplete run.

Raw evidence: /work/worktrees/task-b44512b9/.antiphon/checkpoints/20261020-005604-743b/state.json, state.history.jsonl, executor.log, builds/bin-c1339-final-unit/build.log, and rows/CP-11/console.log. The state is stopped; its old row state of running is historical, not a live process. No CHECKPOINT result line is invented for this attempt. CP-12 had remained queued and was therefore run separately once.

Outstanding ordinary verification: **CP-11**, the complete `/*/*/*/*[Category=Unit]` selection, with enough execution time and native coverage for platform-guarded cases. The Windows checkout is explicitly unreachable from this delegate and the checkpoint tool has no remote-host execution verb. This requires caller-commissioned Code execution with suitable placement; no sub-delegate was launched. Do not reinterpret the current attempt as Full/Clean or mark its skipped cases passed.

## Final affected-class census

CP-12 did not produce a completed receipt; do not treat its class selection as passed. Historical targeted outcomes remain below.

## Completed checkpoint receipts and costs

Every completed report below was read fresh. Its Markdown SHA-256 matches report.json; start/end SHA and fingerprint match, dirty counts are zero, and the producing build's SHA/fingerprint and buildSource=verified were checked. TRX names/outcomes/counts were checked independently. This statement excludes the stopped CP-11 attempt.

| Run | Row | Executed / passed / failed / skipped | Exit | Slot | Row waited / build waited | Receipt wall minutes |
|---|---|---|---:|---|---|---:|
| 20261019-234436-81cc | CP-5 | 1 / 0 / 1 / 0 | 1 | granted | 0s / 0s | 7.22 |
| 20261019-235228-d736 | CP-6 | 2 / 2 / 0 / 0 | 2 | granted | 15s / 90s | 10.76 |
| 20261020-000448-1737 | CP-6 | 2 / 2 / 0 / 0 | 0 | granted | 0s / 0s | 6.82 |
| 20261020-001251-42f9 | CP-7 | 1 / 0 / 1 / 0 | 1 | granted | 0s / 0s | 4.59 |
| 20261020-001805-4a0c | CP-8 | 3 / 3 / 0 / 0 | 0 | granted | 0s / 0s | 4.84 |
| 20261020-002445-7c22 | CP-9 | 1 / 0 / 1 / 0 | 1 | granted | 0s / 0s | 4.27 |
| 20261020-003134-1bda | CP-10 | 2 / 0 / 2 / 0 | 1 | granted | 0s / 0s | 9.02 |
| 20261020-004340-9df1 | CP-13 | 1 / 0 / 1 / 0 | 1 | granted | 0s / 0s | 5.40 |
| 20261020-004931-d9ad | CP-14 | 2 / 2 / 0 / 0 | 0 | granted | 0s / 0s | 5.37 |

Completed receipt wall total: 58.29 minutes. Add 22.03 minutes for the stopped Unit attempt and approximately 20.4 minutes for the stopped CP-12 attempt and the approximately 7s bootstrap slot. These are receipt/attempt costs, not an end-to-end task stopwatch; authoring and some tool-copy cleanup/control overhead are additional. No historical cost is replaced by a later faster run.

### Unedited CHECKPOINT lines

```text
CHECKPOINT CP-5 commit=ee1acc4874f0fed7e2d90c1e55dc2fa0da85e19b build=ok filter=/*/*/ReleaseTagAllocatorTests/C1339_AllocationUtcDay executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b44512b9/.antiphon/checkpoints/20261019-234436-81cc/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=ee1acc4874f0fed7e2d90c1e55dc2fa0da85e19b sourceState=clean buildSource=verified
```

```text
CHECKPOINT CP-6 commit=ec71dd8bd23d276f5dc1a2ea02efea8880d378d2 build=ok filter=/*/*/ReleaseTagAllocatorTests/(C1339_AllocationUtcDay*)|(C1339_RemoteTagAllocation*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-b44512b9/.antiphon/checkpoints/20261019-235228-d736/rows/CP-6/run.trx slot=granted waited=15s dirty=0 source=ec71dd8bd23d276f5dc1a2ea02efea8880d378d2 sourceState=clean buildSource=verified
```

```text
CHECKPOINT CP-6 commit=ec71dd8bd23d276f5dc1a2ea02efea8880d378d2 build=ok filter=/*/*/ReleaseTagAllocatorTests/(C1339_AllocationUtcDay*)|(C1339_RemoteTagAllocation*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-b44512b9/.antiphon/checkpoints/20261020-000448-1737/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=ec71dd8bd23d276f5dc1a2ea02efea8880d378d2 sourceState=clean buildSource=verified
```

```text
CHECKPOINT CP-7 commit=c07f0835099b15dc9bc657b8cd9a25dfce9cd9d6 build=ok filter=/*/*/ReleaseTagAllocatorTests/C1339_PromoteAuthorityHash executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b44512b9/.antiphon/checkpoints/20261020-001251-42f9/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=c07f0835099b15dc9bc657b8cd9a25dfce9cd9d6 sourceState=clean buildSource=verified
```

```text
CHECKPOINT CP-8 commit=af14dad09f166fb3c9013de8db24d44783c54d65 build=ok filter=/*/*/(ReleaseTagAllocatorTests*)|(ReleaseGatePublicationAuthorityTests*)|(ReleaseBranchModelGateTests*)/(C1339_PromoteAuthorityHash*)|(C599A_ExecutionLedgerJoins*)|(C1311_MovingTipAuthority*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-b44512b9/.antiphon/checkpoints/20261020-001805-4a0c/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=af14dad09f166fb3c9013de8db24d44783c54d65 sourceState=clean buildSource=verified
```

```text
CHECKPOINT CP-9 commit=8036c8b577fd20e8a6b14f55f131ba341f1a06e5 build=ok filter=/*/*/ReleaseTagAllocatorTests/C1339_PromoteReservationAndTarget executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b44512b9/.antiphon/checkpoints/20261020-002445-7c22/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=8036c8b577fd20e8a6b14f55f131ba341f1a06e5 sourceState=clean buildSource=verified
```

```text
CHECKPOINT CP-10 commit=3957dee47c932695cb6f763da48c91971e728f6b build=ok filter=/*/*/(ReleaseTagAllocatorTests*)|(ReleaseBranchModelGateTests*)/(C1339_PromoteReservationAndTarget*)|(C1311_PreReleaseTag*) executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-b44512b9/.antiphon/checkpoints/20261020-003134-1bda/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=3957dee47c932695cb6f763da48c91971e728f6b sourceState=clean buildSource=verified
```

```text
CHECKPOINT CP-13 commit=73edede518b9d2ec2a237eb81ae3fe6a0388269b build=ok filter=/*/*/ReleaseTagAllocatorTests/C1339_PromoteReservationAndTarget executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b44512b9/.antiphon/checkpoints/20261020-004340-9df1/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=73edede518b9d2ec2a237eb81ae3fe6a0388269b sourceState=clean buildSource=verified
```

```text
CHECKPOINT CP-14 commit=41b8288f68cec30c8f5033a66d80038f6e1808ed build=ok filter=/*/*/(ReleaseTagAllocatorTests*)|(ReleaseBranchModelGateTests*)/(C1339_PromoteReservationAndTarget*)|(C1311_PreReleaseTag*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-b44512b9/.antiphon/checkpoints/20261020-004931-d9ad/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=41b8288f68cec30c8f5033a66d80038f6e1808ed sourceState=clean buildSource=verified
```

### Receipt provenance

Raw per-run report.json/report.md/TRX remain ignored under /work/worktrees/task-b44512b9/.antiphon/checkpoints/<run>/; no generated JSON, TRX or logs were committed. The following digests retain the source-clean report binding in this pushed Markdown.

| Run | Actual SHA | Clean source fingerprint | Report Markdown SHA-256 |
|---|---|---|---|
| 20261019-234436-81cc | ee1acc4874f0fed7e2d90c1e55dc2fa0da85e19b | 7ff9524410bd3bf080cdeae104077331c58d3d46ae0afd42783090c80ea99e17 | 10D694823E64FE4A89FA7168AB3ABF0359CC617490A3324A1147CDF470FDB2DB |
| 20261019-235228-d736 | ec71dd8bd23d276f5dc1a2ea02efea8880d378d2 | 0d46d6fb383b16b18e5be0c63ba18f5d75d590284d91ec81cae49b57eff8bf9b | 757C5A8D2D3760A13BDA5E9CDE2B2AC4A3AE5B303E0CF765B9BF4C7F3D8DB2C2 |
| 20261020-000448-1737 | ec71dd8bd23d276f5dc1a2ea02efea8880d378d2 | 0d46d6fb383b16b18e5be0c63ba18f5d75d590284d91ec81cae49b57eff8bf9b | D53E3C0D65FDECBD16CB85B7BCF440234E4D4820AFCF9986CA0C05651997006D |
| 20261020-001251-42f9 | c07f0835099b15dc9bc657b8cd9a25dfce9cd9d6 | 44533c88e280692383d3ec82b4464810b6856c4f398c4a54ab25c71c54fe83e2 | C6CB8A620E2ECDCB17608549265ACB125F133EC70D05101AFE8EAEFACDD0F97B |
| 20261020-001805-4a0c | af14dad09f166fb3c9013de8db24d44783c54d65 | 72c2a67b107c941ac2c28742ae3edbb71a03bb521ee72d1e74f7c194b1dc37d9 | 59591DA157E053ABAF7EF86191434A1781FDDF39C9737A8BB53EE2F16F05E183 |
| 20261020-002445-7c22 | 8036c8b577fd20e8a6b14f55f131ba341f1a06e5 | e5d7a25e436b5842c65ce6538921588d52c1c5336c72e71a7c87f59d0ba37734 | 02438DBAB8CA70498EDE2ED6DD21912F22CDBA6F067C75F669BFCE3173A6C230 |
| 20261020-003134-1bda | 3957dee47c932695cb6f763da48c91971e728f6b | 1a601716e72a3f33400441b35c30e6fa67b534e58e7ddbf6fd94dce12f481f68 | CC23A15C05E296D5612C199955B63F4A842C02946B3DDBBCF1A908228F6FD888 |
| 20261020-004340-9df1 | 73edede518b9d2ec2a237eb81ae3fe6a0388269b | bebbabd2febc918e4e2c40973bfaed9dd5b19d175c494e64756bca429f958aa2 | 69321822C5A80D3FF357464BDB0ABA065D6CB81FE429721AD650F81710BB0A32 |
| 20261020-004931-d9ad | 41b8288f68cec30c8f5033a66d80038f6e1808ed | bf8a5641c833c0342e9766bdd76dad3e6eb2c3fc2ab027afc5af1348d9840306 | D2D8FB3C938CE5D2583E6769B2EE20E02E2A48AF185A4A2F3318FF412CE31BF3 |

## Mutation handoff: every control pending

PC-1: pending; PC-2: pending; PC-3: pending; PC-4: pending; PC-5: pending; PC-6: pending; PC-7: pending; PC-8: pending; PC-9: pending; PC-10: pending; PC-11: pending; PC-12: pending; PC-13: pending; PC-14: pending; PC-15: pending; PC-16: pending; PC-17: pending; PC-18: pending; PC-19: pending; PC-20: pending; PC-21: pending; PC-22: pending; PC-23: pending; PC-24: pending; PC-25: pending; PC-26: pending; PC-27: pending; PC-28: pending; PC-29: pending; PC-30: pending; PC-31: pending; PC-32: pending; PC-33: pending; PC-34: pending; PC-35: pending; PC-36: pending; PC-37: pending; PC-38: pending; PC-39: pending; PC-40: pending; PC-41: pending; PC-42: pending; PC-43: pending; PC-44: pending; PC-45: pending; PC-46: pending; PC-47: pending; PC-48: pending; PC-49: pending; PC-50: pending; PC-51: pending; PC-52: pending; PC-53: pending; PC-54: pending; PC-55: pending; PC-56: pending; PC-57: pending; PC-58: pending; PC-59: pending; PC-60: pending; PC-61: pending; PC-62: pending; PC-63: pending; PC-64: pending; PC-65: pending; PC-66: pending; PC-67: pending; PC-68: pending; PC-69: pending; PC-70: pending; PC-71: pending; PC-72: pending; PC-73: pending; PC-74: pending; PC-75: pending; PC-76: pending; PC-77: pending.

All variants remain pending, including PC-72 publisher/promoter; PC-74 malformed record/short SHA/conflicting duplicate; PC-75 root-object (including a one-element wrapped journal)/version type/supported version; PC-76 scalar reservations; PC-77 mismatched requested final; PC-45 non-object row, wrapped candidate/SHA/tag, nonboolean published, releaseId/releaseUrl/finalTag types; PC-33 offset UTC; PC-34 changing clock; every independent authority variant of PC-39/40; every reservation defect repeated on same-final recovery in PC-53; PC-58/59/60/66 remove/throw/silent/replace persistence faults; each PC-61 recovery cut; PC-63 conflicting writer/before/after push. The plan remains the complete recipe/variant inventory; the unbound candidate maps all IDs to exact methods and source anchors.

No deliberate production mutant, PC red/restore/green cycle or post-land qualification ran. Mutation owns payload binding and remaining missing-control discovery. The inherited/updated estimate is at least 85 cycles and 787.25 Mutation minutes; the ordinary-plus-Mutation estimate after the repair amendment is 863.25 minutes. These are planning estimates, not measurements or passed controls.

## Commits and Git audit

Each meaningful slice was committed before its long run and pushed to the assigned branch. No rebase, amend, reset, force-push, merge or target-branch write occurred.

```text
ee1acc4874f0fed7e2d90c1e55dc2fa0da85e19b test(release): witness UTC publication day through publisher
ec71dd8bd23d276f5dc1a2ea02efea8880d378d2 fix(release): allocate new rc tags on the attempt UTC day
c07f0835099b15dc9bc657b8cd9a25dfce9cd9d6 test(release): witness missing promotion authority admission
af14dad09f166fb3c9013de8db24d44783c54d65 fix(release): require current authority for fresh and resumed promotion
8036c8b577fd20e8a6b14f55f131ba341f1a06e5 test(release): witness unchecked promotion reservation and persistence
3957dee47c932695cb6f763da48c91971e728f6b fix(release): require and verify the published promotion reservation
558acbdddc531427a1de598ee637a3da0dc4e4aa test(release): witness wrapped journal root and repair fixture oracles
73edede518b9d2ec2a237eb81ae3fe6a0388269b docs(release): record S5 repair checkpoint scope and cost
41b8288f68cec30c8f5033a66d80038f6e1808ed fix(release): reject wrapped publication journal roots before parsing
6ec71d427574f3a1b74bfbe2042e0ea9a819d191
```

The final line above is the fetched origin/master SHA; preceding lines are the nine task commits before this report. The implementation tip passed scripts/check-evidence-diff.ps1 over f4be7e5494a30a6b32cbac80d5904c75ae3bf072..41b8288f68cec30c8f5033a66d80038f6e1808ed: commits=9 entries=0 violations=0. A no-tags fetch of master and merge-tree returned exit=0, tree 98b5a397a286e421085c7f24d391dbdea5a08556, with no conflicts. After this report is committed, the complete base..HEAD evidence audit, no-tags fetch/merge-tree and remote-tip check are repeated; their final HEAD is recorded in .antiphon/task-b44512b9.md and the caller report. A clean merge-tree is only Git compatibility, not completion of the missing verification gate.

## CP-12 budget stop and exact next Code scope

Run 20261020-011901-859a built successfully (0 errors, 1107 warnings), source 41b8288f68cec30c8f5033a66d80038f6e1808ed, build and row slot=granted waited=0s. It began at 01:19:05 UTC; the row acquired its slot at 01:24:22 UTC. It was still executing after roughly 15 minutes of test time. A SIGINT sent only after validating the owned TUnit host, command path and ancestor chain did not produce a partial TRX. The ownership-checked checkpoint stop then terminated the tree at approximately 01:39:30 UTC; final wait returned exit=6. No completed TRX or CHECKPOINT result line exists, so its executed/passed/failed/skipped counts are unknown. No assertion failure was established and no inherited-red attribution is made. Raw state, build log, executor log and console remain ignored under .antiphon/checkpoints/20261020-011901-859a/. This second incomplete attempt is not included in the completed-receipt table.

| ID | Actual outcome and remaining Final coverage |
|---|---|
| V-1 | No completed current-tip receipt; inherited S1-S2 Review is not substituted for Final. CP-12 pending. |
| V-2 | Targeted PASS in CP-6 at ec71dd8bd23d276f5dc1a2ea02efea8880d378d2. CP-12 pending. |
| V-3 | Targeted PASS in CP-6 at ec71dd8bd23d276f5dc1a2ea02efea8880d378d2. CP-12 pending. |
| V-4 | Targeted PASS in CP-8 at af14dad09f166fb3c9013de8db24d44783c54d65. CP-12 pending. |
| V-5 | Targeted PASS in CP-14 at 41b8288f68cec30c8f5033a66d80038f6e1808ed. Full class pending. |
| R-1 | No completed current-tip adjacency receipt; CP-12 pending. |
| R-2 | V-2 targeted PASS; C599_TagRecovery current-tip adjacency and full class pending in CP-12. |
| R-3 | Targeted PASS: both UTC-day and remote-allocation methods in CP-6. Final CP-12 pending. |
| R-4 | Targeted PASS: all three required methods in CP-8. Final CP-12 pending. |
| R-5 | Targeted PASS: both required methods in CP-14, 2/2 methods and 30 harness rows. Full classes pending. |

Next Code must run CP-11 and CP-12 from the existing plan with sufficient budget and appropriate native placement, inspect fresh nonzero TRX and all required names, and report every actual V/R outcome. Both IDs are explicitly pending; this remains an incomplete Final round, not an Interim or Full/Clean result. The two budget stops did not change any timeout or assertion. No duration tripwire could run for the Final attempts because neither produced a TRX; that read-only diagnostic remains pending with the completed selections. Initial Review follows completion of ordinary verification. Original Code task b44512b9 remains the landing owner.

