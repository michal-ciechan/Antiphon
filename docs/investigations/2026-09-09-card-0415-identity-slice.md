# CARD-0415 identity slice: implementation and verification

CARD-0415 is incomplete. This branch implements S1's specialist execution snapshot
and actionable failure handling; S2-S6 and their acceptance remain ahead. No
fallback chain has been activated and no provider has been qualified.

## Implemented

- Both specialist runner APIs stamp the selected kind and tier, exact ModelId,
  canonical availability alias, live effective model, session Id/StartedAt and
  profile revision. Created events carry the identity metadata.
- Queued availability reads and dispatch use the captured alias. Standing dispatch
  preserves its public requested-kind/live-kind guard and refuses specialist
  model, tier, generation or profile drift before enqueue, with the appended
  `SpecialistIdentityMismatch` task failure code and runner outcome.
- Check digests and unavailable incidents retain the bounded original failure
  reason (800 UTF-16 characters, line endings flattened and task markers scrubbed).
- The CLI-generated `20260908225314_AddSpecialistExecutionIdentity` migration adds
  nullable task metadata. Existing task/pin behavior remains on the legacy path.
- The plan now records DF-1's corrected p95 78.972031s (rank 71), 17 durations
  exceeding 30s and five reaching/exceeding 60s; DF-2's actual bypass source at
  server/appsettings.json:81; and DF-3's bound on AgentControlService changes.

The identity tests use a cloned database, actual specialist task creation and the
real standing dispatcher with a fake runner. Their positive evidence is task
placement/queue insertion without a new session. They do not establish native
UserPrompt delivery, no-tools execution or model qualification.

## Dependency and integration base

Base: `d9d1f90e`. CARD-0412 is implemented on this base; the current recovery service
was last changed by `e138c41b`. Its `GrantReadyAsync` remains the sole granter;
`RedeemAsync` consumes matching grants and `CountedTaskLockKey` is
`antiphon.capacity.counted-tasks`. S1 adds no admission transactions, granter,
provider clock or recovery action. S5b must bind to the landed implementation.

CARD-0412's Attention addition landed first at `35b90042`:
`CapacityRecoveryExhausted = 32`. This slice adds no Attention kind. S5a must append
its kind and preserve that ordinal, with combined server/client assertions.

CARD-0167 remains absent on this branch: the existing
`CodexRealCliStubProxyCanaryTests.B_agent_path_deferred_until_CARD_0167` still
unconditionally throws SkipTestException. It was inspected, not run or counted as
passing. Codex production launch/tool-policy, transport and real-model arms remain
PendingDependency. No CLI flags, tool policies, transport ceilings or AgentControl
launch injection were changed. Claude capability work and other independent
slices also remain unimplemented here.

## Evidence custody

Worktree: `C:\Antiphon\worktrees\card-task-1f1c67b6`.
Raw logs, fresh TRXs, mutation diffs and the scoped control runner are retained at
`C:\Antiphon\worktrees\card-task-1f1c67b6\.antiphon\acceptance\card-0415`.
They are ignored test artifacts, not deployment qualification certificates.

The historical bug reproduced before the producer fix: one executed assertion
failed with a ClaudeCode Check task pinned to the synthetic live Codex session.
The unchanged test then passed. The later 10-case identity matrix also passed.
A matrix build failed on a tuple expression unsupported by expression trees; that
test compilation error was fixed and is not counted as red-control evidence.

PC-1 was repeated against the final snapshot implementation (1 red / 1 green).
PC-2 tier and exact-alias mutations each produced 1 red / 1 green. The generation
and public live-kind mutations were independent edits batched in one run with
separate data rows (2 red / 2 green, one per guard). A first tier mutation altered
both task and event assignments and is excluded; the retained accepted run changed
only the task assignment. Added PC-60 discards the original failure reason and
produced 2 red / 2 green on the named failure-detail assertions. Every accepted
red is an assertion failure in the intended method; no build failure, skip or
zero-test result counts. Source restoration used fresh writes to avoid stale DLLs.

Final unmutated regression results and the per-item ledger follow below.
