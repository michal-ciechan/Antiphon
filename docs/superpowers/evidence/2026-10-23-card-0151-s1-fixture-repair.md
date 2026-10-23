# CARD-0151 S1 fixture cleanup continuation

Code task and appointed landing owner for the whole S1 chain:
`550d40c4-f266-42be-848e-7f52c2007acb`.
Branch: `feat/card-task-550d40c4`; worktree: `/work/worktrees/task-550d40c4`.
Assigned base: `896d83caee1400d991848da964e91b82dd7a75e0`.
Original S1 chain base: `1f55bfc32df6aac32a48f9246748d5786cbe9e35`.
Predecessor Code task: `42bfc023-b5a5-4b8b-948a-8d8c0f702c05`.
The continuation brief appoints this task as owner, superseding the historical owner wording
in the predecessor's report without rewriting that report.

Plan: `docs/superpowers/plans/2026-10-22-card-0151-plan.md`, S1 only.
Implementation and earlier estimate red witnesses:
`docs/superpowers/evidence/2026-10-23-card-0151-s1-code.md`.
The predecessor's ignored `.antiphon/task-42bfc023.md` is absent from its remote Git tree;
its final report was read through `GET /api/agent-tasks/42bfc023-b5a5-4b8b-948a-8d8c0f702c05`.

This report is committed **before** final CP-2. Its final outcome, actual tested SHA,
unedited CHECKPOINT line and receipt validation will be in the task's final report and
ignored `.antiphon/checkpoints/<run>/` evidence. This document does not claim that pending run passed.

## Defect and repair

The existing correction fixture deleted revisions while Done-move worktree cleanup rows still
referenced them. The only code change is in
`tests/Antiphon.Tests/Application/CardCorrectionIntegrationTests.cs`:
delete cleanup endpoints, cleanup targets, then cleanup obligations before deleting revisions
and cards. Every delete follows the fixture's existing owned-card IDs. The model makes each
link in that cleanup tree Restrict, so deleting the leaves first respects all three levels.
No production code, assertion, timeout, skip or test roster changes.

Existing tests are the regression witnesses; no deliberate mutation was performed. The fixture
file is byte-identical at the original S1 base and the assigned continuation base. The predecessor
reported the same twelve failures in a base run; this task captures the assigned-base red below.

Full board searches for `CardWorktreeCleanups` and `CardCorrectionIntegrationTests`, followed by
`get` on all six hits, found related Done cards but no card for this exact fixture/FK defect.
CARD-1251 repaired OrchestratorServiceIntegrationTests and explicitly disclosed other unpatched
helpers; CARD-1244 repaired production ProjectCascade; CARD-1253 concerns other production FKs.
CARD-0424 fixed timestamp precision; CARD-0040 and CARD-0122 concern card transitions.
This is a remaining occurrence of the closed defect family, not evidence that those repairs regressed.
Created **CARD-1530**, Backlog, High, labels `test-fixture,test-hygiene`.
The create command printed the new ID then exited 1 while rendering a Windows card-file path on
Linux (`Cannot find drive. A drive with the name 'C' does not exist.`); a fresh `get` confirmed the
card was created. No duplicate creation was attempted.

## Fresh red witness

The brief expressly allows one extra full-class red capture. This diagnostic used an ignored
one-row manifest `.antiphon/c0151-fixture-red.md`; its CP-1 identity is local to that diagnostic,
not the feature plan's CP-1. One isolated build and one filter, expected source equal to the
assigned base. Terminal exit 1, 43 distinct correction methods, 31 passed, 12 failed, zero skips.
The fresh TRX definitions and each result identity were inspected.

```text
CHECKPOINT CP-1 commit=896d83caee1400d991848da964e91b82dd7a75e0 build=ok filter=/*/Antiphon.Tests.Application/CardCorrectionIntegrationTests/* executed=43 passed=31 failed=12 skipped=0 trx=/work/worktrees/task-550d40c4/.antiphon/checkpoints/20261023-033747-c98a/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=896d83caee1400d991848da964e91b82dd7a75e0 sourceState=clean buildSource=verified
```

Every failure is the same teardown error:

```text
PostgresException: 23503: update or delete on table "CardRevisions" violates foreign key constraint "FK_CardWorktreeCleanups_CardRevisions_DoneRevisionId" on table "CardWorktreeCleanups"
```

Failing method roster:

- `A_terminal_move_stores_a_reason_far_longer_than_the_old_thousand_character_column`
- `A_terminal_move_records_both_the_revision_and_the_terminal_reason`
- `A_card_in_a_terminal_column_can_still_be_corrected`
- `A_terminal_move_rejection_names_the_reopen_endpoint`
- `Reopen_writes_one_revision_with_the_superseded_terminal_facts_and_clears_them`
- `Reopen_defaults_to_the_backlog_column_and_never_spawns`
- `A_reopened_card_recloses_with_a_fresh_completion`
- `Reopen_with_a_stale_token_is_a_conflict_and_writes_no_revision`
- `Reopen_of_a_live_card_or_into_a_terminal_column_or_without_a_reason_is_rejected`
- `Reopen_of_an_archived_card_is_refused_until_unarchive`
- `Reopen_deletes_no_review_checkpoint`
- `Reopen_when_no_live_column_exists_is_a_conflict`

Wall 8m22s; build 417.3558707s; test host 62.6715251s (startup 46.6396901s,
tests 14.3838398s, teardown 1.6480027s). Build and row slots granted, each waited 0s.
The tool retained red build outputs (`refused: still-present`); those exact task-owned outputs
are manually removed after the executor is confirmed dead. Receipts are not rewritten.

The predecessor's CP-2 line, preserved from its final report:

```text
CHECKPOINT CP-2 commit=896d83caee1400d991848da964e91b82dd7a75e0 build=ok filter=/*/Antiphon.Tests.Application/(CardEstimateTests*)|(CardCorrectionIntegrationTests*)/* executed=51 passed=39 failed=12 skipped=0 trx=/work/worktrees/task-42bfc023/.antiphon/checkpoints/20261023-030544-e455/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=896d83caee1400d991848da964e91b82dd7a75e0 sourceState=clean buildSource=verified
```

## Required final verification and scope

Run plan CP-2 once on the commit containing this report and repair, through the checkpoint tool
with `--expected-source-sha` set to committed HEAD, one build, the exact plan filter, and bounded
12-minute build/row and 15-minute total deadlines. Wait until the observer no longer returns 75.
Inspect fresh TRX for all 43 correction methods and eight estimate cases, including each of the
twelve repaired failures; minimum 51, zero failures/skips. Validate `report.json` using
`scripts/validate-checkpoint-receipt.ps1`. This is the corrective CP-2 rerun after the predecessor's
red. No repetitions are required after green.

Plan CP-1 already supplied the eight absent-feature red witnesses; the same estimate methods
are included in CP-2's green selection. CP-3 through CP-6 belong to unassigned S2/S3.
No V-n, R-n or manual product acceptance rows are defined for S1. The explicit brief and plan
exclude Unit, whole-suite, E2E and Windows runs; no whole-assembly run or unbounded selection
is made. Round is Final for this explicit S1 scope.

One unlisted tooling build was necessary to bootstrap the checkpoint tool:
gated `dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c0151-repair-tool/`.
It passed with four compiler warnings, zero errors; build 6.12s, slot granted, waited 30s,
held 7s. The first diagnostic launch refused the missing ignored manifest before any build/test
started (the attempted local `python3` file writer was unavailable); after writing the manifest,
the single diagnostic run above executed. No additional product build was run outside checkpoints.

After green, remove task-owned alternate outputs, run the evidence guard over both the assigned
base..HEAD and full S1 chain base..HEAD, fetch master, run `merge-tree --write-tree origin/master HEAD`
without merging, and verify the remote task ref. Record actual outcomes in the final report.

## Migration, placement and next stage

`GET /api/runner-defaults` returned server2; `GET /api/session-runners` confirmed the assigned
Linux runner available. No host or platform pin was passed. No desktop or Windows qualification
operation was performed.

Migration `20261023030321_AddCardEstimateDays` remains unchanged. Static inspection confirms Up
adds just nullable `numeric(6,2)` fields to Cards and CardRevisions, Down drops those two fields,
and the snapshot adds only the matching properties. CP-2 exercises Up in isolated PostgreSQL.
**Master-only, no RC pick**, as the feature plan requires. No live database mutation or restart.
Restart: none for this fixture repair; eventual activation of the original S1 server feature
requires a server restart only when it is admitted to a product release.

The plan has zero PC-n/variant rows. Every deliberate control and missing-control discovery
remains pending for post-land SourceLanding Mutation. Ordinary integration green discharges none.
After implementation and ordinary verification are green, next is ordinary **Review**, with
this continuation task retained as the appointed S1 landing owner; caller lands after Review
and commissions SourceLanding Mutation.
