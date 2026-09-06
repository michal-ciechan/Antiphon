# CARD-0384: Herdr placement by workspace and tab name

Plan date: 2026-09-06. Ground truth: `73614e3b193ef02bcf51c08bb236772473e0ff60`.
Board card: CARD-0384 / GitHub #39. Related incident: CARD-0388 / GitHub #44.

Implement an optional, agent-owned name pin ahead of last-pane resolution. A configured tab is a dedicated single-pane launch target: find it, create it when absent, launch into an idle PowerShell shell, or adopt an exactly identified occupant. Refuse foreign occupancy without moving elsewhere. Keep the existing placement path for agents without a tab pin. This is ready for TestDesign; no implementation or live-agent changes were made by this Plan stage.

## Ground truth

Paths and line numbers below describe the inspected commit, not proposed code.

| Card assumption / relevant fact | Actual implementation and implication |
|---|---|
| Last-pane is keyed by session ID and disappears when its pane disappears. | `src/Antiphon.SessionRunner/HerdrLastPane.cs:14` stores workspace/tab/pane IDs, origin and env **names**; `HerdrPaneChild.cs:343-428` tries the current session then `ReusePaneOfSessionId`, deletes a mismatched-workspace or missing-pane record, and returns `Allocate`. A live foreign occupant instead throws `pane_occupied` and retains the record. |
| The allocator finds a slot, not a tab label. | `HerdrPaneChild.cs:1124-1166` verifies live sidecar panes against `pane.list`, then calls `HerdrPaneAllocator.Allocate`; `HerdrPaneAllocator.cs:29-66` groups by tab and fills a group with fewer than four panes. **Tabs are already created with `opts.PaneTitle`**, not necessarily untitled (`HerdrPaneChild.cs:1158-1159`). The missing operation is label lookup. Also, runtime currently supplies `TabNumber: 0` for every census row (`SessionRunnerRuntime.cs:321-335`), so production ties break by tab ID, despite the documented lowest-numbered policy. Do not fix that separate discrepancy here. |
| Workspace reuse already exists. | `HerdrPaneChild.cs:1047-1100` selects an exact `antiphon-ws` token first, otherwise exactly one **ordinal, case-sensitive**, untagged label match, otherwise creates a managed workspace. Creation returns a root tab/pane. Reused operator workspaces are never stamped. `docs/herdr-sessions.md:132-150` describes this contract. |
| Agent/project placement settings exist. | They do not. `HerdrLaunchContextResolver.cs:37-94` derives project context from card board, agent board, then pool project, or returns `none` / `Antiphon`. `SessionRunnerContracts.cs:36-59` has `WorkspaceKey`, `WorkspaceLabel`, `WorkspaceCwd`, `PaneTitle`, kind, slug and previous-session hint, but no tab label. |
| Start, AlwaysOn and fresh fallback need identical placement. | `AgentSupervisorService.cs:190-207` calls `AgentControlService.StartAsync`, selecting fresh after repeated failures. `AgentControlService.cs:316-349` queues same-row resume; `:353-441` creates a fresh row and captures the previous ID before replacing `PersistentSessionId`. `AgentSessionService.cs:1451-1497` reconstructs Herdr context centrally for every launch. `:349-358` also handles missing-resume-target fallback with a **fresh conversation on the same row ID**. Both fresh shapes must carry the pin. |
| Attach can launch into an idle shell. | It cannot, and must not be changed to do so. `AgentControlService.cs:476-638` is inspect, persist row, then bind; runner `HerdrPaneChild.cs:106-175` requires detected kind, one expected-family foreground process, expected PID/native identity, and Grok transcript availability. Empty/no-agent is currently `pane_unoccupied` at the runner; the server also has its own checks. Attached origin detaches on stop (`:787-793`), and is excluded from allocator census (`SessionRunnerRuntime.cs:328-329`). |
| The current target-resolution description can simply gain a label fallback. | `docs/herdr-sessions.md:213-225` says last-pane comes before allocator and restricts idle-shell typing to `Origin = launched`. Named placement needs a **separate, higher-priority branch**, because an operator's explicitly named idle shell has no launched-origin history and an old last-pane may point at MavRef-DL. |
| Every failed detection closes the pane. | Already corrected in this checkout by CARD-0383: `HerdrPaneChild.cs:603-608,997-1017` retains an idle shell at detection timeout, writes last-pane, and sets `_keepPaneOnKill`; `KillAsync:795-801` honors it. This improves retry stability but cannot discover/create `Orch`. |
| Relaunch already sets cwd in a reused shell. | It does not generally do so: `CompleteTypedLaunchAsync:579` passes cwd only for the newly created workspace root. An existing operator shell needs an explicit quoted `Set-Location -LiteralPath request.Cwd`, as well as the existing env application/removal. |
| Returning `pane_occupied` automatically makes public Start return 409. | Runner `POST /sessions` maps launch exceptions through `HerdrProblemMapper` (`Program.cs:175-191`, `HerdrProblemMapper.cs:12-16`); `SessionRunnerHttpClient.cs:81-87` preserves the typed problem. But public Start **queues** work (`AgentControlService.cs:238-241,346-349,440-442`) and later failure is stored by `AgentSessionService.cs:361-382`. Add a read-only preflight for the public 409; retain the authoritative launch-time check for races. |
| A dedicated named tab stays dedicated automatically. | Not unless reserved tabs leave the allocator census: today every live launched-origin sidecar is eligible (`SessionRunnerRuntime.cs:321-335`). Another ordinary agent could split the new Orch pane and make its next launch fail the single-pane rule. |

### Actual launch and attach call chains

Standing start / supervision -> `AgentControlService.StartAsync` -> `StartInteractiveSessionAsync` -> `AgentSessionLaunchQueue.EnqueueInteractiveSession` -> `AgentSessionService.LaunchInteractiveAsync` / `LaunchInteractiveProcessAsync` -> central launch-spec enrichment (`AgentSessionService.cs:1451`) -> runner adapter / `RunnerTerminalSession.StartAsync` -> `SessionRunnerHttpClient.StartAsync` -> runner `POST /sessions` -> `SessionRunnerRuntime.StartAsync:94` -> `RunnerSession.StartHerdrAsync:1118` -> `HerdrPaneChild.LaunchAsync:261`.

Today the child does guards -> connect -> ensure workspace -> resolve last-pane -> adopt, relaunch, consume newly created root, or allocate -> complete typed launch. `StartHerdrAsync:1169-1177` calls `KillAsync` on any failure. Therefore merely assigning `_paneId` during inspection can make refusal cleanup close a pane the launch never acquired.

Attach is a separate path: `AgentControlService.AttachHerdrAsync` -> runner inspect HTTP -> runner `/sessions/attach` -> `SessionRunnerRuntime.AttachHerdrAsync:205` -> `HerdrPaneChild.AttachAsync`. Runner `FindBoundPane:280-315` checks live/pending sessions, active sidecars and historical last-pane records. Reuse its evidence carefully; a retired previous session is allowed for fresh placement, but a still-live previous session is not.

### Herdr wire evidence

Read-only local command `herdr --version` reported **0.8.2**; `herdr api schema --json` reported protocol **20**, schema **1**. Its request union contains `method: tab.list` with `TabListParams.workspace_id`; success `ResponseResult` contains `{ type: "tab_list", tabs: TabInfo[] }`. `TabInfo` includes `label`, `workspace_id`, `tab_id`, `pane_count`, and `number`. `TabCreateParams` already accepts `workspace_id`, `label`, `cwd`, `env`, and `focus` (default false). No live pane was inspected or mutated to obtain this schema.

`HerdrClient.cs:162-175` already wraps `tab.create`; `HerdrApiModels.cs:24-31` already models `TabInfo`. Add only the missing typed list wrapper/params/envelope and fake route. Do not infer tab labels from pane labels, agent names, terminal titles or sidecars.

## Decisions

### D-1: Persist optional names on Agent; leave IDs runner-owned

Add nullable `Agent.HerdrWorkspaceLabel` and `Agent.HerdrTabLabel`, mapped to nullable text columns with a 256-character application/EF limit. Add the corresponding camelCase create/update/detail/list DTO fields and client types. Generate the migration with EF CLI; no pane, tab or workspace ID columns and no data backfill. Per-project overrides are unnecessary for v1; agent ownership is explicitly permitted by the card and allows two standing agents to choose different tabs.

Create: missing/null/blank means default. Update: missing/null leaves the field unchanged; an empty/whitespace string explicitly clears it, following existing optional-update conventions. Trim surrounding whitespace, preserve case for display, reject control characters or labels over the limit with 422. Settings take effect on the next launch; saving or calling Start on an already live agent does not move it. Retain settings when switching backend; ignore them while using PtyHost.

Apply the name pin to a standing, cardless, non-pool agent (`session.CardId == null`, owning agent exists, `!IsPoolDelegate`). Card-spawn and pool placement remain their current project/allocator path. Labels may be configured before starting the standing seat; they are not derived automatically from its name, slug, current tab or attached pane.

Settings UI: two optional text inputs in Herdr mode on `AgentCreateModal` and `AgentSettingsModal`: **Workspace label** (placeholder: project name) and **Tab label** (placeholder: automatic placement). Explain that the tab is dedicated to one pane and changes apply on next launch. Clearing sends the explicit empty-string update. Pane display name and Herdr agent slug remain independent of the configured tab name.

### D-2: Keep the workspace contract; make tab label an independent launch field

Keep `WorkspaceKey = project:<guid>` / `none` and project `WorkspaceCwd` unchanged. Resolve existing defaults first, then apply the eligible owning agent's nonblank workspace override to `WorkspaceLabel`; carry its tab override in a new optional trailing `HerdrLaunchOptions.TabLabel = null`.

Workspace selection remains token first, then one untagged exact ordinal label, then creation. **A matching token still wins even if its workspace has been renamed or the override names another workspace.** The workspace override changes the fallback/create label, not established token identity. This is the brief's permitted existing workspace half; do not promise force relocation or consolidate duplicate workspaces. Null tab label takes the old last-pane path, including its existing WorkspaceKey check, even when only a workspace-label override is supplied.

Tab matching is exact `OrdinalIgnoreCase` on Windows and `Ordinal` elsewhere; no substring, fuzzy, cwd, title or agent-slug matching. More than one match is 409 `herdr_tab_ambiguous`, listing candidate IDs/labels, with no creation or allocation. An existing matching tab must have exactly one enumerated pane in that tab/workspace, agreeing with its reported count; otherwise 409 `herdr_tab_invalid`. Do not choose a root/focused pane from a multi-pane tab: the card explicitly scopes that out.

Refactor resolver early returns so every eligible standing path receives the overrides. The existing central spec builder remains the authority for actual queued launches, including missing-resume-target fallback. Preserve `ReusePaneOfSessionId` through `with` updates rather than rebuilding a partial options record that can drop new fields.

### D-3: Named resolution outranks every last-pane hint

After existing kind/backend/provider/Grok launch guards and Herdr protocol validation:

1. Ensure the workspace using D-2.
2. If `TabLabel` is null, execute the existing last-pane -> new-root -> allocator flow. Do not send `tab.list` on this path just to reproduce its current behavior.
3. Otherwise list tabs **in the resolved workspace**, validate their workspace identity, and select by D-2. Last-pane cannot choose another tab or workspace and cannot rescue an occupied/invalid named target.
4. No matching tab in an existing workspace: call `tab.create(workspace_id, label: TabLabel, cwd: request.Cwd, env: request.Env)` once. Use its returned root pane, with no split and no additional rename to `PaneTitle`.
5. If this launch just created the workspace, consume its returned root tab/pane and rename that **new** tab to `TabLabel`. This retains CARD-0323's no-empty-root invariant instead of creating a redundant second tab merely to satisfy the literal operation name in the card. The resulting workspace still has exactly one correctly named tab and one pane. Never consume a reused operator workspace's unrelated root.
6. Existing single-pane target: re-read `pane.get` and `pane.process_info`; require returned IDs still belong to the selected workspace/tab. Check pending/live Antiphon claims. Classify using the foreground process list, not the screen or `AgentStatus`.
7. Zero non-shell foreground processes plus a positively identified live PowerShell shell: acquire it for a typed launch. This permission comes from the explicit tab pin, so a missing or attached-origin historical record does not force allocation. Unknown shell/PID or a non-PowerShell shell is 409 `pane_occupied`, with no typing.
8. Exactly one live foreground process, matching `pane.Agent` kind and expected process family, whose parsed argv names **request.SessionId**: adopt in place, bind PID/start evidence and existing transcript machinery, type no launch script. Reuse `TryReadNativeSessionId`; metadata alone is insufficient. A live previous ID is foreign on a fresh-row launch. Codex currently supplies no native session ID in argv, so an occupied Codex pane refuses; an idle named shell can launch Codex normally.
9. Any other occupant/claim is 409 `pane_occupied`. Retain both name settings and all relevant last-pane records, touch no occupant, and never fall through to allocator, attach, another backend or another tab.

Errors reading Herdr are errors, not absence. Never emulate `AllocatePaneAsync`'s `pane.list` failure-as-empty behavior in the named branch. If a selected tab/pane disappears or moves before acquisition, return 409 `pane_changed`; do not switch targets mid-attempt. A later explicit start or supervision retry resolves names again and can recreate an absent tab. This bounds races without retry loops that create extra furniture.

### D-4: Preserve launch safety, cwd, environment and origin

Factor shared foreground classification out of current last-pane logic where helpful, but preserve the unpinned branch's decisions. The named branch can call the existing typed-launch and adopt completions only after acquiring its target. Do not set `_paneId`, rename a pane, report pane metadata or write a script during read-only resolution/preflight.

Track acquisition and the launch's positively identified shell/child before entering any cleanup-capable code. Existing `KillAsync:807-808` treats a null sidecar as having no unexpected-process evidence; that is unsafe for an existing operator shell if it changes occupant between inspection and launch. For named launches, cleanup must never close/kill an unexpected foreground process when no sidecar exists. Keep the shell and pin, remove only this attempt's transient claim, and preserve the original refusal/error. Use the same ownership guard if a target disappears, moves or gains an occupant before typing. On adopted-process setup failure, no unproven PID is killed.

Recheck foreground and membership immediately before typing/adopting. Herdr has no atomic compare-and-launch RPC; this reduces, but cannot eliminate, a human action in the final RPC gap. Do not claim atomicity against external clients. The runner coordination in D-5 eliminates competing Antiphon launches, and cleanup still refuses unexpected occupants.

For a named idle shell, pass `request.Cwd` to the existing script builder every time. Keep UTF-8 BOM, properly quoted `Set-Location -LiteralPath`, env assignment/redaction, one typed script invocation, and separate Enter. Do not change the prompt-delivery contract or call `agent.start`/`agent.prompt`.

Last-pane may contribute stale `LaunchEnvNames` **only when its actual pane/tab/workspace IDs match this selected target** (current or retired previous session). Ignore mismatched historical records until success, rather than deleting them during named resolution. With no matching record, do not clear arbitrary environment variables in the operator shell. Success writes the current launch's env names and deletes consumed current/previous hints as today; a refusal leaves the records untouched. Detection-timeout idle-shell retention from CARD-0383 still applies.

Typed named launches and named adoption use managed `origin=launched`: explicitly assigning the named single pane makes this the agent's managed launch seat, including ordinary Stop behavior. `attach-herdr` remains `origin=attached`, retains detach-on-stop behavior, does not infer/write a pin, and never starts an idle shell. Existing running attached sessions are not converted by a settings edit or Layer-A runner adoption.

### D-5: Reserve named tabs and serialize target acquisition

Add optional `WorkspaceLabel` and `TabLabel` snapshots to runner sidecar/last-pane JSON. These are diagnostic/placement context and a reservation marker; the Agent fields are the durable pin. Old JSON without them remains valid. Copy them through `WriteSidecar`, `HerdrLastPane.FromSidecar` and `FromLaunchRequest`. Pruning sidecars/hints never clears the Agent pin. Layer-A adoption restores the marker from sidecar without DB access and continues its current process/transcript identity checks.

Exclude the **whole tab** containing any live named-launch sidecar from `CollectLiveAntiphonPanes`, not just that one pane. Preserve attached exclusions. Do not make the allocator read `last-pane/`, infer reservations from labels alone, or modify its geometry/order. An idle named shell with no live Antiphon sidecar is already not allocator capacity. This is the only change to unpinned agents' candidate set: configured dedicated tabs are reserved, while all formerly eligible unpinned tabs retain current behavior. Clearing a pin affects the next launched session's reservation, not a running session's snapshot.

Add runner-owned, instance-scoped placement coordination (a concrete service, no static state): serialize workspace find/create by WorkspaceKey, then serialize the short target select/claim phase by resolved workspace ID. Claims cover tab and pane IDs and survive until a sidecar is published or the attempt fails. Named preflight, named launches and attach acquisition consult these claims; a second session cannot type into a pending first launch. Distinct target launches may wait for detection concurrently after acquisition. The ordinary allocator excludes pending dedicated claims as well as committed reservations. Do not hold a global semaphore across the up-to-60-second detection wait.

Use live/pending bindings and active sidecars as refusal evidence. Historical last-pane bindings permit the current ID and the explicitly carried retired `ReusePaneOfSessionId`; another session's historical claim refuses rather than being silently stolen. Do not exempt a still-live previous session simply because its ID is in that hint. Release transient claims in `finally` on cancellation and every failure, retaining durable names/hints. Recheck under the shared acquisition coordination before any bind/type operation. Do not add process kills to supervision to resolve contention.

### D-6: Honor HTTP refusal without making every Start synchronous

Add runner `POST /herdr/placement/check` accepting a small `HerdrPlacementCheckRequest(SessionId, HerdrLaunchOptions)` with no executable/env values. It uses the same read-only workspace selection, named-tab match and occupant classifier. It never creates a workspace/tab, refreshes metadata, deletes last-pane, reserves a pane, writes a sidecar, or types. Missing workspace/tab returns an allowed result with action `create`; idle/adoptable targets return `relaunch`/`adopt`; occupancy/ambiguity/shape changes use the same typed 409s as launch. No IDs from this response become a persisted pin or authoritative queued target.

Before mutating/enqueueing the proposed interactive session row, choose its ID (existing resume ID or a new GUID), resolve its effective name context and call this check for named Herdr placement only. Preserve the existing idempotent already-live Start and existing supervision-latch semantics. On preflight refusal, public `POST /api/agents/{id}/start` surfaces `ConflictException(Code="pane_occupied")` via existing middleware, preserving `PersistentSessionId`, queued-message associations, settings and last-pane. Supervisor uses this same Start path and records failure/backoff through its existing catch. No new alert sink or retry threshold is part of this card.

Launch always re-resolves and rechecks independently. An occupant arriving **after** an allowed preflight is a runner 409 and an asynchronous Failed session with the runner detail in `FailureReason`; the already returned public HTTP response cannot retroactively become 409. State this distinction in docs/tests. Never translate `pane_occupied` into `ResumeTargetMissingException`, so it cannot trigger conversation fallback or allocator fallback.

Add optional `HerdrNamedTabPlacement` to runner capabilities, advertised only when Herdr is enabled and this implementation exists. The server requires true for named preflight **and** actual named launch. Otherwise report the existing runner-capability mismatch with upgrade guidance. This prevents a new server sending an unknown `TabLabel` to an older runner that silently uses the allocator. Old server -> new runner and null tab -> either supported Herdr runner remain compatible. The check route alone is not a capability guarantee for queued launch.

### D-7: #44 is only partially resolved

Once shipped and explicitly configured on **PM-Orchestrator-Grok** (`2ee02f40-7b6d-48b1-96fc-c4344c651910`) with `herdrWorkspaceLabel="PredictionMarkets"`, `herdrTabLabel="Orch"`, this design fixes the reported missing-Orch -> MavRef-DL placement, subject to the retained workspace-token precedence. It recreates Orch after loss and refuses foreign occupancy instead of filling a specialist tab. No migration automatically configures that agent.

It does **not** establish or fix why the Grok child died (`HerdrChildGone`, detect-none, `HerdrPaneClosed`), Windows multiline `--rules` handling linked to GitHub #41, or which of the two orchestrators owns Slack. CARD-0383 already supplies a missing-native-session guard and idle-timeout retention in the inspected base; do not reimplement it. #44 should reference this plan for placement, configure/verify the pin after landing, then investigate remaining process/argv evidence and the twin PtyHost agent separately. Do not close #44 solely because a pane now lands on Orch; sustained running and transcript-backed delivery still need evidence there.

### Rejected alternatives

- Adding a label fallback after last-pane: a valid old specialist-tab ID would still win.
- Implementing placement in attach-herdr: it changes detach/ownership semantics and still cannot create a missing tab safely through the inspect/bind contract.
- Persisting IDs or relying on metadata TTL for the pin: layout loss/TTL expiry would erase intent; runner startup also cannot consult the server DB.
- Reusing the first same-kind process, a previous fresh-row ID, or `agent_session` metadata alone: insufficient session identity.
- Selecting a root/focused pane in a multi-pane tab or splitting another tab: outside v1 and contradicts dedicated placement.
- Correcting allocator tab ordering, consolidating workspaces, global supervision retry changes, or repairing Grok argv here: separate work, unnecessary for label resolution.

## Replacement target-resolution documentation

During Code, replace the table under `docs/herdr-sessions.md` section 4 (current lines 213-225) and update sections 1/3/3a to distinguish explicit named authority from attached-origin history. Include the settings/PATCH-clear contract, token precedence, allocator reservations, capability requirement and public preflight/queued-race distinction.

| Priority / target state | Resolution |
|---|---|
| Existing launch guards fail or Herdr cannot be verified | Explicit refusal/error; no alternative backend or placement. |
| Resolve workspace | Matching `antiphon-ws` token; else unique untagged exact workspace label; else create managed workspace. Workspace override supplies the fallback/create label. |
| Tab label configured, no matching tab | Create one labelled tab at agent cwd, with one pane; if workspace was just created, name/use its returned root instead. Never allocator/split. |
| Tab label configured, exactly one single-pane match, idle PowerShell | Launch script in that pane, at agent cwd, applying current env and clearing only proven stale launch-env names. No new tab or split. |
| Tab label configured, exactly one single-pane match, one live expected-kind process with this session ID in argv | Adopt that process; type no launch script. |
| Tab label configured, foreign/unidentifiable/wrong-kind occupant or another session's claim | 409 `pane_occupied`; retain names and last-pane; never steal/fallback. |
| Tab label configured, duplicate label or tab not exactly one pane | 409 `herdr_tab_ambiguous` / `herdr_tab_invalid`; no mutation/fallback. |
| Selected named target disappears/moves before acquisition | 409 `pane_changed`; later attempt resolves names again. |
| No tab label, no valid last-pane | Existing new-workspace-root / allocator path; dedicated named tabs excluded from capacity. |
| No tab label, launched-origin idle PowerShell last-pane | Existing in-place relaunch. Attached-origin empty history does not authorize typing. |
| No tab label, matching live last-pane occupant | Existing exact-argv-ID/kind adoption. |
| No tab label, occupied last-pane | Existing 409 `pane_occupied`, record retained, no allocator fallback. |
| Typed launch detect timeout, target still only its verified shell | Preserve pane and write last-pane as CARD-0383 does; name pin survives regardless of pane loss. |

## Implementation slices

All slices belong to Code after TestDesign. Keep changes additive and avoid moving unrelated launch behavior.

| Slice | Files / concrete work | Tests to add or extend |
|---|---|---|
| S1: Agent settings and central context | `server/Domain/Entities/Agent.cs`; `server/Infrastructure/Data/AppDbContext.cs`; CLI-generated `server/Migrations/*`; `server/Application/Dtos/AgentDtos.cs`; `server/Application/Services/AgentService.cs`; `HerdrLaunchContextResolver.cs`; `AgentSessionService.cs`; `AgentControlService.cs`; `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs`. Add fields, validation, API mappings and optional wire field; preserve overrides/previous-ID hint on all standing launch variants. | New `HerdrPlacementSettingsTests` for round-trip/clear/backward compatibility/scoping; extend `HerdrLaunchContextResolverTests` beyond its current four title-only tests; extend `SessionRunnerHttpClientHerdrWireTests` for new/absent TabLabel. |
| S2: Typed listing and pure selection | `src/Antiphon.SessionRunner/HerdrApiModels.cs`, `HerdrClient.cs`; new concrete `HerdrNamedTabResolver.cs` with shared read-only workspace/tab selection and occupant decisions; `tests/Antiphon.SessionRunner.Tests/FakeHerdrServer.cs`. Add tab.list envelope and meaningful fake tab labels/counts/move/delete/race controls. Keep mutation in acquisition/child code. | `HerdrClientTests`, new `HerdrNamedTabPlacementTests`: wire schema, exact/case match, workspace scoping, duplicate/invalid tab, RPC failure never treated as absent. |
| S3: Resolution, ownership and reservations | `HerdrPaneChild.cs`, `SessionRunnerRuntime.cs`, `HerdrPaneSidecar.cs`, `HerdrLastPane.cs`; new instance-owned `HerdrPlacementCoordinator.cs`, registered/injected by runner composition. Implement D-3 through D-5; root label differs from pane title; cwd on named reuse; guard failed-launch cleanup before sidecar; preserve hint on refusal; exclude dedicated tabs from census. Existing script builder can be reused, with a small signature/name refactor if needed. | New placement tests through runtime, plus `HerdrLaunchShapeTests`, `HerdrPaneChildKillTests`, `HerdrAttachTests`, `HerdrPaneSidecarTests`, `HerdrAdoptionSweepTests`, `HerdrPaneAllocatorTests`. Include interleaved named launches and attach, stale prior IDs, borrowed shell cleanup and dedicated-tab exclusion. |
| S4: Public preflight and capability refusal | `SessionRunnerContracts.cs`; runner `Program.cs`, runtime and `HerdrProblemMapper.cs`; `server/Application/Interfaces/ISessionRunnerClient.cs`; `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs`; `AgentControlService.cs`. Read-only check before interactive-row/enqueue mutations, plus launch recheck/capability gate; update affected fake clients deliberately. | New `HerdrPlacementPreflightTests`; extend `AgentControlServiceIntegrationTests`, `AgentSupervisionTests`, `HerdrAlwaysOnChannelParityTests`, HTTP wire tests. Pin public 409 before queue versus race-time runner 409/Failed row; old runner refuses without posting named launch. |
| S5: Configuration UI | `client/src/api/agents.ts`, `client/src/features/agents/AgentCreateModal.tsx`, `AgentSettingsModal.tsx`; reuse text inputs and existing mutation hooks. | New `AgentHerdrPlacement.test.tsx`: create/settings round-trip, clear, backend visibility, labels kept on backend switch, save does not restart/move a live session. |
| S6: Owner docs and completion evidence | `docs/herdr-sessions.md` replacement table above; `docs/ops-http.md` and `docs/antiphon-api.md` document preflight/public error timing and runner route. Report acceptance results and #44 boundary. | Execute TestDesign's concrete tests and scoped regressions; verify optional isolated real-Herdr probe evidence if TestDesign calls for it. No production agent reconfiguration in Code tests. |

S1/S2 precede S3; S4 uses the shared resolver and S1 contracts; S5 uses S1 API fields. Do not release a partial combination that accepts `herdrTabLabel` but silently launches through an incapable runner. Land/migrate/restart through the repository's normal orchestration and main-checkout procedures, not from this plan worktree.

## Acceptance and TestDesign handoff

The brief does not fold TestDesign into Plan. The next stage must turn these observable requirements into named V/R/PC cases, including red-then-green discriminators. The following are required outcomes, not a claim that tests were run.

| Card acceptance | Required observation |
|---|---|
| Named standing start / AlwaysOn restart / fresh fallback lands on Orch in ExampleProject, creating it if missing. | Exercise first launch, same-ID restart, manual fresh/new ID, supervisor fresh threshold, and missing-native-target same-ID fallback. Put an eligible unpinned specialist tab and an old last-pane pointing there in the fixture. Assert selected workspace/tab labels and sidecar IDs, no split, and the correct tab/root creation count. Delete target pane/tab and repeat. |
| Existing idle labelled tab: no new tab or split elsewhere. | Seed an **operator-created** single-pane Orch with no sidecar and a different cwd. Assert same tab/pane IDs, zero tab.create/pane.split/tab.rename, quoted cwd/env launch script, and correct pane title independent of Orch. Repeat with attached-origin historical evidence. |
| Foreign process -> 409 pane_occupied, pin/hint retained, no allocator fallback. | Wrong kind, different ID, no argv ID, multiple processes, occupied Codex, live previous ID, bound/pending claim and non-PowerShell shell. Assert HTTP status **and code**, no pane close/kill/send/rename/metadata side effects, unchanged current/previous hint bytes and DB labels. Free the pane and retry successfully on the same named tab. Cover public preflight and a post-preflight race separately. |
| No tab label -> unchanged behavior. | Existing CARD-0224 last-pane/reuse/occupied tests and CARD-0323 workspace/root tests remain green. Null/absent wire field produces prior decisions/RPCs. Verify allocator still fills unpinned tabs and cannot split a live dedicated named tab, including after runner adoption. |
| Documentation and persistence. | Settings persist across session IDs/pane loss; null/empty update semantics are explicit; no DB ID columns; old sidecars deserialize; target-resolution documentation agrees with runtime tests. |

Additional discriminators: tab case and workspace-token precedence; duplicate and multi-pane refusal; pane-count/ID disagreement; tab-list error; concurrent duplicate launches; live claim versus historical previous hint; pre-sidecar cleanup when a foreign process appears; cancellation releases only transient claims; detect-timeout keeps names/hints; successful adoption types nothing; old runner feature capability refuses before wrong placement. Avoid tests that assert only that a helper returned an enum: drive the runtime/fake RPC boundary for mutation and cleanup assertions.

Use existing `FakeHerdrServer`, fake process liveness, unique temp session-log directories and class-scoped TUnit runs. Server integration fixtures must use the established production-runner guard or an isolated runner, never 17204. Process-spawning tests need the assembly-local `ParallelLimiter<ProcessSpawnLimit>`; global supervisor sweeps follow existing ungrouped `NotInParallel` isolation and assertions scoped to seeded rows. Use an advancing test clock for queue-dependent paths. Run `Antiphon.Tests` and `Antiphon.Agents.Pty.Tests` sequentially if both are needed.

Command pattern for Code (replace ClassName with the concrete touched class, one scoped run at a time):

```powershell
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c384/ -- --treenode-filter "/*/*/ClassName/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c384/ -- --treenode-filter "/*/*/ClassName/*"
pwsh -File scripts/test-client.ps1 AgentHerdrPlacement.test
```

No `dotnet test`, no production session starts/stops, no full-suite expansion merely for confidence. If a real-Herdr smoke is required to validate shell typing, use an isolated named Herdr session and disposable cwd; confirm launch identity from argv and transcript as applicable, not a screen redraw. TestDesign should specify exact setup, owned-process cleanup and pass criteria before Code runs it.

## Plan-stage validation and remaining work

Read the full CARD-0384 and CARD-0388 via `scripts/card.ps1 get`, traced the production launch/attach paths, and read the installed Herdr protocol schema. No builds or tests ran; there is no runtime implementation in this artifact. All decisions above are implementation defaults with reasons; no caller decision blocks TestDesign. The next stage should especially challenge the public 409 timing, named-pane ownership/cleanup, concurrency claims and the dedicated-tab census boundary before Code.
