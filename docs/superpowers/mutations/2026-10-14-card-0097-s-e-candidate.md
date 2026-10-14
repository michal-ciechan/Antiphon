# CARD-0097 S-E unbound Mutation candidate and Code evidence

Original Code / landing owner: `b7ab0eb9-d26c-4941-97f1-a77e28b3c04c`.
Branch: `feat/card-task-b7ab0eb9`.
Worktree: `/work/worktrees/task-b7ab0eb9`.
Task base: `ba774271a61870261e86fb39a6fc0614fb826d53`.
Ordinary tested implementation: `d3f46adbfb28eb0fe9d3ca1d89cd8d049d933ede`.
This document is a subsequent evidence-only change; it does not alter the tested script or tests.

Scope: S-E of [the landed matrix plan](../plans/2026-10-14-card-0097-matrix-plan.md),
D-9 and sections 6.3/6.5, with [the test design](../plans/2026-10-14-card-0097-matrix-test-design.md).
The specific brief selected CP-18 and excluded the whole Unit lane. S-F owns the shared runbook
and generated instructions. No S-F, S-C2, or AgentTaskService changes are included here.

## Ordinary evidence

`MigrateStagePinsScriptTests` drives the new script through the existing S-D `ScriptedApi`
HttpListener fixture. The live GET census at task start superseded the older plan table:
Plan/TestDesign/Code/Investigate/Debug use Codex Frontier, Codex High, ClaudeCode High;
Coverage/Docs/Commit/Test/Deploy/Merge/Mutation use Codex High, Codex Medium, ClaudeCode High;
Review uses ClaudeCode High. Candidate lists are literal test expectations; the script reads
the API. The fixture includes the live shorthand-plus-candidates shape and a card pin.

| Behaviour / ID | Named test in `MigrateStagePinsScriptTests` | What it asserts / outcome |
|---|---|---|
| Preview, V-E1 | `Dry_run_writes_nothing_and_records_before_images` | GET-only default and explicit `-DryRun`; 13 stage pins, 39 absent before-images, 39 Human plan bodies, before/after labels, snapshot path and separate clear instructions. Passed, 1 result. |
| Manual pins, V-E2 | `Manual_and_card_pins_are_listed_and_never_written` | Forbid list, dates and agent-bound stage rows print manual and produce no cell PUT; card pin excluded; pin store unchanged. Passed, 1 result. |
| Apply, V-E3 | `Apply_puts_three_cells_per_role_with_human_provenance_and_the_migration_reason` | 39 ordered cell PUTs, literal candidate lists, Human provenance, multiline UTF-8 operator words, GET-only pin access; changed live candidates drive selected-role PUTs; fable and wildcard Grok holds warn. Passed, 1 result. |
| Failure stop, V-E4 | `A_non_2xx_stops_the_run_and_names_the_cells_written` | `apply` and `restore` stop at the injected 500 with exact prior-write counts, remaining cells and server problem detail/code. Passed, 2 results. |
| Repeat apply, V-E5 | `A_second_apply_sends_the_same_puts_and_leaves_the_store_unchanged` | Second apply sends 39 full-overwrite PUTs with unchanged bodies/store; first snapshot retained beside the second. Passed, 1 result. |
| Restore table, V-E6 | `Restore_re_puts_human_before_images_and_deletes_absent_expired_and_auto_ones` | `human`: one saved-body PUT plus 38 DELETEs; `human-expired` and `auto`: 39 DELETEs with named explanation; repeat restore request sequence and pin store unchanged. Passed, 3 results. |
| Ordered rollback, V-E12 | `Restore_refuses_until_each_migrated_roles_required_stage_pin_is_back` | Empty pins, Preferred Review, expired/future/card-only Review and mismatched API base refuse before writes; restored Required pins permit 39 DELETEs. Passed, 1 result. |
| V-E7 through V-E11 | Withdrawn by the landed design | Not selected; not reported as passed. |
| S-E regression IDs | The landed design names no separate R-E rows | CP-18 selects the full affected class. |
| Windows PowerShell 5.1 runtime execution | Not covered by a test in this run | ASCII source and PowerShell parser checked; ordinary execution used Linux pwsh. |

CP-18 ran through the checkpoint tool with the imported manifest, `rowMinutes: 40`,
`--row-timeout 40m --total-timeout 150m --rows CP-18`, the committed expected SHA,
Linux `UseAppHost=false`, and without `--keep-outputs`. No repetitions requested.
The launcher bootstrap was an additional gated build, necessary because the worktree had no
built checkpoint tool: `build-slot.ps1 -Label b7ab0eb9-checkpoint-tool -- dotnet build
tools/Antiphon.Checkpoints --property:OutputPath=bin-b7ab0eb9-tool/ --property:UseAppHost=false --nologo`;
slot granted, waited 0s, exit 0.

The first CP-18 attempt at `6e70a54b0070f7254f432d5ec269cf859950e771` failed compilation
because the new test omitted its process-limiter namespace import. Commit
`3414d87211acdad8fe638a5214a223c074792667` repaired the import. No tests ran on that attempt.
A malformed local manifest and one mistyped expected SHA were refused before execution.
The corrected tests-first run executed ten cases against the base implementation plus tests;
ten failed because the new script was absent. That is ordinary baseline evidence, not a PC cycle.

Unedited receipts:

```text
CHECKPOINT CP-18 commit=3414d87211acdad8fe638a5214a223c074792667 build=ok filter=/*/*/MigrateStagePinsScriptTests/* executed=10 passed=0 failed=10 skipped=0 trx=/work/worktrees/task-b7ab0eb9/.antiphon/checkpoints/20261014-142239-5805/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=3414d87211acdad8fe638a5214a223c074792667 sourceState=clean buildSource=verified
CHECKPOINT CP-18 commit=d3f46adbfb28eb0fe9d3ca1d89cd8d049d933ede build=ok filter=/*/*/MigrateStagePinsScriptTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-b7ab0eb9/.antiphon/checkpoints/20261014-142948-550a/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=d3f46adbfb28eb0fe9d3ca1d89cd8d049d933ede sourceState=clean buildSource=verified
CHECKPOINT SOURCE VALID source=d3f46adbfb28eb0fe9d3ca1d89cd8d049d933ede rows=1
```

Both `Antiphon.Checkpoints validate` and `scripts/validate-checkpoint-receipt.ps1` returned
the VALID line above against the green `report.json`. Fresh TRX inspection found the seven
named methods and ten results with zero skips. The green report records exit 0. A separate
`wait` initially returned exit 2 for retained tool-copy custody; deleting that task-created
copy and waiting again resolved cleanup without rerunning tests. Generated TRX/JSON/logs remain
ignored under `.antiphon/checkpoints/20261014-142948-550a/`.

Supplementary live acceptance: explicit `-DryRun` completed with 13 pins, 39 before-images,
39 planned cells and no writes. Snapshot/log: `.antiphon/b7ab0eb9-live-dry-run/` and
`.antiphon/b7ab0eb9-live-dry-run.log`. `GET /api/runner-defaults` and
`GET /api/session-runners` returned 200 at start. The live server version read was
`893ddfceeebf4d13d9401848c535abada3983c27`; activation and live apply are operator rollout work.
Restart for this script-only slice: none.

## Pending controls

This candidate has no SourceLanding operation or snapshot binding. The caller commissions
Mutation after ordinary Review and landing through the original Code owner above. Mutation
owns deliberate mutants, red/restore/green execution and missing-control discovery.
No PC was executed by Code, and the ordinary green run discharges no PC.

Production target: `scripts/migrate-stage-pins-to-cells.ps1`.
Project: `tests/Antiphon.Tests`. Class: `MigrateStagePinsScriptTests`.
Use `/*/*/MigrateStagePinsScriptTests/<ExactMethod>`; for parameterized methods use the method
prefix plus `*` and inspect the listed argument row in fresh TRX. Keep controls sharing the
script separate. Do not widen a timeout or loosen an assertion.

| PC / variant | Deliberate defect | Exact method | Pending witness |
|---|---|---|---|
| PC-29 | Ignore the forbiddenAliases eligibility check | `Manual_and_card_pins_are_listed_and_never_written` | Unexpected Plan cell PUT |
| PC-30 / `apply` | Continue writes after the first failed PUT | `A_non_2xx_stops_the_run_and_names_the_cells_written` | Request after TestDesign/Medium failure |
| PC-31 / `human` | DELETE the live Human before-image instead of restoring it | `Restore_re_puts_human_before_images_and_deletes_absent_expired_and_auto_ones` | Missing saved-body Plan/Medium PUT |
| PC-40 | Bypass Required stage-pin restore precondition | `Restore_refuses_until_each_migrated_roles_required_stage_pin_is_back` | DELETE while the pin store is empty |
| PC-45 / `auto` | Re-create the saved Auto body after DELETE | `Restore_re_puts_human_before_images_and_deletes_absent_expired_and_auto_ones` | Unexpected Plan/Medium PUT |

The held-candidate warning and explicit GET-only dry-run checks are additional ordinary
witnesses for Mutation's missing-control review; they have no numbered PC in the landed plan.
