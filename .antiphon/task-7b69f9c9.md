# CARD-1076 S5 Final Review report (task 7b69f9c9)

Outcome: clean. CARD-1076 S5 (client queue reasons, docs for D-2..D-6, V-15) reviewed at the tip
`b7c0b06025040737de7100d97e766fc53e2761a9` of `refs/heads/feat/card-task-d81ed12b`. The tip differs
from the implementation commit `fe9e3f73d44cc41010f24738319bb0df71fc4947` only by
`.antiphon/task-d81ed12b.md` (the Code report), so the source files reviewed and run are the
implementation's. subjectTaskId `d81ed12b-e5eb-44df-84e3-af053b019205` (the Code owner and landing
owner). Base `e839feda98ce3d97b6329bcd4a1030f6299460cd`. Verification round Final, profile v1.
Ordinary scope completed: Full. No defects. Five disclosures below, none a regression, fail-open or
crash. All PC-1..PC-12 stay pending for SourceLanding Mutation.

## Runs (all at b7c0b06025040737de7100d97e766fc53e2761a9, source clean, every driver slot=granted)

Checkpoint tool run `20261006-152117-dbb5`, results root `.antiphon/c1076-review`, `--serial`,
`TUNIT_MAX_PARALLEL_TESTS=1`, `--expected-source-sha` the tip. Tool bootstrap
`dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1076-tool/` under slot
`b956b62e-6d9c-40b8-9464-3d0d681365bf` (held 5s) is the one explained unlisted build; the `run`
was `dotnet exec` of that DLL. Wall 9m32s, builds 2 (bin-c1076-a for CP-1/CP-2, bin-c1076-b for
CP-3..CP-6), verdict GREEN exit=0, `unlisted: none`.

```
CHECKPOINT CP-7 commit=b7c0b06025040737de7100d97e766fc53e2761a9 build=n/a filter=pwsh -File scripts/test-client.ps1 pipelineStageModel homeTasksModel TaskCard executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=b7c0b06025040737de7100d97e766fc53e2761a9 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-1 commit=b7c0b06025040737de7100d97e766fc53e2761a9 build=ok filter=/*/*/(RepositoryMutationLeaseTests*)|(RepositoryMutationLeaseDescribeTests*)|(RepositoryChildJournalInspectorTests*)|(RepositoryFenceObserverTests*)/* executed=29 passed=29 failed=0 skipped=2 trx=/work/worktrees/task-7b69f9c9/.antiphon/c1076-review/20261006-152117-dbb5/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=b7c0b06025040737de7100d97e766fc53e2761a9 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=b7c0b06025040737de7100d97e766fc53e2761a9 build=reused filter=/*/*/LandingGitTests/* executed=57 passed=57 failed=0 skipped=0 trx=/work/worktrees/task-7b69f9c9/.antiphon/c1076-review/20261006-152117-dbb5/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=b7c0b06025040737de7100d97e766fc53e2761a9 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=b7c0b06025040737de7100d97e766fc53e2761a9 build=ok filter=/*/*/(RemoteWorkspacePreparerTests*)|(DispatcherRemotePrepStarvationTests*)|(PhoneHomeTaskDispatchProjectionTests*)/* executed=35 passed=35 failed=0 skipped=0 trx=/work/worktrees/task-7b69f9c9/.antiphon/c1076-review/20261006-152117-dbb5/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=b7c0b06025040737de7100d97e766fc53e2761a9 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=b7c0b06025040737de7100d97e766fc53e2761a9 build=reused filter=/*/*/(AgentTaskDispatcherPredicateTests*)|(DispatchHoldLedgerTests*)|(DelegationLeaseSettingsTests*)|(RunnerBranchContractDocumentationTests*)/* executed=55 passed=55 failed=0 skipped=0 trx=/work/worktrees/task-7b69f9c9/.antiphon/c1076-review/20261006-152117-dbb5/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=b7c0b06025040737de7100d97e766fc53e2761a9 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=b7c0b06025040737de7100d97e766fc53e2761a9 build=reused filter=/*/*/AgentTaskPipelineStatusTests/* executed=66 passed=66 failed=0 skipped=0 trx=/work/worktrees/task-7b69f9c9/.antiphon/c1076-review/20261006-152117-dbb5/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=b7c0b06025040737de7100d97e766fc53e2761a9 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=b7c0b06025040737de7100d97e766fc53e2761a9 build=reused filter=/*/*/DispatchHoldVisibilityTests/(lease_hold_traces_once_per_holder_and_names_the_running_land*)|(lease_fence_is_named_from_the_provider*)|(unknown_lease_holder_is_stable_text*)|(C672_lease_hold_registers_a_waiter_and_dispatch_clears_it*)|(C672_held_aged_carries_the_per_class_wait_ledger*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-7b69f9c9/.antiphon/c1076-review/20261006-152117-dbb5/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=b7c0b06025040737de7100d97e766fc53e2761a9 sourceState=clean buildSource=verified
```

CP-1 `skipped=2` are the two pre-existing Windows-only lease tests the plan names (29 executed on
Linux, as the row expects). CP-7 console: `Test Files 3 passed (3)`, `Tests 112 passed (112)`,
`CLIENT TESTS EXIT CODE: 0`; the dot reporter prints no per-file split.

Additional rows through `scripts/run-checkpoint.ps1` (own slot each, `-NoBuild` on the kept
`bin-c1076-b/`, `-ExpectedSourceSha` the tip), run one at a time after the tool run:

```
CHECKPOINT C1076-REVIEW-LEASE-OWNER commit=b7c0b06025040737de7100d97e766fc53e2761a9 build=reused filter=/*/*/RepositoryMutationLeaseOwnerTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-7b69f9c9/.antiphon/c1076-review/C1076-REVIEW-LEASE-OWNER-20261006-153159-92a6/run.trx slot=granted waited=0s dirty=0 source=b7c0b06025040737de7100d97e766fc53e2761a9 sourceState=clean buildSource=verified
CHECKPOINT C1076-REVIEW-CHILD-RECOVERY commit=b7c0b06025040737de7100d97e766fc53e2761a9 build=reused filter=/*/*/RepositoryChildRecoveryTests/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-7b69f9c9/.antiphon/c1076-review/C1076-REVIEW-CHILD-RECOVERY-20261006-153326-ebf4/run.trx slot=granted waited=0s dirty=0 source=b7c0b06025040737de7100d97e766fc53e2761a9 sourceState=clean buildSource=verified
CHECKPOINT C1076-REVIEW-GUARD commit=b7c0b06025040737de7100d97e766fc53e2761a9 build=reused filter=/*/*/(TestClassificationGuardTests*)|(SlowTestTripwireTests*)/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-7b69f9c9/.antiphon/c1076-review/C1076-REVIEW-GUARD-20261006-153525-f9cc/run.trx slot=granted waited=0s dirty=0 source=b7c0b06025040737de7100d97e766fc53e2761a9 sourceState=clean buildSource=verified
```

`RepositoryChildRecoveryTests` is `[Category("Slow")]`; it ran because the brief named it. Client
`eslint --max-warnings 0` on the six touched files and `tsc -b` (whole client project) passed under
slot `124d2a1b-0c19-4b58-982c-ccd167befdc2` (held 29s). `scripts/check-evidence-diff.ps1` over
`e839feda9..b7c0b0602`: commits=2 entries=1 violations=0 (the entry is the Code report markdown).
Every `bin-c1076-*` directory (56, across a, b and tool) was deleted; `git status` is clean.

## S1 class counts (brief request, first review of the assembled chain)

| Class | Executed |
|---|---|
| RepositoryMutationLeaseTests + RepositoryMutationLeaseDescribeTests + RepositoryChildJournalInspectorTests + RepositoryFenceObserverTests (CP-1) | 29 passed, 2 Windows-only skips |
| LandingGitTests (CP-2) | 57 passed |
| RepositoryMutationLeaseOwnerTests | 8 passed |
| RepositoryChildRecoveryTests (Slow) | 14 passed |

## Code report audit against the plan's `### Checkpoints` table

CP-3 35/35, CP-4 55/55, CP-5 66/66, CP-6 5/5 and CP-7 exit 0 are all present with the plan's exact
filters and expected counts, SHA-validated, `slot=granted`, `buildSource=verified`. CP-1/CP-2 are
not in the Code closed list, as the plan assigns them to S1; both reran green here. Unlisted work:
the tool bootstrap build (explained, slot stated) and the registry guard (explained, slot stated).
The dirty-tree red-first runs are explained as the red witness but their `bin-c1076-red` build does
not state a slot lease (reporting gap, disclosure 4). V/R mapping V-5..V-15, R-1, R-4..R-7,
R-9..R-12 matches the rows that cover them. PC-1..PC-12 correctly reported pending. The new vitest
cases assert exact rendered strings and can go red (the report records 14 red before the edit);
V-15 asserts eight literal pins and went red on the missing key.

## Brief checks

- (a) Unknown reason / missing heldBy: both switches end in a `default` that routes through a
  `never`-typed helper and returns the plain queued text without the token; the unknown branch
  never reads `heldBy`. The two new holder branches index `heldBy[0]` exactly as the pre-existing
  `sharedCheckoutLease` and `siblingLandInFlight` branches do, and the server DTO initialises
  `HeldBy` to an empty list before any branch, so a missing array is not reachable from this
  server. Every consumer was checked: `queueReasonFor` (TaskCard prints `queue.line` only, no
  holder link) and `compactQueueReason` via `rightCell`; nothing else reads `heldBy` or
  `queueReason`. `tsc -b` passing proves no other `Record<AgentTaskPipelineQueueReason, …>` map
  was left non-exhaustive. The stale `BehindTaskId` renders as text only; see disclosure 2.
- (b) Docs against code: push is `["push", "origin", task.WorktreeBranch]` with the budget and tag
  (no `-u`); `RemotePrepPushBudgetMinutes` default 20 and validator `< 1` refusal; one
  `SemaphoreSlim(1,1)` gate per normalised `RepoPath` inside the preparer, waiter in flight;
  `RearmsPreparedWorktree` matches the documented re-arm conditions and the no-baseline case keeps
  the lease; `FirstFencingAsync` skips only `alive == true` with purpose in
  `LiveNonFencingPurposes`, and dead (`false`), unknown (`null`), torn and unreadable records fence;
  the unavailable sentence appends `(purpose=… task=…)` and names the recovery script; the
  documented precedence sharedCheckoutLease → siblingLandInFlight → routingPinNotBefore →
  repositoryLease → remotePrep → hostBudget/concurrencyCap → awaitingDispatch is the `ToQueued`
  order; `hostBudget` existed on the server at the base and was absent from the client type.
- (c) V-15 pins eight literal strings in both docs with `ShouldContain`; renaming a documented key
  or reason to a different token fails the test (the Code report's red-first run shows the missing
  key failing). See disclosure 1 for the two shapes it does not catch.
- (d) Existing reasons: the diff only adds switch cases, label entries and test rows; the existing
  cases, their test rows and the pipeline view components are byte-identical, and the existing
  vitest rows in all three files passed unchanged (112 total).
- Inherited failures CARD-1102 and CARD-1096 are not in any selected filter and were not observed.
- Producer/recipient, storage/recovery, receipt and transcript audit: not applicable; this slice
  touches client rendering, docs and a doc-pin test only, no queue, delivery or transcript path.

## Disclosures (no defect; Backlog cards suggested where noted)

1. V-15 pins literal strings, not code constants. A rename of
   `DelegationSettings.RemotePrepPushBudgetMinutes`, `RepositoryChildPurposes.RemotePrepPush` or
   `AgentTaskPipelineStatusService.QueueReasonRepositoryLease/RemotePrep` in code leaves the docs
   stale while V-15 stays green, and the substring pins `remotePrep`/`repositoryLease` survive a
   doc rename to a superstring such as `remotePreparation`. Same pattern as the file's existing
   pins. Backlog: bind the pins to `nameof(...)`/the constants and match backtick-delimited tokens.
2. The long `remotePrep` line says `behind task-<short> — <title>` from the enqueue-time
   `BehindTaskId`, which CARD-1093 documents can name a finished pusher with three or more waiters.
   The plan's D-7 wording is implemented exactly and the docs disclose it; CARD-1093 already tracks
   the snapshot. No link is rendered, so nothing navigates to a stale holder.
3. `compactQueueReason` builds `lease ~<short>`/`prep ~<short>` inline instead of reusing the
   file's `holderCitation` helper that the `sharedCheckoutLease` branch uses. Cosmetic.
4. Code report: the red-first `bin-c1076-red` build and `--no-build` runs are explained as the
   red witness but do not state a slot lease. Reporting gap only; the SHA-validated receipts are
   the tool run.
5. `heldBy[0]` without optional chaining in the two new branches would throw on an absent array,
   identically to the two pre-existing holder branches; unreachable from this server (DTO default
   `[]`) and not a regression.

## Landing notes

Landing owner `d81ed12b-e5eb-44df-84e3-af053b019205`, land `-ExpectedSourceSha
b7c0b06025040737de7100d97e766fc53e2761a9`. `GET /api/runner-defaults` reports
`globalRunnerId: server2`; omit `-Runner` and `-Platform`. restart: none (client bundle and docs;
not activated by the server). PC-1..PC-12 pending for method-scoped SourceLanding Mutation after
land.
