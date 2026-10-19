# CARD-1359 C540 dispatch-wait shape repair (master-only)

Plan owner: `f26c770a`. Branch: `feat/card-task-f26c770a`.
Base: `87cb3c143e2aecd206aee2c323200a64a7cd0d85`, the Investigate commit itself, so
`docs/investigations/2026-10-19-card-1359-c540-child-launch-stall.md` (task `7e43a724`) is already
on this branch and lands with this plan. Round: Final. Restart: none. Publication is only to the
assigned task ref; landing rebases.

Scope: `tests/Antiphon.E2E` fixtures and one new witness test. No product file changes. The
product hazard the report identifies (deadline-less sync-over-async runner reads in
`AgentSessionRuntime`, no runner request timing) is a separate card and is not planned here.

## Platform

Read on 2026-10-19 from the configured API (`GET /api/runner-defaults`,
`GET /api/session-runners`):

| Fact | Value |
|---|---|
| Global default runner | `server2` (operator reason: desktop only for truly Windows work) |
| `desktop` | platform `windows`, capacity 2 delegated tasks, 0 occupied, accepting |
| `server2` | platform `linux`, draining, not accepting new work |
| `server2-temp` | platform `linux`, capacity 10 sessions, 6 occupied, accepting |

Both checkpoint rows run `tests/Antiphon.E2E` through `LandDeliveryFixture`, which asserts
`OperatingSystem.IsWindows()`, a located ConPTY redistributable and a staged `fakegrok.exe`
before anything else (`tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs:59-61`). The Code
dispatch therefore needs `-Platform Windows`; omit `-Runner` (the platform pin resolves to the
desktop without naming a host). Authoring the edits, Review and land are platform-free; the card
stays `platform Any`.

## Landing order

`origin/master` is `6ec71d427` today. This branch carries eight commits that are not on it: the
CARD-1305 plan branch (`feat/card-task-18b06751`: `216e3ac87`, `19499afdf`, `570d76d51`), the
CARD-1305 Code branch (`feat/card-task-1f4aaf07`, tip `066f4a989`: S0-S3) and the Investigate
commit `87cb3c143`. `git merge-tree --write-tree origin/master HEAD` has no conflicts today.
Land this branch after `1f4aaf07` (the landing rebase drops its patch-identical commits) or with
it (landing this branch publishes those commits too); never before it, because S1 below edits
the `CrashAsync` wait that CARD-1305 S3 made reachable.

## Ground truth

| The brief or report assumes | What the code does | Verdict |
|---|---|---|
| The `before-enqueue` wait is a 60 s quiet window renewed only by `protocol-git-*.json`. | `WaitForBoundaryAsync` (`LandDeliveryFixture.Dispatch.cs:140-141`) calls `UntilProtocolAsync` (`LandDeliveryFixture.cs:363-377`), whose only probe is `ProtocolProgress()` (`:361`, a file count) handed to `ProgressAwareWait.UntilAsync` (`tests/Shared/ProgressAwareWait.cs:17-35`). | Confirmed. |
| Materialization is sequenced after the launch, so the awaited boundary sits behind the whole launch path. | True of the dispatcher's own call (`AgentTaskDispatcher.cs:6157-6166`, after the `Dispatched` log at `:6151`). But `AgentTaskLandNotificationHostedService.cs:27-38` also materializes every intent with `MaterializedAt == null && NextAttemptAt <= now` on a 5 s loop (`:121`), and `DispatchBaseWarningIntentService.CaptureAsync` sets `NextAttemptAt = CreatedAt` (`:69`). After the claim commit (`:6058-6059`) the intents are eligible to the scan immediately. | Partly. The failed run's claim-commit time is not in the evidence; either the stall preceded the commit, or the scan was stalled with the dispatcher. Both fit the report's thread-pool hypothesis and neither changes the fix. It does constrain the witness (D-3): a seam that holds only the dispatcher after the claim cannot keep the scan from materializing within 5 s. |
| Dispatch-side progress markers exist that the quiet window could renew on. | Claim commit writes the task row, the Starting session row, four `AgentTaskEvents` and two intents (`:5783-5790`, `:6023-6029`, `:6040`, `:6058`). Each materialization adds one Warning event, one `AgentTaskLandNotification` and `MaterializedAt` (`DispatchBaseWarningIntentService.cs:123-125`). The runner launch stamps `InteractiveLaunchCompletedAt` on the session (`AgentSessionService.cs:677`). Each enqueue inserts a `SessionQueuedMessages` row with `SourceTaskId = note.TaskId` (`AgentTaskLandNotificationService.cs:139-147`). | Confirmed. In the failed run `MaterializedAt` was 18:38:34.51/.62, 59.8 s after the last protocol-git observation (18:37:34.7); the claim-commit rows (some time in 18:37:41 to 18:38:34) are the markers that would have carried the window. A dispatcher that writes nothing for 60 s still fails, by design (brief: do not widen). |
| The probe is a synchronous `Func<long>`; the markers are database counts. | `ProgressAwareWait.UntilAsync` evaluates `predicate` (async) before `progress` (sync) on every poll (`:27-30`). | The predicate wrapper refreshes the dispatch counts; the probe reads the cached value (D-2). |
| `WaitForBoundaryAsync` is a C540 helper. | The land tests use it at `AgentTaskLandDeliveryE2ETests.cs:102`, `:122`, `:173`. | Wrong. It stays unchanged; `CrashAsync` moves to a dispatch-aware sibling (D-4). |
| The `queue` cut's wait has the same shape. | `CrashAsync` (`DispatchBaseWarningDeliveryE2ETests.cs:120-127`) calls `UntilProtocolAsync` directly for `queue-inserted`. | Confirmed; switched by the same slice. |
| A fixture seam can stretch the path between the claim commit and the barrier while real markers keep moving. | `FileBoundary.ReachedAsync` (`LandDeliveryOptions.cs:133-180`) sees `dispatch-warning-before-materialize` from both materializers (`DispatchBaseWarningIntentService.cs:80-81`), resolves the owner from the intent (`:142-147`) and returns before the barrier logic for an unblocked boundary. `dispatch-warning-claim-committed` and `dispatch-warning-after-dispatch` are reached by the dispatcher only (`:6081`, `:6157`). | Confirmed for `before-materialize`; the seam goes there (D-3). |
| The witness can prove more than 60 s of protocol-git silence before the barrier. | Every protocol-git record carries `at` (`LandDeliveryOptions.cs:215-218`); the barrier is moved into place by `File.Move` (`:176`), both on the child host's clock. | Confirmed: `File.GetLastWriteTimeUtc(barrier) - max(at)` is the measured silence (V-1). |
| The pre-enqueue assertions survive the scan materializing instead of the dispatcher. | `CrashAsync` asserts two notifications and zero keyed queue rows (`:141-145`). The scan's notification pass (`AgentTaskLandNotificationHostedService.cs:92-117`) runs after its intent pass finishes, and the dispatcher's second materialization (`T0+81`, below) completes no later than the scan's own second attempt, so both notes exist before the first `before-enqueue` of the owned task. | Holds; the witness reuses the assertions unchanged. |
| The checkpoint tool accepts E2E rows, method-level OR filters, `Serial` and `Environment`. | `docs/testing-and-build.md` (Checkpoint manifest schema: optional `Serial`, `Environment`); the CARD-1305 plan on this branch validated the same row shape with `import --plan`. | Used below; this table was validated the same way (see Cost). |
| Code budget 45 to 60 minutes. | Two E2E builds (about 4 minutes each) plus three single-method runs (about 2, 3 and 3.5 minutes) plus authoring. | Fits: Cost asks `-ExpectAbout 45`. |

## Decisions

- **D-1 Fixtures only.** No product slice. The report's product fix ideas (deadlines on
  `AgentSessionRuntime`'s blocking runner reads, runner request timing) belong to the separate
  card the brief names; this plan does not file or touch it.
- **D-2 The dispatch waits renew on a composite probe: protocol-git count plus dispatch-side
  markers; the 60 s quiet window and the 600 s cap are unchanged.** The markers are steps of
  the dispatch chain, never periodic touches: `Events` (count of `AgentTaskEvents` for `TaskId`),
  `Intents` (count), `Materialized` (count with `MaterializedAt != null`), `Notifications` (count
  of `AgentTaskLandNotifications` for `TaskId`), `Queued` (count of `SessionQueuedMessages` with
  `SourceTaskId == TaskId`), `Launched` (1 when the task's `AgentSessionId` session has
  `InteractiveLaunchCompletedAt != null`). All are non-decreasing, so their sum plus the
  protocol-git count changes exactly when one of them does. Rejected: (a) Postgres `xmin` row
  versions of the task and session rows (any hosted sweep that touches those rows would renew
  the window on noise, which widens it in effect); (b) child log growth (the degraded runner
  reads log every few seconds and would renew forever); (c) widening the window or the cap
  (brief); (d) `ExecutionTaskId` brief rows (a Grok task with a rules payload queues no brief,
  `AgentTaskDispatcher.cs:6126`, so the marker would be absent exactly in this fixture).
- **D-3 The witness is the pre-enqueue crash flow under a slow-materialize seam.** A new
  `slow-launch.json` in the fixture root maps a boundary name to a delay in seconds; the owned
  task's `FileBoundary.ReachedAsync` records `slow-launch-<boundary>-<guid>.observation.json`
  and sleeps that long before continuing. The witness sets
  `{"dispatch-warning-before-materialize": 40}`. With `T0` the claim commit and `x` the scan's
  next tick (at most 5 s): the dispatcher launches at about `T0+1` and reaches the seam for
  intent 1, the scan's iteration reaches it at `T0+x`; intent 1 materializes at about `T0+41`,
  intent 2 at about `T0+81`; the scan's notification pass follows its own intent pass, so the
  first owned `before-enqueue` barrier is at about `T0+81` to `T0+86`. Before the fix the quiet
  deadline is the last protocol-git observation plus 60 s, at most `T0+60`: red with at least
  20 s margin. After the fix the window renews at `T0` (claim rows), about `T0+41` (first
  materialization) and about `T0+81` (second): every quiet gap is at most about 41 s, 19 s
  under the window. Rejected: a seam at `dispatch-warning-claim-committed` or
  `dispatch-warning-after-dispatch` (dispatcher-only; the scan materializes within 5 s and the
  barrier arrives early, so the witness cannot go red); one hold longer than 60 s (fails under
  the fix as well, because the fix renews on progress, not on time); a fake-clock unit test of
  a composite probe (`ProgressAwareWaitTests` already pins the renewal arithmetic; the defect is
  the fixture's wiring, which only the real fixture exercises).
- **D-4 Only `CrashAsync`'s waits switch.** A new `UntilDispatchAsync` and
  `WaitForDispatchBoundaryAsync` in the Dispatch partial replace `UntilProtocolAsync` and
  `WaitForBoundaryAsync` inside `CrashAsync` (both branches). `WaitForBoundaryAsync`,
  `UntilProtocolAsync` and `ReceiptAsync` stay as they are for the land tests.
  `WaitForDispatchIntentsAsync` stays on `UntilProtocolAsync`: no dispatch-side row exists before
  the claim commit that creates the intents, so the probe would add nothing there.
- **D-5 Verification design is in this plan; next stage is Code.** The brief folds test design
  into this dispatch: one red-first witness, one narrow green.
- **D-6 One red commit, one fix commit, two builds.** S0 (seam plus witness) is committed
  before S1 (the probe). CP-1 runs red from the first build; CP-2 runs green from the second.
- **D-7 Dispatch and prerequisites.** Code runs with `-Platform Windows`, no `-Runner`, in its
  own worktree on the desktop. A fresh worktree has no `client/dist`; the fixture serves it
  (`UsePrebuiltFrontend = true`) and hard-fails without it, so the client bundle build is the one
  pre-authorized unlisted build (Verification design). The rows carry `PhoneHome__Enabled=false`
  in `Environment`; Code starts the run from a shell with the inherited `PhoneHome__*` variables
  cleared, as the Investigate and CARD-1305 runs did, and reports what it set.
- **D-8 Every row is `Serial: true`.** Each row boots real servers, an isolated runner, Postgres
  and native FakeGrok under a per-assembly process limiter; two rows in flight would share the
  host and the limiter would not cap the second process.
- **D-9 Not rerun, with reason.** The other eight C540 rows share the switched wait but add
  about 30 minutes; `ProgressAwareWaitTests` and the land tests touch no edited line
  (`WaitForBoundaryAsync` is unchanged). They stay in the rc Windows lane (F-2).
- **D-10 The wait record names the renewal source.** `UntilDispatchAsync` writes the same
  `wait-<guid>.json` as `UntilProtocolAsync` plus `kind: "dispatch"`, `dispatchBefore` and
  `dispatchAfter` (the six marker values). A future quiet timeout therefore says which marker
  moved last, so the next red is attributed rather than widened.

Checklist for sessions that wait for input: not applicable. No slice adds or changes a session
that waits for input; the FakeGrok caller sessions are fixture-owned and disposed with the
fixture, and the seam delays a boundary inside the child host, not a session.

## Slices

### S0 — slow-materialize seam and red witness (one commit, before the fix)

1. `tests/Antiphon.E2E/Fixtures/LandDeliveryOptions.cs`, `FileBoundary.ReachedAsync`, after
   the owned-task check (line 148) and before the `enqueue-errors` branch (line 150): if
   `slow-launch.json` exists in `options.Root`, parse it as `Dictionary<string, int>`; when it
   names the current `boundary`, write `slow-launch-{boundary}-{Guid:N}.observation.json` with
   `{ boundary, taskId, identity, seconds, at, pid }` and `await Task.Delay(TimeSpan.FromSeconds(seconds), ct)`.
   Nothing after it changes; an absent or non-matching file is a no-op for every other test.
2. `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.Dispatch.cs`: `WriteSlowLaunchAsync(IReadOnlyDictionary<string, int>)`,
   `ClearSlowLaunch()` (deletes the file), and
   `AssertSlowMaterializationPremiseAsync(string boundary, IReadOnlyList<AgentTaskDispatchWarningIntent> intents, int seconds)`:
   the seam records for `dispatch-warning-before-materialize` must name exactly the intent ids
   as their identity set; `File.GetLastWriteTimeUtc(Root/<boundary>.barrier.json)` minus the
   newest protocol-git `at` must exceed 60 s; write `slow-launch-premise.json` with the measured
   silence, the record count and the identities.
3. `tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs`: `CrashAsync` gains
   `int slowMaterializeSeconds = 0`. When positive: call `WriteSlowLaunchAsync` with
   `{"dispatch-warning-before-materialize": slowMaterializeSeconds}` after `UseChildAsync(cut)`
   and before `StartDispatchAsync()`; after the boundary wait returns call `ClearSlowLaunch()`
   (the restarted `none` child must never see it); after `WaitForDispatchIntentsAsync()` call
   `AssertSlowMaterializationPremiseAsync(boundary, intents, slowMaterializeSeconds)`. Add
   `[Test] public Task C540_PreEnqueueWaitSurvivesSlowMaterialization() => CrashAsync("pre-enqueue", "before-enqueue", slowMaterializeSeconds: 40);`
   with a comment naming CARD-1359: dispatch-side progress keeps flowing while protocol-git is
   silent for more than the quiet window. Red today: `WaitForBoundaryAsync("before-enqueue")`
   throws `TimeoutException` `before-enqueue (quiet 60s after <about 80-100>s; progress N)`
   before any premise assertion runs.

### S1 — dispatch-aware quiet window (one commit)

1. `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.Dispatch.cs`:
   `public sealed record DispatchProgress(long Events, long Intents, long Materialized, long Notifications, long Queued, long Launched) { public long Total => ...sum...; }`;
   `DispatchProgressAsync()` reading the six markers of D-2 from one `CreateContext()` with
   `AsNoTracking`; `UntilDispatchAsync(Func<Task<bool>> predicate, string evidence, int quietSeconds = 60)`
   mirroring `UntilProtocolAsync`: take `dispatchBefore`, keep a `long dispatch` that the
   predicate wrapper refreshes (`dispatch = (await DispatchProgressAsync()).Total`) before
   calling `predicate`, pass `() => ProtocolProgress() + dispatch` as the probe, and write the
   D-10 wait record in `finally`; `WaitForDispatchBoundaryAsync(string boundary)` as the
   dispatch-aware twin of `WaitForBoundaryAsync`.
2. `tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs`, `CrashAsync`: the `queue`
   branch calls `UntilDispatchAsync`; the other branch calls `WaitForDispatchBoundaryAsync`.
   No other call site changes.
   Green after S1: the witness (renewals at the claim commit and each materialization) and
   `C540_PreEnqueueCrashRecoversWarnings` (its wait is satisfied in about 10 s, as in both
   reproductions, with a `kind: "dispatch"` record).

### Follow-ups, not slices

- **F-1** Product: give `AgentSessionRuntime`'s blocking runner reads a deadline or make them
  async, and enable runner request timing. The separate card the brief names; the report's
  "Not done, noted" section is its input.
- **F-2** The other eight C540 rows, the land tests and `ProgressAwareWaitTests` run in the next
  rc Windows profile execution of `tests/Antiphon.E2E` (D-9).
- **F-3** If a future `kind: "dispatch"` timeout shows a quiet gap longer than 60 s between two
  markers, the dispatcher itself stalled; attribute it to F-1's card, do not widen the window.

## Verification design

| ID | Required outcome / coverage |
|---|---|
| V-1 | With materialization held 40 s per reach, the `before-enqueue` barrier arrives more than 60 s after the last protocol-git observation while dispatch-side markers keep moving; the seam fires for exactly the two intents; the wait is satisfied; both warnings are then recovered through the restarted host with their original intent identities. Red first at CP-1 (quiet timeout), green at CP-2 (premise assertions pass). |
| V-2 | `C540_PreEnqueueCrashRecoversWarnings` keeps passing on the dispatch-aware wait and its wait record is `kind: "dispatch"`, `outcome: "satisfied"`. CP-2. |
| R-1 | The quiet window stays 60 s and the cap 600 s in every switched wait; `WaitForBoundaryAsync`, `UntilProtocolAsync` and `ReceiptAsync` are byte-identical. Review checks the diff; no row. |
| R-deferred | Eight further C540 rows, the land tests, `ProgressAwareWaitTests`: rc Windows lane (D-9, F-2). |

Red rows are expected failures; Code reports them red with the named exception and does not
"fix" them. A red row that passes before its fix means the witness is a stub and must be
reworked before continuing. CP-1's red must be the quiet-window `TimeoutException` from
`WaitForBoundaryAsync("before-enqueue")`, not a premise assertion and not a Blocked refusal.

Prerequisites on the desktop worktree `C:\Antiphon\worktrees\card-task-f26c770a`, before CP-1:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1359-client-ci -- npm --prefix client ci
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1359-client-build -- npm --prefix client run build
```

That pair is the one pre-authorized unlisted build (D-7); report it with its slot lines. Clear
the inherited `PhoneHome__*` variables in the shell that starts the run (D-7). Do not restart
AppHost or the production runner; the fixture owns random-port servers and an isolated runner
(`LandDeliveryFixture.cs:89` asserts the port is not 17204).

Running the table:

```powershell
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-19-card-1359-c540-wait-shape-plan.md --rows CP-1 --expected-source-sha <S0 sha>
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-19-card-1359-c540-wait-shape-plan.md --rows CP-2 --expected-source-sha <S1 sha>
```

Call `wait <run-id>` again while it returns 75. Commit S0 before the first run and S1 before the
second; do not edit source while a run is in flight. A red run retains its `bin-c1359-red`
output: delete every `bin-c1359-red` and `bin-c1359-green` directory (about one per project)
before finishing. Run
`scripts/check-evidence-diff.ps1 -BaseRef 87cb3c143e2aecd206aee2c323200a64a7cd0d85 -HeadRef <pushed sha>`
and the `git fetch origin master` plus `git merge-tree --write-tree origin/master HEAD` landable
check; report both.

Manual acceptance: inspect each fresh TRX identity and count against the `Expect` column. For
CP-1 confirm the failure message starts with `before-enqueue (quiet 60s after` and that the
fixture root holds at least two `slow-launch-dispatch-warning-before-materialize-*.observation.json`
records. For CP-2 confirm the witness root's `slow-launch-premise.json` shows a silence above
60 s and both intent ids, and that its `before-enqueue` wait record is `kind: "dispatch"`,
`outcome: "satisfied"`, `elapsedSeconds` above 60, `dispatchAfter.materialized == 2`. No UI or
deployment work.

### Mutation handoff

Every control remains pending for method-scoped SourceLanding Mutation after Review and land.
Candidate location `docs/superpowers/mutations/` (unbound).

| PC | Method / variant pending |
|---|---|
| PC-1 | `C540_PreEnqueueWaitSurvivesSlowMaterialization`: in `UntilDispatchAsync` pass `ProtocolProgress` alone as the probe; must fail at the quiet-window timeout; restored must pass. |
| PC-2 | `C540_PreEnqueueWaitSurvivesSlowMaterialization`: remove the seam's `Task.Delay` (keep the record); must fail at the premise assertion (silence under 60 s); restored must pass. |

### Cost

Ordinary floor is the `EstimatedMinutes` sum: **19 minutes** for two rows including two isolated
E2E builds (about 4 minutes each). Add the client bundle prerequisite (about 4 minutes) and
authoring (about 15 minutes): ask `-ExpectAbout 45`, inside the brief's 45 to 60 minute budget.
Measured single-case walls: the pre-enqueue method took 89 s and 105 s in the two Investigate
reproductions; the witness adds about 80 s of held materialization to that. The table was
validated on this branch with `tools/Antiphon.Checkpoints -- import --plan <this file>`.
Additional runs require an actual failure or a missing execution.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S0 | `tests/Antiphon.E2E -> bin-c1359-red/` | red-slow-materialize | `/*/*/DispatchBaseWarningDeliveryE2ETests/C540_PreEnqueueWaitSurvivesSlowMaterialization` | V-1 red | expected red: 1 executed, 1 failed with TimeoutException "before-enqueue (quiet 60s after ...s; progress N)" from WaitForBoundaryAsync, 0 skipped (Windows desktop) | 1 | 8 | true | `PhoneHome__Enabled=false` |
| CP-2 | S1 | `tests/Antiphon.E2E -> bin-c1359-green/` | dispatch-aware-wait | `/*/*/DispatchBaseWarningDeliveryE2ETests/(C540_PreEnqueueCrashRecoversWarnings*)\|(C540_PreEnqueueWaitSurvivesSlowMaterialization*)` | V-1, V-2 | 2 executed, 2 passed, 0 failed/skipped (Windows desktop) | 2 | 11 | true | `PhoneHome__Enabled=false` |
