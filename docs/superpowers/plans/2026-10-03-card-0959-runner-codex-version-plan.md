# CARD-0959: verified Codex CLI version per runner

Date: 2026-10-03. Plan task: `429e016c`. Source baseline:
`f0fadc61635e3d977f045876d65e1e09ef06b17a`.

## Outcome and authority

Advertise the installed Codex CLI version from the process that will launch it,
and refuse delegated `gpt-6.1-sol` work unless that runner supplies fresh evidence
of codex-cli >= `0.159.1`. Include local Windows runners, remote Linux runners,
exact-model profiles, queue-time drift, old binaries and failed probes. Preserve
existing model-hold, sign-in, placement and drain behavior.

The full CARD-0959 read is the specification. Its interim deployment note is
historical: the dispatch brief reports both serving installations upgraded to
0.160.0 and the temporary runner retired. That report is not a capability receipt
and must never become a build-SHA-to-CLI-version lookup.

This dispatch changes only this plan. Next is **TestDesign**, as commissioned:
the V/PC methods below specify the required design, but their source fixtures,
assertion labels and expanded counts still need the TestDesign freeze before
Code. No product decision is left awaiting a human answer. D-1..D-10 are explicit
design choices, including the newly introduced override and necessary internal
capability-probe operation; neither exists at the baseline.

Owners consulted: `docs/project-context.md`, `docs/ops-http.md`,
`docs/orchestration-loop.md`, `docs/agent-card-lifecycle.md`,
`docs/testing-and-build.md`, `docs/agent-kinds.md`,
`docs/ai-agent-tui-configuration.md`, `docs/session-runtime-invariants.md`,
and `docs/resilience.md`. Shape references are the CARD-1008 and CARD-1004 plans.

## Ground truth

Line references below describe the source baseline, not future implementation.

| Card assumption / requested behavior | What the code does | Consequence |
|---|---|---|
| A runner can advertise its installed Codex CLI. | `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:963` defines `RunnerCapabilitiesDto`; `Version` at line 978 is explicitly the runner build SHA. There are no CLI-version fields. | Append nullable fields; preserve `Version`/`Build` semantics. Never derive the CLI from either. |
| Local and phone-home capabilities have the same producer. | Local `GET /capabilities` is `src/Antiphon.SessionRunner/Program.cs:228`, calling `SessionRunnerRuntime.DescribeCapabilities` at `SessionRunnerRuntime.cs:741`. Phone-home independently builds a DTO in `PhoneHomeRuntimeAdapter.cs:31`. | Both must read the same DI-owned probe snapshot. Testing only one producer leaves a real gap. |
| Phone-home conveys capabilities to the server. | `PhoneHomeConnectionService.cs:108` registers a nested capabilities DTO. `PhoneHomeContracts.cs:315` owns that request. `PhoneHomeRunnerDirectory.cs:330` retains registration evidence; line 337 retains previous capabilities when registration omits them. | Preserve additive registration compatibility, but clear the new version evidence on every accepted generation with no new sample. An old runner must not inherit a previous runner's good version. |
| Heartbeats refresh the version evidence. | `PhoneHomeContracts.cs:105` has capacity only; `PhoneHomeConnectionService.cs:354` sends it. `server/Infrastructure/Agents/SessionRunner/PhoneHomeLiveConnection.cs:424` checks epoch, updates liveness and reads capacity. Its `Capabilities` at line 88 is immutable. | Add an optional CLI sample to the heartbeat and a separate generation-bound mutable snapshot. A heartbeat is not a fresh probe. |
| Runner rows already expose build version. | `PhoneHomeRunnerDirectory.cs:661` builds status; line 672 projects `BuildVersion` from capabilities. `PhoneHomeRunnerStatusDto` is `PhoneHomeContracts.cs:367`. However, the list DTO at `server/Application/Dtos/SessionRunnerCatalogueDtos.cs:4` has neither build nor CLI version. `SessionRunnerCatalogue.cs:119` builds the list row. | Add CLI visibility to both the existing status and list APIs. Do not describe buildVersion as an existing list-column field. |
| RunnerStateService owns version state. | `RunnerStateService.cs:48-99` persists and mirrors drain/retirement state. It does not own capability observations. CARD-0953's replacement-store checks are in `PhoneHomeRunnerDirectory.cs:298-313`. | No DB column, migration or RunnerStateService change. Keep the CARD-0953 store/boot/lease checks intact; version evidence follows their accepted identity. |
| A desktop probe can run bare `codex --version`. | `SessionRunnerRuntime.cs:368-370` applies `CodexWindowsLaunchPolicy` before process creation. That policy at lines 63-85 handles native, direct node and stock npm shims; lines 128-154 resolve codex.js and sibling/PATH node. | Probe through that launch resolution, including cwd and effective resolution environment. A separate PATH-only lookup can verify a different installation. |
| A remote Codex launch uses the desktop command. | `server/Application/Services/PhoneHomeLaunchPolicy.cs:289-294` projects standard Codex names to the installed Linux `codex`; lines 234-257 separately enforce remote auth environment. | Build the probe descriptor after remote executable projection. Do not probe a desktop path for remote work or change credential projection. |
| AgentTuiProfileService already supplies sufficient evidence. | `AgentTuiProfileService.cs:903-944` first builds authentication, then executes profile VersionArguments. Lines 1736-1790 require a clean bounded result but return display text such as `Codex 0.160.0`. The stored value is a profile validation result, not a runner/boot-bound observation. | Reuse its strict-output principles, not its authenticated validation flow or cached display value. Do not call validation from admission. |
| The model ladder knows the CLI floor. | `ModelLevelAliases.cs:55-61` maps High/Medium/fallback to `gpt-6.1-sol`; lines 20-22 and 52-53 state the floor only in comments. `ModelAlias.cs:22,96` recognizes the canonical slug. | Put required CLI metadata on the ladder entry, with one literal floor and lookup by the actual canonical model. |
| Tier alone identifies what a pinned task launches. | `AgentTaskDispatcher.cs:5772-5809` gives exact ModelId precedence and persists EffectiveModelId. `ResolveDispatchAliasAsync` at line 5930 honors exact model/specialist snapshots. A blank profile ModelArgumentName means the profile owns the model (`ShippedModelDisplay`, line 5947). | Resolve actual model and active profile revision, not just High/Medium. Preserve opaque/wrapper-owned-model semantics; do not guess the 6.1 slug. |
| There is one admission check. | `AgentTaskService.cs:1052-1105` enforces model holds; lines 1250-1292 finish runner/platform selection; lines 1394-1401 run sign-in checks before persistence. Retry checks sign-in at lines 2638-2644. Dispatcher checks held models at lines 673-735, platform at 789, and resolves again before launch/reuse. | Check create/retry after existing refusals and selected-runner resolution, then recheck dispatch before claim/preparation/input and after any target/model re-resolution. |
| Unknown provider authentication must also become a version refusal. | `AgentTaskService.cs:4021-4070` gives remote Codex auth a five-second bound and admits unknown auth. `ProviderSignInRequiredException.cs:11` owns `provider_sign_in_required`; `ModelDisabledException.cs:14` owns `model_disabled`. | Auth unknown remains auth unknown. Version unknown is a separate fail-closed decision. Neither existing opt-in bypasses the new gate. |
| Existing capability transports are suitable. | `ISessionRunnerClient.cs:21` defaults capability reads to null; `SessionRunnerHttpClient.cs:304` reads local capabilities. `PhoneHomeRunnerClient.cs:25` and `PhoneHomeCommandDispatcher.cs:204` implement the phone-home operation. `PhoneHomeRunnerDirectory.cs:716-755` supplies local/remote descriptors. | Reuse those transports and directory selection. Add one narrowly typed probe operation for exact launch descriptors, not an arbitrary command runner. |

Platform observation on 2026-10-03 at approximately 12:23 UTC: GET
`/api/runner-defaults` returned revision 2 with an automatic remote preference;
GET `/api/session-runners` returned a live Linux runner, a live Windows desktop,
and an unavailable drained temporary entry. This is not a placement constant.
Read both routes again when dispatching each stage. Omit `-Runner` unless pinning
one host. Omit `-Platform` for portable rows; only the native Windows row needs
`-Platform Windows`. `-Platform Any` explicitly removes an inherited OS pin.

## Decisions

### D-1: the ladder entry owns the floor

Extend `ModelLevelAliases` with an immutable Codex entry containing `Alias` and
nullable `RequiredCliVersion`. High, Medium and the existing unknown-level
fallback refer to the same `gpt-6.1-sol` entry, carrying `0.159.1` once. Existing
`ForCodex`, `For` and `ForLaunch` keep their string-returning contracts. A lookup
by canonical alias finds the same entry for exact pinned models. This floor is
the CARD-0903 installed-catalog requirement, not inferred from build metadata or
queried from the provider. Leave other models' enforcement unchanged in this
card, including the separately documented Astra floor.

Use a strict SemVer value/parser shared by runner evidence and server admission
in the contracts assembly; do not add a package solely for three-part versions.
Parse one `codex-cli X.Y.Z` record (also the existing recognized `codex`/`version`
banner forms), normalize the version, and compare numerically with SemVer
prerelease ordering. Reject overflow, missing components, extra records, embedded
text and invalid identifiers. Build metadata does not affect ordering.
`0.159.1-beta.1 < 0.159.1`; `0.160.0-beta.1 > 0.159.1`. This gate proves a version
floor, not provider entitlement or blanket prerelease qualification.

Rejected: comparing strings, a second constant in the dispatcher, gating every
High task regardless of exact model, using the image tag, or silently lowering
the requested model to make old CLIs work.

### D-2: a bounded, unauthenticated runner-owned probe

Add a DI singleton probe/snapshot service to SessionRunner and a hosted refresh
service. Resolve the configured default Codex launcher at startup, before the
first phone-home registration is released, then refresh every five minutes.
The initial attempt delays advertisement at most its bounded budget; failure
still permits the runner to serve other kinds with an explicit unknown sample.
GET `/capabilities` reads memory and never starts a process.

On Windows, use `CodexWindowsLaunchPolicy.Apply` with version-only arguments,
the actual resolution cwd and environment. Preserve sibling-node precedence,
recognized npm-shim checks and missing-package failures. Run the resulting
native executable or `node.exe <installed codex.js> --version` directly with
`UseShellExecute=false` and ArgumentList. Linux resolves and runs the installed
native executable. Do not execute a command string through cmd/sh, a profile's
VersionArguments, arbitrary wrappers, or model/discovery/login commands.

Resolve paths before sanitizing the child environment. The child gets an empty
temporary CODEX_HOME, closed stdin, a neutral scratch cwd, and no credential,
ANTIPHON token, proxy, startup-hook or Node injection variables. Keep only the
OS/runtime environment required by the resolved executable. The probe never
opens an auth file or calls the model/provider. Unrecognized wrappers and
launch-affecting environment that cannot be represented safely yield unknown;
do not execute them to discover what they do. Operator-installed native binaries
and recognized packages remain trusted software; basename alone is not proof of
an arbitrary wrapper's identity.

Bound execution to five seconds, captured stdout and stderr to 4 KiB each, and
post-cancel process-tree termination/pipe cleanup to two further seconds. Await
owned children. An unconfirmed cleanup is unknown and retains owned cleanup
responsibility, never a successful sample. Exit nonzero, stderr, truncation,
multiple lines, missing executable, timeout, cancellation or parser failure all
replace the version with null plus a fixed reason token. Never retain an old
success with a new timestamp after failure. Never publish raw output, env, args,
paths or exception text. Use TimeProvider for refresh tests and budgets.

Single-flight identical requests; serialize probe children on each runner and
bound the per-launcher cache to 32 entries. A full/busy cache returns unknown
within the request budget rather than spawning unbounded children. Cache keys
include resolved executable/package identity, resolution inputs and runner boot;
path/size/mtime changes invalidate the entry. Do not claim this detects a binary
replacement that preserves all file metadata.

Rejected: server-side probing, AgentTui validation reuse (it reads auth), probing
on every catalogue GET, fire-and-forget children, and startup-only sampling.

### D-3: additive wire evidence with generation-safe refresh

Append optional nullable fields to `RunnerCapabilitiesDto`:

| Field | Meaning |
|---|---|
| `codexCliVersion` | Normalized verified SemVer, or null. |
| `codexCliVersionCheckedAtUtc` | Actual completed attempt time, never heartbeat/read time. Null means no probe evidence. |
| `codexCliVersionError` | Fixed reason token for an unknown result; null for success. |
| `codexCliLauncherFingerprint` | Opaque SHA-256 of the normalized launcher identity; no plaintext paths or environment. |

Use a small `RunnerCodexCliVersionDto` for that same four-field snapshot in the
new probe response and as an optional `CodexCli` member appended to
`PhoneHomeCapacityHeartbeat`. The registration already nests capabilities: no
duplicate top-level registration fields and no protocol-number bump. Append a
feature token `codex-cli-version-v1` and a new, non-renumbered phone-home operation
for descriptor-specific probes (D-4). Older readers ignore additions. Older
runners omit them or refuse the new operation; both outcomes are unknown.

Local capabilities and PhoneHomeRuntimeAdapter copy the same default snapshot.
Heartbeat sends that snapshot without probing on the heartbeat thread. Server
stores it separately from immutable `PhoneHomeLiveConnection.Capabilities`,
under the existing connection/slot ownership synchronization. Status, descriptor
and catalogue project the latest sample; disconnect can retain it for display,
but cannot grant dispatch eligibility.

On accepted registration, clear old Codex evidence even when the old general
capabilities object is retained. Bind the new sample to runner id, store id,
boot id and accepted epoch. A missing field on a new registration means unknown;
an old-epoch heartbeat/reply cannot restore evidence. Within an epoch, discard
out-of-order older completed attempts. Missing CLI data on a legacy heartbeat
does not refresh a sample; an explicit failed attempt clears a prior success.
Do not alter CARD-0953 admission or durable drain/store state.

### D-4: verify the selected launcher, including exact profiles

A default version cannot certify a different profile executable. Add the one
necessary **internal runner** route `POST /capabilities/codex-cli-version`, plus
the matching phone-home `CodexCliVersion` operation and
`ISessionRunnerClient.GetCodexCliVersionAsync`. There is **no new server public
API endpoint**. The existing server catalogue/status endpoints remain the UI and
operator read surfaces. The typed operation is necessary because GET
`/capabilities` has no launch descriptor; a profile validation timestamp is not
a substitute. A POST allows a bounded descriptor without leaking paths into
query strings. It is read-only with respect to sessions and credentials.

Request fields are only executable selector, resolution cwd, the PATH/PATHEXT
resolution subset when overridden, and the recognized node/codex.js prefix
when applicable. No auth, prompt, model args, arbitrary args, environment dump or
version command comes over this operation. Resolve using the same inputs the
adapter will receive; remote projection occurs first. Shell wrappers, secret
placeholders in resolution inputs and unsupported loader overrides return
`launcher_unverified` without starting a child. Validate bounds before resolution.
A profile naming another recognized native installation is probed as that
installation; its result never overwrites the default version shown on the row.

Admission uses the default advertised sample only when its launcher fingerprint
is bound to the resolved launch identity; otherwise use the typed operation.
For create-time worktrees that do not exist yet, resolve relative selectors
conservatively: if the future cwd could change resolution, report unknown rather
than certifying the parent directory. Absolute installed selectors and projected
native remote launchers remain supported. Re-resolve before actual dispatch.

The runner resolves descriptors before looking up its cache; a response carries
the launcher fingerprint and sample time. The client accepts it only for the
requested runner/current connection and request identity. No fallback to another
runner, desktop sample or profile-validation cache. Bind response evidence to
the selected profile revision and descriptor in the admission decision; do not
reuse it after a revision/target/env change.

Use a single eight-second owner deadline (including bounded child cleanup), no
retry/hedging and no DB claim held during the request. Keep this POST outside
the admitted GET resilience registry. Caller cancellation propagates; an owned
deadline or transport failure produces unknown. Unsupported old-client default
interface behavior is null, never synthesized success.

Rejected: a general remote process-probe API, executing wrapper-supplied version
args, accepting the standard install's version for all pinned profiles, and
new public catalogue endpoints.

### D-5: fresh means a successful attempt no more than fifteen minutes old

`DelegationSettings.CodexCliVersionMaxAgeMinutes` defaults to 15, is positive and
bounded (1..60), and is checked with the injected clock. Runner refresh defaults
to five minutes and must be configured below the server bound. A success is
usable only when both version and checked-at parse, no error is present, the
launcher and generation match, and age is <= the bound. Equality is accepted;
one tick over refuses. More than one minute in the future is unknown
(`clock_skew`); a tolerated positive skew clamps age to zero. Heartbeats do not
reset age. Runner disconnected, retired, draining or otherwise ineligible still
uses the existing placement refusal/hold and cannot be cured by CLI evidence.

An unknown/stale default sample may trigger one bounded refresh through D-4 for
the selected descriptor. Admit only its fresh success; a stale response itself
never authorizes a launch. A failed refresh erases the corresponding successful
cache entry. There is no indefinite stale-while-revalidate admission.

### D-6: fail closed at create/retry and again before dispatch

Add a shared pure `CodexCliAdmissionPolicy` and bounded request/descriptor helper;
use existing directory, registry/profile, settings and TimeProvider dependencies
in AgentTaskService and AgentTaskDispatcher. No new server DI registration,
attention projection, hold entity, capability-task-input or migration is needed.

1. Resolve the actual model: exact selected profile ModelId when applicable,
   frozen specialist/live-session model where the existing path uses it, else
   the ladder. Inspect effective profile model-argument semantics; a blank
   argument does not magically mean the High alias. Preserve existing
   `model_argument_unsupported` and profile validation refusals. Unknown
   wrapper-owned models are outside this explicit-model floor contract.
2. Resolve the final runner after defaults, platform constraints, existing-process
   binding and any existing routing/drain decision. Never resolve another host
   to satisfy this version check.
3. Retain existing model availability and provider-auth checks and their ordering.
   At create, run the CLI gate immediately after the checks at lines 1396-1400
   and before saving the task. Keep repeat-failure/routing-exhausted branches
   that intentionally save a Blocked row. At retry, place it after existing auth
   checks and before requeue. It does not reinterpret unknown auth as signed out.
4. Before dispatch claim, worktree/mirror preparation or reused-session input,
   re-resolve alias/target and evaluate fresh CLI evidence. Existing held-model
   and provider-auth outcomes keep their codes and behavior. A chain rewalk or
   drain redirect must recompute the CLI decision. Carry descriptor/profile
   identity through the existing launch boundary; if resolution changes between
   preflight and launch/reuse, repeat the bounded preflight outside the claim
   or refuse, never use evidence for the old target.
5. Refuse bad evidence with an HTTP 409 at synchronous admission. A previously
   queued task becomes Blocked through the existing BlockAsync path, with the
   same machine reason prefixed in FailureReason and a Blocked event; zero
   session starts/input/worktree-prep calls. Recovery is an explicit retry after
   repair/upgrade or an operator override. Do not add a new hold/attention path
   or automatically requeue it.

| Code | Conditions / safe problem-details facts |
|---|---|
| `codex_cli_version_too_old` | Parsed version below floor; runnerId, actual model, required version, observed version, checkedAtUtc. |
| `codex_cli_version_unknown` | Omitted/unparseable/error/unsupported/timeout/mismatched launcher or generation; runnerId, model, floor, fixed reason. |
| `codex_cli_version_stale` | Parsed successful observation older than max age after attempted refresh; same facts plus maxAgeMinutes. |

Put these codes and extension construction in
`server/Application/Exceptions/CodexCliVersionRequiredException.cs`, using the
existing HttpException mechanism. Explain upgrade/refresh/retry and the operator
override in the remedy; never suggest bypassing auth. Preserve `model_disabled`
extensions/coda and `provider_sign_in_required` remedy exactly. Existing
`ignoreModelDisabled` and `allowUnauthenticatedProvider` grant no CLI permission.
No new failure-code enum is needed solely to persist the Blocked reason.

### D-7: explicit, expiring operator override; no general gate-disable switch

Introduce a **new** operator-owned configuration opt-in:
`Delegation:CodexCliVersionOverrides`, empty by default. Each item names exactly
one runner id (canonical `desktop` for local), canonical model, nonempty reason,
expiry UTC and allowed refusal codes. Reject wildcards, duplicate tuple entries,
invalid codes and lifetimes longer than 24 hours at configuration validation.
The gate rechecks expiry on every use even without a settings reload. Operator
configuration/restart is the existing control plane; no delegate request,
capability, scripts/delegate.ps1 switch or writable HTTP route is added.

This borrows the explicit opt-in principle of allowUnauthenticatedProvider, but
is deliberately a **different permission**: that existing flag only bypasses the
create-time auth question and is not persisted. Do not overload it. A matching
live CLI exception permits only the named CLI refusal and records a Warning
event with runner, model, code, expiry and operator reason at create/dispatch.
It does not relabel evidence as verified or bypass auth, holds, platform, drain,
capacity, launch-policy rejection or profile restrictions. Refuse absent/expired/
wrong-host/wrong-model/wrong-code entries. Configuration is the operator's action,
not something an agent is authorized to change by encountering a refusal.

Rejected: silent rerouting; permanent global disable; implicit permission from
an existing auth flag; and a task-persisted boolean requiring a new DB migration.

### D-8: expose evidence on existing APIs; no dashboard redesign

Append nullable `codexCliVersion`, `codexCliVersionCheckedAtUtc`,
`codexCliVersionError` plus nullable `codexCliVersionStale` to the catalogue row
and remote status DTO. Stale is null when there is no successful observation,
true for expired successful evidence, false for fresh successful evidence.
Keep capability-level launcher fingerprints internal to capability/admission
plumbing rather than adding installation paths to the catalogue.

The row reports the default installed launcher. An exact profile refusal names
the actual model and launcher-mismatch reason; it must not imply the row proves
all profile commands. Existing GET `/api/session-runners` covers desktop and
remote; existing GET `/api/session-runners/{id}/status` remains remote-only.
An offline configured placeholder returns null version; an unknown id stays 404.
CLI stale is independent of row `stale`/availability and never changes capacity.

In this narrowly scoped card the operator/UI contract is the API fields and
existing task refusal/event rendering. A new visual badge, filters, a Hosts-page
redesign or client-side eligibility logic are not required. No frontend file is
needed to enforce admission; do not move security/correctness decisions to the UI.
Document field semantics and refusals in ops-http and agent-kinds.

### D-9: rollout runner support first, server enforcement second

Land reviewed code, then update each Codex-serving runner with probe/heartbeat
support and the required installed CLI using its existing sanctioned rollout
lane. Old servers tolerate the additions. Read local capabilities or a fresh
phone-home capabilities response to confirm the new fields and version on each
target; build SHA alone is insufficient. Refresh must remain good across two
probe periods. Exact profiles are qualified against their own descriptors.

Only then activate the server gate and catalogue projection; inspect both list
and status evidence and run the commissioned positive and negative acceptance
cases. Omitted data fails closed. A server-first deployment therefore refuses
6.1 work on unupgraded runners even if their actual CLI happens to be new enough.
No automatic transitional/advisory mode is needed with runner-first sequencing.
If unavoidable, the operator may commission a narrowly scoped D-7 exception;
the plan does not authorize setting one. Never change the model ladder for rollout.

Do not redeploy/reset the standing runner from this Plan task. Activation uses
the existing canonical restart/rolling runbooks and checks source SHA against
`/api/version`. Rollback the gate server if necessary; leave additive runner
support installed. Such a rollback removes protection, so retain the external
minimum-CLI operational constraint until enforcement returns. Do not downgrade
a serving runner below the floor while continuing 6.1 dispatch.

### D-10: collision boundaries

The current Plan diff contains this file only. For future Code, CARD-1008 owns
`scripts/c590-remote.sh`, `scripts/deploy-server2.ps1`,
`tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs` and related deployment
transport. None is in this plan's edit set. No Dockerfile/deployment change is
required to advertise an already installed CLI.

CARD-0965's card and current Code task `4c1697dc` were read: its remaining work is
the task-input/queue/readiness/attention proof area. This plan excludes
AgentTaskReplyService, AgentTaskInputService, SessionMessageQueueService,
AttentionService*, ParkedMessageSweepService, capability-task-input endpoints,
AgentTaskEndpoints, server/Program.cs, DB entities/migrations and their tests.
The shared transport files below are changed only in **runner capability**
members; the earlier CARD-0888 implementation touched some of them, but the
current remaining-proof task is not commissioned to modify those members.
There is no planned overlap with either active card's owned changes. Recheck
actual scope at Code admission; if an active owner expands into these files,
serialize that slice and rebaseline after landing rather than asserting that
different methods in one file guarantee collision freedom.

## Scoped slices

Only the plan file is written by this dispatch. Future implementation files are
listed explicitly below; proposed new files are marked **new**.

| Slice | Production/docs files | Tests and completion |
|---|---|---|
| S1 — evidence model and runner probe | `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs`; **new** `src/Antiphon.SessionRunner.Contracts/CodexCliVersion.cs`; **new** `src/Antiphon.SessionRunner/CodexCliVersionProbe.cs`, `CodexCliVersionRefreshService.cs`, `CodexCliVersionSettings.cs`; `src/Antiphon.SessionRunner/Program.cs`, `SessionRunnerRuntime.cs`, `PhoneHomeRuntimeAdapter.cs`. Reuse CodexWindowsLaunchPolicy without changing its launch contract; extract a helper there only if necessary to avoid a duplicate resolver. | **new** `tests/Antiphon.SessionRunner.Tests/CodexCliVersionProbeTests.cs`, `CodexCliVersionWindowsTests.cs`, `CodexCliVersionTestFixture.cs`. Real bounded-child fixtures may live in **new** `tests/Antiphon.SessionRunner.Tests/Fixtures/CodexVersionChild.ps1` (literal fixture commands only); inject only the process-I/O seam to exercise ownership without invoking a real provider. CP-1 and Windows CP-2. |
| S2 — phone-home refresh and exact-launch capability operation | `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs`; **new** `src/Antiphon.SessionRunner.Contracts/CodexCliVersionContracts.cs`; `src/Antiphon.SessionRunner/PhoneHomeConnectionService.cs`, `PhoneHomeCommandDispatcher.cs`, `PhoneHomeRuntimeAdapter.cs`, `Program.cs`; `server/Application/Interfaces/ISessionRunnerClient.cs`; `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs`, `PhoneHomeRunnerClient.cs`, `RunnerScopedSessionRunnerClient.cs`, `RoutingSessionRunnerClient.cs`, `PhoneHomeLiveConnection.cs`, `PhoneHomeRunnerDirectory.cs`. | **new** `tests/Antiphon.Tests/Application/RunnerCodexCliEvidenceTests.cs`. Drive actual serialization and local HTTP/phone-home dispatch with isolated fake processes/clients, including old envelopes and stale epochs. CP-3. Do not mutate task-input members. |
| S3 — metadata and admission | `server/Application/Services/ModelLevelAliases.cs`, `AgentTaskService.cs`, `AgentTaskDispatcher.cs`; **new** `server/Application/Services/CodexCliAdmissionPolicy.cs`, `CodexCliProbeDescriptor.cs`; **new** `server/Application/Exceptions/CodexCliVersionRequiredException.cs`; `server/Application/Settings/DelegationSettings.cs` including its existing validator. Pure helper decisions consume the services' existing dependencies; no server/Program.cs edit. | **new** `tests/Antiphon.Tests/Application/CodexCliAdmissionTests.cs`. Update only explicit test-client capability fixtures in `CodexPhoneHomeCreateTests.cs`, `PinnedCodexProfileDispatchLaunchTests.cs`, `ModelAvailabilityCreateTests.cs`, `ModelAvailabilityDispatcherTests.cs` when their otherwise-valid 6.1 path needs a fresh version. No production/test default that silently treats unknown as good. CP-4, CP-5, CP-7. |
| S4 — existing read surfaces and operator docs | `server/Application/Dtos/SessionRunnerCatalogueDtos.cs`, `server/Application/Services/SessionRunnerCatalogue.cs`; status fields in the contracts/directory files already owned by S2; `docs/ops-http.md`, `docs/agent-kinds.md`, `docs/ai-agent-tui-configuration.md`. | Expand S2's new evidence class with catalogue/status assertions. Existing `RunnerCatalogueTests.cs`, `PhoneHomeDirectoryTests.cs`, `PhoneHomeRunnerRetirementIdentityTests.cs` remain regression coverage. CP-3 final and CP-6. |

TestDesign must freeze any fixture-only adjustment inside the named files before
Code; do not widen to shared queue/attention harness changes to make tests green.
Commit/push each meaningful slice and before any checkpoint. No source edits
under an active build/test run. Final Review runs the closed affected rows at the
reviewed tip, including the Windows row on its qualified lane.

## Verification design

The following are **22 proposed single-result methods**, not claims of existing
tests. Keep vector sets inside their named method or update the expanded CP
minimum in TestDesign if parameterized. Each vector must assert its own outcome,
not merely that setup completed. Use isolated DB schemas and injected clocks.
Process-spawning classes require their assembly's ParallelLimiter<ProcessSpawnLimit>.
No real auth store, provider login, network model request, production runner,
Docker rollout or whole Unit/full-assembly run is part of ordinary verification.

| V | Proposed exact method | Required vectors and observable result |
|---|---|---|
| V-1 | `CodexCliVersionProbeTests.C959_Parses_and_orders_versions` | 0.156.1, 0.159.0, 0.159.1, 0.160.0, 0.9.0, 0.1000.0, prerelease and build metadata; null/empty/noise/two lines/overflow/invalid identifiers. Assert normalized values and numeric comparisons, not lexical order. |
| V-2 | `CodexCliVersionProbeTests.C959_Probe_is_version_only_and_auth_free` | Capture the actual child argv/env/stdin/cwd; exactly version-only command; no auth-file accesses, startup hooks, token sentinel, provider/model request or raw output in logs/DTO. Include supported native Linux resolution, explicit path and changed PATH. |
| V-3 | `CodexCliVersionProbeTests.C959_Failure_bounds_and_cleanup` | Missing binary, nonzero, stderr, output flood, timeout, cancellation and descendant holding a pipe. Assert fixed reason, null version, bounded completion and child cleanup evidence; caller cancellation is distinct. Include a real harmless owned child through the I/O seam. |
| V-4 | `CodexCliVersionProbeTests.C959_Refresh_replaces_evidence` | Startup before advertisement, five-minute clock advance, success then failure, unchanged heartbeat timestamp, single-flight concurrent requests, cache bound and executable identity change. Assert failure clears success and only completed attempts set checked-at. |
| V-5 | `CodexCliVersionProbeTests.C959_Local_and_registration_share_snapshot` | Actual local capability producer and PhoneHomeRuntimeAdapter expose equal fields including unknown; GET causes zero child starts. Deserialize legacy/missing fields and extra future fields without failure. |
| V-6 | `CodexCliVersionWindowsTests.C959_Npm_probe_uses_launch_resolution` | Native Windows stock shim fixture, sibling node versus PATH node, spaces and Unicode; compare selected exe/prefix to CodexWindowsLaunchPolicy.Apply and prove only --version follows codex.js. |
| V-7 | `CodexCliVersionWindowsTests.C959_Native_and_direct_node_probe` | Absolute native codex and already normalized node/codex.js; distinct installed versions, same selected argv and identity as launch. Include one real harmless Windows child cleanup execution. |
| V-8 | `CodexCliVersionWindowsTests.C959_Unverified_launcher_is_unknown` | Missing node/js/native package, modified/nonstock wrapper and unrepresentable loader override: no fallback to bare PATH, no arbitrary wrapper child, unknown reason. |
| V-9 | `RunnerCodexCliEvidenceTests.C959_Heartbeat_updates_only_probe_evidence` | Registration then newer success/failure heartbeat; capacity and liveness remain correct; no CLI payload does not renew checked-at; repeated success heartbeat ages out; out-of-order attempt ignored. |
| V-10 | `RunnerCodexCliEvidenceTests.C959_Freshness_boundaries` | Exactly 15 minutes, +1 tick, null checked-at, future +1 minute and beyond tolerance; fresh liveness with stale probe remains stale. One bounded refresh succeeds or returns unknown/stale; never stale admission. |
| V-11 | `RunnerCodexCliEvidenceTests.C959_Generation_change_clears_version` | Same-store new boot, same-boot new epoch, authorized different-store replacement, omitted capabilities, late reply/heartbeat from prior epoch. No stale good evidence crosses identities; existing retirement/lease refusals unchanged. |
| V-12 | `RunnerCodexCliEvidenceTests.C959_Catalogue_and_status_project_version` | Desktop and two remote rows have different versions; offline placeholder nulls; unavailable formerly known row keeps display evidence but cannot admit; unknown runner 404; buildVersion unchanged and unequal to CLI value. Check actual serialized list/status shapes. |
| V-13 | `RunnerCodexCliEvidenceTests.C959_Exact_probe_transport_is_bound` | Real isolated HTTP route and phone-home operation, selected runner only, matching descriptor fingerprint, old runner unsupported, null/default client and cancellation. Reject stale connection replies; no retry and no tokens/paths in catalogue/error output. |
| V-14 | `CodexCliAdmissionTests.C959_Ladder_and_exact_models_share_floor` | High/Medium/fallback and exact 6.1 use the single 0.159.1 metadata; Frontier/Low/other kinds and exact retired models do not acquire the new floor. Existing alias outputs remain identical. |
| V-15 | `CodexCliAdmissionTests.C959_Create_refuses_bad_versions` | Desktop and remote versions below/equal/above floor; omitted/unparseable/error/stale samples and failed refresh. Assert exact HTTP status/code/extensions, zero inserted task/session/preparation and no other-runner query. |
| V-16 | `CodexCliAdmissionTests.C959_Existing_refusals_and_flags_keep_precedence` | Held 6.1 plus old CLI keeps model_disabled/coda; signed-out remote plus old CLI keeps provider_sign_in_required; unknown auth plus fresh CLI admits. Existing auth/model flags never bypass version. No auth/profiles-validation calls are made by the version probe itself. |
| V-17 | `CodexCliAdmissionTests.C959_Queued_downgrade_blocks_before_claim` | Admit then downgrade/fail/expire before Tick; assert Blocked reason/event and zero worktree/preparation/adapter/input calls. Prior active sessions untouched. Rewalk/redirect rechecks the new actual target/model. |
| V-18 | `CodexCliAdmissionTests.C959_Exact_profile_model_wins` | Low/Frontier pinned to exact 6.1 is gated; High pinned to older exact model is not; normal empty ModelId uses ladder; blank ModelArgumentName retains wrapper-owned semantics and existing unsupported-model refusal. Profile revision/live model drift cannot reuse stale authorization. |
| V-19 | `CodexCliAdmissionTests.C959_Profile_launcher_uses_its_own_evidence` | Default install new plus selected native old refuses; default old plus selected native new admits with its own probe; different PATH/cwd/package/fingerprint and unsupported wrapper return unknown. Never authorize from AgentTui validation RunnerVersion. |
| V-20 | `CodexCliAdmissionTests.C959_Override_is_scoped_expiring_and_audited` | Empty, expired, wrong runner/model/code and invalid configuration refuse; matching operator exception admits only its named refusal and records Warning. It does not alter version evidence or bypass held model, signed-out auth, drain or platform. Advance fake clock past expiry without reload. |
| V-21 | `CodexCliAdmissionTests.C959_Compatible_launch_keeps_model_and_runner` | 0.159.1 and 0.160.0 on local/remote paths actually reach the recording adapter once with exactly the resolved 6.1 model and selected runner; await launch queue. Assert no model downgrade, extra model arg, fallback runner, auth read by CLI probe or spurious Warning. |
| V-22 | `CodexCliAdmissionTests.C959_Retry_and_cancellation_recheck` | Retry after repair requeues using current evidence; wrong/stale evidence refuses without transition. Caller cancellation leaves no new task/claim/session; deadline becomes unknown. Expired override and missing old-client method are never fail-open. |

R-1: existing auth/hold/profile suites: CodexPhoneHomeCreateTests (6 results),
PinnedCodexProfileDispatchLaunchTests (2), ModelAvailabilityCreateTests (11 from
10 methods, one with two arguments), ModelAvailabilityDispatcherTests (3): **22**.
Their version fixtures must explicitly represent good CLI evidence where auth is
the variable; retain all original assertions and unknown-auth behavior.

R-2: RunnerCatalogueTests (4), PhoneHomeDirectoryTests (7),
PhoneHomeRunnerRetirementIdentityTests (9 expanded): **20** results, pinning drain,
identity replacement and unknown rows. No relaxation of CARD-0953 is allowed.

R-3: ModelAliasTests: **76** expanded results across nine methods. Keep historical
alias/hold behavior. R-4: CodexWindowsLaunchPolicyTests: **26** expanded results,
native Windows only. TestDesign rechecks these source-derived counts at its base;
they are not current execution receipts.

### Post-land positive controls

These are **22 control families** for the later SourceLanding Mutation stage.
TestDesign freezes each independent mutant as a separately numbered variant,
its exact fixture vector, first failing assertion label and expected count.
Where a row names several guards, split it: one mutation must not conceal that
another guard is untested. The family count is not a frozen variant census.

| PC | Method (exact filter is `/*/*/<class>/<method>`, Min=1) | Mutation and first observable assertion |
|---|---|---|
| PC-1 | `CodexCliVersionProbeTests/C959_Parses_and_orders_versions` | Replace numeric ordering with lexical ordering / accept malformed output, separately; expected ordering or null-version assertion fails. |
| PC-2 | `CodexCliVersionProbeTests/C959_Probe_is_version_only_and_auth_free` | Append a model argument / inherit credential sentinel, separately; captured argv/env assertion fails before any provider call. |
| PC-3 | `CodexCliVersionProbeTests/C959_Failure_bounds_and_cleanup` | Accept nonzero/stderr/truncated success separately; null-version assertion fails. Remove owned tree cleanup in a safely supervised harmless-child variant; child-exited assertion fails. |
| PC-4 | `CodexCliVersionProbeTests/C959_Refresh_replaces_evidence` | Retain good value on failure / reset time on read / skip single-flight separately; value/time/start-count assertion fails. |
| PC-5 | `CodexCliVersionProbeTests/C959_Local_and_registration_share_snapshot` | Omit CLI fields from either producer separately; producer-parity assertion fails. |
| PC-6 | `CodexCliVersionWindowsTests/C959_Npm_probe_uses_launch_resolution` | Choose PATH node over sibling / execute cmd shim directly separately; exact selected-exe/prefix assertion fails. |
| PC-7 | `CodexCliVersionWindowsTests/C959_Native_and_direct_node_probe` | Substitute default install for explicit native / lose codex.js prefix separately; exact identity/argv assertion fails. |
| PC-8 | `CodexCliVersionWindowsTests/C959_Unverified_launcher_is_unknown` | Fall back after missing package / execute nonstock wrapper separately; unknown or zero-child assertion fails. |
| PC-9 | `RunnerCodexCliEvidenceTests/C959_Heartbeat_updates_only_probe_evidence` | Stamp receive-time as checked-at / preserve success after failed attempt separately; checked-at/value assertion fails. |
| PC-10 | `RunnerCodexCliEvidenceTests/C959_Freshness_boundaries` | Admit age over bound / accept future time / treat failed refresh as good separately; expected stale/unknown assertion fails. |
| PC-11 | `RunnerCodexCliEvidenceTests/C959_Generation_change_clears_version` | Retain evidence on new registration without fields / apply old epoch update separately; new generation's null-version assertion fails. |
| PC-12 | `RunnerCodexCliEvidenceTests/C959_Catalogue_and_status_project_version` | Use build SHA as CLI / use first remote sample for every row separately; row-specific serialized value assertion fails. |
| PC-13 | `RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound` | Route probe to Local / accept mismatched fingerprint or stale connection separately; recorded-target/evidence assertion fails. |
| PC-14 | `CodexCliAdmissionTests/C959_Ladder_and_exact_models_share_floor` | Remove 6.1 floor / give unrelated alias that floor separately; required-version assertion fails. |
| PC-15 | `CodexCliAdmissionTests/C959_Create_refuses_bad_versions` | Fail open on unknown / admit one patch below minimum separately; expected 409 and zero-row assertions fail. |
| PC-16 | `CodexCliAdmissionTests/C959_Existing_refusals_and_flags_keep_precedence` | Run version before hold/auth / reuse auth override for version separately; original-code/version-code assertion fails. |
| PC-17 | `CodexCliAdmissionTests/C959_Queued_downgrade_blocks_before_claim` | Remove dispatch recheck / use pre-redirect runner separately; Blocked/zero-launch assertion fails. |
| PC-18 | `CodexCliAdmissionTests/C959_Exact_profile_model_wins` | Always use tier model / ignore profile revision separately; exact pin refusal/admission assertion fails. |
| PC-19 | `CodexCliAdmissionTests/C959_Profile_launcher_uses_its_own_evidence` | Substitute default version / accept validation RunnerVersion separately; old selected install refusal assertion fails. |
| PC-20 | `CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited` | Remove runner/model/code/expiry checks or Warning separately; scope/refusal/audit assertion fails. |
| PC-21 | `CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner` | Change >= to > / substitute a lower model or another runner separately; exact-floor launch/argv/target assertion fails. |
| PC-22 | `CodexCliAdmissionTests/C959_Retry_and_cancellation_recheck` | Skip retry gate / swallow caller cancellation separately; state/no-new-row assertion fails. |

SourceLanding runs only inherited local children, with evidence and restoration
records outside the snapshot. No commits/pushes from a SourceLanding snapshot.
Each PC is method-scoped red then restored green; zero tests, compilation errors
and fixture failures are not red. Never mutate the daemon's installed executable,
remove a timeout to leave a child unowned, or let a test reach live provider auth.
No PC is executed by this Plan dispatch; ordinary Code does not claim these rows.

### Inspection

TestDesign freeze 79011cb4, 2026-10-03, inspected HEAD and origin/master
d40c16704e5a26c7da8ea0be11ed11170ebc828e (remote read at 12:50 UTC).
This appendix freezes the proposed verification without changing D-1..D-10.
The introductory TestDesign handoff and the preceding PC-1..PC-22 table are
historical: those PC numbers name **families F-1..F-22**, not the independently
executable controls numbered below. Code implements the 22 methods below as
single [Test] results, with internal labelled vectors, no Arguments/data source
expansion, no skips and no test-only fail-open product default.

| Bodies read | Boundaries -> coverage |
|---|---|
| Full CARD-0959 plan and card; CARD-0965/1008/1006/1001 card reads; agent-kinds launch/model sections, project-context, orchestration stage contract, testing manifest/runner/Mutation/delivery owners, resilience and session-runtime owners | D-1..D-10; all V/R; exclusions below |
| CARD-1008 Inspection/Delivery/Admission freeze and CARD-0891 freeze/checklist/provenance/Code reconciliation | Count versus assertion distinction, source-only evidence, importer admission, separate PCs |
| Entire CodexPhoneHomeCreateTests, PinnedCodexProfileDispatchLaunchTests, ModelAvailabilityCreateTests, ModelAvailabilityDispatcherTests, including their nested clients, factories, seeders and service registrations | V-14..V-22, R-1; selected-runner/auth/profile paths, real launch queue versus adapter-only evidence |
| Entire RunnerCatalogueTests, PhoneHomeDirectoryTests, PhoneHomeRunnerRetirementIdentityTests and ModelAliasTests | V-9..V-15, R-2/R-3; serialization, legacy data, disconnected socket, store/lease replacement and alias expansion |
| Entire CodexWindowsLaunchPolicyTests and CodexNpmLayout; launch resolver Apply/native/npm/direct-node branches; Runtime.DescribeCapabilities and PhoneHomeRuntimeAdapter.Capabilities | V-1..V-8, R-4; zero-byte layout is resolution evidence only; Apply is a no-op off Windows |
| Entire BridgeQueueHarness and PhoneHomeTestHost including scripted peer, recording local client, transcript and disposal helpers; FakeAgentProtocolAdapter Start/SendInput/OnSubmitted/conditional-input bodies; TestDbFixture and both ProcessSpawnLimit bodies | V-9/V-11/V-13/V-17/V-21/V-22; isolated DB, real queues, frame routing, fault injection, complete recipient receipt |
| PhoneHomeConnectionServiceTests.Concurrent_reply_and_heartbeat_never_drop_a_reply, Connected/Service/RecordingRuntime setup; PhoneHomeCommandDispatcherTests.Unsupported_operation_or_launch_never_enters_runtime | V-5/V-9/V-13; real writer arbitration and dispatcher, old operation refusal |
| RunnerProcessProbeTests timeout/cancellation tree tests, PID receipt/read/exited helpers; LocalHttpRunner body; both test csproj references/content copying | V-3/V-7/V-13; process ownership seam; LocalHttpRunner hardcodes .exe and is not a portable host fixture |
| AgentTaskDispatcher.BuildLaunchSpecAsync/ResolveDispatchAliasAsync/ShippedModelDisplay; AgentTuiProfileService.ParseRunnerVersion/SingleOutputRecord/NormalizeRunnerVersion; runner Program capability/DI mappings; importer ExtractSection/SplitRow/header parser | V-1/V-5/V-13/V-18/V-19; exact profile precedence, banner grammar, actual route registration; seven-row manifest |

All named existing regression test bodies were read before freezing the roster.
New setup is required, not claimed present: CodexCliVersionTestFixture; a bounded
process-I/O capture plus owned harmless child; nested evidence/admission kits;
explicit runner samples; a shared production mapping callable from an isolated
runner HTTP test host; and per-vector DB faults. Keep these in the new S1/S2/S3
files already listed. No BridgeQueueHarness, PhoneHomeTestHost, task-input or
attention helper edit is needed. Both test projects already reference SessionRunner;
Antiphon.Tests already references the checkpoint tool.

### Delivery inventory

Capability evidence is memory-only by D-3: **no durable sample or probe outbox**
is introduced. Its stable join is runnerId + storeId + bootId + accepted epoch +
launcher fingerprint + checkedAt, with requestId for exact probes. Restart must
lose evidence and reacquire it; pretending that a heartbeat or request ack is a
durable successful sample would be a defect.

| Producer -> destination | Persistence boundary and identity | Recovery and recipient evidence |
|---|---|---|
| Hosted probe -> singleton -> local GET and registration producer | Completed attempt replaces memory atomically; same boot/fingerprint/time on both producers | V-4/V-5 hold startup attempt, fail it, restart singleton; actual deserialized capability fields become unknown then fresh. GET starts zero children. |
| Snapshot -> PhoneHomeConnectionService -> real PhoneHomeConnectionWriter -> server live directory -> catalogue/status | Writer send is volatile; accepted generation owns server memory; no DB capability persistence | V-9 uses real writer, framed socket and server receive path with an already writable and a busy writer. Fail before send, after frame accepted/before sender observes completion, and disconnect before projection. Repeat the sample or reconnect; recipient fields must equal its original checkedAt. V-11 rejects old epoch after reconnect; accepted legacy registration clears it. |
| Admission helper -> selected local HTTP client or phone-home operation -> runner probe -> matching caller | No task claim/DB transaction during eight-second request; connection/request/descriptor/profile identity binds response | V-13 drives production HTTP mapping and PhoneHomeCommandDispatcher. Busy writer/eligible writer, failed request enqueue, lost response, cancelled request and reconnect between request/response must return unknown/cancellation, never borrowed evidence. A fresh explicit request after repair returns the actual requested runner's sample. No automatic retry. |
| Create/retry -> durable Queued task -> dispatcher -> launch queue or reused-session message queue -> recipient | TaskId -> AgentSessionId + accepted session generation -> SessionQueuedMessage.Id/SourceTaskId + full body and transcript baseline; task/queue rows persist, launch work item is volatile | V-17 proves refusal before claim/prep/input. V-21/V-22 use real AgentSessionLaunchQueue and SessionMessageQueueService, then read the recipient's complete matching UserPrompt from the isolated DB (and remote transcript pull). Queue insertion, Dispatched, Sent, StartedArgs and ack alone are insufficient. |

Freeze the V-21/V-22 handoff fault vectors as follows. Each starts through
CreateAsync or RetryAsync, never by inserting the queue row being proved.
For both desktop and remote, exercise an already eligible recipient and a busy
recipient (post-TurnEnd activity, then a real TurnEnd/flush). Hold/release the
recording adapter at the existing readiness/input seams. For **each** handoff,
inject before and after its persistence/receipt boundary: task SaveChanges;
dispatch claim/session SaveChanges; launch scheduling/start; message enqueue;
input submission; transcript confirmation/status save. Use the existing EF
ConfigureDbContext interceptor, adapter ThrowOnStart/BeforeInput/OnSubmitted and
new test-local client faults. Dispose/recreate service graph with the same isolated
DB at restart vectors; PreserveDatabaseOnDispose and AttachSessionId/AttachAgentId
already exist. Refresh CLI evidence before recovery; repeat with downgraded or
expired evidence and assert zero *new* input. Preserve already delivered work.
Where the existing failure path terminalizes the task, require its recorded
failure then **explicit RetryAsync**; do not invent automatic task retry.

For enqueue failure, no recipient receipt is allowed before explicit recovery.
For a crash after input but before confirmation, seed no receipt on the producer:
the recipient callback/pull must persist the actual full received body; recovery
finds that receipt without duplicate submission. Match task marker, complete body,
session, generation and a sequence above the attempt baseline. The expected body
contains LF, Unicode and a suffix sentinel:
"C959 delivery α\nsecond line\nEND-C959"; task framing is composed by the real
producer. Compare the whole composed body, not merely the nonce or suffix.
A clipped/wrong-session/old-generation transcript cannot satisfy this assertion.

Substitutes: the adapter models a recipient and records only bytes it actually
submits; it proves service-to-recipient queue behavior, not a vendor CLI accepting
a model. A scripted peer must call the real dispatcher for the new operation,
not return the expected capability by fiat. Synthetic process output proves parser,
bounds and ownership, not an installed vendor binary. Recreated DI proves durable
recovery at the injected boundary, not OS power-loss atomicity. Native Windows
CP-2 proves its resolver/process mechanics. Live provider canaries remain separate
commissioned acceptance and require a matching complete UserPrompt.

### Proves it works now

These are implementation obligations, not TestDesign execution receipts.
All assertion names below are literal Shouldly custom-message labels. Append a
vector key to a label when iterating. Put the first detecting assertion before
incidental state/trace assertions, so Mutation reports a behavior failure rather
than a fixture failure. PC rows name more specific first assertions where a method
has independent guards.

File binding is exact:
P = tests/Antiphon.SessionRunner.Tests/CodexCliVersionProbeTests.cs;
W = tests/Antiphon.SessionRunner.Tests/CodexCliVersionWindowsTests.cs;
E = tests/Antiphon.Tests/Application/RunnerCodexCliEvidenceTests.cs;
A = tests/Antiphon.Tests/Application/CodexCliAdmissionTests.cs.
Class name is the filename stem. The CP table supplies each exact ordinary command
selection; a method filter is /*/*/ClassName/ExactMethod, MinExecuted 1.

| V | File / exact method | Layer; inputs -> outputs | First detecting label |
|---|---|---|---|
| V-1 | P / C959_Parses_and_orders_versions | Pure contracts parser/comparer. Literal banner/ordering roster below, every invalid row null; normalized strings and numeric/prerelease/build comparisons | C959-v01-numeric |
| V-2 | P / C959_Probe_is_version_only_and_auth_free | Real probe with only process-I/O substituted. Capture final native/node argv, environment, stdin and cwd for bare/absolute/changed PATH selectors; zero auth-file/provider/model calls, no canaries in logs/DTO | C959-v02-argv |
| V-3 | P / C959_Failure_bounds_and_cleanup | Probe and real harmless child. Missing executable; exit 1; stderr; stdout/stderr 4097 bytes; cancellation; five-second timeout; descendant holds pipe; unconfirmed cleanup -> null/fixed reason, completion <=7s logical budget, owned children reaped | C959-v03-failure |
| V-4 | P / C959_Refresh_replaces_evidence | Hosted refresh/clock/cache. Startup holds advertisement, success at T; failure at T+5m replaces it; reads/heartbeat do not change time; 20 identical requests start once; 33 identities never exceed 32 cached entries or one child; path/size/mtime/boot change reprobes | C959-v04-replaced |
| V-5 | P / C959_Local_and_registration_share_snapshot | Actual Runtime capability producer, adapter, registration JSON and local HTTP mapping share success/unknown sample; 10 reads start no child; legacy DTO missing new fields and future field deserialize; new fields/features remain additive | C959-v05-local |
| V-6 | W / C959_Npm_probe_uses_launch_resolution | Windows layout stock shim; sibling present/absent vs PATH node; spaces/Unicode; selected exe/js equals Apply on the matching launch descriptor, followed only by --version | C959-v06-sibling |
| V-7 | W / C959_Native_and_direct_node_probe | Windows explicit native A/B and direct node+relative/absolute js; distinct output; exact selected identity/argv; real harmless tree cancellation completes and reaps | C959-v07-native |
| V-8 | W / C959_Unverified_launcher_is_unknown | Missing node, js, native vendor package independently; nonstock shim; shell wrapper; NODE_OPTIONS loader override; all unknown, no fallback or child | C959-v08-unknown |
| V-9 | E / C959_Heartbeat_updates_only_probe_evidence | Real writer/receive loop. Newer success then failure, omitted/duplicate/older sample, busy writer and reconnect faults; capacity/liveness preserved, checkedAt only from completed attempt | C959-v09-time |
| V-10 | E / C959_Freshness_boundaries | Pure policy + bounded helper at T: age 15m equal admits; +1 tick stale; missing time unknown; future +1m accepted, +1m+tick unknown/clock_skew; refresh good/bad/stale; max-age 1/15/60 valid, 0/61 invalid, refresh interval below bound | C959-v10-age |
| V-11 | E / C959_Generation_change_clears_version | Real directory: same store/new boot, same boot/new epoch, authorized store replacement; omitted capabilities or omitted CLI; late reply/heartbeat ignored; failed/unaccepted registration cannot destroy current evidence | C959-v11-cleared |
| V-12 | E / C959_Catalogue_and_status_project_version | Actual list/status JSON for desktop 0.159.1, remote A 0.160.0, remote B 0.156.1, configured offline and unknown id; freshness null/false/true and disconnected retained display; build SHA independent, no fingerprint/path/env exposure | C959-v12-per-runner |
| V-13 | E / C959_Exact_probe_transport_is_bound | Actual random-port runner route, local client and phone-home dispatcher; request/epoch/runner/fingerprint mismatches independently; unsupported operation/default interface null; request bounds; cancellation/deadline/503; no retry, no DB claim; production recipient sample after reconnect | C959-v13-target |
| V-14 | A / C959_Ladder_and_exact_models_share_floor | Metadata pure test: High/Medium/999/exact 6.1 all floor 0.159.1; Frontier/Low/other kinds/retired exact none; old For/ForCodex/ForLaunch string contracts retained | C959-v14-floor |
| V-15 | A / C959_Create_refuses_bad_versions | Real CreateAsync + existing exception middleware. Cartesian desktop/remote x F-current/floor/old/malformed/empty/timeout/omitted/stale; exact 409 codes/extensions and zero task/session/prep on refusal; healthy selected host preserved | C959-v15-code |
| V-16 | A / C959_Existing_refusals_and_flags_keep_precedence | Held+old -> model_disabled/coda; signed-out+old -> provider_sign_in_required; unknown auth+fresh -> queued. ignoreModelDisabled and allowUnauthenticatedProvider independently bypass no CLI refusal; probe uses no validation/auth flow | C959-v16-precedence |
| V-17 | A / C959_Queued_downgrade_blocks_before_claim | Real Tick: create fresh then old/failed/expired; Blocked reason prefix+event, zero prep/claim/session/input; existing active session unchanged. Redirect and chain rewalk to bad host/model/revision checked again | C959-v17-blocked |
| V-18 | A / C959_Exact_profile_model_wins | Low/Frontier exact 6.1 gated; High exact retired not gated; empty ModelId uses ladder; blank ModelArgumentName + exact id keeps model_argument_unsupported, blank/blank wrapper semantics; specialist/live model and revision drift recheck | C959-v18-model |
| V-19 | A / C959_Profile_launcher_uses_its_own_evidence | Default new/selected old refuses; default old/selected new admits; changed PATH/PATHEXT/cwd/js/executable/metadata/boot invalidates; unknown future cwd and wrapper refuse; validation RunnerVersion=Codex 0.160.0 cannot authorize | C959-v19-selected |
| V-20 | A / C959_Override_is_scoped_expiring_and_audited | Config validation and Create/Tick: scope/expiry/code/reason/lifetime vectors below; matching exception only, Warning with exact facts at each use; evidence unchanged; hold/auth/platform/drain/retirement/capacity/launch/profile refusals retained | C959-v20-scope |
| V-21 | A / C959_Compatible_launch_keeps_model_and_runner | Real producer/launch/message queues to recipient transcript; desktop/remote x floor/current x eligible/busy, recovery boundaries above; one model argument=6.1, original runner, complete full-body receipt, no Warning/probe auth | C959-v21-receipt |
| V-22 | A / C959_Retry_and_cancellation_recheck | Real RetryAsync after repair plus full queue receipt; wrong/stale/old evidence leaves status/event rows unchanged; expired override no permission; caller cancellation during probe leaves no task/claim/session, owned deadline is unknown; recovery reruns admission | C959-v22-state |

#### Literal evidence and exact-launch fixtures

T = 2026-10-03T12:00:00.0000000Z; use FakeTimeProvider, never DateTime.UtcNow
to qualify freshness. All successful samples have error=null, matching fingerprint
and the current generation. Output literals below use JSON string escapes.

| Fixture | stdout; other input | Runner sample | Server 6.1 result after one permitted refresh |
|---|---|---|---|
| F-current | "codex-cli 0.160.0\n"; exit 0, stderr "" | 0.160.0, checkedAt=T | admit |
| F-floor | "codex-cli 0.159.1\r\n"; exit 0 | 0.159.1, T | admit |
| F-old | "codex-cli 0.156.1\n"; also "codex-cli 0.159.0\n" | respective normalized version, T | 409 codex_cli_version_too_old |
| F-malformed | "codex-cli banana\n" | null, T, invalid_output | 409 codex_cli_version_unknown |
| F-empty | ""; also null I/O record | null, T, invalid_output | 409 codex_cli_version_unknown |
| F-timeout | "" while child remains alive at five seconds | null, completed time, timeout after cleanup | 409 codex_cli_version_unknown |
| F-stale | "codex-cli 0.160.0\n", checkedAt=2026-10-03T11:44:59.9999999Z; refresh returns the same successful old timestamp | 0.160.0, original time | 409 codex_cli_version_stale |
| F-failed-refresh | F-stale then exit 1 on refresh | null, new attempt time, nonzero_exit | 409 codex_cli_version_unknown, not stale or good |
| F-legacy | {"backend":"InboxConhost","requested":"inbox","reason":"test","fellBack":false,"version":"d40c1670"}; no CLI fields; exact operation unsupported | all new fields null | 409 codex_cli_version_unknown |
| F-live-override | F-old plus exact operator item below, evaluated at T | remains 0.156.1 | admit + Warning; evidence remains below floor |
| F-expired-override | same item, now=2026-10-03T12:05:00Z (equality expires), and +1 tick | remains 0.156.1 | 409 codex_cli_version_too_old |

Fixed runner reason tokens for this freeze: invalid_output, executable_missing,
nonzero_exit, stderr_output, output_truncated, timeout, cancelled,
cleanup_unconfirmed, launcher_unverified, probe_busy; server helper tokens include
clock_skew, evidence_missing, generation_mismatch, launcher_mismatch,
probe_unavailable. These are new bounded fixture tokens, not baseline constants;
the **three refusal codes above are quoted from D-6**. Assert null plus the
specific reason, never raw stderr or exception text. Caller cancellation throws
OperationCanceledException to its caller rather than becoming an ordinary refusal.

Literal operator item uses canonical desktop; repeat with runner-a:
{"runnerId":"desktop","model":"gpt-6.1-sol","reason":"C959 isolated qualification",
"expiresAtUtc":"2026-10-03T12:05:00Z",
"allowedRefusalCodes":["codex_cli_version_too_old"]}.
Map member casing to the final settings DTO without broadening this permission.
Test empty array; wrong runner/model/code separately; "*" in each scope;
empty reason; duplicate exact tuple; invalid code; missing/invalid UTC expiry;
lifetime 24h accepted versus 24h+tick rejected; expiry at T and T-1tick;
and advancing beyond expiry without reloading settings. Also test the other two
CLI codes with individually matching entries. No override is applied to live config.

Parser fixture roster, in addition to the table: codex-cli 0.9.0, 0.1000.0,
0.159.1-beta.1, 0.160.0-beta.1, 0.159.1+build.7,
0.159.1-beta.2 versus beta.10; banner aliases "codex 0.160.0" and
"version 0.160.0". Accept no trailing terminator, one LF or one CRLF.
Reject whitespace-only, "Codex 0.160.0 extra", "prefix codex-cli 0.160.0",
two records, an extra empty record, bare "0.160.0", missing patch, negative
component, leading-zero numeric component/prerelease, empty prerelease/build
identifier, underscore identifier, and Int32 overflow "codex-cli 2147483648.0.0".
Assert floor prerelease < floor, newer minor prerelease > floor, numeric prerelease
2 < 10, release > same-core prerelease, build metadata equal precedence.

Launcher fixtures live entirely beneath a per-test temporary root; aliases A/B
identify different installed layouts. Fixture selectors are not serving paths.
A Linux native layout resolves PATH="A:B" to A/codex with output F-current and
PATH="B:A" to B/codex with F-old; absolute B/codex must ignore PATH A.
Recognized native layout uses a file identity plus process-I/O seam; zero-byte
files alone never prove a running native binary. Use an executable-format fixture
or the harmless-child seam for execution; never mark an arbitrary shell wrapper
trusted just because it is called codex.

Windows reuses CodexNpmLayout's exact StockNpmShimText under
"C959 npm rôot α": codex.cmd, sibling node.exe,
node_modules/@openai/codex/bin/codex.js and the vendored
codex-win32-x64/vendor/x86_64-pc-windows-msvc/bin/codex.exe.
PATH node lives under a second root; PATHEXT=".EXE;.CMD".
Native final argv is ["--version"]; node final argv is
[absolute-codex.js,"--version"]. Relative js resolves against descriptor cwd.
Compare version-probe resolution with Apply using the same resolution inputs
before replacing launch args with version args. For remote projection a
desktop codex.cmd selector projects to installed Linux codex before resolution.

Fingerprint input differences tested independently: selected executable/package,
effective resolution cwd, PATH, PATHEXT, recognized node/js prefix, size, mtime and
runner boot. Do not assert an invented hash serialization: assert stable equality
for identical descriptors, inequality/invalidation for changed identity, SHA-256
opaque shape, and no plaintext paths/environment on public projections.
Profile revision is additionally bound by admission, not silently folded into
another profile's default-row value. A not-yet-created cwd with relative selector
is unknown; an absolute installed selector is still verifiable.

Seed synthetic inherited values under OPENAI_API_KEY, ANTIPHON_TASK_TOKEN,
HTTP_PROXY, HTTPS_PROXY, NODE_OPTIONS, NODE_PATH, BASH_ENV and ENV, plus a
synthetic parent CODEX_HOME containing auth.json. Every value is a harmless
"C959-..." canary. Capture the actual ProcessStartInfo after sanitization:
UseShellExecute=false, empty fresh CODEX_HOME, neutral scratch cwd, closed stdin,
only OS/runtime allowlist environment, no credential-file/probe-validator access.
Run descriptor validation before process start; oversized/secret-placeholder
resolution fields and unsupported loader overrides return unknown with starts=0.

#### Harmless child and missing setup

S1 must materialize the optional Fixtures/CodexVersionChild.ps1 from literal
source, or embed the same literal in CodexCliVersionTestFixture; locate it from
the repository root if file-backed, since the csproj does not copy arbitrary ps1.
It has only fixed modes: success emits "codex-cli 0.160.0" and exits 0;
nonzero emits that line and exits 1; stderr adds one fixed stderr byte; flood
writes 4097 stdout bytes; tree starts one leaf child that holds inherited pipes;
leaf waits for parent cleanup. Use ProcessStartInfo.ArgumentList with the
resolved pwsh executable, -NoLogo -NoProfile -NonInteractive -File and literal
mode, never -Command or a shell command string. Each parent/leaf writes PID plus
UTC start time into its own receipt file under the owned root before signalling
ready. The leaf also has a hard 20-second self-exit as a secondary safety net.

Only the process-I/O factory redirects an already validated version-only
descriptor to this helper: production launch validation must not accept pwsh as
a Codex launcher. Capture and assert the unmodified production descriptor first.
The fixture owns Process handles and a finally cleanup independent of the
production cleanup being mutated; cleanup validates PID/start identity before
termination and awaits exit. A missing ready/PID receipt is setup failure,
never a passing cleanup assertion. Fake-clock bounds run only after the ready
barrier; real elapsed cleanup has a bounded scheduling margin and a hard test
deadline, not a relaxed product five-plus-two-second budget. Exercise stdout and
stderr caps separately; descendants/pipe cleanup are real on Linux and Windows.
Mutating the product kill leaves the fixture's final rescue intact.

E hosts the production route mapping on loopback port 0 with the probe singleton
and process-I/O fixture; it must not copy a fake handler that merely returns F-current.
Expose/reuse that mapping within the already scoped new probe file and Program
registration. Phone-home uses the actual dispatcher/runtime adapter behind its
framed peer. No call reaches 17204 or a serving runner. Existing LocalHttpRunner
is Windows-only and cannot be used to claim CP-3 portable.

A nests its service kit inside CodexCliAdmissionTests, using BridgeQueueHarness
ConfigureServices/ConfigureDbContext and an isolated schema connection everywhere.
Register selected-runner and unrelated-runner spies separately, add real model/
profile seed rows, and use the existing launch queue; no shared helper mutation.
Use a controllable TimeProvider whose timers advance, or a scaled clock for
queue waits: a FakeTimeProvider never advanced during awaited queue polling hangs.
All new classes are Integration except the purely in-process portions retained
within them; apply assembly-local ProcessSpawnLimit to classes that spawn a child.
No Slow category is added solely for a single bounded child.

### Guards the regression

Source-only census at the inspected current master used [Test] method extraction
and counted [Arguments] per method (default one); no DataSource or Skip attributes
occur in these nine files. The powershell read-only census produced:
6/6, 2/2, 10/11, 3/3, 4/4, 7/7, 7/9, 9/76, 25/26 (methods/results respectively,
in the table order). No test execution is claimed.

| R | Existing class roster (exact class filter operands in CP table) | Decisive assertions retained; fixture changes |
|---|---|---|
| R-1 | CodexPhoneHomeCreateTests 6; PinnedCodexProfileDispatchLaunchTests 2; ModelAvailabilityCreateTests 11; ModelAvailabilityDispatcherTests 3 | Exact auth 409/extensions/remedy, unknown auth queues, auth cancellation inserts nothing, retry-selected runner, one model arg/provenance and whole long block, held model state/events. Add explicit good capabilities and exact probe response to CodexPhoneHomeCreateTests.ProbeClient, with stable per-runner generation and separate CLI-call trace. Preserve auth Calls and local auth/ResolveCalls assertions: local capability reads use Local. Add explicit local runner/registry fixture in ModelAvailabilityCreateTests.CreateService for its two current Codex tier vectors. Pinned tests use exact gpt-5.6-terra and need no new-floor bypass; dispatcher hold tests are Claude and require no capability fixture change. |
| R-2 | RunnerCatalogueTests 4; PhoneHomeDirectoryTests 7; PhoneHomeRunnerRetirementIdentityTests 9 | Row isolation, 404, occupancy/capacity and historical placement; capacity/store/runner/lease/closed-socket refusal codes unchanged. Both bool-argument methods in retirement class contribute 2 each, others 1. No fixture edit planned. |
| R-3 | ModelAliasTests 76 | Per-method expanded roster: Normalize_maps_known_family_text 34; opus hold text 6; sonnet hold text 6; unrecognised opus 3; unrecognised sonnet 3; unknown text 6; CanonicalHoldAlias accepts 7/rejects 10; Bare_sol_and_retired_slugs_keep_their_historical_aliases 1. No fixture edit. |
| R-4 | CodexWindowsLaunchPolicyTests 26 | All 25 methods; Nonpositive_budget_is_refused has arguments 0/-1 (2 results); other methods 1. Native/shim/relative-js selection, sibling precedence, 7000/30000 and +1 ceilings, NUL/surrogate behavior unchanged. Native Windows only, no Linux substitute. |

Checkpoint roster freeze: CP-1=V-1..5 (5), CP-2=V-6..8+R-4 (29),
CP-3=V-9..13 (5), CP-4=V-14..22 (9), CP-5=R-1 (22),
CP-6=R-2 (20), CP-7=R-3 (76). Sum=166 results (22 new, 144 existing).
The exact seven filters/build paths/minima/time estimates below are unchanged:
**delta 0 methods, 0 results, 0 checkpoint rows, 0 ordinary minutes**.
Keep the single-result vector layout; any Code parameterization requires a
documented recount and importer receipt rather than silently inflating the floor.

### Checkpoints

Group names name the lane. CP-2 requires Windows; all other rows are portable
and normally run on the automatically selected Linux lane. The table is a closed
ordinary Code/Review list, pending the TestDesign method/count freeze. Import
validity does not establish that proposed tests already exist. CP-3 runs after
S2-S4 together so its API projection tests have their implementation.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c959-probe/` | portable-runner-probe | `/*/*/CodexCliVersionProbeTests*/C959_*` | V-1..V-5 | 5 proposed single-result methods; 0 failed/skipped | 5 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c959-windows/` | windows-native-launcher | `/*/*/(CodexCliVersionWindowsTests*)\|(CodexWindowsLaunchPolicyTests*)/*` | V-6..V-8, R-4 | 3 proposed plus 26 source-derived regression results; 0 failed/skipped | 29 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S2-S4 | `tests/Antiphon.Tests -> bin-c959-evidence/` | portable-capability-wire | `/*/*/RunnerCodexCliEvidenceTests*/C959_*` | V-9..V-13 | 5 proposed single-result methods; 0 failed/skipped | 5 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c959-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S3 | `tests/Antiphon.Tests -> bin-c959-admission/` | portable-admission-db | `/*/*/CodexCliAdmissionTests*/C959_*` | V-14..V-22 | 9 proposed single-result methods; 0 failed/skipped | 9 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c959-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S3 | `tests/Antiphon.Tests -> bin-c959-auth/` | portable-existing-refusals | `/*/*/(CodexPhoneHomeCreateTests*)\|(PinnedCodexProfileDispatchLaunchTests*)\|(ModelAvailabilityCreateTests*)\|(ModelAvailabilityDispatcherTests*)/*` | R-1 | 22 source-derived expanded results; 0 failed/skipped | 22 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c959-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S2-S4 | `tests/Antiphon.Tests -> bin-c959-directory/` | portable-directory-regression | `/*/*/(RunnerCatalogueTests*)\|(PhoneHomeDirectoryTests*)\|(PhoneHomeRunnerRetirementIdentityTests*)/*` | R-2 | 20 source-derived expanded results; 0 failed/skipped | 20 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c959-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S3 | `tests/Antiphon.Tests -> bin-c959-models/` | portable-model-regression | `/*/*/ModelAliasTests*/*` | R-3 | 76 source-derived expanded results; 0 failed/skipped | 76 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c959-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

## Verification execution and TestDesign handoff

TestDesign must freeze the 22 methods' exact fixture/argument rosters, independent
PC variants with first assertion labels, the harmless child implementation,
profile-resolution input identity, and all fake-client capability updates. Audit
every acceptance condition/guard to at least one V and independent PC; publish
missing/duplicate mappings. Recount all seven CP minima and re-import the actual
table. Do not silently replace the Windows row with Linux skips. No new product
choice is delegated to TestDesign; a discovered implementation contradiction
returns to Plan with concrete evidence.

For ordinary Code use the checkpoint tool once per committed slice group, with
`--expected-source-sha <full-sha>`; continue `wait` while exit is 75 and own every
child through completion. The explicitly commissioned Plan check below uses
`scripts/run-checkpoint.ps1` directly because owner-unverified checkpoint-tool
execution can refuse CARD-0853 ownership. The driver self-leases a build slot;
never double-wrap it. Any other build/test driver requires scripts/build-slot.ps1.
Exit 4 means not run, never an unleased retry. Any bootstrap build for future Code
must be isolated and explicitly listed by its dispatch. No additional build is
needed for this Plan's importer: Antiphon.Tests already references the tool.

Per CP retain executed/passed/failed/skipped counts and the unedited CHECKPOINT
line with source SHA/dirty/build-source/slot facts. An unexpected red is rerun at
the assigned base with the exact failing method before calling it inherited.
Remove only task-owned alternate outputs after all children finish, checking
canonical in-root paths and nonempty names. Keep evidence.

### Plan-stage documentation check

At the committed plan SHA run this exact existing documentation/import selection:

```powershell
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name DOCS-0959 -Project tests/Antiphon.Tests -OutputPath bin-c959-plan/ -Filter '/*/*/(DockerStackDocumentationTests*)|(CheckpointImportTests*)|(CheckpointManifestTests*)/*' -MinExecuted 37 -Expect DockerStackDocumentationTests,CheckpointImportTests,CheckpointManifestTests -ExpectedSourceSha <full-plan-sha> -ResultsRoot .antiphon/c959-plan-checkpoints
```

The earlier task f500932f report had 36 passing results. This baseline has
**37**: 11 DockerStackDocumentationTests, 20 CheckpointImportTests and six
CheckpointManifestTests. These tests validate existing documentation/contracts
and importer fixtures; they do not prove the correctness of a newly written plan.

Then invoke the real importer using the assembly that check built, without a
second build, executor or slot:

```text
dotnet tools/Antiphon.Checkpoints/bin-c959-plan/Antiphon.Checkpoints.dll import --plan docs/superpowers/plans/2026-10-03-card-0959-runner-codex-version-plan.md --out .antiphon/c959-plan-checkpoints/imported.yml
```

Require exit 0 and all seven CP rows/minima/filters in the emitted manifest.
The shell invocation uses literal `|`; only Markdown table cells escape `\|`.
Do not claim runtime tests or PCs passed from this documentation-only check.

### Cost

Ordinary Code floor: **55 minutes** (8+8+8+10+8+8+5), including isolated builds;
**166 proposed/source-derived results**, 22 of them new. Queue waits, authoring
and any explicitly commissioned bootstrap are additional. Final Review repeating
these affected rows adds 55 minutes. Plan verification is one documentation build
plus 37 selected results and one no-build import; estimate eight minutes.

Mutation has 22 families and multiple independent variants per family. Reserve
an initial planning envelope of 300 minutes plus native-Windows scheduling;
this is not a frozen variant count or execution estimate. TestDesign must replace
it with a variant census and per-method red/green build/time budget. No whole-suite
PC cycles and no claimed measured savings.

## Risks and operational acceptance

| Risk | Control / remaining limit |
|---|---|
| Probe certifies a different executable from the launch. | Launch-policy resolution, descriptor/fingerprint binding and D-4 operation; unsupported wrappers fail unknown. File metadata cannot prove protection from a malicious replacement preserving metadata. |
| Binary changes between admission and actual child launch. | Recheck at dispatch and invalidate changed identity; operators drain before in-place upgrades. This is bounded freshness, not an OS-level atomic executable lease. |
| Phone-home heartbeat makes an old version look new. | Checked-at belongs to completed probe, epoch-bound samples, missing registration clears evidence, repeated heartbeats do not extend age. |
| Strict unknown behavior breaks old runners/tests. | Runner-first rollout; explicit good samples in otherwise-valid test fixtures; unknown stays a negative test. No fail-open default for test convenience. |
| A version-only process hangs or starts other work. | Recognized launchers only, fixed argv, auth-free environment, bounded owned children and no arbitrary wrapper execution. No provider-based probing. |
| Deadline/cancellation translated into success. | Caller cancellation propagates; owned timeout is unknown; original auth unknown behavior remains separate. |
| New override becomes a permanent back door. | Operator config only, exact scope/codes, expiry checked per use, audit Warning; no global switch and no shared auth bypass. |
| Pinned profile owns its model invisibly. | Preserve existing model semantics; gate known resolved 6.1 models. Do not claim enforcement over an opaque wrapper that chooses a different model internally. |
| A documentation/import pass is mistaken for implementation proof. | Plan-only receipt explicitly limited; TestDesign next; Code/Review/native Windows and post-land PCs remain required. |
| Concurrent cards expand into shared transport files. | Explicit edit exclusions and Code-admission scope check; serialize any real collision. No deployment or attention/input edits here. |

After reviewed implementation lands, the rollout caller collects: per-runner
probe/version/time evidence on both local and remote APIs; two refresh periods
without timestamp inflation; an offline/legacy unknown refusal; an isolated
below-floor refusal; and a compatible fake/commissioned canary launch on each
OS with unchanged model/runner. A real model canary is a separate authorized
spend and requires complete matching UserPrompt transcript evidence. Never
install an old CLI on a serving runner merely to make the negative case.

## FOLLOW-UPS

Board searches were run with `card.ps1 search -Board Antiphon -All` for
`Codex CLI version` (CARD-0959 and CARD-0903) and `Codex profile` (including
CARD-0140). This plan stays on CARD-0959; no duplicate filed. CARD-0903 owns the
already adopted ladder and CARD-0140 the already established exact-profile
launch behavior. The optional visual runner badge and support for arbitrary
version-wrapper commands are explicit non-goals, not newly discovered defects.
No unrelated structural defect was established in this Plan dispatch.

--- next stage ---
next: test-design
handoff: Freeze CARD-0959's 22 V methods, independent PC variants, exact-launch descriptor and harmless-child fixtures, fake-client updates and seven checkpoint rosters; preserve fail-closed freshness, runner-first rollout, separate operator overrides and the CARD-1008/0965 exclusions.
artifact: docs/superpowers/plans/2026-10-03-card-0959-runner-codex-version-plan.md
