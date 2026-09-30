# CARD-0810: one build lane per checkpoint run (shared `obj/` collision)

Date: 2026-09-30. Stage: Plan, with the verification design folded in (the brief asks for the
closed `### Checkpoints` table). Next: Code. Baseline: `a8b4e9e5` on `feat/card-task-9ed93938`
(clean tree). Card `c1f6c019-056e-481d-96d9-451b34d1b764`, Antiphon board
`8988ca03-7414-47ad-b0b6-51556c701703`, read with `scripts/card.ps1 get CARD-0810`. Related tool
cards, read and left untouched: CARD-0823 (null `renewEverySeconds` crash leaks a slot lease),
CARD-0818 (`CheckpointExecutorLogTests` can wedge the host), CARD-0828 (`CheckpointTaskOwnershipTests`
time out under namespace load). Where this plan touches the same code it says so.

## Outcome and scope

`tools/Antiphon.Checkpoints` never runs two `dotnet build` processes at once inside one run. The
scheduler holds a single **build lane**: it starts the next isolated build only when no build is in
flight, in the order the selected rows first need them, and rows whose build is already done keep
running beside the lane's current build exactly as today. An exclusive row (`serial: true`, a Pty
project, or every row under `--serial`) waits for the lane to be idle and keeps it idle while it runs,
so a `--serial` run is one activity at a time. The run's `state.json`, `report.json`, `report.md` and
`wait` heartbeat show the lane (`maxConcurrentBuilds`, `<build> queued`), so a Review can read from
evidence that no two builds overlapped.

Out of scope, stated so Code does not drift into them:

- Isolating `obj/` per build (`BaseIntermediateOutputPath`, `MSBuildProjectExtensionsPath`,
  `UseArtifactsOutput`). Rejected in D-2.
- `scripts/run-checkpoint.ps1` and `scripts/build-slot.ps1`: one build per invocation already; the
  collision is tool-only.
- The baseline comparer (`Baseline/BaselineComparer.cs`), which already builds one detached checkout
  at a time and never shares a worktree with the run's builds.
- CARD-0823, CARD-0818 and CARD-0828 fixes. The lane reduces CARD-0823's blast radius (at most one
  build lease is held at a time, so a post-grant crash leaks at most one) but does not fix it.
- New CLI flags or manifest fields. `--parallel`, `--serial` and `parallel.maxRows` keep their row
  meaning (D-6).

## Ground truth

What the card asserts against what the code does at the baseline. Line numbers are at `a8b4e9e5`.

| Card assumption | What the code does | Verdict |
|---|---|---|
| The tool isolates each row's build output but at least one shared intermediate folder is not isolated | `BuildStep.BuildArguments` (`Execution/BuildStep.cs:35`) passes only `--property:OutputPath=<bin-x/>`; nothing in the tool, `Directory.Build.props`, `Directory.Build.rsp` or `Directory.Build.targets` sets `BaseIntermediateOutputPath` or `MSBuildProjectExtensionsPath`, so every build of a project in the worktree restores and compiles through the same `<project>/obj/` | Confirmed: `obj/` is shared by design, `OutputPath` moves only `bin` |
| The scheduler starts every prerequisite build together, even under `--serial` | `RunScheduler.RunAsync` (`Execution/RunScheduler.cs:127-130`) creates `buildTasks` for every build a selected non-command row names and starts them all before the row loop; each `BuildOneAsync` takes its own slot lease (`:310`); `--serial` becomes `RunRequest.Serial` (`Program.cs:361`), which `CheckpointApp` maps to `Width = 1` and `SerialAll` (`CheckpointApp.cs:108-109, 144-145`), and `SerialAll` is read only by `IsExclusive` for **row** picking (`:417-421`, `:442-444`) | Confirmed. On server2 (`SessionRunner__BuildSlots__MaxConcurrent=4`) four builds run at once; on the desktop (2 slots, CARD-0823) two |
| Observed twice on 2026-09-29 in tasks 65170902 and 67ee8f94 | `GET /api/agent-tasks/65170902` and `/67ee8f94` (read 2026-09-30): both `workspace=Worktree`, `runnerId=server2`, `observedPlatform=linux`, `runnerSelectionSource=GlobalDefault`. 67ee8f94's stored result: "the tool's parallel builds collided on the shared `obj/` folder in this fresh worktree ... Runs `083451-ebc3` (CP-1..6) and `084529-02c8` (CP-7 alone, 1 rerun) cover every row; `082755-0ef2` also passed CP-4, CP-5 and CP-7." 65170902's stored result no longer carries its text; the card's quote is the record | Confirmed, both on Linux (server2), both against the `tests/Antiphon.Tests` graph, both in a fresh worktree with cold `obj/` |
| The collision is reproducible at will | Plan probes on this runner (server2-temp, linux, dotnet 10.0.401, cold `obj/`, under one build slot): two concurrent builds of `tools/Antiphon.Checkpoints` (1 project) exit 0/0 in 7 s; two concurrent builds of `src/Antiphon.SessionRunner` (5-project graph) exit 0/0 in 20 s; the same two sequentially 25 s then 29 s | Not reproduced in two attempts. It is a race whose window grows with the graph (`tests/Antiphon.Tests` restores and compiles about fifteen projects) and the build count (four at once in the field). On Linux there are no mandatory file locks, so an overlapping write can also succeed and leave a torn intermediate that only fails later in the test host. "Both builds exited 0" is therefore not "no collision" |
| Fix option A: isolate `obj/` per row build | Feasible as a global property, but see D-2: it un-excludes the real `obj/` from the SDK's default globs, forces N full restores and compiles, and needs the same property on every `dotnet run --no-build`, in `run-checkpoint.ps1`'s property guard and in `OutputCleanup` | Rejected (D-2) |
| Fix option B: serialize the prerequisite-build phase when `--serial` is passed | The sharing does not depend on the flag; 67ee8f94 was not a `--serial` run | Adopted and widened: always one build at a time (D-1); `--serial` additionally forbids a build beside a row (D-3) |
| Not stated: a row that runs while another build is in flight is safe | A row runs `dotnet run --project <p> --no-build --property:OutputPath=<bin-x/>` (`BuildStep.RunArguments`, `:52-55`); `--no-build` implies `--no-restore`, the host loads only from `<bin-x/>`, and the concurrent build's writes go to `obj/` and its own `<bin-y/>`. This overlap is today's behaviour (`Pick`, `:423-448`) and is what gives the tool its wall-clock advantage | Kept for non-exclusive rows (D-1); withdrawn for exclusive rows (D-3) |
| Not stated: a sequential second build of the same graph is cheap | Incremental MSBuild skips `CoreCompile` when `obj/` is current and mostly copies into the new `<bin-y/>`; measured 29 s against 25 s for the 5-project graph on this host, where evaluation and copy dominate. The ~15-project `tests/Antiphon.Tests` graph is unmeasured | Expectation, not fact. CP-5 below is a second `tests/Antiphon.Tests` build in the same run; its `seconds` in `report.json` is the measurement (V-9) |
| Not stated: the report can show whether builds overlapped | `RunState.MaxConcurrentRows` (`State/RunState.cs:15`) and `ReportModel.MaxConcurrentRows` (`Report/ReportModel.cs:26`) exist for rows; nothing equivalent for builds. `RunState.Heartbeat` (`:41-47`) prints only builds in state `building`, so a build waiting for the lane would be invisible to `wait` | Closed by D-5 |
| Not stated: existing tests assume parallel builds | `RunSchedulerTests.failed_build_fails_only_its_rows` uses two builds with instant drivers; `CheckpointTaskOwnershipTests.settlement_cancels_two_drivers_and_skips_later_rows` gates two drivers but on **command** rows (no builds); `CheckpointImportTests.serial_import_round_trips_and_excludes_other_rows` schedules command rows; `TimeoutTests.total_deadline_kills_running_and_skips_queued` has one build; `CheckpointFixtures.TwoRowManifest` (two builds) has no caller among the scheduler tests | No existing test needs a weaker assertion. Any that goes red under the lane is a defect to report, not to amend |

Platform facts read on 2026-09-30: `GET /api/runner-defaults` has `globalRunnerId: server2`
(revision 2). `GET /api/session-runners` lists `desktop` (windows, capacity 2 delegated tasks),
`server2` (linux, capacity 10, **draining**, `acceptingNewWork: false`) and `server2-temp` (linux,
capacity 10, occupied 6, accepting work, advertises `workspaceRepositoryV1`). This plan ran on
`server2-temp`. The card is platform `Any`; the checkpoints below carry both estimates and pin no
runner.

## Decisions

### D-1: One build lane per run, always

`RunScheduler.RunAsync` stops creating every build task up front. The main loop becomes the only
place a build starts: when no build is in flight and no exclusive row is running, it starts the build
of the first pending non-command row (row order) whose build is still `pending`, and awaits that
build's task together with the running rows. Rows whose build is `ok` are picked as today (width,
`IsExclusive`) while the lane's build runs. The lane is not a semaphore inside `BuildOneAsync`: a
single decision point in the loop keeps the "no build while an exclusive row runs" rule (D-3) free of
the race a separate build chain would have against `Pick`.

Why always and not only under `--serial`: the shared `obj/` is a property of the worktree, not of the
flag, and the review incident was not a `--serial` run. Why not per-graph: deciding that two builds
have disjoint project graphs needs an MSBuild evaluation, and the gain is one overlapped build whose
second half is mostly copying.

Rejected: serialising only under `--serial` (the card's option B as written); a `SemaphoreSlim(1)`
around the existing eager tasks (order would follow task start order, not row order, and the
exclusive-row rule would need a second lock); graph-aware overlap.

### D-2: `obj/` is not isolated

`--property:BaseIntermediateOutputPath=obj-<name>/` (or `MSBuildProjectExtensionsPath`) would give
every build its own intermediate folder, but:

- The SDK's default item excludes are `$(BaseOutputPath)/**` and `$(BaseIntermediateOutputPath)/**`.
  Overriding the latter un-excludes the real `obj/`, whose generated `*.AssemblyInfo.cs` and
  `*.GlobalUsings.g.cs` from earlier builds are then globbed as `Compile` items (CS0579 duplicate
  attributes). Every project would need `obj/**` in `DefaultItemExcludes` in `Directory.Build.props`:
  a repository-wide build change for a tool-only defect.
- N builds become N full restores and compiles instead of one compile plus N-1 incremental copies,
  on the host whose slots are the scarce resource (CARD-0589).
- `dotnet run --no-build` evaluates the project through `obj/<project>.csproj.nuget.g.props`, so
  every row, rerun and baseline run would need the same property; `run-checkpoint.ps1`'s
  `-MsBuildProperty` guard (which owns `OutputPath`, `OutDir`, `BaseOutputPath`) and `BuildStep.PropertyArguments`
  would have to own a fourth name; `OutputCleanup` would have to learn `obj-<name>` ownership
  beside `bin-<name>`.
- `UseArtifactsOutput`/`ArtifactsPath` changes the `bin-<name>/` layout that CARD-0448's guard,
  `run-checkpoint.ps1`, the delegate docs and `OutputCleanup` all depend on.

### D-3: An exclusive row shares the run with nothing, builds included

`IsExclusive` keeps its meaning (`Serial`, a Pty project, or `SerialAll`). An exclusive row starts
only when `running.Count == 0` **and** no build is in flight; the lane starts no build while an
exclusive row runs. Non-exclusive rows are unchanged: they overlap each other up to `Width` and
overlap the lane's build. Under `--serial` every row is exclusive, so the run is strictly sequential:
build, row, build, row. That is what task 65170902 expected from `--serial` and did not get.

Why: the reasons a row is exclusive are host load and timing sensitivity (Pty hosts, the CARD-0828
pair), and a `-maxcpucount:6` compile is that load. Rejected: exclusivity among rows only (today),
which leaves `--serial` meaning "one row plus one build".

No deadlock: rows wait only for their own build; the lane waits only for a running exclusive row;
a running exclusive row finishes by result or deadline; a build finishes by result, deadline or
cancellation.

### D-4: Build order, unused builds, and builds that never start

The next build is the build of the first pending row, in the selected row order (CP order), whose
build is `pending`; the manifest's `builds:` order is not used. A manifest build no selected row
references is marked `unused` at start (today it sits `pending` forever). When the run ends before a
needed build started (total timeout, external cancel, owner end, admission block) that build is
marked `skipped`; the rows that needed it take the existing terminal state for that path
(`skipped`, `owner-ended`, the admission reason). A build that started and was cancelled stays
`failed`, as today.

### D-5: The lane is visible in state, report and heartbeat

`RunState.MaxConcurrentBuilds` and `ReportModel.MaxConcurrentBuilds` mirror the row counters; the
scheduler records the number of builds in flight when a build starts (1 by construction; the field
is evidence, and PC-1 shows it reaching 2 without the lane). `ReportWriter`'s summary line adds
`max-concurrent-builds: N` after `builds: N`. `RunState.Heartbeat` prints `<id> queued` for every
needed build still `pending`, after the `building` entries, so a `wait` shows why the second build
has not started. Rejected: an `executor.log` line only (not in `report.json`, so Review cannot read it).

### D-6: No new switches

No CLI flag, manifest field or table column. `--parallel`, `--serial` and `parallel.maxRows` keep
their row semantics; `import` and the table schema are unchanged apart from the `Serial` column's
sentence in the owner doc (S3).

### D-7: Slots are unchanged

`BuildOneAsync` keeps acquiring its lease inside the build; the lane wait happens before the slot
wait, so a queued build holds no lease. Rows keep their per-row leases. A slot timeout on the lane's
build fails that build (`failed`, exit 4 for its rows) and frees the lane for the next build, as a
failed build does today.

## Implementation slices

Commit and push each slice as it completes. Scope for the Code dispatch:
`-Scope tools/Antiphon.Checkpoints/**,tests/Antiphon.Tests/Checkpoints/**,tests/Antiphon.Tests/Application/CheckpointManifestDocumentationTests.cs,docs/testing-and-build.md`.

**S1: the lane (`tools/Antiphon.Checkpoints/Execution/RunScheduler.cs`).**

- Replace the eager `buildTasks` list with one in-flight build (`(BuildSpec Spec, BuildProgress Progress, Task Task)?`).
- Loop, in order: terminal checks as today plus D-4's `skipped` marking; `build-failed` sweep as
  today; start the next build when the lane is idle and no exclusive row is running (D-1, D-3, D-4);
  `Pick` gains a `buildInFlight` argument and refuses an exclusive row while one is in flight
  (D-3); when nothing is running and no build is in flight, continue only if a pending row's build is
  still `pending` (the next iteration starts it), otherwise break; `Task.WhenAny` over the running
  row tasks plus the in-flight build task; a completed build clears the lane.
- After the loop and in the drain `catch`, await the in-flight build if any. Exit-code derivation is
  unchanged.
- `BuildOneAsync`, `RunRowAsync`, `IsPtyProject`, `IsExclusive`, `ClosedRow`, `Placeholder` stay.
- Tests, red first, in `tests/Antiphon.Tests/Checkpoints/RunSchedulerTests.cs` (V-1..V-6a) and
  `TimeoutTests.cs` (V-7); `CheckpointTestSupport.cs`'s `FakeDriver` gains `Starts` (each request
  with a snapshot of the requests in flight when it started) and `MaxInFlight(Func<DriverRequest,bool>)`,
  computed from those snapshots, so a test can say "no two builds overlapped" without a timing guess.

**S2: evidence (`State/RunState.cs`, `Report/ReportModel.cs`, `Report/ReportWriter.cs`, `CheckpointApp.BuildReport`).**

- `MaxConcurrentBuilds` on both models, mapped in `BuildReport` beside `MaxConcurrentRows`; the
  summary line in `ReportWriter` (`:45`) gains `max-concurrent-builds: N`.
- `Heartbeat` prints `<id> queued` for needed pending builds (D-5). `unused` builds are not printed.
- Tests: `WaitCommandTests.cs` (V-8), `ReportWriterTests.cs` (V-6b).

**S3: docs (`docs/testing-and-build.md`).**

- Schema row `Serial` (`:166`): "A serial row runs alone: no other row and no build is in flight while
  it runs. It does not set TUnit's in-process parallel limit."
- Checkpoint runner tool paragraph (`:209`): after "Rows overlap (two at a time on Linux, one on
  Windows)" add that builds never do: every build in a worktree shares the projects' `obj/`
  (`OutputPath` moves only `bin`), so the run holds one build lane, at most one `dotnet build` at a
  time, started in the order rows first need them, while rows whose build is done run beside it
  (CARD-0810); the Pty and `serial: true` rows run alone, builds included, and `--serial` makes every
  row exclusive, so a `--serial` run is one activity at a time; `report.json` carries
  `maxConcurrentBuilds` beside `maxConcurrentRows` and `wait` prints a needed build as `<id> queued`
  until the lane is free.
- The phrases `CheckpointManifestDocumentationTests` pins (`### Checkpoint runner tool (CARD-0723)`,
  `tools/Antiphon.Checkpoints`, `wait`, `exit 75`, the manifest phrases) all stay. `docs/cards/` is
  generated; do not edit it. No AGENTS.md change: the owner already routes here.

## Verification design

No test ran during Plan; counts are `[Test]` methods read from the source at `a8b4e9e5` (no
`[Arguments]` in these classes) plus the design's additions. `TimeoutTests` has four Windows-only
methods that `Skip.Test` on Linux. Update a count only for an explained source change and report the
actual roster. Every new test is a `[Category("Unit")]` test on `FakeDriver`, `FixedSlotClient` and
in-memory state; none starts a process or touches a broker.

### Coverage inventory

| ID | Evidence | Class and method |
|---|---|---|
| V-1 | Two builds (`bin-a` for CP-1, `bin-b` for CP-2), width 2, unlimited slots, build drivers gated per build: while `bin-a` builds, no other build has started; after release, `bin-b` starts; at the end `driver.MaxInFlight(IsBuild) == 1`, `State.MaxConcurrentBuilds == 1`, both rows green | `RunSchedulerTests.builds_never_overlap_even_with_free_slots_and_width_two` |
| V-2 | Same manifest, `bin-b` gated, run driver instant: CP-1's run request has the `bin-b` build in its `Starts` snapshot (the row ran beside the lane's build); result all green, builds `ok`/`ok` | `RunSchedulerTests.rows_of_a_finished_build_run_while_the_next_build_is_in_flight` |
| V-3 | `SerialAll = true`, width 2, both builds and both runs gated: `bin-a` builds alone; CP-1 runs while `bin-b` has not started (settle delay, build count 1); after CP-1, `bin-b` builds with no row running; then CP-2; `driver.MaxInFlight == 1` overall and the driver call order is build, run, build, run | `RunSchedulerTests.serial_runs_one_activity_at_a_time_builds_included` |
| V-4a | Width 2, CP-1 (`bin-a`, `Serial = true`) gated, CP-2 (`bin-b`): while CP-1 runs the `bin-b` build has not started; after release `bin-b` builds and CP-2 runs; `driver.MaxInFlight == 1` | `RunSchedulerTests.an_exclusive_row_keeps_the_build_lane_idle_until_it_finishes` |
| V-4b | Width 2, CP-1 (`bin-a`), CP-2 (`bin-a`, `Serial = true`), CP-3 (`bin-b`), `bin-b` build gated, runs instant: CP-2's run does not start while `bin-b` is in flight (its `Starts` snapshot holds no build); it starts after the build completes | `RunSchedulerTests.an_exclusive_row_waits_for_the_in_flight_build_of_another_row` |
| V-5 | `manifest.Builds` ordered `bin-b, bin-a`, rows CP-1 (`bin-a`), CP-2 (`bin-b`), instant drivers: the first build request names `bin-a/`, the second `bin-b/` | `RunSchedulerTests.builds_start_in_the_order_rows_first_need_them` |
| V-6a | A third manifest build no selected row references ends `unused`; `State.MaxConcurrentBuilds == 1`; the referenced builds end `ok` | `RunSchedulerTests.unreferenced_builds_end_unused_and_state_records_one_concurrent_build` |
| V-6b | A `ReportModel` with `MaxConcurrentBuilds = 1` renders `max-concurrent-builds: 1` on the summary line beside `builds:` and `report.json` carries `maxConcurrentBuilds` | `ReportWriterTests.summary_line_and_json_carry_max_concurrent_builds` |
| V-7 | Two builds, `bin-a` build blocks until cancelled, total timeout 200 ms: exactly one build request was made; `bin-a` ends `failed`, `bin-b` ends `skipped`; both rows `skipped`; exit 5; the scheduler returns (no hang on a never-started build) | `TimeoutTests.total_deadline_skips_a_queued_build_without_starting_it` |
| V-8 | `RunState` with `bin-a` `building` and `bin-b` `pending` (needed) and `bin-c` `unused`: `Heartbeat` contains `bin-a building` then `bin-b queued` and not `bin-c` | `WaitCommandTests.heartbeat_lists_a_queued_build_after_the_building_one` |
| V-9 | Live: the CP-5 row below is a second `tests/Antiphon.Tests` build in the same run as CP-1's. The run's `report.json` shows `maxConcurrentBuilds: 1`, two builds `ok` with their `seconds`, and `report.md` shows `max-concurrent-builds: 1`. Code quotes all three in its report; the second build's seconds is the D-1 cost measurement | The Code run of this table through the branch-built tool (procedure below) |
| V-10 | The owner doc carries the lane sentence and the `Serial` sentence, and every pinned phrase still resolves | `CheckpointManifestDocumentationTests` (existing 7 methods; the pinned phrases must still pass) |
| R-1 | The 7 existing scheduler methods (each output built once, rows wait for their build, width two, Pty alone, serial alone, failed build fails only its rows and the next build still runs, distinct leases) pass unchanged | `RunSchedulerTests` existing roster |
| R-2 | Row deadline, total deadline with one build, the four Windows-only process tests (skip on Linux), deadline arithmetic | `TimeoutTests` existing roster |
| R-3 | `wait` behaviour, existing heartbeat wording, empty progress | `WaitCommandTests` existing roster |
| R-4 | Ownership admission, settlement during two command drivers, per-build and per-row owner rechecks, slot-wait cancellation, uncertainty and late settlement all still pass through the rewritten loop | `CheckpointTaskOwnershipTests` (24 methods; CARD-0828 says the uncertainty pair passes when the class runs on its own, which is how CP-5 runs it) |
| R-5 | Serial import round-trip drives the scheduler with command rows and still sees `MaxConcurrentRows == 1`; every importer refusal and fixture is unchanged | `CheckpointImportTests` (20 methods) |

### Existing tests affected

None need a changed assertion. `failed_build_fails_only_its_rows` now also proves the lane frees
after a failed build (R-1). If any listed class goes red under the lane, Code reports it as a
defect of the slice and fixes production, never the assertion (rule 4 of the manifest).

### Positive controls (for the Mutation stage after land; method-scoped)

| PC | Mutation in `RunScheduler.cs` (restore after) | Must go red |
|---|---|---|
| PC-1 | Start the next pending build without checking the lane | V-1 (`MaxInFlight(IsBuild)` 2, `MaxConcurrentBuilds` 2) |
| PC-2 | Drop the "no exclusive row running" condition before starting a build | V-4a |
| PC-3 | Let `Pick` admit an exclusive row while a build is in flight | V-4b, V-3 |
| PC-4 | Choose the next build from `manifest.Builds` order | V-5 |
| PC-5 | Skip the D-4 `skipped` marking on the terminal path | V-7 (`bin-b` state) |
| PC-6 | Heartbeat omits `queued` | V-8 |
| PC-7 | `BuildReport` leaves `MaxConcurrentBuilds` at 0 | V-6b (JSON) and V-9 |

### Cost

Ordinary checkpoint floor: **23 minutes Linux** (9+1+1+1+6+4+1) or **35 minutes Windows**
(13+3+2+1+9+6+1), excluding broker wait. Tool bootstrap builds: about 1 minute Linux, 2 Windows.
Code authoring allowance including the seven scheduler tests and the `FakeDriver` snapshot: about
150 minutes. `-ExpectAbout` for the Code dispatch: 180 minutes Linux. Mutation after land: 7 controls
at about 4 minutes each on Linux (28 minutes) plus one fresh isolated build (8 minutes).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | EstimatedMinutesWindows | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---:|---|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c810a/` | scheduler-lane | `/*/*/RunSchedulerTests/*` | V-1, V-2, V-3, V-4a, V-4b, V-5, V-6a, R-1 | all 14 methods, 0 failed/skipped | 14 | 9 | 13 | false | n/a |
| CP-2 | all | CP-1 | timeouts | `/*/*/TimeoutTests/*` | V-7, R-2 | all 8 listed; Linux 4 executed and 4 skipped (Windows-only), Windows 8 executed; 0 failed | 4 | 1 | 3 | false | n/a |
| CP-3 | all | CP-1 | wait-heartbeat | `/*/*/WaitCommandTests/*` | V-8, R-3 | all 9 methods, 0 failed/skipped | 9 | 1 | 2 | false | n/a |
| CP-4 | all | CP-1 | report-summary | `/*/*/ReportWriterTests/*` | V-6b | all 6 methods, 0 failed/skipped | 6 | 1 | 1 | false | n/a |
| CP-5 | all | `tests/Antiphon.Tests -> bin-c810b/` | ownership-live-lane | `/*/*/CheckpointTaskOwnershipTests/*` | R-4, V-9 | all 24 methods, 0 failed/skipped; the run's report.json shows maxConcurrentBuilds 1 and two builds ok | 24 | 6 | 9 | true | n/a |
| CP-6 | all | CP-5 | importer-serial | `/*/*/CheckpointImportTests/*` | R-5 | all 20 methods, 0 failed/skipped | 20 | 4 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | all | CP-5 | doc-contract | `/*/*/CheckpointManifestDocumentationTests/*` | V-10 | all 7 methods, 0 failed/skipped | 7 | 1 | 1 | false | n/a |

CP-5 deliberately builds `tests/Antiphon.Tests` a second time into its own output: under the
baseline tool the two builds would start together (the card's failure); under S1 the lane builds
`bin-c810a/` first, runs CP-1..CP-4 beside the `bin-c810b/` build, then runs the serial CP-5 alone.
That is the only live proof in this table and it costs one incremental build.

### Runnable checkpoint procedure

There is exactly one `### Checkpoints` table in this artifact; do not append a second. Every row's
`After` is `all`, so the table runs once, after S3 is committed, with no row selector. The tool under
test is also the runner, so bootstrap it from the branch under a build-slot lease first; the bootstrap
is a supporting build outside the table, reported with that reason:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card-0810-tool-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c810-tool/ --nologo
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c810-tool/ -- run --plan docs/superpowers/plans/2026-09-30-card-0810-checkpoint-build-lane-plan.md --max-wait 570s
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c810-tool/ -- wait --run <returned-run-id> --max-wait 570s
```

Repeat `wait`, not `run`, while the exit is 75. Commit before the run and do not edit source under
it. A slot timeout (exit 4) is a row not run, never a reason to run unleased. Quote every `CHECKPOINT`
line, the `max-concurrent-builds` summary line and both builds' `seconds` from `report.json` (V-9).
Delete `bin-c810-tool/` directories before finishing; the green run deletes `bin-c810a/` and
`bin-c810b/` itself. If a row is red, the same CP-n is rerun after the fix and the rerun is counted.

## Risks

- **Wall time.** A table whose builds used to overlap now builds them back to back; the second and
  later builds of an already-compiled graph are mostly copies, so the added time is bounded by one
  incremental build per extra output. CP-5's `seconds` puts a number on it.
- **`--serial` runs get longer.** By design (D-3); the flag now means what its name says.
- **Loop rewrite regressions.** The ownership and timeout classes exercise every terminal path
  (owner end, admission block, total timeout, slot-wait cancel); CP-2 and CP-5 run them in full.
- **CARD-0828 flakiness** could redden CP-5 for reasons outside this card; the plan runs that class
  alone and serial, which is the condition under which the pair passes today. A red there is reported
  with the failing names, not retried into green.
- **Windows.** No process is started by the new tests. The lane rule is platform-neutral; the
  Windows estimates are for the desktop's slower build.

## Post-land check

The first Code stage on any card whose table names two or more builds on server2 or the desktop:
its run's `report.json` shows `maxConcurrentBuilds: 1` and its report needs no "three separate runs"
workaround. CARD-0823's crash, if still open, now leaks at most one lease per crash.
