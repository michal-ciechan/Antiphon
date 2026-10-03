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
Docker rollout or full-assembly run is part of ordinary verification. The Final
continuation also runs the whole Unit lane as CP-8 below.

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

| Family | Method (exact filter is `/*/*/<class>/<method>`, Min=1) | Mutation and first observable assertion |
|---|---|---|
| F-1 | `CodexCliVersionProbeTests/C959_Parses_and_orders_versions` | Replace numeric ordering with lexical ordering / accept malformed output, separately; expected ordering or null-version assertion fails. |
| F-2 | `CodexCliVersionProbeTests/C959_Probe_is_version_only_and_auth_free` | Append a model argument / inherit credential sentinel, separately; captured argv/env assertion fails before any provider call. |
| F-3 | `CodexCliVersionProbeTests/C959_Failure_bounds_and_cleanup` | Accept nonzero/stderr/truncated success separately; null-version assertion fails. Remove owned tree cleanup in a safely supervised harmless-child variant; child-exited assertion fails. |
| F-4 | `CodexCliVersionProbeTests/C959_Refresh_replaces_evidence` | Retain good value on failure / reset time on read / skip single-flight separately; value/time/start-count assertion fails. |
| F-5 | `CodexCliVersionProbeTests/C959_Local_and_registration_share_snapshot` | Omit CLI fields from either producer separately; producer-parity assertion fails. |
| F-6 | `CodexCliVersionWindowsTests/C959_Npm_probe_uses_launch_resolution` | Choose PATH node over sibling / execute cmd shim directly separately; exact selected-exe/prefix assertion fails. |
| F-7 | `CodexCliVersionWindowsTests/C959_Native_and_direct_node_probe` | Substitute default install for explicit native / lose codex.js prefix separately; exact identity/argv assertion fails. |
| F-8 | `CodexCliVersionWindowsTests/C959_Unverified_launcher_is_unknown` | Fall back after missing package / execute nonstock wrapper separately; unknown or zero-child assertion fails. |
| F-9 | `RunnerCodexCliEvidenceTests/C959_Heartbeat_updates_only_probe_evidence` | Stamp receive-time as checked-at / preserve success after failed attempt separately; checked-at/value assertion fails. |
| F-10 | `RunnerCodexCliEvidenceTests/C959_Freshness_boundaries` | Admit age over bound / accept future time / treat failed refresh as good separately; expected stale/unknown assertion fails. |
| F-11 | `RunnerCodexCliEvidenceTests/C959_Generation_change_clears_version` | Retain evidence on new registration without fields / apply old epoch update separately; new generation's null-version assertion fails. |
| F-12 | `RunnerCodexCliEvidenceTests/C959_Catalogue_and_status_project_version` | Use build SHA as CLI / use first remote sample for every row separately; row-specific serialized value assertion fails. |
| F-13 | `RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound` | Route probe to Local / accept mismatched fingerprint or stale connection separately; recorded-target/evidence assertion fails. |
| F-14 | `CodexCliAdmissionTests/C959_Ladder_and_exact_models_share_floor` | Remove 6.1 floor / give unrelated alias that floor separately; required-version assertion fails. |
| F-15 | `CodexCliAdmissionTests/C959_Create_refuses_bad_versions` | Fail open on unknown / admit one patch below minimum separately; expected 409 and zero-row assertions fail. |
| F-16 | `CodexCliAdmissionTests/C959_Existing_refusals_and_flags_keep_precedence` | Run version before hold/auth / reuse auth override for version separately; original-code/version-code assertion fails. |
| F-17 | `CodexCliAdmissionTests/C959_Queued_downgrade_blocks_before_claim` | Remove dispatch recheck / use pre-redirect runner separately; Blocked/zero-launch assertion fails. |
| F-18 | `CodexCliAdmissionTests/C959_Exact_profile_model_wins` | Always use tier model / ignore profile revision separately; exact pin refusal/admission assertion fails. |
| F-19 | `CodexCliAdmissionTests/C959_Profile_launcher_uses_its_own_evidence` | Substitute default version / accept validation RunnerVersion separately; old selected install refusal assertion fails. |
| F-20 | `CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited` | Remove runner/model/code/expiry checks or Warning separately; scope/refusal/audit assertion fails. |
| F-21 | `CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner` | Change >= to > / substitute a lower model or another runner separately; exact-floor launch/argv/target assertion fails. |
| F-22 | `CodexCliAdmissionTests/C959_Retry_and_cancellation_recheck` | Skip retry gate / swallow caller cancellation separately; state/no-new-row assertion fails. |

SourceLanding runs only inherited local children, with evidence and restoration
records outside the snapshot. No commits/pushes from a SourceLanding snapshot.
Each PC is method-scoped red then restored green; zero tests, compilation errors
and fixture failures are not red. Never mutate the daemon's installed executable,
remove a timeout to leave a child unowned, or let a test reach live provider auth.
No PC is executed by this Plan dispatch; ordinary Code does not claim these rows.

### Inspection

TestDesign freeze 79011cb4, 2026-10-03, inspected HEAD and origin/master
d40c16704e5a26c7da8ea0be11ed11170ebc828e (remote tip rechecked unchanged at 13:21 UTC).
This appendix freezes the proposed verification without changing D-1..D-10.
The introductory TestDesign handoff and the preceding F-1..F-22 family table are
historical: the original PC-1..PC-22 identifiers name **families F-1..F-22**, not the independently
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

#### TestDesign amendment ee18a226: V-21/V-22 delivery representation

2026-10-03; inspected implementation start ref
`49ae666d1dfec4069f3dbe4325c61e06685eb51f`. This amendment changes verification
only. D-1..D-10, S1..S4, all 22 method names and every unrelated frozen vector
remain binding. It supersedes the demand that a Codex recipient's UserPrompt
contain the inline LF goal. It authorizes no spill, queue, composer, ceiling,
readiness or provider-policy implementation change.

Additional bodies read for this amendment (the preceding inspection records
the original freeze, not a re-reading of all those files today):

| Bodies read | Boundaries -> coverage or exclusion |
|---|---|
| Entire CodexCliAdmissionTests, including V-21/V-22 and all nested kits, factories, clients and registry | V-21/V-22; local-only delivery, LF assertion on a pointer, admission-only retry; missing setup below |
| BridgeQueueHarness.CreateAsync, HarnessOptions, InsertEntryAsync, DisposeAsync; FakeAgentProtocolAdapter.StartAsync, SendInputAsync, conditional input and submission/Inputs properties | Real queues, actual recipient callback and retained DB -> V-21/V-22; G-186..192, G-214..220 |
| PhoneHomeTestHost.StartAsync and PhoneHomeScriptedPeer receive/reply/transcript/disposal bodies; RunnerCodexCliEvidenceTests.C959_Exact_probe_transport_is_bound actual-dispatch fixture | Real framed operations and transcript pull -> remote V-21/V-22; default Input ack is not receipt |
| DurableRunnerSpillReceiptTests.Dispatcher_pointer_survives_queue_binding_and_reaches_a_complete_UserPrompt, Queued_brief_is_written_by_the_runner_when_a_busy_recipient_becomes_eligible, Screen_only_delivery_keeps_spill_bytes_and_a_retry_writes_the_same_body, BindRunnerAsync | Nearest spill fixtures -> V-21/V-22; their direct enqueue and write-at-OnSubmitted shortcut cannot prove producer-to-recipient or write-before-input |
| AgentTaskDispatcher.FitBriefForTyping/CeilingsForBrief/FitBriefForSession and FailNeverStartedAsync entry/pull/brief-state branches; LandDeliveryBoundary; DelegationReportFormatter.BuildBrief/BuildBriefPointer/FlattenForJoiningComposer; PtyDeliveryCeilings; PtyInputEncoding | Logical versus wire representation -> PC-187/209..215; watchdog -> PC-221 |
| SessionMessageQueueService.BindStagedSpill, attempt metadata, DeliverAsync encoding/submission, late-confirm/transcript queries, AcceptedByCompleteUserPrompt/StampAttemptVerdict; RemoteSpillCourier.FindDurableAsync/HasCompleteMatchingUserPromptAsync; PromptSubmissionMatch | Persistence, complete receipt, session/sequence fences and replay -> PC-216/218..224 |
| PhoneHomeCommandDispatcher.Input/WriteSpillIfPresentAsync; RunnerWorkspaceService.WriteSpillAsync; RunnerCodexAdapter.SendInputAsync | Runner file precedes terminal input -> PC-217; path and bytes -> PC-211/213 |
| session-runtime-invariants CARD-0353/0312, CARD-0647/0649 and multiline-input rules; testing-and-build Final recipe/manifest/gate/Mutation; Code report 1a73906f V table, remaining work and unedited receipts | Contract correction, pending evidence and counts; no new execution claim |

Inherited evidence: the full portable run at
`e6a7e365a577b1ffb496c561ee51de179fc3fdb8` executed 137: 136 passed, V-21
failed at `C959-pc-187`, zero skipped. Latest CP-3 at
`b8f12606294e57532a3d7eb759295f9926236e29` passed 5. Whole Unit at
`c443a6e1e150d85ae828c933e822c15f6a83d0f3` executed/passed 3942, with 52
skips. The raw Unit run.log refines the report's "platform" summary: **33 are
Windows-only; 19 lack jq**, a missing setup prerequisite, not an OS exclusion.
Windows CP-2 was not run. These receipts were read from
`/work/worktrees/task-1a73906f/.antiphon/task-1a73906f.md`; they do not verify
this amendment or the next Code SHA. V-22's passing admission assertions do
not qualify recovery: its fixture manually sets Failed.

**Expected values.** Preserve the literal goal
`B = "C959 delivery α\nsecond line\nEND-C959"`. Freeze final producer inputs
after workspace/refocus resolution and before handoff. Independently compose
`E = DelegationReportFormatter.BuildBrief(frozenTask, settings, resolvedReplyLimit, refocus)`;
assert E contains B ordinally and has LF-only line endings. E includes the entire
task framing and reporting contract. Never derive E from queued.Body, a spill
or a transcript. Inline `W = E.TrimEnd()` follows existing queue normalization;
internal LF, Unicode and suffix stay exact. For a spill, derive expected P using
BuildBriefPointer with E.Length, kind, the applicable measured ceiling and path,
then apply only the known message-owned path substitution; `W = P.TrimEnd()`.
Do not use FitBriefForTyping or its output as the oracle. Literal B, path and
encoding assertions independently protect what the shared framing formatter
cannot prove about itself.

| Case inside existing V-21 | Exact assertions; append vector key to each Shouldly message |
|---|---|
| ClaudeCode (all Claude tiers share this enum), local measured ModernConPty profile, E within existing ceiling, eligible/busy | `submitted.ShouldBe(W, "C959-pc-187")`; `submitted.ShouldContain(B, customMessage: "C959-v21-claude-lf")`; task-body write equals `"\u001b[200~" + W + "\u001b[201~"` (`C959-pc-214`); next task submit is a distinct `"\r"` write (`C959-pc-215`); `receipt.Text.ShouldBe(W, "C959-v21-receipt")`. No spill pointer. |
| Codex local, floor/current x Shared/Worktree x eligible/busy | `ForAgentKind(Codex).BriefInlineMaxBytes.ShouldBe(0, "C959-pc-209")`; bytes read from actual pointer-named file equal `Encoding.UTF8.GetBytes(E)` (`C959-pc-210`); decoded file contains B exactly (`C959-pc-187`); `submitted.ShouldBe(W, "C959-v21-pointer")`; LF and CR each absent (`C959-pc-212`); `receipt.Text.ShouldBe(W, "C959-v21-receipt")`. Retain one model=6.1, selected runner, zero CLI Warning/probe-auth assertions. |
| Codex remote, floor/current x eligible/busy, real runner-bound worktree | Before first terminal input, queue `RemoteSpillBody.ShouldBe(E, "C959-pc-216")`; actual runner file bytes equal UTF-8 E (`C959-pc-211`); `RemoteSpillRelativePath.ShouldBe($".antiphon/inbox/{queueId:D}.md", "C959-pc-213")`; pointer names that exact file under selected RunnerCwd. Same complete single-line W/UserPrompt and model assertions; runner/store/session remain the selected binding. |
| Grok local/remote x eligible/busy, after its existing rules-ready condition | Same zero ceiling, exact file E/B, single-line W, file-before-input and complete UserPrompt assertions as non-Claude Codex, using Grok kind. Zero Codex version queries, no invented 6.1 argument or CLI Warning. Do not bypass Grok initialization. |

Claude inline is conditional on its **existing** ceiling. The explicit local
profile models the measured modern backend at its shipped limit, as in the
inspected fixture; it is not a real PTY run. Runner-bound Claude uses the inbox
ceiling and spills if E exceeds it; its pointer may remain multiline and must
use bracketed paste plus separate Enter. Include a remote eligible/busy spill
pair with exact file E and complete W receipt; single-line PC-212 applies only
to non-Claude. Never widen the remote ceiling or shorten the real reporting
contract to force Claude inline. Claude/Grok tier x CLI-version permutations
are excluded because D-1 gates only Codex. Raw/new kinds share default-deny but
unsupported launches are not invented; specialist full-inline Check policy is
outside this amendment. Keep the complete Codex desktop/remote x floor/current
x eligible/busy matrix.

For every case, join TaskId/ExecutionTaskId -> queue Id -> selected runner/store
-> session Id + accepted generation -> original attempt baseline. Require one
complete recipient UserPrompt above the sequence floor (or original timestamp
floor if unobservable), the expected session/generation, and ordinal equality
of **whole** receipt.Text to W. Record E/W before delivery. Contains(B), a marker,
normalized whitespace, Sent, an Input ack or file existence alone cannot pass.
Normalization only removes framing line-ending differences and trailing whitespace;
never flatten E or normalize away LF inside B.

Exact file E plus complete pointer UserPrompt proves the brief was made available
in the recipient workspace and its pointer submitted. It cannot prove the
provider read the file or replied. Under CARD-0353, no assistant response after
a confirmed pointer is a provider stall, never permission to retype the brief.
Do not require or fabricate an inline E transcript for the non-Claude cases.

Missing setup for Code: extend only A's nested kit/factory for kind/runner,
retained schema/workspace, the existing modern Claude profile, RemoteSpillCourier,
attempt/generation baselines and busy activity after TurnEnd. Extend the test-local
remote adapter through the real phone-home client, framed peer and
PhoneHomeCommandDispatcher Input branch, with RunnerWorkspaceService rooted in
a temporary mirror. The peer's bare `{ok:true}` default is insufficient. A
test-local IPhoneHomeRuntimeSurface may model composer, launch/snapshot and transcript;
its UserPrompt is generated only by actually submitted input. Read the file in
its input callback **before** accepting pointer bytes; never write the file in
OnSubmitted. Pull that transcript through the real Transcript operation/catch-up.
Do not prepopulate expected E/W as successful receipts. Keep shared helpers and
production spill/queue files unchanged. V-21/V-22 stay two single [Test] methods
with internal vectors; no additional TUnit results.

Assertion order is part of the executable freeze. Run the Claude inline vector
first: check CR absence/separate Enter (PC-215), then full submitted W (PC-187),
then bracketed wrapper (PC-214). In local spill vectors check file bytes (PC-210)
before the duplicate literal-B assertion. At remote first input check file
**existence** (PC-217) before exact bytes (PC-211); a missing file is a recorded
false result, never an unlabelled IO exception. Check busy persisted path/bytes
(PC-213/216) before releasing delivery. Place the kind-ceiling (PC-209) and
pointer line-shape (PC-212) assertions before incidental output comparisons.
In PC-218/222/223/224 negative vectors the decisive stored-state assertion is
`row.RemoteSpillBody.ShouldBe(E, "C959-pc-NNN")`, followed by zero qualifying
receipt; a screen-only Sent/Delivered flag alone is deliberately inconclusive.
These orders keep each compiling defect red at its named assertion rather than
at a shared earlier assertion or fixture exception. PCs that temporarily mutate
excluded queue/spill code do so only in commissioned post-land Mutation; they
do not authorize Code to edit those owner areas.

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
| Create/retry -> durable Queued task -> dispatcher -> launch queue or reused-session message queue -> recipient | TaskId -> AgentSessionId + accepted generation -> SessionQueuedMessage.Id/ExecutionTaskId + E/W, spill path/bytes and transcript baseline; task/queue rows persist, launch work item is volatile | V-17 proves refusal before claim/prep/input. V-21/V-22 use real AgentSessionLaunchQueue and SessionMessageQueueService, then require complete matching W UserPrompt in the isolated DB (remote transcript pull), plus actual file bytes E for spilled input. Queue insertion, Dispatched, Sent, StartedArgs and ack alone are insufficient. |

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
already exist. Refresh CLI evidence before a new or explicitly retried dispatch;
repeat those admission vectors with downgraded or expired evidence and assert zero
new input. This is not a new gate on every flush of an already-authorized queue
row. Preserve already delivered work.
Where the existing failure path terminalizes the task, require its recorded
failure then **explicit RetryAsync**; do not invent automatic task retry.
For a claim committed before a lost launch/enqueue, use the existing
LandDeliveryBoundary's dispatch-warning-claim-committed seam, retain the database,
recreate the graph, advance past DeliveryFailTimeoutMinutes and call the real
FailNeverStartedAsync backstop with an empty isolated recipient transcript.
Assert its durable failure before retrying. Before-commit faults use the matching
dispatch-warning-claim-before-commit seam. Neither test manually marks a task
Failed to skip the recovery being proved. SourceTaskId is used by completion notes;
dispatch briefs here are joined through ExecutionTaskId and their full task marker.

For enqueue failure, no recipient receipt is allowed before explicit recovery.
For a crash after input but before confirmation, seed no receipt on the producer:
the recipient callback/pull persists actual complete W; recovery finds that
receipt without duplicate submission. Match whole W, task marker, session,
generation and sequence above the attempt baseline. E keeps literal LF/Unicode/
suffix B. Inline W carries E; non-Claude W is the single-line pointer to exact E.
A clipped/wrong-session/old-generation transcript cannot satisfy the assertion.
The ee18a226 table supersedes the inline-only LF demand, including PC-187/194.

Additional spill handoffs: staged E -> persisted queue-owned E/path -> runner
file -> terminal W -> complete UserPrompt -> release queue spill bytes. Fault
before and after each handoff. Before queue persistence, use the existing claim/
backstop/explicit RetryAsync rule. After persistence, discard the staging courier
by recreating DI and recover E from the row. An ack or screen-only Delivered
retains E until complete W UserPrompt. On runner write failure require zero input
and no receipt; after file write/before input, restart and rewrite identical bytes
to the same owned path. After input/before status-save, pull the actual transcript
and late-confirm with zero further body submissions. File contents remain E after
RemoteSpillBody is cleared. Manual Failed assignment and inserted queue rows
cannot substitute for recovery. PC-216..224 cover these independent guards.

Run before/after handoff faults on desktop and remote Codex with floor/current
evidence and eligible/busy recipients, retaining Shared/Worktree success coverage.
Repeat new/explicit retry admission after downgrade/expiry with zero new input
(V-17/V-22); no new per-flush gate. Claude/Grok success vectors cover their
distinct encoding; do not multiply the shared persistence/replay fault matrix by
ungated kinds. Grok initialization failures, filesystem containment attacks and
specialist input remain separate owners; their unmet prerequisites confer no
success. Every recovery receipt uses this same E/W contract.

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
| V-21 | A / C959_Compatible_launch_keeps_model_and_runner | Real producer/launch/message queues; desktop/remote x floor/current x eligible/busy and recovery boundaries; Codex one model=6.1 and original runner; Claude inline LF or exact spill E plus complete pointer W receipt per amendment; no Warning/probe auth | C959-v21-receipt |
| V-22 | A / C959_Retry_and_cancellation_recheck | Real RetryAsync after durable recovery plus amended E/W recipient receipt; wrong/stale/old evidence leaves status/event rows unchanged; expired override no permission; caller cancellation leaves no task/claim/session, deadline is unknown; recovery reruns admission | C959-v22-state |

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
These freeze the new settings member names; normal case-insensitive configuration binding is permitted without broadening this permission.
Test empty array; wrong runner/model/code separately; "*" in each scope;
empty reason; duplicate exact tuple; invalid code; missing/invalid UTC expiry;
lifetime 24h accepted versus 24h+tick rejected; expiry at T and T-1tick;
and advancing beyond expiry without reloading settings. Also test the other two
CLI codes with individually matching entries. No override is applied to live config.

Parser fixture roster, in addition to the table: codex-cli 0.9.0, 0.1000.0,
0.159.1-beta.1, 0.160.0-beta.1, 0.159.1+build.7,
0.159.1-beta.2 versus beta.10; banner aliases "codex 0.160.0", "codex version 0.160.0" and
"codex-cli version v0.160.0" (the inspected regex permits case-insensitive banners). Accept no trailing terminator, one LF or one CRLF.
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
The single probe I/O seam must record its executable/package resolution reads as
well as process starts. Give its test implementation a fail-on-open audit for the
synthetic auth.json; never read the real inherited home. The guard's PC adds that
open through the same boundary and fails the zero-auth-open assertion. Code/Review
must check that all probe-owned external reads actually use that boundary: the
audit cannot detect an implementation that secretly bypasses it with raw file I/O.
Run descriptor validation before process start; oversized/secret-placeholder
resolution fields and unsupported loader overrides return unknown with starts=0.
Use 65,537 ASCII characters for each oversized executable/cwd/PATH input, one
field at a time with the others valid; NUL and the literal ${secret:C959} are
separate invalid vectors. These are guaranteed-invalid payloads, not invented
claims of a baseline field limit. D-4 leaves per-field numeric ceilings to
implementation, so equality at an invented baseline ceiling is not a frozen
acceptance condition. Code records its chosen finite limits and adds equality/+1
vectors within V-13 before its CP; method count and admission behavior stay fixed.

#### Harmless child and missing setup

S1 must materialize the optional Fixtures/CodexVersionChild.ps1 from literal
source, or embed the same literal in CodexCliVersionTestFixture; locate it from
the repository root if file-backed, since the csproj does not copy arbitrary ps1.
It has only fixed modes: success emits "codex-cli 0.160.0" and exits 0;
nonzero emits that line and exits 1; stderr adds one fixed stderr byte;
stdout-flood and stderr-flood write 4097 bytes to their respective streams;
stdin waits for EOF, timeout waits for cleanup, and tree starts one leaf child
that holds inherited pipes. The leaf waits for parent cleanup. Use ProcessStartInfo.ArgumentList with the
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

The harmless-child literal to implement in the new fixture is:

~~~powershell
param(
    [ValidateSet('success','nonzero','stderr','stdout-flood','stderr-flood','stdin','timeout','tree','leaf')]
    [string]$Mode = 'success',
    [Parameter(Mandatory=$true)][string]$ReceiptRoot
)
$ErrorActionPreference = 'Stop'
if (-not [IO.Directory]::Exists($ReceiptRoot)) { throw 'missing owned receipt root' }
$ownedProcess = [Diagnostics.Process]::GetCurrentProcess()
$receipt = @{
    pid = $PID
    startedUtc = $ownedProcess.StartTime.ToUniversalTime().ToString('O')
    mode = $Mode
} | ConvertTo-Json -Compress
[IO.File]::WriteAllText((Join-Path $ReceiptRoot ($Mode + '.json')), $receipt)
if ($Mode -eq 'leaf' -or $Mode -eq 'timeout') {
    [Threading.Thread]::Sleep(20000)
    exit 0
}
if ($Mode -eq 'tree') {
    $shellName = if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' }
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $PSHOME $shellName))
    $start.UseShellExecute = $false
    foreach ($item in @('-NoLogo','-NoProfile','-NonInteractive','-File',
                         $PSCommandPath,'-Mode','leaf','-ReceiptRoot',$ReceiptRoot)) {
        $start.ArgumentList.Add($item)
    }
    $leaf = [Diagnostics.Process]::Start($start)
    try {
        $readyPath = Join-Path $ReceiptRoot 'leaf.json'
        $readyLimit = [DateTime]::UtcNow.AddSeconds(5)
        while (-not [IO.File]::Exists($readyPath) -and [DateTime]::UtcNow -lt $readyLimit) {
            [Threading.Thread]::Sleep(10)
        }
        if (-not [IO.File]::Exists($readyPath)) { throw 'leaf did not become ready' }
        [IO.File]::WriteAllText((Join-Path $ReceiptRoot 'ready'), 'ready')
        [Console]::Out.WriteLine('codex-cli 0.160.0')
        $leaf.WaitForExit()
    }
    finally {
        if (-not $leaf.HasExited) { $leaf.Kill($true) }
        $leaf.WaitForExit()
        $leaf.Dispose()
    }
    exit 0
}
if ($Mode -eq 'stdout-flood') { [Console]::Out.Write(('A' * 4097)); exit 0 }
if ($Mode -eq 'stderr-flood') { [Console]::Error.Write(('E' * 4097)); exit 0 }
if ($Mode -eq 'stdin') {
    $inputBody = [Console]::In.ReadToEnd()
    if ($inputBody.Length -ne 0) { throw 'probe stdin carried data' }
    [IO.File]::WriteAllText((Join-Path $ReceiptRoot 'stdin-eof'), 'eof')
}
[Console]::Out.WriteLine('codex-cli 0.160.0')
if ($Mode -eq 'stderr') { [Console]::Error.Write('E') }
if ($Mode -eq 'nonzero') { exit 1 }
exit 0
~~~

The parent test, not the killed tree process's finally, is the independent rescue
owner. It checks both receipt identities and waits for both PIDs to exit, even when
the production cleanup mutant kills only the parent. Expected tree output is one
F-current line followed by timeout/cancellation, never success. Other output modes
have exit/stdout/stderr bytes exactly as shown. Failed-start and unconfirmed-reaper
vectors are injected I/O results; a real child is still used for tree ownership.

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
The amendment keeps those seven filters/build paths/minima: **delta 0 feature
methods and 0 feature results**. CP-4's estimate grows 10 -> 14 minutes for
encoding/file/receipt and recovery vectors. CP-8 explicitly carries the Code
report's required Final whole Unit lane: 3942 executed results plus 19 restored
jq-dependent cases = **3961**; the 33 Windows-only cases are separate exclusions.
Current Final total floor is **4127 executions = 166 + 3961**.
The added Unit row does not alter any frozen V/R method or discharge V/R by Unit.
Keep the single-result vector layout; any Code parameterization requires a
documented recount and importer receipt rather than silently inflating the floor.

### Guard inventory

Every row below is a distinct guard and maps 1:1 to its identically numbered PC.
The plan references are D-n; F-n retains the historical 22-family association.
Each multi-condition family has been split. A shared implementation predicate
may have several input vectors, but no single mutation stands in for another
independently bypassable predicate. Newly frozen vector details in this inventory
are additional internal cases of the same V method, not additional TUnit results.

| Guard | Plan reference and invariant | Positive control |
|---|---|---|
| G-1 | D-1; Numeric core ordering | PC-1 |
| G-2 | D-1; Prerelease below release | PC-2 |
| G-3 | D-1; Numeric prerelease ordering | PC-3 |
| G-4 | D-1; Build metadata does not order | PC-4 |
| G-5 | D-1; Exactly three numeric components | PC-5 |
| G-6 | D-1; Overflow rejected | PC-6 |
| G-7 | D-1; Leading zero core rejected | PC-7 |
| G-8 | D-1; Leading zero prerelease rejected | PC-8 |
| G-9 | D-1; Identifier grammar strict | PC-9 |
| G-10 | D-1; No empty suffix identifier | PC-10 |
| G-11 | D-1; One whole output record | PC-11 |
| G-12 | D-1; Anchored banner only | PC-12 |
| G-13 | D-1; No trailing embedded text | PC-13 |
| G-14 | D-2; Version-only argv | PC-14 |
| G-15 | D-2; Profile VersionArguments never executed | PC-15 |
| G-16 | D-2; No shell command evaluation | PC-16 |
| G-17 | D-2; Credential environment absent | PC-17 |
| G-18 | D-2; Antiphon tokens absent | PC-18 |
| G-19 | D-2; Proxy environment absent | PC-19 |
| G-20 | D-2; Node injection absent | PC-20 |
| G-21 | D-2; Startup hook environment absent | PC-21 |
| G-22 | D-2; Fresh empty home | PC-22 |
| G-23 | D-2; Neutral execution cwd | PC-23 |
| G-24 | D-2; Closed stdin | PC-24 |
| G-25 | D-2; Probe-owned I/O never opens authentication | PC-25 |
| G-26 | D-2; No raw diagnostic exposure | PC-26 |
| G-27 | D-2; Missing executable unknown | PC-27 |
| G-28 | D-2; Nonzero exit refused | PC-28 |
| G-29 | D-2; Stderr refused | PC-29 |
| G-30 | D-2; Stdout bounded | PC-30 |
| G-31 | D-2; Stderr bounded | PC-31 |
| G-32 | D-2; Five-second execution deadline | PC-32 |
| G-33 | D-2; Owned descendant termination | PC-33 |
| G-34 | D-2; Two-second cleanup deadline | PC-34 |
| G-35 | D-2; Unconfirmed cleanup not success | PC-35 |
| G-36 | D-2; Failed cleanup stays owned | PC-36 |
| G-37 | D-2; Cancellation never success | PC-37 |
| G-38 | D-2; Initial attempt precedes registration | PC-38 |
| G-39 | D-2; Failed attempt clears old value | PC-39 |
| G-40 | D-2; Attempt time is completion time | PC-40 |
| G-41 | D-2; Reads never renew sample | PC-41 |
| G-42 | D-2; Periodic refresh occurs | PC-42 |
| G-43 | D-2; Identical requests single flight | PC-43 |
| G-44 | D-2; All child probes serialized | PC-44 |
| G-45 | D-2; Cache capacity bounded | PC-45 |
| G-46 | D-2; Busy admission bounded | PC-46 |
| G-47 | D-2; Executable metadata invalidates | PC-47 |
| G-48 | D-2; Mtime invalidates | PC-48 |
| G-49 | D-2; Boot invalidates cache | PC-49 |
| G-50 | D-3; Local producer carries version | PC-50 |
| G-51 | D-3; Local producer carries attempt time | PC-51 |
| G-52 | D-3; Local producer carries error | PC-52 |
| G-53 | D-3; Local producer carries fingerprint | PC-53 |
| G-54 | D-3; Registration carries version | PC-54 |
| G-55 | D-3; Registration carries attempt time | PC-55 |
| G-56 | D-3; Registration carries error | PC-56 |
| G-57 | D-3; Registration carries fingerprint | PC-57 |
| G-58 | D-3; Local feature advertisement | PC-58 |
| G-59 | D-3; Remote feature advertisement | PC-59 |
| G-60 | D-2; GET starts no process | PC-60 |
| G-61 | D-2/D-4; Sibling node precedence | PC-61 |
| G-62 | D-2/D-4; Stock shim not executed | PC-62 |
| G-63 | D-2/D-4; PATH fallback when sibling absent | PC-63 |
| G-64 | D-2/D-4; Arguments preserve path boundaries | PC-64 |
| G-65 | D-4; Explicit native selector honored | PC-65 |
| G-66 | D-4; Direct-node js prefix retained | PC-66 |
| G-67 | D-4; Relative js uses descriptor cwd | PC-67 |
| G-68 | D-2; Windows real child cleanup | PC-68 |
| G-69 | D-2/D-4; Missing node never falls back to codex | PC-69 |
| G-70 | D-2/D-4; Missing js never falls back | PC-70 |
| G-71 | D-2/D-4; Missing native package refused | PC-71 |
| G-72 | D-2/D-4; Nonstock wrapper not trusted | PC-72 |
| G-73 | D-2/D-4; Unrepresentable loader override refused | PC-73 |
| G-74 | D-3; Heartbeat carries snapshot | PC-74 |
| G-75 | D-3; Heartbeat preserves attempt time | PC-75 |
| G-76 | D-3; Failed heartbeat clears success | PC-76 |
| G-77 | D-3; Missing legacy heartbeat not refresh | PC-77 |
| G-78 | D-3; Older completed attempt ignored | PC-78 |
| G-79 | D-3; Writer contention delivers complete sample | PC-79 |
| G-80 | D-3; Reconnect republishes latest snapshot | PC-80 |
| G-81 | D-5; Age upper boundary closed | PC-81 |
| G-82 | D-5; Timestamp required | PC-82 |
| G-83 | D-5; Future skew bounded | PC-83 |
| G-84 | D-5; Tolerated future age clamped | PC-84 |
| G-85 | D-5; Failed refresh cannot use old success | PC-85 |
| G-86 | D-5; Stale refresh remains stale | PC-86 |
| G-87 | D-5; One refresh budget only | PC-87 |
| G-88 | D-5; Configured maximum age lower bound | PC-88 |
| G-89 | D-5; Configured maximum age upper bound | PC-89 |
| G-90 | D-5; Refresh below admission age | PC-90 |
| G-91 | D-5; Error defeats otherwise good version | PC-91 |
| G-92 | D-3; Accepted boot change clears old sample | PC-92 |
| G-93 | D-3; Accepted epoch change clears old sample | PC-93 |
| G-94 | D-3; Accepted store replacement clears old sample | PC-94 |
| G-95 | D-3; Omitted capabilities do not inherit CLI | PC-95 |
| G-96 | D-3; Old epoch heartbeat ignored | PC-96 |
| G-97 | D-3; Old epoch probe reply ignored | PC-97 |
| G-98 | D-3; Rejected registration leaves current sample | PC-98 |
| G-99 | D-8; Build SHA is not CLI version | PC-99 |
| G-100 | D-8; Catalogue values per runner | PC-100 |
| G-101 | D-8; Status uses latest sample | PC-101 |
| G-102 | D-8; Offline placeholder unknown | PC-102 |
| G-103 | D-8; Unobserved stale indicator null | PC-103 |
| G-104 | D-8; Expired indicator independent of liveness | PC-104 |
| G-105 | D-8; Fresh indicator false | PC-105 |
| G-106 | D-8; Private fingerprint excluded from public API | PC-106 |
| G-107 | D-8; Unknown runner remains 404 | PC-107 |
| G-108 | D-4; Probe routed to selected remote | PC-108 |
| G-109 | D-4; Response request correlation | PC-109 |
| G-110 | D-4; Response fingerprint binding | PC-110 |
| G-111 | D-4; Response runner binding | PC-111 |
| G-112 | D-4; Old operation unsupported unknown | PC-112 |
| G-113 | D-4; Interface default unknown | PC-113 |
| G-114 | D-4; Eight-second owner bound | PC-114 |
| G-115 | D-4; No transport retry | PC-115 |
| G-116 | D-4; Caller cancellation propagates | PC-116 |
| G-117 | D-4; Descriptor bounds before process | PC-117 |
| G-118 | D-4; No secrets in resolution descriptor | PC-118 |
| G-119 | D-4; New phone-home operation not renumbered | PC-119 |
| G-120 | D-4; Actual route registered | PC-120 |
| G-121 | D-4; Probe never holds task claim | PC-121 |
| G-122 | D-4; Failed request enqueue not success | PC-122 |
| G-123 | D-4; Lost response not success | PC-123 |
| G-124 | D-1; 6.1 entry owns floor | PC-124 |
| G-125 | D-1; Unrelated models unenforced | PC-125 |
| G-126 | D-1; Exact lookup shares metadata | PC-126 |
| G-127 | D-1; Public alias strings preserved | PC-127 |
| G-128 | D-6; Unknown fails closed at create | PC-128 |
| G-129 | D-6; One patch below floor refused | PC-129 |
| G-130 | D-6; Stale refusal distinct | PC-130 |
| G-131 | D-6; Refusal before task insertion | PC-131 |
| G-132 | D-6; Problem details safe exact facts | PC-132 |
| G-133 | D-6; No alternate-runner version search | PC-133 |
| G-134 | D-6; Model hold precedence | PC-134 |
| G-135 | D-6; Provider sign-in precedence | PC-135 |
| G-136 | D-6; Unknown auth remains admissible | PC-136 |
| G-137 | D-6; Auth flag not CLI override | PC-137 |
| G-138 | D-6; Model flag not CLI override | PC-138 |
| G-139 | D-6; Dispatch rechecks version | PC-139 |
| G-140 | D-6; Preflight before workspace prep | PC-140 |
| G-141 | D-6; Bad queued evidence records Blocked event | PC-141 |
| G-142 | D-6; No automatic retry after block | PC-142 |
| G-143 | D-6; Existing active sessions untouched | PC-143 |
| G-144 | D-6; Redirect runner rechecked | PC-144 |
| G-145 | D-6; Chain rewalk model rechecked | PC-145 |
| G-146 | D-6; Late launch identity change refused | PC-146 |
| G-147 | D-6; Exact model precedes tier | PC-147 |
| G-148 | D-6; Older exact model not gated by High | PC-148 |
| G-149 | D-6; Blank argument retains unsupported exact refusal | PC-149 |
| G-150 | D-6; Opaque wrapper-owned model not guessed | PC-150 |
| G-151 | D-6; Live model snapshot checked | PC-151 |
| G-152 | D-6; Profile revision invalidates admission | PC-152 |
| G-153 | D-4; Selected install owns evidence | PC-153 |
| G-154 | D-4; Selected newer install usable | PC-154 |
| G-155 | D-4; Profile validation cache not version authority | PC-155 |
| G-156 | D-4; PATH binding retained | PC-156 |
| G-157 | D-4; PATHEXT binding retained | PC-157 |
| G-158 | D-4; Future relative cwd conservative | PC-158 |
| G-159 | D-4; Remote executable projection first | PC-159 |
| G-160 | D-7; Override runner exact | PC-160 |
| G-161 | D-7; Override model exact | PC-161 |
| G-162 | D-7; Override refusal-code exact | PC-162 |
| G-163 | D-7; Override expiry per use | PC-163 |
| G-164 | D-7; No wildcard runner config | PC-164 |
| G-165 | D-7; No wildcard model config | PC-165 |
| G-166 | D-7; No wildcard code config | PC-166 |
| G-167 | D-7; Reason mandatory | PC-167 |
| G-168 | D-7; Expiry mandatory/UTC | PC-168 |
| G-169 | D-7; Maximum lifetime 24h | PC-169 |
| G-170 | D-7; Duplicate tuple config refused | PC-170 |
| G-171 | D-7; Allowed codes validated | PC-171 |
| G-172 | D-7; Create exception audited | PC-172 |
| G-173 | D-7; Dispatch exception audited | PC-173 |
| G-174 | D-7; Override never relabels sample | PC-174 |
| G-175 | D-7; Override cannot bypass hold | PC-175 |
| G-176 | D-7; Override cannot bypass sign-in | PC-176 |
| G-177 | D-7; Override cannot bypass drain | PC-177 |
| G-178 | D-7; Override cannot bypass retirement | PC-178 |
| G-179 | D-7; Override cannot bypass platform | PC-179 |
| G-180 | D-7; Override cannot bypass capacity | PC-180 |
| G-181 | D-7; Override cannot bypass launch policy | PC-181 |
| G-182 | D-7; Override cannot bypass profile restriction | PC-182 |
| G-183 | D-6; Exact minimum accepted | PC-183 |
| G-184 | D-6; No model downgrade | PC-184 |
| G-185 | D-6; Selected runner retained | PC-185 |
| G-186 | D-6; Producer actually enqueues the correct wire representation | PC-186 |
| G-187 | D-6; Whole logical brief survives fitting, inline or spilled | PC-187 |
| G-188 | D-6; Busy recipient preserves work | PC-188 |
| G-189 | D-6; No spurious override audit | PC-189 |
| G-190 | D-6; Launch handoff actually occurs | PC-190 |
| G-191 | D-6; Interrupted dispatch cannot count as receipt | PC-191 |
| G-192 | D-6; Recipient identity preserved across retry | PC-192 |
| G-193 | D-6; Retry rechecks current evidence | PC-193 |
| G-194 | D-6; Retry repair reaches recipient | PC-194 |
| G-195 | D-6; Caller cancellation leaves no new task | PC-195 |
| G-196 | D-6; Owned deadline fails unknown | PC-196 |
| G-197 | D-7; Retry checks current override expiry | PC-197 |
| G-198 | D-4; Retry old client fails unknown | PC-198 |
| G-199 | D-2; Probe DTO never publishes raw diagnostic | PC-199 |
| G-200 | D-5; Exactly maximum age remains admissible | PC-200 |
| G-201 | D-3; Live foreign store still refused | PC-201 |
| G-202 | D-3; Store replacement waits full lease | PC-202 |
| G-203 | D-3; Retired slot cannot register | PC-203 |
| G-204 | D-6; Warm reuse checks CLI before input | PC-204 |
| G-205 | D-4; Resolution cwd participates in identity | PC-205 |
| G-206 | D-4; Selected node package owns identity and probe | PC-206 |
| G-207 | D-3; Store replacement waits a full lease after disconnect | PC-207 |
| G-208 | D-3; Store replacement requires explicit retirement clear | PC-208 |
| G-209 | D-6 delivery preservation; Non-Claude inline ceiling remains zero | PC-209 |
| G-210 | D-6 delivery preservation; Local spill keeps exact logical bytes | PC-210 |
| G-211 | D-6 delivery preservation; Runner spill keeps exact logical bytes | PC-211 |
| G-212 | D-6 delivery preservation; Non-Claude pointer is single-line | PC-212 |
| G-213 | D-6 delivery preservation; Pointer binds the message-owned runner path | PC-213 |
| G-214 | D-6 delivery preservation; Inline multiline body retains bracketed-paste wrapper | PC-214 |
| G-215 | D-6 delivery preservation; Submit Enter is a separate write | PC-215 |
| G-216 | D-6 recovery; Remote spill bytes persist before input | PC-216 |
| G-217 | D-6 recovery; Runner writes spill before accepting pointer input | PC-217 |
| G-218 | D-6 recovery; Screen/ack alone cannot release durable spill bytes | PC-218 |
| G-219 | D-6 recovery; Restart retrieves exact spill from the durable queue row | PC-219 |
| G-220 | D-6 recovery; Complete receipt prevents duplicate body submission | PC-220 |
| G-221 | D-6 recovery; Lost committed launch reaches real durable failure before explicit retry | PC-221 |
| G-222 | D-6 receipt; Head-only pointer transcript is not complete delivery | PC-222 |
| G-223 | D-6 receipt; Prior-attempt sequence cannot confirm current delivery | PC-223 |
| G-224 | D-6 receipt; Other session's complete transcript cannot confirm delivery | PC-224 |

### Positive controls

**224 independent variants in 22 families.** Each row changes production behavior
by one compiling defect, leaves the other guards intact, and selects exactly the
named method with /*/*/Class/Method (MinExecuted=1). A label below must be attached
to the direct outcome assertion inside that method. Preserve the correct fixture
even if a mutant would otherwise make setup take a different branch; use the
smallest seam necessary to reach the guarded outcome. Never mutate the assertion,
fixture expectation, a live CLI installation or the independent child rescue.
Capture production-call exceptions as outcomes, then assert with the listed label;
an unexpected admission refusal is a labeled behavioral failure, not fixture setup.
For receipt vectors, use a bounded observation returning false at its deadline and
assert full receipt with that label, rather than throwing an unlabeled wait timeout.
The V table's primary labels identify the initial contract assertion; the per-PC
labels identify the specific vector's first detecting assertion within that method.
Implementation symbols are introduced by S1-S4; mutations below identify their
responsibility and exact replacement, rather than inventing existing line numbers.

Mutation runs baseline/break/red/restore/green after land; Code runs ordinary V/R;
Review judges the pending design before land. A red is one executed method failing
the named assertion, not a compile/setup failure, timeout of the test harness,
zero-test filter or missing fixture. SourceLanding evidence/restoration stays
external and every child remains locally inherited. The listed version/delivery
mutants target the scoped producer/admission code; no queue/attention production
repair or shared-helper rewrite is authorized by this matrix.

| PC | Family; exact method | Compiling defect; fixture -> first red assertion |
|---|---|---|
| PC-1 | F-1; CodexCliVersionProbeTests/C959_Parses_and_orders_versions | Replace numeric component comparison with ordinal string comparison. Input: 0.9.0 versus 0.159.1. **C959-pc-001**: comparison < 0. |
| PC-2 | F-1; CodexCliVersionProbeTests/C959_Parses_and_orders_versions | Ignore prerelease when cores match. Input: 0.159.1-beta.1 versus floor. **C959-pc-002**: comparison < 0. |
| PC-3 | F-1; CodexCliVersionProbeTests/C959_Parses_and_orders_versions | Compare numeric prerelease identifiers as strings. Input: beta.2 versus beta.10. **C959-pc-003**: comparison < 0. |
| PC-4 | F-1; CodexCliVersionProbeTests/C959_Parses_and_orders_versions | Compare build suffix as a final ordering component. Input: 0.159.1+build.7 versus floor. **C959-pc-004**: comparison = 0. |
| PC-5 | F-1; CodexCliVersionProbeTests/C959_Parses_and_orders_versions | Accept two-component output by appending zero. Input: codex-cli 0.159. **C959-pc-005**: version null. |
| PC-6 | F-1; CodexCliVersionProbeTests/C959_Parses_and_orders_versions | Clamp overflowing major to Int32.MaxValue. Input: codex-cli 2147483648.0.0. **C959-pc-006**: version null. |
| PC-7 | F-1; CodexCliVersionProbeTests/C959_Parses_and_orders_versions | Remove core leading-zero check. Input: codex-cli 00.159.1. **C959-pc-007**: version null. |
| PC-8 | F-1; CodexCliVersionProbeTests/C959_Parses_and_orders_versions | Remove numeric prerelease leading-zero check. Input: codex-cli 0.159.1-beta.01. **C959-pc-008**: version null. |
| PC-9 | F-1; CodexCliVersionProbeTests/C959_Parses_and_orders_versions | Allow underscore in identifier predicate. Input: codex-cli 0.159.1-bad_id. **C959-pc-009**: version null. |
| PC-10 | F-1; CodexCliVersionProbeTests/C959_Parses_and_orders_versions | Skip empty-identifier rejection. Input: codex-cli 0.159.1+build..7. **C959-pc-010**: version null. |
| PC-11 | F-1; CodexCliVersionProbeTests/C959_Parses_and_orders_versions | Parse only first line. Input: two F-current records. **C959-pc-011**: version null. |
| PC-12 | F-1; CodexCliVersionProbeTests/C959_Parses_and_orders_versions | Remove start anchor / allow prefix substring match. Input: prefix codex-cli 0.160.0. **C959-pc-012**: version null. |
| PC-13 | F-1; CodexCliVersionProbeTests/C959_Parses_and_orders_versions | Remove end anchor. Input: codex-cli 0.160.0 extra. **C959-pc-013**: version null. |
| PC-14 | F-2; CodexCliVersionProbeTests/C959_Probe_is_version_only_and_auth_free | Append --model and gpt-6.1-sol to production probe args. Input: native descriptor A. **C959-pc-014**: args exactly [--version]. |
| PC-15 | F-2; CodexCliVersionProbeTests/C959_Probe_is_version_only_and_auth_free | Copy profile VersionArguments into probe args. Input: profile VersionArguments=[login]. **C959-pc-015**: args exactly [--version]. |
| PC-16 | F-2; CodexCliVersionProbeTests/C959_Probe_is_version_only_and_auth_free | Set UseShellExecute=true. Input: native descriptor A captured before start. **C959-pc-016**: UseShellExecute false. |
| PC-17 | F-2; CodexCliVersionProbeTests/C959_Probe_is_version_only_and_auth_free | Copy synthetic OPENAI_API_KEY into child environment. Input: credential sentinel. **C959-pc-017**: key absent. |
| PC-18 | F-2; CodexCliVersionProbeTests/C959_Probe_is_version_only_and_auth_free | Copy synthetic ANTIPHON_TASK_TOKEN. Input: task-token sentinel. **C959-pc-018**: key absent. |
| PC-19 | F-2; CodexCliVersionProbeTests/C959_Probe_is_version_only_and_auth_free | Copy synthetic HTTPS_PROXY. Input: proxy sentinel. **C959-pc-019**: key absent. |
| PC-20 | F-2; CodexCliVersionProbeTests/C959_Probe_is_version_only_and_auth_free | Copy synthetic NODE_OPTIONS. Input: parent-only loader sentinel. **C959-pc-020**: key absent. |
| PC-21 | F-2; CodexCliVersionProbeTests/C959_Probe_is_version_only_and_auth_free | Copy synthetic BASH_ENV. Input: parent-only startup sentinel. **C959-pc-021**: key absent. |
| PC-22 | F-2; CodexCliVersionProbeTests/C959_Probe_is_version_only_and_auth_free | Use parent CODEX_HOME instead of created scratch home. Input: synthetic parent auth.json. **C959-pc-022**: child home differs and is empty. |
| PC-23 | F-2; CodexCliVersionProbeTests/C959_Probe_is_version_only_and_auth_free | Use descriptor resolution cwd as execution cwd. Input: cwd with synthetic config. **C959-pc-023**: actual child cwd equals neutral scratch. |
| PC-24 | F-2; CodexCliVersionProbeTests/C959_Probe_is_version_only_and_auth_free | Leave redirected stdin open after start. Input: fixture reads to EOF. **C959-pc-024**: EOF observed before output. |
| PC-25 | F-2; CodexCliVersionProbeTests/C959_Probe_is_version_only_and_auth_free | Add an auth.json open through the probe I/O boundary before starting the child. Input: synthetic parent CODEX_HOME/auth.json with read-audit hook. **C959-pc-025**: auth-file open count 0. |
| PC-26 | F-2; CodexCliVersionProbeTests/C959_Probe_is_version_only_and_auth_free | Log captured stderr/output before replacing it with reason. Input: synthetic output sentinel. **C959-pc-026**: captured logs contain no sentinel. |
| PC-27 | F-3; CodexCliVersionProbeTests/C959_Failure_bounds_and_cleanup | Return floor success when start reports missing. Input: missing selected A, good B on PATH. **C959-pc-027**: version null and executable_missing. |
| PC-28 | F-3; CodexCliVersionProbeTests/C959_Failure_bounds_and_cleanup | Remove exit-code success predicate. Input: F-current stdout with exit 1. **C959-pc-028**: version null and nonzero_exit. |
| PC-29 | F-3; CodexCliVersionProbeTests/C959_Failure_bounds_and_cleanup | Remove empty-stderr predicate. Input: F-current plus stderr byte. **C959-pc-029**: version null and stderr_output. |
| PC-30 | F-3; CodexCliVersionProbeTests/C959_Failure_bounds_and_cleanup | Raise stdout cap from 4096 to 8192. Input: 4097-byte stdout beginning with valid banner. **C959-pc-030**: stdout truncation true and version null. |
| PC-31 | F-3; CodexCliVersionProbeTests/C959_Failure_bounds_and_cleanup | Raise stderr cap from 4096 to 8192. Input: 4097-byte stderr. **C959-pc-031**: stderr truncation true and version null. |
| PC-32 | F-3; CodexCliVersionProbeTests/C959_Failure_bounds_and_cleanup | Change deadline to six seconds. Input: ready held child; clock advance 5s. **C959-pc-032**: operation has entered cleanup at 5s. |
| PC-33 | F-3; CodexCliVersionProbeTests/C959_Failure_bounds_and_cleanup | Replace process-tree kill with parent-only kill. Input: ready tree with pipe-holding leaf. **C959-pc-033**: leaf PID/start no longer alive. |
| PC-34 | F-3; CodexCliVersionProbeTests/C959_Failure_bounds_and_cleanup | Change cleanup deadline to three seconds. Input: held pipe cleanup; clock +2s. **C959-pc-034**: unknown completion at cleanup bound. |
| PC-35 | F-3; CodexCliVersionProbeTests/C959_Failure_bounds_and_cleanup | Ignore cleanup-confirmed flag in success predicate. Input: F-current plus cleanup failure. **C959-pc-035**: version null and cleanup_unconfirmed. |
| PC-36 | F-3; CodexCliVersionProbeTests/C959_Failure_bounds_and_cleanup | Remove retention in probe reaper on unconfirmed cleanup. Input: synthetic kill refusal then release. **C959-pc-036**: owned cleanup count remains 1 until exit. |
| PC-37 | F-3; CodexCliVersionProbeTests/C959_Failure_bounds_and_cleanup | Convert cancelled process result into parsed stdout success. Input: cancel after F-current before exit. **C959-pc-037**: version not successful. |
| PC-38 | F-4; CodexCliVersionProbeTests/C959_Refresh_replaces_evidence | Complete startup-ready signal before awaited initial probe. Input: held initial attempt. **C959-pc-038**: advertisement remains blocked until completion/budget. |
| PC-39 | F-4; CodexCliVersionProbeTests/C959_Refresh_replaces_evidence | Retain cached version on refresh failure. Input: F-current then nonzero at T+5m. **C959-pc-039**: version null. |
| PC-40 | F-4; CodexCliVersionProbeTests/C959_Refresh_replaces_evidence | Stamp checkedAt when child starts. Input: hold start at T, finish at T+2s. **C959-pc-040**: checkedAt T+2s. |
| PC-41 | F-4; CodexCliVersionProbeTests/C959_Refresh_replaces_evidence | Assign clock.Now in snapshot getter. Input: ten reads after +1m. **C959-pc-041**: checkedAt unchanged. |
| PC-42 | F-4; CodexCliVersionProbeTests/C959_Refresh_replaces_evidence | Return without probe on timer tick. Input: clock advance five minutes. **C959-pc-042**: starts 2. |
| PC-43 | F-4; CodexCliVersionProbeTests/C959_Refresh_replaces_evidence | Bypass in-flight task lookup. Input: 20 identical held requests. **C959-pc-043**: starts 1. |
| PC-44 | F-4; CodexCliVersionProbeTests/C959_Refresh_replaces_evidence | Remove shared runner child semaphore. Input: two different held launchers. **C959-pc-044**: peak concurrent children 1. |
| PC-45 | F-4; CodexCliVersionProbeTests/C959_Refresh_replaces_evidence | Allow insertion of the 33rd unexpired entry. Input: 33 distinct descriptors. **C959-pc-045**: cache entries <=32. |
| PC-46 | F-4; CodexCliVersionProbeTests/C959_Refresh_replaces_evidence | Wait indefinitely for the child semaphore instead of deadline. Input: second descriptor while first held. **C959-pc-046**: unknown/probe_busy within owner deadline. |
| PC-47 | F-4; CodexCliVersionProbeTests/C959_Refresh_replaces_evidence | Reuse cached sample after size change. Input: replace selected file with changed length. **C959-pc-047**: probe starts again. |
| PC-48 | F-4; CodexCliVersionProbeTests/C959_Refresh_replaces_evidence | Drop mtime from cache identity. Input: same-length selected file with new mtime. **C959-pc-048**: probe starts again. |
| PC-49 | F-4; CodexCliVersionProbeTests/C959_Refresh_replaces_evidence | Drop runner boot from cache key. Input: same launcher, new boot. **C959-pc-049**: probe starts again. |
| PC-50 | F-5; CodexCliVersionProbeTests/C959_Local_and_registration_share_snapshot | Set local codexCliVersion=null. Input: F-current snapshot. **C959-pc-050**: local version 0.160.0. |
| PC-51 | F-5; CodexCliVersionProbeTests/C959_Local_and_registration_share_snapshot | Set local checkedAt=null. Input: F-current snapshot. **C959-pc-051**: local checkedAt T. |
| PC-52 | F-5; CodexCliVersionProbeTests/C959_Local_and_registration_share_snapshot | Set local error=null. Input: F-timeout snapshot. **C959-pc-052**: local error timeout. |
| PC-53 | F-5; CodexCliVersionProbeTests/C959_Local_and_registration_share_snapshot | Set local fingerprint=null. Input: resolved A snapshot. **C959-pc-053**: local fingerprint equals A. |
| PC-54 | F-5; CodexCliVersionProbeTests/C959_Local_and_registration_share_snapshot | Set adapter codexCliVersion=null. Input: F-current snapshot. **C959-pc-054**: registration version 0.160.0. |
| PC-55 | F-5; CodexCliVersionProbeTests/C959_Local_and_registration_share_snapshot | Set adapter checkedAt=null. Input: F-current snapshot. **C959-pc-055**: registration checkedAt T. |
| PC-56 | F-5; CodexCliVersionProbeTests/C959_Local_and_registration_share_snapshot | Set adapter error=null. Input: F-timeout snapshot. **C959-pc-056**: registration error timeout. |
| PC-57 | F-5; CodexCliVersionProbeTests/C959_Local_and_registration_share_snapshot | Set adapter fingerprint=null. Input: resolved A snapshot. **C959-pc-057**: registration fingerprint equals A. |
| PC-58 | F-5; CodexCliVersionProbeTests/C959_Local_and_registration_share_snapshot | Omit codex-cli-version-v1 from local features. Input: new local snapshot. **C959-pc-058**: feature present. |
| PC-59 | F-5; CodexCliVersionProbeTests/C959_Local_and_registration_share_snapshot | Omit codex-cli-version-v1 from adapter features. Input: new registration. **C959-pc-059**: feature present. |
| PC-60 | F-5; CodexCliVersionProbeTests/C959_Local_and_registration_share_snapshot | Call refresh from local capability GET handler. Input: ten reads of F-current. **C959-pc-060**: starts unchanged. |
| PC-61 | F-6; CodexCliVersionWindowsTests/C959_Npm_probe_uses_launch_resolution | Select PATH node even when sibling exists. Input: stock npm layout with both. **C959-pc-061**: selected exe sibling node. |
| PC-62 | F-6; CodexCliVersionWindowsTests/C959_Npm_probe_uses_launch_resolution | Return codex.cmd as process executable after Apply. Input: stock npm layout. **C959-pc-062**: selected exe node.exe. |
| PC-63 | F-6; CodexCliVersionWindowsTests/C959_Npm_probe_uses_launch_resolution | Return unknown rather than resolve PATH node. Input: stock layout without sibling, PATH node present. **C959-pc-063**: selected exe PATH node. |
| PC-64 | F-6; CodexCliVersionWindowsTests/C959_Npm_probe_uses_launch_resolution | Split js path at spaces before ArgumentList. Input: spaces and Unicode layout. **C959-pc-064**: one complete js argv element. |
| PC-65 | F-7; CodexCliVersionWindowsTests/C959_Native_and_direct_node_probe | Substitute default native A for explicit B. Input: A current/B old. **C959-pc-065**: selected executable B. |
| PC-66 | F-7; CodexCliVersionWindowsTests/C959_Native_and_direct_node_probe | Drop js prefix before appending --version. Input: direct node descriptor. **C959-pc-066**: argv [absolute js,--version]. |
| PC-67 | F-7; CodexCliVersionWindowsTests/C959_Native_and_direct_node_probe | Resolve js from Environment.CurrentDirectory. Input: different descriptor cwd. **C959-pc-067**: resolved js equals layout js. |
| PC-68 | F-7; CodexCliVersionWindowsTests/C959_Native_and_direct_node_probe | Replace Windows tree kill with parent-only kill. Input: Windows tree helper. **C959-pc-068**: leaf PID/start no longer alive. |
| PC-69 | F-8; CodexCliVersionWindowsTests/C959_Unverified_launcher_is_unknown | On node resolution failure return bare codex. Input: npm no sibling/no PATH node. **C959-pc-069**: unknown and child starts 0. |
| PC-70 | F-8; CodexCliVersionWindowsTests/C959_Unverified_launcher_is_unknown | On missing js choose default native. Input: npm no js, good default. **C959-pc-070**: unknown and child starts 0. |
| PC-71 | F-8; CodexCliVersionWindowsTests/C959_Unverified_launcher_is_unknown | Skip vendored package presence guard. Input: npm no native package. **C959-pc-071**: unknown and child starts 0. |
| PC-72 | F-8; CodexCliVersionWindowsTests/C959_Unverified_launcher_is_unknown | Treat basename codex.cmd as stock. Input: StockNpmShimText plus echo line. **C959-pc-072**: unknown and child starts 0. |
| PC-73 | F-8; CodexCliVersionWindowsTests/C959_Unverified_launcher_is_unknown | Ignore descriptor NODE_OPTIONS override. Input: profile loader sentinel. **C959-pc-073**: launcher_unverified and child starts 0. |
| PC-74 | F-9; RunnerCodexCliEvidenceTests/C959_Heartbeat_updates_only_probe_evidence | Omit CodexCli member from outgoing heartbeat. Input: completed new probe. **C959-pc-074**: recipient version updated. |
| PC-75 | F-9; RunnerCodexCliEvidenceTests/C959_Heartbeat_updates_only_probe_evidence | Assign receive-time to server checkedAt. Input: repeat at T+5m. **C959-pc-075**: recipient checkedAt T. |
| PC-76 | F-9; RunnerCodexCliEvidenceTests/C959_Heartbeat_updates_only_probe_evidence | Ignore null-version/error sample. Input: success then failure. **C959-pc-076**: recipient version null. |
| PC-77 | F-9; RunnerCodexCliEvidenceTests/C959_Heartbeat_updates_only_probe_evidence | Assign current time on absent CodexCli. Input: legacy heartbeat at T+16m. **C959-pc-077**: original checkedAt unchanged. |
| PC-78 | F-9; RunnerCodexCliEvidenceTests/C959_Heartbeat_updates_only_probe_evidence | Apply any arriving sample without timestamp comparison. Input: new failure then old success. **C959-pc-078**: recipient still null/error. |
| PC-79 | F-9; RunnerCodexCliEvidenceTests/C959_Heartbeat_updates_only_probe_evidence | Skip heartbeat send when writer is busy. Input: held reply then CLI heartbeat. **C959-pc-079**: recipient receives exact snapshot after release. |
| PC-80 | F-9; RunnerCodexCliEvidenceTests/C959_Heartbeat_updates_only_probe_evidence | Suppress snapshot on next registration after failed send. Input: send failure then fresh connection. **C959-pc-080**: recipient receives latest completed sample. |
| PC-81 | F-10; RunnerCodexCliEvidenceTests/C959_Freshness_boundaries | Use maxAge plus one tick as allowed bound. Input: age 15m+tick, refresh same stale. **C959-pc-081**: codex_cli_version_stale. |
| PC-82 | F-10; RunnerCodexCliEvidenceTests/C959_Freshness_boundaries | Treat missing checkedAt as now. Input: F-current with null time. **C959-pc-082**: codex_cli_version_unknown. |
| PC-83 | F-10; RunnerCodexCliEvidenceTests/C959_Freshness_boundaries | Drop future tolerance check. Input: T+1m+tick. **C959-pc-083**: unknown with clock_skew. |
| PC-84 | F-10; RunnerCodexCliEvidenceTests/C959_Freshness_boundaries | Reject any future checkedAt. Input: T+1m. **C959-pc-084**: admitted, age zero. |
| PC-85 | F-10; RunnerCodexCliEvidenceTests/C959_Freshness_boundaries | Fall back to pre-refresh success after error. Input: stale then F-timeout. **C959-pc-085**: codex_cli_version_unknown. |
| PC-86 | F-10; RunnerCodexCliEvidenceTests/C959_Freshness_boundaries | Mark any refresh response fresh. Input: stale response retains original time. **C959-pc-086**: codex_cli_version_stale. |
| PC-87 | F-10; RunnerCodexCliEvidenceTests/C959_Freshness_boundaries | Retry helper after first failed refresh. Input: two scripted answers failure then good. **C959-pc-087**: one request and unknown. |
| PC-88 | F-10; RunnerCodexCliEvidenceTests/C959_Freshness_boundaries | Remove >=1 validation. Input: MaxAgeMinutes=0. **C959-pc-088**: settings validation fails. |
| PC-89 | F-10; RunnerCodexCliEvidenceTests/C959_Freshness_boundaries | Remove <=60 validation. Input: MaxAgeMinutes=61. **C959-pc-089**: settings validation fails. |
| PC-90 | F-10; RunnerCodexCliEvidenceTests/C959_Freshness_boundaries | Permit refresh equal to max age. Input: refresh interval 15m/maxAge15m. **C959-pc-090**: settings validation fails. |
| PC-91 | F-10; RunnerCodexCliEvidenceTests/C959_Freshness_boundaries | Ignore nonnull error when parsing sample. Input: F-current with timeout error. **C959-pc-091**: codex_cli_version_unknown. |
| PC-92 | F-11; RunnerCodexCliEvidenceTests/C959_Generation_change_clears_version | Retain CLI memory on same-store new boot without CLI. Input: new boot legacy registration. **C959-pc-092**: version null. |
| PC-93 | F-11; RunnerCodexCliEvidenceTests/C959_Generation_change_clears_version | Retain CLI memory on same-boot new epoch without CLI. Input: new epoch legacy registration. **C959-pc-093**: version null. |
| PC-94 | F-11; RunnerCodexCliEvidenceTests/C959_Generation_change_clears_version | Copy CLI from retired old store into new slot. Input: authorized replacement without CLI. **C959-pc-094**: version null. |
| PC-95 | F-11; RunnerCodexCliEvidenceTests/C959_Generation_change_clears_version | Reuse old general capabilities CLI fields. Input: new registration Capabilities=null. **C959-pc-095**: version null. |
| PC-96 | F-11; RunnerCodexCliEvidenceTests/C959_Generation_change_clears_version | Remove CLI heartbeat epoch equality guard. Input: old success after new unknown generation. **C959-pc-096**: version null. |
| PC-97 | F-11; RunnerCodexCliEvidenceTests/C959_Generation_change_clears_version | Remove response connection-generation guard. Input: delayed reply from disconnected socket. **C959-pc-097**: version null/unknown decision. |
| PC-98 | F-11; RunnerCodexCliEvidenceTests/C959_Generation_change_clears_version | Clear CLI before existing registration guards. Input: foreign store refused while current live. **C959-pc-098**: current sample unchanged. |
| PC-99 | F-12; RunnerCodexCliEvidenceTests/C959_Catalogue_and_status_project_version | Project BuildVersion into CLI field. Input: build=d40c1670, CLI=0.160.0. **C959-pc-099**: CLI exactly 0.160.0. |
| PC-100 | F-12; RunnerCodexCliEvidenceTests/C959_Catalogue_and_status_project_version | Use remote A snapshot for every remote row. Input: A current/B old. **C959-pc-100**: B version 0.156.1. |
| PC-101 | F-12; RunnerCodexCliEvidenceTests/C959_Catalogue_and_status_project_version | Project immutable registration capabilities instead of mutable snapshot. Input: heartbeat changes version. **C959-pc-101**: status equals latest. |
| PC-102 | F-12; RunnerCodexCliEvidenceTests/C959_Catalogue_and_status_project_version | Copy first live version to configured offline row. Input: offline runner-b. **C959-pc-102**: version/time/error/stale null. |
| PC-103 | F-12; RunnerCodexCliEvidenceTests/C959_Catalogue_and_status_project_version | Project false for absent successful evidence. Input: F-legacy. **C959-pc-103**: codexCliVersionStale null. |
| PC-104 | F-12; RunnerCodexCliEvidenceTests/C959_Catalogue_and_status_project_version | Compute CLI stale from row availability only. Input: live heartbeat, stale CLI. **C959-pc-104**: codexCliVersionStale true. |
| PC-105 | F-12; RunnerCodexCliEvidenceTests/C959_Catalogue_and_status_project_version | Project true for every parsed sample. Input: F-current. **C959-pc-105**: codexCliVersionStale false. |
| PC-106 | F-12; RunnerCodexCliEvidenceTests/C959_Catalogue_and_status_project_version | Append fingerprint to catalogue JSON. Input: selected A. **C959-pc-106**: JSON has no launcher fingerprint/path. |
| PC-107 | F-12; RunnerCodexCliEvidenceTests/C959_Catalogue_and_status_project_version | Use default row on unknown status id. Input: other-runner/status. **C959-pc-107**: HTTP 404. |
| PC-108 | F-13; RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound | Use directory.Local instead of selected client. Input: runner-a request. **C959-pc-108**: only runner-a recipient sees descriptor. |
| PC-109 | F-13; RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound | Accept wrong requestId frame. Input: wrong id then correct-id deadline. **C959-pc-109**: unknown, no success. |
| PC-110 | F-13; RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound | Ignore descriptor fingerprint mismatch. Input: response fingerprint B for A. **C959-pc-110**: unknown/launcher_mismatch. |
| PC-111 | F-13; RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound | Accept response for another runner. Input: A request/B response. **C959-pc-111**: unknown/generation_mismatch. |
| PC-112 | F-13; RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound | Translate unsupported operation to floor sample. Input: legacy peer. **C959-pc-112**: codex_cli_version_unknown. |
| PC-113 | F-13; RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound | Return floor sample in default GetCodexCliVersionAsync. Input: client omits override. **C959-pc-113**: result null/unknown. |
| PC-114 | F-13; RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound | Set helper deadline to nine seconds. Input: silent peer clock +8s. **C959-pc-114**: unknown completed at deadline. |
| PC-115 | F-13; RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound | Retry once after first 503. Input: 503 followed by good response. **C959-pc-115**: request count 1 and unknown. |
| PC-116 | F-13; RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound | Catch caller OCE and return unknown normally. Input: cancel while waiting. **C959-pc-116**: OperationCanceledException. |
| PC-117 | F-13; RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound | Skip descriptor length validation. Input: oversized executable/cwd/PATH separately. **C959-pc-117**: unknown and starts 0. |
| PC-118 | F-13; RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound | Resolve secret placeholder as ordinary path. Input: secret placeholder in cwd/PATH. **C959-pc-118**: launcher_unverified and starts 0. |
| PC-119 | F-13; RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound | Change existing Capabilities operation numeric value. Input: legacy captured envelope. **C959-pc-119**: existing operation number unchanged. |
| PC-120 | F-13; RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound | Remove production mapping of new POST. Input: isolated real mapping. **C959-pc-120**: HTTP response contains requested sample, not 404. |
| PC-121 | F-13; RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound | Move gate inside dispatch claim transaction. Input: held request plus second DB reader. **C959-pc-121**: no transaction/claim and reader completes. |
| PC-122 | F-13; RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound | Return cached success from enqueue exception catch. Input: writer enqueue failure. **C959-pc-122**: unknown until explicit new request. |
| PC-123 | F-13; RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound | Return default advertised success on response timeout. Input: different selected B, lost reply. **C959-pc-123**: unknown. |
| PC-124 | F-14; CodexCliAdmissionTests/C959_Ladder_and_exact_models_share_floor | Set RequiredCliVersion=null on 6.1 entry. Input: High/Medium/exact/999. **C959-pc-124**: floor 0.159.1. |
| PC-125 | F-14; CodexCliAdmissionTests/C959_Ladder_and_exact_models_share_floor | Assign 6.1 floor to Astra entry. Input: Frontier/Astra. **C959-pc-125**: no new required floor. |
| PC-126 | F-14; CodexCliAdmissionTests/C959_Ladder_and_exact_models_share_floor | Return null from canonical 6.1 lookup. Input: Low exact 6.1. **C959-pc-126**: same floor as High. |
| PC-127 | F-14; CodexCliAdmissionTests/C959_Ladder_and_exact_models_share_floor | Change High ForCodex result to prior Sol. Input: High and Medium. **C959-pc-127**: gpt-6.1-sol. |
| PC-128 | F-15; CodexCliAdmissionTests/C959_Create_refuses_bad_versions | Return admitted for unknown policy result. Input: F-legacy. **C959-pc-128**: 409 codex_cli_version_unknown. |
| PC-129 | F-15; CodexCliAdmissionTests/C959_Create_refuses_bad_versions | Compare only major/minor. Input: 0.159.0. **C959-pc-129**: 409 codex_cli_version_too_old. |
| PC-130 | F-15; CodexCliAdmissionTests/C959_Create_refuses_bad_versions | Map stale to unknown code. Input: F-stale. **C959-pc-130**: 409 codex_cli_version_stale. |
| PC-131 | F-15; CodexCliAdmissionTests/C959_Create_refuses_bad_versions | Save new task before evaluating gate. Input: F-old. **C959-pc-131**: task/session count 0. |
| PC-132 | F-15; CodexCliAdmissionTests/C959_Create_refuses_bad_versions | Use default model instead of actual model in exception facts. Input: Low pinned 6.1 F-old. **C959-pc-132**: model 6.1, floor 0.159.1, selected runner. |
| PC-133 | F-15; CodexCliAdmissionTests/C959_Create_refuses_bad_versions | On selected old query eligible other runner and admit. Input: A old/B new. **C959-pc-133**: 409 and B query count 0. |
| PC-134 | F-16; CodexCliAdmissionTests/C959_Existing_refusals_and_flags_keep_precedence | Run CLI gate before model availability. Input: held 6.1 plus old. **C959-pc-134**: model_disabled and unchanged coda. |
| PC-135 | F-16; CodexCliAdmissionTests/C959_Existing_refusals_and_flags_keep_precedence | Run CLI gate before remote auth refusal. Input: signed-out plus old. **C959-pc-135**: provider_sign_in_required and remedy. |
| PC-136 | F-16; CodexCliAdmissionTests/C959_Existing_refusals_and_flags_keep_precedence | Treat auth null as signed out. Input: unknown auth plus F-current. **C959-pc-136**: Queued. |
| PC-137 | F-16; CodexCliAdmissionTests/C959_Existing_refusals_and_flags_keep_precedence | Skip CLI gate when AllowUnauthenticatedProvider. Input: signed-out permitted plus old. **C959-pc-137**: codex_cli_version_too_old. |
| PC-138 | F-16; CodexCliAdmissionTests/C959_Existing_refusals_and_flags_keep_precedence | Skip CLI gate when IgnoreModelDisabled. Input: held bypass plus old. **C959-pc-138**: codex_cli_version_too_old. |
| PC-139 | F-17; CodexCliAdmissionTests/C959_Queued_downgrade_blocks_before_claim | Remove queued-task CLI preflight. Input: create current then old. **C959-pc-139**: Blocked before launch. |
| PC-140 | F-17; CodexCliAdmissionTests/C959_Queued_downgrade_blocks_before_claim | Move CLI check after preparation. Input: queued old Worktree task. **C959-pc-140**: prep calls 0. |
| PC-141 | F-17; CodexCliAdmissionTests/C959_Queued_downgrade_blocks_before_claim | Remove Blocked event insertion for CLI refusal. Input: queued old. **C959-pc-141**: one Blocked event with code. |
| PC-142 | F-17; CodexCliAdmissionTests/C959_Queued_downgrade_blocks_before_claim | Requeue CLI-blocked tasks each Tick. Input: old then repaired, no explicit retry. **C959-pc-142**: still Blocked. |
| PC-143 | F-17; CodexCliAdmissionTests/C959_Queued_downgrade_blocks_before_claim | Stop active session when another task fails CLI check. Input: working sibling and old queued task. **C959-pc-143**: stop/input calls for active sibling 0. |
| PC-144 | F-17; CodexCliAdmissionTests/C959_Queued_downgrade_blocks_before_claim | Reuse pre-redirect good admission for new bad runner. Input: drain redirect A current to B old. **C959-pc-144**: Blocked, B receives no input. |
| PC-145 | F-17; CodexCliAdmissionTests/C959_Queued_downgrade_blocks_before_claim | Use original non-gated alias after chain picks 6.1. Input: rewalk retired exact to 6.1 on old. **C959-pc-145**: Blocked. |
| PC-146 | F-17; CodexCliAdmissionTests/C959_Queued_downgrade_blocks_before_claim | Drop final descriptor/profile comparison. Input: revision changes after preflight before launch. **C959-pc-146**: no old-authorization launch/input. |
| PC-147 | F-18; CodexCliAdmissionTests/C959_Exact_profile_model_wins | Always use tier alias for gate. Input: Low pinned 6.1 on old. **C959-pc-147**: refused for 6.1. |
| PC-148 | F-18; CodexCliAdmissionTests/C959_Exact_profile_model_wins | Gate by High regardless of exact id. Input: High pinned gpt-5.6-terra on old. **C959-pc-148**: admitted without CLI gate. |
| PC-149 | F-18; CodexCliAdmissionTests/C959_Exact_profile_model_wins | Silently drop exact model on blank ModelArgumentName. Input: blank argument + exact 6.1. **C959-pc-149**: model_argument_unsupported. |
| PC-150 | F-18; CodexCliAdmissionTests/C959_Exact_profile_model_wins | Treat blank/blank profile as High 6.1. Input: wrapper-owned profile. **C959-pc-150**: no invented CLI floor. |
| PC-151 | F-18; CodexCliAdmissionTests/C959_Exact_profile_model_wins | Use tier alias instead of frozen live model. Input: live exact 6.1 with Low task. **C959-pc-151**: refused on old CLI. |
| PC-152 | F-18; CodexCliAdmissionTests/C959_Exact_profile_model_wins | Compare profile id without active revision. Input: same profile revised native B old. **C959-pc-152**: refused after reprobe. |
| PC-153 | F-19; CodexCliAdmissionTests/C959_Profile_launcher_uses_its_own_evidence | Use default snapshot despite differing launcher. Input: default current/selected B old. **C959-pc-153**: codex_cli_version_too_old. |
| PC-154 | F-19; CodexCliAdmissionTests/C959_Profile_launcher_uses_its_own_evidence | Refuse using default old before probing selected A. Input: default old/selected A current. **C959-pc-154**: admitted with selected A evidence. |
| PC-155 | F-19; CodexCliAdmissionTests/C959_Profile_launcher_uses_its_own_evidence | Accept RunnerVersion=Codex 0.160.0 as probe success. Input: selected B unknown validation good. **C959-pc-155**: codex_cli_version_unknown. |
| PC-156 | F-19; CodexCliAdmissionTests/C959_Profile_launcher_uses_its_own_evidence | Drop the profile PATH override while constructing the exact probe descriptor. Input: bare selector, default A:B, profile B:A. **C959-pc-156**: outgoing descriptor PATH is B:A and B old is refused. |
| PC-157 | F-19; CodexCliAdmissionTests/C959_Profile_launcher_uses_its_own_evidence | Drop the profile PATHEXT override while constructing the exact probe descriptor. Input: default .EXE;.CMD, profile .CMD;.EXE. **C959-pc-157**: outgoing descriptor PATHEXT is .CMD;.EXE. |
| PC-158 | F-19; CodexCliAdmissionTests/C959_Profile_launcher_uses_its_own_evidence | Resolve nonexistent future cwd from parent. Input: relative codex with future worktree. **C959-pc-158**: unknown. |
| PC-159 | F-19; CodexCliAdmissionTests/C959_Profile_launcher_uses_its_own_evidence | Send desktop codex.cmd path to remote probe. Input: standard remote Codex launch. **C959-pc-159**: descriptor selector Linux codex. |
| PC-160 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Remove runner equality predicate. Input: wrong runner/live/model/code valid. **C959-pc-160**: refusal preserved. |
| PC-161 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Remove model equality predicate. Input: wrong model/live/runner/code valid. **C959-pc-161**: refusal preserved. |
| PC-162 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Ignore allowedRefusalCodes. Input: too_old-only item with unknown. **C959-pc-162**: codex_cli_version_unknown. |
| PC-163 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Check expiry only at settings load. Input: advance clock to expiry without reload. **C959-pc-163**: refused. |
| PC-164 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Remove runner wildcard validation. Input: runnerId=*. **C959-pc-164**: config invalid. |
| PC-165 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Remove model wildcard validation. Input: model=*. **C959-pc-165**: config invalid. |
| PC-166 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Allow wildcard refusal code. Input: allowedRefusalCodes=[*]. **C959-pc-166**: config invalid. |
| PC-167 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Remove nonempty reason validation. Input: reason empty. **C959-pc-167**: config invalid. |
| PC-168 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Permit missing/invalid expiry. Input: missing expiry and non-UTC malformed value. **C959-pc-168**: config invalid. |
| PC-169 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Remove maximum lifetime validation. Input: T+24h+tick. **C959-pc-169**: config invalid. |
| PC-170 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Remove duplicate tuple detection. Input: two same runner/model entries. **C959-pc-170**: config invalid. |
| PC-171 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Accept arbitrary refusal code. Input: model_disabled in allowed codes. **C959-pc-171**: config invalid. |
| PC-172 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Omit create Warning event. Input: matching live override at Create. **C959-pc-172**: Warning with runner/model/code/expiry/reason. |
| PC-173 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Omit dispatch Warning event. Input: matching live override at Tick. **C959-pc-173**: Warning with runner/model/code/expiry/reason. |
| PC-174 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Replace old observed version with floor. Input: matching live override. **C959-pc-174**: stored/public evidence still 0.156.1. |
| PC-175 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Return success before hold when CLI override exists. Input: held 6.1 plus matching override. **C959-pc-175**: model_disabled. |
| PC-176 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Return success before auth when CLI override exists. Input: signed-out plus matching override. **C959-pc-176**: provider_sign_in_required. |
| PC-177 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Skip selected-runner drain check when override exists. Input: draining runner plus matching override. **C959-pc-177**: existing drain refusal/hold. |
| PC-178 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Skip retired-runner check when override exists. Input: retired runner plus matching override. **C959-pc-178**: existing retired refusal/hold. |
| PC-179 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Skip required-platform predicate when override exists. Input: Linux requirement/Windows runner. **C959-pc-179**: existing platform refusal. |
| PC-180 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Skip capacity predicate when override exists. Input: occupied runner plus matching override. **C959-pc-180**: no claim/new session. |
| PC-181 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Swallow launcher rejection when override exists. Input: unsupported actual launcher plus matching override. **C959-pc-181**: existing launch refusal, starts 0. |
| PC-182 | F-20; CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | Drop model_argument_unsupported when override exists. Input: blank model argument with exact model. **C959-pc-182**: model_argument_unsupported. |
| PC-183 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | Change version >= floor to > floor. Input: Codex F-floor desktop/remote. **C959-pc-183**: complete W UserPrompt plus exact E spill bytes. |
| PC-184 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | Replace final 6.1 model argument with gpt-6-sol. Input: F-current selected task. **C959-pc-184**: one model argument exactly gpt-6.1-sol. |
| PC-185 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | Rewrite final launch target to desktop. Input: remote F-current. **C959-pc-185**: recipient transcript belongs to selected remote session. |
| PC-186 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | Remove Dispatcher queue enqueue for compatible task. Input: eligible/busy. **C959-pc-186**: complete matching W UserPrompt after release and exact E file when spilled. |
| PC-187 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | In FitBriefForTyping, after BuildBrief returns, replace literal `END-C959` with empty text before either inline/spill branch. Input: B in Claude inline first, then Codex/Grok spill vectors. **C959-pc-187**: ordinal whole submitted W equals independent E.TrimEnd() for Claude; whole spill equals E and includes B for non-Claude. Never demand B inside a non-Claude pointer. Assert this before exact wrapped-payload equality. |
| PC-188 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | Drop dispatch handoff when session busy. Input: busy then TurnEnd. **C959-pc-188**: exactly one complete W UserPrompt after eligibility, with E file if spilled. |
| PC-189 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | Emit CLI override Warning on ordinary compatible success. Input: F-current without exception. **C959-pc-189**: CLI Warning count 0. |
| PC-190 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | Remove Dispatcher launch enqueue while retaining message enqueue. Input: fresh compatible task. **C959-pc-190**: adapter starts and complete W UserPrompt arrives, with exact E file if spilled. |
| PC-191 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | In the Dispatcher enqueue-exception catch, set claimed.Status to Working and save it. Input: enqueue fault before persistence. **C959-pc-191**: task is not Working and has no recipient receipt until recovery. |
| PC-192 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | Reuse old session id for retry queue handoff. Input: restart and retry with new session generation. **C959-pc-192**: receipt on retry session/generation only. |
| PC-193 | F-22; CodexCliAdmissionTests/C959_Retry_and_cancellation_recheck | Remove retry CLI gate. Input: failed task on old/unknown/stale runner. **C959-pc-193**: status unchanged and Retried count 0. |
| PC-194 | F-22; CodexCliAdmissionTests/C959_Retry_and_cancellation_recheck | Return Queued without persisting retry transition. Input: real watchdog-failed task repaired to floor, explicit RetryAsync. **C959-pc-194**: real queues yield complete W UserPrompt plus exact E file on retry session/generation; no manually Failed setup qualifies this vector. |
| PC-195 | F-22; CodexCliAdmissionTests/C959_Retry_and_cancellation_recheck | Swallow caller cancellation at create and continue SaveChanges. Input: cancelled exact probe. **C959-pc-195**: no new task/session/claim. |
| PC-196 | F-22; CodexCliAdmissionTests/C959_Retry_and_cancellation_recheck | On owned deadline return admitted. Input: silent exact probe. **C959-pc-196**: codex_cli_version_unknown. |
| PC-197 | F-22; CodexCliAdmissionTests/C959_Retry_and_cancellation_recheck | Reuse previous override admission on retry. Input: same task after expiry. **C959-pc-197**: refused, no Retried event. |
| PC-198 | F-22; CodexCliAdmissionTests/C959_Retry_and_cancellation_recheck | Use default success if new client operation absent. Input: legacy client on retry. **C959-pc-198**: unknown and unchanged status. |
| PC-199 | F-2; CodexCliVersionProbeTests/C959_Probe_is_version_only_and_auth_free | Assign captured stderr to CodexCliVersionError instead of fixed reason. Input: synthetic diagnostic sentinel. **C959-pc-199**: serialized sample excludes sentinel and error is fixed token. |
| PC-200 | F-10; RunnerCodexCliEvidenceTests/C959_Freshness_boundaries | Change age <= maxAge to age < maxAge. Input: age exactly 15m, no error, bound launcher/generation. **C959-pc-200**: admitted without refresh. |
| PC-201 | F-11; RunnerCodexCliEvidenceTests/C959_Generation_change_clears_version | Delete only the slot.Live-is-not-null disjunct in the store replacement guard. Input: retirement cleared, original registration lease expired, same boot, different store, current socket still live and no disconnect. **C959-pc-201**: phone_home_store_mismatch and current sample unchanged. |
| PC-202 | F-11; RunnerCodexCliEvidenceTests/C959_Generation_change_clears_version | Delete only slot.LeaseUntil > now in the store replacement guard. Input: retirement cleared, no socket/disconnect, same boot, new store at 89s of the 90s registration lease. **C959-pc-202**: phone_home_store_mismatch and current sample unchanged. |
| PC-203 | F-11; RunnerCodexCliEvidenceTests/C959_Generation_change_clears_version | Remove retired-slot registration refusal. Input: retired slot with same or foreign store. **C959-pc-203**: existing RunnerRetired code and no accepted sample. |
| PC-204 | F-17; CodexCliAdmissionTests/C959_Queued_downgrade_blocks_before_claim | Skip CLI preflight on warm-session branch only. Input: compatible create then old CLI on selected warm idle session. **C959-pc-204**: Blocked and reused-session input count 0. |
| PC-205 | F-19; CodexCliAdmissionTests/C959_Profile_launcher_uses_its_own_evidence | Remove cwd consistently from the descriptor fingerprint and cache identity. Input: same absolute native executable/metadata with resolution cwds A and B; both report current. **C959-pc-205**: fingerprint differs and second descriptor is probed. |
| PC-206 | F-19; CodexCliAdmissionTests/C959_Profile_launcher_uses_its_own_evidence | Substitute default package A codex.js before both resolution and probing. Input: same node with explicitly selected package B old, default A current. **C959-pc-206**: captured js prefix is B and selected B is refused. |
| PC-207 | F-11; RunnerCodexCliEvidenceTests/C959_Generation_change_clears_version | Delete only the LastDisconnect plus lease disjunct from store replacement. Input: expired registration lease, retirement cleared, same boot, recent heartbeat then disconnect, new store after 89 of 90 seconds from disconnect. **C959-pc-207**: phone_home_store_mismatch and old identity unchanged. |
| PC-208 | F-11; RunnerCodexCliEvidenceTests/C959_Generation_change_clears_version | Delete only !slot.StoreReplacementAuthorized from store replacement. Input: same boot, no socket, expired registration/disconnect leases, no retirement clear, different store. **C959-pc-208**: phone_home_store_mismatch and old identity unchanged. |
| PC-209 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | In PtyDeliveryCeilings.ForAgentKind set the non-Claude BriefInlineMaxBytes to the unmodified instance value. Codex/Grok with measured modern profile. **C959-pc-209**: effective ceiling equals 0, followed by spill+pointer receipt. |
| PC-210 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | In FitBriefForTyping's local File.WriteAllText only, write `brief.Replace("\n", " ")`. **C959-pc-210**: actual pointer-named file bytes equal UTF-8 E, including LF B. Place before aggregate receipt assertion. |
| PC-211 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | In RunnerWorkspaceService.WriteSpillAsync's final write only, replace LF in spill.Body with a space. **C959-pc-211**: runner file bytes equal UTF-8 E at first input callback; pointer ack alone cannot pass. |
| PC-212 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | In BuildBriefPointer force local `joins = false`. Non-Claude short-title pointer fixtures use explanatory form. **C959-pc-212**: submitted pointer contains neither LF nor CR, before whole-W equality. |
| PC-213 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | In BindStagedSpill omit only row.Body path replacement; retain owned path/body. Hold busy recipient. **C959-pc-213**: persisted pointer contains exact `.antiphon/inbox/{row.Id:D}.md`, before allowing input. |
| PC-214 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | In DeliverAsync replace WrapIfMultiline(trimmed) with trimmed. Claude inline LF. **C959-pc-214**: recorded task-body write equals ESC[200~ + W + ESC[201~; harmless fake still submits exact W, so PC-187 passes and this wrapper assertion fails. |
| PC-215 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | In DeliverAsync append `"\r"` to payload in the body SendInputAsync call, leaving later submit untouched. **C959-pc-215**: body write contains no CR and next submit is a separate exactly-CR write; inspect ordered task writes before whole-body equality. |
| PC-216 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | In BindStagedSpill assign row.RemoteSpillBody=null instead of staged.Spill.Body. **C959-pc-216**: a fresh DB context sees E before recipient input, with staging still alive so no setup error masks the loss. |
| PC-217 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | In PhoneHomeCommandDispatcher.Input swap awaited SendInputAsync and WriteSpillIfPresentAsync. **C959-pc-217**: recipient's first input callback observes file existence before the separate PC-211 byte equality; also a forced write failure records zero pointer input. Capture absence as false, not FileNotFoundException. |
| PC-218 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | Make AcceptedByCompleteUserPrompt return true for every outcome. Suppress recipient transcript but keep screen advance/ack. **C959-pc-218**: queue RemoteSpillBody still equals E and receipt count is zero; eventual genuine W UserPrompt releases bytes. |
| PC-219 | F-22; CodexCliAdmissionTests/C959_Retry_and_cancellation_recheck | In RemoteSpillCourier.FindDurableAsync return null when row.RemoteSpillBody is nonempty. After persisted enqueue, recreate DI, remove only the test-owned recipient file, retain DB. **C959-pc-219**: bounded recovery produces exact E at original message-owned path before pointer input and complete W receipt; absent durable lookup is red, not a fake restage. |
| PC-220 | F-22; CodexCliAdmissionTests/C959_Retry_and_cancellation_recheck | Make LateConfirmAttemptedMessagesAsync return LateConfirmCounts.Empty at entry. Crash after actual submission before status save, recreate DI and pull actual transcript. **C959-pc-220**: body submission count across both graphs remains 1 while queue settles against complete W, not a second submit. |
| PC-221 | F-22; CodexCliAdmissionTests/C959_Retry_and_cancellation_recheck | Make FailNeverStartedAsync return 0 at entry. Drop launch after committed claim, retain DB, recreate DI and advance existing clock past delivery timeout. **C959-pc-221**: task durably Failed with recorded failure before explicit RetryAsync; then PC-194's E/W receipt. Do not manufacture failure in fixture. |
| PC-222 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | Make PromptSubmissionMatch.IsCompleteIn return IsConfirmedBy(body, recordText). Recipient records only first 200 characters of non-Claude W. **C959-pc-222**: no transcript-confirmed verdict and spill E retained; observed head-only UserPrompt never passes complete receipt assertion. |
| PC-223 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | Remove only `t.Sequence > baselineSequence` in TryFindConfirmingRecordAsync. Inject identical whole W at sequence equal to baseline, withhold current receipt. **C959-pc-223**: no transcript-confirmed verdict and E retained; release actual new W above floor to finish green. |
| PC-224 | F-21; CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | Remove only `t.AgentSessionId == sessionId` in TryFindConfirmingRecordAsync. Other isolated session holds whole W at sequence above baseline; selected recipient withholds receipt. **C959-pc-224**: selected row remains unconfirmed with E retained, until selected session's actual receipt. |

Audit after ee18a226 amendment: bodies read as listed; **guards=224, mapped=224, missing=0,
duplicate PC maps=0**. All 224 have one exact method, one concrete injected
defect, one fixture and one first detecting label; executable after S1-S4 supplies
the frozen methods/setup. None was executed in TestDesign. A new implementation
guard or an unexecutable control returns for an explicit freeze amendment; it is
not silently discarded. There is no claim that the current baseline already
contains the new tests.

The original freeze's read-only audit covered 208 controls, seven CP rows and
166 results; its harmless-child parse was not execution. This amendment's read-only
audit passed: 224 unique G/PC pairs, unchanged 22 method bindings, eight CP
rows, 4127 minimum executions, 74 ordinary minutes and the Cost below. These
are document checks, not product behavior, importer admission or PC results.

### Out of scope

- Real provider spend, installing/downgrading a serving CLI, live rollout and
  operator override activation: separately commissioned operations. Synthetic
  fixtures cannot certify today's installed CLI or provider entitlement.
- Opaque wrappers choosing their own model, arbitrary version commands, metadata-
  preserving malicious binary replacement and an OS atomic executable lease:
  D-1/D-2/D-4 deliberately do not offer these guarantees.
- Queue/attention implementation changes, new holds, task-input spill changes,
  general post-admission revalidation of every already queued user input, new
  public API and DB migration: D-6/D-10 exclusions. Gate recovery vectors concern
  a new or explicitly retried dispatch; an already-authorized queued body retains
  the existing delivery contract and does not acquire a per-write CLI gate.
- All-model version gating and UI redesign: only known resolved gpt-6.1-sol is
  newly gated. Historical model aliases retain their behavior.
- The delivery matrix uses writable owned local/runner workspaces. Local spill
  IO-denial/API-pointer fallback and a runner binding with no RunnerCwd remain
  their existing owners' scope, not new CLI-gate behavior. Local file handoff
  faults here are crash cuts around a successful write; only the remote
  write-before-input guard gets the explicit writer-failure vector. Do not
  assert that all possible fallback paths require a file, or count a fallback
  pointer as passing the exact-file vector.

### Code admission, overlap and rollout freeze

Read-only evidence at 13:15 UTC: CARD-1008 Code task 0c0689e5 was dispatched;
CARD-0965 Final Review 9dbe1877 and CARD-1001 Final Review a5c51cc2 were dispatched;
CARD-1006 Plan amendment 3fdafc77 was dispatched. Their task detail briefs were
read; null scope fields on summary/detail are **not** proof of exclusive ownership.
The actual Git deltas available in the mirror were also inspected:
c6d5d56b..8273615c for 0965 has six test/helper files and no server/src delta;
f0fadc61..f4044e8f for 1001 has the two Coverage files, its plan,
PlanCoverageHandleTests and scripts/lib/checkpoint-usage.ps1. A brief's claim of
a production serializer change is not substituted for those actual diff paths.

| Sibling / shared file or region | CARD-0959 boundary and landing order |
|---|---|
| CARD-1008: scripts/c590-remote.sh, scripts/deploy-server2.ps1, tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs, deployment fixtures/docs | Entirely excluded from 0959. 1008 keeps its own priority over 0980/0983 and 1010. No dependency forces 0959 to wait for 1008; no Docker/deploy edit is needed to add CLI evidence. |
| CARD-0965: server/Application/Services/AgentTaskReplyService.cs, AgentTaskInputService.cs, SessionMessageQueueService.cs, AttentionService*.cs, ParkedMessageSweepService.cs; AgentTaskEndpoints/server Program; task-input test/helper regions | Excluded from 0959. Read-only use of unchanged queue and bridge harness is permitted. Actual pending 0965 test delta is AgentTaskInputFallbackTests, AgentTaskInputSpillTests, CapacityRecoveryCompatibilityTests, ParkedMessageSweepServiceTests, TaskInputReadFailureTests, TestHelpers/TaskInputSpillFixture; none is a 0959 edit. Prefer finish 0965's in-progress Review/land first operationally, but this is not a Code-start dependency. |
| Potential same-file transport seams with the earlier 0888/0965 chain: SessionRunnerContracts.cs, PhoneHomeContracts.cs, SessionRunnerRuntime.cs, PhoneHomeRuntimeAdapter.cs, PhoneHomeConnectionService.cs, PhoneHomeCommandDispatcher.cs, ISessionRunnerClient.cs, SessionRunnerHttpClient.cs, PhoneHomeRunnerClient.cs, RunnerScopedSessionRunnerClient.cs, RoutingSessionRunnerClient.cs, PhoneHomeLiveConnection.cs, PhoneHomeRunnerDirectory.cs | 0959 owns only new capability/probe members and their projections. Do not modify input/spill serialization or queue/readiness members. If a new 0965 repair touches any of these files, it lands first; rebaseline 0959's affected slice/counts before editing. Same-file different-method claims do not waive this rule. |
| CARD-1006: src/Antiphon.Agents.Pty/GrokDetectors.cs, GrokStartupReadiness.cs; tests/Antiphon.Tests/Agents/GrokSignInPromptDetectorTests.cs, GrokLinuxBlockingPromptTests.cs, GrokStartupReadinessTests.cs, GrokLinuxStartupReadinessTests.cs, GrokTrustPromptDetectorTests.cs, GrokStartupCaptureStoreTests.cs, RunnerGrokAdapterReadyTests*.cs, RunnerGrokAdapterSignInPromptTests.cs, RunnerGrokAdapterTrustPromptTests.cs; card0778/card1004/card1006 frames | Entirely excluded from 0959. No prerequisite and no conditional detector edits. 1006's real/synthetic modal decisions do not qualify a Codex executable. Its plan amendment may land independently. |
| CARD-1001: tools/Antiphon.Checkpoints/Coverage/**, tests/Antiphon.Tests/Checkpoints/PlanCoverageHandleTests.cs, scripts/lib/checkpoint-usage.ps1 | Entirely excluded. 0959 reads/imports manifests, never repairs the tool or census. 1001 may land independently; if its tooling changes reach master before Code, use that current importer and retain its receipt. |

**Landing order:** this freeze -> 0959 S1-S4 Code -> ordinary Review including
Windows -> 0959 land -> runner-first activation -> server enforcement -> separately
commissioned post-land Mutation. The four sibling cards do not presently impose
a source dependency. **0959 can start Code before they land**, provided the
caller rechecks actual active footprints and assigns exclusive files. Any actual
collision pauses that slice for the existing owner to land first; no force
rebase/reset or widening into excluded areas is authorized.

**Code start condition:** this committed freeze is the dispatch artifact; current
base contains D-1..D-10; caller checks current pipeline/runner defaults/host capacity
and no active same-source collision; Code repeats the source census with deltas;
then bootstraps/imports the amended eight-row manifest before implementation.
Native Windows CP-2 is scheduled at the same implementation source SHA.
Missing test helpers listed above are normal S1-S3 work, not missing evidence
to be invented. There is no unresolved human product choice or TestDesign BLOCKER.

No prebuilt Antiphon.Checkpoints.dll was found by the read-only file search under
this worktree, /work/repos/antiphon or /tmp. The real importer was therefore
**not run**: this docs-only brief prohibits its required bootstrap build.
This is a **Code-admission item**, not a TestDesign blocker or an importer receipt.
The existing table was checked as text for seven rows, eleven cells per row,
escaped pipes, nonempty exact filters, distinct build outputs and minima
5/29/5/9/22/20/76. That static check cannot replace the real importer.

Code's explicit bootstrap exception (estimated four minutes, separate from CPs):
through scripts/build-slot.ps1 build only tools/Antiphon.Checkpoints with
OutputPath=bin-c959-tool/ (forward slash), then run its built dll's
import --plan docs/superpowers/plans/2026-10-03-card-0959-runner-codex-version-plan.md
--out .antiphon/c959-code-import.yml, with no second build. Require exit 0 and all
eight exact filters/counts; use this built tool for the owner-bound checkpoint
run/wait calls. Exit 4 means bootstrap not run. Remove only owned alternate outputs
after completion. No DOCS-0959 build/test run was performed by this freeze; the
earlier plan-stage documentation command remains historical, outside Code's
ordinary selection. The original seven-row importer inspection above is historical;
ee18a226 adds the Final Unit row without executing the importer.

Runner-first ordering fixtures (internal V-5/V-11/V-13/V-15 vectors):
1. Old server JSON reader + new runner fields/feature: ignores additions and still
   reads old Build/Version/capacity fields; this proves wire compatibility, not
   behavior of an unavailable historical server executable.
2. Enforcing server + F-legacy runner whose actual synthetic CLI is 0.160.0:
   unknown refusal; actual installation cannot substitute for missing evidence.
3. Upgrade runner generation, publish F-current at T, refresh at T+5m and T+10m:
   list/status/descriptor show those **attempt times**. Extra heartbeats between
   them do not inflate time; selected profile B remains old until its own probe.
4. Activate gate only after both serving OS lanes and each selected profile have
   fresh evidence; both floor/current admit with full recipient evidence, old
   refuses. Roll back server enforcement independently; additive runner JSON
   remains compatible and external minimum-version operations remain necessary.

Windows-only observations are unmeasured here: Apply's Windows branch, sibling
node/PATHEXT resolution, Unicode argv and actual Windows tree cleanup. Linux
execution cannot stand in for CP-2; all 29 results, zero skips, are required on
Windows. The desktop worktree is unreachable from this delegate. No live runner
version, installed CLI upgrade, two-period production observation or Windows
execution is claimed from source fixtures.

### Checkpoints

Group names name the lane. CP-2 requires Windows; all other rows are portable
and normally run on the automatically selected Linux lane. This amended table
is the closed Final ordinary Code/Review list. Import validity is not execution.
CP-3 runs after S2-S4; CP-8 carries the Final whole Unit requirement from the
continuation report and owner recipe. CP-1..7 minima remain 5/29/5/9/22/20/76.
CP-8's 3961 floor is the inherited 3942 Linux executions plus the 19 jq cases
that must run after restoring that prerequisite. Record the 33 Windows-only
exclusions separately; no new skip/failure or census drift permits silent lowering.
CP-2 is a separately commissioned **Debug on native Windows at the final Code
SHA**, 29 executed, zero skipped. Linux Unit skips cannot substitute for it.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c959-probe/` | portable-runner-probe | `/*/*/CodexCliVersionProbeTests*/C959_*` | V-1..V-5 | 5 proposed single-result methods; 0 failed/skipped | 5 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c959-windows/` | windows-native-launcher | `/*/*/(CodexCliVersionWindowsTests*)\|(CodexWindowsLaunchPolicyTests*)/*` | V-6..V-8, R-4 | 3 proposed plus 26 source-derived regression results; 0 failed/skipped | 29 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S2-S4 | `tests/Antiphon.Tests -> bin-c959-evidence/` | portable-capability-wire | `/*/*/RunnerCodexCliEvidenceTests*/C959_*` | V-9..V-13 | 5 proposed single-result methods; 0 failed/skipped | 5 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c959-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S3 | `tests/Antiphon.Tests -> bin-c959-admission/` | portable-admission-db | `/*/*/CodexCliAdmissionTests*/C959_*` | V-14..V-22 | 9 single-result methods, amended E/W delivery and recovery vectors; 0 failed/skipped | 9 | 14 | true | `C804_ORPHAN_SWEEP_ROOT=c959-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S3 | `tests/Antiphon.Tests -> bin-c959-auth/` | portable-existing-refusals | `/*/*/(CodexPhoneHomeCreateTests*)\|(PinnedCodexProfileDispatchLaunchTests*)\|(ModelAvailabilityCreateTests*)\|(ModelAvailabilityDispatcherTests*)/*` | R-1 | 22 source-derived expanded results; 0 failed/skipped | 22 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c959-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S2-S4 | `tests/Antiphon.Tests -> bin-c959-directory/` | portable-directory-regression | `/*/*/(RunnerCatalogueTests*)\|(PhoneHomeDirectoryTests*)\|(PhoneHomeRunnerRetirementIdentityTests*)/*` | R-2 | 20 source-derived expanded results; 0 failed/skipped | 20 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c959-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S3 | `tests/Antiphon.Tests -> bin-c959-models/` | portable-model-regression | `/*/*/ModelAliasTests*/*` | R-3 | 76 source-derived expanded results; 0 failed/skipped | 76 | 5 | true | `C804_ORPHAN_SWEEP_ROOT=c959-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | Final S1-S4 | `tests/Antiphon.Tests -> bin-c959-unit/` | portable-final-unit | `/*/*/*/*[Category=Unit]` | Final whole Unit regression | At least 3961 executed; 0 failed; only the 33 documented Windows-only exclusions, no missing-jq skips and none substitutes for V/R | 3961 | 15 | true | `C804_ORPHAN_SWEEP_ROOT=c959-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

The inherited Unit log is
`/work/worktrees/task-1a73906f/.antiphon/c959-cont-unit/C959-UNIT-20261003-172357-0347/run.log`.
Its 19 missing-jq cases are C973 cold-marker readers (6), cold-helper readers (5),
prune/saved-donor (1), and the seven C912 cold-seed/cache/marker methods (7).
Provide jq on the chosen test host's shell PATH before CP-8; estimate two minutes
of environment setup, no excluded deployment/tool source change. A missing
prerequisite is incomplete, not permission to count these as passed.
The 33 existing Windows-only exclusions comprise executable/npm resolution (2),
canonical cwd/argv/reported paths (3), drive-letter directory listing (5), Grok
raw-rules argv (12), ConPTY delivery profiles (5), Windows checkpoint process
pipes/argv/descendants/concurrency (4), and sharing-mode filesystem locks (2).
These reasons are recorded in that log and cannot qualify native Windows
behavior. CP-2 independently owns this card's 29 required Windows results.

#### Next Code continuation, mandatory order

Start from this amendment's pushed tip, reported as the exact full SHA in the
ee18a226 completion. Retain original landing owner
`1a73906f-2dd0-451e-95be-7dd459c882ad`; this docs-only task is not a new Code
landing owner. Import the current **eight-row** table after the listed four-minute
isolated tool bootstrap. Earlier seven-row importer notes are historical.

1. Implement amended V-21 and dependent V-22 E/W assertions, including Claude
   LF inline and Codex/Grok exact-file plus complete single-line pointer receipt.
   Preserve every other frozen method, vector and decision. Do not repair the
   failed assertion by raising a ceiling or changing production spill behavior.
2. Finish remote/busy delivery and durable recovery at every listed handoff:
   real producer, real queues, retained schema, recreated DI, actual transcript
   pull, real FailNeverStartedAsync then explicit RetryAsync when terminalized,
   exact file bytes, original attempt/generation fences and zero duplicate body
   submits. The present admission-only V-22 pass does not satisfy this item.
3. Complete remaining launcher/transport/profile/override guards identified by
   the Code report: V-2/3/4 identity and owned-child lifecycle, including parent
   exit before descendant EOF; V-9/11 reconnect/stale frames; V-13 wrong-runner/
   fingerprint correlation; V-17 redirect/chain/warm drift; V-18 specialist/live
   model/revision drift; V-19 package/metadata/boot/future-relative-cwd and cached
   validation exclusion; V-20 Tick audit and hold/auth/platform/drain/retirement/
   capacity/launch/profile variants, null/malformed config and kind-canonical model.
   Fingerprint shape alone is not request correlation. If the existing exact-
   launcher seam cannot verify the frozen claim, return next: plan with concrete
   evidence; do not invent a successful sample or relax the freeze.
4. Complete manual strength/guard qualification: inspect the production line and
   detecting assertion for every independent PC (all 224 remain pending for
   post-land Mutation); document receipt provenance, fault recovery, cleanup
   ownership and substitutes' limits. No deliberate mutant or live provider
   canary is authorized in Code by this amendment. The prior Coverage command
   was INVALID; fix no excluded Coverage-tool source and claim no coverage pass.
5. Commit final repairs, then complete fresh Final ordinary verification: whole
   Unit CP-8, every named full affected integration class and all V/R in CP-1..7,
   plus manual qualification. Schedule Windows CP-2 as separate Debug at that
   exact final SHA, minimum 29, zero failed/skipped. Record counts, source and
   unedited receipts. Then ordinary **Review**, not land; caller land and later
   SourceLanding Mutation remain separate. Eventual activation: runners first,
   server enforcement second; no restart/deploy in this continuation.

Pending ordinary qualifications carried forward: V-2/3/4/6/7/8/9/11/13/17/18/
19/20/21/22 and R-4 plus the manual items. Historical green methods do not
discharge missing internal vectors. This amendment itself runs no repository
build, tests, importer or PC; its only validation is read-only document/source
census, diff and arithmetic. No human product choice is outstanding.

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

All costs below are **estimated**, not execution receipts. Ordinary Code V/R is
**74 minutes**, the sum of CP-1..CP-8: 8+8+8+14+8+8+5+15, including
eight isolated builds. CP-1..7 retain **166 executions** (22 new + 144 regression);
Final Unit adds 3961, giving a **4127 executed-result floor**.
Code setup is **6 minutes**: bin-c959-tool bootstrap/import 4 plus jq prerequisite
2. Total ordinary Code verification/admission floor **80 minutes**, excluding
authoring and slot queue wait. Review repeating the complete rows adds 74 minutes.
There is no TestDesign build/test cost receipt; read-only inspection did not
execute any checkpoint.

Mutation now has **224** independent controls (208 retained/retargeted plus 16
delivery/recovery controls). Cost each baseline at four minutes portable, five
Windows, or six for amended V-21/V-22, including isolated build. Each PC costs
red build/run + 0.25-minute restoration/check + green build/run: 8.25 portable,
10.25 Windows and 12.25 for V-21/V-22. The added time funds full internal delivery
vectors, not a wider method selection. No mutation is executed by this amendment.
Each row below names an exact filter and its control count, with MinExecuted=1
per invocation. Baseline and both mutant/restored phases stay method-scoped.

| Family / exact filter | Variants | Baseline minutes | Red/restore/green per PC | Family floor minutes |
|---|---:|---:|---:|---:|
| F-1; /*/*/CodexCliVersionProbeTests/C959_Parses_and_orders_versions | 13 | 4 | 8.25 | 111.25 |
| F-2; /*/*/CodexCliVersionProbeTests/C959_Probe_is_version_only_and_auth_free | 14 | 4 | 8.25 | 119.5 |
| F-3; /*/*/CodexCliVersionProbeTests/C959_Failure_bounds_and_cleanup | 11 | 4 | 8.25 | 94.75 |
| F-4; /*/*/CodexCliVersionProbeTests/C959_Refresh_replaces_evidence | 12 | 4 | 8.25 | 103 |
| F-5; /*/*/CodexCliVersionProbeTests/C959_Local_and_registration_share_snapshot | 11 | 4 | 8.25 | 94.75 |
| F-6; /*/*/CodexCliVersionWindowsTests/C959_Npm_probe_uses_launch_resolution | 4 | 5 | 10.25 | 46 |
| F-7; /*/*/CodexCliVersionWindowsTests/C959_Native_and_direct_node_probe | 4 | 5 | 10.25 | 46 |
| F-8; /*/*/CodexCliVersionWindowsTests/C959_Unverified_launcher_is_unknown | 5 | 5 | 10.25 | 56.25 |
| F-9; /*/*/RunnerCodexCliEvidenceTests/C959_Heartbeat_updates_only_probe_evidence | 7 | 4 | 8.25 | 61.75 |
| F-10; /*/*/RunnerCodexCliEvidenceTests/C959_Freshness_boundaries | 12 | 4 | 8.25 | 103 |
| F-11; /*/*/RunnerCodexCliEvidenceTests/C959_Generation_change_clears_version | 12 | 4 | 8.25 | 103 |
| F-12; /*/*/RunnerCodexCliEvidenceTests/C959_Catalogue_and_status_project_version | 9 | 4 | 8.25 | 78.25 |
| F-13; /*/*/RunnerCodexCliEvidenceTests/C959_Exact_probe_transport_is_bound | 16 | 4 | 8.25 | 136 |
| F-14; /*/*/CodexCliAdmissionTests/C959_Ladder_and_exact_models_share_floor | 4 | 4 | 8.25 | 37 |
| F-15; /*/*/CodexCliAdmissionTests/C959_Create_refuses_bad_versions | 6 | 4 | 8.25 | 53.5 |
| F-16; /*/*/CodexCliAdmissionTests/C959_Existing_refusals_and_flags_keep_precedence | 5 | 4 | 8.25 | 45.25 |
| F-17; /*/*/CodexCliAdmissionTests/C959_Queued_downgrade_blocks_before_claim | 9 | 4 | 8.25 | 78.25 |
| F-18; /*/*/CodexCliAdmissionTests/C959_Exact_profile_model_wins | 6 | 4 | 8.25 | 53.5 |
| F-19; /*/*/CodexCliAdmissionTests/C959_Profile_launcher_uses_its_own_evidence | 9 | 4 | 8.25 | 78.25 |
| F-20; /*/*/CodexCliAdmissionTests/C959_Override_is_scoped_expiring_and_audited | 23 | 4 | 8.25 | 193.75 |
| F-21; /*/*/CodexCliAdmissionTests/C959_Compatible_launch_keeps_model_and_runner | 23 | 6 | 12.25 | 287.75 |
| F-22; /*/*/CodexCliAdmissionTests/C959_Retry_and_cancellation_recheck | 9 | 6 | 12.25 | 116.25 |

The **Mutation PC floor is 2097 minutes** (95 baseline + 2002 red/restore/green),
plus **4 minutes** inherited-driver/evidence setup = **2101 minutes**.
Combined setup/build + ordinary V/R + all PC floor is **2181 minutes**:
6 Code setup + 74 CP ordinary + 4 Mutation setup + 95 method baselines +
2002 PC cycles. A separate full ordinary Review makes **2255 minutes**.
Authoring, native Windows scheduling, slot waits and findings/repairs are additional.

No broad suite substitutes for a named method. Reuse the known clean baseline
for the same method/source after exact restoration; retain every red/green
receipt independently. Relative to repeating an extra baseline for every variant,
22 method baselines instead of 224 save an estimated **878 minutes**
(162 redundant four-minute + 30 redundant six-minute + 10 redundant five-minute
baselines). Measured savings=0: no execution occurred here. There are **470**
method executions in the PC estimate (22 baselines + 224 red + 224 green),
separate from the 4127 ordinary floor. The larger floor follows independently
testing the guards; it is not a claim of measured runtime or permission to batch
controls sharing a method/file.

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
next: code
handoff: Continue from pushed ee18a226 amendment tip: implement V-21/V-22 E/W contract, remote/busy/durable recovery, launcher/transport/profile/override guards and manual qualification; run Final CP-1..8 with jq, separate Windows Debug CP-2 at final SHA, then Review. 22 feature methods/166 results, Unit floor 3961, 224 post-land PCs; preserve excluded source areas.
artifact: docs/superpowers/plans/2026-10-03-card-0959-runner-codex-version-plan.md
