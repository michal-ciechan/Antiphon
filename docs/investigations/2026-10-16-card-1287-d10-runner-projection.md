# D-10: runner display projection contradicts fixture-only diagnosis

Code/landing owner: `8aa5fb3c-2f0b-42f8-adb4-9ece8d838ea4`.
Branch: `feat/card-task-8aa5fb3c`.
Worktree: `C:\Antiphon\worktrees\card-task-8aa5fb3c`.
Task base: `95294927a528e9e6b80df155f234ebe80b8a9bb3`.
Chain base: `b412a32ef89c0f12563eb6c0e8b4a0b586d85793` (prior Code owner 98df1e5f).
Selection: [continuation plan](../superpowers/plans/2026-10-16-card-1287-d10-runner-determinism.md).
Prior outcomes: [snapshot repair evidence](2026-10-16-card-1287-rc-contract-snapshot-repair.md).

## Finding and proposed correction

The brief attributes `runnerId: "desktop"` to development runner defaults and
requires a configuration-only fixture repair retaining null expectations. Source
inspection disproves that cause:

- `AgentTaskService.ToSummary` passes `task.RunnerId` through
  `RunnerRequestIntent.DisplayRunnerId` (AgentTaskService.cs, line 4625).
- `DefaultRunnerRoutingPolicy.cs`, lines 51-52, unconditionally maps null/blank
  stored IDs to `DesktopPublicId`, which is `desktop`. There is no configuration
  branch or runner-default lookup in this projection.
- This mapping was introduced by CARD-0710 commit
  `99d153a4a666f954c57c5321eeab9372de86cd64` on 2026-09-25, before the task base.
- The scenario seeds task entities directly, leaving RunnerId null. It never
  uses create-time runner selection. The drawer uses the same summary projection.
- `HomeTaskService` projects the stored nullable value directly. Its independent
  home-task snapshot should retain null; no blanket replacement is warranted.

The concrete proposed repair is exactly six value corrections: all five
`items[].runnerId` fields in `agent-tasks.json`, and `summary.runnerId` in
`agent-task-detail.json`, from null to `"desktop"`. Keep every other value,
full-object comparison, drawer/recorded-base assertion, and timeout unchanged.
No runner configuration fields were added: they cannot affect this projection.
No product or fixture implementation was changed. Scope clarification was requested
because the brief expressly prescribes a configuration-only repair. The proposed
snapshot changes are not yet applied or tested.

## Fresh red evidence

Tested SHA: `1f2211da08d5af737d4a0ce5e1bc4a91518cebc5` (only the continuation plan
differs from the assigned base; product, test and snapshot bytes are unchanged).
Bound checkpoint run `20261016-032916-383b`; `wait` completed with exit 1.
Fresh TRX TestMethod is exactly
`Antiphon.E2E.ContractSnapshotTests.Delegated_task_board_and_drawer_contracts`:
1 executed, 0 passed, 1 failed, 0 skipped. The failure is full JSON equality at
`agent-tasks.json`, showing desktop versus null. Drawer and recorded-base checks
remain unreached. No positive control or mutation was performed.

```text
CHECKPOINT CP-1 commit=1f2211da08d5af737d4a0ce5e1bc4a91518cebc5 build=ok filter=/*/*/ContractSnapshotTests/Delegated_task_board_and_drawer_contracts executed=1 passed=0 failed=1 skipped=0 trx=C:\Antiphon\worktrees\card-task-8aa5fb3c\.antiphon\checkpoints\20261016-032916-383b\rows\CP-1\run.trx slot=granted waited=0s dirty=0 source=1f2211da08d5af737d4a0ce5e1bc4a91518cebc5 sourceState=clean buildSource=verified
```

Both build and test slots were granted with 0 seconds wait. Build phase 126.386s;
test host 49.456s, test wall 44.165s; checkpoint wall 3m54s. The report lists two
manifest builds, but only bin-d10-red ran; bin-d10-green is unused. Source is clean
and build provenance verified; the red receipt is not a green qualification.

Listed setup completed: checkpoint-tool build (0 errors, 2 warnings, slot granted,
waited 0s, held 11s), npm ci (exit 0, waited 0s, held 83s), fresh frontend build
(exit 0, waited 0s, held 22s). The E2E build has 0 errors and 36 warnings. No
unlisted test selection ran. A mistaken tool DLL path failed before launching
the import command and was corrected without rebuilding.

PhoneHome child overrides were removed and PhoneHome__Enabled=false supplied.
E2E used its owned random-port session runner and disposable Postgres. No shared
AppHost restart or production session operation. Runner defaults and session
runner catalogue were read through their documented GET routes before execution.

## Outstanding verification

| ID | Actual outcome in this continuation |
|---|---|
| V-1 | Not rerun; CP-3 pending. Prior pipeline pass retained as historical evidence. |
| V-2 | Not rerun; CP-4 pending. Prior backlog pass retained as historical evidence. |
| V-3 | FAIL, CP-1; CP-2 green and downstream drawer/base assertions pending. |
| V-4 | Not rerun; CP-5 pending. Prior home pass retained as historical evidence. |
| R-1 | Reader inventory confirmed; four-reader execution remains incomplete. |
| R-2 | Not rerun; CP-6 pending. Prior 86-test pass is historical only. |
| R-3 | Setup frontend build passed; CP-7/8 final frontend/story builds pending. |

The six repaired JSON files have only the four named E2E readers. Story consumers
remain PipelineStagesPanel, BacklogSection, DelegationsBoard, DelegationsHistory,
and TasksSection; TaskDrawer remains in the named client regression selection.
No whole Unit or full assembly run, as excluded by the specific CARD-1287 brief.
No deferred-to-final IDs: this is an incomplete Final round, not an Interim pass.
Zero authored PC rows/variants; SourceLanding Mutation still owns missing-control
discovery and all deliberate red/restore/green cycles after Review and landing.
Restart: none. D-7/D-12 remain out of scope.

Raw receipts/TRX/logs remain ignored under `.antiphon/checkpoints/20261016-032916-383b/`
and `.antiphon/task-8aa5fb3c/`. Final cleanup, evidence guard and publication facts
are recorded in the ignored stored task report `.antiphon/task-8aa5fb3c.md`.

The receipt validator returned `CHECKPOINT SOURCE INVALID reason=cleanup_refused`;
the checkpoint had retained its red output. This is not a qualified green receipt.
The executor is dead, its shadow copy was removed by `wait`, the fixture's runner
directory is empty, and a final process census found no owned Antiphon, dotnet,
node or pty-host children. Client dependencies were restored to client/node_modules.
Automatic approval review rejected the containment-checked recursive removal of
`tools/Antiphon.Checkpoints/bin-d10-tool`, `src/Antiphon.HostCleanup/bin-d10-tool`
and `tests/Antiphon.E2E/bin-d10-red` with `blocked by policy`. These ignored outputs
remain in their original locations; no process was left running and no output was
relocated to evade the evidence policy.
