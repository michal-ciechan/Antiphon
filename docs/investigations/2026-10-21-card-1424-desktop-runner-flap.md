# CARD-1424: desktop runner flaps to platform unknown / not dispatch-eligible

Investigate stage, 2026-10-21. Branch `feat/card-task-07273b7d`, source at `3ce628771`.

## Verdict

**Confirmed (mechanism). The trigger needs one more log read.** The desktop runner is not
dropping a registration, and it is not restarting. The desktop has no registration or heartbeat
record at all. Every read of the desktop's platform and features is a fresh `GET /capabilities`
to `localhost:17204`, cut off after 2 seconds. Any failure becomes `platform = null`,
`features = []`, `available = false` and `dispatchEligible = false`, and nothing keeps the last
good answer. The orchestrator-instructions sweep runs this probe every 60 s. When one probe
fails, the generated settings change, a "settings changed" note goes out, and the next good
sweep changes them back. During the window, Windows-pinned dispatch to the desktop is held.

The probes fail when the desktop host is saturated: CPU at 100% across all 8 cores, the server
itself stalling for 10–22 s, and host-stats samples going stale. The stored evidence for the
historical windows does not show which failure inside the probe happened each time: the 2 s
deadline, the shared runner-read concurrency limiter, or an open read circuit. All three end in
the same null.

## Evidence

### 1. The flap, from stored notes (orchestrator transcript)

Source: `GET /api/sessions/daec6a72-771d-4a66-9634-fbe474d87464/transcript?since=0` (the
Antiphon-Orchestrator session). These are the `UserPrompt` rows whose text has
`runners: desktop platform windows → unknown`. Each timestamp is when the note was delivered,
not when the snapshot was written.

| Day (UTC) | windows→unknown notes |
|---|---|
| 2026-10-15 (first at 08:06:26Z) | 9 |
| 2026-10-16 | 7 |
| 2026-10-17 | 4 |
| 2026-10-18 | 4 |
| 2026-10-19 | 4 |
| 2026-10-20 | 6 |
| 2026-10-21 | 5 |

The pairs for 2026-10-21 are 08:10:15→08:12:24, 17:22:54→(coalesced), 18:47:50→18:49:08,
19:55:34→19:56:49 and 20:23:15→20:25:05.

- **It is older than the 16:27Z restart onto rc `dc47eb48`.** The pattern runs back to at least
  2026-10-15, so it is not a regression in that rc.
- **Only the desktop row changes.** Each pair moves between exactly two instruction versions
  (2026-10-21: `v72e16e61 ↔ vdc84c58c`; 2026-10-20: `v42bddad9 ↔ v09a5d789`). The rest of the
  snapshot is identical.
- **Recovery is one or two sweeps later**, about 60–130 s, which matches the 60 s sweep below.

### 2. How the desktop row is derived (no cache, no lease, 2 s)

- `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs:745-772`. For the
  desktop id, `DescribeAsync` calls `_local.GetCapabilitiesAsync` under
  `timeout.CancelAfter(TimeSpan.FromSeconds(2))` (line 753).
  - Timeouts and all other exceptions set `caps = null` without logging (lines 756-763).
  - The descriptor is then `Platform = Normalize(null)`, `PlatformObservedAt = null`,
    `Available = false`, `DispatchEligible = false`, `Stale = true` (lines 765-771).
  - There is no stored last-known platform or feature list for the desktop.
- Remote runners keep theirs. The same method, at lines 774-784, uses
  `live?.Platform ?? slot.RegisteredPlatform` and `slot.PlatformObservedAt`, gated by the lease.
  Live `GET /api/session-runners` shows the difference:
  - server2: `platformObservedAt` 2026-10-21T16:27:26Z, set at registration.
  - desktop: `platformObservedAt` 20:50:13Z, which is the moment of the request.
- `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:357-369`.
  `GetCapabilitiesAsync` has no cache. It goes through the admitted read pipeline
  (`ResilienceOperations.RunnerCapabilities`) and returns null on a transport error, a timeout
  or a bad body. The 5-minute capability cache at lines 70-93 / 314-330 / 527-545 serves only
  the launch-mismatch checks, not `DescribeAsync`.
- The admitted runner reads share one pipeline per dependency
  (`src/Antiphon.Resilience/ResiliencePipelineCache.cs:131-161`):
  - a `ConcurrencyLimiter` with 32 permits and `QueueLimit = 0` (lines 342-352);
  - a circuit breaker: 50% failures over 30 s, minimum 10 calls, 15 s break (lines 229-256).

  A limiter or circuit rejection becomes an `HttpRequestException`
  (`src/Antiphon.Resilience/AdmittedReadHandler.cs:98-108`), which also ends in `caps = null`.
- `server/Application/Services/SessionRunnerCatalogue.cs:25-26, 78-79, 119-150` turns the null
  descriptor into `platform: null`, `features: []`, `dispatchEligible: false` and
  `unavailableReason: "stale"`.

### 3. How a single failed probe becomes a settings-changed note

- `server/Application/Services/OrchestratorInstructionsHostedService.cs:18-33` reconciles every
  `SweepSeconds`. The default is 60 (`server/Application/Settings/OrchestratorInstructionsSettings.cs:12`).
  Runner-eligibility signals also trigger a reconcile
  (`server/Application/Services/CompositeRunnerEligibilityObserver.cs:13-17`).
- `server/Application/Services/OrchestratorInstructionsSnapshotBuilder.cs:107-121` calls
  `SessionRunnerCatalogue.ListAsync` live on every build. `ToRunnerAsync` (lines 125-157) copies
  `Platform`, `DispatchEligible` and `Features` into the snapshot.
- `server/Application/Services/OrchestratorInstructionsService.cs:156` compares only the
  rendered version. Any difference writes a new row and an `OrchestratorInstructionsRegenerated`
  incident (lines 195-203), then queues the notice (`QueueNoticesAsync`, lines 206-207).
- `server/Application/Services/OrchestratorInstructionsDelta.cs:129-138` writes the exact three
  lines seen in the notes: `platform`, `dispatch-eligible` and `features`.

Nothing smooths the change. One failed probe produces one note. The next good probe produces
the reverse note.

### 4. Dispatch is refused during the window

- `server/Application/Services/AgentTaskDispatcher.cs:6334-6366`. `ClassifyRequiredPlatformAsync`
  calls the same `_runners.DescribeAsync(null, …)` (line 6342) for a desktop-bound task with a
  `RequiredPlatform`.
  - A null platform, or `DispatchEligible == false`, returns a Hold: `runner platform evidence
    for Windows is not current on 'desktop'` (lines 6362-6363).
  - Windows-pinned work therefore waits for a later tick. That includes the RC Windows
    qualification.
- Other callers of the same descriptor read the same transient null and decide on it:
  - `DelegationWorktreeService.cs:305`
  - `TerminalRunnerSeatReleaseService.cs:343, 515, 817, 1088`
  - `TaskParkPublicationService.cs:50, 252`
  - `HostStatsPollService.cs:134-150`, which forces desktop `available`/`eligible` true but
    still loses platform and features.

### 5. The trigger: desktop saturation

- **Host-stats series** (`GET /api/hosts/desktop/stats/series?metric=cpu|memory&window=30m`,
  read at 20:54Z). The 20:23:15Z note sits in a saturated stretch:
  - CPU was 84% at 20:24:02Z, then 100% from 20:24:06Z to 20:24:39Z, then 88–97% until
    20:24:59Z.
  - Memory used rose from 27.2 GB to 28.5 GB (of 34.3 GB). Windows commit charge was 35.2 GB
    of 54.7 GB at 20:51Z.
  - The runner's 5 s samples have gaps of 9.0 s (20:24:25→20:24:34Z) and 7–10 s around
    20:30–20:31Z. Over the 30-minute ring, CPU p50 was 44% and p90 was 81%.
- **Live monitor**, run by this investigation: `GET /api/session-runners` and
  `GET /api/hosts/stats` every 2 s, 20:55–21:14Z, about 550 samples.
  - At 21:08:20Z both endpoints took 22.0 s and 20.0 s, and desktop host-stats was
    `state=stale`.
  - The following samples took 11.6 s, 11.1 s, 3.7 s and 2.0 s, with CPU at 100%.
  - 21:03:32Z took 2.4 s at CPU 100%.

  So the desktop server process itself stalls for more than 2 s under this load, and a
  capabilities probe that is in flight during such a stall fails at its 2 s deadline. No
  `platform: null` was caught in the window. The stall answers came back after it ended and
  probed cleanly.
- A separate result: one 502 at 20:57:43Z after 6.3 s never reached the server log, which
  has no `/api/session-runners` request in that second. It is a front-proxy failure, not this
  bug.

### 6. Hypotheses ruled out

- **Runner restart.** `runnerCapabilities.build.processStartUtc` was
  `2026-10-19T15:56:41.3474292Z` in all three diagnostics bundles (20:53Z, 20:57Z, 21:14Z). No
  flap from 2026-10-19 16:24Z through 2026-10-21 20:23Z coincides with a runner restart.
- **A registration from another source overwriting the record.** The desktop branch of
  `DescribeAsync` (lines 747-772) never reads `_slots`. A `server2-temp` or E2E phone-home
  registration lands in its own slot. `server2-temp` is listed separately as stale and
  draining. An E2E isolated runner uses its own random port, while `_local` is fixed at
  `SessionRunner:BaseUrl` (17204). Neither can change the desktop row.
- **A missed heartbeat or lease expiry.** The desktop has neither. That is exactly why it has
  no grace period.
- **The settings generator reading a partial DB row.** The runner rows come from the live
  catalogue, not from the DB. The two-version alternation shows that only the probe-derived
  fields change.

## Remaining uncertainties

1. **Which probe failure fired each time.** The candidates are the 2 s deadline, the limiter
   (32 permits, queue 0) or an open circuit. From server2, the desktop server log is reachable
   only through the diagnostics bundle tail, which holds about 2,000 lines (about 2–4 minutes)
   and missed every flap window. To settle it, run `scripts/logs.ps1 -Source desktop-server` on
   the desktop and look at 2026-10-21 20:21–20:24Z and 19:53–19:56Z (local time is +01:00):
   - `Resilience outcome runner.get-capabilities dependency runner outcome canceled … elapsed ~2000`
     means the deadline (logged at `AdmittedReadHandler.cs:87-95`);
   - `Resilience circuit runner transition open` means an open circuit;
   - neither line, with a flap, means the limiter, which only increments telemetry.

   The fix below covers all three, so this does not block planning.
2. **Whether the stalls are CPU saturation or thread-pool starvation in the server.** The
   series shows CPU at 100%. Starvation is not measured.
3. **Exact flap times.** They come from note delivery times. Coalesced notes ("changed more
   than once") hide some flaps. The card's 17:20, 18:08, 18:44, 19:48 and 19:52Z times come
   from another observer and do not all match this transcript.

## Not done, noted

- Fix idea (not designed): keep a last-known-good desktop capabilities snapshot with an
  observed-at and a grace TTL, like the remote slot's `RegisteredPlatform`/`PlatformObservedAt`.
  Report stale only after N consecutive failed probes or after the TTL. Log the failed probe's
  reason.
- Desktop log retrieval from a Linux runner is limited to a roughly 4-minute bundle tail. That
  is a recurring gap for investigations like this one.
- The roughly 2.5 runner `404: Session … was not found` lines per second, for six session ids
  the runner does not know (bundle `runner-log.txt`, 20:40–21:14Z), add load. They are not
  shown to cause this.
