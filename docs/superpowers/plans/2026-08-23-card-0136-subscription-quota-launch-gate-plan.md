# CARD-0136 — warn-and-override subscription-quota gate at launch: plan

**Date:** 2026-08-23 · **Card:** CARD-0136 (`7b912237-ce4c-4d80-98e3-5b811ef3e04c`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** master `4c2ce34`. Every line number below was re-read out of the code on that
commit.

**Established facts, not re-derived here (all recorded on the card):** the telemetry sources
(Grok `/usage` → `Usage limit` tab; Codex `/status`, never bare `/usage`; Claude has no proactive
command), the prerequisite (`SubscriptionUsageReader.GetLatestAsync`, CARD-0143, merged `47196b5`,
feature-flagged off), and the ACTING mechanism (warn-and-override at launch, **not** automatic
switching). This plan turns that mechanism into code shapes. It does not reopen any of it.

**Related:** CARD-0143 (the reader this gate consumes), CARD-0138 / CARD-0139 (one rule, several
write paths, one helper — the shape borrowed for the subscription key), CARD-0140 (the profile
pre-flight in the dispatcher, which is the pattern of "refuse before any side effect"), CARD-0083
(`ProviderContractCatalog`, where the Claude `Unknown` that makes pass-through mandatory is
recorded), the usage-limit-and-api-error-resilience spec (the REACTIVE half; this gate never touches
`ApiErrorTurnDied` or `ApiErrorRecoveryService`).

---

## Verdict up front

1. **The gate is a 409, not a 200-with-a-warning-field.** A launch that would trip the threshold is
   *refused* with `code: "subscription_quota_low"` and a detail that names the provider, the key, the
   remaining %, and the time to reset. A 200 is a launch that already happened; a "warning" attached
   to it is a notice about work that is already burning the quota, i.e. silent automatic launching
   with a footnote — the exact thing the card rules out. The caller's two ways forward are the card's
   two: pick another provider (re-send with a different `agentKind` / agent), or re-send **the same
   request** with the override flag. Both are one more call, both are explicit.

2. **The flag is `ignoreSubscriptionQuota`** (`bool`, default `false`) on `StartAgentRequest` and
   `CreateAgentTaskRequest`, and a `-IgnoreSubscriptionQuota` switch on `scripts/delegate.ps1`. Not
   `forceLaunch` — this repo has no generic force flags (none in `server/Application/Dtos/*.cs`) and
   names every opt-out by the rule it opts out of (`DenyDirectEdits`, `Fresh`, `RemoteControl`,
   `IncludeDegradedProviders`). Not `ignoreSubscriptionLimit` — "limit" is the word the reactive
   system already owns (`UsageLimitSignalContract`, `UsageLimitResetParser`); the thing this flag
   ignores is a *quota reading*, and an override of "the limit was hit" would be a different and
   much more dangerous flag.

3. **Two hook points, one rule, one helper — and neither hook is in the dispatcher.** The card names
   `AgentTaskDispatcher`'s launch path, but the dispatcher runs on a background tick
   (`TickAsync`, `AgentTaskDispatcher.cs:143`) long after the HTTP caller has gone: there is nobody
   there to warn, so a gate there can only silently block, which the card forbids. The caller is
   present at **`AgentTaskService.CreateAsync`** (`AgentTaskService.cs:90`, behind
   `POST /api/agent-tasks`), so that is where the task-path gate lives. The agent path gates in
   **`AgentControlService.StartAsync`** (`AgentControlService.cs:80`), *before* the card/interactive
   branch at `:106-118`, so one hook covers both the cardless interactive start and the
   start-onto-a-queued-card spawn. Both hooks call one `SubscriptionQuotaGate.EnforceAsync` and one
   pure `SubscriptionQuotaPolicy.Evaluate`, following CARD-0138's `AgentProfileKind.Sync`
   (`AgentProfileKind.cs:12-16`): the rule is stated once, the write paths call it.

4. **No reading ⇒ pass-through, always.** Claude has no proactive command
   (`ProviderContractCatalog.cs:67-72`, `SubscriptionUsagePoll: Unknown`), monitoring ships disabled
   (`SubscriptionUsageMonitoringSettings.Enabled=false`, `:11`), and a deployment may have zero
   samples for months. `GetLatestAsync` returning `null` is not evidence of anything and the gate
   returns "pass" without a log line above Debug. The same applies to a reading that is **stale**
   (older than `MaxSampleAgeMinutes`) or whose **reset has already passed** (`ResetsAt < now` — the
   quota refreshed after we looked). The gate may only ever refuse on a *fresh, positive* reading.

5. **Internal callers of `StartAsync` opt out explicitly, in code.** `AgentSupervisorService.cs:200`
   (AlwaysOn restart), `ChannelBridgeService.cs:355` (start-to-receive-a-channel-message) and
   `CheckInterpreterProvisioner.cs:126` (deployment warm-up) all construct `new StartAgentRequest(...)`;
   none is a human who can choose a provider, and a supervisor that stops restarting an agent
   because of a quota reading is the "silently stop dispatching" behaviour the card rejected. They
   pass `IgnoreSubscriptionQuota: true` with a one-line comment each. The gate therefore fires only
   for launches that arrive over HTTP (`AgentEndpoints.cs:122-129`, `AgentTaskEndpoints.cs` create),
   which is the population that can act on a 409.

6. **Client UI is out of scope for this card.** `delegate.ps1` is the primary launch caller today
   and already prints the server's 409 detail (`Invoke-Antiphon` catch, `delegate.ps1:117-123`).
   The web client's `useStartAgent` (`client/src/api/agents.ts:449`) surfaces a 409 through the
   existing `ApiError` → `getApiErrorMessage` path (`client/src/api/client.ts:3,17`), so the operator
   *sees* the refusal; what they cannot do yet is click "launch anyway". That confirm-dialog is a
   natural follow-up card (§6), not a silent drop.

---

## 1. What the code does today

### 1.1 The reader (CARD-0143)

`SubscriptionUsageReader` (`server/Application/Services/SubscriptionUsageReader.cs`) is scoped,
registered at `Program.cs:362`, and exposes:

- `GetLatestAsync(CancellationToken)` (`:22`) — newest `Parsed` sample per `(Provider, SubscriptionKey)`.
- `GetLatestAsync(AgentKind provider, string subscriptionKey, CancellationToken)` (`:37`) — newest
  `Parsed` sample for one key, or `null`.

Both return `SubscriptionUsageSnapshot` (`:63-70`):
`(AgentKind Provider, string SubscriptionKey, string? PlanLabel, double RemainingPercent,
DateTime? ResetsAt, DateTime ObservedAt, TimeSpan Age)`. `Age` is computed against
`TimeProvider.GetUtcNow()` at read time; `ResetsAt` is nullable because the parser may fail to read
the reset line while still reading the percentage.

The key is built by `SubscriptionUsageMonitorService.KeyFor` (`SubscriptionUsageMonitorService.cs:344`):
`owner?.TuiProfileId is Guid id ? id.ToString("D") : kind.ToString()`. It is `internal static` and
private to the monitor today; nothing else in the repo derives a subscription key.

### 1.2 Launch path A — standing agent start

`AgentControlService.StartAsync(Guid agentId, StartAgentRequest request, ct)` (`AgentControlService.cs:80`):

| Line | Step |
|---|---|
| `:82` | `LockAgentAsync` |
| `:88` | `ClearSupervisionLatchAsync` |
| `:91-92` | **idempotent return** if `HasLiveSessionAsync` — an already-running agent is not a launch |
| `:98` | `_workspace?.Provision(agent)` (CLAUDE.md floor) — a side effect |
| `:100-102` | resolve remote-control name, `ResolveStartCardAsync` |
| `:105-113` | card branch → `_cardService.SpawnAsync(...)` |
| `:114-118` | cardless branch → `StartInteractiveSessionAsync(agent, remoteControlName, request.Fresh, ct)` (`:133`) |
| `:120-124` | persist `PersistentSessionId`, `Status = Running`, publish `AgentChanged` |

The provider kind is available *before* either branch via `PeekProfileKindAsync(agent, ct)`
(`:377-404`): profile kind if `agent.TuiProfileId` is set, else the default profile's kind, else the
legacy registry default. It is currently called inside `StartInteractiveSessionAsync` (`:161`); the
gate needs it one level up.

`StartAgentRequest` is `record StartAgentRequest(bool? RemoteControl = null, bool Fresh = false)`
(`AgentDtos.cs:260`). The HTTP entry is `POST /api/agents/{id}/start` (`AgentEndpoints.cs:122-129`).

Internal callers (all non-HTTP): `AgentSupervisorService.cs:200`, `ChannelBridgeService.cs:355`,
`CheckInterpreterProvisioner.cs:126`.

### 1.3 Launch path B — task / sub-task dispatch

Two halves, separated in time:

**Create** — `AgentTaskService.CreateAsync(CreateAgentTaskRequest, Caller, ct)` (`AgentTaskService.cs:90`),
behind `POST /api/agent-tasks` (`AgentTaskEndpoints.cs:15`). Kind is resolved at `:225`
(`ResolveAgentKind(request.Kind, request.Role, request.AgentKind)`), after the follow-up and pinned-agent
arms at `:113-168` have already forced `request.AgentKind` to the agent's kind. The row is added at
`:277`; `SaveChangesAsync` at `:296`. It already has an informational-warning mechanism:
`ResolveWorkspace` returns `(workspace, warning)` (`:221`), the warning is recorded as an
`AgentTaskEventType.Warning` event (`:294-295`) and returned on `AgentTaskCreatedDto.Warning`
(`AgentTaskDtos.cs:131-132`), which `delegate.ps1:207` prints. **This is informational — the task is
queued regardless.**

**Dispatch** — `AgentTaskDispatcher.DispatchOneAsync` (`AgentTaskDispatcher.cs:1472`), on the tick.
Pre-flight that refuses *before any side effect* is `ResolveDelegateProgramAsync` (`:1774-1828`,
CARD-0140), which throws `ConflictException(..., "profile_disabled")` / `"profile_not_validated"`;
the outer tick catches and fails the task. The session row is built at `:1554`, the spec at `:1608`.
A pinned standing agent's profile id is known here (`DelegateProgram.ProfileId`, `:1772`); a pool
delegate never has one (`ResolveAgentAsync`, `:2042-2088`, creates `IsPoolDelegate = true` rows
with no `TuiProfileId`), so its monitor key is the kind name.

### 1.4 The refusal shape this repo already uses

`ConflictException(message, code)` (`server/Application/Exceptions/ConflictException.cs:12`) →
`ExceptionMiddleware` emits `application/problem+json` with `status`, `detail`, `traceId` and, when
`HttpException.Code` is set, `code` (`ExceptionMiddleware.cs:55-65`). `delegate.ps1` prints
`$_.ErrorDetails.Message` (the whole problem document) and exits 1 (`:117-123`).

### 1.5 Settings conventions

POCO in `server/Application/Settings/`, bound in `Program.cs` with
`builder.Services.Configure<T>(builder.Configuration.GetSection("Name"))` (`:147-149`), consumed via
`IOptions<T>`; defaults live on the property initialisers, and `appsettings.json` carries only
overrides. `SubscriptionUsageMonitoringSettings` (`:8-45`) is the closest sibling.

---

## 2. Design decisions

### D1 — one policy, two hooks, no dispatcher gate

**Policy** — `SubscriptionQuotaPolicy` (`server/Application/Services/SubscriptionQuotaPolicy.cs`,
`public static`), modelled on `TaskProgressPolicy` / `TaskDeadlinePolicy`: a pure function of
`(SubscriptionUsageSnapshot? snapshot, SubscriptionQuotaGateSettings settings, DateTime now)` →
`SubscriptionQuotaVerdict?` (`null` = pass). No I/O, fully unit-testable with hand-built snapshots.

**Gate** — `SubscriptionQuotaGate` (scoped; ctor `SubscriptionUsageReader`,
`IOptions<SubscriptionQuotaGateSettings>`, `TimeProvider`, `ILogger<SubscriptionQuotaGate>`):

```csharp
public sealed record SubscriptionQuotaVerdict(
    AgentKind Provider, string SubscriptionKey, string? PlanLabel,
    double RemainingPercent, DateTime? ResetsAt, TimeSpan? TimeToReset,
    DateTime ObservedAt, string RuleName);

// null = pass. Never throws on missing/stale data.
Task<SubscriptionQuotaVerdict?> EvaluateAsync(AgentKind provider, string subscriptionKey, CancellationToken ct);

// Throws SubscriptionQuotaLowException unless ignore; returns the verdict (null = clean pass,
// non-null = tripped-but-overridden) so the caller can record the override.
Task<SubscriptionQuotaVerdict?> EnforceAsync(
    AgentKind provider, string subscriptionKey, bool ignore, string launchDescription, CancellationToken ct);
```

**Hook A** — `AgentControlService.StartAsync`, inserted between the idempotent return (`:91-92`) and
`_workspace?.Provision(agent)` (`:98`), so a refused launch provisions nothing and spawns nothing:

```csharp
var kind = await PeekProfileKindAsync(agent, ct);          // existing, :377
if (kind is AgentKind k && _quotaGate is not null)
{
    var overridden = await _quotaGate.EnforceAsync(
        k, SubscriptionUsageKey.For(agent, k), request.IgnoreSubscriptionQuota,
        $"start of agent '{agent.Name}'", ct);
    if (overridden is not null) await RecordQuotaOverrideIncidentAsync(agent, overridden, ct);
}
```

`_quotaGate` is an optional ctor parameter (`SubscriptionQuotaGate? quotaGate = null`), the same
shape as `launchResolver` / `workspace` / `apiKeyEnvResolver` at `:53-57`, so the existing
`AgentControlServiceIntegrationTests` harness keeps constructing the service unchanged and tests that
want the gate wire it explicitly.

**Hook B** — `AgentTaskService.CreateAsync`, immediately after `agentKind` is resolved (`:225`) and
before anything is added to the context (`:277`):

```csharp
var quotaKey = await ResolveSubscriptionKeyAsync(request.AgentId, agentKind, ct); // D3
var quotaOverride = await _quotaGate.EnforceAsync(
    agentKind, quotaKey, request.IgnoreSubscriptionQuota,
    $"task '{BuildTitle(request)}'", ct);
```

When `quotaOverride` is non-null it is folded into the **existing** warning channel: appended to
`warning` (`:221`) so it lands as the `AgentTaskEventType.Warning` event at `:294` *and* on
`AgentTaskCreatedDto.Warning`, which `delegate.ps1:207` already prints — zero script change for the
override-audit path.

**No gate in `DispatchOneAsync`.** Stated reasons: (i) no caller present — a refusal there is a
silent block or a silent fail-the-task, both forbidden by the card; (ii) the task was already
admitted under the rule at create time, and re-judging it minutes later against the same 30-minute
sample cannot produce new information; (iii) the reactive system (`ApiErrorTurnDied`) already owns
"it actually hit the wall". The dispatcher gets **one informational line only**: in
`DispatchOneAsync`, after `ResolveDelegateProgramAsync` (`:1518`), `EvaluateAsync` (never
`EnforceAsync`) and, if tripped, an `AgentTaskEventType.Warning` event reading
`"dispatched on <kind> at <n>% remaining, resets in <t> (quota gate was passed/overridden at create)"`.
That keeps the board honest about what the delegate was launched into without adding a second
decision point.

**Out of scope, named:** `POST /api/sessions` (`SessionEndpoints.cs:39`, raw definition launch with
no agent and no profile — a dev tool) and `POST /api/cards/{id}/spawn` direct (`CardService.SpawnAsync`,
`CardService.cs:532`, when invoked from the board rather than through `StartAsync`). Both are
launches; neither is how delegate.ps1 or the agent Start button launch. Listed in §8.

### D2 — the threshold rule and its settings

```csharp
// server/Application/Settings/SubscriptionQuotaGateSettings.cs  — section "SubscriptionQuotaGate"
public sealed class SubscriptionQuotaGateSettings
{
    /// <summary>Whole-gate switch. Default TRUE: with no samples the gate is inert (D4), so
    /// enabling it costs nothing until monitoring is turned on, and turning monitoring on
    /// then activates the gate without a second flag to remember.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>A sample older than this is no evidence and passes through (D4).
    /// Default 6× the monitor's 30-minute cadence.</summary>
    public int MaxSampleAgeMinutes { get; set; } = 180;

    /// <summary>When a sample carries a percentage but no parseable reset time, assume this
    /// long until reset. Default one week — all three measured providers expose a WEEKLY
    /// limit, so unknown is treated as worst case, never as "about to reset".</summary>
    public int AssumedMinutesToResetWhenUnknown { get; set; } = 10_080;

    /// <summary>ANY rule tripping refuses. Evaluated in order; the first trip names the verdict.</summary>
    public List<SubscriptionQuotaRule> Rules { get; set; } =
    [
        new() { Name = "low-with-a-day-left",  MaxRemainingPercent = 10, MinMinutesToReset = 1440 },
        new() { Name = "critical-with-hours-left", MaxRemainingPercent = 5, MinMinutesToReset = 120 },
    ];
}

public sealed class SubscriptionQuotaRule
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Trips when RemainingPercent &lt;= this ...</summary>
    public double MaxRemainingPercent { get; set; }
    /// <summary>... AND time-to-reset &gt; this.</summary>
    public int MinMinutesToReset { get; set; }
}
```

`SubscriptionQuotaPolicy.Evaluate`:

1. `!settings.Enabled` → pass.
2. `snapshot is null` → pass.
3. `snapshot.Age > MaxSampleAgeMinutes` → pass (Debug log: stale).
4. `timeToReset = snapshot.ResetsAt is DateTime r ? r - now : AssumedMinutesToResetWhenUnknown`.
5. `timeToReset <= TimeSpan.Zero` → pass (the quota has reset since the observation; the reading is
   about a window that no longer exists).
6. First rule with `RemainingPercent <= MaxRemainingPercent && timeToReset > MinMinutesToReset`
   → verdict naming that rule. None → pass.

The card's anchors are the defaults (10 % / >1 day, 5 % / >2 h); an `IValidateOptions` validator
rejects a rule with `MaxRemainingPercent` outside `[0,100]` or negative minutes, same as
`ContextCompactionSettingsValidator` (`Program.cs:152`). The burn-rate refinement the CARD-0143 plan
mentions (using the sample *history*) is deliberately not built; the rule reads one snapshot.

### D3 — one subscription key, derived in one place

Promote the monitor's `KeyFor` to a public static in its own file so the writer (monitor) and the
reader (gate) cannot drift — the CARD-0138 move:

```csharp
// server/Application/Services/SubscriptionUsageKey.cs
public static class SubscriptionUsageKey
{
    /// <summary>The CARD-0143 identity, stated once: a TUI profile is the subscription; with no
    /// profile the key degrades to the kind name (one account per kind).</summary>
    public static string For(Agent? owner, AgentKind kind) =>
        owner?.TuiProfileId is Guid id ? id.ToString("D") : kind.ToString();
}
```

`SubscriptionUsageMonitorService.KeyFor` (`:344`) becomes a one-line forward and its existing tests
keep passing. For the task path, `AgentTaskService.ResolveSubscriptionKeyAsync(Guid? agentId, kind)`:
pinned non-pool agent (already loaded at `:154-156` for the kind check) → `For(pinned, kind)`;
everything else (unpinned, pool, follow-up where `prior.AgentId` resolved) → `For(agentOrNull, kind)`.
This is exactly how the monitor will key that delegate's own samples later, so the gate reads the
row the monitor writes.

**Provider-wide fallback, decided against.** If the exact key has no sample but the *provider* has
one under a different key (a Codex profile's samples, a pool Codex delegate asking), do not borrow
it. Two keys are two subscriptions by construction; warning on another account's reading is a false
positive the operator has to learn to ignore, which is how a warning stops being read. If it turns
out that every Codex launch in practice shares one account, the fix is to give the pool a profile,
not to blur the key.

### D4 — missing, stale, or already-reset data passes

Covered in D2 steps 2-5; stated as its own decision because it is the property the card calls out
("must no-op/pass-through cleanly for a provider with no data, not block by default"). Claude will
never have a sample. A Grok key will have none until `IncludeDegradedProviders` is set
(`SubscriptionUsageMonitoringSettings.cs:20`). A parse regression leaves `RemainingPercent` null,
which the reader already filters out (`SubscriptionUsageReader.cs:27`), so a broken parser reads as
"no data", not as "0 % left". The gate can only say no on a fresh positive number.

### D5 — the HTTP shape

New `SubscriptionQuotaLowException : HttpException` — 409, `code = "subscription_quota_low"`, and
a `Quota` extension object so a programmatic caller does not have to parse the sentence:

```json
{
  "type": "...", "title": "Conflict", "status": 409,
  "code": "subscription_quota_low",
  "detail": "Codex subscription 'SuperPlan' (key 3f2c…) has 3% remaining and resets in 1d 12h (rule low-with-a-day-left). Pick another agentKind/agent, or re-send with ignoreSubscriptionQuota=true to launch anyway.",
  "quota": { "provider": "Codex", "subscriptionKey": "…", "planLabel": "…", "remainingPercent": 3,
             "resetsAt": "2026-08-24T21:13:00Z", "minutesToReset": 2161,
             "observedAt": "…", "rule": "low-with-a-day-left" },
  "traceId": "…"
}
```

Mechanics: `HttpException` gains an optional `IReadOnlyDictionary<string, object?>? Extensions`
(null for every existing exception); `ExceptionMiddleware` merges it into the problem document next
to `code` (`:64-65`). 409 rather than 4xx-of-our-own (e.g. 428) because every refusal-with-a-code in
this codebase is a 409 (`profile_disabled`, `profile_not_validated`, `AgentTaskDispatcher.cs:1797-1807`)
and `delegate.ps1` / the client already treat 409 as "server said no, here is why".

### D6 — the override flag and where it goes

- `StartAgentRequest(bool? RemoteControl = null, bool Fresh = false, bool IgnoreSubscriptionQuota = false)`
  (`AgentDtos.cs:260`).
- `CreateAgentTaskRequest(..., int? ExpectedMinutes = null, bool IgnoreSubscriptionQuota = false)`
  (`AgentTaskDtos.cs:10-52`), with the doc-comment naming the 409 code it bypasses.
- `scripts/delegate.ps1`: `[switch]$IgnoreSubscriptionQuota` on the Create set; body gets
  `ignoreSubscriptionQuota = $true` only when set (the "sent only when chosen" convention at
  `:174-188`). Its help text names the 409 and both ways out.
- `client/src/api/agents.ts` `StartAgentRequest` type gains the optional field (no UI wires it in
  this card — §6).
- The three internal `StartAsync` callers pass `IgnoreSubscriptionQuota: true` (verdict 5). A
  follow-up task (`FollowUpOnTask`) is **gated** like any create: it is new work typed into a
  subscription, and the override is one switch away.

### D7 — the override leaves a trace

An explicit opt-out per call must be findable afterwards.

- Task path: the existing `Warning` event + `AgentTaskCreatedDto.Warning` (D1 Hook B).
- Agent path: new `AgentIncidentKind.SubscriptionQuotaOverridden = 33`, Warning severity, one row
  per overridden start, message = the verdict sentence. Not deduped — every override is a separate
  decision. Never Critical: the operator chose this.
- Both: one `LogWarning` from `EnforceAsync` with provider/key/%/time-to-reset/launch description.

A clean pass logs nothing above Debug; a refusal logs Information (the 409 is the record).

---

## 3. Slices

| Slice | Content | Tests |
|---|---|---|
| **S1** | `SubscriptionUsageKey` (D3) + monitor forward; `SubscriptionQuotaGateSettings` + validator + `Program.cs` binding; `SubscriptionQuotaPolicy` (D2); `SubscriptionQuotaVerdict`; `SubscriptionQuotaLowException` + `HttpException.Extensions` + middleware merge (D5); `SubscriptionQuotaGate` registered scoped. No hook yet. | T1–T9, T16 |
| **S2** | Hook A: `StartAgentRequest.IgnoreSubscriptionQuota`, gate call in `StartAsync`, `SubscriptionQuotaOverridden = 33` incident, three internal callers opt out. | T10–T12 |
| **S3** | Hook B: `CreateAgentTaskRequest.IgnoreSubscriptionQuota`, `ResolveSubscriptionKeyAsync`, gate call in `CreateAsync`, warning fold-in; dispatcher informational event (D1 last paragraph). | T13–T15, T17 |
| **S4** | `delegate.ps1 -IgnoreSubscriptionQuota`; `client/src/api/agents.ts` type; `AGENTS.md`/CLAUDE.md gotcha line; card note. | T18 (script) |

Each slice commits and pushes on its own with the test counts in the message, per the delegate rules.

---

## 4. Verification / test design

Unit tests go in `tests/Antiphon.Tests/Application/SubscriptionQuotaGateTests.cs` (policy + gate
with an in-memory `TestDbFixture` context, scoped keys per test so they never read another test's
samples — the shared-Postgres rule). Hook tests extend the existing harnesses:
`AgentControlServiceIntegrationTests` (constructs `AgentControlService` with optional deps) and
`AgentTaskAgentKindTests.CreateService` (`:537`), which gains a `SubscriptionQuotaGate?` argument.
A sample is planted by inserting a `SubscriptionUsageSample` row directly, the way
`SubscriptionUsageMonitorTests.cs:298-304` already exercises the reader.

| # | Test | Proves |
|---|---|---|
| T1 | `Evaluate_passes_when_there_is_no_snapshot` | D4 — null ⇒ pass |
| T2 | `Evaluate_passes_when_the_sample_is_older_than_MaxSampleAge` | D4 — stale ⇒ pass |
| T3 | `Evaluate_passes_when_ResetsAt_is_already_in_the_past` | D4 — reset since observed ⇒ pass |
| T4 | `Evaluate_trips_the_day_rule_at_10_percent_with_36h_left` | anchor rule 1 |
| T5 | `Evaluate_does_not_trip_at_10_percent_with_6h_left` | the RELATIVE-to-reset half is real |
| T6 | `Evaluate_trips_the_hours_rule_at_5_percent_with_3h_left` | anchor rule 2 |
| T7 | `Evaluate_does_not_trip_at_5_percent_with_1h_left` | |
| T8 | `Evaluate_uses_the_assumed_week_when_ResetsAt_is_null` | D2 step 4 — unknown is worst case |
| T9 | `Evaluate_is_inert_when_Enabled_is_false` | |
| T10 | `Start_returns_409_subscription_quota_low_on_a_fresh_low_Codex_reading` | Hook A refuses; asserts `code`, `quota.remainingPercent`, and that **no session row and no `Provision` call** happened |
| T11 | `Start_with_IgnoreSubscriptionQuota_launches_and_writes_SubscriptionQuotaOverridden` | the flag bypasses at Hook A and leaves the incident |
| T12 | `Start_of_a_Claude_agent_passes_with_no_sample` | Claude pass-through through the real hook |
| T13 | `Create_returns_409_subscription_quota_low_for_a_pinned_agent_whose_profile_key_is_low` | Hook B, keyed by profile id |
| T14 | `Create_with_IgnoreSubscriptionQuota_queues_the_task_and_carries_the_warning` | bypass at Hook B; `AgentTaskCreatedDto.Warning` non-null; `Warning` event present |
| T15 | `Create_of_an_unpinned_Codex_task_does_not_borrow_a_profiles_reading` | D3 — no provider-wide fallback |
| T16 | `KeyFor_and_SubscriptionUsageKey_agree_for_profile_and_profileless_agents` | D3 lockstep |
| T17 | `Dispatch_records_an_informational_warning_and_never_refuses_on_a_low_reading` | D1 — dispatcher does not gate (task reaches `Dispatched`) |
| T18 | `delegate.ps1 -IgnoreSubscriptionQuota` sends `ignoreSubscriptionQuota: true` — there is no `scripts/tests/` harness today (checked); run the script against a throwaway `HttpListener` on a loopback port and assert the posted body, matching how `ANTIPHON_API` is already read from the environment (`delegate.ps1:100-105`) | |

Positive controls, required before claiming green: T10 and T13 must each be run once with the gate
call commented out and observed RED (they are the load-bearing "the hook is actually wired" tests —
the same discipline CARD-0143's closing note records).

Run: `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-quota/ --treenode-filter "/*/Antiphon.Tests.Application/SubscriptionQuotaGateTests/*"` then the two harness classes by name; delete the `bin-quota` directories afterwards.

---

## 5. Risks and what is deliberately not done

1. **A monitoring deployment that never enables `IncludeDegradedProviders` never gates Grok.** By
   design: Grok's polarity/tab navigation is unmeasured (CARD-0143 S5) and a gate on an unverified
   number is worse than none. The card already suppresses Grok's context badge for the same reason.
2. **`Enabled = true` by default.** Defended in D2: no samples ⇒ inert. The one deployment that has
   samples is the one that turned monitoring on deliberately.
3. **No automatic re-route.** The 409 names the alternative; it does not pick it. Card decision.
4. **No burn-rate projection.** One snapshot, two rules. The history is there if a later card wants it.
5. **No dispatcher gate.** D1. If a queued task sits for hours and the subscription drains in the
   meantime, the reactive system catches the wall; the informational event shows what it was launched into.
6. **`POST /api/sessions` and direct card spawn are ungated** (D1, §6).
7. **Two subscriptions on one kind without profiles collapse to one key** — inherited from CARD-0143
   (its plan §5 risk 5), not widened here.

---

## 6. Follow-ups (not this card)

- **Web client confirm dialog**: on a `subscription_quota_low` 409 from `useStartAgent` /
  `useCreateAgentTask`, show the `quota` object and a "Launch anyway" button that re-sends with
  `ignoreSubscriptionQuota: true`. The API shape in D5 is designed so this needs no server change.
- Gate `POST /api/cards/{id}/spawn` and `POST /api/sessions` the same way (one `EnforceAsync` each).
- Attention row (`AttentionKind`) for "a subscription is below threshold" independent of any launch,
  so the operator sees it before trying to launch.
- Grok gate once CARD-0143 S5 lands its measurement.
