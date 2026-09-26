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
  `JournalFinding(Repository, CommonDirectory, Records: [(File, State, AgeAtInspection, ProcessId?, WrittenAt)], InspectedAt)`.
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

R1 review (task `9f69ac66`) pins two points this table leaves open. A caller whose task is pinned
after the raise gets one outage note on that same episode; "nothing else" on the raised row is the
feed row, which refreshes the counts and the last reason and does not tell a caller already in
`NotifiedSessionIds` a second time. `JournalFinding.Records` publishes each inspected record's
file, state, age, process id and write time. A record that disappears between the directory read
and its timestamp is omitted: `GetLastWriteTimeUtc` returns year 1601, and publishing that age
would be a false stale Dead finding until the next inspection.

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

The fix design is unchanged. Five points the plan leaves ambiguous, unrecovered or untestable are pinned
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
- **TD-4 (test seam).** `PhoneHomeRunnerDirectory` exposes `internal IRunnerEligibilityObserver?
  Observer => _observer;` so V-36 can prove `Program` wired it; nothing else reads it.
- **TD-5 (state shape).** `RunnerAlarmState` publishes a `JournalFinding` for every inspected
  repository that has at least one record (any state), with `StaleCount`; the feed projects only
  findings with `StaleCount >= 1` (V-13, V-18, V-36).

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

**S3: coordinator** (`CP-5` red, `CP-6` green;
`tests/Antiphon.Tests/Application/RunnerAlarmCoordinatorTests.cs`, `[Category("Integration")]`,
`[ParallelLimiter<ProcessSpawnLimit>]` because V-13 runs git; each method takes its own
`TestDbFixture.CreateIsolatedSchemaAsync()` and seeds rows directly; MS-4 fakes; one
`FakeTimeProvider` whose `t0` is the first evaluation; the coordinator is built per evaluation in
a fresh scope over one shared `RunnerAlarmState`, as the hosted service does)

Shared seed for V-8, V-10, V-28 (`SeedServer2Async`): runner `server2` ineligible, reason
`transport_abort`. Tasks pinned to `server2`: T1 `Working` parent P1, T2 `Queued` parent P2, T3
`Blocked` parent P1, T7 `Dispatched` parent P5 with `ReplyTo = None` (all `ReplyTo = Session`
unless named); T4 `Succeeded` parent P3; T6 `Working`, role `Check`, parent P4. T5 `Working` parent
P3 on the desktop (`RunnerId` null). Sessions on `server2`: one `Running`, one `Stopped`.

- **V-8: an outage past the grace raises once, with counts, and one note per caller | Integration
  | `RunnerAlarmCoordinatorTests.a_runner_down_past_the_grace_raises_once_with_counts_and_one_note_per_caller`
  | as below.** `t0`: one episode, `RaisedAt == null`, notifier empty. `t0+179 s`: still unraised.
  `t0+180 s`: `RaisedAt == t0+180`, `PinnedOpenTasks == 4` (T1, T2, T3, T7), `LiveSessions == 1`,
  `LastReason == "transport_abort"`; notifier has exactly two notes, sessions `{P1, P2}`, header
  `[runner server2 unavailable]`; P1's body names T1's and T3's short ids and not T2's; P2's names
  T2's; `NotifiedSessionIds == {P1, P2}`; one Warning log naming `server2`. Add T8 (`Working`,
  pinned, parent P1) and evaluate at `t0+240 s`: `PinnedOpenTasks == 5`, notifier still two notes.
  Red: `>` for `>=` against the grace -> fails at `RaisedAt` at `t0+180`; drop `Blocked` from the
  open set -> `PinnedOpenTasks`; drop the `NotSpecialist` filter -> P4 in the session set; drop the
  `ReplyTo == Session` caller filter -> P5 in the session set; drop the once-per-episode check ->
  note count at `t0+240`.
- **V-9: a flap inside the grace leaves no trace | Integration |
  `RunnerAlarmCoordinatorTests.a_flap_inside_the_grace_leaves_no_trace` | nothing.** Ineligible at
  `t0`, eligible at `t0+60 s`: no episode, notifier empty, no log entry above Debug naming the
  runner. Green on the skeleton (control). Red: route the unraised close through the resolve branch
  -> fails at notifier empty.
- **V-10: recovery resolves and tells only the notified callers | Integration |
  `RunnerAlarmCoordinatorTests.recovery_resolves_and_tells_only_the_notified_callers` | two
  recovery notes.** V-8's seed raised at `t0+180`; add T9 (`Queued`, pinned, parent P3); eligible at
  `t0+444 s`: no episode; notifier's new notes go to exactly `{P1, P2}` with header
  `[runner server2 recovered]` and bodies containing `after 7.4 min`; one Information log line.
  Red: send recovery notes to the current callers instead of `NotifiedSessionIds` -> P3 in the
  set; keep the episode after resolve -> fails at no episode.
- **V-11: a draining, retired or disabled runner never alarms (CARD-0727 contract) | Integration |
  `RunnerAlarmCoordinatorTests.a_draining_or_retired_runner_never_raises_and_a_disabled_entry_is_skipped`
  | as below.** (m1) `FakeExclusion` returns `"draining"` for `server2`, ineligible from `t0`:
  evaluations at `t0`, `t0+180`, `t0+600`: no episode, no note. (m2) runner `off` with
  `Enabled == false`, ineligible: never an episode. (m3) the exclusion clears at `t0+700` while
  still ineligible: an episode opens with `DownSince == t0+700`, unraised at `t0+879`, raised at
  `t0+880`. (m4) the CARD-0727 rolling-upgrade sequence on runner `r2`: eligible and `"draining"`,
  then ineligible and `"draining"` for 10 min, then eligible and `"draining"`, then eligible and
  cleared: no episode and no note at any evaluation. (m5) runner `r3` raised at its grace with one
  notified caller P6, then `"retired"` while still ineligible: the episode is gone the same
  evaluation and P6 gets exactly one `[runner r3 recovered]` note (D-3 table row 6). Red: consult
  the exclusion only when opening an episode -> fails at m5's "episode gone"; close an excluded
  raised episode without its recovery note -> fails at P6's note; never consult it -> fails at
  m1; do not skip disabled entries -> fails at m2.
- **V-12: startup opens an episode per enabled remote runner and a normal reconnect closes it
  silently | Integration |
  `RunnerAlarmCoordinatorTests.startup_opens_an_episode_per_enabled_remote_runner_and_a_normal_reconnect_closes_it`
  | as below.** A fresh state; runners `a` and `b` ineligible at the first evaluation `t0`: two
  episodes with `DownSince == t0`. `a` eligible at `t0+40`: `a`'s episode gone, no note, no log
  above Debug. `b` raised at `t0+180`, notes only to `b`'s callers (a task pinned to `a` with
  parent Pa: Pa gets nothing). Red: open episodes only on an eligible-to-ineligible transition
  seen by this process -> fails at two episodes.
- **V-27: `DownSince` honours a disconnect later than the last resolution | Integration |
  `RunnerAlarmCoordinatorTests.down_since_uses_the_last_disconnect_after_the_last_resolution` |
  as below.** Eligible at `t0` (no episode); then ineligible with `LastDisconnectAtUtc == t0+10`,
  evaluated at `t0+100`: `DownSince == t0+10`, unraised at `t0+189`, raised at `t0+190`. Resolve at
  `t0+300`; the source keeps reporting `LastDisconnectAtUtc == t0+10` and goes ineligible again,
  evaluated at `t0+400`: `DownSince == t0+400`. Red: always `now` -> fails at `DownSince == t0+10`;
  always `LastDisconnectAtUtc` -> fails at `DownSince == t0+400`.
- **V-28: a failed note is retried on the next wake and never sent twice (TD-1) | Integration |
  `RunnerAlarmCoordinatorTests.a_failed_note_is_retried_on_the_next_wake_and_never_sent_twice` |
  as below.** V-8's seed; `RecordingNotifier` throws once for P2. At `t0+180`: attempts to P1 and
  P2, `NotifiedSessionIds == {P1}`, one Warning naming P2. At `t0+181`: one more note, to P2;
  `NotifiedSessionIds == {P1, P2}`. At `t0+182`: no attempt. P1 has exactly one note throughout.
  Red: record the session before `NotifyAsync` returns (the plan's literal D-7 wording) -> fails
  at `NotifiedSessionIds == {P1}`.
- **V-13: journal findings are keyed on the common directory and cleared on recovery |
  Integration, `ScratchGitRepo` x2 |
  `RunnerAlarmCoordinatorTests.journal_findings_are_published_per_repository_and_cleared_when_recovered`
  | as below.** Projects over repo1 and repo2 plus one whose path does not exist; an open task whose
  `RepoPath` is a linked worktree of repo1. One dead record 10 min old in repo1:
  `EvaluateJournalsAsync(null)` publishes exactly one `JournalFinding`, `CommonDirectory` equal to
  repo1's (`LandingGit.PathsEqual`), `StaleCount == 1`; the missing path raises no exception and
  logs one Debug line. Delete the record and evaluate: no finding. Replant it and evaluate with
  `JournalEnabled = false`: no finding and no inspection (inspector call count unchanged). Red:
  key repositories on their checkout path instead of `CommonDirectoryAsync` -> two findings; let
  the missing path throw -> the evaluation throws; ignore `JournalEnabled` -> finding present.

**S3: the loop** (`CP-5` red, `CP-6` green;
`tests/Antiphon.Tests/Application/RunnerAlarmHostedServiceTests.cs`, `[Category("Integration")]`,
`[ParallelLimiter<ProcessSpawnLimit>]`; the real `RunnerAlarmHostedService` and `AlarmWakeQueue`
over a service provider with the isolated schema, `AlarmClock` = MS-3's `RecoveryClock` shape
recording every `CreateTimer` due time and period; `UntilAsync` polls the published state with a
5 s real-time bound; "settle" is a 100 ms real delay with no clock advance)

- **V-14: the grace expiry is a timer and a fence signal wakes the loop | Integration |
  `RunnerAlarmHostedServiceTests.the_loop_raises_on_the_grace_timer_and_wakes_on_a_fence_signal` |
  as below.** Source ineligible; start; await the first timer. `Advance(179 s)`, settle: an
  episode, `RaisedAt == null`. `Advance(1 s)`: `UntilAsync` sees `RaisedAt` set with no further
  advance. `AlarmWakeQueue.Fenced(common)` for a `ScratchGitRepo` holding a 10-minute-old dead
  record: `UntilAsync` sees its finding with no clock advance. `StopAsync` completes within 5 s.
  Red: compute the wait due time from `nextSweep` only -> fails at the first `UntilAsync`; drop
  `Fenced`'s wake -> fails at the second.
- **V-15: the sweep runs at the period and nothing polls | Integration |
  `RunnerAlarmHostedServiceTests.the_sweep_runs_at_the_period_and_no_other_timer_exists` | as
  below.** Source eligible, one registered repository, no signals. After start: source `Snapshots`
  calls `s0 >= 1`, inspector calls `i0 >= 1`. `Advance(14 min)`, settle: both unchanged.
  `Advance(1 min)`: `UntilAsync` source calls `== s0 + 1` and inspector calls `== i0 + 1`. Every
  recorded timer has period `Timeout.InfiniteTimeSpan` and a due time of at least 1 min. Red: add a
  1 s `Task.Delay` poll to the loop -> fails at the due-time assertion; a sweep period other than
  `SweepMinutes` -> fails at the 14-minute "unchanged" or the 15-minute `UntilAsync`.
- **V-26: `Alarms:Enabled = false` starts no loop and publishes nothing | Integration |
  `RunnerAlarmHostedServiceTests.disabled_alarms_start_no_loop_and_publish_nothing` | nothing.**
  Source ineligible; `Enabled = false`; start: `ExecuteTask` completes within 5 s, source calls
  `0`, no timer recorded; `Advance(10 min)`, settle: state empty. Green on the waiting skeleton
  (control). Red: ignore `Enabled` -> fails at source calls `0`.
- **V-29: a failing pass is logged and retried without waiting for the sweep | Integration |
  `RunnerAlarmHostedServiceTests.a_failing_pass_is_logged_and_retried_without_waiting_for_the_sweep`
  | as below.** Source eligible at start, then throws once on the next call and reports ineligible
  afterwards. `Signal("server2")`: one Warning is logged; `UntilAsync` an open episode with the
  clock advanced by at most 60 s; `ExecuteTask` is not faulted. Red: remove the loop's catch ->
  `ExecuteTask` faults and no episode opens.
- **V-35: the real directory drives the loop, and a drained runner never raises | Integration,
  `PhoneHomeTestHost` (MS-1) with the real `AlarmWakeQueue` as observer and the directory as the
  source | `RunnerAlarmHostedServiceTests.the_real_directory_drives_the_loop_and_a_drained_runner_never_raises`
  | as below.** `FakeTimeProvider` host, runners `a` and `b`, `FakeExclusion` `"draining"` for
  `b`. Both connected and recovered, loop started. Abort both sockets; `UntilAsync` an episode for
  `a` (signal-driven: no clock advance) and none for `b`. `Advance(180 s)`: `a` raised; `b` still
  none. Reconnect and recover `a`: `UntilAsync` no episode for `a`, no clock advance. Red: make
  `AlarmWakeQueue.Signal` record the id without waking the waiter -> fails at the first `UntilAsync`.

**S3: the notifier and the delivery path** (`CP-5` red, `CP-6` green; both classes over
`BridgeQueueHarness`, `[Category("Integration")]`, `[ParallelLimiter<ProcessSpawnLimit>]`, the
`PostLandMutationDeliveryTests` attributes; recipient evidence per the delivery inventory)

- **V-16: a note is a `Pending` `WhenIdle` `System` row, hinted, never typed inline | Integration |
  `tests/Antiphon.Tests/Application/RunnerAlarmNotifierTests.cs`
  `RunnerAlarmNotifierTests.a_note_is_a_pending_whenidle_system_row_hinted_and_never_typed_inline`
  | as below.** Idle live harness session; `new QueueRunnerAlarmNotifier(h.Queue, recordingFlush)`;
  `NotifyAsync(session, "[runner server2 unavailable]", body)`: exactly one row for the session,
  `Origin == System`, `Status == Pending`, `DeliveryAttempts == 0`, `NoteHeader` equal to the
  header, `Body` containing the body; `h.Adapter.SubmittedBodies` empty when the call returns;
  `recordingFlush.Calls == [session]`. `NotifyAsync` for an unknown session id throws and writes no
  row. Red: `deliverIfIdle: true` -> fails at `SubmittedBodies` empty; omit `TryEnqueue` -> fails
  at `Calls`; `QueuedMessageOrigin.Ui` -> fails at `Origin`.
- **V-30: an idle caller receives the outage and the recovery note as complete prompts |
  Integration, producer to recipient |
  `tests/Antiphon.Tests/Application/RunnerAlarmDeliveryTests.cs`
  `RunnerAlarmDeliveryTests.an_idle_caller_receives_the_outage_and_recovery_notes_as_complete_user_prompts`
  | two `UserPrompt` rows.** One task pinned to `server2` with parent = the harness session; real
  coordinator (fake source, fake clock), real notifier, the harness's `CompletionNoteFlushQueue`
  and the real `CompletionNoteWorkHostedService` flush workers (MS-3). Evaluate at `t0` and
  `t0+180`: `UntilAsync` (10 s) exactly one `UserPrompt` on the session whose text contains
  `[runner server2 unavailable]`, the task's short id and `transport_abort`; its queue row is
  `Sent`. Eligible at `t0+444`: exactly one more `UserPrompt` containing
  `[runner server2 recovered]` and `after 7.4 min`. Red: the notifier passes the header but not
  the body to `EnqueueAsync` -> fails at the short-id assertion.
- **V-31: a busy caller gets the note only after its turn ends | Integration |
  `RunnerAlarmDeliveryTests.a_busy_caller_gets_the_note_only_after_its_turn_ends` | held then
  typed.** `SetWorkingAsync(true)` before the raise; after the raise and `FlushIfIdleAsync`:
  `h.Adapter.Inputs` holds nothing of the note, the row is `Pending`, no `UserPrompt` contains the
  header. TurnEnd plus `h.Queue.OnTurnEndAsync(session)`: exactly one `UserPrompt` containing the
  header and body. Red: `MessageSendMode.Now` in the notifier -> fails at `Inputs` empty.
- **V-32: a dropped flush hint is re-hinted from the durable row (TD-2) | Integration, MS-5 |
  `RunnerAlarmDeliveryTests.a_dropped_flush_hint_is_re_hinted_from_the_durable_row` | one prompt
  after the next wake.** `DroppingFlushQueue` drops the first hint. After the raise and a settle:
  the row is `Pending`, no `UserPrompt`. Evaluate at `t0+181`: `UntilAsync` exactly one `UserPrompt`
  with the note; `DroppingFlushQueue.Calls` has two entries for the session. Red: no re-hint of a
  `Pending` alarm row -> fails at `UntilAsync`.
- **V-33: a failed queue insert is retried and delivered once (TD-1, real stack) | Integration |
  `RunnerAlarmDeliveryTests.a_failed_queue_insert_is_retried_on_the_next_wake_and_delivered_once` |
  one prompt.** A `SaveChangesInterceptor` fails the first `SessionQueuedMessage` insert (the
  `FailFirstCompletionInsert` shape). After the raise: no row, `NotifiedSessionIds` empty, one
  Warning. Evaluate at `t0+181`: one row, `UntilAsync` exactly one `UserPrompt` with the note;
  evaluate at `t0+182`: still one. Red: TD-1 violated (record before enqueue) -> fails at
  `UntilAsync`.
- **V-34: a note orphaned by a restart is typed with the next flush | Integration |
  `RunnerAlarmDeliveryTests.a_note_orphaned_by_a_restart_is_typed_with_the_next_flush` | both
  notes reach the transcript.** `DroppingFlushQueue` drops the first hint; raise -> row R1
  `Pending`. Simulated restart: a new `RunnerAlarmState` and coordinator, hints no longer dropped,
  the runner still down: open at `t1`, raise at `t1+180` -> row R2. `UntilAsync`: the session's
  `UserPrompt` text (one prompt or two) contains R1's body and R2's body; R1 and R2 are `Sent`.
  Evidence row for handoff H4; no guard of its own (G-38 re-raises after the restart; the queue's existing session flush drains R1).

**S3: wiring** (`CP-5` red, `CP-6` green)

- **V-36: `Program` wires the loop, both observers and the default exclusion | Integration, real
  `Program` via `AntiphonWebAppFactory` (the `WorktreeResidueRegistrationTests` attributes:
  `[NotInParallel]`, `[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]`,
  plus `[ParallelLimiter<ProcessSpawnLimit>]`) |
  `tests/Antiphon.Tests/Application/RunnerAlarmWiringTests.cs`
  `RunnerAlarmWiringTests.program_wires_the_loop_the_observers_and_the_default_exclusion` | as
  below.** Exactly one `RunnerAlarmHostedService` among `IHostedService`s;
  `IRunnerAlarmExclusion` is `NeverExcluded`; `IRunnerEligibilitySnapshotSource` is the
  `PhoneHomeRunnerDirectory` singleton; the directory's `internal Observer` accessor is the
  `AlarmWakeQueue` singleton; `IOptions<AlarmSettings>` carries 180 / 15 / 5. Then a
  `ScratchGitRepo` with a 10-minute-old dead record: the resolved `IRepositoryMutationLease`
  refuses `TryAcquireAsync`, and `UntilAsync` (10 s) the resolved `RunnerAlarmState` holds that
  repository's finding. Red: construct the directory without `observer:` in `Program.cs` -> fails
  at `Observer`; register the lease without its fence observer -> fails at the finding; omit the
  hosted-service registration -> fails at the count.

**S4: attention and client** (`CP-8` red, `CP-9` green for the server class; `CP-10` red, `CP-11`
green for the client file; `tests/Antiphon.Tests/Application/RunnerAlarmAttentionTests.cs`,
`[Category("Integration")]`, isolated schema, `AttentionService` built as
`DispatchHeldAttentionTests.cs:265-276` plus `alarms: state`, only rows of the two kinds asserted)

- **V-17: a raised episode is one `Error` row and an unraised one is none | Integration |
  `RunnerAlarmAttentionTests.a_raised_episode_is_one_error_row_and_an_unraised_one_is_none` | as
  below.** State with a raised `server2` episode (`PinnedOpenTasks 3`, `LiveSessions 1`, reason
  `transport_abort`, `NotifiedSessionIds` of two): exactly one item, `Kind == RunnerUnavailable`,
  `Severity == Error`, `Title == "Runner server2 unavailable"`, `ConditionKey ==
  "runner-unavailable:server2"`, headline containing `3 open task(s)` and `1 live session(s)`,
  evidence containing `lastReason=transport_abort` and `notified=2`, `SinceUtc == DownSince`,
  `Actions == [OpenDrawer]`, no task/session/agent id. State with only an unraised episode: no item
  of the kind. Red: project every episode, raised or not -> fails at the unraised "no item".
- **V-18: a journal finding with stale records is one `Error` row naming the recovery command |
  Integration | `RunnerAlarmAttentionTests.a_journal_finding_is_one_error_row_naming_the_recovery_command`
  | as below.** Finding A with two stale records and one `Alive` record: one item, `Kind ==
  RepositoryChildJournalStale`, `Severity == Error`, `ConditionKey == "journal-stale:" + key`
  (key = `Path.TrimEndingDirectorySeparator(Path.GetFullPath(common))`), headline starting `2 stale`,
  evidence containing `recover-repository-children.ps1 -Repository ` + the repository path and
  `-Execute -ConfirmDescendantsExited`, `SinceUtc` = the older stale record's write time. Finding B
  with only an `Alive` record: no item for B. Findings for two repositories each with a stale
  record: two items with distinct keys. Red: count every record instead of stale ones -> fails at
  `2 stale`; project a finding with no stale record -> fails at B's "no item".
- **V-19: no state means no rows, and the summary counts both kinds open | Integration |
  `RunnerAlarmAttentionTests.no_state_means_no_rows_and_the_summary_counts_both_kinds_open` | as
  below.** Service built without `alarms`: no item of either kind. With one raised episode and one
  stale finding: `AttentionSummaryDto.From(dto).Open` is the no-state count plus 2. Red on the
  skeleton at the `+ 2` (builders return `[]`); after S4 a PC maps either kind to a non-open
  bucket -> fails at `+ 2`.
- **V-20: the client knows both kinds and draws them as broken | client (vitest) |
  `client/src/features/attention/attentionVisuals.test.ts`: `ALL_KINDS` gains `RunnerUnavailable`
  and `RepositoryChildJournalStale`; the home-bucket `it.each` (`:90-98`) gains
  `['RunnerUnavailable', 'Error', 'broken']` and `['RepositoryChildJournalStale', 'Error', 'broken']`
  | all cases green.** The existing `maps every kind to a label, a colour, an icon and a hint`,
  `lands every kind in a declared group` and the `unique == visualKeys` lockstep (`:266-268`) then
  cover the two kinds; the new `it.each` cases assert
  `ATTENTION_VISUALS[kind]` is defined and `homeBucketOf(item({ kind, severity: 'Error' })) ===
  'broken'`. Red (the S4-tests commit: union and test lists updated, visuals not): the two new
  `it.each` cases and `maps every kind ...` fail at `toBeDefined` (other every-kind loops may fail on the same two kinds; CP-10). Label/colour/icon/hint strings are
  D-6 copy, asserted non-empty only.

### Guards the regression

- **R-1: the observer seam changes no directory behaviour** | `MultiRunnerDirectoryTests` (7),
  `MultiRunnerRecoveryTests` (8), `PhoneHomeDirectoryTests` (7), `DefaultRunnerEligibilityTests` (3):
  25 executions, 0 failed (`CP-7`). Decisive: `MultiRunnerDirectoryTests`'s supersede/abort
  recipe (`:79-93`) and `DefaultRunnerEligibilityTests`'s lease expiry (`:66-87`) keep their
  eligibility answers with a recorder attached through MS-1's default-null path.
- **R-2: the fence observer changes no lease behaviour** | `RepositoryMutationLeaseTests` (14
  methods, 28 executions, 2 skipped on Linux by their own `SkipTestException`:
  `C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput`,
  `C448_V13_WindowsJunctionAndOtherProcessShareTheLease`), `RepositoryMutationLeaseDescribeTests`
  (1), `RepositoryMutationLeaseOwnerTests` (6 methods, 8 executions): 37 results, 35 passed, 2
  skipped, 0 failed (`CP-7`). Decisive: the C448/C661 fence-and-recover rows at
  `RepositoryMutationLeaseTests.cs:505-600` still refuse and recover exactly as before.
- **R-3: the optional `alarms` parameter and two builders change no existing row** |
  `AttentionServiceTests` (partials `AttentionServiceTests.cs` 124, `AttentionServiceTests.C691.cs`
  32, `AttentionServiceCommitRecoveryTests.cs` 9 executions) and `DispatchHeldAttentionTests` (11):
  174 executions, 0 failed (`CP-12`), run with `TUNIT_MAX_PARALLEL_TESTS=1` as the plan set.
- **R-4: the client's existing attention visuals stay green** | the 15 existing `it` blocks and
  the four existing `it.each` cases of `attentionVisuals.test.ts` (`CP-11`).
- **R-5: every new class declares its lane** | `TestLaneCategoryGuardTests` (1, `CP-7`) fails on
  a new test class without `[Category]`.

### Guard inventory

Safety-critical here means a guard whose failure would **miss an alarm** (an outage or a fence
nobody hears of), **raise a false one** (a flap, a drained or retired runner, a live or in-flight
journal record), **mis-deliver caller notes** (spam, a second note, a lost note, a note typed into
a busy caller, a recovery note to someone never told), **fault or slow the directory, the lease,
the land or the dispatcher**, **poll**, or **touch a journal record** (D-9). Guards are split
where two lines can be broken independently; each maps 1:1 to a distinct PC. Assertions that
are not guards (a notify on a non-flipping `MarkRecovered`, the exact second a raise lands, the
note's wording) are named in their V row but carry no PC.

| G | Plan ref | Guard | PC |
|---|---|---|---|
| G-1 | D-2 | a `MarkRecovered` that flips eligibility notifies | PC-1 |
| G-2 | D-2 | a superseded connection notifies | PC-2 |
| G-3 | D-2 | the live connection's `Disconnect` notifies | PC-3 |
| G-4 | D-2 | a lease expiry seen by `SnapshotOf` notifies | PC-4 |
| G-5 | D-2 | lease-expiry notification is edge-only (the pump reads every 50 ms; per-read signals would poll the loop) | PC-5 |
| G-6 | S1 | the snapshot source's `Eligible` is the dispatch predicate, not `Available` | PC-6 |
| G-7 | D-8 | a busy `landing.lock` refusal is not a fence signal (dispatcher ticks would drive an inspection each) | PC-7 |
| G-8 | D-2 | an observer exception never escapes the directory | PC-8 |
| G-9 | D-2 | the observer runs outside `_gate` | PC-9 |
| G-10 | D-8 | a journal-fenced `AcquireAsync` signals `Fenced(common)` | PC-10 |
| G-11 | D-8 | a non-null `DescribeUnavailableAsync` signals `Fenced(common)` | PC-11 |
| G-12 | D-1 | `RunnerGraceSeconds <= 0` fails validation (every flap would alarm) | PC-12 |
| G-13 | D-1 | `SweepMinutes <= 0` fails validation (a tight sweep is a poll) | PC-13 |
| G-14 | D-1 | `JournalStaleMinutes <= 0` fails validation (every in-flight record would alarm) | PC-14 |
| G-15 | D-8 | an `Alive` record is never stale | PC-15 |
| G-16 | D-8 | a `false` liveness read (missing or reused PID) is `Dead`, never `Alive` | PC-16 |
| G-17 | D-8 | a `Completed` record is never `Alive` | PC-17 |
| G-18 | D-8 | a start-intent or foreign record is `Unknown`, never `Alive` | PC-18 |
| G-19 | D-8 | a non-`.json` or torn file surfaces as `Malformed` | PC-19 |
| G-20 | D-9 | the inspector deletes nothing | PC-20 |
| G-21 | D-8 | the inspector never opens `landing.lock` (CARD-0535 V-2b) | PC-21 |
| G-22 | D-8 | a non-alive record younger than the threshold is not stale | PC-22 |
| G-23 | TD-3 | a `null` liveness read is `Unknown`, never `Alive` | PC-23 |
| G-24 | TD-3 | a `children` file surfaces as one `Malformed` finding | PC-24 |
| G-25 | D-3 | no raise before the grace has elapsed | PC-25 |
| G-26 | D-3 | an open episode past the grace is raised | PC-26 |
| G-27 | D-4 | every open status (`Queued`, `Dispatched`, `Working`, `Blocked`) counts, so its caller is told | PC-27 |
| G-28 | D-4 | specialist tasks' parents are not told | PC-28 |
| G-29 | D-4 | only `ReplyTo == Session` parents are told | PC-29 |
| G-30 | D-7 | a recorded caller is never told twice in one episode | PC-30 |
| G-31 | D-3 | a flap inside the grace closes silently | PC-31 |
| G-32 | D-7 | the recovery note goes only to `NotifiedSessionIds` | PC-32 |
| G-33 | D-3 | a resolved episode leaves the state | PC-33 |
| G-34 | D-5 | an excluded (draining/retired) runner never opens or raises an episode | PC-34 |
| G-35 | D-5 | the exclusion is consulted on every wake, closing a raised episode | PC-35 |
| G-36 | D-3 | disabled entries are never evaluated | PC-36 |
| G-37 | D-3 | a raised episode closed by an exclusion still sends the recovery note to its notified callers | PC-37 |
| G-38 | D-3 | the first pass opens an episode for a runner already down (the CARD-0716 restart case) | PC-38 |
| G-39 | D-3 | a disconnect older than the last resolution is not reused as `DownSince` (instant false raise) | PC-39 |
| G-40 | D-3 | a disconnect later than the last resolution is used as `DownSince` | PC-40 |
| G-41 | TD-1 | a failed note leaves the caller unrecorded and is retried next wake | PC-41 |
| G-42 | D-8 | repositories are keyed on the common directory (one row per repository) | PC-42 |
| G-43 | D-8 | a missing or non-git path is skipped without aborting the pass | PC-43 |
| G-44 | D-1 | `JournalEnabled = false` inspects nothing | PC-44 |
| G-45 | D-3 | the wait due time includes each unraised episode's grace expiry | PC-45 |
| G-46 | D-8 | `Fenced` wakes the loop | PC-46 |
| G-47 | D-3 | no timer other than the grace and the sweep (no poll) | PC-47 |
| G-48 | D-3 | the sweep runs at `SweepMinutes` | PC-48 |
| G-49 | D-1 | `Enabled = false` returns at once | PC-49 |
| G-50 | D-3 | a failing pass is caught, logged and retried; the loop survives | PC-50 |
| G-51 | D-2 | `Signal` wakes the loop | PC-51 |
| G-52 | D-7 | `deliverIfIdle: false` (never typed on the alarm loop's thread) | PC-52 |
| G-53 | D-7 | the notifier hints `CompletionNoteFlushQueue.TryEnqueue(session)` | PC-53 |
| G-54 | D-7 | origin `System` | PC-54 |
| G-55 | D-4 | the caller receives the body, not only the header | PC-55 |
| G-56 | D-7 | `MessageSendMode.WhenIdle`, never `Now` | PC-56 |
| G-57 | TD-2 | a `Pending` alarm row is re-hinted on the next wake | PC-57 |
| G-58 | S1 | `Program` passes the `AlarmWakeQueue` to the directory as its observer | PC-58 |
| G-59 | S1 | `Program` gives the lease its fence observer | PC-59 |
| G-60 | S3 | `Program` registers `RunnerAlarmHostedService` | PC-60 |
| G-61 | D-5 | `Program` registers `NeverExcluded` as `IRunnerAlarmExclusion` | PC-61 |
| G-62 | D-6 | an unraised episode is never a feed row | PC-62 |
| G-63 | D-8 | the journal row counts stale records only | PC-63 |
| G-64 | D-8 | a finding with no stale record is never a feed row | PC-64 |
| G-65 | D-6 | both kinds count as Open in the summary | PC-65 |
| G-66 | D-6 | the client maps both kinds to a visual | PC-66 |
| G-67 | D-6 | the client puts both kinds in the `broken` home bucket at `Error` | PC-67 |

Guards = 67, mapped = 67, missing = 0, duplicate PC maps = 0.

### Positive controls

Mutation runs each after land: apply the compiling defect, run **only** the named method with
`--treenode-filter "/*/*/<Class>/<Method>"` through `scripts/build-slot.ps1` (one invocation per
method; method-level OR is never used), see the named assertion fail, restore the file and
refresh its timestamp, run the same method green. A build error, a fixture error or zero executed
tests is not red. Batch only PCs in different production files **and** different test methods.
Code runs the V/R rows; Review judges this list before land. Server PCs use
`tests/Antiphon.Tests` into `bin-c726-pc/`; client PCs run
`pwsh -File scripts/test-client.ps1 attentionVisuals.test -t "<test name>"` under a build slot.

| PC | Break (file: compiling defect) | Red method | At |
|---|---|---|---|
| PC-1 | `PhoneHomeRunnerDirectory.cs`: delete the `Notify(runnerId)` after `MarkRecovered`'s flip | `RunnerEligibilityObserverTests.disconnect_recovery_and_supersede_notify_the_observer_with_the_runner_id` | recorder `[a]` after `MarkRecovered` |
| PC-2 | same file: delete the `Notify` for the superseded connection in `AcceptConnect` | same method | recorder `[a, a]` after the replacement connects |
| PC-3 | same file: delete the `Notify` after `Disconnect` | same method | fourth `a` after the abort |
| PC-4 | same file: delete the `Notify` in `SnapshotOf`'s expiry branch | `RunnerEligibilityObserverTests.lease_expiry_seen_by_a_snapshot_notifies_once` | recorder count 2 (is 1) |
| PC-5 | same file: notify on every expired read (drop the "was eligible" edge test) | same method | recorder count 2 (is 4) |
| PC-6 | same file (snapshot source): `Eligible = status.Available` | `RunnerEligibilityObserverTests.snapshots_carry_one_row_per_configured_remote_with_the_dispatch_predicate` | connected-not-recovered `Eligible == false` |
| PC-7 | `RepositoryMutationLease.cs`: call `fences?.Fenced(common)` in the `catch (IOException)` branch | `RepositoryFenceObserverTests.a_fenced_acquire_and_a_describe_name_the_common_directory_once_each` | busy-lock recorder `[]` |
| PC-8 | `PhoneHomeRunnerDirectory.cs`: remove the `try/catch` around the observer call | `RunnerEligibilityObserverTests.a_throwing_observer_is_contained_and_logged` | first `Should.NotThrow` |
| PC-9 | same file: move `MarkRecovered`'s `Notify` inside `lock (_gate)` | `RunnerEligibilityObserverTests.the_observer_runs_outside_the_directory_gate` | first recorded cross-thread result `true` |
| PC-10 | `RepositoryMutationLease.cs`: delete the `Fenced` call in `AcquireAsync`'s journal branch | `RepositoryFenceObserverTests.a_fenced_acquire_and_a_describe_name_the_common_directory_once_each` | recorder `[common]` |
| PC-11 | same file: delete the `Fenced` call in `DescribeUnavailableAsync` | same method | recorder `[common, common]` |
| PC-12 | `AlarmSettingsValidator.cs`: delete the `RunnerGraceSeconds <= 0` check | `AlarmSettingsValidatorTests.defaults_validate_and_nonpositive_values_are_named` | `Alarms:RunnerGraceSeconds` failure |
| PC-13 | same file: delete the `SweepMinutes <= 0` check | same method | `Alarms:SweepMinutes` failure |
| PC-14 | same file: delete the `JournalStaleMinutes <= 0` check | same method | `Alarms:JournalStaleMinutes` failure |
| PC-15 | `RepositoryChildJournalInspector.cs`: `Stale = age >= staleAfter` (drop `State != Alive`) | `RepositoryChildJournalInspectorTests.a_live_record_is_alive_and_never_stale` | `Stale == false` |
| PC-16 | same file: map a `false` liveness read to `Alive` | `RepositoryChildJournalInspectorTests.dead_completed_unknown_and_malformed_records_past_the_threshold_are_stale` | state multiset |
| PC-17 | same file: map `Completed` to `Alive` | same method | state multiset |
| PC-18 | same file: the fall-through arm returns `Alive` instead of `Unknown` | same method | state multiset |
| PC-19 | same file: `continue` past non-`.json` names | same method | state multiset (5 findings) |
| PC-20 | same file: `File.Delete(path)` after classifying `Dead` | same method | all seven files exist |
| PC-21 | same file: open `<common>/antiphon/landing.lock` with `FileMode.OpenOrCreate` at the start of `InspectAsync` | same method | `landing.lock` absent |
| PC-22 | same file: `Stale = State != Alive` (drop the age term) | `RepositoryChildJournalInspectorTests.age_threshold_is_inclusive_and_an_absent_journal_raises_nothing` | 4:59 `Stale == false` |
| PC-23 | same file: map a `null` liveness read to `Alive` | `RepositoryChildJournalInspectorTests.a_record_whose_process_cannot_be_read_is_unknown_and_stale` | `State == Unknown` |
| PC-24 | same file: return an empty inspection when `children` is not a directory | `RepositoryChildJournalInspectorTests.a_children_path_that_is_a_file_is_one_malformed_finding` | finding count 1 |
| PC-25 | `RunnerAlarmCoordinator.cs`: compare against `grace / 2` | `RunnerAlarmCoordinatorTests.a_runner_down_past_the_grace_raises_once_with_counts_and_one_note_per_caller` | `RaisedAt == null` at `t0+179` |
| PC-26 | same file: delete the `RaisedAt = now` raise assignment | same method | `RaisedAt == t0+180` |
| PC-27 | same file: drop `Queued` from the open-status set | same method | `PinnedOpenTasks == 4` (is 3) |
| PC-28 | same file: drop the `NotSpecialist` filter | same method | `PinnedOpenTasks == 4` (is 5) |
| PC-29 | same file: drop the `ReplyTo == Session` caller filter | same method | exactly two notes (P5 added) |
| PC-30 | same file: drop the "already in `NotifiedSessionIds`" check | same method | note count at `t0+240` |
| PC-31 | same file: close an unraised episode through the resolve branch | `RunnerAlarmCoordinatorTests.a_flap_inside_the_grace_leaves_no_trace` | notifier empty |
| PC-32 | same file: send recovery notes to the current callers query instead of `NotifiedSessionIds` | `RunnerAlarmCoordinatorTests.recovery_resolves_and_tells_only_the_notified_callers` | recovery set `{P1, P2}` (P3 added) |
| PC-33 | same file: keep the episode after resolving | same method | no episode |
| PC-34 | `RunnerAlarmCoordinator.cs`: never call `IRunnerAlarmExclusion.Excluded` | `RunnerAlarmCoordinatorTests.a_draining_or_retired_runner_never_raises_and_a_disabled_entry_is_skipped` | m1 no episode at `t0` |
| PC-35 | same file: consult the exclusion only when opening an episode | same method | m5 episode gone after `"retired"` |
| PC-36 | same file: evaluate snapshots with `Enabled == false` | same method | m2 no episode for `off` |
| PC-37 | same file: close an excluded raised episode without the recovery note | same method | m5 P6's one `[runner r3 recovered]` note |
| PC-38 | same file: open an episode only when this process saw the runner eligible first | `RunnerAlarmCoordinatorTests.startup_opens_an_episode_per_enabled_remote_runner_and_a_normal_reconnect_closes_it` | two episodes at `t0` |
| PC-39 | same file: `DownSince = LastDisconnectAtUtc ?? now` unconditionally | `RunnerAlarmCoordinatorTests.down_since_uses_the_last_disconnect_after_the_last_resolution` | `DownSince == t0+400` |
| PC-40 | same file: `DownSince = now` unconditionally | same method | `DownSince == t0+10` |
| PC-41 | same file: add the session to `NotifiedSessionIds` before `NotifyAsync` | `RunnerAlarmCoordinatorTests.a_failed_note_is_retried_on_the_next_wake_and_never_sent_twice` | `NotifiedSessionIds == {P1}` |
| PC-42 | same file: key repositories on the checkout path instead of `CommonDirectoryAsync` | `RunnerAlarmCoordinatorTests.journal_findings_are_published_per_repository_and_cleared_when_recovered` | exactly one finding |
| PC-43 | same file: remove the skip for a missing or non-git path | same method | the evaluation completes without throwing |
| PC-44 | same file: ignore `JournalEnabled` | same method | no finding with `JournalEnabled = false` |
| PC-45 | `RunnerAlarmHostedService.cs`: wait until `nextSweep` only | `RunnerAlarmHostedServiceTests.the_loop_raises_on_the_grace_timer_and_wakes_on_a_fence_signal` | first `UntilAsync` (`RaisedAt` set) |
| PC-46 | `AlarmWakeQueue.cs`: `Fenced` records the repository without writing the wake channel | same method | second `UntilAsync` (finding) |
| PC-47 | `RunnerAlarmHostedService.cs`: add `await Task.Delay(TimeSpan.FromSeconds(1), clock, ct)` in the loop | `RunnerAlarmHostedServiceTests.the_sweep_runs_at_the_period_and_no_other_timer_exists` | every due time >= 1 min |
| PC-48 | same file: `nextSweep = now + 2 * SweepMinutes` | same method | 15-minute `UntilAsync` |
| PC-49 | same file: ignore `Enabled` | `RunnerAlarmHostedServiceTests.disabled_alarms_start_no_loop_and_publish_nothing` | source calls `0` |
| PC-50 | same file: remove the loop's `catch` | `RunnerAlarmHostedServiceTests.a_failing_pass_is_logged_and_retried_without_waiting_for_the_sweep` | `UntilAsync` open episode |
| PC-51 | `AlarmWakeQueue.cs`: `Signal` records the id without writing the wake channel | `RunnerAlarmHostedServiceTests.the_real_directory_drives_the_loop_and_a_drained_runner_never_raises` | first `UntilAsync` (episode for `a`) |
| PC-52 | `QueueRunnerAlarmNotifier.cs`: `deliverIfIdle: true` | `RunnerAlarmNotifierTests.a_note_is_a_pending_whenidle_system_row_hinted_and_never_typed_inline` | `SubmittedBodies` empty |
| PC-53 | same file: delete `TryEnqueue(session)` | same method | `Calls == [session]` |
| PC-54 | same file: `QueuedMessageOrigin.Ui` | same method | `Origin == System` |
| PC-55 | same file: pass the header as the body (drop the body) | `RunnerAlarmDeliveryTests.an_idle_caller_receives_the_outage_and_recovery_notes_as_complete_user_prompts` | short-id assertion on the `UserPrompt` |
| PC-56 | same file: `MessageSendMode.Now` | `RunnerAlarmDeliveryTests.a_busy_caller_gets_the_note_only_after_its_turn_ends` | `Adapter.Inputs` holds nothing of the note |
| PC-57 | `RunnerAlarmCoordinator.cs`: delete the re-hint of `Pending` alarm rows | `RunnerAlarmDeliveryTests.a_dropped_flush_hint_is_re_hinted_from_the_durable_row` | `UntilAsync` one `UserPrompt` |
| PC-58 | `Program.cs`: construct the directory without `observer:` | `RunnerAlarmWiringTests.program_wires_the_loop_the_observers_and_the_default_exclusion` | `Observer` is the `AlarmWakeQueue` |
| PC-59 | same file: register the lease without its fence observer | same method | `UntilAsync` finding |
| PC-60 | same file: delete the `AddHostedService<RunnerAlarmHostedService>()` line | same method | one hosted service |
| PC-61 | same file: delete the `NeverExcluded` registration | same method | `IRunnerAlarmExclusion` is `NeverExcluded` (resolve throws) |
| PC-62 | `AttentionService.cs`: project every episode, raised or not | `RunnerAlarmAttentionTests.a_raised_episode_is_one_error_row_and_an_unraised_one_is_none` | unraised "no item" |
| PC-63 | same file: headline count = all records | `RunnerAlarmAttentionTests.a_journal_finding_is_one_error_row_naming_the_recovery_command` | headline starts `2 stale` |
| PC-64 | same file: project findings with `StaleCount == 0` | same method | no item for B |
| PC-65 | `AttentionDtos.cs` (`AttentionSummaryDto.From`): count both kinds outside `Open` | `RunnerAlarmAttentionTests.no_state_means_no_rows_and_the_summary_counts_both_kinds_open` | `Open` is base + 2 |
| PC-66 | `client/src/features/attention/attentionVisuals.ts`: `RunnerUnavailable.hint = ''` | `attentionVisuals` `maps every kind to a label, a colour, an icon and a hint` | `visual.hint.length > 0` for `RunnerUnavailable` |
| PC-67 | same file: `homeBucketOf` sends `RepositoryChildJournalStale` to `review` | `attentionVisuals` `draws RepositoryChildJournalStale in the Error severity bucket` | `toBe('broken')` |

PCs = 67, each executable against a compiling mutation of one named production line; none is
Windows-only, none needs a running stack or a provider.

### Out of scope

- **A live outage on the running stack** (stopping server2's runner for 3 minutes to see the row
  and a note typed into a real caller): it disrupts the production runner and every session on
  it. The recipient evidence is V-30..V-34 through the real queue; after land, the orchestrator's
  read-only activation observation is that `GET /api/attention` carries no `RunnerUnavailable`
  row 180 s after the next canonical restart while `GET /api/session-runners` shows `server2`
  `dispatchEligible` (not a Code row).
- **A real TUI composer**: the note rides the unchanged CARD-0055 multi-line delivery path
  (`SessionMessageQueuePtyIntegrationTests`); no Pty or FakeClaude row, so nothing here is
  co-scheduled with `Antiphon.Agents.Pty.Tests`.
- **Dead or stopped callers**: a `Pending` note on a caller that never idles is never typed (D-7).
- **CARD-0727's row-backed `IRunnerAlarmExclusion`**: its card inherits V-11 as the contract and
  adds its own implementation test.
- **The runner-side repository on server2 and the desktop runner on 17204**: plan scope.
- **Archived-project scoping and journal row wording**: noise, not safety; asserted only where a
  V row names a string the operator must act on (the recovery command, the header lines).
- **Client `tsc -b`**: not a checkpoint; the vitest lockstep (`unique == visualKeys`) and
  `maps every kind ...` fail at run time on a missing entry, which is what CP-10 shows red.
- **Windows lane**: nothing touches junctions, ConPTY, CRLF or E2E. The two Windows-only methods
  of `RepositoryMutationLeaseTests` skip on Linux by their own guard and test inherited handles
  and junctions the fence observer does not touch.
- **S5 docs**: Review reads them; no test.
- **Attention integers**: tests assert member names, never values (D-6).

### Checkpoints

Round 1 is CP-1..CP-7 and Round 2 is CP-8..CP-12. Each source slice has its own
isolated output (forward slash; all deleted before the Code report). Every TUnit row runs through
`pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-n -Project tests/Antiphon.Tests
-OutputPath <Build output> -Filter '<filter>' -MinExecuted <Min> -Expect <classes>
-ResultsRoot .antiphon/c726-checkpoints` (it takes the build slot and adds `UseAppHost=false` off
Windows); `-NoBuild` where Build names a row. Client rows run under
`pwsh -NoProfile -File scripts/build-slot.ps1 -Label <group> -- <command>`. For the R2 review rerun,
use the checked-in [checkpoint manifest](2026-09-26-card-0726-runner-and-journal-alarms-checkpoints.yaml)
with `TUNIT_MAX_PARALLEL_TESTS=1` set in the checkpoint tool's process environment. Its CP-12 row
has `serial: true`, so no other checkpoint runs alongside that database query-count selection.
The CP-8 and CP-10 red expectations below describe their original S4-tests commits; rerunning
them against the implemented S4 code should be green. Class-level OR filters
only (CARD-0403 syntax, each operand parenthesised with a trailing `*`); method-level OR is never
used. `Min` is the source execution count at `4fe3bce9` plus this card's new methods.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-tests | `tests/Antiphon.Tests -> bin-c726-r1-s1-tests/` | seams-red | `/*/*/(AlarmSettingsValidatorTests*)\|(RunnerEligibilityObserverTests*)\|(RepositoryFenceObserverTests*)/*` | V-1..V-4, V-21..V-23 | 7 executed (1 + 5 + 1), 7 failed, each at its named first assertion: V-1 the `Alarms:RunnerGraceSeconds` failure, V-2/V-3/V-4 the first recorder assertion, V-21 the id set, V-22 the Warning log, V-23 the recorded results | 7 | 7 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c726-r1-s1/` | seams-green | `/*/*/(AlarmSettingsValidatorTests*)\|(RunnerEligibilityObserverTests*)\|(RepositoryFenceObserverTests*)/*` | V-1..V-4, V-21..V-23 | all 7 listed, 0 failed/skipped | 7 | 6 |
| CP-3 | S2-tests | `tests/Antiphon.Tests -> bin-c726-r1-s2-tests/` | inspector-red | `/*/*/RepositoryChildJournalInspectorTests/*` | V-5..V-7, V-24, V-25 | 5 executed, 5 failed at their finding-count or state assertions (empty-inspection skeleton) | 5 | 6 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c726-r1-s2/` | inspector-green | `/*/*/RepositoryChildJournalInspectorTests/*` | V-5..V-7, V-24, V-25 | all 6 listed, 0 failed/skipped | 6 | 6 |
| CP-5 | S3-tests | `tests/Antiphon.Tests -> bin-c726-r1-s3-tests/` | alarm-loop-red | `/*/*/(RunnerAlarmCoordinatorTests*)\|(RunnerAlarmHostedServiceTests*)\|(RunnerAlarmNotifierTests*)\|(RunnerAlarmDeliveryTests*)\|(RunnerAlarmWiringTests*)/*` | V-8..V-16, V-26..V-36 | 20 executed (8 + 5 + 1 + 5 + 1); V-9 and V-26 pass (controls); the other 18 fail at their raise, note, finding, timer, row or wiring assertion (V-11 at m3's open episode, V-31 at "row is `Pending`") | 20 | 13 |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c726-r1-s3/` | alarm-loop-green | `/*/*/(RunnerAlarmCoordinatorTests*)\|(RunnerAlarmHostedServiceTests*)\|(RunnerAlarmNotifierTests*)\|(RunnerAlarmDeliveryTests*)\|(RunnerAlarmWiringTests*)/*` | V-8..V-16, V-26..V-36 | all 27 listed, 0 failed/skipped | 27 | 12 |
| CP-7 | S3 | CP-6 | r1-regression | `/*/*/(MultiRunnerDirectoryTests*)\|(MultiRunnerRecoveryTests*)\|(PhoneHomeDirectoryTests*)\|(DefaultRunnerEligibilityTests*)\|(RepositoryMutationLeaseTests*)\|(RepositoryMutationLeaseDescribeTests*)\|(RepositoryMutationLeaseOwnerTests*)\|(TestLaneCategoryGuardTests*)/*` | R-1, R-2, R-5 | 63 results (25 + 37 + 1): 61 passed, 2 skipped (`C448_V28_...`, `C448_V13_...`, Windows-only), 0 failed | 61 | 8 |
| CP-8 | S4-tests | `tests/Antiphon.Tests -> bin-c726-r2-tests/` | attention-red | `/*/*/RunnerAlarmAttentionTests/*` | V-17..V-19 | 3 executed, 3 failed: V-17 and V-18 at "one item of the kind", V-19 at `Open` base + 2 | 3 | 6 |
| CP-9 | S4 | `tests/Antiphon.Tests -> bin-c726-r2/` | attention-green | `/*/*/RunnerAlarmAttentionTests/*` | V-17..V-19 | all 3 listed, 0 failed/skipped | 3 | 5 |
| CP-10 | S4-tests | n/a | client-visuals-red | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c726-client-red -- pwsh -File scripts/test-client.ps1 attentionVisuals.test` | V-20 | `CLIENT TESTS EXIT CODE: 1`; the failures include `maps every kind to a label, a colour, an icon and a hint` and the two new `draws ... in the Error severity bucket` cases at `toBeDefined`, and may include the other every-kind loops (`keeps kinds off the violet tier axis`, `lands every kind in a declared group`, the `unique == visualKeys` lockstep); every failure names `RunnerUnavailable` or `RepositoryChildJournalStale`, and no other case fails | n/a | 3 |
| CP-11 | S4 | n/a | client-visuals-green | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c726-client -- pwsh -File scripts/test-client.ps1 attentionVisuals.test` | V-20, R-4 | 22 tests (16 `it` + 6 `it.each` cases), 0 failed, `CLIENT TESTS EXIT CODE: 0` | n/a | 3 |
| CP-12 | S4 | CP-9 | attention-regression | `/*/*/(AttentionServiceTests*)\|(DispatchHeldAttentionTests*)/*` | R-3 | 174 executed (163 AttentionServiceTests + 11 DispatchHeldAttentionTests), 0 failed; `serial: true` in the checkpoint manifest and `TUNIT_MAX_PARALLEL_TESTS=1` in its process environment | 174 | 24 |

Union of `Covers` = V-1..V-36 and R-1..R-5: the whole ordinary scope. CP-7 and CP-12 reuse their
`Build` row's output with `-NoBuild` and share its `After`. A red row is fixed and rerun as the
same row (reruns counted); an inherited failure is re-run at `4fe3bce9` before it is reported.
CP-12's floor was corrected from 176 after its fresh TRX at `1e0a3114` showed the full
selection contains 163 `AttentionServiceTests` and 11 `DispatchHeldAttentionTests` results,
all passing; no method was omitted by the filter.

### Cost

All figures are estimated. The measured inputs are CARD-0738's on this host (2026-09-26):
`tests/Antiphon.Tests` build 4 min 03 s under a slot, about 1 min per small class including the
Testcontainers Postgres start; nothing was built or run by TestDesign.

- **Ordinary V/R floor (Code)** = sum of `EstimatedMinutes` = 7 + 6 + 6 + 6 + 13 + 12 + 8 + 6 + 5
  + 3 + 3 + 24 = **99 minutes**, builds included: Round 1 (CP-1..CP-7) **58 min**, Round 2
  (CP-8..CP-12) **41 min**, plus slot waits. Against the plan's draft 93 min: +6 min for the
  delivery, wiring and boundary rows (V-21..V-36 and R-4, R-5) and the client red row; CP-12 is
  the same 24 min. Authoring: Round 1 about 4 h (S1..S3, 32 new methods across 9 classes plus
  MS-1..MS-5), Round 2 about 2 h (S4, S5). `-ExpectAbout`: Round 1 about 5 h, Round 2 about 2.75 h.
- **PC floor (Mutation)**, 67 controls, method-scoped red/restore/green, batched only across
  different production files and different test methods, so the round count is the largest
  per-file PC count: `RunnerAlarmCoordinator.cs` holds 21 (PC-25..PC-44, PC-57), then
  `RepositoryChildJournalInspector.cs` 10, `PhoneHomeRunnerDirectory.cs` 8, `RunnerAlarmHostedService.cs`
  5, `QueueRunnerAlarmNotifier.cs` 5, `Program.cs` 4, three files with 3, `AlarmWakeQueue.cs` 2,
  `AttentionDtos.cs` 1, client 2.
  - server (PC-1..PC-65): 21 rounds x 2 incremental `tests/Antiphon.Tests` builds (~4 min each)
    = **~168 min**, plus 65 red and 65 green single-method runs (~1 min each, one invocation per
    method) = **~130 min**: **~298 min**;
  - client (PC-66, PC-67): 2 x (red + green run, ~1.4 min) = **~3 min**;
  - setup (first build into `bin-c726-pc/`, `client/node_modules` present): **~6 min**.
  PC total **~307 min**. Unbatched it would be 65 x (8 + 2) + 3 + 6 ≈ 659 min, so file-disjoint
  batching saves about 352 min. A SourceLanding Mutation may not shard, so no further saving is
  assumed.
- **Total** = Code ordinary 99 min + ~6 h authoring; Mutation ~307 min; the post-land activation
  observation is read-only and under 1 min.

Handoff check: bodies read (15 files and fixtures above); guards = 67, mapped = 67, missing = 0,
duplicate PC maps = 0; all 67 PCs are compiling single-line defects with a named method and
assertion; every V/R row names its checkpoint; no placeholder remains. Stubs: none. V-9 and V-26
are controls that pass on the skeleton and are each made red by their PC (PC-31, PC-49).
