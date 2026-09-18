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
