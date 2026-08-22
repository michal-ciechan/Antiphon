# CARD-0143 — a config-gated 30-minute subscription-usage sweep: plan

**Date:** 2026-08-22 · **Card:** CARD-0143 (`31a4ddf8-abba-480a-87fa-b06f2e0f8e97`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** master `25213b4`. Every line/behaviour claim below was read out of the code on
that commit.

**Parent:** CARD-0136 (proactive subscription switching — the ACTING half; this card is the
DATA-COLLECTION half only). **Measurements this plan is built on:** CARD-0141 (Codex `/status`,
and the `/usage` picker hazard), CARD-0136's Grok `/usage` capture, CARD-0137 (overlay focus blocks
re-sending an overlay-opening slash command).

---

## Verdict up front

The card's *shape* is right and buildable. Its *mechanism as written* — "reuse the exact idle-check
→ `POST /messages {"Mode":"Now"}` → `GET /buffer` sequence the investigations used" — **does not
survive contact with the sessions this sweep will actually target**, and shipping it that way
would, twice an hour, forever:

1. **Kill healthy always-on agents.** `SessionMessageQueueService.EnqueueAsync`'s `Mode.Now` path
   calls `HandleDeliveryFailureAsync` on any unverified delivery
   (`server/Application/Services/SessionMessageQueueService.cs:173`), and that method's kill
   predicate is `var kill = agent is { AlwaysOn: true } && !working && !allSupervision &&
   !preFirstTurn;` (`:1794`). A Now-mode send passes `messageIds: null`, so `allSupervision` and
   `preFirstTurn` are both `false` by construction (`:1769`, `:1766`), and the sweep only ever runs
   against sessions it has just confirmed idle — so `working` is `false` too. **Every failed poll of
   an always-on agent is a kill.** CARD-0137 measured exactly the failure that triggers it: a Grok
   session with its `/usage` overlay already open refuses the send with `NoComposerEvidence`.
2. **Fail every single time on any session that has ever taken a turn.** `DeliverAsync` uses
   CARD-0055's transcript confirmation whenever the session has at least one stored
   `TranscriptEntry` (`var confirmTranscript = baseline.Observable;`, `:1248`) and the kind's
   `DeliveryVerification` contract is `Supported` (`IsVerifiedDeliverySessionAsync`, `:1539`) —
   which Grok and Codex both are. Confirmation then polls for a `UserPrompt` transcript row
   carrying the typed body. **A local TUI command writes no such row**: CARD-0141 measured Codex's
   session transcript staying at **0 entries** across the whole `/status` investigation, and
   CARD-0136 measured Grok's panel rendering with "no model calls yet in this session". So the
   confirm loop is guaranteed to time out (`TranscriptConfirmTimeoutSeconds`, 30 s) after
   re-pressing Enter `SubmitAttempts` times, return `NoTranscriptRecord`, and fall into the kill
   path above.

   The investigations did not hit this because the sessions they drove had **no transcript rows
   yet** — the observability gate then degrades to the legacy screen-only verdict (`:1249-1256`),
   which is why `Mode:"Now"` appeared to work. That is a property of a fresh session, not of the
   command.

So the plan below keeps the card's *discipline* exactly (read-only idle check first, exactly one
thing sent, buffer read after) and replaces the *transport* with a narrow, purpose-built local
command path that shares the queue's per-session lock but none of its prompt-delivery verdicts,
incidents, retries or kills.

---

## 1. Hosted-service shape, and why this one

**Follow `SessionReconciliationHostedService` / `SessionHealthHostedService` for the driver, and
`ContextCompactionService` for the swept service.** Both are the repo's settled shapes for exactly
this kind of work, and CARD-0143 is structurally a twin of CARD-0082's idle auto-compact sweep:
periodically walk live sessions, decide eligibility from `ProviderContractCatalog`, act on the idle
ones only, never act on a working one.

**New files:**

| File | Kind | Why |
|---|---|---|
| `server/Application/Settings/SubscriptionUsageMonitoringSettings.cs` | settings | §6 |
| `server/Application/Services/SubscriptionUsageMonitorService.cs` | **singleton** service, `SweepAsync(CancellationToken) → Task<int>` | §3 |
| `server/Application/Services/SubscriptionUsageParser.cs` | pure static | §4 |
| `server/Application/Services/SubscriptionUsageReader.cs` | scoped, read-only | §5 |
| `server/Domain/Entities/SubscriptionUsageSample.cs` | entity | §5 |
| `server/Infrastructure/Supervision/SubscriptionUsageMonitorHostedService.cs` | `BackgroundService` | below |
| `server/Migrations/<ts>_AddSubscriptionUsageSamples.cs` | EF migration | §5 |

**Its own `BackgroundService`, NOT a branch inside `AgentSupervisorHostedService`.** The two
existing minute-cadence sweeps (`ChannelReplySweepPeriod`, `ContextCompactionSweepPeriod`) ride
inside the supervisor tick because they are DB-only and return in milliseconds. This sweep types
into live terminals and waits on rendered-screen evidence — a per-session budget of seconds, times
N sessions — and `AgentSupervisorHostedService.ExecuteAsync` awaits each branch inline on a
`TickSeconds` timer. Riding along would stall supervision, the channel-reply TTL sweep, auto-compact
and API-error recovery behind terminal I/O. `SessionReconciliationHostedService` is the cleanest
sibling to copy verbatim:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    if (!_settings.Enabled)
    {
        _logger.LogInformation("Subscription usage monitoring disabled by configuration");
        return;                                   // ← the whole feature gate, one line
    }

    using var timer = new PeriodicTimer(
        TimeSpan.FromMinutes(Math.Max(1, _settings.IntervalMinutes)));
    while (await timer.WaitForNextTickAsync(stoppingToken))
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider
                .GetRequiredService<SubscriptionUsageMonitorService>()   // singleton, resolved for symmetry
                .SweepAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        catch (Exception ex) { _logger.LogWarning(ex, "Subscription usage sweep failed"); }
    }
}
```

Registration in `server/Program.cs`, alongside the existing block at `:426-431`:

```csharp
builder.Services.Configure<SubscriptionUsageMonitoringSettings>(
    builder.Configuration.GetSection("SubscriptionUsageMonitoring"));   // beside :146-149
builder.Services.AddSingleton<SubscriptionUsageMonitorService>();       // beside :356
builder.Services.AddScoped<SubscriptionUsageReader>();
builder.Services.AddHostedService<
    Antiphon.Server.Infrastructure.Supervision.SubscriptionUsageMonitorHostedService>();
```

Registration is **unconditional** and the `Enabled` guard lives inside `ExecuteAsync` — the same
shape every sibling uses (`SessionHealthHostedService`, `SessionReconciliationHostedService`,
`AgentTaskCheckHostedService`, `AgentSupervisorHostedService`). It keeps the off-by-default
behaviour testable without a DI-graph test, and keeps the log line that says the feature is off.

**Singleton for the service** for `ContextCompactionService`'s own stated reason
(`ContextCompactionService.cs:20-25`): the in-memory per-session last-poll stamp and consecutive-
failure counter must survive the hosted service's per-tick scope, or two ticks could double-poll.
A restart losing them re-polls once, which is wasteful, not harmful.

**No `appsettings.json` entry is required** (the defaults are the off state). If one is added for
discoverability it must be `"SubscriptionUsageMonitoring": { "Enabled": false }` — a work-machine
deployment has no subscription at all and this must not run there.

---

## 2. Which sessions the sweep touches

Eligibility is decided in this order, cheapest and least intrusive first. Every "no" is a **skip**,
logged at Debug, retried next interval. Nothing here queues, forces, defers or retries.

1. **Kind has an established poll command** — from `ProviderContractCatalog` (§3), applied as an
   EF-translatable `IN` filter over a precomputed kind array, exactly like
   `ContextCompactionService.WhereEligibleForContextWindow` (`ContextCompactionService.cs:80`,
   `:100`). Claude, OpenCode and Raw never leave the database.
2. **Session row is `Running`** — same predicate `IsAcceptingInputAsync` uses
   (`SessionMessageQueueService.cs:705`).
3. **Session is live on the runner** — `AgentSessionRuntime.ListLiveSessions()`
   (`AgentSessionRuntime.cs:665`). This is the card's "do not poll a stopped/idle-with-no-session
   agent".
4. **Per-session cooldown not active** — `MinPollIntervalMinutes` (default 25) against the
   in-memory stamp AND against the newest stored sample for that session, so a server restart
   cannot re-poll everything on boot.
5. **Pull, then confirm idle.** `AgentSessionRuntime.CatchUpTranscriptAsync(sessionId, ct)`
   (`AgentSessionRuntime.cs:414`) and only then
   `SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct)` (`:1909`, `internal static`).
   This is the CARD-0055 lesson, applied in its non-destructive direction: **never act on "the
   transcript does not show activity" without pulling the transcript first.** `CatchUpTranscriptAsync`
   is the fetch-and-persist half with no queue side effects — `SyncTranscriptAsync` must NOT be used
   (its turn-boundary flush re-enters the queue).
6. **No `Pending` queued message for that session.** A pending message means a real body is about
   to be typed; the poll waits a cycle rather than racing it. (The per-session lock in §3 makes
   this belt-and-braces, but a poll that lands between a flush's lock releases would still push a
   panel over a composer that is about to receive a human's message.)

Sessions are capped at `MaxSessionsPerSweep` per pass (default 10) and each poll gets
`PerSessionTimeoutSeconds` (default 20). Anything skipped for the cap is **logged by count** — a
silent truncation reads as "covered everything" when it did not.

---

## 3. The per-provider poll, and the two hazards it is built around

### 3.1 Command selection lives in `ProviderContractCatalog`, not in the sweep

Add a ninth axis to `ProviderContract` (`server/Application/Dtos/ProviderContract.cs:41`):

```csharp
public sealed record SubscriptionUsagePollContract(
    AgentTuiCapabilityState State,
    string Reason,
    /// <summary>The ONLY body this code may type for this kind. Null unless State is Supported/Degraded.</summary>
    string? Command,
    /// <summary>Keys to press after the command to reach the quota view, in order. Empty = renders directly.</summary>
    IReadOnlyList<string> Navigation,
    /// <summary>Whether the command opens a focus-stealing overlay (CARD-0137) that must be Esc'd closed.</summary>
    bool OpensOverlay,
    /// <summary>Bodies that must NEVER be typed for this kind, with the reason. Enforced by test AND at runtime.</summary>
    IReadOnlyDictionary<string, string> Forbidden);
```

Entries (`server/Application/Services/ProviderContractCatalog.cs`):

| Kind | State | Command | Navigation | OpensOverlay | Forbidden |
|---|---|---|---|---|---|
| `Codex` | `Supported` | `/status` | *(none)* | `false` | `/usage` → "opens a `1. Show usage` / `2. Redeem usage limit reset` picker; a `Mode:"Now"`-style send auto-confirms the highlighted option and can redeem the account's one usage-limit reset (CARD-0141)" |
| `Grok` | `Degraded` | `/usage` | *unmeasured — see S5* | `true` | *(none known)* |
| `ClaudeCode` | `Unknown` | `null` | — | — | — |
| `OpenCode` | `Unknown` | `null` | — | — | — |
| `Raw` | `Unsupported` | `null` | — | — | — |

`ProviderContractCatalog.For` throws on an undefined kind and has no silent default
(`ProviderContractCatalog.cs:23`), which is what makes "skip, do not guess" true *by construction*
rather than by care. `ProviderContractCatalogTests` already asserts every axis is declared with a
non-empty reason for every kind (`tests/Antiphon.Tests/Application/ProviderContractCatalogTests.cs:39`)
— adding the axis to that loop makes a future provider addition fail the build until someone
declares an honest state for it.

**Grok is `Degraded`, deliberately.** The command is measured; the navigation to the `Usage limit`
tab is **not** (CARD-0136 recorded the panel's content after the *operator* opened it manually, and
CARD-0143's own text says "then navigate to the `Usage limit` tab" without saying how). The sweep
polls `Supported` kinds only; `IncludeDegradedProviders` (default `false`) is the switch that turns
Grok on once S5 measures it. This is the same Supported-or-Degraded eligibility idea
`ContextCompactionService.IsContextWindowEligible` uses (`:87`), inverted to be conservative.

### 3.2 The transport: a new narrow method ON `SessionMessageQueueService`

`SessionMessageQueueService.TryPollLocalCommandAsync(Guid sessionId, LocalCommandPoll poll,
CancellationToken ct) → Task<LocalCommandPollResult>`.

It lives on the queue service, not in the sweep, for one non-negotiable reason: **the per-session
semaphore that serialises typing is private to that class** (`GetLock`). A poll that types while a
real message delivery is mid-flight is precisely CARD-0055's stale-body shape — the poll's Enter
would submit the message's body, or vice versa. Putting the method inside the class is a small,
additive change that inherits the lock; reproducing the lock outside would be a second source of
truth for "who may type into this session".

What it does, in order, holding the per-session lock throughout:

1. Re-check live / `Running` / not working / no `Pending` rows. Any no → `Skipped(reason)`.
2. **Assert the command against the contract's `Forbidden` map.** If the caller passed a forbidden
   body for that kind, throw `InvalidOperationException` naming the reason. This is the runtime half
   of "Codex must never receive `/usage`"; the compile-time half is that the sweep reads the command
   from the catalog and never composes one.
3. If `OpensOverlay`: write a bare `0x1b` (Esc) first, wait `OverlaySettleMs`, and snapshot. This
   is CARD-0137's fix — an overlay left open from a previous poll makes the next one fail. Esc
   carries no CR, so `PendingTerminalInput.Append` returns `false`
   (`AgentSessionRuntime.cs:1083-1096`) and no manual turn is started.
4. Snapshot the rendered screen (`AgentSessionRuntime.TryGetLiveSnapshot`), write the command body
   through `_runtime.SendInputAsync(sessionId, body, ct, trackManualTurn: false)` (§3.3), and wait
   for composer evidence with the existing `ComposerDeliveryEvidence.IsVisible` check used by
   `WaitForComposerEvidenceAsync` (`:1491`).
   **No evidence ⇒ withhold Enter and return `NotAccepted`.** No incident, no kill, no retype, no
   queue row. This is the same "the Enter is withheld so the message is never lost into a dead
   composer" contract, minus the destructive tail.
5. Write `"\r"` after the same 20 ms gap the delivery path uses.
6. Wait up to `PanelTimeoutSeconds` for the output sequence to advance
   (`TryGetLiveMetadata().LastSequence`), then press each key in `Navigation` with a settle wait
   between.
7. Read `AgentSessionRuntime.GetBufferSnapshot(sessionId)` (`:656`) — this is the in-process
   equivalent of the `GET /api/sessions/{id}/buffer` the investigations used — and return the raw
   buffer to the caller as `Sent(buffer)`.
8. If `OpensOverlay`: write Esc again to leave the composer at a clean baseline for the next poll
   and for real messages.

**What it deliberately does NOT do**, each one a specific defect it would otherwise inherit:

- **No transcript confirmation.** A local command writes no `UserPrompt` row on Grok or Codex
  (measured, CARD-0141/CARD-0136), so `WaitForTranscriptConfirmAsync` could only ever time out.
- **No Enter re-press.** CARD-0055's re-press exists to recover a swallowed submit of *human-owed
  content*; there is nothing owed here, and a re-press into a panel with focus is an unmodelled
  keystroke.
- **No `HandleDeliveryFailureAsync`, ever.** Therefore no always-on kill, no parking, no
  `DeliveryVerificationFailed` incident.
- **No `SessionQueuedMessage` row.** The poll is not a message; the `SubscriptionUsageSample` row
  is its audit trail, and it records the exact body typed (§5).

### 3.3 One change in `AgentSessionRuntime`: `trackManualTurn`

`SendInputAsync` (`AgentSessionRuntime.cs:735`) calls `TryStartManualTurnTracking` for any input
containing a CR after non-empty text (`:751-754`, `:853`, `:1083`). For a **card-bound** session
that is idle — exactly the sessions this sweep targets — `TryCreateManualRunAttemptAsync` then
creates a new `RunAttempt` at `RunPhase.StreamingTurn`, takes `Card.OwnerSessionId`, and bumps the
card's concurrency token (`:891-951`). Twice an hour, forever, a `/status` poll would manufacture a
spurious attempt on somebody's card. (Cardless standing agents are unaffected — `:903` returns
null.)

Add an optional `bool trackManualTurn = true` parameter to `SendInputAsync`; the local-command path
passes `false`. It is truthful — a local command is not a turn — additive, and every existing caller
keeps today's behaviour.

### 3.4 The Codex hazard, stated as code, not as a comment

Three independent things must all hold before `/usage` can reach a Codex session from this feature:

1. `ProviderContractCatalog.For(AgentKind.Codex).SubscriptionUsagePoll.Command == "/status"` — the
   sweep never composes a command, it reads this one.
2. `Forbidden["/usage"]` is asserted at the top of `TryPollLocalCommandAsync` and throws.
3. A test asserts the adapter's recorded inputs contain `/status` and **do not contain** `/usage`
   (§7 T4), plus a catalog test asserting the forbidden entry exists with a reason (§7 T5).

Nothing in this feature ever answers a picker, a numbered menu, or any prompt: the only keys it may
send are the catalog's `Command`, the catalog's `Navigation` list, `\r`, and `0x1b`. `Navigation`
is a fixed per-kind list in reviewed code, never derived from what is on screen.

---

## 4. Parsing

`SubscriptionUsageParser` — pure static, no DI, no DB, testable against the literal measured text.

Input: the raw buffer. Strip escapes with the existing `AnsiStripper.Clean`
(`src/Antiphon.Agents.Pty/AnsiStripper.cs`) — the same helper `ClaudeDetectors`/`CodexDetectors`
already use to read a rendered screen — falling back to `TerminalScreen` if the concatenating
stripper proves to mangle the Grok panel's box drawing (measure in S5; do not assume).

**Codex** (measured, CARD-0141):

```
Weekly limit:         3% left
                       (resets 22:13 on 24 Aug)
```

→ `RemainingPercent = 3`, `ResetsAtRaw = "22:13 on 24 Aug"`.

**Grok** (measured, CARD-0136):

```
Weekly limit (SuperGrok)
[progress bar]  1%
Resets: August 28, 05:31
```

→ `PlanLabel = "SuperGrok"`, `ResetsAtRaw = "August 28, 05:31"`.

**Two polarity/format traps that must be handled explicitly, not papered over:**

- **Codex says "% left"; Grok's bar says "%" with no word.** Codex is unambiguously *remaining*.
  Grok's is almost certainly *used* (a progress bar at 1% six days before an Aug-28 reset reads as
  "1% consumed", and CARD-0136 treats that account as healthy) — but it is **not measured**, and
  getting it backwards inverts CARD-0136's entire switching decision. The parser stores a
  normalised `RemainingPercent` and the per-kind polarity is a declared constant with the measurement
  that establishes it cited beside it. **Grok's polarity is an S5 measurement item and Grok stays
  `Degraded` until it is settled.**
- **Neither reset timestamp carries a year or a timezone.** `ResetsAt` is therefore an *inference*:
  interpret in the session host's local timezone, resolve to the next future occurrence, convert to
  UTC. The raw string is stored alongside it (`ResetsAtRaw`) as the evidence, so a wrong inference
  is diagnosable rather than invisible. If the raw string is present but unparseable, the row is
  still written with `ResetsAt = null` — a percentage with an unknown reset is worth more than
  nothing, and CARD-0136's rule can decline to act on it.

A buffer where the command was sent but nothing matched writes a sample with
`ParseStatus = Unparsed` and a capped `RawExcerpt`. **Silence on a failed parse is forbidden** —
that is CARD-0067's lesson (a lost reply that was a silent `return`) applied here.

---

## 5. Storage

### 5.1 A new append-only table, not columns on `Agent` or `AgentSession`

`server/Domain/Entities/SubscriptionUsageSample.cs`, `DbSet<SubscriptionUsageSample>
SubscriptionUsageSamples` on `AppDbContext` (beside `:61`), migration
`<ts>_AddSubscriptionUsageSamples`.

| Column | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `Provider` | `AgentKind` | the axis CARD-0136 switches on |
| `SubscriptionKey` | `string` | `Agent.TuiProfileId?.ToString("D")` ?? `Provider.ToString()` — see below |
| `PlanLabel` | `string?` | `"SuperGrok"`; null for Codex, which prints none |
| `RemainingPercent` | `double?` | **normalised to remaining**, 0–100; null when `Unparsed` |
| `ResetsAt` | `DateTime?` (UTC) | inferred (§4); null when absent or unparseable |
| `ResetsAtRaw` | `string?` | the evidence behind `ResetsAt` |
| `ObservedAt` | `DateTime` (UTC) | when the buffer was read |
| `AgentId` | `Guid?` | provenance; null for an unclaimed session |
| `AgentSessionId` | `Guid` | provenance |
| `SourceCommand` | `string` | **literally what was typed** — the durable proof that Codex only ever saw `/status` |
| `ParseStatus` | `string`/enum | `Parsed` \| `Unparsed` |
| `RawExcerpt` | `string?` | ANSI-stripped matched region, capped at 500 chars |

Index: `IX_SubscriptionUsageSamples_Provider_SubscriptionKey_ObservedAt` (`ObservedAt` descending) —
the exact shape of the only query CARD-0136 makes.

**Why append-only samples keyed by subscription, and not a column on an existing entity:**

- **The quota belongs to a subscription, not to an agent or a session.** Two Grok agents on one
  SuperGrok account share one weekly limit. Writing it onto `Agent` would store the same number N
  times with no answer to "which row is authoritative", and would make CARD-0136's decision
  agent-scoped when its rule is account-scoped ("stop using a model/subscription").
- **A session dies; the quota fact must outlive it.** `AgentSession` rows are pruned at
  `RetentionSettings.SessionRetentionDays` (90). A quota reading attached there is deleted on a
  clock that has nothing to do with quota.
- **CARD-0136's own framing wants a series, not a value.** "10% left with a day still to go is a
  much worse burn rate than 10% left with an hour to go" is a rate statement; the card's *stated*
  rule needs only the latest reading, but a history makes the burn-rate refinement it gestures at
  computable later with no schema change. The cost is ~96 rows/day at 2 providers.
- It matches how this repo already stores periodic per-entity facts (`CostLedgerEntry`,
  `TokenUsage`, `AgentIncident`) rather than mutating a wide entity.

**On `SubscriptionKey`:** `AgentTuiProfile` is the closest thing this repo has to a subscription
identity — it is what carries the per-provider home directory and credentials, and `Agent` already
references it (`Agent.TuiProfileId`, `Agent.cs:13`). Using it means two agents on one profile
correctly collapse to one subscription and two profiles on the same kind correctly stay apart.
Where an agent has no profile, the key degrades to the kind name, which is the pre-CARD-0114
one-account-per-kind assumption stated explicitly rather than assumed silently.

### 5.2 How CARD-0136 reads it

`SubscriptionUsageReader` (scoped, read-only, `AsNoTracking`):

```csharp
public sealed record SubscriptionUsageSnapshot(
    AgentKind Provider, string SubscriptionKey, string? PlanLabel,
    double RemainingPercent, DateTime? ResetsAt, DateTime ObservedAt, TimeSpan Age);

Task<IReadOnlyList<SubscriptionUsageSnapshot>> GetLatestAsync(CancellationToken ct);
Task<SubscriptionUsageSnapshot?> GetLatestAsync(AgentKind provider, string subscriptionKey, CancellationToken ct);
```

Latest `Parsed` sample per `(Provider, SubscriptionKey)`. `Age` is carried so CARD-0136 can refuse
to act on a stale reading (a sweep that has been off, or a provider whose sessions all died) rather
than treating an hours-old percentage as current — the failure mode this card's own history is
full of.

**This card builds no threshold, no alert, no switching, and no dispatch pause.** The reader is the
seam; CARD-0136 owns everything on the other side of it.

### 5.3 Retention

Add `SubscriptionUsageRetentionDays = 30` to `RetentionSettings`
(`server/Application/Settings/RetentionSettings.cs`) and a pass in `DataRetentionService`, matching
the existing per-table windows. `<= 0` disables that pass, as the file's own doc-comment specifies.

---

## 6. Settings

`server/Application/Settings/SubscriptionUsageMonitoringSettings.cs`, bound to section
`SubscriptionUsageMonitoring`:

| Setting | Default | Why |
|---|---|---|
| `Enabled` | **`false`** | The card's central requirement. A pay-as-you-go / work-machine deployment has no subscription tier at all, and polling there is meaningless or misdirected. |
| `IntervalMinutes` | `30` | The card's stated cadence. |
| `IncludeDegradedProviders` | `false` | Ships Codex (`Supported`) and holds Grok (`Degraded`) until S5 measures its navigation and its %-polarity. |
| `MinPollIntervalMinutes` | `25` | Per-session floor, checked against both the in-memory stamp and the newest stored sample, so a restart storm cannot re-poll on every boot. |
| `MaxSessionsPerSweep` | `10` | Bounds a pass; anything dropped is logged **by count**. |
| `PerSessionTimeoutSeconds` | `20` | Hard budget per poll. |
| `PanelTimeoutSeconds` | `5` | Wait for the panel to render after Enter. |
| `OverlaySettleMs` | `400` | Settle after Esc / after a navigation key. |
| `ConsecutiveFailuresBeforeIncident` | `3` | §8. |

No validator class is needed (there are no cross-field invariants); `Math.Max` floors at the use
site, as the sibling services do.

---

## 7. Test coverage

`tests/Antiphon.Tests/Application/SubscriptionUsageMonitorTests.cs` —
`[Category("Integration")]`, `[NotInParallel]` **with no group key** (the sweep walks every Running
session of an eligible kind, so it must not run concurrently with anything creating such rows; a
group key would serialise it only against itself — the exact mistake `AgentSupervisionTests` made).
Built on `BridgeQueueHarness` (`tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs`), which
already provides a Running session, an owning agent, and a `FakeAgentProtocolAdapter` recording
`Inputs`/`SubmittedBodies`. **Shared-Postgres rule: every assertion is scoped to a row the test
created — no global counts.**

Pure tests in `SubscriptionUsageParserTests.cs` and `ProviderContractCatalogTests.cs`
(`[Category("Unit")]`).

| # | Test | Asserts |
|---|---|---|
| T1 | `Disabled_by_default` | `new SubscriptionUsageMonitoringSettings().Enabled.ShouldBeFalse()`; `IntervalMinutes == 30`. |
| T2 | `The_sweep_does_nothing_when_disabled` | Seed an idle Codex session; sweep with default settings; `h.Adapter.Inputs.ShouldBeEmpty()`, zero samples for that session. **The card's "gates the ENTIRE feature".** |
| T3 | `The_hosted_service_exits_without_ticking_when_disabled` | `ExecuteAsync` returns; no scope is ever created. |
| T4 | **`Codex_is_only_ever_sent_slash_status`** | Idle Codex session, sweep enabled → the joined `h.Adapter.Inputs` contains `/status`, and `ShouldNotContain("/usage")`; the stored sample's `SourceCommand == "/status"`. |
| T5 | **`The_catalog_forbids_slash_usage_for_Codex_with_a_reason`** | `Forbidden` contains `/usage` with a non-empty reason naming the reset-redemption hazard; `TryPollLocalCommandAsync` throws if handed `/usage` for a Codex session. |
| T6 | **`Nothing_is_sent_to_a_session_that_is_not_idle`** | Seed activity after the last `TurnEnd` (the `ContextCompactionSweepTests.Skips_busy` recipe, `tests/Antiphon.Tests/Application/ContextCompactionSweepTests.cs:49`) → `h.Adapter.Inputs.ShouldBeEmpty()`, no sample. |
| T7 | `Skips_a_session_that_is_not_Running_or_not_live` | Two cases; nothing typed. |
| T8 | `Skips_kinds_with_no_established_command` | ClaudeCode / OpenCode / Raw sessions → nothing typed, and they are absent from the eligible-kind array. |
| T9 | `Grok_is_skipped_while_it_is_Degraded` | With `IncludeDegradedProviders = false` (default) a Grok session is not polled; with it `true` it is. |
| T10 | `A_poll_with_no_composer_evidence_withholds_Enter_and_kills_nothing` | `FakeAgentProtocolAdapter.EchoTypedInputToScreen = false` → no `\r` written, session still `Running`, `agent.Status` unchanged, zero `SessionQueuedMessage` rows, zero `AgentIncident` rows for that session. **This is the anti-regression test for the kill path in §Verdict.** |
| T11 | `A_local_command_poll_starts_no_manual_run_attempt` | Card-bound idle session; after a poll, no new `RunAttempt` for that card and `Card.OwnerSessionId` unchanged. |
| T12 | `Parses_the_measured_Codex_panel` | The literal CARD-0141 text, wrapped in ANSI noise → `RemainingPercent == 3`, `ResetsAtRaw == "22:13 on 24 Aug"`. |
| T13 | `Parses_the_measured_Grok_panel` | The literal CARD-0136 text → `PlanLabel == "SuperGrok"`, `ResetsAtRaw == "August 28, 05:31"`, remaining normalised per the declared polarity. |
| T14 | `A_reset_string_with_no_year_resolves_to_the_next_future_occurrence` | Fixed `TimeProvider`; and an unparseable one yields `ResetsAt == null` with `ResetsAtRaw` preserved. |
| T15 | `An_unparsable_panel_records_an_Unparsed_sample_rather_than_nothing` | Row exists with `ParseStatus = Unparsed` and a non-empty `RawExcerpt`. |
| T16 | `Respects_the_per_session_minimum_interval` | Two sweeps inside `MinPollIntervalMinutes` → one poll; the stamp also survives a fresh service instance via the stored sample. |
| T17 | `The_reader_returns_the_newest_parsed_sample_per_subscription` | Three samples for one key, one `Unparsed` newest → the newest `Parsed` wins; scoped to this test's `SubscriptionKey`. |
| T18 | `Every_AgentKind_declares_the_new_axis_with_a_reason` | Extend `ProviderContractCatalogTests.Every_axis_is_declared_with_a_reason` (`:39`) and `Nothing_defaults_to_Supported_via_an_empty_reason` (`:55`). |

Run with `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-usage/
--treenode-filter "/*/Antiphon.Tests.Application/*/*"` (never `dotnet test`), and delete the
`bin-usage/` directories afterwards.

---

## 8. Failure handling — what happens when a poll goes wrong

The card's requirement is "skip and retry next interval, not force/queue it", and every branch below
obeys it. **No branch of this feature may kill a session, park a message, requeue a body, retype
anything, press Enter twice, or answer a prompt.**

| Failure | Response |
|---|---|
| Session not live / not `Running` / working / has `Pending` messages | Skip, Debug log, retry next interval. Nothing typed. |
| Composer evidence never appears (CARD-0137's overlay shape, a wedged TUI, a modal) | **Enter withheld.** `NotAccepted`, Debug log, no incident, no sample, retry next interval. |
| Enter sent, panel never renders (sequence never advances within `PanelTimeoutSeconds`) | Give up; write **no** sample; Debug log. Send the closing Esc if `OpensOverlay`. |
| Panel renders, parse fails | Write an `Unparsed` sample with the excerpt (§4). Counts as a *poll*, not a failure, for the cooldown; counts as a failure for the streak counter. |
| Transport throws (runner down, session vanished mid-poll) | Caught per session inside the sweep loop, `LogWarning`, continue to the next session — `ContextCompactionService.SweepAsync`'s own per-session try/catch shape (`:145-155`). An `OperationCanceledException` is rethrown only when the token is actually cancelled (the `HttpClient`-timeout rule). |
| The whole sweep throws | Caught in the hosted service, `LogWarning`, next tick. |
| `ConsecutiveFailuresBeforeIncident` (3) consecutive failed polls for one session | **One** `AgentIncident` — new `AgentIncidentKind.SubscriptionUsagePollDegraded = 30` (next free; `RunnerBuildStale = 29` is the current highest), `AlertSeverity.Warning`, deduped, not re-raised until a poll succeeds. Rationale: a permanently-broken poll must be *visible* (CARD-0067's silent-`return` lesson) but must not write 48 rows a day (CARD-0101's 37 ignored identical Warnings). Never Critical — a failed usage poll is a missing convenience, not a broken agent. |

---

## 9. Slices

Each slice is independently testable, independently commitable, and leaves the feature off.

- **S1 — contract + parser + settings (no runtime).** The ninth `ProviderContract` axis with all
  five kinds declared, `SubscriptionUsageParser`, `SubscriptionUsageMonitoringSettings`.
  Tests: T1, T5, T12–T14, T18. Nothing is wired; nothing can run.
- **S2 — storage + reader.** Entity, `DbSet`, migration, `SubscriptionUsageReader`,
  `RetentionSettings.SubscriptionUsageRetentionDays` + the `DataRetentionService` pass.
  Tests: T15, T17.
- **S3 — the local-command transport.** `SendInputAsync(trackManualTurn:)` on
  `AgentSessionRuntime`; `TryPollLocalCommandAsync` on `SessionMessageQueueService`.
  Tests: T10, T11 (driven directly, before any sweep exists).
- **S4 — the sweep + hosted service + registration.** `SubscriptionUsageMonitorService`,
  `SubscriptionUsageMonitorHostedService`, `Program.cs` wiring, the
  `SubscriptionUsagePollDegraded` incident kind.
  Tests: T2–T4, T6–T9, T16. **Ships with `Enabled = false`.** Codex only.
- **S5 — the Grok measurement (live, operator-attended).** Drive a real, idle Grok session through
  the S3 transport with `IncludeDegradedProviders = true` on a scratch config and record: (a) which
  tab `/usage` opens on and the exact key(s) that reach `Usage limit`; (b) whether the bar's
  percentage is *used* or *remaining*; (c) whether Esc-then-`/usage` reliably re-opens the panel
  from a stale-overlay state (CARD-0137's actual open question); (d) whether `AnsiStripper.Clean`
  or `TerminalScreen` reconstructs the panel better. Then flip Grok to `Supported` with the
  navigation list filled in, and add its arm to T4/T13. **Do not guess any of these.**
- **S6 (optional, out of scope for the card) —** surface the latest snapshot per subscription on the
  agent card / a diagnostics endpoint. Data collection alone is invisible; CARD-0136 will want a
  surface anyway.

---

## 10. Open questions and risks

1. **Grok's percentage polarity is unmeasured (§4).** Getting it backwards inverts CARD-0136's
   whole decision. Held behind `Degraded` + S5.
2. **Grok's tab navigation is unmeasured (§3.1).** Same holding.
3. **CARD-0137's cold-start case remains untested even for Codex** — Codex renders into scrollback
   with no overlay, so the shape should not apply, but the first live S4 run is the first time this
   repo has driven `/status` into a Codex session that already has transcript rows. Watch for
   `NotAccepted` on the first poll of each session.
4. **Reset timestamps carry no year and no timezone (§4).** `ResetsAt` is an inference; `ResetsAtRaw`
   is the evidence. CARD-0136 should prefer `ResetsAtRaw` for anything it shows a human.
5. **`SubscriptionKey` assumes one subscription per TUI profile.** True today; if a profile is ever
   re-pointed at a different account the samples straddle two subscriptions under one key. A future
   `AccountKey` column sourced from the profile's credential identity closes it; not needed now.
6. **A sample is a point reading, not a ledger.** Between polls the quota moves and nothing knows.
   That is inherent to a 30-minute poll and is why CARD-0136 must read `Age` and refuse to act on a
   stale snapshot.
7. **This feature types into live agent terminals.** That is unavoidable given the only measured
   telemetry source is a TUI panel — but it is the reason for the per-session lock, the withheld
   Enter, the absent retry, and the flat prohibition on any destructive branch. If a cheaper source
   ever appears (an account-level usage API, CARD-0136's `ccusage`-style local estimate), it should
   replace this transport, not sit beside it.
