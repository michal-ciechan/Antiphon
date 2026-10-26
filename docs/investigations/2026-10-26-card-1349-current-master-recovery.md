# CARD-1349 current-master recovery audit

Debug task: 85db4e6b-9bbd-4cf2-9a53-8653ffc10a16. Assigned branch:
`feat/card-task-85db4e6b`. Fetched master and task base:
`7b7aea79b26716654923d79600f0e05cc6c60c29`.
Reviewed source: `ff3ae85d1ab467f0ca557a3a925d9ac7ee86f4ba`, owner
f4fd378a, recovery source c92632b2. Stale request:
`209ecdb7-fb57-4d1f-9720-103f42a5b0d9`.

## Finding

The requested behavior is already on current master. The prior repair is recorded
in `docs/superpowers/plans/2026-10-20-card-1349-cherry-pick-repair.md` and
commit `0c5c3ec3d03c5d10ba927f856733c5c79b1775c3`. The S1-S4 master commits are
`a4207bf702eca3c911c43f4f056e490db484f6ea`,
`89e4c7d664bfa906ed614d4c0071057c64c674d6`,
`71a1809cfec74eebf01b1e27dc25140cbff741ff`, and
`4e66a2c64c2e70c608557ae18b8dd63da64ddab9`. The final reviewed default-observation and
transaction-bound fixture repairs are also present.

Reapplied the complete reviewed server/test delta from common ancestor
`d5dec9555bd726ad11bb30073c1733d6b3e7bb0e` using `git apply --3way`.
After inspecting each conflict and preserving the newer master changes,
the index and working tree had **zero server/test differences** from task base.
This is an integration finding, not a new implementation or a claim that the
old reviewed commit is an ancestor. Thirty of its 31 commits have equivalent
patches on master according to `git cherry`. The remaining S1 patch differs
only in the allowlist context: master already had CARD-1233 entries. Comparing
the two S1 patches confirms the same CARD-1349 production/test changes.

No assertions were edited or removed in this recovery. Current master already
adapts the original consumption/expiry assertions for CARD-1428 and strengthens
the recovery-note delivery assertions. Reinstating the original source verbatim
would undo those subsequent repairs.

## Conflict resolutions

All paths below are relative to the repository. Each resolution keeps the
current-master file in full; all nonconflicting patch applications are no-ops.

| File | Resolution and retained intent |
|---|---|
| `server/Application/Services/AgentTaskDispatcher.cs` | Keep provider-wait injection, ready-queue ordering, reconciliation placement, pending-inspection hold kind, CARD-1428 Peek/invalidate retention across lease refusal, stale-claim reinspection, and ordinary-preflight guard. The asynchronous sibling worker still prevents per-task sibling walks on the serial tick. |
| `server/Application/Services/AgentTaskWorktreeBaseResolver.cs` | Keep CARD-1480 settled-source/debt verification and explicit warnings instead of falling back to stale local tips. The shared bounded inspection and candidate cap remain. |
| `server/Application/Services/CardSiblingInspectionQueue.cs` | Keep CARD-1428 SiblingInspectionResults extraction, Peek/invalidation, in-flight accounting, retained-result cap and owned worker cleanup. Do not restore the obsolete destructive Take/30-second retention implementation. |
| `server/Application/Services/CardSiblingInventory.cs` | Keep bounded unpaid-sync-debt discovery and the SyncDebts snapshot field required by CARD-1480; preserve the independently bounded pending-land query. |
| `tests/Antiphon.Tests/Application/DispatchBaseInspectionDiagnosticsTests.cs` | Keep GitProcessSpawnLimit, the newer git-only lane classification. All diagnostic assertions unchanged. |
| `tests/Antiphon.Tests/Application/DispatchBaseNotificationTests.InspectionDelivery.cs` | Keep ClaudeDialogFixture.CreateBridgeAsync at both sites, preserving guarded Claude delivery observations and all warning/recipient assertions. |
| `tests/Antiphon.Tests/Application/DispatchSiblingInspectionTests.cs` | Keep GitProcessSpawnLimit. Candidate, deadline, ancestor, warning and pending-land assertions unchanged. |
| `tests/Antiphon.Tests/Application/DispatcherSiblingInspectionSchedulingTests.BriefRecovery.cs` | Keep ClaudeDialogFixture, absent-launch observation opt-in, caller Running/live-inventory checks, pending/attempt/hold assertions, actual submitted-body check and Sent/Delivered verdict checks. |
| `tests/Antiphon.Tests/Application/DispatcherSiblingInspectionSchedulingTests.Delivery.cs` | Keep ClaudeDialogFixture.CreateBridgeAsync and all delivery assertions. |
| `tests/Antiphon.Tests/Application/DispatcherSiblingInspectionSchedulingTests.cs` | Keep CARD-1428 retained-result, stale-claim and in-flight-capacity witnesses; keep adopted-checkout newline/path registration repair and CARD-1552 progress-aware saturated-queue drain. These strengthen or adapt the original safety assertions to later behavior. |
| `tests/Antiphon.Tests/slow-tests-allowlist.txt` | Three-way application is already clean and unchanged. Keep all current entries/comments, including the earlier CARD-1233/CARD-1349 reconciliation and later classifications. |

## Verification scope

The Debug brief requests one build and the owner's exact affected-class filters.
Use one alternate-output Antiphon.Tests build through `scripts/build-slot.ps1`,
then a no-build run of the ten exact classes in the owner's CP-34 filter:
DispatchSiblingInspectionTests, DispatcherSiblingInspectionSchedulingTests,
RemoteWorkspaceReadinessTests, DispatchBaseInspectionDiagnosticsTests,
AgentTaskWorktreeBaseResolverTests, AgentTaskDispatchBaseGuardTests,
RemoteWorkspacePreparerTests, DispatchBaseNotificationTests,
DispatchHeldAttentionTests, AgentTaskPipelineStatusTests.
This preserves the original safe dispatch/immutable warning/mirror readiness/
attention invariant. The brief overrides the historical whole-Unit profile;
no Unit or full assembly run is planned. Expected test execution is below the
historical 20-minute estimate; admission wait is recorded separately. Existing
test deadlines are unchanged. Retain TRX and opt-in phase records under
`.antiphon/debug-85db4e6b/`; phases not instrumented remain unknown.

Qualification run `8f055f87` reported native Windows scheduling failures.
Current master already includes CARD-1552's throughput-drain repair and the
adopted-checkout path repair. This Linux recovery neither reverts those changes
nor proves their Windows execution. Native Windows qualification remains
separate; compare the exact failing methods before attributing that run here.

No land, tag, publish, restart or rollout is performed. Original PC-1 through
PC-118 and variants retain their historical pending status. This recovery does
not create new Final/Full Review approval or release qualification.

## Completed verification

Tested commit: `5a978951443d5d1f91c24bb0bf637c2cd0edf309` (audit-only
addition over the fetched master base). All server/test blobs are identical to
base; no source edits occurred during the build or run. Subsequent changes in
this task only append this report. Debian Linux x64, SDK 10.0.403, runtime net9.0.

One build: **0 errors, 1,196 warnings, 3m29.36s**.
`slot=granted waited=0s maxcpucount=6`, held 210s.

One test driver: **437 executed, 437 passed, 0 failed, 0 skipped**;
test-host duration **5m40.632s**, slot held 344s, admission wait 0s.
Fresh TRX confirms exactly the intended ten classes:

| Class | Executed / passed | Failed / skipped |
|---|---:|---:|
| AgentTaskDispatchBaseGuardTests | 96 / 96 | 0 / 0 |
| AgentTaskPipelineStatusTests | 69 / 69 | 0 / 0 |
| AgentTaskWorktreeBaseResolverTests | 67 / 67 | 0 / 0 |
| DispatchBaseInspectionDiagnosticsTests | 12 / 12 | 0 / 0 |
| DispatchBaseNotificationTests | 55 / 55 | 0 / 0 |
| DispatcherSiblingInspectionSchedulingTests | 49 / 49 | 0 / 0 |
| DispatchHeldAttentionTests | 11 / 11 | 0 / 0 |
| DispatchSiblingInspectionTests | 32 / 32 | 0 / 0 |
| RemoteWorkspacePreparerTests | 27 / 27 | 0 / 0 |
| RemoteWorkspaceReadinessTests | 19 / 19 | 0 / 0 |

All ten C1349_InspectionQueueRecovers arguments passed. The relevant saturation
arguments took 1.728s (active limit), 1.754s (total capacity), and 2.724s
(fairness). This does not reproduce the cited native Windows failure on Linux.
The same class has already received later CARD-1428/CARD-1552 and path-assertion
repairs; these were preserved. No Windows claim or unrelated repair is made.

Commands (repository root, each driver individually gated):

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1349-recovery-build -SlotWaitMinutes 15 -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c1349-85db/ --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1349-recovery-tests -SlotWaitMinutes 15 -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c1349-85db/ -- --treenode-filter '/*/Antiphon.Tests.Application/(DispatchSiblingInspectionTests*)|(DispatcherSiblingInspectionSchedulingTests*)|(RemoteWorkspaceReadinessTests*)|(DispatchBaseInspectionDiagnosticsTests*)|(AgentTaskWorktreeBaseResolverTests*)|(AgentTaskDispatchBaseGuardTests*)|(RemoteWorkspacePreparerTests*)|(DispatchBaseNotificationTests*)|(DispatchHeldAttentionTests*)|(AgentTaskPipelineStatusTests*)/*' --report-trx --report-trx-filename owner-cp34.trx --results-directory /work/worktrees/task-85db4e6b/.antiphon/debug-85db4e6b/owner-cp34 --output Detailed
```

The test child inherited ANTIPHON_TEST_PHASE_ROOT pointing to the evidence
`timing` directory, RUN_ID=debug-85db4e6b, GROUP_ID=owner-cp34, ATTEMPT=1,
SUITE=antiphon (all metadata names use the ANTIPHON_TEST_PHASE_ prefix).
Retained evidence root:
`/work/worktrees/task-85db4e6b/.antiphon/debug-85db4e6b/`.
`executed-tests.json` preserves individual argument membership, durations,
native IDs in stdout and outcomes; `classes.json` and `run-manifest.json`
record class counts, source/host identity and exact run times.
`timing/cases-25989-21f55ce4a60e4f9abcc1ac93b7c29f7a.jsonl` holds available
phase spans. Missing spans remain unknown, not zero.

Evidence SHA-256:

| File under evidence root | SHA-256 |
|---|---|
| build.log | ca33a21c020e87b5882ff7ee92307239c1b08d1176b02a32f27ed887839f334b |
| tests.log | 19ad091463b5137e24b8d765f1e2f110f391bcf7dd06ebf6ada7d16049f296bc |
| owner-cp34/owner-cp34.trx | 96ede836009971a04f08533f50edb0a46e2557609bf0cfecf9a61942e3af26a9 |

No additional build, Unit run, whole-assembly run, retry or test mutation was
needed. Review should bind to this branch's exact published tip and assess the
already-present implementation with these fresh ordinary test results. The old
NeedsResolution request does not establish that current master lacks the fix;
the caller can retire or supersede that obsolete request after Review.

2026-10-26: Code task `576ca6bc-adc2-4e4a-82fc-352ff05059a4` exists only
to carry the reviewed docs-only recovery source for CARD-1349 through original
Code landing owner `f4fd378a`. Master already contains the fix, confirmed at
commit `b6db2de3dd0c0bad2ba30823d01882ca6fc9388e`; this task changes no
executable code, tests or allowlist.
