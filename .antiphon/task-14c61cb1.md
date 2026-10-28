# CARD-1604 Review: model-wait token_count heartbeat

Outcome: the change is correct and fails closed, but it has one test-coverage defect. Nothing tests the step on the server that stores the heartbeat, so the stored column and the pull that writes it have no witness. This Final round ran only an Interim scope: the whole Unit lane was not run.

Subject: Code task `db402fb1-065a-4f4f-b559-dd7a0e7a67f3`, branch `feat/card-task-db402fb1`, reviewed tip `556bc984ab3e50560a416844ac246f0a06a22551` (the remote tip had not moved). Base: `rc/2026-10-27` `aa6dfd24a51db7c02e8722a05a21817bf4df407a`. Review platform: server2 (Linux).

## Runs

Each build went through `scripts/build-slot.ps1` with `UseAppHost=false`, and each test run was `dotnet exec` on the built dll through the same gate. Output went to the default `bin/`, so there is no `bin-<name>` folder. The brief says to delete nothing, so the gitignored `bin/` and `obj/` folders were left in place.

| Run | Commit | Selection | Result |
|---|---|---|---|
| U-1 | 556bc984 | `TaskDeadlinePolicyTests` 39, `AgentTaskOverdueDeadlineTests` 35, `RunnerContractMapperTranscriptEventTests` 7, `AbsentLaunchPolicyTests` 88, `EntityScalarSnapshotTests` 35, `CardRelationshipSchemaTests` 3 (`HasPendingModelChanges` + all migrations applied) | 207 total, 207 passed, 0 failed, 0 skipped |
| U-2 | 556bc984 | `CodexTranscriptNormalizerTests` 20, `CodexTranscriptTailerTests` 18 | 38 total, 37 passed, 1 failed (`A_rollout_held_open_by_the_writer_is_still_read`, see below) |
| R-1 | 5111812a (red) | the 3 Antiphon.Tests classes from U-1 | 81 total, 78 passed, 3 failed: mapper `Codex_token_count_maps…`, policy `a_codex_token_count_five_minutes_old_holds…`, overdue `a_recent_codex_token_count_keeps…` (all `ShouldAssertException`) |
| R-2 | 5111812a (red) | the 2 classes from U-2 | 38 total, 35 passed, 3 failed: normalizer `token_count_keeps_its_timestamp…`, tailer `a_token_count_heartbeat_is_on_the_snapshot…` (assertions), plus the same inherited `A_rollout_held_open…` |

The witnesses are real. All five heartbeat witnesses fail on assertions at the red commit and pass at the tip. Clocks: the policy and overdue tests seed times relative to `DateTime.UtcNow` with wide margins (5 vs 20 min, scaled 12 500 vs 50 000 min), so they are deterministic without a fake clock. The tailer test polls for up to 10 s against a fixed fixture.

The inherited red is `A_rollout_held_open_by_the_writer_is_still_read` (`CodexTranscriptTailerTests.cs:532`). It asserts the Windows sharing `IOException` before the tailer starts, so it is red on Linux by construction. The diff does not touch it, and it is equally red at the red commit, which predates the fix. It was not rerun on base.

Other checks:
- `check-evidence-diff.ps1 -BaseRef aa6dfd24 -HeadRef 556bc984`: 4 commits, 0 violations.
- `git merge-tree` was not run, because the brief forbids it. The author reports a clean rc tree `2ba87278` and master conflicts in the DTO files against CARD-1574.

## Defect

**D-1: the step from producer to recipient has no test.** Land owner: Code task `db402fb1-065a-4f4f-b559-dd7a0e7a67f3`.

- **Where:** `server/Application/Services/AgentSessionRuntime.cs:930-953` (`StampCodexTokenCountAsync`) and its two calls (`PullAndPersistAsync` `:961` and `SyncTranscriptAsync` `:1189`).
- **Failure:** no test runs this code.
  - The policy and overdue tests write `AgentSession.CodexTokenCountAt` straight into the database.
  - The overdue harness's dispatcher has no runtime (or uses `CatchUpOverride`), so the gate-2 pull never stamps anything.
  - The mapper and tailer tests stop at the DTO.
  - So all of these mutations stay green: delete the `await StampCodexTokenCountAsync(...)` in `PullAndPersistAsync`, flip the monotonic predicate `CodexTokenCountAt < normalized`, drop `== null`, or let a null observation clear the column.
  - In production, a delete brings back the CARD-1604 false failure, with every test green.
- **Why it matters:** in production the clock only sees a heartbeat through this path. `TryFailOverdueAsync` gate 2 pulls the runner snapshot, the stamp writes the column, and then the policy re-reads it. The stage contract rejects a missing producer-to-recipient test.
- **Fix:** add a test in the style of `AgentSessionRuntimeTests`, using its `StaticSessionRunnerClient` or a stand-in. The runner snapshot carries `CodexTokenCountAt` = T. After `CatchUpTranscriptAsync`:
  - the row holds `Normalize(T)`;
  - a second pull with an older T' leaves T;
  - a pull with null leaves T;
  - no `TranscriptEntries` row is added.

  Better still, add one overdue-dispatcher case built with the real runtime or a stub runner client: the stored heartbeat is stale, the pulled snapshot carries a fresh one, and the task stays `Dispatched`.

  Commit the test so it fails first (for example, with the call removed), then commit the test alone. No production change is expected.

## Audit answers (advisory, not defects)

1. **Migration.**
   - `20261028170715_AddAgentSessionCodexTokenCount` adds one nullable `timestamp with time zone` with no default and no backfill. In PostgreSQL this is a metadata-only, instant `ADD COLUMN`, safe on the 10-28 data and on the server2 stack's database. `Down` drops the column.
   - The snapshot diff is exactly the three column lines. The Designer `BuildTargetModel` is byte-identical to `AppDbContextModelSnapshot.BuildModel`, and `ProductVersion` is 9.0.21 in both. `HasPendingModelChanges()` is false, from `CardRelationshipSchemaTests` in U-1.
   - The CARD-1591 standard does not apply to this column. `AbsentLaunchPolicy.MessageColumns` is a census of `SessionQueuedMessage` columns only. `AgentSession`'s only column census is `EntityScalarSnapshotTests`, which reflects over the type and stays green.
   - `AgentSession` has no concurrency token (the only `xmin` row version is on `ApiErrorRecovery`), so the stamp's `ExecuteUpdate` cannot fail another writer's `SaveChanges`.
2. **Version skew.**
   - Runner contracts deserialize with `JsonSerializerDefaults.Web`, which ignores unknown members, and the field is optional and defaults to null.
   - Old runner with a new server: the field is null, nothing is stamped, and the old 20-minute behaviour applies. The mapper's legacy-JSON case covers this.
   - New runner with an old server: the field is ignored.
   - The runner's `GetTranscript()` uses `with { AcceptedStartedAt }`, which keeps the field, and phone-home and HTTP both map it.
   - No restart order is needed. The fix is only active once both the server (with the migration) and the runner run the new code.
3. **Clock correctness.**
   - All values are UTC, and `SessionGeneration.Normalize` returns `DateTimeKind.Utc` at microsecond precision.
   - The heartbeat changes only the `ModelWait` phase, after the boot substitution, and stays capped at this task's own `elapsed`. A heartbeat older than dispatch or left over from a warm session therefore cannot help.
   - Boot, local execution and the ceiling each have a test showing they do not change.
   - A null column leaves the Claude and Grok paths unchanged apart from one extra primary-key `SELECT`, which runs only on the ModelWait phase arm.
   - Write cost: at most one primary-key `UPDATE` per transcript pull of a Codex session that reported a heartbeat. It updates no rows unless the value is newer. It does not touch `LastSeenAt`, does not track the entity, and does not use the transcript-ingest path. Pulls only happen where they already happened, including the sweep's pull-before-judge gate. There is no new 5-minute write storm.
   - A heartbeat after the task is Failed has no effect: Failed rows are not evaluated.
4. **Safety bounds** (accepted by the card's design, recorded here):
   - Only the role ceiling (240) bounds a Codex session that keeps writing `token_count` but makes no transcript progress. This review did not check Codex's own retry behaviour from source.
   - A future-dated heartbeat (runner clock skew) stays stored, because the stamp is monotonic, and holds the model-wait at 0 until it passes. A future-dated transcript row already has the same exposure, and the ceiling bounds both.
   - A claim-revoked namesake rollout's last heartbeat stays on the tailer, as its entries already do.
5. **Not implemented:** settlement of a Failed task whose session later delivers a report (the card's "also consider"). The author disclosed this. It needs its own follow-up card.

## Review evidence

reviewedSourceClean is false for two reasons: D-1 is open, and these runs were build-slot `dotnet exec` runs, not SHA-validated checkpoint receipts.
