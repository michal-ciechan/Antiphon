# Task 7cea69ea report (CARD-1499)

Plan, findings, checkpoint table and run record: docs/superpowers/plans/2026-10-22-delegate-release-unresolved-plan.md.

- Cause: a phone-home kill is ReleaseSlot, which evicts the runner record; the follow-up GetSessionAsync in AgentSessionService.KillOnAsync then gets 404 not_found and was recorded as an undelivered kill (kind 75), so PoolDelegateRelease saw the row Stopping and raised kind 76 Error, then the slot reconcile logged kind 74 "deferred kill: missing".
- Fix: AgentSessionService.ReadAfterKillAsync treats RunnerProblemException 404 not_found as exited only when the runner's kill answer already reported an exit. Liveness veto, runner and restart behaviour unchanged.
- Witnesses: AgentTaskReplyIntegrationTests.C1499_* (2 positive cases red before the fix, 4 guards).
- PCs pending for Mutation: PC-1 remove the catch; PC-2 drop the `killed` condition; PC-3 widen the catch to any exception.
