# CARD-0312 — the missing rung is "did it answer", not "is it up": a boot-turn reply watch, and a synthetic probe only where nothing was typed

**Date:** 2026-09-04

**Plan task:** e2cde52e (Plan, High; no production code changed)

**Card:** CARD-0312 — "Post-start liveness probe: require a real reply within a timeout before trusting an agent is alive"

**Evidence base (read, not inferred):** `AgentSessionService.StartAsync` / `LaunchInteractiveAsync` / `LaunchInteractiveProcessAsync` / `ResumeAsync` / `ResumeInterruptedLaunchAsync` / `WaitForReadyOrThrowAsync` / `SendBootPromptWithRetryAsync` / `TryLateConfirmBootPromptAsync` / `DeliverLaunchNoteAsync` / `EnqueueResumeContinueAsync`; `SessionMessageQueueService.DeliverAsync` / `WaitForTranscriptConfirmAsync` / `TryFindConfirmingRecordAsync` / `IsVerifiedDeliverySessionAsync` / `HandleDeliveryFailureAsync` / `TryHandleCodexBootWedgeAsync`; `AgentTaskDispatcher.FailNeverStartedAsync` / `RelaunchWedgedAsync` / `FailWedgedAtLimitAsync`; `TaskDeadlinePolicy`; `AgentSupervisorService`; `SessionHealthService`; `SupervisionSettings` / `DelegationSettings` / `AgentRegistrySettings` / `AgentSessionSettings`; `AgentIncidentKind`; `AttentionKind` (`AttentionDtos.cs`); `TranscriptKinds` (`SessionRunnerContracts.cs`); `SessionContextUsage`; `src/Antiphon.FakeClaude` / `src/Antiphon.FakeGrok`; commit `9e8f5a5a` (pong-probe removal, 2026-07-23); `docs/session-runtime-invariants.md`; `docs/superpowers/plans/2026-09-03-card-0353-boot-turn-provider-stall-plan.md`; card text of CARD-0311, CARD-0299, CARD-0353.

---

## Decision

**This is not a separate post-start handshake. It is the missing top rung of the delivery evidence ladder that already exists, and it must be built as one rung, not a second mechanism.**

The ladder today, in order, every rung a real piece of code:

| # | Rung | Question it answers | Where | Budget |
|---|---|---|---|---|
| 1 | `WaitForReadyAsync` → `WaitForReadyOrThrowAsync` | did a process paint a composer and go quiet? | `AgentSessionService.cs:1616-1625` (throws `"Agent process did not become ready."` at `:1624`) | `AgentRegistrySettings.cs:9-10` — 5 s quiet inside 60 s max |
| 2 | composer evidence (`ComposerDeliveryEvidence` / `WaitForComposerEvidenceAsync`) | did our bytes reach the composer? | `SessionMessageQueueService.cs:2664-2680` | `EvidenceTimeoutSeconds` = 15 |
| 3 | submit evidence (sequence advance / settled emptied-composer / Working latch) | did Enter submit rather than fold? | `SessionMessageQueueService.cs:2333-2361`, `SettlePostEvidenceAsync` `:2708` | `PostSubmitAdvanceTimeoutSeconds` = 30, `PostEvidenceSettleMs` = 500 |
| 4 | transcript confirm — a `UserPrompt` row carrying the body | did the input **become a prompt**? | `WaitForTranscriptConfirmAsync` `:2271`, `TryFindConfirmingRecordAsync` `:2634` | `TranscriptConfirmTimeoutSeconds` = 30 |
| **5** | **— nothing —** | **did the agent answer it?** | **absent** | **—** |

Every existing rung is about *our bytes*. Not one of them asks whether the model produced anything. That is the whole of CARD-0312's gap, and it is one rung, not a parallel handshake.

Two consequences follow, and they are the plan:

**(a) Wherever a launch already types a real prompt, that prompt IS the probe.** The card's own requirement #1 — "send it through the normal delivery path, so the probe exercises the same machinery a real brief would" — is satisfied *for free* by watching the prompt that was going to be sent anyway. A synthetic "reply OK" typed *after* a brief would be a second turn buying no evidence the first turn does not already carry.

**(b) A synthetic probe is needed for exactly one launch shape: an unattended launch that types nothing.** `LaunchInteractiveProcessAsync` (`AgentSessionService.cs:392-478`) can finish having sent no prompt at all: `notes` is null for a non-channel agent and for the standing check-interpreter (`AgentControlService.cs:262-269`), `interruptedTurn` may be false, `initialPrompt` is null on a supervisor restart (`AgentSupervisorService.cs:203-204` passes only `Fresh`/`IgnoreSubscriptionQuota`), and `FlushSessionAsync` (`:464`) finds an empty queue. That launch ends with `Status = Running`, an `AgentChanged` event, and zero evidence that anything can be reached. That is the population the card's #4 describes — "the operator discovering it hours later".

### Does it extend `BootWedged` / the delivery watchdog, or is it separate?

**It extends both, and it must not become a third overlapping mechanism.** Concretely:

- **`BootWedged` (CARD-0299)** is already the right *recovery*: kill, one relaunch, then fail at the limit (`SessionMessageQueueService.cs:3243-3320`, `AgentTaskDispatcher.RelaunchWedgedAsync:2765` / `FailWedgedAtLimitAsync:2893`, `DelegationSettings.BootWedgeRelaunchLimit` = 1, `AgentTask.BootWedgeRelaunchCount`). What it is *not* is a detector for this card's failure: its trigger is a conjunction of `AgentKind.Codex` + `Status: Running` + `messages.All(Origin == Delegation && DeliveryAttempts == 1 && LastDeliveryBaselineSequence is null)` + a `NoSubmitOutput` verdict (`:3253-3268`). A session whose brief *was* confirmed as a `UserPrompt` and then produced silence never reaches it. **S4 generalises the trigger and reuses the recovery verbatim.** No new relaunch ladder is introduced.
- **The delivery watchdog `FailNeverStartedAsync`** (`AgentTaskDispatcher.cs:881`, `DeliveryFailTimeoutMinutes` = 10) fires on the *opposite* predicate: `started == false`, i.e. no non-housekeeping turn prompt since dispatch (`:967-978`). The reply watch's precondition is `started == true`. **They are mutually exclusive by construction and cannot double-jeopardy a task** — but the ordering matters and is fixed in S1: reply watch (≈5 min) < `FailNeverStartedAsync` (10 min) < `TaskDeadlinePolicy.ModelWait` (20 min, `DelegationSettings.cs:380`).
- **`TaskDeadlinePolicy.ModelWait`** (`TaskDeadlinePolicy.cs:58,185-200`) is the closest existing *detector*: last transcript entry is a `UserPrompt`/`AssistantText`, session working, 20 minutes. It is **task-scoped only** — its two callers are `AgentTaskDispatcher.cs:1479/1496` and `AttentionService.cs:897`. A standing agent, an interactive session, a channel-bound orchestrator has **no** equivalent. S1's evaluator is that policy's shape, generalised to a session and narrowed to the boot turn.
- **CARD-0353 S1 (`BootModelWaitDeadlineMinutes`, planned, not implemented — `160e9042` is docs only; the setting does not exist in `DelegationSettings.cs`)** is *the same event on the same clock*: "the first assistant, thinking or tool row after the boot prompt never came." **CARD-0312 and CARD-0353 S1 must ship as one primitive with one setting name.** Whichever lands first owns the setting; the other consumes it. Building both would be precisely the third overlapping mechanism this card forbids.

### The hard prior: a periodic probe is forbidden, and this is not one

Antiphon **had** a round-trip liveness probe and deliberately deleted it. `SessionHealthService.cs:53-60`: *"Wedge/deadness detection is DELIVERY-TIME ONLY … The periodic round-trip 'pong' probe was removed 2026-07-23 — it spent model turns on healthy idle sessions and its restart-reset in-memory clock made it spammy."* Same fact in `SupervisionSettings.cs:242-246`, and the TUI echo probe before it "false-positive-killed healthy idle sessions on 2026-07-20". Commit `9e8f5a5a` removed `LivenessProbeSettings` and `RunLivenessProbesAsync` and left `AgentIncidentKind.LivenessProbeFailed = 10` (`AgentIncidentKind.cs:36-37`) so history still renders. `SessionHealthTests.cs:306-335` pins it: `No_probe_prompts_are_ever_sent_to_an_idle_session`.

**That pin stays green, untouched.** This card's probe is *launch-scoped and once*: it fires inside a launch, at most one per launch, never on a schedule, never on a session that is already up. Anything else is the 2026-07 regression coming back.

---

## The four evidenced instances, honestly scored

| # | Instance | What actually broke | Does this card's mechanism catch it? |
|---|---|---|---|
| 1 | school-revision "seemed broken", 149% context, prompt typed-but-unsent | a *mid-life* session, hours after launch | **No — and the plan does not claim it.** Nothing launch-scoped catches a session that degrades later. What it does add is that the *restart* which fixed it becomes verified rather than hoped-for, and S3 puts context fullness in the verdict so 149% is stated, not discovered. The mid-life counterpart is CARD-0294 (`UnmarkedWaiting`) and CARD-0292 (`QueuedInputNeverConverted`), both shipped. |
| 2 | Task `fcecedfd`: brief in the composer, "marked Sent", 10 minutes wasted | rung 3/4 believed screen state | **Partly, and mostly already fixed.** CARD-0299 + CARD-0342 tightened the submit evidence. S4's contribution is that the *recovery* now fires for any kind, not only Codex, and at ≈5 min instead of the 10-minute watchdog. |
| 3 | CARD-0299, 3 of 55 Codex sessions (5.5%) | rung 3 latched on one empty frame | **Already fixed; guarded here.** S5 pins that generalising the wedge trigger leaves the Codex outcome byte-identical. |
| 4 | Antiphon-Orchestrator resume: first `/start` failed at 60 s, second succeeded | rung 1 worked; nothing retried it, and nothing said why | **Yes, on both halves.** S4 routes a rung-1 failure on an unattended agent into `AgentSupervisorService`'s existing ladder so the retry a delegate did by hand happens automatically; S3 makes the message name the 168% context. |

**So: rung 5 closes one of the four outright, converts a second from a 10-minute silence into a ≈5-minute named failure, and makes a third self-recovering.** The card's fourth (mid-life degradation) is out of scope for a launch-time probe and is said so here rather than papered over.

---

## Design questions, answered

### Q1 — every start, only resume, only standing agents?

**Decision: the reply *watch* runs on every launch that sends a prompt (cost: zero extra turns). The synthetic *probe* runs only on an unattended launch that sends none.**

Reasoning, from the launch inventory rather than from taste:

| Launch path | Sends a prompt? | Watch | Synthetic probe |
|---|---|---|---|
| Card spawn — `StartAsync` (`:96`), work prompt at `:213` | always | yes, free | never |
| Delegate dispatch — `EnqueueInteractiveSession` → `LaunchInteractiveProcessAsync`, brief flushed at `:464` | always (brief or spill pointer) | yes, free | never |
| Channel-bound / preamble agent — `DeliverLaunchNoteAsync` (`:438`, `:2137`) | always (`FreshBody`/`ResumeBody`) | yes, free | never |
| Resumed interrupted turn — `EnqueueResumeContinueAsync` (`:445`, `:2231`) | when `ResumeAutoContinue` | yes, free | never |
| Cardless start with `request.Prompt` (`:455`) | yes | yes, free | never |
| `ResumeAsync` — human resume of a card session (`:1191`, flush at `:1266`) | only if the queue is non-empty | yes when it does | never — a human initiated it and is watching |
| `ResumeInterruptedLaunchAsync` (`:525`) | by construction has a `Dispatched` task (`:544-547`) | yes, free | never |
| **Cardless start, unattended, nothing queued** | **no** | n/a | **yes** |

The synthetic-probe population is therefore: a launch that reached `FlushSessionAsync` having typed nothing **and** whose agent is unattended — `agent.AlwaysOn`, or channel-bound (`ChatChannels.Any(c => c.AgentId == agent.Id)`, the same query `AgentControlService.cs:147-152` already runs), or the standing check-interpreter (`AgentControlService.cs:262-266`). **Pool delegates are structurally excluded** (`IsPoolDelegate` agents are created with `AlwaysOn = false`, `AgentTaskDispatcher.cs:3399`, and always carry a brief), which is the direct answer to the card's cost worry: *pool delegates launch constantly and pay nothing.*

Warm-pool reuse pays nothing either — a reused warm agent (`TryReuseWarmAgentAsync`, `:3360-3378`) does not launch, so there is no probe to run and the brief's own reply is watched as usual.

### Q2 — does the probe pollute context or transcript in a way that matters?

**Decision: on the free path, zero pollution — nothing extra is sent. On the synthetic path, one short `UserPrompt` and one short assistant turn, accepted deliberately and bounded three ways.**

1. **It fires at most once per launch**, and only on a launch whose session has just been created or resumed — i.e. at the point where the context is at its emptiest and a two-line exchange is the cheapest it will ever be.
2. **The body is one line with no work in it** (`AgentSessionSettings.BootProbeBody`, default: *"Antiphon liveness check — reply with the single word: ready. Do not do any other work."*). A probe with content invites a long turn, which is the cost the 2026-07-23 removal complained about.
3. **It is legible as machine housekeeping.** It goes through `_messageQueue.EnqueueAsync(..., origin: QueuedMessageOrigin.System)` — the origin already documented as "injected by Antiphon itself (bootstrap/restart/compaction-recovery notes)" (`QueuedMessageOrigin.cs:17-18`) and which does not batch. A human reading the transcript sees the same class of row as the restart note that has always been there.

What is **not** done: no `Mode.Now` (`WhenIdle` only, so it can never race a channel bootstrap into one composer — the CARD-0233 trap), no re-arming, no probe on a session that is already `Running`, and no probe at all where the transcript is not ground truth (see the verdict rule below).

The honest residue: on a channel-bound agent the probe is *never* sent (the preamble note covers it), so the only agents that ever see the extra line are unattended non-channel agents — the smallest and least context-sensitive population there is.

### Q3 — retry/backoff, and does a second failure escalate to a human?

**Decision: two populations, two ladders that already exist, and a hard stop at two.**

**Delegate task sessions** — reuse CARD-0299's machinery unchanged:

- first failure → `BootWedged`-class incident, cancel the queue rows, kill, **one** relaunch via `RelaunchWedgedAsync` (`DelegationSettings.BootWedgeRelaunchLimit` = 1, counted durably on `AgentTask.BootWedgeRelaunchCount`);
- second → `FailWedgedAtLimitAsync` (`:2893`) fails the task with a reason naming what was observed. A failed task already reaches a human through `AttentionKind.RecentFailure` (9) and `FailureUnacknowledged` (15).

**Standing / always-on agents** — reuse `AgentSupervisorService`'s ladder unchanged:

- a failed watch records `AgentIncidentKind.LivenessProbeFailed` (10 — **reused, not a 48th kind**) and increments `AgentSupervisionState.ConsecutiveFailures`, so the *existing* `Backoff` (`min(base·2ⁿ, cap)`, `:258-262`), the existing `FreshAfterResumeFailures = 2` (so the second restart is a **fresh** conversation, which is what actually cleared the 149% case), and the existing `EscalateIfTierCrossedAsync` (`:264-280`) all apply with no new policy;
- **hard stop:** at most **two** consecutive probe-driven restarts per agent. On the third consecutive failure the mechanism *latches off* for that agent (`AgentSupervisionState.LivenessLatchedAt`), raises the incident at `AlertSeverity.Error`, and stops restarting. The latch clears on any human `StartAsync` (which already lifts the supervision latch, `AgentControlService.cs:112`) or on any successful reply.

**Human escalation is the attention feed, per AGENTS.md** ("a decision belongs on the card move/reopen revision and attention feed, never a new column or an alert sink"): a new `AttentionKind.LivenessProbeFailed = 27` projected from *open* `LivenessProbeFailed` incidents, re-verified at read time against a live session and a still-unanswered boot prompt — the exact discipline `BuildQueuedInputStuckItemsAsync` uses (`AttentionService.cs:1420-1497`), copied rather than reinvented.

**Never** does this mechanism restart a session that is producing model output; **never** does it kill a session mid-turn (the `agent.AlwaysOn && working` restraint at `SessionMessageQueueService.cs:3200-3202` is the standing precedent).

---

## The verdict: what counts as "a real reply"

**A transcript row produced by the model, on this session, at a sequence strictly greater than the boot prompt's own confirmed `UserPrompt` sequence, of kind `AssistantText`, `Thinking`, `ToolCall` or `TurnEnd`** (`TranscriptKinds`, `SessionRunnerContracts.cs:281-315`).

Explicitly **not** a verdict, each for a reason already paid for:

- a screen redraw or an output-sequence advance — `DeliveryVerificationSettings.PostSubmitAdvanceTimeoutSeconds`'s own doc comment says it plainly: *"this is wedge detection, not reply detection"* (`SupervisionSettings.cs:276-279`). CARD-0055 / `docs/session-runtime-invariants.md` line 86;
- `QueuedUserPrompt` — that is our input arriving late, not an answer (`SessionRunnerContracts.cs:286-291`);
- `QueueEnqueue` / `QueueDequeue` / `QueueRemove` — CARD-0292 defines these as **inert housekeeping**, and their timestamps can predate their file-order predecessors (`SessionRunnerContracts.cs:293-306`);
- a `SessionRestartBoundary` synthesised by `WriteRestartBoundaryIfInterruptedAsync` (`AgentSessionService.cs:2189`) — Antiphon wrote that row itself;
- a `TurnEnd` whose sequence is at or below the boot prompt — the inherited history of a reused warm session (the CARD-0077 trap `FailNeverStartedAsync` documents at `AgentTaskDispatcher.cs:967-971`).

**Pull before you judge.** The evaluator calls `CatchUpTranscriptAsync` before it may conclude "no reply", because the live stream is not a reliable clock — the rule `FailNeverStartedAsync` states in its own comment at `AgentTaskDispatcher.cs:958-966` ("six records landed in one burst at the instant of the kill").

**Scope-out where there is no ground truth.** A session whose kind is not delivery-verified — `ProviderContractCatalog.For(kind).DeliveryVerification.State != Supported`, i.e. OpenCode/Raw (`IsVerifiedDeliverySessionAsync`, `SessionMessageQueueService.cs:2740-2764`) — gets **no watch and no probe**. There is no transcript to be the verdict, and a screen-only verdict is exactly what CARD-0055/CARD-0264 forbid. Better a session with no watch than a watch that kills healthy sessions on a redraw.

## The clock

**Do not invent 60 seconds.** The card names ~60 s because that is rung 1's readiness budget (`ClaudeReadyMaxWaitMs = 60000`), which measures a *process painting a screen*, not a *model answering*. Measured first-token data from CARD-0353's plan (same repo, same providers, gathered 2026-09-03): Grok first-turn TTFT p50 2.5 s, p90 32 s, max 94 s on a provider-incident day; all turns p99 96 s, max 116 s. A resumed Claude session at 168 % context spends far longer than that re-ingesting before its first token.

**S1 measures the real distribution first** — per `AgentKind`, the gap from the boot prompt's `UserPrompt` to the first `Thinking`/`AssistantText`/`ToolCall` on that session, over transcripts since 2026-08-20 — and sets the default at **≈3× the measured maximum, floored at 3 minutes**, recording the numbers in the setting's doc comment the way `ModelWaitDeadlineMinutes` does. **Planning placeholder: 5 minutes.** This is the same number and the same setting as CARD-0353 S1's `BootModelWaitDeadlineMinutes`.

**It is a sweep, not an inline await.** Blocking `LaunchInteractiveProcessAsync` for five minutes would hold a launch-queue slot and the caller's HTTP request, and would be lost on a server restart — the CARD-0331 mistake (in-memory land queue, no boot reconciliation). Instead the launch **stamps the expectation on the session row** and the existing supervisor tick (`SupervisionSettings.TickSeconds` = 10) resolves it. Restart-safe by construction, and `ResumeInterruptedLaunchAsync` re-derives it from the row.

---

## Alternatives considered and rejected

- **A synthetic probe on every launch, including delegates.** Rejected on cost and on evidence: the brief already exercises composer → submit → transcript → reply through the identical path, so the probe buys nothing the brief does not, and pool delegates launch constantly. This is the card's own Q1 worry, answered with mechanism.
- **A periodic liveness probe (re-adding the pong).** Rejected: measured and deleted twice (`9e8f5a5a`, and the TUI echo probe before it), for spending model turns on healthy idle sessions and for false-positive kills. `SessionHealthTests.cs:312` pins its absence and stays green.
- **Blocking the launch until the reply lands.** Rejected: holds the launch queue and the HTTP request for minutes, and dies with the process. The session row plus the supervisor tick is durable.
- **A new `AgentIncidentKind` (48) and a new relaunch counter.** Rejected: `LivenessProbeFailed = 10` already exists for exactly this and still renders in history; `BootWedgeRelaunchCount` + `BootWedgeRelaunchLimit` are already durable, already migrated, already tested. Minting parallel state is how the card's "third overlapping mechanism" happens.
- **Accepting `TurnEnd` alone as the reply.** Rejected: CARD-0046's split-final shape means a bare `TurnEnd` can arrive with no content (`FakeClaude` models it under `ANTIPHON_FAKE_SPLIT_FINAL`), and `WriteRestartBoundaryIfInterruptedAsync` synthesises turn-ends of Antiphon's own. `TurnEnd` counts only alongside the model-produced kinds, never as the sole basis for "alive" when it is the synthetic kind.
- **Auto-falling back to `Fresh: true` on the first failed watch.** Rejected here, deliberately: a fresh start discards session history and CARD-0311 says explicitly that is the operator's call. The supervisor's existing `FreshAfterResumeFailures = 2` already makes the *second* restart fresh, which is the measured cure for the 149 % case and is a policy that already exists.
- **Reading provider sidecars (Grok `events.jsonl`, `~/.grok/logs/unified.jsonl`) for a first-token signal.** Rejected for the same reason CARD-0353's plan rejected it: it makes a sidecar the verdict. Diagnostics only.

---

## Slices

### S1 — the reply watch primitive and its clock

**Files:** `server/Domain/Entities/AgentSession.cs`; `server/Infrastructure/Data/AppDbContext.cs` + a migration; `server/Application/Services/BootReplyWatch.cs` (new); `server/Application/Settings/DelegationSettings.cs`; `server/Application/Services/SessionMessageQueueService.cs` (stamp point); `server/Application/Services/AgentSessionService.cs` (stamp point for boot prompts).
**Tests:** `tests/Antiphon.Tests/Application/BootReplyWatchTests.cs` (new).

1. **Measure first.** Over `TranscriptEntries` joined to `AgentSessions` since 2026-08-20, per `AgentKind`: the gap from a session's first non-housekeeping `UserPrompt` at/after `StartedAt` (the `TranscriptPromptSpan` predicate) to the first `Thinking`/`AssistantText`/`ToolCall` row past it. Report p50/p90/p99/max. Record them in the setting's doc comment.
2. `AgentSession.BootPromptSequence` (`long?`) and `AgentSession.BootReplyDueAt` (`DateTime?`), nullable, defaulted null, one migration. Null = no watch armed, which is every session that exists today — the change is inert on legacy rows.
3. `BootReplyWatch` — a **pure evaluator**, no DB writes, testable without a harness: given `(bootPromptSequence, dueAt, now, lastEntriesSince)` return `Answered` / `Waiting` / `Overdue`, applying the kind rules above. Same shape as `TaskDeadlinePolicy.ClassifyPhase` (`:185`) so the two read alike.
4. **Arm it** at the single point where a boot prompt becomes ground truth: where `WaitForTranscriptConfirmAsync` returns `DeliveryOutcome.Confirmed(DeliveryConfirmedBy.Transcript)` (`:2319`) **and** the session has no `BootPromptSequence` yet — i.e. the first confirmed prompt of this launch. Stamp `BootPromptSequence` = the confirming row's sequence, `BootReplyDueAt` = now + deadline. Also arm from `TryLateConfirmBootPromptAsync` (`AgentSessionService.cs:991`) so a late-confirmed boot prompt is watched too.
5. **Disarm** on the first qualifying model row (set both to null), on session termination, and on any human input to the session.
6. **Setting:** `DelegationSettings.BootReplyDeadlineMinutes` (placeholder 5; `<= 0` disables). **If CARD-0353 S1 lands first, consume its `BootModelWaitDeadlineMinutes` and add nothing** — one number, one name.

**Ordering invariant, asserted in a test:** `BootReplyDeadlineMinutes` < `DeliveryFailTimeoutMinutes` (10) < `ModelWaitDeadlineMinutes` (20).

### S2 — the synthetic probe, narrowly scoped

**Files:** `server/Application/Services/AgentSessionService.cs` (`LaunchInteractiveProcessAsync`, after `:464`); `server/Application/Settings/AgentSessionSettings.cs`.
**Tests:** `tests/Antiphon.Tests/Application/BootLivenessProbeScopeTests.cs` (new).

1. Track whether this launch typed anything: the launch note (`:438`), the resume-continue (`:445`), the initial prompt (`:455`), and whether `FlushSessionAsync` (`:464`) delivered a row.
2. If it typed **nothing**, the kind is delivery-verified, and the agent is unattended (`AlwaysOn` ∨ channel-bound ∨ standing check-interpreter), enqueue `AgentSessionSettings.BootProbeBody` as `System` / `WhenIdle`. Otherwise do nothing at all.
3. Failure to enqueue is logged and never fatal to the launch — the `DeliverLaunchNoteAsync` posture (`:2164-2177`).
4. `AgentSessionSettings.BootProbeEnabled` (default true) as the kill switch, in the shape of `TranscriptConfirmEnabled`.

### S3 — verdict, diagnostics, incident, attention

**Files:** `server/Application/Services/AgentSupervisorService.cs` (or a small `BootReplyWatchdogService` piggy-backed on the supervisor tick, matching `HerdrCorroborationSettings` / `OrchestratorInvestigationSettings` precedent); `server/Application/Services/AttentionService.cs`; `server/Application/Dtos/AttentionDtos.cs`; `server/Application/Services/AgentSessionService.cs` (`WaitForReadyOrThrowAsync`).
**Tests:** `tests/Antiphon.Tests/Application/BootReplyWatchdogTests.cs` (new), `AttentionServiceTests.cs`, `AgentSessionLaunchFailureTests.cs`.

1. Sweep armed sessions each supervisor tick. On `Overdue`, **`CatchUpTranscriptAsync` first**, re-evaluate, and only then judge.
2. **Diagnostic bundle**, all of it already available today — no dependency on CARD-0311 shipping:
   - composer/screen: `_runtime.TryGetLiveSnapshot(sessionId, out var snap).RenderedScreen`, head-trimmed;
   - **context fullness**: `SessionContextUsage.LoadFullnessAsync` (`SessionContextUsage.cs:139`) — fullness + `ContextFullnessState`;
   - last N transcript entry kinds + sequences since the boot prompt;
   - queue row states and `DeliveryVerdict` for the launch's rows;
   - open incidents on the session; `LaunchResumedAt`, resume mode, and elapsed since arm.
3. Record `AgentIncidentKind.LivenessProbeFailed` (10) at `Warning` (`Error` on the latching third), message naming *what was observed* — "prompt confirmed at sequence N; no assistant, thinking or tool row in 5m12s; context 149 %; composer holds: '…'".
4. `AttentionKind.LivenessProbeFailed = 27`, projected from open incidents with read-time re-verification (live session + still-unanswered boot prompt), on the `BuildQueuedInputStuckItemsAsync` pattern (`AttentionService.cs:1425-1497`). Actions: `OpenAgent`, `OpenDrawer`.
5. **CARD-0311 item 1, done here because it is the same sentence:** `WaitForReadyOrThrowAsync` (`:1616`) gains the session's context fullness when known — `"Agent process did not become ready (resuming a session at 168% context)."` `AgentSessionLaunchFailureTests.cs:289` pins the current string and is updated to pin both forms.

### S4 — bounded recovery and escalation

**Files:** `server/Application/Services/SessionMessageQueueService.cs` (`TryHandleCodexBootWedgeAsync` → `TryHandleBootWedgeAsync`); `server/Application/Services/AgentSupervisorService.cs`; `server/Domain/Entities/AgentSupervisionState.cs` + migration (`LivenessLatchedAt`).
**Tests:** `tests/Antiphon.Tests/Application/SessionMessageQueueBootWedgeTests.cs` (extended), `BootReplyRecoveryTests.cs` (new).

1. Generalise the wedge trigger from `AgentKind.Codex` + `NoSubmitOutput` (`:3253-3268`) to **any delivery-verified kind** + (`NoSubmitOutput` ∨ the new no-reply condition). Everything downstream — cancel rows, incident, kill, `RelaunchWedgedAsync`, `FailWedgedAtLimitAsync`, the limit, the counter — is untouched.
2. **Codex's existing behaviour must be byte-identical**; S5 pins it.
3. Standing agents route to `AgentSupervisorService`: increment `ConsecutiveFailures`, let the existing `Backoff` / `FreshAfterResumeFailures` / `EscalateIfTierCrossedAsync` run. Latch off after two consecutive probe-driven restarts (`LivenessLatchedAt`); clear on human `StartAsync` or a successful reply.
4. **Never kill a working session**, and never restart on the basis of a screen.

### S5 — fakes and the positive controls

**Files:** `src/Antiphon.FakeClaude/Program.cs` + `NoReplyModel.cs` (new); `src/Antiphon.FakeGrok/` (same mode); tests below.

`ANTIPHON_FAKE_NO_REPLY[=N]` — the Nth submitted turn (default 1) writes its `user` JSONL record and clears the composer normally, then emits **no assistant record, no thinking record and no done token**; later turns respond normally. This is the mode CARD-0353 S4 also needs ("FakeGrok hang mode"); build it once, in both fakes, as an opt-in defaulting off — the discipline every other mode in `FakeClaude/Program.cs:15-96` follows.

---

## Verification design

AGENTS.md classes session launch/delivery as safety-critical, so each evidenced failure mode gets a **positive control** — a test that reproduces the failure and asserts the new mechanism fires — and each historical over-reaction gets a **negative control**. Process-spawning classes take `[ParallelLimiter<ProcessSpawnLimit>]` (CARD-0050 S5) and `Antiphon.Tests` / `Antiphon.Agents.Pty.Tests` run sequentially.

### Positive controls

| # | Failure mode | Control | Asserts |
|---|---|---|---|
| P1 | **Reply never comes** (the new rung; instance 4's shape, and CARD-0353's Grok hang) | `ANTIPHON_FAKE_NO_REPLY=1` end-to-end through a real fake TUI | prompt is transcript-confirmed; nothing fails before the deadline; **at** the deadline exactly one `LivenessProbeFailed` incident whose message contains the composer text **and** the context-fullness figure; one attention row; exactly one relaunch; `FailNeverStartedAsync` run over the same task returns 0 (**double-jeopardy control**) |
| P2 | **Typed, never submitted** (task `fcecedfd`) | `ANTIPHON_FAKE_SWALLOW_ENTER=3` (exists) | the ladder still returns `NoSubmitOutput`; the **generalised** handler takes it for a non-Codex kind; one relaunch, then `FailWedgedAtLimitAsync` — not a 10-minute silence |
| P3 | **Codex empty-composer false Sent** (CARD-0299) | the existing `SessionMessageQueueBootWedgeTests` suite, unmodified, plus one added assert | outcome for Codex is unchanged by the generalisation — same incident, same kill, same relaunch count (**regression guard on S4**) |
| P4 | **"did not become ready" with no diagnosis** (Antiphon-Orchestrator resume) | `AgentSessionLaunchFailureTests` with a fake adapter whose `WaitForReadyAsync` returns false, on a session with a known fullness | the failure message names the context fullness; the supervisor ladder schedules the retry (the thing a delegate did by hand); a second failure escalates rather than restarting a third time |
| P5 | **Restart-safety of the watch** | arm a watch, drop and rebuild the service (the `ResumeInterruptedLaunchAsync` shape) | the watch is re-derived from the session row and still fires — the CARD-0331 failure does not recur |

### Negative controls (each one is a mistake this repo has already paid for)

| # | Guards against | Control |
|---|---|---|
| N1 | resurrecting the periodic probe | `SessionHealthTests.No_probe_prompts_are_ever_sent_to_an_idle_session` (`:312`) stays green **unmodified**, plus a new pin that a `Running`, idle, healthy session receives no probe body from the new path |
| N2 | killing a slow-but-alive boot turn | a fake replying at `deadline − 1s` is never failed, never killed, never relaunched, and the watch disarms cleanly |
| N3 | judging a session with no ground truth | an OpenCode/Raw session is neither armed nor probed (`IsVerifiedDeliverySessionAsync` gate) |
| N4 | inherited-history false positives | a **warm reused** session whose transcript already holds assistant rows from the previous task is judged only on rows past `BootPromptSequence` (the CARD-0077 trap) |
| N5 | cost regression on the hot path | a delegate launch with a brief sends **exactly one** prompt — assert the queue received no probe row |
| N6 | double-jeopardy across watchdogs | ordering test: `BootReplyDeadlineMinutes` < `DeliveryFailTimeoutMinutes` < `ModelWaitDeadlineMinutes`, and a session that trips the reply watch is not also failed by the other two |
| N7 | screen-based verdicts creeping back | a session whose output sequence advances (redraw) with **no** model transcript row is still judged `Overdue` — the CARD-0055 rule, restated one rung up |

### Commands

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0312/ --treenode-filter "/*/*/BootReplyWatchTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0312/ --treenode-filter "/*/*/BootReplyWatchdogTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0312/ --treenode-filter "/*/*/BootLivenessProbeScopeTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0312/ --treenode-filter "/*/*/BootReplyRecoveryTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0312/ --treenode-filter "/*/*/SessionMessageQueueBootWedgeTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0312/ --treenode-filter "/*/*/SessionHealthTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0312/ --treenode-filter "/*/*/AgentSessionLaunchFailureTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0312/ --treenode-filter "/*/*/AttentionServiceTests/*"
```

Forward slash on `OutputPath`, and delete the ~12 `bin-card0312` directories afterwards (`docs/testing-and-build.md` line 34). Class filters, not namespace-wide (CARD-0239/CARD-0307).

---

## Settings introduced or reused

| Setting | Default | Note |
|---|---|---|
| `Delegation:BootReplyDeadlineMinutes` | **measure, placeholder 5** | ≈3× measured max, floored at 3 min. **Same setting as CARD-0353 S1's `BootModelWaitDeadlineMinutes`** — whichever ships first owns the name. `<= 0` disables. |
| `AgentSessions:BootProbeEnabled` | `true` | kill switch for the synthetic probe |
| `AgentSessions:BootProbeBody` | one line, no work implied | see Q2 |
| `Delegation:BootWedgeRelaunchLimit` | **1 (unchanged)** | reused, not re-added |
| `Supervision:FreshAfterResumeFailures` | **2 (unchanged)** | the second restart is fresh; already the measured cure |

## Risks

- **The clock is guessed instead of measured.** Mitigated by S1 step 1 being a *gate*, not a suggestion: no default ships without the distribution in the doc comment. A too-short deadline kills healthy slow boots — N2 is the control.
- **CARD-0353 ships the same primitive twice.** Mitigated by naming the collision explicitly and by making the setting shared. If CARD-0353 is executing concurrently, S1 becomes "consume its setting" and this card starts at S2.
- **The synthetic probe re-becomes a periodic probe under later pressure.** Mitigated by N1 keeping the 2026-07 pin green and by the probe being armed only inside a launch.
- **The generalised wedge trigger changes Codex behaviour.** P3 is the guard.
- **`AgentSession` gains two columns.** Nullable, defaulted, inert on legacy rows; one migration.

## Rollback

`BootReplyDeadlineMinutes = 0` disables the watch; `BootProbeEnabled = false` disables the synthetic probe. Neither leaves state behind — armed columns are nullable and are cleared by the disarm path and by session termination. `BootWedgeRelaunchLimit` and the supervisor ladder are unchanged, so reverting this card's code leaves CARD-0299's behaviour exactly as it is today.
