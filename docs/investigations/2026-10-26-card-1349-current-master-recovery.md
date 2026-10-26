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
commit `0c5c3ec3d`. The S1-S4 master commits are `a4207bf70`, `89e4c7d66`,
`71a1809cf`, and `4e66a2c64`. The final reviewed default-observation and
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

Actual commands, counts, source identity, remote identity and evidence paths
will be appended after the requested run.
