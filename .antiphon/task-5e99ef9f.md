# CARD-0100 S3 Code report (task 5e99ef9f)

Outcome: S3 implemented. CP-6 and CP-7 green; CP-5 has 2 failures that also fail on master. The
receipt is not clean (see Validators).

- Branch `feat/card-task-5e99ef9f`, worktree `/work/worktrees/task-5e99ef9f` (runner mirror), landing owner: Code task 5e99ef9f.
- Task base (master at start): `ff4d84df522eaf483059ee11187fe68cc955d44c`.
- Commits: `a818f6816d60c5fe2ec8f405cd878cf0b98500c1` (tests first + DTO fields),
  `d76e14e479d0196ae630fe37658bcd9bf000997c` (implementation + docs; **tested source**),
  `2aaa80c8ac833643b38675d9ac21cab7244d02e7` (test-design A-13 census correction, docs only), then this report commit (docs only).
- Plan `docs/superpowers/plans/2026-10-14-card-0100-card-relationships-plan.md`; test design `docs/superpowers/plans/2026-10-14-card-0100-test-design.md`.

## Changes

- `server/Application/Services/CardBlockers.cs` (an S2 file, edited so there is one definition rather
  than two): `IsResolved`, the new `IsOpenBlocker` (row expression) and `NotBlocked` (card expression)
  are all built from one private `Resolved` expression (archived, Done or Canceled; any other status counts as open).
- `OrchestratorService.LoadEligibleCandidatesAsync`: `.Where(CardBlockers.NotBlocked)`.
- `ExpectationSnapshotReader.ReadBacklogAsync`: `.Where(CardBlockers.NotBlocked)`.
- `AgentTaskPipelineStatusService`: unstarted Backlog rows are split by `LoadOpenBlockersAsync` (a query
  using `CardBlockers.IsOpenBlocker`) into candidates and held cards. `AgentTaskPipelineBacklogDto` gains
  `Blocked` (`WhenWritingDefault`) and `BlockedItems` (`AgentTaskPipelineBlockedBacklogItemDto(CardId,
  BoardId, Identifier, Title, BlockedBy[])`, top 5 by rank, null and omitted when empty), so with no
  relationships the JSON is `{"total":…,"items":[…]}` as before.
- Docs: `docs/orchestration-loop.md` §1 step 4 (pull "with no open blocker", `blocked`/`blockedItems`),
  `docs/agent-card-lifecycle.md` new section "Blocked by another card (CARD-0100)". Each row gives the behaviour, the named test and what it asserts, or says "not covered by a test" (Canceled/reopened blocker at pickup, OD-3 task creation, no attention item).
- Tests: T-15 `OrchestratorServiceIntegrationTests.PollTick_skips_a_card_with_an_open_blocker_and_dispatches_after_the_blocker_closes`,
  T-16 `AgentTaskPipelineStatusTests.C100_investigate_backlog_omits_blocked_cards_and_lists_them_as_blocked`
  (new `AgentTaskPipelineStatusC100Tests.cs`; also pins the empty-DTO JSON), T-17
  `ExpectationSnapshotTests.C100_Backlog_candidates_exclude_cards_with_an_open_blocker`.
- OD-3: no task-creation line changed.

## Red first (at a818f681, no predicate)

Ran as an unlisted run, because the brief asks for tests that are red at master first: slot-gated isolated build `bin-c0100-s3red/` in a detached
worktree, filter `/*/*/*/(PollTick_skips_a_card_with_an_open_blocker*)|(C100_investigate*)|(C100_Backlog*)`.
Build slot: released held=172s; run slot: granted waited=15s. Results: 3 executed, 3 failed, each at the assertion the design names:
`backlog.Total should be 2 but was 3`; `snapshot.EligibleBacklog should be 4 but was 5`;
`verifyBlocked.AgentSessions.CountAsync(...) should be 0 but was 1`.

## Checkpoint run (one flow, at d76e14e479d0196ae630fe37658bcd9bf000997c)

`dotnet tools/Antiphon.Checkpoints/bin-c0100-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-14-card-0100-test-design.md --rows CP-5,CP-6,CP-7 --row-timeout 40m --total-timeout 150m --expected-source-sha d76e14e4… --serial --keep-outputs --max-wait 540s`.
Tool bootstrap: one slot-gated build `bin-c0100-tool/` (UseAppHost=false), waited=60s.

```
run: 20261014-085952-ba4f
source: d76e14e479d0196ae630fe37658bcd9bf000997c state=clean buildSource=verified
CHECKPOINT CP-5 commit=d76e14e479d0196ae630fe37658bcd9bf000997c build=ok filter=/*/*/OrchestratorServiceIntegrationTests/* executed=28 passed=26 failed=2 skipped=0 trx=/work/worktrees/task-5e99ef9f/.antiphon/checkpoints/20261014-085952-ba4f/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=d76e14e479d0196ae630fe37658bcd9bf000997c sourceState=clean buildSource=verified
FAILED Antiphon.Tests.Application.OrchestratorServiceIntegrationTests.External_tracker_sync_marks_cards_terminal_when_issue_leaves_active_states (CP-5) PostgresException: 23503: update or delete on table "Cards" violates foreign key constraint "FK_CardWorktreeCleanups_Cards_CardId" on table "CardWorktreeCleanups" ...
FAILED Antiphon.Tests.Application.OrchestratorServiceIntegrationTests.PollTick_dispatches_external_issue_only_after_blockers_terminal (CP-5) InvalidOperationException: Sequence contains no elements.
CHECKPOINT CP-6 commit=d76e14e479d0196ae630fe37658bcd9bf000997c build=reused filter=/*/*/AgentTaskPipelineStatusTests/* executed=68 passed=68 failed=0 skipped=0 trx=/work/worktrees/task-5e99ef9f/.antiphon/checkpoints/20261014-085952-ba4f/rows/CP-6/run.trx slot=granted waited=15s dirty=0 source=d76e14e479d0196ae630fe37658bcd9bf000997c sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=d76e14e479d0196ae630fe37658bcd9bf000997c build=reused filter=/*/*/ExpectationSnapshotTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-5e99ef9f/.antiphon/checkpoints/20261014-085952-ba4f/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=d76e14e479d0196ae630fe37658bcd9bf000997c sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
wall: 8m21s  builds: 3  max-concurrent-builds: 1  rows: 1 green 2 red 0 skipped
outputs: refused/kept bin-c0100-s3/
verdict: RED exit=1
```

The TRX contains each new method with outcome Passed: T-15 in CP-5, T-16 in CP-6 and T-17 in CP-7.

- CP-5: 28 executed (design 28), 26 passed. The 2 failures are **inherited**. At master
  `ff4d84df5` an isolated slot-gated build `bin-c0100-s3base/` ran the same class filter: 27 executed, 25 passed, and the same 2 methods failed with the same messages (build slot waited=0s, run slot waited=15s). This is filed as **CARD-1251** (Backlog); it is related to CARD-1244.
- CP-6: 68 executed, 0 failed. The tool reported `roster-miss` (exit 3) because the design expected 72. The design's
  census counted `AgentTaskPipelineEndpointTests` (4 methods, a separate class declared in
  `AgentTaskPipelineStatusTests.cs`). That class does not match the filter. The test design now has
  amendment A-13 (commit 2aaa80c8), which sets CP-6 to 68. That class ran as an unlisted run, because it is
  the regression test for R-3's JSON route: `/*/*/AgentTaskPipelineEndpointTests/*` against the kept `bin-c0100-s3` output gave 4/4 passed (slot waited=0s).
- CP-7: 7/7 (design 7).

## Validators

- `scripts/validate-checkpoint-receipt.ps1 … -Rows CP-5,CP-6,CP-7` and `-Rows CP-6,CP-7`: `CHECKPOINT SOURCE INVALID reason=row_failed` (exit 2). `-Rows CP-7`: `CHECKPOINT SOURCE VALID source=d76e14e4… rows=1`.
- `Antiphon.Checkpoints validate --rows CP-5,CP-6,CP-7` and `--rows CP-6,CP-7`: `CHECKPOINT SOURCE INVALID reason=row_failed` (exit 2).
- The tool ended exit 1, not exit 2: it reported no cleanup_refused or ledger mismatch, and it removed its shadow `tool/` directory (`identity-dead`).

## V/R

- V-6: met. T-15, T-16 and T-17 were red before the implementation (a818f681) and green at d76e14e4.
- R-3: met apart from the 2 failures that also fail on master (CARD-1251). With no relationships, the
  pipeline JSON keeps its old shape (T-16's empty-DTO assertion, and the 4/4 endpoint class).
- Deferred: none. As the brief says, the whole Unit lane was not run.

## Other checks

- `scripts/check-evidence-diff.ps1 -BaseRef ff4d84df522eaf483059ee11187fe68cc955d44c -HeadRef HEAD`: exit 0, violations=0.
- `git merge-tree --write-tree origin/master HEAD` (origin/master `ff4d84df5`): exit 0.
- Cleanup, by literal path: `tools/Antiphon.Checkpoints/bin-c0100-tool`, `tools/Antiphon.Checkpoints/obj` (created by the bootstrap build at 08:59:32), `tests/Antiphon.Tests/bin-c0100-s3`, and the two detached scratch worktrees (`red`, `base`) with their `bin-c0100-s3red`/`bin-c0100-s3base` outputs. Kept as ignored evidence: `.antiphon/checkpoints/20261014-085952-ba4f/` (with `builds/bin-c0100-s3` stamp, 684K) and scratch TRX copies.
- Mutation was not run. These PCs are pending for SourceLanding Mutation: PC-17 (tick predicate), PC-18 (pipeline candidates), PC-19 (blocked list), PC-20 (watchdog predicate). PC-1..PC-16, PC-21..PC-34 belong to other slices and stay pending.
