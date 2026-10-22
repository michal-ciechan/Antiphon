# CARD-1461 option C: bounded memory-floor wait with an unleased capped fallback build

Plan task 1490970e, 2026-10-22. Source read at `3fa8d7bd480d7df345aea2bb46c042be19f78a76`
(master tip at dispatch, also this branch's base). Investigation:
`docs/investigations/2026-10-22-card-1461-land-verify-memory-floor-starvation.md` (on master at
`be1e8426e`), Q4 option C and Q5. No source edits here.

## Summary

The desktop land verifier takes a host build slot through `SessionRunnerBuildSlotGate`. While
the Windows RC qualification holds memory without a lease, the broker answers
`build_slot_memory_floor` for every poll, and the gate polls until `Landing:BuildSlotWaitMinutes`
(30) and then fails the land (`build_slot_timeout`). Option C adds a default-off bounded wait for
that one refusal kind: once the floor has refused for `Landing:BuildSlotMemoryFloorFallbackMinutes`
the gate returns an `Unleased` hold at `Landing:BuildSlotMemoryFloorFallbackMaxCpuCount` nodes,
and the verifier builds the way it already builds for an unreachable runner, with a distinct
`BUILD SLOT unleased reason=memory_floor_fallback ...` line. Default off is byte-identical to
today's behaviour. Server-side only: no broker, floor, runner or contract change.

## Decisions

- **D-1 Scope is the gate and its settings only.** Files:
  `server/Application/Settings/LandingSettings.cs`,
  `server/Infrastructure/Agents/SessionRunner/SessionRunnerBuildSlotGate.cs`,
  `tests/Antiphon.Tests/Application/LandVerificationBuildSlotTests.cs`,
  `docs/testing-and-build.md` (the desktop land verifier paragraph, line 891),
  `docs/orchestration-loop.md` (one pointer sentence beside the other `Landing:` settings, ~line 2100).
  Rejected: touching `AgentTaskLandService.VerifyWithObserverAsync` or `VerifyUnderSlotAsync`.
  They already apply any `MaxCpuCount > 0` to the build and skip release for a non-`Granted` hold
  (`AgentTaskLandService.cs:1824-1835,1845-1847`; `ReleaseAsync` returns early), so nothing there
  changes, and that file differs between master and `rc/2026-10-20` (34/13 lines) while the four
  files above are byte-identical on both (D-8).
- **D-2 Settings.** `BuildSlotMemoryFloorFallbackMinutes` (int, default 0 = off) and
  `BuildSlotMemoryFloorFallbackMaxCpuCount` (int, default 2) on `LandingSettings`, bound from the
  existing `Landing` section (`server/Program.cs:482`). The gate clamps the minutes with
  `Math.Max(0, ...)` like its siblings and the cpu count with `Math.Max(1, ...)`: the verifier only
  adds `-maxcpucount` when the hold's count is positive, so a configured 0 would build with
  unbounded MSBuild nodes on a host that is already short of memory. Rejected: a per-request
  purpose flag with a lower floor on the broker (option A): a contract change on both sides plus a
  runner restart, and it still admits a build onto a paging host.
- **D-3 What starts the fallback window.** `floorSince` is the elapsed time at the last answer
  that was *not* a memory-floor refusal (a busy answer or a missed answer), or zero at the start
  of the acquire. The fallback fires in the floor branch when the fallback is enabled and
  `elapsed - floorSince >= fallback`, after the existing timeout check, which stays first.
  Reasons: it matches the existing deadline rule (`elapsed >= wait` on whichever answer arrives,
  `SessionRunnerBuildSlotGate.cs:62-66`), it is reset by anything that is not the floor so a
  busy broker never converts into an unleased build, and it is testable with the file's existing
  `FakeTimeProvider` pattern. Consequence: a fallback window at or above `BuildSlotWaitMinutes`
  never fires; the paragraph in `docs/testing-and-build.md` says so.
  Rejected: counting from the first observed floor answer (a slow first answer would add its
  latency to the wait, and it is not what the deadline does).
- **D-4 The fallback hold.** `new BuildSlotHold(BuildSlotHoldOutcome.Unleased, null, cpu, elapsed,
  floor.QueuePosition)`, reported as
  `BUILD SLOT unleased reason=memory_floor_fallback available=<a>MB floor=<f>MB waited=<s>s maxcpucount=<n>`.
  The reason token is new so Review, grep and the landing evidence tail can tell it from
  `runner_unreachable`. No new `BuildSlotHoldOutcome`: `Unleased` already means "built without a
  lease, nothing to release", and the verifier's release path is unchanged.
- **D-5 Visibility.** The gate logs that one line at `Warning` (today every `BUILD SLOT` line is
  `Information`, `SessionRunnerBuildSlotGate.cs:131`); the observer copy still reaches the server
  log with the land correlation through `LandingVerifier.Line` and the verifier evidence tail. No
  event, attention item or landing-row column: the investigation already proposes a follow-up card
  (option F: carry the last `BUILD SLOT` line in the LandAged detail) and that is where board
  visibility belongs; this plan does not widen into the request writer. The orchestrator should
  file that card; its text is in "Operator decisions".
- **D-6 Interaction with the unreachable path.** Unchanged and independent: a missed answer
  starts the unreachable grace as today and resets `floorSince`; a floor answer after an
  unreachable stretch clears `unreachableSince` as today and starts a fresh floor stretch.
  Neither clock borrows from the other.
- **D-7 Default off, no value chosen here.** The plan ships `0`/`2`. Every live value (whether to
  enable, the window, the count, the 30-minute wait, the 6144 MB floor) is the operator's.
- **D-8 RC line.** The change only alters live land behaviour when it is on the rc line the
  desktop AppHost runs (operator premise 2026-10-22, `docs/release-gates.md:34,368`). Default in
  this plan: Code and land on master as usual; the operator decides whether it also goes onto
  `rc/2026-10-20` now through a Merge-role task that cherry-picks the code commits (predicted clean,
  see "RC question"), or arrives with the next rc cut from master. Rejected as the default: starting
  the Code task at the rc tip (`delegate.ps1 -StartRef`), because the pipeline, the plan document
  and Review all live on master and the rc→master merge would then carry a change master already
  needs for the next cut; it stays available if the operator wants rc first.
- **D-9 Lane.** server2 (the runner default, `GET /api/runner-defaults` → `server2`), no `-Runner`,
  no `-Platform`: every new test is a fake-clock, fake-client Unit test with no OS dependency, like
  the six it joins.

## Reconciliation with the 2026-10-22 premise and CARD-1462

- **A master land is not an activation.** AppHost and server2 run the current rc tip
  (`rc/2026-10-20`, tip `ceb6e3d6b78471db6831258699c493b8eb2d619b` at dispatch). Landing option C on
  master changes nothing on the desktop until (a) it is on the rc line the AppHost runs, (b) the
  AppHost is restarted on that tip, and (c) the operator sets a positive
  `Landing:BuildSlotMemoryFloorFallbackMinutes` in the desktop server's configuration overlay (the
  gitignored `server/appsettings.Development.json` or user-secrets id `antiphon-server`,
  `docs/bootstrap.md:204-207`; never the tracked `server/appsettings.json`). The report must say
  "landed", not "fixed", until all three are true.
- **If lands move to server2 (CARD-1462, Investigate in flight).** The gate is host-agnostic: it
  talks to whichever broker the server's `ISessionRunnerClient` reaches, so option C stays correct
  there. Its value drops: the Windows qualification does not run on server2, and server2's refusals
  come from leased delegate builds and test hosts, which surface as `busy`, not `memory_floor`, and
  the fallback never applies to `busy`. It would still bound a `memory_floor` stretch caused by
  unleased consumers (sessions, Postgres) against server2's 16384 MB floor.
- **Worth building?** Yes, as bounded insurance: three slices of 30-60 minutes, default off,
  server-only, no contract or runner change, and it removes a failure mode (`build_slot_timeout`
  after 30 minutes) that no land-location change removes by itself. Not worth building if the
  operator both expects lands to leave the desktop within days and will not enable the setting on
  the current rc line meanwhile; in that case option G (raise `Landing:BuildSlotWaitMinutes`,
  configuration only) is the interim and this card waits for CARD-1462.
- **Useful meanwhile?** Only on the rc line plus an enabled setting (above). Each RC fix on the
  line re-runs the qualification from scratch (`docs/release-gates.md:281-284`), which is exactly
  the window in which lands starve, so a line that keeps taking fixes keeps producing the problem
  until the fallback is live on it.

## Ground truth

| The card or brief assumes | What the code does | Source |
|---|---|---|
| A land verify waits up to 30 minutes behind the floor and then fails | Yes: a `MemoryFloor` answer counts toward `BuildSlotWaitMinutes`; timeout is `LandVerification.Failure("build-slot", "build_slot_timeout ...")` | `SessionRunnerBuildSlotGate.cs:62-66,79-89`; `AgentTaskLandService.cs:1824-1825` |
| Only an unreachable runner falls back to an unleased build | Yes: the `else` branch after `grace` returns `Unleased` at `UnleasedMaxCpuCount` (4); a reachable refusal never does | `SessionRunnerBuildSlotGate.cs:90-99` |
| The qualification never takes a slot | Yes: zero matches for build-slot names in `scripts/lib/nightly-tests-impl.ps1` | grep this task |
| Server-side only, no runner change | Yes: the answer already carries `AvailableMb`, `FloorMb`, `QueuePosition`, `RetryAfterMs` | `BuildSlotContracts.cs:36`; `RunnerBuildSlotAnswer.cs:13` |
| The verifier needs no change for a fallback hold | Yes: `-maxcpucount:<n>` for any positive `MaxCpuCount`; release only for `Granted` | `AgentTaskLandService.cs:1845-1847`; gate `ReleaseAsync` |
| A fallback cpu count of 0 would be safe | No: `slot is { MaxCpuCount: > 0 }` otherwise builds with no cap; hence the `Math.Max(1, ...)` clamp (D-2) | `AgentTaskLandService.cs:1845` |
| The doc paragraph is at line ~891 | Line 891 on master, 889 on `rc/2026-10-20`, identical text and ±5 lines of context | `git show` both refs |
| "The settings docs" exist for `Landing:` | No dedicated page: `Landing:ReuseVerificationArtifacts` and `Landing:ForceVerificationBuild` are described in `docs/orchestration-loop.md` ~2100-2120 (identical on rc), the slot settings only in the line-891 paragraph; the XML summaries on `LandingSettings` are the other place | grep docs |
| Investigation CP-3/CP-4 (`InstructionBundleTests`, `DockerStackContractTests`) guard this change | No test reads the line-891 paragraph or the `Landing:` slot settings (grep over `tests/`); those rows guard nothing here and are dropped | grep this task |
| The class has 6 tests | Yes, six `[Test]` methods; the investigation's "9/9" assumed three new; this plan adds four (three red witnesses plus one default guard) → 10 | test file |
| `FakeTimeProvider` tests can drive the poll loop from a client callback alone | Only for a one-answer case: the gate's `Task.Delay(delay, time, ct)` needs the test body to `Advance` (the unreachable test's pattern, lines 60-94); witnesses that poll more than once advance from the body | test file |

## Failure modes

| Mode | What happens with option C | Guard |
|---|---|---|
| Fallback build OOM | The host is below the floor by definition; two MSBuild nodes plus VBCSCompiler plus the test host can still push it into paging. A killed or failing child is a `build`/`tests` step failure: the land is not published and retries as today. The cpu count caps compiler nodes only, not the test host. | The fallback line records `available=`/`floor=` at the moment of the decision for correlation; the count is operator-owned; a fallback that fails is still faster than a 30-minute refused land. Rejected: an automatic second fallback or a lower count on failure. |
| A refusal that is not the floor | `busy` keeps today's deadline and resets the floor window (D-3). A definitive 4xx the client maps to `null` is the unreachable path, unchanged. | Witness V-3 (busy resets), guard V-4 (default off still times out). |
| Broker unreachable | Unchanged: grace then `Unleased` at 4 with `reason=runner_unreachable`. Floor→unreachable: the grace governs; unreachable→floor: a fresh floor window. | Existing tests `An_unreachable_answer_...`, `An_unreachable_runner_still_builds_...` stay in CP-2/CP-3. |
| Fallback masks a real build-slot outage | A broken memory probe (available reads low forever) would make every land fall back after the window, hiding the broker defect behind successful lands. | Every fallback logs at `Warning` with the distinct reason token (D-5); the operator's grep is `reason=memory_floor_fallback` in the server log; the fallback never applies to `busy`, so a lease or capacity outage still times out visibly. Rejected: a circuit breaker that disables the fallback after N consecutive uses; it adds state for a condition the Warning already names. |
| Fallback window ≥ wait | Never fires; the timeout check precedes it. | Documented in the line-891 paragraph; no code. |
| Nobody sees the wait on the board | Unchanged: `LandNoProgress` Warning/Error at 300 s/900 s with no reason. | Out of scope; follow-up card (option F) below. |
| Operator sets the setting but it is not active | Setting lives in the server's configuration overlay and needs an AppHost restart; a master-only land never reaches the desktop. | The report wording in "Reconciliation"; proof of activation is a `reason=memory_floor_fallback` line during a floor stretch, or `GET /api/version` SHA on the rc tip plus the overlay value. |

## Slices

Each slice is independent of the next's content; S2 depends on S1's settings properties to
compile, S3 depends on nothing. Commits that touch this plan document carry nothing else, so an rc
cherry-pick can skip them (D-8).

### S1 (30-40 min): settings properties and the four tests, committed red

- `LandingSettings.cs`: add `BuildSlotMemoryFloorFallbackMinutes { get; set; }` (default 0) and
  `BuildSlotMemoryFloorFallbackMaxCpuCount { get; set; } = 2`, each with an XML summary naming the
  behaviour and the default. Nothing reads them yet, so behaviour is unchanged.
- `LandVerificationBuildSlotTests.cs`: extend `Fixture` with `public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow)`
  and `public LandingSettings Settings { get; init; } = new() { BuildSlotWaitMinutes = 30, BuildSlotUnreachableGraceSeconds = 0 }`,
  and make `VerifyAsync` build its gate from those two (the six existing tests are unchanged in
  behaviour: same defaults). Add V-1..V-4 below.
- Run CP-1 at this commit: 3 failed, 1 passed is the red proof (the checkpoint tool's `validate`
  refuses the run with `row_failed`; keep that evidence as the red record, do not retry it).

### S2 (30-45 min): the gate

- `SessionRunnerBuildSlotGate.cs`: read the two settings (clamps per D-2); add `floorSince`
  per D-3, reset in the `Busy` and unreachable branches; in the `MemoryFloor` branch, before the
  waiting-line report, `if (fallback > TimeSpan.Zero && elapsed - floorSince >= fallback)` report
  the D-4 line through a new `Warn` helper (`logger?.LogWarning("{BuildSlotLine}", line)` plus the
  same guarded `report(line)`) and return the D-4 hold. Update the class summary.
- Run CP-1 again (now 4/4) and CP-2 (10/10).

### S3 (30 min): documentation

- `docs/testing-and-build.md:891`: after "a timeout is a `build-slot` verification failure with
  nothing built", add the two settings, the default-off statement, the D-3 rule (window measured
  from the last non-floor answer; a window at or above the wait never fires; busy never falls
  back), the exact line shape, the `Warning` log level, and that the fallback build is unbudgeted
  and invisible to other waiters.
- `docs/orchestration-loop.md` ~2100: one sentence pointing at that paragraph for the two new
  `Landing:` settings, beside `Landing:ReuseVerificationArtifacts`.
- Run CP-3 (10/10 at the final tip).

## Verification design

Witnesses live in `tests/Antiphon.Tests/Application/LandVerificationBuildSlotTests.cs`, same
fixture, `FakeSessionRunnerClient.BuildSlotAcquire` and `FakeTimeProvider`. Floor answer used
throughout: `new RunnerBuildSlotAnswer(null, MemoryFloor: new BuildSlotMemoryFloor(4700, 6144, 1, 60000))`
(`RetryAfterMs` 60000 so each one-minute `Advance` from the test body fires exactly one poll).

| Id | Test (method name) | Setup | Expect | Red at S1 because |
|---|---|---|---|---|
| V-1 | `A_memory_floor_refusal_past_the_fallback_window_builds_unleased_at_the_fallback_cpu_count` (gate level) | `LandingSettings { BuildSlotWaitMinutes = 30, BuildSlotUnreachableGraceSeconds = 60, BuildSlotMemoryFloorFallbackMinutes = 5, BuildSlotMemoryFloorFallbackMaxCpuCount = 2 }`; client always answers floor; body: start `AcquireAsync`, then four times `Advance(1 min)` + `await Task.Delay(1)` asserting not completed, then a fifth `Advance(1 min)` and `await pending.WaitAsync(5 s)` | `Outcome == Unleased`, `MaxCpuCount == 2`, `Waited >= 5 min`, `QueuePosition == 1`; lines contain one starting `BUILD SLOT waiting label=floor reason=memory_floor available=4700MB floor=6144MB` and one equal to `BUILD SLOT unleased reason=memory_floor_fallback available=4700MB floor=6144MB waited=300s maxcpucount=2`; no line starting `BUILD SLOT timeout` | the gate keeps polling until 30 minutes; `WaitAsync(5 s)` throws `TimeoutException` |
| V-2 | `A_memory_floor_fallback_builds_at_the_fallback_cpu_count_and_releases_nothing` (verifier level, `Fixture`) | `Fixture { Settings = new() { BuildSlotWaitMinutes = 30, BuildSlotUnreachableGraceSeconds = 0, BuildSlotMemoryFloorFallbackMinutes = 5, BuildSlotMemoryFloorFallbackMaxCpuCount = 2 } }`; client answers floor and appends `acquire <label>` to the timeline; body advances `f.Time` five minutes in one-minute steps, then awaits `VerifyAsync` with `WaitAsync(5 s)` | `result.Ok`, `Description == "build OK, tests 4/4"`; `Timeline.Select(Head)` ends `["dotnet build", "dotnet run"]` with no `release`; build args contain `-maxcpucount:2` and not `-maxcpucount:4`; `ReleasedBuildSlots` empty; observer line starts `BUILD SLOT unleased reason=memory_floor_fallback` | same as V-1 through the verifier |
| V-3 | `A_busy_answer_restarts_the_memory_floor_fallback_window` (gate level) | fallback 3 min, wait 30; client answers in order floor, floor, floor, busy (`new BuildSlotBusy(2, 2, 1, 60000)`), then floor forever; body advances one minute at a time | not completed after advances 1..5 (elapsed 1-5 min: the busy answer at minute 3 resets the window); completed after advance 6 with `Outcome == Unleased`, `Waited >= 6 min`; exactly one `reason=memory_floor_fallback` line | at S1 nothing completes by minute 6; a naive S2 that never resets the window completes at minute 4 and fails the "not completed after 4" assertion |
| V-4 (guard, green at S1) | `A_reachable_memory_floor_runner_times_out_at_the_wait_deadline_when_the_fallback_is_off` | `new LandingSettings()` (asserts its `BuildSlotMemoryFloorFallbackMinutes == 0` and `...MaxCpuCount == 2` first); client advances 30 minutes then answers floor | `Outcome == Timeout`; a line starting `BUILD SLOT timeout after 30m position=1`; no line starting `BUILD SLOT unleased` | not red: it pins that the default is today's behaviour byte-for-byte, mirroring `A_reachable_busy_runner_times_out_at_the_wait_deadline` |

Mutation hints for a later stage, each one test: drop the `floorSince` reset in the busy branch
(V-3 fails at minute 4); use `UnleasedMaxCpuCount` instead of the setting in the hold (V-1, V-2);
replace `>=` with `>` on the window (V-1 completes one minute late); log the line at `Information`
(no test: covered by Review reading the `Warn` helper).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1461a/` | witnesses | `/*/Antiphon.Tests.Application/LandVerificationBuildSlotTests/(A_memory_floor_*)\|(A_busy_answer_restarts_*)` | V-1, V-2, V-3, V-4 | 4 listed; at the S1 commit 3 failed and 1 passed (red record), after S2 0 failed | 4 | 3 | |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1461b/` | gate-class | `/*/Antiphon.Tests.Application/LandVerificationBuildSlotTests/*` | V-1..V-4 plus the six existing | all listed, 0 failed | 10 | 3 | |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1461c/` | final-tip | `/*/Antiphon.Tests.Application/LandVerificationBuildSlotTests/*` | V-1..V-4 plus the six existing | all listed, 0 failed | 10 | 3 | |

Lane: server2 (Linux, the runner default); Unit tests, no Postgres, no process spawn. One build
per `After` group because the checkpoint tool refuses a reused build across groups. CP-1's red run
at the S1 commit is a run the tool's `validate` refuses (`row_failed`); that refusal is the red
evidence and is committed to the report as such, then CP-1 is run again after S2. The method-segment
OR form is the one `docs/testing-and-build.md` (Combined class filters) records as working on the
pinned TUnit; check the fresh TRX names all four methods, never infer from exit zero. Builds use
`UseAppHost=false` off Windows as the tool already does. Delete every `bin-c1461*` before finishing.

### Cost

Ordinary Code floor: three builds plus three three-minute rows, about 9 minutes of runs plus
authoring (S1 30-40, S2 30-45, S3 30). Review scope: the three files plus the two doc paragraphs;
`scripts/check-evidence-diff.ps1 -Repository . -BaseRef <base> -HeadRef HEAD` over the branch.

### Runs

(recorded by the Code stage)

## RC question

- **Does it belong on `rc/2026-10-20`?** It changes live land behaviour only there (the desktop
  AppHost runs the rc tip). `docs/release-gates.md:281` puts a fix for code already on the line on
  the line; a default-off setting on existing code reads as a fix, but every rc land re-runs the
  qualification from scratch, which is a cost the operator weighs (D-8 default: master now, rc on
  the operator's word).
- **Does it cherry-pick cleanly?** Predicted yes. `git diff --stat origin/rc/2026-10-20 origin/master`
  is empty for `SessionRunnerBuildSlotGate.cs`, `LandingSettings.cs`,
  `LandVerificationBuildSlotTests.cs` and `FakeSessionRunnerClient.cs`; the line-891 paragraph and
  its surrounding lines (rc 884-896, master 886-898) are identical; the `orchestration-loop.md`
  hunks between the two refs are at lines 150, 1315 and 1668, not near 2100. Only this plan
  document would conflict (rc carries no `docs/superpowers/plans`), which is why plan-doc commits
  carry nothing else. Proof for the Merge-role task, per code commit in order, chained on the
  previous result tree: `git merge-tree --write-tree --merge-base <c>^ <rc-tip-or-previous-tree> <c>`
  with no conflict lines; then cherry-pick onto a branch started at the rc tip
  (`delegate.ps1 -StartRef <rc tip>`) and fast-forward `rc/2026-10-20` per
  `docs/release-gates.md:42-56`. Activation afterwards is a restart of the AppHost on the new tip
  plus the configuration overlay value.

## Operator decisions

Every value below is the operator's; the plan ships default off and chooses none of them.

1. Build option C at all now, or wait for CARD-1462's answer on moving lands (see Reconciliation).
2. Enable `Landing:BuildSlotMemoryFloorFallbackMinutes` on the desktop and at what value. Facts:
   the longest measured stretch was 24.0 minutes, five of seven outliers were 6-24 minutes; the
   window plus a 2-4 minute build and test is the new worst case for a land that falls back.
3. `Landing:BuildSlotMemoryFloorFallbackMaxCpuCount` (plan default 2; the unreachable path uses 4;
   the grant on the desktop is 4).
4. Raise `Landing:BuildSlotWaitMinutes` above 30 regardless (option G; configuration only, no code).
5. RC path: cherry-pick onto `rc/2026-10-20` now via a Merge-role task, or let the next rc cut carry
   it (D-8). Either way the setting needs the overlay plus an AppHost restart to act.
6. Leave the desktop `SessionRunner:BuildSlots:MinAvailableMemoryMb` (6144) alone, as the brief
   says; a change there affects every delegate build and needs a runner restart.
7. File the follow-up card for option F: "LandAged detail carries the last `BUILD SLOT` line so the
   board says waiting for memory (or unleased fallback) instead of no progress"; owner area the
   attention service and the landing request writer.

## Session-wait checklist

Not applicable: the plan adds or changes no session that waits for input.
