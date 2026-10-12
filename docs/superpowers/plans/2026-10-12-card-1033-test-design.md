# CARD-1033: verification freeze for native cleanup scheduling

Task `f9bcd692-68a4-4d71-88a5-bf7148e7cec4`, 2026-10-12. Source inspected:
`1edf51aae0133e5b52e205bb15a5da7ad9a39214`; branch `feat/card-task-f9bcd692`.
Bound task credential is present; its value was not read or printed.

This companion appends verification to the [landed fix plan](2026-10-11-card-1033-cleanup-scheduler-native-plan.md).
Its D-1..D-9 and S1..S17 remain controlling. This document supersedes that plan's
**proposed verification manifest only**; use this file as the checkpoint input.
The [CARD-1017 freeze](2026-10-03-card-1017-completed-card-worktree-cleanup-plan.md#verification-design)
retains its parent requirements and PC identities. No production or test code,
build, test execution or mutation is part of this TestDesign dispatch.

The live CARD-1033 description and all three revision entries were read using
`card.ps1 get/history CARD-1033 -Board Antiphon`. They retain execution-off and
historical-cohort protection; no additional activation authority was found.
**Ship Execute=false and CardDoneAfterUtc=null.** Choosing a cutoff, widening a
historical cohort, and enabling execution remain operator decisions after qualification.
Post-land Mutation is paused by the commissioning brief: the controls below are
specified, **pending**, and not represented as run. Ordinary Code/Review can proceed
under the disabled defaults; this design does not qualify activation.

## Verification design

### Inspection

Bodies read (and source census, where explicitly distinguished):

| Bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| `CompletedCardCleanupFixture` and `CompletedCardRemovalFixture`, complete; `CardDoneWorktreeCleanupTests` D18 and `CompletedCardWorktreeRemovalTests` L36, complete | real Git, fresh PostgreSQL observers, external canonical report, pre/after-intent and unregister hooks -> V-1..V-4, R-1/R-2. D.RecoveryDebtHolds currently has only a pending-request arm. S1 must opt fixtures into test-only execution/cutoff. |
| `WorktreeResidueRecoveryTests`, complete; `WorktreeResidueSweepTests`, complete | restart/command/component/crash helpers -> V-2/V-3; the empty budget/cooldown worlds cannot prove these new populated assertions. |
| first seven `WorktreeLandingCleanupRetryTests` bodies; `LandingSafetyHarness`, complete, including SaveFault/TransactionFault/RetirementCutInterceptor, RestartServicesAsync and RunQueuedAsync | actual land channel, request/operation identity, committed-state observer and restart -> V-3/V-5, R-2. RunAsync alone bypasses queue receipt and is insufficient for delivery. |
| `WorktreeResidueEndpointTests` six bodies, `WorktreeResidueScriptTests` body, `WorktreeResidueRegistrationTests` three bodies and ScopeJobActivator; `scripts/worktree-residue.ps1`, complete | existing HTTP/PowerShell/real Hangfire front doors -> V-4/V-5/V-7, R-4. Their constant counters and empty preview assertions are compatibility checks only. |
| `PhoneHomeTestHost.StartAsync/RegisterAsync/ConnectPeerAsync/WaitLiveAsync/DisposeAsync`; runner Workspace dispatcher methods and Dispatcher/RecordingRuntime; selected runner Mirror/Publish/Remove bodies and Scratch | loopback transport -> V-5; canned peer/RecordingRuntime cannot prove deletion. WaitLiveAsync uses wall time; the new delivery fixture uses a monotonic bounded wait instead. |
| `RemoteWorktreeMirrorTests`, complete | eight legacy push/FF tests -> R-3, not runner removal evidence. |
| `WorktreeLockDiagnosticsWindowsTests`, `WorktreeLockChild`, `TestOwnedPtyHost`, complete; owned-host identity/policy tests; DirectSessionRunnerClientDisposalTests and independent OwnedPtyWitness capture | sharing denial, exact retained process identities, monotonic owned teardown -> V-6. New holder uses the inherited dotnet worker mode, not WorktreeLockChild's reflection-in-pwsh launcher. |
| `TestDbFixture`, `TestWorkerModes`, `CrashWorkerProcess`, complete; AntiphonWebAppFactory configuration and ProductionRunnerGuard | cloned PostgreSQL databases, child marker isolation, inherited workers, isolated runtime -> V-2..V-7. No real Program fixture may reach the deployed runner. |
| `SettlementSyncDebtSchemaTests` and `tests/Shared/TestClassificationGuardTests.cs`, complete | nearest additive-schema and compiled Slow-registry fixtures -> V-8, R-5. |
| `CardWorktreeCleanupExecutor`, `WorktreeResidueSweepService`, `GuardedWorktreeRemoval`, `WorktreeSetAside`, `RetirementCommandJournal`, complete; retirement GetRunAsync/TryRetireCoreAsync, land RequestCardDoneCleanupRetryAsync/outcome binding, WorktreeRemovalEvidence.CanDisposeCardDoneAsync, CardService MoveAsync/SaveCardWriteAsync/ApplyAutomatedMoveAsync | direct/pending bypasses, short transaction, exact aside/receipt and reporting order -> V-1..V-5/V-7. SaveChanges inside an outer transaction is not the actual card commit. |
| settings/validator, cleanup endpoint and residue entities; original parent guard and PC tables; CARD-1032 plan fetched per caller refinement and read from `FETCH_HEAD=0fcb74d618c5d3ea89c778910e0dc3ad6da5aea0` | inert defaults and additive identity fields -> V-1/V-8; S3 contract binding below. |
| project conventions, testing/build manifest/coverage/build-slot owners, orchestration cleanup and stage rules, card lifecycle, ops HTTP, session delivery/ownership rules and ModernConPty ADR | stage, receipt, process and activation boundaries throughout. Unchanged legacy foundation regressions below are source-censused; this row does not claim fresh execution. |

#### CARD-1032 boundary binding

The supplied CARD-1032 plan is available at `0fcb74d61` on
`feat/card-task-771fa4ce`; it is not a file or implementation in this starting tree.
Its sibling TestDesign owns its expanded C1032 methods. Bind by these **named
contracts**, not an invented operation number, DTO layout or guessed landed SHA:

| Boundary name | Contract consumed by CARD-1033 | Binding witness |
|---|---|---|
| `TaskWorktreeMirrorBinding`, `RunnerWorkspaceBindingStore` | immutable full task/attempt/binding/store/repository/path/ref/creation identity; retain late/reassigned bindings; historical unbound trees held | S3 U/M prefix rosters, V-2 independent endpoints, V-5 transport |
| `ICompletedCardWorkspaceClient`, `CompletedCardWorkspaceContracts`, `PhoneHomeRunnerClient`, `PhoneHomeCommandDispatcher`, `PhoneHomeRuntimeAdapter` | distinct inspect/guarded-remove capability; no Force or legacy WorkspaceRemove fallback; request/receipt carry identical versioned identity and digest | V-5 real socket and dispatcher; inherited PC-52..70/74..79 |
| `CardWorktreeMirrorCleanupService` (and `CardWorktreeMirrorCleanupAdmission` if split) | new-intent admission and exact-operation reconciliation are distinguishable; read-only receipt reconciliation is distinguishable from replay that can mutate | V-1 direct/resume policy; V-2 durable admission; V-3 result-save recovery |
| `RunnerWorkspaceUseGate`, `RunnerWorkspaceRemovalJournal`, `RunnerCompletedCardWorkspaceRemoval` | workspace gate before runtime/session/custody gates; journal before mutation; exact digest replay; separate directory/registration facts; retained ownership veto | V-3/V-5/V-6; inherited U21 |
| `CompletedCardRunnerFixture`, `CompletedCardMirrorFixture` | real local runner dispatcher/remover, scratch Git/state roots, deterministic I/O hooks; socket receipt can be lost independently of physical mutation | V-5 and native V-6 |

S1 may proceed on the foundation. Before touching an S3-owned file, Code records
the actual landed SHA and maps these contract names to its declarations. It must
serialize shared endpoint/model/fixture/worker-entry files with CARD-1032. The
C1017 U21/M12 selectors below intentionally exclude sibling C1032 prerequisites;
those remain required in CARD-1032's own closed manifest. Include that owner's
ordinary receipts and native binding/fence additions in Review's dependency ledger.
An absent implementation is **binding required**, not a passing substitute.
A semantic contract mismatch returns to Plan before dependent S12/S13b/S15/S17;
Code must not create a second mirror protocol to make the tests compile.

Admission ordering witness: the residue run-row transaction commits the action key,
debit and endpoint link and releases its lock **before** endpoint admission starts.
Endpoint admission follows S3's shared admission/card/task/endpoint ordering; the
foundation currently takes WorkspaceReservationJournal admission, card row, task row.
No code path holds the run row and then waits for a workspace/repository/network
lease. Deterministic two-context tests pause at each boundary and exercise both
orders against reopen and another run. Code binds any additional S3 lock explicitly;
if incompatible, keep the run transaction separate and return the conflict to Plan.

#### Fixture recipes, variants, reachability and classification

Recipe F: extend CompletedCardCleanupFixture with an injected FakeTimeProvider,
Execute=true and a fixture-only UTC cutoff one day before its Done revision. Use
real tracked source, disposable ignored evidence, canonical external Result,
distinct preserved author report and external binary deliverable; snapshot bytes
and origin refs before action. Positive baseline removes directory/registration
(and the local ref where required) and reads preserved bytes afterwards. Each
refusal starts from that baseline and flips one named fact. Expected values are
literal data beside the case name, not obtained by calling the classifier under test.

Recipe S: new `tests/Antiphon.Tests/TestHelpers/CompletedCardSchedulingFixture.cs`
composes F, legacy published/settled candidates and S3 mirror fixtures. Candidate
keys use immutable attempt/generation/endpoint/store identity; two tasks may share
a display name. Seed enough **eligible** rows to exceed 25, plus held/cooling rows.
Fresh contexts and actual services execute admissions; record actual Git/dispatcher
calls, not an optimistic fake remover. Use transaction/save hooks only to block or
fail I/O. Rebuild the service provider and clocks around persisted state on restart.

Recipe Q: new `tests/Antiphon.Tests/TestHelpers/CompletedCardDeliveryFixture.cs`
adds an owned single-worker Hangfire storage/server, real AgentTaskLandQueue and
its production worker/service graph, and loopback PhoneHomeLiveConnection outbound
channel with a real dispatcher peer. Reuse PhoneHomeTestHost framing and registration.
Hangfire memory storage is disposable; PostgreSQL obligations provide durable
recovery after replacing that storage. The fixture owns workers, ephemeral ports,
queue blockers and cancellation. Observe recipient removal and receipts independently.

Recipe X: new CompletedCardCleanupCrashWorker registered in TestWorkerModes;
launch with CrashWorkerProcess's inherited dotnet/deps mechanism. Request contains
fixture owner nonce, allowed cut, full task and target; reject foreign root, outside
ready path and unknown cut before touching the database/files. Parent owns clone,
repository and processes. A ready barrier acknowledges exact cut/process identity;
kill only that owned child, join/drain, then start a fresh worker over the same state.
No exception-only restart stands in for the process-death arms.

Recipe N: new CompletedCardWindowsFixture starts a locally inherited holder in
its own worker mode and a real, test-owned ModernConPty PtyHost using the landed
owned-host helper. Pin PID plus start generation, session and state root; assert
native backend and live host/child/console witnesses before cleanup. A terminal
DB row does not terminate the process. Use FileShare without Delete, real junctions,
and independent outside sentinels. Only test teardown releases/joins the holder.
A missing backend, executable, junction privilege or barrier is failed qualification.

Recipe H: a real Program test host on a cloned database plus isolated runner,
retaining ProductionRunnerGuard. Only this fixture's scoped services/worker are
enabled; unrelated timers, provisioners and machine census stay disabled. Register
real cleanup services through production DI when S17 lands. For actual PowerShell
readback use a fixture-owned loopback HTTP server, literal ArgumentList and an
inherited bounded child, serialize the actual returned object for assertions. A
source-string match in the script is not the oracle. No real secret/token is used.

F/S/Q/X/N/H are Integration. K, A and W additionally carry Slow and their registry
reasons (multiple real-Git/DB or native/crash worlds), and ProcessSpawnLimit; classes
using Hangfire global configuration or process environment are NotInParallel.
Schema is Integration, with no process spawn. Composition is Integration/Slow and
ProcessSpawnLimit. D/L and inherited regressions retain their existing metadata.
R-5 runs when K/A/W/composition first enter the Slow registry. No new Checkpoints
namespace test is added, so its independent census is unchanged.

Persisted UTC governs Done cutoff, age and retry due times. Use clock.GetTimestamp /
GetElapsedTime from TimeProvider.System (or Stopwatch), cancellation budgets and deterministic barriers for elapsed deadlines;
wall-clock jumps must not extend a queue/crash/holder wait. FakeTimeProvider advances
business time without sleeping; it does not supply a frozen deadline clock. A watchdog expiry is a failed reached-boundary claim.
New methods are single [Test] results with named internal vector loops: variants and
assertions do not increase Min. Tests label the case and actual reached seam before
asserting the result. Independent later guards stay active; assert policy refusal,
inspection count or command count at the specific seam so they cannot mask a PC.

### Delivery inventory

Join records with `(CardId, DoneRevisionId, TaskId, Attempt, TargetId, EndpointId,
EndpointIdentity, RunnerId, RunnerStoreId, OperationId, RequestDigest)` plus RunId,
ActionKey and the exact retirement/land request ID. Fields not applicable to a local
endpoint remain null; a missing required field is not a wildcard.

| Path | Producer -> destination | Persistence boundary and recovery | Observable recipient receipt / tests |
|---|---|---|---|
| Manual Done wakeup | actual CardService move -> ICardWorktreeCleanupWakeup -> existing Hangfire WorktreeResidueJob -> scheduler/executor | Done revision + obligation commit before wakeup; failed/dropped wakeup recovers through startup/recurring discovery; outer transaction must commit first or suppress wakeup | A.DoneQueueBusyRecipient and AlreadyEligible end at actual tree/registration/aside absence and fresh API identity readback; K.LostWakeupRecovers; PC-3/83/110 and new post-commit controls |
| Tracker/other Done and startup recovery | actual automated Done writer -> atomic obligation -> startup/recurring existing job | no I/O in CardRevisionLog; legacy missing obligation requires a genuine Done transition; replace in-memory Hangfire storage on restart | K.LostWakeupRecovers and composition startup method end at recipient state; legacy no-revision case stays held; late attempts remain discoverable; PC-83 plus discovery/startup controls |
| Publication cleanup | scheduler -> saved cleanup-only request -> AgentTaskLandQueue -> land worker/repository lease | request references original publication before channel write; rejected/full/lost channel wake and worker crash replay that same request | A.PublicationQueueBusyRecipient/AlreadyEligible, K.PublicationIsImmutable: directory/registration/aside gone; publication ID/SHA and origin refs unchanged; zero push/rebase; PC-90/110 plus enqueue split |
| Mirror cleanup/replay | scheduler/coordinator -> PhoneHomeRunnerClient -> real outbound channel/WebSocket -> dispatcher -> RunnerCompletedCardWorkspaceRemoval | server immutable intent before send; external runner journal before bytes; receipt before reply; import before server completion; server and runner restart separately, same operation/digest | A.PhoneHomeQueueBusyRecipient/AlreadyEligible: exact imported complete runner receipt AND independent runner filesystem/Git state; lost response uses same operation; PC-67/71/74..82 and recovery controls |
| Durable operator readback | executor -> endpoint/run/task-event transaction -> AgentTaskChanged hint -> existing HTTP GET and actual PowerShell Status/Preview | outcome and event visible from second connection before hint; hint failure does not erase committed facts; new host reads same run | A.OperatorOutcomeIsDurable/EveryIntentIsReportable/NoOpaqueBytesInReport/ReportReadbackSurvivesDeletion; PCs-97..100 plus readback controls |

Busy variants hold an **owned** worker/job, repository lease or runner workspace
gate before producing. Observe the recipient blocked and bytes still present,
release the blocker from the test, then await its actual receipt. Already-eligible
variants start the recipient waiting before producing, so a missing wakeup cannot
be hidden by an after-the-fact explicit drain. Do not replace either with direct
executor calls. Timeout, enqueue success, queue insert, event, Sent flag or socket
ack is insufficient. Refused/unknown paths retain byte snapshots and visible debt.

No session-input or caller notification is added. Scheduled cleanup requests must
have ReplyTo=None and no Outcome notification obligation. If Code changes that
scope, return to TestDesign: busy/eligible SessionMessageQueue tests must end with
the matching **complete UserPrompt transcript** after the attempt floor, with
spill content resolved where applicable. Existing report tokens are not receipts.

Substitutes: in-memory Hangfire proves production worker handoff, not durable
Hangfire storage; fresh PostgreSQL-obligation recovery closes its loss case.
Loopback socket proves channel/dispatcher/recipient behavior, not deployed network
connectivity. Save/transport fault hooks prove the chosen fault, not recipient
completion. FakeTimeProvider proves due/deadline policy, not real elapsed performance.
A stubbed native ownership result or Linux unlink cannot qualify V-6.

#### Crash cut roster (required even when a path safely withholds)

C01 before Done commit; C02 after commit/before wake; C03 before Hangfire enqueue;
C04 after Hangfire enqueue; C05 before land channel write; C06 after land channel
write; C07 before socket send; C08 after runner intent; C09 after rename; C10 after
unregister; C11 during ignored-file deletion; C12 after local physical outcome/before
save; C13 after remote physical outcome/before reply; C14 after reply/before server
save; C15 after outcome/event commit/before UI hint. The full parent method owns
these 15 cuts. Its local companion selects C01..C06/C09..C12/C15 (11 cuts).
C09..C11 have both local and mirror variants in the full method. Additionally fault
Hangfire enqueue, land TryEnqueue=false/throw, socket send, response loss, receipt
import save, and debit-before-endpoint handoff. After C01 rollback there is no
removal; explicitly commit a new actual Done transition before expecting recovery.
Other cuts recover the same committed identity or remain held for the named safety
fact. Reopen/replacement/unsupported-store arms withhold rather than fabricate success.
Each arm records exact cut reached, committed rows, recipient bytes/registration,
operation cardinality, report bytes and the absence of new publication/process stops.

The baseline crash cases have the following explicit outcomes. Reopen, replacement
and unsupported-store variants are separate one-flip cases; they cannot replace a
successful recovery baseline. An operation identity is compared with its precrash
value only after its durable creation; before then, require exactly one first issuance.

| Cut | Baseline state immediately after crash | Oracle after fresh worker recovery | Detecting control |
|---|---|---|---|
| C01 | Done transaction rolled back; source bytes intact | no obligation, intent or removal; a subsequent explicitly committed Done is a separate positive case | PC-3, PC-179 |
| C02 | Done/obligation committed; wake absent | job rediscovers same generation and recipient completes one admitted endpoint | PC-83, PC-203 |
| C03 | committed obligation; enqueue not accepted | restart/recurrence reaches recipient; no lost obligation or refunded debit | PC-110 |
| C04 | queued wake can be lost with disposable queue storage | fresh storage plus persisted obligation reaches recipient once | PC-83, PC-110 |
| C05 | cleanup-only land request committed; channel write absent | same request and publication reach land recipient; no new publication | PC-90, PC-193 |
| C06 | land channel write accepted; worker not complete | queue recovery executes same durable request; original publication and refs unchanged | PC-90, PC-204 |
| C07 | server mirror intent committed; socket send absent | exact operation/digest is sent after reconnect, imported receipt and runner removal agree | PC-194 |
| C08 | runner intent/journal exists before mutation | replay resolves same journal and owned tree; no new command allowance | PC-67, PC-84 |
| C09 | owned rename completed; original absent | matching aside removed; registration facts resolved; replacement variant keeps replacement bytes | PC-85, PC-87 |
| C10 | matching registration removed; owned aside still present | endpoint stays partial until aside removal, then completes | PC-89, PC-169 |
| C11 | ignored-file deletion partly completed | remaining owned aside bytes removed; outside sentinel and preserved artifacts unchanged | PC-85, PC-103 |
| C12 | local physical result exists; server result save absent | persisted exact intent/journal plus component observations reconcile same operation; no false success before reconciliation | PC-111 |
| C13 | runner physical result and external receipt exist; response lost | exact old operation returns receipt; server imports it; no new operation | PC-82 |
| C14 | runner reply received; server import not committed | before import endpoint remains pending/partial; replayed same receipt completes it | PC-170 |
| C15 | correlated outcome and event committed; hint absent | fresh HTTP/PowerShell reads committed facts; no repeated mutation required | PC-97, PC-172 |

Crash-worker refusal controls use a second **test-owned** scratch root/task as
the foreign identity. They assert an orderly structured refusal versus acceptance,
not a process startup exception, fixture failure or timeout. The positive owned
request must reach its named cut before the destructive child is killed and joined.

For PC-203's prompt wakeup witness, recurrence is disabled in that fixture phase;
the waiting worker must receive the actual post-commit wakeup. Recovery through
recurrence is exercised separately by K.LostWakeupRecovers. For PC-204 the busy
lease is released before awaiting the real land worker's physical result.

### Proves it works now

Names in the following matrix are fully qualified by class aliases:
K=`Antiphon.Tests.Application.CardWorktreeCleanupRecoveryTests`;
A=`Antiphon.Tests.Application.CardWorktreeCleanupAcceptanceTests`;
W=`Antiphon.Tests.Infrastructure.CompletedCardWorktreeRemovalWindowsTests`;
D=`Antiphon.Tests.Application.CardDoneWorktreeCleanupTests`;
L=`Antiphon.Tests.Infrastructure.CompletedCardWorktreeRemovalTests`;
U=`Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests`;
M=`Antiphon.Tests.Application.CardWorktreeMirrorCleanupTests`.
K/A/W are new files at their namespace paths. C1017 prefixes are retained.
The exact command for a method is the containing CP filter; a PC uses the literal
method filter printed in the Positive controls table. New payloads, labels and
S3 declarations remain **binding required after Code** until coverage lint plus
source review bind them. A syntax binding alone does not establish reachability.

| ID | Behavior / layer | Required methods / recipe / variants | Oracle, reachability and mutation-to-red |
| V-1 | integration | `CardWorktreeCleanupRecoveryTests.C1017_ExecuteFalseCannotDelete`; S; otherwise eligible local and mirror; false/true; old admitted local/publication/mirror intents; read-only receipt import while disabled | False: zero new destructive admissions/calls, bytes and fences unchanged. True positive: recipient removes owned tree. Observe policy at executor entry as well as sweep. Disabled read-only import may persist proven existing receipt. Case -> mutation -> red: Ignore sweep Execute: false arm destructiveCalls=0 turns red (PC-91). |
| V-1 | integration | `CardWorktreeCleanupRecoveryTests.C1017_HistoricalCutoffCannotDelete`; F/S; null, Done before, equal, one persisted microsecond (10 ticks) after UTC cutoff; reopen/new generation; immutable revision time differs from mutable card time | Null/before/equal: tree retained; after: eligible removal. Inventory remains visible. Changing process start/migration time cannot qualify historical rows. Malformed/non-UTC config fails startup validation. Case -> mutation -> red: Accept historical Done: historicalTreeExists=true red (PC-92); null/equality/config have separate new PCs. |
| V-1 | integration | `CardWorktreeCleanupRecoveryTests.C1017_SettlingFloorCannotDelete`; F/S; CompletedAt null and age 119:59.999, 120:00.000, 120:00.001; terminal otherwise eligible | Null/below: no intent and source remains. Equal/above: actual removal. Explicit fixture clock sets age independently of cutoff; repeat mirror admission. Case -> mutation -> red: Remove floor: belowFloorTreeExists=true red (PC-93). |
| V-1 | integration | `CardWorktreeCleanupRecoveryTests.C1033_DirectAndRecoveryGatesHold`; F/S; direct local/mirror new intent and persisted local/publication/mirror replay; independently false Execute, null/equal cutoff and preview run | Call recipient entry directly, bypassing scheduler; zero mutation under each denied gate, policy reason from that seam, same operation/fence retained; corresponding all-positive direct/replay arm reaches removal. Case -> mutation -> red: Omit one named entry check: directDeniedMutations=0 red; new PCs split new/resume/local/mirror/publication paths. |
| V-1 | integration | `CardWorktreeCleanupRecoveryTests.C1033_PreviewDoesNotMutate`; S; populated Preview with Execute=true, qualifying cohort, due local/mirror/legacy work and existing intents | Preview=true, Execute=false in response; zero debit, new intents, queue sends and byte mutations; independent pre/post byte+ref inventory. Existing orphan reservation hygiene is permitted but cannot drop cleanup fences. Case -> mutation -> red: Forward execute into preview: previewMutations=0 red (new preview PC). |
| V-1 | integration | `CardWorktreeCleanupRecoveryTests.C1033_UnknownOwnershipStaysHeld`; F; AgentId present with terminal/stopped DB rows, no live-consumer row; missing/unknown process evidence; AgentId absent positive | Reason process_ownership_unknown and no intent, bytes retained. Record reaching the ownership branch; the absent-AgentId eligible control removes. W calls runner directly and cannot be masked by this hold. Case -> mutation -> red: Delete local AgentId refusal: ownershipReason=process_ownership_unknown red (new ownership PC). |
| V-2 | integration | `CardWorktreeCleanupRecoveryTests.C1017_ActionBudgetIsShared`; S; 26 due eligible distinct physical endpoints: 7 legacy settled + 6 legacy publication + 7 CardDone local + 6 mirrors; add held/cooling/excluded rows outside these 26 | At first I/O observer sees committed admission/debit; exactly 25 accepts total, remaining row Deferred/budget_exhausted. Different shuffled input order produces hand-written persisted cursor order. Second run visits oldest deferred endpoint. Case -> mutation -> red: Allocate 25 per lane: acceptedActions=25 red (PC-94). |
| V-2 | integration | `CardWorktreeCleanupRecoveryTests.C1033_ScopeAndDedupAreExact`; S; two boards in same project, second project, inconsistent/unknown scope; same physical CardDone endpoint represented as legacy release/publication | Compare explicit endpoint/key sets and counts for project/board intersection; unrelated task/landing/inventory absent from scoped report; unknown scope withholds. Duplicate physical endpoint accepts once; same task different attempt/store accepts independently. Case -> mutation -> red: Drop one scope predicate or physical-key collapse: selectedKeys=literalExpectedKeys or duplicateAccepts=1 red (split scope/dedup PCs). |
| V-2 | integration | `CardWorktreeCleanupRecoveryTests.C1033_AdmissionPrecedesIo`; S/X; observer at first git/queue/socket I/O, crash after debit before endpoint command, same-run same-key race and save failure between row/debit | Observer sees run debit and key+endpoint link atomically. Same-key racers leave one admission/debit; after crash old debit remains spent, original command ID reused or first command attached once. New run spends a visit only. Held/cooling/preview costs zero. Case -> mutation -> red: Move debit after I/O: admissionVisibleAtIo=true red; non-atomic save/unique/refund variants have independent PCs. |
| V-2 | integration | `CardWorktreeCleanupRecoveryTests.C1033_ClaimsDoNotHoldDbAcrossIo`; S; barriers in Git, land enqueue and mirror send; concurrent reopen and second run; both winner orders | Second writer commits before owned I/O barrier release, from separate connection; endpoint has at most one issued operation under concurrent runs. Timed failure reports unreached seam. Use monotonic ten-second diagnostic budget; do not wait for timeout to choose a winner. Case -> mutation -> red: Retain specific transaction across awaited I/O: secondWriterCommittedBeforeIoRelease=true red (separate Git/queue/network PCs). |
| V-2 | integration | `CardWorktreeCleanupRecoveryTests.C1033_DiscoveryRemainsOpen`; S; completed header/endpoints, then new settled attempt; missing obligation with real transition; status Done without transition; reopen | Attempts [1,2] remain distinct and later eligible attempt executes; no-revision Done stays held with zero intent; reopened generation issues none. Current immutable transition is the only new admission source. Case -> mutation -> red: Stop scheduler enumeration at complete header: discoveredAttempts=[1,2] red; fabricate revision for legacy status has separate PC. |
| V-2 | integration | `CardWorktreeCleanupRecoveryTests.C1017_CooldownSurvivesRestart`; S; transient local, legacy settled, publication transport and mirror transport refusals; restart at due-1 tick and exactly due | Persisted delay is 24 hours; beforeDueCommands=0; atDueCommands=1 for the due endpoint, positive result at actual recipient. A separate attempt/store with no cooldown stays eligible. Case -> mutation -> red: Discard endpoint NextAttemptAt on restart: beforeDueCommands=0 red (PC-95); legacy cursor and equality split below. |
| V-2 | integration | `CardWorktreeCleanupRecoveryTests.C1017_PermanentRefusalAwaitsEvidence`; S; safety refusal then two sweeps with same evidence, advance 48h; change only irrelevant display data; repair meaningful evidence | No repeated destructive command for unchanged or irrelevant-change evidence. Relevant repair triggers full recheck; held when another guard still fails, removal only when positive whitelist complete. Unknown error remains held with bounded safe reason. Case -> mutation -> red: Treat elapsed time as retry permission: repeatCommands=0 red (PC-96). |
| V-2 | integration | `CardWorktreeCleanupRecoveryTests.C1033_PermanentEvidenceIsComplete`; S; one-field flips in seven evidence groups: generation/attempt, source/repository, preservation pointer/digest, ownership/use, endpoint/store binding, content inventory, registration; include unreadable evidence | Independent literal expectedChanged booleans; each relevant repaired group revisits full eligibility; still-invalid/unknown group withholds. Stored digest excludes content and credential-bearing URLs. Operation identity remains unchanged for already-admitted debt. Case -> mutation -> red: Omit one evidence group from canonical digest: eligibleRechecks=1 red for its repaired case; changed evidence auto-approve has its own PC. |
| V-3 | integration | `CardWorktreeCleanupRecoveryTests.C1017_IntentRecoveryDoesNotResetBudget`; F/S/X; persist command; kill before receipt, recreate coordinator/queue; later run and concurrent replay | Distinct destructive command IDs=1; spent admission retained, exact aside recovery allowed, no fresh destructive command allowance. Source/fence/report identities unchanged. Case -> mutation -> red: Clear spent command slot during restart: distinctDestructiveCommandIds=1 red (PC-84). |
| V-3 | integration | `CardWorktreeCleanupRecoveryTests.C1017_SetAsideRecoveryPreservesAuthority`; F with real publication and owned partial aside; worktree_removal_incomplete, unregistered ignored evidence; restart | Original CardDone disposition and endpoint identity reach guarded remover; owned aside removed and publication unchanged. Ordinary disposition refuses disposable evidence until qualified CardDone replay. Case -> mutation -> red: Drop CardDone endpoint/disposition on reconstruction: ownedSetAsideGone=true red (PC-85). |
| V-3 | integration | `CardWorktreeCleanupRecoveryTests.C1017_SetAsideIdentityMustMatch`; F; valid owned aside baseline; flip original path, computed aside path, git-admin identity, operation owner, source; malformed record separately | Foreign aside sentinel and replacement bytes remain exact; named mismatch reason before delete. Matching receipt succeeds. Use authoritative operation journal when WorktreeSetAsideRecord does not itself carry source/operation. Case -> mutation -> red: Accept wrong receipt owner: foreignAsideBytes=originalBytes red (PC-86); remaining independently compared fields split below. |
| V-3 | integration | `CardWorktreeCleanupRecoveryTests.C1017_ReplacementPathIsUntouched`; F/X; after owned rename recreate original path with different source/sentinel; before unregister and restart variants | Replacement remains byte-identical and registered if separately registered; no recursive original-path delete; old aside either safely completes independently or remains explicit partial debt. Case -> mutation -> red: Delete occupied original during recovery: replacementBytes=originalBytes red (PC-87). |
| V-3 | integration | `CardWorktreeCleanupRecoveryTests.C1017_AbsenceNeedsReceipt`; F; missing source with no receipt; torn receipt; access-denied query; valid matching completed receipt | No receipt or uncertain read: endpointComplete=false and debt queryable. Only a matching receipt plus independently resolved components permits complete. Raw Directory.Exists(false) is insufficient. Case -> mutation -> red: Map missing path to Complete: endpointComplete=false red (PC-88); access-denied has separate PC. |
| V-3 | integration | `CardWorktreeCleanupRecoveryTests.C1017_ComponentFactsAreIndependent`; F/S; directory gone/registration present; registration gone/aside present; directory+registration gone/local ref not deleted; reverse local/mirror partial order | Persist exact nullable tuple per endpoint; local Complete requires its required components, mirror requires directory+registration and keeps refs. Local completion cannot collapse pending mirror; no success from absent original with aside debt. Case -> mutation -> red: Use DirectoryGone alone: endpointComplete=false red (PC-89); registration/ref/aside branches split below. |
| V-3 | integration | `CardWorktreeCleanupRecoveryTests.C1017_PublicationIsImmutable`; Q; real publication with refused cleanup, new CardDone cleanup-only request, busy lease, retry and restart | Run actual land channel; directory/registration/aside gone; saved publication ID, original/rebased/verified SHAs and origin refs same; zero new push/rebase/publication events. ReplyTo=None and no new caller note. Case -> mutation -> red: Route cleanup through publication: publicationCommandsAfterCleanup=0 red (PC-90). |
| V-3 | integration | `CardWorktreeCleanupRecoveryTests.C1017_ResultSaveFailureRetainsDebt`; F/S/Q; fail local result transaction after removal; mirror receipt import/save fails separately; restart fresh server/runner | Persisted intent remains; exact physical/receipt reconciliation completes same operation; before reconciliation API shows pending/partial not invented success. Canonical/report/deliverable bytes survive. Case -> mutation -> red: Delete pending endpoint before outcome save: recoveredEndpointComplete=true red (PC-111); mirror import path split below. |
| V-3 | integration | `CardDoneWorktreeCleanupTests.C1017_RecoveryDebtHolds`; F; extend existing method with pending Queued/Held/Running/NeedsResolution land requests, active unconfirmed/interrupted landing, unfinished repair/recovery owner, missing/unknown publication mapping; exact confirmed publication positive | Each singly-present debt preserves bytes, admits no settled removal and names the reason; all other eligibility facts valid. Confirmed exact publication creates cleanup-only request and real queue completes without publication. AgentId remains null so ownership does not mask debt. Case -> mutation -> red: Bypass pending-request debt: settledRemovalCalls=0 red (PC-15); distinct landing/repair/recovery predicates get new PCs. |
| V-5 | integration | `CardWorktreeCleanupRecoveryTests.C1017_LostWakeupRecovers`; Q; manual Done, tracker automated Done and committed missing-obligation cases; suppress wakeup and replace worker/storage | Fresh startup/recurring job reaches actual removal after reading persisted generation. Done without revision remains held. Observe committed obligation before worker admission. Case -> mutation -> red: Omit missing-obligation recovery query: directoryGone=true red (PC-83). |
| V-5 | integration | `CardWorktreeCleanupRecoveryTests.C1017_EnqueueFailureRemainsRecoverable`; Q; Hangfire throw, land channel false and throw, first send throw; intent already saved; restart | Committed identities survive and actual queued recipients complete after recovery; no debit refund or new publication/operation. Also test save failure before enqueue: no orphan queue work. Case -> mutation -> red: Clear pending after Hangfire failure: directoryGoneAfterRecovery=true red (PC-110); land and send recovery have separate PCs. |
| V-4 | integration | `CardWorktreeCleanupAcceptanceTests.C1017_OperatorOutcomeIsDurable`; H/Q; local complete/mirror pending, reverse order, refusal and event-hint failure | During AgentTaskChanged callback second connection sees committed outcome plus correlated task event. After fresh host start HTTP and actual PowerShell show same per-endpoint facts and safe reason. Case -> mutation -> red: Emit hint before save: committedOutcomeAtEvent=true red (PC-97). |
| V-4 | integration | `CardWorktreeCleanupAcceptanceTests.C1017_EveryIntentIsReportable`; H/S/X; crash before first I/O; seeded 53 rows to force pages, legacy rows with null new fields; a valid test-owned child caller token (authenticated but unauthorized for this route) | GET pages and Preview projection contain literal expected full identities, statuses, retries and links for intent-only/held/deferred/partial/complete rows; stable ordering and bounded page sizes, child caller forbidden. Case -> mutation -> red: Drop run/intent link: reportedIntentIds contains intentId red (PC-98); field loss/paging/scope have separate PCs. |
| V-4 | integration | `CardWorktreeCleanupAcceptanceTests.C1017_NoOpaqueBytesInReport`; H; protected synthetic SECRET_SENTINEL file, credential-shaped synthetic URL/path, failed native/transport message | Actual API and PowerShell output omit file bytes and synthetic credential strings; expected stable reason and non-secret correlated IDs remain present. Case -> mutation -> red: Copy opaque content into reason: output excludes SECRET_SENTINEL red (PC-99); credential-path projection separate. |
| V-4 | integration | `CardWorktreeCleanupAcceptanceTests.C1017_ReportReadbackSurvivesDeletion`; H/Q/F; canonical Result differs from author-detail file; binary and bundle deliverables already external; delete eligible local evidence and mirror | Read canonical Result and separate author detail/deliverables byte-for-byte through their real retrieval paths after restart; ignored raw evidence gone, no generic retention copy produced. Case -> mutation -> red: Accept inside-tree durable report pointer: retrievedReportBytes=originalReportBytes red (PC-100). |
| V-5 | integration | `CardWorktreeCleanupAcceptanceTests.C1033_LocalCrashHandoffs`; X/Q; the 11 named local cuts, atomic debit gap, both enqueue sites and result-save/hint exceptions | Exact cut acknowledged; fresh worker reaches physical recipient result or named safe hold; operation cardinality one; no precommit removal/new publication. Preserve per-cut expected table. Case -> mutation -> red: Drop local persisted pending recovery after worker death: recoveredLocalDirectoryGone=true red at C12; PC-111 defect also detects this method. |
| V-5 | integration | `CardWorktreeCleanupAcceptanceTests.C1017_CrashEveryHandoff`; X/Q; full 15-cut roster and supplemental faults; independent server restart, runner restart, both restarted; late response/store replacement/reopen | Per-cut hand-written outcome table; complete receipt and recipient bytes/Git facts; uncertain identity stays pending with bytes retained; same operation/digest and preserved reports. One executed result contains the full roster. Case -> mutation -> red: Allocate new OperationId after lost reply: distinctRemovalOperationIds=1 red at C13; PC-82 defect also detects this method. Dropping stored receipt before ACK makes C14 fail receipt-before-reply. |
| V-6 | native Windows | `CompletedCardWorktreeRemovalWindowsTests.C1017_LockedSetAsideRetries`; N/F; actual denied rename (root holder), then partial deletion (open ignored file inside owned aside); retry before/after test releases holder | Native operation demonstrably denied, holder alive; incomplete while aside exists, same operation after restart; only after test release/join do owned aside/registration disappear. Missing actual denial is red. Case -> mutation -> red: Complete from absent original while aside remains: completeWhileAsideExists=false red (PC-103). |
| V-6 | native Windows | `CompletedCardWorktreeRemovalWindowsTests.C1017_JunctionOutsideBytesSurvive`; N/F; late junction substitution after final inspection and protected root junction; real outside sentinel | Outside bytes exact; no-follow operation removes link entry or safely refuses; legitimate owned tree positive succeeds. Reach final deletion boundary, not just an early path refusal. Case -> mutation -> red: Follow junction target recursively: outsideSentinel=originalBytes red (PC-104). |
| V-6 | native Windows | `CompletedCardWorktreeRemovalWindowsTests.C1017_LingeringPtyHostIsReported`; N/S3; direct runner guard with terminal DB row, actual live host generation; separate unknown ownership arm | Tree retained, live identity in safe report, holder witnesses alive. Hold occurs at runner ownership check, not local AgentId gate. After test-owned release/join, eligible retry completes. Case -> mutation -> red: Treat terminal DB status as exited host: treeExists=true red (PC-101). |
| V-6 | native Windows | `CompletedCardWorktreeRemovalWindowsTests.C1017_CleanupNeverKillsHolder`; N/S3; direct guarded request while known owned holder live; observe cleanup control calls before test teardown | Zero stop/kill/pool/detach calls and retained handles report live host/child/console after refusal. Teardown releases and joins only its own processes afterwards. Case -> mutation -> red: Call runner stop on refusal: holderAliveAfterCleanup=true red (PC-102). |
| V-7 | integration | `CardCleanupCompositionTests.C1033_UpgradeStaysReportOnly`; H; ship defaults, missing cutoff with legacy Execute=true, Enabled=false/true, malformed/non-UTC cutoff; repeated startup | Resolved production DI; shipped false/null; startup inventory exposes historical/debt rows with no destructive request. One existing recurring job, unchanged cron, no second scheduler; configured eligible fixture control removes. Case -> mutation -> red: Set shipped Execute default true with explicit test-only qualifying cutoff: configuredExecute=false red; separate null/Enabled/one-job controls below. |
| V-7 | integration | `CardCleanupCompositionTests.C1033_ExternalTransactionCannotWake`; H/Q; actual manual Move inside external transaction; rollback and commit variants; save-throws; successful independent save | Second connection sees no Done/obligation and queue empty before outer commit. Rollback causes no removal. If adapter suppresses nested-transaction wake, recurring job after commit removes. Independent commit observes obligation at wake. Case -> mutation -> red: Enqueue immediately after SaveChanges in external transaction: enqueuesBeforeCommit=0 red (new transaction PC). |
| V-7 | integration | `CardCleanupCompositionTests.C1033_StartupRecoversCommittedDone`; H/Q; seed committed Done and tracker Done before startup with no queued wake; Enabled false/true; two startup notifications | Disabled: no enqueue. Enabled: existing job consumes persisted obligations, actual recipient completes; second start produces no duplicate command. Probe actual registration/job path, not hand-constructed executor. Case -> mutation -> red: Remove startup recovery enqueue: startupRecipientDirectoryGone=true red (new startup PC). |
| V-8 | integration | `CardCleanupSchedulingSchemaTests.C1033_AdditiveUpgradePreservesHistory`; isolated predecessor-schema database; active/historical Done/archived-Done/canceled cards, existing run/cursor and S3 binding/endpoint intents; apply generated migration | Old columns/rows/command identities preserved, new nullable fields null, no generated cutoff/deletion authority; real roundtrip for new fields; duplicate (RunId,ActionKey) rejected by exact DB constraint; no pending model changes. Case -> mutation -> red: Omit new unique index in model and generated migration: duplicateAccepted=false red; fabricated historical admission has separate PC. |
| V-5 | integration | `CardWorktreeCleanupAcceptanceTests.C1017_DoneQueueBusyRecipient`; Q; actual manual Done with Hangfire worker; BusyRecipient; preserve committed join keys across restart | Real worker consumes the durable identity; actual recipient directory/registration/aside facts and fresh persisted exact receipt converge. Busy recipient remains blocked until test release; eligible recipient is waiting before enqueue. Preserve reports, refs and operation cardinality. Case -> mutation -> red: Suppress worker dispatch: recipientDirectoryGone.ShouldBeTrue() is red at the same identity; queue insert and ACK are insufficient. |
| V-5 | integration | `CardWorktreeCleanupAcceptanceTests.C1017_DoneQueueAlreadyEligible`; Q; actual manual Done with Hangfire worker; AlreadyEligible; preserve committed join keys across restart | Real worker consumes the durable identity; actual recipient directory/registration/aside facts and fresh persisted exact receipt converge. Busy recipient remains blocked until test release; eligible recipient is waiting before enqueue. Preserve reports, refs and operation cardinality. Case -> mutation -> red: Suppress worker dispatch: recipientDirectoryGone.ShouldBeTrue() is red at the same identity; queue insert and ACK are insufficient. |
| V-5 | integration | `CardWorktreeCleanupAcceptanceTests.C1017_PublicationQueueBusyRecipient`; Q; scheduler cleanup request with actual land worker; BusyRecipient; preserve committed join keys across restart | Real worker consumes the durable identity; actual recipient directory/registration/aside facts and fresh persisted exact receipt converge. Busy recipient remains blocked until test release; eligible recipient is waiting before enqueue. Preserve reports, refs and operation cardinality. Case -> mutation -> red: Drop the cleanup-only queue write: recipientDirectoryGone.ShouldBeTrue() is red at the same identity; queue insert and ACK are insufficient. |
| V-5 | integration | `CardWorktreeCleanupAcceptanceTests.C1017_PublicationQueueAlreadyEligible`; Q; scheduler cleanup request with actual land worker; AlreadyEligible; preserve committed join keys across restart | Real worker consumes the durable identity; actual recipient directory/registration/aside facts and fresh persisted exact receipt converge. Busy recipient remains blocked until test release; eligible recipient is waiting before enqueue. Preserve reports, refs and operation cardinality. Case -> mutation -> red: Drop the cleanup-only queue write: recipientDirectoryGone.ShouldBeTrue() is red at the same identity; queue insert and ACK are insufficient. |
| V-5 | integration | `CardWorktreeCleanupAcceptanceTests.C1017_PhoneHomeQueueBusyRecipient`; Q; actual coordinator/client/channel/socket/runner dispatcher; BusyRecipient; preserve committed join keys across restart | Real worker consumes the durable identity; actual recipient directory/registration/aside facts and fresh persisted exact receipt converge. Busy recipient remains blocked until test release; eligible recipient is waiting before enqueue. Preserve reports, refs and operation cardinality. Case -> mutation -> red: Return socket ACK without executing guarded recipient: recipientDirectoryGone.ShouldBeTrue() is red at the same identity; queue insert and ACK are insufficient. |
| V-5 | integration | `CardWorktreeCleanupAcceptanceTests.C1017_PhoneHomeQueueAlreadyEligible`; Q; actual coordinator/client/channel/socket/runner dispatcher; AlreadyEligible; preserve committed join keys across restart | Real worker consumes the durable identity; actual recipient directory/registration/aside facts and fresh persisted exact receipt converge. Busy recipient remains blocked until test release; eligible recipient is waiting before enqueue. Preserve reports, refs and operation cardinality. Case -> mutation -> red: Return socket ACK without executing guarded recipient: recipientDirectoryGone.ShouldBeTrue() is red at the same identity; queue insert and ACK are insufficient. |

The checkpoint union below is ordinary V-1..V-8. V-6 alone requires a Windows
host; repeated U/L/M and settled-removal rows also qualify native semantics at the
same composed source SHA. S1-S13 portable rows have no platform pin.

### Guards the regression

- R-1: CardTaskSettlement8/ClosedCardSweep2 preserve close/reopen behavior; fixture opt-in preserves the D18 current-Done, generation, artifact snapshot,
  reopen/intent, active-consumer and exact-ownership behaviors. Run D18 after S1,
  S7 and final composition; the expanded D.RecoveryDebtHolds replaces its one-arm
  setup, not its identity. Reject a broad Execute override in deployed configuration.
- R-2: real local authority/content/no-follow/ref-CAS and manual retirement semantics.
  L36 after fixture policy and local recovery changes, WorktreeRemovalAuthority 9, AgentReportStore 28,
  TaskWorktreeRetirement 38, WorkspaceReservationLiveness 20, two selected WorktreeRetirementRace methods (4 results), and the seven frozen
  landing-retry methods (10 results). SettledWorktreeRemoval 26 methods/78 results
  runs on Windows. Decisive checks are preserved bytes, owned command identity,
  protected refs/receipts and unchanged manual release/revoke exclusions; inherited
  parent PCs and each test's existing refusal/positive arm supply their mutations.
- R-3: U21 and M12 parent C1017 contract methods plus the current 25 runner
  Mirror/Publish/Remove and three Workspace dispatcher methods (28), and eight
  RemoteWorktreeMirror methods. Exact guarded receipt/refusal assertions protect
  independent mirrors and no legacy fallback; existing sync tests preserve dirty /
  divergent desktop bytes. CARD-1032 prerequisite methods stay in its owner manifest.
- R-4: three existing WorktreeResidueRecovery methods (IntentCommittedBeforeGit,
  ComponentsAreIndependent, OutageKeepsFence), endpoint6, script1, registration3.
  Retain API restrictions/cron/manual script meanings. The new V-2/V-4/V-5 methods
  supply populated decisive oracles where these old bodies use constants/empty worlds.
- R-5: `TestClassificationGuardTests.Registry_matches_compiled_metadata` checks
  newly Slow classes and their adjacent reasons against compiled metadata. Mutation
  design: omit a newly Slow registry entry; the exact method fails at lines.ShouldBeEmpty.
  This is harness classification, not a deletion-authority guard.

Existing source rosters are recorded below independently of Min. A future overlapping
land that changes a selected signature/Arguments count requires a visible roster
amendment before executing that row; raising a floor to whatever ran is not validation.
No whole Unit, namespace or assembly run is commissioned.

### Guard inventory

The parent G/PC-1..111 are retained verbatim below as imported obligations. Their
original plan references denote CARD-1017. CARD-1033 directly extends G-15,
G-83..104 and G-110/111 (25 controls). Foundation G-1..14/16..51/105..107 (53)
and CARD-1032 G-52..82/108/109 (33) remain owned by their respective artifacts;
repeated V/R rows do not discharge their pending Mutation. New G-112 onward refer
to CARD-1033 D-n and split additional independently bypassable scheduling guards.
A guard's PC is unique even where its detecting method contains several vectors.
Code must add a distinct guard/control if implementation introduces another
independently bypassable safety check; an unbound payload stays binding required.

| Guard | Plan reference + safety-critical invariant | Positive control |
|---|---|---|

| G-1 | D-1; `CardWorktreeCleanupService admission`: InProgress, Review, Canceled and archive-only each retain an otherwise eligible tree; archived Done retains the original generation. | PC-1 |
| G-2 | D-1/D-7; `CardWorktreeCleanupService admission`: Done G1, reopen, Done G2; replay G1 against otherwise identical coordinates. | PC-2 |
| G-3 | D-1/D-3; `CardService Done save and cleanup obligation persistence`: fail save immediately before commit, then retry; observer sees neither row on rollback and both rows after commit. | PC-3 |
| G-4 | D-3; `CardWorktreeCleanupService discovery`: two concurrent contexts discover the same current Done revision. | PC-4 |
| G-5 | D-2; `CardWorktreeCleanupService discovery`: discover attempt 1, later add attempt 2 after exhausted inventory; both discovered independently. | PC-5 |
| G-6 | D-2; `CardWorktreeCleanupService discovery`: same title/path prefix on another board/card; only actual CardId binding qualifies. | PC-6 |
| G-7 | D-2/D-8; `CardWorktreeCleanupService eligibility`: Queued, Dispatched, Working, Blocked; later terminal task becomes eligible without cancellation. | PC-7 |
| G-8 | D-2; `CardWorktreeCleanupService ownership`: duplicate full path owner and same short-ID prefix; all other evidence valid. | PC-8 |
| G-9 | D-2; `CardWorktreeCleanupService workspace-kind branch`: Shared/ReadOnly and repair borrowing source; independent repair worktree positive arm. | PC-9 |
| G-10 | D-2; `CardWorktreeCleanupService SourceLanding branch`: fully restored sealed SourceLanding task bound to Done; ordinary unsourced Mutation positive arm. | PC-10 |
| G-11 | D-2; `CardWorktreeCleanupService managed-tree classification`: canonical, land child, reusable slot, external evidence and runner-state roots; ordinary legacy positive arm. | PC-11 |
| G-12 | D-7; `CardWorktreeCleanupService transactional admission`: barrier after inspection; reopen commits before intent; release barrier. | PC-12 |
| G-13 | D-7; `WorkspaceReservationJournal committed cleanup fence`: intent commits before reopen; old tree may finish, stale continuation rejected and new coordinates admitted. | PC-13 |
| G-14 | D-6/D-8; `WorkspaceUseAdmission and cleanup eligibility`: live follow-up, child, repair, land and session reservation singly present. | PC-14 |
| G-15 | D-6; `CardWorktreeCleanupService executor selection`: unfinished land/repair/recovery row; confirmed publication uses cleanup-only executor as positive arm. | PC-15 |
| G-16 | D-4/D-7; `CardWorktreeCleanupService final admission`: change task attempt after inspection but before intent. | PC-16 |
| G-17 | D-6/D-7; `CardWorktreeCleanupService final admission`: change complete Result after external report proof but before intent. | PC-17 |
| G-18 | D-7; `CardWorktreeCleanupService transaction scope`: hold Git/runner at an I/O barrier; second context commits same-card reopen before release. | PC-18 |
| G-19 | D-5; `WorktreeIgnoredContentGate.RetainAsync`: same ignored TRX/checkpoint/report/build tree before Done and after a genuine Done intent. | PC-19 |
| G-20 | D-5/D-7; `WorktreeRemovalEvidence final CardDone read`: set disposal flag but no committed endpoint intent. | PC-20 |
| G-21 | D-4; `GuardedWorktreeRemoval.AuthorityAsync lease predicate`: null, forged, disposed and foreign-repository lease; each otherwise eligible. | PC-21 |
| G-22 | D-4; `GuardedWorktreeRemoval.Matches SHA predicate`: source commits after captured SHA; branch remains same. | PC-22 |
| G-23 | D-4; `GuardedWorktreeRemoval.AuthorityAsync repository predicate`: same branch/SHA in a second real repository. | PC-23 |
| G-24 | D-4; `GuardedWorktreeRemoval.AuthorityAsync path predicate`: receipt and request name different registered trees with identical SHA. | PC-24 |
| G-25 | D-4; `GuardedWorktreeRemoval.AuthorityAsync ref predicate`: same HEAD but another symbolic branch; detached HEAD separate vector. | PC-25 |
| G-26 | D-4; `GuardedWorktreeRemoval.Matches common-directory predicate`: same SHA but foreign common directory. | PC-26 |
| G-27 | D-4; `GuardedWorktreeRemoval.Matches git-admin predicate`: same common/SHA but another registration admin. | PC-27 |
| G-28 | D-4; `GuardedWorktreeRemoval first content inspection`: staged and unstaged tracked bytes, valid Done and external proof. | PC-28 |
| G-29 | D-4; `GuardedWorktreeRemoval first content inspection`: untracked nonignored source with all other evidence valid. | PC-29 |
| G-30 | D-4; `GuardedWorktreeRemoval fresh remote containment`: clean unique commit absent from every remote ref. | PC-30 |
| G-31 | D-4; `TaskWorktreeRetirementService published-target preservation`: commit pushed only to task branch, absent from configured published target. | PC-31 |
| G-32 | D-4; `AgentTaskLandingProtocol cleanup receipt mapping`: original != landed SHA; genuine saved mapping succeeds, different task/operation mapping refuses. | PC-32 |
| G-33 | D-4; `GuardedWorktreeRemoval.AuthorityAsync remote refresh`: remote loses containment after first inspection; unreadable response separate vector. | PC-33 |
| G-34 | D-5; `WorktreeIgnoredContentGate protected classification`: protected secret-shaped fixture, user-local file, opaque bin-private, unfamiliar evidence path; harmless synthetic bytes only. | PC-34 |
| G-35 | D-4/D-5; `GuardedWorktreeRemoval final inspection`: inject tracked/untracked source after first read; separate ignored-protected late file. | PC-35 |
| G-36 | D-5; `GuardedWorktreeRemoval evidence-set comparison`: add evidence after preservation/first classification; disposable build churn is positive arm. | PC-36 |
| G-37 | D-5; `WorktreeNoFollowDelete.Delete`: swap an ignored directory to an outside symlink after last inspection. | PC-37 |
| G-38 | D-4; `GuardedWorktreeRemoval managed-root check`: outside root, sibling prefix, dot segment and reparse root; exact root child positive arm. | PC-38 |
| G-39 | D-4; `GuardedWorktreeRemoval nested registration check`: registered nested worktree with its own source sentinel. | PC-39 |
| G-40 | D-4; `GuardedWorktreeRemoval registration count`: real directory with missing registration. | PC-40 |
| G-41 | D-4; `GuardedWorktreeRemoval locked registration`: real locked registration. | PC-41 |
| G-42 | D-4; `GuardedWorktreeRemoval prunable registration`: registration reports prunable although source remains present. | PC-42 |
| G-43 | D-6; `artifact survival helper under server/Infrastructure/Files`: existing ResultFilePath below original or recorded set-aside root, and external sibling-prefix positive arm. | PC-43 |
| G-44 | D-6; `artifact survival helper canonical report verification`: external report exists with truncated/different bytes. | PC-44 |
| G-45 | D-6; `artifact survival helper deliverable extraction`: .antiphon/task-<id>.md has distinct extra detail absent from Result; hold until exact external durable pointer exists. | PC-45 |
| G-46 | D-6; `artifact survival helper deliverable containment`: existing deliverable below local tree, mirror or set-aside; external file positive arm. | PC-46 |
| G-47 | D-6; `artifact survival helper deliverable readback`: external missing, access-denied and symlink-to-deletion-root pointers. | PC-47 |
| G-48 | D-7; `GuardedWorktreeRemoval final AuthorityAsync call`: revoke/change the unissued committed authority after final status read. | PC-48 |
| G-49 | D-4/D-9; `GuardedWorktreeRemoval.CompleteBranchAsync`: move source ref after precheck, before update-ref. | PC-49 |
| G-50 | D-4/D-9; `local cleanup ref destination`: eligible successful removal with task ref and published ref in bare origin. | PC-50 |
| G-51 | D-4; `local cleanup command selection`: eligible and refused vectors record all Git commands. | PC-51 |
| G-52 | D-10; `PhoneHomeCommandDispatcher guarded cleanup dispatch`: guarded payload sent to new operation; legacy operation behavior remains separately selected. | PC-52 |
| G-53 | D-10; `runner-local cleanup journal admission`: otherwise-valid request names another task. | PC-53 |
| G-54 | D-10; `runner-local cleanup journal admission`: same task, wrong attempt. | PC-54 |
| G-55 | D-9/D-10; `runner-local cleanup journal admission`: same path on replacement runner store. | PC-55 |
| G-56 | D-4/D-10; `RunnerWorkspaceService guarded removal admission`: path is real but belongs to another recorded repository. | PC-56 |
| G-57 | D-4/D-10; `RunnerWorkspaceService guarded removal admission`: outside path, sibling prefix or valid different task path. | PC-57 |
| G-58 | D-4/D-10; `RunnerWorkspaceService guarded removal admission`: mirror changed symbolic branch at same SHA. | PC-58 |
| G-59 | D-4/D-10; `RunnerWorkspaceService guarded removal admission`: mirror ahead of expected SHA with unique committed bytes. | PC-59 |
| G-60 | D-4/D-10; `RunnerWorkspaceService guarded preservation proof`: raw SHA without bound publication proof; valid rebase mapping positive arm. | PC-60 |
| G-61 | D-4; `RunnerWorkspaceService guarded content inspection`: tracked staged/unstaged and nonignored untracked source. | PC-61 |
| G-62 | D-5; `runner-local ignored-content gate`: ignored evidence eligible only with CardDone; protected/unknown ignored synthetic files held. | PC-62 |
| G-63 | D-5; `runner-local no-follow removal helper`: late symlink swap after final inspection. | PC-63 |
| G-64 | D-8; `runner-local use fence ownership admission`: terminal task/session but live owned child or pooled process; unknown identity separate vector. | PC-64 |
| G-65 | D-8; `SessionRunnerRuntime.StartAsync cleanup admission`: cleanup intent wins before ordinary start; inverse order makes cleanup refuse. | PC-65 |
| G-66 | D-8; `SessionRunnerRuntime re-adoption admission`: restart runner after durable retiring-path intent then attempt re-adoption. | PC-66 |
| G-67 | D-7/D-9; `runner-local journal intent write`: observer reads external intent while filesystem mutation is barrier-held. | PC-67 |
| G-68 | D-10; `runner-local journal operation lookup`: same operation ID replay with changed digest; exact replay returns original receipt. | PC-68 |
| G-69 | D-4/D-8; `RunnerWorkspaceService last source/use read`: late tracked file or late competing owner before removal; all initial reads valid. | PC-69 |
| G-70 | D-10; `RunnerWorkspaceService guarded request handling`: guarded command carrying Force=true and dirty source. | PC-70 |
| G-71 | D-9; `CardWorktreeMirrorCleanupService pending enumeration`: local Complete/mirror Pending; fresh coordinator after restart reaches real mirror. | PC-71 |
| G-72 | D-9; `CardWorktreeCleanupService endpoint scheduling`: offline runner with otherwise removable local tree; mirror remains pending. | PC-72 |
| G-73 | D-4/D-9; `CardWorktreeMirrorCleanupService expected-source selection`: real mirror HEAD H ancestor of preserved pushed P; exact H inventory succeeds; changed H refuses. | PC-73 |
| G-74 | D-9/D-10; `CardWorktreeMirrorCleanupService receipt validation`: valid receipt for another operation; other fields match. | PC-74 |
| G-75 | D-9/D-10; `CardWorktreeMirrorCleanupService receipt endpoint check`: valid operation with different endpoint ID. | PC-75 |
| G-76 | D-9/D-10; `CardWorktreeMirrorCleanupService receipt store check`: valid operation and endpoint but replacement store ID. | PC-76 |
| G-77 | D-9/D-10; `CardWorktreeMirrorCleanupService receipt attempt check`: valid operation/endpoint/store but different task attempt. | PC-77 |
| G-78 | D-9; `CardWorktreeMirrorCleanupService component projection`: ack/empty response/timeout/null path without deletion facts. | PC-78 |
| G-79 | D-10; `RemoteWorkspaceService guarded operation refusal`: unsupported/unknown capability leaves residue; ordinary launch remains admitted. | PC-79 |
| G-80 | D-7; `CardWorktreeMirrorCleanupService remote intent admission`: local intent finishes, reopen commits before remote intent. | PC-80 |
| G-81 | D-9; `CardWorktreeMirrorCleanupService endpoint ledger`: two proven runner/store bindings, first Complete second offline. | PC-81 |
| G-82 | D-9; `CardWorktreeMirrorCleanupService recovery request`: real runner completes deletion, socket loses response; restart server and reconnect. | PC-82 |
| G-83 | D-3/D-12; `WorktreeResidueSweepService discovery`: commit Done then drop wakeup; recurring worker executes eligible target. | PC-83 |
| G-84 | D-7/D-9; `RetirementCommandJournal recovery`: command intent persisted, worker killed before receipt; recreate coordinator. | PC-84 |
| G-85 | D-5/D-9; `GuardedWorktreeRemoval pending set-aside branch`: real Landed+Refused/worktree_removal_incomplete with owned .card-task-5fcba512.removing-ec201e2d085b1894 under scratch root and ignored evidence; restart. | PC-85 |
| G-86 | D-9; `GuardedWorktreeRemoval set-aside receipt binding`: receipt belongs to another original path/git admin/operation; valid owned aside positive arm. | PC-86 |
| G-87 | D-7/D-9; `GuardedWorktreeRemoval recovery original-path check`: owned aside present plus new directory at original path with sentinel. | PC-87 |
| G-88 | D-9; `endpoint absence reconciliation`: path absent, no matching command receipt; access-denied query separate vector. | PC-88 |
| G-89 | D-9; `cleanup endpoint completion predicate`: directory gone with registration present; then registration gone with ref retained. | PC-89 |
| G-90 | D-3/D-9; `AgentTaskLandService cleanup-only execution`: landed receipt cleanup refuses then completes through real land queue. | PC-90 |
| G-91 | D-12; `WorktreeResidueSweepService execution gate`: populated new-Done eligible local+mirror inventory with Execute=false. | PC-91 |
| G-92 | D-12; `WorktreeResidueSweepService Done cutoff predicate`: Done before, exactly at and after activation cutoff; widen cutoff deliberately later. | PC-92 |
| G-93 | D-12; `CardWorktreeCleanupService age predicate`: null completed time, 119:59.999, 120:00 and 120:00.001 minutes. | PC-93 |
| G-94 | D-12; `WorktreeResidueSweepService accepted-action accounting`: 26 due eligible targets split across publication/settled/mirror against budget25. | PC-94 |
| G-95 | D-12; `WorktreeResidueSweepService persisted retry cursor`: transient refusal, restart before due then advance clock to due; no real sleeps. | PC-95 |
| G-96 | D-12; `WorktreeResidueSweepService retry eligibility`: permanent safety refusal with unchanged evidence across two sweeps. | PC-96 |
| G-97 | D-11; `residue outcome save before AgentTaskChanged`: observe via second DB connection from event callback; restart host and GET same run. | PC-97 |
| G-98 | D-11; `cleanup ledger intent/event transaction`: crash before first I/O; GET run exposes full card-generation/task-attempt/endpoint/operation identity. | PC-98 |
| G-99 | D-11; `residue DTO/script projection`: protected synthetic SECRET_SENTINEL bytes; API and real script read populated result. | PC-99 |
| G-100 | D-6/D-11; `AgentReportStore and residue result readback`: delete eligible evidence tree; canonical full Result, distinct task-report detail and external deliverable remain exact. | PC-100 |
| G-101 | D-8; `Windows cleanup process ownership admission`: own real ModernConPty PtyHost remains alive after session row terminal; no open-file-probe authority. | PC-101 |
| G-102 | D-8; `Windows cleanup refusal handling`: live exact owned process holds fixture mirror; cleanup returns refusal with identity. | PC-102 |
| G-103 | D-9; `Windows no-follow partial-delete recovery`: FileShare without Delete causes real partial result, release holder then retry same recorded components. | PC-103 |
| G-104 | D-5; `Windows no-follow removal helper`: late junction replacement and protected root junction with outside sentinel. | PC-104 |
| G-105 | D-6; `artifact survival helper missing-report branch`: no report and no explicit missing-report disposition, then valid existing disposition positive arm. | PC-105 |
| G-106 | D-5; `GuardedWorktreeRemoval first ReparsePoints check`: evidence link present initially; valid later snapshot to avoid masking. | PC-106 |
| G-107 | D-5; `GuardedWorktreeRemoval final ReparsePoints check`: inject link after first clean inspection; no-follow deletion remains protective. | PC-107 |
| G-108 | D-2/D-10; `RunnerWorkspaceService guarded workspace-kind check`: valid registered SourceLanding creation with all ordinary cleanup fields forged. | PC-108 |
| G-109 | D-8; `runner-local process-ownership query`: query unavailable or torn owned-child record with terminal DB row. | PC-109 |
| G-110 | D-3/D-12; `WorktreeResidueSweepService enqueue failure handling`: fail Hangfire enqueue and separately AgentTaskLandQueue enqueue after durable request save; recurring recovery reaches actual deletion. | PC-110 |
| G-111 | D-9; `CardWorktreeCleanupService endpoint outcome persistence`: physical local removal succeeds; fail result transaction then restart. | PC-111 |
| G-112 | D-2; local new-intent executor entry rechecks Execute, independent of sweep. | PC-112 |
| G-113 | D-2; mirror new-intent coordinator entry rechecks Execute. | PC-113 |
| G-114 | D-2; local persisted-intent replay rechecks Execute before mutating bytes. | PC-114 |
| G-115 | D-2; cleanup-only publication recipient rechecks Execute before mutation. | PC-115 |
| G-116 | D-2; mirror mutating replay rechecks Execute while receipt-only reconciliation remains allowed. | PC-116 |
| G-117 | D-2; scheduler preview cannot create mutation admission even with Execute=true. | PC-117 |
| G-118 | D-2; local direct/persisted entry refuses a preview-run mutation. | PC-118 |
| G-119 | D-2; mirror direct/persisted entry refuses a preview-run mutation. | PC-119 |
| G-120 | D-2; absent cutoff withholds new CardDone execution. | PC-120 |
| G-121 | D-2; equality at Done cutoff stays historical. | PC-121 |
| G-122 | D-2; local executor cutoff guard cannot be bypassed by a direct caller. | PC-122 |
| G-123 | D-2; mirror executor cutoff guard cannot be bypassed by a direct caller. | PC-123 |
| G-124 | D-2; mutable card/startup time does not replace immutable Done revision time. | PC-124 |
| G-125 | D-2; malformed/non-UTC configured cutoff does not start execution. | PC-125 |
| G-126 | D-2/D-7; shipped Execute default is false. | PC-126 |
| G-127 | D-2; null task completion time is ineligible independently of elapsed age. | PC-127 |
| G-128 | D-2; mirror admission retains settling floor at its own entry. | PC-128 |
| G-129 | D-3; accepted action debit and endpoint linkage commit before external I/O. | PC-129 |
| G-130 | D-3; candidate action key and run debit save atomically. | PC-130 |
| G-131 | D-3; database enforces unique RunId/ActionKey even for racing callers. | PC-131 |
| G-132 | D-3; crash after debit cannot refund an uncertain accepted action. | PC-132 |
| G-133 | D-3; held/cooling/excluded/preview work consumes no execution debit. | PC-133 |
| G-134 | D-3; board filter constrains the shared candidate task set. | PC-134 |
| G-135 | D-3; project filter constrains the shared candidate task set. | PC-135 |
| G-136 | D-3; publication candidates must belong to the same scoped task set. | PC-136 |
| G-137 | D-3; unidentified scanned inventory cannot expand a scoped request. | PC-137 |
| G-138 | D-3; CardDone and legacy aliases deduplicate one physical endpoint. | PC-138 |
| G-139 | D-3; persisted last-evaluation/stable identity ordering survives restart. | PC-139 |
| G-140 | D-3; admission transaction/row locks do not span Git I/O. | PC-140 |
| G-141 | D-3; admission transaction/row locks do not span land queue I/O. | PC-141 |
| G-142 | D-3; admission transaction/row locks do not span runner I/O. | PC-142 |
| G-143 | D-3; overlapping runs cannot issue two endpoint operations. | PC-143 |
| G-144 | D-3; admission recovery rejoins the recorded endpoint, not current scalar task path. | PC-144 |
| G-145 | D-4; settled legacy cursor NotBefore is read after restart. | PC-145 |
| G-146 | D-4; publication retry cursor applies the same durable cooldown. | PC-146 |
| G-147 | D-4; exactly due time is admitted. | PC-147 |
| G-148 | D-4; transient refusal persists the established 24-hour delay. | PC-148 |
| G-149 | D-4; attempt and mirror-store retry identities remain distinct. | PC-149 |
| G-150 | D-4; permanent-refusal digest includes generation/attempt. | PC-150 |
| G-151 | D-4; permanent-refusal digest includes source/repository identity. | PC-151 |
| G-152 | D-4; permanent-refusal digest includes preservation pointers/digest. | PC-152 |
| G-153 | D-4; permanent-refusal digest includes ownership/use evidence. | PC-153 |
| G-154 | D-4; permanent-refusal digest includes endpoint/store binding. | PC-154 |
| G-155 | D-4; permanent-refusal digest includes observed content inventory. | PC-155 |
| G-156 | D-4; permanent-refusal digest includes registration state. | PC-156 |
| G-157 | D-4; changed evidence triggers eligibility recheck, not approval. | PC-157 |
| G-158 | D-4; unknown errors/evidence stay held, not transient-eligible. | PC-158 |
| G-159 | D-5; scheduler enumerates mirror debt independently of local Complete. | PC-159 |
| G-160 | D-5; offline mirror does not suppress local scheduled progress. | PC-160 |
| G-161 | D-5; set-aside recovery compares original-path identity independently of receipt owner. | PC-161 |
| G-162 | D-5; set-aside recovery compares computed aside-path identity independently of receipt owner. | PC-162 |
| G-163 | D-5; set-aside recovery compares git-admin identity independently of receipt owner. | PC-163 |
| G-164 | D-5; set-aside recovery compares source/journal identity independently of receipt owner. | PC-164 |
| G-165 | D-5; unreadable/torn set-aside record cannot grant absence. | PC-165 |
| G-166 | D-5; access-denied path observation remains unknown. | PC-166 |
| G-167 | D-5; local completion includes registration removal. | PC-167 |
| G-168 | D-5; local completion includes its branch fact. | PC-168 |
| G-169 | D-5; portable recovery includes remaining owned aside bytes. | PC-169 |
| G-170 | D-5; mirror receipt-save failure retains exact request debt. | PC-170 |
| G-171 | D-6; correlated task event and outcome commit in one transaction. | PC-171 |
| G-172 | D-6; hint publication failure cannot lose committed outcome. | PC-172 |
| G-173 | D-6; shared Preview/GetRun projection preserves correlated nullable fields and endpoint distinctions. | PC-173 |
| G-174 | D-6; stored run paging applies bounded Take. | PC-174 |
| G-175 | D-6; paging has stable evaluation/identity tie ordering. | PC-175 |
| G-176 | D-6; child caller cannot read residue run API. | PC-176 |
| G-177 | D-6; actual PowerShell Status exposes local and mirror facts. | PC-177 |
| G-178 | D-6; reason projection excludes credential-bearing paths separately from file contents. | PC-178 |
| G-179 | D-7; manual Done wakeup follows actual independent commit. | PC-179 |
| G-180 | D-7; outer transaction SaveChanges is not an actual commit. | PC-180 |
| G-181 | D-7; startup uses existing job to recover committed Done. | PC-181 |
| G-182 | D-7; Enabled=false suppresses startup scheduling. | PC-182 |
| G-183 | D-7; Enabled=false suppresses manual Done wakeup. | PC-183 |
| G-184 | D-7; recurring registration uses the one existing scheduler/cron. | PC-184 |
| G-185 | D-7; scheduler discovery does not seal a completed header. | PC-185 |
| G-186 | D-2/D-7; status-only historical Done has no new admission authority. | PC-186 |
| G-187 | D-8; local AgentId without qualified ownership proof holds. | PC-187 |
| G-188 | D-1/D-5; active unconfirmed/interrupted landing debt cannot become settled removal. | PC-188 |
| G-189 | D-1/D-5; unfinished repair/recovery consumer debt holds. | PC-189 |
| G-190 | D-1/D-5; unknown publication mapping cannot authorize settled removal. | PC-190 |
| G-191 | D-5; confirmed exact publication routes cleanup-only rather than remaining permanently held. | PC-191 |
| G-192 | D-5/D-7; scheduled cleanup creates no session-input obligation. | PC-192 |
| G-193 | D-7; land enqueue refusal retains pending request. | PC-193 |
| G-194 | D-5/D-7; send failure retains immutable mirror intent. | PC-194 |
| G-195 | D-6; additive migration does not manufacture historical admissions. | PC-195 |
| G-196 | D-7/V-5 fixture safety; crash worker validates parent-owned root before mutation. | PC-196 |
| G-197 | D-7/V-5 fixture safety; crash worker confines ready path outside deletion targets within controls root. | PC-197 |
| G-198 | D-7/V-5 fixture safety; crash worker admits only enumerated cuts for the bound task. | PC-198 |
| G-199 | D-7/V-5 fixture safety; crash request task/target belong to the fixture owner independently of allowed cut. | PC-199 |
| G-200 | D-2; execution disablement permits receipt-only reconciliation without sending a mutating command. | PC-200 |
| G-201 | D-2; publication cleanup recipient retains the CardDone cohort guard on queued replay. | PC-201 |
| G-202 | D-2; publication cleanup recipient refuses preview-run mutation. | PC-202 |
| G-203 | D-7; enabled committed manual Done reaches the waiting existing job worker. | PC-203 |
| G-204 | D-7; accepted cleanup-only land work reaches its repository recipient after a busy lease clears. | PC-204 |

No safety-critical guard is declared unnecessary. Imported controls stay in the
inventory even while their execution is pending. The additional evidence-digest
rows split independently omitted components; within a component, Code must split
further if it implements independently bypassable field comparisons. S3's canonical
request/digest field comparisons remain under its owner's expanded guard inventory.

### Positive controls

Imported PC-1..111 below retain the parent exact method filters and expected-red
assertions. Code binds their source locations to the implemented behavior; it must
not mark a later-held fixture green evidence for an earlier guard. G-86's primary
control targets receipt **operation owner**; its path/admin/source comparisons are
separately controlled below. G-89 targets a mirror receipt with DirectoryGone=true but registration unproved; new
registration/ref/aside controls exercise each independently. G-91 is the sweep
Execute guard; direct/pending recipients have separate controls. G-110 below is the
Hangfire failure branch; land and socket branches are separately controlled.

Per PC: method-scoped baseline green, apply a compiling defect, build/run that same
method red at the named assertion, restore exact source, rebuild/run the same method
green. No broadened class/suite selection, zero-test result, build/fixture error,
changed assertion, timeout increase or equivalent mutation counts as red. Keep
per-PC diff, source/build provenance, exact assertion, counts and restoration evidence.
Run these only after land in a separately commissioned Mutation task once the pause
is lifted. Code runs V/R; ordinary Review judges the pending design before land.

| PC | Compiling defect (break only its G) | Exact detecting filter | Required red assertion |
|---|---|---|---|

| PC-1 | G-1: accept a non-Done generation. | `/*/*/CardDoneWorktreeCleanupTests/C1017_OnlyDoneAuthorizes` | `admittedIntents.ShouldBe(0)` |
| PC-2 | G-2: ignore the DoneRevisionId equality. | `/*/*/CardDoneWorktreeCleanupTests/C1017_GenerationMustMatch` | `oldGenerationIntents.ShouldBe(0)` |
| PC-3 | G-3: omit obligation insertion from the Done transaction. | `/*/*/CardDoneWorktreeCleanupTests/C1017_DoneSaveIsAtomic` | `committedObligations.ShouldHaveSingleItem()` |
| PC-4 | G-4: remove only the generation unique key; barrier both no-row lookups before concurrent inserts. | `/*/*/CardDoneWorktreeCleanupTests/C1017_DuplicateDoneIsIdempotent` | `generationRows.Count.ShouldBe(1)` |
| PC-5 | G-5: return early when header previously completed. | `/*/*/CardDoneWorktreeCleanupTests/C1017_DiscoverPostDoneAttempts` | `targetAttempts.ShouldBe(new[] { 1, 2 })` |
| PC-6 | G-6: include a title-only matching task in the candidate query. | `/*/*/CardDoneWorktreeCleanupTests/C1017_ExactCardBinding` | `targets.ShouldNotContain(foreignTaskId)` |
| PC-7 | G-7: accept Working task status. | `/*/*/CardDoneWorktreeCleanupTests/C1017_TerminalTaskRequired` | `reason.ShouldBe("terminal_required")` |
| PC-8 | G-8: accept multiple exact owners. | `/*/*/CardDoneWorktreeCleanupTests/C1017_UniqueOrdinaryOwner` | `ambiguousIntents.ShouldBe(0)` |
| PC-9 | G-9: treat a borrowed source as an owned Worktree. | `/*/*/CardDoneWorktreeCleanupTests/C1017_SharedAndBorrowedExcluded` | `borrowedBytes.ShouldBe(originalBytes)` |
| PC-10 | G-10: remove SourceLandingOperationId exclusion. | `/*/*/CardDoneWorktreeCleanupTests/C1017_SourceLandingExcluded` | `verificationTreeExists.ShouldBeTrue()` |
| PC-11 | G-11: classify a reusable slot as ordinary. | `/*/*/CardDoneWorktreeCleanupTests/C1017_SlotAndCanonicalExcluded` | `slotSentinel.ShouldBe(originalBytes)` |
| PC-12 | G-12: skip transactional re-read of the card generation. | `/*/*/CardDoneWorktreeCleanupTests/C1017_ReopenBeforeIntentRevokes` | `destructiveCalls.ShouldBe(0)` |
| PC-13 | G-13: release admitted retirement fence during reopen. | `/*/*/CardDoneWorktreeCleanupTests/C1017_IntentBeforeReopenKeepsFence` | `oldPathLaunches.ShouldBe(0)` |
| PC-14 | G-14: ignore active consumer lookup. | `/*/*/CardDoneWorktreeCleanupTests/C1017_ActiveConsumersHold` | `admittedIntents.ShouldBe(0)` |
| PC-15 | G-15: allow outstanding recovery debt into settled removal. | `/*/*/CardDoneWorktreeCleanupTests/C1017_RecoveryDebtHolds` | `settledRemovalCalls.ShouldBe(0)` |
| PC-16 | G-16: omit attempt comparison. | `/*/*/CardDoneWorktreeCleanupTests/C1017_SnapshotAttemptIsFresh` | `staleIntentCount.ShouldBe(0)` |
| PC-17 | G-17: omit report-digest comparison. | `/*/*/CardDoneWorktreeCleanupTests/C1017_SnapshotReportIsFresh` | `staleIntentCount.ShouldBe(0)` |
| PC-18 | G-18: retain generation transaction across the awaited I/O. | `/*/*/CardDoneWorktreeCleanupTests/C1017_NoDbLockAcrossIo` | `secondWriterCommittedBeforeIoRelease.ShouldBeTrue()` |
| PC-19 | G-19: allow evidence disposal for ordinary Publication. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_EvidenceNeedsDoneDisposition` | `beforeDoneEvidenceBytes.ShouldBe(originalBytes)` |
| PC-20 | G-20: accept disposal flag without its intent row. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_DisposalFlagIsNotAuthority` | `directoryExists.ShouldBeTrue()` |
| PC-21 | G-21: bypass leases.Owns only. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_LeaseMustBeGenuine` | `removalCalls.ShouldBe(0)` |
| PC-22 | G-22: ignore source HEAD equality. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_SourceShaMustMatch` | `directoryExists.ShouldBeTrue()` |
| PC-23 | G-23: omit receipt RepositoryPath comparison. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_RepositoryMustMatch` | `foreignRepositoryBytes.ShouldBe(originalBytes)` |
| PC-24 | G-24: omit receipt WorktreePath comparison. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_PathMustMatch` | `otherTreeExists.ShouldBeTrue()` |
| PC-25 | G-25: omit SourceFullRef comparison. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_BranchMustMatch` | `otherBranchStillExists.ShouldBeTrue()` |
| PC-26 | G-26: omit CommonDirectory comparison. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_CommonDirectoryMustMatch` | `foreignCommonBytes.ShouldBe(originalBytes)` |
| PC-27 | G-27: omit GitDirectory comparison. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_AdminDirectoryMustMatch` | `foreignAdminExists.ShouldBeTrue()` |
| PC-28 | G-28: ignore tracked-dirty result at first inspection. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_DirtyTrackedIsHeld` | `fullInspections.ShouldBe(1)` |
| PC-29 | G-29: ignore untracked-source result at first inspection. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_UntrackedSourceIsHeld` | `fullInspections.ShouldBe(1)` |
| PC-30 | G-30: treat contains-source false as true. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_UnpushedCommitIsHeld` | `treeExists.ShouldBeTrue()` |
| PC-31 | G-31: accept task-branch preservation as published-target containment. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_TaskBranchOnlyIsHeld` | `reason.ShouldBe("unlanded_work")` |
| PC-32 | G-32: use supplied landed SHA without bound publication mapping. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_RebaseMappingMustBeBound` | `foreignMappingTreeExists.ShouldBeTrue()` |
| PC-33 | G-33: reuse first remote observation at final directory boundary. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_RemoteProofMustBeFresh` | `treeExists.ShouldBeTrue()` |
| PC-34 | G-34: clear Protected before the first check. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_ProtectedIgnoredIsHeld` | `fullInspections.ShouldBe(1)` |
| PC-35 | G-35: reuse first inspection as final snapshot. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_FinalContentIsRechecked` | `lateBytes.ShouldBe(originalBytes)` |
| PC-36 | G-36: skip second.Evidence.SequenceEqual check. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_EvidenceChurnIsHeld` | `reason.ShouldBe("ignored_content_changed")` |
| PC-37 | G-37: follow directory links when recursively enumerating removal. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_LinksNeverFollowed` | `outsideSentinel.ShouldBe(originalBytes)` |
| PC-38 | G-38: skip managed-root confinement refusal. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_RootConfinementIsFresh` | `outsideTreeExists.ShouldBeTrue()` |
| PC-39 | G-39: skip nested_registration refusal. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_NestedRegistrationIsHeld` | `nestedTreeExists.ShouldBeTrue()` |
| PC-40 | G-40: skip unregistered_directory refusal. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_RegistrationMustExist` | `treeExists.ShouldBeTrue()` |
| PC-41 | G-41: skip registration_locked refusal. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_LockedRegistrationIsHeld` | `treeExists.ShouldBeTrue()` |
| PC-42 | G-42: skip registration_prunable refusal. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_PrunableRegistrationIsHeld` | `treeExists.ShouldBeTrue()` |
| PC-43 | G-43: accept report inside a deletion root. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_ReportMustBeExternal` | `reason.ShouldBe("artifact_unpreserved")` |
| PC-44 | G-44: skip canonical report digest comparison. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_ReportDigestMustMatch` | `reason.ShouldBe("artifact_unpreserved")` |
| PC-45 | G-45: treat inline Result alone as preserving distinct task-report bytes. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_TaskReportDetailMustSurvive` | `detailBytesAfterAttempt.ShouldBe(originalDetailBytes)` |
| PC-46 | G-46: skip deliverable deletion-root test. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_DeliverablesMustBeExternal` | `reason.ShouldBe("artifact_unpreserved")` |
| PC-47 | G-47: accept an unresolved deliverable pointer. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_DeliverablesMustExist` | `reason.ShouldBe("artifact_unpreserved")` |
| PC-48 | G-48: omit last fresh authority read. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_FinalAuthorityIsFresh` | `removalCalls.ShouldBe(0)` |
| PC-49 | G-49: omit expected old SHA argument on update-ref -d. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_BranchCasPreservesConcurrentRef` | `observedRef.ShouldBe(concurrentSha)` |
| PC-50 | G-50: send the local ref deletion to origin as well. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_OriginRefsSurvive` | `originRefsAfter.ShouldBe(originRefsBefore)` |
| PC-51 | G-51: replace ordinary ref deletion with an extra reset --hard of the source before inspection. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_NoImplicitResetOrForce` | `sourceMutationCommands.ShouldBeEmpty()` |
| PC-52 | G-52: route new operation to legacy RemoveAsync. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_GuardedOperationIsDistinct` | `legacyRemovalCalls.ShouldBe(0)` |
| PC-53 | G-53: ignore TaskId equality. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerTaskIdentity` | `treeExists.ShouldBeTrue()` |
| PC-54 | G-54: ignore task-attempt equality. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerAttemptIdentity` | `treeExists.ShouldBeTrue()` |
| PC-55 | G-55: ignore StoreId equality. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerStoreIdentity` | `treeExists.ShouldBeTrue()` |
| PC-56 | G-56: ignore repository binding equality. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerRepositoryIdentity` | `foreignBytes.ShouldBe(originalBytes)` |
| PC-57 | G-57: ignore exact recorded path equality for valid different task path. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerPathIdentity` | `otherTreeExists.ShouldBeTrue()` |
| PC-58 | G-58: ignore symbolic branch equality. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerBranchIdentity` | `otherBranchStillExists.ShouldBeTrue()` |
| PC-59 | G-59: ignore expected HEAD equality. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerHeadIdentity` | `preservationProofChecks.ShouldBe(0)` |
| PC-60 | G-60: accept missing preservation proof. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerPreservationProof` | `treeExists.ShouldBeTrue()` |
| PC-61 | G-61: accept nonempty source status. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerDirtySource` | `finalInspectionCalls.ShouldBe(0)` |
| PC-62 | G-62: blank protected classification. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerIgnoredPolicy` | `protectedBytes.ShouldBe(originalBytes)` |
| PC-63 | G-63: follow the substituted directory link. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerNoFollow` | `outsideSentinel.ShouldBe(originalBytes)` |
| PC-64 | G-64: consider terminal session sufficient despite a live child. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerProcessOwnerHolds` | `treeExists.ShouldBeTrue()` |
| PC-65 | G-65: skip cleanup fence at StartAsync. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerStartFence` | `startsAtRetiringPath.ShouldBe(0)` |
| PC-66 | G-66: skip restored cleanup fence during re-adoption. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerReadoptionFence` | `adoptionsAtRetiringPath.ShouldBe(0)` |
| PC-67 | G-67: move durable intent write after deletion. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerIntentBeforeIo` | `intentVisibleAtFirstMutation.ShouldBeTrue()` |
| PC-68 | G-68: return saved success for changed request digest. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerDigestReplay` | `changedRequestAccepted.ShouldBeFalse()` |
| PC-69 | G-69: skip last source/use validation. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateTreeBytes.ShouldBe(originalBytes)` |
| PC-70 | G-70: allow Force to skip guarded validation. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerNoForceBypass` | `dirtyBytes.ShouldBe(originalBytes)` |
| PC-71 | G-71: filter out targets whose local component is Complete. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_MirrorCompletesIndependently` | `mirrorDirectoryGone.ShouldBeTrue()` |
| PC-72 | G-72: require mirror online before local execution. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_LocalProgressSurvivesOffline` | `localDirectoryGone.ShouldBeTrue()` |
| PC-73 | G-73: require mirror HEAD equal to pushed P even with exact H containment proof. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_MirrorBehindPushedTip` | `mirrorDirectoryGone.ShouldBeTrue()` |
| PC-74 | G-74: ignore receipt operation ID. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReceiptMustMatchOperation` | `mirrorComplete.ShouldBeFalse()` |
| PC-75 | G-75: omit endpoint-ID comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReceiptMustMatchEndpoint` | `mirrorComplete.ShouldBeFalse()` |
| PC-76 | G-76: omit store-ID comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReceiptMustMatchStore` | `mirrorComplete.ShouldBeFalse()` |
| PC-77 | G-77: omit task-attempt comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReceiptMustMatchTaskAttempt` | `mirrorComplete.ShouldBeFalse()` |
| PC-78 | G-78: mark Complete on successful RPC return. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_AckIsNotRemoval` | `mirrorComplete.ShouldBeFalse()` |
| PC-79 | G-79: retry unsupported guarded command using legacy WorkspaceRemove. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_UnsupportedNeverFallsBack` | `legacyRemovalCalls.ShouldBe(0)` |
| PC-80 | G-80: reuse local generation admission for remote issuance. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReopenBetweenEndpoints` | `remoteCommandsIssued.ShouldBe(0)` |
| PC-81 | G-81: overwrite mirror list with last scalar binding. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_MultipleMirrorsRemainDistinct` | `persistedEndpointCount.ShouldBe(2)` |
| PC-82 | G-82: allocate new operation ID on retry. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_LostAckReusesOperation` | `distinctRemovalOperationIds.ShouldBe(1)` |
| PC-83 | G-83: omit missing-obligation recovery query. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_LostWakeupRecovers` | `directoryGone.ShouldBeTrue()` |
| PC-84 | G-84: clear spent command slot on restart. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_IntentRecoveryDoesNotResetBudget` | `distinctDestructiveCommandIds.ShouldBe(1)` |
| PC-85 | G-85: discard CardDone disposition while reconstructing set-aside request. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_SetAsideRecoveryPreservesAuthority` | `ownedSetAsideGone.ShouldBeTrue()` |
| PC-86 | G-86: accept mismatching set-aside receipt. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_SetAsideIdentityMustMatch` | `foreignAsideBytes.ShouldBe(originalBytes)` |
| PC-87 | G-87: delete occupied original path during recovery. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_ReplacementPathIsUntouched` | `replacementBytes.ShouldBe(originalBytes)` |
| PC-88 | G-88: equate missing path with Complete without receipt. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_AbsenceNeedsReceipt` | `endpointComplete.ShouldBeFalse()` |
| PC-89 | G-89: compute Complete from DirectoryGone alone. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_ComponentFactsAreIndependent` | `endpointComplete.ShouldBeFalse()` |
| PC-90 | G-90: send cleanup-only request down ordinary publication path. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_PublicationIsImmutable` | `publicationCommandsAfterCleanup.ShouldBe(0)` |
| PC-91 | G-91: ignore Execute option. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_ExecuteFalseCannotDelete` | `destructiveCalls.ShouldBe(0)` |
| PC-92 | G-92: treat all historical Done generations as new. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_HistoricalCutoffCannotDelete` | `historicalTreeExists.ShouldBeTrue()` |
| PC-93 | G-93: remove settling-floor comparison. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_SettlingFloorCannotDelete` | `belowFloorTreeExists.ShouldBeTrue()` |
| PC-94 | G-94: give each lane its own 25-action allowance. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_ActionBudgetIsShared` | `acceptedActions.ShouldBe(25)` |
| PC-95 | G-95: discard persisted NextAttemptAt after restart. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_CooldownSurvivesRestart` | `beforeDueCommands.ShouldBe(0)` |
| PC-96 | G-96: requeue permanent refusal merely on elapsed time. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_PermanentRefusalAwaitsEvidence` | `repeatCommands.ShouldBe(0)` |
| PC-97 | G-97: emit invalidation before saving endpoint result. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_OperatorOutcomeIsDurable` | `committedOutcomeAtEvent.ShouldBeTrue()` |
| PC-98 | G-98: omit intent-to-run/event link. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_EveryIntentIsReportable` | `reportedIntentIds.ShouldContain(intentId)` |
| PC-99 | G-99: include file contents in refusal detail. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_NoOpaqueBytesInReport` | `operatorOutput.ShouldNotContain("SECRET_SENTINEL")` |
| PC-100 | G-100: publish an inside-tree report pointer as durable. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_ReportReadbackSurvivesDeletion` | `retrievedReportBytes.ShouldBe(originalReportBytes)` |
| PC-101 | G-101: ignore a positively live lingering PtyHost because session is terminal. | `/*/*/CompletedCardWorktreeRemovalWindowsTests/C1017_LingeringPtyHostIsReported` | `treeExists.ShouldBeTrue()` |
| PC-102 | G-102: invoke runner stop on that refusal. | `/*/*/CompletedCardWorktreeRemovalWindowsTests/C1017_CleanupNeverKillsHolder` | `holderAliveAfterCleanup.ShouldBeTrue()` |
| PC-103 | G-103: mark endpoint Complete when original is absent but aside remains. | `/*/*/CompletedCardWorktreeRemovalWindowsTests/C1017_LockedSetAsideRetries` | `completeWhileAsideExists.ShouldBeFalse()` |
| PC-104 | G-104: enumerate junction target recursively during removal. | `/*/*/CompletedCardWorktreeRemovalWindowsTests/C1017_JunctionOutsideBytesSurvive` | `outsideSentinel.ShouldBe(originalBytes)` |
| PC-105 | G-105: accept missing report without disposition. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_MissingReportNeedsDisposition` | `reason.ShouldBe("artifact_unpreserved")` |
| PC-106 | G-106: omit first reparse-point refusal. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_FirstIgnoredLinkIsHeld` | `fullInspections.ShouldBe(1)` |
| PC-107 | G-107: omit final reparse-point refusal. | `/*/*/CompletedCardWorktreeRemovalTests/C1017_FinalIgnoredLinkIsHeld` | `reason.ShouldBe("ignored_reparse_point")` |
| PC-108 | G-108: accept verification creation as ordinary. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerSourceLandingExcluded` | `verificationTreeExists.ShouldBeTrue()` |
| PC-109 | G-109: treat unknown ownership as no owner. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_UnknownProcessOwnershipHolds` | `treeExists.ShouldBeTrue()` |
| PC-110 | G-110: clear durable pending state when enqueue throws. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_EnqueueFailureRemainsRecoverable` | `directoryGoneAfterRecovery.ShouldBeTrue()` |
| PC-111 | G-111: discard pending endpoint before saving component receipt. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_ResultSaveFailureRetainsDebt` | `recoveredEndpointComplete.ShouldBeTrue()` |
| PC-112 | G-112: Remove only the local new-intent Execute call; invoke executor directly. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_DirectAndRecoveryGatesHold` | `localNewDeniedMutations.ShouldBe(0)` |
| PC-113 | G-113: Remove only mirror new-intent Execute call; invoke coordinator directly. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_DirectAndRecoveryGatesHold` | `mirrorNewDeniedMutations.ShouldBe(0)` |
| PC-114 | G-114: Return to ExecuteAsync before checking Execute for a persisted local intent. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_DirectAndRecoveryGatesHold` | `localResumeDeniedMutations.ShouldBe(0)` |
| PC-115 | G-115: Skip Execute on already-queued CardDone publication execution. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_DirectAndRecoveryGatesHold` | `publicationResumeDeniedMutations.ShouldBe(0)` |
| PC-116 | G-116: Treat replay-capable runner call as read-only when Execute=false. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_DirectAndRecoveryGatesHold` | `mirrorResumeDeniedMutations.ShouldBe(0)` |
| PC-117 | G-117: Forward configured Execute without clearing it on preview. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_PreviewDoesNotMutate` | `previewAcceptedActions.ShouldBe(0)` |
| PC-118 | G-118: Ignore the persisted run Preview flag only at local entry. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_DirectAndRecoveryGatesHold` | `localPreviewDeniedMutations.ShouldBe(0)` |
| PC-119 | G-119: Ignore the persisted run Preview flag only at mirror entry. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_DirectAndRecoveryGatesHold` | `mirrorPreviewDeniedMutations.ShouldBe(0)` |
| PC-120 | G-120: Replace null cutoff refusal with DateTime.MinValue. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_HistoricalCutoffCannotDelete` | `nullCutoffTreeExists.ShouldBeTrue()` |
| PC-121 | G-121: Change strict DoneAt > cutoff to >=. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_HistoricalCutoffCannotDelete` | `equalCutoffTreeExists.ShouldBeTrue()` |
| PC-122 | G-122: Omit local cohort recheck; call direct with pre-cutoff endpoint. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_DirectAndRecoveryGatesHold` | `localHistoricalMutations.ShouldBe(0)` |
| PC-123 | G-123: Omit mirror cohort recheck; call direct with pre-cutoff endpoint. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_DirectAndRecoveryGatesHold` | `mirrorHistoricalMutations.ShouldBe(0)` |
| PC-124 | G-124: Use card.UpdatedAt instead of revision DoneAt in cutoff comparison. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_HistoricalCutoffCannotDelete` | `historicalRevisionTreeExists.ShouldBeTrue()` |
| PC-125 | G-125: Normalize a non-UTC offset to UTC instead of refusing the explicit non-UTC configuration. | `/*/*/CardCleanupCompositionTests/C1033_UpgradeStaysReportOnly` | `nonUtcStartupAccepted.ShouldBeFalse()` |
| PC-126 | G-126: Change WorktreeResidueSettings.Execute default to true. | `/*/*/CardCleanupCompositionTests/C1033_UpgradeStaysReportOnly` | `configuredExecute.ShouldBeFalse()` |
| PC-127 | G-127: Treat null CompletedAt as old enough. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_SettlingFloorCannotDelete` | `nullCompletedIntents.ShouldBe(0)` |
| PC-128 | G-128: Omit mirror age check with a below-floor otherwise eligible mirror. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_SettlingFloorCannotDelete` | `mirrorBelowFloorMutations.ShouldBe(0)` |
| PC-129 | G-129: Move admission transaction after first executor call. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_AdmissionPrecedesIo` | `admissionVisibleAtIo.ShouldBeTrue()` |
| PC-130 | G-130: Commit count before inserting admission, with failpoint between commits. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_AdmissionPrecedesIo` | `debitWithoutAdmission.ShouldBe(0)` |
| PC-131 | G-131: Remove matching unique index in model and generated migration; no other key rejects fixture duplicate. | `/*/*/CardCleanupSchedulingSchemaTests/C1033_AdditiveUpgradePreservesHistory` | `duplicateAccepted.ShouldBeFalse()` |
| PC-132 | G-132: Decrement ActionsAccepted during uncertain admission recovery. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_AdmissionPrecedesIo` | `recoveredSpentDebit.ShouldBe(1)` |
| PC-133 | G-133: Debit before the eligibility/due decision. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_AdmissionPrecedesIo` | `ineligibleDebits.ShouldBe(0)` |
| PC-134 | G-134: Remove only BoardId restriction. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_ScopeAndDedupAreExact` | `boardCandidateKeys.ShouldBe(expectedBoardKeys)` |
| PC-135 | G-135: Remove only ProjectId restriction. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_ScopeAndDedupAreExact` | `projectCandidateKeys.ShouldBe(expectedProjectKeys)` |
| PC-136 | G-136: Enumerate all active publications outside the selected task set. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_ScopeAndDedupAreExact` | `publicationKeys.ShouldBe(expectedPublicationKeys)` |
| PC-137 | G-137: Include unmatched scan entries in scoped output. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_ScopeAndDedupAreExact` | `unknownScopedRows.ShouldBeEmpty()` |
| PC-138 | G-138: Keep both lane-prefixed aliases instead of their shared endpoint key. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_ScopeAndDedupAreExact` | `duplicatePhysicalAccepts.ShouldBe(1)` |
| PC-139 | G-139: Order solely by oldest task creation, ignoring last evaluation. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_ActionBudgetIsShared` | `nextAcceptedEndpoint.ShouldBe(deferredEndpointId)` |
| PC-140 | G-140: Retain the run transaction through blocked Git execution. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_ClaimsDoNotHoldDbAcrossIo` | `gitSecondWriterCommittedBeforeIoRelease.ShouldBeTrue()` |
| PC-141 | G-141: Retain the run transaction through blocked land enqueue. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_ClaimsDoNotHoldDbAcrossIo` | `queueSecondWriterCommittedBeforeIoRelease.ShouldBeTrue()` |
| PC-142 | G-142: Retain the run transaction through blocked runner send. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_ClaimsDoNotHoldDbAcrossIo` | `runnerSecondWriterCommittedBeforeIoRelease.ShouldBeTrue()` |
| PC-143 | G-143: Bypass endpoint claim CAS at pre-intent race; leave request uniqueness active for identical IDs. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_ClaimsDoNotHoldDbAcrossIo` | `issuedOperationIds.Count.ShouldBe(1)` |
| PC-144 | G-144: Rebuild interrupted admission from task.WorktreePath after task moves. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_AdmissionPrecedesIo` | `replacementTreeBytes.ShouldBe(originalReplacementBytes)` |
| PC-145 | G-145: Ignore persisted legacy settled NotBefore. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_CooldownSurvivesRestart` | `legacyBeforeDueCommands.ShouldBe(0)` |
| PC-146 | G-146: Skip publication due predicate. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_CooldownSurvivesRestart` | `publicationBeforeDueCommands.ShouldBe(0)` |
| PC-147 | G-147: Use now > due instead of >=. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_CooldownSurvivesRestart` | `atDueCommands.ShouldBe(1)` |
| PC-148 | G-148: Persist NextAttemptAt=now instead of now+24h. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_CooldownSurvivesRestart` | `savedDue.ShouldBe(refusedAt.AddHours(24))` |
| PC-149 | G-149: Key retry cursor only by TaskId. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_CooldownSurvivesRestart` | `unrelatedEndpointExecuted.ShouldBeTrue()` |
| PC-150 | G-150: Omit only the generation evidence component from digest input. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_PermanentEvidenceIsComplete` | `generationRechecks.ShouldBe(1)` |
| PC-151 | G-151: Omit only the source evidence component from digest input. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_PermanentEvidenceIsComplete` | `sourceRechecks.ShouldBe(1)` |
| PC-152 | G-152: Omit only the preservation evidence component from digest input. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_PermanentEvidenceIsComplete` | `preservationRechecks.ShouldBe(1)` |
| PC-153 | G-153: Omit only the ownership evidence component from digest input. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_PermanentEvidenceIsComplete` | `ownershipRechecks.ShouldBe(1)` |
| PC-154 | G-154: Omit only the binding evidence component from digest input. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_PermanentEvidenceIsComplete` | `bindingRechecks.ShouldBe(1)` |
| PC-155 | G-155: Omit only the content evidence component from digest input. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_PermanentEvidenceIsComplete` | `contentRechecks.ShouldBe(1)` |
| PC-156 | G-156: Omit only the registration evidence component from digest input. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_PermanentEvidenceIsComplete` | `registrationRechecks.ShouldBe(1)` |
| PC-157 | G-157: Treat digest inequality as execution authorization despite another invalid guard. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_PermanentEvidenceIsComplete` | `stillUnsafeMutations.ShouldBe(0)` |
| PC-158 | G-158: Default unknown reason to transient and queue on elapsed time. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_PermanentRefusalAwaitsEvidence` | `unknownRepeatCommands.ShouldBe(0)` |
| PC-159 | G-159: Filter scheduled endpoints by local retirement not Complete. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_ComponentFactsAreIndependent` | `pendingMirrorVisited.ShouldBeTrue()` |
| PC-160 | G-160: Return from scheduled target when mirror transport is unavailable. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_ComponentFactsAreIndependent` | `offlineMirrorLocalDirectoryGone.ShouldBeTrue()` |
| PC-161 | G-161: Remove only original-path comparison at recovery authority boundary. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_SetAsideIdentityMustMatch` | `originalPathForeignBytes.ShouldBe(originalBytes)` |
| PC-162 | G-162: Remove only computed aside-path comparison at recovery authority boundary. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_SetAsideIdentityMustMatch` | `asidePathForeignBytes.ShouldBe(originalBytes)` |
| PC-163 | G-163: Remove only git-admin comparison at recovery authority boundary. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_SetAsideIdentityMustMatch` | `adminForeignBytes.ShouldBe(originalBytes)` |
| PC-164 | G-164: Remove only source/journal comparison at recovery authority boundary. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_SetAsideIdentityMustMatch` | `sourceForeignBytes.ShouldBe(originalBytes)` |
| PC-165 | G-165: Catch record parse/I/O failure and return an empty successful receipt. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_SetAsideIdentityMustMatch` | `malformedReceiptComplete.ShouldBeFalse()` |
| PC-166 | G-166: Treat UnauthorizedAccessException as file absent. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_AbsenceNeedsReceipt` | `deniedEndpointComplete.ShouldBeFalse()` |
| PC-167 | G-167: Set local complete without registration fact when directory/ref are gone. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_ComponentFactsAreIndependent` | `registeredEndpointComplete.ShouldBeFalse()` |
| PC-168 | G-168: Ignore failed local-ref CAS after directory/registration success. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_ComponentFactsAreIndependent` | `retainedRefEndpointComplete.ShouldBeFalse()` |
| PC-169 | G-169: Treat original absence and registration absence as complete despite owned aside. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_ComponentFactsAreIndependent` | `asideEndpointComplete.ShouldBeFalse()` |
| PC-170 | G-170: Mark/remove pending endpoint before successful mirror receipt import transaction. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_ResultSaveFailureRetainsDebt` | `mirrorRecoveredOperationId.ShouldBe(originalOperationId)` |
| PC-171 | G-171: Save endpoint outside event transaction then inject event-save failure. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_OperatorOutcomeIsDurable` | `outcomeWithoutEvent.ShouldBeFalse()` |
| PC-172 | G-172: Clear persisted outcome when AgentTaskChanged publication throws. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_OperatorOutcomeIsDurable` | `readbackAfterHintFailure.ShouldBe(expectedOutcome)` |
| PC-173 | G-173: Project both endpoints as the local endpoint or omit the saved store/attempt tuple. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_EveryIntentIsReportable` | `reportedEndpointTuples.ShouldBe(expectedEndpointTuples)` |
| PC-174 | G-174: Remove Take(pageSize) from GetRun query. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_EveryIntentIsReportable` | `firstPage.Count.ShouldBe(50)` |
| PC-175 | G-175: Reverse identity ordering only on subsequent pages. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_EveryIntentIsReportable` | `joinedPageIds.ShouldBe(expectedOrderedIds)` |
| PC-176 | G-176: Remove caller.Task scope refusal. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_EveryIntentIsReportable` | `childRunReadStatus.ShouldBe(403)` |
| PC-177 | G-177: Filter out mirror rows in the real script response projection. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_OperatorOutcomeIsDurable` | `scriptEndpointIds.ShouldBe(expectedLocalAndMirrorIds)` |
| PC-178 | G-178: Include raw fault path containing synthetic credential in report reason. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_NoOpaqueBytesInReport` | `operatorOutput.ShouldNotContain("C1033_URL_CREDENTIAL")` |
| PC-179 | G-179: Invoke wakeup before SaveCardWriteAsync. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_DoneQueueAlreadyEligible` | `obligationVisibleAtWake.ShouldBeTrue()` |
| PC-180 | G-180: Enqueue while Database.CurrentTransaction is still open. | `/*/*/CardCleanupCompositionTests/C1033_ExternalTransactionCannotWake` | `enqueuesBeforeCommit.ShouldBe(0)` |
| PC-181 | G-181: Remove startup recovery enqueue while leaving recurrence registration. | `/*/*/CardCleanupCompositionTests/C1033_StartupRecoversCommittedDone` | `startupRecipientDirectoryGone.ShouldBeTrue()` |
| PC-182 | G-182: Ignore Enabled only at startup recovery. | `/*/*/CardCleanupCompositionTests/C1033_StartupRecoversCommittedDone` | `disabledStartupEnqueues.ShouldBe(0)` |
| PC-183 | G-183: Ignore Enabled only in manual wakeup adapter. | `/*/*/CardCleanupCompositionTests/C1033_UpgradeStaysReportOnly` | `disabledManualEnqueues.ShouldBe(0)` |
| PC-184 | G-184: Register an additional cleanup recurring job. | `/*/*/CardCleanupCompositionTests/C1033_UpgradeStaysReportOnly` | `cleanupRecurringIds.ShouldBe(new[] { "antiphon:worktree-residue" })` |
| PC-185 | G-185: Omit fully completed cleanup headers from scheduler discovery. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_DiscoveryRemainsOpen` | `discoveredAttempts.ShouldBe(new[] { 1, 2 })` |
| PC-186 | G-186: Fabricate a Done revision/cutoff-eligible time for legacy Done status. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_DiscoveryRemainsOpen` | `revisionlessIntents.ShouldBe(0)` |
| PC-187 | G-187: Remove process_ownership_unknown return for AgentId. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_UnknownOwnershipStaysHeld` | `ownershipReason.ShouldBe("process_ownership_unknown")` |
| PC-188 | G-188: Ignore active landing without confirmed publication in debt predicate. | `/*/*/CardDoneWorktreeCleanupTests/C1017_RecoveryDebtHolds` | `unconfirmedSettledRemovalCalls.ShouldBe(0)` |
| PC-189 | G-189: Ignore the independent unfinished repair/recovery obligation query. | `/*/*/CardDoneWorktreeCleanupTests/C1017_RecoveryDebtHolds` | `repairDebtRemovalCalls.ShouldBe(0)` |
| PC-190 | G-190: Treat unprovable landing mapping as absent debt. | `/*/*/CardDoneWorktreeCleanupTests/C1017_RecoveryDebtHolds` | `unknownMappingRemovalCalls.ShouldBe(0)` |
| PC-191 | G-191: Classify confirmed publication as generic recovery debt. | `/*/*/CardDoneWorktreeCleanupTests/C1017_RecoveryDebtHolds` | `confirmedPublicationCleanupGone.ShouldBeTrue()` |
| PC-192 | G-192: Copy task.ReplyTo into scheduled cleanup request instead of None. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_PublicationIsImmutable` | `newCallerOutcomeObligations.ShouldBe(0)` |
| PC-193 | G-193: Clear IsPending when land TryEnqueue returns false/throws. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_EnqueueFailureRemainsRecoverable` | `landDirectoryGoneAfterRecovery.ShouldBeTrue()` |
| PC-194 | G-194: Clear stored request on first socket send failure. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_EnqueueFailureRemainsRecoverable` | `recoveredMirrorOperationId.ShouldBe(originalOperationId)` |
| PC-195 | G-195: Populate new accepted-at/action fields on old Done inventory rows. | `/*/*/CardCleanupSchedulingSchemaTests/C1033_AdditiveUpgradePreservesHistory` | `historicalAcceptedRows.ShouldBeEmpty()` |
| PC-196 | G-196: Remove worker root/nonce ownership check; use foreign-root refusal arm before real crash loop. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1033_LocalCrashHandoffs` | `foreignRootWorkerAccepted.ShouldBeFalse()` |
| PC-197 | G-197: Remove ready-path confinement check. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1033_LocalCrashHandoffs` | `outsideReadyWorkerAccepted.ShouldBeFalse()` |
| PC-198 | G-198: Remove cut allowlist check; unknown-cut fixture arm requests an otherwise valid owned task. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_CrashEveryHandoff` | `unknownCutWorkerAccepted.ShouldBeFalse()` |
| PC-199 | G-199: Remove task/target owner check while retaining root/nonce/cut checks. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1033_LocalCrashHandoffs` | `foreignTaskWorkerAccepted.ShouldBeFalse()` |
| PC-200 | G-200: Return before importing an already complete exact receipt whenever Execute=false. | `/*/*/CardWorktreeCleanupRecoveryTests/C1017_ExecuteFalseCannotDelete` | `disabledReceiptImported.ShouldBeTrue()` |
| PC-201 | G-201: Skip cohort check only for a queued CardDone publication request. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_DirectAndRecoveryGatesHold` | `publicationHistoricalMutations.ShouldBe(0)` |
| PC-202 | G-202: Skip preview guard only for a queued CardDone publication request. | `/*/*/CardWorktreeCleanupRecoveryTests/C1033_DirectAndRecoveryGatesHold` | `publicationPreviewDeniedMutations.ShouldBe(0)` |
| PC-203 | G-203: Replace the manual post-commit wakeup adapter enqueue with a completed no-op. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_DoneQueueAlreadyEligible` | `recipientDirectoryGone.ShouldBeTrue()` |
| PC-204 | G-204: Skip the cleanup-only request execution in the land worker after dequeue. | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_PublicationQueueBusyRecipient` | `recipientDirectoryGone.ShouldBeTrue()` |

### Out of scope

- Activation, historical cohort/cutoff selection, live path deletion, process stops,
  deployment and SourceLanding custody are not commissioned. Keep execution disabled.
- CARD-1032's new protocol/binding/digest/runtime admission implementation and expanded
  C1032 controls remain in that owner's manifest; the U21/M12 compatibility surface
  and composed recipient tests here are additional obligations, not replacements.
- No checkpoint-tool implementation, RemoteScriptContractTests, c590-remote, or server
  AgentSessionService/AgentService/AgentControlService edits. A required seam in those
  files returns to Plan with the observed mismatch, rather than broadening this task.
- No whole Unit/assembly run, deployed phone-home canary or session-input delivery claim.
  Native qualification needs the stated Windows witness, not a Linux substitute.

#### Frozen rosters and slice binding

The following rosters are source-censused at the inspected SHA; new methods are
planned single-result tests. Counts do not claim execution. Each line is
Class: method=argument-expanded-result-count. Named internal vectors count once.
The method filters below enumerate these names, not a category whose membership
can grow silently. Existing full-class selections were narrowed to these methods.

S1 builds K's three policy methods with foundation/local vectors and explicitly
opts F/L fixtures into a test-only UTC cohort. Those receipts qualify only the
local S1 slice. S3 adds the direct/scheduler mirror vectors using the actual S3
contract; S12 completes the queued mirror variants. The final S17 rows require
all frozen vectors, with a recorded vector roster; an unfinished variant cannot
be reported as qualified. S3-dependent methods require the named CARD-1032
implementation before their slice runs. S1/S2 remain independently dispatchable.
Schema/composition test files live in tests/Antiphon.Tests/Application; helper
filenames are in TestHelpers. New guard assertion labels are binding required
after Code, including imported PCs whose current body lacks the specified witness.

```text
CardDoneWorktreeCleanupTests: methods=18, executions=18
C1017_OnlyDoneAuthorizes=1, C1017_GenerationMustMatch=1, C1017_TerminalTaskRequired=1, C1017_UniqueOrdinaryOwner=1, C1017_ReopenBeforeIntentRevokes=1, C1017_IntentBeforeReopenKeepsFence=1, C1017_ActiveConsumersHold=1, C1017_RecoveryDebtHolds=1, C1017_SnapshotAttemptIsFresh=1, C1017_SnapshotReportIsFresh=1, C1017_NoDbLockAcrossIo=1, C1017_SharedAndBorrowedExcluded=1, C1017_SourceLandingExcluded=1, C1017_SlotAndCanonicalExcluded=1, C1017_DiscoverPostDoneAttempts=1, C1017_ExactCardBinding=1, C1017_DuplicateDoneIsIdempotent=1, C1017_DoneSaveIsAtomic=1
CompletedCardWorktreeRemovalTests: methods=36, executions=36
C1017_EvidenceNeedsDoneDisposition=1, C1017_DisposalFlagIsNotAuthority=1, C1017_LeaseMustBeGenuine=1, C1017_SourceShaMustMatch=1, C1017_RepositoryMustMatch=1, C1017_PathMustMatch=1, C1017_BranchMustMatch=1, C1017_CommonDirectoryMustMatch=1, C1017_AdminDirectoryMustMatch=1, C1017_DirtyTrackedIsHeld=1, C1017_UntrackedSourceIsHeld=1, C1017_UnpushedCommitIsHeld=1, C1017_TaskBranchOnlyIsHeld=1, C1017_RebaseMappingMustBeBound=1, C1017_RemoteProofMustBeFresh=1, C1017_ProtectedIgnoredIsHeld=1, C1017_FinalContentIsRechecked=1, C1017_EvidenceChurnIsHeld=1, C1017_LinksNeverFollowed=1, C1017_RootConfinementIsFresh=1, C1017_NestedRegistrationIsHeld=1, C1017_RegistrationMustExist=1, C1017_LockedRegistrationIsHeld=1, C1017_PrunableRegistrationIsHeld=1, C1017_ReportMustBeExternal=1, C1017_ReportDigestMustMatch=1, C1017_TaskReportDetailMustSurvive=1, C1017_DeliverablesMustBeExternal=1, C1017_DeliverablesMustExist=1, C1017_FinalAuthorityIsFresh=1, C1017_BranchCasPreservesConcurrentRef=1, C1017_OriginRefsSurvive=1, C1017_NoImplicitResetOrForce=1, C1017_MissingReportNeedsDisposition=1, C1017_FirstIgnoredLinkIsHeld=1, C1017_FinalIgnoredLinkIsHeld=1
CardTaskSettlementTests: methods=8, executions=8
closing_a_card_cancels_its_blocked_task_with_the_close_reason=1, closing_a_card_cancels_queued_and_unstarted_dispatched_tasks=1, closing_a_card_leaves_started_tasks_running_with_one_warning_event=1, closing_a_card_touches_no_other_cards_tasks_and_no_settled_task=1, archiving_a_card_cancels_its_queued_task_with_the_archive_reason=1, a_move_to_review_settles_nothing_and_a_reopen_resurrects_nothing=1, a_task_settled_by_a_concurrent_writer_is_skipped_and_the_move_still_succeeds=1, a_tracker_stale_close_cancels_the_cards_queued_task=1
ClosedCardSweepTests: methods=2, executions=2
the_sweep_lists_open_tasks_on_closed_and_archived_cards_and_changes_nothing=1, apply_cancels_only_unstarted_rows_created_before_the_close=1
WorktreeRemovalAuthorityTests: methods=3, executions=9
C448_V35_BuildJunkScriptPreservesOpaqueWildcardMatches=1, C688_PublicationCleanupDeletesBranchAtLocalSha=3, C448_V24_LegacyRemovalCannotEraseTaskContents=5
AgentReportStoreTests: methods=7, executions=28
Exact_content_and_full_ids_own_distinct_files=1, Root_selection_requires_a_durable_owned_location=11, Ignore_and_tracking_are_checked_before_writing=5, Ineffective_ignore_attempts_do_not_duplicate_the_exclude_rule=2, Publication_exposes_only_a_complete_verified_report=1, Paths_over_the_persisted_limit_are_unavailable=1, Failures_and_cancellation_never_publish_a_bad_pointer=7
RemoteWorktreeMirrorTests: methods=8, executions=8
Push_reads_the_desktop_origin_and_the_mirror_request_carries_its_https_identity=1, Push_refuses_when_the_desktop_origin_cannot_be_read=1, Sync_fast_forwards_desktop_worktree=1, Non_fast_forward_sync_is_refused=1, Dirty_desktop_tree_is_never_fast_forwarded_over=1, Push_failure_reports_and_does_not_invent_a_sha=1, Push_reports_the_head_it_pushed=1, Mirror_name_is_the_dispatchers_own_short_form=1
WorktreeResidueEndpointTests: methods=6, executions=6
C459_PreviewCannotExecute=1, C459_CallerScopeEnforced=1, C459_ReleaseRejectsStaleApprovalSnapshot=1, C459_RunResultSurvivesRestart=1, C459_RunReportOmitsOpaqueContent=1, C459_RunReportPageIsBounded=1
WorktreeResidueScriptTests: methods=1, executions=1
C459_BatchRequiresFullIds=1
WorktreeResidueRegistrationTests: methods=3, executions=3
C459_OneScheduler=1, C459_DisabledSchedulerStaysOff=1, C459_ScheduledWorkerExecutesBothLanes=1
WorkspaceReservationLivenessTests: methods=8, executions=20
C664_FreshRowBlocksWhateverTheOwner=1, C664_LiveTaskOwnerBlocksAfterGrace=4, C664_TerminalTaskOwnerDoesNotBlockAfterGrace=3, C664_PendingLandKeepsTerminalTaskBlocking=1, C664_LiveSessionOwnerBlocksAfterGrace=4, C664_EndedSessionOwnerDoesNotBlockAfterGrace=2, C664_MissingOrUnattributedOwnerDoesNotBlockAfterGrace=3, C664_RetirementAndFenceRowsAlwaysBlock=2
TaskWorktreeRetirementTests: methods=17, executions=38
C459_ReleasedTerminalTasksRetire=3, C459_TerminalRequired=1, C459_SettlingFloor=4, C459_ReleaseRequired=1, C459_ReleaseSnapshotIsExact=3, C459_HandoffDispositionRequired=6, C459_ArtifactsPreserved=3, C459_RevokeRespectsIntent=5, C459_UniqueFullOwner=3, C459_MutationIsExcluded=1, C459_SourceLandingIsExcluded=1, C459_RepairSourceIsExcluded=1, C459_TargetSelectionIsExplicit=1, C459_RecoveryDebtHolds=2, C459_ReleaseIdentityIsUnique=1, C459_LegacyRowsAreUnreleased=1, C459_OwnProgressKeepsRelease=1
SettledWorktreeRemovalTests: methods=26, executions=78
C459_InterfaceDefaultCannotDeleteSettledTask=1, C459_PurposeCannotBorrowAuthority=1, C459_CoordinatesMatchReceipt=8, C459_ManagedRootRequired=5, C459_NestedRegistrationHolds=1, C459_RegistrationRequired=2, C459_LockedRegistrationHolds=2, C459_PrunableRegistrationHolds=1, C459_RemoteContainmentRequired=7, C459_DestinationIdentityRequired=2, C459_RemoteRefreshBeforeDirectory=3, C459_RemoteRefreshBeforeBranch=2, C459_GenuineLeaseRequired=4, C459_CommittedReceiptRequired=4, C459_FinalAuthorityRequired=1, C459_FirstContentInspectionRequired=6, C459_FinalContentInspectionRequired=4, C459_FirstIgnoredInspectionRequired=3, C459_FinalIgnoredInspectionRequired=3, C459_AbsenceNeedsOwnIntent=6, C459_RecreatedTreeHolds=3, C459_BranchPrecheckRequired=1, C459_BranchDeleteUsesCas=1, C459_NewCheckoutHoldsBranch=1, C459_RemovalNeverForces=1, C459_RetirementRefsCannotBorrowNamespaces=5
RunnerWorkspaceServiceTests: methods=25, executions=25
Publish_pushes_only_own_fast_forward_branch=1, Publish_refuses_a_rebased_tip_without_pushing=1, Publish_equal_tip_is_not_pushed_and_reports_dirty_tree=1, Publish_refuses_foreign_path_and_branch_before_git=1, Publish_refuses_detached_head=1, Publish_refuses_an_active_sequencer_before_push=1, Publish_reports_rejected_non_fast_forward_without_exposing_origin=1, Publish_classifies_a_tip_behind_the_advertised_remote=1, Remove_refuses_committed_unpublished_tip_unless_forced=1, Remove_allows_a_published_tip=1, Remove_allows_a_tip_which_is_ancestor_of_published_sha=1, Mirror_of_a_second_repository_clones_beside_the_primary_and_removes_through_its_own_checkout=1, Mirror_refuses_a_repository_outside_the_allowed_clone_sources=1, Mirror_refuses_credentialed_request_without_echoing_credentials=1, Mirror_refuses_an_existing_checkout_whose_origin_is_another_repository=1, Mirror_mismatch_does_not_echo_credentialed_origin=1, Mirror_refuses_a_secondary_repository_the_push_credential_cannot_push_to=1, Mirror_accepts_a_secondary_repository_when_push_dry_run_succeeds=1, Mirror_creates_worktree_on_branch_at_sha=1, Mirror_refuses_sha_mismatch=1, Mirror_clones_repository_when_absent=1, Mirror_failure_is_admission_error_not_crash=1, Mirror_refuses_a_name_or_sha_it_did_not_shape=1, Remove_refuses_dirty_tree=1, Remove_refuses_a_path_outside_the_worktree_root=1
PhoneHomeCommandDispatcherTests: methods=3, executions=3
Workspace_ops_are_admitted_only_under_allowed_cwd=1, Workspace_mirror_naming_an_unadmitted_repository_is_refused_before_any_git_runs=1, Workspace_publish_route_rejects_a_foreign_target_before_git=1
WorktreeLandingCleanupRetryTests: methods=7, executions=10
C459_AdmissionPinsPublication=4, C459_ScheduledHasNoCallerObligation=1, C459_PublicationIsNotRepeated=1, C459_ExecutionPinsPublicationBeforeResolver=1, C459_ProtocolPinsPublication=1, C459_ConfirmedCleanupRetryCompletes=1, C459_LostWakeupRecoversSameRequest=1
WorktreeResidueRecoveryTests: methods=3, executions=3
C459_IntentCommittedBeforeGit=1, C459_ComponentsAreIndependent=1, C459_OutageKeepsFence=1
WorktreeRetirementRaceTests: methods=2, executions=4
C459_OneRetirementClaim=1, C459_CreateReservesWorkspace=3
CardWorktreeCleanupRecoveryTests: methods=24, executions=24
C1017_ExecuteFalseCannotDelete=1, C1017_HistoricalCutoffCannotDelete=1, C1017_SettlingFloorCannotDelete=1, C1033_DirectAndRecoveryGatesHold=1, C1033_PreviewDoesNotMutate=1, C1033_UnknownOwnershipStaysHeld=1, C1017_ActionBudgetIsShared=1, C1033_ScopeAndDedupAreExact=1, C1033_AdmissionPrecedesIo=1, C1033_ClaimsDoNotHoldDbAcrossIo=1, C1033_DiscoveryRemainsOpen=1, C1017_CooldownSurvivesRestart=1, C1017_PermanentRefusalAwaitsEvidence=1, C1033_PermanentEvidenceIsComplete=1, C1017_IntentRecoveryDoesNotResetBudget=1, C1017_SetAsideRecoveryPreservesAuthority=1, C1017_SetAsideIdentityMustMatch=1, C1017_ReplacementPathIsUntouched=1, C1017_AbsenceNeedsReceipt=1, C1017_ComponentFactsAreIndependent=1, C1017_PublicationIsImmutable=1, C1017_ResultSaveFailureRetainsDebt=1, C1017_LostWakeupRecovers=1, C1017_EnqueueFailureRemainsRecoverable=1
CardWorktreeCleanupAcceptanceTests: methods=12, executions=12
C1017_OperatorOutcomeIsDurable=1, C1017_EveryIntentIsReportable=1, C1017_NoOpaqueBytesInReport=1, C1017_ReportReadbackSurvivesDeletion=1, C1033_LocalCrashHandoffs=1, C1017_CrashEveryHandoff=1, C1017_DoneQueueBusyRecipient=1, C1017_DoneQueueAlreadyEligible=1, C1017_PublicationQueueBusyRecipient=1, C1017_PublicationQueueAlreadyEligible=1, C1017_PhoneHomeQueueBusyRecipient=1, C1017_PhoneHomeQueueAlreadyEligible=1
CompletedCardWorktreeRemovalWindowsTests: methods=4, executions=4
C1017_LockedSetAsideRetries=1, C1017_JunctionOutsideBytesSurvive=1, C1017_LingeringPtyHostIsReported=1, C1017_CleanupNeverKillsHolder=1
CardCleanupCompositionTests: methods=3, executions=3
C1033_UpgradeStaysReportOnly=1, C1033_ExternalTransactionCannotWake=1, C1033_StartupRecoversCommittedDone=1
CardCleanupSchedulingSchemaTests: methods=1, executions=1
C1033_AdditiveUpgradePreservesHistory=1
CompletedCardWorkspaceRemovalTests: methods=21, executions=21
C1017_GuardedOperationIsDistinct=1, C1017_RunnerTaskIdentity=1, C1017_RunnerAttemptIdentity=1, C1017_RunnerStoreIdentity=1, C1017_RunnerRepositoryIdentity=1, C1017_RunnerPathIdentity=1, C1017_RunnerBranchIdentity=1, C1017_RunnerHeadIdentity=1, C1017_RunnerPreservationProof=1, C1017_RunnerDirtySource=1, C1017_RunnerIgnoredPolicy=1, C1017_RunnerNoFollow=1, C1017_RunnerProcessOwnerHolds=1, C1017_RunnerStartFence=1, C1017_RunnerReadoptionFence=1, C1017_RunnerIntentBeforeIo=1, C1017_RunnerDigestReplay=1, C1017_RunnerFreshFinalInspection=1, C1017_RunnerNoForceBypass=1, C1017_RunnerSourceLandingExcluded=1, C1017_UnknownProcessOwnershipHolds=1
CardWorktreeMirrorCleanupTests: methods=12, executions=12
C1017_MirrorCompletesIndependently=1, C1017_LocalProgressSurvivesOffline=1, C1017_MirrorBehindPushedTip=1, C1017_ReceiptMustMatchOperation=1, C1017_ReceiptMustMatchEndpoint=1, C1017_ReceiptMustMatchStore=1, C1017_ReceiptMustMatchTaskAttempt=1, C1017_AckIsNotRemoval=1, C1017_UnsupportedNeverFallsBack=1, C1017_ReopenBetweenEndpoints=1, C1017_MultipleMirrorsRemainDistinct=1, C1017_LostAckReusesOperation=1
TestClassificationGuardTests: methods=1, executions=1
Registry_matches_compiled_metadata=1
```

### Checkpoints

This table is the closed ordinary list: 50 rows, one fresh isolated build and
one exact TUnit filter per row. No build reuse. Names prefixed windows require
Windows; omit a platform pin for portable groups. The table includes incremental
slice checks and an intentional S17 qualification repeat after production DI is
wired with execution still disabled. S17 rows use the same committed source SHA
on both lanes; they may be dispatched as several bounded verification tasks with
no intervening source edits. Preserve the original S1..S17 implementation split.

Commit/push the slice before its rows. Use the checkpoint tool's run --plan with
this path, explicit --rows and --expected-source-sha, through the documented host
build-slot gate; wait on exit 75 until the run finishes. Exit 4 is not a tested row.
Min is -MinExecuted; Expect additionally requires precisely the listed methods
and their argument-expanded results, zero failed and zero skipped. Assert the
frozen named vector roster in new methods so an internal case cannot disappear
behind an unchanged Min. Before execution Code binds compiled coverage for these
selectors; do not run dotnet test or an ad hoc assembly. No build or test driver ran during TestDesign. Read-only prebuilt importer/validator
results are recorded below; syntax/census validation is not an execution receipt. Cleanup removes only the row-owned bin-c1033-* outputs.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1033-01/` | portable-policy-local | `/*/*/CardWorktreeCleanupRecoveryTests/(C1017_ExecuteFalseCannotDelete)\|(C1017_HistoricalCutoffCannotDelete)\|(C1017_SettlingFloorCannotDelete)` | V-1 | all 3 listed methods, 3 results; 0 failed/skipped | 3 | 5 | true |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1033-02/` | portable-fixture-compat | `/*/*/(CardDoneWorktreeCleanupTests)\|(CompletedCardWorktreeRemovalTests)/(C1017_OnlyDoneAuthorizes)\|(C1017_GenerationMustMatch)\|(C1017_TerminalTaskRequired)\|(C1017_UniqueOrdinaryOwner)\|(C1017_ReopenBeforeIntentRevokes)\|(C1017_IntentBeforeReopenKeepsFence)\|(C1017_ActiveConsumersHold)\|(C1017_RecoveryDebtHolds)\|(C1017_SnapshotAttemptIsFresh)\|(C1017_SnapshotReportIsFresh)\|(C1017_NoDbLockAcrossIo)\|(C1017_SharedAndBorrowedExcluded)\|(C1017_SourceLandingExcluded)\|(C1017_SlotAndCanonicalExcluded)\|(C1017_DiscoverPostDoneAttempts)\|(C1017_ExactCardBinding)\|(C1017_DuplicateDoneIsIdempotent)\|(C1017_DoneSaveIsAtomic)\|(C1017_EvidenceNeedsDoneDisposition)\|(C1017_DisposalFlagIsNotAuthority)\|(C1017_LeaseMustBeGenuine)\|(C1017_SourceShaMustMatch)\|(C1017_RepositoryMustMatch)\|(C1017_PathMustMatch)\|(C1017_BranchMustMatch)\|(C1017_CommonDirectoryMustMatch)\|(C1017_AdminDirectoryMustMatch)\|(C1017_DirtyTrackedIsHeld)\|(C1017_UntrackedSourceIsHeld)\|(C1017_UnpushedCommitIsHeld)\|(C1017_TaskBranchOnlyIsHeld)\|(C1017_RebaseMappingMustBeBound)\|(C1017_RemoteProofMustBeFresh)\|(C1017_ProtectedIgnoredIsHeld)\|(C1017_FinalContentIsRechecked)\|(C1017_EvidenceChurnIsHeld)\|(C1017_LinksNeverFollowed)\|(C1017_RootConfinementIsFresh)\|(C1017_NestedRegistrationIsHeld)\|(C1017_RegistrationMustExist)\|(C1017_LockedRegistrationIsHeld)\|(C1017_PrunableRegistrationIsHeld)\|(C1017_ReportMustBeExternal)\|(C1017_ReportDigestMustMatch)\|(C1017_TaskReportDetailMustSurvive)\|(C1017_DeliverablesMustBeExternal)\|(C1017_DeliverablesMustExist)\|(C1017_FinalAuthorityIsFresh)\|(C1017_BranchCasPreservesConcurrentRef)\|(C1017_OriginRefsSurvive)\|(C1017_NoImplicitResetOrForce)\|(C1017_MissingReportNeedsDisposition)\|(C1017_FirstIgnoredLinkIsHeld)\|(C1017_FinalIgnoredLinkIsHeld)` | R-1, R-2 | all 54 listed methods, 54 results; 0 failed/skipped | 54 | 8 | true |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c1033-03/` | portable-classification | `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata` | R-5 | all 1 listed methods, 1 results; 0 failed/skipped | 1 | 3 | true |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1033-04/` | portable-schema | `/*/*/CardCleanupSchedulingSchemaTests/C1033_AdditiveUpgradePreservesHistory` | V-8 | all 1 listed methods, 1 results; 0 failed/skipped | 1 | 5 | true |
| CP-5 | S3 | `tests/Antiphon.Tests -> bin-c1033-05/` | portable-entry-policy | `/*/*/CardWorktreeCleanupRecoveryTests/(C1033_DirectAndRecoveryGatesHold)\|(C1033_PreviewDoesNotMutate)\|(C1033_UnknownOwnershipStaysHeld)` | V-1 | all 3 listed methods, 3 results; 0 failed/skipped | 3 | 6 | true |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c1033-06/` | portable-admission | `/*/*/CardWorktreeCleanupRecoveryTests/(C1017_ActionBudgetIsShared)\|(C1033_ScopeAndDedupAreExact)\|(C1033_AdmissionPrecedesIo)\|(C1033_ClaimsDoNotHoldDbAcrossIo)\|(C1033_DiscoveryRemainsOpen)` | V-2 | all 5 listed methods, 5 results; 0 failed/skipped | 5 | 10 | true |
| CP-7 | S4 | `tests/Antiphon.Tests -> bin-c1033-07/` | portable-retry-policy | `/*/*/CardWorktreeCleanupRecoveryTests/(C1017_CooldownSurvivesRestart)\|(C1017_PermanentRefusalAwaitsEvidence)\|(C1033_PermanentEvidenceIsComplete)` | V-2 | all 3 listed methods, 3 results; 0 failed/skipped | 3 | 7 | true |
| CP-8 | S5 | `tests/Antiphon.Tests -> bin-c1033-08/` | portable-intent-recovery | `/*/*/CardWorktreeCleanupRecoveryTests/(C1017_IntentRecoveryDoesNotResetBudget)\|(C1017_ComponentFactsAreIndependent)\|(C1017_ResultSaveFailureRetainsDebt)` | V-3 | all 3 listed methods, 3 results; 0 failed/skipped | 3 | 7 | true |
| CP-9 | S6 | `tests/Antiphon.Tests -> bin-c1033-09/` | portable-aside-recovery | `/*/*/CardWorktreeCleanupRecoveryTests/(C1017_SetAsideRecoveryPreservesAuthority)\|(C1017_SetAsideIdentityMustMatch)\|(C1017_ReplacementPathIsUntouched)\|(C1017_AbsenceNeedsReceipt)` | V-3 | all 4 listed methods, 4 results; 0 failed/skipped | 4 | 7 | true |
| CP-10 | S6 | `tests/Antiphon.Tests -> bin-c1033-10/` | portable-authority-compat | `/*/*/(CompletedCardWorktreeRemovalTests)\|(WorktreeRemovalAuthorityTests)/(C1017_EvidenceNeedsDoneDisposition)\|(C1017_DisposalFlagIsNotAuthority)\|(C1017_LeaseMustBeGenuine)\|(C1017_SourceShaMustMatch)\|(C1017_RepositoryMustMatch)\|(C1017_PathMustMatch)\|(C1017_BranchMustMatch)\|(C1017_CommonDirectoryMustMatch)\|(C1017_AdminDirectoryMustMatch)\|(C1017_DirtyTrackedIsHeld)\|(C1017_UntrackedSourceIsHeld)\|(C1017_UnpushedCommitIsHeld)\|(C1017_TaskBranchOnlyIsHeld)\|(C1017_RebaseMappingMustBeBound)\|(C1017_RemoteProofMustBeFresh)\|(C1017_ProtectedIgnoredIsHeld)\|(C1017_FinalContentIsRechecked)\|(C1017_EvidenceChurnIsHeld)\|(C1017_LinksNeverFollowed)\|(C1017_RootConfinementIsFresh)\|(C1017_NestedRegistrationIsHeld)\|(C1017_RegistrationMustExist)\|(C1017_LockedRegistrationIsHeld)\|(C1017_PrunableRegistrationIsHeld)\|(C1017_ReportMustBeExternal)\|(C1017_ReportDigestMustMatch)\|(C1017_TaskReportDetailMustSurvive)\|(C1017_DeliverablesMustBeExternal)\|(C1017_DeliverablesMustExist)\|(C1017_FinalAuthorityIsFresh)\|(C1017_BranchCasPreservesConcurrentRef)\|(C1017_OriginRefsSurvive)\|(C1017_NoImplicitResetOrForce)\|(C1017_MissingReportNeedsDisposition)\|(C1017_FirstIgnoredLinkIsHeld)\|(C1017_FinalIgnoredLinkIsHeld)\|(C448_V35_BuildJunkScriptPreservesOpaqueWildcardMatches)\|(C688_PublicationCleanupDeletesBranchAtLocalSha)\|(C448_V24_LegacyRemovalCannotEraseTaskContents)` | R-2 | all 39 listed methods, 45 results; 0 failed/skipped | 45 | 8 | true |
| CP-11 | S7 | `tests/Antiphon.Tests -> bin-c1033-11/` | portable-debt-publication | `/*/*/(CardDoneWorktreeCleanupTests)\|(CardWorktreeCleanupRecoveryTests)/(C1017_OnlyDoneAuthorizes)\|(C1017_GenerationMustMatch)\|(C1017_TerminalTaskRequired)\|(C1017_UniqueOrdinaryOwner)\|(C1017_ReopenBeforeIntentRevokes)\|(C1017_IntentBeforeReopenKeepsFence)\|(C1017_ActiveConsumersHold)\|(C1017_RecoveryDebtHolds)\|(C1017_SnapshotAttemptIsFresh)\|(C1017_SnapshotReportIsFresh)\|(C1017_NoDbLockAcrossIo)\|(C1017_SharedAndBorrowedExcluded)\|(C1017_SourceLandingExcluded)\|(C1017_SlotAndCanonicalExcluded)\|(C1017_DiscoverPostDoneAttempts)\|(C1017_ExactCardBinding)\|(C1017_DuplicateDoneIsIdempotent)\|(C1017_DoneSaveIsAtomic)\|(C1017_PublicationIsImmutable)` | V-3, R-1 | all 19 listed methods, 19 results; 0 failed/skipped | 19 | 7 | true |
| CP-12 | S7 | `tests/Antiphon.Tests -> bin-c1033-12/` | portable-landing-compat | `/*/*/WorktreeLandingCleanupRetryTests/(C459_AdmissionPinsPublication)\|(C459_ScheduledHasNoCallerObligation)\|(C459_PublicationIsNotRepeated)\|(C459_ExecutionPinsPublicationBeforeResolver)\|(C459_ProtocolPinsPublication)\|(C459_ConfirmedCleanupRetryCompletes)\|(C459_LostWakeupRecoversSameRequest)` | R-2 | all 7 listed methods, 10 results; 0 failed/skipped | 10 | 6 | true |
| CP-13 | S8 | `tests/Antiphon.Tests -> bin-c1033-13/` | portable-wakeup-recovery | `/*/*/CardWorktreeCleanupRecoveryTests/(C1017_LostWakeupRecovers)\|(C1017_EnqueueFailureRemainsRecoverable)` | V-5 | all 2 listed methods, 2 results; 0 failed/skipped | 2 | 7 | true |
| CP-14 | S9 | `tests/Antiphon.Tests -> bin-c1033-14/` | portable-operator-readback | `/*/*/CardWorktreeCleanupAcceptanceTests/(C1017_OperatorOutcomeIsDurable)\|(C1017_EveryIntentIsReportable)\|(C1017_NoOpaqueBytesInReport)\|(C1017_ReportReadbackSurvivesDeletion)` | V-4 | all 4 listed methods, 4 results; 0 failed/skipped | 4 | 8 | true |
| CP-15 | S9 | `tests/Antiphon.Tests -> bin-c1033-15/` | portable-classification | `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata` | R-5 | all 1 listed methods, 1 results; 0 failed/skipped | 1 | 3 | true |
| CP-16 | S10 | `tests/Antiphon.Tests -> bin-c1033-16/` | portable-done-delivery | `/*/*/CardWorktreeCleanupAcceptanceTests/(C1017_DoneQueueBusyRecipient)\|(C1017_DoneQueueAlreadyEligible)` | V-5 | all 2 listed methods, 2 results; 0 failed/skipped | 2 | 7 | true |
| CP-17 | S11 | `tests/Antiphon.Tests -> bin-c1033-17/` | portable-publication-delivery | `/*/*/CardWorktreeCleanupAcceptanceTests/(C1017_PublicationQueueBusyRecipient)\|(C1017_PublicationQueueAlreadyEligible)` | V-5 | all 2 listed methods, 2 results; 0 failed/skipped | 2 | 7 | true |
| CP-18 | S12 | `tests/Antiphon.Tests -> bin-c1033-18/` | portable-mirror-delivery | `/*/*/CardWorktreeCleanupAcceptanceTests/(C1017_PhoneHomeQueueBusyRecipient)\|(C1017_PhoneHomeQueueAlreadyEligible)` | V-5 | all 2 listed methods, 2 results; 0 failed/skipped | 2 | 8 | true |
| CP-19 | S13a | `tests/Antiphon.Tests -> bin-c1033-19/` | portable-local-crash | `/*/*/CardWorktreeCleanupAcceptanceTests/C1033_LocalCrashHandoffs` | V-5 | all 1 listed methods, 1 results; 0 failed/skipped | 1 | 12 | true |
| CP-20 | S13b | `tests/Antiphon.Tests -> bin-c1033-20/` | portable-full-crash | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_CrashEveryHandoff` | V-5 | all 1 listed methods, 1 results; 0 failed/skipped | 1 | 16 | true |
| CP-21 | S14 | `tests/Antiphon.Tests -> bin-c1033-21/` | windows-lock-junction | `/*/*/CompletedCardWorktreeRemovalWindowsTests/(C1017_LockedSetAsideRetries)\|(C1017_JunctionOutsideBytesSurvive)` | V-6 | all 2 listed methods, 2 results; 0 failed/skipped | 2 | 8 | true |
| CP-22 | S14 | `tests/Antiphon.Tests -> bin-c1033-22/` | portable-classification | `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata` | R-5 | all 1 listed methods, 1 results; 0 failed/skipped | 1 | 3 | true |
| CP-23 | S15 | `tests/Antiphon.Tests -> bin-c1033-23/` | windows-live-holder | `/*/*/CompletedCardWorktreeRemovalWindowsTests/(C1017_LingeringPtyHostIsReported)\|(C1017_CleanupNeverKillsHolder)` | V-6 | all 2 listed methods, 2 results; 0 failed/skipped | 2 | 10 | true |
| CP-24 | S16 | `tests/Antiphon.SessionRunner.Tests -> bin-c1033-24/` | portable-runner-guard | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_GuardedOperationIsDistinct)\|(C1017_RunnerTaskIdentity)\|(C1017_RunnerAttemptIdentity)\|(C1017_RunnerStoreIdentity)\|(C1017_RunnerRepositoryIdentity)\|(C1017_RunnerPathIdentity)\|(C1017_RunnerBranchIdentity)\|(C1017_RunnerHeadIdentity)\|(C1017_RunnerPreservationProof)\|(C1017_RunnerDirtySource)\|(C1017_RunnerIgnoredPolicy)\|(C1017_RunnerNoFollow)\|(C1017_RunnerProcessOwnerHolds)\|(C1017_RunnerStartFence)\|(C1017_RunnerReadoptionFence)\|(C1017_RunnerIntentBeforeIo)\|(C1017_RunnerDigestReplay)\|(C1017_RunnerFreshFinalInspection)\|(C1017_RunnerNoForceBypass)\|(C1017_RunnerSourceLandingExcluded)\|(C1017_UnknownProcessOwnershipHolds)` | R-3 | all 21 listed methods, 21 results; 0 failed/skipped | 21 | 7 | true |
| CP-25 | S16 | `tests/Antiphon.SessionRunner.Tests -> bin-c1033-25/` | portable-runner-legacy | `/*/*/(RunnerWorkspaceServiceTests)\|(PhoneHomeCommandDispatcherTests)/(Publish_pushes_only_own_fast_forward_branch)\|(Publish_refuses_a_rebased_tip_without_pushing)\|(Publish_equal_tip_is_not_pushed_and_reports_dirty_tree)\|(Publish_refuses_foreign_path_and_branch_before_git)\|(Publish_refuses_detached_head)\|(Publish_refuses_an_active_sequencer_before_push)\|(Publish_reports_rejected_non_fast_forward_without_exposing_origin)\|(Publish_classifies_a_tip_behind_the_advertised_remote)\|(Remove_refuses_committed_unpublished_tip_unless_forced)\|(Remove_allows_a_published_tip)\|(Remove_allows_a_tip_which_is_ancestor_of_published_sha)\|(Mirror_of_a_second_repository_clones_beside_the_primary_and_removes_through_its_own_checkout)\|(Mirror_refuses_a_repository_outside_the_allowed_clone_sources)\|(Mirror_refuses_credentialed_request_without_echoing_credentials)\|(Mirror_refuses_an_existing_checkout_whose_origin_is_another_repository)\|(Mirror_mismatch_does_not_echo_credentialed_origin)\|(Mirror_refuses_a_secondary_repository_the_push_credential_cannot_push_to)\|(Mirror_accepts_a_secondary_repository_when_push_dry_run_succeeds)\|(Mirror_creates_worktree_on_branch_at_sha)\|(Mirror_refuses_sha_mismatch)\|(Mirror_clones_repository_when_absent)\|(Mirror_failure_is_admission_error_not_crash)\|(Mirror_refuses_a_name_or_sha_it_did_not_shape)\|(Remove_refuses_dirty_tree)\|(Remove_refuses_a_path_outside_the_worktree_root)\|(Workspace_ops_are_admitted_only_under_allowed_cwd)\|(Workspace_mirror_naming_an_unadmitted_repository_is_refused_before_any_git_runs)\|(Workspace_publish_route_rejects_a_foreign_target_before_git)` | R-3 | all 28 listed methods, 28 results; 0 failed/skipped | 28 | 7 | true |
| CP-26 | S16 | `tests/Antiphon.Tests -> bin-c1033-26/` | portable-mirror-compat | `/*/*/(CardWorktreeMirrorCleanupTests)\|(RemoteWorktreeMirrorTests)/(C1017_MirrorCompletesIndependently)\|(C1017_LocalProgressSurvivesOffline)\|(C1017_MirrorBehindPushedTip)\|(C1017_ReceiptMustMatchOperation)\|(C1017_ReceiptMustMatchEndpoint)\|(C1017_ReceiptMustMatchStore)\|(C1017_ReceiptMustMatchTaskAttempt)\|(C1017_AckIsNotRemoval)\|(C1017_UnsupportedNeverFallsBack)\|(C1017_ReopenBetweenEndpoints)\|(C1017_MultipleMirrorsRemainDistinct)\|(C1017_LostAckReusesOperation)\|(Push_reads_the_desktop_origin_and_the_mirror_request_carries_its_https_identity)\|(Push_refuses_when_the_desktop_origin_cannot_be_read)\|(Sync_fast_forwards_desktop_worktree)\|(Non_fast_forward_sync_is_refused)\|(Dirty_desktop_tree_is_never_fast_forwarded_over)\|(Push_failure_reports_and_does_not_invent_a_sha)\|(Push_reports_the_head_it_pushed)\|(Mirror_name_is_the_dispatchers_own_short_form)` | R-3 | all 20 listed methods, 20 results; 0 failed/skipped | 20 | 7 | true |
| CP-27 | S16 | `tests/Antiphon.Tests -> bin-c1033-27/` | portable-residue-compat | `/*/*/WorktreeResidueRecoveryTests/(C459_IntentCommittedBeforeGit)\|(C459_ComponentsAreIndependent)\|(C459_OutageKeepsFence)` | R-4 | all 3 listed methods, 3 results; 0 failed/skipped | 3 | 5 | true |
| CP-28 | S16 | `tests/Antiphon.Tests -> bin-c1033-28/` | portable-retirement-compat | `/*/*/(TaskWorktreeRetirementTests)\|(WorkspaceReservationLivenessTests)\|(WorktreeRetirementRaceTests)/(C459_ReleasedTerminalTasksRetire)\|(C459_TerminalRequired)\|(C459_SettlingFloor)\|(C459_ReleaseRequired)\|(C459_ReleaseSnapshotIsExact)\|(C459_HandoffDispositionRequired)\|(C459_ArtifactsPreserved)\|(C459_RevokeRespectsIntent)\|(C459_UniqueFullOwner)\|(C459_MutationIsExcluded)\|(C459_SourceLandingIsExcluded)\|(C459_RepairSourceIsExcluded)\|(C459_TargetSelectionIsExplicit)\|(C459_RecoveryDebtHolds)\|(C459_ReleaseIdentityIsUnique)\|(C459_LegacyRowsAreUnreleased)\|(C459_OwnProgressKeepsRelease)\|(C664_FreshRowBlocksWhateverTheOwner)\|(C664_LiveTaskOwnerBlocksAfterGrace)\|(C664_TerminalTaskOwnerDoesNotBlockAfterGrace)\|(C664_PendingLandKeepsTerminalTaskBlocking)\|(C664_LiveSessionOwnerBlocksAfterGrace)\|(C664_EndedSessionOwnerDoesNotBlockAfterGrace)\|(C664_MissingOrUnattributedOwnerDoesNotBlockAfterGrace)\|(C664_RetirementAndFenceRowsAlwaysBlock)\|(C459_OneRetirementClaim)\|(C459_CreateReservesWorkspace)` | R-2 | all 27 listed methods, 62 results; 0 failed/skipped | 62 | 8 | true |
| CP-29 | S16 | `tests/Antiphon.Tests -> bin-c1033-29/` | portable-artifact-compat | `/*/*/AgentReportStoreTests/(Exact_content_and_full_ids_own_distinct_files)\|(Root_selection_requires_a_durable_owned_location)\|(Ignore_and_tracking_are_checked_before_writing)\|(Ineffective_ignore_attempts_do_not_duplicate_the_exclude_rule)\|(Publication_exposes_only_a_complete_verified_report)\|(Paths_over_the_persisted_limit_are_unavailable)\|(Failures_and_cancellation_never_publish_a_bad_pointer)` | R-2 | all 7 listed methods, 28 results; 0 failed/skipped | 28 | 5 | true |
| CP-30 | S16 | `tests/Antiphon.Tests -> bin-c1033-30/` | windows-settled-before-composition | `/*/*/SettledWorktreeRemovalTests/(C459_InterfaceDefaultCannotDeleteSettledTask)\|(C459_PurposeCannotBorrowAuthority)\|(C459_CoordinatesMatchReceipt)\|(C459_ManagedRootRequired)\|(C459_NestedRegistrationHolds)\|(C459_RegistrationRequired)\|(C459_LockedRegistrationHolds)\|(C459_PrunableRegistrationHolds)\|(C459_RemoteContainmentRequired)\|(C459_DestinationIdentityRequired)\|(C459_RemoteRefreshBeforeDirectory)\|(C459_RemoteRefreshBeforeBranch)\|(C459_GenuineLeaseRequired)\|(C459_CommittedReceiptRequired)\|(C459_FinalAuthorityRequired)\|(C459_FirstContentInspectionRequired)\|(C459_FinalContentInspectionRequired)\|(C459_FirstIgnoredInspectionRequired)\|(C459_FinalIgnoredInspectionRequired)\|(C459_AbsenceNeedsOwnIntent)\|(C459_RecreatedTreeHolds)\|(C459_BranchPrecheckRequired)\|(C459_BranchDeleteUsesCas)\|(C459_NewCheckoutHoldsBranch)\|(C459_RemovalNeverForces)\|(C459_RetirementRefsCannotBorrowNamespaces)` | R-2 | all 26 listed methods, 78 results; 0 failed/skipped | 78 | 10 | true |
| CP-31 | S16 | `tests/Antiphon.SessionRunner.Tests -> bin-c1033-31/` | windows-runner-before-composition | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_GuardedOperationIsDistinct)\|(C1017_RunnerTaskIdentity)\|(C1017_RunnerAttemptIdentity)\|(C1017_RunnerStoreIdentity)\|(C1017_RunnerRepositoryIdentity)\|(C1017_RunnerPathIdentity)\|(C1017_RunnerBranchIdentity)\|(C1017_RunnerHeadIdentity)\|(C1017_RunnerPreservationProof)\|(C1017_RunnerDirtySource)\|(C1017_RunnerIgnoredPolicy)\|(C1017_RunnerNoFollow)\|(C1017_RunnerProcessOwnerHolds)\|(C1017_RunnerStartFence)\|(C1017_RunnerReadoptionFence)\|(C1017_RunnerIntentBeforeIo)\|(C1017_RunnerDigestReplay)\|(C1017_RunnerFreshFinalInspection)\|(C1017_RunnerNoForceBypass)\|(C1017_RunnerSourceLandingExcluded)\|(C1017_UnknownProcessOwnershipHolds)` | R-3 | all 21 listed methods, 21 results; 0 failed/skipped | 21 | 9 | true |
| CP-32 | S17 | `tests/Antiphon.Tests -> bin-c1033-32/` | portable-composition-api | `/*/*/(CardCleanupCompositionTests)\|(WorktreeResidueEndpointTests)\|(WorktreeResidueScriptTests)\|(WorktreeResidueRegistrationTests)/(C1033_UpgradeStaysReportOnly)\|(C1033_ExternalTransactionCannotWake)\|(C1033_StartupRecoversCommittedDone)\|(C459_PreviewCannotExecute)\|(C459_CallerScopeEnforced)\|(C459_ReleaseRejectsStaleApprovalSnapshot)\|(C459_RunResultSurvivesRestart)\|(C459_RunReportOmitsOpaqueContent)\|(C459_RunReportPageIsBounded)\|(C459_BatchRequiresFullIds)\|(C459_OneScheduler)\|(C459_DisabledSchedulerStaysOff)\|(C459_ScheduledWorkerExecutesBothLanes)` | V-7, R-4 | all 13 listed methods, 13 results; 0 failed/skipped | 13 | 8 | true |
| CP-33 | S17 | `tests/Antiphon.Tests -> bin-c1033-33/` | portable-classification | `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata` | R-5 | all 1 listed methods, 1 results; 0 failed/skipped | 1 | 3 | true |
| CP-34 | S17 | `tests/Antiphon.Tests -> bin-c1033-34/` | portable-final-policy | `/*/*/CardWorktreeCleanupRecoveryTests/(C1017_ExecuteFalseCannotDelete)\|(C1017_HistoricalCutoffCannotDelete)\|(C1017_SettlingFloorCannotDelete)\|(C1033_DirectAndRecoveryGatesHold)\|(C1033_PreviewDoesNotMutate)\|(C1033_UnknownOwnershipStaysHeld)` | V-1 | all 6 listed methods, 6 results; 0 failed/skipped | 6 | 7 | true |
| CP-35 | S17 | `tests/Antiphon.Tests -> bin-c1033-35/` | portable-final-scheduling | `/*/*/CardWorktreeCleanupRecoveryTests/(C1017_ActionBudgetIsShared)\|(C1033_ScopeAndDedupAreExact)\|(C1033_AdmissionPrecedesIo)\|(C1033_ClaimsDoNotHoldDbAcrossIo)\|(C1033_DiscoveryRemainsOpen)\|(C1017_CooldownSurvivesRestart)\|(C1017_PermanentRefusalAwaitsEvidence)\|(C1033_PermanentEvidenceIsComplete)` | V-2 | all 8 listed methods, 8 results; 0 failed/skipped | 8 | 12 | true |
| CP-36 | S17 | `tests/Antiphon.Tests -> bin-c1033-36/` | portable-final-recovery | `/*/*/CardWorktreeCleanupRecoveryTests/(C1017_IntentRecoveryDoesNotResetBudget)\|(C1017_ComponentFactsAreIndependent)\|(C1017_ResultSaveFailureRetainsDebt)\|(C1017_SetAsideRecoveryPreservesAuthority)\|(C1017_SetAsideIdentityMustMatch)\|(C1017_ReplacementPathIsUntouched)\|(C1017_AbsenceNeedsReceipt)\|(C1017_LostWakeupRecovers)\|(C1017_EnqueueFailureRemainsRecoverable)\|(C1017_PublicationIsImmutable)` | V-3, V-5 | all 10 listed methods, 10 results; 0 failed/skipped | 10 | 12 | true |
| CP-37 | S17 | `tests/Antiphon.Tests -> bin-c1033-37/` | portable-final-reporting | `/*/*/CardWorktreeCleanupAcceptanceTests/(C1017_OperatorOutcomeIsDurable)\|(C1017_EveryIntentIsReportable)\|(C1017_NoOpaqueBytesInReport)\|(C1017_ReportReadbackSurvivesDeletion)` | V-4 | all 4 listed methods, 4 results; 0 failed/skipped | 4 | 8 | true |
| CP-38 | S17 | `tests/Antiphon.Tests -> bin-c1033-38/` | portable-final-delivery | `/*/*/CardWorktreeCleanupAcceptanceTests/(C1017_DoneQueueBusyRecipient)\|(C1017_DoneQueueAlreadyEligible)\|(C1017_PublicationQueueBusyRecipient)\|(C1017_PublicationQueueAlreadyEligible)\|(C1017_PhoneHomeQueueBusyRecipient)\|(C1017_PhoneHomeQueueAlreadyEligible)` | V-5 | all 6 listed methods, 6 results; 0 failed/skipped | 6 | 14 | true |
| CP-39 | S17 | `tests/Antiphon.Tests -> bin-c1033-39/` | portable-final-local-crash | `/*/*/CardWorktreeCleanupAcceptanceTests/C1033_LocalCrashHandoffs` | V-5 | all 1 listed methods, 1 results; 0 failed/skipped | 1 | 12 | true |
| CP-40 | S17 | `tests/Antiphon.Tests -> bin-c1033-40/` | portable-final-full-crash | `/*/*/CardWorktreeCleanupAcceptanceTests/C1017_CrashEveryHandoff` | V-5 | all 1 listed methods, 1 results; 0 failed/skipped | 1 | 16 | true |
| CP-41 | S17 | `tests/Antiphon.Tests -> bin-c1033-41/` | portable-final-foundation | `/*/*/(CardDoneWorktreeCleanupTests)\|(CompletedCardWorktreeRemovalTests)\|(CardTaskSettlementTests)\|(ClosedCardSweepTests)/(C1017_OnlyDoneAuthorizes)\|(C1017_GenerationMustMatch)\|(C1017_TerminalTaskRequired)\|(C1017_UniqueOrdinaryOwner)\|(C1017_ReopenBeforeIntentRevokes)\|(C1017_IntentBeforeReopenKeepsFence)\|(C1017_ActiveConsumersHold)\|(C1017_RecoveryDebtHolds)\|(C1017_SnapshotAttemptIsFresh)\|(C1017_SnapshotReportIsFresh)\|(C1017_NoDbLockAcrossIo)\|(C1017_SharedAndBorrowedExcluded)\|(C1017_SourceLandingExcluded)\|(C1017_SlotAndCanonicalExcluded)\|(C1017_DiscoverPostDoneAttempts)\|(C1017_ExactCardBinding)\|(C1017_DuplicateDoneIsIdempotent)\|(C1017_DoneSaveIsAtomic)\|(C1017_EvidenceNeedsDoneDisposition)\|(C1017_DisposalFlagIsNotAuthority)\|(C1017_LeaseMustBeGenuine)\|(C1017_SourceShaMustMatch)\|(C1017_RepositoryMustMatch)\|(C1017_PathMustMatch)\|(C1017_BranchMustMatch)\|(C1017_CommonDirectoryMustMatch)\|(C1017_AdminDirectoryMustMatch)\|(C1017_DirtyTrackedIsHeld)\|(C1017_UntrackedSourceIsHeld)\|(C1017_UnpushedCommitIsHeld)\|(C1017_TaskBranchOnlyIsHeld)\|(C1017_RebaseMappingMustBeBound)\|(C1017_RemoteProofMustBeFresh)\|(C1017_ProtectedIgnoredIsHeld)\|(C1017_FinalContentIsRechecked)\|(C1017_EvidenceChurnIsHeld)\|(C1017_LinksNeverFollowed)\|(C1017_RootConfinementIsFresh)\|(C1017_NestedRegistrationIsHeld)\|(C1017_RegistrationMustExist)\|(C1017_LockedRegistrationIsHeld)\|(C1017_PrunableRegistrationIsHeld)\|(C1017_ReportMustBeExternal)\|(C1017_ReportDigestMustMatch)\|(C1017_TaskReportDetailMustSurvive)\|(C1017_DeliverablesMustBeExternal)\|(C1017_DeliverablesMustExist)\|(C1017_FinalAuthorityIsFresh)\|(C1017_BranchCasPreservesConcurrentRef)\|(C1017_OriginRefsSurvive)\|(C1017_NoImplicitResetOrForce)\|(C1017_MissingReportNeedsDisposition)\|(C1017_FirstIgnoredLinkIsHeld)\|(C1017_FinalIgnoredLinkIsHeld)\|(closing_a_card_cancels_its_blocked_task_with_the_close_reason)\|(closing_a_card_cancels_queued_and_unstarted_dispatched_tasks)\|(closing_a_card_leaves_started_tasks_running_with_one_warning_event)\|(closing_a_card_touches_no_other_cards_tasks_and_no_settled_task)\|(archiving_a_card_cancels_its_queued_task_with_the_archive_reason)\|(a_move_to_review_settles_nothing_and_a_reopen_resurrects_nothing)\|(a_task_settled_by_a_concurrent_writer_is_skipped_and_the_move_still_succeeds)\|(a_tracker_stale_close_cancels_the_cards_queued_task)\|(the_sweep_lists_open_tasks_on_closed_and_archived_cards_and_changes_nothing)\|(apply_cancels_only_unstarted_rows_created_before_the_close)` | R-1, R-2 | all 64 listed methods, 64 results; 0 failed/skipped | 64 | 9 | true |
| CP-42 | S17 | `tests/Antiphon.SessionRunner.Tests -> bin-c1033-42/` | portable-final-runner-guard | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_GuardedOperationIsDistinct)\|(C1017_RunnerTaskIdentity)\|(C1017_RunnerAttemptIdentity)\|(C1017_RunnerStoreIdentity)\|(C1017_RunnerRepositoryIdentity)\|(C1017_RunnerPathIdentity)\|(C1017_RunnerBranchIdentity)\|(C1017_RunnerHeadIdentity)\|(C1017_RunnerPreservationProof)\|(C1017_RunnerDirtySource)\|(C1017_RunnerIgnoredPolicy)\|(C1017_RunnerNoFollow)\|(C1017_RunnerProcessOwnerHolds)\|(C1017_RunnerStartFence)\|(C1017_RunnerReadoptionFence)\|(C1017_RunnerIntentBeforeIo)\|(C1017_RunnerDigestReplay)\|(C1017_RunnerFreshFinalInspection)\|(C1017_RunnerNoForceBypass)\|(C1017_RunnerSourceLandingExcluded)\|(C1017_UnknownProcessOwnershipHolds)` | R-3 | all 21 listed methods, 21 results; 0 failed/skipped | 21 | 7 | true |
| CP-43 | S17 | `tests/Antiphon.SessionRunner.Tests -> bin-c1033-43/` | portable-final-runner-legacy | `/*/*/(RunnerWorkspaceServiceTests)\|(PhoneHomeCommandDispatcherTests)/(Publish_pushes_only_own_fast_forward_branch)\|(Publish_refuses_a_rebased_tip_without_pushing)\|(Publish_equal_tip_is_not_pushed_and_reports_dirty_tree)\|(Publish_refuses_foreign_path_and_branch_before_git)\|(Publish_refuses_detached_head)\|(Publish_refuses_an_active_sequencer_before_push)\|(Publish_reports_rejected_non_fast_forward_without_exposing_origin)\|(Publish_classifies_a_tip_behind_the_advertised_remote)\|(Remove_refuses_committed_unpublished_tip_unless_forced)\|(Remove_allows_a_published_tip)\|(Remove_allows_a_tip_which_is_ancestor_of_published_sha)\|(Mirror_of_a_second_repository_clones_beside_the_primary_and_removes_through_its_own_checkout)\|(Mirror_refuses_a_repository_outside_the_allowed_clone_sources)\|(Mirror_refuses_credentialed_request_without_echoing_credentials)\|(Mirror_refuses_an_existing_checkout_whose_origin_is_another_repository)\|(Mirror_mismatch_does_not_echo_credentialed_origin)\|(Mirror_refuses_a_secondary_repository_the_push_credential_cannot_push_to)\|(Mirror_accepts_a_secondary_repository_when_push_dry_run_succeeds)\|(Mirror_creates_worktree_on_branch_at_sha)\|(Mirror_refuses_sha_mismatch)\|(Mirror_clones_repository_when_absent)\|(Mirror_failure_is_admission_error_not_crash)\|(Mirror_refuses_a_name_or_sha_it_did_not_shape)\|(Remove_refuses_dirty_tree)\|(Remove_refuses_a_path_outside_the_worktree_root)\|(Workspace_ops_are_admitted_only_under_allowed_cwd)\|(Workspace_mirror_naming_an_unadmitted_repository_is_refused_before_any_git_runs)\|(Workspace_publish_route_rejects_a_foreign_target_before_git)` | R-3 | all 28 listed methods, 28 results; 0 failed/skipped | 28 | 7 | true |
| CP-44 | S17 | `tests/Antiphon.Tests -> bin-c1033-44/` | portable-final-mirror | `/*/*/(CardWorktreeMirrorCleanupTests)\|(RemoteWorktreeMirrorTests)/(C1017_MirrorCompletesIndependently)\|(C1017_LocalProgressSurvivesOffline)\|(C1017_MirrorBehindPushedTip)\|(C1017_ReceiptMustMatchOperation)\|(C1017_ReceiptMustMatchEndpoint)\|(C1017_ReceiptMustMatchStore)\|(C1017_ReceiptMustMatchTaskAttempt)\|(C1017_AckIsNotRemoval)\|(C1017_UnsupportedNeverFallsBack)\|(C1017_ReopenBetweenEndpoints)\|(C1017_MultipleMirrorsRemainDistinct)\|(C1017_LostAckReusesOperation)\|(Push_reads_the_desktop_origin_and_the_mirror_request_carries_its_https_identity)\|(Push_refuses_when_the_desktop_origin_cannot_be_read)\|(Sync_fast_forwards_desktop_worktree)\|(Non_fast_forward_sync_is_refused)\|(Dirty_desktop_tree_is_never_fast_forwarded_over)\|(Push_failure_reports_and_does_not_invent_a_sha)\|(Push_reports_the_head_it_pushed)\|(Mirror_name_is_the_dispatchers_own_short_form)` | R-3 | all 20 listed methods, 20 results; 0 failed/skipped | 20 | 7 | true |
| CP-45 | S17 | `tests/Antiphon.Tests -> bin-c1033-45/` | portable-final-residue | `/*/*/WorktreeResidueRecoveryTests/(C459_IntentCommittedBeforeGit)\|(C459_ComponentsAreIndependent)\|(C459_OutageKeepsFence)` | R-4 | all 3 listed methods, 3 results; 0 failed/skipped | 3 | 5 | true |
| CP-46 | S17 | `tests/Antiphon.Tests -> bin-c1033-46/` | portable-final-schema | `/*/*/CardCleanupSchedulingSchemaTests/C1033_AdditiveUpgradePreservesHistory` | V-8 | all 1 listed methods, 1 results; 0 failed/skipped | 1 | 5 | true |
| CP-47 | S17 | `tests/Antiphon.Tests -> bin-c1033-47/` | windows-final-native | `/*/*/CompletedCardWorktreeRemovalWindowsTests/(C1017_LockedSetAsideRetries)\|(C1017_JunctionOutsideBytesSurvive)\|(C1017_LingeringPtyHostIsReported)\|(C1017_CleanupNeverKillsHolder)` | V-6 | all 4 listed methods, 4 results; 0 failed/skipped | 4 | 14 | true |
| CP-48 | S17 | `tests/Antiphon.Tests -> bin-c1033-48/` | windows-final-settled | `/*/*/SettledWorktreeRemovalTests/(C459_InterfaceDefaultCannotDeleteSettledTask)\|(C459_PurposeCannotBorrowAuthority)\|(C459_CoordinatesMatchReceipt)\|(C459_ManagedRootRequired)\|(C459_NestedRegistrationHolds)\|(C459_RegistrationRequired)\|(C459_LockedRegistrationHolds)\|(C459_PrunableRegistrationHolds)\|(C459_RemoteContainmentRequired)\|(C459_DestinationIdentityRequired)\|(C459_RemoteRefreshBeforeDirectory)\|(C459_RemoteRefreshBeforeBranch)\|(C459_GenuineLeaseRequired)\|(C459_CommittedReceiptRequired)\|(C459_FinalAuthorityRequired)\|(C459_FirstContentInspectionRequired)\|(C459_FinalContentInspectionRequired)\|(C459_FirstIgnoredInspectionRequired)\|(C459_FinalIgnoredInspectionRequired)\|(C459_AbsenceNeedsOwnIntent)\|(C459_RecreatedTreeHolds)\|(C459_BranchPrecheckRequired)\|(C459_BranchDeleteUsesCas)\|(C459_NewCheckoutHoldsBranch)\|(C459_RemovalNeverForces)\|(C459_RetirementRefsCannotBorrowNamespaces)` | R-2 | all 26 listed methods, 78 results; 0 failed/skipped | 78 | 10 | true |
| CP-49 | S17 | `tests/Antiphon.SessionRunner.Tests -> bin-c1033-49/` | windows-final-runner-native | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_GuardedOperationIsDistinct)\|(C1017_RunnerTaskIdentity)\|(C1017_RunnerAttemptIdentity)\|(C1017_RunnerStoreIdentity)\|(C1017_RunnerRepositoryIdentity)\|(C1017_RunnerPathIdentity)\|(C1017_RunnerBranchIdentity)\|(C1017_RunnerHeadIdentity)\|(C1017_RunnerPreservationProof)\|(C1017_RunnerDirtySource)\|(C1017_RunnerIgnoredPolicy)\|(C1017_RunnerNoFollow)\|(C1017_RunnerProcessOwnerHolds)\|(C1017_RunnerStartFence)\|(C1017_RunnerReadoptionFence)\|(C1017_RunnerIntentBeforeIo)\|(C1017_RunnerDigestReplay)\|(C1017_RunnerFreshFinalInspection)\|(C1017_RunnerNoForceBypass)\|(C1017_RunnerSourceLandingExcluded)\|(C1017_UnknownProcessOwnershipHolds)` | R-3 | all 21 listed methods, 21 results; 0 failed/skipped | 21 | 9 | true |
| CP-50 | S17 | `tests/Antiphon.Tests -> bin-c1033-50/` | windows-final-local-mirror | `/*/*/(CompletedCardWorktreeRemovalTests)\|(CardWorktreeMirrorCleanupTests)/(C1017_EvidenceNeedsDoneDisposition)\|(C1017_DisposalFlagIsNotAuthority)\|(C1017_LeaseMustBeGenuine)\|(C1017_SourceShaMustMatch)\|(C1017_RepositoryMustMatch)\|(C1017_PathMustMatch)\|(C1017_BranchMustMatch)\|(C1017_CommonDirectoryMustMatch)\|(C1017_AdminDirectoryMustMatch)\|(C1017_DirtyTrackedIsHeld)\|(C1017_UntrackedSourceIsHeld)\|(C1017_UnpushedCommitIsHeld)\|(C1017_TaskBranchOnlyIsHeld)\|(C1017_RebaseMappingMustBeBound)\|(C1017_RemoteProofMustBeFresh)\|(C1017_ProtectedIgnoredIsHeld)\|(C1017_FinalContentIsRechecked)\|(C1017_EvidenceChurnIsHeld)\|(C1017_LinksNeverFollowed)\|(C1017_RootConfinementIsFresh)\|(C1017_NestedRegistrationIsHeld)\|(C1017_RegistrationMustExist)\|(C1017_LockedRegistrationIsHeld)\|(C1017_PrunableRegistrationIsHeld)\|(C1017_ReportMustBeExternal)\|(C1017_ReportDigestMustMatch)\|(C1017_TaskReportDetailMustSurvive)\|(C1017_DeliverablesMustBeExternal)\|(C1017_DeliverablesMustExist)\|(C1017_FinalAuthorityIsFresh)\|(C1017_BranchCasPreservesConcurrentRef)\|(C1017_OriginRefsSurvive)\|(C1017_NoImplicitResetOrForce)\|(C1017_MissingReportNeedsDisposition)\|(C1017_FirstIgnoredLinkIsHeld)\|(C1017_FinalIgnoredLinkIsHeld)\|(C1017_MirrorCompletesIndependently)\|(C1017_LocalProgressSurvivesOffline)\|(C1017_MirrorBehindPushedTip)\|(C1017_ReceiptMustMatchOperation)\|(C1017_ReceiptMustMatchEndpoint)\|(C1017_ReceiptMustMatchStore)\|(C1017_ReceiptMustMatchTaskAttempt)\|(C1017_AckIsNotRemoval)\|(C1017_UnsupportedNeverFallsBack)\|(C1017_ReopenBetweenEndpoints)\|(C1017_MultipleMirrorsRemainDistinct)\|(C1017_LostAckReusesOperation)` | R-2, R-3 | all 48 listed methods, 48 results; 0 failed/skipped | 48 | 10 | true |

### Cost

All figures are estimated wall-clock minutes, not measurements. Row estimates
include a 2-minute portable / 3-minute Windows isolated build; the remainder is
fixture startup and the exact filter run. Build-slot queue delay and unexpected
reruns are additional, reported time. Infrastructure/package setup allowance is
10 portable + 15 Windows = **25 minutes**, once per qualification environment.

- Ordinary Code V/R floor = sum CP EstimatedMinutes = **401 minutes**:
  **108 build + 293 fixture/test minutes**, across 50 exact filters above.
  Min total is **770 executed results**, including deliberate repeated
  qualification rows; it is not 770 different tests or an elapsed-time estimate.
- CARD-1033-owned Mutation floor = **1376.8 minutes** for
  **118 controls**, separate from Code. Imported external controls =
  **653.6 minutes** for 86 controls. Full retained inventory
  floor = **2030.4 minutes** for 204 controls. All are pending.
- A PC estimate includes a fresh method-scoped baseline green, one compiling
  red mutation, exact restoration plus restore-green: three isolated builds,
  three runs of its literal filter, and 0.1 minute restoration/source check.
  No class/suite execution is part of a PC. Build failure/zero tests is not red.
  The filter for each costed PC is the exact method printed in its PC row.

| Detecting class / filter family (exact methods in PC table) | Controls (owned / inherited) | Minutes per baseline/red/restore-green run including build | Restore overhead per PC | Total minutes |
|---|---:|---:|---:|---:|
| CardDoneWorktreeCleanupTests | 5 / 17 | 2.5 | 0.1 | 167.2 |
| CompletedCardWorktreeRemovalTests | 0 / 36 | 2.5 | 0.1 | 273.6 |
| CompletedCardWorkspaceRemovalTests | 0 / 21 | 2.5 | 0.1 | 159.6 |
| CardWorktreeMirrorCleanupTests | 0 / 12 | 2.5 | 0.1 | 91.2 |
| CardWorktreeCleanupRecoveryTests | 81 / 0 | 3.5 | 0.1 | 858.6 |
| CardWorktreeCleanupAcceptanceTests (local crash 12; full crash 16; other methods 3.5) | 19 / 0 | 3.5 or 12 or 16 | 0.1 | 315.4 |
| CompletedCardWorktreeRemovalWindowsTests | 4 / 0 | 6 | 0.1 | 72.4 |
| CardCleanupCompositionTests | 7 / 0 | 3.5 | 0.1 | 74.2 |
| CardCleanupSchedulingSchemaTests | 2 / 0 | 3 | 0.1 | 18.2 |

CARD-1033 incremental total = setup 25 + ordinary 401 + owned PC
1376.8 = **1802.8 minutes**. If no imported controls have
qualified evidence, the retained end-to-end total is setup 25 + ordinary 401
+ all PC 2030.4 = **2456.4 minutes**. Separate qualification
lanes may run on separately owned committed checkouts; no concurrent Antiphon.Tests
and Antiphon.Agents.Pty.Tests run is commissioned. No Mutation work starts while
its pause remains in force, and pending controls cannot authorize activation.

Method scoping replaces a hypothetical 10-minute K whole-class run with a
1.5-minute exact K method run: across 81 K controls and three
runs/control, estimated saved test time is **2065.5 minutes**.
This comparison excludes unchanged build cost and is not a measured speedup.
No independent-mutation batching savings are assumed (0): controls often share
executor, admission and recovery files; batching requires Code-bound independence.
The ordinary floor exceeds the prior proposal because it includes fresh builds
and the final composed-SHA repeats explicitly; do not remove a V/R/native row to
fit one Code dispatch's time budget. Select only that slice's rows per dispatch.

#### Handoff audit

Guards=204, mapped=204, missing=0, duplicate PC maps=0.
All 204 controls have a named compiling-defect recipe and a decisive exact-method
red assertion; **executed PCs=0, pending PCs=204**, as the brief requires.
Existing-test source census, static table/ID/coverage arithmetic and read-only
prebuilt manifest import/validation are the validation performed here. New payloads, S3 declarations and assertion labels
remain **binding required after Code**; Review must resolve those bindings before
accepting execution claims. Mutation must subsequently run each bound control
break/red/restore/green after land when separately commissioned.

Next is Code **S1 only**: the three local policy methods, fixture test-only opt-in,
and CP-1..3. Keep Execute=false and CardDoneAfterUtc=null in shipped configuration.
No operator choice is needed for that slice. The cutoff/cohort/activation decision
remains explicit later work. A semantic S3 mismatch returns to Plan at that seam.

#### Read-only manifest validation evidence

The compiled importer and validator were invoked directly from an owned scratch
copy of a pre-existing tool assembly; no compilation, test discovery or test run
was launched. Its source importer/validator blobs match this checkout:
`c08051d12a57f046f8d935e9cd66c5d56549095f` and
`d8da8c209360d65d14907614689691aafbaa2b10`. Assembly SHA256:
`71655b38818a3a0169cc0dab180c0fb774bd2cba3914d441452f682b748f851a`.
The source copy was from task-07c7df01 bin-c1192-s1r2; it was not that task
executing this work. A local foreground PowerShell child loaded the owned copy
and called PlanTableImporter.ImportFile and ManifestValidator.Validate for both
OS arguments. This is parsing evidence, not a source-qualified build receipt.

```text
isWindows=False rows=50 builds=50 Min=770 minutes=401 warnings=2
isWindows=True rows=50 builds=50 Min=770 minutes=401 warnings=2
CP-20: derived row timeout (48 minutes) exceeds the 45 minute advisory threshold; consider splitting the row (timeout is not capped)
CP-40: derived row timeout (48 minutes) exceeds the 45 minute advisory threshold; consider splitting the row (timeout is not capped)
```

The two advisories are the incremental/final executions of the same required
C1017_CrashEveryHandoff method. Each estimates 16 minutes including its build;
48 minutes is a derived ceiling, not expected duration. Retain its single parent
method and complete cut roster, dispatch these rows separately, and await the
owned checkpoint run. The local crash subset already has its own 12-minute row.
Lowering the estimate or dropping cuts to silence the advisory is not authorized.
Static validation also counted 204 unique guard/control pairs, 261 distinct
selected methods, and confirmed that each PC method occurs in the ordinary union.
