# CARD-1124 S1+S2 Final Review (task b4ac501a)

Reviewed: Code owner `a1541b42-51eb-4b93-8c06-6483e540b12c`, branch `feat/card-task-a1541b42` at
`f92d392c6575384e64f8019e10dd6c5e301eaae9`, three commits over master `46a3b81c2b9ab1bdbf9b9b815b29d3b4cd11b7b8`
(478550df6 pins/DTO/failing tests, d137280c8 predicate/projection/docs, f92d392c6 S2). The whole range
master..tip was reviewed. origin/master has moved to `80db50749fcead4278c06f1147ef8e0811416bb6` (one commit,
CARD-1082 settlement sync debt); `git merge-tree --write-tree origin/master HEAD` exit 0, tree `375a9969d`,
no conflicting hunk. Evidence diff 46a3b81c2..f92d392c6: commits=3 entries=0 violations=0.

## Verdict: clean, ordinaryScopeCompleted Full

No defect. The S1 Final Review's D-1 (two red sampler assertions left on master by S1 alone) is resolved
at this tip: `SeatOccupancySamplerTests.cs:46` is `OrphanSlots.ShouldBe(1)` and `:82` is
`Orphan.ShouldBeFalse()`, both exact-value flips of the old assertions (not weaker), and CP-5 is 10/10.
No class that reads the changed semantics is red at the tip. No release path reads `orphan` as authority,
a live Blocked or Queued owner cannot be released or marked failed by the widened predicate, park never
leaks another attempt or another task, and the S1 review's PublicationReceipt regression (settled
published orphan reading `pushed=unknown`) is restored to `yes` and pinned.

## Rerun (one checkpoint-tool run 20261007-033056-11fe, serial, one isolated build)

Manifest: hand-made twelve-row table in the scratchpad, `start --plan ... --serial --expected-source-sha
f92d392c6 --keep-outputs --total-timeout 120m`. Source clean, `buildSource=verified` on every row,
`unlisted: none`. Wall 10m50s, build 152 s. `validate --evidence report.json --expected-source-sha
f92d392c6`: `CHECKPOINT SOURCE VALID rows=12`. Rosters checked from each TRX (distinct methods and
results) against the expected set. Evidence: `.antiphon/checkpoints/20261007-033056-11fe/report.md`
(gitignored, on the mirror under `/work/worktrees/task-b4ac501a`).

| Row | Filter | Executed | Methods | Result |
|---|---|---:|---:|---|
| CP-1 | `/*/*/SeatDesktopJoinTests/*` | 4 | 4 | green (V-1, V-2, R-1) |
| CP-2 | `/*/*/RunnerSlotEndpointTests/*` | 10 | 10 | green (V-3, V-4, R-2) |
| CP-3 | `/*/*/RunnerSlotRulesTests/*` | 2 | 2 | green (R-3) |
| CP-4 | `/*/*/BlockedTaskParkProjectionTests/*` | 2 | 2 | green (V-7) |
| CP-5 | `/*/*/SeatOccupancySamplerTests/*` | 10 | 10 | green (V-5, R-4; D-1 flips) |
| CP-6 | `/*/*/SeatOccupancyAttentionTests/*` | 10 | 10 | green (V-6, R-5) |
| CP-7 | `/*/*/SeatOccupancyProjectionTests/*` | 15 | 4 | green (R-6) |
| CP-8 | `/*/*/HostOccupancySampleEndpointTests/*` | 9 | 9 | green |
| CP-9 | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | 3 | 3 | green |
| CP-10 | `/*/*/RunnerSeatOrphanSweepTests/*` (alone) | 17 | 17 | green, 72 s hostWall (CARD-1137 did not reproduce) |
| CP-11 | `/*/*/TerminalRunnerSeatReleaseTests/*` (alone) | 39 | 29 | green (R-7) |
| CP-12 | `/*/*/BlockedTaskParkReclaimTests/*` | 7 | 7 | green (reads `RunnerSlotsDto` via the slots route) |

Selection: the plan's S1+S2 rows (CP-1..CP-7), the brief's three extra classes, and the grep for
`IsOrphan|SeatDesktopJoin|RunnerSlotDto|RunnerSlotsDto|RunnerSlotParkDto|orphanSlots|SeatOccupancySampler|
SeatObservation|HostOccupancyObservation|SeatDesktopRow|SeatParkRow` over `tests/`. That grep added
`BlockedTaskParkReclaimTests` (deserialises `RunnerSlotsDto` at `:892` and `:918`); `TerminalRunnerSeatReleaseTests`
is the plan's R-7 partner of the sweep and ran as its own row. Excluded as false hits on
`TerminalSeatObservation`: `tests/Antiphon.SessionRunner.Tests/BlockedParkWireTests.cs`,
`TerminalSeatReleaseTests.cs` and `RunnerSeatReleaseFixture.cs` (none reads a changed symbol). No whole-Unit
run, per the brief.

## Code report check

CP-1 4, CP-2 10, CP-3 2, CP-4 2, CP-5 10, CP-6 10, CP-7 15 match the plan table's Expect and this rerun;
the extras (host samples 9, registry 3, sweep alone 17) and the evidence guard reproduce. The Code run
20261007-030528-a003 was `GREEN`, `unlisted: none`; the two unlisted drivers outside it carry reasons
(pre-commit compile to `bin-c1124-s2/`; the D-1 red proof at the S1 tip, 2/2 failed under lease
50a03f77) and the three extra classes ran through `run-checkpoint.ps1`, which takes its own slot.
Every new test has a real outcome assertion and a negative arm: V-5 keeps `OrphanSlots` above zero through
the unbound seat, contrasts the settled published seat (`pushed=yes`, attempt 2 receipt) with the
historical-attempt seat (`pushed=unknown`), and compares park revision/state, the Working task's status and
the Blocked session's `Running` before and after two samples; V-6 asserts the absence of the
`slot-orphan` key for the Blocked seat and its presence for the unbound seat.

## Hard checks

(a) Nothing red on master after landing: all twelve classes above are green at the tip. The two flipped
sampler assertions assert the new truth with the same exact-value form (`ShouldBe(2)` to `ShouldBe(1)`,
`ShouldBeTrue()` to `ShouldBeFalse()`); the Code task proved both red at the S1 tip before flipping them.

(b) Release authority: `IsOrphan` is called at exactly three sites: `RunnerSlotService.ListAsync:45`
(display), `ReconcilePendingReleasesAsync:137` (only for a `pending:orphan:` intent, only to mark it
`failed:` when the seat is no longer an orphan, so the widened predicate can only turn more intents into
`failed:`; V-4 proves the Blocked owner stays `Running` with zero `ReleaseSlot`/`KillGeneration`, and the
Succeeded seat is still reconciled and Stopped), and `SeatOccupancySampler:122` (display). `ReleaseAsync`
passes `fromOrphanSweep: false` and never reads the flag. `RunnerSlotService.ReleaseOrphansAsync` returns
`(0, [])` without the terminal service; `TerminalRunnerSeatReleaseService.ReleaseOrphansAsync:227` gates on
`AutomaticEnabled` and `DiscoverCandidateAsync:317-360` classifies from the session row, runner ids,
generation and the latest task (Blocked goes to `TryHandleTaskAsync`, itself gated by `ParkingEnabled`),
never `IsOrphan`. No file in `TerminalRunnerSeatReleaseService`, `TaskParkPublicationService`,
`BlockedTaskParkingService` or the dispatcher is in the range. True orphans still read `orphan=true`
(V-3 seat B, V-1 settled arms, the sampler's unbound seat) and still emit `SlotOrphan` (V-6, R-5);
the sweep and release regression classes (CP-10, CP-11, CP-12) are green.

(c) Park: `SeatDesktopJoin.CurrentPark` filters `TaskId == owner.Id && Attempt == owner.Attempt` and picks
newest `CreatedAt` then greatest `Id`, mirroring the task-detail rule; null with no owner, no row, or only a
historical attempt (V-2 sessions B and C; the newer attempt-2 row wins on session A). The slots route maps
it to `RunnerSlotParkDto` without the worktree path (canary absent, V-3). While parking is off no park row
is ever written (`TryHandleTaskAsync:77` returns before `RegisterAsync`), so `park` is null everywhere by
construction; the projection reads rows, not the option, and a pre-existing row remains truthfully visible.

(d) Statements: the join is pinned at nine (V-2, `CountingCommandInterceptor`, no INSERT/UPDATE/DELETE).
Mutation: one extra `CountAsync` in `SeatDesktopJoin.LoadAsync` (incremental build into the kept outputs)
turned V-2 red, `should be 9 but was 10`; reverted. The sampler tick has no committed pin, so the review
measured it with a temporary probe test on `SeatOccupancySamplerTests.Rig` (not committed, reverted):
14 reader commands = one `HostBudgets` read, the local-host count, the three runner-host queries
(`SeatOccupancySampler.cs:68-78`), the join (eight on that rig because its tasks carry no card; nine with a
card, so twelve per runner host as claimed) and the `HostOccupancySamples` insert. One extra `CountAsync` in
`SampleRunnerAsync` plus the join mutation turned the probe red, `should be 14 but was 16`; reverted.
No committed test would catch an added sampler query: CARD-1138. Writes: none from the join or the slots
GET (V-3 snapshot equal before and after); the tick's writes are the prune and the sample insert only.

(e) Operator impact (CARD-1079 D-7): `BuildSeatIdleItems` reads `seat.Class` and `IdleSince`, never
`Orphan`; `Classify` still maps Blocked to `IdleBlocked`, so the `SeatIdle` row appears from
`SeatIdleWarningMinutes` with Reply, Cancel and OpenDrawer (V-6: 40 minutes idle gives Warning, actions
`[Reply, Cancel, OpenDrawer]`, evidence `park=Held:park_dirty`, `pushed=unknown`). `SlotOrphan` still fires
for an occupying seat with no live row or no owner (V-6 unbound seat `park=none`; V-3 seat B; V-5
settled seats). `OccupancyDivergence` reads `InFlight`, `DispatchedWorking` and `IdleSeats`, all
unchanged. The only alert lost is the immediate `SlotOrphan` for a live Blocked child, which the ops-http
and lifecycle sentences now state. The instruction files still say "count orphan=true" (pins
`c1065-loop-orphan`, `c1065-orphan-count` unchanged); that rewrite is S3 by plan.

(f) Docs: the plan's sentences 1-7 are present verbatim (diff read). Probes from the kept outputs, no
build, each reverted: ops-http "seat evidence carries `park=`" rewritten failed `c1124-ops-seat-park`;
antiphon-api "also counted live Blocked owners" rewritten failed `c1124-api-orphan-slots`;
`BlockedTaskParkingOptions.cs` `Enabled = true` failed the default-off pin `c1065-options-enabled`. No
assertion weakened or deleted: the replaced `C1079_Join_open_task_is_dispatched_or_working_only` keeps its
Dispatched, Working and mixed-session arms inside the owners loop and `mixedWorkingSession`. No migration:
`RunnerSlotDto.Park`, `SeatDesktopRow.Park`, `SeatObservation.ParkState/ParkReason` are trailing defaulted
parameters and no `Migrations/` file is in the range.

## Disclosures (no card for the first two; CARD-1138 for the pins)

- CARD-1138 (Backlog): no committed pin on the sampler tick's statement count, and no test for the
  cross-task park arm (settled latest task with a park row, older open owner without one; the code
  filters on the owner's task id, so `Park` is null and the receipt follows the latest task).
- S3 remains its own slice: SKILL.md and orchestration-loop still instruct "count orphan=true".
- CARD-1137 did not reproduce with the sweep alone (17/17, 72 s hostWall).

## Platform and process

`GET /api/runner-defaults` global runner server2, no kind defaults; `GET /api/session-runners`: desktop
windows, server2 linux, server2-temp linux; no runner or platform pin. Build-slot broker budget 4, one
lease occupied at start. Every build and run took a host build slot: the driver build, the twelve rows,
three text-pin probe runs, two incremental probe builds and three probe runs (all `slot=granted`,
`waited=0s`). The checkpoint tool's `clean --run` removed the kept outputs, the driver `bin-c1124r2-drv`
was removed with a root-confined rm, no `bin-c1124*` directory remains and the tree is clean. PC-1..PC-11
stay pending for method-scoped SourceLanding Mutation.

--- review evidence ---
subjectTaskId: a1541b42-51eb-4b93-8c06-6483e540b12c
reviewedSourceSha: f92d392c6575384e64f8019e10dd6c5e301eaae9
reviewedSourceClean: true
ordinaryScopeCompleted: Full
