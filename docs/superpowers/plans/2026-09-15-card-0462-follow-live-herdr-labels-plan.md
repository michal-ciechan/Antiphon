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
