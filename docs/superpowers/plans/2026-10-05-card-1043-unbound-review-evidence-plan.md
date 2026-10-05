# CARD-1043: recover Review evidence after a runner-sync block

Date: 2026-10-05. Source inspected: `b2fc3f03d8a7d576216d151bd94680cddc1b9aab`.
Card: Antiphon CARD-1043, `310e45b0-24a6-465b-88e4-02c180db254c`.
Stage: Plan. Next: **TestDesign**, then Code in the slices below.

The defect is confirmed. A lease-busy settlement creates a Review outcome without
approval coordinates. The successful continuation never evaluates its final
evidence because `RecordDelegateStageOutcomeAsync` returns when *any* outcome
already exists. Preserve that history and append a superseding outcome from the
successful final report. Add an explicit, audited recovery operation for already
settled Reviews. Keep runner synchronization and land admission fail-closed.

Complexity: **Hard overall** (the automatic repair alone is Medium). Recovery
crosses stored-report authority, exact Git identity, concurrent outcome writers,
immutable completion snapshots and landing approval. Verification is deliberately
separate; this document is not an executable checkpoint manifest. TestDesign must
read the named fixtures, supply method-level positive controls and add the full
`## Verification design` before Code is commissioned. This is not a defaults-only
decision request: the decisions below are the selected design.

## Ground truth

| Card/brief assumption | Observed code or live record | Design consequence |
|---|---|---|
| A successful report can become Blocked on runner sync. | `AgentTaskReplyService.SettleAsync`, around lines 817-885, classifies the report, then `RemoteSyncBlockReason` overrides Succeeded to Blocked and forces `next=decide`. | Do not bypass that guard. Evaluate replacement approval only after this turn passes synchronization. |
| The first Clean finding stays unbound. | `RecordDelegateStageOutcomeAsync`, around lines 4522-4660, writes the finding even when Blocked; binding requires Review role/stage and Succeeded. Profile-v1 scope becomes **Unknown**, not null. `SubjectTaskId` can retain a follow-up subject even without approval. | Define unbound by missing approval coordinates, not by a null subject alone. Do not confuse a Clean finding with approval. |
| The successful retry should bind. | The unconditional `AnyAsync(StageTaskId, Stage)` early return at line 4530 prevents parsing/binding the new final report. | Replace this one-shot policy only for a still-unbound delegate Review outcome. |
| Retry/backoff must be added. | `RemoteWorkspaceService.WhileLeaseBusyAsync` already retries every second, in ten-second slices, against `Delegation:RunnerSyncBudgetSeconds` (default 120). `RunnerSyncLeaseWaits` carries elapsed wait across scopes/sweeps; `StillWaitingForLease` leaves the task open until the budget is spent. | Adding another retry loop does not fix persisted unbound rows and would multiply latency. Keep existing budgets. |
| A Review must advance its branch to synchronize. | `RemoteSettlementSyncResult.Confirmed` includes `NoPushedProgress` with a non-null confirmed desktop SHA. All three affected successful Reviews below have that state, equal mirror/remote/confirmed SHAs and `mirrorDirty=false`. | A clean, unchanged Review is a valid recovery case. Requiring only `Synchronized` would reproduce the incident. |
| Binding currently verifies the claimed SHA. | `ReviewEvidence.TryParse` validates full object IDs. Existing initial settlement compares review base and cached subject tip only to emit CARD-0788 warnings; it still binds a mismatch (`ReviewEvidenceConsistencyTests`). | Add exact observed-tip checks to the new recovery/re-settlement path. Do not silently change ordinary first-settlement warning semantics. |
| GET/header automatically discover any Clean row. | `LandCompletionFacts.LoadReviewAsync` requires subject and SHA, so unbound rows are invisible. It filters Clean before selecting latest and does not exclude superseded rows. Settlement headers use the DbContext-local `SettlementOutcome`; completion snapshots are immutable. | Select the active outcome before interpreting it. Ensure the replacement is the outcome used by this settlement's snapshot. Never rewrite an old delivered snapshot. |
| A Succeeded owner lands without an evidence ID because the server finds evidence. | `AgentTaskLandService` uses the distinct **ExplicitCaller** SHA approval path when permitted. Recovery/adoption requires an explicit evidence ID; `LandApproval` checks Clean, clean-source assertion, subject/SHA/ref/repository, supersession and Final/Full. | Preserve both approval paths and every refusal. Do not implement evidence-ID fallback or make Full up from Succeeded. |
| Four listed Reviews need unbinding repair. | Live GETs on 2026-10-05 confirm three. `a08e6d8d` already has bound evidence with **Interim** scope, and its report says a required checkpoint timed out. | Recover three candidates conditionally. Leave the fourth bound and ineligible; it needs its outstanding verification, not a binding override. |
| Manual `-Finding -Clean` is sufficient recovery. | `StageOutcomeService.RecordFindingAsync` appends an orchestrator row but intentionally does not copy profile/round/Full. It cannot create Final/Full recovery evidence from the stored report. | Add a distinct stored-report recovery operation; do not relax the generic finding endpoint. |
| Each runner has an independent sync lease. | `RepositoryMutationLease` resolves the common Git directory and exclusively opens `antiphon/landing.lock`. Dispatch, exact-ref fetch, settlement and land share it. `AgentTaskLandingProtocol.RunAsync` retains the lease across verification. | More concurrent tasks can exceed a 120-second wait even when the lease works correctly. Treat scheduling/lease duration separately. |

The principal source owners read were `docs/project-context.md`,
`docs/orchestration-loop.md` (landing, runner dispatch and Final verification),
`docs/ops-http.md`, `docs/agent-card-lifecycle.md`,
`docs/testing-and-build.md` (checkpoint, slot and mutation contracts), and
`docs/session-runtime-invariants.md` (profile-v1 completion obligations).

## Live recovery inventory

These are observations, not durable promises about current branch tips. Before
recovery, read the named Review, its complete stored result and all its stage rows
again. GET detail uses `summary`, `progressEvidence` and `verification` nesting.
Use `/api/stage-outcomes?cardId=<resolved-card-id>&stage=Review&latestOnly=false`
to inspect history. Do not infer missing evidence from a clipped completion note.

| Review task | Existing outcome ID | Result at inspection | Stored report coordinates |
|---|---|---|---|
| `2eabeec3-e2c5-482c-8872-5f4a3b1cedb5` | `3d11c43f-3d80-4a8e-9755-98a85bd86222` | Succeeded; evidence null; Delegate Clean / Final / Unknown; row timestamp is first Blocked settlement | Subject `9bcf4ea9-2a3b-47ed-8df6-1a172a38a166`; SHA `c5c58a8ef4e213ad69a4afb07012208fd3613971`; clean true; Full |
| `4012cd3d-9b0f-4afb-81a7-b2d9814bac8d` | `01357cff-1328-4c87-9812-93cd5944a5dc` | Same defect and same subject/SHA; separate Review | Subject `9bcf4ea9-2a3b-47ed-8df6-1a172a38a166`; SHA `c5c58a8ef4e213ad69a4afb07012208fd3613971`; clean true; Full |
| `031e7c04-cf28-43ea-8d42-6fc333977ec9` | `04c5ffd1-1e68-48bc-bd6b-ab7f04b9d9ec` | Succeeded; evidence null; first Blocked at 00:10:01Z, successful completion 00:14:41Z; row still from the block | Subject `c8655038-e26e-4642-abd6-b8bc05c58ae6`; SHA `e3a52bb1ebe99c70f1615facd59d8b1425de69ea`; clean true; Full |
| `a08e6d8d-ea6b-4fd6-9226-9b8ae2a3b1c7` | `5a7f2132-bf59-49a8-bfe4-71fc62705ac9` | Already bound Delegate Clean / commissioned Final / completed Interim; no Blocked event in returned history | Subject `8bb4bae8-2261-4e93-a77b-c93c8bcc9cd4`; SHA `e30eb42aa0d13d16b07668fe51d38c546d57b7fb`; report explicitly owes CP-6 |

The first two rows were recorded at 2026-10-04 17:14:12Z and 17:35:01Z,
respectively. Their later successful reports are retained in `AgentTask.Result`.
All three affected tasks are profile-v1 Final Reviews. Their current confirmed
runner SHA equals the SHA in the report, despite the separate no-progress warning.
No live evidence or task state was mutated during planning.

## Decisions

### D-1. Repair the outcome writer, not the lease budget

Keep the current bounded sync retry and all genuine failure handling. After a new
turn successfully settles, allow a previously unbound **Delegate Review** outcome
to be superseded from this turn's final report. Do not replay the initial report
or copy its finding into the new row. The final report may now say Found, contain
different coordinates, omit evidence or lower the completed scope.

The predicate is based on present authority: Review role and stage, successful
current settlement, an unsuperseded delegate row without bound coordinates, and
newly validated report/source facts. It need not classify the previous block by
parsing warning prose. This also repairs an equivalent unbound Review after a
different *resolved* block. A still-failing sync cannot satisfy the predicate.
Existing bound rows, explicit orchestrator overrides, other roles/stages and
unsuccessful current turns retain their existing idempotence behavior.

Rejected: deleting the early-return guard for every stage (duplicates outcomes);
binding while Blocked (unconfirmed source); retry-only or increased timeouts (the
old row still wins); changing land validation (turns missing evidence into approval).

### D-2. Append a superseding outcome; never mutate approval history

Extract the existing report/subject binding logic into a concrete
`ReviewEvidenceBindingService`, shared by automatic replacement and explicit
recovery. Keep `ReviewEvidence` as the parser. Insert a new `StageOutcome` with
`SupersedesId=<old-id>` and the same `StageTaskId`. Original row fields, timestamp,
finding and cost stay untouched. For automatic replacement use `Source=Delegate`;
explicit recovery uses `Source=Orchestrator` and provenance identifying the stored
delegate report. The generic finding endpoint still manufactures no Full scope.

Automatic replacement records the **new** finding and capped scope, even when it
does not produce approval. A valid profiled Found result can carry its normal
baseline coordinates; readers must never expose it as a Clean approval. Invalid
or missing final evidence remains unbound and warns. No old Clean row may reappear
through a query that filters before considering supersession.

Select active leaves by explicit `SupersedesId`, then the established timestamp/ID
ordering. Use the same selection for DB reads, settlement's tracked outcomes and
scope headers. For `StageOutcomeService.LatestPerTaskStage`, eliminate superseded
rows **before** grouping by subject: the old unbound row groups by Review task,
whereas the replacement groups by Code subject. `latestOnly=false` retains both.
When filtering date/card ranges, check successors against the full stored relation
so a successor outside the requested interval cannot revive its predecessor.

Rejected: updating the original row in place (erases why the first header lacked
approval); appending without supersession (old IDs stay usable); selecting latest
Clean only (a later Found/invalid result could resurrect approval).

### D-3. Recovery must bind exact source observations

The shared validator retains standalone-block parsing, full 40/64-character SHA
normalization, explicit clean assertion, subject authorization and round capping.
For the new replacement/recovery path additionally require:

1. The final named subject is an authorized Worktree task: follow-up identity must
   match when present; otherwise same-card authorization still applies. Repository
   identity must agree, and the source ref comes from the **subject**, not the
   Review's task branch or an adoption owner.
2. For runner-bound Reviews, this successful settlement's prepared sync confirms
   the full claimed SHA on the Review branch. `Synchronized` and confirmed
   `NoPushedProgress` are both accepted. An observed-only SHA, missing confirmation,
   dirty mirror, wrong branch or unknown state cannot establish the witness.
3. Observe the subject's exact ref through `ITaskProgressGit` with its captured
   endpoint fingerprint. It must be Present at the reported SHA. A cached subject
   completion tip, tracking ref or `FETCH_HEAD` is not fresh authority. Use the
   existing leased observation seam; never reacquire a non-reentrant lease inside
   an already leased observation.
4. For explicit recovery, corroborate the stored successful Review sync evidence
   with a fresh exact-ref observation of the Review branch as well as the subject
   branch. Do not run a full settlement sync, publish a mirror, advance a checkout
   or manufacture a new delegate turn. A missing retired branch or unavailable
   endpoint refuses; it does not silently substitute another ref.

The extra strict checks apply to repairing old unbound rows, not ordinary first
settlement. Existing CARD-0788 diagnostic behavior stays covered by its tests.
Git observes a point in time, not an indefinite freeze: land still performs its
own independent exact-source and approval rechecks before mutation/publication.

A failed new witness leaves the old evidence unbound with a specific warning;
automatic settlement sends `next=decide` with the repair reason and no evidence
header. A genuine runner sync failure retains Blocked/Decide through the existing
path. Explicit recovery returns a typed conflict and creates no approval.

Rejected: comparing the claim only to `WorktreeBaseSha` (can be stale); treating
the Review branch as the landing subject; treating NoPushedProgress as failure;
accepting a legacy missing clean assertion; revalidating source by running a new
Review just to recreate lost metadata.

### D-4. Preserve atomic settlement and serialize supersession

Compute Git/report candidates before any database write lock. Finalize outcome,
task result/status, settlement event and new completion snapshot in one short
database transaction. Use a row lock on the Review task, re-read active outcomes
and compare the task/report identity used to prepare the candidate. No Git,
runner RPC, process launch or queue send occurs under that database lock.

The automatic writer, explicit recovery and generic finding override must use the
same task-row serialization when superseding a Review outcome. A concurrent writer
cannot append a sibling replacement or override newer findings. Repeated automatic
settlement still owns only one completion obligation for its settlement event.
Repeated recovery of the same predecessor/report digest returns the same new ID;
a different successor or changed result is a 409. On restart, these decisions use
database state, not a process-local deduplication cache.

A generic finding may target the Code subject rather than the Review task. Resolve
its candidate predecessor first, lock the predecessor's `StageTaskId` as well as
the requested task in stable GUID order when they differ, then re-read the chain.
If the candidate changed to a different lock identity, retry preparation rather
than writing under the wrong task lock. Test both route-target forms. Compare the
prepared subject/ref/repository and report digest again before the final insert.

`AgentTask.ConcurrencyToken` is currently only `.IsRequired()` in the EF mapping,
not `.IsConcurrencyToken()`. Merely rotating it is **not** compare-and-swap. Do not
silently change that property globally; use the short explicit lock/recheck here.
Retain existing per-session settlement exclusion. Move any remaining native/file
preparation ahead of finalization instead of extending a transaction across it.

Rejected: only an in-memory lock (does not cover recovery/other processes), relying
on wall-clock ordering, or adding an unrestricted unique supersession index whose
historical data has not been audited.

### D-5. Add a named, synchronous stored-report recovery operation

New route: `POST /api/agent-tasks/{reviewId}/review-evidence/rebind`.
Request: `{ evidenceId, expectedReportSha256, reason }`. These are selectors and
an audit reason, never replacement subject/SHA/clean/scope fields. Require a full
named outcome GUID and SHA-256 of the exact UTF-8 stored `AgentTask.Result`.
Reject unknown evidence-override fields. No bulk repair or automatic startup scan.

The Review must already be Succeeded and profile-v1 Final; the named active
predecessor must be its unbound Delegate Review row. Reparse the current complete
stored report and require a Clean finding, a usable standalone evidence block,
`reviewedSourceClean: true` and declared/capped Full scope. Preserve all subject
and exact-source checks in D-3. Missing report/profile/round/source facts require
new evidence rather than inferred values. A current Blocked Review must first
produce a successful continuation through normal settlement.

Provide an explicit parser entry for an already-settled report body: settlement
removes the outer closing report token before saving `Result`. Do not accidentally
truncate this body at an earlier embedded token-shaped line. Automatic settlement
continues parsing its raw final report with the established token boundary.
Hash the stored bytes before any parsing/newline normalization. Do not edit Result.

Authenticate a real operator credential through `OperatorCredential`, or a valid
delegation principal allowed to orchestrate within the Review's project/repository
scope. Derive actor identity from the credential; anonymous/manual-fallback callers,
worker tokens and unrelated capability roots do not gain recovery authority.
Use existing credential readers; never send tokens in bodies or print them.

Response: `{ disposition: "bound"|"already-bound", reviewTaskId, previousEvidenceId,
reviewEvidenceId, reportSha256, subjectTaskId, reviewedSourceSha }`. A read after
commit exposes the same active ID. The response is the recovery receipt; this
operation creates no new completion notification or delegate session. It does not
alter old completion snapshots, task status, cost, handoff or caller receipt state.

Add `scripts/rebind-review-evidence.ps1 -Task <id> -EvidenceId <guid>
-ExpectedReportSha256 <digest> -ReasonFile <path>` with `-WhatIf` for a read-only
preview and the established operator-token option when running as an operator.
Preview prints exact selectors, digest and candidate coordinates; commit mode
still re-reads/revalidates server-side. No automatic request to land follows.

### D-6. Record provenance without a data migration

Use the existing `StageOutcome.Ref` audit field for a versioned, bounded
`review-rebind-v1:` record, serialized by a typed helper. Include mode
(settlement/recovery), final report SHA-256, originating settlement event ID,
observation time, confirmed Review SHA, observed subject SHA and authenticated
actor reference when applicable. `SupersedesId`, `StageTaskId` and the row's bound
coordinates provide the rest of the identity. Validate the complete record fits
the existing 1000-character limit; never clip an authority record.

Append a `FindingRecorded` task event with old/new IDs, source report digest,
actor, reason and source observations in the same transaction. Automatic repair
uses the current settlement event. Historical recovery identifies the final
completion snapshot's source event when available; otherwise it must find the
unique successful reported-completion event matching `CompletedAt`, not a
merge-back `Completed` event. Ambiguous/missing provenance refuses recovery.
Free-form historical warnings may explain the incident but never supply clean or
Full assertions. Retain the original row and all previous completion obligations.

This is a report-derived rebind, not a generic manual override or background
backfill. `StageOutcomeBackfillService` stays retired. Update the StageOutcome
comment and orchestration owner to document this narrowly authorized exception to
the rule that ordinary manual findings never copy Full.

### D-7. Keep fleet placement dynamic

Read `GET /api/runner-defaults` and `GET /api/session-runners` before execution.
Planning read defaults revision 2 and an eligible default Linux runner, with six
occupied sessions; an eligible Windows runner was also occupied. These are live
observations, not hosts to embed in a plan or command. All proposed production
changes are OS-neutral server code. Ordinary lane: **Any / default eligible
runner**, with PostgreSQL, scratch Git and isolated fake runner/terminal peers.
Use a Windows checkpoint only if TestDesign identifies a truly Windows-only
native assertion. Omit `-Runner`; omit `-Platform` unless an OS is required.

### D-8. Investigate contention under CARD-0743

A separate contention investigation is warranted and already exists: **CARD-0743**
(Backlog), "Docs-only land held the repository lease ~17 min; three worktree
dispatches queued behind it." A complete board search for `lease` and a full card
read found this overlap. Do not file a duplicate or redesign locking in CARD-1043.

Code shows one common-directory lock and a land retaining it over verification,
which can include a build-slot wait. Exact-ref fetch and checkout synchronization
take the same lease separately; fair acquisition is not guaranteed. Dispatch has
its own bounded priority/yield mechanism, not a general settlement waiter queue.
These facts explain susceptibility, not the particular holder of every incident.

CARD-0743 should measure: holder purpose/task/acquisition ID; acquisition, hold and
release times; time in fetch/rebase/verify/build-slot/cleanup; per-settlement wait
slices and budget exhaustion; unfinished child-journal fences; and dispatch yield
time. Compare quiet execution with five/six concurrent tasks using existing
controlled fixtures before changing scheduling. Report p50/p95/max hold and wait,
starvation cases and the fraction exceeding 120 seconds. Separate Git time from
build-slot wait and caller/session time. Any lock-scope change must retain checkout,
child-process custody and landing source invariants. Do not increase budgets or
split the lock without those measurements.

## Implementation slices

Commit and push each slice before its checkpoint group. Each slice is a 30-60 minute
Code dispatch; estimates below are authoring budgets, with verification budget added
by TestDesign. Use fresh follow-on task branches/StartRef as required by orchestration;
never rebase or force-push a runner-mirror task branch. Keep colliding settlement
changes sequential.

| Slice | Budget | Files | Deliverable and named tests |
|---|---|---|---|
| S1: binding candidate and active outcome selection | 45-60 min | New `server/Application/Services/ReviewEvidenceBindingService.cs`; `ReviewEvidence.cs`; `LandCompletionFacts.cs`; `StageOutcomeService.cs`; `AgentTaskService.cs` only if projection needs adjustment; `server/Domain/Entities/StageOutcome.cs` comments | Extract without changing first-settlement behavior; add active-leaf selection and typed provenance helper. New `tests/Antiphon.Tests/Application/ReviewEvidenceRebindingTests.cs`; retain `ReviewEvidenceConsistencyTests`, `ReviewEvidenceParserTests`, `StageOutcomeSummaryTests`. Cover subject-changing supersession and embedded-token stored-body parsing. |
| S2: automatic successful continuation | 45-60 min | `server/Application/Services/AgentTaskReplyService.cs`; S1 service; `server/Api/Program.cs` registration; narrowly extend `tests/Antiphon.Tests/TestHelpers/C544World.cs` / runner fixture | Replace the blanket skip for the eligible unbound Review; pass final raw report and prepared sync; atomically commit replacement and new completion snapshot. New `ReviewEvidenceResettlementTests.cs`; extend `RunnerTaskSettlementTests` fixture for a Review with a real subject and final finding. Tests drive actual Blocked -> Answer -> new prompt/turn -> Succeeded. |
| S3: guarded recovery service and endpoint | 45-60 min | New `server/Application/Services/ReviewEvidenceRecoveryService.cs`; new `server/Application/Dtos/ReviewEvidenceRecoveryDtos.cs`; `server/Api/Endpoints/AgentTaskEndpoints.cs`; `server/Api/Program.cs`; `StageOutcomeService.cs` for shared write serialization | Named old row + exact stored report digest; exact-ref observations; credential/scope guard; short task-row transaction; append-only audit and idempotent response. New `ReviewEvidenceRecoveryTests.cs` and `ReviewEvidenceRecoveryEndpointTests.cs`, using existing endpoint/auth and PostgreSQL fixtures. Include two concurrent requests and recovery racing a finding override. |
| S4: recovery CLI and owner documentation | 30-45 min | New `scripts/rebind-review-evidence.ps1`; new `tests/Antiphon.Tests/Scripts/ReviewEvidenceRecoveryScriptTests.cs`; `docs/ops-http.md`, `docs/antiphon-api.md`, `docs/orchestration-loop.md` | Preview and explicit commit with digest/reason; no credentials in output. Document response IDs, refused cases, unchanged landing requirements and recovery runbook below. Keep script ASCII-compatible. |
| S5: delivery and landing capstones | 45-60 min | New `tests/Antiphon.Tests/Application/ReviewEvidenceRebindingDeliveryTests.cs`; `ReviewEvidenceDeliveryTests.cs` / `tests/Antiphon.Tests/TestHelpers/C544DeliveryRig.cs` only for reusable seams; new `ReviewEvidenceRebindingLandingTests.cs` using `LandingSafetyHarness` | Real settlement-to-queue-to-matching-UserPrompt receipt; fresh DB reader sees the replacement. Real Git recovery-owner and adoption-source admission using the new ID, and rejection of the predecessor/dirty/incomplete/wrong-subject evidence. Repair any discovered production defect in its owning slice before continuing. |

Do not rename or weaken the CARD-0788 tests to conceal changes to normal binding.
Do not add a database migration unless TestDesign finds that the bounded existing
provenance field cannot meet D-6; that would be a concrete plan amendment first.

## TestDesign commission and checkpoint lanes

The next stage owns the executable verification design, not another fix design.
Inspection starting points already read include:

- `ReviewEvidenceSettlementTests`: successful/unsuccessful binding, subject guards,
  repeat settlement, source adoption and scope capping.
- `ReviewEvidenceConsistencyTests`: first-settlement mismatches deliberately warn
  and bind; 40/64-character SHAs and missing observations.
- `RunnerTaskSettlementTests`: real lease-budget block and reply/new-turn retry,
  completion obligations, and non-Code unchanged-branch behavior.
- `RunnerSettlementSyncTests`: controlled-clock lease release, cumulative slice
  budget, failed sync preservation and scratch repository/source observation.
- `ReviewEvidenceDeliveryTests`, `C544World`, `C544DeliveryRig`: actual durable
  completion/outbox path, busy/eligible recipients and persistence failure cuts.
- `AgentTaskReviewEvidenceTests`, `StageOutcomeFindingEndpointTests`,
  `StageOutcomeSummaryTests`, `LandingSafetyHarness`: immutable coordinates,
  manual overrides, summary grain and land admission.

Required behaviors for the design:

1. Reproduce the incident through real settlement, with the first unbound row and
   the second report, including confirmed unchanged Review tips. Assert exact new
   subject/SHA/ref/repository/clean/Full and the old/new supersession link.
2. Make the second report differ from the first: new valid coordinates, Found,
   missing/fenced evidence, false/missing clean assertion, Interim/Unknown scope.
   Assert no reuse of the initial Clean approval or synthesis of Full.
3. Exercise a continuing sync failure, missing/wrong confirmed tip, dirty mirror,
   fresh subject-ref mismatch, unavailable/changed endpoint and 40/64-character
   normalization. Separate cached mismatch diagnostics from fresh-ref failure.
4. Exercise explicit recovery from a retained final report and named historical
   unbound row. Test missing profile/report/provenance, stale digest, wrong row,
   already-bound row, unrelated actor/subject and anonymous/worker credentials.
   A fixture shaped like `a08e6d8d` must remain Interim and ineligible.
5. Prove repeat/concurrent recovery returns one successor; newer manual Found
   wins against stale recovery; transaction rollback leaves no half-bound row,
   audit or new completion; a fresh service after restart reaches the same result.
6. Prove active-leaf readers and latest-only summaries do not revive superseded
   Clean rows or count an unbound predecessor under another subject group. Check
   date-filtered history and deterministic equal-timestamp ordering.
7. Prove the successful continuation's header, snapshot and GET agree on the new
   evidence ID. The first Blocked snapshot is unchanged. Carry the new header
   through the real queue to a complete matching UserPrompt for both busy and
   already eligible callers, with before/after commit/enqueue failure recovery.
   A queue row, Sent flag or snapshot alone is not receipt.
8. Prove unchanged `LandApproval` rejects the old ID and accepts the new Final/Full
   ID for a Failed owner and for adoption of the report's actual source task.
   Changing subject to the original adoption owner must fail. New ID never skips
   current branch freshness, clean-source, scope, repository or supersession gates.

One positive control per independently bypassable behavior/guard. TestDesign must
assign exact methods and decisive assertion labels. Code runs ordinary V/R;
ordinary Review judges before land; SourceLanding Mutation runs method-scoped
break/red/restore/green cycles after confirmed publication. Zero tests, compile
failure or fixture failure never counts as a control going red.

The following are **checkpoint envelopes**, not runnable CP rows. TestDesign must
produce the closed `### Checkpoints` table with exact method/class filters,
expanded execution floors, V/R IDs and numeric time estimates. No whole Unit,
namespace or assembly selection is authorized.

| Envelope | After | Lane | Bounded test selection | Planning allowance |
|---|---|---|---|---|
| CP-1 | S1 | Any / default runner; DB + Git | New binding class plus exact parser/consistency/summary methods affected by extraction | 10-15 min |
| CP-2 | S2 | Any / default runner; DB + real scratch Git / controlled sync clock | New re-settlement class; runner sync block/reply and unchanged-Review methods | 10-15 min |
| CP-3 | S3 | Any / default runner; DB + scratch Git + HTTP fixture | Recovery service/endpoint classes, selected generic override regressions | 10-15 min |
| CP-4 | S4 | Any / default runner; offline PowerShell HTTP harness | Recovery script class | 5-10 min |
| CP-5 | S5 | Any / default runner; real queue + isolated terminal | New rebinding delivery class, persistence cuts and complete prompt receipts | 10-15 min |
| CP-6 | S5 | Any / default runner; real Git + PostgreSQL | New rebinding landing class for owner recovery and adoption | 10-15 min |

Ordinary checkpoint planning allowance: 55-85 minutes plus 210-285 minutes of
slice authoring; these are estimates, not measured timings or execution floors.
TestDesign must price its actual method-level PC roster separately, avoiding
duplicated slow fixtures when one causal capstone covers the path. Checkpoint
tool runs once per committed slice group; await it to a non-75 exit. All builds
take host slots and use isolated `bin-c1043-<slice>/` outputs with forward slashes;
remove owned alternate outputs on completion. Commit before long runs, freeze
source during them, and validate clean SHA-bound receipts for the actual selected
rows. Report unexecuted rows honestly. Review runs the full task-range evidence
diff check; generated receipts/logs/TRX remain ignored.

## Activation and recovery runbook

1. Caller lands this documentation-only Plan through the normal task landing
   protocol as soon as this task settles, then commissions TestDesign. The runner
   branch is pushed only to its assigned feature ref; there is no direct master
   push or reachable canonical desktop checkout in this delegate.
2. Implement/verify the slices, obtain ordinary Review, land, activate the server
   from the canonical checkout and confirm `/api/version` SHA. Publication alone
   does not activate the new endpoint. No runner upgrade is required by this design.
3. Read each row in the recovery inventory afresh. Hash the exact returned stored
   Result and preview the named recovery. Prioritize `031e7c04`, whose evidence
   subject is the repair source `c8655038`, not owner `f92f723a`. No new Review run
   is needed when the retained Full report and exact source witnesses remain valid.
4. Invoke the new recovery command once per eligible named predecessor. Retain
   response/new ID and audit provenance. Re-read the Review and stage history;
   confirm the active ID, Full scope and unchanged predecessor. On conflict, inspect
   the code and coordinates; do not edit database rows or use `-Finding` as a bypass.
5. Land through the original owner with `-FromTask <actual-repair-source>` for
   adoption, or `-RecoverReviewedSource` when the report names the failed owner
   itself. Supply the **new** full `-ReviewEvidenceId` and exact reviewed SHA.
   Normal recovery status, same-card/project/ref and source freshness rules remain.
   For CARD-1035 the two Reviews name the same subject/SHA: one eligible recovered
   approval is sufficient; both can be recovered for audit without rerunning tests.
6. Do not rebind `a08e6d8d` to Full. Its incomplete CP-6 and reconciliation needs
   remain on that card's workflow. If any candidate branch/report/witness has been
   removed or changed, explain the refused fact and commission only the evidence
   now missing. No blanket success for the historical list.

This plan is complete when committed and pushed. TestDesign remains required
before code execution; historical production recovery is an activation follow-up,
not a database operation performed by this Plan task.
