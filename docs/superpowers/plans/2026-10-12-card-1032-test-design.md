# CARD-1032 test design: inert runner and mirror cleanup

Freeze date: 2026-10-12. TestDesign task: `23285d2c-f62c-4320-8a1f-c69ba83254ef`.
Source/base: `0fcb74d618c5d3ea89c778910e0dc3ad6da5aea0`.
Card read through the task-bound `scripts/card.ps1 get CARD-1032 -Json`;
the task token was present and was not printed.

This document supplements [the landed fix plan](2026-10-11-card-1032-runner-mirror-cleanup-plan.md).
Its D-1..D-11 and eight S1..S8 implementation slices remain the fix design.
The table here freezes the verification proposal for this card. The [parent
CARD-1017 freeze](2026-10-03-card-1017-completed-card-worktree-cleanup-plan.md#verification-design)
retains its S4/S5 activation, scheduled delivery and native acceptance obligations.
This card's eight slices are inside **parent S3**.

Design only: no production/test changes, builds, test executions or deliberate
mutations were performed by this task. Counts are source/specification counts.
Code runs ordinary V/R; Review judges those results before land. Post-land
Mutation is paused by this brief: the controls below remain pending, including
their concrete patch binding. A docs freeze is not PC execution evidence.

## Verification design

### Inspection

Read setup, assertions and teardown at the following boundaries before naming cases.
The table distinguishes selected bodies from dependency excerpts.

| Bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| Landed CARD-1032 plan; parent CARD-1017 decisions, execution contract, frozen U/M roster, controls 52-82 and 108/109, activation and native rows | S3-only authority; parent V-5/V-6 and R-3 -> V-1..V-6, R-1 |
| `RunnerWorkspaceServiceTests`: 25 Mirror/Publish/Remove bodies, `Scratch.Create/Service/Git/Dispose`, repository helper and existing verification bodies | Real scratch Git; legacy Force semantics; verification authority remains distinct -> V-1/V-3/V-5, R-1 |
| `PhoneHomeCommandDispatcherTests`: three Workspace bodies, Dispatcher and RecordingRuntime; `RunnerPlatformContractTests` | Wire naming, old payloads, default capabilities; fake runtime is not process evidence -> V-2, R-1 |
| `RemoteWorktreeMirrorTests`: eight bodies and ScriptedGit/FakeProgressGit/FakeLeases/UnusedDirectory | Desktop push/FF and no-reset assertions; scripted source facts are regression-only -> R-1 |
| `RemoteWorkspacePreparerTests`: mirror success, timeout, restart, cancel and C672 receipt bodies; Rig setup/restart/dispose, push gate, BriefEnqueueFault | Late result capture, attempt/runner drift; canned MirrorResult cannot prove creation or removal -> V-1/V-6 |
| `CompletedCardCleanupFixture`, `CompletedCardRemovalFixture`; `LandingSafetyHarness` setup/restart and worker-cut bodies; `LandingGitFixture` construction/initialization | Cloned PostgreSQL, actual Result store, real Git, ownership and independent readback -> V-1/V-3/V-5/V-6 |
| `CardDoneWorktreeCleanupTests`: duplicate discovery, transaction rollback, generation/consumer cases; `ChannelOutboundMigrationTests.Upgrade_preserves_history_and_defaults`; `TestDbFixture` | Nearest schema/migration fixture: real predecessor migration, unique indexes, fresh contexts -> V-1/V-2 |
| `CompletedCardWorktreeRemovalTests`: identity, dirty source, rebase mapping, protected ignored content and final inspection bodies; `WorktreeIgnoredContentClassifier`, `WorktreeIgnoredContentGate`, `WorktreeNoFollowDelete` | Runner-local parity witnesses; no runner-to-server production reference -> V-3/V-5 |
| `RunnerAbsenceEvidenceRuntimeTests` creation/race bodies and RuntimeWorld; `HerdrAdoptionSweepTests` R1/R3/R5 and helpers; `PtyHostAdoptionTests.Running_session_survives_runner_restart_with_buffer_and_input_intact`; TestOwnedPtyHost capture/dispose and HerdrTestProcess | Real entry points and retained owner conflict, with owned children; existing simulated census alone insufficient -> V-4 |
| `SessionRunnerRuntime.StartAsync/AttachHerdrAsync/AdoptOrphanedHostsAsync/AdoptHerdrSessionsAsync/RetryPendingHerdrAsync/SweepVanishedSessionsAsync` | Workspace gate before session/custody/pane locks; pty and Herdr startup plus pending retry are separate paths -> V-4 |
| `PhoneHomeTestHost` startup/register/connect/dispose and scripted peer pump; `PhoneHomeLiveConnection.RequestAsync`; `PhoneHomeConnectionService.ReceiveLoopAsync/DispatchAndReplyAsync`; `PhoneHomeConnectionWriter`; connection service tests | Real WebSocket and mutation wait queue; no server outbound Channel exists at this SHA -> V-2/V-6 |
| `CardWorktreeCleanupService.DiscoverAsync/TryCleanupLocalAsync`; local executor admission/publication/consumer/outcome methods; endpoint entity; contracts enum; Program registration reads | Independent mirror debt and immutable endpoint extension. Local publication's cleanup-Complete exclusion is unsuitable for mirror proof -> V-1/V-2/V-6 |
| `WorktreeResidueRegistrationTests`, `AntiphonWebAppFactory` and classification metadata | Real Program fixture with refusing runner, populated default-inert check; empty inventory is insufficient -> V-2/V-6 |
| Owners: project-context; testing-and-build manifest/tool/slots/coverage; orchestration stages/cleanup; session-runtime invariants; Herdr attach/restart; ops-http/card front door | Closed scope and provenance; no stack operations, board writes, provider qualification or live deletion |

#### Recipes, fixtures and reachability

Aliases expand to literal classes and files. A method named below is one `[Test]`
result; named vectors execute inside that method, with no Arguments/DataSource/
Repeat expansion. Existing selected methods currently also produce one result each.

| Recipe / class alias | File and required fixture | Classification / reachable boundary |
|---|---|---|
| DB / B = `CardWorktreeMirrorBindingTests` | New `tests/Antiphon.Tests/Application/CardWorktreeMirrorBindingTests.cs`; cloned TestDbFixture DB, predecessor migration recipe above | Integration; actual EF mapping/index/round-trip. Two concurrent contexts, then a third observer. Distinct keys isolate each unique-index violation. |
| WIRE / W = `CompletedCardWorkspaceContractTests` | New `tests/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceContractTests.cs`; production PhoneHomeFraming JSON and new typed contract validator/digest | Unit; fixed literal JSON/expected values, production serializer/parser. No fake validator. |
| RB / K = `RunnerWorkspaceBindingTests` | New `tests/Antiphon.SessionRunner.Tests/RunnerWorkspaceBindingTests.cs`; new private fixture modeled on RunnerWorkspaceServiceTests.Scratch | Integration + ProcessSpawnLimit; real Git creation and external store, restart by rebuilding service/store. |
| CAP / P = `RemoteWorkspacePreparerTests` | Existing file under `tests/Antiphon.Tests/Application/`; extend private Rig only as needed | Integration; real preparer and isolated DB with controlled replies. CAP proves capture, while S8 MIR proves actual runner creation/receipt. |
| RUN / U = `CompletedCardWorkspaceRemovalTests` | New `tests/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceRemovalTests.cs` and `CompletedCardRunnerFixture.cs` | Integration + Slow + ProcessSpawnLimit, SessionLiveness exclusion for runtime arms; real Git/runtime/journal/remover when available. Add justified class entry to this project's slow registry. |
| MIR / M = `CardWorktreeMirrorCleanupTests` | New `tests/Antiphon.Tests/Application/CardWorktreeMirrorCleanupTests.cs` and `TestHelpers/CompletedCardMirrorFixture.cs`; extend CompletedCardCleanupFixture | Integration + Slow + ProcessSpawnLimit; real DB, coordinator, phone-home connection service, dispatcher and runner executor. Add justified class entry to this project's slow registry. |
| LEG-R / L,D | Existing `RunnerWorkspaceServiceTests`, `PhoneHomeCommandDispatcherTests` | Existing Integration/Unit metadata retained; scratch Git versus recording runtime are distinct recipes. |
| LEG-S / S | Existing `RemoteWorktreeMirrorTests` | Unit; preserve existing scripted-Git assertions, not evidence of physical cleanup. |

The new RUN fixture owns separate origin, repository, worktree, runner-state,
outside-sentinel and report roots. Creation is through RunnerWorkspaceService,
then exact typed binding is read from its external ledger. Use full GUIDs that
share their first eight hex digits for collision cases. Isolate Git global/system
configuration and use a fixture remote, as Scratch.Service does. External report
roots use the existing checkout-local durability recipe, not a temp-root bypass.
MIR reuses this runner in-process or in a locally inherited, fixture-owned crash
child; Antiphon.Tests already references the runner assembly and both assemblies
have the required InternalsVisibleTo. No new external executor is needed.

For S3, RUN drives the concrete use-gate/journal admission operation, holding
its admitted callback at a deterministic barrier. That proves gate order only.
For S4, RUN calls real read-only inspection/classification. Parent destructive
U witnesses are scheduled at S5, when the real remover exists. They are not
stubbed with a success DTO to get early slices green. S6 adds dispatcher
composition; S8 crosses the complete producer/recipient boundary.

Positive baseline: current Done generation, terminal settled owner, no active
consumer or recovery debt, bound ordinary creation on the original store,
clean exact branch/HEAD, fresh preserved H proof, retrievable external complete
Result/deliverables, admissible ignored inventory, known-empty process census.
Each refusal vector starts from a fresh copy of that baseline and changes one
named fact. Proof snapshots, request digest and unrelated receipt fields remain
internally consistent so an earlier guard does not hide the intended guard.

**Evidence order:** assert the actual entry point and production guard were
reached; observe the changed fact independently; assert the refusal/component
outcome and the next boundary not reached; finally compare pre-recorded file
bytes, registration and refs. Initial guards have a downstream inspection/I/O
counter in addition to preserved bytes, so a later guard cannot mask a bypass.
Counters observe calls through real seams and do not implement admission.
For a field comparator that would otherwise be masked, assert the specific
refusal and absence of the next external read; Mutation must bind that first
decisive assertion. Do not generate expected outcomes with the tested predicate.

Durable intent and receipt tests use a new store reader/DbContext at each cut.
Fresh runtime/store objects prove reconstruction; cuts at rename/unregister/
physical deletion/reply also use a process-death arm with parent-owned child
PID/start identity, drained output and joined exit. FakeHerdrServer supplies the
protocol; a real fixture child supplies liveness. Retained live owners remain
visible when an intent conflicts. In parent PC-66,
`adoptionsAtRetiringPath` means **new writable admission**, not hiding a
surviving process or refusing to display its existing ownership.
Parent PC-58 retains the branch-survival assertion and adds the decisive
`identityRefused` assertion: mirror cleanup intentionally leaves branch refs.

Use barrier acknowledgements, Stopwatch or TimeProvider.GetTimestamp/
GetElapsedTime for deadlines, and FakeTimeProvider for elapsed-budget/cooldown
vectors. A backwards wall-clock move must not extend an in-process deadline.
Persisted NextAttemptAt remains a UTC scheduling fact; restarting a process
does not compare timestamps from the prior monotonic clock. Old fixture polling
loops are not a model for new waits. No sleep chooses a race winner.

The fixture, rather than cleanup, releases its busy holder. Teardown records and
reaps its owned children even on assertion failure; unknown ownership or an
unjoined child fails fixture cleanup. Retain assembly-local process limiters and
run rows serially. Program fixtures use ProductionRunnerGuard plus the factory's
refusing client. Native sharing/junction assertions belong to the parent Windows
qualification, not simulated portable success.

#### Frozen new method roster

Each row below supplies the recipe; its named case vectors, compiling defect
and decisive oracle are expanded in the PC table. Classification is the recipe's.
No new method lacks a detecting control.

| Slice / CP | Exact methods (aliases above) | Cases / positive outcome |
|---|---|---|
| S1 / CP-1 | B.`C1032_SchemaUniqueBindings`; B.`C1032_SchemaLegacyUpgrade`; B.`C1032_SchemaRequestAndReceiptPersistence` | Independent preparation and tuple uniqueness; old rows remain unbound; exact immutable request and receipt survive new contexts. |
| S1 / CP-2 | W.`C1032_WireRoundTrip`; W.`C1032_WireRequiredFieldsAndVersion`; W.`C1032_WireDigestFieldCoverage` | Legacy JSON compatibility, distinct inspect/remove identities, valid body accepted, one-field invalid bodies refused and each digest field changes digest. |
| S2 / CP-3 | K.`C1032_BindingFullIdentityCollision`; K.`C1032_BindingCreationRecovery`; K.`C1032_BindingExactReplay`; K.`C1032_BindingStateRootIsolation`; K.`C1032_BindingLegacyCompatibility` | Real creation completes once; interrupted owned creation recovers; legacy tree not enrolled; same request returns same binding; old create remains usable. |
| S2 / CP-4 | B.`C1032_CaptureDistinctBindings`; P.`C1032_CaptureLateReassignment`; P.`C1032_CaptureAttemptAndCancellation`; P.`C1032_CaptureLostReplyRecovery` | Old binding retained after reassignment/cancel; current path needs current runner and attempt; replay retains preparation identity and validates returned facts. |
| S2 / CP-17 | K.`C1032_ForeignOwnerAdoptionRefused` | A saves intent and is cancelled; a colliding short id creates the tree; A's retry is `foreign_mirror_owner` and does not complete. |
| S2 / CP-18 | K.`C1032_AbsentMirrorReplayRefused` | Exact replay after ordinary removal is `stale_mirror_binding`. The same preparation keeps its binding id and does not mint a generation. |
| S2 / CP-19 | K.`C1032_StoreMismatchRefused` | A request addressed to another store or runner is `runner_store_mismatch` before a directory, ledger entry, or completed binding. |
| S2 / CP-20 | P.`C1032_StoreReplacementKeepsDebt` | After the live store is replaced, the retry uses a new preparation for the new store and the old row stays incomplete. |
| S3 / CP-5 | U.`C1032_FenceStartOrders`; U.`C1032_FenceAttachOrders`; U.`C1032_FenceStartupAdoptionOrders`; U.`C1032_FenceHerdrReadoptionOrders`; U.`C1032_FenceCreationOrders`; U.`C1032_FenceUnknownJournal` | Both winner orders through real entry points; startup and pending Herdr arms; cleanup unknown until census; child survives conflict; restored fence remains held. |
| S4 / CP-6 | U.`C1032_InspectionReadOnlyBound`; U.`C1032_InspectionPolicyParity`; U.`C1032_InspectionSourceProof` | Read-only facts for bound tree, no enrollment; hand-written Protected/Evidence/Disposable witnesses; actual H read independently of request baseline. |
| S5 / CP-10 | U.`C1032_RemovalEligiblePhysical`; U.`C1032_RemovalComponentRecovery`; U.`C1032_RemovalOriginalReplacement` | Real directory and matching registration removed; refs/sentinels retained; exact set-aside partial work recovers; replacement and unproved absence held. |
| S6 / CP-11 | U.`C1032_TransportDefaultInert`; U.`C1032_TransportRequiredPayload`; U.`C1032_TransportDispatchReplay` | Unconfigured dispatcher refuses; explicitly configured dispatcher reaches real removal; typed complete replay receipt persists before reply. |
| S7 / CP-13 | M.`C1032_AdmissionCurrentEligibility`; M.`C1032_AdmissionArtifactSurvival`; M.`C1032_AdmissionIntentOrdering`; M.`C1032_AdmissionBoundRebaseProof` | Explicit bound-rebase positive plus fresh generation/attempt/use/source/artifacts at intent; intent and reopen winner orders; fresh observer proves commit before send and no row lock across I/O. |
| S7 / CP-14 | M.`C1032_ReceiptCompleteBoundReceipt` | Genuine complete receipt positive, one-field mismatch/missing-field vectors, digest mismatch and import failure; fresh observer does not see false Complete. |
| S8 / CP-15 | M.`C1032_RecoveryQueuesBusyAndReady`; M.`C1032_RecoveryCrashHandoffs`; M.`C1032_RecoveryInertServerComposition` | Real preparation/inspection/removal transport in busy/ready states and each cut; fresh receipt plus recipient files; default server/sweep/route cannot execute removal even with fixture Execute=true. |

The 21 parent U and 12 parent M exact names are retained in the PC-52..82,
PC-108/109 rows and in the CP rosters below. They add **33** methods to these
**41** new prerequisite methods, making **74 new single-result methods**.
The existing legacy rosters add **36**, for **110 ordinary results**.

Request field names in controls are logical contract members, including nested
binding/proof/policy members. S1 binds their concrete typed/JSON paths without
dropping a member. Conditional rebase source is absent only for direct containment;
its two valid states are pinned in digest vectors. Artifact pointers and effective
policy patterns have deterministic ordering; missing/unknown proof kind or
disposition is not permissive. BindingId is runner-issued: initial capture
checks nonempty identity and returned preparation binding; a later replay checks
equality to the recorded identity. Remote paths are verified by runner facts,
not by desktop File.Exists.

### Delivery inventory

Join preparation by
`(TaskId, Attempt, PreparationId, RunnerId, RunnerStoreId, BindingId)`.
Join removal by that binding plus
`(CardId, DoneRevisionId, TargetId, EndpointId, OperationId, RequestDigest)`.
Transport RequestId/epoch can change on reconnect; the durable command identity
cannot. The receipt includes or resolves the complete immutable binding and proof
identity, with independent directory/registration facts and external journal identity.

The real queue in this checkout is the runner dispatcher's `MutationLock`
wait queue, reached through `PhoneHomeLiveConnection.RequestAsync`, the socket
and `PhoneHomeConnectionService.ReceiveLoopAsync/DispatchAndReplyAsync`.
Server send and runner reply writers have their own semaphores. The server's
Channel is for incoming events, not outbound commands. Use these actual owners;
do not add an invented channel or certify delivery with a direct dispatcher call.

| Path / producer -> destination | Persistence boundary / recovery | Recipient evidence / named controls |
|---|---|---|
| Preparation: RemoteWorkspacePreparer -> RemoteWorkspaceService -> phone-home request -> runner MirrorAsync -> binding store | Preparation identity durable before request; runner creation intent before Git; completed binding before reply; server binding append before current-path projection. Cuts P0-P5 below replay the same preparation after restart. | Actual runner tree HEAD/ref/admin and external binding readback, then server historical binding read from a new context. K creation/replay plus P capture methods; M.C1032_RecoveryQueuesBusyAndReady carries this to real recipient. PC-210..216, PC-218..222, PC-286 and PC-520..529. |
| Inspection: mirror coordinator -> typed inspect -> real dispatcher/runner inspector | No destructive intent yet; inspection is fresh, bound and read-only. Lost inspection reply permits another read, not inferred absence or enrollment. | Actual runner HEAD H and identity match source observations; tree, refs and store digest unchanged. U.C1032_InspectionReadOnlyBound/SourceProof plus M recovery methods. PC-233/234/239 and PC-286. |
| Removal: coordinator -> persisted endpoint request -> real phone-home mutation queue -> guarded runner remover | Server intent committed before send; runner intent/set-aside identity before rename; component journal and typed receipt before reply; server imports exact receipt before Complete. Cuts D0-D11 below retain one operation/digest and reconcile owned components. | Original/owned aside absent, matching registration absent, refs/outside/replacement/report bytes retained; complete matching external receipt and fresh server endpoint agree. M.C1032_RecoveryQueuesBusyAndReady and CrashHandoffs; parent LostAckReusesOperation; PC-67/68/74..82, PC-261..265, PC-279/282/283/285..289, PC-509..512/517. |
| Outcome readback: coordinator -> committed endpoint/component facts -> existing invalidation observer | Receipt and outcome commit before event. Crash before event is recovered by fresh DB read; missing event does not erase debt. | Fresh context returns the matching endpoint and stored receipt, including separate mirror Pending after local Complete. PC-71/81/204/284. Event receipt itself is not physical cleanup proof. |

M.`C1032_RecoveryQueuesBusyAndReady` uses six named vectors:
preparation-busy, preparation-ready, inspection-busy, inspection-ready,
removal-busy and removal-ready. Busy means an earlier actual mutation holds
the recipient's gate; a fixture barrier proves it entered. Release is explicit,
owned by the fixture. Ready means no blocker when the request crosses the socket.
Both end at the recipient evidence above. A queued request, Sent flag, ACK,
success event or a trace containing the operation name is not sufficient.

M.`C1032_RecoveryCrashHandoffs` freezes these cuts, each with a reached-cut
acknowledgement and a hand-written expected terminal outcome:

| Cut | Fault/variant | Expected recovery and oracle |
|---|---|---|
| P0 | before server preparation commit, then rollback | No admitted preparation is sent; rearm creates one valid identity and eventually a real bound tree. |
| P1 | after preparation commit before request/send admission; request-limit or write/enqueue exception | Durable preparation remains; explicit rearm through real queue creates one tree/binding. |
| P2 | runner creation intent persisted before Git; crash before/after worktree add | Same preparation recovers only its own interrupted creation. Legacy pre-existing equal-SHA tree remains unbound. |
| P3 | runner binding complete, reply lost | Same preparation returns the recorded BindingId and real HEAD. |
| P4 | reply received, server binding save fails | Fresh preparer replays original preparation; historical binding is saved once. |
| P5 | binding saved before scalar projection; cancel/reassignment/attempt change | Old binding remains in fresh readback; current path changes only for matching runner/attempt. |
| I0 | inspection request/write fails, response lost or runner restarts | Reinspect exact known binding; no removal while proof unavailable. Restore transport, then eligible cleanup reaches recipient. |
| D0 | before server intent commit; reopen commits first | No removal command; tree bytes preserved and endpoint revoked/refused. |
| D1 | server intent committed before enqueue/send; enqueue/request-limit/write failure | Request bytes and operation remain; same command eventually removes owned mirror after retry becomes due. |
| D2 | request on socket/mutation wait queue, runner disconnects before handler | No false completion; reconnect replays same command to real recipient. |
| D3 | runner intent persisted before rename; restart | Restored fence/census is required; same set-aside identity resumes. |
| D4 | rename complete before journal component save | New process reads owned journal and actual Git/files; original-path replacement survives. |
| D5 | unregister complete before component save | Retry does not prune other registrations; completion still requires owned directory work. |
| D6 | partway through ignored-file deletion | Remaining owned aside entries stay fenced; resume verifies fresh facts and completes, allowance unchanged. |
| D7 | filesystem complete before runner receipt save | Journal recovery derives component facts; absence alone does not authorize a new command. |
| D8 | runner receipt saved before reply send; reply send fails | Reconnect returns byte-identical durable receipt; physical work is not repeated against a replacement. |
| D9 | reply received before server receipt/outcome commit; injected SaveChanges failure or process death | New context/coordinator replays original bytes/operation; imports same receipt before Complete. |
| D10 | outcome saved before invalidation | Fresh observer reads outcome/receipt with no event required. |
| D11 | same runner name reconnects with replacement store; changed task path/policy; wall-clock jump | Old store debt is held; replay payload remains frozen; cooldown and elapsed budget are enforced. |

For P1/D1/D2/D8, inject both a before-send failure and loss after admission.
They exercise actual send/waiter/mutation queues; there is no separate durable
enqueue API in this source. MIR does not simulate an ACK as a completed receipt.
P0/D0 rollback and the deliberately unknown/mismatched variants end in visible
held debt with preserved bytes. Recoverable cuts clear only the injected fault
and run to real recipient evidence, not merely Pending on a row.

Substitutes: CAP scripted replies prove DB capture/CAS only; WIRE round trips
prove schema/digest only; a direct RUN call proves runner behavior only;
FakeHerdrServer proves protocol branch selection, while real child observations
prove survival; new service objects prove storage reconstruction but not OS crash
custody; loopback transport proves installed code wiring but not deployed fleet
connectivity. Native Windows sharing/reparse behavior still needs parent S5.
No new session input, caller notification or Telegram path is added. If Code adds
one, stop for a revised verification design: real SessionMessageQueue busy/ready
and crash recovery must end in the matching **complete UserPrompt transcript**,
not a body fragment, enqueue, input call or Sent flag.

### Proves it works now

These are Code acceptance requirements, not a statement that this docs task
executed them.

- V-1: creation identity and historical capture survive restart/reassignment |
  DB/RB/CAP and MIR | CP-1/3/4/13/15 | exact full task/attempt/store binding is
  retained; unbound history and collisions grant no cleanup authority.
- V-2: immutable typed command, digest and actual composition |
  WIRE/RUN/MIR | CP-1/2/11/14/15 | configured recipient returns matching durable
  receipt; default composition and unsupported peers withhold removal.
- V-3: source, artifacts and ignored-content preservation |
  RUN/MIR | CP-6/8/9/13 | named first/final one-flip refusals preserve bytes;
  contained exact H remains eligible in S5; the bound-rebase positive is owned by S7 / CP-13 / C1032_AdmissionBoundRebaseProof per the 2026-10-13 caller decision.
- V-4: workspace use and retiring intent cannot both obtain new admission |
  real runtime/gate/journal | CP-5/7 | both race orders, attach, startup pty/Herdr
  adoption and pending Herdr retry; live/pooled/unknown ownership holds cleanup.
- V-5: actual eligible removal and component recovery |
  scratch Git/files/journal | CP-7/8/9/10/11/15 | matching directory and
  registration gone, original/outside/ref/artifact identities preserved.
- V-6: independent endpoints and end-to-end durable receipts |
  real PostgreSQL + phone-home mutation queue + runner | CP-13/14/15 |
  local Complete does not hide mirrors; busy/ready and each named cut converge
  to recipient facts or the explicitly specified safety hold.


#### D-12 — R-2 disclosed legacy-binding decision (2026-10-12)

The orchestrator's operator decision retains the S2 path-only contract. The
phone-home caller is the authenticated server; omission or nulling of the whole
binding object deliberately selects legacy creation. This was already reachable
and is not a new regression. A non-null binding still requires recipient-store
admission. No trust-context plumbing or production behavior change is authorized.
No later S1–S8 slice in this plan removes this compatibility path; any retirement
needs separately commissioned work, not an implied future slice.

CP-23 pins an omitted binding through the actual dispatcher and real scratch Git:
the registration file is missing, the exact requested worktree/HEAD exists, the
reply has no binding, no preparation or completion row exists anywhere in the
runner binding-store directory, and the physical git-dir has no owner stamp.
Registration admission is bypassed and no registration file is created or required.
The existing RecipientStoreId helper can best-effort read a present file, then
fall back to the custody GUID; that read grants no binding or ownership claim.
This is not a claim that the legacy helper performs no optional filesystem read.
Case -> mutation -> red: fabricate a binding row from the unbound path and
`Directory.Exists(ledgerRoot).ShouldBeFalse()` must fail. One result, portable.
CP-24 preserves the brief's three existing Workspace dispatcher methods.
### Guards the regression

- R-1: parent's R-3 S3 selection retained | CP-12's 25
  RunnerWorkspaceServiceTests and three PhoneHomeCommandDispatcherTests;
  CP-16's eight RemoteWorktreeMirrorTests | own-branch fast-forward publishing,
  protected dirty/diverged source, typed failures, legacy Force semantics and
  canonical desktop sync assertions. Exact names, case -> mutation -> red are
  PC-400..435 below. These are existing tests, not expanded counts copied from
  the parent without inspection.
- R-2: inert composition and ordinary compatibility | CP-3/11/15/23/24 |
  C1032_BindingLegacyCompatibility preserves ordinary old-peer creation;
  C1032_TransportDefaultInert and C1032_RecoveryInertServerComposition observe
  the unconfigured production graph and populated sweep. No Program-removal
  service registration, public cleanup route or Execute-default change is allowed.
- R-3: parent destructive witnesses remain individually addressable |
  CP-7/8/9/10/11/13/14/15 | the 21 U/12 M methods and parent PC IDs remain
  intact; one eligible physical removal plus guarded refusal/recovery assertions
  prevents an inert no-op implementation from satisfying the scope.

### Guard inventory

D references below are to the landed CARD-1032 plan; parent control IDs retain
their parent semantic boundary. Guard numbers 200+ are additions, not renumbered
parent obligations. Distinct request requiredness, identity comparison, digest
coverage, first/final checks and receipt comparisons are independently bypassable
and have separate controls. Legacy R-1 guards are included because their selected
tests also need a falsifiable mutation. Untested status is explicit: this task
has not executed any control.

| Guard | Plan reference + safety-critical invariant | Control |
|---|---|---|
| G-52 | D-4; dispatcher uses distinct guarded operation | PC-52 |
| G-53 | D-2/D-7; full task identity | PC-53 |
| G-54 | D-2/D-7; attempt identity | PC-54 |
| G-55 | D-2/D-7; persistent store identity | PC-55 |
| G-56 | D-2/D-8; repository identity | PC-56 |
| G-57 | D-2/D-8; exact owned path | PC-57 |
| G-58 | D-5/D-8; symbolic source branch | PC-58 |
| G-59 | D-5; exact inspected HEAD | PC-59 |
| G-60 | D-5; preservation authority present | PC-60 |
| G-61 | D-8; initial tracked-source cleanliness | PC-61 |
| G-62 | D-8; protected ignored names win | PC-62 |
| G-63 | D-8; late directory links are not followed | PC-63 |
| G-64 | D-6; live child blocks cleanup despite terminal row | PC-64 |
| G-65 | D-6; ordinary StartAsync shares workspace fence | PC-65 |
| G-66 | D-6; restored fence blocks new adoption authority | PC-66 |
| G-67 | D-7/D-8; durable runner intent precedes mutation | PC-67 |
| G-68 | D-7; operation ID cannot change digest | PC-68 |
| G-69 | D-8; final tracked source read | PC-69 |
| G-70 | D-4/D-8; Force does not bypass guarded admission | PC-70 |
| G-71 | D-9/D-10; pending mirrors survive local Complete | PC-71 |
| G-72 | D-9; offline mirror does not prevent local progress | PC-72 |
| G-73 | D-5; mirror H may precede preserved P | PC-73 |
| G-74 | D-10; receipt operation matches intent | PC-74 |
| G-75 | D-10; receipt endpoint matches intent | PC-75 |
| G-76 | D-10; receipt store matches intent | PC-76 |
| G-77 | D-10; receipt attempt matches intent | PC-77 |
| G-78 | D-10; completion requires removal facts | PC-78 |
| G-79 | D-4; unsupported guarded operation has no legacy fallback | PC-79 |
| G-80 | D-9; mirror has independent Done admission | PC-80 |
| G-81 | D-3/D-9; distinct store bindings retain endpoints | PC-81 |
| G-82 | D-7/D-10; lost reply retries same operation | PC-82 |
| G-108 | D-1/D-2; SourceLanding is not ordinary authority | PC-108 |
| G-109 | D-6; unknown process evidence holds | PC-109 |
| G-200 | D-2; preparation identity unique in DB | PC-200 |
| G-201 | D-2; binding tuple unique in DB | PC-201 |
| G-202 | D-2; upgrade does not invent historical binding | PC-202 |
| G-203 | D-7; exact request survives persistence | PC-203 |
| G-204 | D-10; exact receipt survives persistence | PC-204 |
| G-205 | D-4; guarded operations have distinct wire values | PC-205 |
| G-206 | D-7; supported schema required | PC-206 |
| G-207 | D-7; canonical serialization is deterministic | PC-207 |
| G-208 | D-7; ambiguous duplicate JSON fields refuse | PC-208 |
| G-209 | D-2; short-ID collision does not enroll foreign tree | PC-209 |
| G-210 | D-2/D-3; partial creation has write-ahead ownership | PC-210 |
| G-211 | D-3; legacy pre-existing tree not enrolled on recovery | PC-211 |
| G-212 | D-3; lost creation reply reuses binding | PC-212 |
| G-213 | D-3; preparation ID immutable | PC-213 |
| G-214 | D-2; configured state root owns binding records | PC-214 |
| G-215 | D-2; binding write/read corruption is unknown | PC-215 |
| G-216 | D-11; optional binding support does not gate old create | PC-216 |
| G-217 | D-3; capture preserves multiple historical bindings | PC-217 |
| G-218 | D-3; late response is retained after reassignment | PC-218 |
| G-219 | D-3; late response cannot overwrite current assignment | PC-219 |
| G-220 | D-3; attempt change protects current path | PC-220 |
| G-221 | D-3; cancellation retains returned binding | PC-221 |
| G-222 | D-3; preparation recovery has durable identity before send | PC-222 |
| G-223 | D-6; path gate canonicalizes aliases | PC-223 |
| G-224 | D-6; explicit attach uses workspace admission | PC-224 |
| G-225 | D-6; startup census completes before cleanup admission | PC-225 |
| G-226 | D-6; Herdr startup adoption records conflicting ownership | PC-226 |
| G-227 | D-6; pending Herdr readoption records conflicting ownership | PC-227 |
| G-228 | D-6; mirror creation shares path fence | PC-228 |
| G-229 | D-6/D-7; journal uncertainty holds cleanup | PC-229 |
| G-230 | D-6; fixed workspace-before-session lock order | PC-230 |
| G-231 | D-6; discovered owner not killed to satisfy fence | PC-231 |
| G-232 | D-6; discovered owner remains observable | PC-232 |
| G-233 | D-4; inspection is read-only | PC-233 |
| G-234 | D-4; inspection cannot enroll unbound historical tree | PC-234 |
| G-235 | D-8; ignored policy unknown names hold | PC-235 |
| G-236 | D-8; evidence precedes disposable match | PC-236 |
| G-237 | D-8; runner protected minima cannot be weakened | PC-237 |
| G-238 | D-8; .antiphon requires specific policy | PC-238 |
| G-239 | D-5; inspect reports actual mirror HEAD | PC-239 |
| G-240 | D-2/D-8; common directory identity | PC-240 |
| G-241 | D-2/D-8; admin directory identity | PC-241 |
| G-242 | D-2; exact binding generation | PC-242 |
| G-243 | D-2; ordinary workspace class required | PC-243 |
| G-244 | D-5; proof endpoint fingerprint | PC-244 |
| G-245 | D-5; proof targets configured published ref | PC-245 |
| G-246 | D-5; rebase mapping belongs to exact source | PC-246 |
| G-247 | D-5; fresh remote containment | PC-247 |
| G-248 | D-8; initial untracked source cleanliness | PC-248 |
| G-249 | D-8; nested repository excluded | PC-249 |
| G-250 | D-8; Git locked registration holds | PC-250 |
| G-251 | D-8; CardDone disposition required for evidence | PC-251 |
| G-252 | D-8; initial links hold inventory | PC-252 |
| G-253 | D-6; pooled owner independently holds | PC-253 |
| G-254 | D-8; final ownership read | PC-254 |
| G-255 | D-8; final ignored inventory | PC-255 |
| G-256 | D-8; final registration/source identity | PC-256 |
| G-257 | D-8; final preservation read | PC-257 |
| G-258 | D-8; eligible removal performs physical work | PC-258 |
| G-259 | D-8; only matching Git registration removed | PC-259 |
| G-260 | D-8; branch refs preserved | PC-260 |
| G-261 | D-7/D-8; set-aside ownership persisted before rename | PC-261 |
| G-262 | D-7/D-8; directory and registration recovered independently | PC-262 |
| G-263 | D-7; spent command allowance remains spent | PC-263 |
| G-264 | D-7; absent original without matching receipt is unknown | PC-264 |
| G-265 | D-8; replacement original path preserved | PC-265 |
| G-266 | D-8; occupied or foreign set-aside refused | PC-266 |
| G-267 | D-4; direct dispatcher without service refuses | PC-267 |
| G-268 | D-4; advertised capability requires composed service | PC-268 |
| G-269 | D-4; required payload validation reached over dispatch | PC-269 |
| G-270 | D-7/D-10; transport replay returns same durable receipt | PC-270 |
| G-271 | D-9; terminal task required at mirror admission | PC-271 |
| G-272 | D-9; settled floor checked before new intent | PC-272 |
| G-273 | D-9; fresh originating attempt/binding | PC-273 |
| G-274 | D-9; active consumer holds mirror | PC-274 |
| G-275 | D-9; unresolved recovery holds new intent | PC-275 |
| G-276 | D-9; report bytes and digest external | PC-276 |
| G-277 | D-9; deliverables external and retrievable | PC-277 |
| G-278 | D-9; artifact snapshot fresh at intent | PC-278 |
| G-279 | D-9; server intent committed before remote send | PC-279 |
| G-280 | D-9; intent winner survives later reopen | PC-280 |
| G-281 | D-9; DB locks released before Git/network | PC-281 |
| G-282 | D-10; request digest validated in receipt | PC-282 |
| G-283 | D-10; receipt imported before Complete | PC-283 |
| G-284 | D-10; durable outcome precedes invalidation | PC-284 |
| G-285 | D-10; real busy mutation queue reaches recipient | PC-285 |
| G-286 | D-3/D-10; enqueue/send failure retains recoverable debt | PC-286 |
| G-287 | D-10; runner result survives failed server save | PC-287 |
| G-288 | D-10; replacement runner store cannot complete old endpoint | PC-288 |
| G-289 | D-10; retry cooldown survives restart | PC-289 |
| G-290 | D-11; default server does not compose coordinator | PC-290 |
| G-291 | D-11; sweep has no automatic mirror caller | PC-291 |
| G-292 | D-11; no new public cleanup route | PC-292 |
| G-293 | D-7/D-10; diagnostics exclude payload secrets | PC-293 |
| G-300 | D-7; required request member CardId | PC-300 |
| G-301 | D-7; required request member DoneRevisionId | PC-301 |
| G-302 | D-7; required request member TargetId | PC-302 |
| G-303 | D-7; required request member EndpointId | PC-303 |
| G-304 | D-7; required request member TaskId | PC-304 |
| G-305 | D-7; required request member Attempt | PC-305 |
| G-306 | D-7; required request member BindingId | PC-306 |
| G-307 | D-7; required request member PreparationId | PC-307 |
| G-308 | D-7; required request member RunnerId | PC-308 |
| G-309 | D-7; required request member RunnerStoreId | PC-309 |
| G-310 | D-7; required request member RepositoryIdentity | PC-310 |
| G-311 | D-7; required request member CommonDirectory | PC-311 |
| G-312 | D-7; required request member GitDirectory | PC-312 |
| G-313 | D-7; required request member WorktreePath | PC-313 |
| G-314 | D-7; required request member SourceFullRef | PC-314 |
| G-315 | D-7; required request member CreationBaselineSha | PC-315 |
| G-316 | D-7; required request member OperationId | PC-316 |
| G-317 | D-7; required request member ExpectedHeadSha | PC-317 |
| G-318 | D-7; required request member DestinationFingerprint | PC-318 |
| G-319 | D-7; required request member DestinationRef | PC-319 |
| G-320 | D-7; required request member ObservedTargetSha | PC-320 |
| G-321 | D-7; required request member ProofKind | PC-321 |
| G-322 | D-7; required request member ProofIdentity | PC-322 |
| G-323 | D-7; required request member PreservedSourceSha | PC-323 |
| G-324 | D-7; required request member ReportDigest | PC-324 |
| G-325 | D-7; required request member ArtifactProofDigest | PC-325 |
| G-326 | D-7; required request member DisposalPolicyDigest | PC-326 |
| G-327 | D-7; required request member Disposition | PC-327 |
| G-340 | D-7; digest includes SchemaVersion | PC-340 |
| G-341 | D-7; digest includes CardId | PC-341 |
| G-342 | D-7; digest includes DoneRevisionId | PC-342 |
| G-343 | D-7; digest includes TargetId | PC-343 |
| G-344 | D-7; digest includes EndpointId | PC-344 |
| G-345 | D-7; digest includes TaskId | PC-345 |
| G-346 | D-7; digest includes Attempt | PC-346 |
| G-347 | D-7; digest includes BindingId | PC-347 |
| G-348 | D-7; digest includes PreparationId | PC-348 |
| G-349 | D-7; digest includes RunnerId | PC-349 |
| G-350 | D-7; digest includes RunnerStoreId | PC-350 |
| G-351 | D-7; digest includes RepositoryIdentity | PC-351 |
| G-352 | D-7; digest includes CommonDirectory | PC-352 |
| G-353 | D-7; digest includes GitDirectory | PC-353 |
| G-354 | D-7; digest includes WorktreePath | PC-354 |
| G-355 | D-7; digest includes SourceFullRef | PC-355 |
| G-356 | D-7; digest includes CreationBaselineSha | PC-356 |
| G-357 | D-7; digest includes OperationId | PC-357 |
| G-358 | D-7; digest includes ExpectedHeadSha | PC-358 |
| G-359 | D-7; digest includes DestinationFingerprint | PC-359 |
| G-360 | D-7; digest includes DestinationRef | PC-360 |
| G-361 | D-7; digest includes ObservedTargetSha | PC-361 |
| G-362 | D-7; digest includes ProofKind | PC-362 |
| G-363 | D-7; digest includes ProofIdentity | PC-363 |
| G-364 | D-7; digest includes PreservedSourceSha | PC-364 |
| G-365 | D-7; digest includes ReportDigest | PC-365 |
| G-366 | D-7; digest includes ArtifactProofDigest | PC-366 |
| G-367 | D-7; digest includes DisposalPolicyDigest | PC-367 |
| G-368 | D-7; digest includes Disposition | PC-368 |
| G-369 | D-7; digest includes RebasedSourceSha | PC-369 |
| G-370 | D-7; digest includes ArtifactPointers | PC-370 |
| G-371 | D-7; digest includes PolicyPatterns | PC-371 |
| G-380 | D-10; receipt SchemaVersion matches persisted request/journal | PC-380 |
| G-381 | D-10; receipt CardId matches persisted request/journal | PC-381 |
| G-382 | D-10; receipt DoneRevisionId matches persisted request/journal | PC-382 |
| G-383 | D-10; receipt TargetId matches persisted request/journal | PC-383 |
| G-384 | D-10; receipt TaskId matches persisted request/journal | PC-384 |
| G-385 | D-10; receipt BindingId matches persisted request/journal | PC-385 |
| G-386 | D-10; receipt PreparationId matches persisted request/journal | PC-386 |
| G-387 | D-10; receipt RunnerId matches persisted request/journal | PC-387 |
| G-388 | D-10; receipt RepositoryIdentity matches persisted request/journal | PC-388 |
| G-389 | D-10; receipt CommonDirectory matches persisted request/journal | PC-389 |
| G-390 | D-10; receipt GitDirectory matches persisted request/journal | PC-390 |
| G-391 | D-10; receipt WorktreePath matches persisted request/journal | PC-391 |
| G-392 | D-10; receipt SourceFullRef matches persisted request/journal | PC-392 |
| G-393 | D-10; receipt CreationBaselineSha matches persisted request/journal | PC-393 |
| G-394 | D-10; receipt ExpectedHeadSha matches persisted request/journal | PC-394 |
| G-395 | D-10; receipt PreservationProofDigest matches persisted request/journal | PC-395 |
| G-396 | D-10; receipt ArtifactProofDigest matches persisted request/journal | PC-396 |
| G-397 | D-10; receipt DisposalPolicyDigest matches persisted request/journal | PC-397 |
| G-398 | D-10; receipt Disposition matches persisted request/journal | PC-398 |
| G-399 | D-10; receipt SetAsideIdentity matches persisted request/journal | PC-399 |
| G-400 | D-11/R-1; preserve Publish_pushes_only_own_fast_forward_branch | PC-400 |
| G-401 | D-11/R-1; preserve Publish_refuses_a_rebased_tip_without_pushing | PC-401 |
| G-402 | D-11/R-1; preserve Publish_equal_tip_is_not_pushed_and_reports_dirty_tree | PC-402 |
| G-403 | D-11/R-1; preserve Publish_refuses_foreign_path_and_branch_before_git | PC-403 |
| G-404 | D-11/R-1; preserve Publish_refuses_detached_head | PC-404 |
| G-405 | D-11/R-1; preserve Publish_refuses_an_active_sequencer_before_push | PC-405 |
| G-406 | D-11/R-1; preserve Publish_reports_rejected_non_fast_forward_without_exposing_origin | PC-406 |
| G-407 | D-11/R-1; preserve Publish_classifies_a_tip_behind_the_advertised_remote | PC-407 |
| G-408 | D-11/R-1; preserve Remove_refuses_committed_unpublished_tip_unless_forced | PC-408 |
| G-409 | D-11/R-1; preserve Remove_allows_a_published_tip | PC-409 |
| G-410 | D-11/R-1; preserve Remove_allows_a_tip_which_is_ancestor_of_published_sha | PC-410 |
| G-411 | D-11/R-1; preserve Mirror_of_a_second_repository_clones_beside_the_primary_and_removes_through_its_own_checkout | PC-411 |
| G-412 | D-11/R-1; preserve Mirror_refuses_a_repository_outside_the_allowed_clone_sources | PC-412 |
| G-413 | D-11/R-1; preserve Mirror_refuses_credentialed_request_without_echoing_credentials | PC-413 |
| G-414 | D-11/R-1; preserve Mirror_refuses_an_existing_checkout_whose_origin_is_another_repository | PC-414 |
| G-415 | D-11/R-1; preserve Mirror_mismatch_does_not_echo_credentialed_origin | PC-415 |
| G-416 | D-11/R-1; preserve Mirror_refuses_a_secondary_repository_the_push_credential_cannot_push_to | PC-416 |
| G-417 | D-11/R-1; preserve Mirror_accepts_a_secondary_repository_when_push_dry_run_succeeds | PC-417 |
| G-418 | D-11/R-1; preserve Mirror_creates_worktree_on_branch_at_sha | PC-418 |
| G-419 | D-11/R-1; preserve Mirror_refuses_sha_mismatch | PC-419 |
| G-420 | D-11/R-1; preserve Mirror_clones_repository_when_absent | PC-420 |
| G-421 | D-11/R-1; preserve Mirror_failure_is_admission_error_not_crash | PC-421 |
| G-422 | D-11/R-1; preserve Mirror_refuses_a_name_or_sha_it_did_not_shape | PC-422 |
| G-423 | D-11/R-1; preserve Remove_refuses_dirty_tree | PC-423 |
| G-424 | D-11/R-1; preserve Remove_refuses_a_path_outside_the_worktree_root | PC-424 |
| G-425 | D-11/R-1; preserve Workspace_ops_are_admitted_only_under_allowed_cwd | PC-425 |
| G-426 | D-11/R-1; preserve Workspace_mirror_naming_an_unadmitted_repository_is_refused_before_any_git_runs | PC-426 |
| G-427 | D-11/R-1; preserve Workspace_publish_route_rejects_a_foreign_target_before_git | PC-427 |
| G-428 | D-11/R-1; preserve Push_reads_the_desktop_origin_and_the_mirror_request_carries_its_https_identity | PC-428 |
| G-429 | D-11/R-1; preserve Push_refuses_when_the_desktop_origin_cannot_be_read | PC-429 |
| G-430 | D-11/R-1; preserve Sync_fast_forwards_desktop_worktree | PC-430 |
| G-431 | D-11/R-1; preserve Non_fast_forward_sync_is_refused | PC-431 |
| G-432 | D-11/R-1; preserve Dirty_desktop_tree_is_never_fast_forwarded_over | PC-432 |
| G-433 | D-11/R-1; preserve Push_failure_reports_and_does_not_invent_a_sha | PC-433 |
| G-434 | D-11/R-1; preserve Push_reports_the_head_it_pushed | PC-434 |
| G-435 | D-11/R-1; preserve Mirror_name_is_the_dispatchers_own_short_form | PC-435 |
| G-500 | D-7; received digest equals canonical payload | PC-500 |
| G-501 | D-8; final HEAD checked independently | PC-501 |
| G-502 | D-8; final symbolic branch checked independently | PC-502 |
| G-503 | D-8; final untracked status checked independently | PC-503 |
| G-504 | D-8; final intent binding checked independently | PC-504 |
| G-505 | D-8; nested registration checked independently | PC-505 |
| G-506 | D-9; report location outside deletion roots | PC-506 |
| G-507 | D-9; complete Result content preserved | PC-507 |
| G-508 | D-9; remote artifact proof actually queried | PC-508 |
| G-509 | D-10; directory success required separately | PC-509 |
| G-510 | D-10; registration success required separately | PC-510 |
| G-511 | D-7/D-10; retries use original bytes | PC-511 |
| G-512 | D-10; finite wait uses monotonic elapsed time | PC-512 |
| G-513 | D-11; Execute default remains false | PC-513 |
| G-514 | D-8; final admin directory checked independently | PC-514 |
| G-515 | D-8; final common directory checked independently | PC-515 |
| G-516 | D-8; final locked/nested inventory checked | PC-516 |
| G-517 | D-7; receipt written outside deletion roots before reply | PC-517 |
| G-520 | D-3; capture validates returned binding TaskId | PC-520 |
| G-521 | D-3; capture validates returned binding Attempt | PC-521 |
| G-522 | D-3; capture validates returned binding PreparationId | PC-522 |
| G-523 | D-3; capture validates returned binding RunnerId | PC-523 |
| G-524 | D-3; capture validates returned binding RunnerStoreId | PC-524 |
| G-525 | D-3; capture validates returned binding BindingId | PC-525 |
| G-526 | D-3; capture validates returned binding RepositoryIdentity | PC-526 |
| G-527 | D-3; capture validates returned binding WorktreePath | PC-527 |
| G-528 | D-3; capture validates returned binding SourceFullRef | PC-528 |
| G-529 | D-3; capture validates returned binding CreationBaselineSha | PC-529 |
| G-530 | D-2/D-3; interrupted creation does not adopt another task's tree | PC-530 |
| G-531 | D-3; completed replay refuses an absent generation | PC-531 |
| G-532 | D-2; a bound mirror whose runner or store is not this recipient is refused before any directory or ledger write | PC-532 |
| G-533 | D-3; preparation reuse requires the live store and leaves an old-store row as incomplete debt | PC-533 |
| G-534 | S2 D1; idle join writes the owner stamp and a fresh server read shows the completed binding | PC-534 |
| G-535 | S2 D1; a busy recipient leaves no partial binding or ledger for the waiting request | PC-535 |
| G-536 | S2 D1; enqueue-then-crash retry reuses the same preparation and completes one binding | PC-536 |
| G-537 | S2 D1; foreign_mirror_owner through the live queue leaves the foreign tree and ledger untouched | PC-537 |
| G-538 | S2 D1; stale_mirror_binding through the live queue does not complete the server row | PC-538 |
| G-539 | S2 D1; runner_store_mismatch through the live queue writes no directory or ledger | PC-539 |
| G-540 | S2; missing registration file refuses a bound mirror that names the custody guid before any tree or ledger write | PC-540 |
| G-541 | S2; empty registration file refuses that bound mirror | PC-541 |
| G-542 | S2; whitespace registration file refuses that bound mirror | PC-542 |
| G-543 | S2; garbled registration file refuses that bound mirror | PC-543 |
| G-544 | S2; wrong-length registration guid refuses that bound mirror | PC-544 |
| G-545 | S2; a directory at the registration path refuses that bound mirror | PC-545 |
| G-546 | S2; an unreadable registration file refuses that bound mirror | PC-546 |
| G-547 | S2; a readable different registration guid still refuses a request that names the custody guid | PC-547 |
| G-548 | S2; a readable matching registration guid completes under that guid, not the custody guid | PC-548 |
| G-549 | S2; an unbound request still creates the mirror when the registration file is missing | PC-549 |
| G-550 | D-12; legacy creation invents no binding ledger or owner claim | PC-550 |

### Positive controls

Each row is a **required, pending** break/red/restore/green cycle after land and
after Mutation is resumed. The detecting filter is method-scoped, with no class
wildcard or suite substitute. The case identifier is `PC-n` plus the named
variant in that row. RUN/MIR variants use the positive baseline and evidence
order above; schema/wire/legacy variants use their bound recipe's real production
entry. Thus recipe, variant, case, oracle, reachability and classification are
specified without counting loop iterations as tests.

For a negative case, first prove the target seam was reached while preceding
guards stayed eligible. The named red assertion must fail because of the compiling
production defect, not because of missing files, setup, compile errors, timeout,
zero execution or a mock expectation unrelated to the guarded outcome. For physical
safety controls, keep file/registration/ref observations as well as refusal
assertions. A restore run uses rebuilt restored source and the same exact method.
Controls sharing a production file/method run separately; batching savings are
not assumed.

These rows name semantic compiling changes, not executable patch payloads.
**Payload status: binding required**, including after Code lands. Code supplies
actual method/file/line and case labels; it does not mark a control executed.
Before mechanical Mutation, bind an exact compile-safe patch to the landed source
and this decisive assertion. If a later guard masks the nominated red assertion,
repair the case/binding through the authorized stage rather than accepting a
surviving mutation. New contract member paths and planned assertion variable names
must be resolved to concrete production/test statements; aliases below are not
permission to mutate test expectations.

Parent PC-52..82 and PC-108/109 are retained. PC-58 uses the additional
identity-refusal assertion described above, because its original branch-survival
assertion alone also holds after a valid mirror removal. Legacy PCs name existing
assertion expressions or their local value; binding must cite the existing actual
assertion, with no weakening/relabeling. Parent S4/S5 controls outside this card
remain pending under the parent artifact, not silently discharged here.

| PC | Recipe | Named variant / case -> compiling production mutation | Exact detecting filter | Expected red assertion |
|---|---|---|---|---|
| PC-52 | RUN | eligible command through dispatcher -> break G-52: route it to legacy RemoveAsync. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_GuardedOperationIsDistinct` | `legacyRemovalCalls.ShouldBe(0)` |
| PC-53 | RUN | different full TaskId with equal short id and valid path -> break G-53: omit TaskId comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerTaskIdentity` | `treeExists.ShouldBeTrue()` |
| PC-54 | RUN | same task and binding coordinates, foreign attempt -> break G-54: omit attempt comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerAttemptIdentity` | `treeExists.ShouldBeTrue()` |
| PC-55 | RUN | new store at same path, otherwise matching journal -> break G-55: omit RunnerStoreId comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerStoreIdentity` | `treeExists.ShouldBeTrue()` |
| PC-56 | RUN | real foreign repository with the same commit -> break G-56: omit repository identity comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerRepositoryIdentity` | `foreignBytes.ShouldBe(originalBytes)` |
| PC-57 | RUN | valid second task tree, sibling-prefix and outside-root variants -> break G-57: omit exact recorded-path comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerPathIdentity` | `otherTreeExists.ShouldBeTrue()` |
| PC-58 | RUN | different symbolic branch at equal SHA; detached variant -> break G-58: omit symbolic-ref comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerBranchIdentity` | `identityRefused.ShouldBeTrue()` |
| PC-59 | RUN | unique committed HEAD changed after request -> break G-59: omit expected HEAD comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerHeadIdentity` | `preservationProofChecks.ShouldBe(0)` |
| PC-60 | RUN | bare SHA with no proof and exact contained positive in S5; bound-rebase positive fixture moves to S7 C1032_AdmissionBoundRebaseProof -> break G-60: accept missing preservation proof. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerPreservationProof` | `treeExists.ShouldBeTrue()` |
| PC-61 | RUN | staged and unstaged tracked edits; separate untracked variant -> break G-61: accept nonempty tracked status. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerDirtySource` | `finalInspectionCalls.ShouldBe(0)` |
| PC-62 | RUN | bin/.env fixture overlaps disposable rule -> break G-62: remove protected-name precedence. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerIgnoredPolicy` | `protectedBytes.ShouldBe(originalBytes)` |
| PC-63 | RUN | swap enumerated directory for outside link after final inspection -> break G-63: enumerate through the substituted link. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerNoFollow` | `outsideSentinel.ShouldBe(originalBytes)` |
| PC-64 | RUN | terminal session row with observed live owned child -> break G-64: use terminal status as sufficient no-owner proof. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerProcessOwnerHolds` | `treeExists.ShouldBeTrue()` |
| PC-65 | RUN | intent-first and start-first with different session IDs at same workspace -> break G-65: skip workspace fence in StartAsync. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerStartFence` | `startsAtRetiringPath.ShouldBe(0)` |
| PC-66 | RUN | restart at durable intent, then attempt admission through adoption -> break G-66: skip restored workspace fence on pty adoption. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerReadoptionFence` | `adoptionsAtRetiringPath.ShouldBe(0)` |
| PC-67 | RUN | pause before first physical rename and read journal independently -> break G-67: move intent persistence after first mutation. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerIntentBeforeIo` | `intentVisibleAtFirstMutation.ShouldBeTrue()` |
| PC-68 | RUN | same ID with changed valid digest versus byte-identical replay -> break G-68: return saved success for changed digest. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerDigestReplay` | `changedRequestAccepted.ShouldBeFalse()` |
| PC-69 | RUN | late tracked file after successful first read -> break G-69: omit final tracked-source validation. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateTreeBytes.ShouldBe(originalBytes)` |
| PC-70 | RUN | raw force=true with otherwise valid request and dirty source -> break G-70: add compiling raw-JSON force arm routing to forced legacy removal. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerNoForceBypass` | `dirtyBytes.ShouldBe(originalBytes)` |
| PC-71 | MIR | local Complete and mirror Pending, fresh coordinator -> break G-71: filter out targets with completed local endpoint. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_MirrorCompletesIndependently` | `mirrorDirectoryGone.ShouldBeTrue()` |
| PC-72 | MIR | offline mirror with eligible local tree -> break G-72: require online mirror before local execution. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_LocalProgressSurvivesOffline` | `localDirectoryGone.ShouldBeTrue()` |
| PC-73 | MIR | real H ancestor of P, and changed-H refusal -> break G-73: require H==P despite exact H containment proof. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_MirrorBehindPushedTip` | `mirrorDirectoryGone.ShouldBeTrue()` |
| PC-74 | MIR | valid foreign-operation receipt, other fields equal -> break G-74: omit receipt operation-ID comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReceiptMustMatchOperation` | `mirrorComplete.ShouldBeFalse()` |
| PC-75 | MIR | valid receipt with only endpoint changed -> break G-75: omit endpoint-ID comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReceiptMustMatchEndpoint` | `mirrorComplete.ShouldBeFalse()` |
| PC-76 | MIR | valid receipt from replacement store -> break G-76: omit store-ID comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReceiptMustMatchStore` | `mirrorComplete.ShouldBeFalse()` |
| PC-77 | MIR | valid receipt with only attempt changed -> break G-77: omit attempt comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReceiptMustMatchTaskAttempt` | `mirrorComplete.ShouldBeFalse()` |
| PC-78 | MIR | ack, empty payload, null path, timeout and component-unknown variants -> break G-78: mark Complete on successful RPC return. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_AckIsNotRemoval` | `mirrorComplete.ShouldBeFalse()` |
| PC-79 | MIR | missing capability or UnsupportedOperation response -> break G-79: retry using WorkspaceRemove. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_UnsupportedNeverFallsBack` | `legacyRemovalCalls.ShouldBe(0)` |
| PC-80 | MIR | reopen commits after local result before mirror intent -> break G-80: reuse local generation admission for mirror. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReopenBetweenEndpoints` | `remoteCommandsIssued.ShouldBe(0)` |
| PC-81 | MIR | two stores, first Complete, second offline -> break G-81: replace normalized bindings with current scalar path. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_MultipleMirrorsRemainDistinct` | `persistedEndpointCount.ShouldBe(2)` |
| PC-82 | MIR | real removal, dropped reply, server restart and reconnect -> break G-82: allocate new operation on retry. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_LostAckReusesOperation` | `distinctRemovalOperationIds.ShouldBe(1)` |
| PC-108 | RUN | genuine verification creation with forged ordinary fields -> break G-108: accept verification creation as ordinary binding. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerSourceLandingExcluded` | `verificationTreeExists.ShouldBeTrue()` |
| PC-109 | RUN | unavailable census and torn owned-child record -> break G-109: treat unknown ownership as empty. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_UnknownProcessOwnershipHolds` | `treeExists.ShouldBeTrue()` |
| PC-200 | DB | concurrent inserts with same PreparationId -> break G-200: drop preparation unique index in mapping and generated migration. | `/*/*/CardWorktreeMirrorBindingTests/C1032_SchemaUniqueBindings` | `preparationRows.ShouldBe(1)` |
| PC-201 | DB | same task/attempt/store/binding under different preparation IDs -> break G-201: drop tuple unique index in mapping and generated migration. | `/*/*/CardWorktreeMirrorBindingTests/C1032_SchemaUniqueBindings` | `duplicateBindingRejected.ShouldBeTrue()` |
| PC-202 | DB | upgrade predecessor DB with active, Done, canceled and scalar-only mirror rows -> break G-202: add synthetic binding backfill for RemoteWorktreePath. | `/*/*/CardWorktreeMirrorBindingTests/C1032_SchemaLegacyUpgrade` | `legacyBindingCount.ShouldBe(0)` |
| PC-203 | DB | save intent, change task coordinates, reopen DbContext -> break G-203: omit immutable-request property from EF persistence mapping. | `/*/*/CardWorktreeMirrorBindingTests/C1032_SchemaRequestAndReceiptPersistence` | `persistedRequestBytes.ShouldBe(originalRequestBytes)` |
| PC-204 | DB | save typed receipt with component facts then fresh context -> break G-204: omit receipt payload column write. | `/*/*/CardWorktreeMirrorBindingTests/C1032_SchemaRequestAndReceiptPersistence` | `persistedReceiptBytes.ShouldBe(originalReceiptBytes)` |
| PC-205 | WIRE | round-trip inspect/remove plus old operation 15 literal -> break G-205: reuse legacy enum value for guarded remove. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRoundTrip` | `guardedOperationValue.ShouldNotBe(15)` |
| PC-206 | WIRE | unknown/zero schema against otherwise valid JSON -> break G-206: accept unknown version as current. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `versionAccepted.ShouldBeFalse()` |
| PC-207 | WIRE | equivalent object property order and artifact order normalization -> break G-207: digest uncanonicalized property insertion order. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `equivalentDigest.ShouldBe(originalDigest)` |
| PC-208 | WIRE | duplicate operationId with two values -> break G-208: accept last duplicate field. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `duplicateAccepted.ShouldBeFalse()` |
| PC-209 | RB | different full IDs share task-deadbeef leaf and equal source -> break G-209: lookup binding by short name alone. | `/*/*/RunnerWorkspaceBindingTests/C1032_BindingFullIdentityCollision` | `foreignBindingAccepted.ShouldBeFalse()` |
| PC-210 | RB | cut after preparation journal, after worktree add, before completion, after completion -> break G-210: write creation intent only after worktree add. | `/*/*/RunnerWorkspaceBindingTests/C1032_BindingCreationRecovery` | `creationIntentAtGitAdd.ShouldBeTrue()` |
| PC-211 | RB | legacy equal-SHA tree exists before incomplete preparation -> break G-211: mark pre-existing tree as completed bound creation. | `/*/*/RunnerWorkspaceBindingTests/C1032_BindingCreationRecovery` | `legacyTreeEnrolled.ShouldBeFalse()` |
| PC-212 | RB | new store object over same state after reply loss -> break G-212: generate new BindingId for exact PreparationId replay. | `/*/*/RunnerWorkspaceBindingTests/C1032_BindingExactReplay` | `replayedBindingId.ShouldBe(originalBindingId)` |
| PC-213 | RB | same preparation with changed attempt/branch/baseline -> break G-213: overwrite existing preparation record on mismatch. | `/*/*/RunnerWorkspaceBindingTests/C1032_BindingExactReplay` | `changedPreparationAccepted.ShouldBeFalse()` |
| PC-214 | RB | payload asks state path inside mirror and outside allowed state root -> break G-214: use payload-supplied state root. | `/*/*/RunnerWorkspaceBindingTests/C1032_BindingStateRootIsolation` | `bindingStoredAtConfiguredRoot.ShouldBeTrue()` |
| PC-215 | RB | torn, unreadable or missing record after interrupted write -> break G-215: recover failed record read as a completed binding. | `/*/*/RunnerWorkspaceBindingTests/C1032_BindingStateRootIsolation` | `bindingAccepted.ShouldBeFalse()` |
| PC-216 | RB | old request without binding extension on configured ordinary service -> break G-216: require binding fields for legacy mirror requests. | `/*/*/RunnerWorkspaceBindingTests/C1032_BindingLegacyCompatibility` | `legacyMirrorExists.ShouldBeTrue()` |
| PC-217 | DB | attempt/store A then B; reload with current scalar pointing only at B -> break G-217: upsert binding by TaskId only. | `/*/*/CardWorktreeMirrorBindingTests/C1032_CaptureDistinctBindings` | `capturedBindingCount.ShouldBe(2)` |
| PC-218 | CAP | hold reply, reassign runner, then return valid binding -> break G-218: return before persisting historical binding when assignment differs. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLateReassignment` | `oldRunnerBindingPersisted.ShouldBeTrue()` |
| PC-219 | CAP | current runner B already has a path when A replies -> break G-219: update scalar path without runner compare. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLateReassignment` | `currentPath.ShouldBe(runnerBPath)` |
| PC-220 | CAP | same runner, increment attempt before reply -> break G-220: drop attempt predicate on scalar update. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureAttemptAndCancellation` | `currentPath.ShouldBe(newAttemptPath)` |
| PC-221 | CAP | cancel originating attempt before reply -> break G-221: skip binding persistence for terminal task. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureAttemptAndCancellation` | `canceledAttemptBindingPersisted.ShouldBeTrue()` |
| PC-222 | CAP | server dies after request send then preparation rearmed -> break G-222: allocate fresh PreparationId on recovery. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `recoveryPreparationId.ShouldBe(firstPreparationId)` |
| PC-223 | RUN | different session IDs and normalized aliases of one workspace -> break G-223: key gate by raw path or session ID. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_FenceStartOrders` | `simultaneousAdmissions.ShouldBe(1)` |
| PC-224 | RUN | real AttachHerdrAsync after intent, then inverse order -> break G-224: omit workspace admission in explicit attach. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_FenceAttachOrders` | `attachEffectsAtRetiringPath.ShouldBe(0)` |
| PC-225 | RUN | hold manifest census at restart with retained live child -> break G-225: mark census ready before manifests inspected. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_FenceStartupAdoptionOrders` | `cleanupAdmittedBeforeCensus.ShouldBeFalse()` |
| PC-226 | RUN | live sidecar at fenced path -> break G-226: omit ownership conflict on startup Herdr adoption. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_FenceHerdrReadoptionOrders` | `ownershipConflictVisible.ShouldBeTrue()` |
| PC-227 | RUN | unreachable sidecar then reconnect via SweepVanishedSessionsAsync -> RetryPendingHerdrAsync -> break G-227: omit workspace guard on pending retry arm. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_FenceHerdrReadoptionOrders` | `cleanupAdmittedDuringPendingAdoption.ShouldBeFalse()` |
| PC-228 | RUN | MirrorAsync competing with retiring gate, both race orders -> break G-228: omit workspace admission from mirror creation. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_FenceCreationOrders` | `mirrorCreatesAtRetiringPath.ShouldBe(0)` |
| PC-229 | RUN | missing/torn/unreadable retirement journal after restart -> break G-229: restore malformed fence as open. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_FenceUnknownJournal` | `cleanupAdmitted.ShouldBeFalse()` |
| PC-230 | RUN | hold workspace, observe request at gate before session gate acquired -> break G-230: acquire session lock before workspace lock. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_FenceStartOrders` | `sessionGateHeldBeforeWorkspace.ShouldBeFalse()` |
| PC-231 | RUN | live surviving child contradicts persisted retiring intent -> break G-231: call KillPidBestEffort on the cleanup conflict arm. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_FenceStartupAdoptionOrders` | `ownedChildStillAlive.ShouldBeTrue()` |
| PC-232 | RUN | same contradictory manifest/sidecar, fresh runtime observation -> break G-232: discard retained owner before recording conflict. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_FenceStartupAdoptionOrders` | `retainedOwnerVisible.ShouldBeTrue()` |
| PC-233 | RUN | known bound clean mirror inspection -> break G-233: invoke removal/registration cleanup from inspect. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionReadOnlyBound` | `repositoryAndTreeDigestAfter.ShouldBe(before)` |
| PC-234 | RUN | unbound valid legacy tree with full caller-supplied identity -> break G-234: create binding while inspecting unknown path. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionReadOnlyBound` | `bindingCountAfter.ShouldBe(bindingCountBefore)` |
| PC-235 | RUN | unknown ignored synthetic file -> break G-235: default unknown classification to Disposable. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionPolicyParity` | `unknownClassification.ShouldBe(Protected)` |
| PC-236 | RUN | known checkpoint evidence overlaps custom disposable rule -> break G-236: classify Disposable before Evidence. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionPolicyParity` | `evidenceClassification.ShouldBe(Evidence)` |
| PC-237 | RUN | request policy removes .env protection -> break G-237: replace protected minima with request policy. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionPolicyParity` | `minimumClassification.ShouldBe(Protected)` |
| PC-238 | RUN | bin/.antiphon/unknown under broad bin rule -> break G-238: apply ancestor disposable glob to .antiphon subtree. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionPolicyParity` | `nestedAntiphonClassification.ShouldBe(Protected)` |
| PC-239 | RUN | H behind P and advanced H2; clean fixture -> break G-239: return request or creation baseline as observed HEAD. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionSourceProof` | `observedHead.ShouldBe(actualHead)` |
| PC-240 | RUN | foreign real common directory with equal commit -> break G-240: omit common-directory comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerRepositoryIdentity` | `commonIdentityRefused.ShouldBeTrue()` |
| PC-241 | RUN | foreign valid worktree admin directory -> break G-241: omit admin-directory comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerRepositoryIdentity` | `adminIdentityRefused.ShouldBeTrue()` |
| PC-242 | RUN | same task/attempt/path but replaced BindingId -> break G-242: omit BindingId comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerTaskIdentity` | `bindingIdentityRefused.ShouldBeTrue()` |
| PC-243 | RUN | canonical, reusable slot, shared or borrowed tree with crafted ordinary fields -> break G-243: accept unsupported workspace kind. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerPathIdentity` | `ordinaryKindRefused.ShouldBeTrue()` |
| PC-244 | RUN | valid proof for different destination -> break G-244: omit destination-fingerprint comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerPreservationProof` | `proofEndpointRefused.ShouldBeTrue()` |
| PC-245 | RUN | H exists only on unique pushed task branch -> break G-245: use task ref instead of configured destination ref. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerPreservationProof` | `unlandedTreeExists.ShouldBeTrue()` |
| PC-246 | RUN | confirmed unrelated landing with equal resulting tree bytes -> break G-246: accept mapping for other task/source operation. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerPreservationProof` | `mappingRefused.ShouldBeTrue()` |
| PC-247 | RUN | remote target rewound after proof or read fails -> break G-247: trust stale observed target without fresh remote check. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerPreservationProof` | `freshProofRefused.ShouldBeTrue()` |
| PC-248 | RUN | nonignored untracked source only -> break G-248: omit untracked inventory from initial status. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerDirtySource` | `untrackedInitialRefused.ShouldBeTrue()` |
| PC-249 | RUN | nested .git repository under otherwise disposable tree -> break G-249: ignore nested repository detection. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerIgnoredPolicy` | `nestedTreeExists.ShouldBeTrue()` |
| PC-250 | RUN | real git worktree lock on otherwise eligible mirror -> break G-250: ignore locked registration. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerIgnoredPolicy` | `lockedTreeExists.ShouldBeTrue()` |
| PC-251 | RUN | known evidence with non-CardDone disposition -> break G-251: allow evidence disposal for ordinary/unknown disposition. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerIgnoredPolicy` | `evidenceBytes.ShouldBe(originalBytes)` |
| PC-252 | RUN | existing link/dangling link to outside owned sentinel -> break G-252: ignore links in ignored-content inventory. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerIgnoredPolicy` | `initialLinkRefused.ShouldBeTrue()` |
| PC-253 | RUN | pooled process at mirror with terminal task row -> break G-253: exclude pooled owners from use census. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerProcessOwnerHolds` | `pooledOwnerTreeExists.ShouldBeTrue()` |
| PC-254 | RUN | new owner appears after initial ownership read -> break G-254: omit final owner validation. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateOwnerTreeExists.ShouldBeTrue()` |
| PC-255 | RUN | protected ignored file appears after initial inventory -> break G-255: reuse initial ignored inventory at deletion. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateProtectedBytes.ShouldBe(originalBytes)` |
| PC-256 | RUN | swap registration/admin identity after initial inspection -> break G-256: reuse initial registration at deletion. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateRegistrationRefused.ShouldBeTrue()` |
| PC-257 | RUN | remote containment disappears before mutation -> break G-257: omit final preservation validation. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateProofRefused.ShouldBeTrue()` |
| PC-258 | RUN | real owned clean redundant mirror, CardDone evidence -> break G-258: return success receipt without executing no-follow delete. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalEligiblePhysical` | `directoryGone.ShouldBeTrue()` |
| PC-259 | RUN | owned mirror and unrelated stale registration -> break G-259: replace targeted unregister with repository-wide prune. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalEligiblePhysical` | `foreignRegistrationExists.ShouldBeTrue()` |
| PC-260 | RUN | snapshot local and origin refs before successful mirror removal -> break G-260: delete owned task ref after remove. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalEligiblePhysical` | `refsAfter.ShouldBe(refsBefore)` |
| PC-261 | RUN | observer paused before rename reads external record -> break G-261: persist set-aside path only after rename. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalComponentRecovery` | `setAsideIdentityVisibleBeforeRename.ShouldBeTrue()` |
| PC-262 | RUN | crash after rename, unregister, partial delete, receipt write; each component pair -> break G-262: treat directoryGone alone as completed removal. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalComponentRecovery` | `completedWithRegistrationPresent.ShouldBeFalse()` |
| PC-263 | RUN | restart with partial receipt and spent allowance -> break G-263: reset allowance while resuming journal. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalComponentRecovery` | `commandAllowanceAfter.ShouldBe(commandAllowanceBefore)` |
| PC-264 | RUN | original absent, no journal or foreign journal -> break G-264: return Complete on Directory.Exists=false. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalOriginalReplacement` | `absenceWithoutReceiptComplete.ShouldBeFalse()` |
| PC-265 | RUN | matching set-aside journal plus newly created original path -> break G-265: delete original pathname while resuming set-aside. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalOriginalReplacement` | `replacementBytes.ShouldBe(originalReplacementBytes)` |
| PC-266 | RUN | recorded sibling name occupied by foreign content -> break G-266: delete pre-existing sibling to make room. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalOriginalReplacement` | `foreignAsideBytes.ShouldBe(originalForeignAsideBytes)` |
| PC-267 | RUN | default runtime/dispatcher with Execute=true equivalent fixture settings -> break G-267: auto-construct guarded remover in dispatcher. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_TransportDefaultInert` | `defaultRemovalEffects.ShouldBe(0)` |
| PC-268 | RUN | capability query and registration on unconfigured then configured runtime -> break G-268: advertise guarded capability unconditionally. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_TransportDefaultInert` | `defaultAdvertisesGuarded.ShouldBeFalse()` |
| PC-269 | RUN | one missing binding payload sent via real guarded frame -> break G-269: default malformed guarded body into a valid command. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_TransportRequiredPayload` | `malformedDispatchAccepted.ShouldBeFalse()` |
| PC-270 | RUN | two distinct transport RequestIds carrying same removal operation -> break G-270: return a newly allocated receipt on second transport request. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_TransportDispatchReplay` | `replayedReceiptBytes.ShouldBe(firstReceiptBytes)` |
| PC-271 | MIR | Queued/Working/Blocked with remaining facts valid -> break G-271: omit terminal-task predicate. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionCurrentEligibility` | `remoteCommandsIssued.ShouldBe(0)` |
| PC-272 | MIR | just below/at configured settling duration using fake clock -> break G-272: omit settling-floor predicate. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionCurrentEligibility` | `beforeFloorCommands.ShouldBe(0)` |
| PC-273 | MIR | change attempt or binding after inspection before admission -> break G-273: admit stale captured attempt/binding. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionCurrentEligibility` | `staleAdmissionCommands.ShouldBe(0)` |
| PC-274 | MIR | open repair/continuation using mirror with terminal owner -> break G-274: ignore active workspace consumer. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionCurrentEligibility` | `consumerHeldCommands.ShouldBe(0)` |
| PC-275 | MIR | pending land/repair/recovery request -> break G-275: omit outstanding-recovery predicate. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionCurrentEligibility` | `recoveryDebtCommands.ShouldBe(0)` |
| PC-276 | MIR | missing/digest-changed canonical Result or path inside deletion root -> break G-276: accept existing report path without verifying external bytes. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionArtifactSurvival` | `unpreservedReportCommands.ShouldBe(0)` |
| PC-277 | MIR | deliverable only below local/mirror/set-aside root; missing external variant -> break G-277: accept File.Exists without external artifact proof. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionArtifactSurvival` | `unpreservedDeliverableCommands.ShouldBe(0)` |
| PC-278 | MIR | change Result/deliverable pointers after initial artifact verification -> break G-278: reuse old artifact digest at intent. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionArtifactSurvival` | `changedArtifactCommands.ShouldBe(0)` |
| PC-279 | MIR | pause outbound send and observe from independent DB connection -> break G-279: send before intent transaction commits. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionIntentOrdering` | `intentCommittedAtSend.ShouldBeTrue()` |
| PC-280 | MIR | intent commits, then reopen, then reconnect replay -> break G-280: revoke already-admitted exact mirror on reopen. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionIntentOrdering` | `admittedMirrorDirectoryGone.ShouldBeTrue()` |
| PC-281 | MIR | hold runner inspection/send and execute independent card writer -> break G-281: hold admission row lock across network await. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionIntentOrdering` | `cardWriteCompletedWhileIoHeld.ShouldBeTrue()` |
| PC-282 | MIR | matching IDs with different request digest -> break G-282: omit receipt digest comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `digestMismatchComplete.ShouldBeFalse()` |
| PC-283 | MIR | receipt-save fault before state update -> break G-283: mark Complete in separate earlier save. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `completeWithoutImportedReceipt.ShouldBeFalse()` |
| PC-284 | MIR | observer reloads DB when invalidation received -> break G-284: emit invalidation before saving component facts. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryCrashHandoffs` | `durableOutcomeVisibleAtEvent.ShouldBeTrue()` |
| PC-285 | MIR | hold runner mutation turn with owned earlier work, then release; ready variant -> break G-285: drop guarded work queued while mutation turn is busy. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryQueuesBusyAndReady` | `busyRecipientDirectoryGone.ShouldBeTrue()` |
| PC-286 | MIR | pre-send request-limit/write failure and queued disconnect -> break G-286: clear intent/preparation debt when transport did not send. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryCrashHandoffs` | `recoveredRecipientReceiptExists.ShouldBeTrue()` |
| PC-287 | MIR | real completed receipt then server save fails and restarts -> break G-287: erase operation identity after save failure. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryCrashHandoffs` | `postRestartOperationId.ShouldBe(originalOperationId)` |
| PC-288 | MIR | reconnect same runner name with different persistent store -> break G-288: retarget old intent to new store. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryCrashHandoffs` | `oldEndpointComplete.ShouldBeFalse()` |
| PC-289 | MIR | due-1 tick then due tick after fresh coordinator -> break G-289: ignore persisted NextAttemptAt on retry. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryCrashHandoffs` | `callsBeforeDue.ShouldBe(0)` |
| PC-290 | MIR | real Program fixture with Execute=true and populated eligible endpoints -> break G-290: register coordinator/executor into production services. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryInertServerComposition` | `productionCoordinatorResolved.ShouldBeFalse()` |
| PC-291 | MIR | invoke existing sweep on populated eligible fixture with Execute=true -> break G-291: add mirror cleanup call to WorktreeResidueSweepService. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryInertServerComposition` | `automaticMirrorCommands.ShouldBe(0)` |
| PC-292 | MIR | enumerate default endpoint data sources and probe candidate route -> break G-292: map public completed-card mirror cleanup endpoint. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryInertServerComposition` | `publicGuardedCleanupRoutes.ShouldBeEmpty()` |
| PC-293 | MIR | synthetic credential URL/process-env/secret-content canaries in failed inputs -> break G-293: log raw guarded payload in refusal detail. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `diagnosticsContainCanary.ShouldBeFalse()` |
| PC-300 | WIRE | required.CardId: remove only CardId from valid bound JSON -> break G-300: remove the CardId required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.CardId")` |
| PC-301 | WIRE | required.DoneRevisionId: remove only DoneRevisionId from valid bound JSON -> break G-301: remove the DoneRevisionId required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.DoneRevisionId")` |
| PC-302 | WIRE | required.TargetId: remove only TargetId from valid bound JSON -> break G-302: remove the TargetId required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.TargetId")` |
| PC-303 | WIRE | required.EndpointId: remove only EndpointId from valid bound JSON -> break G-303: remove the EndpointId required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.EndpointId")` |
| PC-304 | WIRE | required.TaskId: remove only TaskId from valid bound JSON -> break G-304: remove the TaskId required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.TaskId")` |
| PC-305 | WIRE | required.Attempt: remove only Attempt from valid bound JSON -> break G-305: remove the Attempt required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.Attempt")` |
| PC-306 | WIRE | required.BindingId: remove only BindingId from valid bound JSON -> break G-306: remove the BindingId required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.BindingId")` |
| PC-307 | WIRE | required.PreparationId: remove only PreparationId from valid bound JSON -> break G-307: remove the PreparationId required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.PreparationId")` |
| PC-308 | WIRE | required.RunnerId: remove only RunnerId from valid bound JSON -> break G-308: remove the RunnerId required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.RunnerId")` |
| PC-309 | WIRE | required.RunnerStoreId: remove only RunnerStoreId from valid bound JSON -> break G-309: remove the RunnerStoreId required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.RunnerStoreId")` |
| PC-310 | WIRE | required.RepositoryIdentity: remove only RepositoryIdentity from valid bound JSON -> break G-310: remove the RepositoryIdentity required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.RepositoryIdentity")` |
| PC-311 | WIRE | required.CommonDirectory: remove only CommonDirectory from valid bound JSON -> break G-311: remove the CommonDirectory required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.CommonDirectory")` |
| PC-312 | WIRE | required.GitDirectory: remove only GitDirectory from valid bound JSON -> break G-312: remove the GitDirectory required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.GitDirectory")` |
| PC-313 | WIRE | required.WorktreePath: remove only WorktreePath from valid bound JSON -> break G-313: remove the WorktreePath required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.WorktreePath")` |
| PC-314 | WIRE | required.SourceFullRef: remove only SourceFullRef from valid bound JSON -> break G-314: remove the SourceFullRef required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.SourceFullRef")` |
| PC-315 | WIRE | required.CreationBaselineSha: remove only CreationBaselineSha from valid bound JSON -> break G-315: remove the CreationBaselineSha required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.CreationBaselineSha")` |
| PC-316 | WIRE | required.OperationId: remove only OperationId from valid bound JSON -> break G-316: remove the OperationId required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.OperationId")` |
| PC-317 | WIRE | required.ExpectedHeadSha: remove only ExpectedHeadSha from valid bound JSON -> break G-317: remove the ExpectedHeadSha required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.ExpectedHeadSha")` |
| PC-318 | WIRE | required.DestinationFingerprint: remove only DestinationFingerprint from valid bound JSON -> break G-318: remove the DestinationFingerprint required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.DestinationFingerprint")` |
| PC-319 | WIRE | required.DestinationRef: remove only DestinationRef from valid bound JSON -> break G-319: remove the DestinationRef required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.DestinationRef")` |
| PC-320 | WIRE | required.ObservedTargetSha: remove only ObservedTargetSha from valid bound JSON -> break G-320: remove the ObservedTargetSha required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.ObservedTargetSha")` |
| PC-321 | WIRE | required.ProofKind: remove only ProofKind from valid bound JSON -> break G-321: remove the ProofKind required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.ProofKind")` |
| PC-322 | WIRE | required.ProofIdentity: remove only ProofIdentity from valid bound JSON -> break G-322: remove the ProofIdentity required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.ProofIdentity")` |
| PC-323 | WIRE | required.PreservedSourceSha: remove only PreservedSourceSha from valid bound JSON -> break G-323: remove the PreservedSourceSha required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.PreservedSourceSha")` |
| PC-324 | WIRE | required.ReportDigest: remove only ReportDigest from valid bound JSON -> break G-324: remove the ReportDigest required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.ReportDigest")` |
| PC-325 | WIRE | required.ArtifactProofDigest: remove only ArtifactProofDigest from valid bound JSON -> break G-325: remove the ArtifactProofDigest required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.ArtifactProofDigest")` |
| PC-326 | WIRE | required.DisposalPolicyDigest: remove only DisposalPolicyDigest from valid bound JSON -> break G-326: remove the DisposalPolicyDigest required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.DisposalPolicyDigest")` |
| PC-327 | WIRE | required.Disposition: remove only Disposition from valid bound JSON -> break G-327: remove the Disposition required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.Disposition")` |
| PC-340 | WIRE | digest.SchemaVersion: replace only SchemaVersion with a second valid value -> break G-340: exclude SchemaVersion from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.SchemaVersion")` |
| PC-341 | WIRE | digest.CardId: replace only CardId with a second valid value -> break G-341: exclude CardId from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.CardId")` |
| PC-342 | WIRE | digest.DoneRevisionId: replace only DoneRevisionId with a second valid value -> break G-342: exclude DoneRevisionId from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.DoneRevisionId")` |
| PC-343 | WIRE | digest.TargetId: replace only TargetId with a second valid value -> break G-343: exclude TargetId from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.TargetId")` |
| PC-344 | WIRE | digest.EndpointId: replace only EndpointId with a second valid value -> break G-344: exclude EndpointId from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.EndpointId")` |
| PC-345 | WIRE | digest.TaskId: replace only TaskId with a second valid value -> break G-345: exclude TaskId from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.TaskId")` |
| PC-346 | WIRE | digest.Attempt: replace only Attempt with a second valid value -> break G-346: exclude Attempt from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.Attempt")` |
| PC-347 | WIRE | digest.BindingId: replace only BindingId with a second valid value -> break G-347: exclude BindingId from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.BindingId")` |
| PC-348 | WIRE | digest.PreparationId: replace only PreparationId with a second valid value -> break G-348: exclude PreparationId from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.PreparationId")` |
| PC-349 | WIRE | digest.RunnerId: replace only RunnerId with a second valid value -> break G-349: exclude RunnerId from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.RunnerId")` |
| PC-350 | WIRE | digest.RunnerStoreId: replace only RunnerStoreId with a second valid value -> break G-350: exclude RunnerStoreId from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.RunnerStoreId")` |
| PC-351 | WIRE | digest.RepositoryIdentity: replace only RepositoryIdentity with a second valid value -> break G-351: exclude RepositoryIdentity from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.RepositoryIdentity")` |
| PC-352 | WIRE | digest.CommonDirectory: replace only CommonDirectory with a second valid value -> break G-352: exclude CommonDirectory from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.CommonDirectory")` |
| PC-353 | WIRE | digest.GitDirectory: replace only GitDirectory with a second valid value -> break G-353: exclude GitDirectory from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.GitDirectory")` |
| PC-354 | WIRE | digest.WorktreePath: replace only WorktreePath with a second valid value -> break G-354: exclude WorktreePath from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.WorktreePath")` |
| PC-355 | WIRE | digest.SourceFullRef: replace only SourceFullRef with a second valid value -> break G-355: exclude SourceFullRef from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.SourceFullRef")` |
| PC-356 | WIRE | digest.CreationBaselineSha: replace only CreationBaselineSha with a second valid value -> break G-356: exclude CreationBaselineSha from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.CreationBaselineSha")` |
| PC-357 | WIRE | digest.OperationId: replace only OperationId with a second valid value -> break G-357: exclude OperationId from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.OperationId")` |
| PC-358 | WIRE | digest.ExpectedHeadSha: replace only ExpectedHeadSha with a second valid value -> break G-358: exclude ExpectedHeadSha from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.ExpectedHeadSha")` |
| PC-359 | WIRE | digest.DestinationFingerprint: replace only DestinationFingerprint with a second valid value -> break G-359: exclude DestinationFingerprint from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.DestinationFingerprint")` |
| PC-360 | WIRE | digest.DestinationRef: replace only DestinationRef with a second valid value -> break G-360: exclude DestinationRef from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.DestinationRef")` |
| PC-361 | WIRE | digest.ObservedTargetSha: replace only ObservedTargetSha with a second valid value -> break G-361: exclude ObservedTargetSha from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.ObservedTargetSha")` |
| PC-362 | WIRE | digest.ProofKind: replace only ProofKind with a second valid value -> break G-362: exclude ProofKind from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.ProofKind")` |
| PC-363 | WIRE | digest.ProofIdentity: replace only ProofIdentity with a second valid value -> break G-363: exclude ProofIdentity from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.ProofIdentity")` |
| PC-364 | WIRE | digest.PreservedSourceSha: replace only PreservedSourceSha with a second valid value -> break G-364: exclude PreservedSourceSha from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.PreservedSourceSha")` |
| PC-365 | WIRE | digest.ReportDigest: replace only ReportDigest with a second valid value -> break G-365: exclude ReportDigest from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.ReportDigest")` |
| PC-366 | WIRE | digest.ArtifactProofDigest: replace only ArtifactProofDigest with a second valid value -> break G-366: exclude ArtifactProofDigest from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.ArtifactProofDigest")` |
| PC-367 | WIRE | digest.DisposalPolicyDigest: replace only DisposalPolicyDigest with a second valid value -> break G-367: exclude DisposalPolicyDigest from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.DisposalPolicyDigest")` |
| PC-368 | WIRE | digest.Disposition: replace only Disposition with a second valid value -> break G-368: exclude Disposition from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.Disposition")` |
| PC-369 | WIRE | digest.RebasedSourceSha: replace only RebasedSourceSha with a second valid value -> break G-369: exclude RebasedSourceSha from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.RebasedSourceSha")` |
| PC-370 | WIRE | digest.ArtifactPointers: replace only ArtifactPointers with a second valid value -> break G-370: exclude ArtifactPointers from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.ArtifactPointers")` |
| PC-371 | WIRE | digest.PolicyPatterns: replace only PolicyPatterns with a second valid value -> break G-371: exclude PolicyPatterns from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.PolicyPatterns")` |
| PC-380 | MIR | receipt.SchemaVersion: replace only SchemaVersion on genuine recipient receipt; missing/null/unknown arm -> break G-380: omit receipt SchemaVersion comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.SchemaVersion")` |
| PC-381 | MIR | receipt.CardId: replace only CardId on genuine recipient receipt; missing/null/unknown arm -> break G-381: omit receipt CardId comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.CardId")` |
| PC-382 | MIR | receipt.DoneRevisionId: replace only DoneRevisionId on genuine recipient receipt; missing/null/unknown arm -> break G-382: omit receipt DoneRevisionId comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.DoneRevisionId")` |
| PC-383 | MIR | receipt.TargetId: replace only TargetId on genuine recipient receipt; missing/null/unknown arm -> break G-383: omit receipt TargetId comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.TargetId")` |
| PC-384 | MIR | receipt.TaskId: replace only TaskId on genuine recipient receipt; missing/null/unknown arm -> break G-384: omit receipt TaskId comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.TaskId")` |
| PC-385 | MIR | receipt.BindingId: replace only BindingId on genuine recipient receipt; missing/null/unknown arm -> break G-385: omit receipt BindingId comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.BindingId")` |
| PC-386 | MIR | receipt.PreparationId: replace only PreparationId on genuine recipient receipt; missing/null/unknown arm -> break G-386: omit receipt PreparationId comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.PreparationId")` |
| PC-387 | MIR | receipt.RunnerId: replace only RunnerId on genuine recipient receipt; missing/null/unknown arm -> break G-387: omit receipt RunnerId comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.RunnerId")` |
| PC-388 | MIR | receipt.RepositoryIdentity: replace only RepositoryIdentity on genuine recipient receipt; missing/null/unknown arm -> break G-388: omit receipt RepositoryIdentity comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.RepositoryIdentity")` |
| PC-389 | MIR | receipt.CommonDirectory: replace only CommonDirectory on genuine recipient receipt; missing/null/unknown arm -> break G-389: omit receipt CommonDirectory comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.CommonDirectory")` |
| PC-390 | MIR | receipt.GitDirectory: replace only GitDirectory on genuine recipient receipt; missing/null/unknown arm -> break G-390: omit receipt GitDirectory comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.GitDirectory")` |
| PC-391 | MIR | receipt.WorktreePath: replace only WorktreePath on genuine recipient receipt; missing/null/unknown arm -> break G-391: omit receipt WorktreePath comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.WorktreePath")` |
| PC-392 | MIR | receipt.SourceFullRef: replace only SourceFullRef on genuine recipient receipt; missing/null/unknown arm -> break G-392: omit receipt SourceFullRef comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.SourceFullRef")` |
| PC-393 | MIR | receipt.CreationBaselineSha: replace only CreationBaselineSha on genuine recipient receipt; missing/null/unknown arm -> break G-393: omit receipt CreationBaselineSha comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.CreationBaselineSha")` |
| PC-394 | MIR | receipt.ExpectedHeadSha: replace only ExpectedHeadSha on genuine recipient receipt; missing/null/unknown arm -> break G-394: omit receipt ExpectedHeadSha comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.ExpectedHeadSha")` |
| PC-395 | MIR | receipt.PreservationProofDigest: replace only PreservationProofDigest on genuine recipient receipt; missing/null/unknown arm -> break G-395: omit receipt PreservationProofDigest comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.PreservationProofDigest")` |
| PC-396 | MIR | receipt.ArtifactProofDigest: replace only ArtifactProofDigest on genuine recipient receipt; missing/null/unknown arm -> break G-396: omit receipt ArtifactProofDigest comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.ArtifactProofDigest")` |
| PC-397 | MIR | receipt.DisposalPolicyDigest: replace only DisposalPolicyDigest on genuine recipient receipt; missing/null/unknown arm -> break G-397: omit receipt DisposalPolicyDigest comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.DisposalPolicyDigest")` |
| PC-398 | MIR | receipt.Disposition: replace only Disposition on genuine recipient receipt; missing/null/unknown arm -> break G-398: omit receipt Disposition comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.Disposition")` |
| PC-399 | MIR | receipt.SetAsideIdentity: replace only SetAsideIdentity on genuine recipient receipt; missing/null/unknown arm -> break G-399: omit receipt SetAsideIdentity comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.SetAsideIdentity")` |
| PC-400 | LEG-R | forward child commit; expected published tip -> break G-400: omit push invocation. | `/*/*/RunnerWorkspaceServiceTests/Publish_pushes_only_own_fast_forward_branch` | `originTip.ShouldBe(tip)` |
| PC-401 | LEG-R | diverged tip -> break G-401: classify diverged as descends. | `/*/*/RunnerWorkspaceServiceTests/Publish_refuses_a_rebased_tip_without_pushing` | `answer.Pushed.ShouldBeFalse()` |
| PC-402 | LEG-R | equal tip with untracked source -> break G-402: hardcode Dirty=false. | `/*/*/RunnerWorkspaceServiceTests/Publish_equal_tip_is_not_pushed_and_reports_dirty_tree` | `answer.Dirty.ShouldBeTrue()` |
| PC-403 | LEG-R | foreign path and branch -> break G-403: remove target validation before Git. | `/*/*/RunnerWorkspaceServiceTests/Publish_refuses_foreign_path_and_branch_before_git` | `starts.ShouldBeEmpty()` |
| PC-404 | LEG-R | detached worktree -> break G-404: accept detached symbolic head. | `/*/*/RunnerWorkspaceServiceTests/Publish_refuses_detached_head` | `answer.Refusal.ShouldBe("not_on_branch")` |
| PC-405 | LEG-R | MERGE_HEAD exists -> break G-405: omit sequencer check. | `/*/*/RunnerWorkspaceServiceTests/Publish_refuses_an_active_sequencer_before_push` | `answer.Refusal.ShouldBe("sequencer_active")` |
| PC-406 | LEG-R | non-FF remote rejection -> break G-406: report push as successful regardless of exit. | `/*/*/RunnerWorkspaceServiceTests/Publish_reports_rejected_non_fast_forward_without_exposing_origin` | `answer.Pushed.ShouldBeFalse()` |
| PC-407 | LEG-R | ancestor tip behind remote -> break G-407: map behind to equal. | `/*/*/RunnerWorkspaceServiceTests/Publish_classifies_a_tip_behind_the_advertised_remote` | `answer.Relation.ShouldBe("behind")` |
| PC-408 | LEG-R | unpublished commit without Force, then legacy Force positive -> break G-408: omit legacy unpublished-work guard. | `/*/*/RunnerWorkspaceServiceTests/Remove_refuses_committed_unpublished_tip_unless_forced` | `Should.ThrowAsync<PhoneHomeAdmissionException>(productionCall)` |
| PC-409 | LEG-R | equal published SHA -> break G-409: refuse exact published tip. | `/*/*/RunnerWorkspaceServiceTests/Remove_allows_a_published_tip` | `removed.Removed.ShouldBeTrue()` |
| PC-410 | LEG-R | mirror behind published SHA -> break G-410: require equality in legacy removal. | `/*/*/RunnerWorkspaceServiceTests/Remove_allows_a_tip_which_is_ancestor_of_published_sha` | `removed.Removed.ShouldBeTrue()` |
| PC-411 | LEG-R | secondary clone -> break G-411: run remove in primary checkout. | `/*/*/RunnerWorkspaceServiceTests/Mirror_of_a_second_repository_clones_beside_the_primary_and_removes_through_its_own_checkout` | `starts.ShouldContain(matchingSecondaryRemove)` |
| PC-412 | LEG-R | unadmitted repository -> break G-412: skip clone-source admission. | `/*/*/RunnerWorkspaceServiceTests/Mirror_refuses_a_repository_outside_the_allowed_clone_sources` | `starts.ShouldBeEmpty()` |
| PC-413 | LEG-R | synthetic credential URL -> break G-413: echo unredacted invalid request URL in exception. | `/*/*/RunnerWorkspaceServiceTests/Mirror_refuses_credentialed_request_without_echoing_credentials` | `refused.Message.ShouldNotContain(secret)` |
| PC-414 | LEG-R | secondary path has other origin -> break G-414: skip origin identity check. | `/*/*/RunnerWorkspaceServiceTests/Mirror_refuses_an_existing_checkout_whose_origin_is_another_repository` | `Should.ThrowAsync<PhoneHomeAdmissionException>(productionCall)` |
| PC-415 | LEG-R | credentialed existing origin -> break G-415: include raw origin in mismatch text. | `/*/*/RunnerWorkspaceServiceTests/Mirror_mismatch_does_not_echo_credentialed_origin` | `refused.Message.ShouldNotContain(secret)` |
| PC-416 | LEG-R | failing dry-run probe -> break G-416: ignore failed push probe. | `/*/*/RunnerWorkspaceServiceTests/Mirror_refuses_a_secondary_repository_the_push_credential_cannot_push_to` | `Should.ThrowAsync<PhoneHomeAdmissionException>(productionCall)` |
| PC-417 | LEG-R | successful dry-run -> break G-417: remove --dry-run from push probe. | `/*/*/RunnerWorkspaceServiceTests/Mirror_accepts_a_secondary_repository_when_push_dry_run_succeeds` | `start.ArgumentList.Take(6).ShouldBe(expectedProbeArgv)` |
| PC-418 | LEG-R | normal mirror -> break G-418: create detached mirror. | `/*/*/RunnerWorkspaceServiceTests/Mirror_creates_worktree_on_branch_at_sha` | `actualBranch.ShouldBe(Scratch.Branch)` |
| PC-419 | LEG-R | known but wrong fetched SHA -> break G-419: omit fetched-tip equality. | `/*/*/RunnerWorkspaceServiceTests/Mirror_refuses_sha_mismatch` | `Should.ThrowAsync<PhoneHomeAdmissionException>(productionCall)` |
| PC-420 | LEG-R | absent and populated destination variants -> break G-420: refuse absent repository rather than provisioning. | `/*/*/RunnerWorkspaceServiceTests/Mirror_clones_repository_when_absent` | `mirrorHead.ShouldBe(scratch.Sha)` |
| PC-421 | LEG-R | child-start exception and caller cancellation -> break G-421: let start IOException escape without typed mapping. | `/*/*/RunnerWorkspaceServiceTests/Mirror_failure_is_admission_error_not_crash` | `refused.Code.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget)` |
| PC-422 | LEG-R | invalid name with valid branch/SHA -> break G-422: skip mirror-name validation. | `/*/*/RunnerWorkspaceServiceTests/Mirror_refuses_a_name_or_sha_it_did_not_shape` | `Should.ThrowAsync<PhoneHomeAdmissionException>(productionCall)` |
| PC-423 | LEG-R | untracked unique source -> break G-423: omit dirty-status guard. | `/*/*/RunnerWorkspaceServiceTests/Remove_refuses_dirty_tree` | `Should.ThrowAsync<PhoneHomeAdmissionException>(productionCall)` |
| PC-424 | LEG-R | root/outside path -> break G-424: skip managed-root validation. | `/*/*/RunnerWorkspaceServiceTests/Remove_refuses_a_path_outside_the_worktree_root` | `Should.ThrowAsync<PhoneHomeAdmissionException>(productionCall)` |
| PC-425 | LEG-R | /etc removal -> break G-425: allow foreign Cwd into workspace operation. | `/*/*/PhoneHomeCommandDispatcherTests/Workspace_ops_are_admitted_only_under_allowed_cwd` | `removeOutside.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget)` |
| PC-426 | LEG-R | foreign repository request -> break G-426: skip repository admission. | `/*/*/PhoneHomeCommandDispatcherTests/Workspace_mirror_naming_an_unadmitted_repository_is_refused_before_any_git_runs` | `reply.ErrorCode.ShouldBe(PhoneHomeProblemTypes.RepositoryNotAdmitted)` |
| PC-427 | LEG-R | foreign publish path -> break G-427: skip allowed-Cwd routing guard. | `/*/*/PhoneHomeCommandDispatcherTests/Workspace_publish_route_rejects_a_foreign_target_before_git` | `reply.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget)` |
| PC-428 | LEG-S | SSH/HTTPS origin normalization -> break G-428: drop repository identity from successful push result. | `/*/*/RemoteWorktreeMirrorTests/Push_reads_the_desktop_origin_and_the_mirror_request_carries_its_https_identity` | `push.Repository.ShouldBe(expectedHttpsIdentity)` |
| PC-429 | LEG-S | remote lookup fails -> break G-429: continue push on failed origin read. | `/*/*/RemoteWorktreeMirrorTests/Push_refuses_when_the_desktop_origin_cannot_be_read` | `push.Pushed.ShouldBeFalse()` |
| PC-430 | LEG-S | baseline ancestor of observed SHA -> break G-430: return Synchronized without merge. | `/*/*/RemoteWorktreeMirrorTests/Sync_fast_forwards_desktop_worktree` | `git.Commands.ShouldContain(expectedFfCommand)` |
| PC-431 | LEG-S | diverged desktop -> break G-431: permit merge after failed ancestry check. | `/*/*/RemoteWorktreeMirrorTests/Non_fast_forward_sync_is_refused` | `result.State.ShouldBe(RemoteSettlementSyncState.Refused)` |
| PC-432 | LEG-S | dirty tracked desktop -> break G-432: omit dirty check. | `/*/*/RemoteWorktreeMirrorTests/Dirty_desktop_tree_is_never_fast_forwarded_over` | `progress.Observations.ShouldBe(0)` |
| PC-433 | LEG-S | remote push exit128 -> break G-433: mark push successful regardless of exit. | `/*/*/RemoteWorktreeMirrorTests/Push_failure_reports_and_does_not_invent_a_sha` | `result.Pushed.ShouldBeFalse()` |
| PC-434 | LEG-S | successful prep push -> break G-434: omit configured push options/task tag. | `/*/*/RemoteWorktreeMirrorTests/Push_reports_the_head_it_pushed` | `git.LastPushOptions.ShouldNotBeNull()` |
| PC-435 | LEG-S | known full task id -> break G-435: use nine characters in mirror name. | `/*/*/RemoteWorktreeMirrorTests/Mirror_name_is_the_dispatchers_own_short_form` | `mirrorName.ShouldBe("task-deadbeef")` |
| PC-500 | RUN | valid complete payload with one stale digest -> break G-500: omit recomputed digest check before journal admission. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_TransportRequiredPayload` | `payloadDigestAccepted.ShouldBeFalse()` |
| PC-501 | RUN | commit unique change after initial read -> break G-501: omit final HEAD equality. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateHeadRefused.ShouldBeTrue()` |
| PC-502 | RUN | switch branch at same HEAD after initial read -> break G-502: omit final symbolic-ref equality. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateBranchRefused.ShouldBeTrue()` |
| PC-503 | RUN | late nonignored untracked file -> break G-503: omit final untracked status. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateUntrackedBytes.ShouldBe(originalBytes)` |
| PC-504 | RUN | durable intent changed after initial read -> break G-504: skip final journal identity read. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateIntentRefused.ShouldBeTrue()` |
| PC-505 | RUN | nested registered worktree without a nested repository sentinel masking the check -> break G-505: omit nested-registration check. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerIgnoredPolicy` | `nestedRegistrationPreserved.ShouldBeTrue()` |
| PC-506 | MIR | matching report bytes/digest only inside mirror or set-aside -> break G-506: omit report-root exclusion. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionArtifactSurvival` | `internalReportCommands.ShouldBe(0)` |
| PC-507 | MIR | external file exists with truncated Result -> break G-507: accept external report based on existence only. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionArtifactSurvival` | `truncatedReportCommands.ShouldBe(0)` |
| PC-508 | MIR | same-looking desktop path exists but runner artifact does not -> break G-508: use desktop File.Exists for remote artifact proof. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionArtifactSurvival` | `guardedRemovalCommands.ShouldBe(0)` |
| PC-509 | MIR | genuine receipt registration gone, directory remains -> break G-509: treat registration success as complete. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_AckIsNotRemoval` | `mirrorComplete.ShouldBeFalse("directory-remains")` |
| PC-510 | MIR | genuine receipt directory gone, registration remains -> break G-510: treat directory success as complete. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_AckIsNotRemoval` | `mirrorComplete.ShouldBeFalse("registration-remains")` |
| PC-511 | MIR | task/path/proof/policy changes after persisted intent -> break G-511: rebuild replay command from current task. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryCrashHandoffs` | `replayedRequestBytes.ShouldBe(firstRequestBytes)` |
| PC-512 | MIR | move wall clock backward with unchanged monotonic deadline budget -> break G-512: compute timeout from wall-clock deadline. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryCrashHandoffs` | `elapsedBudgetExceeded.ShouldBeFalse()` |
| PC-513 | MIR | default options from production configuration -> break G-513: set default WorktreeResidueSettings.Execute=true. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryInertServerComposition` | `defaultExecute.ShouldBeFalse()` |
| PC-514 | RUN | replace admin directory after initial read preserving other facts -> break G-514: omit final admin equality. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateAdminRefused.ShouldBeTrue()` |
| PC-515 | RUN | common identity changes after initial read -> break G-515: omit final common equality. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateCommonRefused.ShouldBeTrue()` |
| PC-516 | RUN | late worktree lock and separate late nested registration -> break G-516: omit final structural inventory read. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateStructuralRefused.ShouldBeTrue()` |
| PC-517 | RUN | pause at reply writer and independently reopen runner journal -> break G-517: send success before durable receipt commit. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_TransportDispatchReplay` | `receiptVisibleBeforeReply.ShouldBeTrue()` |
| PC-520 | CAP | capture.TaskId: one mismatch/missing member in otherwise valid runner creation receipt -> break G-520: omit returned TaskId validation. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `invalidBindingPersisted.ShouldBeFalse("capture.TaskId")` |
| PC-521 | CAP | capture.Attempt: one mismatch/missing member in otherwise valid runner creation receipt -> break G-521: omit returned Attempt validation. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `invalidBindingPersisted.ShouldBeFalse("capture.Attempt")` |
| PC-522 | CAP | capture.PreparationId: one mismatch/missing member in otherwise valid runner creation receipt -> break G-522: omit returned PreparationId validation. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `invalidBindingPersisted.ShouldBeFalse("capture.PreparationId")` |
| PC-523 | CAP | capture.RunnerId: one mismatch/missing member in otherwise valid runner creation receipt -> break G-523: omit returned RunnerId validation. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `invalidBindingPersisted.ShouldBeFalse("capture.RunnerId")` |
| PC-524 | CAP | capture.RunnerStoreId: one mismatch/missing member in otherwise valid runner creation receipt -> break G-524: omit returned RunnerStoreId validation. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `invalidBindingPersisted.ShouldBeFalse("capture.RunnerStoreId")` |
| PC-525 | CAP | capture.BindingId: empty ID, or a changed ID on exact repeated preparation receipt; a first nonempty runner-issued ID is accepted -> break G-525: omit returned BindingId validation. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `invalidBindingPersisted.ShouldBeFalse("capture.BindingId")` |
| PC-526 | CAP | capture.RepositoryIdentity: one mismatch/missing member in otherwise valid runner creation receipt -> break G-526: omit returned RepositoryIdentity validation. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `invalidBindingPersisted.ShouldBeFalse("capture.RepositoryIdentity")` |
| PC-527 | CAP | capture.WorktreePath: empty path, or changed path on repeated known binding; no desktop filesystem probe -> break G-527: omit returned WorktreePath validation. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `invalidBindingPersisted.ShouldBeFalse("capture.WorktreePath")` |
| PC-528 | CAP | capture.SourceFullRef: one mismatch/missing member in otherwise valid runner creation receipt -> break G-528: omit returned SourceFullRef validation. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `invalidBindingPersisted.ShouldBeFalse("capture.SourceFullRef")` |
| PC-529 | CAP | capture.CreationBaselineSha: one mismatch/missing member in otherwise valid runner creation receipt -> break G-529: omit returned CreationBaselineSha validation. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `invalidBindingPersisted.ShouldBeFalse("capture.CreationBaselineSha")` |
| PC-530 | RB | A writes creation intent and is cancelled; B with the same short id creates the mirror; A retries -> break G-530: adopt a directory when only presence and HEAD match. | `/*/*/RunnerWorkspaceBindingTests/C1032_ForeignOwnerAdoptionRefused` | `foreignAdopted.ShouldBeFalse()` |
| PC-531 | RB | exact replay after ordinary worktree removal -> break G-531: return the completed binding when that generation's directory is absent. | `/*/*/RunnerWorkspaceBindingTests/C1032_AbsentMirrorReplayRefused` | `staleSuccess.ShouldBeFalse()` |
| PC-532 | RB | request names another store or runner -> break G-532: copy the request store into a completed binding. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreMismatchRefused` | `wrongStoreCompleted.ShouldBeFalse()` |
| PC-533 | CAP | store replaced between preparation and retry -> break G-533: reuse the old-store preparation. | `/*/*/RemoteWorkspacePreparerTests/C1032_StoreReplacementKeepsDebt` | `reusedOldStore.ShouldBeFalse()` |
| PC-534 | JOIN | idle registered recipient -> break G-534: skip the owner-stamp write and still return success. | `/*/*/PhoneHomeMirrorJoinTests/C1032_JoinIdleRecipient` | `stampMatches.ShouldBeTrue()` |
| PC-535 | JOIN | second mirror while the mutation lock is held -> break G-535: skip MutationLock.WaitAsync. | `/*/*/PhoneHomeMirrorJoinTests/C1032_JoinBusyRecipient` | `partialBindingLeft.ShouldBeFalse()` |
| PC-536 | JOIN | server drops the waiter after enqueue -> break G-536: mint a new PreparationId on retry. | `/*/*/PhoneHomeMirrorJoinTests/C1032_JoinCrashRetry` | `recoveredPreparationId.ShouldBe(firstPreparationId)` |
| PC-537 | JOIN | colliding short id through the live queue -> break G-537: adopt when the owner stamp does not match. | `/*/*/PhoneHomeMirrorJoinTests/C1032_JoinForeignOwner` | `foreignAdopted.ShouldBeFalse()` |
| PC-538 | JOIN | exact replay after removal through the live queue -> break G-538: return success when the generation directory is absent. | `/*/*/PhoneHomeMirrorJoinTests/C1032_JoinStaleBinding` | `staleCompleted.ShouldBeFalse()` |
| PC-539 | JOIN | registration store file replaced before the request -> break G-539: bind the requested store and skip the mismatch throw. | `/*/*/PhoneHomeMirrorJoinTests/C1032_JoinStoreMismatch` | `wrongStoreCompleted.ShouldBeFalse()` |
| PC-540 | RB | missing registration file, request names the nonempty custody guid -> break G-540: substitute RunnerStoreId and accept the bound mirror. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused*` | `acceptedCustody.ShouldBeFalse("missing")` |
| PC-541 | RB | empty registration file, request names the custody guid -> break G-541: substitute RunnerStoreId and accept the bound mirror. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused*` | `acceptedCustody.ShouldBeFalse("empty")` |
| PC-542 | RB | whitespace registration file, request names the custody guid -> break G-542: substitute RunnerStoreId and accept the bound mirror. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused*` | `acceptedCustody.ShouldBeFalse("whitespace")` |
| PC-543 | RB | garbled registration file, request names the custody guid -> break G-543: substitute RunnerStoreId and accept the bound mirror. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused*` | `acceptedCustody.ShouldBeFalse("garbled")` |
| PC-544 | RB | wrong-length registration guid, request names the custody guid -> break G-544: substitute RunnerStoreId and accept the bound mirror. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused*` | `acceptedCustody.ShouldBeFalse("wrong-length")` |
| PC-545 | RB | directory at the registration path, request names the custody guid -> break G-545: substitute RunnerStoreId and accept the bound mirror. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused*` | `acceptedCustody.ShouldBeFalse("directory")` |
| PC-546 | RB | mode-denied registration file, request names the custody guid -> break G-546: substitute RunnerStoreId and accept the bound mirror. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused*` | `acceptedCustody.ShouldBeFalse("unreadable")` |
| PC-547 | RB | readable registration guid differs and the request names custody -> break G-547: compare the request with RunnerStoreId instead of the file. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused*` | `custodyAccepted.ShouldBeFalse()` |
| PC-548 | RB | readable registration guid matches the request and differs from custody -> break G-548: bind RunnerStoreId and refuse the registration guid. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused*` | `registeredStoreCompleted.ShouldBeTrue()` |
| PC-549 | RB | unbound request and missing registration file -> break G-549: require a parsed registration guid before the legacy mirror. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused*` | `legacyMirrorExists.ShouldBeTrue()` |
| PC-550 | RB | omitted binding and missing registration file -> break G-550: fabricate a binding row from the unbound mirror path. | `/*/*/RunnerWorkspaceBindingTests/C1032_LegacyUnboundPathOnlyContract*` | `Directory.Exists(ledgerRoot).ShouldBeFalse()` |

### Out of scope

- Parent S4: automatic scheduler/wakeup/reconciliation registration, Hangfire and
  land-queue delivery activation, operator API/CLI outcome surfaces, action budget,
  historical cohort cutoff and canary. Their parent busy/ready and crash-handoff
  tests remain required before activation. Explicit S8 fixture calls do not
  qualify a production schedule.
- Parent S5: native Windows sharing/locked-file, junction/reparse and supported
  backend process acceptance. Preserve parent frozen CP-10 (native local),
  CP-11 (complete U roster), CP-12 (local/mirror compatibility) at the same
  implementation SHA. Re-freeze their rosters to include the new binding/fence/
  recovery methods requiring native qualification; a portable result is not
  their substitute. No Windows success or platform skip is claimed here.
- SourceLanding custody/cleanup, external verification evidence, canonical report
  stores, origin/local branch deletion by the mirror operation, slot reset,
  historical ownership adoption, session stopping/parking, provider qualification,
  real runner paths and deployment settings. The new refusal fixtures protect
  these boundaries without transferring authority from their owners.
- No new session input or outbound notification. Existing launch admission is
  checked as compatibility only; it is not a claim of prompt delivery.
- Full Unit, unrelated namespaces, browser E2E and Antiphon.Agents.Pty.Tests are
  outside this ordinary scope. No change to checkpoint tooling or the independent
  Checkpoints namespace census.
- Plan collision exclusions remain: tools/Antiphon.Checkpoints, AgentSessionService,
  AgentService, AgentControlService, c590-remote.sh and RemoteScriptContractTests.
  Program files, TaskWorktreeRetirementService, WorktreeResidueSweepService and
  server no-follow helpers are inspected dependencies, not implementation edits.

### Checkpoints

This is the sole importer-visible closed ordinary list in **this** artifact.
Use this path, not the provisional table in the fix plan. Each row has one exact
filter. Round-2 repair shares CP-3's S2 runner build with CP-17/18/19/22/23/24
and CP-4's S2 server build with CP-20/21. CP-26/27 reuse CP-25's repair build.
All reuse is within the same After group, committed SHA and project. The old
24-row roster remains 130 executions; repair CP-25..27 adds 46 results.
CP-21 is Linux-only phone-home join; CP-22 has ten registration-store results.
Read each fresh TRX's executed class/method roster as well as its count:
the tool's Min is a floor and its derived Expect tokens alone do not enforce
the exact roster. Prefix rows select only the enumerated C1032 methods above;
an extra or missing result requires reconciliation rather than an inflated count.

S1's CP-1 and CP-2 method alternatives end in `*`. Microsoft Testing Platform
matches an OR of bare method names to zero tests; one wildcard in the group
is what makes the enumerated methods run (TUnit issue 4580). No other
C1032 method shares those names, so the star does not add a case.

Select explicit `--rows` after commit/push:
S1=CP-1,CP-2; S2=CP-3,CP-4,CP-17,CP-18,CP-19,CP-20,CP-21,CP-22,CP-23,CP-24; S3=CP-5; S4=CP-6;
S5=CP-7,CP-8,CP-9,CP-10; S6=CP-11,CP-12;
S7=CP-13,CP-14; S8=CP-15,CP-16.
This moves parent destructive witnesses behind S5 implementation and adds
separate S3/S4 reachability checks; it does not change the fix slices.
Each group fits one bounded ordinary selection; do not dispatch all eight
slices as a single implementation assignment.

Use the repository checkpoint tool's `run --plan <this-file> --rows <selection>
--expected-source-sha <committed-sha>`. If bootstrap is needed, build the tool
under `scripts/build-slot.ps1` to its own alternate output, then release that
lease before invoking the built tool DLL with `dotnet exec`. The setup allowance
below includes that bootstrap. The checkpoint drivers take their own slots;
do not hold a wrapper lease around the running checkpoint tool. Await the
foreground command and any exit-75 wait until the run finishes. No unleased
retry after slot timeout. Preserve unedited CHECKPOINT lines, counts, source
receipt and build provenance in the stored report; generated payloads stay
ignored. Clean only task-owned alternate outputs through the checkpoint owner.

CLI-generated EF migration authoring is still required by S1. Its necessary
design-time generation build is a declared authoring exception, not another
verification selection: use the documented EF CLI and build-slot/isolated-output
procedure, record its SHA and reason, and budget the separate three minutes
below. Do not hand-author a migration or replace the schema CP with migration
generation. Additional build/test work needs a stated failure/change reason.

CP-3, CP-4, CP-17, CP-18, CP-19, CP-20, CP-23 and CP-24 need no platform pin. CP-22 requires Linux for its ten-result, zero-skip certification.
CP-21 requires Linux. Its busy and crash fixtures run `/usr/bin/mkfifo` and
`/bin/sh` and call `File.SetUnixFileMode`. Those methods skip on Windows with
that reason; a Windows skip is not acceptance and does not certify the row.
On Linux the six methods execute and must not skip, so a Linux repeat of CP-21
stays valid. Do not replace the fixtures with a portable barrier. CP-22's
mode-bit denial case also skips off Linux; its other identity cases run on
either platform. Resolve runner defaults and catalogue again at dispatch.
Native parent acceptance alone requests Windows.
Expect means the full enumerated roster, zero failures and zero skipped tests
on the platform the row requires;
loops, assertions and PC variants do not raise Min.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1032-schema/` | binding-schema | `/*/*/CardWorktreeMirrorBindingTests/(C1032_SchemaUniqueBindings*)\|(C1032_SchemaLegacyUpgrade*)\|(C1032_SchemaRequestAndReceiptPersistence*)` | V-1, V-2 | all 3 listed; 0 failed/skipped | 3 | 5 | true |
| CP-2 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-contract/` | wire-contract | `/*/*/CompletedCardWorkspaceContractTests/(C1032_WireRoundTrip*)\|(C1032_WireRequiredFieldsAndVersion*)\|(C1032_WireDigestFieldCoverage*)` | V-2 | all 3 listed; 0 failed/skipped | 3 | 4 | true |
| CP-3 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-binding/` | runner-binding | `/*/*/RunnerWorkspaceBindingTests/C1032_Binding*` | V-1, R-2 | all 5 listed; 0 failed/skipped | 5 | 5 | true |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1032-capture/` | server-binding | `/*/*/(CardWorktreeMirrorBindingTests*)\|(RemoteWorkspacePreparerTests*)/C1032_Capture*` | V-1 | all 4 listed; 0 failed/skipped | 4 | 5 | true |
| CP-5 | S3 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-fence/` | use-fence | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_Fence*` | V-4 | all 6 listed; 0 failed/skipped | 6 | 5 | true |
| CP-6 | S4 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-inspect/` | bound-inspection | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_Inspection*` | V-3 | all 46 results (3 original, 42 shape arguments, 1 Git-call method); 0 failed/skipped | 46 | 5 | true |
| CP-7 | S5 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-owners/` | removal-owners | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerProcessOwnerHolds)\|(C1017_RunnerStartFence)\|(C1017_RunnerReadoptionFence)\|(C1017_RunnerIntentBeforeIo)\|(C1017_UnknownProcessOwnershipHolds)` | V-4, V-5, R-3 | all 5 listed; 0 failed/skipped | 5 | 5 | true |
| CP-8 | S5 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-identity/` | removal-identity | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerTaskIdentity)\|(C1017_RunnerAttemptIdentity)\|(C1017_RunnerStoreIdentity)\|(C1017_RunnerRepositoryIdentity)\|(C1017_RunnerPathIdentity)\|(C1017_RunnerBranchIdentity)\|(C1017_RunnerHeadIdentity)\|(C1017_RunnerPreservationProof)\|(C1017_RunnerSourceLandingExcluded)` | V-3, V-5, R-3 | all 9 listed; 0 failed/skipped | 9 | 5 | true |
| CP-9 | S5 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-content/` | removal-content | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerDirtySource)\|(C1017_RunnerIgnoredPolicy)\|(C1017_RunnerFreshFinalInspection)\|(C1017_RunnerNoForceBypass)` | V-3, V-5, R-3 | all 4 listed; 0 failed/skipped | 4 | 5 | true |
| CP-10 | S5 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-components/` | removal-components | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerNoFollow)\|(C1017_RunnerDigestReplay)\|(C1032_Removal*)` | V-5, R-3 | all 5 listed; 0 failed/skipped | 5 | 6 | true |
| CP-11 | S6 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-dispatch/` | guarded-dispatch | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_GuardedOperationIsDistinct)\|(C1032_Transport*)` | V-2, V-5, R-2, R-3 | all 4 listed; 0 failed/skipped | 4 | 5 | true |
| CP-12 | S6 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-legacy/` | runner-legacy | `/*/*/(RunnerWorkspaceServiceTests*)\|(PhoneHomeCommandDispatcherTests*)/(Mirror_*)\|(Publish_*)\|(Remove_*)\|(Workspace_*)` | R-1 | all 28 listed; 0 failed/skipped | 28 | 6 | true |
| CP-13 | S7 | `tests/Antiphon.Tests -> bin-c1032-admission/` | mirror-admission | `/*/*/CardWorktreeMirrorCleanupTests/(C1017_MirrorBehindPushedTip)\|(C1017_ReopenBetweenEndpoints)\|(C1017_MultipleMirrorsRemainDistinct)\|(C1032_Admission*)` | V-1, V-3, V-6, R-3 | all 7 listed including C1032_AdmissionBoundRebaseProof; 0 failed/skipped | 7 | 6 | true |
| CP-14 | S7 | `tests/Antiphon.Tests -> bin-c1032-receipt/` | mirror-receipt | `/*/*/CardWorktreeMirrorCleanupTests/(C1017_ReceiptMustMatchOperation)\|(C1017_ReceiptMustMatchEndpoint)\|(C1017_ReceiptMustMatchStore)\|(C1017_ReceiptMustMatchTaskAttempt)\|(C1017_AckIsNotRemoval)\|(C1017_UnsupportedNeverFallsBack)\|(C1032_Receipt*)` | V-2, V-6, R-3 | all 7 listed; 0 failed/skipped | 7 | 6 | true |
| CP-15 | S8 | `tests/Antiphon.Tests -> bin-c1032-recovery/` | mirror-recovery | `/*/*/CardWorktreeMirrorCleanupTests/(C1017_MirrorCompletesIndependently)\|(C1017_LocalProgressSurvivesOffline)\|(C1017_LostAckReusesOperation)\|(C1032_Recovery*)` | V-1, V-2, V-5, V-6, R-2, R-3 | all 6 listed; 0 failed/skipped | 6 | 7 | true |
| CP-16 | S8 | `tests/Antiphon.Tests -> bin-c1032-sync/` | legacy-sync | `/*/*/RemoteWorktreeMirrorTests/*` | R-1 | all 8 listed; 0 failed/skipped | 8 | 5 | true |
| CP-17 | S2 | CP-3 | foreign-owner | `/*/*/RunnerWorkspaceBindingTests/C1032_ForeignOwnerAdoptionRefused` | V-1 | all 1 listed; 0 failed/skipped | 1 | 1 | true |
| CP-18 | S2 | CP-3 | absent-replay | `/*/*/RunnerWorkspaceBindingTests/C1032_AbsentMirrorReplayRefused` | V-1 | all 1 listed; 0 failed/skipped | 1 | 1 | true |
| CP-19 | S2 | CP-3 | store-mismatch | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreMismatch*` | V-1 | all 1 listed; 0 failed/skipped | 1 | 1 | true |
| CP-20 | S2 | CP-4 | store-replacement | `/*/*/RemoteWorkspacePreparerTests/C1032_StoreReplacement*` | V-1 | all 1 listed; 0 failed/skipped | 1 | 1 | true |
| CP-21 | S2 | CP-4 | mirror-join | `/*/*/PhoneHomeMirrorJoinTests/C1032_Join*` | V-1 | all 6 listed; 0 failed/skipped | 6 | 3 | true |
| CP-22 | S2 | CP-3 | store-identity | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentity*` | V-1 | all 10 listed; 0 failed/skipped | 10 | 1 | true |
| CP-23 | S2 | CP-3 | legacy-unbound-contract | `/*/*/RunnerWorkspaceBindingTests/C1032_LegacyUnboundPathOnlyContract*` | R-2 | all 1 listed; 0 failed/skipped | 1 | 1 | true |
| CP-24 | S2 | CP-3 | workspace-compatibility | `/*/*/PhoneHomeCommandDispatcherTests/Workspace_*` | R-1, R-2 | all 3 listed; 0 failed/skipped | 3 | 1 | true |
| CP-25 | S3-repair | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-removal-repair/` | held-removal-baseline | `/*/*/LegacyWorkspaceRemovalGateTests/(C1032_LegacyRemoveHeldLease*)\|(C1032_LegacyPruneHeldLease*)` | V-4 | both held-lease refusals; 0 failed/skipped after repair | 2 | 3 | true |
| CP-26 | S3-repair | CP-25 | removal-repair-class | `/*/*/LegacyWorkspaceRemovalGateTests/*` | V-4, R-1, R-2 | all 11 results; 0 failed/skipped | 11 | 2 | true |
| CP-27 | S3-repair | CP-25 | workspace-service-class | `/*/*/RunnerWorkspaceServiceTests/*` | R-1, R-2 | all 33 full-class results; 0 failed/skipped | 33 | 3 | true |

#### S3 round-2 removal repair

CP-25 runs first with tests committed over unchanged production from
`91974075f4a04a10ea98d9e06e4e599a10b29c3b`: both methods must fail because
RemoveAsync succeeds while its competing use lease is held. After repair,
CP-25/26/27 run once. CP-5 retains six methods and runs three times; the
unchanged S2 regression group remains 33 results. No Unit lane is assigned
by the repair brief. Additional cost: one isolated runner build plus about
five minutes of scoped Git tests; no full assembly run is needed.

New method witnesses: held present removal and missing-path pruning refuse
`workspace_in_use` before Git, preserve the directory/registration respectively;
released lease permits real removal; creation-first refuses removal and
removal-first queues actual fresh creation until removal/prune finish;
unconfigured removal retains present/absent legacy success even under a held
gate elsewhere; exceptions and cancellation permit the next admission;
retained owners refuse until positively absent. Verification removal has
present/absent variants (two results), and fresh verification creation waits
before Git. All races use deterministic admission barriers or Git entry seams.

| PC | Defect to bind after landing | Exact method filter | Expected red witness | Status |
|---|---|---|---|---|
| PC-551 | Omit present legacy removal admission | `/*/*/LegacyWorkspaceRemovalGateTests/C1032_LegacyRemoveHeldLease*` | expected PhoneHomeAdmissionException is missing | pending |
| PC-552 | Move admission after missing-path prune | `/*/*/LegacyWorkspaceRemovalGateTests/C1032_LegacyPruneHeldLease*` | expected PhoneHomeAdmissionException is missing | pending |
| PC-553 | Keep admission held after disposal | `/*/*/LegacyWorkspaceRemovalGateTests/C1032_LegacyRemoveReleasedLease*` | actual removal refuses workspace_in_use | pending |
| PC-554 | Release removal admission before Git removal/prune | `/*/*/LegacyWorkspaceRemovalGateTests/C1032_LegacyRemovalCreationOrders*` | creation must wait through removal and prune | pending |
| PC-555 | Require a configured gate on legacy remove | `/*/*/LegacyWorkspaceRemovalGateTests/C1032_LegacyUnconfiguredRemoval*` | legacy real removal must succeed | pending |
| PC-556 | Leak removal admission on exception/cancellation | `/*/*/LegacyWorkspaceRemovalGateTests/C1032_LegacyRemovalReleaseOnFailure*` | next admission/removal must succeed; bind exception and cancellation variants separately | pending |
| PC-557 | Ignore retained owners | `/*/*/LegacyWorkspaceRemovalGateTests/C1032_LegacyRemovalRetainedOwner*` | expected PhoneHomeAdmissionException is missing; same-path and absent-sibling prune variants | pending |
| PC-558 | Omit verification removal admission | `/*/*/LegacyWorkspaceRemovalGateTests/C1032_VerificationRemovalHeldLease*` | expected PhoneHomeAdmissionException is missing; bind present and absent variants separately | pending |
| PC-559 | Omit verification creation admission | `/*/*/LegacyWorkspaceRemovalGateTests/C1032_VerificationCreationAdmission*` | GitStarts increases while lease held | pending |
| PC-560 | Omit journal/census hold checks | `/*/*/LegacyWorkspaceRemovalGateTests/C1032_LegacyRemovalJournalFence*` | expected PhoneHomeAdmissionException is missing; retiring, torn and incomplete-census variants | pending |

All original 292 PCs and these ten new IDs remain pending for method-scoped
SourceLanding Mutation. These baseline regressions are ordinary defect proof,
not deliberate mutant cycles. Legacy removals carry no CompletedCard command
identity and do not write that journal; they must honor its existing fences.
Only TryRetireAsync journals immutable cleanup intent before its callback.

### Cost

**Round-2 amendment:** the original estimates below describe the prior freeze.
Sharing six S2 builds preserves every filter/result and removes duplicate
build work. The current 27-row table sums to 103 estimated minutes with 17
distinct outputs and 176 ordinary executions. The repair dispatch selects only
the 33-result S2 group, CP-5 (six results repeated three times), and CP-25..27
(46 results): 97 executions. This bounded repair selection is estimated at
35 minutes including builds/repeated CP-5, excluding slot wait. CP-551..560
increase the pending control inventory to 302 IDs; their named variants need
separate method-scoped Mutation proof. No earlier PC is discharged.

**Estimated**, not measured. Ordinary Code V/R floor is the complete CP-1..CP-24
time sum: **120 minutes**, comprising **22 isolated builds × 3 = 66** and
**54 minutes V/R**. CP-23/24 reuse CP-3 and add two minutes without another build.
Slice floors are S1=9, S2=45, S3=5, S4=5, S5=21, S6=11, S7=12, S8=12 minutes.
Setup/manifest/tool-bootstrap allowance = **3**; S1 EF-generation authoring build
allowance = **3**. Ordinary setup/build + V/R = **126 minutes**, excluding
implementation, slot wait and failure-driven reruns. A separately commissioned
complete Review rerun adds the same **120**, not an unreported broad suite.

Post-land Mutation remains pending: **292 method-scoped controls × 7 =
2,044 minutes**. Each exact filter is in the PC table: baseline green execution
0.5 + bind/apply/check 0.25 + isolated red build/run 3 + restore/check 0.25 +
restored isolated green build/run 3 = 7. Shared initial baseline builds for
the two test projects = **6**; Mutation setup and restoration record = **3**.
Mutation total = **2,053 minutes**. Combined ordinary setup/build/V/R plus
the complete PC battery = **2,179 minutes** estimated (36 h 19 min), before
authoring, slot wait and retries. This is separate staged work, not the duration
of a single Code dispatch or permission to resume Mutation.

The 292 controls include 33 retained parent controls, 36 legacy regression
controls and 223 added independent binding/schema/digest/receipt/use/recovery/
inertness/join predicates. Field-level controls are intentional; sharing a test
method does not collapse independently bypassable comparisons. CP-21 adds
PC-534..539, CP-22 adds PC-540..549, and CP-23 adds PC-550. All remain pending
for method-scoped SourceLanding Mutation, including every named vector/variant.

CP-23/24's two reused builds save **2 × 3 = 6 minutes** versus separate builds.
CP-17..22 retain their separate isolated builds. Native parent qualification and
activation costs remain in the parent plan and are outside this S3 sum.

#### Handoff audit

Static audit after the S2 decision repair: **guards=292, mapped=292, missing=0,
duplicate PC maps=0**. The pre-store-identity freeze had 281 guards; CP-22 adds
G/PC-540..549 and CP-23 adds G/PC-550. All controls remain pending.
Parent U=21/M=12 remain represented once. The complete ordinary table has
24 unique CP rows, 22 isolated outputs, and 130 executions: the preceding
126 plus the explicit legacy contract result and three Workspace smoke results.
This static census is not test/build/mutation execution evidence.
PCs: specified=292, executed=0, pending=292, payloads binding required=292.
The later Mutation acceptance gate requires the named red/restore/green cycles;
paused controls are not waived and cannot be reported PC-clean.

No human policy choice or unverifiable production seam remains for inert S1.
A missing concrete mutation payload after Code stays **binding required**.
Next: **Code S1 — identity and wire schema**, CP-1/CP-2 only, retaining
the eight-slice sequencing and production execution disabled. Parent S4/S5
activation, native qualification and pending Mutation are carried forward.

### S4 repair case additions

| Case | Exact ordinary detecting filter | Results | Oracle |
|---|---|---:|---|
| D1/D2/D3 and full HOLD matrix | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | 42 | hand-written reason, HOLD, actual HEAD, HeldPaths, captured bytes and no intent |
| Read-only inspection/proof calls | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionReadOnlyGitCalls*` | 1 | exact read-only arguments, GIT_OPTIONAL_LOCKS=0, unchanged bytes/index, no intent |

These rows are included in CP-6's existing prefix (46 total results), not extra
checkpoint executions. The repair selection names all required S2/S3 and full-class
regressions. Every added PC and variant remains pending for SourceLanding Mutation.

### S4 repair round 3

The [round-3 verification](2026-10-13-card-1032-s4-r3-verification.md)
supersedes the S4 ordinary selection for Code owner 4da2d6db. It adds 14
regressions for mode changes, damaged/case-varied bare shapes, replacement/graft
ancestry and configured commands. CP-6 now has 60 results. PC-573..577 and all
variants are pending; S5–S8 and native acceptance remain deferred.

### S5 decision amendment — 2026-10-13

Moved exactly: (1) V-3's eligible bound-rebase positive, (2) PC-60's
bound-rebase positive fixture variant. Both move from S5 CP-8
C1017_RunnerPreservationProof to the new S7 CP-13
CardWorktreeMirrorCleanupTests.C1032_AdmissionBoundRebaseProof. CP-13 gains
one ordinary result (seven, previously six); no S5 method/result is dropped.
S5 retains direct-containment positives and a pinned Rebased refusal naming S7
as positive owner; PC-246's negative remains fail-closed in S5. S7's method
also owns authoritative mapping validation once its producer exists. All
controls/variants remain pending for SourceLanding Mutation.

S5 CP-7..10 retain 5/9/4/5 methods. Process-death child arms execute inside
C1032_RemovalComponentRecovery (one parent result), including each durable
state and effects before their subsequent journal save, plus partial-file
deletion. Evidence disposal and richer refusal vectors stay inside their named
parent methods. The child assembly hook performs only the inherited locally
owned scratch operation; it admits no additional test selection or external
executor. Code ordinary red on the guarded missing behavior is required before
production changes.

### S5 recipient controls added by implementation (pending Mutation)

These additions bind recipient predicates exposed by the ordinary regression
cases. They are not deliberate mutant execution. Field-level variants remain
independent controls and Mutation must identify the specific failing field.

| PC | Defect | Exact method filter | Expected red | Variants / status |
|---|---|---|---|---|
| PC-578 | Accept a saved receipt whose field differs from immutable command/progress identity | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerDigestReplay` | expected recipient_store_unreadable is missing for the independently altered receipt field | all 27 receipt fields; pending |
| PC-579 | Skip fresh remote containment after set-aside on replay | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalOriginalReplacement` | stale_preservation_proof is missing and preserved aside/admin oracle fails | after-rename destination loss; pending |
| PC-580 | Reset or recharge a previously spent physical action on replay | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalComponentRecovery` | remaining allowance differs from independently read pre-restart allowance | Deleting, Deleted, Complete, post-delete and partial-file cuts; pending |
| PC-581 | Reject an authoritative valid bound-rebase admission | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionBoundRebaseProof` | eligible bound-rebase mirror directory must be gone | moved S7 positive; implementation/binding pending S7 |
| PC-582 | Dispose evidence without bound external report/artifact/policy authority | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalEligiblePhysical` | evidence_requires_admission is missing or evidence bytes differ | report digest, artifact digest, policy digest, empty external pointers; pending |

PC-578 fields: schemaVersion, cardId, doneRevisionId, targetId, endpointId,
operationId, taskId, attempt, bindingId, preparationId, runnerId, runnerStoreId,
repositoryIdentity, commonDirectory, gitDirectory, worktreePath, sourceFullRef,
creationBaselineSha, expectedHeadSha, preservationProofDigest, artifactProofDigest,
disposalPolicyDigest, disposition, setAsideIdentity, journalIdentity,
directoryRemoved, registrationRemoved. All 27 remain pending.

The Code Final supplement is
[2026-10-13-card-1032-s5-final-verification.md](2026-10-13-card-1032-s5-final-verification.md).
It records the higher-priority Unit/full-class requirement separately from the
caller's CP-7..10 roster. No additional S6–S8 code is commissioned by those runs.
Historical whole-plan cost/count paragraphs above are earlier freezes; this
amendment adds one S7 method and these five control IDs, without waiving any
prior row or named variant. SourceLanding Mutation owns red/restore/green and
further missing-control discovery.

### S5 repair round 2 — F1/F2

Plan D-8 and parent D-5 refuse unknown ignored content and links. Inventory must
classify special files without reading them and return unproven_content before
rename/unregistration, honoring cancellation. The FIFO test runs the real
executor in an inherited owned child with a 12-second hard bound, kill/join,
and an unrelated gate acquisition proving admission release.
Before disposition pin root/ancestors/files, compare recorded identity and regular
file size/hash; mismatch leaves changed bytes and returns changed_since_inventory.
Four new methods are bound by the S5 verification artifact's CP-10 wildcard;
CP-28 covers all three affected classes, 137 results. CP-7/8/9 stay 5/9/4.
Windows late-file/root and sharing qualification remains pending native Review.
S6-S8 and the moved S7 bound-rebase positive remain outside this repair.

| PC | Defect | Exact method filter | Expected red | Status |
|---|---|---|---|---|
| PC-583 | Read ignored FIFO as a regular file | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLinuxIgnoredFifoRefuses` | owned-child hard bound fails or unproven_content/admission release missing | pending SourceLanding Mutation |
| PC-584 | Omit per-file identity comparison | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLateFileIdentityRefuses` | changed_since_inventory or same-byte replacement preservation fails | pending; Linux and Windows variants |
| PC-585 | Omit per-file size/hash comparison | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLateFileContentRefuses` | changed_since_inventory or foreign bytes preservation fails | pending; Linux and Windows variants |
| PC-586 | Omit opened-root identity comparison | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLateRootIdentityRefuses` | changed_since_inventory or replacement-root preservation fails | pending; Linux and Windows variants |

All existing PCs/variants remain pending; ordinary defect red is not Mutation.

Exact additional ordinary method filters (each is included in CP-10 and CP-28):

| Case | Filter | Native qualification |
|---|---|---|
| F1 FIFO | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLinuxIgnoredFifoRefuses*` | Linux only; explicit skip elsewhere |
| F2 same-byte identity | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLateFileIdentityRefuses*` | Windows pending |
| F2 changed bytes and held writer/rename | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLateFileContentRefuses*` | Windows pending; PC-587 write/delete sharing variants |
| F2 opened root | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLateRootIdentityRefuses*` | Windows pending |

Inventory refuses beyond 100,000 entries, 128 descendant levels, 1 GiB total,
256 MiB per regular file or its 30-second deadline. Chunk reads honor cancellation;
these are fail-closed work limits, not weakened test deadlines. Prepared journals
without recorded entry identity/size are refused as unreadable; execution remains
uncomposed, so no live cleanup journal is migrated or activated by this repair.
The Windows sharing test's post-validation seam observes real held-handle effects;
it is never invoked by the Linux test branch and is not Linux qualification.

| PC | Defect | Exact method filter | Expected red | Status |
|---|---|---|---|---|
| PC-587 | Admit a writer or rename after Windows handle validation | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLateFileContentRefuses` | native IOException sharing oracle missing | write-share and delete-share variants; pending Windows SourceLanding Mutation |
