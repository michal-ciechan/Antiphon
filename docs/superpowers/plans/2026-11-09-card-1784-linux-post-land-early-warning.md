# CARD-1784 — Linux post-land early warning

Plan date: 2026-11-09. Base: `d861149d0738bef6019e1a437efc69d245512ab9`.
Card: Linux post-land early warning on server2 after each land batch, not only at rc time.

This plan does not add or change a session that waits for input. Nothing in it releases a seat, and the CARD-1083 parking default is unchanged.

## Intended behaviour

After a Merge batch is on `origin/master`, a Linux host runs one early-warning pass for that tip. The pass is the Unit lane in namespace and single-prefix shards, plus the Integration classes the impacted-classes selector names for the batch diff. It writes one TRX per shard and one receipt keyed by the tip SHA. A one-line summary of new reds since the previous tip, naming the introducing batch, is what the orchestrator reads. New reds get a Debug task under the rc run-group repair loop before the rc cut. The Windows Full qualification stays the native gate.

### Before

A Merge batch's pre-land gate is the fixed high-radius subset plus the selector rows for that diff (`docs/orchestration-loop.md` lines 683-710). Per-task Final does not run the Unit lane (`docs/testing-and-build.md` lines 321-325 and 1940-1942). The first whole Unit or Integration execution of the landed tree is the Windows Full rc run. Runs 231f573c's groups 2, 71 and 117 were already red on Linux at the master tip and were first seen hours later on Windows. There is no receipt keyed by master SHA, no post-land attention row, and no hourly coalesced Linux lane.

### After

```
caller (orchestrator after a batch, or the hourly job)
  -> scripts/post-land-early-warning.ps1 -Range <base>..<tip> -ReceiptRoot <dir>
       HEAD must already equal <tip> and the tree must be clean
       docs-only diff -> receipt skipped-docs, no slot, no TRX
       lock held or last start < 60 min -> receipt skipped-busy / skipped-hourly
       else one build slot, then one shard at a time, slot released between shards
       TRX at .antiphon/post-land-warning/<tip>/<shard>.trx
       receipt <tip>.json: previous tip, new reds, owed Windows filters, summary line
  -> S3: POST the receipt; GET /api/attention shows one Warning row only when
       the latest tip has new reds or a shard with no TRX
  -> orchestrator reads that row and dispatches Debug before the rc cut
```

S1 is useful with no server change: the script prints the summary line and writes the receipt. The orchestrator dispatches from that line. S3 puts the same line on the attention feed. S4 starts the same script once an hour on a Linux server process when an operator has pointed it at a worktree. S4 does not run tests inside the server.

### What stays unchanged

- The build-slot broker stays FIFO with no priority (`src/Antiphon.SessionRunner/BuildSlotBroker.cs` lines 15-19). `MaxConcurrent` stays 2 (`BuildSlotSettings.cs` line 17). The warning holds at most one lease.
- Nightly, the Windows Full rc run, Windmill `u/lndcobra/antiphon_nightly_tests`, and per-task Final stay as they are. No local Scheduled Task.
- The Merge pre-land subset still runs before land. This warning runs after the tip is on master.
- `AttentionKind` values 0 through 65 stay put. `WorkspaceDiskLow` remains 65 (`AttentionDtos.cs` lines 409-416).
- The impacted-classes derive scanner stays one global pattern set (`scripts/impacted-classes.ps1` lines 313-398 and 456-492). This plan does not add a derive pattern.
- Product hosts still track the rc tip. A master land is still not a restart. The script tests a clean checkout at the master tip; it does not test the running server's checkout.
- No session, park rule, or seat release changes.

### Files and line ranges

| Slice | Change |
|---|---|
| S1 | New `scripts/post-land-early-warning.ps1` and `scripts/lib/post-land-early-warning.ps1` (ASCII). New `tests/post-land-unit-shards.json`. New `tests/Antiphon.Tests/Scripts/PostLandEarlyWarningTests.cs`. One subsection after `docs/testing-and-build.md` line 1956. One paragraph after `docs/orchestration-loop.md` line 778. |
| S2 | `tests/impacted-classes.json` land-notification classes (lines 103-112) gain `PreDispatchFailureDeliveryTests`. New kinds `repository-lease-reflected-constructors` and `migration-generated-sql-text` (`windows: required`). New methods on `tests/Antiphon.Tests/Scripts/ImpactedClassesSelectorTests.cs`. |
| S3 | Append `PostLandEarlyWarning = 66` at `AttentionDtos.cs` after line 415. Project it from `AttentionService.GetAsync` after the disk-low row (`AttentionService.cs` line 298). `POST /api/post-land-warnings` beside `AttentionEndpoints.cs` lines 19-36. Append a `DbSet` after the last set in `AppDbContext.cs` (the list runs through line 143 and on). New entity, new migration sorted after master's latest (`20261108121426_Card1773TranscriptPromptStampIndex` on this base). Client kind union `client/src/api/attention.ts` lines 220-225, visual `attentionVisuals.ts` lines 63-66, and the kind list in `attentionVisuals.test.ts` line 88. Pin in `AttentionKindWireTests.cs` lines 82-91. |
| S4 | New `PostLandEarlyWarningJob` and settings, default `Enabled` false. Register next to KeepSix (`server/Program.cs` lines 1212-1213, `HangfireConfiguration.cs` lines 122-130) as `antiphon:post-land-early-warning`, cron `0 * * * *` UTC. The job returns without waiting for tests. |

## Ground truth

Read on 2026-11-09 from this checkout and from `GET /api/runner-defaults` (revision 2, `globalRunnerId` set, `kindDefaults` empty) and `GET /api/session-runners` (one dispatch-eligible Linux runner, one dispatch-eligible Windows runner, one draining runner that is not eligible). Those ids are live facts. Product code, the script, and the checkpoint rows do not embed them and do not pass `-Runner` or `-Platform`.

The card names `docs/investigations/2026-11-08-post-land-retrospective-rc-regressions.md` (Investigate 4651491a). That file is not in this checkout. `docs/orchestration-loop.md` line 812 names the same investigate id and does not carry the file. The replay below uses the why-missed notes that are on this tree.

The brief names rc runs `231f573c` and `dce78ee0`. Groups 2, 4, 71, 96 and 117 are written up for `231f573c` only. `dce78ee0` does not appear in `docs/investigations/`.

| Group | Class | Category on this tree | Linux at the cited tip | What this warning does |
|---|---|---|---|---|
| 2 | `ProcessSpawnLimitTests` | Unit (`ProcessSpawnLimitTests.cs` line 15), namespace `Antiphon.Tests` | Red, same assertion (`2026-11-09-why-missed-process-spawn-limit.md`) | The root Unit shard runs it on every non-docs batch. The `spawn-limit-rosters` kind is already in `tests/impacted-classes.json` lines 233-250. |
| 4 | `Card1277PtyHostFixtureTests` | Integration | Green, 2/2 at `653f1145d` (`2026-11-09-why-missed-pty-host-fixture.md`) | Does not fail. The note classifies a load-sensitive wait, present since `5e21b61a2`, and says no diff selector would have picked the class for that tip. Not a catch for this card. |
| 71 | `PreDispatchFailureDeliveryTests` | Integration, Slow | Red, 13 total, 1 failed (`2026-11-09-why-missed-pre-dispatch-failure-delivery.md`) | A Unit shard does not run it. At `ae5b545d9` the selector returned `land-notification,migrations` and did not name this class. S2 adds the class to `land-notification`, which is the range that already matches. |
| 96 | `Card1773TranscriptPromptStampMigrationTests` | Integration, Slow | Green, 3/3. The red is `Environment.NewLine` on Windows (`2026-11-09-why-missed-transcript-prompt-stamp-migration.md`) | Linux cannot show it. S2 adds the note's `windows: required` kind. The summary lists that filter as owed. A Linux pass is not a pass for it. |
| 117 | `RepositoryMutationLeaseTests` | Integration | Red, 34 total, 1 failed (`2026-11-09-why-missed-repository-mutation-lease.md`) | A Unit shard does not run it. The selector on `0f76e7b0a9` does not name it. S2 adds the note's kind. |

CARD-1728 (`docs/investigations/2026-11-07-debug-unit-shard-completion.md`): the A-L class-letter OR ran 2,177 cases in about a minute, then about 37 minutes of `ProcessSpawnLimit` serial work, 1,904 s of that in `Antiphon.Tests.Checkpoints`, and hit the 45-minute cap with no TRX. Discovery was 47 s for a namespace filter, 116 s for `/*/*/*/*[Category=Unit]`, and 261-419 s for a 12-operand letter OR. A killed host writes no TRX. Those numbers are one loaded run, not a p95.

The derive pool is global. A kind with `HasDerive` selects every file matched by every kind's patterns (`impacted-classes.ps1` lines 714-724). Adding a pattern for group 71 would also attach those files to the migrations kind. S2 adds class names only.

## Decisions

D-1. The trigger is the script, not a land hook and not Windmill. A `-Land` hook fires once per task, on whichever host the orchestrator is on, and would start a lane per card during a burst. Windmill is the cross-machine nightly on the Windows checkout. A local Scheduled Task is forbidden. The orchestrator runs the script after each Merge batch. S4's Hangfire job is the hourly backstop and does not replace the script.

D-2. The script refuses to run shards unless it is on Linux (`$IsLinux`). `-Plan` is pure and is what the tests call. A desktop invocation of the run mode exits 3 with `refused=not-linux` before any slot. The test seam is `POST_LAND_WARNING_PLATFORM` (same shape as `C671_PLATFORM`).

D-3. The caller prepares the checkout. The script checks `git rev-parse HEAD` equals the range tip and that the tracked tree is clean, then stops. It does not fetch, reset, or create a worktree. S4's job may `git fetch origin master` only inside the operator-configured worktree, and it still refuses a dirty tree. The worktree path is a setting. It is empty by default. The job no-ops when the path is empty or the process is not Linux.

D-4. One shard set, one lease at a time. The build takes one slot. Each shard takes its own slot through `scripts/build-slot.ps1 -Label post-land-early-warning` and uses `--no-build`. The script never wraps the loop in one lease (CARD-1627). It does not take the second slot. Reviews keep the other lease. The broker is not given a priority. A shard over 30 minutes is reported as an R1 Debug obligation; the script does not widen a timeout.

D-5. Coalesce for 60 minutes from the last start, and skip a tip that already has a completed receipt. A second start while the lock pid is alive is `skipped-busy`. A dead pid is a stale lock and may be replaced. `-Force` is the operator override and is not the default.

D-6. Docs-only means every `git diff --name-only --diff-filter=ACMRTD` path is under `docs/` or is a `*.md` file, and there is at least one path. Any other path runs the lane. An empty diff skips as docs-only.

D-7. Unit shards come from `tests/post-land-unit-shards.json`, not from an A-L letter OR. Non-Checkpoints Unit classes use one namespace filter `/*/<namespace>/*/*[Category=Unit]`. Checkpoints classes use one single-prefix filter per initial letter, `/*/Antiphon.Tests.Checkpoints/<Letter>*/*[Category=Unit]`. A letter whose first measured journal exceeds 30 minutes is split later into `/*/*/ExactClass/*` shards. Method-level `[Category("Unit")]` on a class with another category stays outside these shards, the same gap CARD-1573 already records for `MatrixDefaultComplexityCreateTests`.

D-8. Integration classes are the `-Range` selector output (not `-MergeSubset`) whose source class carries `[Category("Integration")]`. `windows: required` filters are listed as `owed-windows` and are not counted as passed. Groups of at most eight classes use a parenthesized trailing-star OR. A longer list is several shards, still one at a time.

D-9. New reds are failed `Class.Method` results in this tip's TRX that were not failed in the newest completed receipt whose tip is an ancestor of this tip. The first tip treats every failure as new. The introducing batch is the `-Range` argument, stored in full and printed as 12-hex ends.

D-10. The attention row is computed stored state, not a free-form note. One row for the latest tip, kind 66, key `post-land-warning:<tipSha>`, severity Warning, only when `new-reds` is non-zero or a shard has no TRX. A green latest tip clears the row. The server does not dispatch. The orchestrator does, using the rc repair loop's Debug brief, before the cut.

D-11. S4 `Enabled` defaults false. Turning it on is an operator step on the Linux host that holds the configured worktree. The job also returns immediately when `OperatingSystem.IsLinux()` is false, so a desktop flag cannot start the lane.

Rejected: running the Unit lane inside the Hangfire worker or the Kestrel process. Rejected: a phone-home remote-exec on the session runner. Rejected: changing `MaxConcurrent` or cutting in front of the FIFO. Rejected: claiming groups 4 and 96 as Linux catches. Rejected: a derive pattern for group 71.

## Design of the script

`scripts/lib/post-land-early-warning.ps1` is the pure part. The entry script loads it and, in run mode, detaches with `setsid` so the caller can return. `-Plan` prints the decision and does not detach, lock, or start dotnet.

Receipt JSON fields: `tipSha`, `baseSha`, `batch`, `state`, `startedAt`, `finishedAt`, `previousTipSha`, `shards` (id, filter, trx, executed, passed, failed, skipped), `newReds`, `owedWindows`, `summaryLine`.

Summary line, one physical line:

`post-land tip=<40 hex> batch=<12 hex>..<12 hex> new-reds=<n> <Class.Method>[,...] owed-windows=<n>`

A missing TRX uses `new-reds=unknown no-trx=<shardId>` and still lists any completed shards. The lane continues after a red shard so one batch yields one summary.

Exit codes: 0 skipped or completed; 1 completed with new reds; 2 bad arguments or a dirty tree or a HEAD that is not the tip; 3 `refused=not-linux`; 4 the run finished and the attention POST failed (receipt still written). S1 does not POST, so exit 4 does not apply until S3.

TRX and receipts under `.antiphon/` stay gitignored (`.gitignore` line 55). The script does not force-add them.

## Selector rows (S2)

Add `PreDispatchFailureDeliveryTests` to the existing `land-notification` class list (`tests/impacted-classes.json` lines 103-112). That is the kind the group-71 range already matches.

New kind, class list only:

```json
{
  "id": "repository-lease-reflected-constructors",
  "globs": [
    "server/Infrastructure/Git/LandingGit.cs",
    "server/Infrastructure/Git/RepositoryMutationLease.cs",
    "tests/Antiphon.Tests/TestHelpers/RepositoryLeaseCrashWorker.cs"
  ],
  "classes": ["RepositoryMutationLeaseTests"]
}
```

New kind, `windows` the string `required`:

```json
{
  "id": "migration-generated-sql-text",
  "globs": [
    "server/Migrations/*Card1773TranscriptPromptStampIndex*.cs",
    "server/Migrations/*AddTranscriptHotPathIndexes.cs",
    "tests/Antiphon.Tests/Application/Card1773TranscriptPromptStampMigrationTests.cs",
    "tests/Antiphon.Tests/Application/TranscriptHotPathMigrationTests.cs"
  ],
  "classes": ["Card1773TranscriptPromptStampMigrationTests", "TranscriptHotPathMigrationTests"],
  "windows": "required"
}
```

No pty-host kind. Group 4 was green on Linux, and the note says the row would not have selected the class for that rc.

## Attention and migration (S3)

Entity `PostLandWarningReceipt`: `TipSha` char(40) unique, `BaseSha`, `Batch`, `State`, `SummaryLine`, `NewRedCount`, `OwedWindowsCount`, `PayloadJson`, `CreatedAt`. Additive table. `Down` throws `InvalidOperationException` with the text `PostLandWarningReceipts holds the previous-tip baseline`. It does not drop the table. Sort the migration id after the latest `server/Migrations/2026*.cs` on the Code branch's merged master, which is `20261108121426` on this plan's base. Do not reuse that id.

`POST /api/post-land-warnings` accepts the receipt JSON from the script. The script sends `X-Antiphon-Task-Token` from the environment and never prints it. A second POST for the same tip replaces the payload. `GET /api/attention` projects the latest tip only.

Client: append the kind. Do not renumber. The visual is Warning, report only, review bucket, hint that the orchestrator dispatches Debug and the row stops nothing. The all-kinds test list gains `PostLandEarlyWarning`.

## Hourly job (S4)

`PostLandEarlyWarning:Enabled` defaults false. `Cron` defaults `0 * * * *`. `Worktree` defaults empty. `ReceiptRoot` defaults empty, and the script then uses `<worktree>/.antiphon/post-land-warning`. On each tick the job: returns if disabled, if `Worktree` is blank, or if the process is not Linux; otherwise fetches `origin master` in that worktree and starts `pwsh -NoProfile -File scripts/post-land-early-warning.ps1 -Range <lastCompletedTip>..<HEAD>` under `setsid`. It does not wait. The script's own lock and 60-minute coalesce are the bound. The job does not call `dotnet`.

## Doc contract

`docs/testing-and-build.md`, new subsection after line 1956, states that the post-land warning runs the shard manifest one shard at a time through the build-slot gate, skips a docs-only batch, and is not a per-task Final and not a replacement for the Windows Full run.

`docs/orchestration-loop.md`, new paragraph after line 778, states that a post-land summary with new reds gets one Debug per new class under the rc run-group repair loop, before the rc cut, and that the Windows qualification is left running.

Both sentences are unbroken in those files. `PostLandEarlyWarningTests` reads the files and fails if either sentence is absent.

## Verification design

Witnesses are red first: Code commits the new test, sees it fail because the script or the kind is absent, then commits the implementation. One mutant per witness, restored before the next:

| Id | Witness | Mutant that must make it red again |
|---|---|---|
| V-1 | `Docs_only_range_skips` | Treat a `.cs` path as docs-only. |
| V-2 | `Code_range_plans_namespace_shards_and_one_lane` | Plan a second shard while the lock pid is alive. |
| V-3 | `Start_inside_sixty_minutes_is_hourly` | Treat the coalesce window as zero. |
| V-4 | `New_reds_are_failures_absent_from_the_ancestor_receipt` | Mark every failure as new. |
| V-5 | `Summary_line_names_tip_batch_and_new_reds` | Drop the tip SHA from the line. |
| V-6 | `Non_linux_run_refuses_before_a_slot` | Start a shard when the platform seam is `windows`. |
| V-7 | `Manifest_uses_no_letter_or_and_partitions_unit_classes` | Emit `(A*)\|(B*)\|(C*)\|(D*)\|(E*)\|(F*)\|(G*)\|(H*)\|(I*)\|(J*)\|(K*)\|(L*)`. |
| V-8 | `Integration_list_drops_unit_classes_and_records_windows_as_owed` | Count a `windows: required` filter as passed. |
| V-9 | `Land_notification_range_selects_pre_dispatch` | Remove `PreDispatchFailureDeliveryTests` from the kind. |
| V-10 | `Landing_git_range_selects_repository_mutation_lease` | Remove the new kind. |
| V-11 | `Latest_red_tip_is_one_warning_row` | Emit a row for a green latest tip. |
| V-12 | `Kind_66_is_appended_and_round_trips` | Assign 65 or insert before `WorkspaceDiskLow`. |
| V-13 | `Down_throws_and_up_follows_the_predecessor` | Make `Down` drop the table. |
| V-14 | `Job_does_not_start_tests_in_process` | Call `dotnet test` from the job. |
| R-1 | Doc sentences in both owner files | Delete either sentence. |
| R-2 | Client visual and the all-kinds list include `PostLandEarlyWarning` | Leave the kind off the list. |

Existing tests the slices touch, from the impacted-classes table:

- S1 touches no migration, public DTO, route, operation label, remote `scripts/*.sh` function, or statement budget. No existing row.
- S2 edits `tests/impacted-classes.json`. The closed row is `ImpactedClassesSelectorTests` (13 tests on this base, plus V-9 and V-10).
- S3 edits `server/Application/Dtos/AttentionDtos.cs`, `server/Api/Endpoints/AttentionEndpoints.cs`, and `client/src/api/attention.ts`, which match the `contracts` kind (`ContractSnapshotTests`, 5 tests, `tests/Antiphon.E2E`). It adds a migration, which matches the `migrations` kind. The closed rows are `AppDbContextModelTests` (3 tests, in-memory `UseNpgsql`, no server) and `ContractSnapshotTests`. The migrations kind's other classes and its downgrade derive stay an hour-scale set (`docs/orchestration-loop.md` lines 695-696). That set is not a checkpoint row: it is past the 45-minute advisory and past R1's 30 minutes. `Down` throws, so those rewind fixtures are not newly able to drop this table. A Review that runs the selector and sees a red there owns it. Do not widen that set into this table.
- S3 also edits `AttentionKindWireTests` (5 tests on this base, plus V-12).
- S4 adds a Hangfire registration only. No existing job test is retargeted. V-14 is new.

`totalTimeoutMinutes` is omitted. The tool's default `max(30, 2 * sum(EstimatedMinutes) + 10)` is 140 minutes for the 65-minute sum below. Do not set a lower explicit total.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1784-s1/ | post-land-plan | `/*/*/PostLandEarlyWarningTests/*` | V-1, V-2, V-3, V-4, V-5, V-6, V-7, V-8, R-1 | all listed, 0 failed | 9 | 12 |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-c1784-s2/ | selector-replay | `/*/*/ImpactedClassesSelectorTests/*` | V-9, V-10 | all listed, 0 failed | 15 | 8 |
| CP-3 | S3-S4 | tests/Antiphon.Tests -> bin-c1784-s3/ | warning-feed | `/*/*/(PostLandWarningAttentionTests*)\|(AttentionKindWireTests*)\|(AppDbContextModelTests*)\|(PostLandEarlyWarningJobTests*)/*` | V-11, V-12, V-14 | all listed, 0 failed | 14 | 12 |
| CP-4 | S3-S4 | CP-3 | warning-migration | `/*/*/PostLandWarningMigrationTests/*` | V-13 | all listed, 0 failed | 2 | 15 |
| CP-5 | S3-S4 | tests/Antiphon.E2E -> bin-c1784-e2e/ | contract-snapshot | `/*/*/(ContractSnapshotTests*)/*` | contracts kind | all listed, 0 failed | 5 | 10 |
| CP-6 | S3-S4 | n/a | attention-visual | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/attention/attentionVisuals.test.ts` | R-2 | 0 failed | n/a | 8 |

CP-1's nine methods are V-1 through V-8 and R-1. CP-2's floor 15 is the 13 methods on this base plus V-9 and V-10. CP-3's floor 14 is V-11 (one method), the six wire methods after V-12 is added to the five already in `AttentionKindWireTests`, the three model methods, and the four job methods (`disabled_or_blank_worktree_does_not_start`, `non_linux_does_not_start`, `linux_enabled_starts_detached`, `does_not_run_dotnet_in_process`). CP-4's two methods are the predecessor `Up` and the throwing `Down`. CP-5's floor is the five existing snapshot methods.

## Performance budget

The checkpoint rows are the plan's own tests. They do not run the Unit lane.

| Row | Selection | Expected | Phases | Timeout |
|---|---|---|---|---|
| CP-1 | `PostLandEarlyWarningTests`, 9 methods, category Unit, project `tests/Antiphon.Tests` | 12 min wall including a cold build. The methods invoke `pwsh -Plan` and read files. No container. | Build and slot wait reported by the checkpoint line. Test body is process spawn of pwsh, seconds each. No migration, no host. | Provisional. No p95 sample. Row estimate 12, under the 15-minute advisory. A run over 30 minutes is an R1 Debug, not a wider estimate. |
| CP-2 | `ImpactedClassesSelectorTests`, whole class | 8 min. The selector walks `tests/**/*.cs` once per invocation. | One shared walk per test method today (13 methods). S2 does not add a cache. | Provisional, same rule. |
| CP-3 | Four Unit classes, no live Postgres | 12 min on the CP-3 build. Model tests call `UseNpgsql` with a dummy connection string and do not open it (`AppDbContextModelTests.cs` lines 73-76). | Build once. Test body is in-memory. | Provisional. |
| CP-4 | Two migration methods | 15 min. One shared `MigrationTestDatabase` for the class, predecessor created with `CreateAsync`, not `Down`. | Container start and migrate are the body. Record them. Slot wait is separate. | Provisional, at the advisory ceiling (3 times 15 is 45, which the importer does not warn on). Not a measured p95. |
| CP-5 | `ContractSnapshotTests`, 5 E2E methods, shared `SharedApp` | 10 min, the figure the Merge subset already uses for this class's project row. | One host and one database for the class. | Provisional against that Merge figure, not a new sample. |
| CP-6 | One Vitest file via `scripts/test-client.ps1` | 8 min. The script's typecheck is the cost; the file is small. | No dotnet build. | Provisional. |

Product lane, not a checkpoint row. Provisional from the single CARD-1728 run `20261107-090804-8143` at `5d5973be2`, loaded server2, not a p95. Namespace discovery about 47 s. Non-Checkpoints Unit work is the parallel burst (about a minute for 2,177 A-L cases in that run) plus one namespace of serial spawners outside Checkpoints, unmeasured per namespace. Checkpoints serial work was 1,904 s in the A-L half. Letter-prefix shards keep each TRX inside that chain instead of one 45-minute kill with no TRX. The whole set, one shard at a time, is on the order of an hour to ninety minutes and holds one of two slots. Any shard whose journal exceeds 30 minutes gets a Debug task with the shard id, tip SHA, host, and the journal path. Do not raise the coalesce window or the row cap to hide it.

Recording: each shard sets `ANTIPHON_TEST_PHASE_ROOT` under the receipt directory. TRX is authoritative. The phase journal is how a no-TRX kill still names the in-flight case. The summary line is the attention evidence.

Setup: CP-4's database is once per class. CP-5's host is the existing E2E shared fixture. No per-test container.

R1 obligation for the product lane is the first real shard that exceeds 30 minutes. This plan does not run that lane.

## Slices

S1, about 90 minutes. The manual script, the shard manifest, `PostLandEarlyWarningTests`, and the two doc paragraphs. An orchestrator can run `-Plan` and, on Linux, the detached run against a clean master checkout. No server change.

S2, about 60 minutes. The three selector edits and V-9, V-10. After this, a group-71-shaped diff selects `PreDispatchFailureDeliveryTests` and a group-117-shaped diff selects `RepositoryMutationLeaseTests`.

S3, about 120 minutes. Kind 66, the table, the POST, the feed row, the client visual, the migration whose `Down` throws.

S4, about 90 minutes. The hourly job, default off, Linux-only, detached. Operator step, not a code default: set `PostLandEarlyWarning:Enabled` and `Worktree` on the Linux host that should run the lane. Do not set them on the desktop AppHost. The Windows Full run, native fixtures, and group 96 stay owed on Windows. Nothing in this card runs on the desktop.

## Risks

A letter-prefix Checkpoints shard can still exceed 30 minutes. The first journal that shows it is the split trigger, not a reason to run the 12-operand OR.

The warning and a review share a two-slot host. One warning shard occupies one slot for its whole body. That is the contention the card accepts. It is not both slots, and it is not a broker preemption.

S4 left at the default does nothing. Until an operator sets the flag, only the orchestrator-run script covers a batch. That is the intended first slice.

Group 4 remains a load race the idle Linux run did not show. Group 96 remains Windows-only. This warning does not shorten the Windows Full run and does not qualify an rc by itself.

A Hangfire process that tracks rc code will not contain S4 until that rc includes this card. The script in S1 does not wait for that.

## Operator-only steps

Set `PostLandEarlyWarning:Enabled` and `Worktree` on the Linux server process that should run S4. Leave the desktop unset. Do not add a Scheduled Task. Do not enable Windmill for this lane. The Windows lane for `owed-windows` filters, including group 96, stays on the native rc qualification.
