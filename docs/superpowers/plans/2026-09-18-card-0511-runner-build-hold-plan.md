# CARD-0511: launch on fresh runner evidence; hold, don't ladder, on a stale runner

Plan task `9edf8224`, 2026-09-18, inspected checkout `3a62074e` (master) plus the investigation
commit `8c312c48` cherry-picked onto this branch. Investigation:
[2026-09-18-card-0511-runner-capability-desync-restart-loop.md](../../investigations/2026-09-18-card-0511-runner-capability-desync-restart-loop.md)
(task `2198db1d`). Design authority this plan builds on: CARD-0502 (the `sessionGenerationV1`
gate stays fail-closed), CARD-0160/0112 (null capabilities are no evidence, never a silent
fallback), CARD-0466 (`StandingContinuityState` is the precedent for a durable, non-charging
supervision hold).

## Disposition in five lines

1. **A launch decision is made on evidence gathered for that decision.** `SessionRunnerHttpClient`
   takes one bounded `GET /capabilities` per `StartAsync` / `AttachHerdrAsync` /
   `GetSessionBackendCapabilityMismatchAsync` and evaluates every gate (grok rules, transcript,
   herdr backend, named tab, generation) as a pure function of that answer. The 5-minute
   stale-while-revalidate snapshot no longer feeds any launch permission or refusal (D-1). That
   removes the 09-13 wasted attempt and also the mirror hole (a stale *positive* letting a
   downgraded runner silently open a pty-host).
2. **Runner identity is carried, not cached away.** Every successful probe yields
   `RunnerIdentity` = `{CommitSha ?? InformationalVersion}@{ProcessStartUtc:O}`;
   `RunnerCapabilityMismatchException` carries the identity and build it was decided on. Fix
   point 2 (invalidate on identity change) is satisfied by D-1 plus D-4: no decision trusts a
   snapshot it did not take, and the supervisor compares identities explicitly (D-2).
3. **A stale runner is a hold, not a crash.** `RestartFailurePolicy.Classify` maps
   `RunnerCapabilityMismatchException` to a new `RestartFailureKind.RunnerBuildStale`; `Charge`
   skips the ladder for it (as it does for `ContinuityUnavailable`). The standing agent enters a
   durable **runner-build hold** on `AgentSupervisionState` (three nullable columns) that the
   supervisor releases the moment `GET /capabilities` reports a different runner identity, arming
   an immediate retry. A manual Start clears the hold. No `RestartScheduled`/`Crash` rows are
   written while held (D-3, D-4). `Infrastructure` was not an option: it also charges
   `RestartBackoffFailures` (`RestartFailurePolicy.cs:50-56`), so the 1.4 h ladder would recur.
4. **Kind-29 `RunnerBuildStale` is recorded on the interactive/standing launch path** from the
   same catch that classifies the failure, through the existing `RecordRunnerBuildStaleAsync`
   helper extended to accept the already-known agent id (D-5). The hold's release writes one Info
   `RunnerBuildReplaced` incident naming the new build.
5. **The remaining cache (watchdog transcript check only) keeps its last good answer** when a probe
   fails and re-probes on the next call instead of serving null for five minutes (D-6).

Fix points 1+2 land as slice S1, fix point 5 as S2, fix points 3+4 as S3, docs as S4. S1 and S2
are independent of S3 in code; S3 consumes the identity S1 puts on the exception, so land order
is S1 → S2 → S3 → S4, each green alone. Nothing needs an atomic landing.

## Ground truth

| Card / investigation / brief assumption | Observed on `3a62074e` | Consequence |
|---|---|---|
| "The Server cached/pinned the runner's capability set." | `CapabilityProbeTtl = 5 min` (`SessionRunnerHttpClient.cs:17`); `EnsureCapabilitiesProbedAsync` (`:198-216`) starts a background probe when stale and awaits it **only when `_cachedCapabilities` is null**; the launch path's client instance is captured for the process by the singleton `AgentProtocolAdapterFactory` (`Program.cs:258-259`, `AgentProtocolAdapterFactory.cs:13,26`). | Not a pin, a stale-while-revalidate snapshot with process lifetime. D-1 stops launch decisions reading it at all rather than tuning the TTL. |
| Brief fix 3: "classify the mismatch as a distinct, non-charging infrastructure-hold case (not Unknown)." | `Classify` has no case for `RunnerCapabilityMismatchException` → `Unknown` (`RestartFailurePolicy.cs:12-24`). But `Charge` increments `RestartBackoffFailures` for **every** kind except `ContinuityUnavailable` (`:50-56`), and `Backoff(state.RestartBackoffFailures)` is the 1.4 h ladder (`AgentSupervisorService.cs:220, 460-463`). `Infrastructure` would have laddered identically. | The only non-charging precedent is a durable hold (`StandingContinuityState`). D-3 adds `RunnerBuildStale` as a second non-charging kind with its own hold and release signal. |
| Brief fix 4: "Kind-29 is recorded only on the card-launch path; DB has zero rows." | Writers: card path `AgentSessionService.cs:287-288`, dispatcher watchdog `AgentTaskDispatcher.cs:1417`. The interactive catch `AgentSessionService.cs:366-388` writes `RestartFailureKind`, `FailureReason`, the herdr evidence and the continuity hold, but no incident. | D-5 adds the call in that catch; the helper `RecordRunnerBuildStaleAsync` (`:842-875`) resolves the agent through `AgentTasks`, which a standing session has none of, so it gains an optional known `agentId`. |
| Brief fix 5: "a failed probe overwrites a good snapshot with null and is served for 5 min." | `ProbeCapabilitiesAsync` (`:319-336`) writes `result` unconditionally; `_capabilitiesProbedAt` is stamped **before** the probe (`:207`), so a failed probe pins the null for the TTL. | D-6. After D-1 this cache serves only `GetTranscriptCapabilityMismatchAsync` for the dispatcher watchdog (`AgentTaskDispatcher.cs:1403`); still worth fixing, small. |
| "SessionRunnerGenerationWireTests covers only the empty-cache path." | Every test constructs a fresh client via `Client(handler)` (`:174-176`), so no test ever has a filled snapshot; `SessionRunnerCapabilityGateTests` likewise. | S1 adds tests that fill the snapshot with one runner and answer the next probe as another (both directions), asserting the GET count and the build text in the refusal. |
| Investigation: the supervisor "learns the runner's identity on every tick (`ListAsync`)". | `TickAsync` calls `ListAsync` for reachability only (`AgentSupervisorService.cs:86-99`); `RunnerSessionDto` carries no build. Identity lives only in `RunnerCapabilitiesDto.Build` (`SessionRunnerContracts.cs:942, 955-959`), resolved per process by `RunnerBuildIdentity.Resolve()` (`ProcessStartUtc` = `Process.GetCurrentProcess().StartTime`). | D-4: while any agent is held, the tick issues one bounded `GetCapabilitiesAsync` (the direct, uncached GET) and compares identities. Zero extra traffic when nothing is held. |
| Every standing launch is generation-gated. | `LaunchInteractiveAsync` sets `generation = acceptedGeneration ?? session.StartedAt` (`AgentSessionService.cs:356`) and `spec with { AcceptedStartedAt = acceptedGeneration }` (`:443`); `StartAsync` gates on `spec.AcceptedStartedAt is not null` (`SessionRunnerHttpClient.cs:76-79`). | The hold must cover resume and Fresh alike; CARD-0510 would not help (investigation Q3). |
| Herdr attach has the same shape. | `AgentControlService.cs:797` refuses attach on `GetSessionBackendCapabilityMismatchAsync` (cached) and only then does a fresh `GetCapabilitiesAsync` (`:800`); `AttachHerdrAsync` (`SessionRunnerHttpClient.cs:733-739`) gates generation on the cache too. | S1 moves both public decision methods to the fresh probe; no change in `AgentControlService`. |
| A manual Start re-arms supervision. | `ClearSupervisionLatchAsync` clears `Suspended`, `NextRestartAt`, `LivenessLatchedAt` (`AgentControlService.cs:1061-1073`); `AgentControlService.StartAsync` enqueues the launch (`:527, :616`), so the mismatch reaches the supervisor via `session.RestartFailureKind`, not a synchronous throw. | D-3(v): the same method clears the runner-build hold. The `RecordStartFailureAsync` branch is defensive only. |
| The hold must be visible. | `AgentSupervisionDto` carries `ContinuityHeldAt/SessionId/Reason/Evidence` (`AgentDtos.cs:256`, `AgentService.cs:234`); the client reads `supervision.continuityHeldAt` (`agents.ts:258`, `AgentsPage.tsx:241`, `StandingSessionRecovery.tsx:14`). | D-3(vi): two DTO fields + client type + one notice line; no new page. |
| Test harness can drive this. | `AgentSupervisionTests.BuildHarness(..., runner: ISessionRunnerClient)` (`:585-586, :660`); `FakeSessionRunnerClient.GetCapabilitiesAsync` returns a fixed DTO (`FakeSessionRunnerClient.cs:44-61`); `FakeAgentProtocolAdapter.ThrowOnStart` injects launch failures (`StandingRestartAccountingTests.cs:33`). | S3 tests use a runner fake with a mutable `Build` and an adapter that throws `RunnerCapabilityMismatchException` with a chosen identity. |

## Decisions

**D-1. One fresh, bounded probe per launch decision; gates are pure over its answer.**
`SessionRunnerHttpClient` gains `ProbeForDecisionAsync(ct)` → `RunnerCapabilityProbe`
(`Capabilities: RunnerCapabilitiesDto?`, `Identity: string`, `Unreachable: Exception?`), bounded by
the existing 5 s `CapabilityProbeTimeout`. Outcomes: **answered** (200 + body) → evaluate gates;
**older runner** (404 on `/capabilities`) → `Capabilities = null`, same "no evidence" semantics as
today; **unreachable** (connection refused, timeout, 5xx, malformed body) → `Unreachable` set.
`StartAsync` / `AttachHerdrAsync` / `GetSessionBackendCapabilityMismatchAsync` call it once and
pass the DTO to static gate functions (`TranscriptMismatch(dto, kind)`, `HerdrBackendMismatch(dto)`,
`NamedTabMismatch(dto)`, `GenerationMismatch(dto)`, grok-rules check) which keep today's per-gate
null semantics (transcript: null → launch; the others: null → refuse). A successful probe also
updates the cached snapshot (so the watchdog's view is never older than the last launch).
*Why:* the gate exists for exactly one event, a runner being replaced, and that is the one event a
TTL cache cannot see. One localhost GET per launch is noise; launches are rare and already pay a
runner POST. *Rejected:* (a) keep stale-while-revalidate and await only before refusing: leaves a
stale positive free to launch onto a downgraded runner (the CARD-0160 silent pty-host), and adds a
second code path to reason about; (b) a shorter TTL: still a window, still the wasted attempt;
(c) invalidate the cache from an external identity signal: a cross-component hook so the launch
path can keep trusting a snapshot it did not take.

**D-2. When the probe is unreachable and a positive-evidence gate applies, refuse with a new
`RunnerUnreachableException(message, inner)`; it classifies as `Infrastructure`.** The inner
transport exception is preserved so `Flatten` already sees it; `Classify` also names the type
explicitly. When only the transcript gate applies (no generation, no herdr) the launch proceeds to
the POST, which fails or succeeds on its own. *Why:* "does not advertise sessionGenerationV1" with
no build suffix (the investigation's secondary finding) is the wrong sentence for "nobody answered";
the runner-restart window is transport, paced by the ordinary ladder like every other transport
failure, and must never become a runner-build hold. *Rejected:* fold it into
`RunnerCapabilityMismatchException` with a flag — the two have different classifications and
different operator actions (wait vs rebuild).

**D-3. `RunnerCapabilityMismatchException` → `RestartFailureKind.RunnerBuildStale`, non-charging,
backed by a durable runner-build hold.**
- `RestartFailureKind` gains `RunnerBuildStale`; `Classify` returns it when the chain contains the
  exception; `Charge` returns early for `ContinuityUnavailable` **or** `RunnerBuildStale`.
- `AgentSupervisionState` gains `RunnerBuildHeldAt (DateTime?)`, `RunnerBuildHeldIdentity
  (string?, max 200)`, `RunnerBuildHoldEvidence (string?, max 1000)`; migration
  `AddRunnerBuildHold`.
- New `RunnerBuildHoldState(AppDbContext, TimeProvider)` mirroring `StandingContinuityState`:
  `HoldAsync(agentId, sessionId, identity, message)` sets the three fields, `NextRestartAt = null`,
  saves; `Clear(state)`; `ReleaseAsync(agentId, observedIdentity, now)` clears, sets
  `NextRestartAt = now`, writes `AgentIncidentKind.RunnerBuildReplaced` (Info, new value 68:
  "Runner replaced: built from X, running since Y; retrying the standing session now").
- Where the hold is placed: (i) `LaunchInteractiveAsync` catch, next to the
  `ContinuityUnavailable` line: `if (!stoppedCheck && kind == RunnerBuildStale &&
  agent?.PersistentSessionId == sessionId) → HoldAsync(agentId, sessionId, ex.RunnerIdentity,
  ex.Message)`; (ii) `SuperviseAsync` schedule branch, next to the `ContinuityUnavailable` branch:
  `dead?.RestartFailureKind == RunnerBuildStale` → `policy.Observe(state, dead)` (marks the
  generation consumed; `Charge` is a no-op) + `HoldAsync(..., identity: "unknown")` if not already
  held, return without scheduling; (iii) `RecordStartFailureAsync`: `ex is
  RunnerCapabilityMismatchException` → hold, return (defensive; the standing launch is queued, so
  this branch is not expected to fire); (iv) hold gate: `SuperviseAsync` returns early when
  `RunnerBuildHeldAt is not null`, after the herdr-hold block; (v) `ClearSupervisionLatchAsync`
  also clears the hold (manual Start = operator asserts the runner is fixed; the attempt then
  re-probes fresh under D-1 and re-holds if it is not); (vi) the live-session branch clears a
  leftover hold as hygiene; `AgentSupervisionDto` gains `RunnerBuildHeldAt`, `RunnerBuildHoldEvidence`,
  the client type gains the same, and the agent detail shows one line when held.
*Why:* the ladder paces crash loops; a stale runner is an external prerequisite that no retry can
change, so pacing it is pure loss, and retrying it unpaced is incident spam (two rows per attempt,
`IncidentCapPerAgent = 500`). The hold has a precise release signal (D-4), costs nothing while
waiting, and the incident trail is one Critical row on entry and one Info row on release.
*Rejected:* (a) `Infrastructure`: charges the ladder (ground truth row 2); (b) non-charging with a
fixed retry cadence: polls the stale runner every tick and floods incidents; (c) charge but reset
the counters on runner change: still wastes attempts, needs the same identity tracking, and leaves
the 1.4 h schedule in place until the reset.

**D-4. Runner identity is `{CommitSha ?? InformationalVersion}@{ProcessStartUtc:O}`; the
supervisor compares it once per tick while any agent is held.** `RunnerIdentity.Describe(
RunnerBuildDto?)` (static, `Application/Services`) formats it; `"unknown"` for a null build.
`TickAsync`, after `ListAsync` succeeds, loads the held agents; if any, issues one
`GetCapabilitiesAsync` under a 5 s linked token; for each held agent whose `RunnerBuildHeldIdentity`
differs from the observed identity (an `"unknown"` hold releases on any answered probe) →
`ReleaseAsync`. A failed probe keeps every hold. *Why:* a rebuild always changes `ProcessStartUtc`
and a real fix changes the SHA too; either is enough to justify exactly one fresh attempt, which
under D-1 either launches or re-holds on the new identity with a new Kind-29 row. Zero traffic when
nothing is held. *Rejected:* (a) release on the SSE reconnect in `SessionRunnerEventPump`: the pump
reconnects on any stream hiccup, and does not know builds; (b) a runner-side "started" event: needs
a runner change and a server that is up to hear it; (c) `RunnerStoreId`: a custody-store identity,
stable across rebuilds by design.

**D-5. Kind-29 on the interactive path is written by the existing helper, deduped per
session + message.** `RecordRunnerBuildStaleAsync(sessionId, message, ct, Guid? agentId = null)`:
a known id skips the `AgentTasks` lookup. Called from the interactive catch for every
`RunnerCapabilityMismatchException` (standing or not), before the hold. Dedup predicate for this
call site is `SessionId == sessionId && Kind == RunnerBuildStale && FailureReason == message`: the
message embeds the refused build and its start time, so a second stale build on the same standing
session produces a second row, while a manual retry against the same build does not.
`AgentTaskDispatcher`'s own writer is untouched. *Rejected:* a shared incident service for all three
writers — a refactor with no behaviour change; noted as follow-up.

**D-6. A failed background probe keeps the last good snapshot and marks the cache stale.**
`ProbeCapabilitiesAsync` writes `_cachedCapabilities` only when `result` is non-null; on null it
sets `_capabilitiesProbedAt = DateTimeOffset.MinValue` so the next `EnsureCapabilitiesProbedAsync`
starts a new probe rather than serving the miss for the TTL. The first-caller wait stays as it is.
*Why:* the cache now serves only the dispatcher's watchdog, but a null served for five minutes is
still a false "no capabilities" verdict to that watchdog.

**D-7. Out of scope, deliberately.** `PtyDeliveryProfile` / `SessionDeliveryProfile` caches (they
lower delivery ceilings on positive evidence only; a stale positive there cannot refuse a launch);
`restart-apphost.ps1`'s console-only stale-runner warning (card Q1: by design, documented in
`apphost-runbook.md:100-104`); any change to the ladder constants; CARD-0510; the transcript
watchdog's Kind-29 writer.

## Slices

### S1 — Fresh evidence per launch decision (fix points 1 and 2)

Files:
- `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs`: `ProbeForDecisionAsync`,
  static gate functions, `StartAsync` / `AttachHerdrAsync` / `GetSessionBackendCapabilityMismatchAsync`
  rewired; the private `GetNamedTabPlacementCapabilityMismatchAsync` /
  `GetSessionGenerationCapabilityMismatchAsync` / `EnsureCapabilitiesProbedAsync` usage on the launch
  path removed; refusal messages unchanged in wording except that `DescribeBuild` now always prints
  the build the decision was made on. Update the class comment at `:271-273`.
- `server/Application/Exceptions/RunnerCapabilityMismatchException.cs`: add `RunnerBuildDto? Build`
  and `string RunnerIdentity` (defaults keep the one-arg constructor for existing throw sites and
  tests).
- `server/Application/Exceptions/RunnerUnreachableException.cs` (new).
- `server/Application/Services/RunnerIdentity.cs` (new static helper).
- `server/Application/Services/RestartFailurePolicy.cs`: `RunnerUnreachableException` →
  `Infrastructure`.
- Tests: `tests/Antiphon.Tests/Agents/SessionRunnerGenerationWireTests.cs`,
  `tests/Antiphon.Tests/Agents/SessionRunnerCapabilityGateTests.cs`,
  `tests/Antiphon.Tests/Application/RestartFailureClassificationTests.cs`.

Test intent (names for TestDesign; each pins one sentence of D-1/D-2):
- `C511_V1_Launch_after_runner_replacement_reprobes_and_launches`: first `StartAsync` refused by a
  runner without the feature (snapshot filled, build A); handler then answers with build B carrying
  the feature; second `StartAsync` on the **same client** issues a second GET and POSTs `/sessions`.
  This is the 09-13 shape and the missing test named in the investigation.
- `C511_V2_Stale_positive_never_launches_onto_a_downgraded_runner`: snapshot positive (build B);
  handler switches to build A without the feature; next `StartAsync` is refused, the message names
  build A, no POST.
- `C511_V3_Unreachable_probe_on_a_gated_launch_is_RunnerUnreachable`: handler throws
  `HttpRequestException` for `/capabilities`; generation-bearing `StartAsync` throws
  `RunnerUnreachableException` with the transport exception inner, no POST;
  `RestartFailurePolicy.Classify` → `Infrastructure`.
- `C511_V4_Unreachable_probe_on_an_ungated_launch_proceeds`: Claude, pty-host, no generation;
  `/capabilities` unreachable; `/sessions` is POSTed.
- `C511_V5_Attach_and_backend_gate_use_the_fresh_probe`: `AttachHerdrAsync` and
  `GetSessionBackendCapabilityMismatchAsync` each issue their own GET after a snapshot exists.
- Existing `A_runner_without_sessionGenerationV1_refuses_..._before_any_POST` and the two
  `SessionRunnerCapabilityGateTests` keep passing unchanged (request lists still
  `["/capabilities"]` / `["/capabilities", "/sessions"]`).

### S2 — Cache hygiene for the watchdog path (fix point 5)

Files: `SessionRunnerHttpClient.cs` (`ProbeCapabilitiesAsync`, `EnsureCapabilitiesProbedAsync`);
tests in `SessionRunnerCapabilityGateTests.cs`.

Test intent:
- `C511_V6_Failed_probe_keeps_the_last_good_snapshot`: good snapshot; handler starts throwing; after
  the TTL (inject via a `TimeProvider` seam or by exposing the TTL to tests the way
  `PtyDeliveryProfile` takes `TimeProvider`), `GetTranscriptCapabilityMismatchAsync` still answers
  from the good snapshot.
- `C511_V7_Failed_first_probe_reprobes_on_the_next_call`: handler throws once, then answers; two
  calls → two GETs; the second call sees the answer.

Note for TestDesign: `SessionRunnerHttpClient` reads `DateTimeOffset.UtcNow` directly; S2 should
add an optional `TimeProvider` constructor parameter (default `TimeProvider.System`) so V6 does not
sleep. The DI registration needs no change (typed-client activation resolves `TimeProvider`).

### S3 — Runner-build hold on the standing path (fix points 3 and 4)

Files:
- `server/Domain/Enums/RestartFailureKind.cs` (+`RunnerBuildStale`),
  `server/Domain/Enums/AgentIncidentKind.cs` (+`RunnerBuildReplaced = 68`, doc comment).
- `server/Domain/Entities/AgentSupervisionState.cs` (+3 columns),
  `server/Infrastructure/Data/AppDbContext.cs` (max lengths), `server/Migrations/<stamp>_AddRunnerBuildHold.cs`
  + designer + snapshot.
- `server/Application/Services/RunnerBuildHoldState.cs` (new).
- `server/Application/Services/RestartFailurePolicy.cs` (`Classify`, `Charge`).
- `server/Application/Services/AgentSessionService.cs` (interactive catch; helper signature).
- `server/Application/Services/AgentSupervisorService.cs` (tick-level release, hold gate, schedule
  branch, `RecordStartFailureAsync` branch, live-session hygiene).
- `server/Application/Services/AgentControlService.cs` (`ClearSupervisionLatchAsync`).
- `server/Application/Dtos/AgentDtos.cs`, `server/Application/Services/AgentService.cs`,
  `client/src/api/agents.ts`, `client/src/features/agents/StandingSessionRecovery.tsx` (one notice
  line: held since, evidence, "rebuild the runner: restart-session-runner.ps1").
- Tests: new `tests/Antiphon.Tests/Application/StandingRunnerBuildHoldTests.cs` (harness:
  `AgentSupervisionTests.BuildHarness` with a runner fake whose `Build` is mutable and a
  `FakeAgentProtocolAdapter { ThrowOnStart = new RunnerCapabilityMismatchException(...) }`);
  `RestartFailureClassificationTests.cs`; a client test beside
  `StandingSessionRecovery.test.tsx`.

Test intent:
- `C511_V8_Mismatch_holds_without_charging`: standing agent, resume refused by mismatch (identity A)
  → `RunnerBuildHeldAt` set, `RunnerBuildHeldIdentity == A`, `RestartBackoffFailures` and
  `ConsecutiveFailures` unchanged, `NextRestartAt` null, exactly one Kind-29 row for the session
  (Critical), no `RestartScheduled` row for this generation.
- `C511_V9_Held_agent_is_not_scheduled_or_attempted`: several ticks over minutes → no new incidents,
  no adapter start, hold intact.
- `C511_V10_Runner_replacement_releases_and_retries_at_once`: runner fake reports identity B → next
  tick: hold cleared, `RunnerBuildReplaced` Info row, `NextRestartAt == now`; following tick
  attempts the resume (adapter started with `--resume`, same session id).
- `C511_V11_Unreachable_identity_probe_keeps_the_hold`: runner fake returns null for
  `GetCapabilitiesAsync` → hold intact, no incident.
- `C511_V12_Second_stale_build_reholds_with_a_second_incident`: after release, the attempt is
  refused again with identity C → held on C, a second Kind-29 row (different message), counters
  still unchanged.
- `C511_V13_Manual_start_clears_the_hold`: `Control.StartAsync(agent, new(), automatic:false)` →
  hold fields null before the launch is queued; the queued launch re-probes and either runs or
  re-holds.
- `C511_V14_Classification`: `Classify(new RunnerCapabilityMismatchException("x"))` →
  `RunnerBuildStale`; `Charge(state, RunnerBuildStale)` leaves both counters; wrapped in an
  `AggregateException` still classifies.
- `C511_V15_Interactive_mismatch_records_kind_29_for_any_agent`: non-standing agent's interactive
  launch refused → one Kind-29 row with the agent id (the helper's known-id path).
- `C511_V16_Hold_is_visible`: `GET /api/agents/{id}` shows `supervision.runnerBuildHeldAt` and
  evidence; client renders the notice.

Test-time process safety: these tests use `FakeAgentProtocolAdapter`, spawn nothing, and run under
`[NotInParallel]` like the other supervision suites (the supervisor sweeps every always-on agent in
the shared database).

### S4 — Docs

- `docs/session-runtime-invariants.md` § Standing conversation continuity: add "Runner build hold
  (CARD-0511)": a `RunnerCapabilityMismatchException` is `RunnerBuildStale`, never charged, held
  until the runner identity changes or a manual Start; launch decisions use a fresh probe.
- `docs/apphost-runbook.md` § "AppHost restart does not pick up session-runner source": what the
  standing agent now does (one Critical `RunnerBuildStale` incident, hold, automatic resume after
  `restart-session-runner.ps1`).
- `docs/agent-card-lifecycle.md` only if it lists supervision holds (check at Code time; the
  continuity hold's own doc home is `session-runtime-invariants.md`).

## Independence and landing order

| Slice | Depends on | Can land alone? |
|---|---|---|
| S1 (fix 1+2) | nothing | yes; removes the 09-13 wasted attempt by itself |
| S2 (fix 5) | nothing (same file as S1; trivial rebase either way) | yes |
| S3 (fix 3+4) | S1's `RunnerIdentity` on the exception (otherwise holds are `"unknown"` and release on any answered probe, which still converges but wastes one uncharged attempt per TTL until the launch path is fresh) | yes, but land after S1 |
| S4 | S3 | after S3 |

Recommended: one Code dispatch, four commits in this order, each with its own green run of the
named test files; Review once.

## Verification profile for Code

- Targeted: `dotnet run --project tests/Antiphon.Tests -- --treenode-filter
  "/*/Antiphon.Tests.Agents/SessionRunnerGenerationWireTests/*"`, the same for
  `SessionRunnerCapabilityGateTests`, `/*/Antiphon.Tests.Application/RestartFailureClassificationTests/*`,
  `/*/Antiphon.Tests.Application/StandingRunnerBuildHoldTests/*`, and
  `StandingRestartAccountingTests` + `StandingContinuityRecoveryTests` + `AgentSupervisionTests`
  as the regression set for the supervisor edits.
- Client: `pwsh -File scripts/test-client.ps1` for the `StandingSessionRecovery` test.
- Build to an alternate output path (`--property:OutputPath=bin-c511/`) while daemons hold their
  bins; delete the `bin-c511` directories after.
- Migration: `dotnet ef migrations add AddRunnerBuildHold --project server` from the worktree; the
  shared test Postgres applies it on the first run.

## Backlog (not this card)

- Consolidate the three Kind-29 writers into one `RunnerBuildStaleIncidents` service.
- `restart-apphost.ps1`: turn the stale-runner console warning into a captured line in
  `logs/watchdog-apphost.log` so a 09-13-style reconstruction has it.
- `PtyDeliveryProfile` / `SessionDeliveryProfile`: same TTL pattern; a stale positive there only
  keeps raised delivery ceilings on a downgraded runner, which the delivery watchdog already
  detects.

## Verification design (TestDesign, task 4e9f5c29)

TestDesign dispatch 2026-09-18 on branch tip `5eb6e033` (plan) over master `3a62074e`. Nothing
below changes D-1..D-7; it fixes the exact classes, methods, fixtures and assertions Code
implements for S1-S4, and the controls Mutation runs after land. Rules every row follows:

1. IDs are `V-511-n` (proves it works now), `R-511-n` (carried regression), `G-511-n` (guard)
   and `PC-511-n` (positive control), mapped 1:1 G→PC. The plan's `C511_V1..V16` intents keep
   their numbers as test method names; `V3b/V3c/V5b/V8b/V17/V18` are new methods this stage adds.
2. S1/S2 tests are wire tests over the real `SessionRunnerHttpClient` with the file-local
   `StubHandler` (records every request, answers from a delegate) and `StubFactory`, exactly as
   the existing tests in both files. No new fake; the handler's delegate reads a captured
   variable so the "runner" behind `/capabilities` can be switched between calls on ONE client.
3. S3 tests run the real `AgentSupervisorService`, `AgentControlService`, `AgentSessionService`
   and `AgentSessionLaunchQueue` over the shared test Postgres through
   `AgentSupervisionTests.BuildHarness(root, adapters, definitionKind: "ClaudeCode")`, with
   `FakeAgentProtocolAdapter` as the process (`ThrowOnStart` injects the refusal) and the
   harness's own `FakeSessionRunnerClient` (`h.Runner`) as the runner the supervisor probes.
   The new class is globally `[NotInParallel]` like `StandingRestartAccountingTests` (the
   supervisor sweeps every always-on agent in the shared database) and cleans up through
   `AgentSupervisionTests.CleanupAsync(root)`.
4. Time never waits on a wall clock: the harness's `MutableTimeProvider.Advance` drives the
   ladder; S2 uses `Microsoft.Extensions.Time.Testing.FakeTimeProvider` (already referenced,
   `Microsoft.Extensions.TimeProvider.Testing` 9.5.0) through the new optional constructor seam.
5. Method-scoped filters: `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c511/ -- --treenode-filter "/*/*/<Class>/<Method>"`;
   Vitest: `pwsh -File scripts/test-client.ps1 StandingSessionRecovery`.

### Settled here (the plan left these open or would trip Code)

- **Kind-29 on the interactive path must not be written from a second scope while the catch
  holds its `FOR UPDATE` locks.** `LaunchInteractiveAsync`'s catch opens `evidenceTransaction`
  and selects the `Agents` and `AgentSessions` rows `FOR UPDATE` before any bookkeeping.
  `AgentIncidents.AgentId` is a real FK (`AppDbContext` `HasOne(i => i.Agent)`), so an insert
  from another connection needs `FOR KEY SHARE` on that agent row and blocks behind `FOR UPDATE`
  until the catch commits, which it never does while awaiting the insert. The existing helper
  `RecordRunnerBuildStaleAsync` writes through `_scopeFactory.CreateAsyncScope()`, i.e. another
  connection. Settlement: the interactive call site calls the helper (with the known `agentId`)
  **after** `evidenceTransaction.CommitAsync` and before the `AgentChanged` publish; the hold
  (`RunnerBuildHoldState.HoldAsync` on the catch's own `_db`, like `StandingContinuityState`)
  stays inside the transaction, in the same commit as the Failed outcome. D-5's "before the
  hold" is relaxed to "in the same catch". A Code slip here shows up as V-511-8/V-511-15 failing
  with the 15 s `WaitForIdleAsync` timeout, not as a wrong assertion; that is the signature.
- **Hold gate placement (D-3 iv vs vi).** "After the herdr-hold block" read literally puts the
  gate before the live-session branch, which makes the (vi) hygiene unreachable. Settled: the
  gate is the first statement after the live-session branch returns (before the capacity-wait
  block). For a held agent without a live session the behaviour is identical; V-511-18 pins
  the hygiene.
- **Release and retry happen in the same tick.** `TickAsync` releases (D-4) after `ListAsync`
  and before the per-agent sweep, and `ReleaseAsync` sets `NextRestartAt = now`; the sweep in
  that same tick therefore finds the attempt due and enqueues it. `NextRestartAt == now` is not
  observable afterwards (the successful enqueue nulls it), so V-511-10 pins the release incident,
  the cleared hold and the enqueued `--resume` after one tick plus `WaitForIdleAsync`.
- **`FakeSessionRunnerClient` gains three members** (`tests/Antiphon.Tests/TestHelpers`):
  `RunnerBuildDto? Build { get; set; }` (passed as the DTO's `Build:`; null keeps today's
  shape), `Func<CancellationToken, Task<RunnerCapabilitiesDto?>>? CapabilitiesOverride`
  (consulted first by `GetCapabilitiesAsync`; returning null models an unreachable runner),
  and `int CapabilitiesCalls` (incremented on every `GetCapabilitiesAsync` call, override or not).
  `BuildHarness(..., runner:)` is NOT used: `Harness.Runner` is always the harness's own fake.
- **`RunnerCapabilityMismatchException(string message, RunnerBuildDto? build = null)`** with
  `Build` and `RunnerIdentity` properties; `RunnerIdentity` is initialised from the static
  helper, which must be referenced by its qualified name inside the exception (the property and
  the class share the name, and the Color-Color rule does not apply because the property is a
  string). The one-argument form keeps every existing throw site and test compiling.
- **`SessionRunnerHttpClient` takes `TimeProvider? clock = null` as its last constructor
  parameter** (default `TimeProvider.System`); every `DateTimeOffset.UtcNow` in the TTL logic
  reads `clock.GetUtcNow()`. `SessionRunnerCapabilityGateTests` gains
  `Client(StubHandler handler, TimeProvider? clock = null)`. DI needs no change.
- **Schedule-branch hold (D-3 ii) evidence** is `dead.FailureReason ?? "Runner build stale (no
  failure reason recorded)."`, identity `"unknown"`; V-511-8b pins `RunnerBuildHoldEvidence ==
  dead.FailureReason`.
- **`RunnerBuildReplaced` (Info, 68) message**: `Runner replaced: built from {sha7} (running
  since {ProcessStartUtc:u}); retrying the standing session now.` Tests pin the sha7 substring
  and the word `retrying`; Code may reword the rest.
- **One probe per `StartAsync`, even when no positive-evidence gate applies.** V-511-4 pins the
  request list `["/capabilities", "/sessions"]` for an ungated Claude launch and for a Codex
  launch whose only gate is the transcript gate. That is D-1's sentence taken literally; the
  test exists so a later "skip the probe when ungated" optimisation is a visible decision.
- **Manual Start under a runner-build hold is not refused.** No `HeldCode` for this hold
  (D-3 v); V-511-13 fails on a `ConflictException` if Code models it on the continuity hold.
- **Client notice** (`StandingSessionRecovery.tsx`): a Mantine `Alert` titled
  `Waiting for a rebuilt session runner` rendered when `supervision.runnerBuildHeldAt` is set,
  body = `runnerBuildHoldEvidence` followed by the line `Rebuild the runner: pwsh -File
  scripts/restart-session-runner.ps1`. It never disables Start (the AgentsPage Start button is
  gated on `continuityHeldAt` only and stays so).

### Inspection

Bodies read in full at `5eb6e033`:

- `tests/Antiphon.Tests/Agents/SessionRunnerGenerationWireTests.cs` (201): `[Category("Integration")]`,
  every test builds a fresh client via `Client(handler)`; `Spec(generation)` is Claude + generation;
  `Capabilities(features)` has no build; `StubHandler(Func<HttpRequestMessage,HttpResponseMessage>)`
  records `Requests`; a delegate that throws propagates as the transport exception
  (`C514_Lost_conditional_reply_is_unknown` relies on it). Boundaries -> V-511-1..5b add
  `Build`, `OldRunner`, `NewRunner`, a `Capabilities` overload with build/backends/transcripts,
  and a switchable handler; nothing existing changes.
- `tests/Antiphon.Tests/Agents/SessionRunnerCapabilityGateTests.cs` (87): the two existing
  tests pin `Requests.Count == 1` on a refusal and `["/capabilities","/sessions"]` on a launch
  with no evidence; the file has its own identical `StubHandler`/`StubFactory`/`Json`. Boundaries
  -> V-511-6/7 add the `Client(handler, clock)` helper; R-511-2/3 carry the existing pair.
- `tests/Antiphon.Tests/Application/RestartFailureClassificationTests.cs` (57): `[Category("Unit")]`;
  the wrapper table (Postgres 57P03, DbUpdateException, AggregateException, HttpRequestException
  with SocketException inner, Timeout, TaskCanceled, OperationCanceled) -> Infrastructure;
  `ResumeTargetMissingException` -> ContinuityUnavailable; the Observe test pins Charge
  (Infrastructure bumps `RestartBackoffFailures` only). Boundaries -> V-511-3c/14 append two
  methods; R-511-7 carries both existing ones.
- `tests/Antiphon.Tests/Application/AgentSupervisionTests.cs` (843, harness 540-843):
  `BuildHarness` registers `AgentSessionService`, `AgentSessionLaunchQueue`, `AgentControlService`,
  `AgentSupervisorService`, `AgentService`, `IAlertService`/`NullAlertRouter`, `TimeProvider`
  = `MutableTimeProvider` (offset over the real clock), `ISessionRunnerClient` = the harness's
  `FakeSessionRunnerClient` (`Harness.Runner`), adapters through `QueueAdapterFactory` (FIFO;
  an unqueued dispatch throws). `Harness.Supervisor()` is a fresh scope per call.
  `CreateAlwaysOnAgentAsync` flips `AlwaysOn` through the harness scope; `CleanupAsync` deletes
  incidents, alerts, supervision state, sessions and agents under the root.
- `tests/Antiphon.Tests/Application/StandingRestartAccountingTests.cs` (363): the resume-after-exit
  shape (manual boot, `SessionExitObservation.ObserveMatchingAsync(runtime, id, 1, ProcessExited,
  CreateContext)`, tick schedules `5*2^n` s, `Clock.Advance(+1)`, tick attempts, `WaitForIdleAsync(15 s)`,
  `resumed.StartedArgs` contains `--resume` and not `--session-id`); `ThrowOnStart` as the launch
  failure; `Async_infrastructure_failure_is_consumed_once_across_recreation` (a queued failure is
  observed by the next tick's dead-row branch, `RestartBackoffFailures == 1`). Boundaries -> the
  new class copies this shape verbatim for the held scenario.
- `tests/Antiphon.Tests/Application/StandingContinuityRecoveryTests.cs` (160) and
  `StandingRecoveryFixture.cs` (58): the continuity-hold precedent (`HeldCode` refusal on a
  default start, `ContinuityHeldAt` cleared by an explicit decision, `RestartFailureKind ==
  ContinuityUnavailable` on the session). Boundaries -> R-511-6 carries them; the runner-build hold
  deliberately differs (no `HeldCode`), V-511-13.
- `tests/Antiphon.Tests/TestHelpers/FakeSessionRunnerClient.cs` (177): `GetCapabilitiesAsync`
  builds the DTO from `Advertise*` flags with `Build` = null; `StartAsync` throws
  `NotSupportedException` unless `StartRefusal`; `ListOverride` is the reachability hook the
  accounting tests use. Boundaries -> the three members above.
- `tests/Antiphon.Tests/Agents/FakeAgentProtocolAdapter.cs` (503): `ThrowOnStart` throws before
  `Started`; `StartedArgs`, `StartedSessionId`, `Started`, `Killed`, `Disposed` are the receipts.
- `tests/Antiphon.Tests/TestHelpers/SessionExitObservation.cs` (33): captures the row's
  `StartedAt` as the generation of the synthetic exit.
- `client/src/features/agents/StandingSessionRecovery.test.tsx` (114): `renderWithProviders`,
  MSW `server.use`, the `agent` literal carries `supervision.continuityHeldAt`; five cases pin
  the confirm/dedup/refusal flow. Boundaries -> two new `it` blocks with a `runnerBuildHeldAt`
  agent; none of the five change.
- Production read for the assertions: `SessionRunnerHttpClient.cs` (StartAsync 51-118,
  backend/named-tab/generation gates 148-196, `EnsureCapabilitiesProbedAsync` 198-216,
  `GetCapabilitiesAsync` 242-253, transcript check 275-316, `ProbeCapabilitiesAsync` 319-336,
  `DescribeBuild` 338-345: ` and was built from {sha7} on {AssemblyWriteTimeUtc:yyyy-MM-dd HH:mm}
  (running since {ProcessStartUtc:HH:mm})`), `RestartFailurePolicy.cs`, `StandingContinuityState.cs`,
  `AgentSupervisorService.cs` (tick, `SuperviseAsync` order, `RecordStartFailureAsync`),
  `AgentSessionService.cs` (card catch 283-330, `LaunchInteractiveAsync` 343-420, helper 842-875),
  `AgentControlService.cs` (`StartAsync` 139-193, preflight 452-460, `ClearSupervisionLatchAsync`
  1061-1087: early return when nothing is latched), `AgentSupervisionState.cs`,
  `RestartFailureKind.cs`, `AgentIncidentKind.cs` (last value 67), `AgentDtos.cs`
  (`AgentSupervisionDto`), `AgentService.cs` (supervision projection 220-235, `GetByIdAsync`),
  `SessionRunnerContracts.cs` (`RunnerCapabilitiesDto`, `RunnerBuildDto`), `RunnerBuildIdentity.cs`.

### Delivery inventory

No new asynchronous outcome-delivery path to a session or a person is introduced; no session
input is written by this card, so no `UserPrompt` transcript evidence applies. The two changed
handoffs are supervision state, listed so their persistence boundaries and recovery are pinned:

- **DL-511-A hold placement.** Producer: `LaunchInteractiveAsync` catch (queued launch worker).
  Destination: `AgentSupervisionState.RunnerBuildHeldAt/HeldIdentity/HoldEvidence` plus the
  session's `RestartFailureKind = RunnerBuildStale`. Persistence boundary: one commit
  (`evidenceTransaction`) carrying the Failed outcome and the hold; the Kind-29 incident commits
  separately after it (settled above). Durable identity: the agent id + the session id + the
  runner identity string. Recovery: if the process dies between the outcome commit and the next
  tick, or an older server wrote the outcome without a hold, the schedule branch (D-3 ii) holds
  from the dead row alone (V-511-8b). Observable receipt: the state row (V-511-8), the DTO
  (V-511-16) and the Critical Kind-29 row (V-511-8/15). Substitute declared: the incident and
  DTO prove the hold is recorded and visible, not that an operator read it.
- **DL-511-B release and retry.** Producer: `TickAsync` release step. Destination: cleared hold,
  `NextRestartAt = now`, one `RunnerBuildReplaced` row, then the ordinary supervised attempt
  through `AgentSessionLaunchQueue` (existing path, R-511-6). Persistence boundary: the release
  commits in the tick's own scope before the sweep enqueues. Recovery: a crash after the release
  commit leaves a due `NextRestartAt`, which the next tick attempts (existing ladder behaviour);
  a crash before it leaves the hold, which the next tick re-evaluates against the runner
  identity (V-511-9/10). Observable receipt: `FakeAgentProtocolAdapter.Started` with
  `--resume` and the held session id (V-511-10), the strongest evidence this fixture can give
  (no runner process exists; the real runner POST is covered by the S1 wire tests).

### Proves it works now

| ID | Behaviour | Layer | Test | Expected |
|---|---|---|---|---|
| V-511-1 | D-1: a launch refused by runner A is re-decided on a fresh probe; runner B launches on the same client | wire | `SessionRunnerGenerationWireTests.C511_V1_Launch_after_runner_replacement_reprobes_and_launches` | runner=`OldRunner()`: `StartAsync(Spec(generation))` throws `RunnerCapabilityMismatchException`, `Message` contains `built from aaaaaaa`, `RunnerIdentity == RunnerIdentity.Describe(BuildA)`, `Requests` paths `["/capabilities"]`; switch runner=`NewRunner()`; second `StartAsync` on the same client returns `AcceptedStartedAt == generation`; paths `["/capabilities","/capabilities","/sessions"]` |
| V-511-2 | D-1 mirror: a positive snapshot never launches onto a downgraded runner | wire | `...C511_V2_Stale_positive_never_launches_onto_a_downgraded_runner` | runner=`NewRunner()`: first `StartAsync` launches, paths `["/capabilities","/sessions"]`; switch to `OldRunner()`; second `StartAsync` throws `RunnerCapabilityMismatchException`, `Message` contains `sessionGenerationV1` and `built from aaaaaaa` and not `bbbbbbb`, `RunnerIdentity == Describe(BuildA)`; paths `["/capabilities","/sessions","/capabilities"]` |
| V-511-3 | D-2: unreachable probe + generation gate refuses as `RunnerUnreachableException`, inner preserved, no POST, Infrastructure | wire + unit | `...C511_V3_Unreachable_probe_on_a_gated_launch_is_RunnerUnreachable(string shape)` arms `refused` (`throw new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused))`), `timeout` (`throw new TaskCanceledException()`), `500` (`new HttpResponseMessage(InternalServerError)`), `malformed` (200 with body `not json`) | `Should.ThrowAsync<RunnerUnreachableException>` on `StartAsync(Spec(generation))`; `error.InnerException.ShouldNotBeNull()`; `Requests` contains no `/sessions`; `new RestartFailurePolicy().Classify(error) == Infrastructure`; then `AttachHerdrAsync(new HerdrAttachRequest(sessionId,"pane","claude","claude",1,"none", AcceptedStartedAt: generation))` on the same client throws the same type and posts nothing to `/sessions/attach` |
| V-511-3b | D-1: 404 on `/capabilities` keeps the older-runner refusal (mismatch, not unreachable) | wire | `...C511_V3b_Older_runner_without_a_capabilities_endpoint_is_a_mismatch_not_unreachable` | handler `/capabilities` -> 404; `StartAsync(Spec(generation))` throws `RunnerCapabilityMismatchException`, `Message` contains `does not advertise sessionGenerationV1`, not `built from`; `RunnerIdentity == "unknown"`; no `/sessions` |
| V-511-3c | D-2: `RunnerUnreachableException` classifies Infrastructure with and without an inner | unit | `RestartFailureClassificationTests.C511_V3c_RunnerUnreachable_is_infrastructure_with_or_without_an_inner` | `Classify(new RunnerUnreachableException("no answer", null)) == Infrastructure`; `Classify(new RunnerUnreachableException("no answer", new TaskCanceledException())) == Infrastructure`; `Classify(new AggregateException(new Exception("x"), new RunnerUnreachableException("y", null))) == Infrastructure` |
| V-511-4 | D-2: unreachable probe on an ungated launch proceeds to the POST | wire | `...C511_V4_Unreachable_probe_on_an_ungated_launch_proceeds(string kind)` arms `claude` (`new AgentLaunchSpec("fake", ClaudeCode, "cmd", [], env, tmp, 120, 30)`), `codex` (Kind `Codex`, exe `codex`) | handler `/capabilities` throws `HttpRequestException`, `/sessions` -> `RunnerSessionDto(sessionId, null, ...)`; `launched.SessionId == sessionId`; paths `["/capabilities","/sessions"]` |
| V-511-5 | D-1: attach and the backend gate each take their own probe and answer from the runner of the moment | wire | `...C511_V5_Attach_and_backend_gate_use_the_fresh_probe` | runner=`NewRunner()`: `StartAsync` ok (2 requests); switch to `OldRunner()`: `GetSessionBackendCapabilityMismatchAsync` returns a message containing `SessionBackends=pty-host` and `built from aaaaaaa` (3 requests); `AttachHerdrAsync(..., AcceptedStartedAt: generation)` throws `RunnerCapabilityMismatchException` naming `aaaaaaa` (4 requests, no `/sessions/attach`); switch to `NewRunner()`: backend check returns null (5); `AttachHerdrAsync` returns (paths end `["/capabilities","/sessions/attach"]`, 7 total) |
| V-511-5b | D-1: a decision probe refreshes the watchdog snapshot | wire | `...C511_V5b_A_decision_probe_refreshes_the_watchdog_snapshot` | runner=`OldRunner()` (transcripts `[claude]`): `GetTranscriptCapabilityMismatchAsync(Codex)` non-null naming `aaaaaaa`; switch to `NewRunner()`; `StartAsync(Spec(generation))` ok; `GetTranscriptCapabilityMismatchAsync(Codex)` returns null (request count not asserted on the last call) |
| V-511-6 | D-6: a failed background probe keeps the last good snapshot and marks it stale | wire | `SessionRunnerCapabilityGateTests.C511_V6_Failed_probe_keeps_the_last_good_snapshot` | `FakeTimeProvider` at `2026-09-13T15:00Z`; handler answers `OldRunner`-shaped caps (transcripts `[claude]`, BuildA); call 1 `GetTranscriptCapabilityMismatchAsync(Codex)` non-null with `built from aaaaaaa`; handler now throws `HttpRequestException`; `clock.Advance(6 min)`; call 2: `Requests.Count == 2` and result non-null with `built from aaaaaaa`; call 3 with no advance: `Requests.Count == 3`, result non-null |
| V-511-7 | D-6: a failed first probe re-probes on the next call instead of pinning null for the TTL | wire | `...C511_V7_Failed_first_probe_reprobes_on_the_next_call` | handler throws once then answers `OldRunner`-shaped caps; call 1 returns null; call 2: `Requests.Count == 2`, result non-null naming `aaaaaaa` (deterministic: call 2 starts with a null snapshot and awaits its probe) |
| V-511-8 | D-3 i/D-5: a supervised resume refused by the runner holds on the refused identity, charges nothing, schedules nothing, records one Critical Kind-29 | integration | `StandingRunnerBuildHoldTests.C511_V8_Refused_resume_holds_without_charging` | scenario `HeldOnAAsync`: `h.Runner.Build = A`; manual boot (adapter 1 healthy), `ObserveMatchingAsync(id, 1, ProcessExited)`, tick (schedules: counters 1/1, `RestartScheduled` 1), `Advance(11 s)`, record `crashes` = `Crash` count for the agent, tick (attempts; adapter 2 `ThrowOnStart = Refusal(A)`), `WaitForIdleAsync`. Assert state: `RunnerBuildHeldAt != null`, `RunnerBuildHeldIdentity == RunnerIdentity.Describe(A)`, `RunnerBuildHoldEvidence == Message(A)`, `NextRestartAt == null`, `RestartBackoffFailures == 1`, `ConsecutiveFailures == 1`; session `Status == Failed`, `RestartFailureKind == RunnerBuildStale`, `FailureReason == Message(A)`; incidents for the session: `RunnerBuildStale` count 1 with `Severity == Critical`, `AgentId == agent.Id`, `FailureReason == Message(A)`; `RestartScheduled` count for the agent still 1, `Crash` count == `crashes` |
| V-511-8b | D-3 ii/D-4: a dead `RunnerBuildStale` row without a hold is held by the schedule branch (identity `unknown`, no schedule, no charge) and an `unknown` hold releases on the next answered probe | integration | `...C511_V8b_Dead_stale_row_without_a_hold_is_held_by_the_schedule_branch_and_released_by_any_answer` | manual boot (adapter 1), idle; by hand: session `Status = Failed`, `RestartFailureKind = RunnerBuildStale`, `FailureReason = Message(A)`, `EndedAt = now`; `h.Runner.CapabilitiesOverride = _ => Task.FromResult<RunnerCapabilitiesDto?>(null)`; tick: `RunnerBuildHeldAt != null`, `RunnerBuildHeldIdentity == "unknown"`, `RunnerBuildHoldEvidence == Message(A)`, `NextRestartAt == null`, counters 0/0, `LastObservedRestartSessionId == id`, `RestartScheduled` count 0, `Crash` count 0; then `CapabilitiesOverride = null`, `h.Runner.Build = A`, tick, idle: hold null, `RunnerBuildReplaced` count 1, adapter 2 `Started` with `--resume`, `StartedSessionId == id` |
| V-511-9 | D-3 iv/D-4: a held agent is neither scheduled nor attempted while the runner identity is unchanged, even when a `NextRestartAt` is due; one identity probe per tick | integration | `...C511_V9_Held_agent_is_not_scheduled_or_attempted(string shape)` arms `idle`, `due` | from `HeldOnAAsync` (adapter 3 healthy, queued but must stay unused); arm `due` first sets `NextRestartAt = now - 1 s` by hand; record `calls = h.Runner.CapabilitiesCalls`, incident count; tick, `Advance(2 min)`, tick, `Advance(30 min)`, tick; assert `adapter3.Started == false`, hold fields unchanged (identity A), incident count unchanged, `RestartScheduled` still 1, `h.Runner.CapabilitiesCalls == calls + 3`, arm `due`: `NextRestartAt` unchanged |
| V-511-10 | D-4: a changed runner identity releases the hold, writes `RunnerBuildReplaced`, and the same tick retries the resume without charging | integration | `...C511_V10_Runner_replacement_releases_and_retries_at_once` | from `HeldOnAAsync` (adapter 3 healthy); `h.Runner.Build = B`; one tick; `WaitForIdleAsync`; assert `RunnerBuildHeldAt == null`, `RunnerBuildHeldIdentity == null`, `RunnerBuildHoldEvidence == null`; `RunnerBuildReplaced` count 1, `Severity == Info`, `Message` contains `bbbbbbb` and `retrying`; `adapter3.Started`, `StartedArgs` contains `--resume`, not `--session-id`, `StartedSessionId == id`; counters still 1/1; `RestartScheduled` still 1; session `Status == Running` |
| V-511-11 | D-4: an unreachable identity probe keeps the hold and writes nothing | integration | `...C511_V11_Unreachable_identity_probe_keeps_the_hold` | from `HeldOnAAsync`; `h.Runner.CapabilitiesOverride = _ => Task.FromResult<RunnerCapabilitiesDto?>(null)`; tick, tick, idle; hold fields unchanged (identity A); `RunnerBuildReplaced` count 0; incident count unchanged; `adapter3.Started == false` |
| V-511-12 | D-3/D-5: after a release the retry refused by a second stale build re-holds on it with a second Kind-29 row, counters still unchanged | integration | `...C511_V12_Second_stale_build_reholds_with_a_second_incident` | from `HeldOnAAsync` with adapter 3 `ThrowOnStart = Refusal(C)`; `h.Runner.Build = C`; tick; idle; assert `RunnerBuildHeldIdentity == Describe(C)`, `RunnerBuildHoldEvidence == Message(C)`; `RunnerBuildStale` rows for the session == 2 with distinct `FailureReason` (`Message(A)`, `Message(C)`); `RunnerBuildReplaced` count 1; counters 1/1; `RestartScheduled` still 1; one more tick: nothing changes, adapter 4 (healthy, queued) not started |
| V-511-13 | D-3 v: a manual Start clears the hold before the launch is queued and is never refused with `HeldCode`; the queued launch re-decides | integration | `...C511_V13_Manual_start_clears_the_hold(string outcome)` arms `runs` (adapter 3 healthy), `reholds` (adapter 3 `Refusal(C)`) | from `HeldOnAAsync`; `await h.Control.StartAsync(agent.Id, new(), default)` returns (no `ConflictException`); immediately `RunnerBuildHeldAt == null`; `WaitForIdleAsync`; arm `runs`: `adapter3.Started`, `--resume`, `StartedSessionId == id`, hold still null, session Running; arm `reholds`: `RunnerBuildHeldIdentity == Describe(C)`, `RunnerBuildHoldEvidence == Message(C)`, `RunnerBuildStale` rows == 2, counters 1/1 |
| V-511-14 | D-3: classification and the non-charging rule | unit | `RestartFailureClassificationTests.C511_V14_RunnerBuildStale_is_classified_and_never_charged` | `Classify(new RunnerCapabilityMismatchException("x")) == RunnerBuildStale`; `Classify(new AggregateException(new Exception("other"), new RunnerCapabilityMismatchException("x"))) == RunnerBuildStale`; `state{ConsecutiveFailures=7, RestartBackoffFailures=3}`: `Charge(state, RunnerBuildStale)` leaves 7/3; `Observe(state, session{Failed, RestartFailureKind=RunnerBuildStale})` returns true, stamps `LastObservedRestartSessionId/StartedAt`, leaves 7/3; second `Observe` returns false |
| V-511-15 | D-5: an interactive refusal on any agent records one Kind-29 with the agent id, deduped per session + message | integration | `StandingRunnerBuildHoldTests.C511_V15_Interactive_mismatch_records_kind_29_once_per_message_for_any_agent` | agent created via `AgentService.CreateAsync(new CreateAgentRequest("Interactive", workspace))` (not always-on); adapters `[Refusal(A), Refusal(A)]`; `h.Control.StartAsync(agent.Id, new(), default)`, idle: session `Failed`, `RestartFailureKind == RunnerBuildStale`, `FailureReason == Message(A)`; incidents `Kind == RunnerBuildStale && SessionId == id`: exactly 1, `AgentId == agent.Id`, `Severity == Critical`, `FailureReason == Message(A)`; second manual `StartAsync` (resumes the same row), idle: still exactly 1 |
| V-511-16 | D-3 vi: the hold is visible on the agent DTO and the client renders the notice | service + client | `...C511_V16_Hold_is_visible_on_the_agent_dto`; Vitest `StandingSessionRecovery.test.tsx` `it('runner build hold names the stale build and the rebuild command')` | server: from `HeldOnAAsync`, `AgentService.GetByIdAsync(agent.Id)` (fresh scope) -> `Supervision.RunnerBuildHeldAt != null`, `Supervision.RunnerBuildHoldEvidence == Message(A)`, `Supervision.ContinuityHeldAt == null`; client: render with `supervision: { runnerBuildHeldAt: '2026-09-13T15:55:00Z', runnerBuildHoldEvidence: 'The session runner does not advertise sessionGenerationV1 and was built from 9ebbba7 ...' }` -> `getByText('Waiting for a rebuilt session runner')`, `getByText(/built from 9ebbba7/)`, `getByText(/restart-session-runner\.ps1/)`, and `queryByRole('button', { name: 'Retry after repair' })` is null |
| V-511-16c | client: no notice without the hold | client | `it('no runner build hold renders no runner notice')` | render the existing `agent` literal (continuity hold only): `queryByText('Waiting for a rebuilt session runner')` is null |
| V-511-17 | D-4: `RunnerIdentity.Describe` | unit | `RunnerIdentityTests.C511_V17_Describe_formats_sha_or_version_at_process_start_and_unknown_for_null` | `Describe(null) == "unknown"`; `Describe(new RunnerBuildDto("1.0.0+<40 a>", "<40 a>", UnixEpoch, 2026-09-13T15:50:00Z)) == "<40 a>@2026-09-13T15:50:00.0000000Z"`; `Describe(new RunnerBuildDto("1.2.3-dev", null, UnixEpoch, same)) == "1.2.3-dev@2026-09-13T15:50:00.0000000Z"` |
| V-511-18 | D-3 vi/D-4: a live session clears a leftover hold silently; unheld ticks never probe identity | integration | `StandingRunnerBuildHoldTests.C511_V18_Live_session_clears_a_leftover_hold_and_unheld_ticks_never_probe_identity` | manual boot (adapter 1), idle, session Running; record `calls = h.Runner.CapabilitiesCalls`; tick, tick: `CapabilitiesCalls == calls` (an unheld sweep never probes identity); by hand set the three hold columns (identity `Describe(A)`); tick: hold null, `RunnerBuildReplaced` count 0, `CapabilitiesCalls == calls + 1`, session still Running |

Scenario helpers in `StandingRunnerBuildHoldTests` (private static): `Build(char fill, int hour, int minute)` ->
`new RunnerBuildDto($"1.0.0+{new string(fill, 40)}", new string(fill, 40), DateTime.UnixEpoch, new DateTime(2026, 9, 13, hour, minute, 0, DateTimeKind.Utc))`;
`A = Build('a', 9, 0)`, `B = Build('b', 15, 50)`, `C = Build('c', 16, 30)`;
`Message(b)` = `$"The session runner does not advertise sessionGenerationV1 and was built from {b.CommitSha![..7]} on 1970-01-01 00:00 (running since {b.ProcessStartUtc:HH:mm}). Rebuild and restart it: pwsh -File scripts/restart-session-runner.ps1."`;
`Refusal(b)` = `new RunnerCapabilityMismatchException(Message(b), b)`;
`HeldOnAAsync(string root, params FakeAgentProtocolAdapter[] afterRefusal)` builds the harness with
`[healthyBoot, new FakeAgentProtocolAdapter { ThrowOnStart = Refusal(A) }, ..afterRefusal]`,
`definitionKind: "ClaudeCode"`, sets `h.Runner.Build = A`, runs the V-511-8 sequence and returns
`(Harness h, AgentDetailDto agent, Guid id)`; every test wraps in `try/finally
AgentSupervisionTests.CleanupAsync(root)`. Wire helpers in `SessionRunnerGenerationWireTests`:
`BuildA`/`BuildB` (same shape, process start 09:00 / 15:50 UTC), `OldRunner()` =
`Capabilities(["herdr-attach"], BuildA, backends: ["pty-host"], transcripts: [Claude])`,
`NewRunner()` = `Capabilities(["herdr-attach", SessionGenerationV1, HerdrNamedTabPlacement], BuildB,
backends: ["pty-host","herdr"], transcripts: [Claude, Codex, Grok])`, and
`Switchable(Guid sessionId, DateTime generation, Func<RunnerCapabilitiesDto> runner)` returning a
`StubHandler` that answers `/capabilities` from `runner()`, `/sessions` and `/sessions/attach`
with a `RunnerSessionDto` echoing the generation, else 404.

### Guards the regression

| ID | Regression | Test and decisive assertion |
|---|---|---|
| R-511-1 | one probe per decision, refusal before any POST | `SessionRunnerGenerationWireTests.A_runner_without_sessionGenerationV1_refuses_a_generation_bearing_launch_and_attach_before_any_POST` (both arms): paths `["/capabilities"]` for start and for attach |
| R-511-2 | transcript refusal still costs one GET and no POST | `SessionRunnerCapabilityGateTests.Explicitly_missing_transcript_format_refuses_launch_with_restart_fix`: `Requests.Count == 1`, message names `codex` and the restart script |
| R-511-3 | null capability fields stay no-evidence for the transcript gate | `...Absent_capability_field_is_no_evidence_and_launches_as_before`: `["/capabilities","/sessions"]` |
| R-511-4 | generation echo contract | `A_generation_bearing_launch_posts_acceptedStartedAt_and_maps_the_echo`, `A_launch_response_without_the_echoed_generation_is_refused` (`Code == NotEchoed`, one POST) |
| R-511-5 | the watchdog consumer of the S2 cache | `AgentTaskDeliveryWatchdogTests.Explicit_runner_transcript_mismatch_fails_the_task_but_does_not_kill_the_session` |
| R-511-6 | supervisor ladder, continuity hold, herdr hold untouched by the new gate and branches | `StandingRestartAccountingTests` (all, incl. `Async_infrastructure_failure_is_consumed_once_across_recreation`: Infrastructure still charges `RestartBackoffFailures == 1`), `StandingContinuityRecoveryTests` (all: `HeldCode` still refuses a default start, `ContinuityUnavailable` still holds), `AgentSupervisionTests` (all) |
| R-511-7 | transport and unknown classification | `RestartFailureClassificationTests.Infrastructure_wrappers_and_cancellation_are_classified_by_evidence` (HttpRequestException stays Infrastructure; `57P03` text alone stays Unknown), `Terminal_generation_is_charged_once_and_healthy_completion_defines_a_process_failure` |
| R-511-8 | client recovery flow | the five existing `StandingSessionRecovery.test.tsx` cases |

### Guard inventory

| ID | Guard (plan reference) | Test method | PC |
|---|---|---|---|
| G-511-1 | D-1: a launch decision probes fresh even when a snapshot exists | V-511-1 | PC-511-1 |
| G-511-2 | D-1 (rejected alternative a): a positive snapshot is not trusted for a later decision | V-511-2 | PC-511-2 |
| G-511-3 | D-2: unreachable + positive-evidence gate refuses as `RunnerUnreachableException`, never as a mismatch, never a POST | V-511-3 | PC-511-3 |
| G-511-4 | D-2: unreachable with only the transcript gate (or no gate) proceeds to the POST | V-511-4 | PC-511-4 |
| G-511-5 | D-2: `RunnerUnreachableException` classifies Infrastructure by type, not only by inner | V-511-3c (null-inner row) | PC-511-5 |
| G-511-6 | D-1: `AttachHerdrAsync` decides on its own probe | V-511-5 (attach half) | PC-511-6 |
| G-511-7 | D-1: `GetSessionBackendCapabilityMismatchAsync` decides on its own probe | V-511-5 (backend half) | PC-511-7 |
| G-511-8 | D-1: a successful decision probe replaces the watchdog snapshot | V-511-5b | PC-511-8 |
| G-511-9 | D-6: a failed background probe keeps the last good snapshot | V-511-6 (call 2 non-null) | PC-511-9 |
| G-511-10 | D-6: a failed probe marks the cache stale so the next call re-probes | V-511-6 (call 3 count), V-511-7 | PC-511-10 |
| G-511-11 | D-3: `RunnerCapabilityMismatchException` classifies `RunnerBuildStale` | V-511-14, V-511-8 | PC-511-11 |
| G-511-12 | D-3: `Charge` is a no-op for `RunnerBuildStale` | V-511-14, V-511-8b (counters 0/0) | PC-511-12 |
| G-511-13 | D-3 i: the interactive catch holds on the exception's identity and message | V-511-8 (identity == `Describe(A)` right after idle) | PC-511-13 |
| G-511-14 | D-3 ii: the schedule branch holds a dead stale row instead of scheduling | V-511-8b | PC-511-14 |
| G-511-15 | D-3 iv: a held agent is never attempted, even with a due `NextRestartAt` | V-511-9 arm `due` | PC-511-15 |
| G-511-16 | D-3 v: a manual Start clears the hold | V-511-13 arm `runs` | PC-511-16 |
| G-511-17 | D-4: a changed identity releases the hold and the same tick retries | V-511-10 | PC-511-17 |
| G-511-18 | D-4: a failed identity probe keeps every hold | V-511-11 | PC-511-18 |
| G-511-19 | D-4: an unchanged identity keeps the hold (no release on equality) | V-511-9 arm `idle` | PC-511-19 |
| G-511-20 | D-3/D-4: the release writes the `RunnerBuildReplaced` trail | V-511-10 | PC-511-20 |
| G-511-21 | D-5: the interactive catch records Kind-29 with the known agent id | V-511-15 (count 1 after the first start) | PC-511-21 |
| G-511-22a | D-5: a different message on the same session is a new Kind-29 row | V-511-12 (rows == 2) | PC-511-22a |
| G-511-22b | D-5: the same message on the same session is deduped | V-511-15 (still 1 after the second start) | PC-511-22b |
| G-511-23 | D-3 vi: the DTO carries the hold | V-511-16 (server half) | PC-511-23 |
| G-511-24 | D-3 vi: the client renders the notice from `runnerBuildHeldAt` | V-511-16 (client half) | PC-511-24 |

Guards = 25, mapped = 25, missing = 0, duplicate PC mappings = 0. V-511-3b, 17 and 18 are
pinned without a guard entry: 3b refuses before the POST under either classification, 17 is a
pure formatter whose consumers are G-511-13/17/19, and 18 is hygiene plus a traffic bound.

### Positive controls

Mutation runs each PC method-scoped on a local inherited SourceLanding child:
`dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-pc/ -- --treenode-filter "/*/*/<Class>/<Method>"`
(parameterised methods run every arm; the expected red names the arm) and
`pwsh -File scripts/test-client.ps1 StandingSessionRecovery` for PC-511-24. Each cycle: apply
the mutation, run red at the named assertion, restore, refresh the restored file's timestamp,
run green. Zero tests, build failures and fixture errors are not red. Rows in the same file run
separately (`SessionRunnerHttpClient.cs`: 1, 2, 3, 4, 6, 7, 8, 9, 10; `RestartFailurePolicy.cs`:
5, 11, 12; `AgentSessionService.cs`: 13, 21, 22a, 22b; `AgentSupervisorService.cs`: 14, 15, 17,
18, 19; `RunnerBuildHoldState.cs`: 20; `AgentControlService.cs`: 16; `AgentService.cs`: 23;
`StandingSessionRecovery.tsx`: 24); rows from different files may batch.

| PC | Break (compiling defect) | Exact method | Expected red |
|---|---|---|---|
| PC-511-1 | `ProbeForDecisionAsync`: under the gate, `if (_cachedCapabilities is { } snap) return Answered(snap);` before the GET | `SessionRunnerGenerationWireTests.C511_V1_Launch_after_runner_replacement_reprobes_and_launches` | the second `StartAsync` throws `RunnerCapabilityMismatchException` naming `aaaaaaa` (expected a launch); paths would be `["/capabilities"]` |
| PC-511-2 | `ProbeForDecisionAsync`: return the snapshot when it advertises `sessionGenerationV1` (trust a positive) | `...C511_V2_Stale_positive_never_launches_onto_a_downgraded_runner` | `Should.ThrowAsync<RunnerCapabilityMismatchException>` fails: the second `StartAsync` launches and `/sessions` is posted twice; V-511-1 stays green |
| PC-511-3 | `ProbeForDecisionAsync` catch: map transport/5xx/malformed to `Capabilities = null, Unreachable = null` (older-runner semantics) | `...C511_V3_Unreachable_probe_on_a_gated_launch_is_RunnerUnreachable` | every arm: `Should.ThrowAsync<RunnerUnreachableException>` fails with `RunnerCapabilityMismatchException` thrown instead |
| PC-511-4 | `StartAsync`: `if (probe.Unreachable is not null) throw new RunnerUnreachableException(...)` unconditionally, before checking whether a positive-evidence gate applies | `...C511_V4_Unreachable_probe_on_an_ungated_launch_proceeds` | both arms: `StartAsync` throws `RunnerUnreachableException` (expected `launched.SessionId == sessionId`) |
| PC-511-5 | `RestartFailurePolicy.Classify`: remove `RunnerUnreachableException` from the Infrastructure type list | `RestartFailureClassificationTests.C511_V3c_RunnerUnreachable_is_infrastructure_with_or_without_an_inner` | null-inner row: `ShouldBe(Infrastructure)` fails with `Unknown`; the inner-bearing rows stay green |
| PC-511-6 | `AttachHerdrAsync`: read `_cachedCapabilities` (via `EnsureCapabilitiesProbedAsync`) instead of `ProbeForDecisionAsync` | `...C511_V5_Attach_and_backend_gate_use_the_fresh_probe` | after the switch to `OldRunner()`, `Should.ThrowAsync<RunnerCapabilityMismatchException>` on attach fails (attach succeeds on the cached B) |
| PC-511-7 | `GetSessionBackendCapabilityMismatchAsync`: keep `EnsureCapabilitiesProbedAsync` + `_cachedCapabilities` | same method | after the switch to `OldRunner()`, `backendMismatch.ShouldNotBeNull()` fails (null from the cached B); request count 3 fails (2) |
| PC-511-8 | `ProbeForDecisionAsync`: do not write `_cachedCapabilities` on success | `...C511_V5b_A_decision_probe_refreshes_the_watchdog_snapshot` | final `ShouldBeNull()` fails: the transcript check still answers from A |
| PC-511-9 | `ProbeCapabilitiesAsync`: write `result` unconditionally (today's line) | `SessionRunnerCapabilityGateTests.C511_V6_Failed_probe_keeps_the_last_good_snapshot` | call 2 `ShouldNotBeNull()` fails (null served) |
| PC-511-10 | `ProbeCapabilitiesAsync`: on null result leave `_capabilitiesProbedAt` stamped (drop the `MinValue` reset) | same method; `...C511_V7_Failed_first_probe_reprobes_on_the_next_call` | V6 call 3 `Requests.Count.ShouldBe(3)` fails (2); V7 `Requests.Count.ShouldBe(2)` fails (1) and `second.ShouldNotBeNull()` fails |
| PC-511-11 | `Classify`: remove the `RunnerCapabilityMismatchException` arm | `RestartFailureClassificationTests.C511_V14_RunnerBuildStale_is_classified_and_never_charged` | first `ShouldBe(RunnerBuildStale)` fails with `Unknown` |
| PC-511-12 | `Charge`: drop `RunnerBuildStale` from the early return | same method; `StandingRunnerBuildHoldTests.C511_V8b_...` | V14 `RestartBackoffFailures.ShouldBe(3)` fails (4); V8b `RestartBackoffFailures.ShouldBe(0)` fails (1) |
| PC-511-13 | `LaunchInteractiveAsync` catch: delete the `RunnerBuildHoldState.HoldAsync` call (Kind-29 still written) | `StandingRunnerBuildHoldTests.C511_V8_Refused_resume_holds_without_charging` | `RunnerBuildHeldAt.ShouldNotBeNull()` fails right after idle (null; the schedule branch has not run yet) |
| PC-511-14 | `SuperviseAsync` schedule branch: remove the `dead?.RestartFailureKind == RunnerBuildStale` branch | `...C511_V8b_Dead_stale_row_without_a_hold_is_held_by_the_schedule_branch_and_released_by_any_answer` | `RestartScheduled` count `ShouldBe(0)` fails (1) and `RunnerBuildHeldAt.ShouldNotBeNull()` fails |
| PC-511-15 | `SuperviseAsync`: remove the `RunnerBuildHeldAt is not null` early return | `...C511_V9_Held_agent_is_not_scheduled_or_attempted` | arm `due`: `adapter3.Started.ShouldBeFalse()` fails (the due attempt enqueued the resume); arm `idle` stays green (the schedule branch still holds) |
| PC-511-16 | `ClearSupervisionLatchAsync`: leave the early-return condition and the field clears as today (no hold handling) | `...C511_V13_Manual_start_clears_the_hold` | arm `runs`: `RunnerBuildHeldAt.ShouldBeNull()` right after `Control.StartAsync` fails |
| PC-511-17 | `TickAsync`: delete the release step | `...C511_V10_Runner_replacement_releases_and_retries_at_once` | `RunnerBuildHeldAt.ShouldBeNull()` fails; `adapter3.Started.ShouldBeTrue()` fails |
| PC-511-18 | `TickAsync` release: on a null probe result release every held agent | `...C511_V11_Unreachable_identity_probe_keeps_the_hold` | `RunnerBuildHeldAt.ShouldNotBeNull()` fails; `adapter3.Started.ShouldBeFalse()` fails |
| PC-511-19 | `TickAsync` release: release whenever the probe answered (drop the identity comparison) | `...C511_V9_Held_agent_is_not_scheduled_or_attempted` | arm `idle`: `adapter3.Started.ShouldBeFalse()` fails and `RunnerBuildHeldIdentity.ShouldBe(Describe(A))` fails (null) |
| PC-511-20 | `RunnerBuildHoldState.ReleaseAsync`: drop the `RunnerBuildReplaced` incident | `...C511_V10_Runner_replacement_releases_and_retries_at_once` | `RunnerBuildReplaced` count `ShouldBe(1)` fails (0); the hold clear and the retry stay green |
| PC-511-21 | `LaunchInteractiveAsync` catch: do not call `RecordRunnerBuildStaleAsync` (today's catch) | `...C511_V15_Interactive_mismatch_records_kind_29_once_per_message_for_any_agent` | Kind-29 count `ShouldBe(1)` after the first start fails (0) |
| PC-511-22a | `RecordRunnerBuildStaleAsync` known-id path: dedup on `SessionId && Kind` only (today's predicate) | `...C511_V12_Second_stale_build_reholds_with_a_second_incident` | `RunnerBuildStale` rows `ShouldBe(2)` fails (1) |
| PC-511-22b | `RecordRunnerBuildStaleAsync` known-id path: skip the dedup query | `...C511_V15_...` | count after the second manual start `ShouldBe(1)` fails (2) |
| PC-511-23 | `AgentService` supervision projection: pass `null` for `RunnerBuildHeldAt`/`RunnerBuildHoldEvidence` | `...C511_V16_Hold_is_visible_on_the_agent_dto` | `Supervision.RunnerBuildHeldAt.ShouldNotBeNull()` fails |
| PC-511-24 | `StandingSessionRecovery.tsx`: render the notice on `continuityHeldAt` instead of `runnerBuildHeldAt` | Vitest `runner build hold names the stale build and the rebuild command` | `getByText('Waiting for a rebuilt session runner')` throws (not rendered); the sibling `no runner build hold renders no runner notice` also fails (rendered for the continuity agent) |

### Out of scope

- A `RunnerCapabilityMismatchException` wrapped in an `AggregateException` at the interactive
  catch: V-511-14 pins the classification of the wrapped shape; the hold identity for it is not
  pinned because every real throw site (`SessionRunnerHttpClient.StartAsync`/`AttachHerdrAsync`
  through the adapter) throws it unwrapped.
- A runner that answers `/capabilities` with `Build == null` against an identity-bearing hold
  (`"unknown" != A` releases by plain inequality): pre-CARD-0112 runners are not a supported
  target, and the outcome (one uncharged attempt that re-holds) is the same convergence D-4
  already accepts.
- Two held agents on one tick sharing one identity probe: the count bound is pinned for one
  agent (V-511-9/18); the two-agent case adds a second always-on agent to a `[NotInParallel]`
  suite for a cost guard, not a safety one.
- `AgentChanged` events on hold/release: the interactive catch already publishes after its
  commit and `Control.StartAsync` publishes on the retry; not asserted.
- The 5 s probe deadline (`CapabilityProbeTimeout`) and the release step's 5 s linked token:
  proving them needs a wall-clock wait; the `timeout` arm of V-511-3 pins the classification of
  a cancelled probe instead.
- `PtyDeliveryProfile`/`SessionDeliveryProfile`, `restart-apphost.ps1`, ladder constants,
  CARD-0510, the dispatcher watchdog's own Kind-29 writer (D-7) and the migration itself
  (applied by the shared test Postgres on the first run; its shape is pinned by every S3 test
  reading the three columns).
- HTTP-level JSON casing of the two new DTO fields: System.Text.Json web defaults camel-case
  every other `AgentSupervisionDto` field the client already reads; V-511-16 pins the server
  projection and the client contract separately.

### Cost

All figures are **estimated** (nothing was built or run in this dispatch); assumptions: one
foreground owner, isolated `bin-c511/` output, warm NuGet cache, no concurrent
`Antiphon.Agents.Pty.Tests`, shared test Postgres reachable.

| Ordinary V/R floor, per Code or independent Review pass | Minutes |
|---|---:|
| Setup: restore + build `tests/Antiphon.Tests` into `bin-c511/` (+ `dotnet ef migrations add AddRunnerBuildHold` once, in S3) | 13 |
| `SessionRunnerGenerationWireTests` full class (6 existing methods + 7 new, 19 arms, in-process) | 1 |
| `SessionRunnerCapabilityGateTests` full class (2 + 2) | 1 |
| `RestartFailureClassificationTests` + `RunnerIdentityTests` (unit) | 1 |
| `StandingRunnerBuildHoldTests` full class (10 methods, 12 arms, each a Postgres harness with 2-4 ticks and idle waits) | 4 |
| Regression set: `StandingRestartAccountingTests` (both partials), `StandingContinuityRecoveryTests`, `AgentSupervisionTests`, `AgentTaskDeliveryWatchdogTests` | 11 |
| Vitest `StandingSessionRecovery` (2 new + 5 existing) + client rebuild | 4 |
| **Per-pass setup + ordinary V/R** | **35** |

Code floor 35 (plus 13 more if the migration is generated in a separate build); independent
ordinary Review floor another 35. Band per pass: 30-45.

| PC floor (Mutation), method-scoped red/restore/green | Controls | Min per cycle | Minutes |
|---|---:|---:|---:|
| Wire/unit controls, in-process (PC-511-1..12) | 12 | 3 | 36 |
| Supervision controls over Postgres (PC-511-13..23, 22a/22b counted separately) | 12 | 4 | 48 |
| Vitest control (PC-511-24) | 1 | 2 | 2 |
| Mutation setup: snapshot build of `bin-pc/`, one green run of the six touched classes | - | - | 18 |
| Restoration inventory, timestamp refresh, evidence | - | - | 10 |
| **Mutation floor, all 25 controls, unbatched** | **25** | - | **114** |

Band 95-130. Batching across files in triples (one `SessionRunnerHttpClient` row, one
supervisor row, one policy/service row per cycle) shares a build pair per group and saves about
30 minutes; the nine `SessionRunnerHttpClient.cs` rows cannot batch among themselves. No
concurrency saving is assumed (one managed snapshot; the plan is under the sharding threshold).

**Total verification floor** = setup/build 13 + ordinary V/R 22 + every PC red/restore/green 86
+ Mutation setup/evidence 28 = **149 minutes, estimated**, unbatched.

--- next stage ---
next: code
handoff: Code implements S1-S4 against the finalized verification design (25 guards, 20 V rows across SessionRunnerGenerationWireTests, SessionRunnerCapabilityGateTests, RestartFailureClassificationTests, new RunnerIdentityTests and StandingRunnerBuildHoldTests, two Vitest cases; FakeSessionRunnerClient gains Build/CapabilitiesOverride/CapabilitiesCalls; Kind-29 written after the evidence commit) and runs the ordinary V/R floor; Mutation runs the 25 PCs after land.
artifact: docs/superpowers/plans/2026-09-18-card-0511-runner-build-hold-plan.md
