# CARD-0824 S3 A-1 round 2 (Final, Grok)

Subject: origin/feat/card-task-54d2ac1b at d56e5d92d13e02b288a1e1902275c9950bc388d2 (Claude author, Debug 54d2ac1b-0834-43fc-b7ce-91af0eba1cd0).
Reviewed range: bfeaed2cb9ccff7eb96320d5c753f148a689470c..d56e5d92d13e02b288a1e1902275c9950bc388d2 (red witness 044c3afe, fix 5bd8e564, index d56e5d92).
Reviewer task: 1281fbe2 (Grok). Code landing owner: cfdf8021-2925-4e53-9e45-678ec90b2ec4.
Verdict: **CLEAN**. No blocker. Advisories below do not hold the land. Positive controls stay pending.

## Judgment

`RemoteWorkspaceReadiness.RecordPathAsync` sets `replaceCheckout` from `WindowsSlotCheckout.IsPending(task.WorktreePath)` only. `IsPending` is an ordinal `StartsWith` of `windows-slot-pending:`. A path that merely contains that text does not match. A real slot path, a legacy mirror, a `.ref-only` identity, and a server2 checkout do not start with the prefix, so they are not replaced.

The sentinel is written only by `DelegationWorktreeService.RecordWindowsSlotPendingAsync`, and only when `WindowsSlotCheckoutAdmittedAsync` is true (restart-only `WorkspaceSlotsEnabled`, a Windows runner, `workspaceSlotV1`, and `DecideListed`). The flag defaults to false and is snapshotted in the `RemoteWorkspaceService` constructor. Flag-off and non-Windows runners never write the sentinel, so `replaceCheckout` stays false and `WorktreePath` is left as the real checkout. server2 is unchanged.

Two readiness reports are serialized by `SELECT ... FOR UPDATE`. If `RemoteWorktreePath` is already a different path, `RecordPathAsync` returns false and does not replace. After the first successful record the stored path is no longer pending, so a later report cannot replace a real path. The A-1 case (sentinel, null `slotId`, queued, matching runner, empty or same remote path) now stores the reported legacy mirror.

The witness calls production `ResolveSlotDecisionAsync` and production `RecordPathAsync` with the default null `slotId`. It does not copy the condition.

## Mutation

Restored `var replaceCheckout = slotId != null && WindowsSlotCheckout.IsPending(task.WorktreePath);`, rebuilt, and ran only `Cleared_list_after_checkout_replaces_the_pending_sentinel`.

- Red: 1 failed, 0 passed. `row.WorktreePath` stayed `windows-slot-pending:08240000000040008000000000000004` instead of `C:\Antiphon\runner\workspaces\task-00000004` (assertion at `C0824WindowsSlotAdmissionTests.cs:150`). Lease ecc5908e, duration 23s 290ms.
- Restored the reviewed line with `git checkout`. HEAD stayed d56e5d92 and the diff was empty.
- Green rebuild, then the same method: 1 passed, 0 failed, duration 35s 574ms. Lease df38ce2f.

## Evidence at d56e5d92

```
CHECKPOINT CP-4 commit=d56e5d92d13e02b288a1e1902275c9950bc388d2 build=ok filter=/*/*/C0824WindowsSlotAdmissionTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/ws-05/.antiphon/checkpoints/20261119-222209-04f8/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=d56e5d92d13e02b288a1e1902275c9950bc388d2 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=28.193s wall=28.245s
```

| Row | Result |
|---|---|
| Native `Antiphon.Tests` build | 0 errors. Lease ae53b51a, 4m 34s |
| `C0824WorkspaceSlotServerTests` | 25 passed, 0 failed, 36s 188ms. Lease f4ff7157, load1 9.63 |
| `RemoteWorkspaceReadinessTests` | 19 passed, 0 failed, 39s 081ms. Lease 185ea298 |
| `DerivedImpactIndexTests` | 27 passed, 0 failed, 3m 28s. Lease 4864f257, load1 15.00. Driver rebuilt first, lease 44ddd293, 0 errors |
| Guard batch (policy, guard, slow-merge, tripwire, native Linux) | 53 passed, 0 failed, 54s 181ms. Lease 9068b989, load1 10.54 |
| `check-evidence-diff` c3188afc..d56e5d92 | commits=16 entries=0 violations=0, exit 0 |
| `audit-land-verification-graph.ps1 -Check` | approved, exit 0, digest 09EE55970E496C8A6CAF05E26C2A43AEFB5A940EC10327AA4A843460E155A262, count=80. Lease 2a02b40c |

`C0824_ReleaseRoundTripRealRunner(direct)` cannot be selected alone. Argument cases share the method path. Unescaped `(direct)` is an OR group and runs both arguments (2 passed, 27s 939ms). An extra `/direct` segment is ignored once the node path ends, so both arguments run again (2 passed, 33s 865ms). Escaped parentheses and a property filter on `TestMethodArguments`, `DisplayName`, `TestName`, and `FullyQualifiedName` selected 0 tests. The class of 25 tests passed in 36s, and both pair runs finished under the 35s per-test budget at load around 11. The earlier 35s timeout at load about 20 is load, not a regression in this delta.

## Land

`git merge-tree --write-tree origin/master` (c83d14d4858a66e41e2c51c4631869db99d0a2c7) conflicts in exactly two paths, the only paths both sides changed since merge-base c3188afc:

- `tests/derived-impact-index.json`. Regenerate on the combined tree. Tip `Antiphon.sln` classes is 336; master is 337. Do not hand-merge.
- `scripts/test-live-lane-run.ps1` line 28 only. Base digest 063da62b, master 3162a7da, tip dc502b46 (the S3 replay pin, not the A-1 delta). Recompute `PinnedInputDigest` on the combined tree. Do not keep either side.

## Advisories (do not hold the land)

- The S3 checkpoint prose still names two CP-4 methods. The class now has three, and Min 2 still passes.
- The checkpoint tool warned that the derived CP-4 row deadline of 60 minutes exceeds the 45-minute advisory. The row itself was green in 28s of test wall.
