# CARD-0335 — bounded AutoDetected model-cap holds

**Plan pass, 2026-09-03. Sources verified at `95ad1dd5`: `UsageLimitWallParser`, `ApiErrorRecoveryService`, `ModelAvailability`, `ModelDisabledException`, `AgentSupervisorHostedService`, `AttentionService`, the model-availability tests, and CARD-0331. No production code is changed by this plan.**

## 1. Verified behaviour and decision

There is one production caller of `ModelAvailability.UpsertAutoDetectedAsync`:
`ApiErrorRecoveryService.ApplyWallAsync`. It passes a real expiry only when
`UsageLimitWallParser.Parse` found `resets ...` in the API-error stub, adding the existing
two-minute padding. The named Fable/Opus/Sonnet/Haiku cap fixture and the Grok capacity fixture
contain no reset. The parser correctly classifies those as `ModelCap`, and the recovery writer
currently passes `null`.

The parser is therefore not dropping a reset time that the current upstream signal contains. The
right fix is a conservative, configurable fallback rather than a speculative new parser. Use a
**six-hour** default: it bounds the observed overnight stale hold while still avoiding a fast
retry loop when a provider's opaque cap is genuinely longer. If the provider continues refusing
work after expiry, the next wall detection renews the hold with fresh evidence. Operators can set
`Supervision:ApiErrorRecovery:ModelCapFallbackHoldHours` to a longer value for a provider whose
cap is known to reset more slowly.

Do not add an attention/digest feature. `AttentionService.BuildModelAvailabilityHoldItemsAsync`
already produces an Error row for every active hold, including an AutoDetected/null hold, and
offers `ClearHold`; the Orchestrator attention tab already renders that action. The existing
surface needs only wording that does not represent Antiphon's fallback timestamp as a provider
reset.

The minute supervisor already calls `ModelAvailability.SweepExpiredAsync`, and `IsHeldAsync`
lazily clears an elapsed timed row. Persisting an actual timestamp lets both paths resume queued
work without a new job, migration, or automatic reroute.

## 2. Design

### 2.1 Make every new AutoDetected hold timed

1. Add `ModelCapFallbackHoldHours` to `ApiErrorRecoverySettings`, default `6`, documented as the
   fallback for an AutoDetected wall whose source text does not state a reset. Clamp malformed or
   non-positive configuration to one hour, as the surrounding recovery settings already do.
   Add the explicit default under `Supervision:ApiErrorRecovery` in `server/appsettings.json` so
   the operational knob is discoverable as well as environment-bindable.
2. In `ApiErrorRecoveryService.ApplyWallAsync`, retain the known-reset calculation exactly:
   `wall.ResetAt + ModelAvailability.SessionLimitResumePadding`. For a `ModelCap`, calculate
   `now + TimeSpan.FromHours(ModelCapFallbackHoldHours)` and pass that to the availability writer.
   It remains `WallModelPaused` with no same-session `WallPrompt` or scheduled resume; only future
   dispatches become eligible after the fallback expires.
3. Change `ModelAvailability.UpsertAutoDetectedAsync` to require a non-null `DateTime
   disabledUntil`. Its one production caller now always supplies a timestamp, so this converts the
   desired invariant into a compile-time guard against a future AutoDetected/null writer. Manual
   `UpsertManualAsync` continues to accept `null`, and Manual continues to outrank AutoDetected.

### 2.2 Reconcile the historical null rows safely

The live `GET /api/model-availability` snapshot taken during this plan has no holds, but rolling
out the new writer alone would leave any historical AutoDetected/null row permanently held. Do
not issue a data migration that guesses a wall time at migration-generation time.

Instead, give `ModelAvailability` the same configured fallback duration (via
`IOptions<SupervisionSettings>`, preserving an optional/default constructor argument for the
many small direct service harnesses). On its next sweep or lazy active-hold read:

1. identify an active `Source = AutoDetected`, `DisabledUntil = null` legacy row;
2. materialize `DisabledUntil = HitAt + configured fallback`;
3. clear it in the same save if that resulting timestamp has elapsed.

`ListHeldAsync`, `ListAvailableAsync`, `RequireAsync`, and the supervisor sweep must all share
this normalization through `SweepExpiredAsync` / `FindActiveAsync`, so a pre-existing row cannot
remain blocking merely because the supervisor has not reached its next minute pass. A null Manual
hold is excluded completely. No schema change or EF migration is required: the existing nullable
timestamp is being filled in place, and the update is idempotent.

`ApiErrorRecoveryService` should resolve the scoped `ModelAvailability` from the scope it already
creates for `EnsureAdoptedAsync`, rather than constructing it with `new`. That ensures recovery
and reader/sweeper use the same configured fallback. Thread that scoped service through
`BuildNewRowAsync` and `ApplyWallAsync`.

### 2.3 Tell the truth at the two existing surfaces

The current code treats every timed AutoDetected hold as a `session-limit`: both the 409 and the
attention headline would falsely call the fallback a provider reset. Preserve the existing wording
when `Reason` contains a parsed `resets ` value. For an AutoDetected timed hold whose canonical
reason contains `no reset stated`:

- `ModelDisabledException.SourceClause` says `per-model cap, fallback retry` (and still returns
  the real `disabledUntil` timestamp in its problem-details extension), not `session-limit`.
- `AttentionService.BuildModelAvailabilityHoldItemsAsync` says the provider gave no reset and
  shows the fallback retry time and remaining duration; it must not use the word `resets` for that
  timestamp. Its evidence continues to include the stored `disabled until` value and raw source
  text.

Retain the existing null-AutoDetected wording for an old row until it is normalized, and all
Manual formatting unchanged.

## 3. Implementation slices

| Slice | Files | Work and tests |
|---|---|---|
| **S1 — persist and retire fallback holds** | `server/Application/Settings/SupervisionSettings.cs`; `server/appsettings.json`; `server/Application/Services/ApiErrorRecoveryService.cs`; `server/Application/Services/ModelAvailability.cs` | Add the six-hour option and derive a real expiry for every no-reset wall. Make `UpsertAutoDetectedAsync` non-nullable. Resolve the scoped availability service rather than manually constructing it. Normalize legacy AutoDetected/null rows from `HitAt + fallback` in both the normal sweep and lazy active lookup; never normalize Manual/null. Update XML comments that still describe per-model AutoDetected holds as open-ended. Extend `ApiErrorRecoveryServiceTests`: with a fixed clock and a non-default test fallback, Fable and Grok no-reset fixtures persist exactly `now + fallback`, still create no `WallPrompt`/same-session retry, and retain their model kind/alias. Extend `ModelAvailabilityTests`: a fresh fallback timestamp blocks until expiry and then `IsHeldAsync` clears it; an old AutoDetected/null row is materialized and cleared; an open-ended Manual row remains held. |
| **S2 — correct refusal and attention language** | `server/Application/Exceptions/ModelDisabledException.cs`; `server/Application/Services/AttentionService.cs`; `tests/Antiphon.Tests/Application/ModelAvailabilityCreateTests.cs`; `tests/Antiphon.Tests/Application/AttentionServiceTests.cs` | Branch timed AutoDetected presentation on the canonical `no reset stated` reason. A known parsed reset keeps today’s exact reset wording. A fallback row reports a bounded retry rather than a provider reset in both the 409 and Error attention row, retains `ClearHold`, and disappears after the ordinary clear. Keep the existing legacy-null and Manual assertions. |
| **S3 — operational contract** | `docs/agent-kinds.md`; this plan | Replace the CARD-0022 guidance claiming per-model caps write `DisabledUntil = null` forever. State the six-hour default, the setting path, fresh-evidence renewal behaviour, and that Manual null remains deliberately open-ended. Do not change the model-availability API contract: PUT with omitted `disabledUntil` is still a Manual open-ended hold. |

S1 is the correctness boundary and should be committed before S2; S2 makes the resulting deadline
safe to interpret. S3 lands with S2. No client component, endpoint, DTO, database schema, or new
hosted service is needed.

## 4. Verification

Run the focused backend classes sequentially (TUnit, not `dotnet test`):

```powershell
dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/*/*/ApiErrorRecoveryServiceTests/*"
dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/*/*/ModelAvailabilityTests/*"
dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/*/*/ModelAvailabilityCreateTests/*"
dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/*/*/AttentionServiceTests/*"
```

If live daemons lock the ordinary output, use one unquoted forward-slash alternate output directory
per the testing guide, for example `--property:OutputPath=bin-card0335/`, and remove only those
explicit `bin-card0335` directories afterwards. The tests compile the affected server paths; no
client test run is required because client types and rendering stay unchanged.

After deployment, `GET /api/model-availability` (or `scripts/model-availability.ps1 get`) should
show new no-reset holds with a non-null `disabledUntil`. Any legacy row that remains visible on the
first request is normalized by that request and auto-clears at its `HitAt + fallback` deadline.

## 5. Non-goals

- Parsing an invented provider reset from Fable/Grok cap prose, changing the known-reset parser,
  or using `/usage-credits` as a readout.
- A second attention row, a digest, alert routing, or automatic model rerouting. The existing
  Error hold row and dispatcher’s ordinary availability check are sufficient once the hold is
  bounded.
- Changing Manual hold semantics, including a Manual null `DisabledUntil` and Manual precedence.
- Resuming the original limit-killed session on an opaque cap. The fallback re-opens model
  availability for future queued/new dispatch; an actual provider rejection records fresh evidence
  and renews the bound.
