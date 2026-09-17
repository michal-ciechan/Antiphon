# CARD-0462: follow live Herdr labels into existing standing-agent pins

Date: 2026-09-15. Plan task: `a3cb174d`. Source: `7c02746584daabb8303a96dae23a6a87889d470e`.

Status: implementation plan and folded verification design complete **under the proposed defaults D-2, D-3, D-4 and D-8**. Next stage is **Decide** to accept those defaults, then Code using this verification section; no separate TestDesign dispatch is needed. No production code or live configuration changed in this task.

## Outcome and boundaries

Follow a label edit on the same live, launched-origin pane and tab into an already configured standing-agent pin. The runner observes Herdr and maintains its snapshots; the server alone changes `Agent.HerdrTabLabel` / `HerdrWorkspaceLabel`. A later named launch then resolves the new name to the original tab. No model turn or agent self-PATCH participates.

The investigation [confirmed the stale-label mechanism](../../investigations/2026-09-15-card-0462-follow-live-herdr-tab-rename-into-agent-herdrtablabel-hourly-not-in-agent.md) with seven passing assertions against unchanged production runner code. Its landed evidence is present at this plan's source HEAD (the brief named the pre-landing investigation commit `7eb3aa53`). Those assertions are investigation evidence, not execution of the tests below. Real getter replies and historical production state were not measured by that investigation.

Acceptance example: launch pinned `Old` in pane P / tab T; an operator renames T to `New`; the next eligible observation validates P/T, uniqueness and shape; the server persists `New`; after natural child loss, Start reuses T with zero additional tab/workspace creation. Renaming twice within the hour coalesces to the name observed at the next eligible check.

This is hourly eventual following. It cannot promise that an abrupt exit or immediate relaunch between a rename and its next eligible check will reuse the renamed tab. No pre-Start cooldown bypass is introduced. A workspace with no explicit pin and no surviving managed token also has no authorized workspace-pin repair in this card.

Out of scope: leftover-pane disposal (#46), allocator packing changes, new pins, moves of live panes, follow for attached-origin panes, new Antiphon-initiated `tab.rename`/`workspace.rename` calls, provider instructions, UI controls, and automatic deployment/restart of production services. Existing launch-time rename calls remain unchanged.

## Ground truth

Paths below are relative to the repository. Method names are the durable anchors when line numbers move.

| Card assumption / need | What this checkout does | Design consequence |
|---|---|---|
| Bound pane continues working after rename | `HerdrPaneChild` addresses the pane by ID; `HerdrPaneSidecar.TabLabel` is a launch snapshot | Observe names without changing placement identity. |
| Next Start should reuse renamed tab | `HerdrPaneChild.StartAsync` resolves named labels before last-pane; a missing label creates a tab | DB pin persistence is necessary; editing only last-pane cannot fix it. |
| Baseline periodically notices changes | `HerdrEventPumpService.RunOneStreamAsync` baselines before subscribing; a healthy unchanged stream has no timer | Add an owned timer; reconnect-only baseline is insufficient. |
| `pane.get` knows the tab name | `HerdrPaneInfo.Label` is a pane label. `RefreshStatusAsync` discards returned tab/workspace identity | Add typed `tab.get` / `workspace.get`; retain and compare the identity tuple. |
| Getter methods already exist | `HerdrClient` has list methods, but neither typed getter. Installed protocol-20 schema has `tab_info/tab` and `workspace_info/workspace` envelopes | Add wrappers and literal wire-contract tests; do not invent a rename event dependency. |
| One GET proves uniqueness and shape | `HerdrNamedTabResolver.PickUniqueSinglePaneTab` uses tab and pane enumerations, requires both pane counts equal one | Reuse resolver and additionally require the selected tab/pane to be the bound IDs. |
| All names use the same comparison | Tabs use host comparer (Windows ordinal-ignore-case); workspace fallback uses ordinal equality | Preserve both; use ordinal equality for detecting spelling changes. |
| Workspace label always identifies a configured pin | Resolver supplies project/catch-all labels even with a null agent pin | Carry explicit nullable pin intent separately from effective launch labels. |
| Managed workspace renames need following | `EnsureWorkspaceAsync` / `FindWorkspaceAsync` prefer `antiphon-ws == WorkspaceKey` over labels | Do not change managed workspace pins. Record how the workspace was selected. |
| Runner can persist agent pins | Runner has no DB; assignments are currently in `AgentService.CreateAsync` / `UpdateAsync` | Add a small server service, not DB access in runner or agent self-PATCH. |
| Existing SSE provides reliable rename delivery | Status events carry no labels/generation; hub broadcasts only to subscribers; reconnect catch-up covers transcripts | Use replayable GET state and a server sweep; no new lossy SSE delivery requirement. |
| Agent ordinary update already serializes this race | `AgentService.UpdateAsync` normally loads an unlocked row; only specialist paths take a transaction/owner lock | Explicit placement edits and follow writes need common row serialization and an edit token. |
| Session ID is enough | Same ID can be relaunched; `AcceptedStartedAt` is the normalized accepted generation. Legacy sidecars may lack it | Require the immutable generation and physical standing owner; never fill missing evidence from current DB state. |
| Liveness `true` means verified | `VerifyHerdrLivenessAsync` deliberately returns true on unreachable (no death verdict) | Do not treat that boolean as positive evidence for label following. |
| Saving a sidecar updates the live object | `HerdrPaneChild._sidecar` is a separate immutable record; retirement reads disk; runtime can retain a retired locator | Publish the saved record into the live child under lifecycle coordination. |
| Last-pane can always be patched | Last-pane lacks an accepted generation today; attached exit writes none | Add optional generation metadata and condition updates on exact binding; never create last-pane for this feature. |
| Hourly behavior already has a setting | No label-follow timestamp or setting exists | Add one runner cooldown shared by all observation triggers, plus a cheap server polling interval. |

## Decisions

### D-1. Runner observations, server writes, replayable GET transport

Add a nullable versioned label observation to `RunnerSessionDto` and its server mapping. Baseline, surface refresh and the timer invoke one runner observer. A dedicated server hosted sweep reads the current eligible standing sessions through `ISessionRunnerClient.GetAsync`, then applies their observations through `HerdrLabelFollowService`.

Use existing GET transport, not a new SSE event or a generic PATCH endpoint. This avoids an acknowledgement/outbox protocol for a replaceable snapshot. A lost GET or failed DB write is retried by the next sweep. `List()` remains cheap and never itself authorizes a pin write. The runner still has no DB dependency.

Rejected: writing through status events (no replay/generation), exposing arbitrary label PATCH from runner, injecting the server DB into runner, agent prompts, and making server contact the Herdr socket directly.

### D-2. Proposed default: hourly observation attempts per bound generation

Key the cooldown by `(SessionId, AcceptedStartedAt)`. Default `SessionRunner:Herdr:LabelFollowCooldownMinutes = 60`, positive integer validated at startup. Inject `TimeProvider`; do not add real-hour waits in tests.

First eligible check in a newly launched generation is immediate. Every admitted attempt consumes the interval, including equal labels, ambiguity, invalid shape and backend failure. Persist the attempt claim before getter I/O. Concurrent baseline, GET, timer and any future turn-end nudge share that claim. Runner/server reconnect does not reset it. A newly accepted launch generation may check immediately; this is a deliberate interpretation of the card's per-session option.

Reason: limits the added Herdr work even when session GET runs roughly four times per second, and avoids storms while a bad label persists. Equal labels cause no pin write or `AgentChanged`, although the observation checkpoint advances. Failure never changes labels. A rename during cooldown is read afresh at the next due attempt, not queued as an old event payload.

Rejected: successful-write-only throttling (unbounded reads/no-op attempts), separate trigger cooldowns, process-local timestamps (restart bypass), and delaying the very first observation for an hour. Decision consequence: a repaired backend/ambiguity can wait the remainder of the hour. An explicit faster retry policy would be a change to this default.

### D-3. Proposed default: independent 60-second wakeups; omit optional turn-end hook

Add a 60-second runner label-sweep timer alongside the Herdr event stream. The timer checks due state and skips all label RPCs while cooling down. It lives for the pump's hosted lifetime, including stream reconnects; stream recycling does not reset scheduling. Await and cancel both tasks on shutdown, with no detached tasks. Baseline and GET remain direct triggers.

Add server `HerdrLabelFollowSettings` with `Enabled = true` and `SweepPeriodSeconds = 60`, independent of `HerdrCorroboration.Enabled`. Sweep only live, cardless, non-pool standing Herdr agents with at least one configured pin. One unreachable session must not stop the rest. No dependency on AlwaysOn, channel binding, working/idle transcript state or a provider completing a turn.

Bound each admitted label collection, including lease acquisition, by `SessionRunner:Herdr:LabelFollowObservationTimeoutSeconds = 10` (positive, startup-validated), using a linked cancellation token and the injected clock. `HerdrClient.ConnectTimeoutMs` only bounds connection, not the complete multi-RPC read. A timed-out collection consumes its attempt and publishes no candidate; it cannot hold the pane lease indefinitely or fail the surrounding status refresh. Concurrent callers that find an observation already in flight skip the label work rather than queue another collection.

The optional `OnTurnEnd` nudge is omitted from the initial implementation: timer plus GET already covers both active and idle seats. If later added, it may only call the same GET/service path and cannot reset/bypass the runner cooldown. Healthy-system upper bound is approximately one cooldown plus two sweep periods, excluding I/O outages and processing duration.

### D-4. Proposed default: an explicit manual placement edit wins for this generation

Add internal `Agent.HerdrPlacementEditToken` (GUID, historical default `Guid.Empty`; not an API-editable field). A new launch carries this token, physical `StandingAgentId`, and explicit nullable workspace/tab pin snapshots. Additive absent metadata means ineligible, not guessed defaults.

Any explicit non-null placement-label field in `UpdateAgentRequest`, including clear or reasserting the current spelling, changes the token. Backend/board placement-context changes also invalidate it. Omitted/null PATCH fields do not. Serialize these writes on the agent row, preserving the existing specialist owner-before-seat lock order. Read/reload under that lock; explicit supplied labels must actually be written even if EF originally read the same value. Ordinary unrelated name/details edits do not invalidate following.

The automatic writer requires the launch token to equal the current token. It never rotates it. Once manually edited, neither pin is followed from this generation, even if the operator changes a value away and back. The next launch uses the new configuration and establishes a fresh binding. Runner snapshots may still describe the old physical pane; they cannot override the manually configured next destination.

Reason: manual settings already mean “next launch,” so following the currently occupied tab must not undo that instruction. One token for both placement fields is intentionally conservative. Rejected: `UpdatedAt` (unrelated edits), name-only compare-and-swap (ABA and clear/re-pin races), or resetting the cooldown on manual edit (can immediately undo it). Per-field re-arming without relaunch is deferred.

### D-5. A validated observation describes the original binding, not a new target

Require positive current reads for the bound pane, tab and workspace. All IDs must equal the sidecar's immutable IDs, and the tab's workspace must match. Missing old pane, changed tab or changed workspace is a skip, never a rename. Do not discover a replacement by label or `previous_pane_id`.

For any pin follow, use `HerdrNamedTabResolver` over `tab.list` and `pane.list`, matching the raw observed tab label. Require one host-comparer label match, reported count one, enumerated count one, and the selected tab AND sole pane equal the bound IDs. Zero matches is an inconsistent observation, not permission to create. Duplicates use `herdr_tab_ambiguous`; both count failures use `herdr_tab_invalid`. Applying the card's shape refusal to workspace-only pins is deliberately conservative; it does not change allocator packing.

Record workspace selection provenance at launch: `ManagedToken` (including newly created managed workspace) versus `UniqueUntaggedLabel`. Only the latter can follow an explicitly configured workspace pin. At observation time require no non-whitespace `antiphon-ws` on that workspace, exactly one ordinal-equal untagged match for its new label, and no matching WorkspaceKey token elsewhere that would preempt it on next launch. Check that selected workspace ID is the bound ID. A managed workspace whose token later expires must not become an operator workspace by inference.

Token-owned/foreign-tagged workspaces keep their configured workspace pin and `WorkspaceLabel` snapshot; a valid tab rename may still follow there. For an eligible untagged workspace whose label changed, an ambiguous workspace resolution blocks the whole placement update for that observation (including a simultaneous tab rename). A workspace-only pinned agent may follow a unique untagged workspace rename when its current tab passes that shape check; its `TabLabel` stays null. If packing has made the tab multi-pane, skip the follow and leave packing unchanged.

Returned labels must already be representable exactly by agent settings: nonblank, at most 256 UTF-16 units, no controls, and equal to their own trimmed form. Reject rather than silently trim Herdr's label into a different destination. A case-only tab edit is accepted as a spelling update when unique under the host comparer. Do not create or clear a pin from a live label.

Re-read the pane/tab/workspace identity and relevant labels at the end of collection; require list/get agreement and unchanged values. External Herdr operations do not honor Antiphon's locks, so this bounds rather than eliminates the final external race. The normal next-launch preflight remains the last occupancy/ambiguity guard.

### D-6. Ordered, expiring observations; GET rechecks current identity

Each persisted attempt increments a per-generation sequence and invalidates the preceding actionable result before I/O. Successful completion stores the validated result, completion time and next-due time. A refusal/failure stores a reason with no actionable labels. A later failure must not leave a previous successful candidate actionable.

The GET DTO includes the complete binding stamp, sequence, completion time, expiry, explicit pin intent, validated candidate fields and result code. It is returned as actionable only for a currently live launched-origin binding with a positive pane/child check in this request and matching current pane IDs. In particular, unreachable is not positive evidence; a pane move found by an ordinary `pane.get` suppresses a cached candidate even during cooldown. No such suppression follows new IDs or changes the old pin.

The server accepts only the exact current session/accepted generation, current physical standing owner, launch edit token, and sequence newer than the stored watermark for that binding. Reject missing metadata, expired observations and completion times more than 30 seconds in the server's future; use the shared observation expiry, not two independently configured cooldowns. Clock disagreement logs a skip. No timestamp establishes ownership.

Persist server watermark fields on Agent: nullable `HerdrLabelFollowSessionId`, nullable `HerdrLabelFollowStartedAt`, and `HerdrLabelFollowSequence` (default zero). These are Antiphon identities, not Herdr IDs. Process valid no-change/refusal observations once as well, without changing `UpdatedAt`. Out-of-order or duplicate snapshots never roll a pin back.

### D-7. Snapshot persistence is separate from configuration intent

Add one optional versioned follow-state record to the sidecar: immutable launch intent/provenance, last attempt, sequence, latest completed observation, and matching-last-pane repair state. Preserve absent/null compatibility. `TabLabel` remains null for every unpinned launch: observed raw tab names belong in the observation, never in the allocator's dedicated-tab marker.

For an accepted eligible rename, update sidecar label fields and the child's cached record together: persist atomically first, then replace the immutable in-memory record. A failed save publishes no success. Use unique temporary files; concurrent saves must not collide on a common `.tmp` name. Do not hold the runtime's synchronous `_gate` across I/O.

Use the existing pane placement lease for the short read/validate/persist operation and coordinate with retirement, detach, kill, adoption and disposal. Preserve workspace-key -> workspace-ID -> pane lock order; never reacquire a pane lease recursively through the liveness helper. Recheck object/generation ownership before publishing. A completed exit wins over any delayed observer; it must not resurrect a deleted sidecar. This changes metadata coordination, not kill/exit decisions.

Add optional `AcceptedStartedAt` to `HerdrLastPane` and copy it on retirement. If a last-pane already exists for the exact session/generation/workspace/tab/pane and launched origin, refresh its eligible labels. Never create it just to follow, never overwrite a different or legacy-unproven binding, never change retention time/exit reason. Persist sidecar first; if matching last-pane write fails, retain a repair marker and retry that local repair on subsequent wakeups without another Herdr observation or cooldown reset. Clear it only after success or proof the record no longer belongs to this binding. Retirement must copy the current committed labels, not a captured old sidecar object.

### D-8. Proposed default: reconnect catches up without bypassing cooldown

Server restart: next 60-second sweep pulls the runner's latest still-current observation; watermark makes duplicate delivery harmless. Crash before DB commit leaves the update owed; crash after commit but before UI notification leaves the DB correct and the agent detail/list GET authoritative.

Runner restart: restore attempt time/sequence and intent from sidecar, complete existing adoption first, and suppress pre-restart cached observations until a new due positive validation. Do not reset cooldown or promote old files to new intent. Even if sidecar labels already equal live labels, a later observation still carries the complete candidate to repair a server that missed the earlier one. This can delay recovery by the remaining hour and is the proposed conservative default.

Herdr stream reconnect: baseline calls the same due check. Historical rename/close/status events never supply name evidence. A healthy stream plus no turns is covered by the timer. Legacy sidecars without follow intent/generation remain launch-compatible but do not auto-follow until a new ordinary launch establishes metadata; do not restart agents merely to backfill it.

### D-9. Conditional server transaction and ordinary UI invalidation

`HerdrLabelFollowService` fetches runner state outside DB locks. Then lock/reload the current Agent (existing specialist ordering where applicable), revalidate eligibility/owner/pointer/edit token and current session row. The write must also condition on that session still having the observed normalized generation and live status; do not carry a pre-network tracked entity into SaveChanges. Start reservations already serialize through the agent row. Do not introduce a reverse session-then-agent lock order; this service does not mutate or lock the session row.

Update only already non-null eligible pins and the watermark in one DB transaction. A conditional update losing its predicate is a skip with no retry of the old candidate. Explicit pin edits use the same agent serialization, so manual-before-follow refuses the follow and follow-before-manual leaves the manual value last. Existing attached/card/pool/non-Herdr/current-owner guards are checked server-side even if a forged/test DTO claims eligibility. Physical owner must equal both `AgentSession.StandingAgentId` and the launch owner; no name, cwd, slug or mutable pointer alone proves it.

Only an actual label change stamps `Agent.UpdatedAt` and publishes the existing `AgentChanged` through `IEventBus` after commit. No new alert sink, chat message, supervision incident, process start or runtime state transition. Log bounded structured result codes with session/agent/generation/sequence and safe IDs; no transcript, argv or environment payload. Log refusal once per observation, not once per 250 ms GET.

## Implementation slices

Commit and push each completed slice with its actual verification outcome. New file names below are the intended ownership boundaries; merge small helpers into existing files only if tests keep those boundaries clear.

### S1 — explicit intent, edit precedence and additive contracts

- `server/Domain/Entities/Agent.cs`, `server/Infrastructure/Data/AppDbContext.cs`: edit token and three watermark fields. CLI-generate migration plus designer/model snapshot in `server/Migrations/`; no Herdr ID columns and no changed label values in backfill.
- `server/Application/Services/AgentService.cs`: serialized explicit-edit token changes and actual supplied-label writes; preserve specialist locks, null/clear behavior and unrelated edits.
- `server/Application/Services/HerdrLaunchContextResolver.cs`: attach physical standing owner/token and nullable explicit pins only to eligible standing launch options.
- `src/Antiphon.SessionRunner.Contracts/HerdrLabelFollowContracts.cs` (new), `SessionRunnerContracts.cs`: optional launch intent, observation and DTO field; documented version/null behavior.
- Tests: extend `HerdrPlacementSettingsTests`, `HerdrLaunchContextResolverTests`, `SessionRunnerHttpClientHerdrWireTests`; add server cases V-1, V-12, V-13 below.

### S2 — protocol getters and validation

- `src/Antiphon.SessionRunner/HerdrClient.cs`, `HerdrApiModels.cs`: typed getter requests/envelopes with required-field and returned-ID validation.
- `HerdrPaneChild.cs`: record actual workspace-selection provenance at launch; preserve token refresh and allocator decisions.
- `HerdrLabelObserver.cs` (new): read-only validation; reuse `HerdrNamedTabResolver` rather than duplicating its comparer/count rules.
- Tests/fixtures: `tests/Antiphon.SessionRunner.Tests/FakeHerdrServer.cs` getter routes, recorded failure/gating support and controlled rename/move state; `HerdrClientSurfaceTests`, new `HerdrLabelObservationTests`. V-2 through V-8.

### S3 — durable shared scheduling and consistent snapshots

- `HerdrSettings.cs`, `HerdrPaneSidecar.cs`, `HerdrLastPane.cs`, `HerdrPaneChild.cs`, `SessionRunnerRuntime.cs`: persist attempt/result, exact-binding snapshot refresh, expiry, live DTO exposure, generation-safe retirement and repair. Use the existing placement coordinator; audit all Retire/TryDelete call sites before changing lock placement.
- `HerdrEventPumpService.cs`, runner `Program.cs`: injected clock, hosted timer, baseline/GET delegation and joined shutdown. No extra label RPCs in a cooling-down GET.
- New runner tests `HerdrLabelFollowSchedulingTests`, `HerdrLabelSnapshotTests`; extend `HerdrEventPumpTests`, `HerdrPaneSidecarTests`. V-9 through V-11, V-17, V-18.

### S4 — server application and recovery sweep

- `server/Application/Dtos/SessionRunnerDtos.cs`, `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs`: map the additive DTO exactly, including null/invalid results. Update `tests/Antiphon.Tests/TestHelpers/DirectSessionRunnerClient.cs` mapping for parity.
- New `server/Application/Services/HerdrLabelFollowService.cs`, `server/Application/Settings/HerdrLabelFollowSettings.cs`, `server/Infrastructure/Supervision/HerdrLabelFollowHostedService.cs`; register in `server/Program.cs`. No new public route and no change to the existing event pump's transcript-delivery ordering.
- New server tests `HerdrLabelFollowTests`, `HerdrLabelFollowConcurrencyTests`, `HerdrLabelFollowWireTests`, with Postgres for real transaction/race cases. V-12 through V-16, V-19.

### S5 — full behavior, compatibility and documentation

- New `tests/Antiphon.Tests/Application/HerdrLabelFollowFlowTests.cs` and `tests/Antiphon.Tests/TestHelpers/HerdrLabelFollowHttpFixture.cs`: real runner GET on random loopback port -> production HTTP client -> service -> isolated Postgres; fake only Herdr and provider process evidence. Do not point a test at runner 17204.
- New `tests/Antiphon.SessionRunner.Tests/HerdrLabelFollowLiveTests.cs`: opt-in, disposable-owned real getter/label validation canary (V-21). Separate operator-fixture rename actions from product observation RPCs.
- `docs/herdr-sessions.md` sections 3, 4, 7: state effective pin versus observation intent, hourly delay, move/attached exclusions, manual override, compatibility and reconnect semantics. Update `docs/session-runtime-invariants.md` only for the generation/manual-edit invariant. No generated `docs/cards` edits.
- Run V/R below, report executed/failed/skipped counts, commit/push, hand off ordinary Review. Deliberate PCs are post-land SourceLanding Mutation work.

## Verification design

### Inspection

Inspected test/fixture bodies before naming new cases:

- `HerdrEventPumpTests`: disabled pump, live fake subscription/status path -> V-9, V-10, R-1; existing wall-clock polling is not the new hour-boundary fixture.
- `HerdrNamedTabResolverTests`: explicit comparers, duplicate matching and count failures -> V-4, V-5, R-2.
- `HerdrPlacementSettingsTests`: persisted labels, null/clear behavior and no Herdr ID columns -> V-1, V-12, R-3.
- `SessionRunnerHttpClientHerdrWireTests`: captured launch HTTP body -> V-1, V-19, R-4.
- `FakeHerdrServer`: literal JSON dispatch, request census, failure injection; getter routes are absent -> V-2 and all runner boundary tests.
- `DirectSessionRunnerClient` construction/restart seams and `HerdrDisposalHttpFixture` random-loopback host pattern -> V-16, V-20. Direct client alone cannot prove HTTP mapping; flow fixture must use `SessionRunnerHttpClient`.
- `HerdrNamedTabPlacementLiveTests` / `HerdrLiveSession`: live opt-in pattern, but current helper targets default Herdr and unnecessarily requires Grok -> V-21 must have its own explicitly selected, owned fixture eligibility. Do not silently exercise an operator's historical pane.

Setup to add: getter success/malformed/wrong-ID fixtures; barriers at final read, sidecar commit, server pre-write and last-pane save; a clock local to observer/scheduler; Postgres schema per concurrency case; recording event bus; denied process-kill probe for fake PIDs. Snapshot both active/last-pane JSON and RPC deltas. Fake clock must not freeze the whole session queue graph; use the existing safe clock conventions in `docs/testing-and-build.md`.

**No-furniture oracle:** observation/sweep request delta contains only reads (`pane.get`, `pane.process_info`, existing `pane.read`, `tab.get`, `workspace.get`, `tab.list`, `pane.list`, `workspace.list`, and stream setup). Zero create/split/move/close/rename/report/send/start RPCs, zero OS kills, no changed live session status. Compare before/after pane/workspace counts and IDs too. Exclude fixture setup/teardown and explicit operator rename actions from this delta. Every refusal below uses this oracle and asserts pin labels and eligible label snapshots unchanged; attempt/result metadata may advance.

### Delivery inventory

| Producer -> recipient | Durable identity and persistence | Recovery | Observable receipt / test |
|---|---|---|---|
| Observer -> active child/sidecar | `(session, accepted generation, sequence)`, temp+replace before memory publication | Failed save gives no success; restart restores sequence/cooldown, then fresh due validation | Reload file plus cached child agree; V-11/V-17. |
| Sidecar -> existing matching last-pane | Exact binding incl. accepted generation; sidecar repair marker before second-file write | Retry local repair without fresh Herdr RPC; never patch reused/legacy IDs | Reload matching last-pane labels, unchanged exit/retention identity; V-18. |
| Runner GET -> server Agent pins | Complete observation, then conditional DB labels+watermark transaction | Sweep repeats after lost response/DB failure/server restart; no reliance on SSE | Fresh DB scope and agent detail read show labels; next actual named launch reuses bound tab; V-16/V-20. |
| Committed pins -> UI invalidation | Existing `AgentChanged`, after commit | Reconnect/detail GET is authoritative; no durable SignalR outbox added | Recording bus sees one change on actual update and zero for duplicates/no-op; V-15. A lost UI notification is not a lost pin. |

There is no queue message or agent-input delivery in this design; complete UserPrompt receipts are inapplicable. V-20 parameterizes a working/busy provider and an idle provider and requires DB plus next-launch evidence in both. HTTP 200, file creation, helper return value or `AgentChanged` alone is insufficient acceptance. Fake Herdr cannot establish installed Herdr getter envelopes; V-21 supplies that separate evidence. Crash tests use reconstructed services/runtimes and persisted stores, not an in-memory “retry succeeded” assertion.

### Proves it works now

New class aliases: **O** = runner `HerdrLabelObservationTests`; **T** = runner `HerdrLabelFollowSchedulingTests`; **F** = runner `HerdrLabelSnapshotTests`; **A** = server `HerdrLabelFollowTests`; **C** = server `HerdrLabelFollowConcurrencyTests`; **W** = server `HerdrLabelFollowWireTests`; **E** = server `HerdrLabelFollowFlowTests`; **L** = runner `HerdrLabelFollowLiveTests`. Each method below is a concrete method to implement, not an existing passing test. Parameterized cases report their actual expanded counts.

| ID | Layer / exact method(s) | Decisive behavior |
|---|---|---|
| V-1 | DB + wire: `HerdrPlacementSettingsTests.Manual_placement_edit_rotates_only_the_internal_token`; `HerdrLaunchContextResolverTests.Follow_intent_preserves_nullable_pins_and_physical_owner`; W.`Launch_and_get_round_trip_follow_metadata` | Create/migration preserve pins; explicit same-value/clear edits rotate token; omitted fields/unrelated edits do not; card/pool get no intent; DTO retains null, owner, generation, token and sequence. |
| V-2 | Fake wire: `HerdrClientSurfaceTests.Tab_and_workspace_get_use_schema_envelopes`; O.`Malformed_or_failed_getter_never_yields_a_candidate` | Literal schema envelopes, exact snake_case ID params, missing/wrong-ID result and failed getter refuse with no success snapshot. |
| V-3 | Runner integration: O.`Rename_follows_only_the_same_binding`; O.`Final_read_change_invalidates_collection` | Same IDs/new name accepted; moved tab, moved workspace, disappeared/changed pane and controlled mid-read rename/move skipped. No new target is discovered. |
| V-4 | Runner: O.`Duplicate_tab_label_is_ambiguous`; O.`Case_only_rename_uses_the_host_comparer` | Windows-equivalent duplicates refuse; explicit ordinal variant behaves differently; unique case-only spelling follows. |
| V-5 | Runner: O.`Reported_pane_count_must_be_one`; O.`Enumerated_pane_count_must_be_one`; O.`Only_the_bound_pane_can_validate_the_tab` | Independent reported/enumerated mismatch arms refuse; a single different enumerated pane cannot certify the bound pane. |
| V-6 | Runner: O.`Workspace_follow_requires_original_untagged_selection`; O.`Workspace_follow_requires_current_untagged_state`; O.`Workspace_follow_requires_unique_next_launch_resolution` | Unique untagged explicit pin follows; managed-then-token-expired, foreign-tagged, duplicate untagged, wrong workspace and matching-token-preemption cases do not. Workspace-only single-pane case follows only workspace; packed multi-pane case skips and remains unpinned. |
| V-7 | Runner: O.`Unrepresentable_labels_preserve_pins` | Blank, whitespace edges, controls, 257-char values rejected independently for tab/workspace; canonical 256-char and non-ASCII labels accepted. |
| V-8 | Runner + DB: O.`Attached_or_unpinned_binding_never_becomes_named`; A.`Null_pins_are_never_created_even_with_claimed_intent` | Attached both-pinned case does no follow, including an inconsistent sidecar with otherwise valid follow metadata; wholly unpinned/card/pool launch carries no intent. Workspace-only intent still leaves TabLabel null. Null current DB field cannot become a pin even with fabricated non-null launch intent. |
| V-9 | Runner scheduler: T.`Concurrent_triggers_share_one_persisted_attempt`; T.`Boundary_and_failure_attempts_obey_the_same_hour` | Gate baseline+GET+timer at once: one getter batch. At 59:59 no batch; exactly 60:00 one. Equal/refused/failed attempts consume the same interval; next due reads latest of two renames. |
| V-10 | Hosted runner: T.`Idle_healthy_stream_checks_without_turns_or_gets`; T.`Disabled_or_stopped_pump_does_no_label_work`; T.`Stalled_getter_times_out_and_releases_the_pane_lease` | Rename after baseline on unchanged live subscription, advance clock, no provider turn/GET, observe new result. Cancel/await timer and stream; no post-stop RPC. Withhold one getter reply, advance local clock to 10 seconds, observe no candidate, preserved status and successful acquisition by the next pane actor. |
| V-11 | Runner reconstruction: T.`Restart_preserves_cooldown_and_requires_fresh_validation`; F.`Failed_persistence_does_not_publish_or_advance_cached_labels` | Before/after runner restart, same durable next-due and sequence; no cached actionable receipt until due. Fault before atomic replace leaves old labels/no actionable success; next due can recover. |
| V-12 | Real Postgres races: C.`Manual_edit_before_follow_wins`; C.`Manual_edit_after_follow_wins`; C.`Away_and_back_or_clear_and_repin_does_not_rearm` | Barriers exercise both orders, same-value reaffirmation, clear, both fields and ABA. New DB scope shows manual value; token invalidation prevents later old-generation follow. |
| V-13 | DB: A.`Current_pointer_must_still_name_the_session`; A.`Physical_owner_must_match_both_launch_and_session`; A.`Only_live_standing_herdr_agents_are_eligible` | Independent pointer swap, physical owner swap, card/pool/backend/nonlive/deleted fixtures preserve pins. Unrelated agent edit allows valid follow. |
| V-14 | DB: A.`Same_id_new_generation_rejects_old_observation`; A.`Older_or_duplicate_sequence_cannot_roll_back_labels`; A.`Expired_future_or_unverified_observation_is_ignored` | Normalized timestamp equality; missing generation refuses; newer B then delayed A remains B across service recreation; expired/future/unverified/getter-failure result cannot write. |
| V-15 | DB + event bus: A.`Labels_and_watermark_commit_together_and_only_changes_notify` | Actual pin edit changes UpdatedAt and emits exactly one AgentChanged after commit; equal/duplicate/refusal leaves UpdatedAt/event count; forced rollback changes neither pins nor watermark. |
| V-16 | HTTP/DB reconstruction: E.`Missed_observation_is_recovered_after_server_restart`; E.`Db_failure_retries_the_same_current_snapshot` | Observer commits while server absent; recreated service polls and reads DB receipt. Drop GET reply and inject DB-save failure before commit; retry succeeds once. Crash after commit/before event preserves DB and causes no second change. |
| V-17 | Runner lifecycle: F.`Retirement_and_refresh_cannot_resurrect_or_revert_sidecar` | Gate observer before save versus retirement/detach/new-generation replacement; resulting sidecar absent or owned by new generation, never resurrected. If follow wins, retirement carries new labels. Cached locator equals durable state. |
| V-18 | Runner files: F.`Last_pane_refresh_requires_exact_generation_and_binding`; F.`Last_pane_write_failure_is_repaired_without_new_observation` | Matching record updates; absent stays absent; old generation/legacy/other IDs/attached unchanged; ExitedAt/ExitReason unchanged; injected second-file failure leaves recoverable marker and converges without getters. |
| V-19 | Wire compatibility: W.`Old_peers_and_sidecars_remain_compatible_without_follow`; A.`Sweep_is_independent_of_corroboration_and_turn_state` | Missing optional fields and legacy sidecar do no follow, normal Start/GET still work; disabled corroboration and zero turns do not disable follow sweep; one unreachable session does not starve another. |
| V-20 | HTTP -> DB -> launch: E.`Renamed_pin_is_used_by_the_next_named_launch`; E.`Move_keeps_the_pinned_destination_and_never_creates_during_follow`; E.`Observation_never_mutates_herdr_furniture` | Busy and idle variants, tab-only and simultaneous unique-untagged workspace+tab rename. Assert sidecar/cache, DB/detail, retire/last-pane, resolver options and actual next placement: original tab/pane, zero new furniture on rename relaunch. Move leaves old pin; next launch resolves that pin, not moved pane. Observe-only phase obeys no-furniture oracle. |
| V-21 | Opt-in native: L.`Owned_tab_and_workspace_getters_support_rename_follow_validation` | Explicit selected disposable Herdr fixture, raw getter envelopes captured before/after operator-fixture label edits, IDs unchanged, unique/count validation and renamed-label preflight reuse. No model turn, no production agent mutation. Missing eligible fixture is a reported skip, not fake-backed native proof. |

For V-20 move fixture, seed a safe separate tab under the old pinned name before next launch so its target can be proven without conflating normal missing-pin creation with observer behavior. For V-21 do not add product workspace/tab rename APIs merely for the test: the fixture may use installed CLI/generic RPC as an operator, recorded separately. Inventory and clean only fixture-created IDs/processes.

### Guards the regression

| ID | Existing classes to execute | Decisive regression |
|---|---|---|
| R-1 | Runner `HerdrEventPumpTests`, `HerdrRunnerSessionTests`, `HerdrAdoptionSweepTests` | Replay close/status handling, GET sequence/status and generation adoption remain intact; label failures never produce new exit authority. |
| R-2 | Runner `HerdrNamedTabResolverTests`, `HerdrNamedTabPlacementTests`, `HerdrPaneAllocatorTests` | Existing exact name resolution, two count guards, named-before-last-pane and packed-tab reservation behavior remain intact. |
| R-3 | Server `HerdrPlacementSettingsTests`, `HerdrLaunchContextResolverTests`, `HerdrPlacementPreflightTests` | Existing null/clear validation, card/pool exclusions and read-only placement preflight. |
| R-4 | Server `SessionRunnerHttpClientHerdrWireTests`; runner `HerdrClientTests`, `HerdrClientSurfaceTests`, `HerdrPaneSidecarTests` | Additive JSON, getters do not alter existing wrappers, legacy snapshots and atomic serialization work. |
| R-5 | Runner `HerdrAttachTests`, `HerdrPaneDisposalConcurrencyTests`, `HerdrPaneDisposalStopRegressionTests`; server `AgentAttachHerdrTests` | Attached Stop still detaches; snapshot lease changes neither disposal authorization nor process-release outcomes. |

### Guard inventory and positive controls

Every row defines a distinct guard and a distinct compiling mutation. All are pending **post-land SourceLanding Mutation**, not executed by Plan or Code. Use the exact named method filter in both red and restored green. For parameterized methods run all that method's cases and record which asserted red. A build error, fixture failure or zero tests is not red.

| Guard / PC | Break in production implementation | Exact test expected red; assertion |
|---|---|---|
| G-1 / PC-1 | Remove launched-origin eligibility check in observer | O.`Attached_or_unpinned_binding_never_becomes_named`; attached label snapshot/pin unchanged fails. |
| G-2 / PC-2 | Populate `TabLabel` from raw observation when original pin is null | O.`Attached_or_unpinned_binding_never_becomes_named`; unpinned sidecar TabLabel null fails. |
| G-3 / PC-3 | Treat original/current binding tuple comparison as equal | O.`Rename_follows_only_the_same_binding`; moved binding yields no candidate fails. |
| G-4 / PC-4 | Skip final collection stability comparison | O.`Final_read_change_invalidates_collection`; gated final rename yields no candidate fails. |
| G-5 / PC-5 | Resolve multiple label matches by taking the first | O.`Duplicate_tab_label_is_ambiguous`; refusal/no-change fails. |
| G-6 / PC-6 | Remove reported pane-count guard | O.`Reported_pane_count_must_be_one`; herdr_tab_invalid fails. |
| G-7 / PC-7 | Remove enumerated pane-count guard | O.`Enumerated_pane_count_must_be_one`; herdr_tab_invalid fails. |
| G-8 / PC-8 | Omit selected pane equality with bound PaneId | O.`Only_the_bound_pane_can_validate_the_tab`; wrong sole pane accepted fails. |
| G-9 / PC-9 | Infer untagged provenance from current tokens instead of launch mode | O.`Workspace_follow_requires_original_untagged_selection`; expired managed token changes no workspace label fails. |
| G-10 / PC-10 | Ignore current nonempty workspace ownership token | O.`Workspace_follow_requires_current_untagged_state`; foreign-tagged refusal fails. |
| G-11 / PC-11 | Bypass next-launch workspace resolution identity check | O.`Workspace_follow_requires_unique_next_launch_resolution`; ambiguous/preempted workspace does not follow fails. |
| G-12 / PC-12 | Normalize invalid raw labels instead of rejecting exact mismatch | O.`Unrepresentable_labels_preserve_pins`; edge-whitespace/blank no-change fails. |
| G-13 / PC-13 | Remove server current-pointer predicate | A.`Current_pointer_must_still_name_the_session`; replacement pointer preserves pins fails. |
| G-14 / PC-14 | Remove server physical-owner predicate | A.`Physical_owner_must_match_both_launch_and_session`; mismatched owner preserves pins fails. |
| G-15 / PC-15 | Bypass server live-standing-Herdr eligibility predicate | A.`Only_live_standing_herdr_agents_are_eligible`; card/pool/nonlive/non-Herdr guards fail. |
| G-16 / PC-16 | Compare session ID alone, dropping accepted generation | A.`Same_id_new_generation_rejects_old_observation`; old generation preserves new pin fails. |
| G-17 / PC-17 | Omit launch/current edit-token equality | C.`Away_and_back_or_clear_and_repin_does_not_rearm`; explicit manual value survives old-generation observation fails. |
| G-18 / PC-18 | Accept sequence <= persisted watermark | A.`Older_or_duplicate_sequence_cannot_roll_back_labels`; B remains B after A fails. |
| G-19 / PC-19 | Bypass observation usability (expiry/future/positive verification) predicate | A.`Expired_future_or_unverified_observation_is_ignored`; no DB pin change fails. |
| G-20 / PC-20 | Split cooldown claim from serialized check, allowing simultaneous entrants | T.`Concurrent_triggers_share_one_persisted_attempt`; exactly one getter batch fails. |
| G-21 / PC-21 | Reset restored next-due state on runner adoption | T.`Restart_preserves_cooldown_and_requires_fresh_validation`; no early batch/actionable cached receipt fails. |
| G-22 / PC-22 | Publish cached labels/observation before sidecar atomic write succeeds | F.`Failed_persistence_does_not_publish_or_advance_cached_labels`; failed write leaves cached labels unchanged fails. |
| G-23 / PC-23 | Save captured sidecar without lifecycle ownership recheck/lease | F.`Retirement_and_refresh_cannot_resurrect_or_revert_sidecar`; exited/new binding is not replaced fails. |
| G-24 / PC-24 | Patch last-pane by SessionId only | F.`Last_pane_refresh_requires_exact_generation_and_binding`; mismatched record bytes unchanged fails. |
| G-25 / PC-25 | Allow server to assign a candidate to a currently null pin | A.`Null_pins_are_never_created_even_with_claimed_intent`; null field remains null fails. |
| G-26 / PC-26 | Insert a harmless-looking `tab.rename` to its current label in the observation path | E.`Observation_never_mutates_herdr_furniture`; recorded rename RPC delta is zero fails. |
| G-27 / PC-27 | Omit the collection deadline while retaining host cancellation | T.`Stalled_getter_times_out_and_releases_the_pane_lease`; after advancing the local clock, attempt completion and next actor's lease acquisition fail within the fixture's real bounded assertion deadline. |

Inventory: 27 guards, 27 defined PCs, missing 0, duplicate guard-to-PC mappings 0. PCs sharing a test method or source file are sequential, not batched. Each guard fixture must satisfy the other predicates so a redundant guard cannot mask its mutation: e.g. an attached-origin record with otherwise valid intent for PC-1, workspace-only intent for PC-2, and a changed pane.get tuple with otherwise matching enumeration for PC-3. V-20 separately exercises coherent real-flow moves. Scope PC-20 with a controlled two-entrant barrier so it fails deterministically rather than relying on thread timing. PC-26 executes only against fake Herdr. Counts are design inventory, not completed verification.

### Commands, evidence and cost

Plan verification is static only: review this document against source, ensure all paths resolve or are marked new, check `git diff --check`, commit and push. No source tests/builds are required for this documentation-only dispatch.

For Code, commit before building. Use a fresh producer-owned `bin-c462/` (choose another suffix if already owned), record its exact inventory, and run projects sequentially. Examples from repository root:

```powershell
dotnet build tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c462/ --nologo
dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build --property:OutputPath=bin-c462/ -- --treenode-filter '/*/*/(HerdrLabelObservationTests*)|(HerdrLabelFollowSchedulingTests*)|(HerdrLabelSnapshotTests*)/*' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c462-runner-new
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c462/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c462/ -- --treenode-filter '/*/*/*/*[Category=Unit]' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c462-unit
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c462/ -- --treenode-filter '/*/*/(HerdrLabelFollowTests*)|(HerdrLabelFollowConcurrencyTests*)|(HerdrLabelFollowWireTests*)|(HerdrLabelFollowFlowTests*)/*' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c462-server-new
```

Also execute the named R-1..R-5 classes (including extended V-1/V-2 classes), grouped by project with the same parenthesized class-OR syntax, and V-21 separately when its explicitly selected fixture is available. Validate every intended class/method and nonzero counts in fresh TRX; create new result directories on rerun. Existing tests are not a substitute for new flow/race tests. Use `dotnet run`, not `dotnet test`. Do not co-schedule `Antiphon.Tests` with `Antiphon.Agents.Pty.Tests`. Follow the current Unit + named-integration recipe in `docs/testing-and-build.md`; a broad run needs separately stated scope/cost. Reproduce any suspected inherited red at the base using its failing methods before attributing it to this work.

Mutation example (new results directory for every red/green leg):

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c462-pc/ -- --treenode-filter '/*/*/HerdrLabelFollowTests/Same_id_new_generation_rejects_old_observation' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c462-pc16-red
```

Restore and rebuild/touch restored source, then rerun the **same method** green. Preserve each PC's mutation, expected assertion, counts, source SHA and restoration evidence in the commissioned external evidence root; SourceLanding mutation snapshots never commit/push amendments. No backgrounded/unowned runs and no source edits underneath a running build/test. Clean only inventoried outputs under verified absolute worktree paths using native PowerShell; leave evidence available to the caller.

Estimated costs, not timings measured by this Plan:

| Stage / work | Minutes |
|---|---:|
| Code fixture setup and two isolated project builds | 10–20 |
| Code new V-1..V-20 + Unit lane | 12–25 |
| Code R-1..R-5 named integrations | 10–20 |
| V-21 owned native setup/canary/cleanup | 10–15 |
| Ordinary V/R floor including setup/native | **42–80** |
| Mutation fresh build/setup | 8–15 |
| 27 exact-method red/restore/green cycles, ~2–4 min each | 54–108 |
| Mutation evidence/restoration audit | 8–12 |
| Post-land PC floor | **70–135** |
| Total verification floor | **112–215** |

The new cooldown tests use clock advancement: nine or more hour-boundary/restart/failure scenarios take seconds of simulated time, saving at least nine wall-clock hours versus literal waits. Ordinary runs group classes to amortize fixture/build startup; PC runs remain method-scoped. Real-machine contention can exceed these estimates. Missing native prerequisites leave V-21 visibly unverified; they do not authorize a default-socket probe or a passing-native claim.

## Completion and handoff

Plan deliverable only; all V/R/PC rows are pending implementation/execution. The caller should decide D-2 (generation-scoped attempted-check hour and its recovery delay), D-3 (two 60-second wakeups, no turn-end hook), D-4 (manual placement edit suppresses both fields until next launch), and D-8 (legacy/restarted runner waits for a fresh due validation). These defaults are fully specified, so this task is done, not blocked.

After acceptance: Code S1–S5, ordinary Review, land through the task owner, then commission the post-land SourceLanding Mutation companion. No production restart is included in this Plan task. Activation must later verify actual server and runner versions and a selected eligible canary, not health alone.

## Verification design — TestDesign addendum, 2026-09-16

Authoritative verification addendum from task `30e701dd`, inspected at `9733a3a8ebf4c7a7fa9ce6b35c624e3f550eb61c`. This appends to, and does not alter, D-1–D-9 or S1–S5. Keep V-1–V-21 and R-1–R-5 above with these refinements. **Original G-1–G-27 / PC-1–PC-27 and the original cost estimate are superseded, not extra execution obligations.** Replacement IDs start at 28 to distinguish evidence editions.

New methods are requirements to implement, not claims of existing passing tests. Code implements/runs V/R. Separate ordinary Review judges tests and PC design before land. SourceLanding Mutation executes the compiling defects after land.

**Decision disposition:** `card.ps1 get CARD-0462` revision 5 requests the hourly feature but does not accept the four proposed Plan defaults. No approval appears in this brief. Recommend D-2/D-3/D-4/D-8 as written: every admitted attempt consumes a generation-scoped hour, including faults; independent runner/server 60-second timers with no initial OnTurnEnd hook; explicit manual placement edits suppress both fields until relaunch; runner restart/legacy records require fresh due validation. These are human latency/manual-intent choices. This artifact is complete under those defaults; **next: decide**, then Code if accepted. Changed defaults require corresponding V/PC changes first.

### Inspection

Bodies read for this addendum; method anchors distinguish partial large-class inspection from complete-file inspection:

| Test / fixture bodies read | Boundaries -> coverage |
|---|---|
| Runner `HerdrEventPumpTests`, all five methods; `HerdrClientSurfaceTests`, both methods | Disabled pump, actual subscription/status receipt, additive surface -> V-2/V-9/V-10, R-1/R-4, V-23. |
| Runner `HerdrNamedTabResolverTests`, all seven methods/builders; `HerdrPaneSidecarTests`, all seven methods | Comparers, duplicate/zero matches, count disagreement, failed list, legacy files, save/retirement -> V-3–V-8/V-11/V-18, R-2/R-4, V-22/V-24. |
| `HerdrNamedTabPlacementTests`: `Named_pin_outranks_a_valid_last_pane_and_an_allocator_slot`, `Named_same_id_restart_relaunches_into_the_same_labelled_pane`, both `Named_fresh_id_*` bodies, nested Fixture | Pin precedence, same/fresh session placement, seed/teardown pattern -> V-20/V-26, R-2. |
| `HerdrRunnerSessionTests`: first two sticky-revision methods, BuildSettings/StartHerdrSessionAsync/cleanup; `HerdrAdoptionSweepTests`: R13 replay and R14 child-loss bodies | Refreshed GET versus cached Get, replay is not exit authority, in-place launch -> R-1, V-20/V-23/V-28. |
| `FakeHerdrServer`: NDJSON pipe loop, gates/faults, dispatch, seeds/moves, serializers and state classes | Getters absent; request census and independent reported-count override available -> all runner V. File is also compiled into Antiphon.Tests. |
| `HerdrPaneDisposalFixture`; `HerdrPaneDisposalConcurrencyTests` lease/actor/race/retirement/lock-order bodies; `HerdrPaneDisposalStopRegressionTests` stop/adoption bodies | Existing placement coordination, process probe and barriers -> V-17/V-24/V-28, R-5. |
| Server `HerdrPlacementSettingsTests`, all six methods/builders; `HerdrLaunchContextResolverTests`, title and placement classes/builders | Null/clear, persisted labels, card/pool exclusions, workspace label versus key -> V-1/V-8/V-12/V-25/V-27, R-3. |
| `tests/Antiphon.Tests/Agents/SessionRunnerHttpClientHerdrWireTests.cs`: request capture, old JSON, origin, refusal and exit mapping bodies | HTTP serialization/additive null semantics -> V-1/V-19/V-29, R-4. Actual directory is Agents. |
| `HerdrPlacementPreflightTests`, first three bodies; `AgentControlServiceIntegrationTests.BuildHarness` | Before-queue refusal, named options; real launch queue but default fake adapter/client -> V-20/V-26, R-3. |
| `TestHelpers/DirectSessionRunnerClient`: constructor, BuildRuntime, StartAsync, restart, GetAsync, Map; `HerdrDisposalHttpFixture`, complete | Direct.GetAsync currently calls synchronous runtime.Get, bypassing fresh evidence; random-loopback HTTP pattern -> V-16/V-19/V-20/V-29. |
| `TestHelpers/TestDbFixture` including IsolatedTestSchema; `MockEventBus`; `ProductionRunnerGuard` assembly guard/refusing-client bodies | Isolation is now a cloned database, despite the schema type name; throw-once event bus; no production runner -> V-12–V-16/V-25/V-26. |
| `HerdrNamedTabPlacementLiveTests` and `HerdrLiveSession`, complete | Default socket/Grok prerequisite and broad tab cleanup are unsuitable for the new owned canary -> V-21 requires separate explicit eligibility. |

Production anchors read: runner Program GET registration, HerdrEventPumpService, HerdrPaneSidecar, HerdrLastPane, HerdrNamedTabResolver; runtime RefreshHerdrSurfaceAsync/VerifyHerdrLivenessAsync; server HerdrLaunchContextResolver and AgentService.UpdateAsync row loading/locking. Owners consulted: project context, Herdr/session invariants, testing/build, operations and orchestration. Follow implementation is absent at this HEAD.

**Missing setup assigned to Code:**

1. Extend FakeHerdrServer with literal protocol-20 tab/workspace getter envelopes, malformed/wrong-ID overrides, nth-request barriers and coherent pane moves (both list containment and IDs). Keep raw inconsistent replies separate from coherent flow fixtures. Track/cancel/join its currently detached gated/subscription tasks before using it for shutdown evidence.
2. Add `tests/Antiphon.SessionRunner.Tests/HerdrLabelFollowFixture.cs` beside the inspected placement/disposal fixtures: unique pipe/log root, accepted generation, owner/token/nullable pins, deny-kill process probe and request-interval recorder. Test production internal validation through friend-assembly access; never fake the decision under test.
3. Add timer-aware fake time to the runner test project (server tests already reference Microsoft.Extensions.TimeProvider.Testing). Advance only observer/timer time. Keep queue time real/offset. Disposal OffsetClock does not drive timers and is insufficient. Barriers use two-second real assertion deadlines with cancellation/finally release.
4. Add internal awaitable seams at claim save, final reads, file replacement/cache publication, repair, HTTP response, DB reload/conditional write and postcommit publish. File/EF interceptors inject failures in real persistence. Dispose old instances and abandon volatile state at simulated crashes; no delayed old task may complete behind reconstruction.
5. Build `HerdrLabelFollowHttpFixture` from the random-port fixture with production session routes/client, follow service/hosted sweep and isolated Postgres. Current session GET is inline in Program: extract its registration unchanged into a shared mapper used by Program and fixture, or boot an isolated actual runner. A copied test-only lambda cannot prove route wiring.
6. For V-20, replace BuildHarness's default fake adapter/client registrations with the production runner-backed adapter and SessionRunnerHttpClient. Drive AgentControlService -> real AgentSessionLaunchQueue -> HTTP launch -> HerdrPaneChild. Only Herdr/provider evidence is fake; no model/input work body is needed.
7. Add an event-bus callback that reads DB in a fresh scope before recording AgentChanged, plus throw-once publish failure. Direct-client parity needs awaiting runtime.GetAsync as well as the additive Map change; its current cached Get is not equivalent.
8. V-21 requires `ANTIPHON_HEADED_TESTS=1` plus `ANTIPHON_C462_HERDR_SESSION` selecting an already test-owned instance, and fixture-created workspace/tab/pane IDs. Missing prerequisites produce a reported skip/open native acceptance item. No default-socket fallback, Grok credentials, or cleanup of historical panes.

**Boundary matrix:** successful real flow covers busy/working and idle/done x tab-only pin in managed workspace and both pins in unique untagged workspace (four cases), plus workspace-only/single-pane with TabLabel remaining null. Observer tests cover pin mask none/tab/workspace/both x original managed/untagged provenance; attached with otherwise valid metadata; workspace-only packed tab; expired/new/foreign ownership tokens; simultaneous ambiguous workspace + valid tab rename.

Each narrow guard has one-invalid-predicate and all-valid companions. Also test move+rename, duplicate+count mismatch, manual edit+delayed observation, expired+out-of-order. Counts: (reported,enumerated) = (1,1),(0,1),(2,1),(1,0),(1,2),(2,2). Labels: empty, whitespace-only, edge whitespace, embedded NUL/LF/DEL, 255/256/257 UTF-16 units, supplementary/non-ASCII. Time: due-1 tick/due/due+1 tick, expiry-1 tick/expiry, future+30s/+30s+1 tick. Metadata: absent/null/valid/unknown version; same ID/different normalized generation. Remaining full Cartesian combinations are excluded because validators are independent of provider/turn state; both flow states and independent predicate cases cover those boundaries.

The original no-furniture oracle applies to every refusal/no-op. Isolate the observation interval from setup/operator renames/ordinary launch and retirement traffic. Label failure itself provides no kill/exit authority; separately verified child death retains its existing lifecycle behavior.

### Delivery inventory

No prompt, chat outcome or session-input queue is added by D-1–D-9. Follow uses durable replaceable snapshots pulled over real HTTP. Enqueue-failure is therefore inapplicable to follow; its failure boundaries are claim/result saves, lost responses and DB commit. Do not invent an outbox or UserPrompt for a label update. The subsequent V-20 launch traverses the existing real launch queue.

| Producer -> destination | Durable identity / persistence | Recovery and observable recipient receipt |
|---|---|---|
| Baseline/GET/timer -> observer | SessionId + normalized AcceptedStartedAt + attempt sequence; claim committed before I/O | Crash before claim permits a fresh attempt; after claim leaves no actionable result and preserves due time. T.Attempt_claim_is_durable_before_io and T.New_attempt_clears_old_candidate_before_io reconstruct and receive a fresh due result. |
| Collector -> active sidecar/child | Above stamp plus workspace/tab/pane, physical owner and launch edit token; atomic result/labels/repair-debt commit precedes cache | Failure before replacement leaves old complete file/cache. Crash after file commit before cache publication restores file but no cached authority until fresh due validation. F.Failed_persistence_does_not_publish_or_advance_cached_labels and F.Crash_after_sidecar_commit_recovers_without_stale_publication reload both recipients. |
| Sidecar -> existing last-pane | Exact session/generation/workspace/tab/pane/origin plus repair marker in first-file commit | Fail/crash between files and after second-file write/before marker clear; reconstruct, recheck ownership and retry locally. F.Last_pane_write_failure_is_repaired_without_new_observation and F.Repair_marker_survives_failure_and_respects_replacement require recipient JSON labels, unchanged exit metadata and zero fresh getters. |
| Current runner GET -> Agent pins | Full observation through production route/client; labels and binding watermark commit together | Lost reply, precommit failure, server restart and both-side restart converge. E.Db_failure_retries_the_same_current_snapshot and E.Missed_observation_is_recovered_after_server_restart require fresh DB/detail plus actual subsequent placement receipt. |
| Committed Agent -> operator read | Agent ID/committed labels; AgentChanged is best-effort invalidation | Crash after commit/before publish or throw-once event failure: E.Committed_pin_is_visible_after_notification_failure recreates server and reads production detail HTTP with committed labels. A.Labels_and_watermark_commit_together_and_only_changes_notify checks postcommit order. |
| Persisted pin -> next named launch | Agent ID -> accepted launch generation -> sidecar placement, through real launch queue | E.Renamed_pin_is_used_by_the_next_named_launch follows while busy or already idle, simulates natural child loss, then starts. Receipt: Running new generation in original workspace/tab/pane with zero added tab/workspace creation. Captured options or queue insertion cannot pass. Launch-queue recovery policy is unchanged. |

Each interruption discards old volatile state and recreates from files/DB; an awaited abort seam skips the remaining handoff rather than graceful teardown secretly completing it. HTTP 200, acceptance, queue insertion, Saved/Sent flag, terminal business event, helper success or transport acknowledgement alone cannot meet delivery acceptance.

Substitutes: fake Herdr proves wire/selection logic, not installed behavior (V-21); scripted busy/idle proves scheduling independence, not provider prompt receipt; reconstructed runtime proves process-crash boundaries, not power-loss/fsync guarantees; event-bus spy proves publish timing, not browser repaint; detail GET after dropped notification proves reconnect read correctness, not a durable SignalR inbox. No browser-change claim is made.

If Code introduces input or another outcome queue, extend this inventory and controls first: real producer-to-recipient queue tests with busy/already eligible recipients, each crash/enqueue boundary, and matching **complete UserPrompt transcript** for session input. Review must reject tests that stop before recipient evidence.

### Proves it works now

V-1–V-21 remain required with their exact methods and expanded data arms. Aliases O/T/F/A/C/W/E/L retain their definitions above. Runner classes live under `tests/Antiphon.SessionRunner.Tests/`; A/C/E under `tests/Antiphon.Tests/Application/`; W under `tests/Antiphon.Tests/Agents/` beside the inspected wire class. Aliases are not literal class names.

| ID | Behavior / layer | Test/command selection | Expected |
|---|---|---|---|
| V-22 | Independent identity/read/label guards; runner | All O methods in the active PC table | Single-invalid arms refuse; valid companions follow without mutating RPCs; matrix covers moves/counts/comparers. |
| V-23 | Durable admission, timers, cached GET; runner | All T methods in active PC table; F.Crash_after_sidecar_commit_recovers_without_stale_publication | One attempt; bounded work; joined shutdown; no stale authority after restart/move/outage; fresh due recovery. |
| V-24 | Files/lifecycle; real file I/O | All F methods in active PC table | File/cache agreement, no resurrection, exact generation/identity fence, repair survives repeated failure and replacement. |
| V-25 | Ownership/manual ordering; Postgres | All A/C and HerdrPlacementSettingsTests methods in active PC table | Direct-service guards and independent-connection races prove refusal/manual precedence/conditional SQL. |
| V-26 | Recipient recovery; HTTP + DB + launch queue | E.Renamed_pin_is_used_by_the_next_named_launch; E.Db_failure_retries_the_same_current_snapshot; E.Missed_observation_is_recovered_after_server_restart; E.Committed_pin_is_visible_after_notification_failure | Delivery inventory receipts, four busy/idle flow cases, every persistence/transport handoff interruption. |
| V-27 | Compatibility/migration/settings | W.Launch_and_get_round_trip_follow_metadata; W.Old_peers_and_sidecars_remain_compatible_without_follow; HerdrPlacementSettingsTests.Follow_migration_preserves_existing_labels; T.Invalid_settings_refuse_startup; A.Invalid_sweep_settings_refuse_startup | Migrate from preceding migration with pins/nulls: labels unchanged, token Guid.Empty, watermark null/null/0. Defaults 60m/10s/60s, positive-value validation, no inferred legacy authority. |
| V-28 | No lifecycle authority; runner/server | E.Label_failure_does_not_change_session_lifecycle; E.Observation_never_mutates_herdr_furniture; F.Observer_coordinates_with_existing_pane_actors | Label timeout/failure gives no kill/input/furniture writes. Both race orders against retirement/detach/disposal/adoption/same-ID replacement; unrelated pane progresses. |
| V-29 | Real route and direct helper parity | W.Direct_and_http_get_refresh_and_map_the_same_follow_observation | Independently seeded bindings, both clients perform fresh refresh and preserve every field/generation. Assert HTTP request count; no manually copied DTO substitute. |
| V-30 | Server sweep independence | A.Disabled_sweep_polls_nothing; A.Sweep_is_independent_of_corroboration_and_turn_state | Disabled means zero polls. Enabled with corroboration/AlwaysOn off, no channel or turns still follows; failed first GET does not starve second. |

Every active PC table method is also an ordinary V requirement. Its decisive assertion defines behavior even when this compact index does not repeat the name. Keep each exact method individually discoverable; parameterized method filters run every data arm.

### Guards the regression

Execute original R-1–R-5 class selections. These inspected exact anchors identify decisive existing assertions:

- R-6: GET sequencing | `HerdrRunnerSessionTests.Sticky_revision_plus_changed_read_text_advances_LastSequence_via_GetSnapshot_and_GetAsync` and `Sticky_revision_plus_identical_text_across_reads_does_not_advance` | changed text advances; identical text does not.
- R-7: event replay | `HerdrAdoptionSweepTests.R13_replayed_pane_closed_on_a_healthy_pane_does_nothing` | after real replay subscription, Running and active sidecar exists.
- R-8: named precedence/reuse | `HerdrNamedTabPlacementTests.Named_pin_outranks_a_valid_last_pane_and_an_allocator_slot` and `Named_same_id_restart_relaunches_into_the_same_labelled_pane` | original pane; no added create/split.
- R-9: manual labels | `HerdrPlacementSettingsTests.Updating_labels_on_a_live_agent_launches_nothing` | check/launch count unchanged, Running retained.
- R-10: refusal before queue | `HerdrPlacementPreflightTests.Public_start_refused_by_preflight_is_409_before_any_row_or_queue_mutation` | typed conflict, unchanged pointer/session count, adapter unstarted.
- R-11: legacy/attach | `HerdrPaneSidecarTests.A_sidecar_without_the_field_loads_a_null_generation`, `Placement_labels_round_trip_and_old_files_load_with_null_labels`, `retire_of_an_attached_sidecar_writes_no_last_pane_record` | legacy nulls and no attached last-pane.
- R-12: existing pane leases | `HerdrPaneDisposalConcurrencyTests.C461_G072_Retirement_lease`, `C461_G073_Lock_order`; `HerdrPaneDisposalStopRegressionTests.C461_G075_Stop_completes_when_adoption_wins_the_pane_lease` | retirement waits, lock order, Exited/Detached after adoption.

### Guard inventory

Active replacement inventory follows. Original G-1–G-27 are retired bundled entries. Every row maps 1:1 to its distinct same-numbered PC. Repeated method names indicate different data arms/mutations, not duplicate PC mappings. No safety-critical guard in the specified design is intentionally untested.

| Guard | Plan reference and invariant | Positive control |
|---|---|---|
| G-28 | D-4,D-5: Observer accepts launched origin only | PC-28 |
| G-29 | D-4,D-7: Runner requires present, supported follow-intent metadata | PC-29 |
| G-30 | D-4,D-6: Runner requires an immutable accepted generation | PC-30 |
| G-31 | D-5: pane.get must return bound PaneId | PC-31 |
| G-32 | D-5: Bound pane must retain its TabId | PC-32 |
| G-33 | D-5: Bound pane must retain its WorkspaceId | PC-33 |
| G-34 | D-5,S2: tab.get must return requested bound TabId | PC-34 |
| G-35 | D-5: tab.get workspace must equal bound WorkspaceId | PC-35 |
| G-36 | D-5,S2: workspace.get must return bound WorkspaceId | PC-36 |
| G-37 | D-5,S2: Getter envelopes require mandatory fields | PC-37 |
| G-38 | D-5: Failed read is not absence or successful validation | PC-38 |
| G-39 | D-5: A tab label must have exactly one comparer match | PC-39 |
| G-40 | D-5: Zero tab matches cannot authorize following | PC-40 |
| G-41 | D-5: Reported pane count must equal one | PC-41 |
| G-42 | D-5: Enumerated pane count must equal one | PC-42 |
| G-43 | D-5: Selected tab must be the bound tab | PC-43 |
| G-44 | D-5: Selected sole pane must be the bound pane | PC-44 |
| G-45 | D-5: Final pane identity must retain PaneId | PC-45 |
| G-46 | D-5: Final pane identity must retain TabId | PC-46 |
| G-47 | D-5: Final pane identity must retain WorkspaceId | PC-47 |
| G-48 | D-5: Final tab spelling must equal collected spelling | PC-48 |
| G-49 | D-5: Final workspace spelling must equal collected spelling | PC-49 |
| G-50 | D-5: Tab list/get values must agree | PC-50 |
| G-51 | D-5: Workspace list/get values must agree | PC-51 |
| G-52 | D-5: Only original UniqueUntaggedLabel provenance may follow workspace | PC-52 |
| G-53 | D-5: Current workspace must remain untagged | PC-53 |
| G-54 | D-5: New workspace label must have one untagged match | PC-54 |
| G-55 | D-5: Matching WorkspaceKey token elsewhere must not preempt fallback | PC-55 |
| G-56 | D-5: Resolved workspace must equal bound WorkspaceId | PC-56 |
| G-57 | D-5: Ambiguous changed workspace blocks simultaneous tab follow | PC-57 |
| G-58 | D-5: Observed labels must be nonblank | PC-58 |
| G-59 | D-5: Observed labels fit 256 UTF-16 units | PC-59 |
| G-60 | D-5: Observed labels contain no controls | PC-60 |
| G-61 | D-5: Observed labels already equal trimmed spelling | PC-61 |
| G-62 | D-5: Tab ambiguity uses host label comparer | PC-62 |
| G-63 | D-5: Workspace fallback uses ordinal label equality | PC-63 |
| G-64 | D-7: Unpinned tab snapshots stay null | PC-64 |
| G-65 | D-5,D-7: Managed/foreign workspace snapshot stays configured | PC-65 |
| G-66 | D-2: Concurrent triggers share atomic admission | PC-66 |
| G-67 | D-2: Attempt claim commits before first getter | PC-67 |
| G-68 | D-2: Failed/refused/equal attempts consume cooldown | PC-68 |
| G-69 | D-2: Due boundary admits at exactly one hour | PC-69 |
| G-70 | D-2: Cooldown configuration must be positive | PC-70 |
| G-71 | D-3: Observation timeout configuration must be positive | PC-71 |
| G-72 | D-3: Server sweep period configuration must be positive | PC-72 |
| G-73 | D-3: Whole collection including lease acquisition has deadline | PC-73 |
| G-74 | D-3: In-flight observer callers skip instead of queueing | PC-74 |
| G-75 | D-3: Shutdown cancels runner timer | PC-75 |
| G-76 | D-3: Shutdown awaits both owned tasks | PC-76 |
| G-77 | D-3: Healthy idle stream still gets independent timer checks | PC-77 |
| G-78 | D-6: New attempt invalidates previous actionable result before I/O | PC-78 |
| G-79 | D-8: Runner restart preserves durable cooldown | PC-79 |
| G-80 | D-8: Restart suppresses cached candidate until fresh due validation | PC-80 |
| G-81 | D-6: GET requires positive pane/child evidence in this request | PC-81 |
| G-82 | D-6: GET suppresses cached candidate when current PaneId differs | PC-82 |
| G-83 | D-6,S4: Absent/unknown DTO metadata never gains authority | PC-83 |
| G-84 | D-9: Server current agent pointer must name observation session | PC-84 |
| G-85 | D-9: Launch physical owner must equal agent ID | PC-85 |
| G-86 | D-9: Session physical owner must equal agent ID | PC-86 |
| G-87 | D-6: Server accepted generation must match exactly | PC-87 |
| G-88 | D-9: Server independently refuses attached origin | PC-88 |
| G-89 | D-9: Server independently refuses card sessions | PC-89 |
| G-90 | D-9: Server independently refuses pool agents | PC-90 |
| G-91 | D-9: Server requires current agent Herdr backend | PC-91 |
| G-92 | D-9: Server requires session Herdr backend | PC-92 |
| G-93 | D-9: Server requires live session state | PC-93 |
| G-94 | D-4: Launch/current edit tokens must match | PC-94 |
| G-95 | D-4: Every explicit tab edit rotates token, including same value and clear | PC-95 |
| G-96 | D-4: Every explicit workspace edit rotates token | PC-96 |
| G-97 | D-4: Backend context change invalidates launch token | PC-97 |
| G-98 | D-4: Board context change invalidates launch token | PC-98 |
| G-99 | D-4,D-9: Manual and follow writers use common row serialization | PC-99 |
| G-100 | D-4: Explicit reaffirmation writes after reload, despite stale EF original | PC-100 |
| G-101 | D-9: Follow reloads agent under lock after network read | PC-101 |
| G-102 | D-9: DB update conditions on current session generation | PC-102 |
| G-103 | D-9: DB update conditions on current session liveness | PC-103 |
| G-104 | D-6: Watermark rejects older and duplicate sequences | PC-104 |
| G-105 | D-6: Observation must not be expired | PC-105 |
| G-106 | D-6: Completion may not exceed server clock by more than 30s | PC-106 |
| G-107 | D-6: Only positively validated completed results authorize labels | PC-107 |
| G-108 | D-4,D-9: Current null tab pin cannot become configured | PC-108 |
| G-109 | D-4,D-9: Current null workspace pin cannot become configured | PC-109 |
| G-110 | D-4,D-9: Null launch tab intent cannot authorize later tab pin follow | PC-110 |
| G-111 | D-4,D-9: Null launch workspace intent cannot authorize later workspace pin follow | PC-111 |
| G-112 | D-9: Labels and watermark commit atomically | PC-112 |
| G-113 | D-9: AgentChanged is published only after DB commit | PC-113 |
| G-114 | D-2,D-9: No-change/refusal/duplicate must not stamp UpdatedAt or notify | PC-114 |
| G-115 | D-7: Sidecar commit precedes cached labels/actionable success | PC-115 |
| G-116 | D-7: Concurrent saves use distinct temporary files | PC-116 |
| G-117 | D-7: Observer holds pane lease through publication | PC-117 |
| G-118 | D-7: Publication rechecks runtime object/generation ownership | PC-118 |
| G-119 | D-7: Retirement copies latest committed labels | PC-119 |
| G-120 | D-7: Observer lock order remains workspace-key then workspace-ID then pane | PC-120 |
| G-121 | D-7: Runtime synchronous gate is not held across observer I/O | PC-121 |
| G-122 | D-7: Following never creates absent last-pane record | PC-122 |
| G-123 | D-7: Last-pane SessionId matches | PC-123 |
| G-124 | D-7: Last-pane accepted generation matches and is present | PC-124 |
| G-125 | D-7: Last-pane workspace ID matches | PC-125 |
| G-126 | D-7: Last-pane tab ID matches | PC-126 |
| G-127 | D-7: Last-pane pane ID matches | PC-127 |
| G-128 | D-7: Last-pane origin is launched | PC-128 |
| G-129 | D-7: Last-pane follow preserves retention and exit metadata | PC-129 |
| G-130 | D-7: Repair debt persists atomically with sidecar result | PC-130 |
| G-131 | D-7: Local repair runs even while observation is cooling down | PC-131 |
| G-132 | D-7: Failed repair does not clear debt early | PC-132 |
| G-133 | D-7: Repair revalidates last-pane ownership | PC-133 |
| G-134 | D-8: Server sweep retries lost GET/precommit failure | PC-134 |
| G-135 | D-8: Equal runner snapshot still carries candidate needed by missed server | PC-135 |
| G-136 | D-3: Disabled server sweep polls nothing | PC-136 |
| G-137 | D-3: One failed session cannot starve later eligible sessions | PC-137 |
| G-138 | D-5,D-9: Following has no furniture/input RPC side effects | PC-138 |
| G-139 | D-3,D-9: Observation failure has no kill/exit authority | PC-139 |
| G-140 | D-1,D-9: Consumers receive pins through production HTTP mapping and actual launch | PC-140 |
| G-141 | D-5,D-6: Collection requires positive recorded-child evidence | PC-141 |
| G-142 | D-6: GET suppresses cached candidate when current TabId differs | PC-142 |
| G-143 | D-6: GET suppresses cached candidate when current WorkspaceId differs | PC-143 |
| G-144 | D-3: Disabled Herdr pump does no label work | PC-144 |
| G-145 | D-2: A newly accepted generation gets an immediate first check | PC-145 |
| G-146 | D-9: Common owner-before-seat DB lock order is preserved | PC-146 |


### Positive controls

Each row means: **break its same-numbered G with the stated compiling production defect; expect the exact method red at the stated assertion**. These are executable recipes for the named Code-stage tests. No mutation is run during TestDesign. All temporary defects are restored in SourceLanding Mutation; that stage reports break, assertion-red, restore and freshly rebuilt green separately for every PC.

Use the exact class/method selector, expanding O/T/F/A/C/W/E aliases. Run all parameter rows of that one method. Do not mutate a fixture assertion, replace production logic with a test stub, or count compiler/fixture/timeout-before-assertion failure or zero tests as red. A timed guard test explicitly asserts completion/noncompletion at its barrier deadline, then releases/cancels/joins in finally so a failed assertion cannot leave an orphan.

**Avoid masked controls:** independently test the production boundary being removed. Wire getter-ID validation uses literal replies; downstream observer predicates use typed snapshots injected after getter parsing. Server admission tests call the service directly, bypassing sweep discovery; malformed DTOs otherwise satisfy all predicates. Record boundary disposition and attempted conditional-write count where a later defense could also refuse. Specifically, the initial server-live-status test must assert zero conditional-write commands (not only unchanged pins); the final SQL liveness PC injects the state change after that initial validation. Missing upstream admission then fails the first assertion even if final SQL still blocks. Lifecycle recheck tests inject retirement/replacement before the lease is acquired, while the separate lease PC parks an actor inside the shared critical section. Both retain their real final recipient assertions. Every invalid identity fixture varies exactly the named component; coherent full-flow move tests remain separate.

PCs touching the same file/method are sequential, even when they share a parameterized method. Independent methods/files may batch only with separate per-PC evidence; no concurrency savings are assumed in Cost. SourceLanding uses only its assigned snapshot and inherited local children; no extra unbound worktrees, external executors, commits or pushes. Fixes return to separately commissioned Code/Review/land.

| PC | Compiling production defect | Exact method expected red | Decisive assertion |
|---|---|---|---|

| PC-28 | remove observer origin rejection | `O.Attached_or_unpinned_binding_never_becomes_named` | attached fixture with valid intent returns no candidate and preserves snapshot labels. |
| PC-29 | treat null/unknown follow-state version as version 1 using ordinary launch labels | `O.Missing_or_unknown_intent_is_ineligible` | candidate is null for absent intent and version 99. |
| PC-30 | substitute LaunchedAtUtc for missing AcceptedStartedAt | `O.Missing_generation_is_not_inferred` | candidate remains null despite a live pane and configured pins. |
| PC-31 | omit only returned pane-id equality | `O.Binding_identity_components_must_match` | PaneId mismatch case has no candidate. |
| PC-32 | omit only pane.TabId comparison | `O.Binding_identity_components_must_match` | moved-tab case has no candidate. |
| PC-33 | omit only pane.WorkspaceId comparison | `O.Binding_identity_components_must_match` | moved-workspace case has no candidate. |
| PC-34 | omit only getter tab-id validation | `O.Binding_identity_components_must_match` | wrong tab reply produces no candidate. |
| PC-35 | omit only tab.WorkspaceId comparison | `O.Binding_identity_components_must_match` | cross-workspace tab reply produces no candidate. |
| PC-36 | omit only getter workspace-id validation | `O.Binding_identity_components_must_match` | wrong workspace reply produces no candidate. |
| PC-37 | accept missing label/count payload fields using plausible default values | `O.Malformed_or_failed_getter_never_yields_a_candidate` | malformed literal-envelope case has no candidate, never a fabricated pin. |
| PC-38 | catch a getter/list failure and reuse the previous successful response | `O.Malformed_or_failed_getter_never_yields_a_candidate` | failed-RPC case has no candidate after an earlier success. |
| PC-39 | replace duplicate refusal with first matching tab | `O.Duplicate_tab_label_is_ambiguous` | duplicate case returns herdr_tab_ambiguous. |
| PC-40 | use tab.get result when resolver returns null | `O.Missing_tab_match_is_not_a_rename` | zero-list-match case has no candidate. |
| PC-41 | remove only reported-count comparison | `O.Reported_pane_count_must_be_one` | reported 0/2 with enumerated 1 returns herdr_tab_invalid. |
| PC-42 | remove only enumeration-count comparison and select FirstOrDefault | `O.Enumerated_pane_count_must_be_one` | reported 1/enumerated 2 returns herdr_tab_invalid. |
| PC-43 | omit selected TabId comparison | `O.Selected_tab_must_be_bound` | single wrong selected tab yields no candidate. |
| PC-44 | omit selected PaneId comparison | `O.Only_the_bound_pane_can_validate_the_tab` | one foreign selected pane yields no candidate. |
| PC-45 | omit PaneId from final-read comparison | `O.Final_reads_must_match_initial_and_list_values` | barrier changes only final PaneId; candidate is null. |
| PC-46 | omit TabId from final-read comparison | `O.Final_reads_must_match_initial_and_list_values` | barrier changes only final TabId; candidate is null. |
| PC-47 | omit WorkspaceId from final-read comparison | `O.Final_reads_must_match_initial_and_list_values` | barrier changes only final WorkspaceId; candidate is null. |
| PC-48 | omit final tab-label equality | `O.Final_reads_must_match_initial_and_list_values` | second tab rename before publication yields no candidate. |
| PC-49 | omit final workspace-label equality | `O.Final_reads_must_match_initial_and_list_values` | second workspace rename before publication yields no candidate. |
| PC-50 | omit tab list/get agreement check | `O.Final_reads_must_match_initial_and_list_values` | different tab label/count in list versus getter yields no candidate. |
| PC-51 | omit workspace list/get agreement check | `O.Final_reads_must_match_initial_and_list_values` | different workspace label/token in list versus getter yields no candidate. |
| PC-52 | infer untagged provenance from current tokens | `O.Workspace_follow_requires_original_untagged_selection` | managed-at-launch/token-expired case preserves workspace pin and snapshot. |
| PC-53 | ignore non-whitespace antiphon-ws | `O.Workspace_follow_requires_current_untagged_state` | new foreign or managed token preserves workspace pin and snapshot. |
| PC-54 | choose first untagged label match | `O.Workspace_follow_requires_unique_next_launch_resolution` | two untagged exact matches refuse the placement update. |
| PC-55 | skip token-first preemption check | `O.Workspace_follow_requires_unique_next_launch_resolution` | unique untagged label plus remote matching token yields no candidate. |
| PC-56 | omit selected workspace-id equality | `O.Workspace_follow_requires_unique_next_launch_resolution` | unique same-label different workspace yields no candidate. |
| PC-57 | keep the tab candidate when workspace validation refuses | `O.Ambiguous_workspace_blocks_simultaneous_tab_rename` | both old labels remain, including tab label. |
| PC-58 | remove shared nonblank rejection | `O.Unrepresentable_labels_preserve_pins` | tab and workspace blank cases have no candidate. |
| PC-59 | change shared maximum from 256 to 257 | `O.Unrepresentable_labels_preserve_pins` | 257-unit case has no candidate; 256-unit companion succeeds. |
| PC-60 | remove shared char.IsControl rejection | `O.Unrepresentable_labels_preserve_pins` | embedded LF/NUL/DEL cases have no candidate. |
| PC-61 | trim raw candidate before validation | `O.Unrepresentable_labels_preserve_pins` | leading/trailing-space case has no candidate. |
| PC-62 | force Ordinal for Windows observation selection | `O.Case_only_rename_uses_the_host_comparer` | Orch/orch duplicate fixture refuses on explicit ignore-case path. |
| PC-63 | replace workspace Ordinal with OrdinalIgnoreCase | `O.Workspace_resolution_remains_ordinal` | case-distinct workspace does not create false ambiguity; exact bound workspace follows. |
| PC-64 | copy raw tab name into TabLabel for workspace-only intent | `O.Attached_or_unpinned_binding_never_becomes_named` | workspace-only successful follow leaves sidecar/cache TabLabel null. |
| PC-65 | copy current workspace label whenever tab follow succeeds | `O.Managed_workspace_keeps_snapshot_while_tab_follows` | managed/foreign fixture follows tab but preserves WorkspaceLabel. |
| PC-66 | read due state before releasing/reacquiring claim serialization, without recheck | `T.Concurrent_triggers_share_one_persisted_attempt` | barrier-controlled baseline/GET/timer yield exactly one getter batch. |
| PC-67 | move attempt SaveAtomic after getter collection | `T.Attempt_claim_is_durable_before_io` | crash at first getter leaves durable sequence 1 and due time T+60m. |
| PC-68 | advance next-due only on changed-label success | `T.Boundary_and_failure_attempts_obey_the_same_hour` | failed/refused/equal cases have zero label batches at T+59m59s. |
| PC-69 | change due comparison to admit one second early | `T.Boundary_and_failure_attempts_obey_the_same_hour` | T+59m59s has zero batches; T+60m has one. |
| PC-70 | remove only cooldown positive validator | `T.Invalid_settings_refuse_startup` | cooldown 0/-1 causes OptionsValidationException before any RPC. |
| PC-71 | remove only observation-timeout positive validator | `T.Invalid_settings_refuse_startup` | timeout 0/-1 causes OptionsValidationException before any RPC. |
| PC-72 | remove only sweep-period positive validator | `A.Invalid_sweep_settings_refuse_startup` | sweep period 0/-1 causes OptionsValidationException before polling. |
| PC-73 | use only host token instead of linked observation-deadline token | `T.Stalled_getter_times_out_and_releases_the_pane_lease` | lease-wait/getter-wait variants finish after simulated 10s and next actor acquires lease within 2 real seconds. |
| PC-74 | replace nonwaiting observer admission with awaited semaphore admission | `T.Inflight_triggers_return_without_waiting` | second GET finishes before first getter gate is released. |
| PC-75 | give timer CancellationToken.None | `T.Disabled_or_stopped_pump_does_no_label_work` | Stop completes within 2 real seconds and advancing clock produces zero RPC delta. |
| PC-76 | return from shutdown after joining stream only | `T.Stop_joins_stream_and_timer` | Stop is incomplete while timer finalizer barrier is held. |
| PC-77 | remove label timer invocation while retaining stream baseline | `T.Idle_healthy_stream_checks_without_turns_or_gets` | new observation appears after due time with no GET or turn. |
| PC-78 | retain previous candidate in persisted attempt claim | `T.New_attempt_clears_old_candidate_before_io` | blocked/failed new attempt exposes no old candidate. |
| PC-79 | reset restored next-due to now | `T.Restart_preserves_cooldown_and_requires_fresh_validation` | restart at minute 30 performs zero label batches until minute 60. |
| PC-80 | expose restored completed observation immediately after adoption | `T.Restart_preserves_cooldown_and_requires_fresh_validation` | GET after positive adoption but before due returns no actionable labels. |
| PC-81 | treat VerifyHerdrLivenessAsync unreachable-true as confirmation | `T.Cached_candidate_requires_positive_current_get` | unreachable or missing recorded child suppresses cached actionable labels. |
| PC-82 | omit only current PaneId equality on cached GET | `T.Cached_candidate_is_suppressed_after_move` | PaneId-only mismatch during cooldown suppresses candidate with zero new label getters. |
| PC-83 | map null/unknown follow version to actionable default binding | `W.Old_peers_and_sidecars_remain_compatible_without_follow` | old/unknown DTO keeps follow result null and DB pins unchanged. |
| PC-84 | remove current-session pointer predicate | `A.Current_pointer_must_still_name_the_session` | another current session leaves both pins unchanged. |
| PC-85 | remove only launch-owner equality | `A.Physical_owner_must_match_both_launch_and_session` | different launch StandingAgentId preserves pins. |
| PC-86 | remove only AgentSession.StandingAgentId equality | `A.Physical_owner_must_match_both_launch_and_session` | different persisted session owner preserves pins. |
| PC-87 | compare SessionId without AcceptedStartedAt | `A.Same_id_new_generation_rejects_old_observation` | old same-ID generation cannot change current pin. |
| PC-88 | remove only attached-origin rejection | `A.Only_live_standing_herdr_agents_are_eligible` | otherwise valid attached DTO preserves pins. |
| PC-89 | remove only CardId eligibility predicate | `A.Only_live_standing_herdr_agents_are_eligible` | card-owned session preserves pins. |
| PC-90 | remove only IsPoolDelegate predicate | `A.Only_live_standing_herdr_agents_are_eligible` | pool-agent fixture preserves pins. |
| PC-91 | omit only agent backend predicate | `A.Only_live_standing_herdr_agents_are_eligible` | PtyHost agent with Herdr session preserves pins. |
| PC-92 | omit only session backend predicate | `A.Only_live_standing_herdr_agents_are_eligible` | Herdr agent with PtyHost session preserves pins. |
| PC-93 | omit only initial live-status predicate | `A.Only_live_standing_herdr_agents_are_eligible` | Stopped/Failed/Ended session preserves pins. |
| PC-94 | omit token equality in follow writer | `C.Away_and_back_or_clear_and_repin_does_not_rearm` | ABA and clear/re-pin retain manual configuration after delayed follow. |
| PC-95 | omit token rotation for explicit tab field | `HerdrPlacementSettingsTests.Manual_placement_edit_rotates_only_the_internal_token` | new token differs after tab reaffirmation/clear. |
| PC-96 | omit token rotation for explicit workspace field | `HerdrPlacementSettingsTests.Manual_placement_edit_rotates_only_the_internal_token` | new token differs after workspace reaffirmation/clear. |
| PC-97 | omit backend-change token rotation | `HerdrPlacementSettingsTests.Placement_context_changes_invalidate_follow_intent` | backend away-and-back has changed token and old observation is refused. |
| PC-98 | omit board-change token rotation | `HerdrPlacementSettingsTests.Placement_context_changes_invalidate_follow_intent` | board away-and-back has changed token and old observation is refused. |
| PC-99 | remove agent row lock from manual placement update | `C.Manual_edit_before_follow_wins` | barrier-raced manual commit wins; old-token follow does not overwrite it. |
| PC-100 | use pre-lock tracked label value to decide whether supplied label is modified | `C.Manual_edit_after_follow_wins` | manual same-old-spelling after committed follow persists manual spelling. |
| PC-101 | retain pre-network tracked Agent without reload | `C.Follow_reloads_after_runner_response` | edit/pointer swap during HTTP barrier preserves replacement state. |
| PC-102 | remove only generation condition from final SQL update | `C.Conditional_write_rechecks_session_generation` | generation changed after validation affects zero rows; labels/watermark unchanged. |
| PC-103 | remove only live-session condition from final SQL update | `C.Conditional_write_rechecks_session_liveness` | session terminated after validation affects zero rows; labels/watermark unchanged. |
| PC-104 | allow sequence <= stored sequence | `A.Older_or_duplicate_sequence_cannot_roll_back_labels` | B then delayed A remains B and duplicate gives no event. |
| PC-105 | omit only expiry predicate | `A.Expired_future_or_unverified_observation_is_ignored` | at expiry and after expiry pins unchanged. |
| PC-106 | omit only future-clock predicate | `A.Expired_future_or_unverified_observation_is_ignored` | completion now+30s+1 tick preserves pins. |
| PC-107 | allow refused/in-progress result with candidate fields | `A.Expired_future_or_unverified_observation_is_ignored` | fabricated candidate on refusal/in-progress does not change pins. |
| PC-108 | assign candidate TabLabel regardless of current null | `A.Null_pins_are_never_created_even_with_claimed_intent` | null tab remains null even with otherwise valid nonnull launch tab intent. |
| PC-109 | assign candidate WorkspaceLabel regardless of current null | `A.Null_pins_are_never_created_even_with_claimed_intent` | null workspace remains null even with valid nonnull workspace intent. |
| PC-110 | ignore launch explicit-tab intent while current pin exists | `A.Null_launch_intent_cannot_authorize_follow` | malformed same-token DTO with null launch tab intent preserves tab pin. |
| PC-111 | ignore launch explicit-workspace intent while current pin exists | `A.Null_launch_intent_cannot_authorize_follow` | malformed same-token DTO with null launch workspace intent preserves workspace pin. |
| PC-112 | commit watermark separately before label write | `A.Labels_and_watermark_commit_together_and_only_changes_notify` | injected label-write rollback leaves old labels AND old watermark. |
| PC-113 | publish event before commit | `A.Labels_and_watermark_commit_together_and_only_changes_notify` | event callback reading a fresh DB scope sees new pins and watermark. |
| PC-114 | unconditionally stamp and publish for any processed observation | `A.Labels_and_watermark_commit_together_and_only_changes_notify` | equal/refusal/duplicate leaves UpdatedAt and event count unchanged. |
| PC-115 | publish result/cache before SaveAtomic | `F.Failed_persistence_does_not_publish_or_advance_cached_labels` | injected save failure preserves prior cache/file labels and has no success receipt. |
| PC-116 | replace unique temp suffix with shared .tmp | `F.Concurrent_snapshot_saves_use_distinct_temp_files` | two writers gated after temp creation have distinct temp paths and both complete without corruption. |
| PC-117 | skip observer pane lease | `F.Observer_coordinates_with_existing_pane_actors` | held-pane fixture has zero label writes until release; observer and actor critical sections never overlap. |
| PC-118 | save captured record without final current-object/generation comparison | `F.Retirement_and_refresh_cannot_resurrect_or_revert_sidecar` | replacement-generation/retired record is not overwritten or resurrected. |
| PC-119 | retire from record captured before successful follow | `F.Retirement_and_refresh_cannot_resurrect_or_revert_sidecar` | follow-first arm retires New labels, never Old. |
| PC-120 | request pane lock before workspace lock | `F.Observer_preserves_lock_order` | recorded acquisition order equals workspace-key, workspace-id, pane with no recursive pane acquisition. |
| PC-121 | hold runtime gate during awaited getter via synchronous wait | `F.Blocked_observation_does_not_block_unrelated_runtime_reads` | unrelated session List/Get completes while selected getter is gated. |
| PC-122 | synthesize last-pane when load returns null | `F.Last_pane_refresh_requires_exact_generation_and_binding` | absent last-pane remains absent after valid follow. |
| PC-123 | omit only last-pane SessionId comparison | `F.Last_pane_refresh_requires_exact_generation_and_binding` | mismatched-session JSON bytes remain identical. |
| PC-124 | omit only last-pane generation comparison | `F.Last_pane_refresh_requires_exact_generation_and_binding` | old/null-generation JSON bytes remain identical. |
| PC-125 | omit only last-pane WorkspaceId comparison | `F.Last_pane_refresh_requires_exact_generation_and_binding` | different-workspace JSON bytes remain identical. |
| PC-126 | omit only last-pane TabId comparison | `F.Last_pane_refresh_requires_exact_generation_and_binding` | different-tab JSON bytes remain identical. |
| PC-127 | omit only last-pane PaneId comparison | `F.Last_pane_refresh_requires_exact_generation_and_binding` | different-pane JSON bytes remain identical. |
| PC-128 | omit only last-pane origin check | `F.Last_pane_refresh_requires_exact_generation_and_binding` | attached-origin JSON bytes remain identical. |
| PC-129 | rebuild last-pane with FromSidecar instead of a label-only with-expression | `F.Last_pane_refresh_preserves_retirement_metadata` | ExitedAtUtc/ExitReason and all non-label fields remain identical. |
| PC-130 | save sidecar labels without the required matching-last-pane repair marker | `F.Last_pane_write_failure_is_repaired_without_new_observation` | crash after first-file commit reloads marker and repairs matching last-pane. |
| PC-131 | return for cooldown before attempting local repair | `F.Last_pane_write_failure_is_repaired_without_new_observation` | second wakeup repairs New labels with unchanged attempt sequence and zero getter delta. |
| PC-132 | clear repair marker before last-pane save | `F.Repair_marker_survives_failure_and_respects_replacement` | second injected failure retains durable marker; later successful retry clears it. |
| PC-133 | retry second-file write using captured record without exact-binding recheck | `F.Repair_marker_survives_failure_and_respects_replacement` | replacement last-pane bytes stay unchanged and obsolete marker clears. |
| PC-134 | advance in-memory seen-sequence on failed GET/save and skip retry | `E.Db_failure_retries_the_same_current_snapshot` | recreated service commits exact current pins/watermark once and relaunch uses original tab. |
| PC-135 | omit candidate when live labels already equal sidecar labels | `E.Missed_observation_is_recovered_after_server_restart` | server absent through initial follow and runner restart still reaches New DB pin after fresh due check. |
| PC-136 | ignore Enabled=false, per site (two independent single-site variants; Code round 3 corrects the round-2 single-method remap, which only killed the double mutant) | (a) service site `HerdrLabelFollowService.SweepAsync` `if (!Enabled) return 0` removed alone: `A.Disabled_sweep_polls_nothing` (calls SweepAsync directly; the hosted service is not involved). (b) hosted site `HerdrLabelFollowHostedService.ExecuteAsync` `if (!Enabled) return` removed alone: `E.Hosted_timer_disabled_polls_nothing_across_ticks` | (a) runner GET count stays zero across two direct sweeps. (b) production-registered hosted service on real HTTP/DB: `ExecuteTask` completes within 5 s (with the hosted check removed the service-site check still returns 0 with zero GETs, so the GET/pin assertions are masked for this variant and the ExecuteTask completion wait is the killing assertion); follow GET count stays zero across three fake-clock hour ticks and pins/watermark unchanged. |
| PC-137 | return from sweep on first session exception | `A.Sweep_is_independent_of_corroboration_and_turn_state` | second eligible agent reaches New pin after first GET fails. |
| PC-138 | invoke existing TabRenameAsync with unchanged label during observation | `E.Observation_never_mutates_herdr_furniture` | observation request delta contains zero tab.rename and zero other mutating RPCs. |
| PC-139 | mark session Exited on label collection timeout | `E.Label_failure_does_not_change_session_lifecycle` | same live session/generation/status remains and kill counter is zero. |
| PC-140 | drop candidate label in SessionRunnerHttpClient mapping while preserving transport success | `E.Renamed_pin_is_used_by_the_next_named_launch` | DB/detail show New and next production named launch reuses original tab; no new tab. |
| PC-141 | accept unknown/missing recorded ChildPid or absence from process_info | `O.Collection_requires_positive_recorded_child` | otherwise eligible collection produces no candidate when child identity is unproven. |
| PC-142 | omit only current TabId equality on cached GET | `T.Cached_candidate_is_suppressed_after_move` | coherent pane move during cooldown suppresses candidate without new label getters. |
| PC-143 | omit only current WorkspaceId equality on cached GET | `T.Cached_candidate_is_suppressed_after_move` | WorkspaceId-only mismatch during cooldown suppresses candidate without new label getters. |
| PC-144 | start label timer despite Herdr Enabled=false | `T.Disabled_or_stopped_pump_does_no_label_work` | two simulated timer ticks produce zero label RPCs. |
| PC-145 | inherit previous same-ID generation next-due time | `T.New_generation_is_immediately_eligible` | new generation at old minute 30 completes first observation without advancing clock. |
| PC-146 | acquire specialist seat before owner in follow or manual writer | `C.Specialist_lock_order_is_preserved` | EF command interception records owner then seat for both writers; concurrent ordinary owner operation completes. |

### Out of scope

- No OnTurnEnd hook in this implementation (D-3). If later enabled, add that caller to the same persisted-admission concurrency test; a second cooldown is forbidden.
- No new session-input/outcome queue and no UserPrompt receipt for labels. Existing input delivery mechanics are unchanged; any expansion must meet the delivery rule above.
- No allocator/leftover-disposal redesign, creating pins, following moves/attached panes, automatic backend retry bypass or pre-Start observation. Their existing safety behavior is regression coverage, not permission to extend the fix.
- No OS-power-loss guarantee, live provider turn, automatic production deploy/restart, or browser repaint guarantee. Native getter/preflight correctness is V-21; unavailable native prerequisites remain visibly unverified.
- No full cross-product of every provider with every refusal: the matrix states coverage and independence rationale. No safety-critical product guard is excluded from the active inventory.

### Cost

Static TestDesign verification only in this dispatch: inspect bodies, audit active IDs/methods, preserve the original plan prefix, `git diff --check`, then commit/push. No product build, native probe or V/R/PC run is claimed.

Ordinary Code uses the repository owner's Unit + named affected integration recipe. Commit before builds/long runs. Use one fresh producer-owned output suffix, forward slash, unique TRX directory per invocation; projects run sequentially. These commands specify every ordinary class selection (aliases are expanded):

```powershell
dotnet build tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c462/ --nologo

dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build --property:OutputPath=bin-c462/ -- --treenode-filter '/*/*/(HerdrLabelObservationTests*)|(HerdrLabelFollowSchedulingTests*)|(HerdrLabelSnapshotTests*)/*' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c462-runner-new

dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build --property:OutputPath=bin-c462/ -- --treenode-filter '/*/*/(HerdrEventPumpTests*)|(HerdrRunnerSessionTests*)|(HerdrAdoptionSweepTests*)|(HerdrNamedTabResolverTests*)|(HerdrNamedTabPlacementTests*)|(HerdrPaneAllocatorTests*)|(HerdrClientTests*)|(HerdrClientSurfaceTests*)|(HerdrPaneSidecarTests*)|(HerdrAttachTests*)|(HerdrPaneDisposalConcurrencyTests*)|(HerdrPaneDisposalStopRegressionTests*)/*' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c462-runner-regression

dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c462/ --nologo

dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c462/ -- --treenode-filter '/*/*/*/*[Category=Unit]' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c462-unit

dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c462/ -- --treenode-filter '/*/*/(HerdrLabelFollowTests*)|(HerdrLabelFollowConcurrencyTests*)|(HerdrLabelFollowWireTests*)|(HerdrLabelFollowFlowTests*)/*' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c462-server-new

dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c462/ -- --treenode-filter '/*/*/(HerdrPlacementSettingsTests*)|(HerdrLaunchContextResolverTests*)|(HerdrPlacementPreflightTests*)|(SessionRunnerHttpClientHerdrWireTests*)|(AgentAttachHerdrTests*)/*' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c462-server-regression

dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build --property:OutputPath=bin-c462/ -- --treenode-filter '/*/*/HerdrLabelFollowLiveTests/Owned_tab_and_workspace_getters_support_rename_follow_validation' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c462-native
```

Method-scoped Mutation example (use the assigned external results root for a sourced run):

```powershell
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c462-pc/ -- --treenode-filter '/*/*/HerdrLabelObservationTests/Binding_identity_components_must_match' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c462-pc31-red
```

Use a fresh results directory on rerun; the final native command needs the explicit fixture variables above and runs separately. Require fresh TRX with every intended class/method, actual expanded counts, pass/fail/skip and nonzero execution; discovery output is not evidence. Assert new V methods exist before accepting a class-level run. Both final-file crash and event-failure receipt methods are included in their class selections. Reproduce suspected inherited red at base with only failing methods. Never co-schedule Antiphon.Tests with Antiphon.Agents.Pty.Tests.

The Mutation example above is PC-31 (returned pane identity). Each of the 119 active PCs uses its own table method, expanded class and correct project, in both red and restored green. Do not widen to its class/suite. Touch restored source timestamps or force rebuild; record verified DLL/source identity, exact failed assertion, counts and restoration for each leg. PC build/fixture errors are incomplete evidence.

Estimated minutes, not measured timings; these estimates replace the original 27-control estimate:

| Owner / work | Minutes |
|---|---:|
| Code: fixture/environment setup (owned fake/DB/clock) | 10 |
| Code: two isolated project builds | 10 |
| Code: Unit + new V methods, named filters above | 20 |
| Code: R classes, named filters above | 15 |
| Code: selected native fixture setup, V-21 and cleanup | 12 |
| **Ordinary V/R floor (Code)** | **67** |
| Mutation: fresh snapshot setup/build | 10 |
| Mutation: 119 red rebuilds x 1 minute | 119 |
| Mutation: 119 exact-method red runs x 0.25 minute | 29.75 |
| Mutation: 119 restores/rebuilds x 1 minute | 119 |
| Mutation: 119 exact-method green runs x 0.25 minute | 29.75 |
| Mutation: 119 per-control evidence checks x 0.5 minute | 59.5 |
| Mutation: final restoration/output inventory audit | 8 |
| **Post-land PC floor (Mutation)** | **375** |
| **Total verification floor: setup/build + ordinary V/R + every PC red/restore/green** | **442 minutes (7h 22m), estimated** |

The expanded floor reflects 119 independently inventoried controls, not 119 new product features. Count the actual parameter cases and replace estimates with measured costs in Code/Mutation reports; host contention or rebuilding a larger graph can exceed them. No current full-suite duration is claimed.

Savings: at least nine one-hour scheduler/restart/failure boundaries advance fake time, saving approximately nine wall-clock hours versus literal waits. Grouping ordinary named classes amortizes host/DB setup. No numeric savings are credited for unmeasured class grouping (0 minutes claimed), and no PC batching savings are assumed (0) because most controls share observer/runtime/service files. Native model spend is zero because no provider turn is required. Missing native setup does not convert its 12-minute acceptance allocation into a passing result.

Cleanup only the exact producer-owned `bin-c462` / `bin-c462-pc` directories inventoried across the project graph, after verifying every resolved absolute path remains under the assigned worktree. Preserve TRX/PC evidence; SourceLanding evidence belongs in its assigned external root and never in a snapshot commit. No source edits while a run is in flight; await every child.

**Pre-handoff audit:** bodies/nearest fixtures read as listed; active guards = 119, mapped = 119, missing = 0, duplicate PC mappings = 0. Active G-28–G-146 each have one distinct PC-28–PC-146 with a compiling-defect recipe, exact method and decisive assertion. Original 27 rows are retired, not counted twice. All 119 recipes are executable after their explicitly named test/setup implementation in Code; none was executed by TestDesign. V/R and native evidence remain pending. Numeric cost is fully allocated. No unverifiable seam remains unassigned; human disposition of D-2/D-3/D-4/D-8 is the next action.
