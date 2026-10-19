# CARD-1358 — checkpoint ledger read deadline: progress re-arms it, `unreadable` is not `tampered`

Covers CARD-1358 (primary) and its sibling CARD-1356 (build deadline) in one plan: both are load-sensitivity defects in `tools/Antiphon.Checkpoints`, both are bug fixes to code already on the rc line, they touch disjoint files, and each slice is independently landable. Master-only for this task; the rc recommendation is D-8.

## Outcome

1. `CheckpointFileReader.ReadBytes` no longer bounds a whole file read by a fixed 3-second clock. The 3 seconds become a **stall window** that every delivered chunk re-arms, chunks grow from 8 KiB to 256 KiB, and a stall is thrown as an `IOException` whose message starts `read-stalled:`; a caller cancel stays an `OperationCanceledException`.
2. The build-confinement ledger reports an entry it could not read as `refused: unreadable` with `UNREADABLE <path>: <reason>` lines and no `git restore` command. A real content change beside it is still `refused: tampered` with the same `TAMPERED` line and manual command as today.
3. (CARD-1356) A scheduled build's deadline is the largest derived timeout of the rows that need it, at least 15 minutes and at least `timeouts.rowMinutes`; `--row-timeout` replaces it exactly as it replaces row deadlines; `report.json` records `deadlineMinutes` per build.

Every behaviour above has one red-first witness on a deterministic seam (virtual IO plus `ControlledTimeProvider`, or an injected stamp function); no witness sleeps on the wall clock. One narrow green run executes the `### Checkpoints` table.

## Evidence and ground truth

Inspected task base `6ec71d427574f3a1b74bfbe2042e0ea9a819d191` (branch `feat/card-task-5e84355c`). Cards read in full: CARD-1358 (1 revision), CARD-1356 (0 revisions). The Windows evidence runs `20261019-184323-5ba5` and `20261019-185037-2e0f` live under the desktop checkout of task 1f4aaf07 and are not reachable from this runner mirror; the card's reading of them is taken as given where marked. No builds or tests were run during Plan.

| Card assumption | Code / repository truth | Consequence |
|---|---|---|
| The reader hashes under a fixed 3-second deadline in 8 KiB chunks. | Confirmed. `src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs:40` creates `new CancellationTokenSource(TimeSpan.FromSeconds(3), context.Time)` once per call; `:66` reads with `new byte[8192]`. The window covers open, inspect and the whole read. | Replace the one-shot window with a stall window re-armed on progress (D-1). |
| The slow part is disk throughput. | The handle stream is `new FileStream(handle, FileAccess.Read)` (`CheckpointNativeIo.cs:382`), a synchronous handle, so each `ReadAsync` is a thread-pool hop. A 7.5 MB file is 960 hops at 8 KiB; under thread-pool pressure the hop count, not the bytes, decides. | Raise the chunk to 256 KiB (30 hops for 7.5 MB) in the same slice (D-2). |
| `unreadable` is reported as `tampered`. | Confirmed for the three comparisons: `BuildConfinement.AdmitLedger` (`:162`), `VerifyLedger` (`:175`) and `Settle` (`:281`) feed `pair.Value == "unreadable" || Stamp(full) != pair.Value` straight into `Tampered(...)`, which prints `TAMPERED <path>` plus a `git restore --source=<head>` command. `Stamp` (`:360-379`) returns the bare string `unreadable` on any `IOException`, dropping the reason. Capture is already distinct: a tracked unreadable file at `CaptureLedger` (`:120`) or in-slot `Capture` (`:661`) refuses `tracked-snapshot` / `tracked-unreadable`, not `tampered`. | Classify at the three comparison sites, carry the reason in the stamp, keep the capture refusals (D-3). |
| A deadline miss surfaces through the ledger. | `Stamp` calls `ReceiptFile.ReadBytes(full)` (64 MiB limit, default context). `ReceiptFile` wraps `IOException`, `InvalidDataException` and `OperationCanceledException` into `IOException("receipt-read-refused: <path>: <msg>")`, so a deadline miss, a hard link (`selected file has multiple links`) and an access refusal all become `unreadable`. The docs already record the hard-link case being called `tampered` (`docs/testing-and-build.md:476`, task 47b93e01). | One classification fixes all three shapes; the reason text tells them apart. |
| The only >4 MiB entries were an archive and the tool's Roslyn DLL. | Plausible but unverified here. The ledger deliberately includes **ignored** files outside the write roots (`ListUntracked` adds `--others --ignored`), so every other project's `bin/`, `obj/` and `bin-*/` DLLs are hashed on every build. | The stall design must hold for many large binaries, not two; it does, because it assumes only that a healthy local read returns some bytes within 3 s. |
| `unreadable` must still fail the receipt. | The server never parses the reason: `ReviewEvidenceBindingService` refuses on `evidence.ReviewedSourceClean != true` (`server/Application/Services/ReviewEvidenceBindingService.cs:84`). The tool's exit stays 2 (`ExitCodes.Invalid`) for any non-null settlement kind (`ProcessDriver.cs:182-185`, `RunScheduler.cs:409-418, 433-467`). | No server change. A new settlement kind only changes the human-readable reason and the log. |
| A deterministic slow-reader seam exists. | `VirtualCheckpointIo.StreamFactory` replaces the handle stream and `HostCleanupCheckpointFixture.Clock` is a `ControlledTimeProvider` whose `Advance` fires due timers synchronously and records every timer `create`/`change`/`fire`. `BlockedRead` and `Stalled_receipt_read_is_retried_once` in `HostCleanupCheckpointCustodyTests` already drive the 3-second window this way. | The reader witnesses need no production seam. The ledger witnesses need a stamp-function seam beside the existing `stat` seam (D-4). |
| CARD-1356: `--row-timeout` extends the build. | False. `RunScheduler.cs:421-425` bounds the build by `TimeSpan.FromMinutes(Math.Max(15, request.Manifest.Timeouts.RowMinutes))`; `RowTimeoutOverride` is applied only on the row path (`:522-523`). The plan importer sets per-row `TimeoutMinutes` (`PlanTableImporter.cs:154`) and never `timeouts.rowMinutes`, so a build is always 15 minutes unless the manifest YAML sets that key. The rerun with `--row-timeout 40m` passed because the load had dropped, not because the flag reached the build. | Derive the build deadline from its rows and honour the override (D-6). |
| The PowerShell reader is unaffected. | `scripts/lib/checkpoint-file-reader.ps1:46-60` carries the same fixed 3-second `CancelAfter` and 8 KiB loop, used by `checkpoint-source.ps1:101` to hash untracked files for the `run-checkpoint.ps1` source stamp. | Out of this card's scope (the land gate runs the tool, CARD-0723); file a sibling card (D-7). |

## Decisions

- **D-1 — Progress re-arms a 3-second stall window; no size-scaled total.** Keep `StallDeadline = 3 s` as the one constant (it is the CARD-1318 stalled-receipt contract). Each chunk that returns bytes calls `CancelAfter(StallDeadline)` on the stall source; the total is bounded only by `limit / chunk × StallDeadline`. Rejected: a total deadline scaled by size (`3 s + n s/MiB`) reintroduces a throughput assumption, which is the defect; a larger fixed constant moves the cliff. The stall source is `new CancellationTokenSource(StallDeadline, context.Time)` so `CancelAfter` re-arms the provider's timer, which the controlled clock records as a `change` event. Fallback if the runtime does not re-arm the provider timer (W-1 stays red after the fix): own the timer with `context.Time.CreateTimer`, re-arm with `Change`, and guard the callback against a disposed source.
- **D-2 — Chunk 256 KiB, growth check unchanged.** `Math.Min(buffer.Length, limit - content.Length + 1)` keeps reading one byte past the limit so a file that grows during the read is still refused `selected file exceeds size limit`. Rejected: an async handle (`FileOptions.Asynchronous`), which changes the native open flags qualified by CARD-1199.
- **D-3 — `unreadable` is a settlement kind, not a tamper.** Per entry at admission, verification and settlement: current stamp unreadable, or recorded stamp unreadable, is `UNREADABLE <path>: <reason>`; recorded readable and current readable but different is `TAMPERED <path>` with today's manual command. Any `TAMPERED` makes the kind `tampered` and the `UNREADABLE` lines are appended before the `manual restore required` line; only `UNREADABLE` makes the kind `unreadable`, first line `refused: unreadable`, closing line stating that no content difference was observed, nothing in the worktree or index was changed, no restore is needed and the run should be repeated. `Settlement.Paths` keeps listing every refusing path. Exit stays 2. Rejected: treating an unreadable entry as clean (unprovable is not clean); reusing `tracked-snapshot` (that kind means git itself failed).
- **D-4 — Stamp reason and a stamp seam.** `Stamp` returns `unreadable|<reason>` where the reason is the innermost message on one line, at most 200 characters; `IsUnreadable(stamp)` replaces the four `== "unreadable"` comparisons. `CaptureLedger` gains `Func<string, string>? stamp` beside its `stat` seam and stores it on the `Ledger` record; `Settle` gains the same optional parameter; `Prepare` keeps the real stamp. `BuildConfinement.Stamp` becomes `internal` so a test can delegate to it (`InternalsVisibleTo("Antiphon.Tests")` exists). Rejected: a hard-link or `chmod` fixture (process spawn, root-insensitive, Windows-divergent); a context seam (the ledger tests run real git repositories the virtual IO cannot see).
- **D-5 — `RowTimeout.KeepsConfinement` unchanged.** A build that both timed out and left an unreadable entry reports the timeout (exit 5) with the `UNREADABLE` detail in `build.log`; only `tampered` and `restore-failed` outrank a timeout, because they are findings and a stall is not.
- **D-6 — Build deadline from dependent rows; override applies as for rows.** One helper computes a row's deadline (`spec.TimeoutMinutes ?? RowTimeout.DeriveRowMinutes(spec.EstimatedMinutes, null, manifest.Timeouts.RowMinutes)`, replaced by `RowTimeoutOverride` when set) and is used by both the row path and the new build derivation, so they cannot drift. The build deadline is `max(15, timeouts.rowMinutes, max over request.Rows with Build == id)`, or the override. The value is stored in `BuildProgress.DeadlineMinutes` and copied to `ReportBuild.DeadlineMinutes` from the same local that is passed to `RunWithDeadlineAsync`. Rejected: scaling by slot wait (the build starts after the slot is granted, the wait is already over) and by load average (not portable, and a throughput assumption again); a new `build.timeoutMinutes` manifest key (nothing imports it, and the card's operators reach for `--row-timeout`).
- **D-7 — PowerShell reader is a sibling card, not a slice.** `scripts/lib/checkpoint-file-reader.ps1` has the identical loop; port D-1/D-2 there under its own card so this Code task stays inside its budget and the pwsh harness tests (`CheckpointBoundedReaderTests`) are run by a task that owns them.
- **D-8 — Master first; rc cherry-pick is a separate land.** Both cards are bug fixes to code already on `rc/2026-10-18` (the reader landed with CARD-1199 on 2026-10-13, the build deadline predates it), so `docs/release-gates.md` admits them on the line. The rc value exists only while rc-line Code/Review tasks produce receipts on a loaded host before the next cut from master; the caller decides that after the master land, taking S1+S2 at minimum.
- **D-9 — No session that waits for input is added or changed.** The checkpoint tool is a child process of a Code/Review task; nothing here parks, blocks on input or changes a release deadline (CARD-1083 checklist: not applicable).

## Design

### S1 — reader stall window (`src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs`)

```csharp
public static readonly TimeSpan StallDeadline = TimeSpan.FromSeconds(3);
public const int ReadChunk = 256 * 1024;

using var stall = new CancellationTokenSource(StallDeadline, context.Time);          // open + inspect + first chunk
using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stall.Token);
var content = new MemoryStream();
try
{
    ... open, Inspect, Refusal, size check, afterVerification: unchanged ...
    var buffer = new byte[ReadChunk];
    while ((count = read(buffer, deadline.Token)) != 0)
    {
        if (content.Length + count > limit) throw new InvalidDataException("selected file exceeds size limit");
        content.Write(buffer, 0, count);
        stall.CancelAfter(StallDeadline);                                            // progress re-arms the window
    }
    return content.ToArray();
}
catch (OperationCanceledException) when (stall.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
{
    throw new IOException($"read-stalled: {content.Length} of {metadata.Size} bytes; no progress within {StallDeadline.TotalSeconds:0}s");
}
```

A stall that fires after the last data chunk, while the end-of-file read is in flight, does not fail a read whose bytes are complete. `ReceiptFile.ReadBytes` keeps wrapping both exception types into `receipt-read-refused: <path>: <msg>`; `BlockedRead` and `Stalled_receipt_read_is_retried_once` still see their refusals after 3 silent seconds.

### S2 — ledger classification (`tools/Antiphon.Checkpoints/BuildConfinement.cs`)

- `internal static string Stamp(string full)`: on `IOException`/`UnauthorizedAccessException`/`SecurityException` return `"unreadable|" + Reason(ex)`; `IsUnreadable(string)` and `ReasonOf(string)` helpers.
- `Ledger` gains `Func<string, string> Stamp`; `CaptureLedger(workingDirectory, arguments, stat = null, stamp = null)`; `Settle(baseline, stamp = null)`. The tracked-unreadable capture refusals append `UNREADABLE <path>: <reason>` to their detail.
- `AdmitLedger`, `VerifyLedger`, `Settle` collect `changed` and `unreadable` lists per D-3 and call one `Classify(repository, head, changed, unreadable, tracked, staged)` that renders the text; `Tampered(...)` is folded into it.
- `docs/testing-and-build.md`: rewrite the hard-link sentence at `:476`; add `refused: unreadable` to the exit-2 list at `:491`; rewrite "cancellable three-second read deadline" at `:1224` as the stall window.

### S3 — build deadline (`tools/Antiphon.Checkpoints/Execution/RunScheduler.cs`, `State/RunState.cs`, `Report/ReportModel.cs`, `CheckpointApp.cs`)

```csharp
internal static int RowDeadlineMinutes(CheckpointSpec spec, CheckpointManifest manifest, TimeSpan? rowTimeoutOverride);
internal static int BuildDeadlineMinutes(CheckpointManifest manifest, IEnumerable<CheckpointSpec> rows, string buildId, TimeSpan? rowTimeoutOverride);
```

`BuildOneAsync` sets `progress.DeadlineMinutes` right after `progress.State = "building"` and passes `TimeSpan.FromMinutes(progress.DeadlineMinutes)` to `RunWithDeadlineAsync`; the row path at `:522-523` calls `RowDeadlineMinutes`. `CheckpointApp.BuildReport` copies the field to `ReportBuild.DeadlineMinutes`. `docs/testing-and-build.md` gets one sentence in the CARD-0723 scheduler paragraph (`:466`).

## Slices

### S1 — reader

Files: `src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs`; new `tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointReadDeadlineTests.cs` (`[Category("Unit")]`, no process spawn, uses `HostCleanupCheckpointFixture` and a `PacedStream` that advances `f.Clock` inside `ReadAsync` and serves at most 64 KiB per read).

Red commit: the two witnesses W-1 and W-2 (red on assertion: W-1 throws `receipt-read-refused` at the fourth read, W-2's message lacks `read-stalled`). Fix commit: D-1 and D-2. Then CP-1 to CP-3.

### S2 — ledger

Files: `tools/Antiphon.Checkpoints/BuildConfinement.cs`; `tests/Antiphon.Tests/Checkpoints/CheckpointConfinementLedgerTests.cs` (`LedgerDriver` gains a `stamp` argument passed to `CaptureLedger`); `docs/testing-and-build.md`.

Red commit: the inert seam (D-4 parameters, `Stamp` made internal) plus W-3 to W-6, each red on `Kind`/text assertions (`tampered` where `unreadable` is expected, `TAMPERED node_modules/pkg/f0005.js` where `UNREADABLE` is expected). Fix commit: D-3 classification and the doc lines. Then CP-4 to CP-6.

### S3 — build deadline (CARD-1356)

Files: `tools/Antiphon.Checkpoints/Execution/RunScheduler.cs`, `State/RunState.cs`, `Report/ReportModel.cs`, `CheckpointApp.cs`; `tests/Antiphon.Tests/Checkpoints/RunSchedulerTests.cs`; `docs/testing-and-build.md`.

Red commit: the inert `DeadlineMinutes` fields plus W-7 and W-8 (W-7 red because the helper returns the manifest floor, W-8 red because the recorded value is 0, never a compile error). Fix commit: D-6 and the doc sentence. Then CP-7 to CP-9.

Each slice is independently landable; S3 may be split into its own Code task on CARD-1356 against this same plan if the budget is exceeded.

## Verification design

Witnesses. Each is one TUnit method; "red" is the assertion that fails at the slice's red commit.

| ID | Behaviour | Witness (class.method), seam, decisive assertions |
|---|---|---|
| W-1 | A read that keeps delivering bytes is not cut off by elapsed time. | `HostCleanupCheckpointReadDeadlineTests.large_file_read_that_keeps_making_progress_completes_past_three_seconds`: 7.5 MiB patterned payload in the virtual node; `PacedStream` advances the clock 1 s per 64 KiB read (120 reads, 120 virtual seconds); `ReceiptFile.ReadBytes(f.Context, f.Payload)` returns bytes `SequenceEqual` to the payload with the same SHA-256; `f.Clock.Events.Count(e => e.Action == "change") >= 119`; `Count("stream") == 1`. Red: `IOException receipt-read-refused` at the fourth read. |
| W-2 | A stall is refused as `read-stalled`; a caller cancel is not. | `...read_with_no_progress_for_the_stall_window_is_refused_as_stalled_and_a_caller_cancel_is_not`: reads 1-2 advance 1 s, read 3 advances 3 s; `Should.Throw<IOException>` message contains `receipt-read-refused` and `read-stalled: 131072 of`; second case cancels the caller token inside read 2 and the message contains `receipt-read-refused` but not `read-stalled`. Red: first case's message lacks `read-stalled`. |
| W-3 | An entry unreadable after release is `unreadable`, not `tampered`. | `CheckpointConfinementLedgerTests.ledger_entry_unreadable_after_release_is_refused_unreadable_not_tampered`: real repo (`BeginRepo`), `Capture(root, stamp: real except node_modules/pkg/f0005.js -> "unreadable\|receipt-read-refused: read-stalled: 0 of 7864320 bytes" once a flag is set)`; set the flag; `VerifyLedger(ledger)`: `Kind == "unreadable"`, `Paths == ["node_modules/pkg/f0005.js"]`, Detail starts `refused: unreadable`, contains `UNREADABLE node_modules/pkg/f0005.js: ` and `read-stalled`, contains neither `TAMPERED` nor `restore --source`. Red: `Kind == "tampered"`. |
| W-4 | A real change beside an unreadable entry is still `tampered`. | `...tampered_entry_beside_an_unreadable_entry_is_still_refused_tampered`: same seam for f0005; `File.WriteAllText` on f0006; `AdmitLedger(ledger, strict: true)`: `Kind == "tampered"`, Detail contains `TAMPERED node_modules/pkg/f0006.js` and its `status --short` command, contains `UNREADABLE node_modules/pkg/f0005.js: ` with no command line for f0005; `Paths` holds both. Red: f0005 printed as `TAMPERED`. |
| W-5 | The in-slot baseline classifies the same way. | `...in_slot_baseline_entry_unreadable_at_settlement_is_refused_unreadable`: `Prepare(root, ledger, ct)` then `BuildConfinement.Settle(baseline, stamp)` with App/obj/stale.txt unreadable: `Kind == "unreadable"`, `Paths == ["App/obj/stale.txt"]`, no `TAMPERED`. Red: `tampered`. |
| W-6 | The report and log surface the distinct refusal. | `...scheduler_reports_an_unreadable_ledger_entry_as_unreadable`: `LedgerDriver(slots, onBuild: set flag, stat: null, stamp: seam)` through `RunScheduledAsync`; `report.Builds.Single().SlotReason == "refused: unreadable"`, `State == "failed"`, exit != 0, `build.log` contains `UNREADABLE node_modules/pkg/f0005.js` and not `TAMPERED`. Red: `refused: tampered`. |
| W-7 | Build deadline derivation. | `RunSchedulerTests.build_deadline_is_the_largest_dependent_row_timeout_and_the_row_override_replaces_it`: manifest default 15; CP-1 on bin-a `TimeoutMinutes = 40`, CP-2 on bin-a `EstimatedMinutes = 10` (30), CP-3 on bin-b `EstimatedMinutes = 1`; `BuildDeadlineMinutes(..., "bin-a", null) == 40`, `"bin-b" == 15`, with override 7 minutes both `== 7`, with `timeouts.rowMinutes = 20` bin-b `== 20`. Red: 15 everywhere. |
| W-8 | The scheduler passes and records the derived value. | `...scheduler_passes_the_derived_build_deadline_and_records_it`: `InstantDriver`, `TwoBuildRows()` with CP-1 `TimeoutMinutes = 40`; `result.State.Builds.Single(b => b.Id == "bin-a").DeadlineMinutes == 40`, `bin-b == 15`; rows green. Red: 0. |

Regression controls executed green in the same run: R-1 the thirteen `HostCleanupCheckpointCustodyTests` and eight `HostCleanupCheckpointReceiptTests` (BlockedRead, CARD-1318 retry, receipt caps, growing stream); R-2 the sixteen existing `CheckpointConfinementLedgerTests` (every `refused: tampered` assertion stays, since those are real changes) and `CheckpointTrackedBaselineTests`; R-3 `TimeoutTests`, the fifteen existing `RunSchedulerTests` and `CheckpointManifestTests` (`timeouts.rowMinutes` default 15).

### Checkpoints

Lane: `Antiphon.Tests`, Unit category, portable; ledger rows spawn git under `ParallelLimiter<ProcessSpawnLimit>`. One build, `bin-c1358/`, rebuilt by each run. Counts are executed TUnit cases; a Windows-only method skips off Windows and is listed so.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1358/` | portable-unit-reader | `/*/*/HostCleanupCheckpointReadDeadlineTests/*` | W-1, W-2 | 2 methods, 0 failed/skipped | 2 | 8 |
| CP-2 | S1 | CP-1 | portable-unit-custody | `/*/*/HostCleanupCheckpointCustodyTests/*` | R-1 | 13 executed, 0 failed/skipped | 13 | 1 |
| CP-3 | S1 | CP-1 | portable-unit-receipt | `/*/*/HostCleanupCheckpointReceiptTests/*` | R-1 | 8 executed, 0 failed/skipped | 8 | 1 |
| CP-4 | S2 | CP-1 | process-unit-ledger-new | `/*/*/CheckpointConfinementLedgerTests/(ledger_entry_unreadable_after_release_is_refused_unreadable_not_tampered*)\|(tampered_entry_beside_an_unreadable_entry_is_still_refused_tampered*)\|(in_slot_baseline_entry_unreadable_at_settlement_is_refused_unreadable*)\|(scheduler_reports_an_unreadable_ledger_entry_as_unreadable*)` | W-3 to W-6 | 4 executed, 0 failed/skipped | 4 | 3 |
| CP-5 | S2 | CP-1 | process-unit-ledger | `/*/*/CheckpointConfinementLedgerTests/*` | R-2 | 20 executed, 0 failed/skipped | 20 | 10 |
| CP-6 | S2 | CP-1 | process-unit-baseline | `/*/*/CheckpointTrackedBaselineTests/*` | R-2 | 13 executed, 0 failed/skipped | 13 | 3 |
| CP-7 | S3 | CP-1 | portable-unit-deadline | `/*/*/RunSchedulerTests/(build_deadline_is_the_largest_dependent_row_timeout_and_the_row_override_replaces_it*)\|(scheduler_passes_the_derived_build_deadline_and_records_it*)` | W-7, W-8 | 2 executed, 0 failed/skipped | 2 | 1 |
| CP-8 | S3 | CP-1 | portable-unit-scheduler | `/*/*/RunSchedulerTests/*` | R-3 | 17 executed, 0 failed/skipped | 17 | 2 |
| CP-9 | S3 | CP-1 | portable-unit-timeouts | `/*/*/TimeoutTests/*` | R-3 | 8 listed; off Windows 7 executed and `windows_quick_row_finishes_beside_a_slow_row` skipped; 0 failed | 7 | 2 |
| CP-10 | S3 | CP-1 | portable-unit-manifest | `/*/*/CheckpointManifestTests/*` | R-3 | 7 executed, 0 failed/skipped | 7 | 1 |

Red evidence: commit each slice's witnesses first and run only that slice's new-witness rows (`--rows CP-1`, `--rows CP-4`, `--rows CP-7`) expecting exit 1 with the assertion texts above in the row console log; then commit the fix and run the whole table once. The build is the cost driver, so S1 to S3 witnesses may share one red commit and one red run when the Code task takes all three slices.

### Cost

Execution: one `Antiphon.Tests` build (about 8 minutes) plus about 24 row-minutes, two rows at a time on Linux: roughly 20 minutes per full run, twice (red, green). Authoring: S1 15 minutes, S2 20 minutes, S3 15 minutes. S1+S2 fit the 45-60 minute Code budget; S3 adds about 15 minutes. Review repeats the final required selection with fresh provenance. No suite run is commissioned.

## Platform and dispatch

`GET /api/runner-defaults` reports `globalRunnerId: server2` (operator 2026-09-26: default everything to server2; desktop only for Windows-only work). `GET /api/session-runners` lists `desktop` (windows), `server2` and `server2-temp` (linux). The witnesses are virtual-IO and controlled-clock, the ledger tests run real git on any OS, and the production change has no OS-specific branch beyond the chunk size, so dispatch Code and Review unpinned: omit `-Runner` and `-Platform`. The original failure was on the Windows desktop; a re-run of the CARD-1305 checkpoint there after the land is a confirmation the orchestrator may ask of that task, not a checkpoint row here.

## Completion and handoff

Land S1-S3 to master through the normal pipeline (D-8 for the rc line; D-7 for the PowerShell sibling card). Review must read the classification text in `build.log` for W-3/W-4/W-6 and the recorded `deadlineMinutes` in `report.json` for W-8, not approve on exit codes alone. The next stage is **code**; this section is the verification design, so TestDesign is not a separate stage unless the orchestrator wants a second reading of the witness table.
