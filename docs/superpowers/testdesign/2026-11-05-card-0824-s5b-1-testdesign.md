# CARD-0824 S5b-1 verification design

Date: 2026-11-05. TestDesign task: `05081946`. Source inspected:
`d465bd4e363a3fd3ba334f174a5576852ee7e227`; assigned branch starts at
`ecc9e60ca476c380f650d6d4f1de0d0af0de8f1e` (the landed Plan).

This is an appended verification contract for **S5b-1 only** of
[the unchanged fix design](../plans/2026-11-05-card-0824-s5b-ref-only-plan.md).
It freezes the tests, caller boundaries, controls and checkpoint selection below;
it does not rewrite D-1..D-11 or enable a producer. Operator option a is decided:
no desktop checkout for admitted ref-only runner tasks. Ref-only admission defaults
OFF; first activation remains server2-only; S4a remains operationally OFF. No live
configuration, runner, desktop, cleanup or canary action is authorized by this document.

## Verification design

### Inspection

References below are repository-relative at the source SHA above. `S/` expands to
`server/Application/Services/`, `A/` to `tests/Antiphon.Tests/Application/`, `H/` to
`tests/Antiphon.Tests/TestHelpers/`. New file sites have no source line yet: their
nearest existing anchor is cited, and their final Code SHA, symbol, line and diff
**remain binding required after Code**. A new type's absence is never a compiling
mutation or an assertion-red witness.

| Bodies read | Boundaries -> witnesses or exclusion |
|---|---|
| Plan Decisions, consumer table, S5b-1/2/3, performance, CP-1..CP-10; project-context; testing-and-build checkpoint schema/import/custody; test-performance-standards; orchestration stage/delivery/D-2 policy | D-1..D-7,D-9,D-10 -> V-1..V-7; D-8 and producers -> exclusions below |
| `AgentTask.cs:215`, `AgentTaskLanding.cs:22`; `LandingDtos.cs:28`; `PreparedLandingVerification.cs:8`; `AppDbContext.cs:2178,2320` | Persistence/typed identity -> I1..I7, L2 |
| `H/DelegationTestServices.cs:46,77`; `H/TestDbFixture.cs:27,57,69`; `A/WorkerWorkspaceDefaultMigrationTests.cs:22` (upgrade/round-trip bodies) | Real DI registration, migrated Postgres and fresh-context proof -> I1,I5,I6; no copied graphs |
| `A/C0824WorkspaceSlotRoleSettingsTests.cs` (all bodies, Bound/reload helpers), `A/C0824DesktopLegacyReleaseSettingsTests.cs` (all bodies); `S/WorkspaceSlotRoleGate.cs:17` | Real JSON binding, scalar grammar, monitor failure/recovery -> Q1..Q4 |
| `H/LandingSafetyHarness.cs:40,65,97,258,310,357,444`; `H/LandingGitFixture.cs:30,44,100,191`; `H/LandingProtocolHarness.cs:21,49,80,136`; `A/LandSourceIdentityTests.cs` (all bodies); `A/AgentTaskLandSourcePersistenceTests.cs:34,123,208` | Real Git/lease + controlled verifier; controlled Git barriers only where named -> L1..L6 |
| `S/AgentTaskLandSourceResolver.cs:43,57,91,109,175,349,490`; `S/LandOperationFactory.cs:22,70`; `S/AgentTaskLandingProtocol.cs:431,610,630,699,906`; `S/AgentTaskLandService.cs:918` | Null refusal, recovery reset, factory/admin/registration/probe/lease-gap boundaries -> L1..L6,C1 |
| `A/TaskWorktreeRetirementTests.cs` (all bodies/World); `A/AgentTaskLandCleanupSafetyTests.cs:213` (absent receipt / unknown-purpose bodies); `A/C0824DesktopLegacyReleasePolicyTests.cs:47,111,150`; `A/DesktopLegacyWorld.cs:36,70,119`; `H/CompletedCardCleanupFixture.cs:41,49`; `A/WorktreeHealthServiceTests.cs:26,47,148` | Retirement/system release, card Done and health -> C1,C3; legacy receipt behavior -> R-3 |
| `S/TaskWorktreeRetirementService.cs:120,273,281,317,505,597`; `S/DesktopLegacyReleasePolicyService.cs:145,393,475`; `S/CardWorktreeCleanupService.cs:89`; `S/CardWorktreeCleanupExecutor.cs:30,47,90`; `S/WorktreeResidueSweepService.cs:157,247,611`; `S/WorktreeHealthService.cs:52,234`; `Infrastructure/Git/WorktreeGuardedCleanup.cs:16,58,136` | Every independent desktop entry/action boundary -> C1,C3. Ref retention and NotRequired provenance are separate assertions |
| `H/CompletedCardMirrorFixture.cs:80,93,236,252,322,336,346,376`; `A/CardWorktreeMirrorCleanupTests.cs:22,43,54,169`; `S/CardWorktreeMirrorCleanupService.cs:31,144,196`; `S/RemoteWorkspaceService.cs:1113` | Real phone-home queue, independent recipient journal/filesystem and replay -> C2. Fixture is **Linux-only**, not a portable Windows substitute |
| `S/WorkspaceUseAdmission.cs:19,60,70`; `S/TerminalRunnerSeatReleaseService.cs:1222`; `S/AdvisoryChangePolicy.cs:55`; `A/WorkspaceReservationKeyTests.cs` (all bodies) | Path equality and branch overlap, advisory identity and runner generations -> I7 |
| `S/CompletionNoteWork.cs:7`; `Infrastructure/Orchestration/CompletionNoteWorkHostedService.cs:33,176`; `S/LandNoteReceipt.cs` (all bodies); `S/AgentTaskLandNotificationService.cs:98,166,272,316,366`; `A/AgentTaskLandReceiptTests.cs` (all bodies, Seed/ReceiptSaveFailure); `A/AgentTaskLandQueuedReceiptTests.cs` (all bodies/ReceiptFixture); `A/AgentTaskLandNotificationRecoveryTests.cs:141,197,338`; `H/BridgeQueueHarness.cs:56,113,284,351,512`; `H/ClaudeDialogFixture.cs` (all bodies) | Actual producer -> durable outbox -> real SessionMessageQueue -> modeled recipient complete UserPrompt -> L6. Seed-only receipt tests cannot establish this chain |
| `A/DelegationWorktreeTests.cs:246,1004` (complete two selected bodies) | Frozen base and genuine owned lease unchanged -> R-1,R-2 |

### Delivery inventory

S5b-1 adds no dispatch, settlement, report or session-input producer. It changes
land operation representation, cleanup component outcomes and their existing async
consumers, so testing must finish at their recipients. The durable joins are explicit:

| Path | Producer -> destination | Persistence / recovery | Observable receipt and witness |
|---|---|---|---|
| Ordinary land | `AgentTaskLandService.RequestAsync` -> `AgentTaskLandQueue` -> `RunRequestAsync` / protocol -> bare origin target | Request before enqueue; request/operation checkpoints, source observation pins, verification ticket across lease gap; sweep requeues committed pending request after lost enqueue/restart. Join TaskId + RequestId + Attempt + OperationId + source mode/BindingId + approved S | Independent observer fetches target and verifies exact landed candidate ancestry/bytes; saved operation retains S/mode/binding, source ref retained. L1,L2,L5. A queued request alone is not publication |
| Outcome/refusal/hold caller note | Terminal event + notification written by the land producer -> notification reconciler/flush queue -> **real** `SessionMessageQueueService` -> parent session | Terminal transaction/outbox; keyed queue insert and link; delivery-attempt floor; transcript receipt save. Recovery reuses the same notification/queue key. Join TaskId/RequestId/SourceEventId/NotificationId/QueueMessageId/ParentSessionId/digest | Matching complete `UserPrompt` after the attempt sequence/time floor; note ConfirmingPromptSequence points to that same transcript row. L6. Sent/event/ack/adapter input and QueuedUserPrompt alone are insufficient for this design's receipt |
| Independent remote cleanup retained after desktop NotRequired | Card cleanup discovery -> bound mirror endpoint -> `CardWorktreeMirrorCleanupService` -> phone-home dispatcher **real mutation queue** -> runner removal handler | Done revision/endpoint; immutable operation command persisted before send; runner journal; imported exact receipt. Transport loss/reconnect/import failure replay exact bytes and OperationId. Join task/attempt + mirror BindingId/PreparationId + runner/store + endpoint/operation + slot generation where applicable | Runner-side handler entry, completed journal and exact imported receipt PLUS independent directory/registration absence, preserved origin/source refs and retained artifact bytes. C2. An HTTP response or command record alone is insufficient |

**L6 recipe (production producer, not SeedAsync):** share L1's task/schema with
one BridgeQueueHarness attached parent session. Use `ClaudeDialogFixture.CreateBridgeAsync`
and default `OnSubmitted` (`BridgeQueueHarness.cs:351`) so only an actual accepted
queue submission writes the complete UserPrompt. Start with a prior visible turn,
record its floor. Run once already idle and once `MarkWorkingAsync`; for busy,
assert no receipt/input, then `MarkIdleAsync` and call the production turn-end/flush
path. Reconcile the producer's real notification, drive queue delivery, reconcile
receipt and independently load the full transcript. Assert exactly one matching
whole body, session/digest/identity/floor, then replay reconciliation with no
additional submission. Test successful land and the new recovery refusal outcome.

L6 cuts: before terminal transaction commit (rollback then rerun); after terminal
commit/before land-queue enqueue; after notification commit/before queue insert
(injected enqueue failure); after queue insert/before note link; after link/before
flush; after accepted input/before receipt save. Drop/reconstruct service scopes
and in-memory queues at each cut, retain DB/Git/session identity, advance fake
retry time rather than sleep, and drive the existing sweep/reconcile/turn-end paths.
Each cut must reach the named boundary before injection and finish with the same
producer-to-recipient receipt. Before submission, failure leaves pending debt;
after submission, catch-up recovers without retyping. Also supply Sent/no prompt,
wrong session, wrong identity, truncated middle, and pre-floor complete prompt as
negative evidence, followed by a valid actual submission. No manually inserted
positive prompt may replace default OnSubmitted in L6.

**C2 recipe:** use `CompletedCardMirrorFixture` and Ready/Busy mutation queue
variants (`HoldMutationTurnAsync` / `ReleaseWhenQueuedAsync`), with desktop mode
RefOnly and a foreign identity decoy. Desktop is NotRequired; mirror endpoint is
independently eligible. Cut after discovery, before intent commit, after intent/
before send, after recipient mutation/before response, and before imported receipt
save (`BeforeIntent`, `AfterIntent`, `BeforeRemoval`, `AfterRemoval`,
`FailReceiptImport`). Reconnect/rebuild coordinator at every cut. Verify boundary
hit, persisted immutable request, same operation replay, recipient evidence above,
and no duplicate destructive action. Wrong mirror binding/store/generation or
unreleased slot must retain bytes. Existing slot release remains the only slot
owner; its zero-call expectation is part of C2, not a new cleanup mechanism.

Substitutes: LandingProtocolHarness's controlled Git proves ordering, ticket
comparison and refusal, not OS custody or actual Git effects; L1 uses real local
Git and a real bare remote to close that seam. ControlledVerifier proves candidate
selection/resume, not the product's build acceptance. BridgeQueueHarness uses
production queue/runtime/DB with a modeled supported-Claude adapter; its
OnSubmitted transcript proves this application's end-to-end queue receipt, not a
real provider's terminal transport. The phone-home fixture uses real loopback
transport, recipient and queue, not a deployed runner. Native Windows/path/ADS/
junction and live CLI behavior remain explicit S5b-3 acceptance, never inferred
from Linux or these substitutes. No changed delivery path stops at queue evidence.

### Proves it works now

The following are **future Code tests**, not executions claimed by TestDesign.
Aliases I/Q/L/C name exact methods in the frozen roster. Every method has one
`[Test]`, no `[Arguments]`; listed variants are in-body, assertion-labelled cases.
Each variant gets isolated task/binding/ref state and a reached marker. Counts
below count methods, not loop iterations. Assertion labels are fixed strings to
make PC red evidence decisive.

Fixtures: **DB** = TestDbFixture cloned DB/fresh context; **JSON** = real JSON/
production registration and reload pattern from C0824WorkspaceSlotRoleSettingsTests;
**LAND** = LandingSafetyHarness + real FixtureGit (both command cwd and args),
controlled verifier; **TICKET** = LandingProtocolHarness + barriers, with LAND
positive publication counterpart; **DONE** = CompletedCardCleanupFixture;
**POLICY** = DesktopLegacyWorld; **WIRE** = CompletedCardMirrorFixture;
**QUEUE** = BridgeQueueHarness. Extend `H/DelegationTestServices.cs:77` for new
services, use existing I/O doubles, and do not copy a second DI graph. New wrappers
are insufficient without the named production caller below.

All new files live in `tests/Antiphon.Tests/Application/`, namespace
`Antiphon.Tests.Application`. Class prefix plus method below is the exact test
identity. I = `C0824RefOnlyIdentityTests`, Q = `C0824RefOnlyGateTests`,
L = `C0824RefOnlyLandingTests`, C = `C0824RefOnlyCleanupTests`.

| Case | Exact method | Recipe / variants / production reachability | Oracle; red at base; classification |
|---|---|---|---|
| I1 | `Mode_round_trips_with_legacy_default` | DB: migrate from immediately preceding new migration with legacy raw SQL rows; upgrade and reload; insert RefOnly task/binding/land op and reload from a fresh context. Raw SQL legacy insert omits new columns. Conflicting full TaskId, repo/full-ref and legacy branch owner insert/admission attempts are independent variants | `legacy-default=Checkout`, `persisted-mode=RefOnly`, `persisted-binding=expected`; preparation state/revision/base provenance survive. DB rejects each uniqueness collision; prior rows unchanged. Base model lacks mapped properties/table; behavioral schema assertion red, not a compilation failure. Integration, portable |
| I2 | `Identity_is_unique_stable_and_never_created` | DB + typed workspace/binding selector. Two full GUIDs sharing first eight hex digits; configured root A then B; identity leaf uses N-format full GUID; resolve twice after restart. Record root inventory and I/O spy | `identity-distinct`, `identity-stable`, absolute identity under root A; parent/leaf absent; no mkdir, CreateAsync or session launch. Base creation materializes short-id checkout; scaffolded selector's old path fails equality/absence. Integration, portable |
| I3 | `Missing_repository_never_uses_identity_as_git_cwd` | DB + typed selector through land request/resolver: null/empty RepoPath, missing common identity, null WorktreePath separately; valid paired context includes remote POSIX execution cwd distinct from repo/identity | Named refusal, `no-identity-git`, `no-main-file-io`, `no-launch`; valid case returns repository Git cwd + runner execution cwd. Base null path already refuses at resolver:43 (retained); fallback readers still select WorktreePath, new selector contract absent. Integration, portable |
| I4 | `Mode_does_not_follow_directory_presence` | LAND: explicit RefOnly absent then foreign decoy present; legacy Checkout with missing directory; unknown enum, Checkout+binding, RefOnly without binding are separate variants | `kind-from-row`; bad tuples refuse before I/O; legacy stays Checkout, RefOnly admin is null in both shapes. Base factory:70 branches on directory existence. Integration, portable |
| I5 | `Binding_survives_attempt_and_settings_changes` | DB: persist base SHA/provenance/identity/full ref/preparation state/revision, increment attempt, change configured root/allowlists, gate off and reconstruct services; resolve recorded context | `binding-unchanged`, same mode/base/ref/identity; already-selected preparation failure holds, never calls CreateAsync. Base has no binding persistence; conservative scaffolding cannot supply recorded context. Integration, portable |
| I6 | `Foreign_binding_refuses_before_git_or_file_io` | DB+LAND caller: vary only TaskId, binding id, repository/common, full ref, identity, immutable selected base (valid full SHA but wrong), one per variant; all other fields exact | `foreign-binding-refused`, zero Git/file/session mutation, stored original binding intact. Base has no ownership comparison; compiling representation scaffolding routes unchanged caller to missing refusal. Integration, portable |
| I7 | `Path_equality_consumers_keep_task_and_runner_ownership_distinct` | DB + actual WorkspaceUseAdmission.RequireConsumerAsync/HasLiveTaskConsumerAsync, AdvisoryChangePolicy.CanPrepareSeparateDocs, TerminalRunnerSeatReleaseService.AmbiguousOwnershipAsync. Same repo/different full identities/refs; then same ref/different identity; same slot/generation vs next generation; distinct POSIX runner paths. Seed legitimate reviewed helper tuple for advisory | `reservation-distinct` and `same-ref-fenced`; valid helper allowed with distinct identity, identical identity refused; `seat-distinct` vs `same-generation-ambiguous`; desktop absence never releases a seat. Base path predicates are retained regression, typed context collapsing keys fails new positive. Integration, portable (Windows normalization deferred) |
| Q1 | `Default_off_and_empty_lists_admit_nothing` | JSON: defaults and empty production section; enabled=false with nonempty lists; empty RunnerIds; Roles=none. Call new admission selector only, never dispatch | `new-admission=false` for each; class and bound defaults OFF/[]/none. Negative at base retained; a conservative no-admission scaffold is green here and Q2 must be assertion-red. Unit, portable |
| Q2 | `Role_and_runner_both_must_match` | JSON: enabled true with RunnerIds=[test runner], Roles=Plan; match both vs runner only vs role only vs neither; explicit * and none; similar prefixes/unrecognized id/unknown role do not match | `both-match=true`, exclusions false. New gate absent at base; conservative scaffold fails both-match at an assertion. Unit, portable |
| Q3 | `Malformed_reload_closes_only_new_admission` | JSON production registration: invalid boolean/role scalar shapes, null/object/array roles, invalid grammar, invalid runner list shape/unknown key; valid startup control. Valid->invalid->valid reload with same monitor/gate; resolve an already bound RefOnly task while invalid/off | Startup throws OptionsValidationException naming exact key; `reload-admits=false`, diagnostic once per invalid spell, recovered true; `existing-mode=RefOnly` and binding preserved. No ref-only validation at base. Unit, portable; file reload bound separately, not a 2s pure-body case |
| Q4 | `Mutation_source_landing_and_desktop_are_excluded` | Explicit enabled matching feature configuration plus eligible Worktree/runner/Plan control; independently Shared, ReadOnly, null/blank runner, Mutation, SourceLandingOperationId variants; call IsEligible and admission selector | `eligible-control=true`; every excluded tuple false. Base IsEligible exclusions retained; cannot turn new feature on by dropping a clause. Unit, portable |
| L1 | `Late_land_uses_pinned_source_without_touching_decoy` | LAND: prepare S with fixture-owned authoring checkout, push it, remove that setup checkout **before** SUT, seed RefOnly binding and persisted unique identity. Create another task with a real decoy checkout at the old identity, decoy index.lock and changed bytes. Advance target independently, request ordinary exact S late, restart services, consume real land queue. Paired absent-identity case | `publication-has-S`: observer fetch confirms landed candidate's ancestry/feature bytes from S; source ref remains S, main target only advances by existing canonical contract; other task/decoy index/files/admin/ref unchanged. `source-admin=null`, `no-source-cwd`, `no-source-probe`. Base factory/admission binds/probes decoy. Integration, portable |
| L2 | `Verification_resume_binds_mode_and_binding` | TICKET: pause verifier after lease release; independently alter task mode, task binding, op mode, op binding, ticket mode/binding (rebuild record with one changed field); reacquire using production protocol. Restore valid tuple positive. Also corrupt task/ref/repo snapshots after request | `resume-refused` before target/push/source write for each drift, no stale publication; all barriers reached; valid frozen tuple lands. Base tickets/op lack mode/binding comparisons, valid RefOnly also blocked by checkout expectation. Integration, portable |
| L3 | `Foreign_source_registration_holds_without_mutation` | LAND: attach source full ref to a foreign checkout at different path (even same SHA); do once before request and once during verifier lease gap. Identity decoy on a *different* ref remains L1's allowed control | `source-registration-refused` with exact custody refusal; no publish/remove/reset, source and foreign bytes unchanged. Base lacks explicit absent-registration expectation. Integration, portable |
| L4 | `Recovery_and_interrupted_reset_refuse_before_any_write` | TICKET + one real-Git refusal image: independently reviewed-source recovery, adopt-from-task recovery, resumed persisted source-adopt-reset with dead child/no child id, and already-adopted checkpoint; valid bindings and source observations in each. Ordinary land positive separately | Exact `ref_only_land_recovery_unsupported`; `recovery-writes=0` includes pins/ref updates/reset/remote writes/owned child start, not refusal DB persistence. `recovery-boundary-reached`. Base reaches resolver:349/:490 reset paths. Integration, portable |
| L5 | `Main_and_land_guards_still_fence_ref_only_publication` | LAND/TICKET: independently wrong approved S (local and remote equal to different S2), endpoint fingerprint drift, main index.lock, land index.lock, land sequencer, land tracked dirt; mutate during correct admission/window boundary; paired clean valid ordinary land | Specific existing refusal/hold for each; `target-unpublished`, source/foreign refs/bytes unchanged; `candidate-is-S` clean case. Base retained safety controls; protects against overly broad source-probe removal. Integration, portable |
| L6 | `Land_outcome_reaches_caller_via_queue_and_recovers_handoffs` | LAND+QUEUE: complete producer-to-recipient recipe/cuts above, successful land and recovery refusal, busy and already eligible recipient | `caller-whole-userprompt`, matching note/session/sequence/digest; `one-keyed-row`, `no-retype-after-receipt`; no false confirmation for bad evidence. Base new RefOnly outcome cannot reach receipt. Integration, portable modeled provider |
| C1 | `All_desktop_consumers_skip_ref_only_without_deleting_refs` | DB+LAND/DONE/POLICY: fresh seeded task per named entry below; no producer. Replay pre-existing stale desktop release/endpoint rows to exercise action recheck independently of discovery. Identity foreign decoy with sentinel, unique local ref + recovery pins + remote ref | `desktop-not-required:<consumer>` with mode/binding provenance; DirectoryRemoved/RegistrationRemoved/BranchRemoved remain false; no desktop claim/command intent/journal/remover/diagnostic/probe, no filesystem access at identity, exact decoy bytes and all refs/pins retained. Base selects path/creates cleanup work, or records wrong absent/removed state. Integration, portable |
| C2 | `Remote_cleanup_still_uses_its_binding_and_generation` | WIRE: C2 recipe above; RefOnly desktop identity plus actual independently bound mirror; ready/busy, all crash/send/import cuts, wrong binding/store and slot generation controls | `remote-receipt-and-image` physical/journal/import evidence, desktop NotRequired and untouched, `same-operation-replay`; unreleased/wrong-generation slot retained, no borrowed desktop absence authority. Base discovery ExclusionReason path suppresses otherwise eligible mirror or requires desktop completion. Integration, **Linux-only** fixture |
| C3 | `Legacy_missing_and_dirty_checkouts_keep_their_guards` | LAND/POLICY: Checkout absent (including old raw SQL row) is still Checkout, dirty existing checkout and identity-shaped legacy path; valid Checkout eligible control. Run touched consumers; no conversion by directory absence | `legacy-kind=Checkout`, existing missing/dirty/ownership refusal or retained cleanup residue, no false RefOnly NotRequired; valid eligible Checkout still follows old successful path. Base retained regression. Integration, portable |

C1's closed caller roster (each has its own reached marker and distinct PC):
TaskWorktreeRetirementService.EvaluateEligibilityAsync; ReleaseAsync;
ReleaseSystemAsync; TryRetireAsync action; DesktopLegacyReleasePolicyService
candidate classification, precheck and post-inspection action; CardWorktreeCleanupService
DiscoverAsync; CardWorktreeCleanupExecutor initial admission, replay and intent recheck;
WorktreeResidueSweepService settled and publication lanes plus snapshots;
WorktreeHealthService.SweepAsync; AgentTaskLandingProtocol.CleanupAsync;
WorktreeGuardedCleanup.RemoveAsync defense. Preserve actual branch/ref residue in
health/residue: suppressing all findings is not the oracle. For action guards seed a
stale eligible record directly, bypass earlier filters deliberately; earlier mode
checks must not mask the tested guard. For uncallable internal functions drive the
public caller and existing callback/barrier. Bind any new seam before Code finishes.

- V-1: Durable mode/binding/schema and unique stable coordinates | Integration | I1..I6, CP-1 | persisted tuples and refusal-before-I/O oracles above.
- V-2: Off-by-default validated admission, exclusions, unconditional recorded-mode handling | Unit/Integration | Q1..Q4 + I5, CP-1 | one true eligible control plus all independent false cases.
- V-3: Identity separation at real path-equality callers | Integration | I7, CP-1 | independent reservation/advisory/seat assertions.
- V-4: Exact-S ordinary late land without source checkout/admin | Integration | L1..L5, CP-2 | independent fetched target evidence, absent source probes, refusal variants.
- V-5: Caller actually receives both new result shapes | Integration | L6, CP-5 | complete correlated UserPrompt through real queue, recovery at every handoff.
- V-6: No desktop deletion/claim; explicit NotRequired provenance; refs/pins survive | Integration | C1,C3, CP-3 | each caller reached and independent image/journal oracle.
- V-7: Independent remote cleanup survives desktop NotRequired | Integration | C2, CP-4 | real recipient evidence and slot custody controls.

### Guards the regression

- R-1: Checkout base does not move on reuse | existing Integration | `DelegationWorktreeTests.C508_ReuseKeepsRecordedBase`, CP-6 | WorktreeBaseSha equals originally recorded SHA after master moves, task tip unchanged.
- R-2: Base resolution/creation requires this provider's live lease | existing Integration | `DelegationWorktreeTests.C508_BaseResolutionRequiresOwnedLease`, CP-6 | occupied/foreign lease leaves base/path unset and recording.CreateCalls=0; valid lease creates once from moved selected tip.
- R-3: Existing missing or dirty Checkout never becomes RefOnly; source cleanup residue does not grant deletion | C3, CP-3 | legacy-kind and byte/ref survival, paired eligible Checkout action. Old land cleanup's branch-deletion behavior stays unchanged **only for Checkout**.
- R-4: Source suppression must retain main/land locks, endpoint, S, sequencer and tracked-tree checks | L5, CP-2 | named refusal before publication for one fault at a time.
- R-5: Settings off/invalid cannot strand persisted RefOnly coordinates | I5,Q3, CP-1 | identical bound context despite restart/attempt/config change.
- R-6: Shared RepoPath does not merge ownership; matching remote slot generation still fences | I7,C2, CP-1/CP-4 | same-ref/slot-generation negative plus independent task positive.

### Guard inventory

The inventory covers every safety-critical assertion selected for S5b-1, including
retained guards whose scope could be widened by the new mode. Independently
bypassable call sites have independent rows. Other untouched land/runner safety
policies retain their own batteries; they are not silently claimed from this
selection. Every row below maps 1:1 to its same-numbered PC. None is exempt.
`new` means Code must bind the final compiling site; the cited old line is an
insertion/nearest-fixture anchor, not a claim that the guard exists at base.

| Guard | Plan reference + safety-critical invariant | Positive control |
|---|---|---|
| G-1 | D-2: Legacy SQL default remains Checkout | PC-1 |
| G-2 | D-2: Mode is durably mapped | PC-2 |
| G-3 | D-3: Task binding pointer is durably mapped | PC-3 |
| G-4 | D-3: Full TaskId has one durable binding | PC-4 |
| G-5 | D-3: Repository/full-ref owner is unique | PC-5 |
| G-6 | D-3: Legacy branch owner cannot be overwritten/adopted | PC-6 |
| G-7 | D-2: Identity uses full GUID, not short prefix | PC-7 |
| G-8 | D-2: Persisted identity is never recomputed | PC-8 |
| G-9 | D-2/D-5: Identity selection creates no directory | PC-9 |
| G-10 | D-5: Missing RepoPath cannot fall back to identity | PC-10 |
| G-11 | D-5: Missing common identity refuses | PC-11 |
| G-12 | D-2: Null identity coordinates still refuse | PC-12 |
| G-13 | D-2: Mode never inferred from directory presence | PC-13 |
| G-14 | D-2: Unknown mode refuses before I/O | PC-14 |
| G-15 | D-2/D-3: Checkout plus ref-only binding is contradictory | PC-15 |
| G-16 | D-3: RefOnly requires binding | PC-16 |
| G-17 | D-3/D-6: Attempt retry preserves original binding | PC-17 |
| G-18 | D-3: Selected base SHA/provenance is immutable | PC-18 |
| G-19 | D-6: Gate-off cannot change recorded mode handling | PC-19 |
| G-20 | D-3: Task ownership matches binding | PC-20 |
| G-21 | D-3: Binding pointer matches durable row | PC-21 |
| G-22 | D-3/D-5: Canonical repository identity matches | PC-22 |
| G-23 | D-3: Full source ref matches binding | PC-23 |
| G-24 | D-2/D-3: Identity path matches binding | PC-24 |
| G-25 | D-3: Task selected base matches binding | PC-25 |
| G-26 | D-6: Enabled defaults OFF | PC-26 |
| G-27 | D-6: Empty runner allowlist admits nothing | PC-27 |
| G-28 | D-6: Roles none admits nothing | PC-28 |
| G-29 | D-6: Runner must match independently | PC-29 |
| G-30 | D-6: Role must match independently | PC-30 |
| G-31 | D-1: Only Worktree is eligible | PC-31 |
| G-32 | D-1: Nonempty RunnerId is required | PC-32 |
| G-33 | D-1: Mutation is excluded | PC-33 |
| G-34 | D-1: SourceLanding is excluded | PC-34 |
| G-35 | D-6: Malformed startup configuration refuses | PC-35 |
| G-36 | D-6: Role grammar accepts exact names only | PC-36 |
| G-37 | D-6: Invalid reload closes new admission | PC-37 |
| G-38 | D-5: Reservation keys do not collapse to shared RepoPath | PC-38 |
| G-39 | D-5: Same source branch still fences other writers | PC-39 |
| G-40 | D-5: Advisory helper retains distinct workspace identity | PC-40 |
| G-41 | D-5: Desktop absence supplies no seat-release authority | PC-41 |
| G-42 | D-5: Different generations do not alias remote ownership | PC-42 |
| G-43 | D-7: Land factory omits source canonical resolution | PC-43 |
| G-44 | D-7: Factory does not invent absent source admin path | PC-44 |
| G-45 | D-7: Admission skips source-only index lock probe | PC-45 |
| G-46 | D-7: Verification skips source residue probes | PC-46 |
| G-47 | D-7: Task mode rechecked after lease gap | PC-47 |
| G-48 | D-7: Task binding rechecked after lease gap | PC-48 |
| G-49 | D-7: Ticket captures/compares mode | PC-49 |
| G-50 | D-7: Ticket captures/compares binding | PC-50 |
| G-51 | D-7: Attached source checkout refuses initial admission | PC-51 |
| G-52 | D-7: Source registration rechecked after lease gap | PC-52 |
| G-53 | D-7: Reviewed-source recovery refuses early | PC-53 |
| G-54 | D-7: Adopt-from-task recovery refuses early | PC-54 |
| G-55 | D-7: Persisted reset intent cannot resume in RefOnly | PC-55 |
| G-56 | D-7: Exact approved SHA still required | PC-56 |
| G-57 | D-7: Destination remains bound through verification | PC-57 |
| G-58 | D-7: Main checkout index-lock guard remains | PC-58 |
| G-59 | D-7: Land checkout index-lock guard remains | PC-59 |
| G-60 | D-7: Land sequencer guard remains | PC-60 |
| G-61 | D-7: Land tracked-tree guard remains | PC-61 |
| G-62 | D-9: Retirement eligibility excludes RefOnly | PC-62 |
| G-63 | D-9: Operator release cannot file desktop retirement | PC-63 |
| G-64 | D-9: System release cannot file desktop retirement | PC-64 |
| G-65 | D-9: Retire action rechecks mode on stale row | PC-65 |
| G-66 | D-9: Legacy candidate classification excludes RefOnly | PC-66 |
| G-67 | D-9: Legacy precheck cannot inspect identity | PC-67 |
| G-68 | D-9: Legacy action rechecks mode before release | PC-68 |
| G-69 | D-9: Card discovery creates no local endpoint | PC-69 |
| G-70 | D-9: Card executor admission skips RefOnly | PC-70 |
| G-71 | D-9: Card replay cannot reuse desktop operation | PC-71 |
| G-72 | D-9: Card intent rechecks mode | PC-72 |
| G-73 | D-9: Settled residue lane creates no desktop action | PC-73 |
| G-74 | D-9: Publication residue lane creates no desktop retry | PC-74 |
| G-75 | D-9: Residue snapshot retains mode provenance | PC-75 |
| G-76 | D-9: Health suppresses only missing-checkout finding | PC-76 |
| G-77 | D-9: Land cleanup has no desktop remover | PC-77 |
| G-78 | D-9: Guarded cleanup cannot borrow identity authority | PC-78 |
| G-79 | D-9: No local task-ref deletion at RefOnly cleanup | PC-79 |
| G-80 | D-9: NotRequired is not fake component removal | PC-80 |
| G-81 | D-9/D-10: Checkout cannot use RefOnly shortcut | PC-81 |
| G-82 | D-9: Desktop NotRequired does not suppress remote discovery | PC-82 |
| G-83 | D-9: Desktop NotRequired does not suppress remote execution | PC-83 |
| G-84 | D-9: Remote binding identity remains required | PC-84 |
| G-85 | D-9: Remote store comparison remains required at replay | PC-85 |
| G-86 | D-5/D-9: Unreleased runner slot is not ordinary mirror cleanup | PC-86 |
| G-87 | D-9: Mirror replay preserves immutable operation identity | PC-87 |
| G-88 | D-9: Imported receipt matches actual recipient proof | PC-88 |
| G-89 | D-7 delivery: Terminal transition must retain caller outbox | PC-89 |
| G-90 | D-7 delivery: Lost queue insert/link recovers original key | PC-90 |
| G-91 | D-7 delivery: Recovered queue destination must match | PC-91 |
| G-92 | D-7 delivery: Recovered queue payload digest must match | PC-92 |
| G-93 | D-7 delivery: Idle recipient receives wakeup without later busy turn | PC-93 |
| G-94 | D-7 delivery: Busy recipient delivery survives lost wakeup/restart | PC-94 |
| G-95 | D-7 delivery: Sent alone is never receipt | PC-95 |
| G-96 | D-7 delivery: Transcript session must match | PC-96 |
| G-97 | D-7 delivery: Transcript must contain complete immutable body | PC-97 |
| G-98 | D-7 delivery: Receipt sequence must exceed attempt floor | PC-98 |
| G-99 | D-7 delivery: Receipt timestamp fallback respects attempt floor | PC-99 |
| G-100 | D-7 delivery: Receipt-save recovery cannot retype accepted body | PC-100 |
| G-101 | R-1/D-3: Legacy creation base remains frozen on reuse | PC-101 |
| G-102 | R-2/D-4: Legacy base/create requires this provider live lease | PC-102 |
| G-103 | D-6: Explicit Enabled=false closes new admission even with matching nonempty lists | PC-103 |
| G-104 | D-7: Resolver checkpoint does not invent source GitDirectory for RefOnly | PC-104 |

### Positive controls

Every PC below means: break its one G-n with the listed **compiling production
defect**, run the exact named method, require the listed assertion red, restore
only that mutant and rebuild/run the same method green. The full case recipe,
variant and reachability are in the case table, C1 roster or delivery inventory.
Classify each result `killed-at-intended-assertion`, `survived`, `invalid-control`
(noncompiling/equivalent), or `infrastructure/incomplete`; setup/discovery failure,
zero tests, unrelated red and timeout never count as a kill. All G/PC pairs are
mandatory, including retained safety regressions. No PC is executed by TestDesign.

Use method-scoped filters only: `/*/*/<ExactClassName>*/<ExactMethodName>`.
The alias in each row expands to the exact Class.Method above; require exactly
one executed method (and its in-body named variant reached). Never run a whole
class for a PC. Bind all new symbols and shifted anchors to the landed L before
Mutation, preserve the actual one-line patch/digest and before/after evidence.
New payloads/sites **remain binding required after Code**, even if scaffolding
has made them compile. Review judges that binding before land; Code runs ordinary
V/R; post-land SourceLanding Mutation runs every red/restore/green cycle at L.
A survivor requires a coverage/product finding and a new repair commission; no
production/test repair from the mutation snapshot.

| PC | Break G-n at source anchor (S/ unless stated) | Compiling defect | Exact case method | Required red assertion / variant |
|---|---|---|---|---|
| PC-1 | G-1; `new migration/default; server/Infrastructure/Data/AppDbContext.cs:2178` | change SQL default to RefOnly | `C0824RefOnlyIdentityTests.Mode_round_trips_with_legacy_default` | `legacy-default=Checkout` |
| PC-2 | G-2; `new AgentTask mode mapping; server/Infrastructure/Data/AppDbContext.cs:2178` | Ignore the mode property while retaining the CLR member | `C0824RefOnlyIdentityTests.Mode_round_trips_with_legacy_default` | `persisted-mode=RefOnly` |
| PC-3 | G-3; `new AgentTask binding mapping; server/Infrastructure/Data/AppDbContext.cs:2178` | Ignore the nullable binding pointer | `C0824RefOnlyIdentityTests.Mode_round_trips_with_legacy_default` | `persisted-binding=expected` |
| PC-4 | G-4; `new TaskRefWorkspaceBinding mapping; server/Infrastructure/Data/AppDbContext.cs:2320` | remove only TaskId unique index | `C0824RefOnlyIdentityTests.Mode_round_trips_with_legacy_default` | `duplicate-task-rejected` |
| PC-5 | G-5; `new binding model; server/Infrastructure/Data/AppDbContext.cs:2320` | remove only repository/full-ref unique index | `C0824RefOnlyIdentityTests.Mode_round_trips_with_legacy_default` | `duplicate-ref-rejected` |
| PC-6 | G-6; `new binding admission; DelegationWorktreeService.cs:301` | omit legacy-owner collision lookup | `C0824RefOnlyIdentityTests.Mode_round_trips_with_legacy_default` | `legacy-collision-refused` |
| PC-7 | G-7; `new TaskWorkspaceContext; server/Domain/Entities/AgentTask.cs:215` | use TaskId N string first eight characters for identity leaf | `C0824RefOnlyIdentityTests.Identity_is_unique_stable_and_never_created` | `identity-distinct` |
| PC-8 | G-8; `new TaskWorkspaceContext; AgentTask.cs:215` | recompute identity from current configured root on resolve | `C0824RefOnlyIdentityTests.Identity_is_unique_stable_and_never_created` | `identity-stable` |
| PC-9 | G-9; `new TaskWorkspaceContext; DelegationWorktreeService.cs:414` | insert Directory.CreateDirectory on selected identity | `C0824RefOnlyIdentityTests.Identity_is_unique_stable_and_never_created` | `identity-never-created` |
| PC-10 | G-10; `new TaskWorkspaceContext; AgentTaskLandSourceResolver.cs:43` | substitute WorktreePath when RepoPath is null/empty | `C0824RefOnlyIdentityTests.Missing_repository_never_uses_identity_as_git_cwd` | `no-identity-git` |
| PC-11 | G-11; `new TaskWorkspaceContext; AgentTaskLandSourceResolver.cs:57` | accept repository path as unverified common identity | `C0824RefOnlyIdentityTests.Missing_repository_never_uses_identity_as_git_cwd` | `common-required` |
| PC-12 | G-12; `AgentTaskLandSourceResolver.cs:43` | remove WorktreePath null check and coalesce to empty identity | `C0824RefOnlyIdentityTests.Missing_repository_never_uses_identity_as_git_cwd` | `null-identity-refused` |
| PC-13 | G-13; `new TaskWorkspaceContext; LandOperationFactory.cs:70` | choose Checkout when Directory.Exists(identity) | `C0824RefOnlyIdentityTests.Mode_does_not_follow_directory_presence` | `kind-from-row` |
| PC-14 | G-14; `new TaskWorkspaceContext; AgentTask.cs:215` | treat unknown enum as Checkout | `C0824RefOnlyIdentityTests.Mode_does_not_follow_directory_presence` | `unknown-mode-refused` |
| PC-15 | G-15; `new TaskWorkspaceContext; AgentTask.cs:215` | ignore unexpected binding on Checkout | `C0824RefOnlyIdentityTests.Mode_does_not_follow_directory_presence` | `contradictory-binding-refused` |
| PC-16 | G-16; `new TaskWorkspaceContext; AgentTask.cs:215` | construct RefOnly context without binding | `C0824RefOnlyIdentityTests.Mode_does_not_follow_directory_presence` | `binding-required` |
| PC-17 | G-17; `new binding/context resolver; server/Domain/Entities/AgentTask.cs:215` | allocate new binding on Attempt change | `C0824RefOnlyIdentityTests.Binding_survives_attempt_and_settings_changes` | `binding-unchanged` |
| PC-18 | G-18; `new binding/context resolver; AgentTask.cs:215` | replace recorded base with current main HEAD | `C0824RefOnlyIdentityTests.Binding_survives_attempt_and_settings_changes` | `base-unchanged` |
| PC-19 | G-19; `new context resolver; WorkspaceSlotRoleGate.cs:17` | return legacy context if feature now disabled | `C0824RefOnlyIdentityTests.Binding_survives_attempt_and_settings_changes` | `existing-mode=RefOnly` |
| PC-20 | G-20; `new context resolver; AgentTaskLandSourceResolver.cs:43` | omit binding.TaskId comparison | `C0824RefOnlyIdentityTests.Foreign_binding_refuses_before_git_or_file_io` | `foreign-task-refused` |
| PC-21 | G-21; `new context resolver; AgentTaskLandSourceResolver.cs:43` | load an arbitrary same-repository binding | `C0824RefOnlyIdentityTests.Foreign_binding_refuses_before_git_or_file_io` | `foreign-binding-id-refused` |
| PC-22 | G-22; `new context resolver; AgentTaskLandSourceResolver.cs:57` | omit repository/common identity comparison | `C0824RefOnlyIdentityTests.Foreign_binding_refuses_before_git_or_file_io` | `foreign-repository-refused` |
| PC-23 | G-23; `new context resolver; AgentTaskLandSourceResolver.cs:46` | omit source full-ref comparison | `C0824RefOnlyIdentityTests.Foreign_binding_refuses_before_git_or_file_io` | `foreign-ref-refused` |
| PC-24 | G-24; `new context resolver; AgentTaskLandSourceResolver.cs:46` | omit identity-path comparison | `C0824RefOnlyIdentityTests.Foreign_binding_refuses_before_git_or_file_io` | `foreign-identity-refused` |
| PC-25 | G-25; `new context resolver; AgentTaskLandSourceResolver.cs:46` | omit selected base SHA comparison | `C0824RefOnlyIdentityTests.Foreign_binding_refuses_before_git_or_file_io` | `foreign-base-refused` |
| PC-26 | G-26; `new typed settings/default; WorkspaceSlotRoleGate.cs:17` | default Enabled=true with nonempty explicit lists in case | `C0824RefOnlyGateTests.Default_off_and_empty_lists_admit_nothing` | `default-disabled` |
| PC-27 | G-27; `new typed gate; RemoteWorkspaceService.cs:445` | treat empty RunnerIds as all | `C0824RefOnlyGateTests.Default_off_and_empty_lists_admit_nothing` | `empty-runners-refused` |
| PC-28 | G-28; `new typed gate; WorkspaceSlotRoleGate.cs:17` | parse none as all roles | `C0824RefOnlyGateTests.Default_off_and_empty_lists_admit_nothing` | `roles-none-refused` |
| PC-29 | G-29; `new typed gate; RemoteWorkspaceService.cs:445` | remove runner allowlist comparison | `C0824RefOnlyGateTests.Role_and_runner_both_must_match` | `runner-only-mismatch-refused` |
| PC-30 | G-30; `new typed gate; WorkspaceSlotRoleGate.cs:17` | remove role allowlist comparison | `C0824RefOnlyGateTests.Role_and_runner_both_must_match` | `role-only-mismatch-refused` |
| PC-31 | G-31; `RemoteWorkspaceService.cs:445` | remove Workspace==Worktree clause | `C0824RefOnlyGateTests.Mutation_source_landing_and_desktop_are_excluded` | `shared-readonly-refused` |
| PC-32 | G-32; `RemoteWorkspaceService.cs:445` | remove nonempty RunnerId clause | `C0824RefOnlyGateTests.Mutation_source_landing_and_desktop_are_excluded` | `desktop-refused` |
| PC-33 | G-33; `RemoteWorkspaceService.cs:445` | remove Mutation exclusion | `C0824RefOnlyGateTests.Mutation_source_landing_and_desktop_are_excluded` | `mutation-refused` |
| PC-34 | G-34; `RemoteWorkspaceService.cs:445` | remove SourceLanding exclusion | `C0824RefOnlyGateTests.Mutation_source_landing_and_desktop_are_excluded` | `source-landing-refused` |
| PC-35 | G-35; `new ref-only registration; WorkspaceSlotRoleGate.cs:17` | skip shape validation before binding | `C0824RefOnlyGateTests.Malformed_reload_closes_only_new_admission` | `invalid-startup-key-refused` |
| PC-36 | G-36; `new ref-only role parser; WorkspaceSlotRoleGate.cs:17` | accept case-folded unknown/numeric role tokens | `C0824RefOnlyGateTests.Malformed_reload_closes_only_new_admission` | `invalid-role-grammar-refused` |
| PC-37 | G-37; `new ref-only gate; WorkspaceSlotRoleGate.cs:17` | reuse last good enabled settings after OptionsValidationException | `C0824RefOnlyGateTests.Malformed_reload_closes_only_new_admission` | `reload-admits=false` |
| PC-38 | G-38; `WorkspaceUseAdmission.cs:19,70 (new context caller)` | use repository as every task identity key | `C0824RefOnlyIdentityTests.Path_equality_consumers_keep_task_and_runner_ownership_distinct` | `reservation-distinct` |
| PC-39 | G-39; `WorkspaceUseAdmission.cs:78` | remove branch-overlap predicate | `C0824RefOnlyIdentityTests.Path_equality_consumers_keep_task_and_runner_ownership_distinct` | `same-ref-fenced` |
| PC-40 | G-40; `AdvisoryChangePolicy.cs:61` | compare shared RepoPath for workspace inequality | `C0824RefOnlyIdentityTests.Path_equality_consumers_keep_task_and_runner_ownership_distinct` | `advisory-distinct-allowed` |
| PC-41 | G-41; `TerminalRunnerSeatReleaseService.cs:1222 (new mode arm)` | return not-ambiguous solely for missing desktop path | `C0824RefOnlyIdentityTests.Path_equality_consumers_keep_task_and_runner_ownership_distinct` | `same-generation-ambiguous` |
| PC-42 | G-42; `TerminalRunnerSeatReleaseService.cs:1237` | drop generation equality from slot ownership predicate | `C0824RefOnlyIdentityTests.Path_equality_consumers_keep_task_and_runner_ownership_distinct` | `next-generation-distinct` |
| PC-43 | G-43; `LandOperationFactory.cs:70 (new mode arm)` | run existing Directory.Exists/canonical arm for RefOnly | `C0824RefOnlyLandingTests.Late_land_uses_pinned_source_without_touching_decoy` | `no-source-cwd` |
| PC-44 | G-44; `LandOperationFactory.cs:83 (new mode arm)` | populate RefOnly GitDirectory from common/worktrees/leaf | `C0824RefOnlyLandingTests.Late_land_uses_pinned_source_without_touching_decoy` | `source-admin=null` |
| PC-45 | G-45; `AgentTaskLandService.cs:922 (new mode arm)` | probe RefOnly identity as source checkout | `C0824RefOnlyLandingTests.Late_land_uses_pinned_source_without_touching_decoy` | `publication-has-S with locked foreign decoy` |
| PC-46 | G-46; `AgentTaskLandingProtocol.cs:622,671 (new mode arm)` | capture SourceCanonical for RefOnly then run residue probe | `C0824RefOnlyLandingTests.Late_land_uses_pinned_source_without_touching_decoy` | `no-source-probe` |
| PC-47 | G-47; `AgentTaskLandingProtocol.cs:699 (new comparison)` | omit task mode comparison against operation | `C0824RefOnlyLandingTests.Verification_resume_binds_mode_and_binding` | `resume-refused:task-mode` |
| PC-48 | G-48; `AgentTaskLandingProtocol.cs:699 (new comparison)` | omit task binding comparison against operation | `C0824RefOnlyLandingTests.Verification_resume_binds_mode_and_binding` | `resume-refused:task-binding` |
| PC-49 | G-49; `PreparedLandingVerification.cs:8; AgentTaskLandingProtocol.cs:642 (new field)` | leave mode out of ticket comparison | `C0824RefOnlyLandingTests.Verification_resume_binds_mode_and_binding` | `resume-refused:ticket-mode` |
| PC-50 | G-50; `PreparedLandingVerification.cs:8; AgentTaskLandingProtocol.cs:642 (new field)` | leave binding id out of ticket comparison | `C0824RefOnlyLandingTests.Verification_resume_binds_mode_and_binding` | `resume-refused:ticket-binding` |
| PC-51 | G-51; `new registration guard; AgentTaskLandingProtocol.cs:615` | skip source-full-ref attached-registration check before verification | `C0824RefOnlyLandingTests.Foreign_source_registration_holds_without_mutation` | `source-registration-refused:initial` |
| PC-52 | G-52; `new registration guard; AgentTaskLandingProtocol.cs:643` | omit post-window source-full-ref check | `C0824RefOnlyLandingTests.Foreign_source_registration_holds_without_mutation` | `source-registration-refused:resume` |
| PC-53 | G-53; `AgentTaskLandSourceResolver.cs:91 (new guard)` | allow RecoverReviewedSource mode through RefOnly check | `C0824RefOnlyLandingTests.Recovery_and_interrupted_reset_refuse_before_any_write` | `ref_only_land_recovery_unsupported:reviewed; recovery-writes=0` |
| PC-54 | G-54; `AgentTaskLandSourceResolver.cs:91 (new guard)` | allow AdoptFromTask mode through RefOnly check | `C0824RefOnlyLandingTests.Recovery_and_interrupted_reset_refuse_before_any_write` | `ref_only_land_recovery_unsupported:adopt; recovery-writes=0` |
| PC-55 | G-55; `AgentTaskLandSourceResolver.cs:62,349,490 (new guard)` | allow source-adopt-reset continuation including already-adopted checkpoint | `C0824RefOnlyLandingTests.Recovery_and_interrupted_reset_refuse_before_any_write` | `ref_only_land_recovery_unsupported:resumed-reset; recovery-writes=0` |
| PC-56 | G-56; `LandOperationFactory.cs:26` | remove expected-S mismatch refusal | `C0824RefOnlyLandingTests.Main_and_land_guards_still_fence_ref_only_publication` | `target-unpublished:wrong-approved-S` |
| PC-57 | G-57; `AgentTaskLandingProtocol.cs:642` | omit Destination equality in ticket validation | `C0824RefOnlyLandingTests.Main_and_land_guards_still_fence_ref_only_publication` | `target-unpublished:endpoint-drift` |
| PC-58 | G-58; `AgentTaskLandService.cs:945` | skip main target checkout lock probe for RefOnly | `C0824RefOnlyLandingTests.Main_and_land_guards_still_fence_ref_only_publication` | `target-unpublished:main-lock` |
| PC-59 | G-59; `AgentTaskLandingProtocol.cs:665` | skip candidate index-lock guard for RefOnly | `C0824RefOnlyLandingTests.Main_and_land_guards_still_fence_ref_only_publication` | `target-unpublished:land-lock` |
| PC-60 | G-60; `AgentTaskLandingProtocol.cs:666` | omit land sequencer test | `C0824RefOnlyLandingTests.Main_and_land_guards_still_fence_ref_only_publication` | `target-unpublished:land-sequencer` |
| PC-61 | G-61; `AgentTaskLandingProtocol.cs:668` | ignore nonempty candidate status | `C0824RefOnlyLandingTests.Main_and_land_guards_still_fence_ref_only_publication` | `target-unpublished:land-dirt` |
| PC-62 | G-62; `TaskWorktreeRetirementService.cs:281` | omit only eligibility mode guard | `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | `desktop-not-required:eligibility` |
| PC-63 | G-63; `TaskWorktreeRetirementService.cs:120` | omit only operator release mode guard | `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | `desktop-not-required:operator-release` |
| PC-64 | G-64; `TaskWorktreeRetirementService.cs:104` | omit only system release mode guard | `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | `desktop-not-required:system-release` |
| PC-65 | G-65; `TaskWorktreeRetirementService.cs:317` | omit action-time mode recheck | `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | `desktop-not-required:retire-action` |
| PC-66 | G-66; `DesktopLegacyReleasePolicyService.cs:150` | include RefOnly in candidate query/classification | `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | `desktop-not-required:legacy-candidate` |
| PC-67 | G-67; `DesktopLegacyReleasePolicyService.cs:393` | omit precheck mode guard on stale selected candidate | `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | `desktop-not-required:legacy-precheck` |
| PC-68 | G-68; `DesktopLegacyReleasePolicyService.cs:190 (new recheck)` | omit post-inspection fresh-mode recheck | `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | `desktop-not-required:legacy-action` |
| PC-69 | G-69; `CardWorktreeCleanupService.cs:100` | create Pending local endpoint for RefOnly | `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | `desktop-not-required:card-discovery` |
| PC-70 | G-70; `CardWorktreeCleanupExecutor.cs:47` | omit initial executor mode guard | `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | `desktop-not-required:card-admission` |
| PC-71 | G-71; `CardWorktreeCleanupExecutor.cs:37` | omit replay mode guard for old operation | `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | `desktop-not-required:card-replay` |
| PC-72 | G-72; `CardWorktreeCleanupExecutor.cs:90` | omit fresh-mode recheck before intent | `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | `desktop-not-required:card-intent` |
| PC-73 | G-73; `WorktreeResidueSweepService.cs:157` | omit RefOnly settled-lane exclusion | `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | `desktop-not-required:residue-settled` |
| PC-74 | G-74; `WorktreeResidueSweepService.cs:265` | omit RefOnly publication-lane exclusion | `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | `desktop-not-required:residue-publication` |
| PC-75 | G-75; `WorktreeResidueSweepService.cs:611` | project every task as Checkout | `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | `desktop-not-required:residue-projection` |
| PC-76 | G-76; `WorktreeHealthService.cs:52` | project RefOnly as Checkout for classification | `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | `desktop-not-required:health` |
| PC-77 | G-77; `AgentTaskLandingProtocol.cs:431` | fall through to worktrees.TryRemoveAsync for RefOnly | `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | `desktop-not-required:land-cleanup` |
| PC-78 | G-78; `Infrastructure/Git/WorktreeGuardedCleanup.cs:16` | omit RefOnly defense in direct cleanup entry | `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | `desktop-not-required:guarded-cleanup` |
| PC-79 | G-79; `AgentTaskLandingProtocol.cs:431 (new mode arm)` | delete owned SourceFullRef while returning NotRequired | `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | `source-ref-and-pins-retained` |
| PC-80 | G-80; `new cleanup component provenance; AgentTaskLandingProtocol.cs:465` | set DirectoryRemoved=true to represent no-op | `C0824RefOnlyCleanupTests.All_desktop_consumers_skip_ref_only_without_deleting_refs` | `removed-flags=false; mode-provenance=RefOnly` |
| PC-81 | G-81; `TaskWorktreeRetirementService.cs:281 (new shortcut)` | skip all Checkout guards whenever directory absent | `C0824RefOnlyCleanupTests.Legacy_missing_and_dirty_checkouts_keep_their_guards` | `legacy-kind=Checkout; no-false-NotRequired` |
| PC-82 | G-82; `CardWorktreeCleanupService.cs:118` | continue task loop before independent mirror binding discovery | `C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation` | `remote-receipt-and-image` |
| PC-83 | G-83; `TaskWorktreeRetirementService.cs:415,505 (new mode arm)` | return before independent eligible remote cleanup | `C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation` | `remote-action-reached` |
| PC-84 | G-84; `CardWorktreeMirrorCleanupService.cs:154` | omit binding.TaskId/Attempt comparison; change only TaskId in fixture | `C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation` | `wrong-binding-preserved` |
| PC-85 | G-85; `CardWorktreeMirrorCleanupService.cs:199` | omit live store comparison on replay | `C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation` | `wrong-store-preserved` |
| PC-86 | G-86; `RemoteWorkspaceService.cs:1121` | remove unreleased-slot early return | `C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation` | `slot-bytes-preserved; no-remove-command` |
| PC-87 | G-87; `CardWorktreeMirrorCleanupService.cs:48` | replace OperationId with fresh id on replay | `C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation` | `same-operation-replay` |
| PC-88 | G-88; `CardWorktreeMirrorCleanupService.cs:216` | accept Complete=true without matching receipt | `C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation` | `bad-receipt-not-imported` |
| PC-89 | G-89; `AgentTaskLandService.cs:1534` | skip AddNotification for RefOnly outcomes | `C0824RefOnlyLandingTests.Land_outcome_reaches_caller_via_queue_and_recovers_handoffs` | `caller-whole-userprompt:terminal-commit-cut` |
| PC-90 | G-90; `AgentTaskLandNotificationService.cs:98` | return before linking an existing keyed row after restart | `C0824RefOnlyLandingTests.Land_outcome_reaches_caller_via_queue_and_recovers_handoffs` | `caller-whole-userprompt:insert-before-link` |
| PC-91 | G-91; `AgentTaskLandNotificationService.cs:102` | omit existing.AgentSessionId comparison | `C0824RefOnlyLandingTests.Land_outcome_reaches_caller_via_queue_and_recovers_handoffs` | `wrong-destination-unconfirmed` |
| PC-92 | G-92; `AgentTaskLandNotificationService.cs:102` | omit existing.ContentDigest comparison | `C0824RefOnlyLandingTests.Land_outcome_reaches_caller_via_queue_and_recovers_handoffs` | `wrong-digest-unconfirmed` |
| PC-93 | G-93; `AgentTaskLandNotificationService.cs:184` | omit flush wakeup after committed queue link | `C0824RefOnlyLandingTests.Land_outcome_reaches_caller_via_queue_and_recovers_handoffs` | `caller-whole-userprompt:already-idle` |
| PC-94 | G-94; `AgentTaskLandNotificationService.cs:115 (recovery arm)` | omit actionable recovered-row flush | `C0824RefOnlyLandingTests.Land_outcome_reaches_caller_via_queue_and_recovers_handoffs` | `caller-whole-userprompt:busy-recovery` |
| PC-95 | G-95; `AgentTaskLandNotificationService.cs:272` | confirm Sent row before scanning transcript | `C0824RefOnlyLandingTests.Land_outcome_reaches_caller_via_queue_and_recovers_handoffs` | `sent-without-prompt-unconfirmed` |
| PC-96 | G-96; `LandNoteReceipt.cs:38` | omit session filter | `C0824RefOnlyLandingTests.Land_outcome_reaches_caller_via_queue_and_recovers_handoffs` | `wrong-session-unconfirmed` |
| PC-97 | G-97; `LandNoteReceipt.cs:78` | drop IsCompleteIn while retaining identity check | `C0824RefOnlyLandingTests.Land_outcome_reaches_caller_via_queue_and_recovers_handoffs` | `truncated-middle-unconfirmed` |
| PC-98 | G-98; `LandNoteReceipt.cs:43` | use >= instead of > for baseline sequence | `C0824RefOnlyLandingTests.Land_outcome_reaches_caller_via_queue_and_recovers_handoffs` | `equal-floor-unconfirmed` |
| PC-99 | G-99; `LandNoteReceipt.cs:47` | omit timestamp floor on null sequence baseline | `C0824RefOnlyLandingTests.Land_outcome_reaches_caller_via_queue_and_recovers_handoffs` | `old-time-unconfirmed` |
| PC-100 | G-100; `AgentTaskLandNotificationService.cs:366 (recovery path)` | clear delivery attempt evidence after receipt save failure | `C0824RefOnlyLandingTests.Land_outcome_reaches_caller_via_queue_and_recovers_handoffs` | `no-retype-after-receipt` |
| PC-101 | G-101; `DelegationWorktreeService.cs:414 (reuse arm)` | overwrite WorktreeBaseSha from current default tip on reuse | `DelegationWorktreeTests.C508_ReuseKeepsRecordedBase` | `WorktreeBaseSha==recordedSha` |
| PC-102 | G-102; `DelegationWorktreeService.cs:370 (owned lease validation)` | omit ownership check for supplied foreign lease | `DelegationWorktreeTests.C508_BaseResolutionRequiresOwnedLease` | `recording.CreateCalls==0 in foreign-lease case` |
| PC-103 | G-103; `new typed gate; RemoteWorkspaceService.cs:445` | omit Enabled predicate while keeping defaults unchanged | `C0824RefOnlyGateTests.Default_off_and_empty_lists_admit_nothing` | `explicit-disabled-refused` |
| PC-104 | G-104; `AgentTaskLandSourceResolver.cs:175 (new mode arm)` | store common/worktrees/leaf in observed RefOnly checkpoint | `C0824RefOnlyLandingTests.Late_land_uses_pinned_source_without_touching_decoy` | `request-source-admin=null` |

Guard audit: **guards=104, mapped=104, missing=0, duplicate PC maps=0**. All 104 PCs must run after land; executed in this design-only stage: **0**. Pending is not a mutation-clean claim.

### Out of scope

- S5b-2's actual ref CAS/sync/debt/park/report/artifact/decision/child/control paths,
  and S5b-3's ref creation/push/baseline/dispatch/deny-hook producer: no new S5b-1
  caller may dispatch RefOnly. D-4 zero-old/CAS, ancestry and publication controls
  belong to those slices, not to a fake ref mutator in this one. S5b-1 persists
  binding preparation state but does not advance it by Git. A new source mutator
  discovered in Code requires Plan/TestDesign extension before implementation.
- Consumer-table disposition, explicitly: reservations/advisory/seat -> I7;
  land/cleanup/health/residue -> L1..L6,C1..C3; typed context separation -> I2..I6.
  Base/sibling/repair-base readers -> unchanged Checkout R-1/R-2 now and production
  RefOnly caller tests in S5b-3. Authentication/default child/report/reroute/merge
  helper, session/control reconstruction, file decision grants, Home/Zombie display,
  Check/reply progress, parked resume, blocked/sync recovery, canonical report and
  deliverable blob readers -> S5b-2's named caller tests. Session.WorkingDirectory/
  RunnerCwd, prompt spill and launch-spec construction -> S5b-3. These callers do
  not receive a newly produced RefOnly task in S5b-1; typed context availability
  is not evidence that they have migrated. No S5b-3 activation before their witnesses.
- Live Windows execution is unavailable and expressly forbidden during the active
  qualification. All S5b-1 identity/land/cleanup tests except C2 are portable on
  fixture-owned paths; C2 uses an explicitly Linux-only fixture. No new Windows
  test/skip is credited here. S5b-3 CP-10 owns actual Windows normalization/case,
  late-land/decoy and restart native witnesses; portable path strings do not prove
  Windows filesystem semantics. S4a native cleanup/ADS/reflog safety repairs stay
  independently required and operationally OFF.
- No production canary, setting activation, migration of legacy task modes,
  bulk cleanup, ref garbage collection, slot/parking policy change or real CLI
  provider transport qualification. No master/rc publication or deployment here.
- No full server/Pty/provider/rc suite. Direct graph is Antiphon.Tests -> server ->
  Pty/Messaging/Messaging.Client/SessionRunner.Contracts/Resilience/Observability,
  with SessionRunner and fixture tools referenced by the test project. This slice
  changes server model/services/settings and selects their direct caller batches;
  no provider implementation or wire-schema change. If Code changes contracts,
  enumerate downstream consumers and revise scope before taking qualification credit.

### Checkpoints

This table **refines only parent S5b-1 CP-1..CP-3**, leaving the parent's fix design
and later slices intact. It is the closed Code ordinary roster: **20 new methods +
2 retained methods = 22 executions**, zero failed/skipped. Parent CP-1 gains I7;
parent CP-2 gains L5/L6 and is split to isolate caller delivery, remote cleanup and
portable methods. New classes must contain exactly the frozen methods. If reflection
or prefix matching discovers extra methods, compare fresh TRX fully qualified
Class.Method identities and refuse widened membership; update the manifest before
running. Min is an executed-method floor, never assertion-loop count or minutes.

One isolated build (CP-1) reused at the **same clean committed source SHA** by each
later row, one exact filter per row. No concurrent test hosts; all rows Serial.
C2 must run on Linux; never treat its Windows skip as green. No hard-coded host
pin in portable rows and nothing runs on the desktop for this task. The already
authorized implementation location is server2.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S5b-1 | tests/Antiphon.Tests -> bin-c0824-s5b1/ | server-identity-gate | /*/Antiphon.Tests.Application/(C0824RefOnlyIdentityTests*)\|(C0824RefOnlyGateTests*)/* | V-1,V-2,V-3,R-5,R-6; I1..I7,Q1..Q4 | exactly 11 frozen methods; 0 failed/skipped | 11 | 10 | true | TEMP=/tmp |
| CP-2 | S5b-1 | CP-1 | server-land | /*/Antiphon.Tests.Application/C0824RefOnlyLandingTests*/(Late_land_uses_pinned_source_without_touching_decoy*)\|(Verification_resume_binds_mode_and_binding*)\|(Foreign_source_registration_holds_without_mutation*)\|(Recovery_and_interrupted_reset_refuse_before_any_write*)\|(Main_and_land_guards_still_fence_ref_only_publication*) | V-4,R-4; L1..L5 | exactly 5 frozen methods; 0 failed/skipped | 5 | 4 | true | TEMP=/tmp |
| CP-3 | S5b-1 | CP-1 | server-desktop-cleanup | /*/Antiphon.Tests.Application/C0824RefOnlyCleanupTests*/(All_desktop_consumers_skip_ref_only_without_deleting_refs*)\|(Legacy_missing_and_dirty_checkouts_keep_their_guards*) | V-6,R-3; C1,C3 | exactly 2 frozen methods; 0 failed/skipped | 2 | 5 | true | TEMP=/tmp |
| CP-4 | S5b-1 | CP-1 | server-runner-cleanup-linux | /*/Antiphon.Tests.Application/C0824RefOnlyCleanupTests*/Remote_cleanup_still_uses_its_binding_and_generation | V-7,R-6; C2 | exactly 1 method; Linux real queue/receipt; 0 failed/skipped | 1 | 4 | true | TEMP=/tmp |
| CP-5 | S5b-1 | CP-1 | server-land-caller-receipt | /*/Antiphon.Tests.Application/C0824RefOnlyLandingTests*/Land_outcome_reaches_caller_via_queue_and_recovers_handoffs | V-5; L6 | exactly 1 method; all cuts + busy/ready UserPrompt; 0 failed/skipped | 1 | 4 | true | TEMP=/tmp |
| CP-6 | S5b-1 | CP-1 | server-legacy-base | /*/Antiphon.Tests.Application/DelegationWorktreeTests*/(C508_ReuseKeepsRecordedBase*)\|(C508_BaseResolutionRequiresOwnedLease*) | R-1,R-2 | exactly 2 retained methods; 0 failed/skipped | 2 | 1 | true | TEMP=/tmp |

Execution uses `tools/Antiphon.Checkpoints` run/import/wait, not dotnet test.
Import this artifact to ignored `.antiphon/checkpoints/<run>/manifest.yaml`; set
`build.properties.UseAppHost: false`, `build.slotWaitMinutes: 12` and the measured
finite budgets below before `run <manifest> --expected-source-sha <C>`.
Use explicit `--total-timeout 56m --hard-wall-timeout 140m` for the whole selection
**only after** the performance Debug disposition below; single-row staging is
preferred if that disposition requires it. Tool-owned isolated cold outputs are
permitted; direct builds and mutation builds use native bin/obj only, each through
`scripts/build-slot.ps1`, with UseAppHost=false on both build and no-build run.
Never bypass timeout exit 4 or use -NoSlot. Commit assertion-red witnesses before
fixes; commit source before the long run. Wait for terminal status (75 is pending),
retain unedited CHECKPOINT lines, TRX roster/counts and validated clean SHA/build
receipts; Code/Review run check-evidence-diff.ps1 over their recorded base..HEAD.

Performance refinement (all new durations **estimated/provisional**, CARD-0824):
CP-1 is 6min cold build + 4min identity/gate execution; other rows are 4/5/4/4/1min
execution including their host startup/teardown. Each test selection is under
~10min expected charged execution; CP-1 build contention can exceed that and must
be reported separately, not hidden by widening the case timeout. Initial build
cap 20min, row test caps CP-1 8min, CP-2 8min, CP-3 10min, CP-4 8min, CP-5 8min,
CP-6 2min (about twice estimated test durations). The tool importer derives 30/15min
row defaults; **those are not R5 budgets**. In the imported manifest set per-row
`timeoutMinutes` to 8/8/10/8/8/2 and `timeouts.rowMinutes: 20`. These are actual
schema fields: `RunScheduler.BuildDeadlineMinutes` takes max(15,
timeouts.rowMinutes, selected row deadlines), so the one shared build cap is
**20min**, independent of the 8min CP-1 test cap. Do not pass --row-timeout, which
would override both phases. This source inspection closes the phase-budget seam.
No broad timeout inflation to hide assertion red. The total charged cap is a
provisional 2x 28min; its 140min hard wall adds the separate 84min admission cap.

Pure gate cases initially 2s body; JSON reload Q3 15s (existing owner fixture
10s poll/debounce bound, provisional exception); ordinary Git cases 40s, multi-step
identity/land 60s. C1/C2/L6 aggregate intentional matrices need bounded per-variant
40s/60s and whole-method caps 10min/8min/8min respectively, provisional CARD-0824:
these are aggregate recipes, not a new 8min timeout on one Git call. Collect first
red/green timings and split exact methods/CP rows through a TestDesign amendment
if they exceed the packed budget. Existing R-1/R-2 retain their current budgets.
Git/DB/HTTP calls and output drains use existing cancellable drivers with initially
10s/5s/5s limits and 15s teardown; no abandoned child survives a red/green leg.

R1: six rows plus seven build/test slot acquisitions can exceed 30min including
admission; the 104-PC battery certainly exceeds it. **Before scheduling a complete
Code/Review or Mutation battery the caller must commission a bounded CARD-0824
Debug performance task** with these exact filters, C/L SHA, host/load, known slot
waits and build/test/DB/queue phase timings, as the parent Plan already requires.
This is not a permission question or authority to run on desktop. Code authoring,
red witnesses and individually bounded rows can proceed under the resulting
packing. Do not schedule a 30-hour undifferentiated run. No Debug is dispatched by
this delegate and no measured p95 is invented.

Share TestDbFixture/template/container/migration readiness once per host. Use
per-method clones, never migrate a container per class/test; I1's migration subject
is the explicit exception and records upgrade time. Reuse immutable Git seed data;
each variant owns writable refs/files. Extend existing harness attachment options
to share one loopback host per test host; reconnect/rebuild state only at restart
cuts. Do not repeat fixture startup per variant when the variant can reset cloned
DB/task state. Apply assembly-local ParallelLimiter<ProcessSpawnLimit> to all new
process-spawning classes (the conservative limiter covers their helper closure);
no concurrent Antiphon.Tests/Pty/FakeClaude run.

Retain TestTiming/TRX and roster evidence under ignored
`.antiphon/checkpoints/<run-id>/`: source/host/platform/category/group, per-method
p50/p95/max/sample count, build and slot wait, DB/container/readiness/seed/clone,
loopback startup, body, teardown, individual Git/HTTP/DB/filesystem calls and
unattributed remainder. Unknown is unknown. No repeated green benchmark battery;
first required red/green provides bounded collection. Parent cited build/Git
historical comparators have **not** had their full receipts rebound here, so this
artifact treats them as context, not measured budgets for these new tests.

### Cost

All numbers here are **estimated planning floors/reservations**, not paid or
measured runtime. Ordinary Code V/R floor is the table sum:
**10 + 4 + 5 + 4 + 4 + 1 = 28min**. This includes one estimated 6min isolated build
and 22min test/setup/teardown; no extra build is added to that sum. No tests/builds
were run by TestDesign; only cheap importer checks. Authoring and independent
Review are separate; an ordinary Review uses the same 28min execution scope.

Mutation keeps exact-method filters `/*/*/<Class>*/<Method>` from the PC table,
Min=1 per leg. No whole-class PC run. Budget one 6min incremental/native build
per mutated red and restored green leg (2 builds/control), 0.5min patch/bind/
restore accounting/control, plus the following method execution reservations:

| PC selection (all exact case aliases expand to frozen Class.Method) | Controls | Red minutes each | Restored green minutes each | Build + patch + red + green subtotal |
|---|---:|---:|---:|---:|
| I1..I7 + Q1..Q4 | 43 | 1 | 1 | 43 x (12 + 0.5 + 1 + 1) = 623.5min |
| L1..L5 | 20 | 2 | 2 | 20 x 16.5 = 330min |
| L6 | 12 | 4 | 4 | 12 x 20.5 = 246min |
| C1 | 19 | 5 | 5 | 19 x 22.5 = 427.5min |
| C2 | 7 | 4 | 4 | 7 x 20.5 = 143.5min |
| C3 | 1 | 2 | 2 | 16.5min |
| B1/B2 (R-1/R-2 exact methods) | 2 | 1 | 1 | 29min |

Controls total **104**; cycles subtotal **1,816min** = 1,248 build + 52
patch/restore + 516 red/green execution minutes. Before mutation, establish clean
L method-scoped greens: one shared 6min native build + 38min summed per-method
reservations (11 I/Q x1; five L1..L5 x2; L6 x4; C1 x5; C2 x4; C3 x2; B1/B2 x1)
= **44min**. Reuse those baseline greens at unchanged L; do not pay one cold build
for every baseline method. **Mutation floor = 44 + 1,816 = 1,860min (31h00m)**,
including binding/discovery/restoration, before admission and reporting. Conservative
per-method reservations deliberately exceed average packed V/R method times.

**Combined one Code V/R + post-land Mutation = 28 + 1,860 = 1,888min (31h28m)**,
of which builds total 6 + 6 + 1,248 = 1,260min. Add independent Review 28min if
budgeting the whole ordinary pipeline: **1,916min (31h56m)**, before authoring,
queue waits, fixes or native later-slice acceptance. These costs expose the large
consumer/control matrix; they do not claim a short cheap mutation stage.

Admission is separate: 12min finite maximum/acquisition; ordinary has one build
plus six row acquisitions, **84min maximum admission** (cited current expected
5–10min each -> 35–70min ordinary wait). Mutation direct baseline/208 red/green
builds and 230 method test invocations each lease separately; budget 439
acquisitions, **5,268min admission ceiling** (actual waits must be measured; do
not interpret the ceiling as expected runtime). Hard-wall budgets are set per
bounded run, with no shared lease around a mutant loop. Exit 4 stays not-run.

Savings: reuse one isolated build in six ordinary rows saves **5 x 6 = 30min**
versus rebuilding each row. One shared baseline mutation build saves
**21 x 6 = 126min** versus one build per baseline method. No speculative mutation
batching savings credited: most controls share files/methods, and each changed
source needs a fresh build. Only genuinely independent files AND methods may batch
under the mutation owner policy, with per-PC intended-red evidence. Full suite
cost is not a measured comparable here; no unsupported percentage saving claimed.

Before handoff: guards=104, mapped=104, missing=0, duplicate PC maps=0; **all 104
PCs scheduled as mandatory post-land work, none run here**. The frozen methods,
assertion labels and mutation payloads above require exact Code/L binding and
Review audit. No claim of executed V/R, mutation green or native qualification.

### Binding and import handoff

Code must first make additive CLR/API scaffolding compile while preserving the old
behavior (no migration/mapping or consumer fix in that scaffolding). Commit the
first behavioral witnesses assertion-red: I1 first checks EF metadata/schema
before a new-column insert so missing mapping is an assertion, not a setup SQL
exception; Q2's conservative admission scaffold returns false and fails
`both-match=true`; context positive cases fail the expected typed coordinates;
land/cleanup witnesses run actual old callers. Then implement and run the closed
ordinary table. Retained negatives Q1/Q4/C3/R-1/R-2 are explicitly allowed to be
base-green; they are not misreported as new red witnesses. No reflection test may
silently return when a member is missing. Preserve red assertion receipts and
compile/discovery counts; the absence of a future type is not evidence.

Each independently tested guard needs reachability independent of another guard:
I6 corrupts only the durable binding dimension under test and keeps request/task
snapshots and other binding dimensions consistent; for stale cleanup actions,
seed/claim the old row directly and enter the public action without repeating
candidate discovery. Instrument the tested boundary with existing callback/trace
seams (or a narrow internal callback if none exists). Record `reached:<guard>`
before evaluating it; when the mutant removes the evaluation, its neighbor trace
must show the unsafe arm was reachable. A test that only observes an earlier
refusal must be repaired before the PC is considered bound.

L6 adds distinct recovered-key variants for PC-91/PC-92: preinsert the note's key
with **only** destination or digest changed, keep the complete immutable body and
attempt floor otherwise valid, and reconcile via the producer's outbox. Assert
link/confirmation absent; do not let body mismatch mask the digest check. Reuse
`AgentTaskLandNotificationRecoveryTests.cs:197`'s existing recovery seam. The
positive receipt still comes from actual default OnSubmitted delivery. The
historical matcher accepts complete QueuedUserPrompt for nonlegacy land lifecycle
notes; this slice preserves that policy. This design requires a UserPrompt as its
end-to-end evidence and does not add a test that falsely demands the historical
matcher reject an otherwise valid QueuedUserPrompt.

C2 has **two independent entry subrecipes**: (a) the completed-card mirror flow
specified above, and (b) retirement's independent RemoteWorkspaceService arm using
`RemoteWorkspaceService.cs:47` with the fixture's `Wire.Directory`, real connected
phone-home client, and `TaskWorktreeRetirementService.cs:505`. Extend
CompletedCardMirrorFixture's configuration seam to register this concrete service
in DelegationTestServices, then invoke the production retirement public path on a
RefOnly task with desktop NotRequired. Assert the remote action is actually
reached (PC-83), and inspect recipient bytes/registrations after the real
WorkspaceRemove operation. This is different from the card removal receipt
protocol; its response alone cannot prove removal. A slot negative calls the
same service with an unreleased slot tuple and verifies zero recipient removal
and retained physical bytes. Slot-generation drift is exercised through existing
conditional release/ownership code, not by adding generation to WorkspaceRemove.
Keep WIRE's protected sentinel for the refusal variants; an eligible positive
uses preserved/report-qualified contents that the existing remote policy permits.
No claim that one remote producer test covers the other entry point.

Cheap syntax/import validation only (no SDK builds, discovery or test executions):
use an already built local checkpoint CLI, `import --plan <artifact> --repo-root
/work/worktrees/task-05081946 --out .antiphon/checkpoints/05081946-lint/<name>.yaml`.
The local prebuilt executable used here is
`/tmp/claude-1654/-work-worktrees-task-a2cfcdc1/99fce261-fb7b-4ec8-b78d-86c1d6a3d216/scratchpad/tool-head/Antiphon.Checkpoints.dll`.
It is a syntax-only substitute with unbound build provenance, not executable
verification credit. The Plan importer source at this task baseline was also
read. Parent plan import: exit 0, 10 rows, warnings only for S5b-3 CP-10's 48min
derived timeout and its host-distinct build (do not combine Windows/Linux builds
to silence it). This artifact: exit 0, 6 rows, no warnings. Initial `validate
--plan` was unsupported by that older binary (`missing --evidence`); corrected
to the actual no-build `import` verb. Initial exact method OR operands were
rejected until every OR operand received the importer's required trailing `*`;
final filters retain frozen-roster equality checks. Generated YAML stays ignored.
No tool test or product build was started, and no bin/obj output needs deletion.

For PC-93/PC-94, warm the completion worker through its initial recovery sweep
before creating the land result, and keep its next periodic sweep outside the
case's bounded interval. Already-idle delivery must be triggered by the producer's
actual CompletionNoteFlushQueue wakeup and
Infrastructure/Orchestration/CompletionNoteWorkHostedService.FlushAsync, never by
a direct test call to FlushIfIdleAsync. In the recovered-key variant, begin busy,
lose the wakeup, end the modeled turn while the delivery worker is stopped, then
restart/reconcile with the recipient already eligible; the recovered-row wakeup
must deliver. Do not inject an extra turn-end flush that masks this recovery arm.
Record reached queue intake AND the complete UserPrompt; queue intake alone is
not the delivery verdict. All owned hosted services are stopped and awaited.

L6's negative floor variants include equal sequence, null sequence baseline with
old timestamp, and absent both floors, plus wrong recovered-key destination and
digest. C2 includes `AlterReceipt` returning Complete=true with a changed exact
receipt field; assert `bad-receipt-not-imported` and retain recipient journal/
image evidence separately from the untrusted response. These variants make their
individual controls load-bearing rather than incidentally protected by another
comparison.
