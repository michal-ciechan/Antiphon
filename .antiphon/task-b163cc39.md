# CARD-1446 Code report (task b163cc39): milestone 3, S5 complete and green

Branch `feat/card-task-b163cc39`, worktree `/work/worktrees/task-b163cc39` (server2 runner mirror), start ref
`7febbb891d4317bc061bd4a93f4d1328a7ea5673` (origin/master at dispatch; milestones 1 and 2 landed). **Tested tip
`5d2cb6f2ff52ffb412b526ed1aec0a6eb5b96cb6`**; this report commit is the final tip and touches only this file.
Plan `docs/superpowers/plans/2026-10-22-card-1446-stale-landings-plan.md` and the milestone-2 amendment are unchanged
(both on master). New amendment carrying the S5 rows and the one unlisted row:
`docs/superpowers/plans/2026-10-22-card-1446-stale-landings-plan-m3-checkpoints.md` (the `--plan` the tool ran; `import --plan`
validated it before the first commit: `imported 4 rows`). Lane: `GET /api/runner-defaults` -> `globalRunnerId: server2`;
`GET /api/session-runners` lists desktop (windows), server2 (linux), server2-temp; every row ran on server2, no `-Runner`,
no `-Platform`. No session waits for input (plan D-6). Nothing was deleted outside test schemas and this task's own build
output. Wall time about 80 minutes against a 60-minute budget (two build-and-run cycles were repeated, see Repetitions).

## Outcome

S5 (plan D-5) is implemented, red-witnessed, green and receipt-validated at `5d2cb6f2f`: the
`WorktreeResidueCandidateCursor.LastOutcomeKey` column with its migration, candidate-row dedupe in the SettledTask and
Publication lanes, the `candidates=M rows=N unchanged=M-N` log line, `Retention:WorktreeResidueHeldRowRetentionDays` and
`DataRetentionService.PruneWorktreeResidueHeldRowsAsync`, `WorktreeResidueCandidateRowTests` (V-11, V-12), CP-14..CP-16 and
the living docs. CARD-1459 and CARD-1468 were not touched; no S5 row needed their lines.

## Commits (base..HEAD, oldest first)

| SHA | Slice | Note |
|---|---|---|
| `efbab099af52af0fd8aae95581343346dc4b6096` | S5-test | entity column, `AppDbContext` max length 200, migration pair + snapshot, `RetentionSettings` property, `appsettings.json`, prune skeleton (returns 0), test class, m3 amendment; CP-14 build failed here (CS0738/CS0246: the test's `IWorktreeManager` stub lacked the `WorktreeInfo` namespace import) |
| `d5cf68dc4bca3930c320c2e4fba7c4881ef14459` | S5-test fix | one `using` plus `new[]` instead of a collection expression in the test; **CP-14 red here** |
| `0e157c67fd12e5c6b229702f9f668a3977d46507` | S5-impl | sweep dedupe, log line, retention pass, `RunOnceAsync` wiring, docs; CP-15/RV-2/CP-16 all passed here but the run verdict was RED exit 3 on my RV-2 `Min` miscount (18; the class has 15 methods) |
| `5d2cb6f2ff52ffb412b526ed1aec0a6eb5b96cb6` | amendment fix | RV-2 `Min` 15 with the reason; **CP-15, CP-16, RV-2 GREEN and validated here**; source identical to `0e157c67f` outside the amendment file |

## Files changed

- `server/Domain/Entities/WorktreeResidueRun.cs`: `WorktreeResidueCandidateCursor.LastOutcomeKey` (string?, 200).
- `server/Infrastructure/Data/AppDbContext.cs`: `HasMaxLength(200)` on the new column.
- `server/Migrations/20261022095749_AddWorktreeResidueCursorLastOutcome.cs` + `.Designer.cs`, `AppDbContextModelSnapshot.cs`:
  generated with `dotnet tool restore` + `dotnet ef migrations add ... --project server` through `scripts/build-slot.ps1`
  (slot held 112 s). `Up` adds one nullable `character varying(200)` column; `Down` drops it. Additive and safe on the
  existing database (no backfill, no index, no constraint); applied by the server's startup `Database.Migrate()`
  (`Program.cs:1048`). Id `20261022095749` sorts after `20261022015609_AddRuntimeActionConversationKey` (latest on current
  origin/master `7b3955823`), after the queued CARD-0167 S2 `AddProviderEndpoints` `20261022074212`, and after
  `20261020142734_AddStandingAutomaticFreshRecovery` (latest on origin/rc/2026-10-20 `ceb6e3d6b`). Snapshot diff is
  exactly the one property; the Designer equals the snapshot.
- `server/Application/Settings/RetentionSettings.cs`: `WorktreeResidueHeldRowRetentionDays` (30; `<= 0` disables, the
  class's existing convention). `server/appsettings.json` `Retention` block mirrors it.
- `server/Application/Services/DataRetentionService.cs`: `PruneWorktreeResidueHeldRowsAsync` deletes
  `WorktreeResidueRunCandidates` with `Outcome in (Held, Preview)` and `EvaluatedAt < now - days` in one
  `ExecuteDeleteAsync`; run headers and action rows (`Queued`, `Refused`, `Partial`, `Removed`) are never selected.
  Called from `RunOnceAsync` after the subscription-usage pass; `DataRetentionSweepResult` gains
  `WorktreeResidueHeldRows = 0` (positional default, existing callers unchanged). `Preview` is included as the plan says
  although the sweep never writes that outcome today (preview rows are `Held` rows on a `Preview=true` run).
- `server/Application/Services/WorktreeResidueSweepService.cs`: `PersistRunAsync` loads one cursor per candidate up front
  (`task:<id:N>` for every evaluated task, `landing:<opId:N>` for every lane operation; `LoadCursorsAsync`, keyed
  dictionary). `ShouldWriteRow` computes `<Lane>|<Outcome>|<ReasonCode>` (`OutcomeKey`, clipped to 200): on a non-preview
  run it creates the cursor if missing, stamps `LastOutcomeKey` and `LastEvaluatedAt`, and returns false only for a
  `Held` outcome whose key equals the stored one; action rows are always written; a preview run returns true and does
  not touch the cursor (the operator asked to see every row, and a change seen only in a preview must still be recorded
  by the next daily run). The Inventory lane is unchanged. Run counters (`Candidates`, `Held`, ...) are incremented
  outside the write decision, so they still count every evaluation. The service logs
  `Worktree residue run {RunId} preview={Preview} candidates={Candidates} rows={Rows} unchanged={Unchanged}` at
  Information. The SettledTask lane's 24-hour refusal cooldown (`PersistCooldownAsync`, a per-call DB query that could
  add a second cursor for a key the dedupe had just created) became `StampTaskCooldown` on the same preloaded cursor;
  same columns, same values (`NotBefore = now + 24h`, `LastEvaluatedAt = now`). `StampLandingCooldown` takes the
  dictionary for the same reason. Cursor rows grow once to the number of tasks with a worktree path (one row per task,
  then in-place updates), which is what D-5 asks for ("the cursor is updated either way").
- `tests/Antiphon.Tests/Application/WorktreeResidueCandidateRowTests.cs` (new; test DB isolated schema, `FakeTimeProvider`,
  `TaskWorktreeRetirementService` + an empty `IWorktreeManager`, `Execute=false`): `C1446_unchanged_held_rows_are_not_rewritten`
  (3 settled tasks + 1 published task with a worktree path + its landing = 5 candidates: run 1 writes 5 Held rows in both
  lanes, run 2 a day later writes 0 while `Candidates=5 Held=5`, the log line says `candidates=5 rows=0 unchanged=5`,
  5 cursors carry `|Held|` and `LastEvaluatedAt` of the second run), `C1446_changed_verdict_and_preview_write_rows`
  (one task flips to Working: a preview writes all 5 rows with `live_owner` for it and consumes nothing, the next daily
  run writes exactly that one row, the run after writes 0), `C1446_retention_prunes_only_old_held_rows` (7 rows: old Held,
  old Preview, fresh Held, old Queued/Refused/Partial/Removed; days=0 removes 0, days=30 removes exactly the two old
  Held/Preview rows, the run header survives, a second pass removes 0).
- `docs/orchestration-loop.md` (residue activation paragraph: the dedupe rule, the log line, the retention setting),
  `docs/ops-http.md` (run route: a daily run's page omits unchanged Held candidates; counters still count them; preview
  lists every row).

## Filter rewrites (brief item; manifest/amendment only, plan untouched)

- CP-14 `/*/*/WorktreeResidueCandidateRowTests/C1446_unchanged_held_rows_are_not_rewritten`: **unchanged**; one class and
  one method, no OR operand, parses and ran as written.
- CP-15 `/*/*/WorktreeResidueCandidateRowTests/*`: **unchanged**; one class, no OR operand.
- CP-16 `/*/*/(WorktreeResidueRegistrationTests/*)|(WorktreeRetirementRaceTests/*)|(CardWorktreeMirrorCleanupTests/*)` ->
  `/*/*/(WorktreeResidueRegistrationTests*)|(WorktreeRetirementRaceTests*)|(CardWorktreeMirrorCleanupTests*)/*` (same roster;
  the milestone-2 amendment already carried this rewrite but never ran it). Ran 84 cases: 3 registration, 54 race
  (44 methods, 15 `[Arguments]` rows), 27 mirror-cleanup.

## Checkpoints (closed list CP-14..CP-16 plus the unlisted RV-2; checkpoint tool on server2)

Tool built under `scripts/build-slot.ps1` into `bin-c1446-tool/` (15 s), used for `import`, `start`, `wait`; deleted after
validation. Red row at the test commit before any implementation commit existed.

CP-14 first attempt at `efbab099a` (build failed on the test stub; fixed in the next commit, no implementation involved):
```
CHECKPOINT CP-14 commit=efbab099af52af0fd8aae95581343346dc4b6096 build=failed filter=/*/*/WorktreeResidueCandidateRowTests/C1446_unchanged_held_rows_are_not_rewritten executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=efbab099af52af0fd8aae95581343346dc4b6096 sourceState=clean buildSource=unknown
```
Red run `20261022-100431-e00e` at `d5cf68dc4` (verdict RED exit=1 as intended; V-11 witness):
```
CHECKPOINT CP-14 commit=d5cf68dc4bca3930c320c2e4fba7c4881ef14459 build=ok filter=/*/*/WorktreeResidueCandidateRowTests/C1446_unchanged_held_rows_are_not_rewritten executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b163cc39/.antiphon/checkpoints/20261022-100431-e00e/rows/CP-14/run.trx slot=granted waited=105s dirty=0 source=d5cf68dc4bca3930c320c2e4fba7c4881ef14459 sourceState=clean buildSource=verified
```
`failures.md`: `(await RowsAsync(db, second.Id)).Count should be 0 but was 5` (`second-run-rows`): the second unchanged run
wrote N=5 rows, the plan's stated red reason. The first-run assertions (5 candidates, 5 Held, both lanes) passed, so the
seed is sound.

Green run 1 `20261022-101614-6c82` at `0e157c67f`: CP-15 `executed=3 passed=3`, RV-2 `executed=15 passed=15`, CP-16
`executed=84 passed=84`, 0 failed, 0 skipped, every row `slot=granted sourceState=clean buildSource=verified`, but
**verdict RED exit=3** because the amendment's RV-2 `Min` was 18 against 15 methods; `bin-c1446-s5/` kept, so
`validate` said `cleanup_refused`. Not a test failure; the floor was corrected in `5d2cb6f2f` and the selection rerun.

Green run 2 `20261022-103141-7514` at `5d2cb6f2f` (**verdict GREEN exit=0**, outputs deleted):
```
CHECKPOINT CP-15 commit=5d2cb6f2ff52ffb412b526ed1aec0a6eb5b96cb6 build=ok filter=/*/*/WorktreeResidueCandidateRowTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-b163cc39/.antiphon/checkpoints/20261022-103141-7514/rows/CP-15/run.trx slot=granted waited=15s dirty=0 source=5d2cb6f2ff52ffb412b526ed1aec0a6eb5b96cb6 sourceState=clean buildSource=verified
CHECKPOINT RV-2 commit=5d2cb6f2ff52ffb412b526ed1aec0a6eb5b96cb6 build=reused filter=/*/*/WorktreeResidueSweepTests/* executed=15 passed=15 failed=0 skipped=0 trx=/work/worktrees/task-b163cc39/.antiphon/checkpoints/20261022-103141-7514/rows/RV-2/run.trx slot=granted waited=225s dirty=0 source=5d2cb6f2ff52ffb412b526ed1aec0a6eb5b96cb6 sourceState=clean buildSource=verified
CHECKPOINT CP-16 commit=5d2cb6f2ff52ffb412b526ed1aec0a6eb5b96cb6 build=reused filter=/*/*/(WorktreeResidueRegistrationTests*)|(WorktreeRetirementRaceTests*)|(CardWorktreeMirrorCleanupTests*)/* executed=84 passed=84 failed=0 skipped=0 trx=/work/worktrees/task-b163cc39/.antiphon/checkpoints/20261022-103141-7514/rows/CP-16/run.trx slot=granted waited=60s dirty=0 source=5d2cb6f2ff52ffb412b526ed1aec0a6eb5b96cb6 sourceState=clean buildSource=verified
```
`pwsh -NoProfile -Command "& ./scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261022-103141-7514/report.json
-ExpectedSourceSha 5d2cb6f2ff52ffb412b526ed1aec0a6eb5b96cb6 -Rows 'CP-15','CP-16','RV-2'"` ->
`CHECKPOINT SOURCE VALID source=5d2cb6f2ff52ffb412b526ed1aec0a6eb5b96cb6 rows=3` (exit 0).
Rosters (TRX): CP-15 the three `C1446_*` methods; RV-2 `C1213_ResidueBothFormsKeepsDirectory`, nine `C459_*`, three
`classify_*`, `execute_never_treats_legacy_landed_event_as_cleanup_authority`, `job_logs_information_summary_and_warning_per_kept_row`,
`validator_accepts_defaults_and_rejects_empty_cron`; CP-16 `C459_*` x3 registration, `C1017_*`/`C1032_*`/other race cases x54,
mirror-cleanup x27. Builds: s5red 300 s, s5 (run 1) 363 s, s5 (run 2) 438 s; CP-16 tests 216 s / 274 s
(`WorktreeRetirementRaceTests` 471 s / 831 s cumulative under load).

Repetitions per unchanged selection: CP-14 two runs (one build failure, one red); CP-15, CP-16, RV-2 two runs each (both
passing by counts; the first unvalidated for the `Min` floor, the second validated). Within the 3-normal budget; no flake.

## Unlisted builds and runs (each with its reason)

- **RV-2** `WorktreeResidueSweepTests` (15): S5 replaces the SettledTask lane's `PersistCooldownAsync` cursor write and
  changes when every candidate row is written, which this class covers (`C459_CooldownSurvivesRestart`,
  `C459_FairnessSurvivesRestart`, `C459_BudgetIsShared`, `C459_OnlyCompleteCountsRemoved`, ...) and CP-16 does not. Listed
  in the m3 amendment with this reason; it reused CP-15's build.
- `dotnet ef migrations add` through `scripts/build-slot.ps1` (one server build, 112 s): the plan's migration procedure.
  Its `server/bin` and `src/*/bin` output was deleted before the first checkpoint run.
- The checkpoint tool build (15 s, build slot) and `import --plan` on the m3 amendment (table validation, no test).
  No other build or test driver ran; no whole-suite run.

## Deviations from the plan and decisions it left open

- A preview run leaves `LastOutcomeKey` alone (the plan only says preview writes every row). Reason above; pinned by
  `C1446_changed_verdict_and_preview_write_rows`.
- The `candidates=/rows=/unchanged=` line is logged by `WorktreeResidueSweepService.PersistRunAsync` (the job's one
  service call), not by `WorktreeResidueJob`, which only sees the legacy result DTO; asserted by V-11.
- `PersistCooldownAsync` is replaced (RV-2 covers it); `StampLandingCooldown` changed signature only.
- RV-2's `Min` was first written as 18, a miscount, which cost one extra build-and-run cycle.
- Red rows ran through the checkpoint tool, not `run-checkpoint.ps1`, as the brief directed.

## Verification design outcomes

| ID | Outcome |
|---|---|
| V-11 | pass (CP-15 `C1446_unchanged_held_rows_are_not_rewritten`, `C1446_changed_verdict_and_preview_write_rows`; red at CP-14) |
| V-12 | pass (CP-15 `C1446_retention_prunes_only_old_held_rows`) |
| V-13 | pass (CP-16 84/84; RV-2 15/15 beside it). CP-5 and CP-13 were discharged by milestones 1 and 2 |

PCs pending for method-scoped SourceLanding Mutation (plan "Mutation targets", S5 part): the `LastOutcomeKey` equality
(`string.Equals` in `ShouldWriteRow`; detected by `C1446_unchanged_held_rows_are_not_rewritten` if inverted and by
`C1446_changed_verdict_and_preview_write_rows` if made always-true); the `preview` early return (same second method); the
retention `Outcome` filter and the `EvaluatedAt < cutoff` comparison and the `<= 0` switch
(`C1446_retention_prunes_only_old_held_rows`). **Gap disclosed:** the `outcome == Held` restriction (action rows always
written) has no witness, because every S5 test runs with `Execute=false` and no action row is produced; a mutant that
dedupes action rows too would survive CP-15, CP-16 and RV-2. Review/Mutation should treat it as a missing control
(a sweep-level Execute test with a retiring release, or the Publication lane with `AgentTaskLandService`, would cover it).

## Data-safety binding (brief)

`WorktreeResidue:Execute` stays false; S5 adds no deletion of worktree bytes. The only new deletion is the retention pass
over `WorktreeResidueRunCandidates` log rows (Held/Preview older than 30 days; `0` disables; action rows and run headers
kept, so every queued cleanup keeps its provenance). The dedupe loses no verdict: counters still count, changed verdicts
are written, previews list everything. `LastOutcomeKey` is a persisted cursor column, so the dedupe survives a restart
and the first run after deploy writes every Held row once (null key). Secrets custody is untouched.

## Landability, evidence policy, rc

- `git fetch origin master` -> `7b3955823570598bc1ab81126699520b755120ca`; `git merge-tree --write-tree origin/master HEAD`
  exit 0, no conflicts, at `5d2cb6f2f` and again at the report tip (see the final message).
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 7febbb891... -HeadRef HEAD`: `commits=4 entries=0 violations=0`
  at `5d2cb6f2f`; rerun at the report tip in the final message.
- **rc (`origin/rc/2026-10-20` = `ceb6e3d6b`), sequential cherry-pick in a discarded scratch worktree:** `efbab099a`
  (S5-test) and `d5cf68dc4` apply clean; `0e157c67f` (S5-impl) conflicts on `WorktreeResidueSweepService.cs` (rc lacks
  S4c's Publication lane pacing, which the dedupe is woven into; the CARD-1432 chain and S4c are master-only) and
  `docs/orchestration-loop.md` (rc lacks the S4d paragraph the sentence extends); `DataRetentionService.cs` and the
  settings apply. So: **rc-eligible on their own** are the entity column, the migration, `Retention:WorktreeResidueHeldRowRetentionDays`,
  the prune pass and its test (V-12); **not rc-eligible** without S4c are the sweep dedupe, its two V-11 tests and the
  docs. One caveat for the migration on rc: its Designer was generated from master's model and carries
  `SessionRuntimeActions.ConversationKey` (migration `20261022015609`, not on rc); the `Up`/`Down` are still correct and
  additive, but an rc port should regenerate the Designer on rc (or pick `20261022015609` first) to keep the model
  snapshot consistent.

## Deploy and operator notes

New setting with a safe default, one additive migration (above). After deploy the first daily run writes every Held
candidate once (null cursor key), subsequent runs only changed verdicts; the run route page therefore shrinks on daily
runs while `candidates`/`held` counters keep the full count, and `POST .../worktree-residue/preview` still lists every
row. `Retention:WorktreeResidueHeldRowRetentionDays=0` disables pruning (plan Rollback); the dedupe has no switch because
it loses no verdict.
