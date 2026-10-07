# CARD-1124 S1 Final Review (task 1a2d0c15)

Reviewed: Code owner `31d3fbaa-9d13-4a72-b189-df78bb652e3c`, branch `feat/card-task-31d3fbaa` at
`d137280c81f7ff24e619e9e3cdc7e57808e19bf4`, two commits over master
`46a3b81c2b9ab1bdbf9b9b815b29d3b4cd11b7b8` (478550df6 pins/DTO/failing tests, d137280c8
predicate/projection/docs). origin/master had not moved; `git merge-tree --write-tree
origin/master HEAD` exit 0 (tree bd604688b), no conflicting hunk. Evidence diff
46a3b81c2..d137280c8: commits=2 violations=0.

## Verdict: found (one defect), ordinaryScopeCompleted Full

**Defect D-1 (red tests on master).** Where: `tests/Antiphon.Tests/Application/SeatOccupancySamplerTests.cs:46`
(`OrphanSlots.ShouldBe(2)`) and `:82` (`blocked.Orphan.ShouldBeTrue()`). Failure: at this SHA both
assertions are red (`should be 2 but was 1`, `should be True but was False`); the checkpoint tool's
`--baseline 46a3b81c2` stage ran both at master and they pass, classification INTRODUCED. Why: S1
widened the owner predicate, so the rig's live Blocked seat correctly reads `orphan=false`, and the
plan assigns the assertion flips to S2 ("S2 still owns :46 and :82"). The plan does not sequence S1
and S2 as one landing: `## Implementation slices` says "Each slice commits its docs and pins with its
code and ends with its checkpoint rows green at that commit", CP-1..CP-4 do not include the sampler,
and the Handoff only says "One Code task may take S1-S3 in order if the orchestrator prefers".
Landing S1 alone therefore puts two red tests on master. Fix: before land, flip the two assertions
(`OrphanSlots` 1, `Orphan` false) in the same branch, either by doing S2 on this branch or by a
minimal S1 amendment that moves those two lines out of S2 (the plan's S2 row keeps the new
`C1124_Blocked_owner_is_idle_not_orphan_and_carries_its_park` test). No other class touching
`IsOrphan`, `SeatDesktopJoin`, `RunnerSlotDto`, the sampler or `orphanSlots` is red at this SHA.

## Rerun (one checkpoint-tool run, run 20261007-022934-d2fe, serial, one isolated build)

Manifest: hand-made ten-row table (scratchpad), `start --plan ... --serial --expected-source-sha
d137280c8 --baseline 46a3b81c2 --keep-outputs`. Source clean, buildSource verified on every row.
Wall 8m21s (build 139 s). `validate --rows CP-1,CP-2,CP-3,CP-4,CP-6,CP-7,CP-8,CP-9`: SOURCE VALID
rows=8. Evidence: `.antiphon/checkpoints/20261007-022934-d2fe/report.md` (gitignored, on the mirror).

| Row | Filter | Executed | Result |
|---|---|---:|---|
| CP-1 | `/*/*/SeatDesktopJoinTests/*` | 4 | green (C1079 blocked/warm/receipt, C1079 grouped query, C1124 owner, C1124 nine statements) |
| CP-2 | `/*/*/RunnerSlotEndpointTests/*` | 10 | green |
| CP-3 | `/*/*/RunnerSlotRulesTests/*` | 2 | green |
| CP-4 | `/*/*/BlockedTaskParkProjectionTests/*` | 2 | green |
| CP-5 | `/*/*/SeatOccupancySamplerTests/*` | 9 | 2 red (D-1), INTRODUCED |
| CP-6 | `/*/*/SeatOccupancyProjectionTests/*` | 15 | green |
| CP-7 | `/*/*/SeatOccupancyAttentionTests/*` | 9 | green |
| CP-8 | `/*/*/HostOccupancySampleEndpointTests/*` | 9 | green |
| CP-9 | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | 3 | green |
| CP-10 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | 56 | 1 red: load noise, see below |

CP-10's red, `RunnerSeatOrphanSweepTests.Discovery_request_uses_runner_owned_delivery_evidence`, was a
`TaskCanceledException` in `SessionRunnerHttpClient.ListAsync` from the fixture's `AcquireAsync`
after 23.9 s with all 56 methods started in one 65 ms window. It passed alone at master (baseline
stage, 18.5 s) and alone at this SHA from the kept outputs (10.8 s). S1 touches nothing on that path.
Filed as **CARD-1137** (Backlog). Not a CARD-1124 defect.

Code report check: CP-1 4, CP-2 10, CP-3 2, CP-4 2 match the plan table and my rerun; registry
guard 3 and evidence guard 2/0 reproduced; `unlisted: none` in the Code run; the stopped run
20261007-021301-3e21 produced no rows. Every new test has a real outcome assertion and a negative
arm (V-1 settled statuses and the Stopped session; V-2 sessions B and C and the attempt-1 receipt;
V-3 seat B orphan and the canary; V-4 the Succeeded seat reconciled).

## Hard checks

(a) Release authority: `ListAsync` and `SeatOccupancySampler` read `IsOrphan` for display only.
`ReleaseAsync` passes `fromOrphanSweep: false` and never reads the flag. `ReleaseOrphansAsync`
returns `(0, [])` without `TerminalRunnerSeatReleaseService`; that service gates on
`AutomaticEnabled` and classifies in `DiscoverCandidateAsync` from the session row, runner ids and
the latest task status (Blocked goes to `TryHandleTaskAsync`), never `IsOrphan`.
`ReconcilePendingReleasesAsync` reads `IsOrphan` only on a `pending:orphan:` intent and only to mark
it `failed:` when the seat is no longer an orphan; the widened predicate can only turn more intents
into `failed:` (V-4 proves the Blocked owner stays Running, the Succeeded seat is still reconciled
and Stopped, zero ReleaseSlot/KillGeneration). A seat with no live row, no owner task and not warm
pool still reads `orphan=true` (V-3 seat B, existing `:79` arm, V-1 settled arms).
(b) `Park` is null with no park row (V-2 session B, V-3 seat B) and null when only a historical
attempt has a row (V-2 session C, attempt 3 with an attempt-1 park); the newest current-attempt row
wins (V-2 newer attempt-2 row). Parking off is not read; the projection reads rows only.
(c) Statements: V-2 measured 9 reader commands with no INSERT/UPDATE/DELETE. Mutation probe: one
extra `CountAsync` in `SeatDesktopJoin.LoadAsync` (50 s incremental build into the kept outputs,
52 s run) turned `C1124_Join_reports_the_current_attempt_park_in_nine_statements` red
(`should be 9 but was 10`); reverted, tree clean. Per host tick: `SampleRunnerAsync` issues three
queries before the join (sessions, pending count, dispatched/working count), so 12 reader statements
plus one inventory RPC, verified by reading `SeatOccupancySampler.cs:68-78`; no test pins the 12.
(d) Docs: sentences 1, 2 and 4 of the plan table are present verbatim. Probes from the kept outputs,
each reverted: restoring "remains orphan=true" in `session-runtime-invariants.md` failed
`c1124-no-stale-orphan`; rewriting "a live Blocked child is not an orphan" in `ops-http.md` failed
`c1124-ops-orphan-owner`; flipping `Enabled = false` to `true` in `BlockedTaskParkingOptions.cs`
failed the default-off pin `c1065-options-enabled`. The S3-owned `count orphan=true` pins still
match the unchanged instruction files.
(e) No assertion weakened or deleted: the two flipped `OpenTaskId` arms moved from `ShouldBeNull` to
`ShouldBe(task)`; `C1079_Join_open_task_is_dispatched_or_working_only` was replaced by the wider
`C1124_Join_owner_task_is_queued_dispatched_working_or_blocked`. No migration; `RunnerSlotDto.Park`
is a trailing defaulted parameter.

## Disclosure (no card; hand to S2)

`SeatDesktopRow.PublicationReceipt` is now keyed on the owner task's current attempt (plan D-2) instead
of the latest task. For an orphan seat whose latest task is settled with a receipt-bearing park, the
sampler's `Pushed` field flips from `yes` to `unknown` in `OrphanEvidence`. No test pins either
reading; S2 owns the evidence text and should decide whether to pin it.

## Platform and process

`GET /api/runner-defaults` global runner server2, no kind defaults; `GET /api/session-runners`:
desktop windows 0/2, server2 linux 0/10 draining, server2-temp linux 3/10. No runner or platform pin
needed. Every build and run took a host build slot (driver build, checkpoint rows, three pin probes,
one mutation build, two method runs). Kept outputs removed with `clean --run`; driver `bin-c1124r-drv`
removed with a root-confined rm; no `bin-c1124*` directory remains; tree clean. PC-1..PC-11 stay
pending for method-scoped SourceLanding Mutation.

--- review evidence ---
subjectTaskId: 31d3fbaa-9d13-4a72-b189-df78bb652e3c
reviewedSourceSha: d137280c81f7ff24e619e9e3cdc7e57808e19bf4
reviewedSourceClean: true
ordinaryScopeCompleted: Full
