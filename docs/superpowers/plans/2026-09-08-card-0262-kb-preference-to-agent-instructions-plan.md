# CARD-0262 - Durable per-agent instructions, antiphon.md, and queued rereads

- Date: 2026-09-08.
- Status: D3 amendment complete; operator released the build hold. Ready for separate TestDesign after this amendment lands.
- Code baseline: `b97003db` in task `00afd0a8`.
- Amendment baseline: plan commit `4c230f5a`; explicit operator override in task `d4769008` on 2026-09-08 requires a per-agent file even in shared/Unverified workspaces and releases the build hold.
- Supersedes the design in [the 2026-08-31 plan](2026-08-31-card-0262-kb-preference-to-agent-instructions-plan.md), especially its managed-CLAUDE floor and next-launch-only decisions.
- Evidence: full live CARD-0262 description, including both operator refinements dated 2026-08-31; full successful investigation report `2ad377d7` dated 2026-09-07; current sources named below.
- This artifact changes no application behavior. No build, model probe, deployment, pin capture, or card mutation was performed for this re-plan.

## Outcome and dispatch sequence

Introduce a per-agent standing-instruction store as the source of truth. Compose its current
snapshot into each supported named agent's launch instructions, project it into a small,
Antiphon-owned `cwd\.antiphon\pins\<agent-id>\antiphon.md` in every workspace used by that named
agent after first pin use, point only that agent at its exact file, add a Claude-specific import
where its discovery scope is exclusive, and queue a reread when the active set changes or context
is compacted. Capture accepts any explicitly designated standing instruction, including one linked
to a foreign KB row; it is not PDF-specific.

The operator's 2026-09-08 decision supersedes the card's earlier "do not dispatch a build until
asked" language: that hold is released. This dispatch amends only this plan; the caller will
update the card's own record separately. Land this amendment, then dispatch **TestDesign**, then
Code using its verification design. Settle `next: test-design`; no further hold-release decision
is needed. This planning dispatch itself performs no implementation or card-description edit.

## Ground truth

| Assumption or requirement | Evidence at the baseline | Design consequence |
|---|---|---|
| A KB row already feeds instructions | Investigation `2ad377d7` confirms no Antiphon pin entity, endpoint, or KB-to-instruction integration. The original KB is external. | Add a capture contract and store; do not invent a foreign database reader. No historic row becomes active merely through deployment. |
| CARD-0059's generated file fixes the gap | `AgentWorkspaceProvisioner` renders a generic floor at Create/Start, and returns `LeftAlone` for ordinary unmarked files. | Keep the existing floor. Pin content goes in a different owned file and launch composition. |
| CARD-0250 is the missing mechanism | Investigation confirms its attachment follow-up implementation is landed. `ChannelReplyDispatcher.DispatchMachineTurnFollowUpAsync` exists. | Do not reopen attachment routing. Exclude our internal reread turns from its existing machine-turn follow-up path. |
| The operator still wants pins in the generated floor | The second 2026-08-31 refinement replaces that section with a lazy file, Claude import, and live rereads; task `d4769008` overrides the later dedicated-only restriction with mandatory per-agent file identity. | No pin list in the managed `CLAUDE.md` body, `AGENTS.md`, `SOUL.md`, or `MEMORY.md`. Always create the agent-specific projection after first use; native import eligibility is separate. The earlier optional MEMORY mirror is superseded. |
| Antiphon can freely edit workspace files | `docs/agent-workspaces.md` reserves memory files to the agent; CARD-0059 says never touch an unmarked `CLAUDE.md`. | Document two narrow exceptions: the new whole owned file and a delimited import stanza. Do not relax floor adoption or take ownership of surrounding text. |
| Composition only means adding one launch call | `AgentSessionLaunchComposer` is shared by AgentControlService, CardService's assigned-agent path, and OrchestratorService. `AgentService` and `PolicyRefreshService` recompute stamps separately. | One named-agent pin composition helper must serve all these paths; no pin inheritance into pool delegates or task-role composition. |
| Every provider takes the same append argument | Current composer uses Claude `--append-system-prompt`, Codex developer instructions, and a `GrokRulesPayload`. Raw/OpenCode do not enter that composition branch. | Support the three existing instruction-capable kinds. Show an explicit unsupported-runtime state for other kinds; a file alone is not runtime support. |
| Grok resume accepts new inline rules | CARD-0395's current code uses runner-owned rules files, receipts and queued acknowledgements; legacy inline-rules resume is refused. | Retain that path and its refusal. Do not revert to `--rules <new text>` or claim new stamps prove adoption. |
| Drift only raises a badge | CARD-0334's `PolicyRefreshService` can kill/resume at idle. Its default file list includes `CLAUDE.md`, but not `antiphon.md`. | Separate pin drift from restart-eligible bundle/file drift; pin changes use WhenIdle, not a restart. |
| A compaction note reaches every standing agent | `CompactionRecoveryService` gates the generic note on nonempty SystemPromptAppend, saves its watermark before enqueue, and delegates Grok recovery early to `GrokRulesRefreshService`. | Add durable pin recovery for plain standing agents too. Do not hang pin recovery on the generic watermark or bypass Grok's existing barrier. |
| WhenIdle enqueue is durable evidence that instructions arrived | Queue persists rows and verifies delivery; its enqueue may also deliver inline. Screen-confirmed delivery exists and is weaker than a matching transcript UserPrompt. | Commit pin intent before enqueue; use non-inline enqueue/reconciliation. Report saved, projected, queued, and transcript-confirmed separately. |
| A session token is a universal authorization scheme | `AgentTaskService.AuthenticateAsync` also accepts task and capability principals. `docs/antiphon-api.md` documents a seeded-admin, unauthenticated operator API. | Resolve session ownership explicitly. A headerless trusted-host UI call is not cryptographic proof of operator identity. Do not promise a hostile-agent security boundary. |
| A cwd belongs to one agent | The runtime owner documents multiple agents and a human using the same cwd. Claude instruction files also load from ancestors. The operator's amendment explicitly retains this leak concern. | D3 uses immutable AgentId-specific files and exact per-session pointers. D4 never adds private imports to a shared discovery scope; unique filenames alone would not stop a shared CLAUDE from loading all imports. |
| Gitignored runtime state cannot be a delivery artifact | This repo ignores `.antiphon/`; delegation briefs use `.antiphon/task-<short>-brief.md`, and retained reports use `.antiphon/reports/<full-task-guid>/<hash>.md` (orchestration owner). | Use `.antiphon/pins/<full-agent-guid>/antiphon.md`; ignored files remain explicit read targets, never depend on search/index discovery. |

Claude's current [memory documentation](https://code.claude.com/docs/en/memory) confirms relative
`@` imports, expansion at launch, skipping imports inside code spans/fences, and project-root
CLAUDE reload after compaction. It also describes ancestor discovery. This supports the Claude
enhancement only; it is not evidence of universal imports or immediate reload on arbitrary file
writes. The implementation must still run the installed-CLI canary in S7. This plan relies on
one direct import, not a particular maximum nesting depth.

## Decisions

### D1 - General capture, scoped to one named agent

Support any concise, user-authorized standing instruction. Store the relevant `AgentId` explicitly;
do not infer it from a path, a KB name, or the agent currently busy with a project. Include channel
agents and named non-channel agents, whether AlwaysOn or manually started. A card-backed launch
of that same named agent carries its pins. Ephemeral/pool task delegates carry none; relevant
preferences can be stated in their task brief by their caller.

A project agent or adapter maps a row tagged as a standing instruction to the capture API at the
time it records/tags the row. Removing the tag or deleting that source instruction calls revoke;
editing it uses atomic replacement. A stable source key makes this repeatable. Ordinary KB facts,
retrieved webpages and quoted messages are not promoted. No polling, schema discovery, implicit
historical backfill, or global bundle rewrite is part of v1. An unintegrated external KB therefore
still needs its writer to make the call; describe this honestly in both docs and UI.

### D2 - Database authority, launch snapshot plus owned projection

Pins are data, distinct from reviewed bundles and the operator's `SystemPromptAppend`. The DB
owns active/revoked state. The renderer produces both the launch snapshot and the agent-specific
`antiphon.md` projection from the same immutable snapshot; it never ingests edits from that file.
A pin is a preference or standing instruction within existing authority, not authorization for
new spending, recipients,
secrets, or prohibited operations. Explicit operator contracts retain precedence.

Use a small `## Pinned` heading. Preserve the original text as literal data; do not interpret
Markdown, `@path`, template placeholders, shell syntax, attachment markers, or HTML inside it.
In the file, use a fenced literal block with a delimiter longer than any delimiter run in the
content, so pin text cannot create another Claude import. Put only instruction text and stable
pin IDs in this block; source references and audit history stay in the API/UI.

### D3 - Always create an agent-unique file and supply its exact path

After first pin use, always materialize `cwd\.antiphon\pins\<agent-id>\antiphon.md`, including
shared, ordinary project and Unverified workspaces. `<agent-id>` is the owning named AgentId's
full lowercase 32-hex GUID (`Guid.ToString("N")`), never a display name, slug, short ID, session ID
or caller-supplied path component. A rename preserves the path; a different/recreated agent gets
a different directory even if its name is reused. Never write a fixed `cwd\antiphon.md`, an alias
to the latest writer, a combined set, or a shared index enumerating other agents' pin files.
Creating/starting an agent with no pin history still creates no pin file/import: "always" removes
the workspace eligibility gate after first use, not the lazy first-use contract.

Remove the proposed `pinWorkspaceMode` file gate. There is no Disabled or API-only projection
mode. Shared use, an unknown dedication status, an existing `.antiphon/` ignore rule, or absence
of a Git repository never suppresses file creation. Create the necessary `.antiphon\pins\<id>`
parents inside an existing validated cwd; do not create a missing cwd or change the session cwd.
Use the actual execution host/cwd, including a named-agent card launch's worktree, not merely
the agent's default path on the server. Reconcile the configured workspace and all still-used
named-session projection locations from one desired snapshot; delegates receive no projection.

Every supported named launch and change/resume/compaction note identifies its own AgentId,
revision/hash and verified absolute projection path. Read that exact file even when gitignored;
never glob `.antiphon/pins`, enumerate siblings, or fall back to a root `antiphon.md`. Resolve the
path server-side from persisted owning session identity. A cwd/host change refreshes the pointer
even when the active content hash is unchanged. All kinds get the stored file after first use;
unsupported kinds and tool-disabled seats still report their actual runtime-read limitations.

Serialize by canonical execution host/path and AgentId; use Windows case/separator semantics,
verify containment of every component, and refuse symlink/junction/reparse ambiguity. Two agents
sharing a cwd must create and update different files concurrently without claiming the entire
workspace. Multiple sessions of one named agent may share its projection at that host/cwd.
The marker and persisted ownership must agree with the complete AgentId before any replacement
or cleanup. No filesystem or database lookup may infer agent identity from the cwd.

An I/O, tracked-file, ownership or unsafe-path conflict is a visible failed/pending mandatory
projection with durable retry/repair intent; it is not a successful launch/API-only configuration.
Persist the pin even on file failure. API reread is emergency continuity for an existing session;
a new/resumed pin-bearing launch must first obtain a current verified file or refuse with
`pin_projection_unavailable` (known stale content uses the stricter lifecycle rule below).
Never claim to have created or read a file that is unavailable, or overwrite foreign content to
satisfy "always". Reconciliation remains outstanding until the file is repaired.

This prevents collisions and automatic cross-agent instruction delivery, not deliberate reads
by another process with the same filesystem access. Gitignore and GUID paths are not ACLs;
Antiphon cannot fence external/shared-directory access. Do not call these files confidential or
claim hostile-tenant isolation. The operator chose file delivery with that filesystem limitation.
Rejected: the former dedicated-only/API-only fallback, mutable-name paths, fixed shared filenames,
merging everyone's pins, automatic cwd changes, and unconditional per-agent imports in a shared
CLAUDE. Native import scope is handled separately in D4.

### D4 - Narrow CLAUDE import ownership exception

File creation in D3 is unconditional with respect to workspace sharing; native auto-import is
an additional delivery mechanism only in an exclusive Claude discovery scope. A static `@` line
is not conditional on AgentId: putting A's and B's unique targets in a shared `CLAUDE.md` would
still load both sets into both sessions. Comments or an instruction to ignore another agent's
import cannot prevent that content exposure. Shared/Unverified workspaces therefore keep their
agent-specific files and exact launch/queued **file-read** pointers, with no Antiphon pin import
in the shared CLAUDE. This is not the superseded launch/API-only fallback.

Use `pinClaudeImportMode: Unverified | Dedicated | Disabled` for this enhancement only; default
Unverified, no migration scan. Dedicated requires an operator assertion of exclusive human/
unregistered use plus a canonical discovery-overlap check against registered agents (including
stopped ones) and live task workspaces, including ancestor/descendant scopes. A creation flow
that explicitly allocates an exclusive workspace may set Dedicated. Neither an agent-source
capture nor a mode change can disable D3's file. Revalidate import scope at cwd/kind changes and
Antiphon launches. Serialize scope admission and import install/removal by canonical discovery
scope so two racing launches cannot both assert exclusivity. Before admitting another identity,
remove only our verified owned import; then sharing can proceed with both agents' separate files.
If it cannot be neutralized, surface
`pin_import_scope_conflict` and refuse the affected launch instead of exposing the other set.
External/unregistered launches cannot be fenced; Dedicated remains an assertion, not an ACL.

For an eligible Claude scope, install exactly one active direct
`@.antiphon/pins/<agent-id>/antiphon.md` import, with the actual full AgentId substituted. Relative
paths resolve from the containing CLAUDE, so verify the resolved target matches this projection.
For a new or CARD-0059-managed CLAUDE floor, render the import as part of the floor's own hash and
render cycle. It carries no pin text. For an unmarked `CLAUDE.md`, a separate import provisioner
may append only this delimited, agent-keyed stanza; it must never mark/adopt/rewrite the entire file:

```markdown
<!-- antiphon:pins-import begin v2 agent=<agent-id> -->
@.antiphon/pins/<agent-id>/antiphon.md
<!-- antiphon:pins-import end agent=<agent-id> -->
```

Preserve all original bytes outside that appended range, including BOM, encoding and newline
style. A file with no trailing newline gets a tracked separator; cleanup removes only bytes
owned by the recorded append, with a compare-before-write check. Refuse unsupported encodings,
ambiguous/malformed stanzas and concurrent edits instead of guessing. Do not follow a CLAUDE
symlink into another file. Recognize an existing equivalent active direct import (including
`@./.antiphon/pins/<agent-id>/antiphon.md`) outside literal spans/fences only when it resolves to
this agent's verified file in an eligible scope, and do not duplicate or claim ownership of that
user-authored import. Another agent's import or the old root `@antiphon.md` is not equivalent.
Do not interpret example code as an installed import. Do not adopt a legacy v1 file/stanza from
its name/marker alone; neutralize it only with recorded ownership, otherwise show a conflict.

Keep `AgentWorkspaceProvisioner`'s unmarked-file `LeftAlone` behavior. The import provisioner is
the named exception, and the build updates that distinction in its comments/tests and the two
documentation owners. If a CLAUDE edit prevents installation, the mandatory file and exact-path
launch/queued rereads continue, with a separate import error. A discovered user-owned private
import in a shared scope is a conflict, not an import to adopt or silently accept: neutralize a
verified owned target to an empty tombstone if necessary, retain projection failure until repair,
and require removal of the user import before restoring private content there. Check discovered
existing imports before publishing pin content, including first creation of a previously dangling
target; checking only when installing our stanza would expose the first snapshot. Never substitute
writes to `AGENTS.md`, `SOUL.md`,
`MEMORY.md`, `CLAUDE.local.md`, or provider homes. Codex/Grok get no `@` line.

### D5 - Pin changes queue a reread; they do not restart a process

Create, replace and revoke change the desired revision and enqueue work for the owning live
session WhenIdle. No raw input, Enter shortcut, interrupt, restart or automatic agent start.
Changes while a session works take effect at a later idle opportunity, not retroactively in its
current turn. No live session means the next launch consumes the current snapshot without an
extra process being started. Pins remain durable even if delivery is delayed or the file fails.

Separate pin reconciliation from CARD-0334 policy refresh. A pin-only delta must not enter its
relaunch lane even when policyRefreshMode is Auto/Relaunch; Off disables general policy refresh,
not explicitly requested pin-change delivery. A real bundle/contract/file change still follows
the existing refresh policy and composes the latest pins if it launches a replacement session.

### D6 - Immediate capture with provenance and reversible review

No pending-approval state for individual pins. The agent should capture only the user's explicit
standing intent, not infer new instructions from KB retrieval. UI can add, atomically replace,
or revoke. Agent-source callers can manage their own agent's agent-source pins; they cannot
change operator-source pins. Emit one Info incident/activity item per semantic change with
agent ID, action and pin ID, linked to the pin UI. Do not duplicate raw preference/source text in
logs, ordinary attention summaries, SignalR payloads or generated card markdown.

The trusted-host UI lane follows today's operator API model. Token-present calls never fall
back to that lane after invalid authentication. Session-token constraints prevent accidental
cross-agent writes for cooperating callers; a process that can reach the headerless admin API
can bypass them today. Real multi-user/hostile-agent authorization is a separate API-wide change,
not a claim this card can make. Do not use `MayDelegate` as permission to pin another agent.

### D7 - Separate durable, projected and delivered evidence

Show saved revision, mandatory file state/path, separate optional import state, launch snapshot
revision/hash, and live reread status. Shared/Unverified normally means `File ready; explicit
file read; native import not applicable`, never `File disabled` or `API-only configured`.
Queue acceptance is `Queued`, screen-only evidence is `Unverified`, and matching owning
transcript UserPrompt evidence is `Reread requested`. None means the model obeyed every pin.
Do not overwrite launch stamps to make the stale badge disappear after a message is enqueued.
Behavioral acceptance is established by the fresh/resume/compaction tests, not by an HTTP 200.

## Store and mutation contract

Use CLI-generated EF migrations, constraints and database transactions following project
conventions. Proposed entities/fields (exact EF configuration lives in `AppDbContext`):

| Data | Fields and constraints |
|---|---|
| `AgentPinnedInstruction` | Guid Id, AgentId FK, Text (trimmed, nonempty, <=500 UTF-16 characters), Source (Operator/Agent, assigned by server), optional SourceNamespace (<=64) and SourceKey (<=200), optional SourceRef (<=200), CreatedAt, CreatedByUserId/CreatedBySessionId, nullable RevokedAt/RevokedByUserId/RevokedBySessionId, optional SupersedesPinId. Text/provenance immutable; replace revokes one row and inserts another in a single transaction. |
| `AgentPinnedInstructionState` | One lazy row per AgentId; monotonic Revision, concurrency token, current full SHA-256, first-use time. At most 20 active pins per agent; count and writes serialized under the agent/state row lock. Exact no-ops do not rotate revision/hash. |
| `AgentPinReconciliation` | One durable latest desired revision/hash per AgentId, pending/error state, and reconciliation progress. Pin mutation and dirty revision commit together. An API delivery receipt never clears pending file work; pin rows retain change history. |
| `AgentPinProjection` / retained cleanup record | Child projection locations keyed uniquely by (AgentId, canonical execution host, canonical cwd), with unique canonical target path per host. Persist full owning AgentId, path-schema version, exact derived relative/absolute file path, location generation, desired/projected revision, last-written byte hash, marker version, pending/error state, and independent import scope/mode/target/ownership/range/separator/hash. Record current configured/live-session consumers and retain old-path cleanup until settled. Hard-delete cleanup survives the agent cascade. Different AgentIds in the same cwd never share a target or an ownership record. |
| `AgentPinOperation` | AgentId + client RequestId unique, operation fingerprint and result IDs/revision for repeat-safe create/replace/revoke. Reusing a RequestId with different content is 409. This prevents HTTP retry from duplicating replacement or its activity/queue work. |
| Session/queue evidence | Persist owning named AgentId, projection location/generation and exact path for pin-bearing sessions, launch pin revision/hash, and last transcript-confirmed pin notification revision/hash/location generation. Add nullable `PinRefreshKey` plus requested revision/hash/location generation to queue rows, unique per (AgentSessionId, PinRefreshKey); keys cover change revision, path change, resume generation, or compaction sequence. Keep launch evidence separate from message evidence. |

Partial unique index on active `(AgentId, SourceNamespace, SourceKey)` when both source fields are
present. Same-source same-text capture is idempotent; different text requires explicit replacement
with the observed revision. Never silently resurrect a revoked source on a delayed retry. Re-pin
is an explicit operation with current expected revision and reference to the revoked row. Text
similarity is not source identity, and one agent's source key is not another's instruction.

Normalize line endings to LF; reject empty text, NUL, terminal/control characters other than LF
and ordinary text whitespace. Reject, never truncate, length/cap violations. Validate the complete
rendered candidate against the existing provider launch limits as part of mutation where the
current launch can be resolved, and always recheck the fully resolved launch. A later enlarged
operator append can still refuse a launch; record that actual refusal without dropping pins.
Render pins after channel-template expansion so `{...}` inside a pin is not a template input.
Keep the existing secret-placeholder launch tripwire; pins are not a secret store.

Soft revoke lasts for the life of the agent; hard deletion of an agent follows the existing
agent-delete contract, with file cleanup intent durably preserved independently before any
cascade. Do not describe this as an indefinite audit archive surviving agent deletion.

For pre-feature sessions, backfill named ownership only from an unambiguous current persisted
agent/session relationship, such as the agent's exact PersistentSessionId. Do not infer it from
cwd, newest transcript, or a card's later reassignment. Unknown ownership shows that live delivery
cannot be established; the next properly stamped named-agent launch establishes the relationship.
Keep cleanup records outside the deleting agent FK cascade, with the original ID as audit data.

First use atomically records mandatory projection intent with the pin; no workspace-mode choice
is needed to accept it. Resolve path components only from persisted AgentId and validated host/cwd,
never from pin text, SourceKey, display name or API input. Provision parents and write files after
commit through the execution-host I/O seam. Keep one desired content revision with separate
per-location completion: success in A's default cwd does not establish its card-worktree file.
An agent rename neither rotates content revision nor changes the path. A cwd/host change advances
location generation and schedules file/pointer reconciliation even for identical pin text.

### API

Add a focused `AgentPinnedInstructionEndpoints.cs`, DTOs and application service; register it
through the existing API composition root. Use Problem Details via HttpException subclasses.

| Route | Contract |
|---|---|
| `GET /api/agents/{id}/pinned-instructions?includeRevoked=false` | Active list by default, current revision/full hash, effective kind support, mandatory projection states/paths by location, separate native-import states, pending cleanup, launch evidence and live reread evidence. Session callers get their resolved own-session read target; revoked history opt-in for UI. |
| `POST /api/agents/{id}/pinned-instructions` | `{ requestId, expectedRevision, text, sourceNamespace?, sourceKey?, sourceRef?, replacesPinId?, repinsPinId? }`. Server assigns source/actor. Returns saved row(s), revision/hash and `reconciliation: pending`; 201 for new capture, 200 for exact replay/no-op. |
| `POST /api/agents/{id}/pinned-instructions/{pinId}/revoke` | `{ requestId, expectedRevision }`; soft revoke. An already-revoked row is a no-op; request replay returns the original result. Return current status for the UI, never pretend file/queue work was atomic with the DB. |
| Existing agent PATCH | Operator-only `pinClaudeImportMode` controls the optional native import; Dedicated requires canonical exclusive discovery scope. It never disables mandatory files, including when Disabled/Unverified. No `pinWorkspaceMode` file gate. Agent-token pin routes cannot change this setting. |
| `POST /api/agents/{id}/pinned-instructions/reconcile` | Operator retry of failed projection/notification, with expected revision. Repairs only owned artifacts and retries unconfirmed queue work through the queue's supported retry behavior; no agent start, force-send or ownership takeover. |

Expected revision 0 means an unused store. Check RequestId replay before optimistic concurrency;
two distinct concurrent semantic writes cannot both apply to the same revision. Return 409
`pin_revision_conflict`, `pin_source_conflict` or `pin_request_conflict`; 422 on invalid text,
capacity or unsupported capture target; 403 on caller mismatch; 404 on a pin outside the target
agent, without disclosing its content. Repeated no-ops produce neither a revision nor an incident.

If `X-Antiphon-Task-Token` is supplied, resolve it using existing token machinery and then require
a current live session owned by the requested named, non-pool agent. Validate lifecycle/status
and session ownership from DB, not `ANTIPHON_AGENT_ID` supplied in a body/env. Reject expired,
stopped, task-delegate, capability and cross-agent principals for this v1 API, including GET.
Do not assume a non-null SessionId proves standing ownership. Record only the session ID, never
the token. A project-side exporter uses its serving agent's scoped capture path or the trusted
operator API; adding a new integration credential is outside this card.

### UI and capture instruction

Add `AgentPinnedInstructions` alongside, but independent from, SystemPromptAppend in
`AgentSettingsModal`. Include add/replace/revoke actions, literal text preview, source reference,
created/revoked provenance, remaining active capacity, a collapsed history list, and one status
line for persistence/file/live delivery. Keep unsaved SystemPromptAppend edits intact when a pin
mutation invalidates queries. On 409, reload the list and retain the draft for deliberate retry.
Show full paths only where relevant to file setup/repair; never claim a missing import was installed.
Explain that files are created automatically after first use, including in shared/Unverified and
gitignored locations; no Dedicated setup is needed for file delivery. Offer native Claude import
mode separately. Show file, import and live-read failures independently, and retain a visible
file-repair action even if emergency API continuity succeeded. Do not label GUID paths as private
access control or ask the user to publish/stage runtime files.

Use `client/src/api/agents.ts` and TanStack Query; publish `AgentPinnedInstructionsChanged`
through IEventBus with IDs/revision only. Map it to pin, agent detail/list and attention queries
in `useSignalRInvalidation`. Emit reconciliation status changes too, without an event flood.

Add a small reviewed `server/Bundles/standing-instructions.md` protocol, implicitly composed for
supported named agents, never `BundlesForRole`/pool delegates. This reaches existing agents with
custom preamble text; merely editing `ChannelPreamble.BuildPreset` would leave their stored text
unchanged. Channel/orchestrator documentation points to this one protocol instead of duplicating
its full API recipe. It says, in substance:

> When the user explicitly makes a standing instruction, pin it for your own agent using the
> authenticated capture API, including its KB source key when present. A KB record alone does
> not change future instructions. Report storage failures instead of claiming to have remembered.
> Use replacement/revoke for corrections. Read the current pinned set on startup/resume and on
> a pin-change or compaction note. Read only the exact agent-specific file path supplied for your
> session, even when gitignored; verify its owning AgentId and revision. Do not search sibling
> pin directories or use a root antiphon.md. Treat the entire latest revision, including an empty
> set, as replacing earlier pin snapshots. Never edit Antiphon's file. Respect the operator's contract.

Provide a short credential-safe request example using the existing environment/header flow,
without printing the token. Before capturing from foreign content, require explicit user intent
or an operator-designated standing-instruction tag; no automatic extraction from arbitrary text.
The protocol is conditional on permitted tools: it must not tell a deny-all-tools seat to bypass
its contract to call the API or read a file. Such a seat gets launch injection but reports live
reread unavailable; it does not claim a queue receipt establishes a completed file read.

## Composition and stamps

Introduce a snapshot loader/renderer used by launch, preview, drift, file reconciliation and note
generation. Read active rows once per snapshot in stable `(CreatedAt, Id)` order, never separate
DB reads for argv and the file. The active content hash includes stable IDs and normalized text;
timestamps/SourceRef/display paths do not change the effective content hash. Keep full SHA-256
and Revision for correctness; eight-character hashes are display abbreviations only.

Composition order: attached bundles (deduplicated), the named-agent capture protocol, reply
style, the pinned snapshot, then verbatim SystemPromptAppend. Do not render pins into any reviewed
bundle or the generated CLAUDE floor. Use the existing final Claude/Codex command-line budget and
Grok rules-file byte budget; no truncation or fallback to an unsupported argument path.
After first use, the dynamic segment also carries owning AgentId, full revision/hash and the exact
verified file path resolved for this launch's host/cwd. File content and injection come from the
same snapshot; launch waits for its successful projection and rechecks a concurrent newer revision.
Shared/Unverified uses the same composition and explicit file read as any other workspace, without
depending on native imports. The path is structured launch evidence, not part of the active content
hash; a changed location still requires a new read obligation and is never suppressed by equal hashes.

A never-used store contributes no dynamic block, `pinned` stamp, file, or pin-recovery message.
The new static capture-protocol bundle is an intentional launch change even for zero-pin named
agents; the old plan's blanket byte-identical-to-pre-feature claim cannot coexist with universal
capture discovery. The low-level composer retains its no-op behavior when no new segment is
passed, and task delegates remain unchanged. After first use, compose an explicit empty snapshot
when all pins are revoked, so resumed sessions can discard earlier snapshots. Stamp it too.

Append a `pinned v<hash8>` pseudo-stamp and persist structured pin revision/full hash separately.
Keep the launched values immutable for that launch. `AgentService` batch-loads pin states/active
sets for list projections (no per-agent query loop) and uses the same helper for detail/preview;
`PolicyRefreshService` must use it too. Include named-agent card launches and fresh/resume paths,
and resolve the actual profile kind consistently with the current composer.

For general policy refresh, compare bundle stamps excluding only the well-defined dynamic pin
segment. For file drift, exclude only ownership-verified per-agent projections at their recorded
paths, and normalize only the exact managed import stanza out of `CLAUDE.md` content before its
policy hash; the pin reconciler tracks both separately.
For a managed floor, derive the comparison from its body with the stanza omitted, including
recomputing/omitting its generated hash marker so the marker alone cannot trigger a restart.
Unrelated authored changes, other imports and the static capture-protocol bundle still count as
policy drift. A user-configured InstructionFiles list must not accidentally restore the generated
pin file to the restart lane. Never exclude every file named `antiphon.md` or all `.antiphon/`
contents by name alone. Malformed/unowned stanzas are not stripped. Preserve legacy launch
hash comparison until a compatible normalized baseline is established; do not manufacture a
general-policy refresh solely because this hash representation changed.

The static protocol explicitly makes embedded/imported snapshots replaceable: current DB/file
revision supersedes older pin snapshots, but never the operator contract. This matters for revoke
and for provider resumes retaining old instruction text. Startup/resume and compaction notes
refresh the current set even when the content hash matches the last delivered note, because the
conversation generation/context has changed. A live file write alone is not sufficient.

## antiphon.md lifecycle - `.antiphon\pins\<agent-id>\antiphon.md`

The file is an agent-scoped local runtime projection, not a portable source of agent settings or
a filesystem confidentiality boundary. All references to `antiphon.md` below mean the full
agent-specific D3 path, never a cwd-root file. Its first line identifies schema, owning AgentId
and full content hash, followed by `## Pinned`, revision and the
literal instruction block. Include a short owned-file/read-current-set contract; keep history and
KB source content out. Never read this file back as a database update.

| Event | Required outcome |
|---|---|
| Agent create/start with no pin history | No new pin file/import. Existing CARD-0059 behavior remains. |
| First capture in any existing valid cwd, including shared/Unverified | Persist pin and mandatory projection intent first; create validated `.antiphon\pins\<agent-id>` parents, project by same-directory temp + atomic replace/create; install D4 import only after file exists and native scope is eligible. Persist ownership and successful hashes separately from import state. |
| Pin create/replace/revoke | Atomically rewrite the whole owned projection from the latest complete snapshot; compare identical bytes to avoid mtime churn. Queue the matching revision after reconcile. |
| Last pin revoked | Retain a small agent-specific owned file saying no active pins in every still-used location, and retain our import only where eligible. Queue this empty revision with the owner's path. No dangling import or resurrection from a stale snapshot; other agents' files are untouched. |
| Missing cwd, first-write failure, unowned/tracked target, unsafe path | Do not create the cwd, overwrite a foreign file, or point the agent at it as trusted content. Record mandatory projection failure and retry intent; existing-session API continuity does not settle it. Fresh/resumed pin-bearing launch requires repair under D3. |
| Unsafe or uneditable CLAUDE import | File creation still proceeds where its path is safe. Deliver the verified exact file pointer without adding an import; known unsafe existing auto-imports require D4 neutralization/refusal. |
| Managed file removed | Recreate at reconcile/next launch in every still-required location, including shared/Unverified; never recreate another agent's file from this agent's snapshot. |
| Removed ownership marker or unexpected contents | Treat as a conflict; do not adopt foreign/manual content. Operator repair regenerates from DB after checking the recorded ownership and current revision. |
| Agent rename | Same full AgentId, same file identity; no rename, content revision or cleanup solely for a display-name change. |
| Agent cwd/host changes or named-card launch uses another worktree | Persist new location and old-path cleanup intent before pointer changes. Create a current file in the new actual cwd after first use and supply that exact path. Continue reconciling an old projection while an owning live session still uses it. Once unused, remove only our exact import stanza and verified owned file. For a user-owned import, retain an empty owned tombstone with retained-for-import status. Never change another agent's path or delete shared parents/siblings. |
| Import mode Disabled/Unverified or workspace becomes shared | Retain/update the mandatory file; remove only our verified owned native-import stanza before another identity can discover it. Deliver exact per-session file pointers. User-owned imports and unsafe cleanup use D4's explicit conflict/repair rule. |
| Kind changes | Pins and mandatory file survive, including unsupported kinds. Remove only our Claude-specific stanza when leaving Claude. Returning to Claude installs it idempotently only where D4 scope permits. |
| Agent hard-delete | Queue ownership-checked cleanup durably outside cascaded pin rows; no model turn or automatic session start for cleanup. Keep pending/error visible through the existing attention mechanism. |
| Startup or missed callback | Reconcile dirty desired states and compare known owned projections. A completed HTTP response is not the only opportunity to write the file or notify. |

All file writes verify the resolved path, full owning AgentId, expected previous bytes and latest
desired revision under a per-projection serialization seam. Shared CLAUDE append/cleanup and Git
exclude changes have their own canonical file locks; lock ordering is consistent. Recheck before
replacement; retries converge to the newest revision rather than letting a slow older writer win.
Use an infrastructure I/O
interface for testable atomic file work; keep pure rendering in Application. Reconcile on pin
mutation, workspace/kind/import-mode change, launch and a bounded background retry for dirty states.
Record create intent before I/O so a crash after creation but before success persistence can
recognize only that intended AgentId/path/revision/byte hash on retry. Never adopt an arbitrary
file simply because its marker looks plausible. Cleanup has the same compare-before-write rules;
remove only verified owned files and, if desired, empty owned leaf directories, never recursively
remove `.antiphon`, `pins`, or another agent's subtree.

Honor existing ignore coverage (this repo already ignores `.antiphon/`); otherwise add a narrow
local Git exclude for the owned `.antiphon/pins/<agent-id>/` subtree including temporary files,
using its path relative to the repository root when cwd is a subdirectory.
Resolve the effective exclude path with Git (`git rev-parse --git-path info/exclude`), accounting
for linked worktrees/common Git metadata; do not assume cwd contains a `.git` directory. Preserve
unrelated exclude bytes and serialize updates. Never stage/commit a projection or force-add it;
refuse an already tracked target even if an ignore rule matches. Non-Git workspaces still get the
file. Do not change the project's `.gitignore` or hide its entire CLAUDE file. An appended import
in tracked unmarked CLAUDE remains a visible local diff; UI/docs explain its runtime-only target.

A stale owned file after revocation is more serious than a never-created file. Queue an API-based
current-set reread for the live session and show cleanup failure. Before a new/resumed launch,
neutralize the known stale auto-import (regenerate/empty/remove only owned bytes); if impossible,
refuse that launch with `pin_projection_stale` instead of loading known revoked content while
reporting success. Do not kill the existing session. Explicit-path readers also retain mandatory
projection failure until their current file is restored; before consuming its pins, the read contract
checks AgentId/revision against the supplied pointer and uses emergency API recovery on mismatch.
Unsafe shared-scope imports discovered after configuration changes use D4's removal/conflict rule;
ordinary shared files remain valid and must keep being written.

## Durable queue and compaction behavior

1. Capture/revoke transaction commits pin rows, new revision/hash, semantic activity, and dirty
   reconciliation state. Do not perform filesystem or runner I/O inside this DB transaction.
2. A reconciler reads the latest snapshot and writes each required agent-specific file regardless
   of sharing/import mode, then supplies its verified path. Actual failed/unavailable projection
   selects an explicitly degraded API recovery note for existing sessions; this does not strand
   notification behind endless file retries or clear mandatory file work. Shared is never a failure.
3. Resolve the actual owning live sessions from persisted identity, including a named-agent
   card session. Never broadcast by cwd or channel. Persist a notification intent per session
   generation/change revision/projection location generation; offline state still requires the
   configured projection, and the next launch consumes its current snapshot and exact file path.
4. Extend the queue's enqueue contract with a nullable unique PinRefreshKey, using its established
   session serialization/DB transaction pattern. Insert or obtain the same WhenIdle System row
   idempotently with `deliverIfIdle: false`, then link the intent. A crash after queue insertion
   but before linking rediscovers the same key; a crash before enqueue leaves a recoverable intent.
5. Coalesce only messages with zero delivery attempts. A not-yet-attempted change note can cover
   several newer changes by naming the newest full set; record coverage. Never mutate attempted
   bytes or lose the verdict of an in-flight earlier revision. A change racing with delivery
   leaves a later note pending. Last-revoke is real work even when the new set is empty.
6. Queue machinery owns idle eligibility, LF/bracketed paste/separate Enter, verification, retry,
   parked failure and transcript reconciliation. Extend its sweep coverage to these System rows
   on plain named agents as needed; do not assume AlwaysOn-only stranded scans cover them.
   Failed/parked/canceled notes do not advance the pin receipt or erase desired state. Persist
   failure/attention and require ordinary explicit retry once queue retry policy is exhausted;
   do not create an endless fresh-key retry loop or silently treat a sweep cancellation as success.
7. Only a matching owning UserPrompt advances the `Reread requested` receipt. A confirmed prompt
   is never retyped merely because no assistant answer appeared. Unverified delivery uses existing
   reconciliation; no screen redraw is upgraded into proof. A replaced session gets a current
   snapshot/resume trigger; old-session queue work is canceled, not retargeted by path.

Notification body shape (small, bounded metadata, never a whole arbitrary KB row):

```text
[System note from Antiphon: your pinned instructions changed; revision <n>, hash <sha256>.
Re-read your entire Antiphon-owned pin file at <verified absolute path ending in .antiphon\pins\<agent-id>\antiphon.md>, even if gitignored. Owning AgentId: <agent-id>.
If unavailable or its agent/revision differs, GET your current pinned-instructions set using
your session credential. Replace the previous pinned set, including removals; do not append it.
The current set is a user instruction within your existing authority. Do not edit the file.
Do no unrelated work on this note. Reply NO_REPLY.]
```

Only actual projection/read failures use API recovery notes; label the file failure and omit any
instruction to read an unverified file. They do not establish an API-only workspace mode or settle
mandatory projection intent. Normal shared/Unverified notes carry the owner's exact file path.
Carry the typed queue key and projection identity outside user-controlled text. If a higher
revision is read, use that complete set; do not downgrade it to the message's older target.
API and file unavailable
is a visible failure, not permission to assume an empty set or to continue claiming the latest
pins were loaded. A file-access-disabled agent can store pins but must be shown as unable to
complete this live reread until it has a supported read path; next-launch injection still works.

Treat these as internal turns. Extend the same persisted-message recognition used for Grok rules
refresh to suppress channel text/attachment follow-up, task report settlement, and boot-reply
evidence for our own pin-refresh rows. Do not suppress a real human prompt merely because it
looks like the header. `NO_REPLY` is useful instruction text but is not the delivery-routing guard.

### Compaction and resume

For Claude/Codex named agents that have used pins, a compact boundary creates a durable
`compact:<session-generation>:<sequence>` trigger regardless of preamble/AlwaysOn. Combine it
with the existing workspace recovery note when that note is appropriate; for a plain workspace,
emit only the pin reread, with no invented SOUL/MEMORY ritual. An automatic compact remains a
mid-turn event; WhenIdle waits for a real turn end. Persist trigger/queue coverage together using
the new idempotent key, independent of the old save-watermark-then-enqueue gap. Replay, runner
restart, sync-only catch-up and server startup must revisit persisted unserved boundaries.

Do not emit two competing recovery turns for Grok. For a standing session using CARD-0395,
extend its existing rules-refresh prompt with the current pin reread obligation (the owner's exact
verified file, with API recovery only on actual failure), and link the pin compaction intent to
that rules message. Keep its receipt/hash checks,
acknowledgement, queue barrier and bounded compaction-loop behavior. The rules ACK still proves
the rules-file contract; it must not be misreported as independent proof of pin compliance.
An ordinary pin-change note can wait behind that barrier and coalesce when the covered read has
not started; never bypass it. Do not add a second rules-file transport or live rewrite API.

The static standing protocol in the runner-owned Grok rules file says that its pin snapshot is
historical and the latest external pin set supersedes it. Thus a live change need not mutate that
runner file; a later rules reread must also read the current pins, so old snapshot text cannot
be treated as a renewed pin. Fresh/resumed launches still supply the current GrokRulesPayload
through the existing composer and receipt path. Legacy resume refusals remain; no claim that
CARD-0395's outstanding real-provider acceptance is settled by this card.

Named-agent resume also creates a generation-specific current-set read obligation even if its
launch already composed the latest hash; unchanged hash does not establish provider adoption of
new launch arguments. Reuse the normal startup/resume note where possible and its queue ordering.
Pin-bearing older sessions without the static protocol receive a self-contained first change
note; after their next launch the protocol is permanent. Test two compactions after a live revoke
to expose old launch snapshot resurrection, not just a single successful reread prompt.

## Implementation slices and test ownership

These are design slices for the later Code task, not work completed here. Separate TestDesign
must turn the acceptance targets into V-n/R-n/PC-n cases, red controls, fixtures and exact commands.
File names marked **new** are proposed, not existing mechanisms.

| Slice | Concrete files/seams | Acceptance tests to extend or add |
|---|---|---|
| S1 - Store, concurrency and caller boundary | **New** Domain pin/state/operation/reconciliation/projection entities; `Agent.cs`, `AgentSession.cs`, `SessionQueuedMessage.cs`; `Infrastructure/Data/AppDbContext.cs`; CLI migration; **new** `AgentPinnedInstructionService.cs`, DTOs, endpoints; Program registration. Reuse token resolver with explicit live named-agent ownership validation. | **New** `AgentPinnedInstructionServiceTests`/`AgentPinnedInstructionEndpointTests`: persistence/reload, cap under concurrent writers, revision conflicts, repeated requests, replacement/re-pin, soft revoke, forged source, wrong/dead/task/capability token, operator-vs-agent source rules and trusted-host boundary; first-use projection intent, full-ID path ownership, independent per-location progress, rename stability, location-generation changes and cleanup surviving cascade. Scope assertions to test-owned agents. |
| S2 - Snapshot and every named launch | **New** pure pin renderer/snapshot loader and `Bundles/standing-instructions.md`; `InstructionBundleComposer.cs`, `AgentSessionLaunchComposer.cs`, `AgentControlService.cs`, `CardService.cs`, `OrchestratorService.cs`, `AgentService.cs`; session launch stamp/path plumbing. | `InstructionBundleTests`, `AgentSystemPromptLaunchTests`, `NamedCodexAgentLaunchTests`, `GrokRulesCompositionTests`, `DelegateBundleLaunchTests`; **new** `AgentPinnedInstructionCompositionTests`: ordering, stable/full hash, empty-after-revoke, no template/import interpretation, actual budgets, unsupported kinds, named-card actual-cwd inclusion and pool exclusion; current verified file plus exact owner/path on every supported launch, shared/Unverified success and real projection-failure refusal. |
| S3 - Mandatory per-agent file and scoped import | **New** `AgentPinnedInstructionWorkspaceService.cs` and execution-host Infrastructure atomic-file I/O seam; cwd/import-mode handling; `AgentWorkspaceProvisioner.cs` managed-floor optional import; Git local exclusion through existing Git seam. | `AgentWorkspaceProvisionerTests` retain unmarked LeftAlone; **new** `AgentPinWorkspaceTests`: lazy/no-history, two same-cwd agents with distinct files, Unverified/Disabled import modes still create files, rename/name-reuse identity, shared/ancestor native-import exclusion, immutable byte preservation, managed floor refresh retains own import, same-agent active vs fenced/foreign-ID import, CRLF/BOM/no-final-newline, atomic failure/concurrent writer, foreign/tracked target, marker conflict, path alias/reparse, linked-worktree ignore handling, last revoke, cwd/kind/delete cleanup without sibling deletion, unavailable/stale projection and unsafe-import launch refusal. |
| S4 - Durable change/reread queue | **New** `AgentPinnedInstructionReconciler.cs`; queue idempotent enqueue extension and scan integration in `SessionMessageQueueService.cs`; lifecycle wakeups; `ChannelReplyDispatcher.cs`, task-settlement and boot-evidence internal-turn classification; `AttentionService.cs`/`AgentSupervisorService.cs` activity and unresolved cleanup projection. | **New** `AgentPinRefreshTests`: crash before/after file write/enqueue/intent link, replay and coalescing, attempted-byte immutability, latest-wins races, busy idle gate, offline/replacement session, plain-agent stranded flush, parked failure/no automatic fresh retries, screen-unverified vs transcript receipt; same-cwd sessions receive only their own exact file pointers, equal-hash path changes still notify, API recovery never clears file failure; existing queue delivery-verification and channel machine-turn suites. |
| S5 - Policy and recovery integration | `PolicyRefreshService.cs`, `InstructionFileStamps.cs`, `AgentService.cs`, `CompactionRecoveryService.cs`, `AgentSessionRuntime.cs`, `ChannelPreamble.cs`, `GrokRulesRefreshService.cs`; resume note composition. | `PolicyRefreshServiceTests`, `InstructionFileStampTests`, `CompactionRecoveryTests`, `GrokRulesCompactionRecoveryTests`, `GrokRulesQueueBarrierTests`; **new** pin recovery cases: pin-only no kill under Auto/Relaunch/Off, true bundle drift preserved, hash-baseline migration, no-preamble compaction, replay/catch-up, auto compact mid-turn, Grok barrier/coverage and old snapshot revoke across resume/two compactions. |
| S6 - Operator UI and owners | **New** `client/src/features/agents/AgentPinnedInstructions.tsx`; `AgentSettingsModal.tsx`, `client/src/api/agents.ts`, `useSignalRInvalidation.ts`; update docs/agent-workspaces.md, docs/agent-instruction-file-contract.md, docs/agent-kinds.md, docs/agent-credentials.md, docs/session-runtime-invariants.md, docs/antiphon-api.md and applicable orchestration/channel capture guidance. | **New** `AgentPinnedInstructions.test.tsx`; `AgentBundleAttachments.test.tsx`, invalidation tests. Add/replace/revoke/history, retain drafts on conflict/invalidation, mandatory file readiness in shared/Unverified, optional import setup as separate state, repair pending despite API receipt, unsupported kinds; no raw pin content in events/attention export. |
| S7 - End-to-end acceptance | **New** isolated server/runner pin scenario and installed-CLI canary, using existing fake/real provider fixtures; no production runner, broker or workspace. | Persist source-tagged arbitrary instruction, fresh launch, mid-turn replace, last revoke, resume, two compactions; verify next actual behavior for Claude/Codex/Grok where supported. Claude native import to the unique target with unmarked file in exclusive scope; two same-cwd agents both receive files and only their own pins through exact pointers, with no shared pin import; shared/ancestor cross-load negative. Separate queue proof from model compliance. Real-provider prerequisites/remaining CARD-0395 limitations reported explicitly. |

### Verification handoff constraints

The TestDesign stage must include meaningful negative controls: remove the pin segment from
composition; remove the import stanza installation; omit the change/revoke trigger; allow raw
pin text to be parsed as an import/template; compare only enqueue status; let policy refresh see
pin-only drift; let the old Grok snapshot win after compaction. Import-installation controls apply
to eligible exclusive scopes, not shared scopes where that import must be absent. Each should fail
the corresponding acceptance check. Do not add tests that merely repeat renderer implementation details.

For the D3 amendment, add explicit acceptance cases and red controls for:

- Two different full AgentIds sharing a short-ID prefix in one cwd, with distinct synthetic pin
  canaries, concurrent first capture/replace/revoke, independent current files and exact launch/queue pointers. Mutants using
  one root `antiphon.md`, a last-writer alias, truncated/name-derived IDs or cwd-based ownership
  must fail. Include rename and deletion/recreation with the same display name.
- Shared, Unverified and native-import-Disabled first capture creates the file, even if ignored;
  non-Git and linked Git worktrees work too. Restore the old Dedicated-only gate or drop file
  creation while retaining API/launch injection: acceptance must fail. Assert actual file bytes
  and the explicit read target, not just a ready flag or successful pin POST.
- A shared unmarked CLAUDE stays byte-identical, with neither A nor B imported. Test ancestor/
  descendant discovery and exclusive-to-shared transition, including a stopped registered agent
  and a task workspace. A mutant appending two agent-unique imports into shared CLAUDE must fail
  context/canary isolation despite having separate files. No conditional prose or marker is a
  substitute for preventing native cross-load; S7 validates installed Claude behavior. Include a
  pre-existing user import to a not-yet-created target: first capture must detect the unsafe
  shared import before writing nonempty pins, retain file failure, and require repair.
- In an eligible exclusive unmarked CLAUDE, only the owned v2 stanza/separator is appended and
  later removed; its direct target resolves to the exact full-ID projection. Preserve BOM,
  encoding, newline style and every surrounding byte through refresh and cleanup. Wrong-agent
  imports, fenced examples, root legacy imports and forged markers never confer ownership.
- Revoke/delete/cwd changes affect only the owning file/stanza, and a still-live same-agent
  session keeps its old location current until release. Simulate a stale writer, crash after file
  create, failed import cleanup and delete cascade. Sibling files and unrelated `.antiphon`
  content survive; equal content hashes do not suppress location-change rereads.
- Read-only/missing cwd, a tracked target and unsafe reparse paths remain visible mandatory-file
  failures, with no foreign overwrite or false completion after API continuity. Repair converges
  to the latest revision and unblocks launch. Pin-only file/stanza changes never trigger restart;
  unrelated similarly named files still count in configured policy drift.

Filesystem isolation assertions cover collisions, pointers and automatic loading, not an inability
for a hostile same-user process to open a sibling file. Do not use filesystem ACL denial as the
expected result for this GUID-path design.

Use `dotnet run --project tests/<ProjectName>` with explicit test-class treenode filters, never
`dotnet test`. Use the documented isolated output path (for example
`--property:OutputPath=bin-card0262/`, with its trailing forward slash) if needed. Run Antiphon.Tests
and Antiphon.Agents.Pty.Tests sequentially; process-spawning classes take their assembly's limiter.
Use `pwsh -File scripts/test-client.ps1 AgentPinnedInstructions.test` for the new client suite.
Rebuild client/dist before E2E. New Program-booting tests require the production-runner guard or
an isolated random runner. Real CLI tests use isolated provider homes/stubs per the testing and
agent-kind owners and must name their actual provider/version evidence, not a guessed endpoint.

No build or tests were run in this planning task. Documentation checks are limited to diff
whitespace, referenced-path/convention review, and confirming that the amendment commit contains
only this revised plan. Code must not report S1-S7 as verified from this artifact.

## Rollout, limitations and completion criteria

1. The operator has required always-create-file and released the build hold in task `d4769008`.
   Caller updates the card's standing hold language separately. Land this amendment before the
   separate TestDesign dispatch, then Code; do not implement the superseded dedicated-only plan.
2. Migration creates no pins and writes no files. Deploy the capture protocol with the feature;
   existing running sessions need their normal instruction refresh or an explicit explanatory
   note before autonomous capture can be expected. Do not mass-edit stored custom preambles.
3. For the originating agent, read its actual AgentId, host, kind, session, cwd, instruction-file
   ownership and shared use through the owning deployment. That external workspace is not
   established by this checkout. Resolve its full-ID file path and create it after first capture
   regardless of shared/Unverified status. Assess Dedicated solely for the optional native import;
   shared use always gets the file plus exact launch/queued file-read pointers.
4. With operator authorization on that deployment, capture the original desired instruction once,
   with its original KB source key, and verify full propagation. Use synthetic non-private source
   keys in repository tests/docs. Deployment alone does not backfill that foreign row.
5. Close CARD-0262 only with store/API/UI, launch/stamp, file/import, queued change/revoke,
   compaction/resume and per-agent isolation evidence, plus clear reporting of any unavailable
   provider acceptance. Attachment delivery remains CARD-0250's separately landed behavior.

Limits remain explicit: per-agent persistence is not portable KB sync; no pin inheritance into
delegates; files are mandatory after first use but native imports require exclusive scope;
per-agent paths prevent collision/automatic misdelivery, not filesystem access by other processes;
API locality is not full authentication;
notification delivery is not proof of model obedience; a preference cannot revoke already-performed
work. Rolling back application code stops new capture/reconciliation but does not erase owned
files or imported instructions. Quiesce pin-bearing use and perform ownership-checked cleanup before rollback
where necessary; pending owned-file cleanup and any stale-import refusal remain visible. Never
drop the pin tables or delete arbitrary workspace files as an implicit rollback step.

## Verification design

TestDesign: 2026-09-08, task `8ef399b2`, against landed plan/code HEAD `4db2076b`.
This section specifies executable acceptance for S1-S7; it does not implement or certify them.
The D3 amendment is the baseline: a file is mandatory after first use in shared/Unverified
workspaces, while native Claude imports require exclusive discovery scope. The build hold is
released. The next stage is Code, with no further design decision required.

### Proves it works now

#### Fixtures, naming and independent oracles

All classes below labelled **new** must be added by Code. In each class, prefix methods with
the corresponding zero-padded verification ID (`V01_`, `V02_`, etc.); use named data rows for
the enumerated variants. Thus `/*/*/AgentPinWorkspaceTests/V07_*` is an executable narrow
filter after implementation. A matrix row is not satisfied by one representative variant.
Existing classes are regression coverage to extend, not a claim that they already test pins.

- **Store/HTTP:** use `TestDbFixture` and real PostgreSQL, fresh DbContexts for reloads and
  competing writes. Scope all reads/counts to fixture AgentIds/session IDs/request IDs. Use
  `AntiphonWebAppFactory` for the actual endpoints, its isolated schema and refusing runner;
  preserve `ProductionRunnerGuard`. Assert attempts as well as successful starts. A global
  reconciliation sweep takes `[NotInParallel]` without a group key. Migration acceptance uses
  an isolated database, never the running installation's database.
- **Workspace:** add a fixture under `tests/Antiphon.Tests/TestHelpers/` wrapping the real new
  Infrastructure file implementation. Each test owns an absolute temporary root outside the
  checkout's instruction-discovery tree, a non-Git cwd and disposable Git repos/worktrees.
  Add an external-I/O decorator with awaitable gates immediately before create/replace/delete,
  after atomic publication but before success persistence, and before import compare/write.
  It can throw access/I/O errors, record host/path/operation/bytes, and delegate to real disk.
  Use barriers/TaskCompletionSource, not timing sleeps or only mocks returning `Ready`.
  Races require separate service instances/DbContexts. Inspect files with `ReadAllBytes` and
  precomputed byte arrays; never build the expected file through the production renderer.
- **Identity data:** within each test generate a unique eight-hex prefix, then make A and B
  full GUIDs sharing it but with different remaining 24 hex digits. Give them distinct valid
  slugs and colliding slug/name prefixes. Pin unpredictable `A_ONLY_<nonce>` and
  `B_ONLY_<nonce>` instructions. Calculate expected paths in the test with full `ToString("N")`.
  Reuse A's display name for C after deleting A. Seed unrelated sibling files, a root
  `antiphon.md` decoy, and `.antiphon/task-sentinel.txt`; snapshot bytes before each operation.
  A separate host fixture gives two execution hosts the same cwd spelling but different backing
  directories. A launch must not silently substitute server-local I/O for execution-host I/O.
- **Launch/queue:** extend the `BridgeQueueHarness` pattern and its recording protocol adapter;
  register the git graph through `DelegationTestServices`. Drive actual `AgentControlService`,
  named-card `CardService`, and `OrchestratorService` entry points, then inspect launch requests,
  persisted session ownership/stamps and real queued rows. Use `TimeProvider.System` or a
  real-clock offset, never a frozen clock in queue polling. Record every raw input, Enter,
  interrupt, stop, start and outbound-message call. Assert transcript receipts from persisted
  owning `UserPrompt` records, not adapter output alone.
- **Process/CLI:** new process-spawning classes take their assembly's
  `[ParallelLimiter<ProcessSpawnLimit>]`; opt-in installed CLI tests also use `[Explicit]` and
  the established serial group. Reuse `FakeLlmApiServer`, `RealCliStubEnv.ForClaude/ForCodex/ForGrok`,
  `RealCliStubGate`, `RealCliStubClaudeConfig.SeedOnboarding` and the isolated direct-runner
  pattern in `RealCliStubBServerHarness`. Inspect recorded model requests as well as transcripts.
  Require nonce on the chat path and synthetic credential on its expected path before accepting
  a stub run. The test cannot infer that no external request occurred merely because all requests
  observed by the stub were local. Keep provider homes isolated; never copy the user's Codex auth.
- **No fake assurance:** file-ready means expected bytes exist at the exact host/path and agree
  with the stored snapshot/ownership. Launch-ready additionally means the request consumed that
  verified revision. `Queued`, screen-only `Unverified`, transcript `Reread requested`, actual
  file read, and model behavior remain separate assertions/evidence columns.

#### Store, caller and launch coverage

| ID | Behaviour / layer / test | Setup, action and required assertions |
|---|---|---|
| V-1 | Durable capture and first-use intent / integration / **new** `AgentPinnedInstructionServiceTests.V01_*` | Migrate an empty isolated DB: no pin rows or file I/O. Capture a synthetic source-tagged instruction, recreate the service/DbContext, and verify immutable text/provenance, full hash, revision 1, first-use time and dirty mandatory projection intent. Inject failure before transaction commit: none survives and no I/O occurs. Inject failure after commit before reconcile: all durable intent survives. Exact RequestId replay, same-source/same-text capture and already-revoked no-op create no extra revision, activity or work; changed RequestId fingerprint returns `pin_request_conflict` even with a stale expected revision. |
| V-2 | Atomic limits, source replacement and history / integration / **new** `AgentPinnedInstructionServiceTests.V02_*` | With 19 active pins, race two captures at the same expected revision in separate transactions: one wins, the other conflicts; retry cannot exceed 20. Race replace/revoke on one revision: never leave both old and new active, nor lose both through a partial replacement. Changed text for an active source requires replacement; delayed replay after revoke cannot resurrect it; explicit re-pin links the revoked row with current revision. Test 0/1/500/501 UTF-16 units, normalized CRLF, NUL/ESC/other forbidden controls, 20/21 pins and provenance length boundaries. Reject without truncation or revision/activity. Two agents may use the same source key independently. |
| V-3 | Real endpoint principal boundary / integration / **new** `AgentPinnedInstructionEndpointTests.V03_*` | Exercise GET, capture and revoke through HTTP with valid own live named-session token, wrong agent, stopped/expired session, task-delegate, capability, invalid and empty token-present headers. Even `MayDelegate` cannot authorize cross-agent access; token-present failure never uses headerless operator fallback. Agent-source cannot forge Source/actor, replace/revoke operator-source pins or set Dedicated. Foreign pin IDs return 404 without text; caller mismatch 403; expected-revision/source/request conflicts are the specified 409 Problem Details. Headerless trusted-host operator success is explicitly tested as today's API model, not secure identity. Reconcile retries existing intent with expected revision and causes no start/force-send. |
| V-4 | Same snapshot in every supported named launch / integration / **new** `AgentPinnedInstructionCompositionTests.V04_*` | Data rows: Claude/Codex/Grok, fresh/resume, AlwaysOn/manual, custom/no preamble, Unverified/Dedicated/Disabled; cover ordinary start, assigned named-card worktree and orchestrator launch entry points. Persist a pin then launch. Assert actual argv/developer instructions/GrokRulesPayload contains its text, full AgentId, revision/full hash and verified absolute execution-cwd file pointer; compare to disk and immutable session launch evidence. Capture before launch with no live session still creates the configured projection and starts nothing. Block publication, create newer revision, release launch: never launch the now-obsolete snapshot. Inspect the second current read/retry or explicit refusal. Pool/task-role delegates receive neither pins, protocol nor projections. Raw/OpenCode retain stored projections but advertise unsupported runtime; deny-all-tools named seats retain file/injection but receive no instruction to bypass tools and show unavailable live read. |
| V-5 | Literal rendering, budgets and stamps / unit plus integration / **new** `AgentPinnedInstructionCompositionTests.V05_*` | Use pin text containing `{agent.name}`, an LF-start `@./payload-canary.md`, inline backticks, runs of backticks and tildes, HTML, shell syntax and `[[attach:...]]`. Exact normalized text survives as literal data inside an unbreakable fence; payload contents do not enter instructions and no attachment/command is executed. Launch order is bundles, static protocol, style, snapshot, verbatim operator append. Reorder DB enumeration: deterministic snapshot; source ref/rename/path changes leave effective full hash alone, text/ID changes do not. Compare actual provider budget boundary at N and N+1 using current composer limits, including a later enlarged operator append and the existing secret-placeholder tripwire. No truncation/unsupported fallback. Never-used has only the intentional static protocol change; post-last-revoke has an explicit stamped empty set. List/detail/preview and policy use matching snapshots with bounded batch query count for 1 versus 20 agents. |

#### Mandatory file, discovery and byte custody coverage

| ID | Behaviour / layer / test | Setup, action and required assertions |
|---|---|---|
| V-6 | Lazy first use in every workspace / integration / **new** `AgentPinWorkspaceTests.V06_*` | Start/create never-used A: no pin subtree or pin import. First capture then reconcile in shared and single-agent Unverified, native-import Disabled, ordinary project, existing `.antiphon/` ignore, non-Git and linked worktree/subdirectory variants. In every case inspect actual `.antiphon/pins/<full-A>/antiphon.md`, marker/full ID/revision, `## Pinned`, literal canary and exact launch/read target; no Dedicated prerequisite and no API-only/file-disabled result. No root alias, sibling index or changed cwd. A missing cwd remains missing and is visibly pending/failed; file creation starts only once that cwd is independently repaired. |
| V-7 | Full identity and concurrent shared use / integration / **new** `AgentPinWorkspaceTests.V07_*` | A/B share cwd and short-ID prefix. Interleave first creates, replaces and A's last revoke using barriers; assert different target/ownership rows and independent latest bytes, B never loses its pin, A ends empty. Launch both and inspect their actual launch and change-note targets: each contains only its owning ID/path/canary, never a sibling path or directory-search recipe. Rename A, then delete/recreate its name as C: rename preserves identity/revision/path, C uses its own full ID and starts with no history. Same-agent sessions in one location share one projection; two hosts with the same cwd spelling do not. Repeat through case/separator/trailing-separator aliases to verify canonical locking, not just string equality. |
| V-8 | Shared/ancestor automatic discovery / integration plus V-29 installed CLI / **new** `AgentPinWorkspaceTests.V08_*` | Snapshot an unmarked CLAUDE in shared cwd: after both captures/launches it is byte-identical, with neither import. Dedicated admission must detect same cwd, ancestor/descendant in both directions, case aliases, stopped registered agents and a live task worktree under the scope. Test root `work` versus unrelated `work-other` to prevent prefix over-rejection. Race two scope admissions while import installation is gated: both may proceed only after no foreign pin import can be discovered; they cannot both retain exclusive imports. Transition an already Dedicated A to sharing with B: remove only A's verified owned stanza before B is admitted, retain/update both files. Fail that removal: B's launch returns `pin_import_scope_conflict` and no runner start. |
| V-9 | User-owned and dangling imports checked before publication / integration plus V-29 / **new** `AgentPinWorkspaceTests.V09_*` | In shared cwd and an ancestor CLAUDE, place a user-authored direct import to A's not-yet-created full-ID target. First capture commits intent but publishes no nonempty private snapshot into that discovery path: record scope conflict/mandatory file failure, refuse affected launch. Any newly created file is only an ownership-recorded empty tombstone. Repeat with an existing owned private target: neutralize only verified owned bytes, preserve user CLAUDE bytes, keep failure until its user import is removed; API receipt does not clear it. Remove the user import as a fixture operator action and reconcile: latest file becomes ready and launch unblocks. A foreign/unowned target is never emptied or adopted. An old root import, another agent's import and forged legacy-v1 markers never count as an equivalent owned import; unsafe legacy content remains a surfaced conflict. |
| V-10 | Narrow unmarked CLAUDE append / integration / **new** `AgentPinWorkspaceTests.V10_*` | Dedicated, nonoverlapping scope with verified file. Test ASCII and strict UTF-8 (non-ASCII text, with/without BOM), LF/CRLF, empty file, one/multiple final newlines and no final newline. Record original bytes. Append exactly the D4 full-ID v2 stanza plus recorded necessary separator; prefix stays identical, no managed-floor marker is added, import resolves to this file. Reconcile/rewrite pins repeatedly: one active stanza, unchanged original bytes, no unnecessary mtime change. Disable native import: remove only recorded stanza/separator, restoring the original byte array. Preserve surrounding author edits made between completed operations; an edit during the compare/write window must instead fail safely under V-12. |
| V-11 | Import recognition and managed-floor regression / integration / **new** `AgentPinWorkspaceTests.V11_*`; existing `AgentWorkspaceProvisionerTests` | Equivalent direct `@.antiphon/pins/<A>/antiphon.md` and `@./.antiphon/pins/<A>/antiphon.md` in an eligible scope suppress duplication but remain user-owned, including on cleanup. Inline-code and backtick/tilde fenced examples do not suppress installation. Wrong-ID/root imports do not satisfy A's requirement and are checked for unsafe discovery. Malformed, duplicate/nested/mismatched agent-keyed stanzas and unrecorded legacy-v1 markers produce separate import conflicts, never whole-file adoption. Existing provisioner still returns `LeftAlone` byte-for-byte for ordinary unmarked files. A newly generated/managed floor retains exactly its own import across job/channel/style refresh, accounts for it in its own hash, and contains no pin text. Codex/Grok and scope-ineligible Claude install no import; AGENTS/SOUL/MEMORY/CLAUDE.local/provider-home sentinel bytes never change. |
| V-12 | Append/cleanup edits and unsupported encoding / integration / **new** `AgentPinWorkspaceTests.V12_*` | Pause append after original-byte read; author writes new bytes; release: compare detects conflict, preserves the author's complete bytes and does not mark import success. Retry from a fresh read can append once. Pause cleanup similarly, including a user edit inside the stanza or tracked separator: no guessed removal/whole-file rewrite. Ordinary import-install failure leaves mandatory file and verified explicit launch/read working; unsafe sharing cleanup instead refuses admission per V-8. Minimum supported corpus is strict UTF-8/ASCII above; malformed UTF-8 and UTF-32 BOM fixtures must be explicitly refused with all bytes unchanged. For any additional encoding Code elects to support (e.g. UTF-16 LE/BE), add BOM/newline/no-final-newline byte-round-trip cases; otherwise assert explicit unsupported-encoding status. Never decode with replacement or silently transcode. A CLAUDE symlink is refused without changing its referent; safe projection still succeeds. |
| V-13 | Foreign/tracked/unsafe target and I/O refusal / integration / **new** `AgentPinWorkspaceTests.V13_*` | Separate rows: unmarked target; plausible full-ID marker without persisted ownership/intent; marker owned by B; removed marker; manually altered bytes; Git-tracked target even with ignore coverage; parent component replaced by a file; junction/reparse at each directory component; file symlink; containment escape/unsafe canonical path; access denied; missing cwd; remote host unavailable. Capture remains saved, mandatory file work pending/failed; preserve foreign/referent/sentinel bytes and do not create missing cwd. Fresh and resumed pin-bearing launches return `pin_projection_unavailable`, with zero launch attempts and no trusted pointer to foreign bytes. Path components never come from pin/source/display name; callers cannot choose target paths. Repair fixture obstruction, reconcile current revision and assert real bytes, correct pointer and successful launch. Genuine NTFS reparse coverage is required; unavailable privileges are pending coverage, not a pass. |
| V-14 | Atomic publication, crash recognition and newest writer / integration / **new** `AgentPinWorkspaceTests.V14_*` | Gate before atomic create/replace: target is either absent/old complete bytes, never partial new content. Release/fail at temp write, before publish, after publish before ownership-success save; recreate service and reconcile. Only the recorded intended AgentId/path/revision/byte hash can be recovered without adoption. A plausible but different artifact stays a conflict. Pause revision n, commit/reconcile n+1, release n through its stale-write guard: final file cannot regress. Pause cleanup, repin/change desired revision, release: current file cannot be deleted. Unchanged bytes preserve mtime. Temp files stay within owned ignored leaf and are not advertised as read targets. |
| V-15 | Revoke, location and delete cleanup / integration / **new** `AgentPinWorkspaceTests.V15_*` | Last revoke writes/stamps empty set in every still-used location and retains eligible import. Change default cwd/host with old live A session and a named-card worktree: persist new location and cleanup intent, create new file and pointer, keep old session's file current through another mutation; equal content hash still advances location/read generation. On last consumer release, remove only verified owned file/stanza; user-owned import keeps an empty retained-for-import tombstone. Change Claude to Codex/Raw and back: file survives, own import removed/reinstalled only when eligible. Hard-delete after failed cleanup: reload after FK cascade and retry from retained cleanup record; B/C/sibling/.antiphon sentinel survive, never recursive parent deletion. Recreated same-name C cannot authorize A cleanup. |
| V-16 | Stale revocation refusal and emergency continuity / integration / **new** `AgentPinWorkspaceTests.V16_*` | Establish nonempty owned imported file, revoke last pin, then deny regeneration/empty/removal. Existing session is not killed; receives explicitly degraded current-set API recovery without an instruction to trust the stale file, and file cleanup remains failed. Fresh/resumed launch that could load known revoked content returns `pin_projection_stale`, not success with fresh inline pins. In shared explicit-read mode, a stale/unverified projection also cannot satisfy new/resumed launch. Repair and reconcile newest empty revision before launch succeeds. Simulated file read with wrong ID/revision invokes own-session API recovery, never sibling/root fallback; file plus API failure is visible, never invented empty success. |
| V-17 | Git exclusion custody / integration / **new** `AgentPinWorkspaceTests.V17_*` | Real disposable repo, subdir cwd and linked worktree with common Git metadata: resolve effective `info/exclude` through Git; existing covering ignore causes no edit, otherwise add only root-relative owned ID subtree including temps. Concurrent A/B exclude appends preserve original exclude bytes and both rules. `git check-ignore` covers projection/temp, `git ls-files` never contains new projection, tracked `.gitignore` unchanged; tracked unmarked CLAUDE append remains visible in `git diff`. No hiding entire CLAUDE or `.antiphon` subtree. Tracked-file conflict is tested separately from ignore success. Non-Git requires no Git prerequisite. |

#### Delivery, policy and recovery coverage

| ID | Behaviour / layer / test | Setup, action and required assertions |
|---|---|---|
| V-18 | Durable queue intent and crash gaps / integration / **new** `AgentPinRefreshTests.V18_*` | Kill/recreate service objects at post-pin-commit/pre-file, post-file/pre-intent, post-intent/pre-enqueue, post-enqueue/pre-link and post-link/pre-flush seams. Replay each twice. Inspect per-session unique PinRefreshKey, one recoverable WhenIdle System row and eventual matching current full-set note. Enqueue uses `deliverIfIdle:false`: even idle adapter sees no input before intent linkage/explicit queue processing. Saved/projected/queued/receipt remain distinct. Plain manual agent's stranded System note participates in sweep; no-preamble/AlwaysOn assumptions. No live session means no start, but configured file work still completes. |
| V-19 | Ownership and exact pointer on every trigger / integration / **new** `AgentPinRefreshTests.V19_*` | A/B same cwd plus A default/live-card/old-live-location sessions: create/replace/revoke, resume and compaction target only persisted owning named sessions, each with its actual absolute path, full ID/revision/hash/location generation. Change host/cwd with equal hash: new obligation is queued. Persist an old session then reassign its card: its ownership is not reassigned by cwd/card lookup. Pre-feature ownership backfill accepts only exact unambiguous persisted link, otherwise reports unavailable live delivery. Session replacement cancels old pending work and creates a current new-session obligation, never retargets attempted bytes or broadcasts by cwd/channel. |
| V-20 | Idle, coalescing and bounded failure / integration / **new** `AgentPinRefreshTests.V20_*` | While owner is working, replace then revoke: zero raw input/Enter/interrupt/stop/start; file may update but delivery waits for real TurnEnd. Two unattempted revisions coalesce to newest entire set with coverage. Gate after attempt begins, change revision: attempted message bytes remain immutable, later note survives and is sent at next eligible turn. Exhaust supported retry policy: parked/failed/canceled shows attention, receipt unchanged, repeated sweeps create no endless fresh keys. Explicit retry uses supported queue retry and eventually confirms. Already transcript-confirmed prompt with no assistant answer is not retyped. |
| V-21 | Evidence ladder / integration / **new** `AgentPinRefreshTests.V21_*` | For an actual pin queue row inject separately: enqueue-only, screen echo, AssistantText containing header, wrong-session UserPrompt, wrong key/hash/location, unrelated user prompt, and exact owning UserPrompt via transcript sync/catch-up. Only the last advances pin `Reread requested` and only once; launch revision/hash remains unchanged. Read a later complete revision: do not downgrade to an older message. API continuity success while file I/O still fails leaves file pending/failed and visible repair; UI must not call transcript delivery a successful file read or pin compliance. |
| V-22 | Pin-only policy drift never restarts / integration / **new** `AgentPinPolicyTests.V22_*`; existing `PolicyRefreshServiceTests`, `InstructionFileStampTests` | Under Auto/Relaunch/Off change pins, first install/remove an owned stanza and regenerate managed-floor hash: zero kill/start calls, pin queue still works. Explicitly include the owned path in InstructionFiles. Contrast a real bundle change, authored CLAUDE edit, unrelated root/sibling `antiphon.md`, unowned/malformed stanza and unrelated `.antiphon` file configured for drift: each still follows existing policy, and any real replacement launch includes current pins. Only exact ownership-verified paths/ranges are normalized; legacy hash baseline establishment alone causes no restart. |
| V-23 | Compaction/resume durable coverage / integration / **new** `AgentPinRecoveryTests.V23_*`; existing `CompactionRecoveryTests` | Claude/Codex named agent, pins used, no preamble and not AlwaysOn: compact boundary persists idempotent generation/sequence trigger and queues pin-only note. No pins/history preserves old no-note case. Test boundary replay, sync-only catch-up and service restart before enqueue; mid-turn auto compact remains held until TurnEnd. Combine workspace recovery where applicable without two turns; plain cwd gets no invented SOUL/MEMORY instructions. Resume of same hash produces a new generation obligation. Replace then last-revoke, resume and process two distinct compactions: every new request names current empty full set/own file, never resurrects historical launch pins. |
| V-24 | Grok barrier, current pins and receipts / integration / **new** `AgentPinRecoveryTests.V24_*`; existing `GrokRulesCompactionRecoveryTests`, `GrokRulesQueueBarrierTests` | Seed real persisted CARD-0395 rules state and captured native compact event. Recovery carries current exact pin pointer in the single rules refresh row and links pin intent to it. Ordinary pin-change row cannot pass closed barrier through enqueue/flush/retry/sweep. A rules ACK alone is not a pin receipt/compliance claim. Keep hash/receipt refusal and legacy-inline resume refusal. After revoke and two compactions, inspect the actual combined recovery text and requested external snapshot as empty while runner-owned historical rules bytes are unchanged; pending current pin change is not lost to rules coverage. V-30 separately tests actual model behavior. |
| V-25 | Internal-turn routing and redaction / integration / **new** `AgentPinInternalTurnTests.V25_*`; existing `ChannelMachineTurnTextTests`, `ChannelMachineTurnMatchTests` | Complete a persisted pin-refresh turn with arbitrary answer, attachment marker and even a valid-looking task report token, not just NO_REPLY. Assert zero outbound text/attachments, no task settlement or boot-ready evidence. A real human prompt with identical header wording still receives normal routing. Use persisted queue identity, not string prefix suppression. Scan captured IEventBus/log/attention/export payloads for synthetic text/source-ref canaries: semantic activity contains IDs/action only, one per actual change; no raw pins or credential values. Pin detail UI/API remains the intended text display. |

#### UI and provider acceptance

| ID | Behaviour / layer / test | Setup, action and required assertions |
|---|---|---|
| V-26 | Review/repair UI without losing drafts / unit / **new** `AgentPinnedInstructions.test.tsx`; existing `AgentBundleAttachments.test.tsx`, `useSignalRInvalidation.test.ts` | Add, atomically replace, revoke, view history/provenance/capacity and render literal malicious-looking text. Interleave SystemPromptAppend draft and pin draft with event invalidation/409: both drafts survive, current revision reloads and retry is deliberate. Shared/Unverified/Disabled shows file-ready + exact explicit-read target, separate optional-import state. Test saved-but-file-failed with API receipt, import-only conflict with file ready, queued, screen-unverified, transcript-requested, unsupported kind and tool-disabled seat. Repair remains available until file recovered. No filesystem confidentiality claim or demand to stage runtime files. IDs/revision/status events invalidate pin/detail/list/attention queries without exposing text. |
| V-27 | Real API/UI-to-runtime journey / E2E / **new** `tests/Antiphon.E2E/AgentPinnedInstructionsE2ETests.cs`, methods `V27_*` | Use `AntiphonAppFixture` with fresh built client, own Postgres, random `IsolatedSessionRunner`, refusing external message producer and fake provider. Through UI capture source-tagged arbitrary standing instruction, launch, hold a turn, replace, last-revoke, resume and replay two captured compact boundaries. Inspect disk/session/queue/transcript and rendered status at each boundary; same-cwd A/B both get files with no shared import. Reload browser and restart only fixture server between commit and reconciliation to exercise startup repair. Fake scripted output proves wiring only. Use TestDiagnostics per owner and fixture-owned process teardown. |
| V-28 | Installed Claude really expands the exclusive stanza / integration, installed CLI against stub / **new** `tests/Antiphon.Tests/Agents/AgentPinClaudeImportCanaryTests.cs`, methods `V28_*` | In an isolated exclusive repo create projection and append stanza through production service. Run installed Claude with a nonce prompt, no dynamic pin injection, no prompt containing the pin canary, and no scripted Read tool request. In recorded model request require A's file-only canary. Repeat equivalent direct import, fenced-example-plus-installed-import, non-ASCII/BOM/CRLF and after pin replacement. Include root payload file targeted by literal `@` pin: its secret-free canary must not appear. After cleanup the file-only canary must be absent on fresh launch. This isolates native expansion from launch injection; scripted assistant text is never the oracle. Record executable/version, cwd, file hashes, exact serialized context evidence and nonce/key receipts. |
| V-29 | Installed Claude cross-load negative / integration, installed CLI against stub / **new** `AgentPinClaudeImportCanaryTests.V29_*` | Drive production shared and ancestor/descendant cases from V-8/V-9, then start actual A/B sessions. On automatic-discovery-only arm (no pin injection/tool reads), neither canary occurs in any serialized system/message/tool context. On normal named-launch arm A's request has A only and B's has B only; their pointer read contract names only their file. Include owned-exclusive-to-shared transition and a newly dangling user import: before repair, no affected launch/no nonempty publication; after repair both normal arms are isolated. Exercise an ancestor inside a repo and above its root so the test does not assume discovery stops at `.git`. PC-12 intentionally adds both imports: foreign canary must become observable, establishing sensitivity of the request oracle. GUID paths are not tested as access denial. |
| V-30 | Actual fresh/change/revoke/resume/two-compaction behavior / live probe / **new** `tests/Antiphon.Tests/Agents/AgentPinBehaviorAcceptanceTests.cs`, methods `V30_*` | Execute the challenge protocol below separately for Claude/Codex/Grok using isolated provider homes and fixture-owned runner/server. Pins originate through capture and files/notes through production paths. Require provider-native transcript read evidence for exact file plus behavioral challenge outputs after every stage; model compliance is not inferred from the stub canaries or queue receipts. Report each provider/version/phase separately, with skipped/refused/missing compaction explicitly pending. Grok uses the existing rules receipt/ACK/barrier; this does not close unrelated CARD-0395 acceptance. |

V-30 challenge protocol: generate six random challenge keys and two six-item answer sets
outside the agent cwd/context. The operator append defines only a stable default: unknown pin
challenge returns `BASE`, and latest complete pin set supersedes older snapshots. The initial
single pin (under 500 characters) maps the six keys to A answers; replacement maps them to B
answers. Never put expected answers into human test prompts, recovery text or stub responses.
Fresh launch asks key 1 (A1 expected). Start a bounded ordinary work turn, replace while working,
and verify no mid-turn input; after WhenIdle reread ask key 2 (B2). Revoke the last pin; after
empty-set reread ask key 3 (BASE). Resume that same conversation, ask unused key 4 (BASE), then
after each of two distinct genuine native compact boundaries and covered rereads ask keys 5/6
(BASE). Unused keys prevent a previous answer from satisfying the later assertion. Keep a
parallel B agent's unrelated canary absent from A's challenges/context. Do not accept an
assistant's claim to have read a file without transcript tool/read evidence and a changed answer.

For real compaction use the installed provider's supported operation, not a forged transcript
boundary: Claude manual compact after adequate fixture conversation; Codex native compact
with its emitted boundary verified; Grok's calibrated native automatic-compaction workload and
config from `GrokRulesCompactionAcceptanceTests`. Deterministic V-23/V-24 already cover replay
using captured events. Use the existing bounded Grok workload/ceiling rather than inventing a
cheaper false acceptance. A provider that cannot produce the required native boundaries within
the ceiling is pending/failed acceptance, not a green fake-provider substitution. Keep the actual
provider home/auth prerequisites and CLI version in the evidence manifest. No real-provider
probe, model spend, production pin capture or shared-stack restart is performed in TestDesign.

### Guards the regression

| ID | Future regression | Caught by / decisive assertion |
|---|---|---|
| R-1 | Restore Dedicated-only/API-only files or claim ready without writing | V-6/V-13: first-use real bytes in each workspace; mandatory failure and zero launch attempts on I/O refusal. |
| R-2 | Root/short/name-derived paths, last-writer alias or cwd identity | V-7/V-19: colliding short IDs, recreated names and same-cwd sessions retain disjoint full-ID files and exact pointers. |
| R-3 | Treat unique files as sufficient shared native-import isolation | V-8/V-9/V-29: no foreign canary in installed Claude request; refuse admission/publication before unsafe dangling target resolves. |
| R-4 | Adopt/rewrite unmarked CLAUDE, strip user imports or guess through edits | V-10/V-11/V-12: full original byte arrays and concurrent author edits survive, only recorded append bytes can be removed. |
| R-5 | Trust forged markers, unsafe paths, tracked targets or stale writes | V-13/V-14: foreign bytes preserved, latest desired file only, durable ownership intent distinguishes crash recovery from adoption. |
| R-6 | Lose old-location/delete cleanup or erase sibling runtime state | V-15/V-16: live old location stays current, cleanup survives cascade, stale revoke refuses launch, all sentinels survive. |
| R-7 | Omit a named launch path, inject delegates or parse pin syntax | V-4/V-5/V-28: actual request/file identity, pool exclusion, literal canary not imported, budgets and operator append retained. |
| R-8 | Lose change/revoke/compact/resume intent in crash gaps | V-18/V-20/V-23: fresh service replay recovers one current obligation, empty sets are work and attempted bytes never change. |
| R-9 | Mark receipt from enqueue/screen, redirect by cwd or restart to refresh | V-19/V-21/V-22: only owning UserPrompt stamps receipt, location-only changes notify, pin-only deltas cause zero process control. |
| R-10 | Restore historical Grok pins or bypass its queue barrier | V-24/V-30: one combined rules recovery, empty current set remains authoritative after resume/two compactions. |
| R-11 | Route internal replies, forge caller authority or expose raw text in events | V-3/V-25: HTTP principal matrix refuses, typed internal turns send/settle nothing, canary-free events/attention. |
| R-12 | UI hides mandatory repair or falsely claims compliance | V-26/V-27: separate persistence/file/import/queue/transcript status, drafts preserved, actual failed file remains actionable. |

### Positive controls

Code runs each control **green baseline -> one broken guard -> expected assertion red -> revert
only that edit -> same assertion green**. Each a/b/etc. arm is a separate mutant/run. Use an
uncommitted one-line production edit at the named seam; do not weaken the test or change its
expected value. New seam names below describe the implementation point from S1-S7, not
existing symbols to pretend already exist. Record the actual file:line and exact edit in the
evidence ledger when Code supplies those symbols. Keep every gate deterministic and reachable;
an injected dependency error, compilation error, zero tests, timeout or fixture setup failure is
not a red control. If a defense in depth masks the mutant, add a direct production-path case
that exercises the weakened guard; do not remove a second guard to manufacture a failure.

| ID | One-line production mutation (each listed arm separately) | Expected red assertion |
|---|---|---|
| PC-1 | Gate projection on `pinClaudeImportMode == Dedicated`; separately return projection-ready without calling file publication. | V-6 shared/Unverified/Disabled actual file missing despite saved pins/injection. |
| PC-2 | Change directory component from full GUID to its first 8 hex chars; separately substitute display name/slug or root `antiphon.md`. | V-7 exact derived targets differ or A/B collide; rename/recreated-name identity fails. |
| PC-3 | Publish/update a fixed root alias to the latest writer instead of each agent target. | V-6/V-7 root decoy changes or A/B's required files/pointers are absent. |
| PC-4 | Resolve owning agent/session via cwd match instead of persisted AgentId; separately backfill from the card's current assignment. | V-7/V-19 owning snapshot/pointer or legacy-ownership refusal fails. |
| PC-5 | Resolve projection cwd from `Agent.WorkingDirectory` for a named-card launch; separately discard execution-host identity. | V-4/V-7 actual card-cwd/remote-host file absent and launch target wrong. |
| PC-6 | Suppress location-change obligation when content hash is unchanged. | V-15/V-19 new generation/path reread absent despite equal full hash. |
| PC-7 | Allow pin-bearing new/resumed launch when projection is unavailable; separately ignore latest-revision recheck before launch. | V-13 observes forbidden launch attempt; V-4 blocked writer race launches obsolete snapshot. |
| PC-8 | Skip stale-auto-import refusal when neutralization fails. | V-16 returns success/starts instead of `pin_projection_stale`. |
| PC-9 | Omit canonical ancestor/descendant overlap from import admission; separately omit stopped agents or live task workspaces. | V-8 each corresponding overlap retains/installs a foreign discoverable import or admits forbidden launch. |
| PC-10 | Remove the canonical scope admission/install lock. | V-8 two contenders pass the gated admission check and retain conflicting exclusive imports. |
| PC-11 | Ignore failure removing owned import before shared admission. | V-8 B starts despite `pin_import_scope_conflict` and preserved A import. |
| PC-12 | Permit import installation in shared/Unverified scopes. | V-8 shared CLAUDE byte equality fails; V-29 installed Claude normal A/B request contains the foreign canary. Also run the isolated discovery sensitivity arm with both exact imports deliberately seeded by the fixture. |
| PC-13 | Check existing unsafe imports only after publishing the projection; separately skip ancestor user-import scan. | V-9 pre-publication observer sees nonempty canary under dangling target; V-29 refused-launch/publication assertion fails. |
| PC-14 | Accept a user-owned shared import as native-ready instead of conflict/tombstone. | V-9 private content remains/restores before user import repair. |
| PC-15 | Skip eligible stanza installation. | V-10 one exact stanza absent; V-28 native-only model request lacks file-only canary. |
| PC-16 | Route unmarked CLAUDE through whole-floor rendering/adoption. | V-10/V-11 original bytes/LeftAlone result/absence of managed marker fail. |
| PC-17 | Write CLAUDE via normalized UTF-8 text instead of original bytes plus encoded append. | V-10 BOM/CRLF/non-ASCII/no-final-newline round trip differs. |
| PC-18 | Omit tracked separator from cleanup; separately remove extra preceding newline bytes. | V-10 no-final-newline or multiple-final-newlines fails exact original-byte restoration. |
| PC-19 | Remove append compare-before-write; separately remove cleanup comparison. | V-12 barrier-injected author edit is lost or modified instead of conflict. |
| PC-20 | Decode unsupported/invalid bytes with replacement instead of refusal. | V-12 unsupported corpus is mutated or falsely reports installed import. |
| PC-21 | Treat an equivalent user-authored import as Antiphon-owned. | V-11 cleanup deletes the user's direct import. |
| PC-22 | Treat a fenced/inline-code example as an active import; separately recognize wrong-ID/root target as equivalent. | V-11 legitimate eligible stanza is absent or wrong target accepted; V-28 fence example arm lacks native canary. |
| PC-23 | Accept malformed/unrecorded legacy stanza as owned; separately drop own import from managed-floor regeneration. | V-11 foreign stanza changes without ownership, or managed refresh loses its verified import. |
| PC-24 | Remove full-AgentId/recorded-ownership check on replacement; separately recover any plausible marker after crash. | V-13/V-14 foreign/forged file changes or is advertised as trusted instead of conflict. |
| PC-25 | Remove tracked-target refusal. | V-13 tracked file bytes change even when ignored. |
| PC-26 | Skip containment or reparse validation (separate mutations for each); separately follow CLAUDE symlink. | V-13 external/referent canary touched or trusted target incorrectly returned; V-12 CLAUDE referent changed. |
| PC-27 | Write new bytes straight to final path instead of same-directory atomic publish. | V-14 paused write exposes partial/unpublished target bytes. |
| PC-28 | Skip final latest-revision guard; separately skip current-byte check in projection overwrite/cleanup. | V-14 old writer wins, current repin is deleted, or concurrent author bytes are overwritten. |
| PC-29 | Delete projection on cwd change without checking active consumers; separately omit retained cleanup intent before cascade. | V-15 old live-session file disappears/stops updating, or fresh service cannot recover delete cleanup. |
| PC-30 | Cleanup `.antiphon/pins` recursively rather than verified owning leaf. | V-15 B/C/sibling sentinel bytes disappear (fixture root only). |
| PC-31 | Overwrite `info/exclude` instead of appending under comparison/lock; separately use cwd-relative rule for repo-subdir cwd. | V-17 unrelated bytes/one racing agent rule lost, or real `git check-ignore` misses target. |
| PC-32 | Omit pin segment at named composition seam; separately allow named-pin helper in pool/task-role path. | V-4 real launch payload lacks pin or delegate unexpectedly inherits it. |
| PC-33 | Render pin block without protective fence; separately expand templates after inserting pins. | V-5 literal preservation fails; V-28 payload file canary leaks into native request. |
| PC-34 | Bypass complete provider launch-budget recheck; separately bypass existing secret-placeholder tripwire for pins. | V-5 boundary/placeholder input reaches launch instead of explicit refusal. |
| PC-35 | Omit dirty reconciliation intent from mutation transaction; separately omit intended publication identity persistence. | V-1/V-18 restart loses file/notification work; V-14 post-create recovery cannot distinguish/recover intended bytes. |
| PC-36 | Omit change trigger; separately return early when active pin count becomes zero. | V-18/V-20 replacement or last-revoke note absent; V-23 empty-set recovery stale. |
| PC-37 | Pass `deliverIfIdle:true` at pin enqueue. | V-18 idle adapter receives input before intent linkage/owned queue processing. |
| PC-38 | Omit per-session PinRefreshKey idempotency; separately set attempted row eligible for coalescing. | V-18 crash/replay duplicates row; V-20 attempted bytes change or newer obligation is lost. |
| PC-39 | Use Now/raw input instead of WhenIdle for pin notes. | V-20 working session sees input/Enter before TurnEnd. |
| PC-40 | Generate a fresh refresh key when parked/canceled retry is exhausted; separately retype transcript-confirmed note with no assistant reply. | V-20 bounded-failure row/key/attempt count or no-retype assertion fails. |
| PC-41 | Stamp receipt from enqueue/screen; separately drop owning-session or full-hash/location match from transcript receipt. | V-21 nonmatching evidence incorrectly advances receipt; immutable launch evidence must remain unchanged. |
| PC-42 | Clear mandatory projection error after successful API receipt. | V-16/V-21/V-26 repaired status is claimed while actual file is failed/stale. |
| PC-43 | Include dynamic pin stamp/owned file/managed import marker in restart hash (separate arms). | V-22 first install/change/remove invokes forbidden process-control call under Auto/Relaunch. |
| PC-44 | Exclude every `antiphon.md`/`.antiphon` file or all stanza-looking text from policy drift. | V-22 unrelated authored/foreign-file drift incorrectly disappears. |
| PC-45 | Gate pin compaction on SystemPromptAppend/AlwaysOn; separately save old watermark and skip unserved-trigger replay. | V-23 plain agent or post-crash catch-up has no durable covered reread. |
| PC-46 | Skip resume/compaction obligation when hash equals last delivered. | V-23 new generation/two distinct compact boundaries lack their own read coverage. |
| PC-47 | Treat historical Grok embedded pin snapshot as current on rules reread; separately omit current-pin obligation from combined rules refresh. | V-24 actual combined prompt/current empty snapshot assertion fails; V-30 post-revoke unused challenge returns historical answer. |
| PC-48 | Exempt pin-refresh rows from Grok rules barrier; separately treat rules ACK alone as pin receipt. | V-24 input passes closed barrier or pin receipt advances without owning pin evidence. |
| PC-49 | Remove persisted pin-message internal-turn recognition from channel, task-settlement or boot-evidence lane (each separately). | V-25 outbound send/attachment, task settlement or boot evidence appears for an internal row. |
| PC-50 | Suppress routing by header text instead of persisted message identity. | V-25 real human lookalike prompt loses its normal response. |
| PC-51 | On invalid token allow headerless fallback; separately drop live named-owner check or operator-source mutation guard. | V-3 invalid/wrong/dead/task/capability caller or agent editing operator pin is accepted. |
| PC-52 | Bypass expected revision serialization; separately omit RequestId fingerprint comparison or active-source uniqueness. | V-1/V-2 conflicting writes both succeed, replay changes meaning or duplicate active source appears. |
| PC-53 | Drop cap/length/control-character validation (each separately). | V-2 forbidden boundary/control payload persists or exceeds active capacity instead of rejection. |
| PC-54 | Include raw text/source ref in ordinary changed event/attention item. | V-25 captured event/export contains the synthetic canary. |
| PC-55 | Mark UI file ready from queued/API success; separately clear drafts on invalidation/conflict. | V-26 missing-file repair disappears or user's draft is lost. |
| PC-56 | Emit unconditional file/API tool-use instructions for a deny-all-tools seat. | V-4 tool-disabled protocol/request contains a forbidden read recipe or claims live read support. |

Controls that deliberately expose synthetic pins or alter filesystem cleanup run only under
fixture-owned temporary roots and isolated providers. Never mutate shared production workspace
files, weaken `ProductionRunnerGuard`, redirect to 17204 or change authentication for a PC.

### Out of scope

- ACL/confidentiality isolation from another process with the same filesystem permissions,
  headerless-host API hardening and unregistered external launches. D3 GUID paths prevent
  collision/automatic misdelivery, not deliberate reads; D4 Dedicated includes an operator
  assertion whose truth cannot be established by this repository's tests.
- Foreign KB discovery/backfill and the original production preference capture. V-1/V-27 use
  an arbitrary synthetic source key and explicit capture; rollout step 4 needs the actual
  originating deployment and its authorization. Deployment is not capture evidence.
- New transports, universal native imports, pin inheritance to delegates, actual unsupported
  Raw/OpenCode compliance and changing CARD-0250 attachment behavior. Their exclusions and
  compatibility are tested; their features are not added here.
- Proving all future model behavior, retracting already-performed actions, or treating a model
  answer/queue acknowledgement as filesystem security. V-30 proves only its recorded challenges.
- Broad nightly, unrelated live Grok acceptance and live-stack deployment/restart. This section
  forces targeted tests and reports provider prerequisites; it does not turn an unavailable
  real-provider phase into a passed deterministic substitute.

### Cost

Suites forced: new pin classes in `Antiphon.Tests`, named existing regression classes below,
three scoped client files, one isolated E2E class, installed-Claude stub canary and separate
provider behavior acceptance. No full ~25-minute `Antiphon.Tests` sweep is required merely for
this card. Verification estimates are planning estimates, not measured run times: deterministic
baseline/regressions/client/E2E about 25-45 minutes after build, installed-Claude stub about
10-20 minutes, PC runs about 90-180 minutes depending on rebuild cost. Real behavior acceptance
is additional (Claude/Codex roughly 15-30 minutes each; Grok may use its existing 120-minute
ceiling). Do not report a five-minute verification floor for S1-S7.

Run from the implementation checkout in PowerShell. Keep a single isolated output name with
trailing forward slash; every native invocation checks its actual exit code. Commands below
name **future** tests to implement. TestDesign did not run them. A newly added file or renamed
test requires updating this list and the ID coverage manifest, never quietly omitting coverage.

```powershell
$pinClasses = @(
    'AgentPinnedInstructionServiceTests', 'AgentPinnedInstructionEndpointTests',
    'AgentPinnedInstructionCompositionTests', 'AgentPinWorkspaceTests',
    'AgentPinRefreshTests', 'AgentPinPolicyTests', 'AgentPinRecoveryTests',
    'AgentPinInternalTurnTests'
)
$pinFilter = '/*/*/' + (($pinClasses | ForEach-Object { '(' + $_ + '*)' }) -join '|') + '/*'
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0262/ -- --treenode-filter $pinFilter --report-trx --report-trx-filename card0262-pins.trx
if ($LASTEXITCODE -ne 0) { throw 'CARD-0262 pin suite failed' }

$regressionClasses = @(
    'AgentWorkspaceProvisionerTests', 'InstructionBundleTests',
    'AgentSystemPromptLaunchTests', 'NamedCodexAgentLaunchTests',
    'GrokRulesCompositionTests', 'DelegateBundleLaunchTests',
    'SessionMessageQueueServiceTests', 'PolicyRefreshServiceTests',
    'InstructionFileStampTests', 'CompactionRecoveryTests',
    'GrokRulesCompactionRecoveryTests', 'GrokRulesQueueBarrierTests',
    'GrokRulesTransportCompatibilityTests', 'ChannelMachineTurnTextTests',
    'ChannelMachineTurnMatchTests'
)
$regressionFilter = '/*/*/' + (($regressionClasses | ForEach-Object { '(' + $_ + '*)' }) -join '|') + '/*'
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0262/ -- --treenode-filter $regressionFilter --report-trx --report-trx-filename card0262-regressions.trx
if ($LASTEXITCODE -ne 0) { throw 'CARD-0262 regression suite failed' }

pwsh -NoProfile -File scripts/test-client.ps1 AgentPinnedInstructions.test AgentBundleAttachments.test useSignalRInvalidation.test
if ($LASTEXITCODE -ne 0) { throw 'CARD-0262 client suite failed' }
Push-Location client
try {
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Client build failed' }
} finally { Pop-Location }
dotnet run --project tests/Antiphon.E2E --property:OutputPath=bin-card0262/ -- --treenode-filter '/*/*/AgentPinnedInstructionsE2ETests/*' --report-trx --report-trx-filename card0262-e2e.trx
if ($LASTEXITCODE -ne 0) { throw 'CARD-0262 isolated E2E failed' }

$priorPinStubFlag = $env:ANTIPHON_REAL_CLI_STUB_TESTS
try {
    $env:ANTIPHON_REAL_CLI_STUB_TESTS = '1'
    dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0262/ -- --treenode-filter '/*/*/AgentPinClaudeImportCanaryTests/*' --report-trx --report-trx-filename card0262-claude-import.trx
    if ($LASTEXITCODE -ne 0) { throw 'Installed Claude pin import canary failed' }
} finally { $env:ANTIPHON_REAL_CLI_STUB_TESTS = $priorPinStubFlag }
```

Run each PC's V filter separately on the compiled mutant (omit `--no-build`). Example command,
repeated with unique `pcNN-arm-red`/`pcNN-arm-restored` TRX names and its mapped class/method:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0262/ -- --treenode-filter '/*/*/AgentPinWorkspaceTests/V07_*' --report-trx --report-trx-filename card0262-pc02-short-red.trx
# Nonzero is expected only when the mapped assertion actually failed in this fresh TRX.
```

For V-30 add `[Explicit]`, `[NotInParallel("Headed")]`, the process limiter and a card-specific
`ANTIPHON_PIN_BEHAVIOR_TESTS=1` opt-in to the new class. Select individual methods
`V30_Claude_*`, `V30_Codex_*`, `V30_Grok_*`. Set that flag plus the existing respective gates:
Claude `ANTIPHON_HEADED_TESTS=1`; Codex `ANTIPHON_CODEX_HEADED_TESTS=1` and its dedicated
authenticated test home; Grok `ANTIPHON_HEADED_TESTS=1`, `ANTIPHON_GROK_RULES_LIVE_TESTS=1`,
`ANTIPHON_GROK_RULES_ENDURANCE_TESTS=1` and its established test auth path. Save/restore env
values around the scoped run as above, never change provider routing or copy auth to bypass a
skip/refusal. Invoke sequentially with:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0262/ -- --treenode-filter '/*/*/AgentPinBehaviorAcceptanceTests/V30_Claude_*' --report-trx --report-trx-filename card0262-behavior-claude.trx
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0262/ -- --treenode-filter '/*/*/AgentPinBehaviorAcceptanceTests/V30_Codex_*' --report-trx --report-trx-filename card0262-behavior-codex.trx
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0262/ -- --treenode-filter '/*/*/AgentPinBehaviorAcceptanceTests/V30_Grok_*' --report-trx --report-trx-filename card0262-behavior-grok.trx
```

Capture each command's exit/verdict before issuing the next. No `Antiphon.Agents.Pty.Tests`
run is forced by this server-side design alone; if Code changes a Pty component, run its named
touched classes **after** Antiphon.Tests, using the same isolated-output convention. Likewise,
runner changes force the named affected `Antiphon.SessionRunner.Tests` classes. Do not launch
all headed/provider tests as a substitute for these scoped canaries.

Build's deliverable includes a compact evidence manifest: implementation SHA; every V/data row
mapped to actual test method and fresh TRX outcome; every PC arm's mutation file/line, baseline,
expected red assertion and restored-green result; CLI executable/version and context/transcript
artifact paths; saved/projected/queued/receipt/behavior columns; explicit pending provider/reparse
coverage and reasons. Check fresh TRX has nonzero execution and each expected class/method/data
row, with no accidental extra classes from wildcard suffixes. `--list-tests`, a skipped explicit
class or exit zero alone is not evidence. Keep only synthetic pin content in retained fixtures.

TestDesign validation is limited to append-only plan diff, case/PC references, named existing
fixture paths and command conventions. No application behavior, tests, builds, CLI probes or
live deployment were executed in this stage. Code implements this section and runs the controls;
Review assesses its evidence before rollout/closure under the completion criteria above.
