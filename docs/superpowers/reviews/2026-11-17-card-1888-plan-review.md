# CARD-1888 plan review, round 1

Review task `6696c9ea`. Ordinary round 1, Final, full scope for this plan. Blockers only. The subject changes no code and no tests.

Subject task `c58df935-1bc5-4306-a24b-67980f420143` (Plan, Succeeded). Reviewed source `1b6c23bed2bf26f41ac3da986611a0241eebc744` on `origin/feat/card-task-c58df935`. Two commits, `7bd3255c3ecbe516c9c96722fa06f7b2a90dca51` and `1b6c23bed2bf26f41ac3da986611a0241eebc744`. The range from `300c8a99d0d267a39791410b7540189ab702f251` adds only `docs/superpowers/plans/2026-11-17-card-1888-transcript-ingest-probe-plan.md`.

`origin/master` at review time is `300c8a99d0d267a39791410b7540189ab702f251`, the same pin the plan names. CARD-1888 (`110038c1-0d78-46c2-a9d6-7fdf8dfd3319`) is InProgress, Normal/Soon, rank 8. Its body is the high-water proposal and the `pg_stat_statements` counts. History is three moves: Plan `c58df935` dispatched, Plan settled Succeeded, this Review dispatched.

## Verdict

**Two blockers. The plan is not clean.**

D-1: a delete of a middle stored row leaves `FirstRowId` unchanged, so the prefix memo treats a missing `UserPrompt` as stored and a later replay does not insert it again. D-2: the unsafe S2 commit is not bound to land with S3 in one Merge. Advisories A1–A6 do not hold the land.

## D-1. A middle delete is invisible to the lowest-row id

| | |
|---|---|
| Where | Plan section 3, rules 1–2 and the equivalence argument (lines 104–141). `FirstRowId` is the only staleness check. |
| Failure | After a clean commit the memo stores a SHA-256 of a leading prefix and the id of the lowest-`Sequence` row. On the next batch, a matching prefix digest omits those UUIDs from the probe. Deleting any row except that lowest one leaves `FirstRowId` the same. The replay's prefix still digests to the memo, the missing UUID is not probed, and the row is not inserted again. |
| Why | Transcript-confirmed `UserPrompt` is the delivery verdict (`docs/session-runtime-invariants.md`). A stuck missing prompt is a lost receipt. Whole-session retention is a different case: `DataRetentionService.PruneTranscriptsAsync` deletes every row for the session (`DataRetentionService.cs:372`) under `BeginMutationAsync`, and `PruneSessionsAsync` deletes the session row (`:290`), which cascades `TranscriptEntries` (`AppDbContext.cs:1783-1786`). Both remove the lowest row, so `FirstRowId` changes. W-3 deletes every row through a second `AppDbContext` and therefore also changes `FirstRowId`. It does not delete one middle row. No production `UPDATE` or per-row `DELETE` of `TranscriptEntries` exists (server search: the only direct delete is line 372; there is no `UPDATE "TranscriptEntries"`). A manual delete, or a later writer that removes one row, is still invisible. The S3 re-grep is an author gate on this card. It does not run for a delete that lands afterwards. |
| Fix | When the prefix digest matches, and only then, compare a stored prefix count before skipping UUIDs. Record `PrefixStoredMaxSequence` and `PrefixStoredCount` (rows with `Sequence <=` that max) on the clean commit. On the skip path run `SELECT count(*) FROM "TranscriptEntries" WHERE "AgentSessionId" = @id AND "Sequence" <= @memoPrefixMax`. A mismatch drops the memo and uses today's full probe. Gate the query to the skip path. A `count(*)` on every prelude, including the one-entry live batch, is the option the plan already rejects, and it would invalidate the memo after every live append because those appends sit above the old max. `max(Sequence)` and `max(Id)` stay the wrong check: a middle delete leaves the max in place, and every append moves it. Cost of the gated count: one range scan of `IX_TranscriptEntries_AgentSessionId_Sequence` over the prefix (about 15,000 index tuples on the measured snapshot, up to about 75,000), once per snapshot replay that would have skipped, on the order of the card's 0.6 prelude/s. Product inserts rebase to `max+1` (`AgentSessionRuntime.cs:1481`), so they do not refill a hole at or below the memo max. Extend W-3, or add one witness, that deletes a single middle `UserPrompt` through a second context and expects that row back. S2 stays red on that witness. S3 stays red until the count check exists. An in-place `UPDATE` of `Uuid` or `Kind` keeps the count; no production statement does that, and the S3 re-grep remains the gate for a new one. |

## D-2. S2 and S3 are not one land

| | |
|---|---|
| Where | D-9 (plan lines 221–223) and F-4 (lines 328–330). |
| Failure | S2 commits the prefix skip without the fingerprint, on purpose, so W-3 has a committed red. That commit will not re-insert rows another writer deleted. F-4 says the branch is never landed at S2. It does not say S2 and S3 land together in one Merge, cherry-picked in order into one push, and never as separate lands. |
| Why | Mutation checks are waived (operator 2026-11-12), so the red has to come from slice order. That is safe only when the unsafe commit cannot be published on its own. "Never landed at S2" is the author's intention during the branch. It is not a Merge rule. |
| Fix | Add one sentence to section 7 and to F-4: S2 and S3 land together in one Merge, cherry-picked in order into one push, and never as separate lands. |

## What the code confirms

Checked at `1b6c23bed`, which matches `300c8a99d` outside the plan file.

**The probe.** `PersistTranscriptCoreAsync` (`AgentSessionRuntime.cs:1409`) puts every incoming UUID into a set (`:1433`) and chunks by 512 (`:1438`). `TranscriptIngestPrelude.ReadAsync` (`TranscriptIngestPrelude.cs:54-85`) sends the first chunk, at most 512, in the `session-state.identity` command when any UUID is present. Each further chunk is its own `session-state.identity` query (`AgentSessionRuntime.cs:1449-1457`). A batch of N distinct UUIDs costs `ceil(N/512)` commands and N index seeks on `IX_TranscriptEntries_AgentSessionId_Uuid`, whether or not the rows are new. The card's 1,068,019 calls at 602 rows per call are that chunk form. The first 512 keys ride in the prelude, so both statements have to shrink. The plan's split of the two shapes matches the code.

**Nine whole-snapshot ingests.** Each of these passes the runner's full `GetTranscriptAsync` result into persist:

1. `AgentSessionRuntime.cs:973` `CatchUpForCeilingAsync`, persist at `:987`
2. `AgentSessionRuntime.cs:1028` `PullAndPersistAsync`, persist at `:1030` (`CatchUpTranscriptAsync` at `:963`)
3. `AgentSessionRuntime.cs:1071` `ProbeWorkingTranscriptAsync`, `PersistTranscriptCoreAsync` at `:1110`
4. `AgentSessionRuntime.cs:1256` `SyncTranscriptAsync`, persist at `:1258`
5. `SessionRunnerEventPump.cs:204` calls `SyncTranscriptAsync`
6. `AgentSessionService.cs:2357` calls `SyncTranscriptAsync`
7. `AgentSessionRuntime.ReceiptReadiness.cs:53`, `PersistTranscriptCoreAsync` at `:81`
8. `PhoneHomeRecoveryPump.cs:387`, `PersistTranscriptAsync` at `:388`
9. `ChannelOutboundTerminalTranscript.cs:22`, `PersistTranscriptAsync` at `:28`

`ObserveTranscriptAsync` (`:413-415`) persists one entry. The four `PersistTranscriptCoreAsync` callers are `:1110`, `:1358`, `:1403`, and `ReceiptReadiness.cs:81`. Each already holds a `SessionStateStore.Lease`. `PersistTranscriptAsync(Guid, IReadOnlyList, CancellationToken = default)` (`:1351`) is the method PhoneHome and the channel proof call. The plan leaves that signature as it is.

**Missed middle entry on a later snapshot (question a).** The digest is SHA-256 over the identity tuples in source order, length-prefixed (`(Uuid ?? "", Kind, Uuid is null ? Sequence : 0)`). A `UserPrompt` that was absent from the batch that recorded the memo, and that later appears in the middle, changes the prefix bytes. Rule 1 then probes every UUID. That is why the card's high-water mark is the wrong design: stored `Sequence` is rebased (`:1460-1481`) and runner `Sequence` restarts per tailer (`:1426-1432`). Clean, for this question, means the return at `:1567` after `SaveTrackedChangesAsync` (`:1687-1701`) commits the whole `pending` list in one transaction, or the duplicate return at `:1544` when every entry was found or skipped. The individual-row fallback (`:1557-1562`, `PersistRowsIndividuallyAsync`) can commit a prefix of `pending` and still sets `AcceptedGeneration` with `NeedsReload` false when the failure was stubbed (`:1680`). Rule 4 already excludes fallback. `AcceptedGeneration` by itself includes that fallback return, so the memo has to be recorded on the `:1567` and `:1544` returns only. A conflict or a partial fallback therefore does not become a memo. The duplicate return is sound only while the prefix it skips was proven by a probe or by an earlier clean memo. A middle delete breaks that, which is D-1.

**Restart, second process, compaction, null UUID (question c).** The memo is a field on the in-memory `SessionStateStore.Entry`. A process restart drops it, and the next replay pays today's probe once. Another server's inserts do not move the lowest row id; the tail probe still sees new UUIDs. Another server's whole-session delete changes `FirstRowId`. A middle delete by anyone is D-1. Compaction that changes `Uuid`, `Kind`, or a null-UUID `Sequence` changes the digest and falls back to the full probe. A text-only rewrite keeps the digest, and today's `(Uuid, Kind)` skip also leaves the stored text as it was. Null-UUID rows keep the sequence probe (D-6). The re-insert is real: a null UUID whose runner sequence `s` is at or below the stored max, with no row stored at `s`, misses `seenSeqs` (`:1476`) and is inserted at `max+1` (`:1481`). The next replay probes `s` again, misses again, and inserts again. The plan leaves that behavior unchanged. It is unobserved in the card's data.

**Memory and clearing (question d).** One memo per cached session, about 60 bytes, is bounded by `SessionStateSettings.MaxSessions` default 4096 (`SessionStateSettings.cs:6`). `EvictIdleLocked` (`SessionStateStore.cs:243-252`) drops the entry, and a non-admitted entry is removed when its user count hits zero (`:261-266`). The plan clears the field in `LoadAsync` and `BeginMutationAsync`. Idle eviction, capacity refusal, reconcile, and restart all drop it. Recording is limited to a batch at least as long as the current memo, so a one-entry live batch does not replace a snapshot memo (rule 4).

**Order and idempotence (question e).** The tail keeps source order. A tail `(Uuid, Kind)` that repeats a stored prefix row is still in the probed tail, so the existing `seenUuids` skip still applies. A second replay of a verified batch inserts nothing when the tail is empty or already stored. That equivalence holds for a prefix whose rows are still present. D-1 is the case where they are not.

**Red witnesses.** At base, nothing filters the UUID set before `Chunk(512)`. W-1's replay is the 1,200-entry snapshot plus 5 new entries: 1,205 distinct UUIDs, commands `512 + 512 + 181` across the prelude and two chunk queries. The counter has to start at that replay. The earlier cold persist of 1,200 keys is a separate batch; counting it as well would stay above 5 after the fix. W-2's replay is the same 1,205 keys, and a one-entry `ObserveTranscriptAsync` between the two snapshots must not replace the memo. Both assertions fail at base because 1,205 > 5, and the row-count assertion is independent, so the tests can go red. W-4 is a green guard at base. W-3 is green at base and red at S2 only because S2 skips the deleted prefix. That red is real for a whole-session delete. It becomes a proof of the fingerprint only together with D-2, and it does not cover D-1 until the middle-row witness above is added.

**Checkpoint table.** Re-import at this tip, leased, load 8.93, `UseAppHost=false`, `TEMP=/tmp`: exit 0, `imported 5 rows`, 0 warnings. Manifest `.antiphon/c1888-review-import.yaml`. Three builds (`bin-c1888-red/`, `bin-c1888-memo/`, `bin-c1888-green/`), one filter per row, ids CP-1..CP-5, row timeouts 45/45/45/24/24. CP-4 and CP-5 reuse `bin-c1888-green`. No explicit total, so a run derives `2 * 61 + 10 = 132` minutes. The manifest's `totalMinutes: 90` is the schema default, not an explicit cap. Estimate 15 derives a 45-minute row timeout with no advisory warning. The build deadline for each isolated output is that 45-minute row timeout.

**Scope.** No migration, no index, no setting. S4 replaces the whole-session `(Timestamp, CreatedAt)` list in `PolicyRefreshService.HasBeenIdleLongEnoughAsync` (`:532-540`) with a SQL max. `TranscriptPromptSpan.LoadAsync` (`:88-94`) reads `UserPrompt` and `QueuedUserPrompt` text in sequence order and keeps null timestamps in the window (`:96-98`). Leaving that read, and the unattributed full-row shape, to a follow-up is the right split. `PhoneHomeRecoveryPump.cs:388` only calls `PersistTranscriptAsync`; the plan does not edit that file. This review did not re-walk the CARD-1960 or CARD-1915 branches. The plan's claim that they share no file stands as the author's scan at `300c8a99d`.

## Operator decisions

- **F-1.** Yes. Ship the prefix memo. The high-water mark skips an unstored middle entry because runner sequence and stored sequence are different number spaces.
- **F-2.** Yes. Keep S4 here. Split `TranscriptPromptSpan` and the unattributed full-row read. The prompt-span read is delivery evidence.
- **F-3.** Yes. File a separate card for the null-UUID re-insert. Measure it first. This plan correctly leaves the sequence probe unchanged.
- **F-4.** Yes only after D-2's sentence is in the plan. The incomplete S2 commit is acceptable as the committed red for W-3 because mutation checks are waived. It is acceptable when S2 and S3 land together in one Merge and never as separate lands. The plan does not say that yet.

## Advisories

These do not force another Review by themselves. D-1's witness should land in the same plan edit as the count check.

- **A1.** The plan names one delete, `PruneTranscriptsAsync`. `PruneSessionsAsync` (`DataRetentionService.cs:290`) deletes the session and cascades every transcript row, also under `BeginMutationAsync`. Both are whole-session. `FirstRowId` sees them. The count check in D-1 sees them too.
- **A2.** CP-4's parenthetical uses 7 recovery tests and 41 runtime tests. Executed cases today: `AgentSessionRuntimeTests` 41 methods / 48 cases, `WorkingTranscriptRecoveryTests` 13 methods / 54 cases, standing 6, commit 8, cache 2. With 4 new tests that is 122 cases. Floor 68 equals 48 + 4 + 6 + 8 + 2, so a run that executes none of `WorkingTranscriptRecoveryTests` still meets Min. CP-5's floor 85 is below `DataRetentionServiceTests` at 64 methods / 101 cases, so a run that executes none of `PolicyRefreshServiceTests` (21 methods / 24 cases) still meets Min. Raise CP-4 Min to 122 and CP-5 Min to 125, and correct the parentheticals. The filters already name the classes and require 0 failed.
- **A3.** `AcceptedGeneration` is set on the fallback return (`:1562`) as well as on the clean return. The memo recorder has to key off the clean and all-duplicate returns, which rule 4 already requires.
- **A4.** The W-1 and W-2 counter starts at the replay. Including the cold 1,200-key persist keeps the key count above 5 after the fix. The plan's red figure of 1,205 is the replay alone.
- **A5.** Do not pass `--total-timeout` below the derived 132 minutes. The schema default of 90 in the imported manifest is not an explicit cap.
- **A6.** CARD-1960 and CARD-1915 file overlap was not re-checked against current remote tips. PhoneHome's call signature was.

## Verification

No product file was modified. `git diff --check` on `300c8a99d..1b6c23bed` exited 0.

`scripts/check-evidence-diff.ps1 -BaseRef 300c8a99d0d267a39791410b7540189ab702f251 -HeadRef 1b6c23bed2bf26f41ac3da986611a0241eebc744`: `EVIDENCE result commits=2 entries=0 violations=0`.

`scripts/impacted-classes.ps1 -MergeSubset -Range 300c8a99d0d267a39791410b7540189ab702f251..1b6c23bed2bf26f41ac3da986611a0241eebc744`: `mode=merge-subset`, `migrations=False`, `kinds=` empty. The downgrade selector was not selected (0 migration rows). The printed filters are the fixed merge-subset set. This plan review does not re-run them. No `server/Migrations`, `AppDbContext`, or model file is in the subject diff.

`git merge-tree --write-tree origin/master HEAD` at `1b6c23bed` exited 0. Tree `8014092e908eba82f6c43e06bab4d566d854a0ab`. `origin/master` is `300c8a99d0d267a39791410b7540189ab702f251`.

OS-branch witnesses: no OS-selected production behavior changed. No new input wait. Parking stays dormant (CARD-1083). The plan has no positive-control rows. Mutation checks stay waived. The guard batch (`TestClassificationPolicy*`, `TestClassificationGuard*`, `SlowTestRegistryMerge*`, `SlowTestTripwire*`, `NativeLinuxPolicyWitnessTests*`) was not run: the subject changes no code and no tests.

Platform reads, both HTTP 200, with no fleet location recorded: `GET /api/runner-defaults` keys `revision`, `globalRunnerId`, `kindDefaults`, `updatedAt`, `lastReason`, `lastProvenance`, `lastCallerTaskId`, `supportedKinds`, `unresolvedReferences`, and `globalRunnerId=server2`. `GET /api/session-runners` returned three runners. `GET /api/version` reported `capabilities` `land-v2` and `operator-shutdown-v1`. `-Runner` and `-Platform` are omitted.

Checkpoint import: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1888-plan-import -- dotnet run --project tools/Antiphon.Checkpoints -p:UseAppHost=false -- import --plan docs/superpowers/plans/2026-11-17-card-1888-transcript-ingest-probe-plan.md --out .antiphon/c1888-review-import.yaml`. One-minute load before the command was 8.93. Lease `8480f450-fc62-4677-abc9-b03a9cceec3e`, waited 0s, held 31s, exit 0. The slot printed its known note that an implicit `dotnet run` build does not receive `-maxcpucount`; the Linux compiler policy still bound cpus 0–5. Output: `imported 5 rows`, no `WARNING` line.

`reviewedSourceClean` is false. The plan table imported, and there is no test receipt with verified build provenance because this subject has no tests yet.
