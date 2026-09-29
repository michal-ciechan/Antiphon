# CARD-0822: live orchestrator instructions file generated from settings

Date: 2026-09-29. Stage: Plan, with TestDesign folded in. Next: Code.
Baseline: `d2fb7536eb6385822b5d306fc49157effa6de35c`.
Card `5a5bce6d-468f-4271-89f3-47fb7602c900` on board `8988ca03-7414-47ad-b0b6-51556c701703`,
read in full with `scripts/card.ps1 get CARD-0822`. Related cards read: CARD-0439 (orchestrator
bundle should surface the transcript chain), CARD-0509 (orchestrator restart loop after context
overflow), CARD-0317 (keep-warm cadence), CARD-0818 (the "Code held at two" example the card cites).

## Outcome and scope

The server generates one Markdown file, `ANTIPHON_ORCHESTRATOR_INSTRUCTIONS.md`, from live
settings; every orchestrator launch is handed its path in the environment; a Claude orchestrator
reads it automatically at session start, on resume and after every compaction through the existing
SessionStart hook; and whenever a covered setting changes the server regenerates the file and
queues one short `settings changed` note to every running orchestrator, naming the diff and the
path. Delegates (Worker tasks) are never notified: they take their settings from their brief.

In scope: the settings model, renderer, change detection, file write, HTTP read route, notice
delivery, launch env, hook and workspace-script changes, bundle and doc wording, the doc-pin tests
that wording touches, and the tests proving a settings change yields both a file update and a
delivered notice. Out of scope: making `Delegation` settings hot-reloadable (they stay
`IOptions`, restart to change; the startup regeneration covers that), a settings UI, any change to
how Workers receive settings, and any change to the CARD-0334 relaunch lane.

## Ground truth

| Card assumption | What the code does at the baseline | Consequence for this plan |
|---|---|---|
| Orchestrators learn operating settings from a static prompt bundle. | `server/Bundles/orchestrator.md` hard-codes "Code and Review at two, and one task in each other pipeline stage" and "Keep the Code stage at a depth of two". `DelegationSettings.RolePolicy[*].RecommendedInFlight` (Code 2, Review 2, others 1) is what `GET /api/agent-tasks/pipeline` reports and what `DelegationOpenGate` refuses on (409 `concurrency_limit`, axis `role`). Four copies pin the phrase "depth of two" (`StandingPipelinePolicyDocumentationTests.Phrases`). | The bundle keeps the rule and loses the numbers; the numbers live in the generated file. The pinned phrase changes in all four copies and in the test (D-8). |
| A running orchestrator never finds out when a setting changes. | CARD-0334 `PolicyRefreshService` detects drift of bundles and of `PolicyRefreshSettings.InstructionFiles` under the agent's cwd, for standing agents only, once the session has been idle 2 min and outside a 30 min cooldown; it relaunches (kill + `--resume`) or queues a WhenIdle System note (`ChannelPreamble.PolicyDriftNotifyBody`). Nothing watches settings rows. Sub-orchestrator task sessions are not in its population. | Do not put the generated file under any cwd or in `InstructionFiles`: a regeneration must not become a relaunch. Build a separate, lighter notice lane that also reaches task-bound orchestrators (D-3, D-4). |
| Pipeline queue settings: per-role recommended in-flight, `maxConcurrentTasks`, `MaxOpenTasks`. | All three are `DelegationSettings` (`IOptions`, bound at startup, no `IOptionsMonitor`). `MaxConcurrentTasks` is the desktop cap and can be overridden live by a `HostBudget` row for host `local` (`HostBudgetService.UpsertAsync`, `AgentIncidentKind.HostBudgetChanged`). | Section 1 of the file renders the role table and both caps; the effective local budget comes from `HostBudgetService`, not the raw setting. A config edit reaches the file at the next server start (D-3). |
| Routing pins and held providers. | `RoutingPin` (card or stage-wide grain, ordered candidates, forbidden aliases, `NotBefore`/`NotAfter`, lazy expiry) written by `RoutingPinService.UpsertAsync/ClearAsync`; `ModelAvailabilityHold` (kind, alias or `*`, `DisabledUntil`, Manual or AutoDetected) written by `ModelAvailability.UpsertManualAsync/ClearAsync/UpsertAutoDetectedAsync/SweepExpiredAsync`. Neither raises an incident or event today. | Both writers gain a change signal after their save (D-3). Auto-detected holds and expiry sweeps count as changes: an orchestrator that keeps dispatching Fable under a session-limit hold is exactly the stale fact the card names. |
| Runner defaults and the phone-home runners with capacity, platform and features. | `RunnerRoutingSettings` singleton with revisions; `PutAsync` publishes `RunnerDefaultsChanged` on `IEventBus` (UI only). `SessionRunnerCatalogue.ListAsync` gives id, platform, `DispatchEligible`, capacity, occupancy, features, draining, `AcceptingNewWork`. `PhoneHomeRunnerDirectory.Register` and disconnects call `IRunnerEligibilityObserver.Changed(runnerId)` (CARD-0726), which `Program.cs` wires to `AlarmWakeQueue`. Live read 2026-09-29: defaults revision 2, global `server2` (Human); desktop windows cap 2, server2 linux cap 10. | Sections 2 and 3 render these. Occupancy is deliberately not rendered (it changes on every dispatch and would make the file churn). Runner changes reach the generator through a composite eligibility observer plus explicit signals from capacity, drain and retire paths (D-3). |
| Model-level aliases. | `ModelLevelAliases` is code (`fable`/`opus`/`sonnet`/`haiku`, Codex full slugs, Grok `grok-4.7`); `RolePolicy` also carries `Level`, `EscalateTo` and optional `Kind` per role; `MinOrchestratorLevel` is a setting. | Section 6 renders the level ladder per kind and the per-role level; a code change reaches the file at the next start. |
| Operator standing instructions. | The only standing-instruction store is `AgentPinnedInstruction` (CARD-0262): per agent, at most 20 active rows of at most 500 chars, already projected into that agent's own workspace. There is no fleet-wide operator instruction store. | A global file must not repeat one agent's pins to every orchestrator. Section 7 renders a new fleet-wide config list (D-2) and points at the per-agent projection for the rest. |
| Read on start, after compaction, on new orchestrator launch. | `.claude/settings.json` and `scripts/orchestrator-workspace.ps1 Write-ClaudeSettings` install `scripts/hooks/orchestrator-investigation-hook.mjs` on `SessionStart` with matcher `compact` only; the wrapper arms on `ANTIPHON_TASK_KIND=Orchestrator`, on no `ANTIPHON_TASK_ID` with `ANTIPHON_ORCHESTRATOR` not `0`, and returns `additionalContext` (fail-open, 5 s timeout). Compaction is also observed server-side by `CompactionRecoveryService`, which queues `RecoveryNoteBody` only for agents with a `SystemPromptAppend`. Orchestrator tasks are ClaudeCode only (`AgentTaskService` refuses other kinds); a standing orchestrator may be Grok or Codex. | The hook grows a `startup|resume|compact` matcher and injects the file as `additionalContext` (D-5). The server-side compaction note carries the re-read only for non-Claude orchestrator sessions, so a Claude session is not told twice. |
| Delivery to orchestrators: session message, WhenIdle vs Now, attribution. | `SessionMessageQueueService.EnqueueAsync` with `MessageSendMode.WhenIdle` and `QueuedMessageOrigin.System` delivers one body per turn at a turn end, verified by transcript `UserPrompt`; `Now` creates no row and types into a possibly busy composer. CARD-0714 report attribution treats a newer real prompt without the task marker as a barrier for its own response only; `[check …]` notes already reach sub-orchestrator callers WhenIdle. | WhenIdle System notes to orchestrator sessions only, never Now, never to Workers (D-4). |
| Orchestrators run in worktrees and other repos. | A sub-orchestrator is a pool delegate in a Worktree (default `WorkspaceMode.Worktree`) and may be placed on a phone-home runner when it is ClaudeCode (`DefaultRunnerRoutingPolicy`); a standing orchestrator runs in a sibling `<checkout>-orchestrator` workspace (CARD-0251); `-Dir` sends work to another repo. `docs/cards/` is the precedent for generated files, written into the project checkout. | One global file outside every checkout, plus the HTTP route for a session whose host cannot see the desktop path (D-1). |
| No secrets may be written. | `docs/agent-credentials.md`: four secret stores; `{{key:NAME}}` legal in env values only; `ApiKeyPlaceholder.EnsureAbsent` refuses rather than strips; runner defaults store no secrets; operator token, phone-home shared secret and OAuth stores are files the server never renders. | The snapshot builder reads none of those stores or columns, and the renderer refuses secret-shaped output (D-7). |

## Decisions

### D-1: one global generated file in Antiphon's data root, path in the launch environment, mirrored by an HTTP route

Default path: `%LOCALAPPDATA%\Antiphon\orchestrator\ANTIPHON_ORCHESTRATOR_INSTRUCTIONS.md` on
Windows; `$XDG_DATA_HOME/antiphon/orchestrator/ANTIPHON_ORCHESTRATOR_INSTRUCTIONS.md`, else
`~/.local/share/antiphon/orchestrator/...`, on Linux and macOS. That is the same root the key ring
and the operator token already use; factor the root resolution out of `AgentTuiSettings` into a
small shared `AntiphonDataPaths` helper rather than copying it. Override with
`Delegation:OrchestratorInstructions:Path`, absolute only, refused at startup otherwise.

Every covered setting is fleet-wide (delegation caps, runner defaults, runners, holds, host
budgets, model levels). The two per-scope facts are rendered inside that one file: routing pins
grouped by board, and the note that open-task caps count per project. Scope is therefore global.

The path reaches an orchestrator as `ANTIPHON_ORCHESTRATOR_INSTRUCTIONS=<absolute path>`:
set in `AgentSessionLaunchComposer.ComposeForAgentAsync` next to `ANTIPHON_ORCHESTRATOR=1`
(standing agents carrying the `orchestrator` bundle) and in `AgentTaskDispatcher.BuildEnv` when
`task.Kind == AgentTaskKind.Orchestrator`. Workers get neither. `GET /api/orchestrator-instructions`
returns the same body as `text/markdown` with an `ETag` equal to the version and an
`X-Antiphon-Instructions-Version` header; `ANTIPHON_ORCHESTRATOR_INSTRUCTIONS_URL` carries that
absolute URL so a sub-orchestrator placed on server2 reads the route when the desktop path does not
exist. `POST /api/orchestrator-instructions/refresh` forces a regeneration and requires the
`X-Antiphon-Operator-Token` header like the other operator routes.

Rejected: a per-project file in the checkout or sibling workspace (N writers, git residue in
worktrees under the CARD-0459 sweeps, and a Shared task's commit-on-settle footprint could commit
it); projecting it per agent through the CARD-0262 pin projection (per-agent by design, and a pool
sub-orchestrator has no cwd until dispatch); a bundle (bundles are static repo files whose version is
the content hash, composed into `--append-system-prompt` at launch; live data there would re-version
the bundle on every change and is exactly the "wrong tomorrow" content `server/Bundles/README.md`
forbids); an `@` import from `CLAUDE.md` (would put a 16 KiB live file into every turn's system
prompt and break the one-source-of-truth contract in `docs/agent-instruction-file-contract.md`).

### D-2: a typed snapshot, a pure renderer, a content hash, a byte cap, and a generated-file header

`OrchestratorInstructionsSnapshotBuilder` (scoped; reads `AppDbContext`, `HostBudgetService`,
`RunnerDefaultSettingsService`, `ModelAvailability`, `RoutingPinService`, `SessionRunnerCatalogue`,
`DelegationSettings`) produces an `OrchestratorInstructionsSnapshot` record: plain data, JSON
serializable, no entities. `OrchestratorInstructionsRenderer.Render(snapshot)` is a pure static
function producing deterministic Markdown. Version = first 8 hex of SHA-256 over the LF-normalised,
trimmed body (`InstructionFileStamps.HashOf`), excluding the stamp line, so the stamp line can carry
the revision without changing the hash.

The file opens with a stamp line and a fixed header (pinned by test):

```text
[orchestrator-instructions v1a2b3c4d rev 17]
Generated by Antiphon from live settings and overwritten on every covered change. Do not edit this
file; change the setting each section names. Live copy: GET /api/orchestrator-instructions.
```

Sections, in order, each naming the route that owns the setting:

1. Pipeline caps: `MaxConcurrentTasks` with the effective local budget from `HostBudgetService`,
   `MaxOpenTasks` (per project scope), `DefaultWorkerWorkspace`, `MinOrchestratorLevel`, and a
   role table of `RecommendedInFlight`, `Level`, `EscalateTo`, `Kind`. One sentence states that
   create refuses at the recommended number unless `-IgnoreConcurrencyLimit` and that live counts
   are `GET /api/agent-tasks/pipeline`.
2. Hosts and runners: one row per catalogue entry with platform, dispatch-eligible, declared
   capacity, effective budget and source, draining or retired, features. Never occupancy,
   never observation timestamps.
3. Runner defaults: revision, provenance, global runner, per-kind runners, last reason.
4. Held models: active holds with kind, alias, source, until, reason; or `none`.
5. Routing pins: stage-wide pins, then card pins grouped by board, with role, candidates,
   forbidden aliases, strength, provenance, not-before, not-after, reason.
6. Model levels: `ModelLevelAliases` per kind per level, plus the reminder that tier names are
   never `-Kind` values.
7. Operator standing instructions: the lines of the new
   `Delegation:OrchestratorInstructions:StandingInstructions` (string list, at most 10 lines of
   300 chars, validated at startup), followed by one fixed sentence that per-agent pinned
   instructions (CARD-0262) are projected into the agent's own workspace and are not repeated here.

Byte cap: 16 384 UTF-8 bytes, `Delegation:OrchestratorInstructions:MaxBytes`. When a render
exceeds it the renderer truncates in a fixed order until it fits: card pins (newest 20 per board,
then fewer), holds (newest 20), standing-instruction lines, then the features column; each cut
adds a line `… N more: GET <route>`. Nothing else is cut; the cap must never drop section 1.

Rejected: rendering occupancy or in-flight counts (churn on every dispatch); a JSON file (an
orchestrator reads Markdown; the HTTP route can grow a JSON variant later); per-agent pins in the
global file (leaks one agent's instructions to every orchestrator).

### D-3: change detection is a signal from every covered writer, coalesced into one reconcile, with a one-minute sweep as backstop

`OrchestratorInstructionsService` (singleton) owns `Signal(string reason)`,
`ReconcileNowAsync(string reason, CancellationToken)` and `WhenIdleAsync()`. `Signal` records the
reason and schedules one coalesced background reconcile (the `RemoteWorkspacePreparer` shape:
a single worker, later signals fold into the pending run). A reconcile builds the snapshot,
renders, and compares the version with `OrchestratorInstructionsState.Version` (new singleton row,
`Id = "fleet"`: `Revision`, `Version`, `Body`, `SnapshotJson`, `PreviousSnapshotJson`, `WrittenAt`,
`WrittenPath`, `LastReason`, `LastWriteError`). Unchanged: no write, no notice, no incident.
Changed: write the file atomically (temp file in the same directory, then move), save the row
with `Revision + 1`, record `AgentIncidentKind.OrchestratorInstructionsRegenerated` (Info, no
alert, no agent; the message is the reason and the delta), then hand the two snapshots to the
notice lane (D-4). A write failure keeps the previous row, records
`OrchestratorInstructionsWriteFailed` (Warning, alert) and sends no notice; the HTTP route keeps
serving the last good body from the row.

Signals, placed after the writer's own `SaveChangesAsync` succeeds:

| Writer | Signal reason |
|---|---|
| `RunnerDefaultSettingsService.PutAsync` (only when not `Same`) | `runner-defaults rev N` |
| `HostBudgetService.UpsertAsync` | `host-budget <host>` |
| `ModelAvailability.UpsertManualAsync`, `ClearAsync`, `UpsertAutoDetectedAsync`, `SweepExpiredAsync` when it cleared anything | `hold <kind>/<alias>` |
| `RoutingPinService.UpsertAsync`, `ClearAsync`, and its lazy expiry when it persists a `ClearedAt` | `routing-pin <role>` |
| `PhoneHomeRunnerDirectory` eligibility changes | via a `CompositeRunnerEligibilityObserver` registered in `Program.cs` that fans out to `AlarmWakeQueue` and to this service; `SetDeclaredCapacityAsync`, drain, drain-clear and retire add explicit signals where `Notify` is not already on their path (Code verifies each path against the source and lists which needed one) |
| Server start | `startup`, run once by `OrchestratorInstructionsHostedService` before its timer, so a `Delegation` config edit plus restart reaches the file |
| `OrchestratorInstructionsHostedService` timer | `sweep`, every `Delegation:OrchestratorInstructions:SweepSeconds` (default 60); a no-change sweep is a no-op by the hash comparison |
| `POST /api/orchestrator-instructions/refresh` | `operator` |

Rejected: `IOptionsMonitor<DelegationSettings>` (restart semantics are documented and relied on;
not this card); polling only (a one-minute lag would be acceptable but the tests must prove the
write path itself notifies, so the trigger is the contract and the sweep is the safety net);
watching the file (the server is its only writer); reusing `IEventBus` (it is a UI broadcast with
no server subscribers).

### D-4: notices are WhenIdle System notes to orchestrator sessions only, coalesced per session, never Now, never to Workers

Recipients per reconcile, computed by a pure `OrchestratorInstructionsRecipients.Select` over
loaded rows and covered by unit tests: standing agents that carry the `orchestrator` bundle
attachment, are not pool delegates, have a `PersistentSessionId` whose session is `Running` on the
`PtyHost` backend with `CardId == null`, and whose `PolicyRefreshMode` is not `Off`; plus every
session named by an `AgentTask` with `Kind == Orchestrator` in `Dispatched` or `Working`. Excluded
by construction: Worker task sessions, specialists, Herdr panes, card sessions. This is the union
`OrchestratorInvestigationSweepService` already uses, minus its behavioural arm.

Each recipient session carries a new column `AgentSession.OrchestratorInstructionsVersion`,
stamped at launch with the current version (next to `InstructionFileStamp`) and at every notice. A
session already at the new version is skipped. The note body is
`ChannelPreamble.OrchestratorInstructionsChangedBody(delta, path, url)`:

```text
[System note from Antiphon: orchestrator settings changed (v1a2b3c4d → v5e6f7a8b): caps: Code
recommended in-flight 2 → 5; runners: server2 dispatch-eligible yes → no. Re-read
ANTIPHON_ORCHESTRATOR_INSTRUCTIONS (<path>, or GET /api/orchestrator-instructions) before your next
dispatch; where the new values differ from what you told a delegate, steer it with -Refine. Reply
NO_REPLY unless you have something for the user.]
```

`OrchestratorInstructionsDelta.Format(before, after)` is a pure function over the two typed
snapshots, never over file text: one line per changed fact as `<section>: <key> <old> → <new>`,
`added` or `removed`, at most 6 lines then `+N more`, each line at most 120 chars, the whole body at
most 700 chars. When a session's stamped version is older than `PreviousSnapshotJson` (it missed an
intermediate change while busy) the delta is still current-versus-previous and the body says
`changed more than once since v<old>`.

Delivery: `SessionMessageQueueService.EnqueueAsync(session, body, MessageSendMode.WhenIdle,
origin: QueuedMessageOrigin.System, noteHeader: "[orchestrator-instructions]", deliverIfIdle: true)`.
System origin delivers one body per turn and never batches with a Delegation completion note, so
a settings change can never be read as part of a report. Before enqueueing, any `Pending` row on
that session with the same `NoteHeader` is canceled and replaced, so a burst of changes yields one
note per turn boundary; there is no idle floor and no cooldown. Each enqueue records
`OrchestratorInstructionsNotified` (Info, per agent and session).

Why not `Now`: it creates no queue row, types into a possibly busy composer, and on a sub-orchestrator
it would insert an unmarked prompt mid-turn, which is the CARD-0714 attribution hazard the card
names. Why not Workers: they do not dispatch, their settings arrive in the brief, and an extra turn
on a delegate is a report-attribution barrier and a cost. Why WhenIdle to task-bound orchestrators
too: `[check …]` notes already reach sub-orchestrator callers this way, the note carries no report
token or next-stage block, and a sub-orchestrator running a multi-hour chunk is the case the card
describes.

Defaults stated for the caller to veto: `Delegation:OrchestratorInstructions:Notify` is `All`
(standing and task-bound orchestrators); `Standing` and `Off` are the other values.

### D-5: reading points are the launch env, the bundle rule, the SessionStart hook, and a compaction note for non-Claude seats

`scripts/hooks/orchestrator-investigation-hook.mjs` handles `SessionStart` for sources
`startup`, `resume` and `compact` (matcher `startup|resume|compact` in `.claude/settings.json` and in
`Write-ClaudeSettings`). When armed it reads `process.env.ANTIPHON_ORCHESTRATOR_INSTRUCTIONS`; if the
file exists it emits the file text as `additionalContext`, truncated at 16 384 bytes with a marker
line, and on `compact` appends the existing `COMPACT_CONTEXT` after a blank line. Missing env, missing
file, unreadable file or an unarmed session stay silent and exit 0. No network call from the hook.
This makes "read at start, on resume and after compaction" automatic for every Claude orchestrator
(standing seats through the sibling workspace settings, sub-orchestrators through the repo's
`.claude/settings.json`) without spending a turn.

`CompactionRecoveryService.OnCompactBoundaryAsync`: for a recipient session (D-4 rule) whose
`AgentKind` is not `ClaudeCode`, queue `ChannelPreamble.OrchestratorInstructionsCompactionBody(path,
url)` WhenIdle System; a Claude session keeps its existing behaviour (the hook already injected the
file). Agents with a preamble keep `RecoveryNoteBody` unchanged.

`server/Bundles/orchestrator.md` gains one paragraph (at most 700 chars): the file's purpose, the
env var and route, read it at session start, after compaction and whenever the settings-changed
note arrives, it outranks the numbers in this bundle and in the docs, and never edit it. The
standing-policy paragraph becomes settings-driven: "Code and Review at their recommended in-flight
and every other pipeline stage at its own (shipped defaults two and one; the live numbers are
section 1 of your orchestrator instructions file)" and "keep the Code stage at a depth of its
recommended in-flight". `scripts/orchestrator-workspace.ps1` `Write-ClaudeContext` and
`Write-AgentsContext` add the same one-line rule. No change to `BootstrapBody` or
`RestartResumeBody`.

Rejected: a server-queued note at every launch (a turn per launch that the hook makes unnecessary);
relying on the bundle sentence alone (a sentence is not a read; the hook is).

### D-6: not a bundle, not an instruction file, not an import; documented in the instruction-file contract

The file is never added to `PolicyRefreshSettings.InstructionFiles`, never lives under an agent's
cwd, and never appears in a bundle or a `CLAUDE.md` `@` import. `docs/agent-instruction-file-contract.md`
gains a section "Generated orchestrator instructions (CARD-0822)" owning: the path rule, the env
vars, the route, the stamp line and hash rule, the byte cap, the never-edit rule, the exclusions
above and why, and the precedence rule (live file over bundle numbers over doc numbers).
`server/Bundles/README.md` gets two sentences saying live operating settings are not bundle
content and pointing at that section. `docs/ops-http.md` gets the two route rows. `AGENTS.md` and
`docs/orchestration-loop.md` §1 reword the fixed depth as in D-5.

### D-7: no secret can reach the file, by construction and by tripwire

The snapshot builder reads only: `DelegationSettings` public caps and role policy, `HostBudgets`,
`RunnerRoutingSettings` and its kind defaults, `ModelAvailabilityHolds`, `RoutingPins` with card
identifiers and board names, the runner catalogue DTO, and the standing-instructions config list.
It never touches `ApiKeys`, `AgentTuiSecrets`, `LlmProviders`, `DelegationCapabilities`,
`Agent.LaunchEnvJson`, `Project.DefaultLaunchEnvJson`, task env overrides, the operator token file,
phone-home shared secrets, `GROK_HOME` or `CLAUDE_CONFIG_DIR`, and it renders no environment
variable values of any kind. The renderer refuses (throws `OrchestratorInstructionsRefusedException`,
the service records `OrchestratorInstructionsWriteFailed` and keeps the previous file) when its
output contains `{{key:` or any of the names `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN`,
`CLAUDE_CODE_OAUTH_TOKEN`, `OPENAI_API_KEY`, `XAI_API_KEY`, `GROK_CODE_XAI_API_KEY`,
`X-Antiphon-Operator-Token`, `SharedSecret`. Loud beats literal, as `ApiKeyPlaceholder` already
decides. Reasons on pins, holds and runner defaults are operator prose already visible on their GET
routes and are rendered as-is. The file is not a credential and needs no owner-only ACL; the
directory is created with the same helper the operator token uses.

### D-8: the doc-pin tests move with the wording

`StandingPipelinePolicyDocumentationTests.Phrases` replaces `"depth of two"` with
`"recommended in-flight"`; the other three phrases stay. `TaskPlatformGuidanceTests`,
`RunnerDefaultGuidanceTests` and `InstructionBundleTests` assertions on `orchestrator.md` are
unaffected by the additions and must stay green (the bundle still opens "You are an orchestrator.").
The hook settings test that pins `matcher === 'compact'` changes to the new matcher.

### D-9: settings block and defaults

`Delegation:OrchestratorInstructions` (`OrchestratorInstructionsSettings`, validated at startup):
`Enabled` true; `Path` "" (platform default); `MaxBytes` 16384 (4096..65536); `SweepSeconds` 60
(10..3600); `Notify` `All`; `StandingInstructions` empty list (at most 10 lines, each at most 300
chars, no `{{key:`). `Enabled=false` disables generation, notices and the env vars together, and the
route answers 404 `orchestrator_instructions_disabled`.

## Implementation slices

| Slice | Files / changes | Coverage that closes it |
|---|---|---|
| S1: model and renderer | `server/Application/Settings/OrchestratorInstructionsSettings.cs` (+ `DelegationSettings.OrchestratorInstructions`, validator); `server/Application/Services/AntiphonDataPaths.cs` (root resolution factored from `AgentTuiSettings`); `OrchestratorInstructionsSnapshot.cs`, `OrchestratorInstructionsRenderer.cs`, `OrchestratorInstructionsDelta.cs`, `OrchestratorInstructionsRecipients.cs`; `ChannelPreamble` bodies | Unit classes in V-1..V-4, R-1, R-2 |
| S2: state, generation, triggers, route | `server/Domain/Entities/OrchestratorInstructionsState.cs`; `AppDbContext` + migration `AddOrchestratorInstructions` (state table, `AgentSessions.OrchestratorInstructionsVersion`); `OrchestratorInstructionsSnapshotBuilder.cs`, `OrchestratorInstructionsService.cs`, `OrchestratorInstructionsHostedService.cs`, `CompositeRunnerEligibilityObserver.cs`; signals in the six writers of D-3; `AgentIncidentKind` 80..82; `server/Api/Endpoints/OrchestratorInstructionsEndpoints.cs`; `Program.cs` registrations | V-5, V-6, V-9, V-10, R-3, R-4, migration shape test |
| S3: notices, launch env, compaction | notice lane inside `OrchestratorInstructionsService`; `AgentSessionLaunchComposer` and `AgentTaskDispatcher.BuildEnv` env vars; session version stamp where `InstructionFileStamp` is set; `CompactionRecoveryService` non-Claude branch | V-7, V-8, V-11, V-12, R-5, R-6 |
| S4: hook, scripts, bundle, docs | `scripts/hooks/orchestrator-investigation-hook.mjs`, its tests, `.claude/settings.json`; `scripts/orchestrator-workspace.ps1`; `server/Bundles/orchestrator.md`, `server/Bundles/README.md`; `AGENTS.md`, `docs/orchestration-loop.md`, `.claude/skills/antiphon-orchestrator/SKILL.md`, `docs/agent-instruction-file-contract.md`, `docs/ops-http.md`, `docs/agent-credentials.md`; `StandingPipelinePolicyDocumentationTests` phrase; new `OrchestratorInstructionsGuidanceTests` | V-13, V-14, R-7, R-8 |

Commit each slice as it completes; S1-S4 form one verification group and the checkpoint table runs
once after S4 is committed. Never edit generated `docs/cards/` files.

## Platform, execution and rollout

`GET /api/runner-defaults` and `GET /api/session-runners` were read on 2026-09-29 through the
production API: runner defaults revision 2 route everything to `server2` (Human), and both `desktop`
(windows, capacity 2) and `server2` (linux, capacity 10) are dispatch-eligible. Nothing in this card
needs an OS-specific lane: the path resolution has both branches and is unit-tested through a fake
`AgentTuiPathEnvironment`, the file write is plain .NET IO, and the hook is Node. Keep the task
platform Any, omit `-Runner` and `-Platform`, and let the runtime default place Code and Review.

Code runs the closed `### Checkpoints` table with the CARD-0723 tool after S4 is committed:

```text
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-29-card-0822-orchestrator-instructions-file-plan.md --after S1-S4 --max-wait 570s
```

Bootstrap the tool through `scripts/build-slot.ps1`; the rows lease their own slots; keep calling
`wait <run-id>` until the exit is not 75. The migration is generated with the repo tool manifest's
`dotnet-ef` (CARD-0677) and its shape is pinned by a Unit test; the shared-Postgres integration rows
use isolated schemas as the harness already does. Rollout after land is the ordinary restart; on the
first start the `startup` reconcile writes the file and every surviving orchestrator session (null
version) receives one notice. Rollback is a reviewed revert; the state row and column are inert
without the service.

## Verification design

No builds or tests were run during Plan. Counts below are source-derived floors for the checkpoint
tool: a method contributes one executed result, an `[Arguments]` method one per argument.

### Proves it works now

| ID | Evidence |
|---|---|
| V-1 | `OrchestratorInstructionsRendererTests` (Unit, 6 methods, 8 results): `Render_is_deterministic_and_version_is_the_body_hash`; `Header_declares_generated_and_names_the_route`; `Sections_render_caps_runners_defaults_holds_pins_levels_and_standing_lines` (asserts each section heading, the role table, the desktop and a remote runner row, the global default with reason, a hold line, a stage-wide and a card pin line, the four Claude aliases, a standing line, and the per-agent-pins sentence); `Occupancy_and_timestamps_are_not_rendered` (a snapshot whose only difference is occupancy renders byte-identical); `Oversized_snapshot_truncates_in_priority_order_under_the_byte_cap` (200 pins, 60 holds, 50 runners, 10 standing lines; asserts size at most 16 384, section 1 intact, `… N more:` lines in the stated order); `Secret_shaped_content_refuses_the_render` with `[Arguments]` for `{{key:x}}`, `ANTHROPIC_API_KEY=abc`, `X-Antiphon-Operator-Token: abc` placed in a standing line, asserting the refusal exception. |
| V-2 | `OrchestratorInstructionsDeltaTests` (Unit, 4 methods): changed, added and removed facts per section; empty on equal snapshots; line and length caps with `+N more`; `Notice_body_names_versions_path_route_refine_and_no_reply_and_stays_under_700_chars`. |
| V-3 | `OrchestratorInstructionsRecipientTests` (Unit, 4 methods): standing bundle-carrying agents with a Running PtyHost session; orchestrator task sessions in and worker task sessions out; Herdr, specialist, card and `Off` sessions out; a session at the current version skipped. |
| V-4 | `OrchestratorInstructionsPathTests` (Unit, 1 method, 2 results): `[Arguments("windows")]`, `[Arguments("linux")]` default path under the platform data root; relative override refused. |
| V-5 | `OrchestratorInstructionsRefreshTests` (Integration, `BridgeQueueHarness` with `AlwaysOn`, `Path` under `h.TempRoot`, the agent carrying the `orchestrator` bundle, isolated schema, `[NotInParallel("MessageQueue")]`): `Runner_defaults_put_rewrites_the_file_and_delivers_one_notice_to_an_idle_standing_orchestrator`: call `RunnerDefaultSettingsService.PutAsync` with a new global runner, `await WhenIdleAsync()`, then assert the file exists, its stamp line carries the state row's version, its body names the new runner and reason, the state row revision incremented, `h.Adapter.SubmittedBodies` holds exactly one body that starts with the note prefix and contains the delta line and the path, and the session column equals the new version. |
| V-6 | Same class, `Covered_writes_each_regenerate_and_notify` with `[Arguments]` `host-budget`, `hold-upsert`, `hold-clear`, `routing-pin`, `runner-eligibility` (the last through the composite observer with a fake directory): same three assertions (file version advanced, row revision advanced, one body delivered naming the changed section). |
| V-7 | Same class, `An_orchestrator_task_session_receives_the_notice_when_idle`: an `AgentTask` row `Kind=Orchestrator`, `Status=Working`, `AgentSessionId=h.SessionId`, agent marked pool delegate with no bundle; one body delivered. |
| V-8 | Same class, `Launch_records_the_current_version_on_the_session_and_the_env_carries_the_path_and_url`: `AgentSessionLaunchComposer.ComposeForAgentAsync` on the bundle-carrying agent yields both env vars and the stamped version; `AgentTaskDispatcher.BuildEnv` for an Orchestrator task yields both, and for a Worker task neither. |
| V-9 | Same class, `Sweep_regenerates_after_a_direct_row_edit_without_a_trigger`: `ExecuteUpdateAsync` on `HostBudgets`, then `ReconcileNowAsync("sweep")`; file and notice as in V-5. |
| V-10 | Same class, `Startup_regenerates_and_a_pre_feature_session_gets_one_notice`: session column null, `ReconcileNowAsync("startup")` on an empty state row; file written, one notice, column stamped. |
| V-11 | `CompactionRecoveryTests` (+2 methods, 8 total): `Compact_boundary_on_a_non_claude_orchestrator_session_queues_the_instructions_re_read_note` (session `AgentKind=Grok`, bundle attached, no preamble: exactly one body, the compaction re-read note naming the path and route) and `Compact_boundary_on_a_claude_orchestrator_session_does_not_add_a_second_note` (with preamble: `SubmittedBodies` is exactly `[RecoveryNoteBody]`). |
| V-12 | `OrchestratorInstructionsEndpointTests` (Integration, `AntiphonWebAppFactory`, 2 methods): GET returns the body with `ETag` and version header equal to the state row; POST refresh without the operator token is 403 `operator_token_required` and with it returns the version. |
| V-13 | Hook tests in `scripts/hooks/__tests__/orchestrator-investigation-hook.test.mjs` (+7 cases): startup injects the file text when armed and the env path exists; resume injects; compact injects the file then `COMPACT_CONTEXT`; missing env is silent; missing file is silent; a 40 KiB file is cut at 16 384 bytes with the marker; a worker (`ANTIPHON_TASK_ID` set, kind Worker) is silent. The settings test asserts the matcher is `startup\|resume\|compact` and both entries reuse one wrapper command. |
| V-14 | `OrchestratorInstructionsGuidanceTests` (Unit, 4 methods): `orchestrator.md` names `ANTIPHON_ORCHESTRATOR_INSTRUCTIONS`, `GET /api/orchestrator-instructions`, "after compaction" and "outranks", and no longer contains "at a depth of two"; the four policy copies no longer contain "depth of two" and still contain the three retained phrases; `orchestrator-workspace.ps1` contains the one-line rule in both context writers and the new matcher; `docs/agent-instruction-file-contract.md` has the CARD-0822 section naming the path rule, the stamp line and the exclusion from `InstructionFiles`. |

### Guards the regression

| ID | Evidence |
|---|---|
| R-1 | Renderer determinism and cap (V-1) guard the hash comparison that makes the sweep a no-op. |
| R-2 | `OrchestratorInstructionsRefreshTests.An_unchanged_write_neither_rewrites_nor_notifies`: repeat the V-5 PUT with the same values; file bytes and mtime unchanged, revision unchanged, no second body. |
| R-3 | `A_busy_session_gets_the_notice_after_its_turn_ends_and_a_second_change_replaces_the_pending_row`: `h.MarkWorkingAsync()`, two covered writes, assert one `Pending` row with the `[orchestrator-instructions]` header whose body carries the second delta and the more-than-once wording, then a turn end delivers exactly one body. |
| R-4 | `A_worker_delegate_session_receives_nothing`: same as V-7 with `Kind=Worker`; no row, no body. |
| R-5 | `Secret_bearing_rows_never_reach_the_file_or_the_notice`: seed an `ApiKey`, an `AgentTuiSecret`, an `LlmProvider` key, a `DelegationCapability`, and `Agent.LaunchEnvJson` with distinct sentinel values, then a covered write; assert none of the sentinels appear in the file, the row body, or any delivered body. |
| R-6 | `A_refused_render_keeps_the_previous_file_and_records_a_warning_without_a_notice`: after a good V-5 state, set a standing line containing `{{key:x}}` through the settings instance and reconcile; file bytes unchanged, `OrchestratorInstructionsWriteFailed` incident present, no body. |
| R-7 | Existing doc-pin classes (`StandingPipelinePolicyDocumentationTests`, `TaskPlatformGuidanceTests`, `RunnerDefaultGuidanceTests`, `InstructionBundleTests`, `PolicyRefreshDeltaTests`) stay green after the bundle and doc edits. |
| R-8 | `OrchestratorInstructionsMigrationShapeTests` (Unit, 1 method) pins the state table, the session column as nullable `character varying(16)`, and that the model snapshot has no pending changes. `CompactionRecoveryTests` existing 6 methods stay green. |

### Positive controls for the later Mutation stage

Method-scoped, restore between each; Code does not run them.

| PC | Mutation | Exact test expected red |
|---|---|---|
| PC-1 | Remove the `Signal` call after `RunnerDefaultSettingsService.PutAsync` saves | `/*/*/OrchestratorInstructionsRefreshTests/Runner_defaults_put_rewrites_the_file_and_delivers_one_notice_to_an_idle_standing_orchestrator` (no hosted sweep runs in the harness) |
| PC-2 | Skip the version comparison in the reconcile (always write) | `/*/*/OrchestratorInstructionsRefreshTests/An_unchanged_write_neither_rewrites_nor_notifies` |
| PC-3 | Drop the `Kind == Orchestrator` filter in `OrchestratorInstructionsRecipients.Select` | `/*/*/OrchestratorInstructionsRefreshTests/A_worker_delegate_session_receives_nothing` |
| PC-4 | Remove the tripwire scan from the renderer | `/*/*/OrchestratorInstructionsRendererTests/Secret_shaped_content_refuses_the_render` |
| PC-5 | Stop stamping the session column at enqueue | `/*/*/OrchestratorInstructionsRefreshTests/A_busy_session_gets_the_notice_after_its_turn_ends_and_a_second_change_replaces_the_pending_row` (duplicate body) |
| PC-6 | Make the hook ignore `ANTIPHON_ORCHESTRATOR_INSTRUCTIONS` | the startup-injects hook case under `pwsh -File scripts/test-hooks.ps1` |

### Cost

Ordinary Code verification floor from the table: 12 + 2 + 9 + 6 + 2 = **31 minutes**, including
one isolated build of `tests/Antiphon.Tests`. Authoring estimate for S1-S4: 4-6 hours including
the migration and the hook. Code `-ExpectAbout`: 6-7 hours. Estimates allow for host contention.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S4 | `tests/Antiphon.Tests -> bin-c822/` | unit-new | `/*/*/(OrchestratorInstructionsRendererTests*)\|(OrchestratorInstructionsDeltaTests*)\|(OrchestratorInstructionsRecipientTests*)\|(OrchestratorInstructionsPathTests*)\|(OrchestratorInstructionsGuidanceTests*)\|(OrchestratorInstructionsMigrationShapeTests*)/*` | V-1, V-2, V-3, V-4, V-14, R-1, R-8 | all listed, 0 failed/skipped; renderer 8 results, delta 4, recipients 4, path 2, guidance 4, migration 1 | 23 | 12 | false |
| CP-2 | S1-S4 | CP-1 | doc-pins-existing | `/*/*/(StandingPipelinePolicyDocumentationTests*)\|(TaskPlatformGuidanceTests*)\|(RunnerDefaultGuidanceTests*)\|(InstructionBundleTests*)\|(PolicyRefreshDeltaTests*)/*` | R-7 | all listed, 0 failed/skipped | 59 | 2 | false |
| CP-3 | S1-S4 | CP-1 | refresh-integration | `/*/*/OrchestratorInstructionsRefreshTests/*` | V-5, V-6, V-7, V-8, V-9, V-10, R-2, R-3, R-4, R-5, R-6 | all 11 methods, 15 results, 0 failed/skipped | 15 | 9 | true |
| CP-4 | S1-S4 | CP-1 | compaction-endpoint | `/*/*/(CompactionRecoveryTests*)\|(OrchestratorInstructionsEndpointTests*)/*` | V-11, V-12, R-8 | all listed, 0 failed/skipped; compaction 8 results, endpoint 2 | 10 | 6 | true |
| CP-5 | S4 | n/a | hooks | `pwsh -File scripts/test-hooks.ps1` | V-13 | `HOOKS TESTS EXIT CODE: 0`, the 7 new cases and the matcher case listed as passing | n/a | 2 | false |

CP-2 through CP-4 reuse CP-1's output with `--no-build`. Report each generated `CHECKPOINT` line
with executed, passed, failed and skipped counts and any reruns; any other build or test run needs
a stated reason. If Code finds an existing test class asserting on `BuildEnv` or
`ComposeForAgentAsync` env keys (`AgentBundleAttachmentTests`, `DelegateBundleLaunchTests`,
`StandingSessionSelectionTests.Composition` name those keys), run it as an unlisted row with the
reason "env contract widened by S3" and report it. Serial rows keep the MessageQueue exclusion
honest; the Unit rows may overlap.
