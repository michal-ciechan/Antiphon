# CARD-1281 cheap-slice checkpoint manifest

This is the importable copy of the checkpoint rows in the [verification design](2026-10-15-card-1281-pipeline-speed-plan.md#verification-design) and its Plan binding follow-up. The parent retains an earlier planning inventory; its first Checkpoints heading is not executable. Keep each row identical to its parent entry.

Status: **B-1/B-2/B-3 design-bound; dependent verification/integration pending**. Follow the parent's [binding follow-up](2026-10-15-card-1281-pipeline-speed-plan.md#plan-binding-follow-up-2026-10-15-task-024ec8fb). S2-policy and S5 portable preflight can start independently; full S2/S3/S4 wait for the shared integration prerequisite and focused verification binding. S5 is not live qualification. Proposed C1281 methods do not exist yet; the parent's original 64 PCs remain pending, with new integration coverage to bind.

Run one committed slice group at a time with the checkpoint tool and the host build-slot gate. Use `--after S2`, `--after S3`, `--after S4` or `--after S5`; never run the whole manifest against a partially implemented slice. Reuse builds only within that group. Source must be committed and frozen, with exact SHA receipts. Observe actual result rosters; Min counts TUnit executions, not internal matrix/assertion rows.

For the independently commissionable policy subset use `--after S2-policy` (CP-12/CP-13). Its five methods are V-1/V-4/V-5/V-6/V-7; no receipt-correction stubs. All rows use the portable application/database/script lane; live Windows RC qualification stays separate.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1281-a/` | advisory-unit | `/*/*/*/*[Category=Unit]` | R-1 | >=1 executed; full Unit roster, 0 failed/skipped | 1 | 7 |
| CP-2 | S2 | CP-1 | advisory-acceptance | `/*/*/(AdvisoryChangePolicyTests*)\|(StageOutcomeSummaryTests*)/*` | V-1,V-2,V-3,V-4,V-5,V-6,V-7,R-1 | A 7 + summary 5 methods, all variants, 0 failed/skipped | 12 | 6 |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1281-b/` | handoff-unit | `/*/*/*/*[Category=Unit]` | R-2 | >=1 executed; full Unit roster, 0 failed/skipped | 1 | 7 |
| CP-4 | S3 | CP-3 | handoff-acceptance | `/*/*/PipelineHandoffPilotTests/*` | V-8,V-9,V-10,V-11,R-2 | 12 executions, every cut reached, 0 failed/skipped | 12 | 12 |
| CP-5 | S4 | `tests/Antiphon.Tests -> bin-c1281-c/` | close-unit | `/*/*/*/*[Category=Unit]` | R-3 | >=1 executed; full Unit roster, 0 failed/skipped | 1 | 7 |
| CP-6 | S4 | CP-5 | close-acceptance | `/*/*/(PublishedCardCompletionTests*)\|(CardWorkTransitionServiceTests*)/*` | V-12,V-13,V-14,V-15,V-16,V-17,V-18,R-3 | C 7 + transition 13 methods, all variants, 0 failed/skipped | 20 | 8 |
| CP-7 | S5 | `tests/Antiphon.Tests -> bin-c1281-d/` | qualification-admission | `/*/*/ReleaseGateActivationTests/(C599_Admission*)\|(C599_QueuedReadiness*)` | V-19,R-4 | 2 methods, every negative and valid control, 0 failed/skipped | 2 | 6 |
| CP-8 | S5 | CP-7 | qualification-credit | `/*/*/ReleaseGateRunTests/C599_CreditVerdicts` | V-20,R-4 | 1 execution; 24 named harness assertions, 0 failed/skipped | 1 | 2 |
| CP-9 | S5 | CP-7 | qualification-baseline | `/*/*/InterimVerificationPolicyTests/C544_BaselineCompletion` | V-21,R-4 | 1 execution, Found Full control admits, 0 failed/skipped | 1 | 3 |
| CP-10 | S5 | CP-7 | qualification-final | `/*/*/InterimVerificationLandGuardTests/C544_FinalFullRequired` | V-21,R-4 | 1 execution, all scope variants, 0 failed/skipped | 1 | 3 |
| CP-11 | S5 | CP-7 | qualification-watchdog | `/*/*/NightlyWatchdogCoreTests/(C545_IntentBeforeNetwork*)\|(C545_ReceiptIdempotent*)\|(C545_ReaderHeldWithoutSession*)` | R-4 | 3 executions, all assertion rows, 0 failed/skipped | 3 | 2 |
| CP-12 | S2-policy | `tests/Antiphon.Tests -> bin-c1281-a-policy/` | advisory-policy-unit | `/*/*/*/*[Category=Unit]` | R-1 | >=1 executed; full Unit roster, 0 failed/skipped | 1 | 7 |
| CP-13 | S2-policy | CP-12 | advisory-policy-acceptance | `/*/*/(AdvisoryChangePolicyTests*)\|(StageOutcomeSummaryTests*)/*` | V-1,V-4,V-5,V-6,V-7,R-1 | A policy 5 + summary 5 methods, all variants, 0 failed/skipped | 10 | 6 |

### Cost

Estimated ordinary Code floor: 63 minutes (S2 13, S3 19, S4 15, S5 16), including 16 minutes build/setup. The parent's 453-minute Mutation floor and live qualification budget are separate; these rows run no PCs or live operations.

Landing S2-policy first adds 13 estimated minutes before the later full S2 run (76 total for these component checkpoints). Shared-integration verification and additional controls remain to be costed by focused TestDesign; 76 is not a complete program estimate.

## Shared bindings B-1/B-2/B-3 (TestDesign, 2026-10-15, task 598a38d5)

This appendix supplements the authoritative [binding decisions](2026-10-15-card-1281-pipeline-speed-plan.md#binding-decisions-under-d-2-d-3-and-d-4); it changes neither the fix design nor CP-1..CP-13. Inspected base: `179044a46d1f20625a91a91af84615b45f81289e`. D-1..D-12 remain approved. New method names below are implementation contracts, not passing tests.

**Dispatch order and exclusive scope.** First commission **S0-foundation**, then land it: the five entities `ReviewEvidenceCorrection`, `PipelineHandoffAction`, `PipelineHandoffObservation`, `CardCompletionAcceptance`, `CardCompletionReceipt` under `server/Domain/Entities/`; `server/Infrastructure/Data/AppDbContext.cs`; CLI-generated `server/Migrations/*` and snapshot; optional handoff-action link and card-lock hooks in `server/Application/Services/AgentTaskService.cs`; new `tests/Antiphon.Tests/Application/PipelineSpeedIntegrationTests.cs`, including its nested fixture. No foundation dependency on future component services, and no new endpoint registration yet.

After S0 lands, commission the parent's B-1 receipt-correction slice (S2), B-2 caller pilot (S3/D-3), and B-3 acceptance/close (S4/D-4) in their already assigned disjoint component scopes. B-1 waits for the active D-2 policy author **1904d993** to land before touching `AdvisoryChangePolicyTests.cs` or its policy contracts. B-2 and B-3 can proceed independently against S0; neither requires the other's component. All shared entity/model/admission/test-file changes return to the sequential integration owner. D-5 task **18880da1**, qualification files/scripts and the existing shared fixtures remain outside these writes.

Finally commission **S6-wiring**, after all three components land: `server/Program.cs` registrations plus the same integration test file/nested fixture. It consumes the actual endpoint extensions and services; component repairs return to their owners. S6 does not enable automatic close: D-4 still owes the 30-event shadow gate. No task consumes unlanded source. S0 may start Code now; the component recipes below and the parent's CP-1..CP-6 remain mandatory, followed by S6. New artifacts/payload locations and compiling mutation lines must be bound to Code's actual output; an unbound payload remains **binding required after Code**.

## Verification design

IDs continue the parent census: V-22..V-37, R-5..R-8, G-65..G-121 and PC-65..PC-121. Parent V/R and PC-1..PC-64 remain required in their original slices; the new rows do not replace them.

### Inspection

| Bodies read | Boundaries -> coverage or exclusion |
|---|---|
| Parent verification appendix/binding follow-up; checkpoint manifest; project, lifecycle, orchestration and session delivery owners; testing checkpoint schema/coverage contract | Scope/order, V-22..V-37; existing delivery/close safeguards carried below |
| `ReviewEvidenceRecoveryEndpointTests` (all methods and Factory/credential helpers), `ReviewRecoveryWorld`, recovery prepared/lock/rollback tests; `ReviewEvidenceRecoveryService.RebindAsync` and task-lock helper; rebind endpoint body; `LandApproval.LoadUsableEvidenceAsync` | V-27..V-29, R-6: real authentication and fresh reads, current predecessor under Review lock. Recovery is not receipt correction |
| `AgentTaskService.CreateAsync` admission transaction through post-commit event; `C544World.BuildServices/CreateTaskAsync/RestartAsync/DispatchAsync`; `C544DeliveryRig` creation, submit callback, scan/flush/restart and fault interceptor; `CompletionWarningDeliveryTests` and `VerificationRoundDeliveryTests.AssertReceivedOnceAsync` | V-25/V-30..V-32/V-36/V-37, R-7; DispatchAsync manually binds a fixture session and does not prove real launch |
| `CardWorkTransitionServiceTests` dispatch/settle/human-precedence cases and nested harness; `CardService.ApplyAutomatedMoveAsync`; `CardWorkflowRun`; `CardWorkflowRunFactoryTests`; EF run/notification mappings | V-24/V-26/V-33/V-34/V-35, R-5/R-8; old automated move does not preserve proposal identity |
| `PipelineDefinitionMigrationTests` upgrade, unique-index, immutable-revision bodies and LegacyFixture; `TestDbFixture`; `AntiphonWebAppFactory` | V-22..V-24: actual migrated PostgreSQL, fresh contexts, isolated cloned database; real Program retains refusing runner and owned operator token |
| `LandingGitFixture.InitializeAsync/RequiredAsync`; `AgentTaskLandPublicationTests.C494_LostPushAckReconcilesRealTargetBeforeSourceChecks` | V-34/V-36 and parent V-13: private bare remote plus independent clone; fixture publication is not deployment |
| `validate-checkpoint-receipt.ps1`; `RunCheckpointSourceScriptTests.C835_ReceiptValidation`, Fixture.RunAsync/ValidateAsync; `CheckpointSourceExecutionTests` driver/receipt examples; `ScriptHarness`; `PlanTableImporter.ExtractSection` | V-29: real validator invocation; shim-written TRX is only a parser fixture. First-table-only import requires extraction below |

### Delivery inventory

| Durable identity | Producer -> destination / persistence | Recovery and recipient oracle |
|---|---|---|
| B-2 predecessor/attempt/settlement event -> notification ID -> queue ID/session/generation/attempt floor/wire digest -> action ID | Actual reply settlement commits completion obligation; notification scanner enqueues into real SessionMessageQueueService; caller receives completion, then explicitly POSTs consumption | Parent V-10 plus V-36/V-37 run busy-until-TurnEnd and already eligible recipients. Cut obligation insert, settlement commit, queue insert/commit, dropped wakeup, render commit, spill write, attempt commit and prompt acceptance; assert each cut fired, restart services and recover the same IDs. Require exactly one complete matching UserPrompt above the attempt floor, matching session/generation; compare frozen wire and spill digests. G-21..G-25/G-53/G-54 retain PC-21..PC-25/PC-53/PC-54 |
| B-2 action natural key/request digest -> child task ID | Explicit authenticated consumption/hold -> durable action/observation; RequestChildAsync -> real CreateAsync inserts child and link in one admission transaction | V-25/V-31/V-32/V-36: cut after consumption, before insertion, between child/link writes, after commit before response, and at post-commit AgentTaskChanged failure. Restart and scoped GET must return the actual same child or durable pending/refusal history. No delivery-derived consumption time, unkeyed retry or duplicate child. G-69/G-70/G-71/G-72/G-73/G-117 and parent G-26/G-49 |
| B-1 predecessor/report/selection/bundle digest -> correction/audit/successor | Authorized POST imports evidence, then commits correction and optional approval; recipient is the authorized API reader and LandApproval | V-28/V-29: fail import before durable retention, fail each DB handoff, lose committed response; recreate services, repeat POST and read actual stored correction/active outcome. Landing consumer must resolve its correction link. No new asynchronous session delivery; no fabricated Review notification |
| B-3 card/run/acceptance revision + Code owner/C/Review/O/L -> receipt/move revision | Acceptance PUT -> immutable revision; confirmed publication plus proposal -> transactional card move/dequeue/receipt; CardChanged follows commit | V-34/V-35: cut before save/commit, after commit before response, and event publication failure. Restart; GET acceptance and card plus fresh receipt/move reads show one correlated Done revision, or none after rollback. Never credit CardChanged as receipt; missed invalidation is recovered by GET. No new session input or guaranteed SignalR replay |

The session adapter is a declared substitute: C544DeliveryRig records **the actual submitted bytes** as UserPrompt through its OnSubmitted callback. It proves the real server queue-to-transcript contract, not native provider ingestion or model reasoning. Never insert expected success text without a submit. Truncated text, old sequence, foreign session and old generation are negative cases, not success shortcuts. MockEventBus proves commit ordering only, not browser receipt. B-2 ends at durable child admission; child launch/brief transport is unchanged and has no new delivery claim. B-3 ends at fresh authorized GET of durable state. A request, queue insert, event, Sent flag or ack proves none of these receipts.

### Proves it works now

**Recipe and census.** All new methods belong to `PipelineSpeedIntegrationTests` in the file above, Category Integration, with `ParallelLimiter<ProcessSpawnLimit>` and global `NotInParallel` for the card/sweep fixture. Use a per-test cloned PostgreSQL database, private Git roots, fresh service providers on restart, and the factory's production-runner refusal. S0 tests invoke the actual admission hooks without future services. S6 boots real Program and must not add missing production registrations/endpoints in test overrides. Use nested fixture/interceptors; leave shared helpers unchanged.

For each method, start with a valid control then change one named dimension. Assert named boundary/barrier entry, release and join every contender in finally, and query from a separate context. A timeout, fixture failure or SQL/build error outside the intended assertion is not red. All new V rows are **new safety acceptance, red-first** (not a claim of a historical regression). Commit compiling failing tests before fixes; unavailable API can yield the asserted HTTP 404-versus-success red, schema tests can use raw catalog queries, and admission tests may follow a compile-only hook introduction. No constant-success stubs. R rows are existing behavior. Internal variants count once; only V-37 has nine Arguments executions.

| ID / behavior | Recipe / exact method | Variant / case -> oracle and reachability |
|---|---|---|
| V-22 B-1 schema | Migrated DB; `PipelineSpeedIntegrationTests.C1281_B1Schema` | Upgrade a populated pre-foundation database; retain old report/outcome/cost bytes. Persist and reload every correction coordinate/digest/validation/actor/time/successor field. Duplicate predecessor+canonical bundle refuses at DB unique constraint; another bundle persists. Application mutation/deletion of the immutable correction refuses. Assert no pending migration/model drift |
| V-23 B-2 schema | Migrated DB; `PipelineSpeedIntegrationTests.C1281_B2Schema` | Persist action plus two observations, restart/read ordered history. Exact natural-key duplicate refuses; vary attempt, settlement event, next stage, run ID and each workflow-fence component independently and retain distinct keys. Observation mutation/deletion refuses. Child link is nullable while pending; clocks remain individually nullable |
| V-24 B-3 schema | Migrated DB; `PipelineSpeedIntegrationTests.C1281_B3Schema` | Persist complete acceptance items/deployment evidence/category decision/companion and receipt bindings; reload exact payload. Duplicate card+acceptance revision and duplicate card+O+acceptance receipt refuse independently; next revision persists; acceptance mutation/deletion refuses. Readiness can store unset O/L; that row cannot close (parent V-13/V-14) |
| V-25 B-2 admission transaction | C544World plus hook; `PipelineSpeedIntegrationTests.C1281_B2AtomicChild` | Seed durable pending action; real CreateAsync with action ID. Race two callers, cut between child and link persistence and before commit: both absent or both present, never an orphan. Lost committed response/restart returns same child. Changed request digest refuses; same-key replay creates exactly one child. Reach actual admission even with optional open-gate service absent |
| V-26 B-3 admission fence | Two DB connections; `PipelineSpeedIntegrationTests.C1281_B3AdmissionFence` | Hold card lock as close writer; real implementation CreateAsync must block, then recheck a committed Done/revised token and refuse. Opposite order: queued insertion commits first and close must refuse without canceling it. Observe attempted lock and NOWAIT contention, not elapsed sleeps. Ordinary unbound admission and explicitly commissioned post-Done Mutation retain their existing admission behavior |
| V-27 B-1 authorization | Actual POST; `PipelineSpeedIntegrationTests.C1281_B1Authorization` | Anonymous/invalid/revoked credential, Worker, wrong project, repository sibling, foreign board/card each refuse with zero correction/audit/successor. Operator, scoped orchestrator task/session and capability controls succeed. Forged actor never enters stored evidence; actor derives from principal |
| V-28 B-1 correction transaction | Actual POST + LandApproval; `PipelineSpeedIntegrationTests.C1281_B1CorrectionTransaction` | Report changed after prepare, predecessor superseded under Review lock, substantive or unclassified legacy Found each refuses. Concurrent duplicate/import retry and restart return one correction/audit/successor; pre-audit/save/commit cuts roll all back. Historical report/outcome/cost unchanged. Tamper/remove correction link: actual landing consumer refuses, while valid typed behavior + Full proof admits |
| V-29 B-1 receipt import | Actual validator process/retained storage; `PipelineSpeedIntegrationTests.C1281_B1ReceiptImport` | Valid exact commissioned rows/source; independently absent/failed run, missing TRX/provenance, altered blob digest, wrong producer, changed selection commit/section/rows, wrong subject/SHA/ref/repository. Refusal returns exactly missing selection and starts zero Reviews. Delete caller copy after success/restart: retained bytes and validation remain usable. Observe actual validator argv and retained content digests |
| V-30 B-2 authorization | Actual POST and GET; `PipelineSpeedIntegrationTests.C1281_B2Authorization` | V-27 principal/scope/actor matrix for both routes, with an existing foreign action for GET. Denied GET leaks no history/child/body; denied POST writes nothing. Allowed GET sees same principal-bound action and actual child after restart |
| V-31 B-2 consumption/history | Actual RecordAsync POST/GET; `PipelineSpeedIntegrationTests.C1281_B2ObservationReadback` | Delivered completion with no explicit call has no consumption. POST records server time, reason/next observation and separate nullable latency clocks. Wrong note/queue/session/generation/digest association refuses. Crash after consumption or held write/restart retains history. GET joins actual child dispatch time, never copies receipt/consumption into missing clocks |
| V-32 B-2 refusal/recovery | Actual RequestChildAsync HTTP; `PipelineSpeedIntegrationTests.C1281_B2HttpChildRecovery` | Parent V-9 admission matrix reaches real create checks; classified refusal survives a separate transaction and acknowledges no child. Duplicate/lost response resolves through scoped GET. Throw AgentTaskChanged after committed creation: retry yields same child and one link. Stale workflow tuple and changed request digest refuse (parent V-11) |
| V-33 B-3 authorization | Actual PUT/GET/complete POST; `PipelineSpeedIntegrationTests.C1281_B3Authorization` | V-27 principal/scope/actor matrix on all three verbs. A body naming another card cannot redirect the path target. Denial leaves acceptance/move/receipt unchanged and GET exposes no foreign evidence. Every successful item actor/time comes from authenticated server context |
| V-34 B-3 atomic close | Actual CompletePublishedAsync and private publication; `PipelineSpeedIntegrationTests.C1281_B3AtomicClose` | Parent V-13..V-18 predicates plus barriers after proposal: human move/reopen, acceptance writer, queued task, Review override. Both contender orders; changed token/run/acceptance/policy decision refuses. Lock Review evidence with its real writers in a consistent order. Cut each move/dequeue/receipt handoff and commit/response; one receipt and one Done move or total rollback. Event observer's fresh connection sees committed rows; event failure/restart GET recovers same close |
| V-35 B-3 acceptance writer | Actual PUT/GET; `PipelineSpeedIntegrationTests.C1281_B3AcceptanceRevision` | Competing expected revisions: one append and one conflict; no in-place overwrite. Shared card lock serializes with close. Missing record, pending acceptance, pending deployment, wrong L/build/version, absent explicit-empty category decision and foreign companion refuse close independently through parent V-14. Prepublication ready-for-release remains Review; later O/L requires a new acceptance revision |
| V-36 actual wiring/receipt/consumption | Real Program + queue adapter; `PipelineSpeedIntegrationTests.C1281_WiredRoundtrip` | Resolve registered validator/services; actual routes round-trip B-1 correction and B-3 acceptance/close. Produce B-2 completion through reply service, both busy and already eligible; complete matching UserPrompt precedes explicit consumption, then POST/GET reports same child. No test-only route/service mapping and zero production-runner starts |
| V-37 delivery recovery through caller action | Same queue/HTTP composition; `PipelineSpeedIntegrationTests.C1281_WiredDeliveryRecovery` | Nine Arguments: obligation-insert, settled-committed, note-insert, note-committed, wakeup-dropped, render-committed, spill-written, attempt-committed, prompt-accepted. Each loops busy/eligible and inline/spill. Assert cut reached, full frozen prompt exactly once, scoped action receipt association and one child after restart; reject clipped/pre-floor/foreign-session/old-generation evidence |

**Receipt fixture boundary (V-29).** Reuse C835's serialized receipt shapes and actual validator invocation for adversarial parser cases, declaring its shim/TRX substitute. Those bytes cannot establish that execution happened. The positive producer-provenance arm must import an unedited, clean, actually executed checkpoint pack from the commissioned Code source/selection, with its producer record and all referenced TRX/build/source files; retain its original source/repository identity in the seeded Review and subject, rather than relabeling it to a scratch SHA. S0's committed CP-14..CP-16 pack is the first available producer input. Its paths/digests/producer identity are a **Code-output binding obligation**, supplied to the slice-local fixture in ignored input storage, never checked in or inferred from exit 0. Capture the exact pack in the Code handoff before S2/S6; missing material makes this arm incomplete, never silently a synthetic green. This is fixture provenance, not permission for another build or Review. Parent V-2/V-3 additionally run the component's actual correction route/validator/land consumer.

### Guards the regression

- R-5: S0 admission/schema changes preserve workflow snapshots and Review locking: `CardWorkflowRunFactoryTests.CreateFromRevision_snapshots_every_stage_in_order_with_queued_status_and_first_stage_pointer` retains exact revision/first stage; `ReviewEvidenceRecoveryTests.C1043_LockOrder` retains sorted Review/subject lock observations. V-22..V-26 preserve preexisting rows and no-action admission.
- R-6: `ReviewEvidenceRecoveryEndpointTests.C1043_ResponseAfterCommit` retains authenticated actor, fresh committed successor and idempotent retry. V-28/V-29 and parent V-2/V-3 retain correction-specific identity/history safeguards; correction never broadens recovery/override authority.
- R-7: V-25/V-30/V-31/V-32/V-36/V-37 plus parent V-8..V-11 retain current key, actual refusal checks and queue-to-complete-UserPrompt evidence. No automatic dispatcher or delivery timestamp used as caller action.
- R-8: `CardWorkTransitionServiceTests.a_settled_task_with_nothing_else_open_moves_the_card_to_review_and_dequeues_it` retains Review, cleared assignment/queue and original move reason. V-24/V-26/V-33..V-36 plus parent V-12..V-18 retain no premature Done, human precedence, queued-task preservation and post-Done Mutation.

### Guard inventory

Parent guards remain in their own 1:1 maps. The following are the additional schema, authorization and transaction guards. Each new row maps to one distinct PC; none is exempt. A logical guard used by several routes must be tested on every route. If Code implements independently bypassable copies, split the row and add distinct PCs/cost before declaring the pack bound; a broad authorization test alone does not discharge those copies.

| Guard | Plan reference / invariant | Control |
|---|---|---|
| G-65 | B-1 correction natural-key uniqueness | PC-65 |
| G-66 | B-1 correction append-only custody | PC-66 |
| G-67 | B-2 complete natural-key uniqueness | PC-67 |
| G-68 | B-2 observations append-only | PC-68 |
| G-69 | B-2 child and action link commit together | PC-69 |
| G-70 | B-2 same key with changed request refuses | PC-70 |
| G-71 | B-2 consumption requires explicit caller action | PC-71 |
| G-72 | B-2 distinct server clocks, unknown stays unknown | PC-72 |
| G-73 | B-2 receipt association belongs to this action/destination | PC-73 |
| G-74 | B-3 acceptance revision uniqueness | PC-74 |
| G-75 | B-3 acceptance append-only | PC-75 |
| G-76 | B-3 close receipt unique card/O/acceptance identity | PC-76 |
| G-77 | B-3 implementation admission participates in card lock | PC-77 |
| G-78 | B-3 implementation admission rechecks after lock | PC-78 |
| G-79 | B-3 acceptance writer participates in card lock | PC-79 |
| G-80 | B-3 approval check serializes with evidence writers | PC-80 |
| G-81 | B-3 consistent lock order across close/admission/evidence | PC-81 |
| G-82 | B-3 move/dequeue/receipt commit atomically | PC-82 |
| G-83 | B-3 CardChanged only after commit | PC-83 |
| G-84 | B-1 retained report digest rechecked under Review lock | PC-84 |
| G-85 | B-1 predecessor current under Review lock | PC-85 |
| G-86 | B-1 correction/audit/successor atomic | PC-86 |
| G-87 | B-1 imported blob digests verified | PC-87 |
| G-88 | B-1 imported producer provenance required | PC-88 |
| G-89 | B-1 commissioned committed selection exact | PC-89 |
| G-90 | B-1 typed behavior assessment permits correction | PC-90 |
| G-91 | B-1 landing consumer validates correction link | PC-91 |
| G-92 | B-1 durable imported evidence before approval | PC-92 |
| G-93 | B-1 valid credential | PC-93 |
| G-94 | B-1 delegating principal | PC-94 |
| G-95 | B-1 project scope | PC-95 |
| G-96 | B-1 repository scope | PC-96 |
| G-97 | B-1 board/card scope | PC-97 |
| G-98 | B-1 server-derived actor | PC-98 |
| G-99 | B-2 valid credential | PC-99 |
| G-100 | B-2 delegating principal | PC-100 |
| G-101 | B-2 project scope | PC-101 |
| G-102 | B-2 repository scope | PC-102 |
| G-103 | B-2 board/card scope | PC-103 |
| G-104 | B-2 server-derived actor | PC-104 |
| G-105 | B-3 valid credential | PC-105 |
| G-106 | B-3 delegating principal | PC-106 |
| G-107 | B-3 project scope | PC-107 |
| G-108 | B-3 repository scope | PC-108 |
| G-109 | B-3 board/card scope | PC-109 |
| G-110 | B-3 server-derived actor | PC-110 |
| G-111 | B-1 correction POST invokes authorization | PC-111 |
| G-112 | B-2 action POST invokes authorization | PC-112 |
| G-113 | B-2 scoped GET invokes authorization | PC-113 |
| G-114 | B-3 acceptance PUT invokes authorization | PC-114 |
| G-115 | B-3 acceptance GET invokes authorization | PC-115 |
| G-116 | B-3 complete POST invokes authorization | PC-116 |
| G-117 | B-2 admission refusal persisted separately, no child ack | PC-117 |
| G-118 | B-1 missing execution returns exact missing selection | PC-118 |
| G-119 | B-3 current policy/decision revision required | PC-119 |
| G-120 | B-3 original workflow tuple required | PC-120 |
| G-121 | B-3 original acceptance revision required | PC-121 |

### Positive controls

Mutation runs **break/red/restore/green after land**; Code runs V/R, and Review judges the design before land. Every control below and every inherited PC is required when its owning Mutation pack runs. Compiling mutations touch production predicates/mappings/migrations, never test expectations. Assert the named boundary was reached; red must be the exact named assertion below. For index mutations use a fresh database migrated through the changed migration, not a pre-mutated template. After restoring source, recreate that database and run the identical exact-method filter green. An incidental deadlock/timeout is not PC-81 red: observe lock acquisition order as an explicit assertion with bounded release/join.

All labels below are required test assertion messages. Unbound line locations, imported payloads and any split guards stay **binding required after Code**, not presumed covered by a passing class run.

| PC | Compiling defect (break corresponding G) | Exact detecting filter | Expected red assertion |
|---|---|---|---|
| PC-65 | drop the correction unique index in the generated migration | `/*/*/PipelineSpeedIntegrationTests/C1281_B1Schema` | `duplicate-correction-refused` |
| PC-66 | allow correction update/delete in the persistence guard | `/*/*/PipelineSpeedIntegrationTests/C1281_B1Schema` | `correction-immutable` |
| PC-67 | remove the action unique index | `/*/*/PipelineSpeedIntegrationTests/C1281_B2Schema` | `duplicate-action-refused` |
| PC-68 | allow an existing observation to be overwritten/deleted | `/*/*/PipelineSpeedIntegrationTests/C1281_B2Schema` | `observation-immutable` |
| PC-69 | commit the child before persisting its link | `/*/*/PipelineSpeedIntegrationTests/C1281_B2AtomicChild` | `child-and-link-atomic` |
| PC-70 | skip canonical request-digest comparison | `/*/*/PipelineSpeedIntegrationTests/C1281_B2AtomicChild` | `changed-request-refused` |
| PC-71 | stamp consumption from notification confirmation | `/*/*/PipelineSpeedIntegrationTests/C1281_B2ObservationReadback` | `delivered-not-consumed` |
| PC-72 | fill absent dispatch time from consumption time | `/*/*/PipelineSpeedIntegrationTests/C1281_B2ObservationReadback` | `unknown-dispatch-null` |
| PC-73 | bypass the notification/queue/destination association check | `/*/*/PipelineSpeedIntegrationTests/C1281_B2ObservationReadback` | `foreign-receipt-refused` |
| PC-74 | drop the card/acceptance-revision unique index | `/*/*/PipelineSpeedIntegrationTests/C1281_B3Schema` | `duplicate-acceptance-refused` |
| PC-75 | allow an acceptance revision to be overwritten/deleted | `/*/*/PipelineSpeedIntegrationTests/C1281_B3Schema` | `acceptance-immutable` |
| PC-76 | drop the close-receipt unique index | `/*/*/PipelineSpeedIntegrationTests/C1281_B3Schema` | `duplicate-close-receipt-refused` |
| PC-77 | omit card lock in CreateAsync's insertion transaction | `/*/*/PipelineSpeedIntegrationTests/C1281_B3AdmissionFence` | `admission-waits-for-card-lock` |
| PC-78 | use the pre-lock card snapshot for terminal admission | `/*/*/PipelineSpeedIntegrationTests/C1281_B3AdmissionFence` | `post-close-implementation-refused` |
| PC-79 | save acceptance without taking the close writer's card lock | `/*/*/PipelineSpeedIntegrationTests/C1281_B3AcceptanceRevision` | `acceptance-waits-for-close-lock` |
| PC-80 | omit the Review evidence-writer lock during close | `/*/*/PipelineSpeedIntegrationTests/C1281_B3AtomicClose` | `review-writer-serialized` |
| PC-81 | reverse the close path's order relative to the agreed shared order | `/*/*/PipelineSpeedIntegrationTests/C1281_B3AtomicClose` | `lock-order-consistent` |
| PC-82 | commit the move before persisting receipt/dequeue | `/*/*/PipelineSpeedIntegrationTests/C1281_B3AtomicClose` | `close-effects-atomic` |
| PC-83 | publish CardChanged before transaction commit | `/*/*/PipelineSpeedIntegrationTests/C1281_B3AtomicClose` | `event-sees-committed-close` |
| PC-84 | skip the post-prepare report-digest recheck | `/*/*/PipelineSpeedIntegrationTests/C1281_B1CorrectionTransaction` | `changed-report-refused` |
| PC-85 | skip the active-predecessor recheck | `/*/*/PipelineSpeedIntegrationTests/C1281_B1CorrectionTransaction` | `superseded-predecessor-refused` |
| PC-86 | commit correction before audit/successor save | `/*/*/PipelineSpeedIntegrationTests/C1281_B1CorrectionTransaction` | `correction-effects-atomic` |
| PC-87 | skip imported content-digest verification | `/*/*/PipelineSpeedIntegrationTests/C1281_B1ReceiptImport` | `tampered-blob-refused` |
| PC-88 | accept the pack without matching retained producer identity | `/*/*/PipelineSpeedIntegrationTests/C1281_B1ReceiptImport` | `foreign-producer-refused` |
| PC-89 | accept receipt rows against a different selection commit | `/*/*/PipelineSpeedIntegrationTests/C1281_B1ReceiptImport` | `changed-selection-refused` |
| PC-90 | treat unclassified legacy Found as no substantive finding | `/*/*/PipelineSpeedIntegrationTests/C1281_B1CorrectionTransaction` | `unclassified-found-refused` |
| PC-91 | skip correction-link validation in LandApproval | `/*/*/PipelineSpeedIntegrationTests/C1281_B1CorrectionTransaction` | `broken-correction-link-refused` |
| PC-92 | save usable successor despite failed durable import | `/*/*/PipelineSpeedIntegrationTests/C1281_B1ReceiptImport` | `import-failure-no-approval` |
| PC-93 | accept an invalid or revoked credential | `/*/*/PipelineSpeedIntegrationTests/C1281_B1Authorization` | `invalid-credential-refused` |
| PC-94 | allow a Worker through the orchestrator-principal check | `/*/*/PipelineSpeedIntegrationTests/C1281_B1Authorization` | `worker-refused` |
| PC-95 | omit project equality | `/*/*/PipelineSpeedIntegrationTests/C1281_B1Authorization` | `foreign-project-refused` |
| PC-96 | admit a sibling path as within the authorized root | `/*/*/PipelineSpeedIntegrationTests/C1281_B1Authorization` | `foreign-repository-refused` |
| PC-97 | ignore the target card's authorized board binding | `/*/*/PipelineSpeedIntegrationTests/C1281_B1Authorization` | `foreign-board-card-refused` |
| PC-98 | store the supplied actor instead of authenticated identity | `/*/*/PipelineSpeedIntegrationTests/C1281_B1Authorization` | `authenticated-actor-retained` |
| PC-99 | accept an invalid or revoked credential | `/*/*/PipelineSpeedIntegrationTests/C1281_B2Authorization` | `invalid-credential-refused` |
| PC-100 | allow a Worker through the orchestrator-principal check | `/*/*/PipelineSpeedIntegrationTests/C1281_B2Authorization` | `worker-refused` |
| PC-101 | omit project equality | `/*/*/PipelineSpeedIntegrationTests/C1281_B2Authorization` | `foreign-project-refused` |
| PC-102 | admit a sibling path as within the authorized root | `/*/*/PipelineSpeedIntegrationTests/C1281_B2Authorization` | `foreign-repository-refused` |
| PC-103 | ignore the target card's authorized board binding | `/*/*/PipelineSpeedIntegrationTests/C1281_B2Authorization` | `foreign-board-card-refused` |
| PC-104 | store the supplied actor instead of authenticated identity | `/*/*/PipelineSpeedIntegrationTests/C1281_B2Authorization` | `authenticated-actor-retained` |
| PC-105 | accept an invalid or revoked credential | `/*/*/PipelineSpeedIntegrationTests/C1281_B3Authorization` | `invalid-credential-refused` |
| PC-106 | allow a Worker through the orchestrator-principal check | `/*/*/PipelineSpeedIntegrationTests/C1281_B3Authorization` | `worker-refused` |
| PC-107 | omit project equality | `/*/*/PipelineSpeedIntegrationTests/C1281_B3Authorization` | `foreign-project-refused` |
| PC-108 | admit a sibling path as within the authorized root | `/*/*/PipelineSpeedIntegrationTests/C1281_B3Authorization` | `foreign-repository-refused` |
| PC-109 | ignore the target card's authorized board binding | `/*/*/PipelineSpeedIntegrationTests/C1281_B3Authorization` | `foreign-board-card-refused` |
| PC-110 | store the supplied actor instead of authenticated identity | `/*/*/PipelineSpeedIntegrationTests/C1281_B3Authorization` | `authenticated-actor-retained` |
| PC-111 | bypass the correction POST authorization call | `/*/*/PipelineSpeedIntegrationTests/C1281_B1Authorization` | `post-anonymous-refused` |
| PC-112 | bypass the handoff POST authorization call | `/*/*/PipelineSpeedIntegrationTests/C1281_B2Authorization` | `post-anonymous-refused` |
| PC-113 | bypass the handoff GET authorization call | `/*/*/PipelineSpeedIntegrationTests/C1281_B2Authorization` | `get-foreign-history-refused` |
| PC-114 | bypass the acceptance PUT authorization call | `/*/*/PipelineSpeedIntegrationTests/C1281_B3Authorization` | `put-anonymous-refused` |
| PC-115 | bypass the acceptance GET authorization call | `/*/*/PipelineSpeedIntegrationTests/C1281_B3Authorization` | `get-foreign-acceptance-refused` |
| PC-116 | bypass the complete POST authorization call | `/*/*/PipelineSpeedIntegrationTests/C1281_B3Authorization` | `complete-anonymous-refused` |
| PC-117 | discard the refusal observation when create rolls back | `/*/*/PipelineSpeedIntegrationTests/C1281_B2HttpChildRecovery` | `refusal-durable-zero-child` |
| PC-118 | return the full scope instead of the one missing row selection | `/*/*/PipelineSpeedIntegrationTests/C1281_B1ReceiptImport` | `missing-selection-exact` |
| PC-119 | ignore changed policy/decision revision after proposal | `/*/*/PipelineSpeedIntegrationTests/C1281_B3AtomicClose` | `changed-policy-refused` |
| PC-120 | ignore changed run/stage/updatedAt after proposal | `/*/*/PipelineSpeedIntegrationTests/C1281_B3AtomicClose` | `changed-workflow-refused` |
| PC-121 | reload and silently adopt a newer acceptance revision | `/*/*/PipelineSpeedIntegrationTests/C1281_B3AtomicClose` | `changed-acceptance-refused` |

### Out of scope

- D-2 policy authoring/bundles and D-5 preflight/live qualification: owned by 1904d993/18880da1. CP-1..CP-13 and their cost remain unchanged. This appendix claims no qualification credit.
- No transport rewrite, new dispatcher, train/member projection, browser delivery guarantee, provider canary, scheduler activation, or automatic close enablement. Parent Q-4/Q-5 operating cohorts and all D-5 live gates remain separate.
- No source edit, build, test, receipt import, PC or live send is performed by this TestDesign task. Fixtures cannot satisfy the live shadow/pilot gates.

### Checkpoints

The importer reads the **first** exact `### Checkpoints` only. To run these new rows without editing CP-1..CP-13, extract this entire appendix to an ignored manifest first. In the Code checkout, read the committed plan (pin its full plan SHA in the brief), confirm a unique marker, and retain the original document SHA/digest plus the extracted bytes/digest in checkpoint provenance. This is a deterministic copy, not a second editable plan:

```powershell
$bindingDoc = 'docs/superpowers/plans/2026-10-15-card-1281-cheap-slices-checkpoints.md'
$bindingText = [IO.File]::ReadAllText((Join-Path (Get-Location) $bindingDoc))
$bindingMarker = '## Shared bindings B-1/B-2/B-3 (TestDesign, 2026-10-15, task 598a38d5)'
$bindingLines = $bindingText.Replace("`r`n", "`n").Split("`n")
$bindingHits = @(0..($bindingLines.Length - 1) | Where-Object { $bindingLines[$_] -ceq $bindingMarker })
if ($bindingHits.Count -ne 1) { throw 'binding appendix heading not unique' }
$bindingCopy = '.antiphon/c1281-shared-bindings-plan.md'
[IO.Directory]::CreateDirectory((Join-Path (Get-Location) '.antiphon')) | Out-Null
[IO.File]::WriteAllText((Join-Path (Get-Location) $bindingCopy), ($bindingLines[$bindingHits[0]..($bindingLines.Length - 1)] -join "`n"), [Text.UTF8Encoding]::new($false))
```

The exact-line comparison deliberately ignores the marker quoted in this code example.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-14 | S0-foundation | `tests/Antiphon.Tests -> bin-c1281-foundation/` | binding-schema | `/*/*/PipelineSpeedIntegrationTests/(C1281_B1Schema*)\|(C1281_B2Schema*)\|(C1281_B3Schema*)` | V-22,V-23,V-24,R-5 | all 3 methods, every schema variant, 0 failed/skipped | 3 | 6 |
| CP-15 | S0-foundation | CP-14 | binding-admission | `/*/*/PipelineSpeedIntegrationTests/(C1281_B2AtomicChild*)\|(C1281_B3AdmissionFence*)` | V-25,V-26,R-5,R-7,R-8 | both methods, both contender orders/cuts, 0 failed/skipped | 2 | 4 |
| CP-16 | S0-foundation | CP-14 | binding-old-locks | `/*/*/(CardWorkflowRunFactoryTests*)\|(ReviewEvidenceRecoveryTests*)/(CreateFromRevision_snapshots_every_stage_in_order_with_queued_status_and_first_stage_pointer*)\|(C1043_LockOrder*)` | R-5 | 2 methods, 0 failed/skipped | 2 | 3 |
| CP-17 | S6-wiring | `tests/Antiphon.Tests -> bin-c1281-wiring/` | binding-http-auth | `/*/*/PipelineSpeedIntegrationTests/(C1281_B1Authorization)\|(C1281_B2Authorization)\|(C1281_B3Authorization)` | V-27,V-30,V-33 | 3 methods, every route/principal/scope variant, 0 failed/skipped | 3 | 7 |
| CP-18 | S6-wiring | CP-17 | binding-correction | `/*/*/PipelineSpeedIntegrationTests/(C1281_B1CorrectionTransaction)\|(C1281_B1ReceiptImport)` | V-28,V-29,R-6 | 2 methods; authentic producer pack and actual validator reached, 0 failed/skipped | 2 | 6 |
| CP-19 | S6-wiring | CP-17 | binding-caller | `/*/*/PipelineSpeedIntegrationTests/(C1281_B2ObservationReadback)\|(C1281_B2HttpChildRecovery)` | V-31,V-32,R-7 | 2 methods, restart/refusal/notification cuts reached, 0 failed/skipped | 2 | 6 |
| CP-20 | S6-wiring | CP-17 | binding-close | `/*/*/PipelineSpeedIntegrationTests/(C1281_B3AtomicClose)\|(C1281_B3AcceptanceRevision)` | V-34,V-35,R-8 | 2 methods, races/commit cuts reached, 0 failed/skipped | 2 | 6 |
| CP-21 | S6-wiring | CP-17 | binding-wired-roundtrip | `/*/*/PipelineSpeedIntegrationTests/C1281_WiredRoundtrip` | V-36,R-7,R-8 | 1 method, busy and eligible complete UserPrompt plus fresh GET receipts, 0 failed/skipped | 1 | 3 |
| CP-22 | S6-wiring | CP-17 | binding-wired-recovery | `/*/*/PipelineSpeedIntegrationTests/C1281_WiredDeliveryRecovery` | V-37,R-7 | 9 Arguments executions, all queue cuts/recipient variants, 0 failed/skipped | 9 | 8 |
| CP-23 | S6-wiring | CP-17 | binding-old-http-card | `/*/*/(ReviewEvidenceRecoveryEndpointTests)\|(CardWorkTransitionServiceTests)/(C1043_ResponseAfterCommit)\|(a_settled_task_with_nothing_else_open_moves_the_card_to_review_and_dequeues_it)` | R-6,R-8 | 2 methods, 0 failed/skipped | 2 | 3 |

Exact filters form the closed ordinary scope of this **addition**: 28 minimum TUnit executions across 10 rows, not the number of internal assertions. S0 has 7; S6 has 21. The parent's component Unit/acceptance rows remain required; these shared integration passes select only the named schema, admission, HTTP and existing regression methods. No extra namespace/full-assembly run is authorized.

Code front door (after exact-line extraction above): `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1281-bindings -- dotnet run --project tools/Antiphon.Checkpoints -- run --plan .antiphon/c1281-shared-bindings-plan.md --after S0-foundation --expected-source-sha <committed-Code-sha> --serial`. S6 uses `--after S6-wiring` on its own committed tip, after the three component lands. Importing the unextracted parent file cannot run these rows. Wait through exit 75 to terminal result, keep unedited CP lines and fresh TRX, run the base..HEAD evidence guard, and remove only owned alternate outputs. No Code mutation runs.

### Cost

All numbers below are **estimated**, not measured; no builds/tests ran here.

- Added ordinary V/R floor (Code): **52 minutes** = S0 13 (CP-14/15/16: 6+4+3) + S6 39 (CP-17..23: 7+6+6+6+3+8+3). Includes **4 build/setup + 48 test minutes**. Filters are the exact rows above. A full initial red-first pass of new-behavior rows adds at most **46 minutes**; existing R-only CP-16/23 need no invented red.
- Added PC floor (Mutation): **57 x 8 = 456 minutes**, using every exact-method filter in PC-65..PC-121. Each cycle budgets 2 build + 2 intended assertion-red + 2 restore/rebuild + 2 same-method green minutes. All 57 cycles are required; zero build reuse or selective-PC saving assumed.
- Added ordinary + PC total: **508 minutes** = 4 setup/build + 48 V/R + 456 PC; including the initial red-first allowance, **554 minutes**. Independent Review's same 52-minute ordinary scope is additional (606 with that allowance). Authoring, slot waits, artifact binding and inherited-red diagnosis are additional.
- Whole cheap-slice budget now: existing 76-minute ordinary floor (including separately landed S2-policy) + 52 = **128 minutes**; existing PC floor 453 + 456 = **909 minutes**; **1,037 minutes ordinary + Mutation**, before red-first reruns, Review and live Q gates. The original parent budget did not include these shared guards.
- Two isolated builds and eight reused rows save an estimated **16 build minutes** versus rebuilding for all 10 rows (10 x 2 minus 2 x 2). Test omission saves **0 minutes**: every named ordinary row and PC remains required.

Handoff audit: **new guards=57, mapped=57, missing=0, duplicate PC maps=0**; combined with parent **guards=121, mapped=121, missing=0, duplicate PC maps=0**. **PCs executed=0; required pending=121**. This docs-only stage does not claim “all PCs run”; Mutation must run all controls and report break/red/restore/green before claiming PC-clean. Code's compile-time payload/line binding must be recorded, including any newly split guards. **Next: code, S0-foundation only**, then the component/final-integration order above; no human choice is outstanding.

S0 Code filter correction (04228bff): the original CP-14 method OR executed zero tests on TUnit 1.44. Suffix wildcards on each OR operand, as required by docs/testing-and-build.md, retain the named-method roster; inspect fresh TRX. Evidence: `docs/superpowers/evidence/2026-10-15-card-1281-s0-foundation.md`.
