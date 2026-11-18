# CARD-1987 plan review, round 1

CLEAN. The plan is safe to land. Advisories below do not hold the land (D-2).

Reviewed source `7fc39b0f073c2dbaff23f5c812b1c941ca754613` on `feat/card-task-fa6c783e`. Diff is one commit, one file, parent `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4` (`origin/master`). Plan owner `fa6c783e-9d1d-4121-bdb2-28b65e4e0489` (Claude Opus). Reviewer is Grok. Mutation stays waived (operator 2026-11-12). Positive controls stay pending. `reviewedSourceClean` stays false: the import below is a manifest, and this plan review has no checkpoint-tool receipt.

## The 15% figure and the 30% target

Both sentences in the plan are the right reading of B-0, and neither is a measurement after the change.

B-0 is 108,400 statements in 647 s, **167.5 qps**. The table's own sum is S1 ~17.6 (the 18.1/s binding read, only "if Route is ~all of it") plus S2's 4.0 full-row load plus S2's 4.0 streaming `RunAttempts` select. The `LastSeenAt` UPDATE stays, so it saves 0. That is 17.6 + 4.0 + 4.0 = **25.6 qps**. 25.6 / 167.5 = **15.3%**, which the plan rounds to 15%. D-10 and Q-4 state that 15% as the S1+S2 cut. The table and the ground-truth row both keep the condition, and both mark the per-caller split of the 18.1/s **UNOBSERVED**. Q-4 asks the operator to accept about 15% now. The plan does not claim S1+S2 reach 30%.

The numbered ceilings do not already show 30%. S4 is "up to ~11.6" including statement #9. S5 is "up to 6.4". S3 "saves bytes, not qps" unless two loads collapse, and that qps is not in the table. 25.6 + 11.6 + 6.4 = **43.6 qps, 26% of 167.5**, before the activity `BEGIN`/`COMMIT` pair, whose share the plan marks **UNOBSERVED**. Q-4 leaves the 30% target to the measured S3-S5 amendment. That is the correct gate.

A 60 s cache of an 18.1/s read leaves about 18.1/60 = 0.30/s, so the saved rate is about 17.8 qps. The table's 17.6 and the later "about 0.3/s" residual are the same estimate at two roundings. See advisories.

## Repeat-reads investigation

`origin/feat/card-task-24283212` is pushed at `3bb6b67ed71739a2a82448189f4a1cb3efe015cd`. The note is `docs/investigations/2026-11-18-repeat-reads-and-cache-candidates.md`. It was written while this plan branch was still at `e229fb4d3`, so it records no agreement with the plan.

Its window is 2026-11-18 05:52:19Z-06:02:45Z (626 s) on the server that started 01:16Z, source SHA `e229fb4d3` (CARD-1888 and CARD-1969 already in that process). It states no new total qps. The cheap high-rate rows it does state are still the ones S1 and S2 cut: runner lookup **19.7/s** (B-0 was 18.1/s) and `LastSeenAt` **7.3/s** (B-0 was 4.0/s). S1 and S2 stay the right first qps cuts on that later window.

Three facts the amendment should carry:

- `SessionRuntimeActions` Phase updates at **8.6/s** are absent from the plan's B-0 top 10. They belong in the S3 candidate list once M3 names the call site.
- Identity commands were **194/min (~3.2/s)** in the note's 31 s counter sample. One heavy `pg_stat_statements` shape was 39 calls / 10 min. M-1's question "is `session-state.identity` now under 1/s" is unanswered for the command rate. The amendment should not treat identity as already under 1/s.
- The largest DB time in that window is `prompt-span.read` (~45 s / 10 min). The note's recommended first slice is prompt-span observability. That is CARD-1969's objective (time), and this plan's objective is qps. The plan correctly leaves transcripts to CARD-1888 and CARD-1969.

The note says the runner binding "looks launch-stable but unverified". At this SHA the three binding columns are assigned only on insert (`AgentTaskDispatcher.cs:6354-6378` and `:6900-6918`, `AgentControlService.cs` around `:798-801`). `CK_AgentSessions_RunnerBinding_AllOrNone` is `AppDbContext.cs:1629-1631`. No `SetProperty` writes those columns. A second server process would break a purely in-process transcript cache. S1 does not depend on that, because nothing writes the binding after insert. The plan's ground-truth row on that point is right.

## Design checks

- **S1 scope.** `GetBindingAsync` (`PhoneHomeRunnerDirectory.cs:203-217`) is the three-column projection. `Route` (`RoutingSessionRunnerClient.cs:123-133`) calls it on every runner operation and then `Resolve` for `Remote`. The plan caches only inside `Route`, never caches `Missing`, and leaves `Resolve` on every call. Other readers stay uncached: the recovery pump's own cache (`PhoneHomeRecoveryPump.cs:912-931`), `TaskParkPublicationService`, and `EnsureInputTransportAvailableAsync` (`AgentSessionRuntime.cs:2229-2238`). `Local` rows (null binding) are stable too. Delivery-evidence columns (transcripts, receipts, TurnEnd, prompt spans, queue status, land-notification state and `ConcurrencyToken`, runner answers, `Status`/`StartedAt`, `RunAttempts.Phase`) are outside the cached triple.
- **S2 scope.** `RecordActivityAsync` is `AgentSessionRuntime.cs:2661-2690`: tracked session load, streaming `RunAttempts` load, `SaveChanges`, throttle 1,000 ms (`AgentSessionSettings.cs:88`). With a streaming attempt the save dirties two entities, so EF opens a transaction. One CTE statement removes that pair. Neither entity has a concurrency token, and the `SaveChanges` hooks (`AppDbContext.cs:147-197`) do not touch them. The existing burst test counts `SaveChanges` that modify `LastSeenAt`; moving it to `rt.activity.write` command counts is required, because `ExecuteSql` does not raise that interceptor. `BridgeQueueHarness` has no mention router, so `ObserveOutputAsync` does not add the liveness select on that path.
- **Identity cite.** Lines `1449-1457` are `WriteRestartBoundaryIfInterruptedAsync`. `PersistTranscriptCoreAsync` starts at `1468`. The identity `TagWith` is `1532-1540`. S2's edit range `2661-2690` still does not overlap ingest (`1468-1540`) or the probe region the plan cites at `1046-1240`. The collision claim holds. The line numbers in the ground-truth table are the advisory below.
- **M-3.** `LandNotificationState` at `LandingEnums.cs:22` is Queued=0, RetryPending=1, AwaitingReceipt=2, Confirmed=3, NotRequired=4, DestinationUnavailable=5, Canceled=6, LegacyUnverified=7. `NOT IN (3,4,6,7)` matches the four names the plan lists. The due page (`LandNotificationReconcileQueries.cs:90-102`) excludes those same four and also requires a queue id, a due `NextAttemptAt`, or provider-input-wait. M-3 is the wider census. `NoteById` (`:36-39`) loads the whole notification. `SessionScanById` (`:41-46`) projects `Status` and `StartedAt`. The concurrency token is assigned at `AgentTaskLandNotificationService.cs:98` before the decision. The rescan park is `AgentTaskLandNotificationHostedService.cs` around `:360-420`.
- **Diagnostics contract.** `dbCommands` is on `GET /api/diagnostics/session-state` (`DiagnosticsEndpoints.cs:16-35`). `DbCommandMetrics` allowlists `session-state.` and `rt.`. `ContractSnapshotTests` names no diagnostics route. Adding `GET /api/diagnostics/db-rate` needs no contract regeneration. Program registration cites match: interceptors `Program.cs:131-135`, `ISessionRunnerClient` `Program.cs:392`.
- **Checkpoint floors.** `PhoneHomeSessionRoutingTests` has 5 `[Test]` methods. `RunAttemptStallDetectorTests` has 1. `AgentSessionRuntimeActivityTests` has 1. No class name in `tests/` is a longer prefix of those three. CP-4 Min 8 = 3 new cache tests + 5 routing tests. CP-6 Min 4 = 2 new activity tests + the burst test + the stall detector. CP-2 Min 4 is a floor, one test per W-6..W-9.
- **Red rows.** W-10's red is 5 binding reads against budget 1, which is what `Route` does today. W-12's "3 commands and 1 transaction" matches default batching: two loads plus one batched `SaveChanges` command. `QueryBudget` counts `DbCommand`s. `BEGIN`/`COMMIT` go through `DbTransaction`, which that interceptor does not count, and the new test counts the transaction on its own interceptor. If the provider sends the two updates as two commands, the base count is 4 and the row is still red against budget 1. The Expect sentence is stored on the manifest and is not compared to test output.
- **Q-1..Q-4.** Recommended Yes / Yes / Not now / Yes. The plan says Code uses those defaults when the operator has not answered. That is a stated default, not an open blocker.

## Checks at the reviewed SHA

| Check | Result |
|---|---|
| `git merge-tree --write-tree origin/master HEAD` | tree `de9efa3f295d83655391f5ec4e2a9875821831a0`, exit 0. `origin/master` is `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4` |
| `scripts/check-evidence-diff.ps1 -BaseRef e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4 -HeadRef 7fc39b0f073c2dbaff23f5c812b1c941ca754613` | commits=1, entries=0, violations=0, exit 0 |
| `scripts/impacted-classes.ps1 -Range` JSON | kinds=[], classes=[], files=[] (count 0). The diff touches no `server/Migrations/**`, `AppDbContext.cs`, or `*Model.cs` |
| `scripts/impacted-classes.ps1 -MergeSubset -Range` | `migrations=False`. Downgrade selector count 0. The printed class list is the always-on merge subset |
| Checkpoint import | load 14.19 at start. Slot `fad26c36-02a1-4706-8368-06ed36f89615`, waited 0 s, held 10 s. `imported 6 rows`, exit 0, no importer warnings. `minExecuted` 4, 1, 8, 1, 4. `timeoutMinutes` 45 on every row. Builds `bin-c1987-m/`, `-s1red/`, `-s1/`, `-s2red/`, `-s2/` |
| Product tests and the guard batch | Omitted. The subject changes no code and no tests. CP-1..CP-6 name tests and a script that do not exist yet |

The table has no Serial column, so every manifest row is `serial: false`. The prose "one `--serial` run per group" is not a tool flag. `BuildStep` does not pass TUnit `--serial`. `row.Serial` is row exclusivity. Red and green rows are different commits, and the plan runs each red row once at its test commit. `timeouts.totalMinutes: 90` and `rowMinutes: 15` are `TimeoutDefaults`. `ExplicitTotalMinutes` is unset, so a combined six-row run would derive its own total. The plan runs one group per commit. The `dotnet run` import printed the known slot note that `-maxcpucount` is handed to the program. The plan's own import used that same form. The five output paths end in `/`.

Producer/recipient delivery does not apply. The diff adds no session that waits for input. Parking disabled (CARD-1083) is unchanged. No OS-selected production path changed.

## Advisories

These do not hold the land (D-2). No review rerun is required for them.

- Session-row delete is `DataRetentionService.PruneSessionsAsync`. The cutoff is `SessionRetentionDays`, default 90 (`RetentionSettings.cs:18`), and only `Stopped` or `Failed` rows that nothing still names. `TranscriptRetentionDays` is 7 and does not delete the session. The plan's "idle 7 days" sentence is that transcript window. The 60 s stale route after a delete stays rare at either window.
- Point the identity cites at `AgentSessionRuntime.cs:1532-1540` (and the prelude read at `:1510-1512`). Lines `:1449-1457` are `WriteRestartBoundaryIfInterruptedAsync`.
- Keep the Route-is-almost-all condition next to the 15% sentences in D-10 and Q-4. The split is UNOBSERVED. Use ~17.8 qps saved and ~0.3/s residual for the same 18.1/s figure, or state which rounding the 17.6 uses.
- The S3-S5 amendment starts from investigation `3bb6b67e` as well as M-1..M-4. Include the 8.6/s `SessionRuntimeActions` Phase update. Do not treat identity as already under 1/s. Prompt-span DB time stays with CARD-1969.
- M-3 counts every note outside states 3, 4, 6, and 7, including notes the due page skips. Read it as a census, then compare it to the due-page predicate before designing S4.
- Assign `clock ?? TimeProvider.System`. The optional `TimeProvider? clock = null` lets `new RoutingSessionRunnerClient(directory)` compile. DI will inject the registered `TimeProvider` in `Program.cs`. A test that constructs the client directly throws if the field is used while null.
- D-7's "~2.6 captures/s" is about 167/64 command samples. Transaction events add a capture on 1 event in 16.
- Run the utility redaction before any log line or JSON field is filled. The pattern's `connection` and `token` alternatives replace the label of any statement whose text contains those words. The rate still counts. The label does not. `ConcurrencyToken` is one word, so `\btoken\b` does not hide that column name.
- The "70 classes" forecast includes `AgentSessionSettings.cs` (21) and `DbCommandMetrics.cs` (2), which this plan does not edit. Code already re-runs `impacted-classes.ps1 -Range origin/master..HEAD` per slice. The short checkpoint rows stay as written.
