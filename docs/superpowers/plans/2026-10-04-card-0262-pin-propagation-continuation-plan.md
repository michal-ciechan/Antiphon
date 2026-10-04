# CARD-0262: Complete per-agent pin propagation after landed S1

Date: 2026-10-04. Plan task: `02e747d6-a4cd-4f8d-a250-d14c496e3fba`.
Source baseline: `ab7079b9bc51efe79788cc8e585d75276ca0474a`.
Status: implementation plan and appended verification design complete; **Code next**, ordered by slice.
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

### Historical checkpoint proposal (superseded by Verification design)

Proposal for TestDesign to finalize. All class filters name planned or existing classes in the
slice table. `Min=1` on proposed suites is only a nonzero floor, **not a claim that one test covers
the suite**; TestDesign derives actual execution floors/Expect rosters from its method/data design.
Names must remain exact (escape Markdown OR pipes). Split oversized regression groups by named
methods/classes if measurement exceeds a foreground window; do not widen to the whole assembly.
Rows sharing a build have identical After groups. Estimates include builds and are not measurements.

**Budgeted S3a.2 split (Code f9183542, 2026-10-04).** Next in S3a order is the
dormant Linux x64 publication/custody/fence primitive. CP-29 specifies six native
publication methods, the complete three-method inspection class and the two adjacent
phone-home admission methods (11 executions). Commit the executable failing tests
before implementing the primitive, then rerun the same row. File intent lives in a
runner-owned journal outside workspace discovery, keyed by canonical owner path;
its monotonic fence survives reconstruction and cleanup. Exact operation replay must
verify current disk bytes. Only journal-proven custody permits replacement/deletion;
marker-shaped content never grants custody. Final comparisons preserve editor bytes,
and cleanup removes only the file and an empty owner leaf. This is process-crash
recovery with file/directory flushes, not ACL isolation from same-user edits.

This part accepts non-Git workspaces only: any ancestor `.git` refuses publication
until the native exclude/tracked-target implementation lands. No transport, capability,
DI activation, server intent/CAS, legacy upgrade, import custody or Windows support is
added. CP-3–6 and full V/R remain owed; CP-29 covers host portions of V-13–15/V-34,
R-5/R-6 and the existing inspection portions of V-32 only. All PC-1–207 and native
variants remain pending SourceLanding Mutation. Whole Unit and Windows remain
explicitly deferred by this dispatch. Added ordinary estimated cost: 10 minutes per
CP-29 run; red then green planned, no repeats after green.

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

## Verification design

TestDesign: 2026-10-04, task `9579b548`, inspected source `723e2e0902559dad5e54f58c77f21d27770f39df`. This appended design is the executable verification authority for the continuation; D-1 through D-9 and the implementation slices above are unchanged. The earlier 23-row table is historical proposal only. CARD-0262 remains open. No production code, live pins, deployment, provider probe, build or application test is delivered by this stage.

September V-1–31 and R-1–12 retain their meanings and all boundary obligations. V-32–35 add native location, migration/receipt, transport and final activation proof. September PC references below are prefixed **Sep-**; the new PC-n inventory splits their independent arms and adds October guards. It supersedes September's grouping/execution assignments, not its safety requirements. Code runs ordinary V/R, Review judges evidence/design before land, and a separately commissioned SourceLanding Mutation runs all controls after land.

### Inspection

The following are body reads, not conclusions from filenames or historical passing counts. Existing test rosters below were additionally enumerated from source; enumeration is not execution.

| Bodies read | Boundaries → obligations |
|---|---|
| Complete `AgentPinnedInstructionServiceTests`, `AgentPinnedInstructionEndpointTests`, `AgentPinPathTests`; their World/SeedLiveSession/FreshDb helpers; production Capture/Revoke, RecordProjectionWriteAsync, EnsureLocationCoreAsync and PreserveCleanupOnDeleteAsync | V-1–3, V-6, V-7, V-14, V-15, V-33; R-1, R-2, R-5, R-6, R-11. S1's unconditional Ready assertion must be replaced by a valid host receipt, not preserved as desired behavior. Its persistence/replay corpus stays. |
| `InstructionBundleTests` composition/order/budget bodies; `AgentSessionLaunchComposer.ComposeForAgentAsync/PeekProfileKindAsync`; initial named Codex, Grok payload, delegate and launch-ownership tests | V-4, V-5, V-32, V-35; R-7. Independent expected bytes replace renderer self-comparison. |
| `AgentWorkspaceProvisionerTests` empty/stale/unmarked/marker bodies; `InstructionFileStampTests` compute/drift bodies; `GrokRulesFileStoreTests` complete StoreFixture and file tests | V-6–17, V-22, V-31; R-1–6, R-9. New pin workspace/import tests use these nearest filesystem fixtures. |
| `PhoneHomeCommandDispatcherTests.Unsupported_operation_or_launch_never_enters_runtime`, `Workspace_ops_are_admitted_only_under_allowed_cwd`, pre-ack restart body; `LocalHttpRunner.StartAsync/CrashAsync`; `PhoneHomeStandingLaunchTests` binding bodies | V-32–35. Existing LocalHttpRunner is Windows .exe-specific; new portable loopback fixture needs DLL launch, not an OS skip. |
| Complete `BridgeQueueHarness`, including AttachSession, default OnSubmitted, InsertEntry and disposal; queue Enqueue/flush entry points; selected receipt tests listed in ER below and LF/paste/Enter bodies | V-18–21, R-8/R-9. Harness default only seeds PersistentSessionId; new fixture explicitly persists StandingAgentId, launch generation and runner binding. Default automatic UserPrompt insertion is disabled in negative evidence rows. |
| `CompactionRecoveryTests` replay/plain/manual/auto bodies, complete `GrokRulesCompactionRecoveryTests`, `GrokRulesQueueBarrierTests`, `GrokRulesTransportCompatibilityTests`; production compact service | V-23/V-24, R-8–10. Generic watermark/preamble gating is insufficient. Raw Windows argv compatibility method skips on Linux, so CP-16 is Windows. |
| `ChannelMachineTurnTextTests` incident-followup/Restarted helper, `ChannelMachineTurnMatchTests` identity bodies, `TaskReportHousekeepingTests` C714 bodies, `BootReplyWatchTests` evidence bodies, task reply Grok-housekeeping and genuine-report bodies | V-25/R-11; exercise each actual consumer, including deferred task selection; string helpers alone cannot prove routing safety. |
| `TestDbFixture`, `AntiphonWebAppFactory.ConfigureWebHost/ResetAsync`, `ProductionRunnerGuard`, `DelegationTestServices`; E2E fixture startup/owned-host restart/isolated runner process launch | All integration tests. IsolatedTestSchema currently means a cloned PostgreSQL database, not SearchPath isolation. No in-memory DB substitute; every new context uses the clone connection. |
| `AgentBundleAttachments.test.tsx` handlers/save/drift bodies, `useSignalRInvalidation.test.ts`, `client/src/test/utils.ts` | V-26, R-12; Mantine test env and fresh QueryClient; UI cannot derive file-ready from delivery. |
| Complete CLI gates `RealCliStubGate`, `HeadedClaudeGate`, `HeadedCodexGate`; `ClaudeRealCliStubProxyCanaryTests`; RealCliStubBServerHarness service setup; GrokRulesCompactionAcceptanceTests RunAsync/native challenge workload | V-28–30. Existing CLI gates require Windows. Older Claude B-server test uses Now and cannot substitute for new WhenIdle acceptance. Codex test auth is a dedicated home; Grok uses its established explicit auth path. |
| Owners `docs/testing-and-build.md` checkpoint, slot, mutation and delivery sections; project context; instruction-file/workspace owners; session Grok/C714 invariants; orchestration stage/SourceLanding contract; checkpoint importer/RowTimeout/RowRunner bodies | Closed manifest, exact source receipts, no skipped native qualification, post-land controls, numeric costs. |

**Missing setup to implement, not missing authority:** the proposed pin classes/services/runner operations are absent. Add fixture helpers under the existing test helper directories: a disposable native host root outside repository instruction discovery; real Git root/subdirectory/linked worktree; two hosts with identical logical cwd spelling but disjoint backing roots; runner-store UUIDs; a typed-I/O decorator with awaitable publication/compare/receipt barriers; a cloned-DB bridge graph; real local HTTP and loopback phone-home transports; captured native transcript replay and complete spill resolution. Use local inherited child hosts with owned roots and process limiter; no production runner/broker. Store history for PC setup through fixture seed records, so a mutation of recovery does not fail during historical fixture construction. CP-6 additionally needs a test-owned Linux container server/runner paired with the Windows process: cloned fixture DB, random ports, copied managed DLLs, disjoint mounted disposable roots and awaited teardown. Implement the bridge from the inspected local HTTP/phone-home fixtures; metadata-only Windows/POSIX probes are not mixed-OS acceptance.

All new async test bodies await every child/queue operation and use cancellation-bounded barriers, not sleeps to order a race. Use System time or the existing real-timer clock. Pin expected bytes/hash are constructed independently from fixed ID/text fixtures, never by calling the renderer under test. Prefix-colliding A/B IDs use full immutable 32-hex targets; include C recreated with A's name and root/sibling sentinels. Git/NTFS/POSIX behavior must touch actual native disk. Readiness fakes can schedule a fault but cannot be the positive oracle.

**Slice reachability:** S2-core composition tests explicitly construct the dormant pure snapshot/composer path; they do not activate production DI. Actual launch-entry coverage is deferred to S2-launch. Before S3b, S3a cleanup tests seed trusted legacy file/import custody and exact bytes to exercise retirement and kind changes; CP-7 repeats them with the real S3b writer. S3a unsupported-host cases assert the prepared-location admission result; S2-launch proves its zero-Start consequence. S4/S5 tests explicitly construct the production service graph under test while production registration remains dormant. No test-only product activation flag is required.

**Boundary policy:** the matrices below explicitly cross factors where interaction matters: busy/idle with every delivery cut and semantic mutation; every actual Start seam with provider and fresh/resume; both import writers with tracked/staged/ignore; both native OS paths with reparse/compare/fence. Encoding variants do not cross every provider: byte preservation is host I/O and V-28 separately checks installed Claude expansion. Auth principal/limit matrices do not cross OS because they are HTTP/DB decisions; native placement is independently tested. No blanket cartesian omission is implied by an example row.

### Delivery inventory

Durable identity **F** = full AgentId + projection ID + runner ID/store UUID + canonical native cwd/path + path schema/location generation + desired revision/full content hash + operation ID/fence + expected old/intended new byte hashes. Durable identity **N** = owning session ID + launch/conversation generation + trigger kind/ID (content revision, location change, resume, compact sequence/native ID) + F's location/revision/hash, joined to persisted notification ID, bounded PinRefreshKey, queue message ID/coverage, attempted body digest and transcript attempt floor. Persist both identities before their respective handoffs. No field may be inferred from a current card assignee or cwd.

| Path: producer → destination | Persistence boundary and recovery | Observable receipt and tests |
|---|---|---|
| Capture/replace/revoke/location reconcile → execution-host file over typed local HTTP or phone-home | Desired revision commits; file/import intents independently commit before I/O; host persists F/fence before publish; server CAS after actual-byte receipt. Lost request/reply and stale reorder replay same F. | Actual native file bytes at F and verified host receipt, plus eventual N/full UserPrompt for live owners. V-14, V-32–34, V-18; G/PC file-intent, fence, replay and CAS rows. |
| Retire/move/delete → host cleanup and import writer | Retained custody outside FK cascade before delete; durable cleanup fence/tombstone; last configured/live consumer rechecked; retry only exact owned bytes. | Native absence/owned empty tombstone plus surviving sibling bytes and matching cleanup receipt; no recipient session exists for retired locations. V-15, V15_HostCleanup, V-34; no queue substitute is claimed for file cleanup. |
| Reconciler → notification worker → real SessionMessageQueueService → live owner | Durable N intent; non-inline unique System/WhenIdle insertion; link coverage before flush; event loss recovered by startup/backstop. | Matching complete owning UserPrompt after attempt floor, not enqueue/event/Sent/ACK. V-18–21 use real queue and production receipt reconciler. Busy and already eligible owners both covered. |
| Resume/final Start → launch queue/runner → owning conversation | Accepted generation/prepared F and launch snapshot saved; N resume obligation distinct even at same hash; replay after accepted launch and before note covered. | Actual launch payload/native F plus matching complete new-generation UserPrompt; launch acceptance alone proves neither adoption nor reread. V-4, V-23, V-35, V-27. |
| Claude/Codex live compact, transcript catch-up or startup → pin recovery → queue → owner | Native boundary persists before N; independent pin obligation survives generic watermark and lost event; replay identical generation/sequence. | Complete owning UserPrompt after actual idle boundary, including manual/no-preamble. V-23 and V-18 cut protocol; V-30 adds genuine native compact/read/behavior. |
| Grok rules recovery → single combined rules/pin message → owner | Rules generation/native boundary joined to N; same queue row covers both, preserves barrier/receipt/hash/ACK and bounded chain. | Complete owning UserPrompt containing full current pin request; ACK alone never advances pin receipt. V-24 cuts/busy/idle; V-30 independent native read and BASE challenge. |
| Failed file reconciliation → explicitly degraded own-agent API note → owner | N retains projection failure and own-session endpoint; same durable queue/retry path. | Matching full UserPrompt proves request only; file remains unresolved, future Start blocked until host repair. V-16, V-21, V-26. |
| Semantic mutation/status → ID-only event → client query invalidation/refetch | Pin/status DB is authority; reconnect/browser reload queries current state; event itself need not become a second durable outbox. | Mounted UI refetch/render of correct revision/status (V-26, V-27), not event publish. No model/session delivery claim. |

**K0–K9 handoff cuts**, each restarted/replayed twice in V18_HandoffRecovery, with create/replace/last-revoke and busy/already-eligible recipients: K0 mutation transaction abort; K1 desired state committed before wakeup/file; K2 host publication before server file receipt; K3 file receipt before N intent; K4 N intent before enqueue (including enqueue throws before commit); K5 queue insert commit before returning/linking (including thrown/lost enqueue response); K6 coverage link before explicit flush; K7 attempt metadata saved before terminal input; K8 terminal submit before transcript sync; K9 complete transcript committed before pin receipt save. K0 expects no new work; K1–K9 eventually require current complete recipient evidence. At K8 drop the input response, retain native transcript, then catch up: never retype a received prompt. Test linkage failures as well as process death. Resume and each compact producer apply K3–K9 separately; V-34 covers each transport's pre-dispatch/host-intent/publish/reply/DB-CAS cuts.

**Substitutes and limits:** BridgeQueueHarness adapter records real queue framing and injects transcript records via production sync/catch-up; it proves delivery bookkeeping/recipient correlation, not a native provider read. The isolated fake-provider E2E proves UI/API/runtime wiring and native transcript plumbing, not instruction obedience. Installed-Claude stub request capture proves actual automatic import/context isolation, not paid-model compliance or absence of unobserved external traffic. V-30 alone supplies native tool/read evidence and six actual behavioral answers; an assistant claim, rules ACK or argv replacement cannot substitute. Missing real provider/NTFS prerequisites remain pending acceptance and cannot close CARD-0262.

### Proves it works now

This is a specification of what Code must prove, not a statement that the absent methods already pass. All 79 exact methods below are required. Each new method is a single non-parameterized TUnit execution with explicitly labeled internal scenarios, except inherited methods whose existing Arguments expansion is retained in ER. Internal matrix cells are assertions, never MinExecuted. Every listed scenario is required; Code may split methods only by amending the committed roster/checkpoints and cost before execution. Methods live under `tests/Antiphon.Tests/Application/`, runner classes under `tests/Antiphon.SessionRunner.Tests/`, canary/behavior under `tests/Antiphon.Tests/Agents/`, and E2E under `tests/Antiphon.E2E/`.

| ID | Exact method / layer | Slice and frozen scenario roster | Expected decisive result |
|---|---|---|---|
| V-1 | `AgentPinnedInstructionServiceTests.V01_RestartMigration` / integration | S2-core: Empty migrated clone; pre-commit failure; post-commit wakeup loss; replay twice with new contexts. Exact replay and changed RequestId fingerprint with stale revision. | No I/O in transaction; committed desired state remains recoverable; empty migration creates no pin state. |
| V-2 | `AgentPinnedInstructionServiceTests.V02_AllValidationBoundaries` / integration | S2-core: Text null/0/1/500/501 UTF-16 units, CRLF, NUL/ESC/U+0001, surrogate pair at limit; namespace 64/65, key/ref 200/201; 19/20/21 pins; concurrent capture and replace/revoke; two owners sharing source key. Null/whitespace-only text; each provenance field NUL/ESC/newline; namespace-only and key-only requests. | Reject without truncation/revision/activity; one racing winner; explicit replace/re-pin preserves history; delayed replay cannot resurrect. |
| V-3 | `AgentPinnedInstructionEndpointTests.V03_PrincipalMatrix` / integration | S2-core: GET/capture/revoke/reconcile crossed with headerless operator, own live named token, wrong owner, stopped, expired, task with MayDelegate, capability, invalid and empty header; agent-source/operator-source and foreign pin ID. Forged source and claimed actor fields; independently inspect persisted actor from the authenticated session. | Only authorized operations succeed; header-present refusal never falls back; source/import-mode forging and operator-pin edits refused; 403/404/409/422 and canary-free errors. Claimed actor never overrides authenticated provenance. |
| V-4 | `AgentPinnedInstructionCompositionTests.V04_SupportedSnapshot` / unit/integration | S2-core: Claude/Codex/Grok effective profile crossed with never-used/nonempty/revoked and custom/no preamble; stored Kind deliberately disagrees in each kind. | One immutable owner/revision/full-hash snapshot; independent expected content in provider payload; zero-pin supported named protocol included only after activation. |
| V-4 | `AgentPinnedInstructionCompositionTests.V04_ExcludedAndToolDisabled` / unit/integration | S2-core: Pool/task/capability; Raw/OpenCode named; deny-all-tools Claude/Codex/Grok. | No delegate inheritance; unsupported runtime status with mandatory named file retained; tool-disabled injection has no forbidden read/API recipe and no live-read claim. |
| V-5 | `AgentPinnedInstructionCompositionTests.V05_LiteralOrderEmptyHash` / unit/integration | S2-core: Literal {agentName}, {agent.name}, LF @./payload-canary.md, backtick/tilde runs through 8, HTML/shell/attachment syntax; shuffled rows; rename/source/path-only changes; last revoke. | Literal bytes protected by longer fence; bundles/protocol/style/pins/verbatim append order; independent SHA/order oracle; explicit empty differs from never-used; payload canary excluded. |
| V-5 | `AgentPinnedInstructionCompositionTests.V05_ResolvedBudgets` / unit/integration | S2-core: Each provider at complete effective limit N and N+1, including grown append after capture, Unicode byte/UTF-16 differences, unresolved secret placeholder. | Resolvable mutation preflight and final launch validation refuse excess/secret placeholder; no truncation, fallback or runner attempt. |
| V-5 | `AgentPinnedInstructionCompositionTests.V05_BatchPreview` / unit/integration | S2-core: List/detail/preview of 1 and 20 named agents, including unavailable projection and effective profile override. | Snapshot equality and bounded batch pin-query count (same number for 1 and 20), read operations do not throw on file availability. |
| V-32 | `AgentPinnedInstructionCompositionTests.V32_DormantRegistration` / unit/integration | S2-core: Boot production DI before activation using the refusing runner and a pending S1 pin; explicitly resolve dormant services for tests. | No-op remains registered and protocol absent from actual launch until S2-launch; no starts or file writes from partial deployment. At S2-launch update this test to assert the complete activated graph, preserving pre-activation receipt at its slice SHA. |
| V-6 | `AgentPinWorkspaceTests.V06_FirstUseMatrix` / integration | S3a: Never-used then first capture: 3 import modes x 2 populations (single/shared) x 4 cwd forms (non-Git, repo root, ignored repo, linked worktree subdir) = 24 independently labeled scenarios. | Actual full-ID file after use in all 24; no pin subtree before use; no root alias/index/cwd creation or process start; bytes and explicit target agree. |
| V-7 | `AgentPinWorkspaceTests.V07_FullIdentityConcurrent` / integration | S3a: A/B share eight-hex prefix and cwd; interleave first capture/replace/A last-revoke; rename A, delete and recreate name as C; two A sessions one location; root and sibling sentinels. | Disjoint full-ID paths, latest independent bytes, A empty/B current, rename stable and C history-free; no sibling pointer or root alias. |
| V-13 | `AgentPinWorkspaceTests.V13_ForeignTrackedRefusal` / integration | S3a: Unmarked/forged full-ID/wrong owner/removed marker/changed bytes, tracked-even-ignored target, parent file, denied I/O, absent cwd and unavailable host; repair each. | Foreign bytes unchanged and no Ready; no cwd created; saved state preserved; actual repair writes newest file. Native links are in H/HW. |
| V-14 | `AgentPinWorkspaceTests.V14_CrashPublication` / integration | S3a: Crash before intent commit, after intent/before temp, temp failure, before atomic rename, after publication/before DB receipt; replay twice; matching and mismatching intended bytes. | Only durable exact intent recovers; old/absent or full new target, never partial; no marker-only adoption; no-op mtime stable and temps unadvertised. |
| V-14 | `AgentPinWorkspaceTests.V14_LatestAndCompare` / integration | S3a: Pause n before write, publish n+1, release n; pause cleanup then re-pin; editor changes bytes immediately before final replace/delete comparison. | No regression or current-file deletion; author bytes survive conflict; separate writers/contexts exercise final checks. |
| V-15 | `AgentPinWorkspaceTests.V15_ConsumersAndCleanup` / integration | S3a: Configured cwd move plus old live/card session; host move at equal hash; last release; last revoke; kind Claude-Codex-Raw-Claude; delete after failed cleanup and DB cascade. | Recompute consumers; all used files remain current/empty after revoke; retained custody cleans only owner leaf/import; siblings survive; user import keeps owned empty tombstone. |
| V-15 | `AgentPinWorkspaceTests.V15_MissingRetirement` / integration | S3a: Configured/live/none x present/confirmed-absent/unavailable/denied; consumer-acquisition race; remove disposable linked worktree after release; sweep twice; later reuse. | Only confirmed-absent unused location retires and clears attention; no recreation; required/unknown stays pending; reuse has new generation and verified bytes. |
| V-16 | `AgentPinWorkspaceTests.V16_DegradedRecovery` / integration | S3a: Failure to neutralize known revoked discoverable target; unreadable/wrong owner/wrong revision file; inspect error and repair through explicitly resolved dormant workspace service. | Structured stale/unavailable state persists and never becomes empty success; repair requires actual newest bytes. Actual launch refusal and degraded API-note delivery are V13_StartFailureMatrix and V16_ApiDoesNotRepair after those slices exist. |
| V-17 | `AgentPinWorkspaceTests.V17_GitExcludes` / integration | S3a: Repo root/subdir/linked worktree; existing broad coverage and none; concurrent A/B appends; projection/temp/unrelated files. | Git-resolved info/exclude preserves bytes and both narrow leaves; check-ignore covers temp and final; ls-files and tracked .gitignore unchanged; non-Git works without Git. |
| V-32 | `AgentPinLocationTests.V32_FinalNativeLocation` / integration | S3a: Configured offline and live RunnerId/RunnerStoreId/RunnerCwd; same cwd spelling on two fixture hosts; finalized card worktree; Linux server with Windows metadata and reverse metadata transport. | No server-local Path.GetFullPath on foreign syntax; each file exists on bound execution host; no server fallback. Native OS semantics separately run H/HW, not emulated. |
| V-32 | `AgentPinLocationTests.V32_StoreRebind` / integration | S3a: Reuse runner ID/cwd with a different store UUID; stale receipt; host unavailable; new valid receipt. | Old Ready/receipt not inherited; new generation remains unavailable until new host verifies bytes. |
| V-33 | `AgentPinLocationTests.V33_LegacyUpgrade` / integration | S3a: S1 local Windows-looking Pending rows with authoritative live/configured binding, ambiguous binding, and legacy written row with/without custody; two owners. | Upgrade by authoritative native location only; uncertain old rows retained as repair/cleanup; no cross-owner merge, blind deletion or Ready promotion. |
| V-33 | `AgentPinLocationTests.V33_ReadyCompareSet` / integration | S3a: Wrong owner/operation/revision/full hash/location generation/store/path/bytes; delayed receipt after newer desired state or retirement; matching receipt. | DB completion affects only exact current intent; stale/foreign receipts leave Pending/retired; matching host-confirmed bytes alone becomes Ready. |
| V-34 | `AgentPinTransportTests.V34_Transports` / integration | S3a: Real local HTTP and loopback phone-home server/client/dispatcher; inspect/publish/cleanup, nonempty and empty file, full owner/location/operation. | Native target bytes and durable receipt match across both transports; field loss cannot pass; no shell/arbitrary path or server-local fallback. |
| V-34 | `AgentPinTransportTests.V34_ReplayCuts` / integration | S3a: Each transport: failure before dispatch, host intent persisted, after publish before response, response before server receipt commit; duplicate/reordered RPC, host/server restart. | Same operation resumes, newest bytes retained, one identity; cleanup tombstone fences delayed publication; DB cannot become Ready from RPC acceptance. |
| V-34 | `AgentPinTransportTests.V34_Unsupported` / integration | S3a: 404/null/missing feature/old schema/unavailable runner; pin-bearing versus never-used. | Structured Unsupported/Unavailable; no fallback writes or pin-bearing start; no-history launch remains supported. |
| V-32 | `AgentPinWorkspaceStoreTests.V32_PosixPaths` / integration | S3a: Native Linux case-distinct dirs, separator/trailing separator/dot aliases; case-sensitive volume; duplicate canonical target contenders. | Distinct case paths remain distinct; aliases serialize one target; host canonical identity echoed and validated. |
| V-13 | `AgentPinWorkspaceStoreTests.V13_PathSafety` / integration | S3a: Native symlink at cwd/.antiphon/pins/full-ID/file plus containment escape; component swapped to symlink before final I/O; denied operation via injected native-I/O fault. | No outside canary touched/advertised and no absent cwd creation; all independently unsafe components rejected. |
| V-14 | `AgentPinWorkspaceStoreTests.V14_AtomicAndFence` / integration | S3a: Two store instances: paused temp/replace; newer operation; cleanup tombstone; delayed old write after process restart; lost response replay. | No partial target; durable monotonic fence survives restart and prevents overwrite/resurrection; same replay returns same verified receipt. |
| V-34 | `AgentPinWorkspaceStoreTests.V34_ProtocolAdmission` / integration | S3a: Typed pin request bad owner, target escape, content/hash mismatch, schema, store, action; legal inspect/publish/cleanup. | Reject before any I/O; no arbitrary-file primitive; legal operation yields exact disk-backed receipt. |
| V-15 | `AgentPinWorkspaceStoreTests.V15_HostCleanup` / integration | S3a: Exact file/import custody versus null legacy/mismatched bytes/foreign sibling; empty leaf versus populated leaf. | Compare before delete, preserve unknown files and parents; exact owner only; cleanup receipt never inferred from absence of response. |
| V-32 | `AgentPinWorkspaceWindowsTests.V32_NativeAliases` / integration | S3a: Native NTFS drive case, separator/trailing separator, path alias, volume identity; two contender writers. | Host canonical lock serializes aliases; metadata preserves native path; no Linux parsing substitute. |
| V-13 | `AgentPinWorkspaceWindowsTests.V13_ReparseAllComponents` / integration | S3a: NTFS junction at cwd/.antiphon/pins/full-ID, final file symlink, final component swap, containment escape. | No referent/sentinel mutation or trusted receipt; privilege failure is pending qualification, never a pass. |
| V-14 | `AgentPinWorkspaceWindowsTests.V14_PublicationAndFence` / integration | S3a: Native sharing denial, temp/rename failure, host restart, delayed write after cleanup, compare-before-delete author edit. | Old bytes preserved; exact retry recovers; durable fence and cleanup custody remain enforced on NTFS. |
| V-34 | `AgentPinWorkspaceWindowsTests.V34_HttpReceipt` / integration | S3a: Windows local HTTP runner through existing LocalHttpRunner; nonempty/empty, lost response and restart. | Actual full-ID file and host/store-bound receipt; inspect native bytes after recovery. |
| V-8 | `AgentPinImportTests.V08_DiscoveryMatrix` / integration | S3b: Shared/Unverified/Disabled; Dedicated same cwd, parent and child both directions, stopped registered owner, live task workspace, work versus work-other; owned-exclusive to shared transition. | No unsafe native imports; unmarked shared CLAUDE byte-identical; only safe unrelated scope admitted; verified own import removed before sharing, failure blocks admission. |
| V-8 | `AgentPinImportTests.V08_AdmissionRace` / integration | S3b: Two services/contexts gate scope admission versus import installation, then fail owned-import removal. | Cannot retain two exclusive imports; no runner attempt while pin_import_scope_conflict remains. |
| V-9 | `AgentPinImportTests.V09_DanglingBeforePublish` / integration | S3b: Shared and ancestor (inside/above repo) user import to absent or existing owned target; foreign target and legacy root import. | Observe no nonempty publication before safe scope; user bytes untouched; owned target neutralized/tombstoned; repair enables current publication. |
| V-10 | `AgentPinImportTests.V10_ByteRoundTrip` / integration | S3b: ASCII and UTF-8 non-ASCII with/without BOM x LF/CRLF x zero/one/two final newlines, plus empty file; non-Git/already-ignored Git. | Only exact v2 full-ID stanza/separator appended; no managed marker or mtime churn; disable restores original bytes; completed-operation outside edits preserved. |
| V-11 | `AgentPinImportTests.V11_RecognitionAndRender` / integration | S3b: Direct @ and @./ same-owner user import; inline/backtick/tilde examples; wrong-ID/root; malformed/duplicate/nested/mismatched/legacy markers; managed/unmarked; all Render callers job/channel/style/safety-removal. | Never adopt user import; fenced examples do not suppress real stanza; managed writer exclusively Render; no fallback surfaces; existing LeftAlone preserved; desired import survives floor refresh. |
| V-12 | `AgentPinImportTests.V12_EditAndEncoding` / integration | S3b: Deterministic author edit before final append/cleanup comparison (inside stanza/separator and outside); malformed UTF-8, UTF-32 BOM, UTF-16 LE/BE; import-only write failure. | Compare refuses changed bytes; invalid/unsupported encodings explicitly refused unchanged (UTF-16 unsupported baseline); safe mandatory projection/explicit launch remains usable after optional refusal. |
| V-31 | `AgentPinImportTests.V31_GitAdmission` / integration | S3b: Managed/unmarked x tracked/staged/untracked-nonignored/untracked-ignored/non-Git, plus linked worktree and existing ignore on tracked file. | Both writers refuse tracked/staged or nonignored; correct reason; no CLAUDE exclude added; git add -A and clone/index contain no new private pointer; safe file and explicit launch work. |
| V-31 | `AgentPinImportTests.V31_FinalStageRace` / integration | S3b: Both writers pause after Git check, stage CLAUDE, then publish; pre-existing tracked dangling private import. | Final recheck refuses with unchanged index/bytes; unsafe existing import prevents first nonempty publication. |
| V-12 | `AgentPinImportWindowsTests.V12_ImportReparseBytes` / integration | S3b: Native Windows CLAUDE symlink/junction ancestor plus BOM/CRLF/no-final-newline round trip and editor race. | Referent untouched; valid byte restoration and comparison on NTFS. |
| V-31 | `AgentPinImportWindowsTests.V31_GitWriters` / integration | S3b: Managed/unmarked x staged race/ignored untracked/tracked-even-ignored under Windows linked worktree. | Correct tracked/ignore refusal in both writers; ordinary staging introduces no pointer. |
| V-18 | `AgentPinRefreshTests.V18_HandoffRecovery` / integration | S4: Cuts K0-K9 below x busy/already-eligible x create/replace/last-revoke; restart fresh graph, replay twice; enqueue exception before and after insert commit. | Eventually exactly one covered current obligation and complete owning UserPrompt; no early receipt, no lost durable work or duplicate terminal input. |
| V-18 | `AgentPinRefreshTests.V18_AlreadyEligible` / integration | S4: Idle manual/no-preamble recipient; gate enqueue commit and intent linkage separately. | deliverIfIdle:false: zero input before durable coverage link; normal flush then complete transcript match, not merely a queue row. |
| V-18 | `AgentPinRefreshTests.V18_ManualSweep` / integration | S4: Lost event/wakeup, manual versus AlwaysOn, idle versus working, no live session, stopped/replaced session. | Backstop finds pin System rows without broadening unrelated System behavior; busy held; offline starts nothing; eventual recipient receipt for eligible owner. |
| V-19 | `AgentPinRefreshTests.V19_OwnershipAndPaths` / integration | S4: A/B same cwd; A default/card/old live location; card reassigned; legacy unique/ambiguous/no ownership link; equal hash with changed location; replacement session. | Only persisted owning session gets exact full-ID absolute pointer; location change owes reread; ambiguous ownership unavailable; cancel obsolete rows, never retarget attempted bytes. |
| V-20 | `AgentPinRefreshTests.V20_BusyCoalescing` / integration | S4: Working owner: replace then last-revoke; compact while working; real committed TurnEnd releases. | Zero input/Enter/interrupt/kill/start while busy; never-attempted rows coalesce latest complete empty set with coverage, then one full recipient prompt. |
| V-20 | `AgentPinRefreshTests.V20_AttemptedImmutable` / integration | S4: Gate after attempt start, commit newer revision/path; queue retry and stale worker. | Attempted Body/key/revision/hash/location/verdict unchanged; later obligation survives and receives its own complete UserPrompt. |
| V-20 | `AgentPinRefreshTests.V20_BoundedRetry` / integration | S4: Pending/parked/canceled/exhausted; repeated backstops; explicit supported retry; confirmed prompt without answer. | No new retry keys or retype of confirmed prompt; explicit retry succeeds under normal queue rules; file status independent. |
| V-21 | `AgentPinRefreshTests.V21_EvidenceLadder` / integration | S4: Queue insert/event/Sent/ACK, screen echo, AssistantText, QueuedUserPrompt alone, wrong session/key/hash/path/revision/location, clipped prefix, complete inline, complete spill-aware native transcript via catch-up. | Only matching complete owning UserPrompt after attempt floor advances PinLastNotified; no launch stamp change, no compliance/read claim. |
| V-21 | `AgentPinRefreshTests.V21_GenerationAndMonotonic` / integration | S4: Prior conversation/launch generation, stale sequence/timestamp, late old message after newer confirmation; exact current receipt; crash after transcript before notification receipt save. | No stale adoption/downgrade; restart reconciles current complete prompt once with zero retype; launch evidence immutable. |
| V-16 | `AgentPinRefreshTests.V16_ApiDoesNotRepair` / integration | S4: Actual failed file triggers own-agent API continuity note; successful complete prompt/API reply; then real file repair. | No degraded note when healthy; continuity receipt leaves mandatory failure/launch block until actual bytes recovered. |
| V-25 | `AgentPinInternalTurnTests.V25_RoutingGuards` / integration | S4: Queue-backed pin turns through real channel text/attachment dispatcher, task immediate/deferred selection and boot watcher; arbitrary answer, valid report token and PDF marker; early/late answer. | Zero outbound text/attachments/PDF conversion/task settlement/nudge/boot evidence; older genuine task turn remains attributable. |
| V-25 | `AgentPinInternalTurnTests.V25_HumanLookalike` / integration | S4: Human prompt with same header text but no persisted pin identity; mixed prompt and pin turn. | Human response/task work retained; no prefix-only suppression; matching complete pin prompt required for internal classification. |
| V-25 | `AgentPinInternalTurnTests.V25_RedactedActivity` / integration | S4: Capture/replace/revoke/no-op/replay, projection error and retained cleanup; synthetic text/source/credential canaries in inputs. | Events/logs/activity/attention/export exclude all canaries; one semantic activity per real change, none on replay/no-op. |
| V-22 | `AgentPinPolicyTests.V22_PinOnly` / integration | S5: Auto/Relaunch/Off x first import/change/last revoke/removal; owned path explicitly in InstructionFiles; managed marker recalculation and legacy baseline. | Zero process control for dynamic pins; Off still delivers requested reread through queue; launch stamps not overwritten to hide drift. |
| V-22 | `AgentPinPolicyTests.V22_RealDrift` / integration | S5: Bundle, authored CLAUDE outside stanza, root/sibling antiphon.md, unrelated .antiphon file, unrecorded/malformed stanza. | Real drift remains visible and follows existing eligible lane, replacement includes current pins; exact normalization only. |
| V-22 | `AgentPinPolicyTests.V22_CrashIntent` / integration | S5: Append/managed Render/remove after publication-before-success-save x Auto/Relaunch; valid intent/mismatch/missing intent. | Exact intent normalizes with zero restart before repair; mismatch/unrecorded authored changes retain drift. |
| V-22 | `AgentPinPolicyTests.V22_PreviewAndParity` / integration | S5: Broken-file Grok with valid rules receipt through list/detail/preview/Sweep Notify/manual Notify; Relaunch failure followed by healthy second agent; effective kind mismatch, never-used/revoked/custom/no preamble, pool/task; two deployment sweeps. Existing working/cooldown/model-budget/credential/legacy-Grok refusals with pins present. | No swallowed exception/lost policy work; Start refusal reported; shared static stamps equal; one eligible rollout relaunch only; subsequent pin changes no static drift. |
| V-23 | `AgentPinRecoveryTests.V23_PlainDurable` / integration | S5: Claude/Codex x live/sync/startup x busy/eligible x manual-no-preamble/AlwaysOn; no-history control; watermark-before-enqueue crash and queue cuts K3-K9. | Durable compact generation independent of generic watermark; actual queue/full prompt receipt after idle; no plain-agent memory ritual; no-history keeps no-note. |
| V-23 | `AgentPinRecoveryTests.V23_ResumeTwoCompacts` / integration | S5: Replace then last revoke; same-conversation resume same hash then two distinct captured compact boundaries; replay each; workspace-note eligible/plain variants. | Three separate read obligations, all current empty own file; correct launch-note ordering, no duplicate turn or resurrection. |
| V-24 | `AgentPinRecoveryTests.V24_GrokCombined` / integration | S5: Real persisted rules receipt + captured Grok native boundary, current/nonempty/revoked set; live/sync/startup and K3-K9 cuts; busy/eligible; two boundaries and resume. | One rules row covers pins; current external pointer wins historical snapshot; full owning UserPrompt required; rules file not rewritten; bounded follow-on unchanged. |
| V-24 | `AgentPinRecoveryTests.V24_GrokBarrier` / integration | S5: Pending/failed rules x enqueue/flush/retry/stranded sweep; ACK-only/mismatched hash/legacy inline resume versus valid receipt+prompt+ACK. | Pin input never bypasses barrier; ACK alone cannot stamp pin receipt; existing hash/resume refusals hold and legal combined recovery completes. |
| V-4 | `AgentPinLaunchIntegrationTests.V04_AllEntryPoints` / integration | S2-launch: AgentControl/CardService assigned-agent/Orchestrator assigned-agent x Claude/Codex/Grok x fresh/resume x manual/AlwaysOn; each through finalized local/remote worktree in paired runs. | Real disk and actual runner launch payload share one immutable snapshot and full owning AgentId; native final path, not configured cwd; same live Start is idempotent. |
| V-13 | `AgentPinLaunchIntegrationTests.V13_StartFailureMatrix` / integration | S2-launch: Three entry paths x fresh/resume x unavailable/stale-revoked/unknown ownership/old-runner; no-history and repaired controls. | pin_projection_unavailable or pin_projection_stale before runner attempt; working sessions untouched; repaired current bytes launch; no-history unaffected. |
| V-35 | `AgentPinLaunchIntegrationTests.V35_FinalRecheck` / integration | S2-launch: Each entry point gated after prepare before runtime start; change revision/location/runner store/scope independently; fake Ready with missing/foreign bytes. | Recompose current or explicit retry/refusal, zero knowingly stale start; preview still readable; passed prepared record alone is insufficient. |
| V-35 | `AgentPinLaunchIntegrationTests.V35_Activation` / integration | S2-launch: Real Program registrations, production pin capture and startup scan; partial/old runner versus supported runner; named no-history and pool controls. Production hosted worker; obligation seeded before host startup; dropped wakeup after startup recovered by real bounded backstop; native compact event subscription, without direct worker calls. | Production no-op replaced only with projection+notification+recovery+policy complete; real file and complete queue UserPrompt from API capture/restart; protocol is on supported named path. Each path requires complete owning UserPrompt after its actual trigger; observe worker registration/start and awaited bounded backstop with no sleeping race. |
| V-11 | `AgentPinLaunchIntegrationTests.V11_StartRenderOrder` / integration | S2-launch: Managed floor first capture during live session, failed file repair gated on next Start; all three entry paths; repeated no-op start. | pending_next_start and no floor rewrite/start while working; verify file before Render before runner; one full Render, retained desired import, no remove/reappend churn. |
| V-27 | `AgentPinnedInstructionsE2ETests.V27_UiToRuntime` / browser/runtime | S7: Production UI/API, isolated server/runner/FakeClaude, A/B same cwd, launch-busy replace-idle-revoke-resume-two captured compacts. | Real file bytes, immutable launch, actual queue and full UserPrompt each phase; distinct UI evidence and no shared import/cross-load; zero outbound producer calls. |
| V-27 | `AgentPinnedInstructionsE2ETests.V27_StartupCrash` / browser/runtime | S7: Restart only fixture host after capture commit before reconcile and after queue commit before receipt; browser reload. | Startup reaches same durable obligation and complete recipient transcript without duplicate input; UI refetches current status. |
| V-28 | `AgentPinClaudeImportCanaryTests.V28_NativeOnly` / installed CLI/stub | S7: Installed Claude against stub; eligible ignored unmarked file, direct user import, fenced example, BOM/non-ASCII/CRLF; replacement then cleanup. | Serialized model request includes file-only canary before cleanup and excludes it after; no injection or scripted Read; nonce/key at local chat endpoint; actual CLI/version recorded. |
| V-29 | `AgentPinClaudeImportCanaryTests.V29_Isolation` / installed CLI/stub | S7: Shared, ancestor within/above repo, exclusive-to-shared, dangling import then repair; automatic-only and normal named A/B launches. | Automatic-only context has neither canary; normal launch contains owner only; unsafe pre-repair launch/publication refused; request oracle sensitive to deliberate dual-import fixture. |
| V-28 | `AgentPinClaudeImportCanaryTests.V28_PayloadLiteral` / installed CLI/stub | S7: Pin text contains LF @./payload-canary.md with a different canary in that file; all source is synthetic. | Own literal pin present, payload-file canary absent from serialized context, no tool execution or attachment. |
| V-30 | `AgentPinBehaviorAcceptanceTests.V30_Claude_Journey` / live provider | S7: Claude: six unused random keys and A/B answer sets outside cwd/context; fresh A1, busy replace then B2, revoke BASE3, same-conversation resume BASE4, native compact BASE5 and native compact BASE6. | Each stage requires exact full native read evidence for owner path plus complete queue UserPrompt plus actual expected answer; sibling absent; no expected answer in prompts/stub responses; native boundaries distinct, never forged. |
| V-30 | `AgentPinBehaviorAcceptanceTests.V30_Codex_Journey` / live provider | S7: Codex: six unused random keys and A/B answer sets outside cwd/context; fresh A1, busy replace then B2, revoke BASE3, same-conversation resume BASE4, native compact BASE5 and native compact BASE6. | Each stage requires exact full native read evidence for owner path plus complete queue UserPrompt plus actual expected answer; sibling absent; no expected answer in prompts/stub responses; native boundaries distinct, never forged. |
| V-30 | `AgentPinBehaviorAcceptanceTests.V30_Grok_Journey` / live provider | S7: Grok: six unused random keys and A/B answer sets outside cwd/context; fresh A1, busy replace then B2, revoke BASE3, same-conversation resume BASE4, native compact BASE5 and native compact BASE6. | Each stage requires exact full native read evidence for owner path plus complete queue UserPrompt plus actual expected answer; sibling absent; no expected answer in prompts/stub responses; native boundaries distinct, never forged. |
| V-34 | `AgentPinMixedOsTransportTests.V34_MixedOsRoundTrip` / integration | S3a: Windows lane with test-owned Linux containers: Linux server to Windows runner and Windows server to Linux runner, each HTTP/phone-home; same logical cwd spelling plus native distinct roots, store rebind and lost publication response. | Actual destination-native full-ID bytes and matching F receipt across both OS directions; no file appears on source server; current real queue proof remains V-18. Containers/runner use only fixture DB, ports and roots. |

V-26 uses eight exact top-level Vitest test titles (no enclosing describe prefix, so anchored PC filters select one test) in new `AgentPinnedInstructions.test.tsx`: `V26_CaptureReplaceRevokeHistory` (API methods, optimistic revision, capacity and provenance); `V26_LiteralText` (HTML/template/attachment input displayed as text); `V26_Drafts` (pin and operator-append drafts survive invalidation and 409, deliberate retry uses new revision); `V26_StatusLadder` (saved, failed mandatory file, queued, screen-only, transcript-requested, API recovery and successful file repair remain distinct); `V26_ImportStates` (Shared/Unverified/Disabled, tracked/nonignored refusal, pending_next_start with file ready); `V26_EffectiveProfile` (stored-kind/profile mismatch, Raw/OpenCode and deny-all tools); `V26_RetainedCleanup` (unused absent retired versus required missing/unknown repair); `V26_Repair` (repair retries work without force-send or compliance label). Add `V26_PinEventsRefetch` to `useSignalRInvalidation.test.ts`: mounted pin/detail/list/attention queries refetch on ID-only event, unrelated data and drafts survive; reconnect/refetch covers lost event. Existing 10 bundle and 5 invalidation tests remain, so CP-21 requires **24 Vitest results**, all three files, zero skipped/failed.

**ER — exact inherited regression roster.** All names below remain required, including each current Arguments row. Counts are source-derived floors, not measured TRX. Only S1 tests and intended changed behavior may need fixture/signature updates: replace server-only fake Ready with valid native receipt, replace legacy foreign path assumptions with typed native metadata while retaining their original identity assertions. Preserve S1's existing 27 executions (16 store + 7 endpoint + 4 path), plus the three new supplement methods. Do not delete an assertion to keep the old fixture green. For other class-scoped filters, newly added methods join the roster; update expected counts in the same commit.

- `InstructionBundleTests`: 51 methods, 71 executions. `C1015_Composed_workers_keep_generated_evidence_untracked`; `C1015_Code_keeps_range_guard_and_exact_source`; `C1015_Review_remains_read_only_and_checks_history`; `C1015_SourceLanding_keeps_its_external_evidence_exception`; `C1011_composed_windows_routing_contract`; `C1011_model_kind_and_tier_contract`; `C1011_owner_role_wide_policy_and_fallback`; `C1011_qualification_gates`; `C1011_activation_order`; `C807_ShippedReviewExampleParses`; `Channel_sources_do_not_instruct_universal_pdf_conversion`; `Phone_with_attachments_and_append_keeps_the_command_line_budget_guard`; `C499_V08_CodeBriefCarriesTheTaskScopedProgressClaimContract`; `C499_V08b_RepairBriefNamesOwnerSourceBaselineAndAssignedCheckout`; `C470_composed_roles_separate_vr_from_pc`; `the_orchestrator_preset_prompt_is_embedded_and_not_attachable`; `the_orchestrator_preset_enables_remote_control_and_the_full_feature_pipeline`; `the_catalog_holds_exactly_the_bundles_that_ship`; `every_bundle_has_text_and_an_eight_hex_digit_content_version`; `the_version_is_the_content_hash_so_two_bundles_never_share_one`; `no_bundle_carries_a_channel_preamble_placeholder`; `a_rendered_bundle_leads_with_its_versioned_header`; `every_bundle_summarises_itself_in_its_opening_sentence`; `an_unknown_key_throws_and_names_the_ones_that_exist`; `bundles_compose_in_declared_order_under_their_headers`; `a_key_reachable_twice_is_composed_once`; `the_style_block_lands_after_the_bundles_and_the_agents_own_append_lands_last`; `with_no_bundles_the_composition_is_the_agents_own_append_byte_for_byte`; `nothing_to_compose_is_empty_so_the_flag_is_omitted_entirely`; `the_check_interpreter_contract_forwards_to_its_bundle`; `the_diagnose_contract_forwards_to_its_bundle_with_the_pinned_hard_rules`; `the_output_distiller_contract_forwards_to_its_bundle_with_the_pinned_invariants`; `the_distill_reporting_contract_never_offers_blocked_and_keeps_the_handoff_anchor`; `the_orchestrator_contract_forwards_to_its_bundle_with_its_text_intact`; `orchestrator_bundle_points_to_operational_autonomy_without_growing` (2 Arguments results); `delegate_basics_carries_the_standing_rules_and_none_of_the_days_state`; `the_worst_case_composition_measured_sits_far_under_the_budget`; `an_oversized_composition_throws_and_names_what_to_shrink`; `the_test_design_composition_is_past_the_batch_command_ceiling`; `the_other_arguments_count_towards_the_budget_not_just_the_append`; `a_helper_worker_role_carries_only_the_delegate_basics` (8 Arguments results); `a_stage_worker_carries_its_stage_bundle_then_the_basics` (6 Arguments results); `an_investigate_worker_carries_the_stage_bundle_and_a_docs_worker_does_not`; `a_sub_orchestrator_carries_its_own_contract_first_then_the_basics`; `a_specialist_task_carries_nothing` (3 Arguments results); `the_board_api_bundle_is_on_no_role_by_default`; `each_stage_bundle_is_ascii_and_under_the_size_cap` (6 Arguments results); `stage_bundle_invariants_are_pinned_by_substring`; `C589_V6_BuildSlotGateIsAStandingRuleReviewEnforces`; `C467_V21_DeliveryInventoryAndReviewAreMandatory`; `a_realistic_code_worker_composition_stays_under_the_command_line_budget`.
- `AgentWorkspaceProvisionerTests`: 24 methods, 24 executions. `an_empty_directory_gets_a_floor_naming_the_agents_job`; `a_second_call_that_would_write_the_same_thing_writes_nothing`; `our_own_stale_file_is_rewritten_when_the_agents_job_changes`; `an_unmarked_file_is_never_touched`; `a_file_whose_marker_line_was_deleted_is_ownership_taken_back`; `a_marker_that_is_not_ours_is_not_ours`; `the_hand_written_stopgap_is_adopted_once_and_maintained_after_that`; `a_file_that_merely_resembles_the_stopgap_is_not_adopted`; `the_stopgap_is_recognised_through_a_crlf_checkout`; `the_deny_all_hook_in_the_directory_makes_the_floor_say_so`; `an_ordinary_settings_file_never_makes_the_floor_claim_the_agent_has_no_tools`; `no_hook_no_claim`; `a_conventions_file_in_the_directory_is_named_by_absolute_path`; `a_conventions_file_in_an_ancestor_is_found_and_named_by_absolute_path`; `the_nearest_conventions_file_wins`; `no_conventions_file_anywhere_leaves_the_section_out_rather_than_pointing_at_nothing`; `a_bound_agent_gets_a_channel_section_naming_the_attach_follow_up_rule`; `an_unbound_agent_does_not_get_the_channel_section`; `an_unmarked_file_stays_left_alone_even_when_the_agent_is_channel_bound`; `an_agent_with_no_written_job_gets_a_floor_that_says_so_instead_of_inventing_one`; `a_working_directory_that_does_not_exist_is_not_created_and_not_an_error`; `an_agent_with_no_working_directory_at_all_degrades_quietly`; `a_directory_it_cannot_write_is_logged_and_survived`; `the_check_interpreter_gets_its_floor_when_it_is_provisioned`.
- `SessionMessageQueueServiceTests`: 25 methods, 25 executions. `Send_now_delivers_immediately_and_does_not_queue`; `Delivery_sends_body_then_a_separate_CR_not_one_combined_write`; `Multiline_delivery_is_wrapped_in_bracketed_paste`; `When_idle_message_is_held_while_the_agent_is_working`; `Turn_end_flushes_the_oldest_queued_message`; `When_idle_and_agent_is_idle_the_message_is_delivered_right_away`; `Turn_end_with_empty_queue_broadcasts_finished`; `Send_now_promotes_a_specific_queued_message`; `Cancel_removes_a_pending_message_without_delivering_it`; `Cancel_keeps_a_non_pending_message_unchanged`; `Cancel_pending_if_untyped_refuses_an_attempted_message`; `An_interrupt_marker_ends_the_turn_and_unblocks_when_idle_delivery`; `A_real_user_message_mentioning_interruption_is_not_a_turn_end`; `Local_slash_command_records_do_not_read_as_working_and_do_not_block_delivery`; `Local_slash_command_records_do_not_end_a_running_turn`; `Backfilled_stale_activity_above_a_turn_end_does_not_read_as_working`; `A_stale_backfill_above_a_clean_same_timestamp_turn_end_reads_idle`; `A_real_post_end_activity_keeps_a_mixed_backfill_shape_working`; `Activity_newer_than_the_last_turn_end_still_reads_as_working`; `Session_restart_boundary_ends_an_interrupted_turn`; `A_message_that_spent_its_delivery_attempts_serialises_as_parked`; `Channel_and_scheduled_rows_stay_pending_behind_a_terminal_capacity_recovery`; `Supervision_rows_still_deliver_behind_a_terminal_capacity_recovery`; `a_held_row_is_skipped_until_the_hold_lapses`; `SendNow_delivers_a_held_row`.
- `ChannelMachineTurnTextTests`: 18 methods, 19 executions. `The_incident_shape_delivers_plain_text_as_a_follow_up`; `A_check_row_matches_by_conversation_key_when_the_stored_body_was_amended`; `A_grok_flattened_task_done_turn_delivers_plain_text`; `Follow_up_send_stamps_LastReplyAt_without_touching_inbound_columns`; `Check_and_Scheduled_origins_deliver_plain_text` (2 Arguments results); `System_plain_text_is_not_delivered_and_does_not_claim`; `System_origin_with_attach_still_sends`; `Exact_NO_REPLY_is_silence_and_does_not_claim`; `Prose_around_NO_REPLY_is_delivered`; `Re_running_the_same_turn_end_and_a_restart_do_not_double_send`; `Trailing_text_follows_via_the_dispatched_watermark`; `An_api_error_stub_in_the_trailing_window_sends_nothing`; `Stop_marker_before_text_does_not_claim_then_the_text_sends_once`; `A_produce_failure_un_claims_so_the_next_trigger_sends_once`; `Empty_origins_dial_sends_nothing_for_plain_text_but_still_sends_markers`; `MachineTurnTextOrigins_rejects_Channel_Ui_and_Supervision`; `An_operator_typed_plain_text_turn_sends_nothing_and_raises_no_incident`; `Implied_bundle_plus_text_sends_both_and_stamps`.
- `ChannelMachineTurnMatchTests`: 10 methods, 10 executions. `Flattened_task_done_fails_120_char_body_containment_and_passes_id_and_header`; `Batched_two_task_headers_collect_both_ids`; `Superseded_joined_check_header_is_injection_shaped_and_collects_the_check_id`; `Scheduled_banner_is_injection_shaped_and_has_no_task_id`; `Header_probe_is_the_first_line_never_the_report_body`; `Check_id_via_conversation_key_matches_the_header_short_id`; `Antiphon_task_marker_is_injection_shaped_but_not_a_source_task_id`; `Channel_envelope_and_operator_prose_are_not_injection_shaped`; `System_and_session_tokens_are_injection_shaped`; `Header_probe_skips_leading_blank_lines_and_empty_body`.
- `TaskReportHousekeepingTests`: 10 methods, 11 executions. `C714_lf_envelope_is_grok_housekeeping`; `C714_crlf_envelope_is_grok_housekeeping`; `C714_unknown_system_reminder_is_real_prompt`; `C714_quoted_reminder_is_real_prompt`; `C714_suffixed_reminder_is_real_prompt`; `C714_incomplete_envelope_is_real_prompt`; `C714_mismatched_completion_ids_are_real_prompt`; `C714_grok_shape_on_other_provider_is_real_prompt` (2 Arguments results); `C714_claude_notification_keeps_its_identity`; `C714_codex_text_is_not_a_housekeeping_prompt`.
- `BootReplyWatchTests`: 17 methods, 27 executions. `an_unarmed_watch_is_disarmed_whatever_the_rows_say`; `any_model_produced_row_past_the_prompt_answers_the_watch` (5 Arguments results); `rows_that_are_not_the_model_answering_never_close_the_watch` (7 Arguments results); `an_inherited_row_at_or_below_the_boot_prompt_cannot_answer_it`; `inside_the_deadline_a_silent_session_is_waiting_not_overdue`; `a_prompt_with_nothing_after_it_is_a_boot_turn`; `one_model_row_since_the_clock_ends_the_boot_turn`; `a_prompt_alone_is_not_a_model_reply_for_the_cheap_exists`; `rows_before_the_launch_clock_are_invisible`; `a_refinement_typed_into_a_still_silent_session_restarts_the_wait`; `housekeeping_prompt_records_are_neither_evidence_nor_disqualifiers`; `a_session_that_has_written_nothing_is_not_a_boot_turn`; `arming_stamps_the_row_from_the_prompt_clock_not_from_now`; `arming_a_session_that_already_answered_clears_the_watch_instead`; `a_zero_deadline_disarms_rather_than_arming_at_zero`; `the_watch_survives_being_rebuilt_from_the_row_alone`; `the_boot_deadline_is_one_setting_and_it_orders_correctly_against_the_others`.
- `PolicyRefreshServiceTests`: 21 methods, 24 executions. `Phone_refresh_records_loaded_stamp`; `Phone_notify_or_working_keeps_loaded_stamp` (3 Arguments results); `Grok_legacy_policy_drift_refuses_before_kill_queue_or_stamp_mutation` (2 Arguments results); `Drift_and_idle_kills_resumes_and_records_incident`; `Working_is_skipped`; `Queued_Pending_row_is_skipped`; `Channel_row_owed_a_reply_is_skipped`; `Cooldown_is_skipped`; `Suspended_is_skipped`; `Held_model_is_skipped`; `Unbound_transcript_is_Notify_not_a_kill`; `Codex_is_Notify_not_a_kill`; `Herdr_null_stamp_does_nothing`; `StartAsync_throw_records_PolicyRefreshFailed_and_does_not_touch_the_ladder`; `A_server_restart_mid_relaunch_leaves_a_Starting_row_that_CARD_0340_resume_handles`; `Notify_dedupes_across_sweeps_and_a_new_service_instance`; `RefreshPolicyAsync_force_skips_idle_minutes_and_relaunches`; `RefreshPolicyAsync_working_is_409_session_working_even_with_force`; `RefreshPolicyAsync_without_a_live_session_is_409_not_resumable`; `RefreshPolicyAsync_notify_lane_returns_notified`; `Off_mode_is_neither_lane`.
- `InstructionFileStampTests`: 15 methods, 15 executions. `the_hash_rule_matches_InstructionBundle_Version`; `a_missing_file_is_omitted_and_does_not_fail_the_stamp`; `crlf_and_lf_checkouts_of_the_same_text_share_a_version`; `composition_order_follows_the_file_list_not_the_filesystem`; `parent_segments_and_absolute_paths_are_skipped`; `default_instruction_files_are_the_plan_list`; `validator_rejects_idle_below_one_and_cooldown_below_five`; `validator_rejects_absolute_paths_and_parent_segments`; `validator_accepts_the_shipped_defaults`; `a_null_stamp_is_no_evidence_and_never_drift`; `an_edited_bundle_and_an_edited_file_are_listed_separately`; `file_only_drift_does_not_count_as_bundle_drift`; `an_empty_launch_stamp_is_drift_once_something_is_composed`; `json_round_trips_camel_case_and_enum_names`; `an_update_that_omits_policyRefreshMode_leaves_it_alone`.
- `CompactionRecoveryTests`: 6 methods, 6 executions. `Compact_boundary_records_info_incident_and_enqueues_one_recovery_note`; `Compaction_incident_does_not_raise_alert`; `Duplicate_boundary_events_are_deduped_after_simulated_replay`; `Agent_without_preamble_gets_incident_but_no_note`; `Recovery_note_is_delivered_even_with_the_full_post_compaction_record_set`; `Mid_work_compaction_defers_the_note_to_the_next_turn_end`.
- `GrokRulesTransportCompatibilityTests`: 4 methods, 24 executions. `Unsafe_raw_rules_are_refused_server_side_before_runner_calls` (12 Arguments results); `Missing_capability_refuses_before_sending_a_payload_to_an_old_runner` (3 Arguments results); `Invalid_body_is_refused_server_side_before_any_runner_request` (8 Arguments results); `Payload_and_budget_round_trip_while_receipt_remains_metadata_only`.
- `AgentSystemPromptLaunchTests`: 26 methods, 29 executions. `Phone_fresh_launch_preserves_order_append_and_stamp` (2 Arguments results); `Phone_resume_replaces_loaded_stamp_without_changing_append` (2 Arguments results); `Start_with_system_prompt_append_passes_flag_on_fresh_launch`; `Second_start_on_a_live_session_is_a_no_op_and_does_not_rebootstrap`; `Resume_launch_also_carries_append_system_prompt_and_delivers_restart_note`; `Agent_without_preamble_launches_without_preamble_or_notes_but_is_still_named`; `Cardless_start_does_not_deliver_Details_as_a_prompt`; `Cardless_start_with_Prompt_delivers_it_and_not_Details`; `Interactive_launch_names_the_session_by_agent_name`; `Interactive_launch_passes_the_default_model_level_as_opus`; `Interactive_launch_maps_frontier_level_to_fable`; `Resume_not_found_fallback_delivers_fresh_bootstrap`; `Fallback_with_stale_mid_turn_transcript_still_delivers_bootstrap`; `Launch_note_yields_to_an_owed_channel_row`; `Note_delivery_failure_falls_back_to_queue_and_does_not_fail_launch`; `Bootstrap_produces_no_channel_reply`; `The_standing_check_interpreter_gets_no_bootstrap_note`; `A_normal_style_agent_launches_with_exactly_the_arguments_it_did_before`; `A_styled_agent_carries_its_style_block_before_its_own_contract`; `A_style_alone_produces_the_flag_but_still_no_launch_notes` (2 Arguments results); `An_attached_bundle_rides_the_launch_of_a_standing_agent_that_has_no_role`; `Attaching_a_bundle_to_a_running_agent_raises_the_drift_badge_and_touches_nothing_else`; `The_badge_clears_at_the_next_launch_because_a_launch_is_the_reconcile_point`; `A_standing_launch_records_the_instruction_file_stamp_and_clears_file_drift`; `An_edited_instruction_file_shows_as_file_drift_without_bundle_drift`; `An_agent_with_no_attachments_and_no_style_records_an_empty_stamp_not_a_null_one`.
- `NamedCodexAgentLaunchTests`: 6 methods, 6 executions. `A_codex_agent_launches_with_its_tier_slug_and_reasoning_effort`; `A_codex_agents_standing_instructions_ride_developer_instructions`; `A_codex_agents_long_standing_instructions_arrive_whole`; `A_null_profile_agent_keeps_default_Kind_and_still_launches_from_the_registry`; `A_Kind_PATCH_changes_the_column_and_not_the_composed_launch`; `A_codex_agent_with_an_exact_model_id_keeps_it_and_still_sets_effort`.
- `GrokRulesCompositionTests`: 3 methods, 9 executions. `C470_mutation_rules_payload_contains_its_stage`; `Worker_and_stage_bundles_reach_typed_rules_payload_without_argv_text` (6 Arguments results); `Standing_channel_composition_preserves_attachment_style_append_and_preamble_bytes` (2 Arguments results).
- `DelegateBundleLaunchTests`: 15 methods, 23 executions. `Phone_delegate_launch_and_brief_keep_stage_contracts` (4 Arguments results); `Phone_mixed_composition_exempts_internal_reports`; `C470_mutation_claude_and_codex_launch_contract` (2 Arguments results); `a_worker_launches_with_the_delegate_basics_bundle_under_its_versioned_header`; `a_sub_orchestrator_launches_with_its_own_contract_first_then_the_basics_each_once`; `a_specialist_task_launches_with_no_system_prompt_at_all` (3 Arguments results); `an_over_budget_composition_fails_the_launch_and_names_the_task`; `bundles_ride_the_launch_and_never_the_brief`; `a_pinned_agents_attachments_ride_its_launch_on_top_of_its_role`; `attaching_a_bundle_the_role_already_grants_composes_it_once`; `a_worker_launch_sets_task_kind_worker_alongside_task_id`; `an_orchestrator_task_sets_task_kind_orchestrator_so_the_hook_arms`; `a_specialist_task_still_launches_with_nothing_even_when_its_agent_carries_attachments` (3 Arguments results); `a_grok_investigate_launch_carries_the_stage_bundle_in_the_typed_payload`; `a_codex_investigate_launch_carries_the_stage_bundle_on_developer_instructions`.
- `PhoneHomeStandingLaunchTests`: 15 methods, 15 executions. `Start_commits_binding_before_remote_launch`; `Binding_constraint_rejects_partial_owner`; `Unsupported_start_is_refused_before_reservation`; `Only_exact_host_root_maps_to_runner_cwd`; `Projection_keeps_identity_rules_and_local_definition`; `Resume_never_probes_host_history_or_starts_fresh`; `Secrets_never_cross_child_or_status_boundary`; `Unknown_launch_blocks_replacement_until_owner_probe`; `Launch_handoff_cuts_preserve_owner_and_reservation`; `Standing_agent_start_on_a_draining_runner_is_refused`; `Runner_bound_raw_agent_projects_allow_listed_exe_only`; `Runner_bound_named_claude_agent_is_admitted_and_projects`; `Runner_bound_claude_remote_control_is_refused`; `Runner_bound_named_claude_create_and_patch_refuse_remote_control`; `Card_start_and_onagent_stay_refused_for_runner_bound_agent`.
- `AgentSessionLaunchQueueOwnershipTests`: 7 methods, 7 executions. `Owns_is_true_from_enqueue_until_the_launch_settles`; `ResumeInterrupted_registers_before_running_and_a_second_call_is_a_noop`; `A_faulted_resume_still_releases_ownership`; `Queued_launch_carries_the_explicit_accepted_generation_and_never_re_reads_a_replaced_row`; `C514_Two_recovery_workers_resume_one_original_work`; `C514_Failed_enqueue_cannot_drop_launch_responsibility`; `C514_Old_deferred_work_never_targets_replacement`.
- `SessionMessageQueueDeliveryVerificationTests`: 4 methods, 4 executions. `Late_confirm_marks_the_message_sent_with_zero_writes_to_the_terminal`; `Queue_enqueue_does_not_confirm_delivery`; `A_clipped_prefix_parks_as_truncated_not_sent`; `A_complete_long_body_still_marks_sent`.
- `AgentTaskReplyIntegrationTests`: 2 methods, 3 executions. `Grok_rules_refresh_with_task_marker_neither_settles_nor_nudges` (2 Arguments results); `a_marked_turn_settles_the_task_and_stores_the_report_verbatim`.
- `AgentPinnedInstructionServiceTests`: 14 methods, 16 executions. `V01_empty_migrated_store_has_no_pin_rows_or_io`; `V01_capture_persists_immutable_text_provenance_and_first_use_intent`; `V01_failure_before_commit_leaves_nothing_and_skips_io`; `V01_failure_after_commit_before_reconcile_keeps_durable_intent`; `V01_exact_request_id_replay_is_noop`; `V01_same_source_same_text_and_revoked_revoke_are_nops`; `V01_changed_request_id_fingerprint_conflicts_even_with_stale_revision`; `V01_full_id_path_rename_locations_and_cleanup_cascade`; `V02_race_two_captures_one_wins_and_retry_cannot_exceed_20`; `V02_race_replace_and_revoke_never_duplicates_or_drops_both`; `V02_changed_text_requires_replace_and_delayed_replay_does_not_resurrect`; `V02_empty_and_short_text` (3 Arguments results); `V02_length_and_control_and_provenance_boundaries`; `V02_two_agents_may_share_a_source_key`.
- `AgentPinnedInstructionEndpointTests`: 7 methods, 7 executions. `V03_headerless_operator_capture_get_and_revoke_succeed`; `V03_own_live_session_token_captures_as_agent_source`; `V03_token_present_failures_never_fall_back_to_operator`; `V03_wrong_stopped_task_capability_and_delegate_tokens_are_forbidden`; `V03_agent_cannot_forge_source_or_change_import_mode_or_touch_operator_pins`; `V03_foreign_pin_is_404_without_text_and_conflicts_are_409`; `V03_operator_can_set_dedicated_and_reconcile_does_not_start_or_send`.
- `AgentPinPathTests`: 4 methods, 4 executions. `V01_relative_path_uses_full_agent_id_hex`; `V01_canonical_cwd_uses_windows_separators_and_drops_trailing_slash`; `V02_text_normalizes_crlf_and_rejects_empty_and_controls`; `V02_snapshot_hash_is_stable_for_id_and_text_order`.
- `GrokRulesCompactionRecoveryTests`: 1 methods, 11 executions. `Captured_native_boundary_reaches_one_durable_refresh_without_idle_input` (11 Arguments results).
- `GrokRulesQueueBarrierTests`: 1 methods, 28 executions. `Closed_rules_barrier_holds_ordinary_input_on_each_delivery_entry_point` (28 Arguments results).
- `PhoneHomeCommandDispatcherTests`: 2 methods, 2 executions. `Unsupported_operation_or_launch_never_enters_runtime`; `Workspace_ops_are_admitted_only_under_allowed_cwd`.

The two scoped legacy selections deliberately exclude unrelated watchdog/process-restart, quota, landing, report-spend and attachment-deliverable tests from those large classes; their pin-specific replacements are V-18–25. All other ER classes run whole as listed. Existing Windows-only path and raw-Grok argv cases are isolated in CP-6/CP-16; no skipped result is accepted as a portable pass.

### Guards the regression

| ID | Regression | Decisive ordinary tests/assertions |
|---|---|---|
| R-1 | Dedicated/API-only files or false Ready | V06_FirstUseMatrix actual bytes in 24 scenarios; V13_StartFailureMatrix zero unavailable launch attempts. |
| R-2 | Short/name/root identities, cwd-based recipient or wrong host | V07_FullIdentityConcurrent, V19_OwnershipAndPaths, V32_FinalNativeLocation and V32_StoreRebind: exact disjoint paths/owning transcript/store identity. |
| R-3 | Shared/ancestor cross-load or commit-able private import | V08/V09/V31, V29_Isolation: no unsafe nonempty publication, no index pointer, no foreign canary in serialized native context. |
| R-4 | Rewrite authored CLAUDE or bypass managed Render | V10_ByteRoundTrip, V11_RecognitionAndRender, V12_EditAndEncoding, V11_StartRenderOrder: exact original bytes and publication-before-Render ordering. |
| R-5 | Marker adoption, unsafe paths, stale writer or unconditional Ready | V13/V14, V33_ReadyCompareSet, V34_ReplayCuts: foreign bytes survive, newest generation wins, host fence survives restart. |
| R-6 | Lose cleanup, retain absent-unused errors, erase siblings | V15_ConsumersAndCleanup, V15_MissingRetirement, V15_HostCleanup, V16_DegradedRecovery: consumer/absence matrix, custody survives cascade, sibling bytes unchanged. |
| R-7 | Omit launch seam, parse pin text, leak to delegate or evade budgets | V04_AllEntryPoints, V04_ExcludedAndToolDisabled, V05_LiteralOrderEmptyHash, V05_ResolvedBudgets, V28_PayloadLiteral: actual payload/bytes, no payload import, correct refusal. |
| R-8 | Lose durable change/revoke/resume/compact work | V18_HandoffRecovery K0–K9, V20_AttemptedImmutable, V23_PlainDurable, V23_ResumeTwoCompacts: complete current recipient prompts after recovery, not queue inserts. |
| R-9 | False transcript receipt, drift normalization error or throwing preview | V21_EvidenceLadder, V21_GenerationAndMonotonic, all four V22 methods: complete owning prompt only; zero pin-only process calls; true drift/Notify/parity survive. |
| R-10 | Old Grok pins win or pin input bypasses rules barrier | V24_GrokCombined, V24_GrokBarrier, V30_Grok_Journey: one current full-set request, barrier holds, native reads and unused BASE answers after revoke/resume/two compacts. |
| R-11 | Principal escalation, pin answer publication/settlement or data leak | V03_PrincipalMatrix and three V25 methods: authorized HTTP only, zero internal outbound/task/boot effects, canary-free status events. |
| R-12 | UI loses drafts or equates saved/requested with applied | Eight V26 titles, V26_PinEventsRefetch and both V27 methods: separate evidence/status, retained drafts and real mandatory repair. |

### Guard inventory

Every row identifies one independently bypassable feature guard. Each G-n maps 1:1 to the distinct PC-n with the same number. Native companion cycles below belong to the same guard/control, not duplicate mappings. No safety-critical feature guard is left untested. Test-environment isolation (production runner refusal, credential custody and process ownership) is preserved and never deliberately disabled; it is fixture authority, not a mutation target.

| Guard | Plan reference + safety-critical invariant | Positive control | September arm |
|---|---|---|---|
| G-1 | D-3: First-use publication independent of Dedicated | PC-1 | Sep-PC-1a |
| G-2 | D-3: Ready requires real publication | PC-2 | Sep-PC-1b |
| G-3 | D-3: Full 32-hex path identity | PC-3 | Sep-PC-2a |
| G-4 | D-3: Name-independent path identity | PC-4 | Sep-PC-2b |
| G-5 | D-3: No root projection substitution | PC-5 | Sep-PC-2c |
| G-6 | D-3: No latest-writer alias | PC-6 | Sep-PC-3 |
| G-7 | D-7: Standing ownership is never cwd ownership | PC-7 | Sep-PC-4a |
| G-8 | D-7: Legacy ownership ignores later card reassignment | PC-8 | Sep-PC-4b |
| G-9 | D-6: Final card worktree location is authoritative | PC-9 | Sep-PC-5a |
| G-10 | D-3: Execution host cannot fall back to server | PC-10 | Sep-PC-5b |
| G-11 | D-7: Equal content at new location still owes delivery | PC-11 | Sep-PC-6 |
| G-12 | D-6: Unavailable projection blocks actual Start | PC-12 | Sep-PC-7a |
| G-13 | D-6: Final semantic revision is rechecked | PC-13 | Sep-PC-7b |
| G-14 | D-6: Unneutralized revoked discoverable content blocks Start | PC-14 | Sep-PC-8 |
| G-15 | D-5: Ancestor and descendant discovery overlap excludes import | PC-15 | Sep-PC-9a |
| G-16 | D-5: Stopped registered agents participate in discovery | PC-16 | Sep-PC-9b |
| G-17 | D-5: Live task workspaces participate in discovery | PC-17 | Sep-PC-9c |
| G-18 | D-5: Admission and installation serialize across contenders | PC-18 | Sep-PC-10 |
| G-19 | D-5: Sharing waits for owned-import removal | PC-19 | Sep-PC-11 |
| G-20 | D-5: Optional import requires eligible exclusive scope | PC-20 | Sep-PC-12 |
| G-21 | D-5: Unsafe dangling import checked before nonempty publish | PC-21 | Sep-PC-13a |
| G-22 | D-5: Ancestor user imports checked before nonempty publish | PC-22 | Sep-PC-13b |
| G-23 | D-5: User import is conflict, not ownership | PC-23 | Sep-PC-14 |
| G-24 | D-5: Eligible import actually installed | PC-24 | Sep-PC-15 |
| G-25 | D-5: Unmarked files retain authored floor | PC-25 | Sep-PC-16 |
| G-26 | D-5: Append preserves encoding/BOM/newlines | PC-26 | Sep-PC-17 |
| G-27 | D-5: Cleanup removes only its recorded separator | PC-27 | Sep-PC-18a |
| G-28 | D-5: Cleanup preserves pre-existing final newlines | PC-28 | Sep-PC-18b |
| G-29 | D-5: Append compares current author bytes | PC-29 | Sep-PC-19a |
| G-30 | D-5: Removal compares current stanza/separator bytes | PC-30 | Sep-PC-19b |
| G-31 | D-5: Unsupported encoding is refused without replacement decoding | PC-31 | Sep-PC-20 |
| G-32 | D-5: Equivalent user import never becomes owned | PC-32 | Sep-PC-21 |
| G-33 | D-5: Fenced examples do not count as active imports | PC-33 | Sep-PC-22a |
| G-34 | D-5: Wrong-owner/root imports cannot satisfy own import | PC-34 | Sep-PC-22b |
| G-35 | D-5: Malformed/unrecorded stanza does not grant custody | PC-35 | Sep-PC-23a |
| G-36 | D-5: Render retains explicit desired owned import | PC-36 | Sep-PC-23b |
| G-37 | D-4: Replacement checks full recorded owner | PC-37 | Sep-PC-24a |
| G-38 | D-4: Crash adoption requires exact persisted intent | PC-38 | Sep-PC-24b |
| G-39 | D-4: Tracked projection cannot be overwritten | PC-39 | Sep-PC-25 |
| G-40 | D-3: Native target containment enforced | PC-40 | Sep-PC-26a |
| G-41 | D-3: Native projection components reject reparse ambiguity | PC-41 | Sep-PC-26b |
| G-42 | D-5: CLAUDE reparse target refused | PC-42 | Sep-PC-26c |
| G-43 | D-4: Publish is atomic same-directory replacement | PC-43 | Sep-PC-27 |
| G-44 | D-4: Latest desired revision checked before host publication | PC-44 | Sep-PC-28a |
| G-45 | D-4: Projection overwrite compares expected bytes | PC-45 | Sep-PC-28b |
| G-46 | D-4: Cleanup rechecks desired revision | PC-46 | Sep-PC-28c |
| G-47 | D-4: Projection cleanup compares expected bytes | PC-47 | Sep-PC-28d |
| G-48 | D-4: Old location retained for live owner | PC-48 | Sep-PC-29a |
| G-49 | D-4: Delete retains custody outside FK cascade | PC-49 | Sep-PC-29b |
| G-50 | D-4: Cleanup limited to owned leaf | PC-50 | Sep-PC-30 |
| G-51 | D-4: Git exclude append preserves concurrent bytes | PC-51 | Sep-PC-31a |
| G-52 | D-4: Git exclude rules relative to Git root | PC-52 | Sep-PC-31b |
| G-53 | D-2: Named composition contains snapshot | PC-53 | Sep-PC-32a |
| G-54 | D-1: Ephemeral delegates do not inherit named pins | PC-54 | Sep-PC-32b |
| G-55 | D-2: Pin literals use an unbreakable fence | PC-55 | Sep-PC-33a |
| G-56 | D-2: Template expansion precedes pin literals | PC-56 | Sep-PC-33b |
| G-57 | D-2: Claude complete provider budget rechecked | PC-57 | Sep-PC-34a |
| G-58 | D-2: Codex complete provider budget rechecked | PC-58 | Sep-PC-34a |
| G-59 | D-2: Grok complete provider budget rechecked | PC-59 | Sep-PC-34a |
| G-60 | D-2: Secret placeholder tripwire applies to pin content | PC-60 | Sep-PC-34b |
| G-61 | D-4: Mutation commits durable reconciliation intent | PC-61 | Sep-PC-35a |
| G-62 | D-4: Publication intent precedes I/O | PC-62 | Sep-PC-35b |
| G-63 | D-7: Semantic change creates notification obligation | PC-63 | Sep-PC-36a |
| G-64 | D-7: Last revoke remains notification work | PC-64 | Sep-PC-36b |
| G-65 | D-7: Pin insertion never delivers inline | PC-65 | Sep-PC-37 |
| G-66 | D-7: Queue deduplication uses stable per-session key | PC-66 | Sep-PC-38a |
| G-67 | D-7: Attempted message cannot coalesce | PC-67 | Sep-PC-38b |
| G-68 | D-7: Working session receives only WhenIdle input | PC-68 | Sep-PC-39 |
| G-69 | D-7: Exhaustion does not create endless fresh keys | PC-69 | Sep-PC-40a |
| G-70 | D-7: Confirmed unanswered note is never retyped | PC-70 | Sep-PC-40b |
| G-71 | D-7: Enqueue/Sent is not recipient receipt | PC-71 | Sep-PC-41a |
| G-72 | D-7: Screen-only evidence is not recipient receipt | PC-72 | Sep-PC-41b |
| G-73 | D-7: Receipt must belong to owning session | PC-73 | Sep-PC-41c |
| G-74 | D-7: Receipt must match full hash | PC-74 | Sep-PC-41d |
| G-75 | D-7: Receipt must match location generation | PC-75 | Sep-PC-41e |
| G-76 | D-7: API continuity cannot clear file failure | PC-76 | Sep-PC-42 |
| G-77 | D-8: Dynamic snapshot excluded from static bundle stamp | PC-77 | Sep-PC-43a |
| G-78 | D-8: Exact owned file excluded from policy drift | PC-78 | Sep-PC-43b |
| G-79 | D-8: Managed marker normalized without pin stanza | PC-79 | Sep-PC-43c |
| G-80 | D-8: Unrelated similarly named file remains drift | PC-80 | Sep-PC-44a |
| G-81 | D-8: Unrelated .antiphon content remains drift | PC-81 | Sep-PC-44b |
| G-82 | D-8: Only verified owned stanza bytes normalized | PC-82 | Sep-PC-44c |
| G-83 | D-8: Plain named compact owes pin reread | PC-83 | Sep-PC-45a |
| G-84 | D-8: Manual named compact owes pin reread | PC-84 | Sep-PC-45b |
| G-85 | D-8: Generic watermark cannot discharge pin work | PC-85 | Sep-PC-45c |
| G-86 | D-8: Resume is a new obligation at same hash | PC-86 | Sep-PC-46a |
| G-87 | D-8: Each compact generation owes fresh read | PC-87 | Sep-PC-46b |
| G-88 | D-8: Latest full external set wins historical Grok snapshot | PC-88 | Sep-PC-47a |
| G-89 | D-8: Grok rules message includes pin obligation | PC-89 | Sep-PC-47b |
| G-90 | D-8: Pin messages obey rules barrier | PC-90 | Sep-PC-48a |
| G-91 | D-8: Rules ACK alone is not pin receipt | PC-91 | Sep-PC-48b |
| G-92 | D-7: Internal pin turn excluded from channel text | PC-92 | Sep-PC-49a |
| G-93 | D-7: Internal pin turn excluded from attachment conversion | PC-93 | Sep-PC-49b |
| G-94 | D-7: Internal pin turn excluded from immediate settlement | PC-94 | Sep-PC-49c |
| G-95 | D-7: Internal pin turn excluded from deferred settlement | PC-95 | Sep-PC-49d |
| G-96 | D-7: Internal pin turn excluded from boot evidence | PC-96 | Sep-PC-49e |
| G-97 | D-7: Human text cannot be suppressed by pin header | PC-97 | Sep-PC-50 |
| G-98 | D-1: Header-present failure never uses operator fallback | PC-98 | Sep-PC-51a |
| G-99 | D-1: Caller owns named agent | PC-99 | Sep-PC-51b |
| G-100 | D-1: Caller session is live | PC-100 | Sep-PC-51c |
| G-101 | D-1: Caller token is unexpired | PC-101 | Sep-PC-51c |
| G-102 | D-1: Task principals cannot manage pins | PC-102 | Sep-PC-51d |
| G-103 | D-1: Capability principals cannot manage pins | PC-103 | Sep-PC-51d |
| G-104 | D-1: Agent cannot mutate operator pins | PC-104 | Sep-PC-51e |
| G-105 | D-1: Agent cannot forge source | PC-105 | Sep-PC-51f |
| G-106 | D-1: Actor provenance derives from authenticated principal | PC-106 | Sep-PC-51f |
| G-107 | D-1: Agent cannot change import mode | PC-107 | Sep-PC-51f |
| G-108 | D-1: Expected revision serializes mutation | PC-108 | Sep-PC-52a |
| G-109 | D-1: Request replay fingerprint remains exact | PC-109 | Sep-PC-52b |
| G-110 | D-1: Active source key cannot be duplicated | PC-110 | Sep-PC-52c |
| G-111 | D-1: Capacity limit enforced | PC-111 | Sep-PC-53a |
| G-112 | D-1: Null text refused | PC-112 | Sep-PC-53b |
| G-113 | D-1: Normalized empty text refused | PC-113 | Sep-PC-53b |
| G-114 | D-1: Text maximum enforced | PC-114 | Sep-PC-53b |
| G-115 | D-1: Forbidden controls refused | PC-115 | Sep-PC-53c |
| G-116 | D-1: Source namespace length enforced | PC-116 | Sep-PC-53d |
| G-117 | D-1: Source key length enforced | PC-117 | Sep-PC-53d |
| G-118 | D-1: Source reference length enforced | PC-118 | Sep-PC-53d |
| G-119 | D-9: Changed events exclude raw pin text/source | PC-119 | Sep-PC-54a |
| G-120 | D-9: Attention/activity/log export excludes raw pin data | PC-120 | Sep-PC-54b |
| G-121 | D-9: UI projection status is independent of request success | PC-121 | Sep-PC-55a |
| G-122 | D-9: Invalidation preserves unsaved pin draft | PC-122 | Sep-PC-55b |
| G-123 | D-9: Conflict preserves unsaved operator append | PC-123 | Sep-PC-55c |
| G-124 | D-2: Tool-disabled instruction respects tool policy | PC-124 | Sep-PC-56 |
| G-125 | D-5: Unmarked import refuses tracked/staged target | PC-125 | Sep-PC-57a |
| G-126 | D-5: Managed import refuses tracked/staged target | PC-126 | Sep-PC-57b |
| G-127 | D-5: Unmarked Git import requires prior ignore | PC-127 | Sep-PC-57c |
| G-128 | D-5: Managed Git import requires prior ignore | PC-128 | Sep-PC-57d |
| G-129 | D-6: Read-only composition does not enforce Start gate | PC-129 | Sep-PC-58 |
| G-130 | D-4: Confirmed-absent unused location retires cleanly | PC-130 | Sep-PC-59a |
| G-131 | D-4: Required missing location cannot retire | PC-131 | Sep-PC-59b |
| G-132 | D-5: Append writer excludes managed floors | PC-132 | Sep-PC-60a |
| G-133 | D-6: Start verifies projection before Render | PC-133 | Sep-PC-60b |
| G-134 | D-8: Append crash intent authorizes exact normalization | PC-134 | Sep-PC-61a |
| G-135 | D-8: Render crash intent authorizes exact normalization | PC-135 | Sep-PC-61b |
| G-136 | D-8: Removal crash intent authorizes exact normalization | PC-136 | Sep-PC-61c |
| G-137 | D-8: Policy selector includes named static protocol | PC-137 | Sep-PC-62a |
| G-138 | D-8: Static protocol selection independent of pin count | PC-138 | Sep-PC-62b |
| G-139 | D-3: POSIX case-distinct paths never fold together | PC-139 | October addition |
| G-140 | D-3: Windows aliases share canonical publication lock | PC-140 | October addition |
| G-141 | D-3: Runner store identity binds readiness | PC-141 | October addition |
| G-142 | D-3: Legacy path schema cannot grant native authority | PC-142 | October addition |
| G-143 | D-4: Ready completion compares current durable operation | PC-143 | October addition |
| G-144 | D-4: Ready completion compares desired revision | PC-144 | October addition |
| G-145 | D-4: Ready completion compares full hash | PC-145 | October addition |
| G-146 | D-4: Ready completion compares owner | PC-146 | October addition |
| G-147 | D-4: Ready completion compares location generation | PC-147 | October addition |
| G-148 | D-4: Ready completion compares runner store | PC-148 | October addition |
| G-149 | D-4: Host rejects operation older than durable fence | PC-149 | October addition |
| G-150 | D-4: Cleanup tombstone survives host restart | PC-150 | October addition |
| G-151 | D-4: Replay receipt verifies disk bytes | PC-151 | October addition |
| G-152 | D-4: Configured consumer flag is recomputed | PC-152 | October addition |
| G-153 | D-4: Unknown host inspection is not absence | PC-153 | October addition |
| G-154 | D-4: Nullable legacy custody cannot delete | PC-154 | October addition |
| G-155 | D-4: File and import custody kept separately | PC-155 | October addition |
| G-156 | D-4: Pin transaction never performs filesystem/runner I/O | PC-156 | October addition |
| G-157 | D-3: Typed operation validates server-derived target | PC-157 | October addition |
| G-158 | D-3: Unsupported runner capability is explicit | PC-158 | October addition |
| G-159 | D-3: Typed HTTP operation preserves durable identity | PC-159 | October addition |
| G-160 | D-3: Typed phone-home operation preserves durable identity | PC-160 | October addition |
| G-161 | D-5: Unmarked writer rechecks staged status at publication | PC-161 | October addition |
| G-162 | D-5: Managed writer rechecks staged status at publication | PC-162 | October addition |
| G-163 | D-6: Final location evidence revalidated | PC-163 | October addition |
| G-164 | D-6: Final store evidence revalidated | PC-164 | October addition |
| G-165 | D-6: Final scope evidence revalidated | PC-165 | October addition |
| G-166 | D-7: Notification intent durable before enqueue | PC-166 | October addition |
| G-167 | D-7: Coverage linked before normal flush | PC-167 | October addition |
| G-168 | D-7: Stranded scan includes manual pin System rows | PC-168 | October addition |
| G-169 | D-7: Stable notification key fits database bound | PC-169 | October addition |
| G-170 | D-7: Recipient evidence must be complete | PC-170 | October addition |
| G-171 | D-7: Receipt cannot cross launch/conversation generation | PC-171 | October addition |
| G-172 | D-7: Receipt requires attempt sequence/time floor | PC-172 | October addition |
| G-173 | D-7: Receipt never downgrades adopted revision | PC-173 | October addition |
| G-174 | D-7: Launch evidence immutable during notification | PC-174 | October addition |
| G-175 | D-7: Pending work canceled when session obsolete | PC-175 | October addition |
| G-176 | D-7: Degraded API note only after actual file failure | PC-176 | October addition |
| G-177 | D-8: Grok compact uses one combined recovery turn | PC-177 | October addition |
| G-178 | D-8: Rules bytes not rewritten by dynamic pins | PC-178 | October addition |
| G-179 | D-9: Semantic no-op does not create activity | PC-179 | October addition |
| G-180 | D-9: Dormant slices do not activate partial path | PC-180 | October addition |
| G-181 | D-9: Activation registers the real reconciler | PC-181 | October addition |
| G-182 | D-7: Receipt matches exact pin key | PC-182 | October addition |
| G-183 | D-7: Receipt matches requested revision | PC-183 | October addition |
| G-184 | D-7: Receipt matches exact file path | PC-184 | October addition |
| G-185 | D-2: Snapshot order and hash depend on immutable ID/text only | PC-185 | October addition |
| G-186 | D-2: Used-but-empty snapshot explicitly replaces old pins | PC-186 | October addition |
| G-187 | D-3: Never-used agent creates no pin file | PC-187 | October addition |
| G-188 | D-2: Effective profile kind controls protocol support | PC-188 | October addition |
| G-189 | D-4: User-owned import retains neutral tombstone | PC-189 | October addition |
| G-190 | D-4: Host validates expected content digest | PC-190 | October addition |
| G-191 | D-3: Native component checks repeated before final I/O | PC-191 | October addition |
| G-192 | D-7: Coverage commits all superseded never-attempted obligations | PC-192 | October addition |
| G-193 | D-8: Working state still gates policy refresh | PC-193 | October addition |
| G-194 | D-8: Cooldown still gates policy refresh | PC-194 | October addition |
| G-195 | D-8: Model-budget hold still gates policy refresh | PC-195 | October addition |
| G-196 | D-8: Credential refusal still gates launch | PC-196 | October addition |
| G-197 | D-8: Legacy Grok resume refusal remains enforced | PC-197 | October addition |
| G-198 | D-1: Provenance controls refused | PC-198 | October addition |
| G-199 | D-1: Source namespace and key remain paired | PC-199 | October addition |
| G-200 | D-4: Host rejects mismatched full owner | PC-200 | October addition |
| G-201 | D-4: Host rejects unsupported pin schema | PC-201 | October addition |
| G-202 | D-4: Host rejects mismatched store identity | PC-202 | October addition |
| G-203 | D-4: Host rejects unknown operation kind | PC-203 | October addition |
| G-204 | D-9: Activation starts notification worker | PC-204 | October addition |
| G-205 | D-9: Activation starts startup reconciliation | PC-205 | October addition |
| G-206 | D-9: Activation schedules reconciliation backstop | PC-206 | October addition |
| G-207 | D-9: Activation subscribes native compact recovery | PC-207 | October addition |

### Positive controls

Run **baseline green → one compiling defect → intended assertion red → exact source restore/fresh build → same method green** after confirmed land. Code executes ordinary V/R only; Review judges reachability before land. Bind each PC to the earliest landed slice where its method and targeted guard are complete, recording original/reviewed/landed SHAs. PC-180 (dormancy) must run on the S2-core landed snapshot before S2-launch changes its assertion to the activated graph; its preserved result cannot be rerun against the final activated source. All other controls may run on their complete slice or the final landing; native companion cycles require the S7 landing. Recommission a control when later changes affect its guard, retaining the earlier SHA evidence. The mutations below target production guards, never test expectations. Application seams are the slice's named production service; native path/fence guards target AgentPinWorkspaceStore, import guards its writer/AgentPinClaudeImportService/Render, queue guards the notification/queue services, receipt guards the production pin-receipt reconciler, and UI guards AgentPinnedInstructions. Record actual implementation file/line and exact edit when landing supplies the symbols. These are concrete branch/assignment substitutions that must compile; absent seams or masked assertions return to Code/Plan, not a claimed red.

Each main test gets a stable assertion message `c262-gNNN` for its mapped row, plus the named scenario. Assert directly at the guarded seam before downstream defense can mask the defect; seed historical state to reach it. For example, CAS tests assert affected rows before launch, import tests observe bytes before a later Start refusal, and queue tests observe input at the unlinked barrier. Do not disable a second guard. All native/path mutations use fixture-owned roots only. A build error, zero tests, timeout, dependency/fixture failure or surviving mutant is not an expected red.

The exact TUnit filter for each class-qualified method below is `/*/*/ClassName/ExactMethodName`, replacing both segments with that row's literal class/method (no whole-class PC run). New methods have no Arguments suffix. For each existing parameterized regression ever used as a follow-up PC, use its exact method prefix per the testing owner and inspect every Arguments result. UI uses `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c262-ui-pc -- pwsh -NoProfile -File scripts/test-client.ps1 AgentPinnedInstructions.test -t ^V26_Drafts$` (substitute only the row's exact V26 title).

Costs in the last column are minutes for baseline/red/restored-green: I=3 × (2 build + 1 test)=9; N=3 × (2 build + 2 test)=12; U=3 × 1 Vitest/transpile=3 (no separate C# build). Native companions C=3 × (2 build + 20 test)=66 and Grok behavior B=3 × (2 build + 120 test)=366 are additional full three-phase cycles. All are estimates, not observed timings. SourceLanding uses copied local checkpoint driver/library and phase-specific alternate outputs, retains evidence externally, restores source and never commits/pushes from its snapshot.

| PC | Break guard by this compiling production defect | Exact method expected red | Decisive assertion (also required in restored green) | Minutes |
|---|---|---|---|---:|
| PC-1 | G-1: Return from reconcile unless import mode is Dedicated | `AgentPinWorkspaceTests.V06_FirstUseMatrix` | Shared/Unverified/Disabled expected file exists; label `c262-g001` | 9 |
| PC-2 | G-2: Return a successful write result without calling host publish | `AgentPinWorkspaceTests.V06_FirstUseMatrix` | Independent ReadAllBytes equals expected full set; label `c262-g002` | 9 |
| PC-3 | G-3: Use AgentId hex substring 0..8 for directory component | `AgentPinWorkspaceTests.V07_FullIdentityConcurrent` | A and B exact full-ID paths both exist; label `c262-g003` | 9 |
| PC-4 | G-4: Use display name for directory component | `AgentPinWorkspaceTests.V07_FullIdentityConcurrent` | Rename preserves absolute path and recreated C has distinct path; label `c262-g004` | 9 |
| PC-5 | G-5: Return cwd/antiphon.md as projection target | `AgentPinWorkspaceTests.V07_FullIdentityConcurrent` | Root decoy bytes unchanged and full-ID path exists; label `c262-g005` | 9 |
| PC-6 | G-6: Publish a copy of each new pin file over root antiphon.md | `AgentPinWorkspaceTests.V07_FullIdentityConcurrent` | Root decoy bytes unchanged; label `c262-g006` | 9 |
| PC-7 | G-7: Select first agent with matching cwd when choosing recipient | `AgentPinRefreshTests.V19_OwnershipAndPaths` | Only A recipient prompt names A full ID/path and B receives no A note; label `c262-g007` | 9 |
| PC-8 | G-8: Backfill StandingAgentId from current Card.AgentId | `AgentPinRefreshTests.V19_OwnershipAndPaths` | Ambiguous/reassigned legacy session remains unavailable, not adopted; label `c262-g008` | 9 |
| PC-9 | G-9: Replace finalized launch cwd with configured agent cwd | `AgentPinLaunchIntegrationTests.V04_AllEntryPoints` | Real runtime worktree file exists and runner payload names it; label `c262-g009` | 9 |
| PC-10 | G-10: Use server-local workspace writer for bound runner request | `AgentPinLocationTests.V32_FinalNativeLocation` | Bound host has expected bytes and server decoy is unchanged; label `c262-g010` | 9 |
| PC-11 | G-11: Suppress notification when only content hash matches | `AgentPinRefreshTests.V19_OwnershipAndPaths` | New location generation has covered complete UserPrompt; label `c262-g011` | 9 |
| PC-12 | G-12: Bypass mandatory Ready/byte-verification gate at runtime start | `AgentPinLaunchIntegrationTests.V13_StartFailureMatrix` | Unavailable fresh/resume has zero runner launch attempts; label `c262-g012` | 9 |
| PC-13 | G-13: Skip desired revision comparison immediately before runner call | `AgentPinLaunchIntegrationTests.V35_FinalRecheck` | Obsolete revision never reaches runner; label `c262-g013` | 9 |
| PC-14 | G-14: Convert stale-projection refusal to available | `AgentPinLaunchIntegrationTests.V13_StartFailureMatrix` | Failed neutralization returns pin_projection_stale with zero runner launch attempts; label `c262-g014` | 9 |
| PC-15 | G-15: Use exact-cwd equality in overlap predicate | `AgentPinImportTests.V08_DiscoveryMatrix` | Both ancestor directions refuse exclusive import; label `c262-g015` | 9 |
| PC-16 | G-16: Filter overlap candidates to running agents | `AgentPinImportTests.V08_DiscoveryMatrix` | Stopped-owner overlap has no new stanza; label `c262-g016` | 9 |
| PC-17 | G-17: Omit task workspace candidates from overlap scan | `AgentPinImportTests.V08_DiscoveryMatrix` | Live task overlap has no new stanza; label `c262-g017` | 9 |
| PC-18 | G-18: Replace scope-lock acquisition with completed no-op lease | `AgentPinImportTests.V08_AdmissionRace` | Two gated contenders cannot retain conflicting exclusive imports; label `c262-g018` | 9 |
| PC-19 | G-19: Ignore owned-import removal failure and admit sharing | `AgentPinImportTests.V08_AdmissionRace` | Conflict remains and zero runner attempts; label `c262-g019` | 9 |
| PC-20 | G-20: Treat Shared/Unverified as import eligible | `AgentPinImportTests.V08_DiscoveryMatrix` | Shared CLAUDE bytes remain exactly original; label `c262-g020` | 9 |
| PC-21 | G-21: Move same-cwd import scan after host publication | `AgentPinImportTests.V09_DanglingBeforePublish` | Pre-publication observer never sees nonempty private file; label `c262-g021` | 9 |
| PC-22 | G-22: Remove ancestor discovery scan | `AgentPinImportTests.V09_DanglingBeforePublish` | Ancestor dangling target never contains nonempty pins; label `c262-g022` | 9 |
| PC-23 | G-23: Return native-ready for unsafe user-owned shared import | `AgentPinImportTests.V09_DanglingBeforePublish` | User bytes retained and owned private target stays neutralized until repair; label `c262-g023` | 9 |
| PC-24 | G-24: Return installed without appending stanza | `AgentPinImportTests.V10_ByteRoundTrip` | One exact v2 full-ID stanza exists; label `c262-g024` | 9 |
| PC-25 | G-25: Render whole managed floor over eligible unmarked file | `AgentPinImportTests.V10_ByteRoundTrip` | Original byte prefix is unchanged and no managed marker added; label `c262-g025` | 9 |
| PC-26 | G-26: Decode then re-encode whole CLAUDE using normalized UTF-8 LF | `AgentPinImportTests.V10_ByteRoundTrip` | BOM/CRLF/non-ASCII byte prefix equals original; label `c262-g026` | 9 |
| PC-27 | G-27: Leave recorded append separator behind on removal | `AgentPinImportTests.V10_ByteRoundTrip` | No-final-newline original restored byte-for-byte; label `c262-g027` | 9 |
| PC-28 | G-28: Remove one additional newline preceding recorded range | `AgentPinImportTests.V10_ByteRoundTrip` | Multiple-final-newlines original restored byte-for-byte; label `c262-g028` | 9 |
| PC-29 | G-29: Skip final append expected-byte comparison | `AgentPinImportTests.V12_EditAndEncoding` | Barrier-injected author bytes remain untouched on conflict; label `c262-g029` | 9 |
| PC-30 | G-30: Skip final removal expected-byte comparison | `AgentPinImportTests.V12_EditAndEncoding` | Edited stanza/separator unchanged and import not marked removed; label `c262-g030` | 9 |
| PC-31 | G-31: Use replacement decoder and continue append on invalid bytes | `AgentPinImportTests.V12_EditAndEncoding` | Malformed UTF-8/UTF-32/UTF-16 remain unchanged with unsupported status; label `c262-g031` | 9 |
| PC-32 | G-32: Persist cleanup custody for a pre-existing equivalent user import | `AgentPinImportTests.V11_RecognitionAndRender` | Disabling imports retains the exact user-authored import; label `c262-g032` | 9 |
| PC-33 | G-33: Accept fenced/inline occurrence as equivalent active import | `AgentPinImportTests.V11_RecognitionAndRender` | Eligible real stanza exists despite fenced example; label `c262-g033` | 9 |
| PC-34 | G-34: Accept any antiphon.md target as equivalent | `AgentPinImportTests.V11_RecognitionAndRender` | Wrong-ID/root never satisfies own target; label `c262-g034` | 9 |
| PC-35 | G-35: Accept legacy or malformed marker as owned | `AgentPinImportTests.V11_RecognitionAndRender` | Foreign malformed stanza unchanged with conflict; label `c262-g035` | 9 |
| PC-36 | G-36: Drop import state when regenerating managed floor | `AgentPinImportTests.V11_RecognitionAndRender` | Job/channel/style regenerated floor retains exactly one own stanza; label `c262-g036` | 9 |
| PC-37 | G-37: Remove AgentId ownership comparison from overwrite admission | `AgentPinWorkspaceTests.V13_ForeignTrackedRefusal` | B-owned target bytes unchanged; label `c262-g037` | 9 |
| PC-38 | G-38: Accept marker-shaped file with no matching durable intent | `AgentPinWorkspaceTests.V14_CrashPublication` | Forged artifact remains conflict rather than Ready; label `c262-g038` | 9 |
| PC-39 | G-39: Bypass tracked-target refusal while leaving ignore logic intact | `AgentPinWorkspaceTests.V13_ForeignTrackedRefusal` | Tracked-even-ignored bytes unchanged; label `c262-g039` | 9 |
| PC-40 | G-40: Skip target containment predicate | `AgentPinWorkspaceStoreTests.V13_PathSafety` | Outside canary unchanged and no trusted outside receipt; label `c262-g040` | 12 |
| PC-41 | G-41: Bypass component link/reparse inspection | `AgentPinWorkspaceWindowsTests.V13_ReparseAllComponents` | Junction/symlink referents untouched and receipt refused; label `c262-g041` | 12 |
| PC-42 | G-42: Follow CLAUDE file symlink when appending import | `AgentPinImportWindowsTests.V12_ImportReparseBytes` | CLAUDE referent bytes unchanged; label `c262-g042` | 12 |
| PC-43 | G-43: Write intended bytes directly to final target in two gated chunks | `AgentPinWorkspaceStoreTests.V14_AtomicAndFence` | At barrier target is absent/old complete bytes, never partial; label `c262-g043` | 12 |
| PC-44 | G-44: Omit newest-revision condition before replace | `AgentPinWorkspaceTests.V14_LatestAndCompare` | Released n cannot replace already-current n+1; label `c262-g044` | 9 |
| PC-45 | G-45: Bypass final file compare before overwrite | `AgentPinWorkspaceTests.V14_LatestAndCompare` | Concurrent author bytes preserved; label `c262-g045` | 9 |
| PC-46 | G-46: Omit current desired revision/re-pin check from cleanup | `AgentPinWorkspaceTests.V14_LatestAndCompare` | Re-pinned current file survives old cleanup; label `c262-g046` | 9 |
| PC-47 | G-47: Bypass final file compare before delete | `AgentPinWorkspaceTests.V14_LatestAndCompare` | Concurrent author bytes survive cleanup; label `c262-g047` | 9 |
| PC-48 | G-48: Ignore live consumer count on configured cwd change | `AgentPinWorkspaceTests.V15_ConsumersAndCleanup` | Old live file exists and receives next revision; label `c262-g048` | 9 |
| PC-49 | G-49: Omit copying file/import custody before agent delete commit | `AgentPinWorkspaceTests.V15_ConsumersAndCleanup` | Fresh service after cascade completes exact owned cleanup; label `c262-g049` | 9 |
| PC-50 | G-50: Recursively delete fixture pins parent instead of owner leaf | `AgentPinWorkspaceTests.V15_ConsumersAndCleanup` | B/C/sibling and unrelated .antiphon sentinel bytes survive; label `c262-g050` | 9 |
| PC-51 | G-51: Overwrite info/exclude with only current agent rule | `AgentPinWorkspaceTests.V17_GitExcludes` | Original bytes and both A/B rules retained; label `c262-g051` | 9 |
| PC-52 | G-52: Build exclude rule relative to subdir cwd | `AgentPinWorkspaceTests.V17_GitExcludes` | Real git check-ignore covers final and temp in linked subdir; label `c262-g052` | 9 |
| PC-53 | G-53: Drop pin segment from composed provider payload | `AgentPinLaunchIntegrationTests.V04_AllEntryPoints` | Actual provider request contains independent pin canary and full hash; label `c262-g053` | 9 |
| PC-54 | G-54: Run named selector on pool/task role | `AgentPinnedInstructionCompositionTests.V04_ExcludedAndToolDisabled` | Delegate payload excludes pin and capture protocol; label `c262-g054` | 9 |
| PC-55 | G-55: Render a fixed triple-backtick fence | `AgentPinnedInstructionCompositionTests.V05_LiteralOrderEmptyHash` | Content delimiter cannot terminate literal pinned block; label `c262-g055` | 9 |
| PC-56 | G-56: Expand channel placeholders after adding pins | `AgentPinnedInstructionCompositionTests.V05_LiteralOrderEmptyHash` | Literal {agentName} stays byte-identical; label `c262-g056` | 9 |
| PC-57 | G-57: Skip Claude final complete payload/launch budget validation | `AgentPinnedInstructionCompositionTests.V05_ResolvedBudgets` | Claude N+1 refuses with zero launch attempt; label `c262-g057` | 9 |
| PC-58 | G-58: Skip Codex final complete payload/launch budget validation | `AgentPinnedInstructionCompositionTests.V05_ResolvedBudgets` | Codex N+1 refuses with zero launch attempt; label `c262-g058` | 9 |
| PC-59 | G-59: Skip Grok final complete payload/launch budget validation | `AgentPinnedInstructionCompositionTests.V05_ResolvedBudgets` | Grok N+1 refuses with zero launch attempt; label `c262-g059` | 9 |
| PC-60 | G-60: Bypass unresolved-key check for pin-bearing payload | `AgentPinnedInstructionCompositionTests.V05_ResolvedBudgets` | Secret placeholder refuses before runner call; label `c262-g060` | 9 |
| PC-61 | G-61: Omit dirty intent write in capture transaction | `AgentPinnedInstructionServiceTests.V01_RestartMigration` | Post-commit wakeup loss still leaves recoverable desired state; label `c262-g061` | 9 |
| PC-62 | G-62: Omit saving exact intended bytes/operation before publish | `AgentPinWorkspaceTests.V14_CrashPublication` | Fresh service recovers only recorded published bytes after receipt loss; label `c262-g062` | 9 |
| PC-63 | G-63: Omit change notification trigger after publication | `AgentPinRefreshTests.V18_HandoffRecovery` | Replace converges to matching complete recipient prompt; label `c262-g063` | 9 |
| PC-64 | G-64: Return early when active count is zero | `AgentPinRefreshTests.V20_BusyCoalescing` | Last-revoke complete empty-set UserPrompt received; label `c262-g064` | 9 |
| PC-65 | G-65: Pass deliverIfIdle:true for pin enqueue | `AgentPinRefreshTests.V18_AlreadyEligible` | Input count remains zero at unlinked-coverage barrier; label `c262-g065` | 9 |
| PC-66 | G-66: Generate fresh key on recovery after committed queue insert | `AgentPinRefreshTests.V18_HandoffRecovery` | One durable queue identity and one recipient prompt after replay; label `c262-g066` | 9 |
| PC-67 | G-67: Allow rows with attempts > 0 into coalescing candidates | `AgentPinRefreshTests.V20_AttemptedImmutable` | Attempted bytes/metadata/verdict unchanged; label `c262-g067` | 9 |
| PC-68 | G-68: Choose Now instead of WhenIdle in pin enqueue | `AgentPinRefreshTests.V20_BusyCoalescing` | Zero input and Enter before committed TurnEnd; label `c262-g068` | 9 |
| PC-69 | G-69: Create a new obligation key for parked/exhausted row on sweep | `AgentPinRefreshTests.V20_BoundedRetry` | Repeated sweeps preserve key count and attempt bound; label `c262-g069` | 9 |
| PC-70 | G-70: Retry confirmed row when no assistant response exists | `AgentPinRefreshTests.V20_BoundedRetry` | Confirmed unanswered prompt has zero additional terminal writes; label `c262-g070` | 9 |
| PC-71 | G-71: Advance PinLastNotified immediately on queue insertion | `AgentPinRefreshTests.V21_EvidenceLadder` | Receipt fields remain null without complete UserPrompt; label `c262-g071` | 9 |
| PC-72 | G-72: Accept screen-only delivery verdict as pin receipt | `AgentPinRefreshTests.V21_EvidenceLadder` | Screen-only arm does not advance PinLastNotified; label `c262-g072` | 9 |
| PC-73 | G-73: Remove AgentSessionId predicate from pin receipt query | `AgentPinRefreshTests.V21_EvidenceLadder` | Wrong-session complete prompt leaves receipt unchanged; label `c262-g073` | 9 |
| PC-74 | G-74: Ignore hash when correlating complete pin prompt | `AgentPinRefreshTests.V21_EvidenceLadder` | Wrong-hash UserPrompt leaves receipt unchanged; label `c262-g074` | 9 |
| PC-75 | G-75: Ignore location generation when correlating pin prompt | `AgentPinRefreshTests.V21_EvidenceLadder` | Wrong-location UserPrompt leaves receipt unchanged; label `c262-g075` | 9 |
| PC-76 | G-76: Mark projection Ready when degraded prompt is confirmed | `AgentPinRefreshTests.V16_ApiDoesNotRepair` | Mandatory file remains failed and launch blocked; label `c262-g076` | 9 |
| PC-77 | G-77: Append pin hash to static policy stamp | `AgentPinPolicyTests.V22_PinOnly` | Pin-only change causes zero kill/start; label `c262-g077` | 9 |
| PC-78 | G-78: Stop normalizing explicitly configured owned pin file | `AgentPinPolicyTests.V22_PinOnly` | Owned-file-only change causes zero kill/start; label `c262-g078` | 9 |
| PC-79 | G-79: Hash changed managed marker without recomputing static floor | `AgentPinPolicyTests.V22_PinOnly` | Managed import change causes zero kill/start; label `c262-g079` | 9 |
| PC-80 | G-80: Ignore every antiphon.md path | `AgentPinPolicyTests.V22_RealDrift` | Root/sibling authored antiphon.md change is drift; label `c262-g080` | 9 |
| PC-81 | G-81: Ignore all .antiphon paths | `AgentPinPolicyTests.V22_RealDrift` | Unrelated configured .antiphon file change is drift; label `c262-g081` | 9 |
| PC-82 | G-82: Strip all stanza-looking ranges regardless of custody | `AgentPinPolicyTests.V22_RealDrift` | Malformed/unrecorded stanza change is drift; label `c262-g082` | 9 |
| PC-83 | G-83: Require nonempty preamble for pin compact trigger | `AgentPinRecoveryTests.V23_PlainDurable` | No-preamble boundary reaches matching complete recipient prompt; label `c262-g083` | 9 |
| PC-84 | G-84: Require AlwaysOn for pin compact trigger | `AgentPinRecoveryTests.V23_PlainDurable` | Manual boundary reaches matching complete recipient prompt; label `c262-g084` | 9 |
| PC-85 | G-85: Treat generic compact watermark as pin delivery coverage | `AgentPinRecoveryTests.V23_PlainDurable` | Restart after watermark-before-enqueue still receives prompt; label `c262-g085` | 9 |
| PC-86 | G-86: Skip resume trigger when hash equals last delivered | `AgentPinRecoveryTests.V23_ResumeTwoCompacts` | New resume generation has complete empty-set prompt; label `c262-g086` | 9 |
| PC-87 | G-87: Deduplicate compact triggers by content hash only | `AgentPinRecoveryTests.V23_ResumeTwoCompacts` | Two distinct compact boundaries each have complete prompt coverage; label `c262-g087` | 9 |
| PC-88 | G-88: Render old launch revision/path as authoritative combined note | `AgentPinRecoveryTests.V24_GrokCombined` | Combined request names current empty revision and own current path; label `c262-g088` | 9 |
| PC-89 | G-89: Omit pin read segment/coverage from combined refresh | `AgentPinRecoveryTests.V24_GrokCombined` | Single actual rules prompt contains current pin request with linked coverage; label `c262-g089` | 9 |
| PC-90 | G-90: Treat pin refresh key as unconditional bypass of closed rules state | `AgentPinRecoveryTests.V24_GrokBarrier` | All entry points have zero pin input while barrier closed; label `c262-g090` | 9 |
| PC-91 | G-91: Advance pin receipt on rules ACK without matching UserPrompt | `AgentPinRecoveryTests.V24_GrokBarrier` | ACK-only leaves PinLastNotified unchanged; label `c262-g091` | 9 |
| PC-92 | G-92: Remove pin-backed internal branch in text dispatch | `AgentPinInternalTurnTests.V25_RoutingGuards` | Zero outbound text for arbitrary internal answer; label `c262-g092` | 9 |
| PC-93 | G-93: Remove pin-backed internal branch in attachment route | `AgentPinInternalTurnTests.V25_RoutingGuards` | Zero attachments and PDF conversion calls; label `c262-g093` | 9 |
| PC-94 | G-94: Remove pin-backed housekeeping check from task turn selector | `AgentPinInternalTurnTests.V25_RoutingGuards` | Valid-looking report token leaves task unsettled; label `c262-g094` | 9 |
| PC-95 | G-95: Remove pin-backed exclusion in deferred report sweep | `AgentPinInternalTurnTests.V25_RoutingGuards` | Late internal answer does not settle/nudge task; label `c262-g095` | 9 |
| PC-96 | G-96: Count internal pin answer as boot reply | `AgentPinInternalTurnTests.V25_RoutingGuards` | Boot obligation remains unchanged by internal answer; label `c262-g096` | 9 |
| PC-97 | G-97: Classify pin internal turn by prefix alone | `AgentPinInternalTurnTests.V25_HumanLookalike` | Human lookalike receives ordinary outbound response; label `c262-g097` | 9 |
| PC-98 | G-98: On invalid/empty token return operator principal | `AgentPinnedInstructionEndpointTests.V03_PrincipalMatrix` | Invalid/empty header is 403 and no mutation; label `c262-g098` | 9 |
| PC-99 | G-99: Remove caller AgentId equality check | `AgentPinnedInstructionEndpointTests.V03_PrincipalMatrix` | Cross-agent token receives 403; label `c262-g099` | 9 |
| PC-100 | G-100: Ignore stopped-session state | `AgentPinnedInstructionEndpointTests.V03_PrincipalMatrix` | Stopped caller receives 403; label `c262-g100` | 9 |
| PC-101 | G-101: Ignore expired token state | `AgentPinnedInstructionEndpointTests.V03_PrincipalMatrix` | Expired caller receives 403; label `c262-g101` | 9 |
| PC-102 | G-102: Accept task MayDelegate principal as named owner | `AgentPinnedInstructionEndpointTests.V03_PrincipalMatrix` | Task MayDelegate caller receives 403; label `c262-g102` | 9 |
| PC-103 | G-103: Accept capability principal as named owner | `AgentPinnedInstructionEndpointTests.V03_PrincipalMatrix` | Capability caller receives 403; label `c262-g103` | 9 |
| PC-104 | G-104: Remove source ownership check on replace/revoke | `AgentPinnedInstructionEndpointTests.V03_PrincipalMatrix` | Agent edit of operator pin is refused; label `c262-g104` | 9 |
| PC-105 | G-105: Remove request.Source versus principal.Source refusal | `AgentPinnedInstructionEndpointTests.V03_PrincipalMatrix` | Forged operator source from live agent token receives 403; label `c262-g105` | 9 |
| PC-106 | G-106: Assign another seeded session ID instead of principal.SessionId to CreatedBySessionId | `AgentPinnedInstructionEndpointTests.V03_PrincipalMatrix` | Persisted CreatedBySessionId is exactly the authenticated owner, never the supplied or foreign actor; label `c262-g106` | 9 |
| PC-107 | G-107: Ignore agent restriction on Dedicated mutation | `AgentPinnedInstructionEndpointTests.V03_PrincipalMatrix` | Agent Dedicated write refused; label `c262-g107` | 9 |
| PC-108 | G-108: Skip expectedRevision comparison under lock | `AgentPinnedInstructionServiceTests.V02_AllValidationBoundaries` | Concurrent same-revision writes have exactly one winner; label `c262-g108` | 9 |
| PC-109 | G-109: Return replay result without fingerprint comparison | `AgentPinnedInstructionServiceTests.V01_RestartMigration` | Changed request fingerprint conflicts even with stale expected revision; label `c262-g109` | 9 |
| PC-110 | G-110: Ignore active source conflict during capture | `AgentPinnedInstructionServiceTests.V02_AllValidationBoundaries` | Same-source changed-text capture requires explicit replacement; label `c262-g110` | 9 |
| PC-111 | G-111: Remove active-count >= 20 refusal | `AgentPinnedInstructionServiceTests.V02_AllValidationBoundaries` | Twenty-first pin rejected with unchanged revision; label `c262-g111` | 9 |
| PC-112 | G-112: Replace null-input rejection with text = "accepted-null" before normalization | `AgentPinnedInstructionServiceTests.V02_AllValidationBoundaries` | Null text receives text validation refusal without persistence; label `c262-g112` | 9 |
| PC-113 | G-113: Remove normalized.Length == 0 rejection | `AgentPinnedInstructionServiceTests.V02_AllValidationBoundaries` | Empty/whitespace-only text receives text validation refusal without persistence; label `c262-g113` | 9 |
| PC-114 | G-114: Remove normalized.Length > MaxTextLength rejection | `AgentPinnedInstructionServiceTests.V02_AllValidationBoundaries` | 501 UTF-16 units receives typed text validation refusal without persistence; label `c262-g114` | 9 |
| PC-115 | G-115: Bypass control-character validation | `AgentPinnedInstructionServiceTests.V02_AllValidationBoundaries` | NUL/ESC/U+0001 rejected without persistence; label `c262-g115` | 9 |
| PC-116 | G-116: Skip NormalizeOptional maximum check only when field is sourceNamespace | `AgentPinnedInstructionServiceTests.V02_AllValidationBoundaries` | 65-character namespace receives typed validation refusal; label `c262-g116` | 9 |
| PC-117 | G-117: Skip NormalizeOptional maximum check only when field is sourceKey | `AgentPinnedInstructionServiceTests.V02_AllValidationBoundaries` | 201-character key receives typed validation refusal; label `c262-g117` | 9 |
| PC-118 | G-118: Skip NormalizeOptional maximum check only when field is sourceRef | `AgentPinnedInstructionServiceTests.V02_AllValidationBoundaries` | 201-character reference receives typed validation refusal; label `c262-g118` | 9 |
| PC-119 | G-119: Add Text and SourceRef to ordinary changed-event payload | `AgentPinInternalTurnTests.V25_RedactedActivity` | Captured event serialization excludes synthetic text/source canaries; label `c262-g119` | 9 |
| PC-120 | G-120: Include pin text/source in repair attention message | `AgentPinInternalTurnTests.V25_RedactedActivity` | Attention/export/log payloads exclude canaries; label `c262-g120` | 9 |
| PC-121 | G-121: Derive file-ready from queued/API receipt | `AgentPinnedInstructions.test.tsx::V26_StatusLadder` | Failed mandatory file still displays repair action after confirmed API continuity; label `c262-g121` | 3 |
| PC-122 | G-122: Reset pin draft in query invalidation effect | `AgentPinnedInstructions.test.tsx::V26_Drafts` | Pin draft text remains after invalidation; label `c262-g122` | 3 |
| PC-123 | G-123: Reset SystemPromptAppend draft on 409 reload | `AgentPinnedInstructions.test.tsx::V26_Drafts` | Operator append draft remains after conflict; label `c262-g123` | 3 |
| PC-124 | G-124: Emit normal file/API read recipe for deny-all-tools seat | `AgentPinnedInstructionCompositionTests.V04_ExcludedAndToolDisabled` | No forbidden tool recipe and live-read status unavailable; label `c262-g124` | 9 |
| PC-125 | G-125: Bypass tracked guard only in append writer | `AgentPinImportTests.V31_GitAdmission` | Unmarked tracked/staged file/index unchanged and refusal shown; label `c262-g125` | 9 |
| PC-126 | G-126: Bypass tracked guard only in Render writer | `AgentPinImportTests.V31_GitAdmission` | Managed tracked/staged file/index unchanged and refusal shown; label `c262-g126` | 9 |
| PC-127 | G-127: Bypass effective-ignore check only in append writer | `AgentPinImportTests.V31_GitAdmission` | Untracked nonignored append refused with no new exclude; label `c262-g127` | 9 |
| PC-128 | G-128: Bypass effective-ignore check only in Render writer | `AgentPinImportTests.V31_GitAdmission` | Untracked nonignored managed import refused; label `c262-g128` | 9 |
| PC-129 | G-129: Throw pin_projection_unavailable inside ComposeForAgentAsync | `AgentPinPolicyTests.V22_PreviewAndParity` | Broken-file Grok preview/Notify finishes intended work without exception; label `c262-g129` | 9 |
| PC-130 | G-130: Treat all missing cwd inspections as mandatory failure | `AgentPinWorkspaceTests.V15_MissingRetirement` | Unused confirmed absence retires with no repair/retry; label `c262-g130` | 9 |
| PC-131 | G-131: Ignore configured/live consumers on absent cwd | `AgentPinWorkspaceTests.V15_MissingRetirement` | Required/racing location remains pending, not retired; label `c262-g131` | 9 |
| PC-132 | G-132: Bypass managed-marker exclusion in append service | `AgentPinImportTests.V11_RecognitionAndRender` | Direct append call performs zero writes to managed floor; label `c262-g132` | 9 |
| PC-133 | G-133: Move Provision before projection reconciliation | `AgentPinLaunchIntegrationTests.V11_StartRenderOrder` | At blocked publication barrier there is no Render import and no runner start; label `c262-g133` | 9 |
| PC-134 | G-134: Require ownership-success for append normalization | `AgentPinPolicyTests.V22_CrashIntent` | Post-append/pre-success valid-intent sweep makes zero process calls; label `c262-g134` | 9 |
| PC-135 | G-135: Require ownership-success for managed-floor normalization | `AgentPinPolicyTests.V22_CrashIntent` | Post-Render/pre-success valid-intent sweep makes zero process calls; label `c262-g135` | 9 |
| PC-136 | G-136: Require ownership-success for removal normalization | `AgentPinPolicyTests.V22_CrashIntent` | Post-remove/pre-success valid-intent sweep makes zero process calls; label `c262-g136` | 9 |
| PC-137 | G-137: Drop protocol from policy implicit selection | `AgentPinPolicyTests.V22_PreviewAndParity` | Launch/preview/policy static stamps equal; label `c262-g137` | 9 |
| PC-138 | G-138: Gate policy protocol on nonempty active pins | `AgentPinPolicyTests.V22_PreviewAndParity` | Never-used/revoked stamp parity and second-sweep no-relaunch hold; label `c262-g138` | 9 |
| PC-139 | G-139: Use OrdinalIgnoreCase for Linux canonical location key | `AgentPinWorkspaceStoreTests.V32_PosixPaths` | Case-distinct directories retain separate ownership/bytes; label `c262-g139` | 12 |
| PC-140 | G-140: Lock raw supplied Windows string rather than native canonical identity | `AgentPinWorkspaceWindowsTests.V32_NativeAliases` | Aliased contender cannot enter publication while first lease held; label `c262-g140` | 12 |
| PC-141 | G-141: Ignore RunnerStoreId when matching projection receipt | `AgentPinLocationTests.V32_StoreRebind` | Replacement store remains unavailable until its own verified receipt; label `c262-g141` | 9 |
| PC-142 | G-142: Mark legacy local Windows-looking Pending row Ready without resolution | `AgentPinLocationTests.V33_LegacyUpgrade` | Unresolved legacy row stays repairable and no disk I/O occurs; label `c262-g142` | 9 |
| PC-143 | G-143: Omit only OperationId equality from the DB Ready completion predicate | `AgentPinLocationTests.V33_ReadyCompareSet` | Mismatched operation cannot change status to Ready; label `c262-g143` | 9 |
| PC-144 | G-144: Ignore desired revision in DB completion predicate | `AgentPinLocationTests.V33_ReadyCompareSet` | Delayed old revision leaves current pending; label `c262-g144` | 9 |
| PC-145 | G-145: Ignore full hash in DB completion predicate | `AgentPinLocationTests.V33_ReadyCompareSet` | Wrong full hash leaves pending; label `c262-g145` | 9 |
| PC-146 | G-146: Ignore owner in DB completion predicate | `AgentPinLocationTests.V33_ReadyCompareSet` | Wrong owner receipt affects zero rows; label `c262-g146` | 9 |
| PC-147 | G-147: Ignore location generation in DB completion predicate | `AgentPinLocationTests.V33_ReadyCompareSet` | Wrong location generation receipt affects zero rows; label `c262-g147` | 9 |
| PC-148 | G-148: Ignore runner store in DB completion predicate | `AgentPinLocationTests.V33_ReadyCompareSet` | Wrong runner store receipt affects zero rows; label `c262-g148` | 9 |
| PC-149 | G-149: Bypass host operation-sequence fence | `AgentPinWorkspaceStoreTests.V14_AtomicAndFence` | Late older RPC cannot overwrite newer file; label `c262-g149` | 12 |
| PC-150 | G-150: Delete durable fence after cleanup instead of retaining tombstone | `AgentPinWorkspaceStoreTests.V14_AtomicAndFence` | Restarted host refuses delayed pre-cleanup publish; label `c262-g150` | 12 |
| PC-151 | G-151: Return saved receipt without checking current disk | `AgentPinTransportTests.V34_ReplayCuts` | Changed/missing post-restart target never produces trusted Ready; label `c262-g151` | 9 |
| PC-152 | G-152: Keep existing HasConfiguredConsumer OR new flag | `AgentPinWorkspaceTests.V15_ConsumersAndCleanup` | Released former configured location retires after last live consumer; label `c262-g152` | 9 |
| PC-153 | G-153: Map unavailable/denied inspection to not-found | `AgentPinWorkspaceTests.V15_MissingRetirement` | Unknown inspection remains unresolved; label `c262-g153` | 9 |
| PC-154 | G-154: Authorize cleanup from legacy stored path alone | `AgentPinWorkspaceStoreTests.V15_HostCleanup` | Unowned/legacy-null target and siblings remain; label `c262-g154` | 12 |
| PC-155 | G-155: Overwrite import expected hash with projection file hash | `AgentPinImportTests.V10_ByteRoundTrip` | Exact import removal restores original bytes after repeated file updates; label `c262-g155` | 9 |
| PC-156 | G-156: Call workspace publish before capture transaction commits | `AgentPinnedInstructionServiceTests.V01_RestartMigration` | I/O observer sees no active DB transaction; label `c262-g156` | 9 |
| PC-157 | G-157: Honor arbitrary request target outside allowed owner leaf | `AgentPinWorkspaceStoreTests.V34_ProtocolAdmission` | Escape request rejected before I/O; label `c262-g157` | 12 |
| PC-158 | G-158: Treat absent pin capability as supported | `AgentPinTransportTests.V34_Unsupported` | Old runner refuses pin-bearing request before sending publish; label `c262-g158` | 9 |
| PC-159 | G-159: Drop operation identity when serializing HTTP request | `AgentPinTransportTests.V34_Transports` | HTTP receipt/file identity equals original durable operation; label `c262-g159` | 9 |
| PC-160 | G-160: Drop location generation when serializing phone-home frame | `AgentPinTransportTests.V34_Transports` | Phone-home receipt/file identity equals original durable location; label `c262-g160` | 9 |
| PC-161 | G-161: Skip append writer final Git recheck | `AgentPinImportTests.V31_FinalStageRace` | Staged-at-barrier unmarked CLAUDE unchanged; label `c262-g161` | 9 |
| PC-162 | G-162: Skip Render writer final Git recheck | `AgentPinImportTests.V31_FinalStageRace` | Staged-at-barrier managed CLAUDE unchanged; label `c262-g162` | 9 |
| PC-163 | G-163: Skip final native location comparison | `AgentPinLaunchIntegrationTests.V35_FinalRecheck` | Changed final cwd never starts with old evidence; label `c262-g163` | 9 |
| PC-164 | G-164: Skip final runner-store comparison | `AgentPinLaunchIntegrationTests.V35_FinalRecheck` | Rebound store never starts with old evidence; label `c262-g164` | 9 |
| PC-165 | G-165: Skip final discovery scope comparison | `AgentPinLaunchIntegrationTests.V35_FinalRecheck` | New sharing conflict never starts with old evidence; label `c262-g165` | 9 |
| PC-166 | G-166: Omit notification-intent save before queue call | `AgentPinRefreshTests.V18_HandoffRecovery` | Restart at pre-enqueue cut recovers same obligation to full prompt; label `c262-g166` | 9 |
| PC-167 | G-167: Flush queue before committing coverage link | `AgentPinRefreshTests.V18_AlreadyEligible` | No terminal input while coverage link uncommitted; label `c262-g167` | 9 |
| PC-168 | G-168: Require AlwaysOn when scanning pin System rows | `AgentPinRefreshTests.V18_ManualSweep` | Manual idle owner eventually has complete UserPrompt; label `c262-g168` | 9 |
| PC-169 | G-169: Concatenate full identities beyond 80 chars into PinRefreshKey | `AgentPinRefreshTests.V18_HandoffRecovery` | Key length <= 80 is asserted before enqueue and durable recovery succeeds; label `c262-g169` | 9 |
| PC-170 | G-170: Use identity/header match without completeness check | `AgentPinRefreshTests.V21_EvidenceLadder` | Clipped prefix/spill-pointer-only leaves receipt unchanged; label `c262-g170` | 9 |
| PC-171 | G-171: Remove generation check from receipt match | `AgentPinRefreshTests.V21_GenerationAndMonotonic` | Prior-generation prompt never advances current receipt; label `c262-g171` | 9 |
| PC-172 | G-172: Remove attempt floor checks from transcript query | `AgentPinRefreshTests.V21_GenerationAndMonotonic` | Old matching prompt never advances receipt; label `c262-g172` | 9 |
| PC-173 | G-173: Assign late old revision over current receipt | `AgentPinRefreshTests.V21_GenerationAndMonotonic` | Newer receipt remains unchanged; label `c262-g173` | 9 |
| PC-174 | G-174: Update PinLaunch fields when note enqueued or received | `AgentPinRefreshTests.V21_EvidenceLadder` | Launch revision/hash/path remain original; label `c262-g174` | 9 |
| PC-175 | G-175: Keep old pending pin note eligible after session replacement | `AgentPinRefreshTests.V19_OwnershipAndPaths` | Old pending row canceled; only new owner receives new obligation; label `c262-g175` | 9 |
| PC-176 | G-176: Always generate API recovery instead of file note | `AgentPinRefreshTests.V16_ApiDoesNotRepair` | Healthy file path produces exact file-read note, not degraded API note; label `c262-g176` | 9 |
| PC-177 | G-177: Create separate pin row alongside rules refresh | `AgentPinRecoveryTests.V24_GrokCombined` | Exactly one recovery message carries both coverage obligations; label `c262-g177` | 9 |
| PC-178 | G-178: Rewrite active rules file on pin change | `AgentPinRecoveryTests.V24_GrokCombined` | Historical runner rules bytes unchanged after revoke/two compacts; label `c262-g178` | 9 |
| PC-179 | G-179: Publish activity unconditionally on request replay | `AgentPinInternalTurnTests.V25_RedactedActivity` | Replay/no-op adds zero semantic activity; label `c262-g179` | 9 |
| PC-180 | G-180: Replace no-op production registration during S2-core | `AgentPinnedInstructionCompositionTests.V32_DormantRegistration` | Pre-activation Program still has no-op and zero pin file I/O; label `c262-g180` | 9 |
| PC-181 | G-181: Leave NoOpAgentPinnedInstructionReconciler registered at activation | `AgentPinLaunchIntegrationTests.V35_Activation` | Production API capture reaches real file and complete UserPrompt; label `c262-g181` | 9 |
| PC-182 | G-182: Ignore PinRefreshKey in prompt correlation | `AgentPinRefreshTests.V21_EvidenceLadder` | Wrong-key full prompt leaves receipt unchanged; label `c262-g182` | 9 |
| PC-183 | G-183: Ignore requested revision in prompt correlation | `AgentPinRefreshTests.V21_EvidenceLadder` | Wrong-revision full prompt leaves receipt unchanged; label `c262-g183` | 9 |
| PC-184 | G-184: Ignore requested absolute path in prompt correlation | `AgentPinRefreshTests.V21_EvidenceLadder` | Wrong-path full prompt leaves receipt unchanged; label `c262-g184` | 9 |
| PC-185 | G-185: Hash active rows in DB enumeration order | `AgentPinnedInstructionCompositionTests.V05_LiteralOrderEmptyHash` | Shuffled rows yield independently expected identical full hash; label `c262-g185` | 9 |
| PC-186 | G-186: Treat empty used snapshot as never-used and omit it | `AgentPinnedInstructionCompositionTests.V05_LiteralOrderEmptyHash` | Last revoke renders stamped explicit empty set; label `c262-g186` | 9 |
| PC-187 | G-187: Reconcile an empty projection for every never-used agent | `AgentPinWorkspaceTests.V06_FirstUseMatrix` | No pin subtree before first use; label `c262-g187` | 9 |
| PC-188 | G-188: Use stored Agent.Kind instead of effective profile kind | `AgentPinnedInstructionCompositionTests.V04_SupportedSnapshot` | Mismatched profile kind gets correct supported protocol and provider payload; label `c262-g188` | 9 |
| PC-189 | G-189: Delete old owned target despite retained user import | `AgentPinWorkspaceTests.V15_ConsumersAndCleanup` | User import target remains owned empty tombstone; label `c262-g189` | 9 |
| PC-190 | G-190: Skip host intended-byte/hash equality validation | `AgentPinWorkspaceStoreTests.V34_ProtocolAdmission` | Mismatched digest request refused before I/O; label `c262-g190` | 12 |
| PC-191 | G-191: Use initial component inspection after gated symlink swap | `AgentPinWorkspaceStoreTests.V13_PathSafety` | Post-inspection symlink swap leaves referent unchanged; label `c262-g191` | 12 |
| PC-192 | G-192: Update latest body but omit older intent coverage links | `AgentPinRefreshTests.V20_BusyCoalescing` | All never-attempted superseded revisions link to newest delivered complete set; label `c262-g192` | 9 |
| PC-193 | G-193: Ignore working state for pin-bearing policy refresh | `AgentPinPolicyTests.V22_PreviewAndParity` | Working pin-bearing session has zero process attempts; label `c262-g193` | 9 |
| PC-194 | G-194: Ignore cooldown for pin-bearing policy refresh | `AgentPinPolicyTests.V22_PreviewAndParity` | Within-cooldown pin-bearing session has zero process attempts; label `c262-g194` | 9 |
| PC-195 | G-195: Ignore model-budget hold for pin-bearing policy refresh | `AgentPinPolicyTests.V22_PreviewAndParity` | Held-budget pin-bearing session has zero process attempts; label `c262-g195` | 9 |
| PC-196 | G-196: Ignore credential failure for pin-bearing refresh launch | `AgentPinPolicyTests.V22_PreviewAndParity` | Unauthenticated pin-bearing launch has zero runner attempts; label `c262-g196` | 9 |
| PC-197 | G-197: Permit pin-bearing legacy inline Grok resume | `AgentPinPolicyTests.V22_PreviewAndParity` | Legacy Grok pin-bearing resume refuses before kill/start; label `c262-g197` | 9 |
| PC-198 | G-198: Skip control-character rejection in NormalizeOptional | `AgentPinnedInstructionServiceTests.V02_AllValidationBoundaries` | NUL/ESC/newline in each provenance field receives typed validation refusal; label `c262-g198` | 9 |
| PC-199 | G-199: Remove the namespace/key null-pair condition | `AgentPinnedInstructionServiceTests.V02_AllValidationBoundaries` | Either missing half rejects without revision/activity; label `c262-g199` | 9 |
| PC-200 | G-200: Skip owner versus typed target owner validation | `AgentPinWorkspaceStoreTests.V34_ProtocolAdmission` | Mismatched owner request is refused before native I/O; label `c262-g200` | 12 |
| PC-201 | G-201: Skip supported-path-schema check | `AgentPinWorkspaceStoreTests.V34_ProtocolAdmission` | Unknown schema request is refused before native I/O; label `c262-g201` | 12 |
| PC-202 | G-202: Skip expected runner-store UUID comparison | `AgentPinWorkspaceStoreTests.V34_ProtocolAdmission` | Request for previous store is refused before native I/O; label `c262-g202` | 12 |
| PC-203 | G-203: Map unknown operation kind to Publish | `AgentPinWorkspaceStoreTests.V34_ProtocolAdmission` | Unknown action is refused before native I/O; label `c262-g203` | 12 |
| PC-204 | G-204: Omit hosted notification-worker registration | `AgentPinLaunchIntegrationTests.V35_Activation` | Production capture with no direct test worker call reaches a complete recipient prompt; label `c262-g204` | 9 |
| PC-205 | G-205: Omit startup pending-work reconciliation registration | `AgentPinLaunchIntegrationTests.V35_Activation` | Pending fixture obligation seeded before host start reaches complete recipient prompt without mutation/event; label `c262-g205` | 9 |
| PC-206 | G-206: Omit periodic pin-backstop registration | `AgentPinLaunchIntegrationTests.V35_Activation` | Dropped wakeup after startup is recovered by the bounded real backstop to complete recipient prompt; label `c262-g206` | 9 |
| PC-207 | G-207: Omit pin native-compact recovery subscription | `AgentPinLaunchIntegrationTests.V35_Activation` | Production native compact event reaches complete recipient prompt without direct service call; label `c262-g207` | 9 |

Native companion cycles (same G/PC, additional method-scoped cycle; never combine their filter with the main method):

- PC-20-native: Permit shared native import installation; expect `AgentPinClaudeImportCanaryTests.V29_Isolation` red at **Automatic-only shared request excludes both A/B canaries**. 66 minutes. Require stub nonce/key receipt before evaluating serialized context.
- PC-24-native: Return installed without appending stanza; expect `AgentPinClaudeImportCanaryTests.V28_NativeOnly` red at **Serialized native-only request contains file-only canary**. 66 minutes. Require stub nonce/key receipt before evaluating serialized context.
- PC-55-native: Remove protective pin fence; expect `AgentPinClaudeImportCanaryTests.V28_PayloadLiteral` red at **Serialized request excludes payload-file canary**. 66 minutes. Require stub nonce/key receipt before evaluating serialized context.
- PC-88-native: Only in the first post-revoke native-compact combined recovery, make historical launch pins authoritative over the current empty set; expect `AgentPinBehaviorAcceptanceTests.V30_Grok_Journey` red at **Unused post-revoke key 5 answer equals BASE with independent native read**. 366 minutes. Keep path/revision metadata valid and change only the historical-versus-current instruction precedence, so the intended native challenge assertion is reachable.

### Out of scope

- Foreign KB discovery/backfill, incident-agent capture and live rollout: owning deployment/AgentId/source bytes are not established here. Synthetic explicit capture is the acceptance input. Keep CARD-0262 open pending its completion evidence.
- ACL secrecy against another same-user process, unregistered external launches, trusted-host API redesign and truth of the operator Dedicated assertion. Automatic discovery/collision/recipient isolation is covered; stronger guarantees were not designed.
- New native import surfaces or Raw/OpenCode behavioral compliance, pin inheritance to pool delegates and CARD-0250 behavior changes. Their exclusion/compatibility is tested.
- Exhaustive future model obedience and retroactive undo of completed actions. V-30 proves only its six recorded challenges per provider. Stub/provider ACK cannot replace it.
- An external editor changing bytes after the final comparison remains the declared compare/write TOCTOU limit. Deterministic edits before comparison, native link substitution and stale RPC races remain in scope.
- Full unrelated assemblies/nightly and unlisted live-provider probes. The ER exceptions above identify excluded large-class families; no required V/R boundary is excluded.

### Checkpoints

**Budgeted S3a.1 split (Code f065f946, 2026-10-04).** The operator's 30–60 minute
brief permits the first part of an oversized slice. S3a follows S2-core and does not
depend on PC-180. This task implements only a dormant, read-only host inspection
contract and native POSIX path validation. CP-28 is its closed list: three new
disk-backed inspection methods plus the two existing adjacent phone-home admission
methods. CP-3–6 remain owed for full S3a; no original checkpoint floor is reduced.
The inspection methods cover only the read portion of V-13/V-32/V-34 and R-5/R-6
(no arbitrary target, no filesystem mutation). They do not complete those IDs.
Publication, native Git excludes, durable intent/fences, custody/cleanup, application
location/receipt CAS and legacy upgrade, HTTP/phone-home wiring, Windows and mixed-OS
qualification remain S3a work. Later full-host methods must incorporate these cases
while retaining the planned publication/concurrency cases and updating their roster.
An observed file hash grants no ownership, Ready state, launch or cleanup authority.
Linux x64 opens through no-follow directory descriptors and accepts only a regular
file (including nonblocking FIFO refusal), with a 1 MiB inspection bound. Windows
and unqualified architectures return explicit unsupported in this part. Inspection
rechecks path components before and after reading; it does not claim
filesystem ACL isolation against concurrent same-user path replacement. No capability
or production registration is added. Whole Unit and all other Final obligations are
deferred by this task's explicit budget instruction, not passed.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-28 | S3a.1 | `tests/Antiphon.SessionRunner.Tests -> bin-c262-inspect/` | linux-native-inspection | `/*/*/(AgentPinWorkspaceStoreTests*)\|(PhoneHomeCommandDispatcherTests*)/(V*)\|(Unsupported_operation_or_launch_never_enters_runtime*)\|(Workspace_ops_are_admitted_only_under_allowed_cwd*)` | V-13, V-32, V-34, R-5, R-6 read-only portions | all 5 roster executions; 0 failed/skipped | 5 | 10 | true | `n/a` |
| CP-29 | S3a.2 | `tests/Antiphon.SessionRunner.Tests -> bin-c262-publish/` | linux-native-publication | `/*/*/(AgentPinPublicationTests*)\|(AgentPinWorkspaceStoreTests*)\|(PhoneHomeCommandDispatcherTests*)/(V*)\|(Unsupported_operation_or_launch_never_enters_runtime*)\|(Workspace_ops_are_admitted_only_under_allowed_cwd*)` | V-13–15, V-32, V-34, R-5, R-6 host portions | all 11 roster executions; 0 failed/skipped | 11 | 10 | true | `n/a` |
| CP-1 | S2-core | `tests/Antiphon.Tests -> bin-c262-core/` | portable-snapshot-store | `/*/*/(AgentPinnedInstructionCompositionTests*)\|(AgentPinnedInstructionServiceTests*)\|(AgentPinnedInstructionEndpointTests*)/*` | V-1–5, V-32, R-7, R-11 | all 32 roster executions; 0 failed/skipped | 32 | 10 | true | `n/a` |
| CP-2 | S2-core | `CP-1` | portable-bundle-regression | `/*/*/InstructionBundleTests/*` | V-4, V-5, R-7 | all 71 roster executions; 0 failed/skipped | 71 | 4 | true | `n/a` |
| CP-3 | S3a | `tests/Antiphon.Tests -> bin-c262-projection/` | portable-projection | `/*/*/(AgentPinWorkspaceTests*)\|(AgentPinLocationTests*)\|(AgentPinTransportTests*)/*` | V-6, V-7, V-13–17, V-32–34, R-1, R-2, R-5, R-6 | all 16 roster executions; 0 failed/skipped | 16 | 12 | true | `n/a` |
| CP-4 | S3a | `tests/Antiphon.SessionRunner.Tests -> bin-c262-posix/` | linux-native-host | `/*/*/(AgentPinWorkspaceStoreTests*)\|(PhoneHomeCommandDispatcherTests*)/(V*)\|(Unsupported_operation_or_launch_never_enters_runtime*)\|(Workspace_ops_are_admitted_only_under_allowed_cwd*)` | V-13–15, V-32, V-34, R-5, R-6 | all 7 roster executions; 0 failed/skipped | 7 | 10 | true | `n/a` |
| CP-5 | S3a | `tests/Antiphon.SessionRunner.Tests -> bin-c262-ntfs/` | windows-native-host | `/*/*/AgentPinWorkspaceWindowsTests/*` | V-13, V-14, V-32, V-34, R-5 | all 4 roster executions; 0 failed/skipped | 4 | 10 | true | `n/a` |
| CP-6 | S3a | `tests/Antiphon.Tests -> bin-c262-path/` | windows-path-mixed-os | `/*/*/(AgentPinPathTests*)\|(AgentPinMixedOsTransportTests*)/*` | V-1, V-2, V-5, V-34, R-2 | all 5 roster executions; 0 failed/skipped | 5 | 16 | true | `n/a` |
| CP-7 | S3b | `tests/Antiphon.Tests -> bin-c262-import/` | portable-imports | `/*/*/(AgentPinImportTests*)\|(AgentPinWorkspaceTests*)\|(AgentWorkspaceProvisionerTests*)/*` | V-6–17, V-31, R-1–6 | all 41 roster executions; 0 failed/skipped | 41 | 12 | true | `n/a` |
| CP-8 | S3b | `tests/Antiphon.Tests -> bin-c262-import-ntfs/` | windows-native-import | `/*/*/AgentPinImportWindowsTests/*` | V-12, V-31, R-3, R-4 | all 2 roster executions; 0 failed/skipped | 2 | 10 | true | `n/a` |
| CP-9 | S4 | `tests/Antiphon.Tests -> bin-c262-queue/` | portable-pin-queue | `/*/*/(AgentPinRefreshTests*)\|(AgentPinInternalTurnTests*)/*` | V-16, V-18–21, V-25, R-2, R-8, R-9, R-11 | all 13 roster executions; 0 failed/skipped | 13 | 12 | true | `n/a` |
| CP-10 | S4 | `CP-9` | portable-queue-receipt | `/*/*/SessionMessageQueueDeliveryVerificationTests/(Late_confirm_marks_the_message_sent_with_zero_writes_to_the_terminal*)\|(Queue_enqueue_does_not_confirm_delivery*)\|(A_clipped_prefix_parks_as_truncated_not_sent*)\|(A_complete_long_body_still_marks_sent*)` | V-21, R-9 | all 4 roster executions; 0 failed/skipped | 4 | 3 | true | `n/a` |
| CP-11 | S4 | `CP-9` | portable-queue-routing | `/*/*/(SessionMessageQueueServiceTests*)\|(ChannelMachineTurnTextTests*)\|(ChannelMachineTurnMatchTests*)/*` | V-20, V-25, R-8, R-11 | all 54 roster executions; 0 failed/skipped | 54 | 8 | true | `n/a` |
| CP-12 | S4 | `CP-9` | portable-report-boot | `/*/*/(TaskReportHousekeepingTests*)\|(BootReplyWatchTests*)/*` | V-25, R-11 | all 38 roster executions; 0 failed/skipped | 38 | 5 | true | `n/a` |
| CP-13 | S4 | `CP-9` | portable-task-report | `/*/*/AgentTaskReplyIntegrationTests/(Grok_rules_refresh_with_task_marker_neither_settles_nor_nudges*)\|(a_marked_turn_settles_the_task_and_stores_the_report_verbatim*)` | V-25, R-11 | all 3 roster executions; 0 failed/skipped | 3 | 4 | true | `n/a` |
| CP-14 | S5 | `tests/Antiphon.Tests -> bin-c262-policy/` | portable-policy | `/*/*/(AgentPinPolicyTests*)\|(PolicyRefreshServiceTests*)\|(InstructionFileStampTests*)/*` | V-22, R-9 | all 43 roster executions; 0 failed/skipped | 43 | 10 | true | `n/a` |
| CP-15 | S5 | `CP-14` | portable-recovery | `/*/*/(AgentPinRecoveryTests*)\|(CompactionRecoveryTests*)\|(GrokRulesCompactionRecoveryTests*)/*` | V-23, V-24, R-8–10 | all 21 roster executions; 0 failed/skipped | 21 | 8 | true | `n/a` |
| CP-16 | S5 | `tests/Antiphon.Tests -> bin-c262-grok-ntfs/` | windows-grok-regression | `/*/*/(GrokRulesQueueBarrierTests*)\|(GrokRulesTransportCompatibilityTests*)/*` | V-24, R-10 | all 52 roster executions; 0 failed/skipped | 52 | 8 | true | `n/a` |
| CP-17 | S2-launch | `tests/Antiphon.Tests -> bin-c262-launch/` | portable-pin-activation | `/*/*/(AgentPinLaunchIntegrationTests*)\|(AgentPinnedInstructionCompositionTests*)\|(AgentPinLocationTests*)/*` | V-4, V-5, V-11, V-13, V-32, V-33, V-35, R-1, R-2, R-7 | all 15 roster executions; 0 failed/skipped | 15 | 12 | true | `n/a` |
| CP-18 | S2-launch | `CP-17` | portable-launch-regression | `/*/*/(AgentSystemPromptLaunchTests*)\|(NamedCodexAgentLaunchTests*)\|(GrokRulesCompositionTests*)\|(DelegateBundleLaunchTests*)/*` | V-4, V-5, R-7 | all 67 roster executions; 0 failed/skipped | 67 | 8 | true | `n/a` |
| CP-19 | S2-launch | `CP-17` | portable-launch-ownership | `/*/*/(PhoneHomeStandingLaunchTests*)\|(AgentSessionLaunchQueueOwnershipTests*)/*` | V-4, V-19, V-35, R-2, R-8 | all 22 roster executions; 0 failed/skipped | 22 | 6 | true | `n/a` |
| CP-20 | S2-launch | `CP-17` | portable-activated-recovery | `/*/*/(AgentPinRefreshTests*)\|(AgentPinPolicyTests*)\|(AgentPinRecoveryTests*)/*` | V-16, V-18–24, R-8–10 | all 18 roster executions; 0 failed/skipped | 18 | 10 | true | `n/a` |
| CP-21 | S6 | `n/a` | portable-client | `pwsh -NoProfile -File scripts/test-client.ps1 AgentPinnedInstructions.test AgentBundleAttachments.test useSignalRInvalidation.test` | V-26, R-12 | 24 Vitest results in 3 files; 0 failed/skipped | n/a | 4 | true | `n/a` |
| CP-22 | S7 | `n/a` | windows-client-build | `npm --prefix client run build` | V-27, R-12 | exit 0; fresh client/dist on CP-23 host | n/a | 4 | true | `n/a` |
| CP-23 | S7 | `tests/Antiphon.E2E -> bin-c262-e2e/` | windows-isolated-journey | `/*/*/AgentPinnedInstructionsE2ETests/*` | V-27, R-1–12 | all 2 roster executions; 0 failed/skipped | 2 | 15 | true | `n/a` |
| CP-24 | S7 | `tests/Antiphon.Tests -> bin-c262-cli/` | windows-claude-import | `/*/*/AgentPinClaudeImportCanaryTests/*` | V-28, V-29, R-3, R-7 | all 3 roster executions; 0 failed/skipped | 3 | 20 | true | `ANTIPHON_REAL_CLI_STUB_TESTS=1` |
| CP-25 | S7 | `tests/Antiphon.Tests -> bin-c262-claude-live/` | windows-claude-behavior | `/*/*/AgentPinBehaviorAcceptanceTests/V30_Claude_Journey` | V-30, R-10 | all 1 roster executions; 0 failed/skipped | 1 | 30 | true | `ANTIPHON_PIN_BEHAVIOR_TESTS=1;ANTIPHON_HEADED_TESTS=1` |
| CP-26 | S7 | `tests/Antiphon.Tests -> bin-c262-codex-live/` | windows-codex-behavior | `/*/*/AgentPinBehaviorAcceptanceTests/V30_Codex_Journey` | V-30, R-10 | all 1 roster executions; 0 failed/skipped | 1 | 30 | true | `ANTIPHON_PIN_BEHAVIOR_TESTS=1;ANTIPHON_CODEX_HEADED_TESTS=1` |
| CP-27 | S7 | `tests/Antiphon.Tests -> bin-c262-grok-live/` | windows-grok-behavior | `/*/*/AgentPinBehaviorAcceptanceTests/V30_Grok_Journey` | V-30, R-10 | all 1 roster executions; 0 failed/skipped | 1 | 120 | true | `ANTIPHON_PIN_BEHAVIOR_TESTS=1;ANTIPHON_HEADED_TESTS=1;ANTIPHON_GROK_RULES_LIVE_TESTS=1;ANTIPHON_GROK_RULES_ENDURANCE_TESTS=1` |

**Execution order and lanes.** The final manifest has 27 rows, replacing the 23-row proposal. Negative/refusal/race scenarios are authored and exercised before success scenarios in each new method (the intended assertion must be reachable, not a fixture failure); CP order follows S2-core → S3a → S3b → S4 → S5 → S2-launch → S6 → S7. An ordinary row is green at its committed slice; post-land PC red cycles remain separate. Slice activation is dormant until S2-launch. Final cumulative source must rerun any earlier row whose tested source/behavior changed; preserve earlier slice SHA evidence and never relabel it as a later SHA.

Portable rows use the current eligible default lane and cloned PostgreSQL. CP-4 requires Linux native disk. CP-5/6/8/16 require Windows; CP-22–27 require Windows with modern ConPTY/provider prerequisites (CP-22 creates the built client consumed by CP-23 in that checkout). Do not send the whole project to Windows merely because a subset needs it. The caller reads runner defaults/session runners/pipeline/host limits at dispatch; no fixed fleet host is prescribed. Same-row build reuse requires the same committed source, After, worktree, OS and verified output binding. Each provider row intentionally has its own build so distinct commissioned hosts cannot reuse another host's binaries.

Native prerequisites: NTFS junction/file-symlink creation and disposable Git worktrees for CP-5/8; a Linux-container engine reachable only through the fixture-owned container IDs, working Windows-to-container/container-to-Windows loopback routing and both managed server/runner payloads for CP-6; installed Playwright browser plus FakeClaude apphost for CP-23; installed Claude plus isolated CLAUDE_CONFIG_DIR, onboarding/trust and stub synthetic credentials for CP-24; supported installed CLI/version, independent native read transcript, isolated homes and already authorized live authentication for CP-25–27. Codex uses HeadedCodexGate.TestHome with explicitly seeded login, never operator auth copying. Grok uses isolated GROK_HOME and ANTIPHON_GROK_TEST_AUTH_PATH/native auth path as in its inspected fixture, plus calibrated native compaction workload. TestDesign did not probe/claim these prerequisites. Refusal/skip remains pending acceptance, never zero-failure success.

Use the checkpoint tool once per committed slice and lane selection. Bootstrap its CLI under one slot, then release that slot before invoking its built DLL; the executor leases every row itself. This explicit auxiliary tool build is included in setup cost and is not an unlisted application build. In PowerShell:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c262-checkpoint-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c262-tool/ --nologo
if ($LASTEXITCODE -ne 0) { throw "Checkpoint tool bootstrap failed" }
$c262SourceSha = (git rev-parse HEAD).Trim()
dotnet tools/Antiphon.Checkpoints/bin-c262-tool/net9.0/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-04-card-0262-pin-propagation-continuation-plan.md --expected-source-sha $c262SourceSha --rows CP-1,CP-2 --max-wait 60s
```

Do not wrap the built tool or run-checkpoint.ps1 in a second lease. Custom commands in CP-21/22 inherit the executor row lease. Follow every exit 75 with the same built tool's `wait --run` invocation using the emitted run ID and `--max-wait 60s`, until terminal; never finish the task with an executor running. Exit 4/refusal is not permission for an unleased retry.

CP-27 runs by itself using the supported explicit `--row-timeout 130m --total-timeout 150m` options; its single native scenario retains the inspected 120-minute workload ceiling and a test Timeout of 7,260,000 ms for teardown. CP-25/26 use `--row-timeout 40m --total-timeout 50m` individually. CP-24 uses `--row-timeout 35m --total-timeout 45m`. If exporting YAML with the tool's import verb, set those rows' `timeoutMinutes` to 130/40/40/35 respectively and preserve the same source/roster; do not rely on a default 45-minute warning/ceiling for Grok. This is a declared bounded workload, not permission to extend timeouts after failure.

Preserve unedited CHECKPOINT lines, actual SHA, counts, row environment, provider version/native transcript/read references and receipt source/build provenance in the stored report. Validate receipts against the exact tested SHA; Code/Review run `scripts/check-evidence-diff.ps1` over the entire task base..HEAD. Generated TRX/JSON/logs/checkpoint outputs stay ignored. No source edits under an in-flight run. Remove only task-owned forward-slash `bin-c262-*/` outputs using the checkpoint cleanup contract; never daemon bin/ or obj/. Add authoring time to dispatch budgets; setup errors require repair and the same row rerun, not loosened assertions.

### Cost

All figures are **estimated**, not measured. Ordinary Code V/R floor = **401 minutes**, the exact sum of CP-1–27 EstimatedMinutes. It includes 16 isolated row builds at an estimated 2 minutes each (**32 minutes build**) and **369 minutes** test/command work. Lane provisioning/manifest/source-receipt setup adds **20 minutes**, excluding implementation/test authoring.

- S2-core: CP-1, CP-2 = **14 minutes**, exact filters above.
- S3a: CP-3, CP-4, CP-5, CP-6 = **48 minutes**, exact filters above.
- S3b: CP-7, CP-8 = **22 minutes**, exact filters above.
- S4: CP-9, CP-10, CP-11, CP-12, CP-13 = **32 minutes**, exact filters above.
- S5: CP-14, CP-15, CP-16 = **26 minutes**, exact filters above.
- S2-launch: CP-17, CP-18, CP-19, CP-20 = **36 minutes**, exact filters above.
- S6: CP-21 = **4 minutes**, exact filters above.
- S7: CP-22, CP-23, CP-24, CP-25, CP-26, CP-27 = **219 minutes**, exact filters above.

Separate post-land Mutation PC floor = **2457 minutes** for 207 main cycles plus 4 native companion cycles. I: 188 × 9 = 1692 minutes. N: 16 × 12 = 192 minutes. U: 3 × 3 = 9 minutes. Native companions: PC-20-native 66 minutes, PC-24-native 66 minutes, PC-55-native 66 minutes, PC-88-native 366 minutes. All names/filters and per-control minutes are in Positive controls; this cost includes one baseline build/run, one compiling-mutant build/red run, and one restored fresh build/green run per cycle. Add **20 minutes** SourceLanding discovery/restoration/report setup.

Total verification floor = ordinary setup 20 + ordinary V/R 401 (build 32 already included) + Mutation setup 20 + every PC baseline/red/restore/green 2457 = **2898 minutes**. This is cumulative across separately commissioned slices/OS/provider lanes, not one foreground run. It intentionally replaces September's 90–180-minute grouped-PC estimate; independent guard sensitivity and real provider cycles cannot fit that number. Authoring, slot waits, failed prerequisite setup, inherited-red diagnosis and failure-driven reruns are additional and must be reported.

Savings: 9 legal same-slice build reuses avoid 9 × 2 = **18 minutes** versus rebuilding every TUnit row. No mutation batching savings are assumed (**0 minutes**) because most controls share production files/methods, and native providers must remain isolated/serial. No repeated full-assembly runs are included; scope is the exact table, not a claim of an unmeasured full-suite saving.

**Handoff audit:** bodies read as listed; guards=207, mapped=207, missing=0, duplicate PC maps=0. All main/companion PC recipes name a concrete compiling defect, exact executable test method/title and decisive assertion; tests are for Code to implement, no executed-PC claim. The inherited Sep-PC-1–62 catalog has a mapping for every arm, including native companion oracles. The 27-row manifest covers V-1–35 (V-26 client) and R-1–12; all numeric floors count test executions rather than scenario/assertion counts. No product decision or unverifiable architecture seam remains in this design; missing OS/auth fixtures are recorded setup/qualification work.

--- next stage ---
next: code
handoff: Implement S2-core first using the appended Verification design and CP-1/2, preserving landed S1 and dormant production wiring. Continue the ordered slices on their native lanes; keep CARD-0262 open. Ordinary V/R precedes Review/land; all mapped PCs belong to separately commissioned post-land Mutation.
artifact: docs/superpowers/plans/2026-10-04-card-0262-pin-propagation-continuation-plan.md
