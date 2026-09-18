# CARD-0511 investigation: restart loop from SessionRunner/Server capability desync (2026-09-13)

Date: 2026-09-18. Card: CARD-0511 "Restart loop: SessionRunner/Server capability desync after
sessionGenerationV1 (2026-09-13, distinct from CARD-0509)". Stage: Investigate (task 2198db1d).

## Verdict

Root cause **confirmed** from stored evidence (AgentIncidents rows, the runner's own Serilog log,
the daemon supervisor log, the watchdog log, the main checkout's reflog, and the code at
`8ccdb1c9` and at HEAD `3a62074e`). Two mechanisms combined:

1. **A correct fail-closed gate met a stale runner.** The 14:59:17Z AppHost restart was the first
   server build carrying the CARD-0502 gate (`64430137`, "no generation-bearing launch without
   `sessionGenerationV1`"). The always-on runner was still the 00:12Z build from `9ebbba7`, which
   predates the gate. Every launch of the standing orchestrator from 15:04:50Z to 15:49:18Z was
   refused, correctly.
2. **The server's capability snapshot is stale-while-revalidate and lives for the process.** After
   the operator rebuilt the runner (listening 15:55:09Z), the next attempt at ~15:55:4xZ was decided
   on the *previous* runner's snapshot: `SessionRunnerHttpClient` refreshes only in the background
   once any snapshot exists, and the launch path's client instance is captured for the server's
   lifetime by the singleton `AgentProtocolAdapterFactory`. That wasted attempt was the 10th
   consecutive failure, which escalated the ladder to 1.4 h. The AppHost restart at 15:57:04Z
   "fixed" it only because a new process has an empty cache and therefore *waits* for a fresh probe.

The card's step 4 ("the Server cached/pinned the runner's capability set") is right in effect but
the belief is not permanent: it is a 5-minute TTL snapshot that is never awaited after the first
fill, so the first attempt after any runner replacement can fail, and with the backoff ladder that
one failure costs the next 1.4 h.

## Evidence still available (checked 2026-09-18)

| Source | State | Notes |
|---|---|---|
| `server/logs/antiphon-20260913.log` | **gone** | `Serilog:RetainedFileTimeLimitDays` is 5 (`server/appsettings.json:289`); oldest surviving file is `antiphon-20260914.log`. |
| `AgentIncidents` rows, agent `a392cbc4` (Antiphon-Orchestrator) | **intact** | 21 rows in 15:01–15:56Z plus the recovery rows through 16:55Z. |
| `%TEMP%\antiphon-logs\session-runner-20260913.log` | **intact** | 14-day retention (`docs/bootstrap.md:419`), 4.99 MB. Timestamps are local (+01:00). |
| `logs/session-runner.20260917-081914.log` (daemon stdout roll) | **intact** | Covers 2026-07-16 → 2026-09-17; has the supervisor's 15:54Z rebuild milestones. |
| `logs/watchdog-apphost.log` | **intact** | Records both AppHost restart lock stamps that day. |
| main checkout reflog (`C:\src\Antiphon`) | **intact** | Pins when master moved to `2fb81db3`, `565232bd`, `8ccdb1c9`. |
| `AgentSessions` row `b474fa57` | overwritten | One row per conversation; it was re-launched in place at 16:31Z, so the 15:xx failure fields are gone. |

## Timeline (UTC)

| Time | Event | Source |
|---|---|---|
| 09-12 23:52 | `9ebbba77` committed (runner's build commit). | `git log` |
| 00:12 | Old runner starts, built from `9ebbba7` ("Now listening" 01:12:29 +01:00). | runner log line 281 |
| 07:24 | CARD-0502 fast-forwarded onto master (`2fb81db3`, includes gate commit `64430137`). **No AppHost restart followed**; only two restart lock stamps exist that day (14:59:17Z, 15:57:04Z). | reflog; watchdog log |
| 09:08:59 | Fresh conversation `b474fa57` accepted and launched; Recovered 09:19:34. Consistent with the running server predating the gate. | incidents Kind 59, 3 |
| 14:58:01 | Land of CARD-0498 merged in main checkout (`565232bd`). `64430137` is an ancestor of `565232bd`. | reflog; `git merge-base` |
| 14:59:17 | `restart-apphost.ps1` (lock stamp, PID 47340): **first server build with the gate**. Runner preserved by design; `check-daemon-build.ps1` (called at `scripts/restart-apphost.ps1:170`) only *warns* about a stale runner. | watchdog log |
| 15:04:49.8 | `PolicyRefreshService` killed the healthy orchestrator session (docs changed) and queued its resume; `PolicyRefreshed` (Kind 49) is written only after `control.StartAsync` returned (`PolicyRefreshService.cs:410-420`). | incidents |
| 15:04:50 | Queued launch → gate → first probe on this server instance (cache empty, awaited) → old runner lacks `sessionGenerationV1` → `RunnerCapabilityMismatchException` → session marked Failed with the message (`AgentSessionService.cs:366-388`). Supervisor tick 15:04:50.56: Crash (Kind 0) + "Restart attempt 2 … 10s". | incidents |
| 15:05:18 → 15:49:18 | Attempts 2–9 die identically; ladder 10s, 20s, 40s, 1.3m, 2.7m, 5.3m, 10.7m, 21.3m, 42.7m (attempt 10 scheduled 16:31:58Z). All correct refusals: the runner really was `9ebbba7`. | incidents Kind 0/2 |
| 15:54:37 | Operator's `restart-session-runner.ps1`: supervisor `service-exit`; build 15:54:41–15:54:58; new runner PID 12244 (`1.0.0+8ccdb1c9…`), "Now listening" 15:55:09.4Z. | daemon stdout roll lines 113379-113405; runner log 29741 |
| ~15:55:1x–4x | A launch attempt runs **36 min ahead of the scheduled 16:31:58Z** (most likely a manual Start: `AgentControlService.StartAsync` clears supervision state for manual semantics, `AgentSupervisorService.cs:276-277`; the server log that would prove it is gone). The gate refuses with the **old** build text "built from 9ebbba7 on 2026-09-13 00:12 (running since 00:12)". | incidents |
| 15:55:48 | Crash + "Restart attempt 11 scheduled for 17:21:07Z (backing off 1.4h)" + BackoffEscalated "10 consecutive failures; hourly-or-slower". | incidents Kind 0/2/4 |
| 15:57:04 | `restart-apphost.ps1` (lock stamp, PID 30988). HEAD was `8ccdb1c9` (moved 15:24:57Z, next move 16:05:18Z). | watchdog log; reflog |
| 15:59:10 | New runner starts tailing `b474fa57`'s transcript = successful launch. | runner log (16:59:10 +01:00) |
| 16:09:58 | "Recovered: running healthily for 10 min after 0 failure(s); backoff reset." | incidents Kind 3 |

The runner log contains **no** line naming `b474fa57` between 09:09Z and 15:59Z other than transcript
tailing, and no launch/kill lines at all in 15:03–15:06Z or 15:54–15:56Z: every refusal happened
server-side before any POST (as `SessionRunnerGenerationWireTests.cs:68` asserts), and the runner does
not log kills by session id, so the 15:04:49Z kill is inferred from the incident ordering.

Precise count: 10 deaths, attempts 2–11 scheduled (the card's "13+" overstates; 13 is the morning
CARD-0509-adjacent loop's attempt number at 09:02Z).

## Why the identical error text after the runner rebuild (the actual mechanism)

Code cited at HEAD `3a62074e`; the capability region is byte-identical to `8ccdb1c9` (the only diffs
since are the pane-disposal additions).

- Gate: `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:76-79` — `StartAsync`
  calls `GetSessionGenerationCapabilityMismatchAsync` whenever `spec.AcceptedStartedAt` is set.
  It is **always** set: `AgentSessionService.cs:443` does `spec = spec with { AcceptedStartedAt =
  acceptedGeneration }` and `acceptedGeneration` is `acceptedGeneration ?? session.StartedAt`
  (`:356`), so fresh launches are gated too.
- Snapshot: `SessionRunnerHttpClient.cs:17` `CapabilityProbeTtl = 5 min`; `:33-36` the cache fields;
  `:198-216` `EnsureCapabilitiesProbedAsync`: if the snapshot is older than the TTL it starts a
  background probe and **only awaits it when `_cachedCapabilities` is null**. `:183-196` then reads
  the cache and refuses with `DescribeBuild(cached?.Build)` (`:338-345`), which is where "built from
  9ebbba7 … running since 00:12" came from. A snapshot of the new runner would have printed
  `8ccdb1c9` and `15:54`. This proves the decision at 15:55:4xZ used a pre-restart snapshot.
- Lifetime: `AddHttpClient<ISessionRunnerClient, SessionRunnerHttpClient>` (`server/Program.cs:259`)
  is a transient typed client, but `AgentProtocolAdapterFactory` is a singleton (`Program.cs:258`)
  that captures one instance (`AgentProtocolAdapterFactory.cs:13,26`) and hands it to every
  `RunnerClaudeAdapter` (`:35-37`) → `RunnerTerminalSession` (`RunnerClaudeAdapter.cs:34`) →
  `_client.StartAsync` (`RunnerTerminalSession.cs:37`). The launch worker resolves adapters through
  that factory (`AgentSessionService.cs:185, 442`). So the launch path's snapshot lives as long as
  the server process — which is why an AppHost restart, and nothing less, emptied it.
- The comment at `:271-273, :296-297` states the design intent: "The first launch waits for its
  bounded probe … later stale snapshots refresh in the background". That is safe for a runner that
  only ever gets *older*; it is wrong for the one event this gate exists for, a runner being replaced.
- Nothing invalidates the snapshot on runner replacement. The server learns the runner's identity on
  every supervisor tick (`AgentSupervisorService.cs:88 ListAsync`) and `RunnerBuildDto.ProcessStartUtc`
  is in the DTO, but no code compares them. The transcript-format gate (`:275-317`) and the two
  delivery-profile caches (`PtyDeliveryProfile.cs:38,115-118`; `SessionDeliveryProfile.cs:20,108-109`)
  share the same pattern.

## Why one wasted attempt cost 1.4 hours

- `RestartFailurePolicy.Classify` (`server/Application/Services/RestartFailurePolicy.cs:12-24`) has
  no case for `RunnerCapabilityMismatchException`, so it is `Unknown`; `Charge` (`:50-56`) increments
  `RestartBackoffFailures` for everything except `ContinuityUnavailable`. The launch worker stores that
  kind on the session (`AgentSessionService.cs:381`) and the supervisor charges it on the next tick
  (`AgentSupervisorService.cs:216-221`). Ladder: `BackoffBaseSeconds=5 · 2^n` (`SupervisionSettings.cs:17`,
  `AgentSupervisorService.cs:460-463`), hourly tier at ≥ 1 h (`:29, :465-482`).
- A stale runner is therefore paced like a crash loop: the ladder keeps doubling while the cause is
  entirely external, and the one attempt that could have succeeded (post-rebuild) both failed on the
  stale snapshot and pushed the next try out to 17:21Z.
- The standing-agent launch path never records the dedicated `RunnerBuildStale` incident (Kind 29):
  `RecordRunnerBuildStaleAsync` is called only from the card-launch `StartAsync` catch
  (`AgentSessionService.cs:287-288`), not from `LaunchInteractiveProcessAsync`'s catch (`:366-388`).
  The DB has **zero** Kind-29 rows ever. The operator saw only generic "Session died" crashes.

## Answers to the card's questions

1. **Why was the runner not rebuilt when the repo advanced?** By design. The runner is owned by the
   "Antiphon Session Runner" Scheduled Task and is rebuilt only by `restart-session-runner.ps1`
   (`run-daemon.ps1 -BuildProjectDir`). `restart-apphost.ps1` preserves it (`:10-13, :89-92, :167-169`)
   and calls `scripts/check-daemon-build.ps1` (`:170`), which prints `WARNING: session-runner is stale
   for N source change(s)` (`check-daemon-build.ps1:73`) and nothing more. `docs/apphost-runbook.md:100-104`
   documents exactly this ("AppHost restart does not pick up session-runner source"). There is no
   server-side mechanism either. On 09-13 the warning would have gone to the console of whoever ran
   the 14:59Z land restart; it is not captured anywhere that survives.
2. **Should a runner rebuild alone clear the belief?** Today it clears it only for the *second*
   launch after the rebuild (or after ≤ 5 min plus one attempt). The refusal needs fresh evidence:
   either the gate re-probes synchronously before refusing, or the snapshot is invalidated when the
   runner's identity (`Build.ProcessStartUtc`/commit, or the supervisor's per-tick `ListAsync`
   reconnect) changes. `AgentSessionService`/`AgentControlService` hold no belief of their own; the
   belief is inside the one `SessionRunnerHttpClient` captured by the singleton factory.
3. **Would CARD-0510 (fresh session after 3 failures) have helped?** No. A fresh launch also carries
   `AcceptedStartedAt` (`AgentSessionService.cs:356, 443`) and would have been refused identically;
   it would only have discarded the conversation. The 153% contextFullness reading is unrelated to
   this failure mode.

## Blast radius on 09-13

Zero `AgentSessions` rows carry the "does not advertise" text, zero sessions were created between
14:55Z and 15:58Z, and zero incidents for other agents mention it: the standing orchestrator was the
only launch attempted in the window. Any delegate dispatch in that window would have been refused the
same way (and *would* have produced a Kind-29 incident via the card-launch path).

## Secondary finding (code, not observed on 09-13)

`ProbeCapabilitiesAsync` (`SessionRunnerHttpClient.cs:319-336`) writes `null` into the cache on any
probe failure (5 s timeout, connection refused during a runner restart). With a null snapshot and a
completed probe, `EnsureCapabilitiesProbedAsync` neither starts a new probe (TTL not expired) nor
waits, so every gated launch for up to 5 min is refused as "does not advertise sessionGenerationV1"
with no build suffix. The 15:55:48Z message *had* the suffix, so this did not fire that day, but it
is the same class of hazard and will fire whenever a launch coincides with a runner restart window.

## Test coverage today

- `SessionRunnerGenerationWireTests.cs:66-96` covers only the empty-cache path (first probe awaited,
  refusal before any POST). `SessionRunnerCapabilityGateTests.cs` covers transcript-format refusal.
- No test replaces the runner behind an existing snapshot and asserts the next launch re-probes.
  CARD-0502's own tests sidestep the cache on purpose ("probe attach capability on a fresh client so
  the cache cannot hide the GET", `1b2cd473`).

## Current state (2026-09-18)

Live runner is `27daabe5`, live server `aa02c398`, main checkout HEAD `3a62074e`; the runner has no
newer runner-source commits, the server is 5 commits behind. Drift between the three is the normal
operating condition here and remains unguarded except by the console warning.

## Remaining uncertainties

- What triggered the 15:55:4xZ attempt 36 min early: most likely a manual Start (it clears
  `NextRestartAt`); the server log that would show the request is gone.
- The exact probe schedule between 15:49Z and 15:55Z (other launches on the same client instance
  could have refreshed the snapshot earlier); irrelevant to the conclusion, since any probe before
  15:54:37Z returned the old DTO and a probe during the down window would have blanked the suffix.
- Whether the 14:59Z restart printed the stale-runner warning (console only).

## Not done, noted

- Fix idea (one line, for Plan): make the gate refuse only on fresh evidence (synchronous re-probe
  before refusing, and/or invalidate the snapshot when the runner identity seen by the supervisor's
  tick changes), classify `RunnerCapabilityMismatchException` as an infrastructure hold that does not
  charge the ladder and re-arms when the runner changes, record `RunnerBuildStale` on the standing
  launch path, and keep a failed probe from overwriting a good snapshot with null.
