# CARD-1213 test design: branch compatibility and git-common

Date: 2026-10-13. Task: bb8f5555. Bound task token present (value not read or printed).
Design only: no production/test edits, builds, discovery runs or test executions in this stage.
Parent fix design: [2026-10-13-card-1213-branch-names-and-git-common.md](2026-10-13-card-1213-branch-names-and-git-common.md), commit 7a5476b36a85389f2d358383ff410765a66a243e.
This companion appends verification obligations; it does not replace the fix design.
OD-1..OD-6 use the recommendations from the brief. Mutation is paused: PCs below are specified, **pending**, and not claimed executed.

S1 (60 minutes, two commit groups: contracts/server, then runner/gate) must close CP-1..CP-8 before S2 begins. Its closure includes the shared parser/regex, capability advertisement and mint gate, OwnedBranch plus five consumers, runner publish/park, cleanup/retirement, scan glob and dispatch adoption. No partial new-name mint rollout before those consumers accept both forms. S2 (45 minutes) closes CP-9..CP-10; S3 (30 minutes) closes CP-11..CP-12. These are estimated authoring-plus-ordinary-check budgets, excluding host queue time. Preserve the plan's commit-group order inside S1; land S1 before starting S2.

## Verification design

### Inspection

Bodies read at the parent SHA (paths below are relative to the checkout):

- Application tests: WorktreeHealthServiceTests (Classify, sweep/upsert/clear and RecordingWorktreeManager), WorktreeResidueSweepTests (classify/execute, SeedWorktreeTaskAsync), WorktreeResidueRegistrationTests (three scheduler bodies) | scan/DTO/residue -> V-3/R-3; unchanged scheduler retained as regression only.
- Infrastructure WorktreeManagerTests.cs: parser, create, registered/dangling scan, GitTestEnvironment; WorktreeManagerStartRefLocalOnlyTests (both methods) | explicit branch, directory, parser and glob -> V-1,V-3/R-1,R-3. File has three top-level classes, not one.
- CardDoneWorktreeCleanupTests: UniqueOrdinaryOwner, terminal/generation/intent bodies; TaskWorktreeRetirementTests: ReleasedTerminalTasksRetire, UniqueFullOwner, World; helpers CompletedCardCleanupFixture.CreateAsync and SettledRemovalHarness.CreateAsync/SeedRetirementAsync | exact owner/destructive boundary -> V-4/R-4.
- RemoteWorktreeMirrorTests push/sync bodies and ScriptedGit; RunnerSettlementSyncTests Pushed_tip_fast_forwards_exact_owned_checkout, Rejects_foreign_task_branch_before_fetch, LocalMirrorPublisher and SyncWorld; TaskCompletionProgressPolicyTests Prepare-adjacent policy/World and FakeTaskProgressGit; SettlementSyncDebtPolicyTests eligibility/ref cases | stored ref and real Git -> V-5/R-5. The progress test must call PrepareAsync: EvaluateAsync alone misses the changed fullRef.
- AgentTaskDispatchBaseGuardTests T0442_V25 and seed/provider helpers, its SiblingWarnings partial census; WorktreeStartRefDispatchTests C613_StartRefCreatesOwnBranchAndBaseline/C613_ReusedWorktreeKeepsRecordedBase; RepairSourceWorld creation/DI; RemoteVerificationWorkspaceTests all four methods and FakeTransport | mint/adoption/snapshot -> V-2/R-2.
- RunnerWorkspaceServiceTests publish bodies and Scratch; WorkspaceParkPublicationTests publication/lease barrier and World; RunnerWorkspaceBindingTests legacy and bound Request helper | runner ownership -> V-6/R-6.
- TaskParkPublicationTests publication modes and PublicationWorld; TaskParkProofWire socket pump; PhoneHomeMirrorJoinTests idle/busy/crash bodies; PhoneHomeTestHost registration/connection; PhoneHomeLiveConnection.RequestAsync and PhoneHomeCommandDispatcher.MutateAsync | V-7/R-7. Existing Join fixture uses FIFO/poll sleeps; copy its transport topology, not those waits.
- InstructionBundleTests catalog/hash/render/order/style/append/budget/role bodies; DelegateBundleLaunchTests BuildLaunchSpec, SpecOf/CreateHarness and provider payload checks; DelegateScriptKindTests payload test and runner helper; DelegationUnitTests.cs Runner_worktree_brief_names_mirror_and_fast_forward_contract | V-8..V-10/R-8..R-10. Formatter class is DelegationReportFormatterTests, not DelegationUnitTests.
- Production bodies: DelegationWorktreeService mint/adoption; WorktreeManager scan/parser; RemoteWorkspaceService.OwnedBranch/SyncCoreAsync; PhoneHomeRunnerMirrorPublisher; TaskCompletionProgressService.PrepareAsync; SettlementSyncDebtPolicy.Eligible; LocalTaskParkPublisher.InspectAsync; cleanup/retirement identity guards; health ToDto; dispatcher ownBranch; runner publish/park/capabilities; composer/catalog/ForDelegate | guard inventory below. Owners read: project-context, testing-and-build checkpoint/filter/mutation/delivery rules, orchestration stage/brief rules, session-runtime publication invariant, Bundles README.

Reachability corrections: health Classify consumes entry.ShortId directly; TryShortFromBranch is reached by ToDto for a finding with null TaskId. Use an orphan sweep DTO, not merely a healthy Classify call. The planned regex's $ can admit a final LF in .NET: require full-input consumption while preserving D-4's accepted language. The legacy/new languages cannot overlap because task is not digits; the disjointness case pins both near-candidates. Review currently precedes basics in ForDelegate: T24 must use that production composition and require common text before the override (a placeholder positioned in stage-review can satisfy this without reversing the role map). LocalTaskParkPublisher has a binding TaskId/FullRef, not an AgentTask: test that boundary directly; do not fabricate stored-branch ownership from TaskId alone.

Fresh census on 7a5476b36a85389f2d358383ff410765a66a243e: source [Test] methods with literal [Arguments] expanded; no tests/build/discovery executed. Partial AgentTaskDispatchBaseGuardTests includes both files. Methods/results in this census are separate quantities; internal loops are one result. Inspection found no dynamic argument sources in these selections. Runtime TRX remains the acceptance evidence; a count alone cannot prove the intended methods ran.

| Selected class | Existing methods | Existing results | New single-result methods | Expected results after slice |
|---|---:|---:|---:|---:|
| DelegateBranchNameTests | 0 | 0 | 9 | 9 |
| WorktreeManagerTests | 5 | 10 | 0 | 10 |
| WorktreeManagerSafetyTests | 3 | 3 | 0 | 3 |
| WorktreeManagerGitIntegrationTests | 15 | 15 | 2 | 17 |
| WorktreeManagerStartRefLocalOnlyTests | 2 | 2 | 0 | 2 |
| WorktreeHealthServiceTests | 6 | 6 | 1 | 7 |
| WorktreeResidueSweepTests | 14 | 14 | 1 | 15 |
| WorktreeResidueRegistrationTests | 3 | 3 | 0 | 3 |
| CardDoneWorktreeCleanupTests | 18 | 18 | 2 | 20 |
| TaskWorktreeRetirementTests | 17 | 38 | 2 | 40 |
| WorktreeRetirementRaceTests | 44 | 54 | 0 | 54 |
| RemoteWorktreeMirrorTests | 8 | 8 | 1 | 9 |
| RunnerSettlementSyncTests | 29 | 29 | 1 | 30 |
| SettlementSyncDebtPolicyTests | 12 | 30 | 1 | 31 |
| TaskCompletionProgressPolicyTests | 26 | 33 | 1 | 34 |
| AgentTaskDispatchBaseGuardTests | 43 | 87 | 5 | 92 |
| WorktreeStartRefDispatchTests | 10 | 30 | 0 | 30 |
| RemoteVerificationWorkspaceTests | 4 | 4 | 0 | 4 |
| DelegateBranchDeliveryTests | 0 | 0 | 3 | 3 |
| TaskParkPublicationTests | 3 | 3 | 1 | 4 |
| RunnerWorkspaceServiceTests | 33 | 33 | 2 | 35 |
| WorkspaceParkPublicationTests | 3 | 3 | 1 | 4 |
| RunnerWorkspaceBindingTests | 10 | 19 | 1 | 20 |
| InstructionFragmentTests | 0 | 0 | 7 | 7 |
| InstructionBundleTests | 54 | 74 | 4 | 78 |
| DelegateBundleLaunchTests | 15 | 23 | 1 | 24 |
| DelegateScriptKindTests | 38 | 46 | 0 | 46 |
| DelegationReportFormatterTests | 71 | 93 | 0 | 93 |

Parent-filter audit: CP-1 has no existing class (planned new file); CP-2 selects only 12 current results and misses the GitIntegration/Safety classes; corrected below. Parent CP-3=23, CP-4=110, CP-5=100, CP-6=121, CP-7=55, CP-8=74, CP-9=69 current results. Parent CP-10 selects 74 InstructionBundle results and zero DelegationUnitTests: no class by that name exists; corrected to DelegationReportFormatterTests and split from the contract rerun. Trailing wildcards on each OR operand are required by the pinned TUnit discovery; escaped pipes below are Markdown only. These are source counts, not measured executions.

### Delivery inventory

Changed branch payloads traverse existing transports; no new notification or session-input queue is introduced. D-1 is desktop creation -> remote mirror: durable join is TaskId/Attempt, WorktreeBranch/BaseSha, PreparationId, runner/store and BindingId; task/baseline and preparation rows precede transport; runner binding intent/owner stamp precede completed server binding. Recovery rereads those coordinates. Receipt is recipient symbolic HEAD, exact commit and binding/owner stamp plus fresh server row.
D-2 is PhoneHomeRunnerMirrorPublisher -> PhoneHomeRunnerClient -> live request/send waiter -> socket -> PhoneHomeCommandDispatcher mutation queue -> RunnerWorkspaceService.PublishAsync -> origin -> desktop SyncCoreAsync. Join is persisted task/attempt, full ref, baseline and source SHA (RPC request ID is correlation only). Socket queue/waiters are volatile. Recovery repeats from persisted task/baseline and re-observes exact origin before desktop fast-forward. Receipt is origin ref=S and desktop HEAD=S with expected file bytes, not Pushed or an RPC result.
D-3 is TaskParkPublicationService -> local publisher OR phone-home WorkspacePark command -> RunnerWorkspaceParkService -> origin -> persisted park publication proof. Join includes TaskId/Attempt, park episode/request, runner/store/generation, full ref, baseline, receipt ID and source SHA. Recovery validates the same persisted episode and rereads source/origin; receipt is origin exact ref=S plus matching persisted proof and a successful fresh Verify. Publication alone gives no seat-release authority.

V-7 uses the **real production request/mutation queue**, not a replacement in-memory command list: PhoneHomeTestHost, real PhoneHomeRunnerClient, framed loopback WebSocket, production PhoneHomeCommandDispatcher and real scratch Git workspaces. The queue here is the live connection waiters/send gate and runner MutationLock; it is not a durable SessionMessageQueue row. Drive D-1 from the real preparer, D-2 from the real publisher/sync service and D-3 from the real publication service. Test legacy/new pairs with already eligible recipient and a recipient held by a separate gated mutating operation. Observe second dispatch reaching its wait before releasing the first. Use TaskCompletionSource gates with RunContinuationsAsynchronously and bounded cancellation; join pumps/children in finally. Reuse PhoneHomeAdoptionGate/eligibility observer for live readiness, not WaitLiveAsync polling.

Recovery matrix for each D-1..D-3: (a) committed task/intent before enqueue, then recreate producer scope; (b) request-limit/closed socket before send, restore peer, retry from durable record; (c) request received before recipient action, disconnect/recreate and retry; (d) recipient durable effect before lost reply, recreate producer and re-observe; (e) received reply before server save fails, restart with fresh DbContext; (f) proof saved before restart, replay idempotently. No success is asserted while the recipient gate is closed or proof is absent. At each arm preserve the original full ref/base and preparation/park identity, compare recipient state, and retain unrelated branch/file canaries. Connection epoch/RPC IDs may change; durable identity must not. A lost reply can leave completed recipient work; replay need not mean exactly one wire request.

Substitutes: ScriptedGit/FakeTaskProgressGit prove ref selection and refusal ordering only; LocalMirrorPublisher proves local Git exchange only; pure parser and BuildLaunchSpec assertions prove composition/serialization only. They cannot prove socket delivery, provider consumption or transcript receipt. V-7 closes runner delivery with recipient Git evidence. S2 changes system instruction composition, S3 changes brief wording, and neither changes session input submission/recovery: native provider/brief receipt is excluded, not inferred from launch args. If Code expands scope into session input, add real SessionMessageQueue busy/eligible/crash tests and matching **complete UserPrompt** (same session/generation, past baseline, full matching body) before claiming delivery; enqueue, event, Sent and ack are insufficient.

### Proves it works now

Recipe U = pure Unit, hand-written expected values, no I/O. G = real isolated Git, recorded command barriers, sentinel files/ref observations. H = isolated PostgreSQL schema plus production policy/service, fresh verification context (real Git when removal is asserted). D = isolated DB + real DelegationWorktreeService/dispatcher, recording runner directory, scratch Git; no provider process. Q = V-7 transport recipe above. L = existing real BuildLaunchSpec harness; no provider spawn. All new methods are single [Test] executions with internal labeled one-flip tables; no Arguments/Matrix expansion is added. A table loop counts as one test. Test names in PC rows are exact binding obligations, not evidence that methods already exist.

| ID | Behaviour/layer | Recipe, case and exact method group | Expected oracle |
|---|---|---|---|
| V-1 | Both-form parser/build (T1,T2) | U; DelegateBranchNameTests C1213_* in PC-1..PC-10; WorktreeManagerTests.TryParseDelegateTaskBranch_accepts_feat_card_task_short_ids_only delegates to shared parser | Exact name/parsed fields and false for hostile rows. Pin expected role tokens with a hand-written map, not enum.ToString as oracle. |
| V-2 | Mint/adopt/snapshot (T3..T5,T16) | D; five new AgentTaskDispatchBaseGuardTests methods in PC-11..PC-15; use actual provisioning and fresh persisted task reads | Default-on local/capable runner new name; unsupported/unknown legacy or existing refusal. Snapshot validation succeeds with legacy, tampered new coordinates refuse. Retry keeps existing HEAD/files. |
| V-3 | Worktree path, scan, health, residue (T6..T8) | G/H; methods PC-16..PC-21 | symbolic-ref + metadata; exact scan set/ShortId/path; healthy joined task; orphan DTO short id; no cleanup authority from scan. |
| V-4 | Done cleanup and retirement (T9,T10) | H/G; methods PC-22..PC-28 | Both-form ownership admitted; foreign path/tail rejected, ambiguity held; authorized retirement removes exact local tree/ref with remote unchanged. |
| V-5 | OwnedBranch and its five consumers (T11,T12) | U/G/Q; PC-29..PC-35; SyncCoreAsync, PrepareAsync, phone-home publisher, debt policy, local park | Correct stored ref at each boundary, actual recipient SHA for Git arms; malformed/null identity withheld rather than invented. |
| V-6 | Runner publish/park/capability/binding (T13..T15) | G/U; PC-36..PC-41 | Both forms publish/park, foreign tail gets 409/park_invalid_target before Git; exact advertised feature; unchanged task mirror directory and bound identity. |
| V-7 | Producer-to-recipient recovery (T5,T11..T14) | Q; three new DelegateBranchDeliveryTests methods PC-34,PC-42..PC-46; six recovery cut points per path, old/new; ready/busy | Recipient Git and durable server row agree; interrupted handoff not mislabeled success; restored retry completes without changing selected branch. |
| V-8 | Fragment/catalog/expansion/stamps (T17..T22) | U; seven new InstructionFragmentTests methods PC-47..PC-54 and PC-57 | Embedded fragment key/hash/header, once per composition, fail-loud valid unknown token, append/brief verbatim, hash-sensitive stamps. |
| V-9 | Shipped contract/override/launch/budget (T23..T25) | U/L; four new InstructionBundleTests methods and one DelegateBundleLaunchTests method PC-55..PC-58,PC-60..PC-68; InstructionBundleTests.the_worst_case_composition_measured_sits_far_under_the_budget, an_oversized_composition_throws_and_names_what_to_shrink, the_test_design_composition_is_past_the_batch_command_ceiling | Named sentences retained, Review override after fragment, provider payload expanded, worst shipped composition fits actual argv budget including CRLF/other args. |
| V-10 | Concrete runner branch contract (T26) | U; changed DelegationReportFormatterTests.Runner_worktree_brief_names_mirror_and_fast_forward_contract (PC-59), both branch forms | Brief cites git-common and exact branch/base/HEAD push destination; default desktop/shared omissions retained. |

Hostile parser rows also include null/empty, wrong prefix, duplicate ref prefix, trailing slash, 200+ chars, short/long/non-hex suffix, casing and Unicode confusables. Numeric limits are lexical (0 and leading zeros are accepted by D-4); no invented positive-number restriction. Grammar permits stage task and unknown syntactically valid stage tokens; only minting an unknown role with no valid token falls back. Legacy parser case compatibility is the D-4 exact grammar: ASCII prefix/stage case-sensitive, hex case-insensitive. Long/malformed card identifiers fall back rather than mint an unparseable branch.

Fragment grammar rows in C1213_ExpansionScope: {{GIT-COMMON}} valid; valid unknown {{NOPE}} fails; {{X}}, lowercase, underscore, 33-character name and unmatched braces are outside D-5 grammar and remain literal in synthetic bundles. The shipped-bundle test separately forbids residual {{. Preserve literal braces in operator append and brief. C1213_CatalogAndHash computes expected SHA256 from normalized fixture bytes independent of production hashing. No reflected patching of static cached catalogs: pass a synthetic bundle/fragment resolver through an internal composition overload that production Compose also calls; otherwise route next: plan for the unverifiable seam.

### Guards the regression

- R-1: accidental parser broadening/legacy loss | PC-2..PC-10 methods; exact accept/reject/field labels, including final-LF and ambiguous-looking stage task.
- R-2: runner skew, unbound change, snapshot contamination or branch rename on retry | PC-11..PC-15,PC-42 methods; persisted branch and outgoing request, existing HEAD and sentinel equality. Snapshot fixture calls real Ensure/Validate; fake transport alone cannot prove mint path.
- R-3: missed new refs, swallowed card-spawn refs or lost task directory | PC-16..PC-21 methods and unchanged scheduler classes; exact real scan set/orphan DTO/path.
- R-4: parser success used as deletion authority | PC-22..PC-28; one condition flipped from an admitted owner, refusal/release count and bytes; existing retirement-race class remains in scope.
- R-5: any of the five consumers derives legacy ref | PC-29..PC-35; each consumer reached independently, exact ref assertions plus origin/desktop proof where applicable. Existing dirty/diverged/endpoint/baseline regressions stay selected.
- R-6: runner accepts foreign id or loses old-format mirrors | PC-36..PC-41; same valid fixture with foreign tail only, no Git invocation on early rejection; both valid formats reach origin.
- R-7: delivery claim stops at request/ack or recovery chooses a different branch | PC-42..PC-46; real queue and recipient/durable identity assertions after controlled cut points.
- R-8: unknown placeholders leak, literal append changes, fragments become attachable bundles, or drift misses fragment edits | PC-47..PC-54; catalog key set, diagnostic fields, byte identity and independent hash/stamp tests.
- R-9: common instructions weaken or stage override/budget disappears | PC-55..PC-58,PC-60..PC-68; labeled full-sentence pins, production Review ordering and real launch-builder payload. Content pins do not establish an agent obeys them.
- R-10: migration drops task-specific push/base information | PC-59 existing formatter method with legacy/new internal cases; assert exact command, not a generic word push.

### Guard inventory

Scope: safety/compatibility guards implicated by this change. Unchanged transport/custody/landing algorithms retain their owner-card PCs; selecting their regression classes does not commission unrelated controls again. Independently bypassable conditions are split 1:1 below. Added split IDs inherit their original V/R row: PC-69 from 3; 70 from 6; 71 from 7; 72..73 from 8; 74 from 17; 75 from 19; 76..77 from 24; 78 from 27; 79 from 33; 80 from 35; 81..82 from 47; 83 from 48; 84 from 49; 85 from 51; 86..87 from 52; 88..89 from 53; 90 from 54; 91..93 from 59; 94 from 62; 95 from 65; 96 from 66; 97..98 from 67; 99 from 68; 100 from 14. No guard is excluded as noncritical merely to avoid a PC.

| Guard | Plan reference and invariant | Control |
|---|---|---|
| G-1 | D-1/T1: Card/stage/task suffix is explicit | PC-1 |
| G-2 | D-4/T2: Legacy branch admission | PC-2 |
| G-3 | D-4/T2: New branch admission | PC-3 |
| G-4 | D-4/T2: Whole input consumed | PC-4 |
| G-5 | D-4/T2: ASCII card number has 1..8 digits | PC-5 |
| G-6 | D-4/T2: Stage starts lowercase ASCII letter | PC-6 |
| G-7 | D-4/T2: Task suffix is exactly 8 characters | PC-7 |
| G-8 | D-4/T2: Prefix/stage case is exact | PC-8 |
| G-9 | D-4/T2: Ref/path injection excluded | PC-9 |
| G-10 | D-4/T2: Legacy/new interpretations are disjoint | PC-10 |
| G-11 | D-2/T3: Unbound or malformed card/unknown role keeps legacy | PC-11 |
| G-12 | D-4/T5: New format requires positive runner capability | PC-12 |
| G-13 | D-2/T4: SourceLanding stays on verification creation path and legacy branch | PC-13 |
| G-14 | D-4/T16: Existing metadata wins mint on initial adoption | PC-14 |
| G-15 | D-4/T16: Null branch probes both candidates before base selection | PC-15 |
| G-16 | D-3/T6: Explicit branch reaches worktree creation | PC-16 |
| G-17 | D-3/T6: Directory remains task-based | PC-17 |
| G-18 | D-4/T7: Dangling scan glob includes new names | PC-18 |
| G-19 | D-4/T7: Registered scan entries require parsed delegate refs | PC-19 |
| G-20 | D-4/T8: Orphan health DTO parses new short id | PC-20 |
| G-21 | D-3/T7: Residue derives unchanged task directory | PC-21 |
| G-22 | D-4/T9: Done ownership accepts both forms | PC-22 |
| G-23 | D-4/T9: Done ownership requires matching parsed task tail | PC-23 |
| G-24 | D-3/T9: Done ownership requires ordinary leaf | PC-24 |
| G-25 | D-4/T10: Released ordinary owner in either form retires | PC-25 |
| G-26 | D-4/T10: Retirement ownership tail is checked | PC-26 |
| G-27 | D-3/T10: Retirement requires ordinary leaf | PC-27 |
| G-28 | D-4/T10: Full-task ambiguity cannot become cleanup authority | PC-28 |
| G-29 | D-4/T11: OwnedBranch uses validated stored spelling | PC-29 |
| G-30 | D-4/T11: Settlement reads and fast-forwards exact owned ref | PC-30 |
| G-31 | D-4/T11: Settlement foreign branch rejected before I/O | PC-31 |
| G-32 | D-4/T12: Progress preparation follows stored ref | PC-32 |
| G-33 | D-4/T12: Debt ref is taken from stored task branch | PC-33 |
| G-34 | D-4/T12: Phone-home publisher transports stored owned ref | PC-34 |
| G-35 | D-4/T12: Local park accepts both owned forms | PC-35 |
| G-36 | D-4/T13: Runner publish accepts both formats for mirror name | PC-36 |
| G-37 | D-4/T13: Runner publish refuses foreign task tail before Git | PC-37 |
| G-38 | D-4/T14: Runner park accepts owned refs in both forms | PC-38 |
| G-39 | D-4/T14: Runner park refuses foreign task tail | PC-39 |
| G-40 | D-4/T15: Runner advertises implemented branch capability | PC-40 |
| G-41 | D-3/T6: Mirror path and binding retain task identity | PC-41 |
| G-42 | D-4/T5: Old recipient receives legacy at mirror boundary | PC-42 |
| G-43 | D-4/delivery: Admission failure does not become publication success | PC-43 |
| G-44 | D-4/delivery: Lost in-flight reply retains uncertainty until re-observation | PC-44 |
| G-45 | D-4/delivery: Persisted branch identity survives server recovery | PC-45 |
| G-46 | D-4/delivery: Busy recipient serializes mutations without premature completion | PC-46 |
| G-47 | D-5/T17: Fragment resource is embedded | PC-47 |
| G-48 | D-7/T17: Fragment version changes with content | PC-48 |
| G-49 | D-5/T18: Repeated fragment occurrences cite first expansion | PC-49 |
| G-50 | D-6/T19: Unknown valid token fails with diagnostic context | PC-50 |
| G-51 | D-5/OD-4: Operator append is byte-preserved | PC-51 |
| G-52 | D-6/T21: Fragment forbids recursive opening braces | PC-52 |
| G-53 | D-7/T21: Human fragment contract is pinned | PC-53 |
| G-54 | D-5/T20: Fragment stamps participate once | PC-54 |
| G-55 | D-6/T22: Shipped role compositions resolve tokens | PC-55 |
| G-56 | D-8/T24: Review override follows common fragment | PC-56 |
| G-57 | D-5/T25: Expanded text and other argv share existing budget | PC-57 |
| G-58 | D-5/T22: Launch builder passes expanded provider instructions | PC-58 |
| G-59 | D-8/T26: Runner brief preserves exact push command | PC-59 |
| G-60 | D-5 §2.1/T23: assigned branch and no branch switching | PC-60 |
| G-61 | D-5 §2.1/T23: commit/push each slice and full 40-hex claims | PC-61 |
| G-62 | D-5 §2.1/T23: Common instructions forbid target merge | PC-62 |
| G-63 | D-5 §2.1/T23: commit failing tests before the fix | PC-63 |
| G-64 | D-5 §2.1/T23: review subject stays read-only including author | PC-64 |
| G-65 | D-5 §2.1/T23: Landability requires merge-tree result | PC-65 |
| G-66 | D-5 §2.1/T23: commit_refs retry has bound | PC-66 |
| G-67 | D-5 §2.1/T23: Common instructions exclude secrets | PC-67 |
| G-68 | D-8: SourceLanding exception remains explicit | PC-68 |
| G-69 | D-4/T2: Parsed hexadecimal task identity is normalized | PC-69 |
| G-70 | D-4/T2: Stage length is bounded at 16 | PC-70 |
| G-71 | D-4/T2: Task suffix alphabet is hexadecimal | PC-71 |
| G-72 | D-4/T2: Card digits remain ASCII | PC-72 |
| G-73 | D-4/T2: Stage letters remain ASCII | PC-73 |
| G-74 | D-3/T6: Worktree metadata identifier remains task-based | PC-74 |
| G-75 | D-4/T7: Dangling scan entries require parsed delegate refs | PC-75 |
| G-76 | D-3/T9: Done ownership requires managed root | PC-76 |
| G-77 | D-3/T9: Done cleanup excludes canonical repository | PC-77 |
| G-78 | D-3/T10: Retirement requires managed root | PC-78 |
| G-79 | D-4/T12: Debt pending requires result ref equality | PC-79 |
| G-80 | D-4/T12: Local park requires matching task tail | PC-80 |
| G-81 | D-5/T17: Fragment is excluded from bundle catalog | PC-81 |
| G-82 | D-5/T17: Fragment catalog has exactly git-common | PC-82 |
| G-83 | D-7/T17: Fragment hashing normalizes line endings | PC-83 |
| G-84 | D-5/T18: Fragment expansion state is per composition | PC-84 |
| G-85 | D-5/OD-4: Brief text is outside expansion | PC-85 |
| G-86 | D-6/T21: Fragment forbids agentName substitution | PC-86 |
| G-87 | D-6/T21: Fragment forbids channels substitution | PC-87 |
| G-88 | D-7/T21: Fragment size ceiling is pinned | PC-88 |
| G-89 | D-7/T21: INVARIANTS marker is pinned | PC-89 |
| G-90 | D-5/T20: Changed fragment text changes StampLine | PC-90 |
| G-91 | D-8/T26: Runner brief preserves assigned branch | PC-91 |
| G-92 | D-8/T26: Runner brief preserves full base SHA | PC-92 |
| G-93 | D-8/T26: Runner brief cites common contract | PC-93 |
| G-94 | D-5 §2.1/T23: Common instructions forbid rewriting pushed commits | PC-94 |
| G-95 | D-5 §2.1/T23: Landability requires fresh fetch | PC-95 |
| G-96 | D-5 §2.1/T23: commit_refs retry excludes force | PC-96 |
| G-97 | D-5 §2.1/T23: Generated evidence stays excluded | PC-97 |
| G-98 | D-5 §2.1/T23: Code and Review check full-range evidence diff | PC-98 |
| G-99 | D-8: Review subject restriction remains explicit | PC-99 |
| G-100 | D-4/T16: Conflict-after-list adoption preserves the existing branch | PC-100 |

### Positive controls

Status: specified/pending (Mutation paused by the commissioning brief). Code runs V/R; separate Review judges the design/evidence before land. After land and explicit resumption, Mutation must run **each PC and listed variant**: baseline green, compiling defect, intended assertion red, exact restore, rebuilt green. No PC has run here. Missing source bindings remain **binding required after Code**; a static payload/checklist is not proof of reachability.

The table's recipe resolves setup and layer above: classification is Unit for U, integration for G/H/D/Q, launch-contract integration for L. Each PC is classification binding-required/unexecuted until Code binds it and Mutation proves it. Variant is the case and Expected red is the oracle. Reachability for each row is planned, not measured: hold other guards at their positive fixture values, assert a labeled entry/boundary witness, then the labeled decisive assertion. Mutation must record the first failing assertion; an earlier fixture/compile failure or zero selection is not red. The shown filter is exactly one method (no class/suite PCs); internal variant loops remain one execution. Mutants change production code or embedded text and compile; do not mutate assertions.

| PC | Recipe | Variant/case | Break its guard with a compiling defect | Detecting filter | Expected red |
|---|---|---|---|---|---|
| PC-1 | U | CARD-1213 Plan; TestDesign; Orchestrator; all 17 role tokens | omit stage from Build | `/*/*/DelegateBranchNameTests/C1213_BuildCardStage` | built-name: exact hand-written branch |
| PC-2 | U | legacy bare/full ref, lowercase/uppercase tail | remove legacy regex alternative | `/*/*/DelegateBranchNameTests/C1213_ParseBothForms` | legacy-accepted: true |
| PC-3 | U | new bare/full ref; upper hex; stage task | remove new regex alternative | `/*/*/DelegateBranchNameTests/C1213_ParseBothForms` | new-accepted: true |
| PC-4 | U | prefix/suffix junk, space, NUL, CR, LF, final newline | remove whole-match-length check (or replace strict end anchor with $) | `/*/*/DelegateBranchNameTests/C1213_RejectEnvelopeJunk` | envelope-rejected: false for final LF |
| PC-5 | U | 0, 00001213, 99999999 accepted; empty/9 digits rejected | change {1,8} to {0,9} | `/*/*/DelegateBranchNameTests/C1213_NumberBounds` | number-rejected: false for 123456789 |
| PC-6 | U | a, a123456789012345, testdesign accepted; digit-first, 17 chars rejected | allow digit as first stage character | `/*/*/DelegateBranchNameTests/C1213_StageBounds` | stage-rejected: false at selected boundary |
| PC-7 | U | 7/9 characters and g rejected; ABCD1234 accepted | change tail {8} to {7,9} | `/*/*/DelegateBranchNameTests/C1213_HexBounds` | tail-rejected: false |
| PC-8 | U | Feat, CARD, Plan, fullwidth digits, Arabic digits, Cyrillic a, combining mark | enable RegexOptions.IgnoreCase | `/*/*/DelegateBranchNameTests/C1213_CaseAndUnicode` | alphabet-rejected: false |
| PC-9 | U | slash stage, ../, backslash, colon refspec, refs/tags, double refs/heads, 201 chars | replace stage class with .+ | `/*/*/DelegateBranchNameTests/C1213_PathInjection` | path-rejected: false |
| PC-10 | U | feat/card-task-deadbeef vs feat/card-1213-task-deadbeef; feat/card-task-code-deadbeef rejected | assign new form card/stage fields to legacy parse | `/*/*/DelegateBranchNameTests/C1213_FormsAreDisjoint` | legacy-fields: card/stage absent |
| PC-11 | D | null CardId, missing row/db, E03-003, CARD-123456789, CARD-unicode, unknown enum | force CARD-0/code when metadata unresolved | `/*/*/AgentTaskDispatchBaseGuardTests/C1213_UnboundAndUnknownMintLegacy` | fallback-branch: feat/card-task-<literal tail> |
| PC-12 | D | no runner; exact feature; absent descriptor/capabilities/features, empty list, near-match/wrong-case feature, registry failure | treat null/missing feature as supported | `/*/*/AgentTaskDispatchBaseGuardTests/C1213_MintUsesCardRoleAndRunnerCapability` | old-runner-branch: legacy; no new-format request |
| PC-13 | D | card-bound snapshot on local and capable remote paths | route snapshot through ordinary new-name creation | `/*/*/AgentTaskDispatchBaseGuardTests/C1213_SourceLandingKeepsLegacyIdentity` | snapshot-created: true after capturing provisioning refusal; then legacy branch and validated identity |
| PC-14 | D | legacy/new existing registration; capability toggled; conflict-after-list gate | replace adopted.Branch with freshly built branch at initial adoption | `/*/*/AgentTaskDispatchBaseGuardTests/C1213_RetryAdoptsRecordedBranch` | adopted-branch: pre-existing branch; original HEAD/file bytes |
| PC-15 | D | legacy/new candidate, WorktreeBranch null, would-be invalid reselected base | probe only new candidate; separately only legacy | `/*/*/AgentTaskDispatchBaseGuardTests/C1213_NullBranchFindsEitherKeptCandidate` | kept-adopted: dispatched with original branch/base |
| PC-16 | G | fresh legacy/new branch, same task identifier in isolated repos | ignore branch argument and derive legacy | `/*/*/WorktreeManagerGitIntegrationTests/C1213_ExplicitBranchKeepsDirectoryIdentity` | symbolic-head: refs/heads/<requested branch> |
| PC-17 | G | new branch; card-task-deadbeef directory and task-deadbeef metadata | derive directory from card/stage | `/*/*/WorktreeManagerGitIntegrationTests/C1213_ExplicitBranchKeepsDirectoryIdentity` | directory-identity / metadata-identity: exact literals |
| PC-18 | G | dangling legacy/new plus feat/card-1213 and E03-003 canaries | restore feat/card-task-* glob | `/*/*/WorktreeManagerGitIntegrationTests/C1213_ScanBothFormsAndExcludeCardSpawn` | dangling-set: both literal branch rows |
| PC-19 | G | registered/dangling pairs and malformed/card-spawn canaries | bypass TryParse for registered entries | `/*/*/WorktreeManagerGitIntegrationTests/C1213_ScanBothFormsAndExcludeCardSpawn` | scan-excludes-canary: absent; short IDs exact |
| PC-20 | H | known healthy and orphan registered/dangling rows in both forms | keep TryShortFromBranch legacy-only | `/*/*/WorktreeHealthServiceTests/C1213_SweepBothFormsPreservesIdentity` | orphan-short-id: deadbeef; known healthy has no finding |
| PC-21 | G | real scan with orphan legacy/new refs and card-task leaves | derive residue path from card/stage instead of parsed tail | `/*/*/WorktreeResidueSweepTests/C1213_ResidueBothFormsKeepsDirectory` | residue-path: exact task leaf; Keep true |
| PC-22 | H | same owner legacy/new | restore literal legacy equality | `/*/*/CardDoneWorktreeCleanupTests/C1213_OwnershipBothForms` | owner-classification: null for both |
| PC-23 | H | same valid path, only branch tail foreign; malformed branch | accept any parsed branch without tail equality | `/*/*/CardDoneWorktreeCleanupTests/C1213_OwnershipBothForms` | foreign-owner: ordinary_owner_required |
| PC-24 | H | valid owned branch, foreign leaf; canonical path; outside managed root | drop leaf check | `/*/*/CardDoneWorktreeCleanupTests/C1213_OwnershipPathGuards` | unsafe-path: ordinary_owner_required |
| PC-25 | G | released task, exact receipt, real Git legacy/new | restore expectedBranch legacy equality | `/*/*/TaskWorktreeRetirementTests/C1213_RetirementBothForms` | retired: directory and exact local ref gone; remote unchanged |
| PC-26 | H | valid leaf, only branch tail foreign | omit parsed id equals task id | `/*/*/TaskWorktreeRetirementTests/C1213_RetirementRefusesForeignIdentity` | retirement-refused: outside_scope; sentinel preserved |
| PC-27 | H | matching branch with foreign leaf/outside root | drop retirement leaf check | `/*/*/TaskWorktreeRetirementTests/C1213_RetirementRefusesForeignIdentity` | retirement-refused: outside_scope; no release |
| PC-28 | H | second full GUID sharing short id, same path/ref | skip owners.Count/owner-id check | `/*/*/TaskWorktreeRetirementTests/C1213_RetirementRefusesForeignIdentity` | ambiguous-owner: identity_ambiguous |
| PC-29 | U | legacy/new matching id; null/foreign/malformed stored branch | recompute legacy from task GUID | `/*/*/RemoteWorktreeMirrorTests/C1213_OwnedBranchUsesStoredIdentity` | owned-branch: exact stored new branch; invalid refused |
| PC-30 | G | legacy/new; pushed S plus different sibling S2 | derive legacy fullRef in SyncCoreAsync | `/*/*/RunnerSettlementSyncTests/C1213_SyncUsesStoredOwnedBranch` | desktop-receipt: HEAD=S, exact ref and work.txt bytes |
| PC-31 | G | add new-format foreign tail and valid-looking malformed cases | bypass OwnedBranch rejection at sync entry | `/*/*/RunnerSettlementSyncTests/Rejects_foreign_task_branch_before_fetch` | BranchMismatch plus no fetch/ls-remote/merge and unchanged HEAD |
| PC-32 | U | missing sync; NotApplicable stub; timeout; inspection failure, both forms | derive fullRef from id in PrepareAsync | `/*/*/TaskCompletionProgressPolicyTests/C1213_PrepareUsesStoredOwnedRef` | progress-ref: exact stored full ref in unavailable result |
| PC-33 | U | both forms lease-busy positive; foreign/null/tag and stale legacy result negative | derive expected full ref from task id | `/*/*/SettlementSyncDebtPolicyTests/C1213_DebtUsesStoredOwnedRef` | debt-state: Pending only exact ref; otherwise Unavailable |
| PC-34 | Q | real publisher, old/new branch; ready/busy runner | derive legacy branch in PhoneHomeRunnerMirrorPublisher | `/*/*/DelegateBranchDeliveryTests/C1213_QueuedBranchesReachRecipient` | recipient-ref: origin exact branch=S, sibling unchanged |
| PC-35 | G | local Worktree old/new; valid path, foreign tail negative | keep legacy equality | `/*/*/TaskParkPublicationTests/C1213_LocalParkBothForms` | local-park: Published exact remote S; foreign Held park_invalid_target |
| PC-36 | G | legacy/new task-deadbeef mirror | keep legacy request.Branch comparison | `/*/*/RunnerWorkspaceServiceTests/C1213_PublishBothFormsChecksTail` | publish-receipt: origin exact ref=S |
| PC-37 | G | same valid mirror, flip tail to cafebabe | accept any parser success without name-tail comparison | `/*/*/RunnerWorkspaceServiceTests/C1213_PublishBothFormsChecksTail` | foreign-publish: UnsupportedTarget 409 and zero Git starts |
| PC-38 | G | legacy/new same task; prepared and verified receipt | keep refs/heads/feat/card- plus name equality | `/*/*/WorkspaceParkPublicationTests/C1213_ParkBothFormsChecksTail` | park-receipt: Published with exact origin S |
| PC-39 | G | valid mirror, foreign full ref only | omit name/task/ref tail comparison | `/*/*/WorkspaceParkPublicationTests/C1213_ParkBothFormsChecksTail` | foreign-park: Held park_invalid_target; zero pushes |
| PC-40 | U | real PhoneHomeRuntimeAdapter capabilities and dispatcher capabilities | remove WorkspaceCardBranchV1 from StaticFeatures | `/*/*/RunnerWorkspaceServiceTests/C1213_AdvertisesCardBranchCapability` | advertised-feature: contains exact contract constant |
| PC-41 | G | bound old/new mirror replay with same PreparationId | rewrite new branch to legacy before ledger persistence | `/*/*/RunnerWorkspaceBindingTests/C1213_BindingBothBranchForms` | binding-branch: exact requested branch and SourceFullRef; same BindingId |
| PC-42 | Q | registered peer lacks only WorkspaceCardBranchV1; card-bound dispatch | ignore capability during ordinary mint | `/*/*/DelegateBranchDeliveryTests/C1213_OldRunnerReceivesLegacy` | old-peer-receipt: recorded request legacy; real mirror HEAD/base matches |
| PC-43 | Q | before send/request-limit or disconnected peer, each operation; both forms | turn never-sent failure into successful service result | `/*/*/DelegateBranchDeliveryTests/C1213_QueuedBranchRecovery` | enqueue-failure: no completed binding/receipt; retry reaches exact recipient state |
| PC-44 | Q | after recipient mutation before response, each operation; both forms | trust ack/error as publication without remote re-observation | `/*/*/DelegateBranchDeliveryTests/C1213_QueuedBranchRecovery` | lost-reply: no confirmed result before proof; replay exact ref/identity |
| PC-45 | Q | after task/baseline save before send; after response before server save; after saved receipt | recompute legacy ref on recovery from persisted new task | `/*/*/DelegateBranchDeliveryTests/C1213_QueuedBranchRecovery` | recovered-ref: original ref/base/preparation/park identity and recipient S |
| PC-46 | Q | hold first mutating runtime call, enqueue second old/new operation | bypass PhoneHomeCommandDispatcher.MutationLock.WaitAsync | `/*/*/DelegateBranchDeliveryTests/C1213_QueuedBranchesReachRecipient` | busy-recipient: second runtime-entry count zero before release; receipt after |
| PC-47 | U | manifest resource plus catalog key sets | remove fragment embed item | `/*/*/InstructionFragmentTests/C1213_CatalogAndHash` | fragment-keys: [git-common]; bundle-keys unchanged |
| PC-48 | U | LF/CRLF same text; single character changed | use fixed fragment hash | `/*/*/InstructionFragmentTests/C1213_CatalogAndHash` | content-hash: independently SHA256-normalized first 8 hex |
| PC-49 | U | synthetic two bundles and twice in one bundle; fresh second composition | expand full body for each occurrence | `/*/*/InstructionFragmentTests/C1213_ExpansionOnce` | expansion-count: one body, later see-above; fresh call full body |
| PC-50 | U | synthetic bundle {{NOPE}} and {{GIT-COMMON-X}} | emit unknown token verbatim | `/*/*/InstructionFragmentTests/C1213_UnknownPlaceholderFailsLoudly` | unknown-token: InvalidOperationException names bundle/token/known GIT-COMMON |
| PC-51 | U | literal {{GIT-COMMON}} in append and brief; style ordering preserved | expand final composed append | `/*/*/InstructionFragmentTests/C1213_ExpansionScope` | literal-append / literal-brief: byte-exact unchanged |
| PC-52 | U | {{, {agentName}, {channels} injected separately | skip recursion validation and insert {{ into fragment | `/*/*/InstructionFragmentTests/C1213_FragmentLimits` | fragment-forbidden: refused at catalog validation |
| PC-53 | U | shipped contract v1 and INVARIANTS; 1500 accepted/1501 rejected fixture | remove contract v1 first line | `/*/*/InstructionFragmentTests/C1213_FragmentLimits` | fragment-contract / fragment-size: exact first line and <=1500 |
| PC-54 | U | two references, same bundle version, change only fragment text | omit fragment stamps | `/*/*/InstructionFragmentTests/C1213_FragmentStampsChangeWithText` | fragment-stamp: exactly once and changed StampLine |
| PC-55 | U | explicit worker roles and orchestrator; specialist remains empty | bypass expansion in Compose | `/*/*/InstructionBundleTests/C1213_RoleCompositionsResolve` | role-unresolved: no {{ in catalog-derived output; git-common count one |
| PC-56 | U | production ForDelegate Review order, not synthetic order | move common body after review override | `/*/*/InstructionBundleTests/C1213_ReviewOverrideFollowsFragment` | review-order: fragment index < subject-readonly rule index |
| PC-57 | U | shipped max role/CRLF compositions; synthetic overbudget fragment | measure pre-expansion bundle text or bypass budget check | `/*/*/InstructionFragmentTests/C1213_ExpandedBudgetIsEnforced` | expanded-budget: oversize throws without truncation |
| PC-58 | L | Claude argv; Codex developer_instructions; Grok rules payload | feed raw bundle.Render to provider path | `/*/*/DelegateBundleLaunchTests/C1213_ProvidersReceiveExpandedInstructions` | provider-payload: exact fragment header/body, no token |
| PC-59 | U | legacy/new branch, full 40-hex base and exact push refspec | drop push command from formatter | `/*/*/DelegationReportFormatterTests/Runner_worktree_brief_names_mirror_and_fast_forward_contract` | runner-contract: exact branch/base/push command/citation |
| PC-60 | U | shipped composed Code/Review/Mutation and orchestrator | remove assigned-branch restriction | `/*/*/InstructionBundleTests/C1213_GitContractPins` | contract-assigned-branch: corresponding full invariant sentence retained |
| PC-61 | U | shipped composed Code/Review/Mutation and orchestrator | remove push after every commit rule | `/*/*/InstructionBundleTests/C1213_GitContractPins` | contract-commit-push: corresponding full invariant sentence retained |
| PC-62 | U | shipped composed Code/Review/Mutation and orchestrator | delete never merge condition | `/*/*/InstructionBundleTests/C1213_GitContractPins` | contract-no-rewrite: corresponding full invariant sentence retained |
| PC-63 | U | shipped composed Code/Review/Mutation and orchestrator | remove test-first sentence | `/*/*/InstructionBundleTests/C1213_GitContractPins` | contract-test-first: corresponding full invariant sentence retained |
| PC-64 | U | shipped composed Code/Review/Mutation and orchestrator | remove author/read-only restriction | `/*/*/InstructionBundleTests/C1213_GitContractPins` | contract-review-freeze: corresponding full invariant sentence retained |
| PC-65 | U | shipped composed Code/Review/Mutation and orchestrator | delete merge-tree requirement | `/*/*/InstructionBundleTests/C1213_GitContractPins` | contract-merge-tree: corresponding full invariant sentence retained |
| PC-66 | U | shipped composed Code/Review/Mutation and orchestrator | delete retry bound | `/*/*/InstructionBundleTests/C1213_GitContractPins` | contract-push-retry: corresponding full invariant sentence retained |
| PC-67 | U | shipped composed Code/Review/Mutation and orchestrator | delete secrets restriction | `/*/*/InstructionBundleTests/C1213_GitContractPins` | contract-evidence-custody: corresponding full invariant sentence retained |
| PC-68 | U | Mutation snapshot and Review compositions | remove SourceLanding no-commit exception | `/*/*/InstructionBundleTests/C1213_StageExceptionsRemain` | stage-exception: explicit exception plus subject restriction |
| PC-69 | U | new bare/full ref; upper hex; stage task | omit id lowercase normalization | `/*/*/DelegateBranchNameTests/C1213_ParseBothForms` | normalized-id: deadbeef |
| PC-70 | U | a, a123456789012345, testdesign accepted; digit-first, 17 chars rejected | raise stage max length to 17 | `/*/*/DelegateBranchNameTests/C1213_StageBounds` | stage-rejected: false at selected boundary |
| PC-71 | U | 7/9 characters and g rejected; ABCD1234 accepted | admit g in tail character class | `/*/*/DelegateBranchNameTests/C1213_HexBounds` | tail-rejected: false |
| PC-72 | U | Feat, CARD, Plan, fullwidth digits, Arabic digits, Cyrillic a, combining mark | widen card digits to Unicode digit class | `/*/*/DelegateBranchNameTests/C1213_CaseAndUnicode` | alphabet-rejected: false |
| PC-73 | U | Feat, CARD, Plan, fullwidth digits, Arabic digits, Cyrillic a, combining mark | widen stage to Unicode letter class | `/*/*/DelegateBranchNameTests/C1213_CaseAndUnicode` | alphabet-rejected: false |
| PC-74 | G | new branch; card-task-deadbeef directory and task-deadbeef metadata | derive metadata CardId from card/stage | `/*/*/WorktreeManagerGitIntegrationTests/C1213_ExplicitBranchKeepsDirectoryIdentity` | directory-identity / metadata-identity: exact literals |
| PC-75 | G | registered/dangling pairs and malformed/card-spawn canaries | bypass TryParse for dangling branches | `/*/*/WorktreeManagerGitIntegrationTests/C1213_ScanBothFormsAndExcludeCardSpawn` | scan-excludes-canary: absent; short IDs exact |
| PC-76 | H | valid owned branch, foreign leaf; canonical path; outside managed root | drop managed-root check | `/*/*/CardDoneWorktreeCleanupTests/C1213_OwnershipPathGuards` | unsafe-path: ordinary_owner_required |
| PC-77 | H | valid owned branch, foreign leaf; canonical path; outside managed root | drop canonical-repository exclusion | `/*/*/CardDoneWorktreeCleanupTests/C1213_OwnershipPathGuards` | unsafe-path: ordinary_owner_required |
| PC-78 | H | matching branch with foreign leaf/outside root | drop retirement managed-root check | `/*/*/TaskWorktreeRetirementTests/C1213_RetirementRefusesForeignIdentity` | retirement-refused: outside_scope; no release |
| PC-79 | U | both forms lease-busy positive; foreign/null/tag and stale legacy result negative | accept result.FullRef without equality | `/*/*/SettlementSyncDebtPolicyTests/C1213_DebtUsesStoredOwnedRef` | debt-state: Pending only exact ref; otherwise Unavailable |
| PC-80 | G | local Worktree old/new; valid path, foreign tail negative | remove parsed tail validation | `/*/*/TaskParkPublicationTests/C1213_LocalParkBothForms` | local-park: Published exact remote S; foreign Held park_invalid_target |
| PC-81 | U | manifest resource plus catalog key sets | allow Fragments resources through bundle loader | `/*/*/InstructionFragmentTests/C1213_CatalogAndHash` | fragment-keys: [git-common]; bundle-keys unchanged |
| PC-82 | U | manifest resource plus catalog key sets | embed second nonempty fragment extra.md | `/*/*/InstructionFragmentTests/C1213_CatalogAndHash` | fragment-keys: [git-common]; bundle-keys unchanged |
| PC-83 | U | LF/CRLF same text; single character changed | hash raw CRLF instead of normalized text | `/*/*/InstructionFragmentTests/C1213_CatalogAndHash` | content-hash: independently SHA256-normalized first 8 hex |
| PC-84 | U | synthetic two bundles and twice in one bundle; fresh second composition | retain seen set across Compose calls | `/*/*/InstructionFragmentTests/C1213_ExpansionOnce` | expansion-count: one body, later see-above; fresh call full body |
| PC-85 | U | literal {{GIT-COMMON}} in append and brief; style ordering preserved | expand BuildBrief output | `/*/*/InstructionFragmentTests/C1213_ExpansionScope` | literal-append / literal-brief: byte-exact unchanged |
| PC-86 | U | {{, {agentName}, {channels} injected separately | skip agentName validation and insert {agentName} | `/*/*/InstructionFragmentTests/C1213_FragmentLimits` | fragment-forbidden: refused at catalog validation |
| PC-87 | U | {{, {agentName}, {channels} injected separately | skip channels validation and insert {channels} | `/*/*/InstructionFragmentTests/C1213_FragmentLimits` | fragment-forbidden: refused at catalog validation |
| PC-88 | U | shipped contract v1 and INVARIANTS; 1500 accepted/1501 rejected fixture | accept 1501-character fragment | `/*/*/InstructionFragmentTests/C1213_FragmentLimits` | fragment-contract / fragment-size: exact first line and <=1500 |
| PC-89 | U | shipped contract v1 and INVARIANTS; 1500 accepted/1501 rejected fixture | remove INVARIANTS marker | `/*/*/InstructionFragmentTests/C1213_FragmentLimits` | fragment-contract / fragment-size: exact first line and <=1500 |
| PC-90 | U | two references, same bundle version, change only fragment text | reuse stale version after changing fragment content | `/*/*/InstructionFragmentTests/C1213_FragmentStampsChangeWithText` | fragment-stamp: exactly once and changed StampLine |
| PC-91 | U | legacy/new branch, full 40-hex base and exact push refspec | drop assigned branch field from formatter | `/*/*/DelegationReportFormatterTests/Runner_worktree_brief_names_mirror_and_fast_forward_contract` | runner-contract: exact branch/base/push command/citation |
| PC-92 | U | legacy/new branch, full 40-hex base and exact push refspec | drop base SHA field from formatter | `/*/*/DelegationReportFormatterTests/Runner_worktree_brief_names_mirror_and_fast_forward_contract` | runner-contract: exact branch/base/push command/citation |
| PC-93 | U | legacy/new branch, full 40-hex base and exact push refspec | drop git-common citation | `/*/*/DelegationReportFormatterTests/Runner_worktree_brief_names_mirror_and_fast_forward_contract` | runner-contract: exact branch/base/push command/citation |
| PC-94 | U | shipped composed Code/Review/Mutation and orchestrator | delete pushed-commit rewrite restriction | `/*/*/InstructionBundleTests/C1213_GitContractPins` | contract-no-rewrite: corresponding full invariant sentence retained |
| PC-95 | U | shipped composed Code/Review/Mutation and orchestrator | delete fetch origin master requirement | `/*/*/InstructionBundleTests/C1213_GitContractPins` | contract-merge-tree: corresponding full invariant sentence retained |
| PC-96 | U | shipped composed Code/Review/Mutation and orchestrator | delete no-force condition from retry instruction | `/*/*/InstructionBundleTests/C1213_GitContractPins` | contract-push-retry: corresponding full invariant sentence retained |
| PC-97 | U | shipped composed Code/Review/Mutation and orchestrator | delete generated-evidence exclusion | `/*/*/InstructionBundleTests/C1213_GitContractPins` | contract-evidence-custody: corresponding full invariant sentence retained |
| PC-98 | U | shipped composed Code/Review/Mutation and orchestrator | delete evidence-diff command | `/*/*/InstructionBundleTests/C1213_GitContractPins` | contract-evidence-custody: corresponding full invariant sentence retained |
| PC-99 | U | Mutation snapshot and Review compositions | remove Review subject-branch restriction | `/*/*/InstructionBundleTests/C1213_StageExceptionsRemain` | stage-exception: explicit exception plus subject restriction |
| PC-100 | D | legacy/new existing registration; capability toggled; conflict-after-list gate | replace existing.Branch with freshly built branch in the conflict recovery path | `/*/*/AgentTaskDispatchBaseGuardTests/C1213_RetryAdoptsRecordedBranch` | adopted-branch: pre-existing branch; original HEAD/file bytes |

### Out of scope

- Rename/migrate existing refs, landing authority, SourceLanding custody execution, directory-name migration, multi-card naming, provider compliance and native session input delivery: unchanged or excluded by the fix design. Snapshot tests exercise creation/validation, not a Mutation run.
- No broad Unit/namespace/full-suite run. Existing tests within selected classes are ordinary regression scope; new rows use deterministic gates. Do not copy the legacy sleep/poll timing patterns into C1213 tests. No OS pin is required by the proposed cases. Git/PostgreSQL/pwsh/loopback prerequisites are explicit; missing prerequisite/skip is not acceptance.
- Dormant ComposeNamed/pin projection and warm provider consumption are outside this Compose change. Stamp tests prove composition identity changes; they do not prove a live agent has refreshed.
- Cosmetic CompactStuck/XML/script wording gets diff review against D-3/D-4 and the unchanged script-class regression; it confers no ownership authority.

### Checkpoints

S1 Code binding amendment (2026-10-13, ba777bcd): CP-2 excludes only
WorktreeManagerGitIntegrationTests.WorktreeManager_raw_remove_refuses_without_authority.
The original task base 1f86014eaa0897b07d66a6e276a47d2d91101504 reproduced its
contradictory removal assertion with 1 executed/1 failed, clean source and verified build
(run 20261013-135418-85c7, CP-B1). The fresh CP-2 TRX census is 32 results/27 methods;
its explicit positive method roster below retains the other 31 results/26 methods and
all four named classes. Each method operand carries the pinned TUnit wildcard.
No existing assertion changed. S1 Final total is 475 results.

S1 checkpoint amendment (2026-10-13, task c930b7b3): CP-4 excludes only
CardDoneWorktreeCleanupTests.C1017_TerminalTaskRequired, by exact method name
(`(*)&(!C1017_TerminalTaskRequired)`, no wildcard on the excluded name; a bare
`(!Name)` operand selects zero results under the pinned TUnit, see run
20261013-204127-846d). Review 560fbb63 measured CP-4 at
b43e21ecb68ee6513692bd760416b6ad9fb7910c as 114 executed/113 passed/1 failed; the one
failure is the line-105 assertion `(await f.CleanupAsync()).IsClean.ShouldBeTrue()` after
the task is restored to Succeeded. The unchanged pre-slice base
b5653d5832aac66c5ac1ff1a0012efb33b54de79 reproduced it with 1 executed/0 passed/1 failed,
clean source and verified build (run 20261013-202658-1887, CP-B1). S1 does not touch the
method or its fixture. The three classes stay selected whole; every other result,
including all C1213_* methods, remains, so CP-4 expects 113 (S1 Final total one lower).
No assertion changed; filing the inherited red belongs to the orchestrator.

New naming is off by default through Delegation:CardBranchNames; enabled tests opt in.
The disabled/capable case in C1213_MintUsesCardRoleAndRunnerCapability isolates that
setting from recipient capability. This implements the brief's rollout restriction:
runner images and their capability advertisements precede server enablement.
The otherwise omitted DelegationSettings.cs is the setting's required owner.
C1213_QueuedBranchRecovery uses the real settlement evidence writer and failed
DbContext save for D-2's reply-before-save cut; no provider/transcript input is introduced.

Cost amendment: mandatory pre-slice negative runs are additional to the original
39-minute S1 ordinary-check floor. The first six consumer rows measured 28m11s
including a 30s build-slot wait, and base confirmation measured 3m28s. Allow about
100 minutes for authoring, negative qualification and Final checks under this host
load; these are estimates, not permission to omit any remaining S1 row.


S2 Code binding amendment (2026-10-13, task bf9ffadd). S2 rows are CP-9 and CP-10 of this
table (the parent plan's CP-8/CP-9 numbering is superseded). CP-13 is added for S2 because S2 edits
delegate-basics, stage-review and orchestrator text, the composer and the stamp line, and these
classes pin that text, its size, its bytes or a composition: AgentBundleAttachmentTests,
GrokRulesCompositionTests and CheckpointRepeatDocumentationTests change in place (expanded
render instead of raw render; orchestrator byte pin), the others are unchanged regressions.
GrokRulesLaunchRefusalTests is left out (its budget method skips off Windows);
GrokRulesDispatchAcceptanceTests and DelegateLaunchArgvIntegrityTests are Explicit or
Windows-headed and are not run: their one changed assertion is review-only. CP-13's Min is the
method count, a lower bound; the run's TRX is the result count.
Brief-directed grammar: a `{{` that does not open a well-formed known placeholder throws
(malformed: one-character name, lowercase, underscore, 33-character name, unterminated, nested,
inner space); a well-formed unknown name throws (unknown). C1213_ExpansionScope carries those rows;
the operator append and the brief stay literal.
D-8 placement: stage-review is 2,475 of its 2,500-character cap and every other line is pinned
verbatim (TaskPlatformGuidanceTests, C1015), so stage-review gains only `{{GIT-COMMON}}` before its
INVARIANTS line (2,490) and the subject-branch restriction is a git-common sentence that Review
reads first (C1213_ReviewOverrideFollowsFragment, C1213_StageExceptionsRemain).
Budget: the orchestrator worst case (the_worst_case_composition_measured_sits_far_under_the_budget)
had 243 characters of headroom, so delegate-basics replaces its git sentences (commit/push slice,
evidence-diff, commit before a long run) with the placeholder and shortens unpinned wording, 7,084 to
5,997 characters, for a 1,163-character fragment plus one orchestrator sentence.

Closed ordinary scope below replaces parent §6's provisional execution list. Build each row to its own isolated OutputPath; no build reuse is assumed. Use the checkpoint tool through the host build-slot gate, one run per committed slice group, and wait to a terminal exit (75 means still running). The exact command shape is `pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1213-checkpoints -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1213-tool/ -- run --plan docs/superpowers/plans/2026-10-13-card-1213-test-design.md --after S1 --expected-source-sha <committed-sha>`; substitute S2/S3. Tool bootstrap is included in setup below; row builds take their own slots. Record unedited CP lines, counts, full SHA, clean source/build receipts. Exit 4 is not-run; zero tests is wrong selection. No hand-run build, test or widened filter. Remove only the producer-owned bin-c1213-cpN/ and bin-c1213-tool/ outputs after their runs.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1213-cp1/` | parser | `/*/*/DelegateBranchNameTests*/*` | V-1/R-1 | 9 executed; 0 failed; 0 skipped; named roster | 9 | 3 | true |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1213-cp2/` | manager | `/*/*/(WorktreeManagerTests*)\|(WorktreeManagerSafetyTests*)\|(WorktreeManagerGitIntegrationTests*)\|(WorktreeManagerStartRefLocalOnlyTests*)/(C1213_ExplicitBranchKeepsDirectoryIdentity*)\|(C1213_ScanBothFormsAndExcludeCardSpawn*)\|(Create_names_the_unresolved_start_ref_in_the_refusal*)\|(Create_refuses_a_start_sha_only_origin_has_without_fetching_it*)\|(IsAlreadyUnregisteredWorktreeError_matches_only_the_not_a_working_tree_fatal*)\|(ParseWorktreeList_captures_a_bare_locked_line*)\|(ParseWorktreeList_captures_locked_reason_and_prunable_reason*)\|(Residue_status_distinguishes_clean_tracked_and_protected_files*)\|(Sanitise_accepts_safe_identifier_and_builds_expected_names*)\|(Sanitise_rejects_path_traversal_and_special_chars*)\|(ScanDelegateWorktrees_names_a_dangling_task_branch_and_ignores_card_spawn*)\|(ScanDelegateWorktrees_names_a_locked_registration_whose_directory_is_gone*)\|(TryParseDelegateTaskBranch_accepts_feat_card_task_short_ids_only*)\|(WorktreeJanitor_does_not_convert_raw_refusal_into_authority*)\|(WorktreeJanitor_preserves_stale_unregistered_leftover_directory*)\|(WorktreeJanitor_preserves_stale_worktrees_without_receipt*)\|(WorktreeManager_create_produces_worktree_under_root*)\|(WorktreeManager_create_throws_when_branch_or_path_exists*)\|(WorktreeManager_list_returns_only_worktrees_under_root*)\|(WorktreeManager_raw_remove_cannot_use_local_ancestry_as_authority*)\|(WorktreeManager_raw_remove_preserves_unmerged_directory_and_branch*)\|(WorktreeManager_raw_remove_preserves_unregistered_leftover_directory*)\|(WorktreeManager_remove_still_throws_when_the_worktree_is_locked*)\|(WorktreeManager_touch_updates_last_touched_metadata*)\|(WorktreeManager_try_remove_reports_directory_residue_when_a_file_is_held*)\|(Worktree_path_confinement_rejects_resolved_escape*)` | V-1,V-3/R-1,R-3 | 31 executed; 0 failed; 0 skipped; named roster; one confirmed inherited method excluded | 31 | 4 | true |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c1213-cp3/` | health | `/*/*/(WorktreeHealthServiceTests*)\|(WorktreeResidueSweepTests*)\|(WorktreeResidueRegistrationTests*)/*` | V-3/R-3 | 25 executed; 0 failed; 0 skipped; named roster | 25 | 4 | true |
| CP-4 | S1 | `tests/Antiphon.Tests -> bin-c1213-cp4/` | ownership | `/*/*/(CardDoneWorktreeCleanupTests*)\|(TaskWorktreeRetirementTests*)\|(WorktreeRetirementRaceTests*)/(*)&(!C1017_TerminalTaskRequired)` | V-4/R-4 | 113 executed; 0 failed; 0 skipped; named roster; one confirmed inherited method excluded | 113 | 6 | true |
| CP-5 | S1 | `tests/Antiphon.Tests -> bin-c1213-cp5/` | sync | `/*/*/(RemoteWorktreeMirrorTests*)\|(RunnerSettlementSyncTests*)\|(SettlementSyncDebtPolicyTests*)\|(TaskCompletionProgressPolicyTests*)/*` | V-5/R-5 | 104 executed; 0 failed; 0 skipped; named roster | 104 | 5 | true |
| CP-6 | S1 | `tests/Antiphon.Tests -> bin-c1213-cp6/` | dispatch | `/*/*/(AgentTaskDispatchBaseGuardTests*)\|(WorktreeStartRefDispatchTests*)\|(RemoteVerificationWorkspaceTests*)/*` | V-2/R-2 | 126 executed; 0 failed; 0 skipped; named roster | 126 | 6 | true |
| CP-7 | S1 | `tests/Antiphon.Tests -> bin-c1213-cp7/` | delivery | `/*/*/(DelegateBranchDeliveryTests*)\|(TaskParkPublicationTests*)/*` | V-5,V-7/R-5,R-7 | 7 executed; 0 failed; 0 skipped; named roster | 7 | 6 | true |
| CP-8 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c1213-cp8/` | runner | `/*/*/(RunnerWorkspaceServiceTests*)\|(WorkspaceParkPublicationTests*)\|(RunnerWorkspaceBindingTests*)/*` | V-6/R-6 | 59 executed; 0 failed; 0 skipped; named roster | 59 | 5 | true |
| CP-9 | S2 | `tests/Antiphon.Tests -> bin-c1213-cp9/` | fragments | `/*/*/(InstructionFragmentTests*)\|(InstructionBundleTests*)/*` | V-8,V-9/R-8,R-9 | 85 executed; 0 failed; 0 skipped; named roster | 85 | 3 | true |
| CP-10 | S2 | `tests/Antiphon.Tests -> bin-c1213-cp10/` | launch | `/*/*/(DelegateBundleLaunchTests*)\|(DelegateScriptKindTests*)/*` | V-9/R-9 | 70 executed; 0 failed; 0 skipped; named roster | 70 | 3 | true |
| CP-11 | S3 | `tests/Antiphon.Tests -> bin-c1213-cp11/` | brief | `/*/*/DelegationReportFormatterTests*/*` | V-10/R-10 | 93 executed; 0 failed; 0 skipped; named roster | 93 | 5 | true |
| CP-12 | S3 | `tests/Antiphon.Tests -> bin-c1213-cp12/` | contract | `/*/*/InstructionBundleTests*/*` | V-9/R-9 | 78 executed; 0 failed; 0 skipped; named roster | 78 | 3 | true |
| CP-13 | S2 | `tests/Antiphon.Tests -> bin-c1213-cp13/` | affected | `/*/*/(AgentBundleAttachmentTests*)\|(GrokRulesCompositionTests*)\|(CheckpointRepeatDocumentationTests*)\|(TaskPlatformGuidanceTests*)\|(ScopedVerificationInstructionTests*)\|(PostLandMutationContractTests*)\|(VerificationRoundInstructionTests*)\|(MechanicalPcContractTests*)\|(CheckpointManifestDocumentationTests*)\|(OrchestratorInstructionsGuidanceTests*)\|(RepairSourceDocumentationTests*)\|(BlockedTaskParkProjectionTests*)\|(OutboundConversionTaskTests*)\|(PostLandRetrospectiveContractTests*)\|(AgentReplyStyleTests*)\|(AgentPinnedInstructionCompositionTests*)\|(InstructionFileStampTests*)\|(PolicyRefreshDeltaTests*)\|(CodexDelegateDispatchTests*)\|(PinnedCodexProfileDispatchLaunchTests*)\|(StandingPipelinePolicyDocumentationTests*)\|(DelegationCapabilityContractTests*)\|(SpecialistQualificationContractTests*)\|(CardFilePrivacyDocumentationTests*)\|(BootStallDocumentationTests*)/*` | V-9/R-9 | named roster; at least 191 executed (one per method); 0 failed; 0 skipped | 191 | 6 | true |

Code binding: the census adds 46 new methods and changes the existing parser/foreign-ref/formatter and raw-bundle assertions in place. Each C1213 method must include its hand-written table, target assertion label and reachable production call; do not add placeholder tests to hit Min. Frozen names/IDs/filters stay fixed; a necessary count change requires an explicit design amendment and renewed census before its run. Additional test-only files: Application/DelegateBranchDeliveryTests.cs (nearest topology TaskParkProofWire/PhoneHomeMirrorJoinTests), plus the two new test files already in the parent. Minimal branch/gate parameters may be added to the inspected helpers; unchanged defaults preserve existing fixtures. Bind TaskParkPublicationTests to the local OwnedBranch consumer (missing from parent's test list).

### Cost

Estimates, not measurements; host slot wait is additional and must be reported. Ordinary Code V/R floor = **53 minutes**, the sum of CP EstimatedMinutes (802 expected results including the S3 contract rerun). Filters/minutes: CP-1 parser 3; CP-2 manager 4; CP-3 health 4; CP-4 ownership 6; CP-5 sync 5; CP-6 dispatch 6; CP-7 delivery 6; CP-8 runner 5; CP-9 fragments 3; CP-10 launch 3; CP-11 brief 5; CP-12 contract 3. This includes 24 minutes for twelve isolated builds plus 29 for execution; checkpoint bootstrap/setup adds 2: ordinary setup+V/R = **55 minutes**. Slice budgets remain S1=21 authoring+39 checks=60, S2=39+6=45, S3=22+8=30; bootstrap 2 is shared. Re-estimate rather than silently dropping a row if authoring exceeds these estimates.

PC floor is separate from Code. Each method filter in the PC table runs baseline/red/restored-green with isolated outputs. Per recipe estimate includes all three builds/runs: U=6 (2+2+2), G/H/L=9 (3+3+3), D=12 (4+4+4), Q=15 (5+5+5) minutes. Each PC adds 0.25 minutes for exact restore/timestamp/provenance checks; same-method PCs remain separate cycles.

| PC recipe/filter family | PC IDs | Baseline/red/green minutes |
|---|---|---:|
| U: exact method filters above | PC-1, PC-2, PC-3, PC-4, PC-5, PC-6, PC-7, PC-8, PC-9, PC-10, PC-29, PC-32, PC-33, PC-40, PC-47, PC-48, PC-49, PC-50, PC-51, PC-52, PC-53, PC-54, PC-55, PC-56, PC-57, PC-59, PC-60, PC-61, PC-62, PC-63, PC-64, PC-65, PC-66, PC-67, PC-68, PC-69, PC-70, PC-71, PC-72, PC-73, PC-79, PC-81, PC-82, PC-83, PC-84, PC-85, PC-86, PC-87, PC-88, PC-89, PC-90, PC-91, PC-92, PC-93, PC-94, PC-95, PC-96, PC-97, PC-98, PC-99 | 360 |
| D: exact method filters above | PC-11, PC-12, PC-13, PC-14, PC-15, PC-100 | 72 |
| G: exact method filters above | PC-16, PC-17, PC-18, PC-19, PC-21, PC-25, PC-30, PC-31, PC-35, PC-36, PC-37, PC-38, PC-39, PC-41, PC-74, PC-75, PC-80 | 153 |
| H: exact method filters above | PC-20, PC-22, PC-23, PC-24, PC-26, PC-27, PC-28, PC-76, PC-77, PC-78 | 90 |
| Q: exact method filters above | PC-34, PC-42, PC-43, PC-44, PC-45, PC-46 | 90 |
| L: exact method filters above | PC-58 | 9 |

Mutation setup=2; phase builds/runs=774; restoration=25; **Mutation PC floor=801 minutes**. Combined ordinary setup+V/R+Mutation = **856 minutes**, excluding authoring, independent Review and slot waits. No PC time is charged to Code; Mutation remains paused. No assumed batching saving: most controls share parser/composer/consumer files or the same method, and isolated source-qualified phases are required. Reusing whole classes would spend more and would violate method scoping. Savings from replacing a full suite are not quantified because no current full-suite measurement exists; claimed scheduling savings=0.

Handoff audit: **guards=100, mapped=100, missing=0, duplicate PC maps=0**. Each PC has a method filter, recipe, variants, compiling defect and decisive red oracle. All 100 PCs are required when Mutation resumes; **PCs run=0, pending=100** by explicit commissioning instruction. Reachability remains unproven and new methods and any unbound assertion payloads remain binding required after Code. Code must not label these controls green. There is no open human choice under OD-1..OD-6. Next: Code, S1 first; return to Plan if the named real-queue or composition seam cannot be bound without changing the fix design.
