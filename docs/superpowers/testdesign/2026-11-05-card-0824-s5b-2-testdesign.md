# CARD-0824 S5b-2 verification design

Date: 2026-11-05. TestDesign task: `2b5414eb`. Assigned source:
`e6da2453013e7312c52c6ac6c117fc86b6c790d3` on `feat/card-task-2b5414eb`.
Prerequisite source inspected with `git show`: S5b-1 Code
`ff85fc977afc7168a578db6da7f3c02eb71e7df3`, reviewed by task `8f14e2c9`.
The prerequisite is on its task branch at inspection; this document does not claim
that it is already on master. Code starts after the caller has published S5b-1.

This appends the verification contract for **S5b-2 only** to the
[unchanged fix design](../plans/2026-11-05-card-0824-s5b-ref-only-plan.md), following
the [S5b-1 format](2026-11-05-card-0824-s5b-1-testdesign.md). It replaces only that
plan's provisional S5b-2 verification selection (CP-4..CP-7), not its fix design.
No production RefOnly selector/producer is added. `Enabled=false`, `RunnerIds=[]`,
`Roles="none"` remain the defaults; recorded modes continue unconditionally when
admission is off. No desktop operation, activation or S4a enablement is commissioned.

## Verification design

### Inspection

`S/` = `server/Application/Services/`; `A/` =
`tests/Antiphon.Tests/Application/`; `H/` = `tests/Antiphon.Tests/TestHelpers/`.
Locations describe the inspected source, not future insertion lines. New service,
DTO fields, test/helper seams and mutation payloads **remain binding required after
Code**: record full C/L SHA, path, symbol, line/diff, exact method/variant and
assertion before mutation. An absent future type is not an assertion-red witness.

| Bodies read | Boundaries -> witnesses or exclusion |
|---|---|
| Plan D-1..D-11, complete consumer disposition and S5b-2/CP-4..CP-7; S5b-1 template, Review sections 2..5/advisories; performance Debug `origin/feat/card-task-5ae98d31:docs/investigations/2026-11-05-debug-s5b-1-performance.md`, entire report | Required scope, surviving controls and bounded packing -> all V/R; S5b-3 excluded |
| project-context; testing-and-build checkpoint schema/runner, build slots, PC execution, delivery/restoration; test-performance-standards; orchestration-loop stage/delivery/D-2 policy; session-runtime-invariants, agent-card-lifecycle, herdr-sessions ownership/delivery | Layering, no new release authority, complete UserPrompt, pending post-land PCs -> delivery recipes and Cost |
| `S/RemoteWorkspaceService.cs:445–538,538–880,940–1105`; `S/PhoneHomeRunnerMirrorPublisher.cs` all; `TaskProgressDtos.cs`, `RemoteSettlementSyncDtos.cs` all; `S/TaskCompletionProgressService.cs` remote preparation/evaluation below | Shared sync/checkout strategy, exact S, lease, pin, mirror, resume -> S1..S7,D1..D3,C1..C4,R-1..R-3 |
| `A/RunnerSettlementSyncTests.cs`: `Pushed_tip_fast_forwards_exact_owned_checkout`, `C1082_SettledDebtSyncOnlyFastForwardsToTheRecordedSource`, `C1082_SettledDebtSyncRefusesAChangedEpisode`, `Endpoint_change_is_not_followed`, lease/retry/reservation/postcondition bodies; `SyncWorld` setup/service/Git methods; `H/RunnerSettlementWorld.cs` setup, restart, settlement and sweep bodies; `H/DelegationTestServices.cs` graph | Real bare origin + independent runner author; actual reply/debt caller and DI reuse -> S,D,W; existing R methods read in full |
| `S/SettlementSyncRecoveryService.cs` and `S/BlockedTaskSyncRecoveryService.cs` all; `A/SettlementSyncRecoveryTests.cs` due-debt body and `AssertCrashRecoversAsync`; `A/BlockedTaskSyncRecoveryTests.cs` lease-busy and publication-debt bodies | claim/Git/save gaps, identity absence and exact source -> D1..D3,C2 |
| `S/DeliverableBundleService.cs:48–75,138–304` (including output root), `S/GitWorkspaceService.cs:140–270`; `A/DeliverableBundleServiceTests.cs` disk-root byte, manifest/ZIP/budget, mixed/all-doc diff, branch-only, disabled/status bodies and GitRepo helper | Existing disk/moving-ref behavior and binary/blob seams -> B1..B4,R-4; legacy GitRepo's unbounded helper is not copied |
| `S/DelegateCheckProbe.cs:458–511`; `S/AgentTaskReplyService.cs:1287–1303,3702–3865,5173–5203,5493–5528`; `Infrastructure/Files/AgentReportStore.cs:250–280`; `A/DelegateCheckProbeTests.cs` working/idle and read-only probe contract | B..S counts, observation vs baseline, no spill/main file fallback -> R1..R3,B1..B4,W1 |
| `S/TaskParkPublicationService.cs:110–160,280–310,400–442`; `A/TaskParkPublicationTests.cs` workspace-modes, receipt/attempt body and PublicationWorld; `A/BlockedTaskParkResumeTests.cs` first two bodies; `H/BlockedTaskParkFixture.cs` setup/restart; `A/RunnerSeatReleaseFixture.cs` setup and wire registration | Production park/reply/recovery tuple and runner execution, not new parking policy -> C1..C4 |
| `S/AgentTaskDispatcher.cs:1149–1317` drain rebind/release/reload/eligibility bodies; `S/RemoteWorkspaceService.cs:319–322` slot-release producer; `A/PhoneHomeRollingRunnerDrainTests.cs` `C0824_DrainRebindReleasesTheOldSlotFirst`, failed-target-mirror restart, target validation, RollingWorld seed; `A/PhoneHomeRollingRunnerTests.cs` RollingWorld setup; `A/C0824WorkspaceSlotServerTests.cs` real-runner release body and `A/C0824SlotTransportWorld.cs` setup/runtime | Dispatcher TickAsync is the redirect entry; real runner slot ledger closes the scripted-response seam -> C4 |
| `H/BridgeQueueHarness.cs` options, attachment/default `OnSubmitted`, queue/runtime setup; `H/ClaudeDialogFixture.cs` all; `H/CompletedCardMirrorFixture.cs` setup, HoldMutationTurn/ReleaseWhenQueued, reconnect, client and receipt bodies; `A/TaskParkProofWire.cs` all; `PhoneHomeRunnerClient` WorkspacePark/Publish and dispatcher WorkspacePublish arm | Real queue + recipient evidence -> S5,C1..C3,W1. ProofWire currently lacks a park/publish recipient; extend existing dispatcher/runtime fixture, do not claim its current release-only surface covers these calls |
| `S/AgentTaskService.cs:180–225,2184–2220,4260–4328`; `S/AgentTaskDecisionQuestionService.cs:94–143`; `S/AgentSessionService.cs:3760–3796`; `S/AgentControlService.cs:1260–1306`; HomeTask projection and ZombieCensus DB snapshot | Explicit child/auth, file grants, control/session reservation and display -> U1..U3 |
| `A/AgentTaskServiceIntegrationTests.cs` token round-trip/default-directory/merge-child bodies; `A/AgentTaskDecisionQuestionIntegrationTests.cs` first three bodies; `A/AgentSessionServiceIntegrationTests.cs` controlled launch body/setup; `A/AgentControlServiceIntegrationTests.cs` legacy-provider launch/setup; HomeTask first running/blocked projection bodies; ZombieCensus normal-fixture/setup | Nearest fixtures for new consumer file; production callers, not context-wrapper-only tests -> U1..U3 |
| S5b-1 `S/TaskWorkspaceContext.cs` all, `H/RefOnlyWorkspaceTestSetup.cs` all, `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` all, guarded remover entry and legacy-policy precheck/action rereads; base `S/DelegationWorktreeService.cs:660–713,901–920`; `H/CompletedCardCleanupFixture.cs` setup/root; DesktopLegacyWorld setup/seed/tick and policy eligible/age bodies | A-1/A-3/A-4/A-6 -> A1..A3,R-5; binding/identity use -> S4,U3 |
| `A/C0824DesktopLegacyReleaseNativeTests.cs` held-directory/long-path/junction bodies and native setup; S5b-1 RecordPlanned collision predicate | Actual Windows case comparison -> N1, explicitly separate native reservation below |

The commit-range source review must also inspect the final diff's caller closure.
If Code changes shared runtime/contracts instead of server-only strategy/DTO handling,
bind the affected consumers and amend this selection before executing it; no unknown
dependency closure is credited by these rows.

| S5b-1 advisory | S5b-2 disposition |
|---|---|
| A-1 stale classified/validated revision at legacy precheck and action | Carry into Code: two independent revision fences and A1/PCs; preserve the original validated snapshot, hold `task_changed` on token drift |
| A-2 safe Checkout-path deltas | Preserve in rollout notes; no additional product change/test is requested for the informational inventory |
| A-3 untested remover owner-row refusal; merge-back path | Carry now: A2 directly reaches Checkout-typed removal independently of merge-back; both merge-back entries refuse RefOnly before any Git |
| A-4 incomplete preparation | Carry now as A3, with real existing ref/remote S so missing preparation is the decisive refusal at both resolver and resumed protocol |
| A-5 down-migration needs draining RefOnly landings | Preserve downgrade/drain limitation in rollout notes; no migration downgrade is added to S5b-2 |
| A-6 cleanup fixture under TEMP | Carry now as R-5; explicit persistent report root decouples storage from checkout/TEMP location, never loosen artifact preservation |
| A-7 Windows case-sensitive repository collision query | Carry comparator correction and N1's two controls; actual Windows execution remains separately commissioned on a non-desktop runner |

### Delivery inventory

S5b-2 changes existing asynchronous consumers/producers, even though task admission
has no new producer. Each path below finishes at independently observed recipient
state. **Request/queue insert/event/Sent/ack is never delivery evidence.**

| Path | Producer -> destination | Persistence boundary / recovery / durable join | Observable receipt |
|---|---|---|---|
| Sync and mirror publication | Reply `OnTurnEndAsync` or review-evidence preparation -> RemoteWorkspaceService -> PhoneHomeRunnerMirrorPublisher -> real phone-home mutation queue -> RunnerWorkspaceService.PublishAsync -> bare origin; desktop ref strategy consumes exact S | Persisted task/baseline before send; existing runner publication + observation pin; local settlement pin then owned-ref CAS then settlement/debt transaction. Join TaskId/Attempt/BindingId/FullRef/B/endpoint/runner-store and mirror path. Lost send/result is re-observed; never infer a durable request journal where this best-effort publish API has none | S5 recipient handler trace + independent bare-origin exact ref/blob observation; S1/S3 independently read local owned ref and unchanged main checkout/foreign task. Response.Pushed alone fails the oracle |
| Settled sync debt | Settlement transaction -> AgentTaskSyncDebt -> dispatcher RecoverSettlementSync -> ref strategy | DebtId/SettlementEventId/TaskId/Attempt/mode/BindingId/SourceSha/full ref/endpoint, revision/due claim; claim/Git/save separate. Restart retries exact S, not current remote. Missing identity is expected | D1..D3: fresh context Ready only at exact recorded S, independent ref/blob read. Settlement report/event bytes unchanged. Dashboard invalidation is advisory: fresh detail GET recovers missed event; no new durable event delivery claim |
| Report/bundle and caller note | Real reply settlement -> canonical report + immutable pinned-source bundle -> completion obligation/outbox -> CompletionNoteWork/sweep/flush -> **real SessionMessageQueueService** -> caller | SettlementEventId/TaskId/Attempt/NotificationId/QueueMessageId/ParentSessionId/content digest/S; task.Result survives canonical-store failure. Existing unique note key and delivery floor are retained | W1: matching **complete UserPrompt** after attempt floor in same session/generation; ConfirmingPromptSequence binds that row. B1/B2 independently inspect attached bytes/hash. A shortened forwarded note must be complete for its immutable expected body; full report remains in Result/store |
| Blocked/park publication debt | TaskParkPublicationService -> real phone-home WorkspacePark queue -> runner park publisher -> exact origin; BlockedTaskSyncRecovery -> owned desktop ref | ParkId/ActionId/TaskId/Attempt/BlockEventId/report digest/runner-store/session-generation/repository/endpoint/full ref/B/S + mode/BindingId via immutable baseline. Intent before send, digest-bound receipt accepted, claim/Git/save gaps | C2: real recipient execution and independently observed publication bytes/ref, fresh digest-bound receipt and Ready at exactly S. Park receipt never grants a new release/approval; existing conditional release still owns it |
| Blocked answer / parked continuation | AgentTaskReplyService.AnswerAsync -> AgentTaskService.TryAcceptReleasedSeatAnswerAsync -> existing-session queue OR durable ReleasedSeatAnswer -> next-attempt/dispatch queue -> bound remote session input | Answer event id/target attempt/park id/mode/BindingId/runner-store/session-generation/queue key, persisted answer before attempt and queue. Restart dispatcher/queue resumes same owed input; busy recipient defers | C1/C3: default adapter acceptance writes the matching complete UserPrompt with exact answer and parked S/full ref; independently observe remote cwd. Launch recorder/brief/queue alone is insufficient. C4 refused desktop redirect emits no input |
| Existing-task runner redirect | Dispatcher TickAsync/RebindDrainingAsync -> old runner WorkspaceSlotRelease mutation queue; after proved release, persist new runner binding and queue target preparation | TaskId/Attempt/desktop BindingId/old runner-store/slot-generation -> release request/recipient ledger -> task release marker -> new runner/preparation. Unknown/lost release response retains old binding; restart rechecks/replays exact old tuple before moving | C4: real old runner ledger Free/detached for exact task/generation, newer generation untouched; independent source-ref/blob survival; no target prepare until release observed. Target queue acceptance alone cannot prove continuation: C1/C3 require that prompt. This changes mode handling, not the slot release protocol |

**Q recipe (W1 and C1/C3):** attach ClaudeDialogFixture.CreateBridgeAsync to the
same isolated schema/session/agent used by the production caller. Keep default
OnSubmitted so only an actual queue submission writes a complete UserPrompt. Start
with a prior turn to establish sequence/time/generation floor. Each successful
delivery runs already eligible and busy (`MarkWorkingAsync`, assert no receipt or
submission, `MarkIdleAsync`, production turn-end flush). Independently load the
whole transcript; require one exact normalized body, session, digest, attempt floor
and confirming sequence. Repeat recovery and assert no second submission.
Supply a complete `QueuedUserPrompt` without an accepted `UserPrompt` as negative
evidence too; a queued transcript row cannot discharge this receipt contract.

For each producer-to-queue handoff cut **before transaction commit**, **after commit
before enqueue** (enqueue throws), **after queue insertion before link**, **after
link before flush**, **after accepted input before receipt save**. For continuation
also cut before answer save, after answer save/before attempt save, after attempt
save/before dispatch enqueue, after launch/before brief enqueue. Reach each boundary
before injecting; dispose/recreate scopes, in-memory queues and runtime attachment,
retain DB and recipient generation, then drive actual sweep/reconcile/dispatch/flush.
Before acceptance the immutable obligation remains pending; after acceptance catch-up
uses transcript evidence without retyping. No seeded positive transcript may replace
submission. Independently falsify complete-body, destination, generation and floor
evidence; restore the real delivery to prove the fixture can finish.

**P recipe (S5/C2):** extend the existing CompletedCardMirrorFixture queue/connection
setup to expose publish/park production clients and recipient handlers on its owned
RunnerWorkspaceService; keep its `HoldMutationTurnAsync`/`ReleaseWhenQueuedAsync`
ordering proof. One recipient already eligible, one held behind a real mutation
request: queue waiter observed, tested handler not entered, release holder, await
both requests. Cut before send, after enqueue/before handler, after recipient
publication/before response and after response/before server acceptance. C2 also
cuts before/after intent and receipt commit and at recovery `claimed/synced/saved`.
Disconnect/reconnect/rebuild only the owned fixture, advance fake due time and retry
the same semantic binding. Wire RequestId may change: compare immutable task/park/
source tuple and exact origin bytes rather than inventing request deduplication.
Publish replay may inspect again; it must not move any unrelated ref or rewind S.
An enqueue failure has zero recipient writes; recovery must subsequently reach the
recipient. Incomplete/unknown result stays unconfirmed. Retain runner mirror bytes.

For C4 reuse the real C0824SlotTransportWorld pool/dispatcher from the release
round-trip test with RollingWorld's production drain-rebind caller. Scripted
release replies alone prove only server ordering. Observe real recipient ledger
and detach/remaining source bytes. Exercise already-eligible and queued-behind-
mutation recipients, pre-enqueue failure, recipient release/before lost reply,
and task-save/before new-runner preparation cuts. Keep old task/slot/generation
until independent exact release; wrong store/generation leaves it leased and
target unprepared. Then C1/C3 carry the resumed path to UserPrompt. These are
fixture extensions on existing protocols; no new producer or release policy.

Substitutes: local SyncWorld/RecordingGit closes real Git/ref/lease effects but its
LocalMirrorPublisher bypasses the queue; S5's P recipe closes that seam. BridgeQueue
uses a modeled supported-Claude recipient, proving application queue receipt, not
real provider terminal input. Phone-home loopback closes application transport and
recipient execution, not deployed runner operation. Controlled publication/launch
responses alone cannot close P/Q. Barrier callbacks prove races/ordering, not OS
custody. Native Windows comparison and real-provider qualification remain separate;
Linux path strings or ack flags cannot substitute for them.

### Proves it works now

All following methods are **to be authored by Code**. One `[Test]` per method, no
`[Arguments]`; variants are labelled in-body, with isolated task/ref state and a
`reached:<case>:<variant>:<boundary>` assertion. Min counts methods, never variants.
All portable new files are `A/<Class>.cs`, namespace `Antiphon.Tests.Application`,
Category Integration (also Slow where queue/restart warrants), with assembly-local
`ParallelLimiter<ProcessSpawnLimit>` covering spawned helper closure. App groups:
server/git, plus server messaging consumer in W1/C3. No Pty implementation changes.

Aliases: S=`C0824RefOnlySyncTests`; D=`C0824RefOnlyDebtTests`;
R=`C0824RefOnlyReadTests`; B=`C0824RefOnlyDeliverableTests`;
C=`C0824RefOnlyContinuationTests`; U=`C0824RefOnlyConsumerTests`;
A=`C0824RefOnlyBoundaryTests`; W=`C0824RefOnlyReceiptTests`.
These exact class names intentionally avoid matching S5b-1 class prefixes.

Dependency scope read from the current project files: Antiphon.Tests references
server, SessionRunner, Resilience, HostCleanup, messaging/client/gateway/testing,
FakeLlmApi, watchdog and checkpoint/audit tools plus their fixture children.
Server references Pty, Messaging, Messaging.Client, SessionRunner.Contracts,
Resilience and Observability. Only server Git/delegation and its queue/artifact
consumers change; build dependencies are not a request to execute their entire
test assemblies. Downstream server API/Home/Zombie projections and child/session
callers are covered by U1..U3, completion/progress by S/R/W, blob readers by B.
No Messaging, SessionRunner or Pty production change is assumed; a shared DTO
change outside server must bind its extra downstream tests before Code proceeds.

Fixtures: **SYNC** = extend SyncWorld with explicit RefOnly seed and persisted
binding (S5b-1 setup pattern), author setup checkout removed before SUT, real bare
origin and independent runner. **SETTLE** = RunnerSettlementWorld + same graph;
**PARK** = PublicationWorld/RunnerSeatReleaseFixture + same persisted tuple;
**REDIRECT** = RollingWorld drain caller + C0824SlotTransportWorld recipient;
**Q/P** = preceding real-queue recipes; **LAND** = LandingSafetyHarness;
**LEGACY** = DesktopLegacyWorld. Extend DelegationTestServices, not a copied DI graph.
Setup may create fixture-owned author checkouts; trace resets before SUT, which
must not create one. Main HEAD/index/tracked bytes, foreign task/ref/decoy bytes,
registry and full ref inventory are captured independently before and after.

| Case | Exact method | Recipe / variant / production reachability | Decisive oracle / red-first witness / classification |
|---|---|---|---|
| S1 | `Pushed_tip_advances_only_owned_ref` | SYNC through SyncAsync; L=B then runner publishes S; both branch naming forms, absent identity and foreign decoy identity | `owned-ref=S`, `evaluation-progress-at-S` through TaskCompletionProgressService; changed bound ref after sync is indeterminate; Confirmed S with explicit RefOnly/binding provenance and before/after ref tips; `main-intact`, `foreign-intact`, no identity cwd/checkout/reset/clean/worktree/delete/force/wildcard fetch. Base refuses missing registration; positive S is assertion-red. Portable integration |
| S2 | `Equal_tip_is_idempotent_and_ancestry_failures_preserve_refs` | SYNC equal B and equal non-B S, then B !ancestor S and L !ancestor S: ahead/diverged/rewound independently, with real graph and unknown ancestry I/O variants | `equal-confirmed`, no owned-ref rewrite on equal; `unconfirmed:<relation>`, no unsafe mutation for each negative. Base equal case cannot confirm; old ancestry negatives retained. Portable integration |
| S3 | `Concurrent_ref_advance_fails_compare_and_swap` | SYNC barrier immediately before update-ref; independent writer moves L to T after under-lease validation; paired CAS I/O failure/post-read unknown/wrong S. Follow with fresh valid retry | `race-preserved=T`, `Confirmed=false`; failed/unknown CAS not success; post-read mismatch not confirmed; valid retry exact S. Base lacks ref CAS, scaffold reaches assertion. Portable integration |
| S4 | `Endpoint_pin_and_binding_changes_refuse` | SYNC initial and under-lease endpoint change/ambiguous; exact observation pin missing/changed; task binding id, binding TaskId, repo/common, full ref, identity, base, preparation, revision and baseline discriminator/version/mode/binding each changed alone. Barrier variants use valid stale outer snapshots so inner guard is reached | `refused:<dimension>:<phase>`, no owned/foreign ref or main writes. Unknown object/pin/inspection remains unconfirmed. Base has no ref context and cannot reach valid counterpart. Portable integration |
| S5 | `Reported_tip_and_mirror_publication_keep_existing_contract` | SETTLE through reply and SyncForReviewEvidence; P recipe, remote initially absent/equal, unpushed descendant; dirty-equal, diverged/behind, pushed=false, changed re-observation, missing capability/runner binding, report naming wrong/short/full S. Bind-refusal recovery must not publish | `recipient-published=S` independently observed and confirmed; `report-exact-S`, `mirror-proof-required`, bad cases unconfirmed with no foreign/main writes; no publication in report-only recovery. Base can publish only with Checkout; new RefOnly positive is red. Portable integration, Linux loopback fixture |
| S6 | `Lease_window_and_timeout_leave_no_running_child` | SYNC real competing repository lease + recording child barrier, reservation held; fake-clock slice/cumulative timeout, caller cancellation, foreign/released lease; release contention within budget valid counterpart | `no-unleased-write`, `reserved-ref-preserved`, bounded slice/fairness cleared on terminal exit, cancellation propagates; actual child exit/output drains and InFlight=0 after each exit. Base RefOnly positive is red; retained lifetime guards otherwise base-green. Portable integration |
| S7 | `Source_checkout_attachment_is_not_fast_forwarded` | SYNC owned full ref actually checked out elsewhere, independently before initial validation and during lease gap; no-attachment valid counterpart | `attached-ref-preserved`, attached index/files/HEAD unchanged, unconfirmed before CAS. Test source registration itself, not identity-path existence. Base has no RefOnly guard; new mode positive red. Portable integration |
| D1 | `Absent_identity_directory_does_not_supersede_debt` | SETTLE actual Pending debt then recovery with absent identity, plus decoy present; `claimed/synced/saved` crashes and post-CAS/pre-debt-save crash | `debt-ready=S`, exact local ref/blob and fresh detail; no Superseded for expected absence at claim or save; restart idempotent. Base RegistrationGone supersedes: assertion-red. Portable integration |
| D2 | `Recovery_confirms_only_recorded_sha_and_attempt` | SETTLE debt via producer; remote moves past S, task attempt/mode/binding/revision/baseline/path changes independently before claim and after Git before save; concurrent debt revision winner. Keep other matching tuple components coherent | `debt-not-ready:<dimension>`, no false ConfirmedSha, original settlement/history untouched; exact valid S recovers. Base RefOnly positive red; negative episode policy retained. Portable integration |
| D3 | `Disabling_gate_keeps_existing_recovery_alive` | SETTLE restart, all RefOnly admission off/empty; settlement debt-mint switch off after debt exists; Pending due vs Held/not-due/Ready controls; version-1 Checkout JSON round-trip and RefOnly missing discriminator | `existing-mode-retained`, due exact S Ready despite flag-off, Held remains Held/no Git, no new producer; invalid RefOnly baseline refuses. Base new-mode positive red, v1 retained. Portable integration |
| R1 | `Check_distinguishes_creation_baseline_from_remote_observation` | actual DelegateCheckProbe.GatherAsync/RenderDigest on SYNC task: only creation B; pinned confirmed S; exact read-only observed T with objects unavailable; no endpoint/unknown observation. Instrument Git mutations and save refusal | `check-sha-and-time`, baseline labelled baseline/stale, newer remote observation labelled with source/time; missing log/counts and runner dirt explicitly unknown (never zero/clean). No Check fetch/ref move/write. Base returns null/desktop state: red. Portable integration |
| R2 | `Reply_progress_counts_confirmed_range_never_main_head` | SETTLE real completion and bind-refusal paths; main has unrelated commit/files; confirmed S, admitted pending pinned S, pending unpinned/unknown; call progress and repair-hint count consumers | `count=B..S`, `pending-not-confirmed`, unknown stays unavailable; no HEAD count or identity/status read. Base new-mode positive red; existing pending conservative behavior retained. Portable integration |
| R3 | `Long_report_retains_text_without_materializing_identity` | SETTLE long marked report, canonical store success/missing/failure; explicit persistent artifact root; differing decoy report under identity/main; bind-refusal/report-store path consumers | `result-full` (head/middle/tail all survive DB reload), canonical pointer when available, explicit unavailable otherwise, `no-spill-write`, no decoy read. Base writes WorkingDirectory spill: red. W1 proves delivery independently. Portable integration |
| B1 | `Bundle_reads_exact_settled_bytes_despite_stale_disk` | SETTLE actual no-channel Succeeded reply calls bundle; Plan/Docs and named other role variants; committed BOM/CRLF/Unicode bytes at S disagree with every disk root; explicit allowed durable output root | `bundle-bytes=S`, manifest SHA256/length exact, no identity/main source reads or writes. Canonical artifact destination may be its configured ignored store, never unrelated source root. Base disk-first chooses decoy: red. Portable integration |
| B2 | `Bundle_pins_one_sha_while_branch_moves` | actual TryBuildAsync from settled task, barrier after first file; branch advances to T with different second file/length; pending admitted S vs unconfirmed/invalid-source controls; replay after restart | `all-members-from=S` including size queries, diff and hashes; missing admission yields omitted/unavailable, never moving-ref fallback. Base second file at moving branch: red. Portable integration |
| B3 | `Commit_diff_handles_docs_only_rename_delete_and_named_paths` | real B..S diff through bundle caller: added/renamed/deleted docs, mixed code+doc unnamed vs named, quoted/space/Unicode filenames via -z diff; dirty/untracked canonical decoys | `paths=expected-at-S`, rename destination only, deleted absent, mixed unnamed no bundle, named safe doc included. No working-tree diff/untracked union. Base uses worktree: red. Portable integration |
| B4 | `Missing_oversize_excluded_and_link_blobs_are_not_attached` | exact S contains symlink entry, excluded docs/cards and .antiphon, traversal/absolute requests, missing blob; per-source/total limits at boundary, lying/failed length and streaming overflow via I/O seam; safe small doc counterpart | `omitted:<reason>`, incomplete explicit; symlink/path/excluded source never opened, byte budget checked before allocation and during stream, safe bytes copied/hash exact. Small configured limits avoid 64MiB allocations. Base disk fallback/unsafe type fails: red. Portable integration |
| C1 | `Park_resume_never_adds_a_desktop_worktree` | PARK actual AgentTaskReplyService.AnswerAsync/dispatch with seeded Published RefOnly tuple; source S/receipt verified, remote execution missing then existing; Q recipe for the resumed prompt, absent identity then foreign decoy | `remote-prompt-received` exact answer/S/ref, `runner-cwd=bound`, same mode/binding; desktop registry delta zero and no prune/add/reset/clean. Base RefuseParkResume recreates checkout: red. No S5b-3 new-task mode selection exercised. Portable integration |
| C2 | `Pending_park_debt_keeps_exact_publication_and_mode` | PARK production publication and BlockedTaskSyncRecovery, P recipe and recovery cuts; receipt digest/source/mode/binding/attempt/store/generation each independently wrong; parking and admission disabled after acceptance | `park-ready=S` exact ref/bytes and imported proof; changed tuple never Ready; no fresh approval/release from debt, mirror retained. Base lacks mode-aware park/debt: positive red. Portable integration, Linux queue fixture |
| C3 | `Blocked_reply_preserves_runner_and_binding_when_gate_closes` | PARK live busy/idle session answer plus parked answer after release, close feature gate/restart between acceptance/dispatch, Q cuts; corrupt destination/generation/body/floor and same-key body separately | `answer-received-once`, mode/binding/runner retained; accepted answer event maps to target attempt, queued/live and released branches never both deliver. Base has no recorded RefOnly continuation: positive red. Portable integration |
| C4 | `Runner_redirect_requires_old_custody_and_rejects_desktop` | actual AgentTaskDispatcher.TickAsync -> RebindDrainingAsync/ReleaseSlotBeforeRebindAsync; old slot release pending/unknown, wrong store and generation, valid remote redirect; separately seed the same recorded RefOnly task with a missing/desktop RunnerId and drive TickAsync | `redirect-refused:<dimension>` before desktop or target preparation/launch; `ref_only_requires_remote_runner` for desktop; no checkout materialization. Valid remote rebind retains original source binding/base, new runner custody explicit. Base identity may become cwd: red. Portable integration |
| U1 | `Child_auth_uses_explicit_repo_ref_and_remote_execution` | actual token AuthenticateAsync -> authorized orchestrator CreateAsync/follow-up/default-child context/CreateMergeTask; frozen S then parent branch moves; Worker permission negative retained, explicit Shared/local requests | `child-own-worktree-from=S`, caller explicit repository/ref/remote cwd with runner path semantics; Shared/main access refused, unusable remote binding holds; merge helper cannot default to identity/main Shared workspace. Base Authenticate selects identity: red. Portable integration |
| U2 | `Decision_and_control_paths_do_not_grant_identity_file_access` | actual decision CheckAsync with bound runner cwd and file policy; session launch reservation and control adoption reconstruction with task owner; missing/wrong runner/store/session binding and traversal | `grant-root=runner`, safe relative runner path grants only within runner; identity/RepoPath grants refused, control/session no local launch at identity. Base chooses WorktreePath: red. Portable integration with modeled remote adapter; native pane custody unchanged |
| U3 | `Same_repo_tasks_keep_distinct_ownership_and_display_mode` | WorkspaceUseAdmission, AdvisoryChangePolicy, TerminalRunnerSeatRelease ownership, HomeTask.Get, ZombieCensus DB snapshot and inherited-follow-up projection; two tasks share repo but distinct identities/refs; same ref/slot-generation negatives | `owners-distinct`, same-ref fenced, same-generation ambiguous; displays RefOnly not missing/clean. Absence grants no release/census kill authority. Base new-mode projection red, existing equality fences retained. Portable integration |
| A1 | `Legacy_release_keeps_the_classified_revision` | LEGACY two separate barriers `before:legacy-precheck` and `before:legacy-action`; change only ConcurrencyToken while task stays eligible Checkout and same SHA. Reach stale snapshot through production RunTick; paired unchanged eligible task retires | `stale-snapshot-held:<site>` task_changed before filing; no release/intent/removal for changed task; same-token control acts. S5b-1 reread swaps token: assertion-red. Portable integration, A-1 |
| A2 | `Local_merge_and_checkout_typed_removal_refuse_ref_only_owner` | first call TryMergeBackAsync and TryCleanupNoChangeAsync on RefOnly with plausible registered decoy; second independent leg calls production IWorktreeManager.TryRemoveAsync LocalMerge with Checkout-typed source and current RefOnly owner row. Use otherwise removable clean/published fixture and real evidence/lease | `merge-zero-git` for both entry points; `owner-row-refused=desktop_not_required` before remover Git, all bytes/refs retained. Contradictory owner variant gets named refusal. First fence must not mask second leg. Base merge runs Git; S5b-1 remover negative retained but O-1 did not reach it. Portable integration, A-3 |
| A3 | `Incomplete_preparation_refuses_initial_and_resumed_land` | LAND correct ref/remote S and otherwise landable binding, states Planned and RefCreated; initial resolver and independently resumed protocol past resolver with persisted operation, positive Published counterpart | `preparation-incomplete:<site>:<state>` ref_only_preparation_incomplete before child/ref/remote writes. Must not fail earlier branch/remote observation. Base mode positive red; S5b-1 O-2 survives until these variants. Portable integration, A-4 |
| W1 | `Settlement_note_reaches_caller_and_recovers_handoffs` | SETTLE real report/settlement producer, Q recipe, successful sync and Pending debt report, store unavailable long-report case; independent transcript inspection after service/queue rebuild | `caller-received-once`, complete immutable note with same TaskId/Attempt/digest/floor/ConfirmingPromptSequence; full Result retained; Sent/no prompt never confirmed. Base RefOnly cannot complete intended outcome; queue contracts otherwise retained. Portable integration |

V-1 = S1..S7 (owned ref sync), V-2 = D1..D3 (durable debt), V-3 = R1..R3
(honest reads/report retention), V-4 = B1..B4 (pinned artifacts), V-5 = C1..C4
(continuations), V-6 = U1..U3 (consumer authority), V-7 = A1..A3 + R-5
(Review carry-ins), V-8 = W1 (caller receipt). Layer/test/expected are the frozen
rows above; ordinary commands are the CP filters below. No V result is claimed here.

### Guards the regression

- R-1: Checkout still fast-forwards exactly S | retained Integration |
  `RunnerSettlementSyncTests.Pushed_tip_fast_forwards_exact_owned_checkout` |
  clean registered task checkout HEAD/content at S; main/other task unchanged.
- R-2: Checkout endpoint change/absence/ambiguity refuses before following it |
  `RunnerSettlementSyncTests.Endpoint_change_is_not_followed` | exact reason,
  no fetch/ls-remote/merge, original checkout remains B.
- R-3: Checkout debt only follows recorded S |
  `RunnerSettlementSyncTests.C1082_SettledDebtSyncOnlyFastForwardsToTheRecordedSource` |
  first S confirmed, later remote T refuses TipNotReported and stays S.
- R-4: Legacy bundle behavior stays available; mixed unnamed docs remain absent |
  B1/B3 include paired Checkout task through the same bundle caller; exact-byte and
  eligibility assertions. No complete DeliverableBundle suite is needed for a mode arm.
- R-5: Cleanup C1 remains location-independent |
  `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs`
  from prerequisite, including card-intent; explicit persistent report/evidence
  root outside both TEMP and source checkout, fresh canonical report preservation
  asserted before destructive leg. Test source checkout under fixture-owned TEMP
  and ordinary managed root as in-body variants; never change global TEMP to evade
  the case. Independently configure a TEMP-contained report-store destination and
  call the real store before changing the fixture owner to RefOnly; require no
  successful preservation receipt (`temporary-report-root-refused`) and retained
  source bytes. This negative storage variant must finish at an assertion rather
  than abort fixture setup. A-6's environmental red is not a valid PC red.
- R-6: Off default and no new producer | D3/U1 through recorded binding, source
  diff confirms no production RecordPlannedAsync/RefOnlyDesktopGate.Admit call or
  assignment of new-task RefOnly mode. A7's direct binding-service fixture is not
  activation. Preserve v1 Checkout DTO JSON and result enum/outcome semantics.
- R-7: Classification/performance registration | CP-5 existing 47 executions;
  all new tests Integration, native tagged separately, helpers process-limited,
  timings recorded. This meta-policy check supplies no product/delivery proof.

### Guard inventory

Every safety-critical assertion selected above has a control below, including
retained or currently untested native guards. No exemptions. Independently
bypassable sites/dimensions are separate rows; each G-n maps to exactly one PC-n.
Untouched internal park receipt, Git child and terminal release invariants beyond
the explicitly asserted dimensions retain their owner batteries and are excluded
from claims here. Final extraction may reveal an additional bypass; bind it with
its own PC before declaring this inventory complete at C/L.

The data-loss entry points are explicit:

| Hazardous path | Fence / recipient oracle |
|---|---|
| Settlement or resume falls into desktop checkout/merge/reset/clean/add/prune | S1,S6,C1: RefOnly strategy and genuine lease; zero forbidden desktop commands, registry/bytes unchanged |
| Unchecked update-ref rewinds a raced/ahead/diverged ref or advances another owner's ref | S2..S4,S7: full binding/endpoint/ancestry/registration proof plus expected-old CAS; independent exact ref and attached-tree reads |
| LocalMerge, no-change cleanup or Checkout-typed remover deletes a RefOnly identity/foreign checkout | A2 and R-5: early merge refusal and independently reached current-owner fence, persistent artifact preservation; exact bytes/admin/ref inventory retained |
| Continuation/redirect/child/helper/control uses identity or RepoPath as editable desktop cwd | C1,C4,U1,U2: authenticated execution binding, release/store/generation proof, remote cwd and no local launch/edit |
| Publication follows wrong endpoint/runner or treats a lost send as successful push | S5,C2: real queued recipient and independent exact origin bytes/ref; failure remains owed/unconfirmed |
| Debt absence/revision drift loses the exact unpublished source or follows newer work | D1..D3,C2: same immutable S/episode, claim/save comparisons, no false Superseded/Ready; preserve history and mirror |
| Missing canonical report/source causes loss, decoy attachment or workspace spill | R3,B1..B4,W1: full Result retained, admitted pinned blobs, bounded bytes/hash, explicit omission and real caller receipt |
| Settlement/cleanup deletes a ref/pin it did not create | S1,A2,R-5: no RefOnly desktop deletion authority and full foreign-ref/pin inventory unchanged. Owned task ref also stays; S2 alone owns delayed deletion |
| Windows aliases allow conflicting ref owners | N1: host-aware repository equality, unique full-ref owner; native-only obligation |

CARD-1708 reflog-only salvage stays with its salvage-ref work. No test or fix here
pretends branch publication accounts for reflog-only commits.

| Guard | Plan reference + safety-critical invariant | Positive control |
|---|---|---|
| G-1 | D-4/D-5 mode strategy never falls back to a desktop checkout | PC-1 |
| G-2 | D-3 exact owned full ref, not main HEAD or reconstructed legacy ref | PC-2 |
| G-3 | D-9 no deletion of owned/foreign refs or recovery pins in settlement | PC-3 |
| G-4 | D-3 task binding pointer is current before sync | PC-4 |
| G-5 | D-3 binding belongs to full TaskId | PC-5 |
| G-6 | D-4 repository matches recorded binding | PC-6 |
| G-7 | D-4 live common directory matches recorded binding | PC-7 |
| G-8 | D-2 task identity path matches durable binding | PC-8 |
| G-9 | D-3 task full ref matches binding full ref | PC-9 |
| G-10 | D-3 frozen selected base SHA matches binding | PC-10 |
| G-11 | D-3 frozen selected base ref matches binding | PC-11 |
| G-12 | D-3 frozen selected base provenance matches binding | PC-12 |
| G-13 | D-3 frozen selected base owner matches binding | PC-13 |
| G-14 | D-3 sync accepts only Published preparation | PC-14 |
| G-15 | D-4 binding revision is rechecked after lease gap | PC-15 |
| G-16 | D-6 supported baseline version is mandatory | PC-16 |
| G-17 | D-6 baseline mode matches task mode | PC-17 |
| G-18 | D-6 baseline binding matches task binding | PC-18 |
| G-19 | D-4 fresh binding still matches task under lease | PC-19 |
| G-20 | D-4 initial endpoint matches dispatch fingerprint | PC-20 |
| G-21 | D-4 endpoint still matches under lease | PC-21 |
| G-22 | D-4 exact observation pin names S | PC-22 |
| G-23 | D-4 initial L equals fresh under-lease L | PC-23 |
| G-24 | D-4 atomic advancement includes expected old L | PC-24 |
| G-25 | D-4 post-CAS read must observe exact S | PC-25 |
| G-26 | D-4 settlement pin write must succeed before CAS | PC-26 |
| G-27 | D-4 B ancestor S before lease | PC-27 |
| G-28 | D-4 B ancestor S again under lease | PC-28 |
| G-29 | D-4 L ancestor S (equal separately idempotent) | PC-29 |
| G-30 | D-4 no attached source checkout at initial inspection | PC-30 |
| G-31 | D-4 no source checkout attached during lease gap | PC-31 |
| G-32 | D-4 genuine owned live repository lease precedes CAS | PC-32 |
| G-33 | D-4 workspace reservation admission excludes retirement/writer | PC-33 |
| G-34 | D-4 deadline joins killed child and drained output | PC-34 |
| G-35 | D-4 caller cancellation joins child independently | PC-35 |
| G-36 | D-4 cumulative lease wait is sliced and bounded | PC-36 |
| G-37 | D-4 terminal sync clears fairness waiter | PC-37 |
| G-38 | D-4 mirror divergence/behind never authorizes advancement | PC-38 |
| G-39 | D-4 descendant mirror must prove successful push | PC-39 |
| G-40 | D-4 re-observed remote tip equals mirror publication tip | PC-40 |
| G-41 | D-4 bind-refusal report names one full exact S | PC-41 |
| G-42 | D-4 missing publication capability cannot synthesize publication proof | PC-42 |
| G-43 | D-5 missing authenticated runner binding cannot route publication elsewhere | PC-43 |
| G-44 | D-4 recovered report-only sync does not publish runner mirror | PC-44 |
| G-45 | D-4 absent remote source is unconfirmed (not empty success) | PC-45 |
| G-46 | D-6 expected identity absence never supersedes claim | PC-46 |
| G-47 | D-6 expected identity absence never supersedes save | PC-47 |
| G-48 | D-4 settled sync requests debt's recorded S only | PC-48 |
| G-49 | D-3 settlement debt claim requires same attempt | PC-49 |
| G-50 | D-6 settlement debt claim requires same mode | PC-50 |
| G-51 | D-3 settlement debt claim requires same binding | PC-51 |
| G-52 | D-3 task attempt rechecked before debt save | PC-52 |
| G-53 | D-6 mode rechecked before debt save | PC-53 |
| G-54 | D-3 binding rechecked before debt save | PC-54 |
| G-55 | D-3 baseline rechecked before debt save | PC-55 |
| G-56 | D-4 debt save requires claimed revision/state | PC-56 |
| G-57 | D-4 Ready requires exact debt full ref | PC-57 |
| G-58 | D-4 Ready requires confirmed after-ref S | PC-58 |
| G-59 | D-4 Ready requires remote S | PC-59 |
| G-60 | D-6 recorded RefOnly sync/debt ignores new-admission gate | PC-60 |
| G-61 | D-6 RefOnly baseline missing discriminator is invalid | PC-61 |
| G-62 | D-6 Held debt is observation-only, never a fresh sync | PC-62 |
| G-63 | D-10 legacy v1 Checkout JSON retains Checkout interpretation | PC-63 |
| G-64 | D-8 Check baseline B is not current runner observation | PC-64 |
| G-65 | D-8 runner working-tree dirt is unknown, not synthetic zero | PC-65 |
| G-66 | D-8 Check is read-only (no fetch/ref advance) | PC-66 |
| G-67 | D-8 Check observation includes source SHA and time | PC-67 |
| G-68 | D-8 missing remote objects leave log/counts unavailable | PC-68 |
| G-69 | D-8 reply progress counts B..confirmed S | PC-69 |
| G-70 | D-8 repair-hint counts use the same admitted source | PC-70 |
| G-71 | D-8 pending read requires verified source ancestry | PC-71 |
| G-72 | D-8 pending source must be a full object id | PC-72 |
| G-73 | D-8 pending source object must be locally readable | PC-73 |
| G-74 | D-8 pending reads bind exact admitted pin/source | PC-74 |
| G-75 | D-8 report retention never truncates durable Result | PC-75 |
| G-76 | D-8 RefOnly report failure never spills into identity/main | PC-76 |
| G-77 | D-8 report recovery never scans decoy identity/main files | PC-77 |
| G-78 | D-8 bundle sources never use disk fallback | PC-78 |
| G-79 | D-5 artifact destination is admitted persistent output, not identity | PC-79 |
| G-80 | D-8 source bytes/hash preserve binary representation | PC-80 |
| G-81 | D-8 bundle requires admitted confirmed/pending S | PC-81 |
| G-82 | D-8 one S frozen for diff/length/all bundle files | PC-82 |
| G-83 | D-8 invalid pending source cannot rescue bundle from branch | PC-83 |
| G-84 | D-8 RefOnly diff is committed B..S, not worktree/status | PC-84 |
| G-85 | D-8 rename includes destination path at S | PC-85 |
| G-86 | D-8 deleted paths remain absent | PC-86 |
| G-87 | D-8 mixed unnamed code/docs does not imply doc bundle | PC-87 |
| G-88 | D-8 relative blob paths cannot escape admitted repository scope | PC-88 |
| G-89 | D-8 excluded docs/cards and .antiphon never attach | PC-89 |
| G-90 | D-8 symlink/non-blob tree entries never attach | PC-90 |
| G-91 | D-8 missing blob never falls back to disk | PC-91 |
| G-92 | D-8 source size admitted before allocating/reading blob | PC-92 |
| G-93 | D-8 bounded stream rejects lying/changed length | PC-93 |
| G-94 | D-8 total bundle budget decreases per accepted member | PC-94 |
| G-95 | D-8 unavailable/omitted source cannot claim complete empty success | PC-95 |
| G-96 | D-5 park resume never materializes desktop checkout | PC-96 |
| G-97 | D-5 continuation launches in bound runner cwd | PC-97 |
| G-98 | D-6 park proof carries recorded mode | PC-98 |
| G-99 | D-3 park proof carries recorded binding | PC-99 |
| G-100 | D-4 persisted park receipt digest must match | PC-100 |
| G-101 | D-3 park recovery requires exact task attempt | PC-101 |
| G-102 | D-5 park proof binds runner store | PC-102 |
| G-103 | D-5 park proof binds accepted session generation | PC-103 |
| G-104 | D-4 park debt follows only recorded S | PC-104 |
| G-105 | D-4 park debt save requires current episode/revision | PC-105 |
| G-106 | D-6 park debt save rechecks mode after Git | PC-106 |
| G-107 | D-3 park debt save rechecks binding after Git | PC-107 |
| G-108 | D-6 accepted park debt recovers when admission/parking are off | PC-108 |
| G-109 | D-6 accepted blocked answer survives restart before attempt/queue | PC-109 |
| G-110 | D-6 one answer event targets only one attempt/queue key | PC-110 |
| G-111 | D-6 answer receipt requires complete exact body | PC-111 |
| G-112 | D-6 answer receipt requires actual UserPrompt kind | PC-112 |
| G-113 | D-6 answer receipt binds destination session | PC-113 |
| G-114 | D-6 answer receipt binds accepted generation | PC-114 |
| G-115 | D-6 answer receipt is above attempt floor | PC-115 |
| G-116 | D-6 accepted input recovered without retyping | PC-116 |
| G-117 | D-5 RefOnly redirect to desktop is refused before launch/prep | PC-117 |
| G-118 | D-5 pending/unknown old slot custody refuses rebind | PC-118 |
| G-119 | D-5 reroute release proof binds old runner store | PC-119 |
| G-120 | D-5 reroute release proof binds slot generation | PC-120 |
| G-121 | D-5 authentication returns explicit runner context | PC-121 |
| G-122 | D-5 default child owns separate worktree | PC-122 |
| G-123 | D-5 child source is frozen S | PC-123 |
| G-124 | D-5 explicit Shared/local request cannot gain main checkout authority | PC-124 |
| G-125 | D-5 merge-helper creation does not share identity/main path | PC-125 |
| G-126 | D-5 file decision scope uses bound runner root/semantics | PC-126 |
| G-127 | D-5 session owner reconstruction cannot launch from identity | PC-127 |
| G-128 | D-5 control adoption needs authenticated remote execution binding | PC-128 |
| G-129 | D-2 shared repo does not collapse reservation ownership | PC-129 |
| G-130 | D-4 same full ref still excludes another writer | PC-130 |
| G-131 | D-5 advisory helper distinct ownership still required | PC-131 |
| G-132 | D-5 same slot generation remains ambiguous despite distinct desktop identity | PC-132 |
| G-133 | D-2 HomeTask projection exposes RefOnly identity as history | PC-133 |
| G-134 | D-2 ZombieCensus snapshot cannot turn RefOnly absence into orphan custody | PC-134 |
| G-135 | D-2 inherited follow-up displays RefOnly source not missing worktree | PC-135 |
| G-136 | A-1/D-9 precheck keeps classified revision | PC-136 |
| G-137 | A-1/D-9 action keeps validated revision | PC-137 |
| G-138 | A-3/D-5 merge-back refuses RefOnly before any Git | PC-138 |
| G-139 | A-3/D-5 no-change cleanup refuses RefOnly before any Git | PC-139 |
| G-140 | A-3/D-9 Checkout-typed removal rechecks RefOnly owner row | PC-140 |
| G-141 | A-3/D-9 contradictory Checkout owner/binding refuses removal | PC-141 |
| G-142 | A-4/D-7 initial resolver refuses incomplete RefOnly preparation | PC-142 |
| G-143 | A-4/D-7 resumed protocol refuses incomplete preparation independently | PC-143 |
| G-144 | D-8 durable settlement/outbox precedes enqueue | PC-144 |
| G-145 | D-8 queue link recovery uses immutable note identity/body | PC-145 |
| G-146 | D-8 caller receipt requires complete body not Sent/ack | PC-146 |
| G-147 | D-8 caller receipt requires actual UserPrompt kind | PC-147 |
| G-148 | D-8 caller receipt binds destination session | PC-148 |
| G-149 | D-8 caller receipt binds accepted generation | PC-149 |
| G-150 | D-8 caller receipt is beyond delivery-attempt floor | PC-150 |
| G-151 | D-8 caller transcript confirmation survives receipt-save crash | PC-151 |
| G-152 | D-8 caller queue replay is idempotent | PC-152 |
| G-153 | D-10 Checkout strategy still performs exact clean FF | PC-153 |
| G-154 | D-4 Checkout endpoint failure stays fail-closed | PC-154 |
| G-155 | D-4 Checkout debt cannot follow newer remote T | PC-155 |
| G-156 | A-6/D-8 cleanup artifact preservation needs persistent root | PC-156 |
| G-157 | A-7/D-3 Windows binding owner comparison respects host path equality | PC-157 |
| G-158 | A-7/D-3 Windows legacy owner comparison respects host path equality | PC-158 |
| G-159 | D-4/D-8 completion re-observes the bound RefOnly ref at S rather than an identity checkout | PC-159 |

### Positive controls

These are mandatory **post-land Mutation** work, not TestDesign executions. Review
judges witnesses/payloads before land; Code runs V/R. Bind each payload to final C
and published L; do not weaken tests to fit an earlier refusal. For every PC run
baseline green -> one compiling defect -> exact intended assertion red -> restore
-> fresh build -> same exact method green, Min=1 each leg. Missing tests, zero
executions, build/setup errors, timeout or a different earlier assertion are invalid
red. No whole-class/suite mutation selection. All named variants run, including
initial/under-lease and claim/save sites as separate PCs. Keep reached evidence
and per-PC red/restore/green provenance externally under the assigned verification
root; SourceLanding never commits/pushes amendments from its snapshot.

In the following table the exact method is the case alias expanded above (or R-1,
R-2, R-3, R-5 exact method). The command is always
`--treenode-filter "/*/*/<ExactClass>/<ExactMethod>"`; no class suffix wildcard is
needed for these exact method-scoped PC invocations. Each assertion string is a
fixed failure label Code must implement; variant suffixes bind the affected dimension.
Payloads marked `new` name the required compiling behavior change at the final
site, not a nonexistent symbol. Never replace a refusal with another refusal.

| PC | Compiling defect (break corresponding G-n) | Exact case | Intended red assertion |
|---|---|---|---|
| PC-1 | new: route RefOnly through ValidateCheckout/merge instead of ref strategy | S1 | `owned-ref=S` |
| PC-2 | new: use HEAD as update-ref destination, with distinct main branch and both branch-name forms seeded | S1 | `main-intact` |
| PC-3 | insert deletion of fixture foreign task full ref after the successful CAS | S1 | `foreign-intact` |
| PC-4 | skip task.TaskRefWorkspaceBindingId equality against resolved binding at sync admission | S4 | `refused:binding-id:initial` |
| PC-5 | remove binding.TaskId comparison; all other tuple fields remain valid | S4 | `refused:binding-task:initial` |
| PC-6 | remove repository comparison only | S4 | `refused:repository:initial` |
| PC-7 | skip RequireRepositoryIdentity at sync entry with a different valid common directory | S4 | `refused:common:initial` |
| PC-8 | remove binding identity-path comparison only | S4 | `refused:identity:initial` |
| PC-9 | remove binding full-ref comparison only | S4 | `refused:full-ref:initial` |
| PC-10 | remove only SelectedBaseSha comparison; valid but different full commit | S4 | `refused:base-sha:initial` |
| PC-11 | remove only SelectedBaseRef comparison | S4 | `refused:base-ref:initial` |
| PC-12 | remove only SelectedBaseSource comparison | S4 | `refused:base-provenance:initial` |
| PC-13 | remove only SelectedBaseTaskId comparison | S4 | `refused:base-owner:initial` |
| PC-14 | new: accept RefCreated at sync entry with real branch/remote S available | S4 | `refused:preparation:initial` |
| PC-15 | new: omit expected binding revision check on reacquisition, other values unchanged | S4 | `refused:revision:under-lease` |
| PC-16 | new: accept unsupported non-null baseline version with otherwise valid tuple | S4 | `refused:baseline-version:initial` |
| PC-17 | new: omit only mode comparison in baseline admission | S4 | `refused:baseline-mode:initial` |
| PC-18 | new: omit only binding comparison in baseline admission | S4 | `refused:baseline-binding:initial` |
| PC-19 | new: reuse pre-lease resolved binding without reread, rotate binding pointer only at gap | S4 | `refused:binding-id:under-lease` |
| PC-20 | omit initial fingerprint equality while keeping under-lease check; stop before inner check via boundary refusal observation | S4 | `endpoint-refused-before-observe` |
| PC-21 | omit final fingerprint equality; change endpoint after successful initial observation | S4 | `refused:endpoint:under-lease` |
| PC-22 | omit pinned observation-ref SHA comparison after replacing its tip with another locally available commit | S4 | `refused:observation-pin:under-lease` |
| PC-23 | omit l0 versus under-lease L comparison; move owned ref before final validation, leave CAS old equal fresh L | S3 | `race-preserved=T` |
| PC-24 | new: remove expected-old argument from update-ref; move ref after validation immediately before command | S3 | `race-preserved=T` |
| PC-25 | new: return confirmed result despite failed or different post-read (two variants) | S3 | `postcondition-unconfirmed` |
| PC-26 | ignore failed task-owned settlement pin update and continue to CAS | S4 | `refused:settlement-pin:under-lease` |
| PC-27 | skip only pre-lease ancestry refusal, keep later guard; barrier records admission before later guard | S2 | `baseline-refused-before-lease` |
| PC-28 | skip under-lease ancestry refusal; seam returns valid initial ancestry and false under lease | S2 | `unconfirmed:baseline-lineage:under-lease` |
| PC-29 | bypass L ancestry refusal for actual local-ahead/diverged cases | S2 | `unconfirmed:local-lineage` |
| PC-30 | new: skip first full-ref registration rejection; inspect initial refusal at its boundary before inner validation | S7 | `registration-refused-before-observe` |
| PC-31 | new: skip under-lease full-ref registration rejection after attaching source in gap | S7 | `attached-ref-preserved:under-lease` |
| PC-32 | new: proceed on null/foreign/disposed lease at ref mutation boundary, independently arranged | S6 | `no-unleased-write` |
| PC-33 | ignore failed TryAdmitConsumerAsync and run sync despite occupied reservation | S6 | `reserved-ref-preserved` |
| PC-34 | return on budget cancellation without awaiting owned blocked child's termination/drains | S6 | `child-exited:timeout` |
| PC-35 | return or throw cancellation before joining caller-canceled owned child/drains | S6 | `child-exited:caller` |
| PC-36 | remove per-sweep slice bound so a busy lease occupies the full cumulative budget | S6 | `lease-window-bounded` |
| PC-37 | retain task fairness waiter on refusal/timeout/caller cancellation; all other state restored | S6 | `terminal-waiter-cleared` |
| PC-38 | ignore mirror Relation refusal while origin otherwise carries readable descendant S | S5 | `mirror-proof-required:relation` |
| PC-39 | ignore Pushed=false on descends response while a prior readable origin exists | S5 | `mirror-proof-required:push` |
| PC-40 | drop afterPublish.Sha equality to mirror.Tip | S5 | `mirror-proof-required:reobserve` |
| PC-41 | replace NamesCommit/report check with true for wrong full SHA and abbreviation variants | S5 | `report-exact-S` |
| PC-42 | new: manufacture a pushed response when required capability is absent | S5 | `mirror-proof-required:capability` |
| PC-43 | new: substitute fallback runner/mirror cwd for missing bound runner coordinates | S5 | `mirror-proof-required:runner` |
| PC-44 | enter mirror publisher even when reportedTips is supplied | S5 | `report-only-no-publish` |
| PC-45 | return Confirmed at B when exact remote ref is missing after failed/unavailable publisher | S5 | `mirror-proof-required:missing` |
| PC-46 | retain WorktreeMissing/RegistrationGone supersession for RefOnly in recovery ClaimAsync | D1 | `debt-ready=S:claim` |
| PC-47 | retain missing-identity supersession for RefOnly only in recovery SaveAsync | D1 | `debt-ready=S:save` |
| PC-48 | follow latest origin by passing null reportedTips instead of debt.SourceSha | D2 | `debt-not-ready:source` |
| PC-49 | remove attempt equality in EpisodeMatches; keep SyncSettled tuple coherent to expose the claim acceptance | D2 | `debt-not-ready:attempt:claim` |
| PC-50 | new: omit mode discriminator at claim with otherwise matching new baseline | D2 | `debt-not-ready:mode:claim` |
| PC-51 | new: omit binding identity at claim; coherent task/baseline point at new binding | D2 | `debt-not-ready:binding:claim` |
| PC-52 | remove post-Git task attempt drift check only | D2 | `debt-not-ready:attempt:save` |
| PC-53 | new: omit post-Git mode drift check only | D2 | `debt-not-ready:mode:save` |
| PC-54 | new: omit post-Git binding drift check only | D2 | `debt-not-ready:binding:save` |
| PC-55 | remove post-Git ProgressBaselineJson drift check only | D2 | `debt-not-ready:baseline:save` |
| PC-56 | remove revision predicate from save update; competing writer preserves otherwise same source/attempt | D2 | `debt-not-ready:revision:save` |
| PC-57 | remove result.FullRef comparison from ready predicate; inject mismatched valid result only after sync reached | D2 | `debt-not-ready:result-ref` |
| PC-58 | remove DesktopAfterSha equality from ready predicate with otherwise Confirmed result | D2 | `debt-not-ready:result-after` |
| PC-59 | remove result.RemoteSha equality only from ready predicate | D2 | `debt-not-ready:result-remote` |
| PC-60 | new: put existing sync/recovery behind RefOnlyDesktop.Enabled | D3 | `existing-mode-retained` |
| PC-61 | new: deserialize missing RefOnly discriminator as a valid legacy checkout baseline | D3 | `invalid-ref-only-baseline` |
| PC-62 | change Held recheck to return a runnable claim instead of just hourly restamp | D3 | `held-no-git` |
| PC-63 | new: interpret discriminator-absent v1 Checkout as RefOnly or reject v1 | D3 | `legacy-v1-preserved` |
| PC-64 | new: label creation-time baseline as current remote tip | R1 | `check-baseline-labelled` |
| PC-65 | new: populate Changed/Untracked=0 with available/clean status for absent desktop identity | R1 | `check-dirt-unknown` |
| PC-66 | new: fetch the observed ref during Check to fill missing objects | R1 | `check-read-only` |
| PC-67 | new: render creation timestamp/source for a newer remote observation | R1 | `check-sha-and-time` |
| PC-68 | new: replace missing-object result with empty complete log/counts | R1 | `check-objects-unknown` |
| PC-69 | use main HEAD instead of prepared.DesktopAfterSha as range end | R2 | `count=B..S` |
| PC-70 | retain WorktreePath/HEAD in TryCountWorktreeCommitsAsync for RefOnly | R2 | `repair-count=B..S` |
| PC-71 | drop SourceDescends requirement at pending read admission | R2 | `pending-not-confirmed:ancestry` |
| PC-72 | drop full-id admission for pending source; locally readable abbreviation supplied | R2 | `pending-not-confirmed:full-sha` |
| PC-73 | drop RevParseCommit success gate only; full advertised SHA is absent locally | R2 | `pending-not-confirmed:object` |
| PC-74 | new: accept changed/missing pending observation pin at RefOnly read boundary | R2 | `pending-not-confirmed:pin` |
| PC-75 | truncate Result to forwarded excerpt on canonical-store failure | R3 | `result-full` |
| PC-76 | restore ResolveSpillFileAsync desktop fallback for RefOnly | R3 | `no-spill-write` |
| PC-77 | restore bind-refusal/report-path disk scan for RefOnly despite canonical source admission | R3 | `no-decoy-read` |
| PC-78 | restore disk-first ReadContentAsync for RefOnly | B1 | `bundle-bytes=S` |
| PC-79 | new: derive bundle directory from identity WorktreePath instead of admitted artifact root | B1 | `artifact-root-confined` |
| PC-80 | decode/re-encode UTF16 source as UTF8 before manifest/copy | B1 | `bundle-bytes=S` |
| PC-81 | new: build from task branch despite unconfirmed/unadmitted settlement source | B1 | `bundle-source-admitted` |
| PC-82 | resolve current branch again for second file (size and bytes) after branch moves | B2 | `all-members-from=S` |
| PC-83 | new: allow branch fallback for pending source lacking admitted pin | B2 | `bundle-unavailable:pending` |
| PC-84 | call GetChangesSinceAsync without explicit end and union untracked files | B3 | `paths=expected-at-S` |
| PC-85 | use oldPath rather than rename destination in committed diff reader | B3 | `rename-destination-only` |
| PC-86 | include deleted source by reading it at B rather than omitting it at S | B3 | `deleted-absent` |
| PC-87 | force docsOnlyDiff=true for a mixed unnamed committed diff | B3 | `mixed-unnamed-no-bundle` |
| PC-88 | bypass path normalization/confinement for traversal and absolute path variants | B4 | `omitted:path` |
| PC-89 | remove excluded-source-prefix check only | B4 | `omitted:excluded` |
| PC-90 | skip tree-mode/type check and read symlink target text as a document | B4 | `omitted:link` |
| PC-91 | on cat-file miss, read same path from canonical disk | B4 | `omitted:missing` |
| PC-92 | skip length > remaining guard and open an oversize source stream | B4 | `omitted:preallocation-budget` |
| PC-93 | remove streaming remaining-byte check after admitted short length | B4 | `omitted:stream-budget` |
| PC-94 | omit remaining -= bytes.Length; each small member individually fits | B4 | `omitted:total-budget` |
| PC-95 | mark manifest Complete=true after an unavailable/omitted requested source | B4 | `sources-incomplete` |
| PC-96 | route RefOnly through old RefuseParkResume worktree prune/add path | C1 | `no-desktop-worktree` |
| PC-97 | substitute identity path as session execution cwd/RunnerCwd | C1 | `runner-cwd=bound` |
| PC-98 | new: omit mode comparison when accepting stored park proof | C2 | `park-not-ready:mode` |
| PC-99 | new: omit binding identity comparison when accepting stored proof | C2 | `park-not-ready:binding` |
| PC-100 | accept stored proof despite corrupted PublicationReceiptDigest | C2 | `park-not-ready:digest` |
| PC-101 | omit attempt comparison on recovery claim while other captured episode values match | C2 | `park-not-ready:attempt` |
| PC-102 | omit RunnerStoreId comparison on receipt acceptance; change store alone | C2 | `park-not-ready:store` |
| PC-103 | omit AcceptedStartedAt comparison on receipt acceptance; session id remains valid | C2 | `park-not-ready:generation` |
| PC-104 | pass null reported source when SyncParkedAsync calls shared sync | C2 | `park-not-ready:source` |
| PC-105 | remove claimed park Revision predicate at save after independent row update | C2 | `park-not-ready:revision:save` |
| PC-106 | new: omit task/park source-mode comparison at save after only mode changes at synced barrier | C2 | `park-not-ready:mode:save` |
| PC-107 | new: omit task/park binding comparison at save after only binding changes at synced barrier | C2 | `park-not-ready:binding:save` |
| PC-108 | new: gate existing debt recovery on either Enabled option; independent flag variants | C2 | `park-ready=S:gate-off` |
| PC-109 | clear ReleasedSeatAnswer after acceptance before durable enqueue intent | C3 | `answer-received-once:restart` |
| PC-110 | generate fresh queue key from retry instead of persisted answer event/target attempt | C3 | `answer-received-once:replay` |
| PC-111 | confirm answer delivery from a truncated-middle UserPrompt | C3 | `answer-unconfirmed:body` |
| PC-112 | accept complete QueuedUserPrompt without matching accepted UserPrompt | C3 | `answer-unconfirmed:queued-kind` |
| PC-113 | accept otherwise exact complete prompt from different session | C3 | `answer-unconfirmed:session` |
| PC-114 | accept otherwise exact complete prompt from prior generation | C3 | `answer-unconfirmed:generation` |
| PC-115 | accept exact complete pre-floor prompt | C3 | `answer-unconfirmed:floor` |
| PC-116 | on receipt-save failure, resend without transcript catch-up | C3 | `answer-received-once:post-submit` |
| PC-117 | new: allow RunnerId=null desktop redirect for recorded RefOnly | C4 | `redirect-refused:desktop` |
| PC-118 | skip pending/unknown old-slot release refusal in RebindDrainingAsync and retarget immediately | C4 | `redirect-refused:old-release` |
| PC-119 | omit store equality on redirect proof only | C4 | `redirect-refused:store` |
| PC-120 | omit slot-generation equality on redirect proof only | C4 | `redirect-refused:slot-generation` |
| PC-121 | restore WorktreePath selection as Caller.WorkingDirectory for RefOnly | U1 | `caller-remote-context` |
| PC-122 | default child to Shared using parent execution directory instead of its own Worktree | U1 | `child-own-worktree` |
| PC-123 | resolve moved parent branch again rather than use frozen source SHA | U1 | `child-own-worktree-from=S` |
| PC-124 | new: accept explicit Shared request rooted in RepoPath despite no execution custody | U1 | `shared-main-refused` |
| PC-125 | retain CreateMergeTaskAsync Shared identity cwd for RefOnly parent | U1 | `merge-helper-no-identity` |
| PC-126 | restore WorktreePath/RepoPath root for RefOnly decision CheckAsync | U2 | `grant-root=runner` |
| PC-127 | restore RefOnly identity->path mapping in AgentSessionService reservation/launch caller | U2 | `session-no-identity-launch` |
| PC-128 | new: fall back to identity cwd in AgentControlService when bound runner coordinates absent | U2 | `control-no-identity-launch` |
| PC-129 | use RepoPath as workspace identity key for all RefOnly tasks | U3 | `owners-distinct` |
| PC-130 | ignore full-ref overlap when unique identity paths differ | U3 | `same-ref-fenced` |
| PC-131 | compare only RepoPath in CanPrepareSeparateDocs instead of task workspace identities | U3 | `advisory-owners-distinct` |
| PC-132 | omit remote slot/generation overlap in terminal ownership ambiguity check | U3 | `same-generation-ambiguous` |
| PC-133 | drop mode from HomeTask projection only | U3 | `ref-only-display:home` |
| PC-134 | drop mode from ZombieCensus snapshot only | U3 | `ref-only-display:census` |
| PC-135 | retain directory-existence-only inherited follow-up message | U3 | `ref-only-display:inherited` |
| PC-136 | omit precheck token comparison and replace snapshot with freshly read task | A1 | `stale-snapshot-held:legacy-precheck` |
| PC-137 | omit action token comparison and pass newly read token to ReleaseSystemAsync | A1 | `stale-snapshot-held:legacy-action` |
| PC-138 | new: drop RefOnly entry refusal on cleanupOnly=false path | A2 | `merge-zero-git:merge` |
| PC-139 | new: drop RefOnly entry refusal on cleanupOnly=true path | A2 | `merge-zero-git:cleanup` |
| PC-140 | remove currentOwner DesktopWorkspaceCleanup refusal in GuardedWorktreeRemoval.RemoveAsync | A2 | `owner-row-refused=desktop_not_required` |
| PC-141 | suppress desktop_workspace_binding_contradictory owner reason while source tuple remains valid Checkout | A2 | `owner-row-refused=desktop_workspace_binding_contradictory` |
| PC-142 | remove preparation-state refusal from AgentTaskLandSourceResolver only | A3 | `preparation-incomplete:resolver` |
| PC-143 | remove preparation-state refusal from AgentTaskLandingProtocol only; seed operation past resolver | A3 | `preparation-incomplete:protocol` |
| PC-144 | skip persisted completion obligation when first enqueue fails | W1 | `caller-received-once:commit-enqueue` |
| PC-145 | accept recovered queue row with same key but altered digest/body | W1 | `caller-unconfirmed:key-body` |
| PC-146 | confirm notification from Sent state or a truncated-middle prompt without exact whole-body match | W1 | `caller-unconfirmed:body` |
| PC-147 | accept complete QueuedUserPrompt as caller receipt without accepted UserPrompt | W1 | `caller-unconfirmed:queued-kind` |
| PC-148 | accept complete matching body in wrong parent session | W1 | `caller-unconfirmed:session` |
| PC-149 | accept complete matching body from earlier generation in same session | W1 | `caller-unconfirmed:generation` |
| PC-150 | drop sequence/time-floor check for complete old prompt | W1 | `caller-unconfirmed:floor` |
| PC-151 | on receipt-save failure, resend before transcript catch-up instead of recovering the accepted prompt | W1 | `caller-received-once:receipt-save` |
| PC-152 | new random message key for same notification retry after queue insert/link cut | W1 | `caller-received-once:queue-link` |
| PC-153 | skip checkout merge and return Synchronized at S | R-1 | `checkout-head=S` |
| PC-154 | remove initial ValidateCheckout endpoint comparison for changed endpoint; test stops before fetch boundary | R-2 | `checkout-endpoint-refused` |
| PC-155 | pass no reportedTips for settled Checkout debt recovery | R-3 | `checkout-debt-stays=S` |
| PC-156 | remove AgentReportStore temp-root rejection only; R-5's existing negative variant already configures a TEMP-contained destination | R-5 | `temporary-report-root-refused` |
| PC-157 | restore exact RepositoryPath string predicate for binding-only owner collision with case alias | N1 | `one-native-ref-owner` |
| PC-158 | omit legacy Checkout-owner collision check while binding-owner check remains; only legacy owner seeded | N1 | `one-native-legacy-owner` |
| PC-159 | restore ObservePrimaryCheckoutAsync for RefOnly in TaskCompletionProgressService.EvaluateRemoteAsync after successful sync; changed ref must remain indeterminate | S1 | `evaluation-progress-at-S` |

### Out of scope

- S5b-3 new-task selection, ref creation, initial push, new-task boot/hook/canary
  activation and dispatch acceptance. S5b-2 resumes seeded existing RefOnly tasks;
  no arbitrary checkout, sparse checkout or disposable desktop workaround.
- Ref garbage collection or settlement branch deletion. Keep owned task ref and
  pins, and every foreign ref. S2 delayed deletion remains separately gated;
  CARD-1708 salvage-ref work owns reflog-only commits and is not duplicated here.
- S4a activation, legacy backlog deletion/migration, new release/parking timers,
  broad main-checkout edits, recovery reset/adoption of RefOnly owners, deployment.
- Untouched terminal/provider/Herdr transport custody. Mode-aware server callers
  are covered; no provider/PTY suite or real external messaging recipient is selected
  without such an implementation change. No full Unit/assembly or rc credit.
- Native Windows is a separate qualification reservation below, not inferred from
  Linux. Nothing runs on the desktop under this commission. No native green is
  required or claimed from this documents-only task.

### Checkpoints

This first table is the **closed portable ordinary scope** (28 new methods, the
one inherited cleanup method, three retained sync methods and 47 policy results:
**79 TUnit executions**). All after commits must include the prerequisite S5b-1.
Run A = CP-1..CP-3, Run B = CP-4..CP-5; **never more than three rows per run**.
One isolated build in each run, same SHA/properties, subsequent rows reuse that
run's build. A cross-run reference is not binary reuse. The extra cold build is
intentional per performance Debug P3, not an accidental duplicate build.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S5b-2 | tests/Antiphon.Tests -> bin-c0824-s5b2-a/ | ref2-sync-debt | /*/Antiphon.Tests.Application/(C0824RefOnlySyncTests*)\|(C0824RefOnlyDebtTests*)/* | V-1,V-2,R-6 | exactly S1..S7,D1..D3; 10 passed, 0 failed/skipped | 10 | 10 | true |
| CP-2 | S5b-2 | CP-1 | ref2-reads-receipt | /*/Antiphon.Tests.Application/(C0824RefOnlyReadTests*)\|(C0824RefOnlyDeliverableTests*)\|(C0824RefOnlyReceiptTests*)/* | V-3,V-4,V-8,R-4 | exactly R1..R3,B1..B4,W1; 8 passed, 0 failed/skipped | 8 | 4 | true |
| CP-3 | S5b-2 | CP-1 | ref2-continuation-consumer | /*/Antiphon.Tests.Application/(C0824RefOnlyContinuationTests*)\|(C0824RefOnlyConsumerTests*)/* | V-5,V-6,R-6 | exactly C1..C4,U1..U3; 7 passed, 0 failed/skipped | 7 | 4 | true |
| CP-4 | S5b-2 | tests/Antiphon.Tests -> bin-c0824-s5b2-b/ | ref2-advisories-legacy | /*/Antiphon.Tests.Application/(C0824RefOnlyBoundaryTests*)\|(C0824RefOnlyCleanupTests*)\|(RunnerSettlementSyncTests*)/(Legacy_release_keeps_the_classified_revision*)\|(Local_merge_and_checkout_typed_removal_refuse_ref_only_owner*)\|(Incomplete_preparation_refuses_initial_and_resumed_land*)\|(All_desktop_consumers_skip_ref_only_without_deleting_refs*)\|(Pushed_tip_fast_forwards_exact_owned_checkout*)\|(Endpoint_change_is_not_followed*)\|(C1082_SettledDebtSyncOnlyFastForwardsToTheRecordedSource*) | V-7,R-1,R-2,R-3,R-5 | exactly A1..A3 and four listed inherited methods; 7 passed, 0 failed/skipped | 7 | 11 | true |
| CP-5 | S5b-2 | CP-4 | ref2-classification | /*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)/* | R-7 | existing roster: 47 passed, 0 failed/skipped; rebind if upstream roster changes | 47 | 3 | true |

Import this artifact (only its first Checkpoints table), select runs explicitly
with `--rows CP-1,CP-2,CP-3` and `--rows CP-4,CP-5`. CP-4's class/method Cartesian
filter must yield exactly the seven named methods, no cross-class homonyms; audit
source suffixes and TRX roster before claiming the Min/Expect. If it widens, amend
the manifest selection, never silently execute excess scope. Gate every direct
build/test and checkpoint-tool bootstrap with scripts/build-slot.ps1; native direct
builds use default bin/obj, checkpoints own cold output exceptions. No build/test
is run by this TestDesign. Code/Review run check-evidence-diff over base..HEAD.

Manifest pins (import then edit ignored YAML; do not accept importer's 15min/3x
row defaults): `build.slotWaitMinutes: 12`, `timeouts.rowMinutes: 20` to retain
20min build ceiling; row caps CP-1=6, CP-2=6, CP-3=6, CP-4=8, CP-5=6 minutes.
Estimated host executions are respectively 3,4,4,4,3 minutes; bounds are provisional
CARD-0824 pending these new methods' first red/green timings. CP-5 cap is measured
comparator 2 x 181s rounded to 6min. Build estimate 7min follows Debug p50=431.6s;
20min cap retains its measured 817.1s max without pretending it is 2x p95.
Run A charged execution cap **38min** (20+6+6+6), hard wall **86min**
(38 + 4 acquisitions x12). Run B cap **34min** (20+8+6), hard wall **70min**
(34 + 3x12). Caps are failure bounds, not expected runtime. These totals are below
the selected-estimate derivation (Run A 46min, Run B 38min), so supply the explicit
CLI `--total-timeout-override-reason` shown below.
Do not run the whole table without `--rows` or these per-run manifests.

Use the tool's `import --plan <this artifact> --out <ignored-manifest.yaml>`, then
pin the YAML build/row settings above and `build.properties.UseAppHost: false`.
Run that **manifest as a positional argument**; a direct `run --plan` would
discard the YAML pins and is not this recipe. Use these exact selections:

```text
dotnet run --project tools/Antiphon.Checkpoints --no-build -p:UseAppHost=false -- run <pinned-manifest.yaml> --rows CP-1,CP-2,CP-3 --expected-source-sha <C> --total-timeout 38m --hard-wall-timeout 86m --total-timeout-override-reason "CARD-0824 Debug 5ae98d31 measured build and row caps; bounded Run A"
dotnet run --project tools/Antiphon.Checkpoints --no-build -p:UseAppHost=false -- run <pinned-manifest.yaml> --rows CP-4,CP-5 --expected-source-sha <C> --total-timeout 34m --hard-wall-timeout 70m --total-timeout-override-reason "CARD-0824 Debug 5ae98d31 measured build and row caps; bounded Run B"
```

The no-build tool invocation assumes a proven tool binary; otherwise account its
one native-output bootstrap build under build-slot.ps1. Every child test/build
driver still takes its own lease. Wait until terminal, observing
in at most 60s waits so progress remains visible. Exit 75 is still running; 4 is
not-run admission timeout; no NoSlot or retry outside the gate. No edits while
drivers run. Keep essential unedited CHECKPOINT lines, counters, exact TRX roster,
source/build clean receipts and per-acquisition admission positions/waits.

#### Native Windows reservation (separate from the portable importer table)

N1, `C0824RefOnlyBoundaryNativeTests.Windows_case_alias_cannot_own_the_same_ref_twice`:
nearest fixture C0824DesktopLegacyReleaseNativeTests + RefOnlyWorkspaceTestSetup.
On actual Windows, call RecordPlannedAsync with two full task GUIDs and the same
full ref/repository spelled with case-only differences and trailing-separator
variants (GUIDs share their eight-character branch suffix but differ in full).
Include an existing Checkout owner and a binding-only conflicting owner
separately. Keep canonical common identity valid; one binding succeeds, the second
refuses owner conflict before Git; fresh DB proves one owner. Resolve the accepted
binding through case aliases; keep two genuinely distinct POSIX runner paths
distinct. Oracle `one-native-ref-owner`; S5b-1 exact RepositoryPath query can admit
the binding-only alias, making this assertion-red. Classification Integration +
repository NativeWindows policy; skip on Linux is **not execution credit**.

| Native CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| NCP-1 | separately commissioned S5b-2 native | tests/Antiphon.Tests -> bin-c0824-ref2-native/ | ref2-windows-binding | /*/Antiphon.Tests.Application/C0824RefOnlyBoundaryNativeTests/Windows_case_alias_cannot_own_the_same_ref_twice | N1,A-7,PC-157,PC-158 | 1 passed, 0 failed/skipped on actual Windows | 1 | 9 |

N1/PC-157..PC-158 require a separately placed **non-desktop Windows runner**; do not use
this table to dispatch to the desktop or append S5b-3 lifecycle acceptance. If no
authorized native host is available, keep this explicit untested obligation and
the native verdict incomplete; do not call Linux evidence Windows-clean. Portable
Code/Review can assess the feature-off slice with this bounded limitation recorded.

### Cost

All costs are **estimated floors/reservations**, not measured S5b-2 runtimes.
Measured comparators are the S5b-1 Debug's 11 successful cold builds (p50 431.6s,
p95/max 817.1s), 39 acquisitions (mean 46.5s, p95 255s, max 405s), host startup
10–22s (loaded 51–67s), ordinary row maxima 42–85s, and L6 body green 39.6s
(n=1). Do not promote those small, different selections to S5b-2 p95. The Debug
report and its source/run identities are the retained provenance; fresh Windows
and these new method distributions are **unknown**.

**Ordinary Code V/R floor = table sum 10+4+4+11+3 = 32min**: 14min for two
isolated builds and 18min host/setup/body/teardown. Run A=18min, Run B=14min.
Two builds are budgeted explicitly; do not add them a second time. Independent
portable Review costs another 32min if budgeting the pipeline. Separately placed
NCP-1 adds **9min** (7 cold build + 2 host/test); it is excluded from the portable
sum and cannot be reported executed on Linux. TestDesign ran zero builds/tests.

Mutation's exact-method baseline greens cover 32 portable methods (28 new + four
retained) and N1. Reuse one native-default-output build per platform at unchanged
L: portable 3min build + 25 ordinary methods x1min + seven heavier methods x2min
= **42min**; Windows 7min build + N1 x2min = **9min**. Baseline total **51min**.
The seven heavier methods are S5,C1,C2,C3,C4,W1,R-5. These are method execution
reservations including host startup, not test-body timeouts or Min counts.

Each cycle has one mutated red build and one restored green build, provisionally
3min each, plus 0.5min patch/binding/restoration accounting. Incremental build
3min is an estimate informed by the Debug's 143s warm comparator; it is not a
measured S5b-2 or Windows p95. Use no alternate direct-build output. Native 3min
incremental costs require first-baseline measurement and rebudget if exceeded.

| Exact-method PC selection (aliases expand to the frozen roster) | PCs | Red test minutes each | Restored green test minutes each | Build + accounting + red + green |
|---|---:|---:|---:|---:|
| S1..S4,S6,S7,D1..D3,R1..R3,B1..B4,U1..U3,A1..A3,R-1..R-3 | 114 | 1 | 1 | 114 x (6+0.5+1+1) = 969min |
| S5,C1..C4,W1,R-5 | 43 | 2 | 2 | 43 x (6+0.5+2+2) = 451.5min |
| N1 (PC-157,PC-158), actual Windows only | 2 | 2 | 2 | 2 x (6+0.5+2+2) = 21min |

159 cycles = **1,441.5min** (954 build + 79.5 accounting + 408 red/green tests).
**Post-land Mutation floor = 51+1,441.5 = 1,492.5min (24h52m30s)**, including baseline,
binding/discovery and restoration, before reporting/admission. Portable mutation
portion is **1,462.5min**, native portion **30min**. No all-PC battery is run as one
selection: use one PC cycle per bounded run (exact method, four separate driver
leases), and baseline methods in bounded packs. No speculative batching saving
is credited; most controls share files/methods. Only different files AND methods
may batch, preserving per-PC assertion-red and restoration evidence.

Combined **portable Code V/R + portable Mutation = 32+1,462.5 = 1,494.5min**.
Including separate native Code and native Mutation gives
**32+9+1,492.5 = 1,533.5min (25h33m30s)**. Add ordinary portable/native Review 41min
for **1,574.5min (26h14m30s)** before authoring/fixes/admission. Builds in that complete
Code+Mutation reservation total 14+7+3+7+954 = **985min**. There is no desktop
spend hidden in the portable floor.

Admission is separate: ordinary portable 2 build + 5 row acquisitions = **7**,
12min finite bound each, **84min maximum**; cited mean comparator gives **5.425min**
additional wait, not a guarantee. Native Code has two acquisitions, ceiling 24min,
expected Windows wait unknown. Mutation has 35 baseline acquisitions (2 builds +
33 method invocations) and 636 cycle acquisitions (318 builds + 318 tests),
**671 total**: ceiling **8,052min**; applying the historical Linux mean uniformly
is a load scenario of **520.025min**, not a measured prediction for Windows.
Uniform p95-wait scenario = **2,851.75min**. Record actual admission independently
of execution, per driver, and slot timeout as not-run. No one lease around a loop.

Savings: two build-sharing packs instead of rebuilding every one of five rows
save **3 x7 = 21min** ordinary build time. One shared portable mutation baseline
build saves **31 x3 = 93min** versus a build per method. A single seven-acquisition
ordinary run could save one 7min cold build, but is rejected by the measured P3
packing obligation. No full-suite percentage saving is invented. Test count stays
minimal by using in-body variants: 28 new portable methods plus one native method;
the larger PC count exposes distinct bypasses rather than multiplying ordinary
test processes. Safety controls are not removed to conceal their cost.

Performance obligations: share TestDbFixture/container/template/migrations once
per host; clone DB per method, reuse immutable Git seed objects with private
writable refs/index; one shared phone-home/loopback host per test host, with
isolated session/store/mirror identities per method. Restart/crash cuts alone
justify a separately owned host or rebuilding scopes/connection; record and
charge that exception, never restart expensive infrastructure per fault variant.
Extend fixture attachment/reset seams instead of replicating setup. Isolate
schemas and process limits; no Antiphon.Tests/Pty/FakeClaude co-scheduling.
Provisional method caps CARD-0824: 60s ordinary integration, 80s for Q/P/restart
methods, native 120s; collect first required red/green samples, then calibrate near
2x p95, never raise caps to turn red green. A new multi-case method exceeding its
cap is a performance defect to split/repair, not justification for a retry.
Bound Git/HTTP/DB calls and output drains using existing admitted drivers; use
initial test-I/O budgets of 10s Git, 5s loopback/DB and 15s teardown, provisional
CARD-0824 until measured. Production sync/publication deadline behavior retains
its existing budgets; fake-clock tests drive them without wall sleeps. Record
call durations plus test setup/readiness/migration/clone, host, body and teardown.
Keep `ANTIPHON_TEST_PHASE_GROUP_ID` equal to each actual CP id, all TRX and receipts
under ignored checkpoint directories; unknown subphases stay unknown, with explicit
remainder. Explicit persistent test report roots stay outside source/TEMP; output
cleanup follows fixture ownership, never arbitrary recursive worktree cleanup.

The existing performance Debug supplies this packing decision. Expected normal
runs are under 30min; an admission burst can still take a run past 30min. Preserve
run/source/load and acquisition evidence and commission the required R1 Debug
follow-up if expected or observed >30min; no admission-only exemption. Authoring
and 24h Mutation are many bounded runs, never a blanket 30-hour command. Only
first required red/green executions collect new timing; no redundant green battery.

### Binding and import handoff

Before handoff: **guards=159, mapped=159, missing=0, duplicate PC maps=0**.
**All 159 PCs and every named variant must run after land**; none ran during
TestDesign. PC-157/158 are explicitly native/untested here. There is no executed
V/R, mutation-clean or native-clean claim. Code runs both portable CP packs;
Review judges their exact-source receipts and this pending control inventory.

Code first commits compiling additive representation/API scaffolding preserving
old consumer behavior, then assertion-red behavioral witnesses before the fix.
New fields/types alone cannot be red evidence. Required red-first anchors: S1
expects owned ref S but missing-checkout strategy refuses; D1 expects Ready but
absence supersedes; B1 expects committed bytes but disk decoy wins; C1 expects no
desktop add but legacy resume materializes it; U1 expects runner context but
authentication chooses identity; A1 expects stale-snapshot refusal but old reread
accepts new token; A3 reaches incomplete state with a real source already present.
A2's direct owner-row leg and unchanged ancestry/receipt/Checkout guards may be
base-green; their post-land controls must be assertion-red. N1 is actual Windows
only. Preserve source SHA, boundary hit, expected assertion and nonzero count at
each red, then implement and commit before long CP runs.

For every initial/under-lease or claim/save control, hold all other checks coherent
and reach the named site independently. In particular, initial endpoint/source
registration/ancestry cases assert **no remote observation/pin or lease crossing**
after their initial refusal; unchanged final bytes alone would let an early-guard
mutant survive behind a later check. Under-lease variants pass initial checks and
change state only in the barrier. Debt claim variants keep the downstream sync
tuple coherent and assert no claim/Git crossing; save variants change only after
sync. A2's Checkout-typed remover request bypasses merge refusal deliberately and
must use a removable published fixture; verification-only source custody cannot
mask the owner guard. A3's resumed operation enters protocol independently of the
resolver. Add narrow internal boundary callbacks only where existing seams cannot
show the crossing; no self-comparison or wrapper-only oracle.

The 24 plan methods are all retained. Additional A1..A3/W1 exist only to close
the concrete Review and recipient seams. Bind final source sites for every PC,
including helpers and new DTO wire/JSON round-trip mappings; preserve old v1 and
DesktopBefore/After semantics with explicit mode provenance. S1 also drives
TaskCompletionProgressService.EvaluateAsync: RefOnly primary re-observation must
validate its bound ref at S, never ObservePrimaryCheckoutAsync on the identity.
An unchanged registered-checkout observer would strand otherwise successful sync.

If final code exposes a genuinely unverifiable caller seam, return `next: plan`
with its exact boundary instead of replacing recipient evidence with a mock ack.
A real human decision goes to `next: decide`; ref-only mode, off defaults and this
documents-only commission require no further permission. Bind native admission
through the separate runner commission, without violating the no-desktop rule.
