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
| 1 | "A runner drops or reports not dispatch-eligible" is one observable state. | `PhoneHomeRunnerDirectory` (`server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs`) keeps one `RunnerSlot` per configured entry (`:50-53`, from `PhoneHomeRunnerCatalog.Configured`, which includes `Enabled: false` entries, `PhoneHomeRunnerSettings.cs:176-190`). Eligibility is `live is { DispatchEligible: true, SocketOpen: true } && !live.IsLeaseExpired(LeaseSeconds)` (`DescribeAsync` `:541-543`; `Status` `:471-472`). It flips in four places: `AcceptConnect` (a superseded connection, `:296-300`), `MarkRecovered` (true, `:314-324`), `Disconnect` (false, `:329-339`, called once from the connect route after every classified end, `SessionRunnerEndpoints.cs:198`), and `SnapshotOf` (false when the lease expired, evaluated lazily on read, `:566-579`). Nothing observes these transitions; the only readers are the recovery pump (every 50 ms, `PhoneHomeRecoveryPump.cs:71`, `:98`), the catalogue and the status route. Live fleet: `desktop` (windows) and `server2` (linux), both eligible at 2026-09-26T02:04Z; runner defaults are revision 1 with no global or kind default. | D-2: the directory gets a minimal observer (`IRunnerEligibilityObserver.Changed(runnerId)`) called at those four points, under nothing but the existing `_gate`; the coordinator re-reads `Status(runnerId)` on each signal rather than trusting the event payload. Lease expiry is caught by the pump's 50 ms `SnapshotLive` (it already runs; no new poll) and, failing that, by the sweep. Disabled entries are skipped. |
| 2 | Every force-kill restart dropped server2 (CARD-0716) and "nobody was told". | After a desktop restart every remote slot starts with `Live == null` and `LastRecovered == null` (`RemoteInventoryPending` `:449-456`); the runner reconnects with a 1 s to 15 s backoff (`src/Antiphon.SessionRunner/PhoneHomeSettings.cs:82-83`), then the pump's catch-up List marks it recovered (`PhoneHomeRecoveryPump.cs:129-141`). CARD-0716 has landed the graceful stop (`SessionRunnerEndpoints.cs:139-160`, reason `request_aborted`). `Status` names the reason (`transport_abort`, `request_aborted`, `lease_expired`, `socket_closed`, `superseded`, ...) and `LastDisconnectAtUtc` (`:481-500`). | D-3: startup opens an episode for every enabled remote runner with `DownSince = now`; a normal restart recovers inside the 180 s grace and nothing is raised. The row's "last error" is `Status.DisconnectReason`. |
| 3 | The attention feed is where a raised alarm lives, and it can hold a runner-scoped row. | `AttentionService.GetAsync` (`server/Application/Services/AttentionService.cs:157-262`) is a read-time projection; a row needs no task, session or agent (`ZombieCensusReport` rows pass six nulls, `AttentionService.Leaks.cs:171`; `InboundUnconsumed` has "no required agent"). Singleton state is projected through an optional constructor parameter (`ZombieCensusState? censusState = null`, `:141`, `:153`, published by the census job). `ConditionKey` is the client's identity for a row (`dispatch-held:{id:N}`, `zombie-census:{class}`). `AttentionKind` is append-only; highest shipped member is `ZombieCensusReport = 47` (`AttentionDtos.cs:311`); the CARD-0738 plan (not landed) reserves `48 CardClosedWhileWorking`. The client maps every member (`client/src/api/attention.ts:17`, `attentionVisuals.ts:43`, lockstep test `attentionVisuals.test.ts:100`, `:118`, `:267`). | D-6: `RunnerAlarmState` is the singleton the feed reads through a new optional parameter; `RunnerUnavailable = 49` and `RepositoryChildJournalStale = 50` (Code assigns the next free numbers after whatever has shipped; the plan's numbers are placeholders that skip 0738's 48). Two `ConditionKey`s: `runner-unavailable:{runnerId}` and `journal-stale:{commonDirectoryKey}`. |
| 4 | "Send a WhenIdle note to each caller session" has an existing path. | `SessionMessageQueueService.EnqueueAsync(sessionId, body, MessageSendMode.WhenIdle, ct, origin, conversationKey, sourceTaskId, contentDigest, noteHeader, ..., deliverIfIdle)` (`SessionMessageQueueService.cs:344-368`). The Delegation-origin digest dedupe (`:592-625`) applies only to `Origin == Delegation` with a `SourceTaskId`; `System` origin ("injected by Antiphon itself: bootstrap/restart/compaction-recovery notes", `QueuedMessageOrigin.cs:17`) delivers one per turn and has no dedupe. CARD-0699's recovery enqueues with `deliverIfIdle: false` and then `CompletionNoteFlushQueue.TryEnqueue(session)` so delivery runs on the flush workers, not the caller's thread (`CompletionNoteWorkHostedService.cs:127-132`; `CompletionNoteWork.cs:13`). The `CallerNoteUndelivered` row watches Delegation and Check notes only (`AttentionDtos.cs:158-164`). | D-7: origin `System`, header `[runner <id> unavailable]` / `[runner <id> recovered]` / `[repository journal stale]`, `deliverIfIdle: false` plus `TryEnqueue`; the coordinator's episode holds the notified session ids so the recovery note goes to exactly those sessions and a note is never sent twice for one episode. A Pending note on a dead caller is simply never typed; it is not this card's job to watch it. |
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
  `Task EvaluateJournalsAsync(repositories, now, ct)`. It takes `ISessionRunnerDirectory`,
  `PhoneHomeRunnerDirectory` (for `Status`, `RemoteRunnerIds` and the enabled flag through a new
  `IsEnabled(runnerId)` accessor), `IRunnerAlarmExclusion` (D-5), `RunnerAlarmState`,
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
`docs/orchestration-loop.md:1218`) is not what the card asks for and needs its own card, with the
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
