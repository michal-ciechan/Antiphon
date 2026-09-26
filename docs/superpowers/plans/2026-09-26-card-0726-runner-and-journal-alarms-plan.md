# CARD-0726: runner-eligibility and stale child-journal alarms inside Antiphon

Date: 2026-09-26. Stage: Plan, with the `### Checkpoints` table the brief asks for (red-first
rows, two rounds). Task: `ea7d1a1c`, written on the server2 Linux runner. Source inspected:
`fa86dc6420844382eafa4c656ae03c65834ad9a3` (`origin/master` at the time; the task branch was
fast-forwarded onto it before any line below was cited, and the only commits between the task's
StartRef `bafc3366` and that tip are CARD-0718 plan documents, so every code citation is against
unchanged files). Next stage: **TestDesign**, which owns the final roster; the `## Verification
design` below is the plan's closed list and red-first mechanism for it.

No production code, configuration, test or deployment changed during Plan. Two read-only
reads were taken against the live desktop server through `ANTIPHON_API` and are reported in
ground-truth row 1: `GET /api/runner-defaults` (revision 1, no global or per-kind default) and
`GET /api/session-runners` (`desktop` windows and `server2` linux, both `dispatchEligible`). No
build or test was run; row 14 cites the CARD-0738 measurements taken on this same lane on
2026-09-26 for the checkpoint estimates.

The caller's standing authority ("Yes we still need it; create a card to make it procedural and
antiphon to do the check") covers everything designed here. Nothing needed a further approval.

## Outcome and scope

Antiphon raises two alarms that today only the orchestrator's session-scoped
`watch-server2.sh` loop notices, and it raises them the way the rest of the fleet is told about
stuck work: an attention-feed row and a `WhenIdle` note to the caller sessions that own the
affected tasks. Nothing new polls per second. The only timers are one grace timer per runner
outage and one 15-minute backstop sweep, in the CARD-0699 style (act on events, check at
startup, sweep as a backstop).

1. **Runner unavailable.** A configured, enabled phone-home runner that stops being
   dispatch-eligible (socket end, superseded connection, lease expiry, or never recovered after a
   desktop restart) starts a grace timer (`Alarms:RunnerGraceSeconds`, default 180). If it is
   still not eligible when the timer expires, one `RunnerUnavailable` attention row is raised
   naming the runner, how long it has been down, the last disconnect reason, and how many open
   tasks and live sessions are pinned to it; each caller session with an open task on that runner
   gets one note. Recovery removes the row and sends a recovery note to exactly the sessions that
   were told. A flap inside the grace period leaves no trace anywhere. A runner that CARD-0727
   marks Draining or Retired is not an outage and never alarms.
2. **Stale child journal.** Every registered repository's `<git-common-dir>/antiphon/children/`
   is inspected at server startup (which is the end of every restart), whenever a land, a
   dispatch, or a StartRef fetch is refused because the journal fences the repository mutation
   lease, and in the sweep. A record older than `Alarms:JournalStaleMinutes` (default 5) whose
   owning process is not confirmed alive raises one `RepositoryChildJournalStale` row per
   repository naming the repository, each record's state, and the exact recovery command. Live
   records never raise. Recovery stays manual (D-9).

In scope: a new `Alarms` settings section; an observer seam on `PhoneHomeRunnerDirectory`; a
fence-observer seam on `RepositoryMutationLease`; the `RunnerAlarmCoordinator` (pure state
machine over a clock), `RunnerAlarmState` (the singleton the feed projects), `RunnerAlarmHostedService`
(the one loop), `RepositoryChildJournalInspector` (read-only classification of records); two
`AttentionKind` members and their projections; the client visuals; docs.

Out of scope: any automatic recovery of journal records (D-9); the runner-side repository on
server2 (`/work/repos/antiphon`; the desktop cannot read that filesystem, and its journals fence
only the runner-side worktree creation that CARD-0604's mirror runs); a runner page in the client
(the row links nowhere new; `targetOf` returns null as it does for census rows); the desktop
runner on 17204 (it is not a phone-home runner and has no eligibility state; the existing
`RunnerConsulted=false` degrade covers it); an alert sink, a digest page, a new column, an
`AgentIncident` row (the incident table feeds the CARD-0338 digest pager, which is the alert sink
the card forbids); retiring `watch-server2.sh` itself (it is not in the repository; the
orchestrator stops re-arming it once this lands, and the docs say so).

## Ground truth

What the card and the brief assume against what the inspected code does. Lines are from
`fa86dc64`.

| # | Card / brief assumption | What the inspected code does | Design consequence |
|---|---|---|---|
| 1 | "A runner drops or reports not dispatch-eligible" is one observable state. | `PhoneHomeRunnerDirectory` (`server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs`) keeps one `RunnerSlot` per configured entry (`:50-53`, from `PhoneHomeRunnerCatalog.Configured`, which includes `Enabled: false` entries, `PhoneHomeRunnerSettings.cs:176-190`). Eligibility is `live is { DispatchEligible: true, SocketOpen: true } && !live.IsLeaseExpired(LeaseSeconds)` (`DescribeAsync` `:542-543`; `Status` `:495`). It flips in four places: `AcceptConnect` (a superseded connection, `:283-287`), `MarkRecovered` (true, `:300-311`), `Disconnect` (false, `:328-338`, called once from the connect route after every classified end, `SessionRunnerEndpoints.cs:198`), and `SnapshotOf` (false when the lease expired, evaluated lazily on read, `:566-579`). Nothing observes these transitions; the only readers are the recovery pump (every 50 ms, `PhoneHomeRecoveryPump.cs:71`, `:98`), the catalogue and the status route. Live fleet: `desktop` (windows) and `server2` (linux), both eligible at 2026-09-26T02:04Z; runner defaults are revision 1 with no global or kind default. | D-2: the directory gets a minimal observer (`IRunnerEligibilityObserver.Changed(runnerId)`) called at those four points, under nothing but the existing `_gate`; the coordinator re-reads `Status(runnerId)` on each signal rather than trusting the event payload. Lease expiry is caught by the pump's 50 ms `SnapshotLive` (it already runs; no new poll) and, failing that, by the sweep. Disabled entries are skipped. |
| 2 | Every force-kill restart dropped server2 (CARD-0716) and "nobody was told". | After a desktop restart every remote slot starts with `Live == null` and `LastRecovered == null` (`RemoteInventoryPending` `:449-456`); the runner reconnects with a 1 s to 15 s backoff (`src/Antiphon.SessionRunner/PhoneHomeSettings.cs:82-83`), then the pump's catch-up List marks it recovered (`PhoneHomeRecoveryPump.cs:129-141`). CARD-0716 has landed the graceful stop (`SessionRunnerEndpoints.cs:139-160`, reason `request_aborted`). `Status` names the reason (`transport_abort`, `request_aborted`, `lease_expired`, `socket_closed`, `superseded`, ...) and `LastDisconnectAtUtc` (`:481-500`). | D-3: startup opens an episode for every enabled remote runner with `DownSince = now`; a normal restart recovers inside the 180 s grace and nothing is raised. The row's "last error" is `Status.DisconnectReason`. |
| 3 | The attention feed is where a raised alarm lives, and it can hold a runner-scoped row. | `AttentionService.GetAsync` (`server/Application/Services/AttentionService.cs:157-262`) is a read-time projection; a row needs no task, session or agent (`ZombieCensusReport` rows pass six nulls, `AttentionService.Leaks.cs:171`; `InboundUnconsumed` has "no required agent"). Singleton state is projected through an optional constructor parameter (`ZombieCensusState? censusState = null`, `:141`, `:153`, published by the census job). `ConditionKey` is the client's identity for a row (`dispatch-held:{id:N}`, `zombie-census:{class}`). `AttentionKind` is append-only; highest shipped member is `ZombieCensusReport = 47` (`AttentionDtos.cs:311`); the CARD-0738 plan (not landed) reserves `48 CardClosedWhileWorking`. The client maps every member (`client/src/api/attention.ts:17`, `attentionVisuals.ts:43`, lockstep test `attentionVisuals.test.ts:100`, `:118`, `:267`). | D-6: `RunnerAlarmState` is the singleton the feed reads through a new optional parameter; `RunnerUnavailable = 49` and `RepositoryChildJournalStale = 50` (Code assigns the next free numbers after whatever has shipped; the plan's numbers are placeholders that skip 0738's 48). Two `ConditionKey`s: `runner-unavailable:{runnerId}` and `journal-stale:{commonDirectoryKey}`. |
| 4 | "Send a WhenIdle note to each caller session" has an existing path. | `SessionMessageQueueService.EnqueueAsync(sessionId, body, MessageSendMode.WhenIdle, ct, origin, conversationKey, sourceTaskId, contentDigest, noteHeader, ..., deliverIfIdle)` (`SessionMessageQueueService.cs:344-368`). The Delegation-origin digest dedupe (`:592-625`) applies only to `Origin == Delegation` with a `SourceTaskId`; `System` origin ("injected by Antiphon itself: bootstrap/restart/compaction-recovery notes", `QueuedMessageOrigin.cs:18`) delivers one per turn and has no dedupe. CARD-0699's recovery enqueues with `deliverIfIdle: false` and then `CompletionNoteFlushQueue.TryEnqueue(session)` so delivery runs on the flush workers, not the caller's thread (`CompletionNoteWorkHostedService.cs:125-131`; `CompletionNoteWork.cs:13`). The `CallerNoteUndelivered` row watches Delegation and Check notes only (`AttentionDtos.cs:138-143`). | D-7: origin `System`, header `[runner <id> unavailable]` / `[runner <id> recovered]` / `[repository journal stale]`, `deliverIfIdle: false` plus `TryEnqueue`; the coordinator's episode holds the notified session ids so the recovery note goes to exactly those sessions and a note is never sent twice for one episode. A Pending note on a dead caller is simply never typed; it is not this card's job to watch it. |
| 5 | "Tasks pinned to the runner" and "caller sessions with open tasks on that runner" are readable. | `AgentTask.RunnerId` (`AgentTask.cs:203`, null or empty means desktop), `AgentTask.ParentSessionId` (`:47`), `AgentTask.Status` (`:314`); `AgentSession.RunnerId` (`AgentSession.cs:46`). The catalogue already counts live sessions and pending remote-worktree tasks per runner (`SessionRunnerCatalogue.cs:80-96`); the dispatcher holds a Queued task whose runner is not eligible with `DispatchHoldDetails.RunnerUnavailable` (`AgentTaskDispatcher.cs:954`, class `Runner`), which the `DispatchHeld` row surfaces per task after `Delegation:DispatchHeldWarningSeconds` (300). | D-4: open = `Status in (Queued, Dispatched, Working, Blocked)` and `AgentTaskRoles.NotSpecialist`; pinned = `RunnerId == id`; callers = distinct `ParentSessionId` of those tasks where `ReplyTo == Session`. The runner row is runner-scoped and coexists with per-task `DispatchHeld` rows; it does not suppress them (a human reads "the runner is down" once and "this task waits on it" per task). |
| 6 | A per-runner "Draining" state exists (CARD-0727) and must not alarm. | Not landed. `grep Draining server/` finds only the Pty custody enum, the alert throttle and channel-bridge words. The CARD-0727 plan (`2026-09-25-card-0727-rolling-runner-upgrade-plan.md`, D-6 to D-9) designs a durable `SessionRunnerState` row (`Draining`, `RedirectTo`, `RetireWhenIdle`, retired) mirrored into the directory; a draining runner keeps `DispatchEligible` true (D-7: "Draining gates new work only; `Resolve` is untouched"), so drain itself never trips the eligibility check; the redeploy inside a rolling upgrade (drain, redeploy, clear) does drop the socket, and retire leaves a slot that never reconnects. CARD-0727 is InProgress, Critical/Now. | D-5: a one-method seam `IRunnerAlarmExclusion.Excluded(runnerId) -> string?` registered now with a null-returning default; CARD-0727 registers the row-backed implementation (`"draining"` / `"retired"`). An excluded runner's open episode is closed silently (no note, no row); the sweep re-evaluates so a drain cleared mid-outage re-arms the grace. |
| 7 | The child journal lives in `.git/antiphon/children`, and "dead" is computable. | `RepositoryChildJournal` (`server/Infrastructure/Git/RepositoryChildJournal.cs`) writes `<common>/antiphon/children/<guid>.json` (`ChildRecord(SchemaVersion, CommonDirectory, ProcessId, StartTicks, Completed)`, `:15-25`, `:131-132`) before every mutating owned git command (`LandingGit.cs:77`) and deletes it after the exact handle exits. `HasUnfinishedAsync` (`:96-129`) treats every file as a fence, including a dead root ("a dead/reused root PID is not an acknowledged exit of its process tree", `:120-122`). Liveness is `ILandingGit.IsProcessAliveAsync(pid, startTicks)` (`LandingGit.cs:35-46`): `false` for a missing PID or a different start identity, `null` when the process cannot be read. `scripts/recover-repository-children.ps1` classifies `alive`, `dead`, `reused`, `completed`, `unknown` and recovers only the middle three under `-Execute -ConfirmDescendantsExited`; it never auto-runs. | D-8: `RepositoryChildJournalInspector.InspectAsync(repository)` reuses `ILandingGit.CommonDirectoryAsync` and `IsProcessAliveAsync` and reproduces the script's five states plus `malformed` (non-`.json`, unreadable, wrong common directory). Age is the file's last-write time. A record raises when its state is not `alive` and its age exceeds the threshold. |
| 8 | "A failed land or dispatch whose refusal names a child record" is one hook. | Three callers name the fence: the dispatcher's `DescribeLeaseHoldAsync` (`AgentTaskDispatcher.cs:1097`, `:1122`, `LeaseFenced`), `StartRefAvailability.AcquireLeaseAsync` (`:124`), and both through `RepositoryMutationLease.DescribeUnavailableAsync` (`RepositoryMutationLease.cs:72-80`). The land path does not name it: `AgentTaskLandService.RunAsync` gets `null` from `TryAcquireAsync` (`:297-303`) and `HoldOnBusyLeaseAsync` describes the owner only. The single point every refusal passes through is `RepositoryMutationLease.AcquireAsync` returning `null` because `HasUnfinishedAsync` was true (`:33-37`). | D-8: `RepositoryMutationLease` gets an optional `IRepositoryFenceObserver` and calls `Fenced(common)` at `:33-37` and in `DescribeUnavailableAsync` when it returns non-null. The coordinator inspects that repository on the next wake. No land or dispatch code changes. |
| 9 | "Every registered repository" is a known set. | `Project.LocalRepositoryPath` (`Project.cs:14`) is the registered checkout; the attention service already enumerates non-archived projects' paths (`AttentionService.cs:2568-2580`). Open tasks carry `RepoPath` (`AgentTask.cs:155`), which is the checkout the lease and journal are keyed on. There is no repository entity beyond these. | D-8: repositories = distinct non-empty `LocalRepositoryPath` of non-archived projects, plus distinct `RepoPath` of open non-specialist tasks and of tasks with a pending land request; paths are normalised through `CommonDirectoryAsync` so two worktrees of one repository are one row. A path that no longer exists or is not a git repository is skipped with one Debug log line. |
| 10 | "Check at startup and after every restart" needs a restart hook. | The server has no post-restart event distinct from startup: `restart-apphost.ps1` kills and relaunches the AppHost and proves activation through `GET /api/version` (`docs/bootstrap.md:577`); the graceful stop (CARD-0716) ends the receive loops on `ApplicationStopping`. Startup passes are `BackgroundService.StartAsync` overrides (`SessionStateWarmupService.cs:8-12`) or the first iteration of `ExecuteAsync` (`CompletionNoteWorkHostedService.cs:25-30`, `nextSweep = now`). | D-3: the hosted service's first loop iteration runs the journal inspection and opens the runner episodes; that is the "after every restart" check. |
| 11 | The CARD-0699 style is an implementable pattern. | `CompletionNoteWorkHostedService.RecoverAsync` (`:25-58`): one loop, `flushes.Recovery.Take(now)` for signalled work, a 15-minute `nextSweep`, `WaitAsync(nextSweep, clock, ct)` that returns on a signal or the earliest due time (`CompletionNoteWork.cs:86-98`, a bounded `Channel<bool>` plus `Task.Delay(delay, clock, ct)`), a failure counter that backs off at most 60 s and requests a sweep. Tests drive it with `FakeTimeProvider`. | D-3: `RunnerAlarmHostedService` copies that loop shape with its own `AlarmWakeQueue` (signal, per-runner due times, sweep flag). No Hangfire: the grace timer needs in-process scheduling, and the test host runs with the Hangfire worker disabled (`HangfireStartupSafetyTests.cs:35`). |
| 12 | Configuration follows an existing shape. | Settings classes bind a section with a validator and `ValidateOnStart` (`Program.cs:179-186`; `ZombieCensusSettingsValidator.cs`); `PhoneHomeRunner` already has 25 keys and its validator is 60 lines (`PhoneHomeRunnerSettings.cs:225-290`). | D-1: a small `Alarms` section rather than more `PhoneHomeRunner` keys. |
| 13 | The directory and the journal have test harnesses the new tests can reuse. | `PhoneHomeTestHost` (`tests/Antiphon.Tests/TestHelpers/PhoneHomeTestHost.cs`) hosts the real `PhoneHomeRunnerDirectory` with an injectable `TimeProvider` (`:53`, `:109`), scripted peers that connect, `Socket.Abort()` and reconnect (`MultiRunnerDirectoryTests.cs:88-93`), `MarkRecovered` called by hand (`:112-113`), and `LeaseSeconds = 90` (`:74`); the recovery pump is not hosted there. `ScratchGitRepo` plus `LandingGit` plants journal records by writing `ChildRecord` JSON (`RepositoryMutationLeaseTests.cs:515-523`, `:561-563`); `RepositoryMutationLeaseDescribeTests` is the one-class fence-description shape. `AttentionService` is built directly with six positional arguments (`DispatchHeldAttentionTests.cs:269-276`) on `TestDbFixture.CreateIsolatedSchemaAsync()`. | Every V row below names one of these harnesses; nothing needs a running runner, 17202 to 17205, or the desktop. |
| 14 | The Linux lane can run the classes. | CARD-0738 measured on this host on 2026-09-26: `tests/Antiphon.Tests` build 4 min 03 s under a build slot (`maxcpucount=6`), single small classes about 1 min each including the Testcontainers Postgres start; a method-level OR filter matches nothing, so each new test class is small and filtered as `/*/*/<Class>/*`. `run-checkpoint.ps1` adds `UseAppHost=false` off Windows. Inherited red at `0e83d3f2`: `AgentTaskCardBindingTests` 14 of 19 failed (`workspace_default_not_git`, CARD-0644), not this card's and not in any row below. | EstimatedMinutes = 4 min build + about 1 min per small class, 2 min for a class that starts the phone-home test host. Lane: Linux (server2). No `-Platform Windows`: nothing here touches junctions, ConPTY, CRLF or E2E. No `-Runner` pin. |

## Decisions

Numbered D-1 to D-10, written under stated defaults. Each names what was rejected and why.

### D-1. One `Alarms` settings section

`server/Application/Settings/AlarmSettings.cs`, bound from `Alarms` with an `AlarmSettingsValidator`
and `ValidateOnStart` beside `ZombieCensus` (`Program.cs:179-186`):

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Off: the hosted service returns at once; the feed shows nothing of either kind. |
| `RunnerGraceSeconds` | `180` | How long a runner may be ineligible before it is an outage. Must be positive. |
| `SweepMinutes` | `15` | The backstop sweep period. Must be positive. |
| `JournalStaleMinutes` | `5` | A journal record younger than this is an operation in flight, whatever its process state. Must be positive. |
| `JournalEnabled` | `true` | Off: no repository is inspected (a host whose projects live on a filesystem the server cannot read). |

Rejected: more keys under `PhoneHomeRunner` (its validator already lists 25 keys, and the journal
half is not a phone-home concern); a `Delegation` key (the grace is about a runner, not a task);
hard-coded constants (the card asks for the grace to be configurable, and the 5-minute journal
threshold is the orchestrator's current loop value, which an operator may want to raise on a slow
disk).

### D-2. The directory tells an observer; the observer never trusts the payload

`PhoneHomeRunnerDirectory` gains a trailing optional constructor parameter
`IRunnerEligibilityObserver? observer = null` (`server/Application/Interfaces/IRunnerEligibilityObserver.cs`,
one method `void Changed(string runnerId)`), wired in `Program.cs:295-301`. It is called after the
`_gate` block, never inside it, at the four transitions of ground-truth row 1: `AcceptConnect`
(when a connection was superseded), `MarkRecovered` (when the flag actually flipped), `Disconnect`
(when the connection was the slot's live one), and `SnapshotOf` (when it just set
`DispatchEligible = false` for an expired lease). The call is wrapped so an observer exception is
logged at Warning and never reaches the connect route or the pump.

The coordinator treats `Changed` as "look again", not as a fact: on every wake it re-reads
`Status(runnerId)` for every enabled remote id and derives eligibility from
`Available && DispatchEligible`, the same predicate the catalogue and `DescribeAsync` use. An
event that arrives late, twice, or for a runner that has already recovered is therefore harmless.

Rejected: a .NET `event` on the directory (a subscriber that throws inside `_gate` would take the
directory down; the observer call sits outside the lock by construction); the alarm service
polling `Status` on its own timer (the card forbids per-second polling, and the 50 ms pump already
evaluates lease expiry through `SnapshotLive`); publishing on `IEventBus` (that bus is the
client's SignalR fan-out, not an in-process signal, and the coordinator would then depend on a
network hub to hear about a local flip).

### D-3. One hosted loop, one coordinator, in-memory episodes

Three new types:

- `RunnerAlarmState` (`server/Infrastructure/Agents/SessionRunner/RunnerAlarmState.cs`, singleton,
  the `ZombieCensusState` shape): the current episodes and the last journal inspection, published
  as immutable snapshots (`Volatile.Read` / `Interlocked.Exchange`). Records:
  `RunnerOutageEpisode(RunnerId, DownSince, LastReason, RaisedAt?, PinnedOpenTasks, LiveSessions, NotifiedSessionIds)`;
  `JournalFinding(Repository, CommonDirectory, Records: [(File, State, AgeAtInspection, ProcessId?)], InspectedAt)`.
- `RunnerAlarmCoordinator` (`server/Application/Services/RunnerAlarmCoordinator.cs`, scoped, pure
  apart from the database and the queue): `Task EvaluateRunnersAsync(now, ct)` and
  `Task EvaluateJournalsAsync(repositories, now, ct)`. It takes `IRunnerEligibilitySnapshotSource`
  (S1: the directory behind one read per runner), `IRunnerAlarmExclusion` (D-5), `RunnerAlarmState`,
  `RepositoryChildJournalInspector` (D-8), `AppDbContext`, `SessionMessageQueueService`,
  `CompletionNoteFlushQueue`, `IOptions<AlarmSettings>`, `TimeProvider`, `ILogger`.
- `RunnerAlarmHostedService` (`server/Infrastructure/Orchestration/RunnerAlarmHostedService.cs`,
  `BackgroundService`): the CARD-0699 loop. Its `AlarmWakeQueue` singleton (`Signal(runnerId)`,
  `Fenced(repositoryOrCommon)`, `RequestSweep()`, `Take(now)`, `WaitAsync(due, clock, ct)`) is what
  the two observers write to. First iteration: sweep (journals over every repository of row 9, and
  a runner pass that opens an episode for each enabled remote runner not yet eligible with
  `DownSince = now`). Every later wake: the runner pass for all runners (cheap: a dictionary
  read per runner), journal inspection for repositories named by `Fenced`, and a full sweep when
  `now >= nextSweep` (then `nextSweep = now + SweepMinutes`). The wait's due time is the earliest
  of `nextSweep` and every open, unraised episode's `DownSince + RunnerGraceSeconds`, so the
  grace expiry is a timer, not a poll. Failures follow CARD-0699: log, request a sweep, back off
  at most 60 s.

Runner pass per enabled remote runner `r`, with `eligible` from row 1's predicate and `excluded`
from D-5:

| Episode | Eligible | Excluded | Action |
|---|---|---|---|
| none | false | false | open `DownSince = now` (or `Status.LastDisconnectAtUtc` when it is later than the last resolution and earlier than now), `LastReason = Status.DisconnectReason` |
| open, unraised | false | false | if `now - DownSince >= grace`: raise (D-4); else nothing |
| open, raised | false | false | refresh `LastReason` and the counts on the snapshot; nothing else |
| open, unraised | true | any | close silently (a flap; no trace) |
| open, raised | true | any | resolve: drop the row, recovery note to `NotifiedSessionIds` (D-7), one Information log line with the total downtime |
| open | any | true | close silently and remember the exclusion in the log at Debug; a raised episode still sends the recovery note, because those callers were told of an outage that is now explained |
| none | true | any | nothing |

Episodes live in memory. A desktop restart during an outage forgets the raised flag and the
notified set; the first iteration opens a fresh episode with `DownSince = now`, so a runner that
is still down 180 s after the restart is raised again and its callers are told again. That is the
correct reading: the restart is a new fact, and a caller told twice about a runner that has been
down across a desktop restart is not spam. Rejected: a durable `RunnerOutageEpisode` table (a
migration, retention, and a second source of truth for what the directory already knows in
memory; nothing survives a restart on the runner side either); `AgentIncident` rows (they feed
the digest pager, an alert sink, and require an agent or session); Hangfire (row 11).

### D-4. What a raised runner row and its notes carry

The feed row, built by `AttentionService.BuildRunnerUnavailableItemsAsync` from
`RunnerAlarmState` and placed right after `BuildZombieCensusItemsAsync` (`AttentionService.cs:231`):

- `Kind = RunnerUnavailable`, `Severity = Error` (the runner is broken for dispatch; `Critical`
  is reserved for rows that block a human answer). No task, session, agent or message id.
- `Title = "Runner {displayName} unavailable"`; `Headline = "Not dispatch-eligible for {m} min
  ({reason}); {n} open task(s) and {k} live session(s) are pinned to it."`; `Evidence =
  "runner={id}; downSince={O}; raisedAt={O}; lastReason={reason}; lastDisconnectAt={O};
  reconnects={n}; pinnedTasks={short ids, up to 8}; notified={count} caller session(s)."`;
  `SinceUtc = DownSince`; `Actions = [OpenDrawer]`; `ConditionKey = "runner-unavailable:{id}"`.

Counts come from one query at raise time (row 5) and are refreshed on each later wake while the
episode is raised, so the row's numbers move with the fleet. The raise also writes one Warning log
line naming the runner, the downtime and the reason, which is the desktop log's record.

Notes (D-7 for the mechanism), one per distinct caller session:

```
[runner server2 unavailable]
Runner 'server2' has not been dispatch-eligible for 3.1 min (since 2026-09-26T01:02:03Z; last
reason: transport_abort). 2 of your open tasks are pinned to it: ea7d1a1c (Plan CARD-0726),
5d1c0e9a (Code CARD-0710). New dispatches to it stay Queued until it recovers; Antiphon sends a
note when it does. Do not reroute them silently.
```

```
[runner server2 recovered]
Runner 'server2' is dispatch-eligible again after 7.4 min down (since 2026-09-26T01:02:03Z).
Queued work bound to it dispatches on the next tick.
```

Rejected: a note per task (five tasks on one runner would be five turns for one fact); a note on
every wake while the outage lasts (the row already moves; the caller was told once); an `Escalate`
or `Cancel` action on the row (the row is runner-scoped; the per-task verbs stay on the
`DispatchHeld` rows).

### D-5. Draining and retired runners are excluded through a seam CARD-0727 fills

`server/Application/Interfaces/IRunnerAlarmExclusion.cs`: `string? Excluded(string runnerId)`,
returning a reason or null. This card registers `NeverExcluded` (always null). CARD-0727 R2
registers the implementation backed by its `SessionRunnerState` row and returns `"draining"`
while `Draining` is set and `"retired"` after retire; nothing else in this card changes when it
does. The coordinator consults the seam on every wake (D-3 table), so a runner drained during an
open episode is closed the same wake, and one whose drain is cleared while it is still down
re-opens an episode with `DownSince = now`.

Rejected: reading `DispatchEligible` alone (row 6: a draining runner stays eligible, and a
retired one looks exactly like an outage forever); waiting for CARD-0727 to land first (the
seam costs one interface and one three-line class, and this card's tests pin the behaviour with a
fake exclusion so 0727's implementation inherits a passing contract).

### D-6. Two appended attention kinds, projected read-time from the singleton

`AttentionKind.RunnerUnavailable` and `AttentionKind.RepositoryChildJournalStale`, appended after
the highest shipped member at Code time (49 and 50 if CARD-0738's 48 lands first; 48 and 49 if it
has not; the plan's tests assert the member names and severities, never the integers, so the
order of landing does not matter). `AttentionService` gets a trailing optional constructor
parameter `RunnerAlarmState? alarms = null` (the `censusState` shape, `:141`, `:153`): a harness
that predates this card builds the service unchanged and sees no rows. `AttentionSummaryDto.From`
needs no change: both kinds count as Open.

Client: `client/src/api/attention.ts` union, `attentionVisuals.ts` entries (`RunnerUnavailable`:
label "Runner unavailable", colour `danger`, icon `TbPlugConnectedX` (already imported), hint
"A phone-home runner has not been dispatch-eligible past the grace; its queued work waits.";
`RepositoryChildJournalStale`: label "Repository fenced", colour `danger`, icon `TbGitCommit`,
hint "A dead child-journal record fences every land and dispatch in this repository; run the
recovery command in the evidence."), the group tables and the lockstep list in
`attentionVisuals.test.ts` (`:68-69`, `:93-94`, `:267`). `targetOf` is unchanged (null for both;
the evidence is the whole instruction).

Rejected: one shared "operations" kind with the subject in the headline (the client groups and
labels on kind, and two conditions with different recovery verbs deserve two badges); a durable
attention entity (none exists; every row is a projection).

### D-7. Caller notes go through the queue as `System` notes, held by the episode, never twice

`SessionMessageQueueService.EnqueueAsync(session, body, MessageSendMode.WhenIdle, ct,
QueuedMessageOrigin.System, noteHeader: header, deliverIfIdle: false)` followed by
`CompletionNoteFlushQueue.TryEnqueue(session)`, exactly the CARD-0699 recovery path (row 4), so
delivery runs on the flush workers and lands at the caller's next idle point. The coordinator
enqueues once per session per episode and records the session id in `NotifiedSessionIds` before
the enqueue returns; a session missing from the database (`RequireSessionAsync` throws) or a
queue failure is logged at Warning and the loop continues with the next session. The recovery
note goes only to `NotifiedSessionIds`; a caller whose task was created after the raise hears
nothing, which is right: it was never told of an outage. For journal findings there is no caller:
a fenced repository is an operator fact, and the `DispatchHeld` and `LandHeld` rows already tell
the affected tasks' callers per task. Rejected: `QueuedMessageOrigin.Delegation` (its digest
dedupe needs a `SourceTaskId`, and a Delegation note is "a report", which this is not);
`MessageSendMode.Now` (types into a busy caller); `deliverIfIdle: true` (runs delivery on the
alarm loop's thread, the CARD-0312 hazard).

### D-8. Journal inspection is read-only, keyed on the common directory, and event-driven at the fence

`RepositoryChildJournalInspector` (`server/Infrastructure/Git/RepositoryChildJournalInspector.cs`,
singleton over `ILandingGit`): `Task<JournalInspection> InspectAsync(string repository, TimeSpan
staleAfter, DateTimeOffset now, CancellationToken ct)`. It resolves the common directory, lists
`antiphon/children`, and classifies each entry as the script does (row 7): `.json` with
`SchemaVersion 1`, matching `CommonDirectory`, `ProcessId` and `StartTicks` -> `alive` /
`dead` / `reused` by `IsProcessAliveAsync` (`true` alive; `false` dead when the PID is gone and
`reused` cannot be told apart by that method, so both report `dead`; `null` -> `unknown`);
`StartTicks null && Completed` -> `completed`; anything else -> `unknown`; a non-`.json` file, a
reparse point, an unreadable or torn file -> `malformed`. Age is `File.GetLastWriteTimeUtc`. A
record is `stale` when its state is not `alive` and `now - age >= staleAfter`. The inspector
never opens `landing.lock` (the CARD-0535 V-2b rule) and never deletes anything.

Trigger points: the sweep and the first iteration inspect every repository of row 9;
`RepositoryMutationLease` gets a trailing optional `IRepositoryFenceObserver? fences = null`
(`void Fenced(string commonDirectory)`), called when `AcquireAsync` returns null because
`HasUnfinishedAsync` was true (`:33-37`) and when `DescribeUnavailableAsync` returns non-null
(`:75-79`); the observer is the `AlarmWakeQueue`, and the coordinator inspects that one
repository on the next wake. That covers the land (`AgentTaskLandService.cs:297`), the dispatcher
(`:1097`, `:1122`) and the StartRef fetch (`StartRefAvailability.cs:124`) without touching them.

The feed row, one per repository with at least one stale record: `Kind =
RepositoryChildJournalStale`, `Severity = Error`, `Title = "Repository fenced: {repository}"`,
`Headline = "{n} stale child-journal record(s) ({states}) fence every land and dispatch here;
oldest {m} min."`, `Evidence = "common={common}; records: {file} state={state} age={s}s
pid={pid}; ... Recovery: pwsh -NoProfile -File scripts/recover-repository-children.ps1
-Repository {repository} (preview), then -Execute -ConfirmDescendantsExited after confirming the
descendants exited. Unknown or malformed records are retained by the script and need
inspection."`, `SinceUtc = oldest record's write time`, `Actions = [OpenDrawer]`, `ConditionKey =
"journal-stale:{commonDirectoryKey}"` where the key is `Path.TrimEndingDirectorySeparator(Path.GetFullPath(common))`
(the lease's own `PathKey`). A repository whose stale set becomes empty on a later inspection
drops off the feed; a live record alone never raises.

Rejected: hooking the three refusal sites individually (three edits for one fact the lease
already knows); inspecting on every `DescribeUnavailableAsync` call inline (the dispatcher calls
it every tick while a task is held; the inspection runs git and the filesystem, so it belongs on
the alarm loop, once per wake); raising `unknown` and `malformed` at a longer threshold or not at
all (they fence admission exactly as a dead record does and are the ones most in need of a
human).

### D-9. Recovery stays manual

The card asks whether the server may run the recovery itself when every descendant is provably
exited, reusing the `-ConfirmDescendantsExited` proof. It may not, for this reason: that switch is
a human attestation, not a computable predicate. `recover-repository-children.ps1` itself says a
dead or reused root PID "cannot prove" the descendants exited, and the server has no better
evidence: orphaned grandchildren re-parent, the journal records only the root's identity, and
`IsProcessAliveAsync` answers for one PID. The one automatic proof that would be sound (every
process on the host started after the record was written, which a machine reboot establishes:
"A server restart alone does not establish descendant exit; a machine reboot does",
`docs/orchestration-loop.md:1216`) is not what the card asks for and needs its own card, with the
boot-time reading and its Windows/Linux differences designed and tested there. So: the row names
the exact command, the docs say the row replaces the manual `ls`, and nothing deletes a record.
`Alarms` carries no `JournalAutoRecover` key, so a later card adds the key and the behaviour
together rather than shipping a dormant switch.

### D-10. Docs and the retired loop

- `docs/orchestration-loop.md`: in the repository-mutation paragraph (`:1202-1219`), after the
  recovery command, one sentence: Antiphon inspects every registered repository's journal at
  startup, on a fenced land or dispatch and every 15 minutes, and raises a
  `RepositoryChildJournalStale` attention row naming the command; an orchestrator watches the
  feed, not `ls`. In the Post-land server activation check paragraph (`:115-130`), one sentence:
  after a restart, a runner that has not recovered within `Alarms:RunnerGraceSeconds` raises
  `RunnerUnavailable` and notes the callers; no session-scoped watch loop is re-armed.
- `docs/bootstrap.md` canonical local restart (`:390-400`): the same two sentences as the
  "after a restart" step, in place of any manual journal listing.
- `docs/ops-http.md` attention row (`:96`): the two kinds, their `ConditionKey`s and the `Alarms`
  keys; `docs/session-runtime-invariants.md`: one line beside the CARD-0699 invariant naming the
  alarm loop's timers (grace and sweep only).
- `AGENTS.md` is unchanged: the safety core already says decisions and alarms belong on the
  attention feed.

## Slices

Two Code rounds. R1 (S1 to S3) is the server mechanism and is complete on its own: the alarm
loop runs, notes go out, the state is published, and the feed ignores it until S4. R2 (S4, S5)
is the surface and the docs. Each `Sn-tests` commit lands the compiling red tests first; each
`Sn` commit makes them green.

### S1. Settings and the three seams (R1)

- `server/Application/Settings/AlarmSettings.cs`, `AlarmSettingsValidator.cs`; `Program.cs`
  registration next to `ZombieCensus` (`:179-186`); `server/appsettings.json` gains
  `"Alarms": { "Enabled": true }` (strict JSON, defaults in the class).
- `server/Application/Interfaces/IRunnerEligibilityObserver.cs`,
  `IRepositoryFenceObserver.cs`, `IRunnerAlarmExclusion.cs` (+ `NeverExcluded` in
  `server/Application/Services/NeverExcluded.cs`).
- `server/Application/Interfaces/IRunnerEligibilitySnapshotSource.cs`: `IReadOnlyList<RunnerEligibilitySnapshot> Snapshots()`
  with `RunnerEligibilitySnapshot(RunnerId, DisplayName, Enabled, Eligible, DisconnectReason,
  LastDisconnectAtUtc, Reconnects)`.
- `PhoneHomeRunnerDirectory`: implements that source (one row per configured slot, derived from
  `Status` and `DescribeAsync`'s predicate, `Enabled` from `Entry.Enabled`); trailing optional
  `IRunnerEligibilityObserver? observer = null`; a `Notify(runnerId)` helper called outside
  `_gate` at the four transitions (D-2). `Program.cs:295-301` passes the observer and registers
  the directory as the source.
- `RepositoryMutationLease`: trailing optional `IRepositoryFenceObserver? fences = null`; calls at
  `:33-37` and `:75-79`. `Program.cs:393` stays a plain registration (the observer resolves from
  DI through a factory lambda).
- `tests/Antiphon.Tests/TestHelpers/PhoneHomeTestHost.cs`: `StartAsync(..., IRunnerEligibilityObserver? observer = null)`.
- Tests: `AlarmSettingsValidatorTests`, `RunnerEligibilityObserverTests`, `RepositoryFenceObserverTests`.

### S2. The journal inspector (R1)

- `server/Infrastructure/Git/RepositoryChildJournalInspector.cs` with `JournalInspection`,
  `JournalRecordFinding`, `JournalRecordState { Alive, Dead, Completed, Unknown, Malformed }`
  (D-8). Registered singleton in `Program.cs` beside `ILandingGit` (`:372`).
- Tests: `RepositoryChildJournalInspectorTests`.

### S3. State, wake queue, coordinator, hosted service (R1)

- `server/Infrastructure/Agents/SessionRunner/RunnerAlarmState.cs` (singleton, D-3 records).
- `server/Application/Services/AlarmWakeQueue.cs` (singleton; implements both observers).
- `server/Application/Interfaces/IRunnerAlarmNotifier.cs` (`Task NotifyAsync(Guid sessionId,
  string header, string body, CancellationToken ct)`) and
  `server/Application/Services/QueueRunnerAlarmNotifier.cs` (D-7: queue + flush queue).
- `server/Application/Services/RunnerAlarmCoordinator.cs` (scoped, D-3 table, D-4 texts, D-8
  repository set): `EvaluateRunnersAsync(now, ct)`, `EvaluateJournalsAsync(repositories?, now, ct)`
  (null = every repository of row 9), `IReadOnlyList<string> RegisteredRepositoriesAsync(ct)`.
- `server/Infrastructure/Orchestration/RunnerAlarmHostedService.cs` (D-3 loop).
- `Program.cs`: singletons (`RunnerAlarmState`, `AlarmWakeQueue`, `NeverExcluded` as
  `IRunnerAlarmExclusion`), scoped (`RunnerAlarmCoordinator`, `QueueRunnerAlarmNotifier` as
  `IRunnerAlarmNotifier`), hosted service after `PhoneHomeRecoveryPump` (`:789`), and the two
  observer registrations resolving `AlarmWakeQueue`.
- Tests: `RunnerAlarmCoordinatorTests`, `RunnerAlarmHostedServiceTests`, `RunnerAlarmNotifierTests`.

### S4. Attention projection and client visuals (R2)

- `AttentionDtos.cs`: the two members (D-6). `AttentionService`: `RunnerAlarmState? alarms = null`
  parameter; `BuildRunnerUnavailableItemsAsync` and `BuildJournalStaleItemsAsync` after `:231`.
- `client/src/api/attention.ts`, `client/src/features/attention/attentionVisuals.ts`,
  `attentionVisuals.test.ts` (D-6).
- Tests: `RunnerAlarmAttentionTests`; the vitest file.

### S5. Docs (R2)

`docs/orchestration-loop.md`, `docs/bootstrap.md`, `docs/ops-http.md`,
`docs/session-runtime-invariants.md` (D-10). No test; Review reads them.

## Verification design

This is the plan's draft. TestDesign (task `08e6f2d2`) keeps its IDs and red-first discipline and
supersedes its roster detail, checkpoint table and cost with `## Test design` at the end of this
document; where the two differ, `## Test design` governs.

### Harness and red-first discipline

- Every backend class takes its own migrated schema (`TestDbFixture.CreateIsolatedSchemaAsync()`,
  the `DispatchHeldAttentionTests` shape) where it needs rows, seeds `AgentTask`, `AgentSession`
  and `Project` rows directly, and never calls `AgentTaskService.CreateAsync` (row 14's inherited
  red). `[Category("Integration")]`; process-spawning classes carry
  `[ParallelLimiter<ProcessSpawnLimit>]`.
- Directory-level tests use `PhoneHomeTestHost.StartAsync(clock: fake, configured: <two enabled
  runners>, observer: recorder)` and scripted peers (`ConnectPeerAsync`, `Socket.Abort()`,
  `MarkRecovered` by hand, `NoteHeartbeat` withheld while the `FakeTimeProvider` advances past
  `LeaseSeconds = 90`, then `SnapshotLive` to evaluate the lease). Nothing hosts the recovery pump.
- Journal tests use `ScratchGitRepo` plus the real `LandingGit`, plant `ChildRecord` JSON as
  `RepositoryMutationLeaseTests.cs:515-523` does (a dead record uses the PID and start ticks of a
  `pwsh -NoProfile -c exit` child after it exited; the alive record uses the test process's own
  PID and start ticks), and set file write times with `File.SetLastWriteTimeUtc` to control age.
- Coordinator tests use a `FakeEligibilitySource : IRunnerEligibilitySnapshotSource` (S1's
  `Snapshot` behind a small interface the directory implements), a `RecordingNotifier`, a
  `FakeExclusion`, `FakeTimeProvider`, and the isolated schema for tasks and sessions.
- The hosted-service test runs the real `RunnerAlarmHostedService` over the fake source with a
  `FakeTimeProvider`, `StartAsync`, then `clock.Advance(...)` and `UntilAsync` polls on the
  published state (the `C699_*` tests' `RecoveryWorker` / `RecoveryClock` shape in
  `PostLandMutationDeliveryTests.CompletionRecovery.cs:46-80`).
- The notifier test builds the real `SessionMessageQueueService` the way
  `PhoneHomeOutageMentionTests` or `LandingSafetyHarness` does (TestDesign picks one) with one
  live `AgentSession` row and asserts the queue row, not delivery.
- The attention test builds `AttentionService` with the six positional arguments plus
  `alarms: state` and asserts only rows of the two new kinds.
- Red first: `S1-tests` compiles against the interfaces and the optional parameters with the
  directory and lease not yet calling them; `S2-tests` against an inspector that returns an empty
  inspection; `S3-tests` against a coordinator whose methods return without evaluating and a
  hosted service that waits forever; `S4-tests` against the enum members with builders that
  return `[]`. A red row must fail at the roster's assertion (a missing observer call, a missing
  finding, an unraised episode, no row of the kind), never at compile or fixture time. Nothing
  contacts server2's production runner, 17202 to 17205, a provider, or the desktop.

### Coverage roster and decisive assertions

| ID | Slice | Class.method | Decisive assertion (red on the skeleton, green after the slice) |
|---|---|---|---|
| V-1 | S1 | `AlarmSettingsValidatorTests.defaults_validate_and_nonpositive_values_are_named` | Defaults pass; `RunnerGraceSeconds = 0`, `SweepMinutes = 0`, `JournalStaleMinutes = -1` each produce one failure naming `Alarms:<Key>`. Red: the validator returns Success for everything. |
| V-2 | S1 | `RunnerEligibilityObserverTests.disconnect_recovery_and_supersede_notify_the_observer_with_the_runner_id` | Two runners `a`, `b`. `ConnectPeerAsync(a)` then `MarkRecovered(liveA)`: recorder has `[a]`. `peerA.Socket.Abort()` and wait for `Status(a).Available == false`: recorder ends with `a` again and never contains `b`. Reconnect `a` while the first is still live (supersede): one more `a`. Red: recorder empty. |
| V-3 | S1 | `RunnerEligibilityObserverTests.lease_expiry_seen_by_a_snapshot_notifies_once` | Recovered `a`; advance the fake clock 91 s with no heartbeat; `SnapshotLive(a)` twice: recorder has exactly one new `a` and `Status(a).DispatchEligible == false`. Red: no notification. |
| V-4 | S1 | `RepositoryFenceObserverTests.a_fenced_acquire_and_a_describe_name_the_common_directory_once_each` | Plant one dead record; `TryAcquireAsync` returns null and the recorder has `[common]`; `DescribeUnavailableAsync` returns the script text and the recorder has `[common, common]`; delete the record: `TryAcquireAsync` succeeds and the recorder is unchanged. Red: recorder empty. |
| V-5 | S2 | `RepositoryChildJournalInspectorTests.a_live_record_is_alive_and_never_stale` | Record with the test process's PID and start ticks, write time 1 h ago, threshold 5 min: one finding, `State == Alive`, `Stale == false`. Green throughout (control). |
| V-6 | S2 | `RepositoryChildJournalInspectorTests.dead_completed_and_malformed_records_past_the_threshold_are_stale` | Three files: exited child's record, `Completed` record, `not-a-journal.txt`; all written 10 min ago; threshold 5 min: three findings with `Dead`, `Completed`, `Malformed`, all `Stale`; `StaleCount == 3`. Red: empty inspection. |
| V-7 | S2 | `RepositoryChildJournalInspectorTests.a_young_dead_record_and_an_empty_directory_raise_nothing` | Exited child's record written now: `Dead` and `Stale == false`; no `children` directory: zero findings; `landing.lock` is not created by the inspection. |
| V-8 | S3 | `RunnerAlarmCoordinatorTests.a_runner_down_past_the_grace_raises_once_with_counts_and_one_note_per_caller` | Runner `server2` ineligible (reason `transport_abort`); two open tasks pinned to it with parents P1 and P2, a third open task with parent P1, a Succeeded task pinned to it, an open task on the desktop; one live session on the runner. `EvaluateRunnersAsync` at t0: episode open, unraised, notifier empty. At t0+179 s: still unraised. At t0+180 s: `RaisedAt == now`, `PinnedOpenTasks == 3`, `LiveSessions == 1`, notifier has exactly two notes (P1, P2) with header `[runner server2 unavailable]` and bodies naming `transport_abort` and the pinned short ids; `NotifiedSessionIds == {P1, P2}`. A fourth evaluation at t0+240 s adds no note. Red: never raised, no note. |
| V-9 | S3 | `RunnerAlarmCoordinatorTests.a_flap_inside_the_grace_leaves_no_trace` | Ineligible at t0, eligible at t0+60 s: no episode, no note, no log entry above Debug for that runner (captured logger). Green throughout (control). |
| V-10 | S3 | `RunnerAlarmCoordinatorTests.recovery_resolves_and_tells_only_the_notified_callers` | After V-8's raise, a new open task with parent P3 is added; the runner becomes eligible: episode gone from the snapshot, notifier has recovery notes for P1 and P2 only, header `[runner server2 recovered]`, body naming the downtime. Red: episode still present, no recovery note. |
| V-11 | S3 | `RunnerAlarmCoordinatorTests.a_draining_or_retired_runner_never_raises_and_a_disabled_entry_is_skipped` | `FakeExclusion` returns `"draining"` for `server2`: at t0+600 s still no episode and no note; a runner whose snapshot has `Enabled == false` is never evaluated. Then the exclusion returns null while still ineligible: an episode opens with `DownSince == now`. Red: raised despite the exclusion. |
| V-12 | S3 | `RunnerAlarmCoordinatorTests.startup_opens_an_episode_per_enabled_remote_runner_and_a_normal_reconnect_closes_it` | Two remote runners ineligible at start: two open episodes; runner `a` eligible at +40 s: `a` closed silently; `b` raised at +180 s with its own note set. |
| V-13 | S3 | `RunnerAlarmCoordinatorTests.journal_findings_are_published_per_repository_and_cleared_when_recovered` | Two `ScratchGitRepo`s registered as projects; one stale dead record in the first: `EvaluateJournalsAsync(null)` publishes one `JournalFinding` keyed on the first repository's common directory with `StaleCount == 1`; delete the record and evaluate again: no finding. A `Project` path that does not exist is skipped without throwing. Red: no finding. |
| V-14 | S3 | `RunnerAlarmHostedServiceTests.the_loop_raises_on_the_grace_timer_and_wakes_on_a_fence_signal` | Real hosted service, fake source (ineligible), `FakeTimeProvider`: start; `Advance(179 s)`: state has no raised episode; `Advance(1 s)`: `UntilAsync` sees `RaisedAt` set without any further advance. Then `AlarmWakeQueue.Fenced(common)` for a repository with a stale record: `UntilAsync` sees the finding without advancing the clock. Stop cleanly. Red: nothing raised. |
| V-15 | S3 | `RunnerAlarmHostedServiceTests.the_sweep_runs_at_the_period_and_no_other_timer_exists` | Fake source eligible, no fences: after `Advance(14 min)` the source's `Snapshot` call count equals the startup pass; after `Advance(1 min)` it increments by one runner pass and one journal sweep; between, the fake clock has at most one pending timer (the `RecoveryClock.TimerCreated` shape). |
| V-16 | S3 | `RunnerAlarmNotifierTests.a_note_is_a_pending_whenidle_system_row_with_the_header` | One live session; `NotifyAsync(session, header, body)`: exactly one `SessionQueuedMessage` for the session, `Origin == System`, `Status == Pending`, body starts with the header line, and `CompletionNoteFlushQueue` received the session id. |
| V-17 | S4 | `RunnerAlarmAttentionTests.a_raised_episode_is_one_error_row_and_an_unraised_one_is_none` | State with a raised episode for `server2` (`PinnedOpenTasks 3`, `LiveSessions 1`, reason `transport_abort`): exactly one item of `Kind == RunnerUnavailable`, `Severity == Error`, `ConditionKey == "runner-unavailable:server2"`, headline containing `3 open task(s)`, evidence containing `transport_abort`, `SinceUtc == DownSince`, `Actions == [OpenDrawer]`. State with only an unraised episode: no item of the kind. Red: no row. |
| V-18 | S4 | `RunnerAlarmAttentionTests.a_journal_finding_is_one_error_row_naming_the_recovery_command` | One finding with two stale records: one item, `Kind == RepositoryChildJournalStale`, `Severity == Error`, `ConditionKey == "journal-stale:" + key`, evidence containing `recover-repository-children.ps1 -Repository ` + the repository path and `-Execute -ConfirmDescendantsExited`, `SinceUtc` the oldest record's write time. Red: no row. |
| V-19 | S4 | `RunnerAlarmAttentionTests.no_state_means_no_rows_and_the_summary_counts_both_kinds_open` | Service built without `alarms`: no rows of either kind. With one of each: `AttentionSummaryDto.From(dto).Open` counts both. |
| V-20 | S4 | `client/src/features/attention/attentionVisuals.test.ts` (`maps every kind`, `lands every kind in a declared group`, key-list lockstep) | The union, the visuals map and the lockstep list all carry `RunnerUnavailable` and `RepositoryChildJournalStale`; `tsc` in the vitest run rejects a union member without a visuals entry. |
| R-1 | all | `MultiRunnerDirectoryTests`, `MultiRunnerRecoveryTests` | The observer seam changes no directory behaviour (15 source methods). |
| R-2 | all | `RepositoryMutationLeaseTests`, `RepositoryMutationLeaseDescribeTests` | The fence observer changes no lease behaviour and the inspector shares its classification (15 source methods, argument-expanded). |
| R-3 | all | `AttentionServiceTests` (all partials), `DispatchHeldAttentionTests` | The optional `alarms` parameter and two extra builders change no existing row (138 source methods plus 10, argument expansion may raise the count). |

### Execution and evidence

Builds go to `bin-c726-r1/` and `bin-c726-r2/` (forward slash); every row runs through
`scripts/run-checkpoint.ps1` on server2, which takes its own build slot and adds
`UseAppHost=false`; results under `.antiphon/c726-checkpoints`. Both `bin-c726-*` directories
are deleted before the Code report. Every row is reported as its `CHECKPOINT` line. Unlisted
runs need a stated reason.

#### Plan draft checkpoints (superseded by `## Test design` `### Checkpoints`)

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-tests | `tests/Antiphon.Tests -> bin-c726-r1/` | seams-red | `/*/*/(AlarmSettingsValidatorTests*)\|(RunnerEligibilityObserverTests*)\|(RepositoryFenceObserverTests*)/*` | V-1 to V-4 | 4 executed; V-1 fails at its failure-name assertion; V-2, V-3, V-4 fail at their recorder assertions | 4 | 7 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c726-r1/` | seams-green | `/*/*/(AlarmSettingsValidatorTests*)\|(RunnerEligibilityObserverTests*)\|(RepositoryFenceObserverTests*)/*` | V-1 to V-4 | all listed, 0 failed/skipped | 4 | 7 |
| CP-3 | S2-tests | `tests/Antiphon.Tests -> bin-c726-r1/` | inspector-red | `/*/*/RepositoryChildJournalInspectorTests/*` | V-5 to V-7 | 3 executed; V-6 fails at `StaleCount`; V-5 fails at the `Alive` finding; V-7 passes | 3 | 6 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c726-r1/` | inspector-green | `/*/*/RepositoryChildJournalInspectorTests/*` | V-5 to V-7 | all listed, 0 failed/skipped | 3 | 6 |
| CP-5 | S3-tests | `tests/Antiphon.Tests -> bin-c726-r1/` | coordinator-red | `/*/*/(RunnerAlarmCoordinatorTests*)\|(RunnerAlarmHostedServiceTests*)\|(RunnerAlarmNotifierTests*)/*` | V-8 to V-16 | 9 executed; V-9 passes (control); the other eight fail at their raise/note/finding/timer/row assertions (V-11 at its final open-episode assertion) | 9 | 9 |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c726-r1/` | coordinator-green | `/*/*/(RunnerAlarmCoordinatorTests*)\|(RunnerAlarmHostedServiceTests*)\|(RunnerAlarmNotifierTests*)/*` | V-8 to V-16 | all listed, 0 failed/skipped | 9 | 9 |
| CP-7 | S1-S3 | CP-6 | r1-regression | `/*/*/(MultiRunnerDirectoryTests*)\|(MultiRunnerRecoveryTests*)\|(RepositoryMutationLeaseTests*)\|(RepositoryMutationLeaseDescribeTests*)/*` | R-1, R-2 | >= 30 executed, 0 failed (7 + 8 + 14 + 1 source methods; `[Arguments]` expansion raises the count) | 30 | 10 |
| CP-8 | S4-tests | `tests/Antiphon.Tests -> bin-c726-r2/` | attention-red | `/*/*/RunnerAlarmAttentionTests/*` | V-17 to V-19 | 3 executed; V-17, V-18 fail (no row of the kind); V-19 passes | 3 | 6 |
| CP-9 | S4 | `tests/Antiphon.Tests -> bin-c726-r2/` | attention-green | `/*/*/RunnerAlarmAttentionTests/*` | V-17 to V-19 | all listed, 0 failed/skipped | 3 | 6 |
| CP-10 | S4 | n/a | client-visuals | `pwsh -File scripts/test-client.ps1 attentionVisuals.test` | V-20 | attentionVisuals.test.ts: all tests pass, `CLIENT TESTS EXIT CODE: 0` | n/a | 3 |
| CP-11 | all | CP-9 | attention-regression | `/*/*/(AttentionServiceTests*)\|(DispatchHeldAttentionTests*)/*` with `TUNIT_MAX_PARALLEL_TESTS=1` | R-3 | >= 148 executed, 0 failed (122 + 10 + 6 + 10 source methods; argument expansion may raise the count) | 148 | 24 |

CP-7 reuses CP-6's output with `--no-build` (same `After`); CP-11 reuses CP-9's. Round 1 is
CP-1 to CP-7; Round 2 is CP-8 to CP-11 and the S5 docs.

#### Plan draft cost (superseded by `## Test design` `### Cost`)

Ordinary Code floor: the sum of `EstimatedMinutes`, 93 minutes across the two rounds (R1 54,
R2 39), plus authoring. `-ExpectAbout` for the R1 Code dispatch: about 3 h; for R2: about 2 h.
The Code briefs point at this table with
`checkpoints: docs/superpowers/plans/2026-09-26-card-0726-runner-and-journal-alarms-plan.md@<sha> section "### Checkpoints"`.

## Risks and open points

- **CARD-0738 and this card both append to `AttentionKind`.** Whichever lands second renumbers
  its plan's placeholder; the tests assert names, not integers (D-6). A Review checks the client
  lockstep list carries every shipped member.
- **CARD-0727 R2 supplies the real `IRunnerAlarmExclusion`.** Until it lands, a rolling upgrade's
  redeploy of a runner raises an outage after 180 s if the runner is down that long; the operator
  running the upgrade sees a row they expect. V-11 pins the contract so 0727's implementation
  inherits it.
- **In-memory episodes across a desktop restart** (D-3): a runner still down 180 s after a restart
  is raised again and its callers told again. Accepted as correct; noted in `docs/ops-http.md`.
- **Journal age is file time.** A clock skew between the file system and the server clock moves
  the threshold by the skew; 5 minutes is far above any observed skew on the desktop, and the
  record's own JSON carries no timestamp to prefer.
- **`IsProcessAliveAsync` returns null for a process the server cannot read** (Windows access
  denied). Such a record classifies `Unknown` and raises past the threshold (D-8); the row says
  the script will retain it, which is the truthful state.
- **Nothing here retires `watch-server2.sh`.** It is the orchestrator's local file; the
  orchestration-loop doc's new sentence is the instruction not to re-arm it once the runner row
  exists, and the caller's standing authority covers that.

## Test design

TestDesign task `08e6f2d2`, server2 Linux lane, against plan commit `4fe3bce9` and source
`4fe3bce9` (no `server/`, `tests/` or `client/` file changed between the plan's inspected
`fa86dc64` and this commit except CARD-0718's `DelegationUnitTests.cs` and `ScratchGitRepo.cs`
additions, neither of which a row below relies on). Nothing was built or run by TestDesign; every
count below is a source count (argument expansion included) at `4fe3bce9`. The plan's IDs
V-1..V-20, R-1..R-3 and CP-1..CP-11 are kept with their meaning; rows V-21..V-34 and R-4..R-5 are
added for boundaries and delivery evidence the draft did not reach, and CP rows are renumbered in
the final table.

### Stated defaults (TestDesign, for Review)

The fix design is unchanged. Three behaviours the plan leaves ambiguous or unrecovered are pinned
here as defaults under the caller's standing authority, because a guard cannot be tested until its
behaviour is fixed:

- **TD-1 (D-7 enqueue order).** A caller session enters `NotifiedSessionIds` only after its
  `NotifyAsync` returned; a throwing `NotifyAsync` (missing session, queue insert failure) is
  logged at Warning and that session is retried on the next wake of the same raised episode. A
  session is never sent a second outage note once recorded. (The plan's "before the enqueue
  returns" would leave a failed caller permanently untold and would then send it a recovery note
  for an outage it never heard of.)
- **TD-2 (lost flush hint).** The CARD-0699 backstop sweep re-flushes only rows with a
  `SourceTaskId` and `ContentDigest` (`CompletionNoteWorkHostedService.cs:69-71`, `:156`), and
  `FlushStrandedQueuesAsync` picks up only always-on sessions or Delegation/Supervision/Mention
  origins (`QueueAttention.cs:85-90`). A `System` alarm note whose `TryEnqueue` was dropped, or
  whose flush threw, therefore waits for the caller's next turn end, which never comes for an
  idle caller. Default: the episode also records each note's queue row id (the `onCreated`
  callback of `EnqueueAsync`); every sweep and every wake that finds such a row still `Pending`
  with `DeliveryAttempts == 0` calls `CompletionNoteFlushQueue.TryEnqueue(session)` again. No new
  table, no new timer.
- **TD-3 (fences the inspector must not hide).** Every state in which
  `RepositoryChildJournal.HasUnfinishedAsync` returns true must surface as a finding: a
  `children` path that is a file (`:103`) is one `Malformed` finding named after the path; a
  record whose liveness read returns null is `Unknown`; a `.tmp` torn save is `Malformed`. D-8's
  classification governs where ground-truth row 7 differs: a wrong `CommonDirectory` is `Unknown`,
  not `Malformed`.

### Inspection

Bodies read by TestDesign at `4fe3bce9`, and what each changes in the roster:

- `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs:32-55, 270-345, 440-613`
  | the four transitions are `AcceptConnect` supersede (`:283-287`), `MarkRecovered` (`:300-311`),
  `Disconnect` (`:328-338`) and `SnapshotOf` (`:566-579`); `SnapshotOf` writes
  `DispatchEligible = false` on **every** read after expiry, so "notify once" needs an edge test
  (V-3 reads twice); `Status` returns eligibility as `live is { DispatchEligible: true } &&
  available` (`:495`) and the desktop alias is not a slot -> V-2, V-3, V-21, V-22, V-23.
- `tests/Antiphon.Tests/TestHelpers/PhoneHomeTestHost.cs:1-140, 150-175` | `StartAsync(clock,
  connectionString, limits, configureDbContext, shutdownTimeout, configured)` builds the directory
  inline with no observer: **MS-1** adds `IRunnerEligibilityObserver? observer = null` passed to
  the constructor; `Logs` (`CapturingLoggerProvider`) captures the Warning V-22 asserts;
  `ConnectPeerAsync(runnerId:, storeId:, secret:)` and `WaitLiveAsync(runnerId:)` are the peer
  verbs -> V-2, V-3, V-21..V-23, V-35.
- `tests/Antiphon.Tests/Application/MultiRunnerDirectoryTests.cs` (7 `[Test]`, `Pair(secretA,
  secretB)` configured map, `Socket.Abort()` then replacement at `:88-89`) and
  `DefaultRunnerEligibilityTests.cs` (3, `FakeTimeProvider` host, `Advance(91 s)` expires the lease
  at `:79`) | the supersede and lease-expiry recipes V-2 and V-3 copy; both classes join R-1.
- `server/Infrastructure/Git/RepositoryMutationLease.cs:20-80` | `AcquireAsync` returns null for
  two different reasons: the journal fence (`:33-37`) and a busy `landing.lock` (`IOException`,
  `:53`); only the first may call `Fenced` -> V-4 gains the busy-lock boundary (G-7).
- `server/Infrastructure/Git/RepositoryChildJournal.cs:1-140` and `LandingGit.cs:35-46` |
  `HasUnfinishedAsync` fences on a `children` file, any non-`.json` name, a torn or foreign record,
  an alive or unreadable PID, and a dead one; `IsProcessAliveAsync` is non-virtual, returns false
  for a missing PID **and** for a live PID with other start ticks -> V-6 plants a "reused" record
  from the test process's own PID with `StartTicks + 1` and a missing PID, so no child process is
  spawned; **MS-2** `UnreadableLivenessGit : LandingGit, ILandingGit` re-implements the interface
  with `public new Task<bool?> IsProcessAliveAsync(...) => Task.FromResult<bool?>(null)` for V-24.
- `tests/Antiphon.Tests/Infrastructure/RepositoryMutationLeaseTests.cs:505-570` and
  `RepositoryMutationLeaseDescribeTests.cs` | records are planted by serialising
  `RepositoryChildJournal.ChildRecord` into `<common>/antiphon/children/<guid>.json` (internal type,
  visible to the test assembly); the describe test is the one-class, 30 s-timeout shape the new
  journal classes copy, with `[ParallelLimiter<ProcessSpawnLimit>]` because git runs.
- `tests/Antiphon.Tests/TestHelpers/ScratchGitRepo.cs` | `Path`, `WorktreeRoot`, `GitAsync(...)`;
  a linked worktree for V-13 is `GitAsync("worktree", "add", <dir>)`.
- `server/Application/Services/SessionMessageQueueService.cs:340-368, 1325, 1366-1440, 1516` and
  `server/Infrastructure/Orchestration/CompletionNoteWorkHostedService.cs:16-168` | `EnqueueAsync`
  takes `onCreated` (row id) and `deliverIfIdle`; the flush worker calls `FlushIfIdleAsync`; the
  turn-end entry is `OnTurnEndAsync`; the sweep's `SourceTaskId`/`ContentDigest` filter is why
  TD-2 exists -> DP-1 in the delivery inventory, V-16, V-30..V-34.
- `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs:29-80, 260-300` and
  `PostLandMutationDeliveryTests.cs:905-960, 1104-1120` | the harness is the real queue over a
  `FakeAgentProtocolAdapter` whose `OnSubmitted` writes a stamped `UserPrompt` plus `TurnEnd`
  transcript row (CARD-0055), so a delivered note is observable as a transcript row; the
  busy-caller recipe is `SetWorkingAsync(connection, session, true)` then `FlushIfIdleAsync`
  (zero `Adapter.Inputs`) then TurnEnd + `OnTurnEndAsync` -> V-16, V-30..V-34 use it; the
  `RecoveryWorker(h, clock)` / `RecoveryClock` shape of `PostLandMutationDeliveryTests.CompletionRecovery.cs:234-263`
  is copied (it is `private` there: **MS-3** copies the two helpers into the new class).
- `server/Application/Services/AttentionService.cs:128-156, 225-235` and
  `DispatchHeldAttentionTests.cs:262-277` | six positional arguments plus named optionals;
  `BuildZombieCensusItemsAsync` is the insertion point; the new parameter goes last -> V-17..V-19.
- `client/src/features/attention/attentionVisuals.test.ts:1-280` (15 `it`, one 4-case `it.each`)
  | `ALL_KINDS` list, the per-kind visual test, the group test, the home-bucket `it.each`
  (`:90-98`) and the `unique == visualKeys` lockstep (`:266-268`) -> V-20 adds both kinds to
  `ALL_KINDS` and two `it.each` cases (`Error` -> `broken`).
- `server/Application/Services/SessionRunnerCatalogue.cs:78-96` | "live" sessions are
  `Created | Starting | Running | Stopping` on the runner -> V-8 seeds one `Running` and one
  `Stopped` session. `AgentTaskReplyTo` is `None = 0, Session = 1, ...` -> V-8 seeds a `None`
  task that is pinned but has no caller.

Missing setup (Code adds it in the `Sn-tests` commit that first needs it): **MS-1** the
`PhoneHomeTestHost` observer parameter; **MS-2** `UnreadableLivenessGit`; **MS-3** the recovery
worker/clock helpers copied into `RunnerAlarmDeliveryTests`; **MS-4** `FakeEligibilitySource`,
`RecordingNotifier` (optionally throwing once per session), `FakeExclusion` and a
`RecordingFenceObserver`/`RecordingEligibilityObserver`, all in
`tests/Antiphon.Tests/TestHelpers/RunnerAlarmFakes.cs`; **MS-5** `DroppingFlushQueue :
CompletionNoteFlushQueue` (`TryEnqueue` is `virtual`, `CompletionNoteWork.cs:13`) that drops the
first N hints and records every call.

Boundary combinations covered: grace at 179/180 s (V-8, V-14); journal age at 4:59 / 5:00 (V-7);
eligible x excluded x episode state, all seven D-3 table rows (V-8..V-12, V-27); drain before,
during and after an outage and the CARD-0727 drain -> redeploy -> clear sequence (V-11); lease
expiry read once and twice (V-3); fence vs busy lock (V-4); two worktrees of one repository
(V-13); idle, busy and failing recipients (V-30..V-34). Excluded: a runner removed from
configuration while an episode is open (configuration is bound at startup; a change restarts the
server and D-3's restart rule applies).

### Delivery inventory

Two paths deliver session input (DP-1, DP-2); three are in-process signals or read-time
projections whose "recipient" is the alarm state or the feed (DP-3..DP-5). The durable identity
for DP-1/DP-2 is the **queue row id** (`SessionQueuedMessage.Id`, returned through `onCreated`,
TD-2), joined to the episode `(RunnerId, DownSince)` and the caller `AgentSessionId`. The
recipient receipt is a **complete `UserPrompt` transcript row** on the caller session whose text
contains the note's header line and its whole body, with the row `Sent`; a queue row, a
`TryEnqueue` call, a `NotifiedSessionIds` entry or a `Sent` flag alone is never counted.

| Path | Producer | Destination | Persistence boundary | Recovery at each handoff | Observable receipt |
|---|---|---|---|---|---|
| DP-1 outage note | `RunnerAlarmCoordinator` raise (D-4) -> `QueueRunnerAlarmNotifier.NotifyAsync` | caller session composer, via `EnqueueAsync(System, WhenIdle, deliverIfIdle: false)` -> `CompletionNoteFlushQueue.TryEnqueue` -> flush worker `FlushIfIdleAsync`, or `OnTurnEndAsync` for a busy caller | `SessionQueuedMessages` row `Pending` (the only durable hop; the episode is memory, D-3) | H1 enqueue throws: session not recorded, retried next wake (TD-1; V-28, V-33). H2 row saved, hint dropped: re-hint by row id on the next wake/sweep (TD-2; V-32). H3 caller busy: held, typed at its turn end (V-31). H4 crash after the row, before the hint: the row survives; the restart re-raises after the grace and that note's flush drains every Pending row of the session (V-34). H5 crash before the enqueue: nothing durable; the restart re-opens the episode (D-3) and raises again (V-12 + V-30). | `UserPrompt` on the caller starting `[runner server2 unavailable]` with the pinned short ids and reason, exactly one per episode per caller (V-30, V-31, V-33); row `Origin == System`, `Status == Sent` |
| DP-2 recovery note | coordinator resolve (D-3 table rows 5 and 6) | as DP-1, only to `NotifiedSessionIds` | as DP-1 | as DP-1 H1..H3; H4/H5: a restart loses `NotifiedSessionIds`, so no recovery note follows a pre-restart outage note (accepted: the restart's own episode starts clean, D-3) | `UserPrompt` starting `[runner server2 recovered]` on P1 only, none on a caller never told (V-30, V-10) |
| DP-3 eligibility signal | `PhoneHomeRunnerDirectory` `Notify` at the four transitions (D-2) | `AlarmWakeQueue.Signal(runnerId)` -> hosted-service wake | none | a lost or late signal is harmless: every wake re-reads every runner; the 15-minute sweep and the startup pass re-read with no signal (V-15, V-12) | `RunnerAlarmState` episode for the runner (V-14, V-35) |
| DP-4 fence signal | `RepositoryMutationLease` fence refusal / describe (D-8) | `AlarmWakeQueue.Fenced(common)` -> one inspection on the next wake | none | the sweep and the startup pass inspect every repository (V-13, V-15) | `RunnerAlarmState` `JournalFinding` (V-14, V-36) |
| DP-5 feed projection | `AttentionService` builders at read time | `GET /api/attention` item | none (read-time, D-6) | next read | item of the kind (V-17..V-19) |

Producer-to-recipient through the real queue: `RunnerAlarmDeliveryTests` (V-30..V-34) runs the
real coordinator, the real `QueueRunnerAlarmNotifier`, the real `SessionMessageQueueService`, the
real `CompletionNoteFlushQueue` and the real `CompletionNoteWorkHostedService` flush workers over
`BridgeQueueHarness`, whose adapter writes the `UserPrompt` row only when the queue actually
submits. It covers **one already eligible** recipient (idle caller, V-30), **a busy recipient**
(V-31), **enqueue failure** at the database boundary (V-33), **a dropped hint** (V-32) and **a
crash between the durable row and the hint** (V-34).

Declared substitutes and what each cannot prove:

- `FakeAgentProtocolAdapter` for a real TUI: proves the queue submitted the whole body and that the
  transcript-confirmation path saw a `UserPrompt`; cannot prove a real Claude composer accepts the
  bracketed paste. The note uses the unchanged multi-line delivery path already covered by
  `SessionMessageQueuePtyIntegrationTests` (CARD-0055); nothing here alters it, so no new pty row.
- `RecordingNotifier` (coordinator tests V-8..V-12, V-27, V-28): proves which sessions, which
  header and when; cannot prove delivery. Closed by V-30..V-34.
- `FakeEligibilitySource` (coordinator and hosted-service tests): proves the state machine;
  cannot prove the directory's predicate or its observer calls. Closed by V-2, V-3, V-21..V-23 and
  the real-directory loop test V-35.
- Test-side `OnTurnEndAsync` call for the caller's turn end: proves the queue's turn-end flush
  types the held note; cannot prove the transcript ingester calls `OnTurnEndAsync` on a real
  TurnEnd, which is existing CARD-0055/0164 behaviour this card does not touch.
- Dead or stopped caller (the note is never typed): out of scope per D-7; no substitute claims it.

### Proves it works now

Each row: `V-n: behaviour | layer | test | expected`, then the **decisive assertions**, the
**red-first line** (the production change that turns the method red at that assertion; a compile
failure is never counted) and the checkpoint rows. File homes: `Application/` classes sit beside
`MultiRunnerDirectoryTests`, `Infrastructure/` beside `RepositoryMutationLeaseDescribeTests`.
Every class carries `[Category("Unit")]` or `[Category("Integration")]` (R-5); every class that
runs git or boots a process-spawning harness carries `[ParallelLimiter<ProcessSpawnLimit>]`.

**S1: settings and seams** (`CP-1` red, `CP-2` green)

- **V-1: `Alarms` defaults validate and every non-positive key is named | Unit |
  `tests/Antiphon.Tests/Application/AlarmSettingsValidatorTests.cs`
  `AlarmSettingsValidatorTests.defaults_validate_and_nonpositive_values_are_named` | green.**
  Asserts `new AlarmSettings()` is `Enabled`, `JournalEnabled`, `RunnerGraceSeconds == 180`,
  `SweepMinutes == 15`, `JournalStaleMinutes == 5` and `Validate(...).Succeeded`; each of
  `RunnerGraceSeconds = 0`, `SweepMinutes = 0`, `JournalStaleMinutes = -1` alone gives `Failed`
  with one failure containing `Alarms:<Key>`. Red: delete the `RunnerGraceSeconds <= 0` check ->
  fails at the `Alarms:RunnerGraceSeconds` failure assertion.
- **V-2: the directory tells the observer on recovery, supersede and a live disconnect, and only
  then | Integration, real directory over `PhoneHomeTestHost` (MS-1) |
  `tests/Antiphon.Tests/Application/RunnerEligibilityObserverTests.cs`
  `RunnerEligibilityObserverTests.disconnect_recovery_and_supersede_notify_the_observer_with_the_runner_id`
  | recorder sequence exactly as below.** Runners `runner-a`, `runner-b` (`Pair` as in
  `MultiRunnerDirectoryTests`). First `ConnectPeerAsync(a)`: recorder `[]` (no supersede, no
  flip). `MarkRecovered(liveA)`: `[a]`; `MarkRecovered(liveA)` again: still `[a]` (no flip, no
  call). Connect a replacement for `a` while the first is live: `[a, a]`. `MarkRecovered(replacement)`:
  `[a, a, a]`. `replacement.Socket.Abort()` and wait `Status(a).Available == false`: `[a, a, a, a]`,
  and the superseded first connection's own route end adds nothing (it is not the slot's live
  connection). `b` never appears. Red: remove the `Notify` after `MarkRecovered` -> fails at
  `[a]`; after the supersede -> at `[a, a]`; after `Disconnect` -> at the fourth `a`; notify on a
  non-flipping `MarkRecovered` -> at the repeated-call assertion.
- **V-3: lease expiry seen by a read notifies once | Integration, `FakeTimeProvider` host (the
  `DefaultRunnerEligibilityTests.cs:66-87` recipe) |
  `RunnerEligibilityObserverTests.lease_expiry_seen_by_a_snapshot_notifies_once` | one new call.**
  Recovered `a` (recorder `[a]`); `clock.Advance(91 s)`; `SnapshotLive(a)` twice and `Status(a)`
  once: recorder `[a, a]` exactly and `Status(a).DispatchEligible == false`. Red: no `Notify` in
  `SnapshotOf` -> fails at count 2; notify on every expired read instead of the true->false edge
  -> fails at count 2 with 4.
- **V-21: the directory's snapshot source reports one row per configured remote with the dispatch
  predicate | Integration, `FakeTimeProvider` host |
  `RunnerEligibilityObserverTests.snapshots_carry_one_row_per_configured_remote_with_the_dispatch_predicate`
  | rows as below.** Configured `a`, `b` enabled and `c` with `Enabled = false`.
  `((IRunnerEligibilitySnapshotSource)host.Directory).Snapshots()` ids are exactly `{a, b, c}` (no
  `desktop`), `c.Enabled == false`; `a` connected but not recovered: `Eligible == false`;
  recovered: `Eligible == true`, `Reconnects == 1`; `Advance(91 s)`: `Eligible == false`,
  `DisconnectReason == "lease_expired"`. Red: derive `Eligible` from `Status.Available` instead of
  `Status.DispatchEligible` -> fails at the connected-not-recovered assertion.
- **V-22: a throwing observer never reaches the connect route or the pump | Integration |
  `RunnerEligibilityObserverTests.a_throwing_observer_is_contained_and_logged` | no throw, one
  Warning.** Observer throws on every call. `MarkRecovered(liveA)` and then
  `Disconnect(liveA, "socket_closed")` do not throw (`Should.NotThrow`); after the first,
  `Status(a).DispatchEligible == true`; after the second, `Status(a).Available == false`;
  `host.Logs` holds Warning entries naming `runner-a` for each. Red: remove the observer
  `try/catch` in `Notify` -> fails at the first `Should.NotThrow`.
- **V-23: the observer runs outside the directory gate | Integration |
  `RunnerEligibilityObserverTests.the_observer_runs_outside_the_directory_gate` | every
  cross-thread read completes.** The observer's `Changed` runs `Task.Run(() =>
  directory.Status(id)).Wait(TimeSpan.FromSeconds(2))` and records the result. After
  `MarkRecovered(liveA)` and an abort: recorded results `[true, true]`. Red: move the `Notify`
  call inside `lock (_gate)` in `MarkRecovered` -> the other thread blocks on `_gate` and the
  first recorded result is `false`.
- **V-4: the lease names the common directory on a journal fence only | Integration, real
  `LandingGit` over `ScratchGitRepo` | `tests/Antiphon.Tests/Infrastructure/RepositoryFenceObserverTests.cs`
  `RepositoryFenceObserverTests.a_fenced_acquire_and_a_describe_name_the_common_directory_once_each`
  | recorder as below.** `new RepositoryMutationLease(git, fences: recorder)`. Busy lock first:
  hold one lease, a second `TryAcquireAsync` returns null, recorder `[]`; release. Plant a dead
  record (own PID, `StartTicks + 1`): `TryAcquireAsync` null and recorder `[common]`
  (`LandingGit.PathsEqual`); `DescribeUnavailableAsync` non-null and recorder `[common, common]`;
  delete the record: `TryAcquireAsync` succeeds, `DescribeUnavailableAsync` is null, recorder still
  two entries. Red: drop the `Fenced` call in `AcquireAsync` -> fails at `[common]`; in
  `DescribeUnavailableAsync` -> at the second entry; call `Fenced` in the `IOException` branch ->
  at the busy-lock `[]`.

**S2: the journal inspector** (`CP-3` red, `CP-4` green;
`tests/Antiphon.Tests/Infrastructure/RepositoryChildJournalInspectorTests.cs`, real `LandingGit`,
records planted as `RepositoryMutationLeaseTests.cs:515-523`, ages set with
`File.SetLastWriteTimeUtc`, `now` passed explicitly)

- **V-5: a live record is `Alive` and never stale | Integration |
  `RepositoryChildJournalInspectorTests.a_live_record_is_alive_and_never_stale` | one finding.**
  Record with the test process's PID and start ticks, written 1 h before `now`, threshold 5 min:
  `Findings.Single()` has `State == Alive`, `Stale == false`, `ProcessId == Environment.ProcessId`;
  `StaleCount == 0`. Red: mark a record stale on age alone (drop the `State != Alive` term) ->
  fails at `Stale == false`.
- **V-6: every non-live record past the threshold is stale and named | Integration |
  `RepositoryChildJournalInspectorTests.dead_completed_unknown_and_malformed_records_past_the_threshold_are_stale`
  | seven findings.** Seven files, all 10 min old: reused (own PID, `StartTicks + 1`) and missing
  PID (`int.MaxValue`) -> `Dead`, `Dead`; `Completed` with null `StartTicks` -> `Completed`;
  start intent (`ProcessId` and `StartTicks` null) and foreign `CommonDirectory` -> `Unknown`,
  `Unknown`; `invalid json` in a `.json` and a `<guid>.json.tmp` -> `Malformed`, `Malformed`. The
  state multiset is exactly that, every `Stale == true`, `StaleCount == 7`; afterwards all seven
  files still exist and `<common>/antiphon/landing.lock` does not. Red: classify `false` liveness
  as `Unknown` -> multiset; map a torn file to `Unknown` -> multiset; delete a classified file ->
  the file-count assertion; open `landing.lock` -> the last assertion.
- **V-7: the age threshold is inclusive and an absent journal raises nothing | Integration |
  `RepositoryChildJournalInspectorTests.age_threshold_is_inclusive_and_an_absent_journal_raises_nothing`
  | as below.** One dead record: inspected at write time + 4:59 -> `Dead`, `Stale == false`; at
  + 5:00 -> `Stale == true`. A repository with no `antiphon/children`: zero findings, and neither
  `children` nor `landing.lock` is created. Red: `>` for `>=` in the age comparison -> fails at
  the 5:00 assertion.
- **V-24: an unreadable process is `Unknown` and stale (TD-3) | Integration, MS-2 |
  `RepositoryChildJournalInspectorTests.a_record_whose_process_cannot_be_read_is_unknown_and_stale`
  | one `Unknown` finding.** Inspector over `UnreadableLivenessGit`; the test process's own PID and
  ticks, 10 min old: `State == Unknown`, `Stale == true`. Red: treat `null` liveness as alive
  (`!= false`) -> `Alive`, `Stale == false`.
- **V-25: a `children` path that is a file is one `Malformed` finding (TD-3) | Integration |
  `RepositoryChildJournalInspectorTests.a_children_path_that_is_a_file_is_one_malformed_finding` |
  one finding.** `antiphon/children` created as a file 10 min old: the lease's
  `DescribeUnavailableAsync` is non-null (control: it fences), and the inspection has exactly one
  finding, `Malformed`, `Stale`, whose `File` is that path. Red: return an empty inspection when
  `children` is not a directory -> fails at the finding count.
