# CARD-0262: Complete per-agent pin propagation after landed S1

Date: 2026-10-04. Plan task: `02e747d6-a4cd-4f8d-a250-d14c496e3fba`.
Source baseline: `ab7079b9bc51efe79788cc8e585d75276ca0474a`.
Status: implementation plan complete; **TestDesign next**, before further Code.
CARD-0262 remains open. This document delivers no runtime functionality.

## Outcome and authority

Complete the missing path from the existing per-agent pin store to the agent's actual
instructions: an execution-host file, launch composition, durable WhenIdle rereads, and
resume/compaction recovery. Keep the landed capture API and its data. A successful save must
no longer be mistaken for successful propagation.

Start from the [October investigation](../../investigations/2026-10-04-card-0262-a-user-preference-stored-in-kb-does-not-reach-the-agents-own-instructions.md).
The [September 8 plan](2026-09-08-card-0262-kb-preference-to-agent-instructions-plan.md)
remains the detailed requirement catalog, including its review corrections D-1 through D-4,
V-1 through V-31 and PC-1 through PC-62. This continuation supersedes its implementation order,
claims that S1 is future work, obsolete command examples, and instruction to proceed directly
to Code. Its ownership, import-safety and empty-set requirements are retained below.

The live card read in this task is InProgress, revision count 35, platform Any. Its September 8
operator revision releases the earlier build hold and says a file must always be created even
when gitignored, using an agent-unique path to address shared-workspace leakage. Do not restore
the old hold, Dedicated-only file gate, API-only configuration, root `antiphon.md`, or MEMORY
mirror. “Always” applies after first use: a never-used agent still creates no pin file.

The dispatch does not fold TestDesign into Plan. The old verification catalog must be mapped
onto the current code and current checkpoint/Mutation contracts by a separate TestDesign task.
No additional operator decision is required to continue that work. Decisions below retain
recorded product choices and make their implementation consequences explicit.

## Ground truth

Paths in this table are repository-relative. These are source observations at the baseline,
not newly executed runtime tests. The investigation owns historical execution and land receipts.

| Card/older-plan assumption | What the code does now | Consequence |
|---|---|---|
| No pin store/API exists; implement S1 first | `server/Migrations/20260913121031_AddAgentPinnedInstructions.cs`, six pin entities, `server/Application/Services/AgentPinnedInstructionService.cs`, and `server/Api/Endpoints/AgentPinnedInstructionEndpoints.cs` exist. Investigation confirms S1 land `8ccdb1c93d6dc288b05738ee0f7faded35548b4a` is an ancestor. | Preserve S1; add only missing runtime metadata and integration. Historical 27/27 S1 checks are storage/path evidence, not acceptance of S2-S7. |
| Foreign KB rows automatically become instructions | Capture receives text plus optional source strings. No KB reader/importer was found. | Use explicit own-agent capture by the source writer or operator. Deployment never backfills a foreign KB. |
| Successful capture produces a file | `AgentPinnedInstructionService.CaptureAsync` commits desired revision/projection intent; `server/Program.cs:522` registers `NoOpAgentPinnedInstructionReconciler`, which only records calls in memory. | Replace production wiring only when the complete reconciler and its dependencies are ready. Pending remains truthful meanwhile. |
| Persisted target paths are usable on the execution host | `AgentPinPaths.CanonicalCwd` calls server-local `Path.GetFullPath` then replaces `/` with `\\`; first capture uses `DefaultHost = "local"`. `PhoneHomeLaunchPolicy` and sessions have separate runner bindings/cwds. | Resolve host/platform/cwd at the actual execution boundary. S1 paths are desired metadata, not proof of native path validation or remote I/O. |
| Existing projection/cleanup rows already implement custody | `RecordProjectionWriteAsync` sets Ready without a revision/ownership comparison; no production caller exists. `AgentPinCleanupRecord` retains paths but not file/import ownership hashes or ranges. `EnsureLocationCoreAsync` ORs the configured-consumer flag and directory-change handling ignores `previousCwd`. | Add guarded completion, recomputed consumers, pre-I/O intent and retained cleanup evidence before enabling writes/deletes. Do not authorize cleanup from a path alone. |
| Pins reach fresh and resumed launches | `AgentSessionLaunchComposer.ComposeForAgentAsync` passes attached bundles, reply style and `SystemPromptAppend` only. `InstructionBundleComposer` has no pin segment. | Add a common snapshot/renderer and named-only capture protocol; transport through existing Claude/Codex/Grok paths. |
| The configured cwd is always the launch cwd | Card launch composition happens before worktree/runtime location finalization. `AgentSessionService.BuildRuntimeLaunchSpecAsync` uses `session.RunnerCwd ?? cwd`. | Carry prepared pin evidence through the real launch boundary and validate after the final host/cwd is known. A default-cwd file cannot qualify a worktree launch. |
| Every named session is already stamped with pin ownership and adoption | `AgentSession` has `StandingAgentId` and nullable `PinLaunch*`/`PinLastNotified*` fields; `SessionQueuedMessage` has `PinRefreshKey`/requested fields and a unique index. There are no production assignments to the pin fields. | Reuse the columns, wire every named path and add durable generation-specific notification intent. Do not infer ownership from cwd/current card assignment. |
| Generated CLAUDE automatically imports the file | `AgentWorkspaceProvisioner` renders static floor inputs and preserves unmarked files. No pin import exists. `AgentControlService.StartAsync` currently provisions before launch composition. | Pass explicit desired import state to Render; move Start ordering. A separate narrow append writer handles eligible unmarked files only. |
| An event is a reread delivery mechanism | Capture publishes `AgentPinnedInstructionsChanged`; no production pin consumer exists. Queue enqueue supports `deliverIfIdle`, but no pin-key argument/integration. | Durable intent plus idempotent non-inline queue insertion; event/wakeup loss must be recoverable. |
| Existing compaction recovery already covers all pins | `CompactionRecoveryService` gates the generic note on a preamble, saves its watermark before enqueue, and delegates Grok recovery early. | Pin recovery must cover plain/manual named agents and replay crash gaps; combine with Grok's existing rules barrier. |
| General policy refresh is harmless for new pin files | `PolicyRefreshService` can relaunch and shares composition with Grok preflight. `InstructionFileStamps` hashes configured files. | Keep pin drift out of restart decisions without hiding genuine authored/bundle changes or breaking preview/Notify. |
| UI and actual-provider acceptance shipped with S1 | No `AgentPinnedInstructions.tsx`, production file writer, pin reread service or pin launch tests exist beyond the S1 suites. | Complete operator status/actions and isolated acceptance; never close from S1, publication, cleanup, or queue acceptance alone. |

## Decisions

### D-1 — Preserve the per-agent store and explicit capture contract

Keep the current limits, provenance, request idempotency, revision concurrency, replacement,
re-pin and soft-revoke behavior. Agent-source tokens manage only their own named agent's
permitted pins; pool/task/capability principals remain excluded. Header-present auth failures
never fall back to the existing trusted-host operator lane. That lane is not a hostile-tenant
security boundary. The generic capture protocol teaches supported named agents that a KB save
alone is insufficient; only explicit user standing intent or an operator-designated source tag
is eligible. Source corrections/revokes must call the corresponding API.

Reason: the confirmed gap is downstream of storage. Rejected: replacing S1, automatic KB
polling/backfill, promoting arbitrary retrieved text, global bundles of user preferences, and
pin inheritance by ephemeral delegates. CARD-0250 attachment behavior stays separate.

### D-2 — One immutable snapshot, with explicit empty revisions

Load active rows once in existing `(CreatedAt, Id)` order and preserve `AgentPinSnapshotHasher`
semantics. The snapshot carries full owning AgentId, revision/full SHA-256 and immutable pin
IDs/text. Renderer outputs a literal `## Pinned` section with a fence longer than any content
delimiter. Source references/history stay out of launch/file content. Never-used and used-but-
empty are distinct: the latter explicitly replaces all previous pins, including on resume.

Compose attached bundles, the implicit named capture protocol, reply style, pins, then verbatim
operator append. Expand channel placeholders in the appropriate static inputs **before** adding
pin literals. Preserve secret-placeholder rejection and fully resolved provider limits; reject
oversize content rather than truncate. Preflight mutations where a launch is resolvable, and
always validate the actual launch. Paths/generations are structured metadata, not content hashes.

One shared selector controls implicit protocol inclusion for launch, preview/list and policy,
using effective profile kind. Claude, Codex and Grok are supported; Raw/OpenCode still retain
files after first use but report runtime unsupported. Tool-disabled seats get injection without
instructions to bypass their tool policy; live file/API reread is reported unavailable.

Reason: independent rendering/queries can launch a different revision from the file. Rejected:
parsing file edits back into DB, injecting before template expansion, treating last revoke as no
work, mutating reviewed bundles, and updating launch stamps after enqueue to conceal staleness.

### D-3 — Mandatory full-AgentId file, on its actual execution host

The logical path is `cwd/.antiphon/pins/<AgentId:N>/antiphon.md`; use native separators in host
I/O and Windows configuration. The full lowercase 32-hex ID is immutable across rename and
different for a recreated name. Every required configured/live location gets its own projection,
including shared, Unverified, import-Disabled, non-Git and linked-worktree locations. There is
no shared alias or index. Pointers name only that agent's exact verified absolute path and its
revision/hash/location generation, even when the file is gitignored.

Introduce a typed execution-location value and external-I/O seam. Resolve an existing session
from persisted standing ownership plus `RunnerId`/`RunnerStoreId`/`RunnerCwd`; resolve a pending
launch from its finalized routing/worktree specification. For an offline agent resolve its
configured location through the same launch-location policy without starting it. Do not use
the Plan task's runner placement as that agent's execution placement.

The host validates an existing cwd, native canonical path, containment, case/volume semantics
and all components for symlink/junction/reparse ambiguity. Windows paths must never be parsed
by a Linux server's `Path.GetFullPath`, nor POSIX names folded with Windows case rules. Linux
case-distinct directories stay distinct. Bind receipts to runner store identity as well as
logical runner ID; runner replacement cannot inherit Ready just by reusing a name/path.

Use a narrow typed runner operation for inspect/publish/cleanup and native import/Git checks,
shared by local HTTP and phone-home transports; never remote shell commands or a general
arbitrary-file writer. Server-derived AgentId/location/expected bytes/revision define allowed
targets. Advertise capability and return structured Unsupported/Unavailable on older runners.
The real host must confirm bytes; no server-local fallback for a runner-bound session.

Version the path schema and reconcile legacy S1 Pending metadata from authoritative bindings.
Preserve old rows as superseded/cleanup-pending where ownership is uncertain; never reinterpret
their Windows-looking strings on Linux, merge agents, mark them Ready, or erase possible files
blindly. A previously written artifact needs recorded ownership evidence, regardless of what
the no-op baseline normally produced. Store unresolved cases as visible repair work.

Reason: unique filenames do not fix wrong-host writes or cross-platform canonicalization.
Rejected: hard-coded fleet hosts, `local` as proof of location, short IDs/display names, a file
gate based on sharing, new cwd creation, or silently changing the agent's working directory.

### D-4 — Persist intent before I/O; guard completion and cleanup

Add operation-specific pre-I/O evidence: projection/location generation, desired revision/hash,
expected old bytes/hash, intended new bytes/hash, full owner, marker/schema, action and stable
operation identity. Track file and import intent separately; existing generic hash fields cannot
simultaneously describe both. Retained delete cleanup copies the verified file/import custody
record outside the agent FK cascade before deletion commits. Nullable legacy custody refuses
destructive cleanup and remains repairable.

Serialize server reconciliation by projection and host-side publication by canonical path.
Use a durable monotonic operation fence/receipt at the host so a delayed older RPC cannot
recreate a cleaned-up generation or overwrite a newer one. Requests are replayable after lost
responses; receipts prove the same identity/operation/byte hash. Recheck desired state and
expected bytes before atomic same-directory create/replace, and compare-and-set DB completion
against that intent. `RecordProjectionWriteAsync` must no longer set Ready unconditionally.
No filesystem/runner I/O occurs inside the pin mutation transaction.

Recompute configured/live consumers from authoritative metadata rather than sticky flags.
Keep old locations current while any owning session uses them. Last revoke writes an explicit
empty owned file. On retirement remove only verified owned file/stanza/empty leaf; keep siblings
and unrelated `.antiphon` content. An unused cwd confirmed absent on its host retires cleanly;
a required missing cwd, unavailable host or denied inspection remains unresolved. Preserve an
empty tombstone if a user-owned import still requires the old target. A rename alone does nothing.

Use existing ignore coverage or a narrow local Git exclude for this agent's leaf including
temporary files; locate `info/exclude` through Git for linked worktrees/subdirectory cwd. Never
stage runtime files or change project `.gitignore`; refuse a tracked projection even if ignored.
Only trusted intent can recover a crash after publication/before DB receipt; a marker is not proof.
External editors retain the September plan's compare/write TOCTOU limitation, explicitly tested
with a deterministic edit before final comparison; no claim of filesystem ACL isolation.

Reason: save success, file success and cleanup success are different transactions. Rejected:
filesystem work in the DB transaction, unconditional Ready setters, adopting marker-shaped
foreign files, recursive directory removal, and treating API reread as completed file repair.

### D-5 — Keep native Claude imports optional and discovery-safe

Retain September D4 in full. Default Unverified controls **only** the optional import. Dedicated
requires the operator assertion plus canonical discovery-overlap checks, including stopped
registered agents, live task workspaces and ancestor/descendant paths. Serialize admission and
import changes. Shared/Unverified launches use their exact individual file pointers without
adding any pin import to shared CLAUDE. Two unique imports in one shared file still leak both sets.

Check unsafe existing imports before first nonempty publication, including dangling/user-owned
imports and ancestor discovery. Remove only verified owned imports before admitting sharing;
otherwise neutralize a verified owned target and return `pin_import_scope_conflict` until repair.
Do not overwrite user-owned CLAUDE bytes to solve the conflict.

For an eligible unmarked CLAUDE append only the September v2 agent-keyed stanza, preserving BOM,
encoding, separators and every surrounding byte. Equivalent active same-owner user imports
are not adopted; fenced examples, wrong-agent/root imports and forged markers are not ownership.
For managed floors only `AgentWorkspaceProvisioner.Render` writes/removes an import, with explicit
desired import state through every caller. First capture while running records `pending_next_start`
for a managed floor; repeated pin changes do not rewrite it. Preserve ordinary unmarked LeftAlone.

Both writers refuse tracked/staged CLAUDE (`pin_import_target_tracked`), and untracked Git CLAUDE
without existing effective ignore coverage (`pin_import_target_not_ignored`); recheck immediately
before publication. Optional import refusal leaves a safe current mandatory file/launch usable.
An already unsafe import is a discovery conflict, not merely this optional refusal. Do not use
AGENTS.md, MEMORY.md, SOUL.md, CLAUDE.local.md or provider homes as fallback output surfaces.

Reason: preserve authored files and prevent routine `git add -A` from spreading private pointers.
Rejected: unconditional imports in shared scopes, taking over unmarked floors, hiding all CLAUDE
files via excludes, or calling GUID paths confidential against same-user filesystem access.

### D-6 — A real Start must qualify the final location; composition stays readable

Separate nonthrowing snapshot/preview composition from Start preparation. The projection gate
belongs in actual named starts/resumes: `AgentControlService`, the assigned-agent `CardService`
path, and assigned-agent `OrchestratorService` path. Carry typed prepared evidence through
`AgentLaunchComposition`/launch/session request records into `AgentSessionService` and check at
the final runtime launch boundary after worktree/phone-home projection. Stamp immutable owning
AgentId, file identity, revision/hash and location generation for that launch. A resume gets a
new read obligation even when the snapshot hash matches.

Required order: resolve actual host/cwd and ownership; check discovery safety; load snapshot;
reconcile/verify file; derive optional import state; provision/render eligible floor; compose;
recheck revision/location/scope before calling the runner. A concurrent semantic update forces
current recomposition or explicit retry/refusal, never a knowingly stale launch. Existing live
Start idempotency stays intact. Never-used launches do not require a pin projection.

Missing/unverified mandatory files refuse new/resumed pin-bearing starts with
`pin_projection_unavailable`; known stale revoked projections that cannot be neutralized refuse
with `pin_projection_stale`. Existing working sessions are not killed. Preview/list/drift and
both Grok policy preflight calls return availability/reason rather than throwing pin-file errors.
Existing provider, budget, credentials and legacy Grok resume refusals stay enforced.

Reason: composition is also a read/diagnostic operation; early composition cannot verify a later
worktree path. Rejected: fail preview on file errors, accepting default-cwd receipts for a card
worktree, and counting replacement argv as evidence that a resumed provider adopted new pins.

### D-7 — Durable WhenIdle obligations with honest receipts

Add a persisted `AgentPinNotification` intent keyed by owning session and trigger identity,
including launch/conversation generation, projection/location generation and target revision/hash.
Triggers cover content change, path change, resume and compact sequence. Reuse the existing
nullable queue pin fields and unique index; add a typed enqueue overload/payload, not ad hoc
post-insert field mutation. Generate bounded stable keys that fit the existing 80-character limit.

Mutation commits desired state first; reconciler writes files, persists notification intent,
then idempotently inserts/obtains a WhenIdle System queue row with `deliverIfIdle:false` and
links coverage before normal queue processing. Crash at any boundary replays safely. Coalesce
only never-attempted rows; attempted bytes and their verdict remain immutable. A newer change
after an attempted note leaves a later obligation. Queue retry/park/cancel policy remains the
owner; exhausted retries do not generate endless new keys.

Notify only sessions proved owned by the named agent. `StandingAgentId` is primary; legacy
backfill requires an unambiguous persisted ownership link, never cwd, transcript recency or the
card's later assignee. Cancel obsolete session work; do not retarget attempted messages. Extend
stranded scans to pin System rows on manual/plain agents. Offline capture starts no process.

A normal note names exact owner/file/revision/hash and says read the complete current set, replace
older snapshots including removals, do not search siblings or edit the file, and perform no
unrelated work. Only actual file failure selects an explicitly degraded own-agent API recovery
note; that continuity path never clears mandatory file failure or enables a new launch.

Only complete owning transcript UserPrompt evidence advances `PinLastNotified*` (“Reread
requested”). Enqueue means Queued; screen-only means Unverified. Neither proves a file read or
model compliance. Keep launch evidence separate. Confirmed prompts with no assistant reply are
not retyped. Queue owns LF/bracketed paste/separate Enter and the committed-state idle gate.

Pin turns are recognized from persisted queue identity and matching prompt evidence in channel
text/attachment routing, task turn selection and boot evidence. Reuse the current backed-rules
housekeeping pattern, including `TaskReportTurnSelector`/`TaskReportHousekeeping`; text prefixes
alone never suppress real human prompts. Internal answers cannot settle a task or publish a PDF.

Reason: event callbacks and inline queue delivery leave crash gaps and false receipts. Rejected:
raw input, interrupt/restart, broadcast by cwd, treating `NO_REPLY` as the routing guard, and
acknowledging delivery from a screen redraw or unrelated assistant text.

### D-8 — Pin refresh is independent of policy restarts and survives compaction

Separate static policy stamps from dynamic pins. Normalize only exact owned paths/ranges whose
bytes match pre-I/O intent (including publication-before-success crash windows); recompute managed
floor marker effects without the pin stanza. Never ignore every `antiphon.md` or `.antiphon` file.
An explicitly configured pin file still cannot put pin-only changes in the restart lane. Preserve
genuine authored/bundle changes and establish compatible legacy baselines without false drift.
Off disables ordinary policy refresh, not requested pin-change delivery.

For Claude/Codex, persist compact generation/sequence obligations independently of the generic
recovery watermark and preamble/AlwaysOn eligibility. Startup, transcript catch-up and replay
recover unserved boundaries. Combine with a workspace note only when appropriate; plain agents
get no invented memory ritual. Automatic compact remains mid-turn until the real idle boundary.
Integrate with normal resume/start notes without losing their ordering or duplicating turns.

For Grok, extend the existing rules refresh message with the current external pin reread and
link coverage to that one message. Preserve rules receipt/hash/ACK, queue barrier and bounded
compaction behavior; ACK does not independently prove pin adoption. The static protocol says
the embedded rules snapshot is historical and the newest complete pin set wins. Do not rewrite
the running rules file through a second transport. After revoke, resume and two compactions
must not resurrect the old snapshot. Existing Check-seat recovery/continuity policies remain intact.

Reason: changes should apply at idle without process churn, and context reset is a new read
obligation even at the same hash. Rejected: generic watermark as the pin outbox, a second Grok
recovery turn, bypassing its barrier, and success-only/blanket drift normalization.

### D-9 — Separate operator evidence and activate only a complete path

Show saved state, each required projection, optional import, immutable launch snapshot, queued/
failed reread and transcript-confirmed request separately. Report effective profile support,
tool limitations and retained cleanup; do not call any of them model compliance. Pin mutation
events/activity/attention carry IDs/action/revision/status, never raw text/source references or
credentials. Record one semantic activity item per actual change, not replay/no-op.

The first production activation includes projection, notification, recovery and policy isolation
together. Earlier slices land additive dormant services and tested contracts; they keep the S1
no-op registration and do not include the new protocol in production until activation. This is
an implementation sequencing boundary, not an optional permanent API-only product mode.
Runner capability must be deployed before activation on a pin-bearing host; an old runner
reports unresolved projection and blocks only pin-bearing starts, not unrelated no-history work.

The protocol deliberately changes static policy for supported named agents even with zero pins.
Account for the existing policy's one-time eligible idle relaunch wave; verify a second sweep
does not repeat it. Do not mass-edit stored custom preambles. Rollback preserves DB rows and
ownership receipts; quiesce pin-bearing use and perform verified cleanup if reverting code would
leave stale discoverable content. Do not drop tables or recursively remove runtime directories.

## Implementation slices

Use the existing S-n names where possible. `S2-core` and `S2-launch` split the old S2 to remove
its dependency cycle with file verification. Order: **S2-core → S3a → S3b → S4 → S5 → S2-launch
→ S6 → S7**. S1 is landed and is regression scope only. S3a and S3b are separate Code tasks;
do not combine projection/import work with S4 queue work. Each slice commits/pushes its own
meaningful changes and runs its approved checkpoints. Do not activate dormant slices early.

New paths below are proposals for Code to create; unmarked paths already exist. Application
owns pure composition/reconciliation policy; Infrastructure and runner own external I/O. Use
concrete services except at I/O seams. Schema additions use CLI-generated migrations.

| Slice / closes | Production files and changes | Tests (new unless marked existing) |
|---|---|---|
| S2-core: shared snapshot contract | New `server/Application/Services/AgentPinSnapshotLoader.cs`, `AgentPinRenderer.cs`, `NamedAgentInstructionSelection.cs`; extend existing `AgentPinSnapshotHasher.cs`, `InstructionBundleComposer.cs`, `InstructionBundles.cs`; add dormant `server/Bundles/standing-instructions.md`. Keep actual launch inclusion dormant. | `tests/Antiphon.Tests/Application/AgentPinnedInstructionCompositionTests.cs`; existing `InstructionBundleTests.cs`, `AgentPinPathTests.cs`, `AgentPinnedInstructionServiceTests.cs`, `AgentPinnedInstructionEndpointTests.cs`. Preserve S1 corpus; verify literal/empty snapshots and budgets with independent expected bytes. |
| S3a: host-native projection and custody | New `server/Application/Services/AgentPinnedInstructionWorkspaceService.cs`, `server/Application/Interfaces/IAgentPinWorkspaceClient.cs`, `server/Infrastructure/Agents/SessionRunner/AgentPinWorkspaceClient.cs`, `src/Antiphon.SessionRunner.Contracts/AgentPinWorkspaceContracts.cs`, `src/Antiphon.SessionRunner/AgentPinWorkspaceStore.cs`; extend `AgentPinPaths.cs`, `AgentPinnedInstructionService.cs`, projection/cleanup entities, `AppDbContext.cs`, migrations; wire typed local HTTP/phone-home operations through `ISessionRunnerClient.cs`, client implementations, `PhoneHomeContracts.cs`, `PhoneHomeCommandDispatcher.cs` and runner `Program.cs`. Implement capability, inspect/write/cleanup receipt, compare/fence, native Git exclusion, legacy location upgrade, consumer retirement. Production pin reconciler still dormant. | `tests/Antiphon.Tests/Application/AgentPinWorkspaceTests.cs`, `AgentPinLocationTests.cs`, `AgentPinTransportTests.cs`; `tests/Antiphon.SessionRunner.Tests/AgentPinWorkspaceStoreTests.cs`, `AgentPinWorkspaceWindowsTests.cs`; existing path/store suites and `PhoneHomeCommandDispatcherTests.cs`. Actual disk and both host transports, native Linux case/symlink and Windows alias/junction cases, lost response/delayed RPC, cleanup after cascade. |
| S3b: optional imports and launch-scope admission | New `server/Application/Services/AgentPinClaudeImportService.cs`; extend workspace service/host store, `AgentWorkspaceProvisioner.cs`, `AgentService.cs` import-mode/cwd/kind handling and retained custody model. Keep activation dormant. Read `docs/agent-workspaces.md` and `docs/agent-instruction-file-contract.md` before their eventual S6 changes. | `tests/Antiphon.Tests/Application/AgentPinImportTests.cs`, `AgentPinImportWindowsTests.cs`; extend workspace/location/Windows classes; existing `AgentWorkspaceProvisionerTests.cs`. Shared and ancestor imports, tracked/staged refusal, dangling user import before creation, BOM/CRLF/no-newline byte restoration, Render-only managed imports. |
| S4: durable reconcile and queue | New `server/Application/Services/AgentPinnedInstructionReconciler.cs`, `AgentPinNotificationService.cs`, `server/Domain/Entities/AgentPinNotification.cs`; extend `SessionMessageQueueService.cs`, session/queue entities/DTOs and migration for durable triggers/coverage; `ChannelReplyDispatcher.cs`, `TaskReportTurnSelector.cs`, `TaskReportHousekeeping.cs`, `BootReplyWatch.cs`, `AttentionService.cs`, `AgentSupervisorService.cs`. Add bounded startup/backstop wakeups, queue receipt reconciliation and plain-agent sweep. No production replacement of no-op yet. | `tests/Antiphon.Tests/Application/AgentPinRefreshTests.cs`, `AgentPinInternalTurnTests.cs`; existing `SessionMessageQueueDeliveryVerificationTests.cs`, `SessionMessageQueueServiceTests.cs`, `ChannelMachineTurnTextTests.cs`, `ChannelMachineTurnMatchTests.cs`, `TaskReportHousekeepingTests.cs`, `AgentTaskReplyIntegrationTests.cs`, `BootReplyWatchTests.cs`. Persisted identity, injected crash gaps, attempted-body immutability and zero unrelated side effects. |
| S5: policy and context-reset integration | Extend `PolicyRefreshService.cs`, `InstructionFileStamps.cs`, `AgentService.cs`, `CompactionRecoveryService.cs`, `AgentSessionRuntime.cs`, `ChannelPreamble.cs`, `GrokRulesRefreshService.cs`; use shared selector and current snapshots. Add recovery hooks but activate them with S2-launch. Preserve no-history behavior. | `tests/Antiphon.Tests/Application/AgentPinPolicyTests.cs`, `AgentPinRecoveryTests.cs`; existing `PolicyRefreshServiceTests.cs`, `InstructionFileStampTests.cs`, `CompactionRecoveryTests.cs`, `GrokRulesCompactionRecoveryTests.cs`, `GrokRulesQueueBarrierTests.cs`, `GrokRulesTransportCompatibilityTests.cs`. Nonthrowing Grok preflight, intent-crash normalization, real drift, two compact boundaries after revoke. |
| S2-launch: final-location integration and production activation | Extend `AgentSessionLaunchComposer.cs`, `AgentControlService.cs`, `CardService.cs`, `OrchestratorService.cs`, `AgentSessionService.cs`, `PhoneHomeLaunchPolicy.cs` and typed launch/session records. Add snapshot/batch preview to `AgentService.cs`; extend pin status DTOs/endpoints; replace no-op DI and register recoverable work in `server/Program.cs` only now. Install protocol, enforce actual Start gates, stamp every named fresh/resume/card path, and wire mutation/patch/delete/startup wakeups. | Extend composition/location/refresh/recovery/policy classes; new `tests/Antiphon.Tests/Application/AgentPinLaunchIntegrationTests.cs`. Existing `AgentSystemPromptLaunchTests.cs`, `NamedCodexAgentLaunchTests.cs`, `GrokRulesCompositionTests.cs`, `DelegateBundleLaunchTests.cs`, `PhoneHomeStandingLaunchTests.cs`, `AgentSessionLaunchQueueOwnershipTests.cs`. Drive production DI, all three named entry paths and finalized remote/worktree cwd; fake Ready alone cannot pass. |
| S6: operator UI and owned documentation | New `client/src/features/agents/AgentPinnedInstructions.tsx`; extend `AgentSettingsModal.tsx`, `client/src/api/agents.ts`, `client/src/hooks/useSignalRInvalidation.ts`. Update `docs/agent-workspaces.md`, `docs/agent-instruction-file-contract.md`, `docs/agent-kinds.md`, `docs/agent-credentials.md`, `docs/session-runtime-invariants.md`, `docs/antiphon-api.md` and capture references in relevant channel/orchestration owner docs. No generated `docs/cards/` edits. | New `client/src/features/agents/AgentPinnedInstructions.test.tsx`; existing `AgentBundleAttachments.test.tsx`, `client/src/hooks/useSignalRInvalidation.test.ts`. Preserve pin and SystemPromptAppend drafts on conflict/invalidation; saved/file/import/requested states and repair remain independent. |
| S7: isolated runtime and provider qualification | New `tests/Antiphon.E2E/AgentPinnedInstructionsE2ETests.cs`, `tests/Antiphon.Tests/Agents/AgentPinClaudeImportCanaryTests.cs`, `AgentPinBehaviorAcceptanceTests.cs`; helpers under existing test helper directories. Reuse isolated server/runner, DB, provider-home and stub fixtures. Add no production backfill or provider-home copying. | API/UI → actual file → launch → busy change → idle note → replace/revoke → resume → two native compactions. Claude native-only import/cross-load request oracle separately from injected context. Claude/Codex/Grok behavioral challenges retain September V-30's unseen-answer protocol and independent read evidence. |

## Test-design handoff and checkpoint proposal

This is **not** the completed Verification design and does not authorize Code to execute a
partial manifest. TestDesign adds `## Verification design`, freezes exact method/data rosters,
maps inherited V/R/PC obligations to the new slices/classes, and replaces the proposal below
with the executable closed list before settling `next: code`. Keep every September acceptance
obligation; do not interpret its “new” labels or historical test counts as present coverage.

Required acceptance mapping:

| Coverage family | Inherited obligations | Additions from current ground truth |
|---|---|---|
| A1 storage and rendering | V-1–V-5; R-7/R-11; PC-32–35/51–53/56 | Preserve existing S1 tests; raw text cannot be templated; shared implicit selection; no production activation during S2-core. |
| A2 host projection and lifecycle | V-6/V-7/V-13–17; R-1/R-2/R-5/R-6 | Mixed server/runner OS, Linux case sensitivity, Windows junctions, runner-store rebinding, legacy S1 metadata, delayed stale RPC after cleanup, guarded Ready and custody surviving cascade. |
| A3 import ownership/isolation | V-8–V-12/V-31; R-3/R-4 | Existing user import before first target creation; nonoverlap admission; both writer paths; final staging race; no tracked import after fixture `git add -A`. |
| A4 durable notification | V-18–V-21/V-25; R-8/R-9/R-11 | DB intent and unique key through actual queue seam, spill-aware complete UserPrompt matching, manual agents, production internal-turn consumers and current CARD-0714 task report logic. |
| A5 policy/recovery | V-22–V-24; R-9/R-10 | Current committed session-state gates, no pin-only relaunch, true policy drift retained, static rollout once only, no-preamble/manual compact, native resume ownership and Grok barrier preserved. |
| A6 launch activation | V-4/V-5/V-13/V-16/V-19/V-22 | Production DI is no longer no-op; actual final runtime cwd/host on all entry paths; partial/old runner capability refusal; null/unknown legacy ownership; concurrent newer revision before Start. |
| A7 UI | V-26; R-12 | Effective profile kind rather than stored Kind; separate request/file/cleanup state, byte-sensitive text rendering and preserved drafts. |
| A8 journey/provider | V-27–V-30 | Independent file bytes, serialized model context, actual file-read and challenge outputs; two same-cwd identities with colliding short-ID prefixes; absent prerequisites stay pending. |

TestDesign must use real Postgres with test-scoped schemas/IDs, actual fixture files and Git
worktrees, and deterministic barriers around publication/receipt/queue boundaries. A fake returning
Ready or a renderer compared with itself is not a useful oracle. Assert zero runner start/input/
stop and zero outbound sends where required, alongside positive success. Never target production
runner ports, broker or workspaces. Program-boot tests use the established refusing/isolated guard.

The September PC catalog remains sensitivity work, with new host/fence controls added. Map
each arm to one exact test method and assertion. **Mutation is a separate post-land stage** under
today's SourceLanding contract; the old prose assigning all PCs to Code is superseded. SourceLanding
keeps evidence external and never commits from the snapshot. Code/ordinary Review complete their
V/R profile; Review and publication do not claim PC-clean. Do not copy old wildcard whole-class
PC commands: each red/restored-green cycle is method-scoped, and fixture/build errors are not red.

### Platform lanes

Read-only platform discovery in this task: `/api/runner-defaults` revision 2 uses a global
preference and no per-kind overrides; `/api/session-runners` reported eligible accepting Linux
and Windows lanes, plus an unavailable/draining entry. This is a dated observation, not a dispatch
reservation. No fleet location is embedded in this plan. Re-read both routes before dispatch;
the caller additionally applies pipeline/host limits. Normally omit `-Runner` and `-Platform`.
Use `-Platform Windows` only for the Windows filesystem/native-provider subset; use `-Platform
Linux` for native POSIX filesystem qualification if the default resolves elsewhere. `-Platform Any`
explicitly removes an inherited pin. Runner pinning is only for a justified single-host need.

Each Group below names its lane: `portable` runs on the eligible default; `linux` proves native
POSIX behavior; `windows` proves NTFS/native process behavior; `provider` requires the provider
and isolated authenticated/stub fixture on a compatible lane chosen at dispatch. TestDesign must
separate platform-specific methods so ordinary rows cannot silently skip them.

### Checkpoints

Proposal for TestDesign to finalize. All class filters name planned or existing classes in the
slice table. `Min=1` on proposed suites is only a nonzero floor, **not a claim that one test covers
the suite**; TestDesign derives actual execution floors/Expect rosters from its method/data design.
Names must remain exact (escape Markdown OR pipes). Split oversized regression groups by named
methods/classes if measurement exceeds a foreground window; do not widen to the whole assembly.
Rows sharing a build have identical After groups. Estimates include builds and are not measurements.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S2-core | `tests/Antiphon.Tests -> bin-c262-core/` | portable-snapshot-store | `/*/*/(AgentPinnedInstructionCompositionTests)\|(AgentPinnedInstructionServiceTests)\|(AgentPinnedInstructionEndpointTests)\|(InstructionBundleTests)/*` | A1 | all selected methods/data, 0 failed/skipped | 1 | 10 | false | n/a |
| CP-2 | S3a | `tests/Antiphon.Tests -> bin-c262-projection/` | portable-projection | `/*/*/(AgentPinWorkspaceTests)\|(AgentPinLocationTests)\|(AgentPinTransportTests)\|(AgentPinPathTests)/*` | A2 | all selected methods/data, 0 failed/skipped | 1 | 12 | false | n/a |
| CP-3 | S3a | `tests/Antiphon.SessionRunner.Tests -> bin-c262-host/` | linux-native-host | `/*/*/(AgentPinWorkspaceStoreTests)\|(PhoneHomeCommandDispatcherTests)/*` | A2 | all selected methods/data, 0 failed/skipped | 1 | 10 | true | n/a |
| CP-4 | S3a | `tests/Antiphon.SessionRunner.Tests -> bin-c262-ntfs/` | windows-native-host | `/*/*/AgentPinWorkspaceWindowsTests/*` | A2 | all Windows alias/junction/custody cases, 0 failed/skipped | 1 | 10 | true | n/a |
| CP-5 | S3b | `tests/Antiphon.Tests -> bin-c262-import/` | portable-imports | `/*/*/(AgentPinImportTests)\|(AgentPinWorkspaceTests)\|(AgentWorkspaceProvisionerTests)/*` | A2, A3 | all selected methods/data, 0 failed/skipped | 1 | 12 | false | n/a |
| CP-6 | S3b | `tests/Antiphon.Tests -> bin-c262-import-ntfs/` | windows-native-import | `/*/*/AgentPinImportWindowsTests/*` | A3 | all Windows import/junction/byte-custody cases, 0 failed/skipped | 1 | 10 | true | n/a |
| CP-7 | S4 | `tests/Antiphon.Tests -> bin-c262-queue/` | portable-pin-queue | `/*/*/(AgentPinRefreshTests)\|(AgentPinInternalTurnTests)\|(SessionMessageQueueDeliveryVerificationTests)/*` | A4 | all selected methods/data, 0 failed/skipped | 1 | 12 | false | n/a |
| CP-8 | S4 | CP-7 | portable-queue-routing-regressions | `/*/*/(SessionMessageQueueServiceTests)\|(ChannelMachineTurnTextTests)\|(ChannelMachineTurnMatchTests)/*` | A4 | all selected methods/data, 0 failed/skipped | 1 | 8 | false | n/a |
| CP-9 | S4 | CP-7 | portable-report-boot-regressions | `/*/*/(TaskReportHousekeepingTests)\|(AgentTaskReplyIntegrationTests)\|(BootReplyWatchTests)/*` | A4 | all selected methods/data, 0 failed/skipped | 1 | 8 | false | n/a |
| CP-10 | S5 | `tests/Antiphon.Tests -> bin-c262-recovery/` | portable-policy | `/*/*/(AgentPinPolicyTests)\|(PolicyRefreshServiceTests)\|(InstructionFileStampTests)/*` | A5 | all selected methods/data, 0 failed/skipped | 1 | 10 | false | n/a |
| CP-11 | S5 | CP-10 | portable-recovery | `/*/*/(AgentPinRecoveryTests)\|(CompactionRecoveryTests)\|(GrokRulesCompactionRecoveryTests)/*` | A5 | all selected methods/data, 0 failed/skipped | 1 | 8 | false | n/a |
| CP-12 | S5 | CP-10 | portable-grok-barrier | `/*/*/(GrokRulesQueueBarrierTests)\|(GrokRulesTransportCompatibilityTests)/*` | A5 | all selected methods/data, 0 failed/skipped | 1 | 5 | false | n/a |
| CP-13 | S2-launch | `tests/Antiphon.Tests -> bin-c262-launch/` | portable-pin-activation | `/*/*/(AgentPinLaunchIntegrationTests)\|(AgentPinnedInstructionCompositionTests)\|(AgentPinLocationTests)/*` | A1, A2, A6 | all selected methods/data, 0 failed/skipped | 1 | 12 | false | n/a |
| CP-14 | S2-launch | CP-13 | portable-launch-regressions | `/*/*/(AgentSystemPromptLaunchTests)\|(NamedCodexAgentLaunchTests)\|(GrokRulesCompositionTests)\|(DelegateBundleLaunchTests)/*` | A6 | all selected methods/data, 0 failed/skipped | 1 | 8 | false | n/a |
| CP-15 | S2-launch | CP-13 | portable-launch-ownership | `/*/*/(PhoneHomeStandingLaunchTests)\|(AgentSessionLaunchQueueOwnershipTests)/*` | A6 | all selected methods/data, 0 failed/skipped | 1 | 6 | false | n/a |
| CP-16 | S2-launch | CP-13 | portable-activation-recovery | `/*/*/(AgentPinRefreshTests)\|(AgentPinPolicyTests)\|(AgentPinRecoveryTests)/*` | A4, A5, A6 | all selected methods/data against production activation, 0 failed/skipped | 1 | 10 | false | n/a |
| CP-17 | S6 | n/a | portable-client | `pwsh -NoProfile -File scripts/test-client.ps1 AgentPinnedInstructions.test AgentBundleAttachments.test useSignalRInvalidation.test` | A7 | all 3 files executed, 0 failed/skipped; TD freezes test count | n/a | 4 | false | n/a |
| CP-18 | S7 | n/a | portable-client-build | `npm --prefix client run build` | A8 | exit 0 and freshly built client/dist for CP-19 | n/a | 4 | true | n/a |
| CP-19 | S7 | `tests/Antiphon.E2E -> bin-c262-e2e/` | portable-isolated-journey | `/*/*/AgentPinnedInstructionsE2ETests/*` | A7, A8 | all journey cases, 0 failed/skipped | 1 | 15 | true | n/a |
| CP-20 | S7 | `tests/Antiphon.Tests -> bin-c262-cli/` | provider-claude-import | `/*/*/AgentPinClaudeImportCanaryTests/*` | A3, A8 | V28/V29 context canaries, 0 failed/skipped | 1 | 20 | true | `ANTIPHON_REAL_CLI_STUB_TESTS=1` |
| CP-21 | S7 | CP-20 | provider-claude-behavior | `/*/*/AgentPinBehaviorAcceptanceTests/V30_Claude_*` | A8 | all six challenge phases plus read evidence, 0 failed/skipped | 1 | 30 | true | `ANTIPHON_PIN_BEHAVIOR_TESTS=1;ANTIPHON_HEADED_TESTS=1` |
| CP-22 | S7 | CP-20 | provider-codex-behavior | `/*/*/AgentPinBehaviorAcceptanceTests/V30_Codex_*` | A8 | all six challenge phases plus read evidence, 0 failed/skipped | 1 | 30 | true | `ANTIPHON_PIN_BEHAVIOR_TESTS=1;ANTIPHON_CODEX_HEADED_TESTS=1` |
| CP-23 | S7 | CP-20 | provider-grok-behavior | `/*/*/AgentPinBehaviorAcceptanceTests/V30_Grok_*` | A8 | all six challenge phases plus read/rules evidence, 0 failed/skipped | 1 | 120 | true | `ANTIPHON_PIN_BEHAVIOR_TESTS=1;ANTIPHON_HEADED_TESTS=1;ANTIPHON_GROK_RULES_LIVE_TESTS=1;ANTIPHON_GROK_RULES_ENDURANCE_TESTS=1` |

TestDesign must resolve provider/OS fixture prerequisites and compatible build reuse for CP-20–23;
if providers require different platforms, each gets its own build row. The Grok estimate preserves
the old bounded 120-minute workload, not permission to bypass checkpoint limits. Import a YAML
manifest with an explicit supported timeout pin or design valid bounded phase rows retaining one
isolated scenario's evidence; do not pretend a 45-minute default qualifies the whole workload.
CP-6 retains the separate Windows import/reparse qualification after S3b.

### Execution and cost constraints for TestDesign

Run the finalized manifest through `tools/Antiphon.Checkpoints` once per committed slice group,
with `--expected-source-sha`, selected After/rows, and foreground `wait` until no exit 75 remains.
The tool's own build/launch driver goes through `scripts/build-slot.ps1`; row builds/commands use
the checkpoint gate. Slot refusal/timeout is not permission for an unleased retry. Preserve exact
CHECKPOINT lines and validate source receipts. Generated receipts/TRX/logs remain ignored.

Use forward-slash `bin-<name>/` outputs and remove every owned output directory on completion.
No source edits during an in-flight run. Keep test assemblies/process-spawning runs serial where
required and use their assembly-local process limiter. Run only the approved scoped profile,
not an unrelated full suite; prove inherited reds at the base using the failing methods. Code/
Review run `scripts/check-evidence-diff.ps1` over the full task range.

The proposed ordinary checkpoint estimate sums to **374 minutes**, including the provider
qualification ceiling; this is a planning estimate, spread across separate slice dispatches.
TestDesign recalculates from its final table and actual lane constraints, adds authoring time to
dispatch budgets, and budgets post-land Mutation separately. Do not convert assertion counts or
minutes into MinExecuted. Missing provider/OS prerequisites mean pending acceptance, not green.

## Release and completion

1. TestDesign commits/pushes the current verification design and final checkpoint manifest. Code
   implements the ordered slices; independent Review checks exact source/receipts. Land and
   activate through the existing orchestration/release owners, with runner capability first.
2. Confirm the served `/api/version` and runner capability/version for the code actually activated.
   Exercise synthetic isolated acceptance before any production preference capture. Observe
   pending S1 intents converging and the one-time static-protocol drift, without blanket restarts.
3. Before repairing the original incident, identify the actual owning AgentId, provider, host,
   cwd and original external KB bytes/source key on its owning deployment. Those facts were
   unavailable to the investigation and remain unclaimed here. An authorized explicit capture
   or source-writer integration is required; no automatic foreign-store synchronization is promised.
4. Completion evidence must distinguish saved revision, real file bytes/owner/location, launch
   revision, queue receipt, native read and observed behavior. Include last revoke, resume, two
   compactions and shared-workspace isolation. Any unavailable provider acceptance stays visible.
   Use the current separate post-land Mutation companion policy; no S1 or cleanup-only closeout.

No source, card state, runtime settings, sessions, KB rows or live pins changed during Plan.
No builds/tests/provider probes ran. Validation for this artifact is source/path reconciliation,
read-only card/platform discovery, checkpoint-table consistency and Git diff whitespace checks.

--- next stage ---
next: test-design
handoff: Reconcile the September V/R/PC catalog onto this S1-aware continuation, freeze method/data rosters and execution-lane checkpoints, and add Verification design before Code. Preserve mandatory full-AgentId files, final-host launch gating, durable rereads and post-land Mutation separation; keep CARD-0262 open.
artifact: docs/superpowers/plans/2026-10-04-card-0262-pin-propagation-continuation-plan.md
