# CARD-0462 investigation: Follow live Herdr tab rename into Agent herdrTabLabel (hourly, not in-agent)

Date: 2026-09-15. Task: a61aba72. Source inspected: b97abfd83819e2d1b705b4ed0527cfa8953b797f.

## Outcome

**Confirmed: live label changes have no observation-to-persistence path.** The runner keeps using the bound pane ID, while its sidecar and the server's configured pin retain launch-time labels. At the next launch, the named-label branch precedes last-pane reuse and creates a tab when the old label has no match. This mechanism was reproduced against unchanged production runner code using the repository's isolated `FakeHerdrServer`.

This confirms the behavior described by the card, not a historical production execution on `w2:tZ`. The historical agent returned 404 from the available server and the default Herdr socket was unavailable. No competing explanation is needed for the reproduced behavior. Remaining uncertainties are listed below; next stage is **Plan**.

## Evidence and reproduction

### Stored inputs and limits

- Full board description: [card.txt](evidence/card-0462/card.txt), captured through `pwsh -NoProfile -File scripts/card.ps1 get CARD-0462`. Card row `0b14040a-4382-44c0-9bd0-3a1156cd07b3`, board `8988ca03-7414-47ad-b0b6-51556c701703`, revision count 1 (`card.txt:2`, `card.txt:9`). The original need is recorded at `card.txt:12`; required behavior and exclusions are at `card.txt:26`, `card.txt:35`, `card.txt:45`.
- Available server: historical agent `2ee02f40-7b6d-48b1-96fc-c4344c651910` returned HTTP 404, independently checked twice. Installed CLI is Herdr 0.8.2; `herdr tab get w2:tZ` returned `server_not_running` at the default socket. [run-metadata.json](evidence/card-0462/run-metadata.json):2 records the capture and limitations. This does not establish the state of another named Herdr session or another deployment.
- [herdr-schema-excerpt.json](evidence/card-0462/herdr-schema-excerpt.json) preserves selected, unmodified schema objects from the installed CLI's `herdr api schema --json`. These are protocol definitions, not observed live RPC replies.
- [Program.cs](evidence/card-0462/Program.cs) and [Reproduce.csproj](evidence/card-0462/Reproduce.csproj) are evidence-only harness files. They compile the existing fake and use the existing friend-assembly name to call production runner code. They start no real provider or Herdr process and access no database. A dead OS-process probe prevents fake PID loss from authorizing a real OS kill. Backend label changes are fixture state changes; no Antiphon `tab.rename` is introduced.

### Measured result

One foreground harness execution at commit `af43de4a` completed with exit 0: **7 assertions passed, 0 failed**, including three existing-refusal fixtures. Build: 0 errors, 1 nullable-return warning (`CS8603`, evidence harness `Program.cs:96`). No TUnit suite was run; this is a mechanism reproduction, not full regression verification. Production files remained identical to `b97abfd83819e2d1b705b4ed0527cfa8953b797f`.

The complete seven-row output is [reproduction.jsonl](evidence/card-0462/reproduction.jsonl). Session `5bf305e5-1842-484d-b2f9-5ba43a01b69e`:

| Step | Observation | Stored evidence |
|---|---|---|
| Launch | `Running`, workspace `w1`, tab `w1:t2`, pane `w1:p2`, pin snapshot `Original tab` | `reproduction.jsonl:1` |
| Backend label edits; baseline plus GET | Same pane/tab, still `Running`; live label `Renamed tab`, sidecar `Original tab`; workspace labels also diverge | `reproduction.jsonl:2` |
| RPC census for that baseline plus GET | 4 `pane.get`, 2 `pane.process_info`, 1 `pane.read`; no tab/workspace label reads | `reproduction.jsonl:2` |
| Preflight using unchanged launch options | `Action=create`, workspace still `w1` | `reproduction.jsonl:3` |
| Simulated natural child loss, shell retained | Real liveness handler retires `w1:p2` with stale `Original tab` into last-pane | `reproduction.jsonl:4` |
| Restart same session/options | Exactly 1 `tab.create`; new `w1:t3` / `w1:p3` labelled `Original tab`; renamed `w1:t2` remains | `reproduction.jsonl:5` |
| Existing guards | Duplicate Windows-equivalent labels -> `herdr_tab_ambiguous`; reported or enumerated pane-count mismatch -> `herdr_tab_invalid` | `reproduction.jsonl:6` |

The workspace in this reproduction was token-owned. Renaming its label did not create a workspace: workspace count stayed 1. Thus the extra tab is attributable to the stale tab pin even when workspace identity remains sound. The harness supplies unchanged launch options directly; the DB-to-options portion below is reconstructed from source, not claimed as an exercised server integration.

Rerun from the repository root on Windows, with a fresh alternate output name if `bin-c462/` already exists:

```powershell
dotnet run --project docs/investigations/evidence/card-0462/Reproduce.csproj --property:OutputPath=bin-c462/
```

The run's seven producer-owned `bin-c462` directories were inventoried. Automatic approval review rejected both a validated inventory-based cleanup and a narrower command naming the exact absolute directories, with the reason `blocked by policy`. They and the harness's `obj` directory remain ignored in this worktree; the exact paths are retained in `run-metadata.json` for caller cleanup. No stack restart, deployment, production label change, real pane disposal, or provider turn was performed.

## 1. Current observation path and RPC facts

### Herdr event pump

`src/Antiphon.SessionRunner/HerdrEventPumpService.cs:31` runs only when enabled, waits when no live panes exist, recycles its subscription when the pane set changes, and reconnects with configured 1-to-30-second backoff. `RunOneStreamAsync` calls `BaselineSweepAsync` before subscribing (`:111`, `:119`). There is **no periodic baseline timer** inside a healthy unchanged subscription.

`BuildSubscriptions` requests global `pane.closed`, global `pane.exited`, and per-pane `pane.agent_status_changed` (`:133`). `HandleEventAsync` recognizes close/exit and the underscored or dotted status spellings; every other event is ignored (`:146`, `:179`). Close/exit triggers liveness verification; status changes update the cache and may publish `SessionAgentStatus`. Neither path observes labels.

`BaselineSweepAsync` reads `pane.get`, applies `AgentStatus`, then calls `VerifyHerdrLivenessAsync` (`HerdrEventPumpService.cs:182`). That verifier reads `pane.get` again and, when the sidecar has `ChildPid`, `pane.process_info` (`SessionRunnerRuntime.cs:1605`). It does not compare the returned workspace/tab IDs or read their labels. Its runtime bar does not call `pane.read`; the stronger adoption documentation must not be mistaken for this exact call sequence.

### Session GET refresh

`SessionRunnerRuntime.GetAsync` runs `TryStampHerdrVerifiedAsync`, then `RefreshHerdrSurfaceAsync` (`src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:710`). Verification reads `pane.get` and optionally `pane.process_info` (`:2410`). Refresh delegates to `HerdrPaneChild.RefreshStatusAsync`, which reads **both** `pane.get` and `pane.read` with `source=visible`, `stripAnsi=true` (`src/Antiphon.SessionRunner/HerdrPaneChild.cs:1071`). It returns only revision, content-sequence, and agent status. Workspace/tab identity from `pane.get` is discarded here.

The caller updates sequence/status only; status publication is explicitly suppressed for GET (`SessionRunnerRuntime.cs:1556`, `:1563`). `List()` only converts cached state to DTOs (`:700`). The observed four-plus-two-plus-one RPC count agrees with baseline and single-session GET combined.

Idle sessions already have callers independent of provider turns: `RunnerTerminalSession` starts an exit-monitor loop on Start/Attach (`server/Infrastructure/Agents/SessionRunner/RunnerTerminalSession.cs:41`, `:86`), which calls GET and waits 250 ms after each iteration (`:267`). That loop exits on any exception. Herdr corroboration separately queries Running Herdr sessions and performs a GET (`server/Application/Services/HerdrStatusCorroborationService.cs:54`, `:105`), scheduled by `server/Infrastructure/Supervision/AgentSupervisorHostedService.cs:195`. These are source-established triggers, not measurements of this unavailable historical session's actual polling cadence. General reconciliation usually uses the cheap list and only selectively GETs (`server/Application/Services/SessionReconciliationService.cs:125`, `:182`, `:497`).

### Label-read surface already available versus absent

| Surface | Current availability and data |
|---|---|
| `pane.get {pane_id}` | Typed wrapper exists at `HerdrClient.cs:332`. `HerdrPaneInfo` has `PaneId`, `TabId`, `WorkspaceId`, `TerminalId`; `Label` is the pane label, not the tab label (`HerdrApiModels.cs:33`). |
| `tab.get {tab_id}` | Installed schema defines the request and `result.type=tab_info`, `result.tab`; there is no typed `TabGetAsync` wrapper in `HerdrClient`. Existing `HerdrTabInfo` already models tab/workspace IDs, label, and pane count (`HerdrApiModels.cs:24`). |
| `workspace.get {workspace_id}` | Installed schema defines the request and `result.type=workspace_info`, `result.workspace`; there is no typed `WorkspaceGetAsync` wrapper. Existing `HerdrWorkspaceInfo` models ID, label, and tokens (`HerdrApiModels.cs:13`). |
| `tab.list`, `pane.list`, `workspace.list` | Typed wrappers exist (`HerdrClient.cs:187`, `:338`, `:139`). Named resolution and workspace matching already use these enumerations. A GET of one tab/workspace alone cannot establish label uniqueness among its siblings. |
| Generic RPC transport | `HerdrClient.SendRequestAsync` is public (`:91`). Missing typed getters are a client-surface gap, not absence of the RPC in the installed protocol. |

All filenames in that table are under `src/Antiphon.SessionRunner/`. Exact selected request schemas: `evidence/card-0462/herdr-schema-excerpt.json:6`; response envelopes `:91`; identity/label fields `:141`, `:159`, `:175`.

## 2. Pin ownership, persistence, and server boundary

### Current reads and writes

- `Agent.HerdrWorkspaceLabel` / `HerdrTabLabel` are nullable agent settings, each max 256 characters (`server/Domain/Entities/Agent.cs:70`, `server/Infrastructure/Data/AppDbContext.cs:848`). They contain names, not Herdr IDs.
- Production assignments are in agent create (`server/Application/Services/AgentService.cs:505`) and ordinary update (`:675`). Update stamps `UpdatedAt`, saves, then publishes `AgentChanged` (`:681`, `:690`, `:695`). `NormalizeHerdrLabel` trims, maps blank to null, and rejects controls or length over 256 (`:1555`). PATCH null means omitted; empty string clears. No runner-observation writer was found in the source census.
- DTO mapping exposes both labels (`AgentService.cs:1196`, `:1285`; `server/Application/Dtos/AgentDtos.cs:75`, `:135`, `:329`, `:396`). Client create submits trimmed labels; settings submit only changed labels and use empty string to clear (`client/src/features/agents/AgentCreateModal.tsx:156`, `AgentSettingsModal.tsx:182`, `:209`).
- `HerdrLaunchContextResolver.ApplyStandingOverrides` reads configured labels into `HerdrLaunchOptions`, leaving `WorkspaceKey` unchanged. It excludes card-owned sessions, missing agents, and pool delegates (`server/Application/Services/HerdrLaunchContextResolver.cs:87`). Public preflight composes options through this resolver (`AgentControlService.cs:398`); launch does too (`AgentSessionService.cs:1632`).
- The runner copies these options into its sidecar at launch (`src/Antiphon.SessionRunner/HerdrPaneChild.cs:985`, `:1015`). The fields explicitly describe launch snapshots (`HerdrPaneSidecar.cs:48`). `HerdrPaneChild.Sidecar` exposes an immutable record; the private cached `_sidecar` is distinct from the disk file (`HerdrPaneChild.cs:27`, `:65`). Merely overwriting the file would not replace the cached record.
- Retirement copies sidecar labels to last-pane (`HerdrLastPane.cs:46`, `:62`); timeout retirement instead copies launch options (`:70`, `:93`). Attached-origin retirement writes no last-pane (`HerdrPaneSidecar.cs:131`). `LoadAll` enumerates only the active sidecar directory (`:158`); it does not refresh last-pane records.
- A nonblank sidecar `TabLabel` also marks a dedicated named tab for allocator reservation (`SessionRunnerRuntime.cs:585`). A workspace snapshot is not proof that the agent explicitly configured a workspace pin: resolver defaults populate it too. These existing meanings explain the card's no-new-pins requirement.

### Existing runner-to-server connection

The present event chain is:

`ApplyHerdrAgentStatus` -> `SessionRunnerEventHub` -> server HTTP SSE parser -> server `SessionRunnerEventPump` -> `AgentSessionRuntime.ObserveAgentStatusAsync`.

Evidence: `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:1593`, `:3245`; `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:620`; `SessionRunnerEventPump.cs:87`; `server/Application/Services/AgentSessionRuntime.cs:430`.

That server handler creates a DB scope and resolves the current agent through `Agent.PersistentSessionId == sessionId.ToString("D")`; it publishes UI invalidation and may nudge an idle-checked queue flush. **It does not write pins.** Current `RunnerAgentStatusEvent` contains only session ID, statuses, and time (`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:277`). `RunnerSessionDto` likewise has no label/placement observation (`:184`). There is therefore no existing event or GET payload that supplies a verified rename to a server pin writer. The server owns the existing DB update capability; the runner does not have an `AppDbContext` or the agent's nullable-pin intent.

Current standing-owner evidence includes the mutable `PersistentSessionId` pointer and historical physical `AgentSession.StandingAgentId` (`server/Domain/Entities/Agent.cs:127`, `AgentSession.cs:8`). The sidecar carries `AcceptedStartedAt` (`HerdrPaneSidecar.cs:52`); general reconciliation compares accepted generations (`SessionReconciliationService.cs:470`). Current status events do not carry that generation. These are existing identity facts for Plan to assess, not a proposed rename contract.

The event hub only broadcasts to currently subscribed channels (`SessionRunnerRuntime.cs:3272`); the server pump's reconnect catch-up backfills transcripts (`SessionRunnerEventPump.cs:36`, `:111`). Neither constitutes durable label delivery or a pin-update acknowledgment.

## 3. Rename versus move; workspace rules

The current stored identity tuple is workspace ID, tab ID, pane ID (`HerdrPaneSidecar.cs:23`). `pane.get` provides the current tuple. The card's rename case is an existing bound pane with unchanged tab identity and a different current tab label; the excluded move case has changed tab identity (`evidence/card-0462/card.txt:35`). Launch acquisition already detects a change in either tab or workspace ID and refuses `herdr_pane_changed` (`HerdrPaneChild.cs:439`, `:451`). Ordinary refresh does not perform this identity comparison.

The installed schema also declares `tab_renamed` with tab ID/workspace ID/label and `workspace_renamed` with workspace ID/label (`herdr-schema-excerpt.json:189`). It declares `pane_moved` with `previous_pane_id`, `previous_tab_id`, `previous_workspace_id`, and the new pane object (`:237`). **Do not assume all moves preserve pane ID:** the schema explicitly allows old/new identities to be represented. Real move behavior and rename-event delivery were not exercised. A missing old pane is not positive evidence of a rename. The event names are schema facts; the pump currently neither subscribes to nor handles them.

Workspace selection is token-first (`HerdrPaneChild.cs:1367`, `:1429`):

1. Exact `antiphon-ws` token equal to `WorkspaceKey` wins even after a label edit; managed launches refresh that token.
2. Otherwise exactly one **untagged** workspace with an ordinal-equal label is reused, without stamping ownership (`:1375`). Non-empty means non-whitespace token (`:1444`).
3. No match, multiple untagged matches, or only foreign-tagged matches reaches workspace creation (`:1387`, `:1409`). Read-only preflight returns no existing workspace in those cases.

The card only authorizes following workspace labels for untagged operator workspaces when the unique-untagged match still holds; token-owned workspaces already survive renaming (`card.txt:39`). Workspace equality is ordinal even on Windows. Tab equality uses the host comparer described next. Reordering tabs/workspaces and changing IDs are distinct from renaming; this investigation did not perform any of those operations.

## 4. Existing ambiguity and invalid-tab refusal

`src/Antiphon.SessionRunner/HerdrNamedTabResolver.cs:24` selects `OrdinalIgnoreCase` on Windows and `Ordinal` elsewhere. Matching uses exact whole labels under that comparer and ordinal workspace ID equality (`:27`). It does not trim Herdr's returned labels or perform substring matching. Therefore a case-only tab edit on Windows does not cause the missing-match/new-tab branch; the main reproduction changes the whole label.

`PickUniqueSinglePaneTab` (`:47`) returns null for zero matches, throws `herdr_tab_ambiguous` for more than one, and requires **both** `HerdrTabInfo.PaneCount == 1` and exactly one enumerated pane whose tab/workspace IDs match. Either count failing throws `herdr_tab_invalid` (`:56`, `:66`, `:72`). Launch uses this resolver before creating a tab (`HerdrPaneChild.cs:415`); runner preflight uses it too (`SessionRunnerRuntime.cs:647`). These refusals exist for launch/preflight, not for live pin following, which does not exist.

The harness exercised duplicate `new`/`NEW` labels under the Windows comparer, reported count 2 with one enumerated pane, and reported count 1 with two enumerated panes. All produced the expected refusal codes (`reproduction.jsonl:6`). Existing test coverage names include `HerdrNamedTabResolverTests.Two_matches_are_herdr_tab_ambiguous_listing_both_ids` and `Multi_pane_tab_or_count_disagreement_is_herdr_tab_invalid` (`tests/Antiphon.SessionRunner.Tests/HerdrNamedTabResolverTests.cs:40`, `:63`); those test methods were inspected, not executed in this investigation.

## 5. Cooldown precedents and their limits

No Herdr rename-follow cooldown, timestamp, or one-hour default exists in `HerdrSettings` (`src/Antiphon.SessionRunner/HerdrSettings.cs:7`) or the pin read/write paths above. One hour is the card's requested new behavior (`card.txt:41`). Existing patterns are:

| Precedent | Scope and behavior | Limitation relevant to this card |
|---|---|---|
| `WatchdogCooldownStore.TryRecord` | Singleton; concurrent dictionary keyed by session/rule; compare/update permits one entrant; explicit supplied UTC/time window (`server/Application/Services/WatchdogCooldownStore.cs:9`; registration `server/Program.cs:417`) | In-memory; active-state latch blocks independently of elapsed time until cleared. It is not a plain periodic throttle or durable record. |
| `PolicyRefreshService` | Singleton per-agent attempt timestamps, injected `TimeProvider`, plus recent DB incident lookup (`server/Application/Services/PolicyRefreshService.cs:34`, `:503`, `:544`, `:594`) | Default is 30 minutes, min 5 (`server/Application/Settings/SupervisionSettings.cs:82`). Incident history and policy-specific gates are part of its semantics. |
| `HerdrStatusCorroborationService` | Per-session sustained-status interval and last incident since the current status episode (`server/Application/Services/HerdrStatusCorroborationService.cs:120`, `:138`) | Episode hysteresis, not a once-per-hour label refresh. |
| `HerdrStatusPushService` | Per-session state, debounce, heartbeat; runner-local example independent of provider turns (`src/Antiphon.SessionRunner/HerdrStatusPushService.cs:19`, `:35`, `:100`) | Display-metadata push to Herdr; no DB pin update or shared server cooldown. |

These establish available techniques and tradeoffs; this investigation does not select or adapt one. The existing placement coordinator has an explicit workspace-key -> workspace-ID -> pane lock order and only serializes Antiphon's own actions (`src/Antiphon.SessionRunner/HerdrPlacementCoordinator.cs:22`). It does not prevent an operator changing Herdr between separate RPCs.

## Remaining uncertainties and Plan inputs

1. **Historical deployment:** no live PM agent row, live sidecar, or original before/after transcript was available here. Resolving historical attribution requires the deployment containing that agent and its stored sidecar/Herdr observations. This does not affect the isolated confirmation of the current mechanism.
2. **Live protocol behavior:** `tab.get`/`workspace.get` envelopes and rename/move events are schema-backed; real replies, event replay behavior, and move-induced ID changes remain unmeasured in this environment. A read-only capture from a running isolated Herdr would resolve the getter portion; a separately owned fixture can establish live rename/move behavior.
3. **Cooldown semantics:** per-agent versus per-session, whether attempts or successful changes consume the hour, persistence across server/runner restart, and treatment of a second rename inside the hour are not fixed by current code. A cooldown does not itself guarantee follow-before-restart during that hour. The card permits either key and requires a shared cooldown if two triggers exist.
4. **Ordering and stale observations:** current sidecar/disk/last-pane state is separate from the DB, and current SSE has no durable rename acknowledgment. Plan must specify behavior across disconnects, same-ID relaunch generations, current-owner changes, and a concurrent manual pin edit. No such behavior is proven by this harness.
5. **Validation:** Herdr schema labels are strings; agent persistence trims and rejects controls/overlength, with blank meaning clear. The card forbids inventing pins; blank/unrepresentable live labels and nullable workspace pins need an explicit acceptance/refusal interpretation. Current ordinary agent update is not evidence of a conditional rename-follow write.
6. **Verification extent:** attached-origin, unpinned/card/pool exclusions, unique-untagged workspace rename following, cooldown timing, and server persistence were source-traced or requested by the card, not executed as a future feature. There is no implementation to certify for those cases.

Scope remains the card's existing pins and live label changes. Leftover-pane closing, allocator policy changes, creating pins, moving panes, and Antiphon-initiated `tab.rename` remain excluded (`card.txt:45`). No card or live agent was modified.

## Not done, noted

Fix idea only: carry verified runner label observations to a server-owned update of existing eligible pins with the card's shared one-hour cooldown; architecture, implementation, and feature tests are left to Plan.

--- next stage ---
next: plan
handoff: Plan CARD-0462 from the confirmed fake-Herdr reproduction: missing live label observation/persistence leaves named pins stale and creates a new tab before last-pane reuse. Preserve identity, ownership, ambiguity and single-pane guards; resolve cooldown, stale-write and reconnect semantics without changing placement scope.
artifact: docs/investigations/2026-09-15-card-0462-follow-live-herdr-tab-rename-into-agent-herdrtablabel-hourly-not-in-agent.md
