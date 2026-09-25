# CARD-0696 repair 1 evidence

The pre-body overlay Esc refusal now reaches the existing immediate-send restoration path.
All commissioned Final-profile tests passed on server2 Linux under the caller's standing
Codex authority. No further approval was needed. Next stage: Review; original landing owner:
`58011e5e`. Every positive control remains pending for method-scoped SourceLanding Mutation.

## Scope and red proof

Starting candidate: `0a0834693d318a980dba11b93f89282dc167f240`.
Review reproduction: `/work/worktrees/task-2fc0cff8/.antiphon/task-2fc0cff8.md`.

Only the proactive overlay dismissal in `SessionMessageQueueService.DeliverAsync` gained a
narrow catch for `phone_home_unavailable` / `phone_home_connection_closed_before_send`.
It returns the same pre-body outcome used by the body-write refusal. Send-now therefore
restores every prior scalar; durable immediate enqueue removes only its provisional row;
Mode Now returns the typed retryable refusal. Post-body submission, in-flight errors,
cancellation, CARD-0584's shared harness and CARD-0698's working-state SQL were not changed.
The runtime-invariants owner documents this boundary.

Three new tests in `PhoneHomeImmediateSendTests` were committed before the fix at
`d494c079cb5a602f35490b7ae33d5ef00c544bed`. Each exercises both a resolving client's unavailable
refusal and a captured connection's closed-before-send refusal, using the existing real
WebSocket/PostgreSQL harness. They assert the initial attempted input is Esc, zero accepted
inputs, the typed refusal, and an exact durable snapshot including an older attempted row,
baseline, verdict, timestamps and retained spill. The test code adds no Linux-only assumptions.

The extra focused red checkpoint was announced before running because the repair brief
requires fresh red-first regression proof. All three tests failed at the intended boundary:

- Mode Now exposed a raw closed-before-send transport exception instead of the safe refusal.
- Send-now changed Pending to Sent, attempts 1 to 2, baseline 1 to 3, and cleared the verdict.
- Durable immediate enqueue retained its new Sent provisional row.

```text
CHECKPOINT Repair-red commit=d494c079cb5a602f35490b7ae33d5ef00c544bed build=ok filter=/*/*/PhoneHomeImmediateSendTests/C696Repair_* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-c99b5b44/.antiphon/c696-repair/red/Repair-red-20260925-124113-00b9/run.trx
```

## Final verification

Tested source: `2f3b85a0d378c1553cb69cf0a869f3ae3d83ac21`.
One isolated red build and one isolated final build; zero compile failures and zero test
reruns. The final build was made by CP-1 and reused for CP-2 through CP-5. CP-1 and CP-2
are green reruns of the original filters for this repair, not repeated original red/mutation
qualification. Original Code red evidence remains in
`docs/investigations/2026-09-25-card-0696-code-evidence.md`.

```text
CHECKPOINT CP-1 commit=2f3b85a0d378c1553cb69cf0a869f3ae3d83ac21 build=ok filter=/*/*/(PhoneHomeOutageMentionTests*)|(PhoneHomeImmediateSendTests*)|(PhoneHomePendingInventoryTests*)/C696Red_* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-c99b5b44/.antiphon/c696-repair/final/CP-1-20260925-124512-c953/run.trx
CHECKPOINT CP-2 commit=2f3b85a0d378c1553cb69cf0a869f3ae3d83ac21 build=reused filter=/*/*/PhoneHomeSchedulePreviewTests/Before_first_List_preview_remains_queueable_without_writes executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-c99b5b44/.antiphon/c696-repair/final/CP-2-20260925-124806-c63f/run.trx
CHECKPOINT CP-3 commit=2f3b85a0d378c1553cb69cf0a869f3ae3d83ac21 build=reused filter=/*/*/(PhoneHomeOutageMentionTests*)|(PhoneHomeImmediateSendTests*)|(PhoneHomePendingInventoryTests*)|(PhoneHomeSchedulePreviewTests*)/* executed=33 passed=33 failed=0 skipped=0 trx=/work/worktrees/task-c99b5b44/.antiphon/c696-repair/final/CP-3-20260925-124926-21ed/run.trx
CHECKPOINT CP-4 commit=2f3b85a0d378c1553cb69cf0a869f3ae3d83ac21 build=reused filter=/*/*/(AgentChannelServiceIntegrationTests*)|(PhoneHomeStrandedQueueTests*)|(PhoneHomeDirectoryTests*)|(SessionMessageQueuePhoneHomeDropTests*)|(ScheduleEndpointsTests*)|(ScheduleSweepTests*)/* executed=61 passed=61 failed=0 skipped=0 trx=/work/worktrees/task-c99b5b44/.antiphon/c696-repair/final/CP-4-20260925-125208-a023/run.trx
CHECKPOINT CP-5 commit=2f3b85a0d378c1553cb69cf0a869f3ae3d83ac21 build=reused filter=/*/*/*/*[Category=Unit] executed=3042 passed=3042 failed=0 skipped=28 trx=/work/worktrees/task-c99b5b44/.antiphon/c696-repair/final/CP-5-20260925-125359-767a/run.trx
```

CP-3 executes all 30 original methods plus the three new regressions. CP-4 executes every
method in the six named classes, including all five phone-home transport-drop controls.
The body-written/Enter-unavailable recovery, in-flight loss, caller cancellation, and
late-confirm-without-retyping assertions all passed. No new test was skipped. The Unit lane
discovered 3,070 cases: 3,042 passed, 27 Windows-specific skips and one missing Edge/Chrome
PDF-renderer skip. Lane and Slow-registry guards passed.

Read-only duration audits found zero unlisted >=5s cases in Repair-red, CP-1 and CP-3.
They returned exit 1 for CP-2 (one unchanged preview test, 8.998s), CP-4 (two unchanged
tests, 5.428s and 5.520s), and CP-5 (52 cases across ten unchanged classes). These are
duration observations, not assertion failures or claims about baseline timing. No unrelated
test classifications were changed. Full case identities and durations are retained in the
`*-duration.log` files alongside the checkpoint logs and TRX evidence.

## Reproduction and cleanup

From `/work/worktrees/task-c99b5b44`:

```sh
pwsh -NoProfile -File .antiphon/c696-repair/run.ps1 -Phase final
```

The driver runs the exact CP-1 through CP-5 filters above, raises CP-3's minimum to 33 for
the added tests, and prints fresh counts/rosters. The red phase requires checking out the
test-only commit in a separate worktree. Its git PATH shim bounds helper/test git calls
to 30 seconds; direct git operations used 30 seconds and network operations 60 seconds.
All background operations were polled inside the task, with waits below two minutes.

All 34 build-log-proven directories for `bin-c696-repair-red-c99b5b44/` and
`bin-c696-repair-final-c99b5b44/` were removed after verification. Inventory:
`/work/worktrees/task-c99b5b44/.antiphon/c696-repair/output-inventory.json`.
Logs, TRX, audit JSON and execution/cleanup scripts remain under
`/work/worktrees/task-c99b5b44/.antiphon/c696-repair/`.
No production restart, deployment or landing was performed. Changes after the tested
source commit are documentation/evidence only.
