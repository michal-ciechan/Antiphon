# AI Agent TUI Configuration — External API

## 1. Overview

The Antiphon browser and automation clients use a REST surface to manage TUI profiles, write or clear protected environment secrets, discover models, validate launch behaviour, and select a profile/model for an agent. Secret values are accepted only by dedicated write operations and never appear in any read response. Existing agent callers remain compatible while profile selection is introduced.

## 2. API Surface Summary

| Surface | Protocol | Audience | Purpose |
|---|---|---|---|
| Runner type catalogue | REST | Browser, operators | Describe supported runner types, curated suggestions, setup guidance, and capability vocabulary. |
| TUI profiles | REST | Browser, operators | Manage profile identity, active revision, launch settings, authentication mode, capabilities, and status. |
| Profile secrets | REST | Browser, operators | Write, replace, or clear protected environment values without reading them back. |
| Models and validation | REST | Browser, operators | Read cached catalogues, refresh discovery, test a profile, and inspect sanitized stage results. |
| Agent selection | REST | Browser, automation clients | Select a profile and optional exact model for future sessions. |

## 3. Authentication & Authorization

These endpoints initially use Antiphon's existing trusted administration boundary. When finer-grained authorization is introduced, profile reads and model reads may be granted separately from profile mutation and secret mutation. Secret plaintext is never authorized for read because no read operation exists.

The server applies normal request-size limits, rejects secret values in query strings, and sanitizes authorization, validation, discovery, and launch errors before returning Problem Details. The browser must not persist submitted secret values after a write completes.

## 4. ID Scheme & Conventions

- Profile, revision, validation-run, and agent IDs are UUIDs serialized in canonical hyphenated form.
- Runner type is a stable case-insensitive symbolic name such as `ClaudeCode`, `Codex`, or `OpenCode`.
- Environment-variable names are case-preserving and compared according to the target operating system at validation and launch time.
- Model identifiers are opaque runner-owned strings. They are URL-encoded when placed in a route and returned unchanged in JSON.
- Revision numbers increase monotonically within one profile and support optimistic concurrency.
- JSON uses camelCase. Errors use the existing Problem Details envelope with a stable `code` extension.

## 5. Endpoints / Operations

### 5.1 Runner types

| Method | Path | Purpose | Auth | Requirements |
|---|---|---|---|---|
| GET | `/api/agent-tui/runner-types` | List runner types, default argument semantics, curated models, guidance, and capability definitions. | Profile read | FR-3, FR-9, FR-15, FR-18 |

The response contains runner type, display name, description, curated model entries, supported authentication modes, default model-argument name, capability descriptions, and setup/troubleshooting guidance. It contains no machine-specific credentials.

### 5.2 Profiles

| Method | Path | Purpose | Auth | Requirements |
|---|---|---|---|---|
| GET | `/api/agent-tui/profiles` | List profile summaries, active revision, enabled/default state, selected auth mode, model/validation freshness, and secret set/missing metadata. | Profile read | FR-1, FR-2, FR-13, FR-15, FR-17 |
| POST | `/api/agent-tui/profiles` | Create a profile and its first immutable revision. | Profile write | FR-1, FR-2 |
| GET | `/api/agent-tui/profiles/{profileId}` | Read one profile, active revision, non-secret launch settings, capabilities, models, guidance, and secret metadata. | Profile read | FR-1, FR-2, FR-13, FR-15, FR-18 |
| PATCH | `/api/agent-tui/profiles/{profileId}` | Create and activate a new revision using optimistic concurrency. | Profile write | FR-1, FR-2, FR-6 |
| POST | `/api/agent-tui/profiles/{profileId}/duplicate` | Create a disabled draft profile without copying managed secret values. | Profile write | FR-1, FR-12 |
| DELETE | `/api/agent-tui/profiles/{profileId}` | Delete an unreferenced profile or return a conflict explaining its assignments. | Profile write | FR-1, FR-4, FR-17 |

Create and patch requests contain display name, runner type, enabled/default intent, executable, ordered arguments, working-directory policy, model-argument name or omission, authentication mode, required and ordinary environment metadata, curated/operator model entries, and guidance overrides. A patch supplies `expectedRevision`; a stale value returns `profile_revision_conflict` and does not create a revision.

Read responses contain a command preview with secret values excluded, revision/provenance data, capability snapshot, validation summary, and `secretEnvironment` entries shaped as `{ name, configured, updatedAt }`. Ciphertext and plaintext are never serialized.

Deletion returns `profile_in_use` when agents, the installation default, or sessions still require the profile. Historical immutable revisions referenced by sessions are retained under the server's lifecycle policy even after a later supported archive operation.

### 5.3 Protected environment values

| Method | Path | Purpose | Auth | Requirements |
|---|---|---|---|---|
| PUT | `/api/agent-tui/profiles/{profileId}/secrets/{environmentName}` | Create or replace one protected environment value. | Secret write | FR-12, FR-13 |
| DELETE | `/api/agent-tui/profiles/{profileId}/secrets/{environmentName}` | Clear one protected environment value. | Secret write | FR-12, FR-13 |

The PUT request contains exactly one non-empty `value` and `expectedRevision`. Success returns only `{ name, configured: true, updatedAt, revision }`. Omission never clears a value; clearing requires DELETE. Secret writes are idempotent with an optional request id. Repeating the same request id returns the original sanitized outcome without re-emitting audit changes.

Errors include `secret_protection_unavailable`, `secret_write_failed`, `invalid_environment_name`, and `profile_revision_conflict`. None includes submitted input, ciphertext, provider output, or a full child environment.

### 5.4 Models, capabilities, and validation

| Method | Path | Purpose | Auth | Requirements |
|---|---|---|---|---|
| GET | `/api/agent-tui/profiles/{profileId}/models` | Read merged discovered, curated, and operator-added models with source and availability. | Profile read | FR-5, FR-8–FR-10 |
| POST | `/api/agent-tui/profiles/{profileId}/models/refresh` | Start or join a bounded model discovery run. | Profile test | FR-7, FR-10 |
| GET | `/api/agent-tui/profiles/{profileId}/capabilities` | Read capability states, reasons, runner version, and probe time. | Profile read | FR-15 |
| POST | `/api/agent-tui/profiles/{profileId}/validate` | Start or join a bounded staged profile test. | Profile test | FR-14, FR-15 |
| GET | `/api/agent-tui/validation-runs/{runId}` | Read sanitized stage results and final suitability. | Profile read | FR-14, FR-15 |

Refresh returns a validation-run-style resource with status `queued`, `running`, `succeeded`, `partial`, `failed`, or `timedOut`. The server permits only one active discovery and one active validation per profile; concurrent calls join the active run. A completed discovery returns the merged catalogue and whether cached results were retained.

Validation stages identify executable, arguments, working directory, environment readiness, version/capabilities, discovery, startup, and clean stop. Results contain bounded sanitized messages. They classify suitability for interactive, queued, delegated, and resumable use rather than returning one misleading Boolean.

### 5.5 Agent selection

| Method | Path | Purpose | Auth | Requirements |
|---|---|---|---|---|
| POST | `/api/agents` | Create an agent with optional `tuiProfileId` and `modelId`. | Existing agent write | FR-4–FR-6, FR-16–FR-17 |
| PATCH | `/api/agents/{agentId}` | Change the profile and optional model for future sessions. | Existing agent write | FR-4–FR-6 |
| GET | `/api/agents/{agentId}` | Read selected profile/model and the profile revision/model used by any live session. | Existing agent read | FR-4–FR-6, FR-15 |

When `tuiProfileId` is omitted by a compatibility caller, the installation default is used. When `modelId` is null or omitted, the model argument is omitted. A supplied model may be verified, suggested, stale, or operator-added but must belong to the selected profile catalogue. The response distinguishes `configuredSelection` from `liveSessionSelection`; this exposes changes that apply on restart.

Errors include `profile_disabled`, `profile_not_validated`, `profile_not_found`, `model_not_in_profile`, and `capability_required`. Existing generic model-level fields remain accepted during the migration window and are mapped only when the selected imported profile defines an unambiguous equivalent.

## 6. Streaming / Push Semantics

Existing SignalR invalidation notifies clients when a profile, catalogue, validation run, or agent selection changes. REST remains authoritative. Updates are at-least-once invalidations rather than ordered profile event replay; clients refetch by ID and revision.

## 7. Client Libraries

No new external package is vended. The Antiphon browser's typed API client encapsulates request and response shapes. Automation clients may call the REST contract directly.

## 8. Versioning & Compatibility

Fields are additive during the migration window. Existing agents and callers without profile fields resolve through the imported installation default. Existing runner-definition reads remain available until all launch paths and clients use profiles; their eventual removal requires a separate deprecation decision.

Profile revisions are immutable. New runner capabilities add symbolic entries without changing prior meanings. Unknown capabilities and runner types are preserved in responses so newer servers do not force older clients to mislabel them.

## 9. Rate Limits & Quotas

CRUD uses normal Antiphon limits. Discovery and validation are limited to one active run of each type per profile, have a 30-second server deadline, and retain a bounded recent history. Secret writes use the normal administrative mutation limit and request-size ceiling.

## 10. Impact On Requirements

| Requirement | Impact |
|---|---|
| FR-1–FR-3 | Profile and runner-type operations expose the administration contract. |
| FR-4–FR-6 | Agent operations expose configured and live profile/model selection. |
| FR-7–FR-10 | Model operations expose discovery, provenance, availability, and cache fallback. |
| FR-11–FR-13 | Dedicated secret writes preserve both auth modes and write-only reads. |
| FR-14–FR-15 | Validation and capability operations expose staged suitability. |
| FR-16–FR-17 | Compatibility omission resolves through the imported installation default. |
| FR-18–FR-19 | Runner-type and profile reads expose guidance and optional-model launch configuration. |

## 11. Open Questions

None for the specification baseline.
