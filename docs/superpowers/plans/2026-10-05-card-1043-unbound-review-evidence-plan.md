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

## Verification design

TestDesign: 2026-10-05, inspected at
`402e2efcc24ffa6f8432adbab9c5431f1e53ee0a`. This section appends verification
to D-1–D-8 and S1–S5; it does not replace the fix design. **Next: Code.**
New method names below are the required implementation roster, not claims that
tests already exist or passed. Every new method is one non-parameterized
`[Test]`; scenario matrices are internal loops with assertion messages naming
the row. Do not multiply `Min` by those loops. Existing argument expansion is
counted separately.

### Inspection

Paths below are relative to `tests/Antiphon.Tests/` unless prefixed `server/`
or `scripts/`. “Read” means test/helper bodies, not just name searches.

| Bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| `Application/ReviewEvidenceParserTests.cs` (all 14 methods and helpers), `server/Application/Services/ReviewEvidence.cs` | Standalone/duplicate/placement/40-and-64-OID/clean/scope grammar, raw closing token -> V-1/R-1. Stored-body entry does not exist yet. |
| `Application/ReviewEvidenceConsistencyTests.cs` (all bodies), `Application/ReviewEvidenceSettlementTests.cs` (all 11 methods, `SettleAsync`, subject seeding and `AdoptionFixture`) | First-settlement warning-and-bind behavior and real settlement authority -> V-1/V-2/R-1/R-2. Only two consistency methods are selected; its large transport matrices are replaced by CARD-1043 delivery capstones. |
| `Application/StageOutcomeSummaryTests.cs`, `Application/StageOutcomeFindingEndpointTests.cs`, `Application/AgentTaskReviewEvidenceTests.cs` (all bodies) | Latest grain, append-only overrides, HTTP finding and immutable coordinates -> V-1/V-3/R-1/R-3. The C488 parser-only and method-calling-method “atomic” tests are not substitutes for rollback or settlement proof. |
| `Application/RunnerTaskSettlementTests.cs`: `C788_NonCodeNoPushRoleMatrix`, `Runner_sync_block_commits_the_completion_obligation`, `AssertCompletionObligationAsync`, `Sync_uncertainty_blocks_and_reply_retries`, `Sustained_lease_contention_spans_sweeps_without_holding_up_other_settlements`; `TestHelpers/RunnerSettlementWorld.cs` (entire fixture); `Application/RunnerSettlementSyncTests.cs` nested `SyncWorld` setup, exact-ref baseline, runner push, service and disposal helpers | Real lease block, Answer watermark, new turn, unchanged branch, controlled sync clock and real Git -> V-2/R-2. Cumulative-budget redesign/throughput is D-8, excluded; retain existing budgets. |
| `TestHelpers/C544World.cs`, `TestHelpers/C544DeliveryRig.cs` (entire files), `Application/ReviewEvidenceDeliveryTests.cs` (all bodies), `Application/VerificationRoundDeliveryTests.cs` `AssertReceivedOnceAsync` | Settlement/obligation/outbox/flush/frozen rendering/attempt/receipt; busy and eligible callers, spills and restart cuts -> V-6/R-6. Existing single-notification helpers cannot represent this defect without extension. |
| `TestHelpers/LandingSafetyHarness.cs` (entire fixture, including `RequestAsync`, `RunRequestAsync`, restart and fault interceptors); `Application/AgentTaskLandAdoptionTests.cs` first three recovery methods; `Application/AgentTaskLandRecoveryTests.cs` first two recovery methods and worker-death setup; `TestHelpers/LandContractWebAppFactory.cs` | Real Git, DB, v2 HTTP admission and publication -> V-7/R-7. Controlled verifier cannot prove an external verifier's implementation; no landing engine redesign. |
| `TestHelpers/AntiphonWebAppFactory.cs`, `ApiKeys/DelegationCapabilityApiTests.cs`, `Application/AgentTaskRetryCommitRecoveryEndpointTests.cs` | Real Program, HTTP Problem Details, isolated operator credential and refused production runner -> V-4/R-4. Named recovery endpoint and its strict principal authorization must be added. |
| `Scripts/ScriptHarness.cs`, `Scripts/CapabilityScriptTests.cs`, `Scripts/RunnerDrainScriptTests.cs`, `scripts/runner-drain.ps1` | Credential readers, offline HTTP/script harness, ASCII, literal selector/reason transport -> V-5/R-5. Do not copy CapabilityScriptTests' Windows-only DPAPI runner for this OS-neutral command. |
| `TestHelpers/TestDbFixture.cs`, `TestHelpers/TestDbFixtureLifecycle.cs` creation/start/migrate/clone/drop bodies | PostgreSQL 16-alpine Testcontainers; “IsolatedTestSchema” actually clones a database. All concurrency and rollback proofs use independent Npgsql connections, never EF InMemory or SQLite. |
| `server/Application/Services/AgentTaskReplyService.cs` `SettleAsync`, `RecordDelegateStageOutcomeAsync`, surrounding persistence/obligation call sites; entire `LandCompletionFacts.cs`, `StageOutcomeService.cs`, `LandApproval.cs`; `AgentTaskService.cs` caller/authentication; `server/Api/Endpoints/AgentTaskEndpoints.cs` finding, land/v2 and caller resolution; `server/Infrastructure/Security/OperatorCredential.cs` | Confirms one-shot early return, Clean-first read, subject grouping, unsafely permissive tokenless manual caller for this new operation, and unchanged land refusals -> V-1–V-4/V-7 and R-1–R-4/R-7. |

Owner contracts read: `docs/project-context.md`, relevant landing/Final and
stage handoff sections of `docs/orchestration-loop.md`, checkpoint/filter/slot/
mutation/delivery sections of `docs/testing-and-build.md`, profile-v1 completion
in `docs/session-runtime-invariants.md`, and operator/capability sections of
`docs/ops-http.md` and `docs/agent-credentials.md`.

Missing setup to implement, in the owning slice:

- **S1:** Add the binding/typed-provenance test entry points and real exact-ref
  observations. `C544World` has no bare origin or captured subject endpoint;
  its local branch alone is not a fresh remote witness. Use `SyncWorld` topology
  or an attached `LandingSafetyHarness` for positive fresh-ref cases. Controlled
  `ITaskProgressGit` observations cover invalid states and 64-character OIDs;
  real scratch Git covers the ordinary 40-character publication path.
- **S2:** Extend `RunnerSettlementWorld` to seed a same-card/project Worktree Code
  subject at the reviewed tip and a profile-v1 Review with `Stage=Review`.
  Register the new concrete services in every hand-built graph. Expose provider
  restart and interceptor registration without changing existing default tests.
  Hold the real common-directory lease, advance the fake sync clock only after
  its observed busy signal, settle Blocked, call real `AnswerAsync`, and seed a
  new owning UserPrompt/report/TurnEnd. An old done token must leave it Working.
  Do not directly set Succeeded in this incident reproduction.
- **S3:** Use separate service providers and connections over one isolated database
  for races, because one reply-service instance has a per-session gate. Add
  deterministic preparation/lock/insert barriers via test-side Git and EF command/
  transaction interceptors, plus one-shot save/commit failures. Prove row-lock
  ownership with an independent `SELECT ... FOR UPDATE NOWAIT` probe; merely
  observing one row after two serialized calls is insufficient. Bound waits to
  30 seconds and release barriers in `finally`. Source/Git callbacks record
  whether a finalization DB transaction is held. No global concurrency-token
  mapping change, process-local substitute lock, timing sleeps or retry-to-green.
- **S3/S5 HTTP:** Derive an owned-database factory from
  `AntiphonWebAppFactory`, copying the hosted-land-drain exclusions from the
  sealed `LandContractWebAppFactory`; override its connection and scoped graph
  to attach to the fixture database/repository. Use actual endpoint routing,
  credential readers and JSON binding. Keep the refusing session runner and
  disable competing hosted land drains. Read only this factory's synthetic
  operator token; generate task/capability canaries for the other principal rows.
- **S4:** Add an offline loopback HTTP capture harness callable through
  `ScriptHarnessProcess` custody. Run the actual new script. Stub GET detail,
  history and rebind responses, capture request bodies/headers in memory and
  redact bearer values from assertion diagnostics. No live endpoint, DPAPI,
  provider session or Windows-native behavior is needed.
- **S5:** Extend delivery helpers with overloads keyed by
  `(taskId, SourceEventId)` or notification ID. Keep old overloads unchanged.
  `NotificationAsync(taskId)` currently uses SingleOrDefault and
  `AssertReceivedOnceAsync` currently requires one queue row for the whole task;
  neither can verify two settlements. Fault cuts must target the **second**
  event/obligation, with fresh one-shot interceptors, not the first block.
  Add a receiver assertion joining notification -> queue -> frozen delivery ->
  matching caller UserPrompt. No test inserts the expected receipt itself.
  Add an attachment/composition seam joining the delivery rig to S2's real
  runner Review/Code-subject graph; its current default ReadOnly Review cannot
  reproduce a runner-sync block. Pass `reviewedSourceClean: true` explicitly:
  the current delivery helper omits that line, which is adequate for its existing
  invalid-evidence cases but cannot create this plan's repaired approval.

Isolation rows needed for decisive PCs: the bound-row recovery method includes
both the actual bound-Interim/stored-Interim incident shape and a bound-Interim
row with an otherwise valid stored Full report. The latter isolates G-49 from
the independent Full guard. Landing negatives change exactly one field of an
otherwise genuinely rebound row; the no-fallback row uses an unlatched Succeeded
owner so ExplicitCaller would otherwise be eligible. Lock/lease instrumentation
records violations and the test asserts them after the call; a swallowed callback
exception or deadlock timeout is not an intended red.

These are implementable fixture extensions at inspected seams; no unverifiable
production seam or human design choice is left for another Plan stage. This
TestDesign checkout has PowerShell but has not qualified a .NET/PostgreSQL/Docker
execution lane. Code must preflight the chosen eligible host, not assume this
documentation run proves availability.

Boundary matrix: use both Synchronized and confirmed NoPushedProgress, local
and runner-bound Review, 40/64 and mixed-case OIDs, follow-up and same-card
authorization, current Clean/Found/missing finding, bare/missing/fenced/quoted/
duplicate/after-handoff evidence, clean true/false/missing/malformed/duplicate,
scope Full/Interim/None/Unknown and commissioned Final/Interim. Invalid report
rows append the newly classified unbound outcome as D-2 requires; failed fresh
witness rows must never create bound approval and must report next=decide.
A valid Found successor may carry baseline coordinates but is never an approval.

Single-invalid-field rows isolate every refusal; selected paired rows cover
cached-correct/fresh-wrong, observed-correct/confirmed-missing, same-card/wrong-
follow-up, same-SHA/wrong-ref, matching-refs/missing-historical-sync, and
Full/clean-missing. Do not cross every unrelated invalid field: one independent
refusal per guard plus these authority-precedence combinations is decisive.
Readers cover Clean -> Found, Clean -> unbound, unbound -> bound-with-new-subject,
and a third superseding leaf; identical timestamps and out-of-window successors
must not change active authority. Original rows remain visible in full history.

### Delivery inventory

| Path | Producer -> destination, durable identity | Persistence boundary and recovery | Observable receipt / coverage |
|---|---|---|---|
| Automatic successful Review continuation, including refusal/warning result | `AgentTaskReplyService` -> snapshotted caller session via `AgentTaskLandNotificationService` and real `SessionMessageQueueService`. Join Review ID + new settlement event ID + replacement outcome ID + TaskCompletion ID + queue ID; retain `task:<root>`, raw digest, wire hash and member queue IDs. | Commit final task/result/outcome/event/snapshot/obligation together. Notification scan recovers postcommit/pre-enqueue; keyed enqueue recovers lost acknowledgment; stranded flush recovers lost wakeup; frozen render/attempt and transcript reconciliation recover later cuts. | Complete matching UserPrompt in the recorded destination above the attempt floor, with exact replacement evidence ID/subject/SHA/Final/Full or exact refusal header. V-2/V-6, R-2/R-6; G-32, G-40, G-74–G-91, G-104. |
| First Blocked completion remains pending or was already received when successful continuation settles | Same producer and destination, but first and second **SourceEventId/TaskCompletion ID** remain different even though Review/root IDs match. | Old snapshot/render/receipt never rewritten or borrowed. Exercise first received and first still pending. A batch may confirm both only if its frozen membership and complete prompt carry both full logical notes. | The old prompt alone never confirms the new obligation. Each notification joins to its own rendering/member and complete prompt. V-6/R-6; G-74/G-75. |
| Named stored-report recovery | Authenticated HTTP handler -> requesting HTTP client, joined by named Review, predecessor, exact report SHA-256 and returned successor ID. **Synchronous**; no new session, completion note or async queue. | Commit replacement and audit atomically before response. After a lost response, repeat exact request against fresh services returns already-bound with identical ID. | Actual HTTP response plus independent GET/DB read of that ID and matching provenance. V-3/V-4, R-3/R-4; G-41/G-47/G-50/G-62/G-63/G-69/G-112. |
| Existing land/v2 recovery/adoption consumer | Actual v2 HTTP request -> existing durable land request and landing worker using the new evidence ID. | Use `RunRequestAsync` and fresh approval/source checks; retain owner versus adoption source identity. No new land delivery implementation is introduced. | Independent bare-origin containment/publication receipt proves accepted rebound evidence was usable through execution. This is **publication**, not caller delivery; unchanged land-outcome transport is excluded below. V-7/R-7. |

Delivery cuts are nine separately named methods:
`C1043_CutObligationInsert`, `C1043_CutSettlementCommit`,
`C1043_CutEnqueueInsert`, `C1043_CutEnqueueCommit`,
`C1043_CutWakeup`, `C1043_CutSpillCommit`,
`C1043_CutRenderCommit`, `C1043_CutAttemptCommit`,
`C1043_CutReceiptCommit`. Map respectively to existing fault points
obligation-insert, settled-committed, note-insert, note-committed, wakeup-dropped,
spill-written, render-committed, attempt-committed, prompt-accepted.
Each method runs busy/eligible x raw-inline/distilled-spill (four internal rows).
Assert the cut fired once, inspect committed state through a fresh context,
dispose/recreate services, advance only the fixture clock, and finish at the
matching complete recipient UserPrompt. Before-commit failure repeats the actual
final turn after rollback; after-commit failure must not mint another settlement.
No cut may pass only on a request, event, queue insert, Sent flag or ack.

Receipt methods also cover raw-inline, distilled-inline, polled-inline and
raw-spill, busy and eligible (eight internal rows). For distillation, compare the
frozen header and full rendered summary; the original raw report is retained in
the snapshot and is not required to appear inline. For a spill, require the
complete pointer prompt **and** matching file hash/content carrying the header.
A wrong-session, wrong-kind, below/at-floor, truncated, header-only or ID-only
candidate leaves ConfirmingPromptSequence null. Missing/tampered spill never
confirms.

Substitutes: the fake terminal adapter writes transcript entries only from actual
queue submissions; this proves server-to-adapter receipt and correlation, not
Claude/Codex native terminal behavior. Seeded delegate turns stand in for a
provider report, not for caller receipt. One-shot persistence exceptions followed
by full provider/context restart model crash persistence cuts, not OS child
custody or machine power loss. Controlled invalid Git observations prove branch
selection/validation, not transport correctness; real bare-origin positives and
moved-ref negatives supply that integration evidence. Controlled land verification
proves approval integration/publication, not the implementation of a native build
verifier. None of these substitutes permits stopping before recipient evidence.


New-test shorthand used below (all files must be created in the plan's named locations):

| Shorthand | Class | New non-parameterized methods |
|---|---|---:|
| B | `ReviewEvidenceRebindingTests` | 15 |
| S | `ReviewEvidenceResettlementTests` | 9 |
| R | `ReviewEvidenceRecoveryTests` | 21 |
| E | `ReviewEvidenceRecoveryEndpointTests` | 6 |
| C | `ReviewEvidenceRecoveryScriptTests` | 4 |
| D | `ReviewEvidenceRebindingDeliveryTests` | 16 |
| L | `ReviewEvidenceRebindingLandingTests` | 6 |

### Proves it works now

- V-1: Shared parser/binder, exact witnesses, active readers and bounded provenance |
  unit plus PostgreSQL/read model and scratch Git |
  all 15 methods in `ReviewEvidenceRebindingTests`, existing parser/summary roster
  in CP-1 and warning semantics in CP-2 |
  authoritative candidate tuple, explicit refusal, deterministic active leaf,
  preserved history; assertion labels are in the PC table.
- V-2: Actual Blocked -> Answer -> new turn -> Succeeded |
  production settlement with PostgreSQL, real lease and scratch origin |
  all nine `ReviewEvidenceResettlementTests` methods, CP-3–CP-5 |
  final report owns subject/SHA/ref/repository/clean/scope/finding; original row
  and first snapshot unchanged; new ID agrees across fresh GET and snapshot.
  `C1043_FinalReportWins` includes a different valid same-card subject and SHA,
  with no follow-up constraint; follow-up mismatch is separately refused.
  `C1043_ConfirmedNoPushBinds` runs confirmed unchanged and synchronized positives.
- V-3: Named historical recovery, audit, idempotence and serialization |
  production services + PostgreSQL + scratch Git |
  all 21 `ReviewEvidenceRecoveryTests` methods, CP-6/CP-8 |
  exact report-derived replacement and provenance; repeat after restart returns
  same ID; two recovery requests converge; stale recovery loses to manual Found
  through either Review-target or Code-target finding route. Test both possible
  winner schedules; a later manual Found may supersede a completed recovery,
  never form a sibling. Automatic/recovery race is also a row in
  `C1043_ConcurrentRecovery`.
- V-4: Authorized strict HTTP recovery |
  real Program TestServer and isolated PostgreSQL |
  all six `ReviewEvidenceRecoveryEndpointTests` methods plus finding regressions,
  CP-7 |
  operator, same-scope orchestrator task, standing-session and capability
  positives; unauthorized/worker/wrong-project/root/extra-field negatives.
  Success response IDs/digest equal fresh GET and DB. Lost HTTP response reuses
  the same ID. Unknown-field tests send raw JSON, not a DTO that drops fields.
- V-5: Recovery command |
  real PowerShell script through owned offline HTTP harness |
  all four `ReviewEvidenceRecoveryScriptTests` methods, CP-9 |
  exact selectors/reason, GET-only WhatIf, one explicit POST, nonzero conflict,
  ASCII source, no token disclosure and no land/dispatch side effect.
- V-6: Complete caller receipt of the new outcome across every handoff |
  production settlement/outbox/queue, PostgreSQL and isolated fake terminal |
  all 16 `ReviewEvidenceRebindingDeliveryTests` methods, CP-10/CP-11 |
  complete joined UserPrompt for both caller states and nine recovery cuts;
  distinct first/second completion obligations, immutable old receipt.
- V-7: Rebound evidence reaches recovery-owner and adoption publication |
  actual `POST /api/agent-tasks/{id}/land/v2`, PostgreSQL and real scratch Git |
  all six `ReviewEvidenceRebindingLandingTests` methods, CP-12 |
  new Final/Full ID admitted for Failed/Blocked owner and for the actual adoption
  source; run durable request to publication and independently inspect origin.
  Each positive method runs automatic-rebind and named-recovery evidence.
  Refused variants create no land request or, for a post-admission source move,
  no target advance/publication.

### Guards the regression

- R-1: Preserve ordinary first-settlement behavior while repairing reader grain |
  `C788_SubjectTipMismatchWarnsAndBinds` (remote/primary = 2 executions),
  `C788_ReviewBaseMismatchWarnsAndBinds` (40/64 = 2),
  all 14 parser methods (21 executions), all five summary methods and new B
  methods | mismatch still binds with exactly its warning; raw/stored parser
  boundaries differ intentionally; Found/unbound successor cannot resurrect Clean.
- R-2: The old early return, old report or an unverified SHA cannot authorize |
  S methods and all 11 `ReviewEvidenceSettlementTests` methods;
  `RunnerTaskSettlementTests.Sync_uncertainty_blocks_and_reply_retries`,
  `Runner_sync_block_commits_the_completion_obligation`,
  `C788_NonCodeNoPushRoleMatrix` (four roles) |
  real first unbound row followed by an exact linked final row; continuing failure
  remains Blocked/Decide and no approval; old turn does not settle a fresh reply.
- R-3: Historical recovery is neither a generic override nor a partial write |
  R methods; `AgentTaskReviewEvidenceTests.C488_ManualFindingFieldsRestricted`
  and `C488_OverrideDoesNotCopyApproval` |
  typed conflict leaves complete old state, generic findings copy no approval or
  Full scope, transaction failures leave no successor/audit/obligation fragment,
  independent row-lock probes and race schedules prove DB serialization.
- R-4: New HTTP path cannot inherit anonymous manual-caller permission |
  E methods and all three `StageOutcomeFindingEndpointTests` methods |
  403 with no successor for unauthorized principals; strict selectors; actual
  route returns only committed identity; existing finding route keeps its behavior.
- R-5: Preview, stale digest and synthetic credentials cannot cause hidden writes |
  C methods | zero POSTs in WhatIf, exactly supplied digest, nonzero 409, no secret
  or follow-on land request in captured traffic/output.
- R-6: A previous receipt or partial prompt is not this settlement's receipt |
  D methods | immutable first bytes; second event/outcome/notification/queue/member
  tuple joins to complete destination UserPrompt above floor, or remains
  unconfirmed. All nine cuts finish at that same recipient assertion.
- R-7: Rebinding does not weaken land approval |
  L methods | original unbound ID refused, a superseded **bound** successor gets
  `review_evidence_superseded`; false/null clean, incomplete scope, Found, wrong
  subject/SHA/ref/repository and fresh-ref movement refuse. The bound-successor
  case is essential: an unbound predecessor alone stops at the earlier
  `review_evidence_ineligible` check and cannot exercise supersession.

### Guard inventory

One distinct PC per guard. Multiple guards may share a method with isolated
matrix rows, but no PC ID protects two guards. These are the safety assertions
relied on by this change and its touched delivery/land consumers; unrelated
landing, provider and process-custody machinery is excluded, not silently
claimed. All listed guards require execution after land; none is waived.

| Guard | Plan reference and invariant | Positive control |
|---|---|---|
| G-1 | D-1: Only Review-role continuations replace rows | PC-1 |
| G-2 | D-1: Only Review-stage continuations replace rows | PC-2 |
| G-3 | D-1/D-3: An unsuccessful current settlement cannot bind | PC-3 |
| G-4 | D-1: An orchestrator predecessor is never automatically overwritten | PC-4 |
| G-5 | D-1: A bound predecessor is never automatically rebound | PC-5 |
| G-6 | D-1/D-4: A superseded predecessor cannot gain a sibling replacement | PC-6 |
| G-7 | D-1: Replacement uses the current report finding and coordinates | PC-7 |
| G-8 | D-2: Original outcome bytes, cost and timestamp remain immutable | PC-8 |
| G-9 | D-2: Replacement revokes the predecessor through SupersedesId | PC-9 |
| G-10 | D-2/D-3: Only usable standalone evidence can supply coordinates | PC-10 |
| G-11 | D-3: Only normalized full object IDs can bind | PC-11 |
| G-12 | D-3/D-5: Clean source must be explicitly true for repaired approval | PC-12 |
| G-13 | D-2/D-3: Missing or malformed scope never becomes Full | PC-13 |
| G-14 | D-3: Commissioned Interim caps declared Full | PC-14 |
| G-15 | D-5: Stored-body parsing does not cut at an embedded report token | PC-15 |
| G-16 | D-5: Raw settlement still obeys its closing-token boundary | PC-16 |
| G-17 | D-3: Subject must exist and be a Worktree task | PC-17 |
| G-18 | D-3: A follow-up cannot name a different same-card subject | PC-18 |
| G-19 | D-3: A non-follow-up subject needs same-card authority | PC-19 |
| G-20 | D-3: Review and subject repository identities agree | PC-20 |
| G-21 | D-3: Evidence ref is taken from the named subject | PC-21 |
| G-22 | D-3: Current sync must confirm the claimed full SHA | PC-22 |
| G-23 | D-3: Only confirmed supported sync states are witnesses | PC-23 |
| G-24 | D-3: A dirty mirror cannot witness a repair | PC-24 |
| G-25 | D-3: Sync witness belongs to the Review's exact branch | PC-25 |
| G-26 | D-3: Confirmed unchanged Reviews are recoverable | PC-26 |
| G-27 | D-3: Subject's fresh exact-ref observation must be Present | PC-27 |
| G-28 | D-3: Fresh subject tip must equal the claimed SHA | PC-28 |
| G-29 | D-3: Captured endpoint identity must survive observation | PC-29 |
| G-30 | D-3/D-4: Leased observation must not reacquire the same lease | PC-30 |
| G-31 | D-2: DB reader selects active leaves before filtering Clean | PC-31 |
| G-32 | D-2: Settlement's tracked reader selects the replacement | PC-32 |
| G-33 | D-2: Summary removes predecessors before subject grouping | PC-33 |
| G-34 | D-2: Filtered reads consult successors outside their range | PC-34 |
| G-35 | D-2: Equal timestamps have deterministic ID ordering | PC-35 |
| G-36 | D-4: Cross-service settlement writers share a database lock | PC-36 |
| G-37 | D-4: Lock excludes external Git/RPC/file/queue work | PC-37 |
| G-38 | D-4: Changed report invalidates a prepared candidate | PC-38 |
| G-39 | D-4: Changed prepared subject invalidates the candidate | PC-39 |
| G-40 | D-4: Settlement commits outcome/status/event/snapshot atomically | PC-40 |
| G-41 | D-4/D-5: Recovery repeat is durable and returns the same new ID | PC-41 |
| G-42 | D-4: Two recovery writers serialize on the Review row | PC-42 |
| G-43 | D-4: A finding targeting Review serializes with recovery | PC-43 |
| G-44 | D-4: A finding targeting Code also locks the predecessor Review | PC-44 |
| G-45 | D-4: Multiple task locks use stable GUID order | PC-45 |
| G-46 | D-4: A changed predecessor lock identity forces re-preparation | PC-46 |
| G-47 | D-4: Failed recovery audit/outcome writes roll back together | PC-47 |
| G-48 | D-5: Recovery names the exact active evidence row owned by Review | PC-48 |
| G-49 | D-5: Already-bound historical rows are not upgraded | PC-49 |
| G-50 | D-5: Digest hashes exact UTF-8 stored bytes before parsing | PC-50 |
| G-51 | D-5: Recovery requires a retained nonempty report | PC-51 |
| G-52 | D-5: Recovery requires profile version 1 | PC-52 |
| G-53 | D-5: Recovery requires Succeeded current status | PC-53 |
| G-54 | D-5: Recovery requires commissioned Final | PC-54 |
| G-55 | D-5: Recovery requires the stored report's Clean finding | PC-55 |
| G-56 | D-5: Recovery requires declared/capped Full | PC-56 |
| G-57 | D-3/D-5: Stored successful Review sync is corroborated | PC-57 |
| G-58 | D-3/D-5: Recovery freshly observes Review as well as subject | PC-58 |
| G-59 | D-5: Recovery neither syncs nor publishes source | PC-59 |
| G-60 | D-6: Provenance comes from the successful reported-completion event | PC-60 |
| G-61 | D-6: Bounded typed provenance is complete, never clipped | PC-61 |
| G-62 | D-6: Recovery audit records IDs, digest, observations and authenticated actor | PC-62 |
| G-63 | D-5: Explicit recovery preserves task and completion history | PC-63 |
| G-64 | D-5: No tokenless/manual or invalid credential can recover | PC-64 |
| G-65 | D-5: Worker tokens cannot recover | PC-65 |
| G-66 | D-5: Delegation principal is confined to project scope | PC-66 |
| G-67 | D-5: Delegation principal is confined to repository roots | PC-67 |
| G-68 | D-5: Request cannot override report coordinates or actor | PC-68 |
| G-69 | D-5: HTTP response follows durable commit and is repeatable | PC-69 |
| G-70 | D-5: Preview performs no recovery mutation | PC-70 |
| G-71 | D-5: CLI sends exact named selectors and literal reason-file bytes | PC-71 |
| G-72 | D-5: CLI rejects failures and never automatically lands | PC-72 |
| G-73 | D-5: CLI never reveals bearer credentials | PC-73 |
| G-74 | D-4/D-6: Each settlement owns a distinct durable completion identity | PC-74 |
| G-75 | D-2/D-4: Prior delivered snapshot is immutable | PC-75 |
| G-76 | D-4: Busy caller receives no input before becoming eligible | PC-76 |
| G-77 | D-4: Failed obligation insert cannot commit a repaired settlement | PC-77 |
| G-78 | D-4: Committed settlement survives death before enqueue | PC-78 |
| G-79 | D-4: Enqueue failure leaves retryable durable obligation | PC-79 |
| G-80 | D-4: Enqueue commit before acknowledgment reuses keyed row | PC-80 |
| G-81 | D-4: Lost flush wakeup is recovered without a new settlement | PC-81 |
| G-82 | D-4: Spill-before-render-commit failure safely re-renders | PC-82 |
| G-83 | D-4: Frozen rendering survives postcommit restart | PC-83 |
| G-84 | D-4: Prompt accepted before delivery verdict does not duplicate typing | PC-84 |
| G-85 | D-4: Prompt accepted before notification confirmation is rediscovered | PC-85 |
| G-86 | D-4: Header/ID/prefix is insufficient receipt | PC-86 |
| G-87 | D-4: Receipt must belong to destination session | PC-87 |
| G-88 | D-4: Receipt must be UserPrompt, not queued/assistant text | PC-88 |
| G-89 | D-4: Receipt must be newer than the attempt floor | PC-89 |
| G-90 | D-4: Pointer receipt requires matching spill-file content | PC-90 |
| G-91 | D-2/D-3: Found or invalid final evidence never resurrects old Clean in delivery | PC-91 |
| G-92 | D-3/D-5: Recovery-owner land consumes new subject evidence | PC-92 |
| G-93 | D-3/D-5: Adoption uses report subject, not original landing owner | PC-93 |
| G-94 | D-2: Superseded approval cannot be used for land | PC-94 |
| G-95 | D-3: Land rechecks current exact source SHA | PC-95 |
| G-96 | D-3: Rebound approval cannot bypass clean-source assertion | PC-96 |
| G-97 | D-3: Rebound approval cannot bypass Final/Full scope | PC-97 |
| G-98 | D-3: Rebound approval cannot bypass landing subject identity | PC-98 |
| G-99 | D-3: Rebound approval cannot bypass landing SHA identity | PC-99 |
| G-100 | D-3: Rebound approval cannot bypass landing ref identity | PC-100 |
| G-101 | D-3: Rebound approval cannot bypass landing repository identity | PC-101 |
| G-102 | D-3: Explicit rejected evidence cannot fall back to caller approval | PC-102 |
| G-103 | D-2/D-5: Generic finding still cannot manufacture Final/Full | PC-103 |
| G-104 | D-1/D-3: Current sync failure retains Blocked/Decide | PC-104 |
| G-105 | D-4: Changed prepared subject ref invalidates the candidate | PC-105 |
| G-106 | D-4: Changed prepared repository invalidates the candidate | PC-106 |
| G-107 | D-5: Named predecessor must belong to the named Review | PC-107 |
| G-108 | D-5: Named predecessor must have Review stage | PC-108 |
| G-109 | D-5: Named predecessor must be Delegate evidence | PC-109 |
| G-110 | D-3/D-5: Duplicate standalone blocks cannot create approval | PC-110 |
| G-111 | D-3/D-5: Evidence after the next-stage block is not authority | PC-111 |
| G-112 | D-5: Synchronous recovery response derives actor from credential | PC-112 |
| G-113 | D-3: Final Found evidence is a baseline, never land approval | PC-113 |
| G-114 | D-3: A failed fresh repair witness forces a Decide handoff without an approval header | PC-114 |

### Positive controls

**Code** authors every named method and runs V/R. **Review** judges this inventory,
the implementation and ordinary evidence before land. **Mutation**, after
confirmed publication on an exact SourceLanding snapshot, runs all PCs:
method-scoped baseline-green, compile the defect, intended assertion-red, restore
the exact bytes, fresh-build and method-scoped green. Never mutate the test or
its expected value. No compile error, zero execution, setup exception or timeout
is intended red.

In the table, each method is its literal `Class.Method`. The exact phase filter
is `/*/*/<Class>/<Method>`, substituting that row's two literal components;
all new methods are non-parameterized, so no suffix wildcard is needed. Each
phase requires `-MinExecuted 1`, the full method in `-Expect`, a new isolated
`bin-c1043-pc<N>-<phase>/` output and external evidence directory. Use the
copied, unmodified checkpoint driver and host slot gate per the testing owner.
The “assertion” cell is an exact required Shouldly message label followed by its
decisive value. Mutation must report that label in the red failure.

Defects name the smallest production condition/assignment to change. Code must
keep the named boundaries testable; if a condition is factored differently,
Mutation applies the same semantic compiling defect at its landed implementation,
not a broad bypass of the whole validator. Each failure uses an otherwise-valid
fixture so earlier refusals cannot mask it. For timeout-sensitive lease/lock PCs,
assert a recorded nested-acquire/lock-probe violation before any wait timeout.
Cross-service races must not rely on the same singleton's session gate.

| PC | Break | Exact method | Expected red assertion | Cycle minutes |
|---|---|---|---|---:|
| PC-1 | G-1: remove the Role == Review conjunct | `ReviewEvidenceResettlementTests.C1043_AutomaticEligibility` | `G1`: role: predecessor ID unchanged | 7 |
| PC-2 | G-2: remove the Stage == Review conjunct | `ReviewEvidenceResettlementTests.C1043_AutomaticEligibility` | `G2`: stage: predecessor ID unchanged | 7 |
| PC-3 | G-3: remove the current Status == Succeeded conjunct from repair eligibility; use a local report-token failure so sync does not mask this guard | `ReviewEvidenceResettlementTests.C1043_AutomaticEligibility` | `G3`: failed/blocked current report: no bound successor | 7 |
| PC-4 | G-4: remove the predecessor Source == Delegate check | `ReviewEvidenceResettlementTests.C1043_AutomaticEligibility` | `G4`: orchestrator: predecessor ID unchanged | 7 |
| PC-5 | G-5: treat a populated ReviewedSourceSha as unbound | `ReviewEvidenceResettlementTests.C1043_AutomaticEligibility` | `G5`: bound: row count remains 1 | 7 |
| PC-6 | G-6: select the old unbound delegate row without excluding rows already superseded | `ReviewEvidenceResettlementTests.C1043_AutomaticEligibility` | `G6`: superseded: no sibling of the existing successor | 7 |
| PC-7 | G-7: restore the old AnyAsync early return when an outcome exists | `ReviewEvidenceResettlementTests.C1043_FinalReportWins` | `G7`: new row subject/SHA/finding equal final report, never first report | 7 |
| PC-8 | G-8: update the predecessor Detail to the final finding while appending | `ReviewEvidenceResettlementTests.C1043_AppendPreservesHistory` | `G8`: original row equals pre-continuation field snapshot | 7 |
| PC-9 | G-9: assign null to the new row SupersedesId | `ReviewEvidenceResettlementTests.C1043_AppendPreservesHistory` | `G9`: replacement.SupersedesId equals original.Id | 7 |
| PC-10 | G-10: strip Markdown fence lines before parsing the binding report, admitting its fenced coordinates | `ReviewEvidenceRebindingTests.C1043_StandaloneAndOid` | `G10`: fenced/quoted/duplicate/after-handoff rows have no usable candidate | 6 |
| PC-11 | G-11: replace full-object-ID validation with nonempty-string acceptance | `ReviewEvidenceRebindingTests.C1043_StandaloneAndOid` | `G11`: short/dirty-suffix SHA is refused; 40/64 uppercase normalizes | 6 |
| PC-12 | G-12: replace reviewedSourceClean == true with != false, specifically admitting the missing-clean row | `ReviewEvidenceRebindingTests.C1043_CleanAndScope` | `G12`: false/missing/duplicate-clean: approval candidate absent | 6 |
| PC-13 | G-13: default unknown parsed scope to Full | `ReviewEvidenceRebindingTests.C1043_CleanAndScope` | `G13`: missing/duplicate/numeric scope = Unknown | 6 |
| PC-14 | G-14: make CapToRound return declared unconditionally | `ReviewEvidenceRebindingTests.C1043_CleanAndScope` | `G14`: commissioned-Interim candidate scope = Interim | 6 |
| PC-15 | G-15: route stored-body parsing through TextBeforeClosingReportToken | `ReviewEvidenceRebindingTests.C1043_StoredBodyTokenBoundary` | `G15`: evidence after embedded token is retained from exact stored body | 6 |
| PC-16 | G-16: make raw-report parsing use the stored-body no-cut entry point | `ReviewEvidenceRebindingTests.C1043_RawTokenBoundary` | `G16`: evidence after final closing token is absent | 6 |
| PC-17 | G-17: omit the Workspace == Worktree check | `ReviewEvidenceRebindingTests.C1043_SubjectAuthorization` | `G17`: missing/Shared/ReadOnly subject yields no bound candidate | 6 |
| PC-18 | G-18: omit the follow-up identity check | `ReviewEvidenceRebindingTests.C1043_SubjectAuthorization` | `G18`: follow-up mismatch is refused even on same card | 6 |
| PC-19 | G-19: allow any existing Worktree subject | `ReviewEvidenceRebindingTests.C1043_SubjectAuthorization` | `G19`: foreign/no-card subject is refused | 6 |
| PC-20 | G-20: bypass the repository identity equality | `ReviewEvidenceRebindingTests.C1043_SubjectRepositoryAndRef` | `G20`: cross-repository same-card subject is refused | 6 |
| PC-21 | G-21: assign the Review branch as ReviewedSourceRef | `ReviewEvidenceRebindingTests.C1043_SubjectRepositoryAndRef` | `G21`: ReviewedSourceRef equals subject ref, differs from Review and adoption owner refs | 6 |
| PC-22 | G-22: compare ObservedSha instead of ConfirmedSha; observed-only fixture has the correct claim | `ReviewEvidenceRebindingTests.C1043_CurrentSyncWitness` | `G22`: observed-only/null/wrong-confirmed SHA is refused | 6 |
| PC-23 | G-23: remove the sync-state allowlist while retaining SHA comparison | `ReviewEvidenceRebindingTests.C1043_CurrentSyncWitness` | `G23`: Unavailable/Refused/unknown state with correct SHA is refused | 6 |
| PC-24 | G-24: ignore the MirrorDirty field | `ReviewEvidenceRebindingTests.C1043_CurrentSyncWitness` | `G24`: dirty=true with correct confirmed SHA is refused | 6 |
| PC-25 | G-25: skip the witness branch equality | `ReviewEvidenceRebindingTests.C1043_CurrentSyncWitness` | `G25`: wrong-branch witness with correct SHA is refused | 6 |
| PC-26 | G-26: accept only Synchronized in the confirmed-state check | `ReviewEvidenceResettlementTests.C1043_ConfirmedNoPushBinds` | `G26`: NoPushedProgress produces a new Final/Full evidence ID | 7 |
| PC-27 | G-27: accept non-Present observations using the cached subject tip | `ReviewEvidenceRebindingTests.C1043_FreshSubjectWitness` | `G27`: Missing/Unavailable with cached matching SHA has no candidate | 6 |
| PC-28 | G-28: compare only cached CompletionProgressEvidenceJson | `ReviewEvidenceRebindingTests.C1043_FreshSubjectWitness` | `G28`: moved ref refuses despite matching cached completion SHA | 6 |
| PC-29 | G-29: pass null instead of the captured expected fingerprint | `ReviewEvidenceRebindingTests.C1043_EndpointWitness` | `G29`: changed/missing endpoint fingerprint refuses, no alternate ref used | 6 |
| PC-30 | G-30: call ObserveExactRefAsync instead of ObserveExactRefUnderLeaseAsync in the already-leased branch | `ReviewEvidenceRebindingTests.C1043_ObservationUsesExistingLease` | `G30`: nested acquire count=0 and candidate completes while test holds lease | 6 |
| PC-31 | G-31: restore the pre-supersession Clean-first LoadReviewAsync query | `ReviewEvidenceRebindingTests.C1043_ActiveReaders` | `G31`: Clean -> Found/unbound successor yields null approval | 6 |
| PC-32 | G-32: return the first tracked Review outcome instead of active replacement | `ReviewEvidenceResettlementTests.C1043_HeaderSnapshotAndGetAgree` | `G32`: snapshot/header/GET evidence IDs equal new row ID | 7 |
| PC-33 | G-33: group by SubjectTaskId before removing superseded rows | `ReviewEvidenceRebindingTests.C1043_ActiveSummary` | `G33`: unbound Review -> bound Code subject contributes one run | 6 |
| PC-34 | G-34: restrict successor lookup to the already filtered rows | `ReviewEvidenceRebindingTests.C1043_FilteredSummary` | `G34`: latestOnly date/card window cannot resurrect predecessor | 6 |
| PC-35 | G-35: replace ThenByDescending(Id) with ThenBy(Id) | `ReviewEvidenceRebindingTests.C1043_EqualTimestampOrdering` | `G35`: repeated fresh reads select prescribed descending-ID leaf | 6 |
| PC-36 | G-36: skip task-row lock acquisition for automatic repair | `ReviewEvidenceResettlementTests.C1043_SettlementsSerialize` | `G36`: competing connection cannot acquire Review row lock; exactly one final event/obligation | 7 |
| PC-37 | G-37: move candidate exact-ref observation into the locked transaction | `ReviewEvidenceResettlementTests.C1043_PreparationBeforeLock` | `G37`: all instrumented external operations see no held finalization transaction | 7 |
| PC-38 | G-38: omit the under-lock report-digest equality | `ReviewEvidenceRecoveryTests.C1043_PreparedReportChanged` | `G38`: 409 and no successor after Result changes at preparation barrier | 7 |
| PC-39 | G-39: omit subject ID from the under-lock prepared-coordinate comparison | `ReviewEvidenceRecoveryTests.C1043_PreparedCoordinatesChanged` | `G39`: changed subject gives 409 and no successor | 7 |
| PC-40 | G-40: save the new outcome in a separate committed context before finalization | `ReviewEvidenceResettlementTests.C1043_SettlementRollback` | `G40`: each precommit cut leaves only original outcome and first obligation | 7 |
| PC-41 | G-41: treat a matching already-completed recovery as conflict instead of returning its successor | `ReviewEvidenceRecoveryTests.C1043_RepeatAfterRestart` | `G41`: one successor/audit; second response disposition=already-bound with same ID | 7 |
| PC-42 | G-42: skip recovery task-row lock | `ReviewEvidenceRecoveryTests.C1043_ConcurrentRecovery` | `G42`: second writer cannot acquire Review lock; both responses name one successor | 7 |
| PC-43 | G-43: omit the predecessor re-read in recovery after manual Found commits | `ReviewEvidenceRecoveryTests.C1043_OverrideReviewWins` | `G43`: stale prepared recovery returns 409 and manual Found stays the only active leaf | 7 |
| PC-44 | G-44: lock only the requested Code task in RecordFindingAsync | `ReviewEvidenceRecoveryTests.C1043_OverrideSubjectWins` | `G44`: Review lock is held by Code-target finding; no sibling successor | 7 |
| PC-45 | G-45: iterate requested task before predecessor task without sorting | `ReviewEvidenceRecoveryTests.C1043_LockOrder` | `G45`: observed lock IDs ascend for both reversed-GUID fixtures; competing operations finish | 7 |
| PC-46 | G-46: ignore changed StageTaskId after candidate-predecessor re-read | `ReviewEvidenceRecoveryTests.C1043_PredecessorIdentityChanged` | `G46`: writer re-prepares under new Review ID and never appends under old lock | 7 |
| PC-47 | G-47: commit successor before adding the recovery audit | `ReviewEvidenceRecoveryTests.C1043_RecoveryRollback` | `G47`: old row only and zero new FindingRecorded after insert/audit/precommit faults | 7 |
| PC-48 | G-48: select latest outcome instead of enforcing request evidenceId | `ReviewEvidenceRecoveryTests.C1043_NamedPredecessor` | `G48`: foreign task/wrong stage/wrong source/wrong ID/superseded row gives 409 with no write | 7 |
| PC-49 | G-49: admit any bound Delegate row into recovery; report remains otherwise Full so bound-row guard is isolated | `ReviewEvidenceRecoveryTests.C1043_BoundInterimIsNotRecovery` | `G49`: a08e-shaped bound Interim row stays byte-identical and request fails | 7 |
| PC-50 | G-50: normalize line endings before computing the report hash | `ReviewEvidenceRecoveryTests.C1043_ExactStoredDigest` | `G50`: changed newline/non-ASCII/stale digest gives 409; exact bytes accepted | 7 |
| PC-51 | G-51: fall back to CompletionSnapshotJson.RawResult for absent Result | `ReviewEvidenceRecoveryTests.C1043_RecoveryPrerequisites` | `G51`: absent Result refuses even with valid snapshot text | 7 |
| PC-52 | G-52: treat absent profile as version 1 | `ReviewEvidenceRecoveryTests.C1043_RecoveryPrerequisites` | `G52`: missing/unsupported profile refuses | 7 |
| PC-53 | G-53: omit status check, using retained successful sync as substitute | `ReviewEvidenceRecoveryTests.C1043_RecoveryPrerequisites` | `G53`: Blocked/Failed/Working refuses | 7 |
| PC-54 | G-54: default or promote the commissioned round to Final | `ReviewEvidenceRecoveryTests.C1043_RecoveryPrerequisites` | `G54`: commissioned Interim/null round refuses | 7 |
| PC-55 | G-55: use predecessor.Outcome instead of parsing stored finding | `ReviewEvidenceRecoveryTests.C1043_RecoveryReportAuthority` | `G55`: stored Found/missing finding refuses, old Clean is irrelevant | 7 |
| PC-56 | G-56: remove the completed-scope Full check | `ReviewEvidenceRecoveryTests.C1043_RecoveryReportAuthority` | `G56`: Full accepted; Interim/None/Unknown refuses without inferred Full | 7 |
| PC-57 | G-57: omit stored-sync validation | `ReviewEvidenceRecoveryTests.C1043_StoredSyncRequired` | `G57`: missing/unconfirmed historical sync refuses despite fresh refs matching | 7 |
| PC-58 | G-58: reuse stored ConfirmedSha instead of observing Review ref | `ReviewEvidenceRecoveryTests.C1043_ReviewRefFreshness` | `G58`: moved/missing/retired Review ref refuses while subject still matches | 7 |
| PC-59 | G-59: invoke the full settlement-sync seam before recovery observations | `ReviewEvidenceRecoveryTests.C1043_RecoveryIsReadOnlyToGit` | `G59`: no publish/merge/reset/checkout command or runner sync RPC | 7 |
| PC-60 | G-60: use newest Completed event regardless of origin/time | `ReviewEvidenceRecoveryTests.C1043_ProvenanceEvent` | `G60`: snapshot event or unique matching reported event chosen; ambiguous/missing/merge-only refuses | 7 |
| PC-61 | G-61: truncate the serialized Ref to 1000 characters | `ReviewEvidenceRebindingTests.C1043_ProvenanceBounds` | `G61`: 1000-character record round-trips; 1001 is refused | 6 |
| PC-62 | G-62: omit authenticated actor from both recovery Ref and event | `ReviewEvidenceRecoveryTests.C1043_AuditProvenance` | `G62`: one committed FindingRecorded plus decoded Ref matches authoritative tuple | 7 |
| PC-63 | G-63: append a new TaskCompletion for recovery | `ReviewEvidenceRecoveryTests.C1043_RecoveryPreservesHistory` | `G63`: Result/status/cost/handoff/snapshots/receipt stamps unchanged; no new session/notification | 7 |
| PC-64 | G-64: authorize ResolveCallerAsync's tokenless manual fallback | `ReviewEvidenceRecoveryEndpointTests.C1043_CredentialRequired` | `G64`: anonymous/invalid/revoked credential returns 403 and zero successor | 7 |
| PC-65 | G-65: remove the MayDelegate/principal-kind authorization guard | `ReviewEvidenceRecoveryEndpointTests.C1043_WorkerDenied` | `G65`: valid Worker token returns 403 even for same task/root | 7 |
| PC-66 | G-66: omit project equality authorization | `ReviewEvidenceRecoveryEndpointTests.C1043_ProjectScope` | `G66`: authenticated different-project principal returns 403 | 7 |
| PC-67 | G-67: treat any authenticated capability as root-authorized | `ReviewEvidenceRecoveryEndpointTests.C1043_RepositoryScope` | `G67`: sibling/prefix-trick/unrelated capability root returns 403 | 7 |
| PC-68 | G-68: allow unmapped JSON members instead of rejecting them | `ReviewEvidenceRecoveryEndpointTests.C1043_SelectorsOnly` | `G68`: extra subject/SHA/clean/scope/actor properties each return 400; zero writes | 7 |
| PC-69 | G-69: acknowledge a new evidence ID before saving the successor | `ReviewEvidenceRecoveryEndpointTests.C1043_ResponseAfterCommit` | `G69`: fresh GET/DB sees returned ID; lost-response retry returns same ID | 7 |
| PC-70 | G-70: remove ShouldProcess/WhatIf guard around rebind POST | `ReviewEvidenceRecoveryScriptTests.C1043_PreviewOnly` | `G70`: WhatIf has only GETs and zero POSTs | 6 |
| PC-71 | G-71: recalculate/replace ExpectedReportSha256 from preview instead of sending caller selector | `ReviewEvidenceRecoveryScriptTests.C1043_CommitSelectors` | `G71`: stub receives exact evidence ID/digest/reason; no coordinate override | 6 |
| PC-72 | G-72: retry rebind after conflict using the fresh digest | `ReviewEvidenceRecoveryScriptTests.C1043_ConflictStops` | `G72`: 409 exits nonzero; exactly one rebind POST and zero land/retry/dispatch requests | 6 |
| PC-73 | G-73: write the synthetic header token to output before request | `ReviewEvidenceRecoveryScriptTests.C1043_CredentialsStayPrivate` | `G73`: synthetic bearer absent from stdout/stderr/error/preview; header reaches stub | 6 |
| PC-74 | G-74: deduplicate completion notifications by TaskId instead of TaskId+SourceEventId | `ReviewEvidenceRebindingDeliveryTests.C1043_TwoSettlementReceipts` | `G74`: two SourceEventIds/two obligations; neither receipt can confirm the other | 9 |
| PC-75 | G-75: rewrite the first snapshot header to the new evidence ID | `ReviewEvidenceRebindingDeliveryTests.C1043_SnapshotSurvivesRebind` | `G75`: first snapshot/digest/wire/receipt equal captured bytes after second settlement | 9 |
| PC-76 | G-76: bypass completion queue busy-recipient guard | `ReviewEvidenceRebindingDeliveryTests.C1043_ReceiptBusyAndEligible` | `G76`: zero submitted bodies before busy TurnEnd; complete new wire afterward | 9 |
| PC-77 | G-77: catch obligation-insert failure and commit remaining settlement writes | `ReviewEvidenceRebindingDeliveryTests.C1043_CutObligationInsert` | `G77`: cut reached; old row/status/first obligation only; restart yields complete new UserPrompt | 9 |
| PC-78 | G-78: exclude TaskCompletion from startup notification scan | `ReviewEvidenceRebindingDeliveryTests.C1043_CutSettlementCommit` | `G78`: after restart same obligation reaches complete caller UserPrompt | 9 |
| PC-79 | G-79: mark obligation Confirmed in enqueue exception handler | `ReviewEvidenceRebindingDeliveryTests.C1043_CutEnqueueInsert` | `G79`: cut reached; restart gives one keyed row and complete UserPrompt | 9 |
| PC-80 | G-80: remove completion notification identity from queue deduplication predicate | `ReviewEvidenceRebindingDeliveryTests.C1043_CutEnqueueCommit` | `G80`: recovered QueueMessageId equals existing ID and exactly one complete prompt | 9 |
| PC-81 | G-81: exclude Completion messages from stranded-queue flush | `ReviewEvidenceRebindingDeliveryTests.C1043_CutWakeup` | `G81`: dropped wakeup consumed; stranded scan eventually produces full prompt | 9 |
| PC-82 | G-82: treat existing uncommitted spill file as an already delivered completion | `ReviewEvidenceRebindingDeliveryTests.C1043_CutSpillCommit` | `G82`: rendering absent at cut; recovery delivers pointer and matching spill bytes | 9 |
| PC-83 | G-83: rebuild delivery from current task Result when frozen delivery exists | `ReviewEvidenceRebindingDeliveryTests.C1043_CutRenderCommit` | `G83`: recovered wire/digest equal committed rendering despite later Result rewrite | 9 |
| PC-84 | G-84: skip transcript reconciliation for Sent rows with no verdict and resend | `ReviewEvidenceRebindingDeliveryTests.C1043_CutAttemptCommit` | `G84`: one complete UserPrompt after recovery; same attempt/queue identity | 9 |
| PC-85 | G-85: skip AwaitingReceipt TaskCompletion rows during notification recovery | `ReviewEvidenceRebindingDeliveryTests.C1043_CutReceiptCommit` | `G85`: same confirming prompt sequence after restart; exactly one complete prompt | 9 |
| PC-86 | G-86: replace complete-wire matching with notification-ID/header substring match | `ReviewEvidenceRebindingDeliveryTests.C1043_IncompletePromptIsNotReceipt` | `G86`: Confirmed=false and ConfirmingPromptSequence=null for truncated forms | 9 |
| PC-87 | G-87: remove AgentSessionId predicate from receipt query | `ReviewEvidenceRebindingDeliveryTests.C1043_ReceiptIdentity` | `G87`: complete wire in foreign session does not confirm | 9 |
| PC-88 | G-88: remove Kind == UserPrompt predicate | `ReviewEvidenceRebindingDeliveryTests.C1043_ReceiptIdentity` | `G88`: queued/AssistantText copies do not confirm | 9 |
| PC-89 | G-89: change Sequence > floor to >= floor | `ReviewEvidenceRebindingDeliveryTests.C1043_ReceiptIdentity` | `G89`: at/below-floor full prompt does not confirm | 9 |
| PC-90 | G-90: return true from completion spill hash verification | `ReviewEvidenceRebindingDeliveryTests.C1043_SpillHashRequired` | `G90`: missing/tampered spill leaves notification unconfirmed | 9 |
| PC-91 | G-91: reuse the previous Clean header when the current outcome supplies no approval | `ReviewEvidenceRebindingDeliveryTests.C1043_FinalRefusalReceipt` | `G91`: complete final prompt has no review-evidence header and names Found/refusal as appropriate | 9 |
| PC-92 | G-92: reject Source=Orchestrator review-rebind-v1 evidence in recovery approval | `ReviewEvidenceRebindingLandingTests.C1043_RecoveryOwnerLandV2` | `G92`: v2 request persists new ID/owner/SHA; run publishes exact reviewed commit | 8 |
| PC-93 | G-93: pass original owner instead of adoption source to LoadRecoveryEvidenceAsync | `ReviewEvidenceRebindingLandingTests.C1043_AdoptionLandV2` | `G93`: request RecoverySourceTaskId/ref/SHA match adopted subject and remote contains its commit | 8 |
| PC-94 | G-94: remove supersession query from LandApproval.LoadUsableEvidenceAsync | `ReviewEvidenceRebindingLandingTests.C1043_LandSupersession` | `G94`: old unbound ID refused; later superseded bound new ID gives review_evidence_superseded | 8 |
| PC-95 | G-95: use evidence SHA without comparing fresh source observation | `ReviewEvidenceRebindingLandingTests.C1043_LandFreshness` | `G95`: move source after rebind; request/run refuses before target mutation | 8 |
| PC-96 | G-96: remove ReviewedSourceClean guard from LandApproval | `ReviewEvidenceRebindingLandingTests.C1043_LandCoordinates` | `G96`: clean=false/null variant gives review_evidence_source_not_clean | 8 |
| PC-97 | G-97: omit LoadRecoveryEvidenceAsync's Final/Full check for Unknown/None variants | `ReviewEvidenceRebindingLandingTests.C1043_LandCoordinates` | `G97`: Interim/Unknown/None variant gives review_verification_scope_ineligible | 8 |
| PC-98 | G-98: remove SubjectTaskId equality from LandApproval | `ReviewEvidenceRebindingLandingTests.C1043_LandCoordinates` | `G98`: wrong-subject gives review_evidence_subject_mismatch | 8 |
| PC-99 | G-99: remove reviewed SHA equality from LandApproval | `ReviewEvidenceRebindingLandingTests.C1043_LandCoordinates` | `G99`: wrong-full-SHA gives review_evidence_sha_mismatch | 8 |
| PC-100 | G-100: remove ReviewedSourceRef equality from LandApproval | `ReviewEvidenceRebindingLandingTests.C1043_LandCoordinates` | `G100`: wrong-ref gives review_evidence_ref_mismatch | 8 |
| PC-101 | G-101: remove SameRepository refusal from LandApproval | `ReviewEvidenceRebindingLandingTests.C1043_LandCoordinates` | `G101`: wrong-repository gives review_evidence_repository_mismatch | 8 |
| PC-102 | G-102: on evidence ConflictException retry admission as ExplicitCaller | `ReviewEvidenceRebindingLandingTests.C1043_NoApprovalFallback` | `G102`: Succeeded owner with rejected supplied ID gets refusal and zero land request | 8 |
| PC-103 | G-103: copy predecessor profile/Final/Full fields into generic override | `ReviewEvidenceRecoveryTests.C1043_ManualFindingCannotCopyFull` | `G103`: explicit manual SHA/clean override retains null profile/round/scope | 7 |
| PC-104 | G-104: make RemoteSyncBlockReason return null for a real lease-budget failure | `ReviewEvidenceResettlementTests.C1043_ContinuingSyncFailure` | `G104`: status=Blocked and next=Decide even if old stored sync is confirmed | 7 |
| PC-105 | G-105: omit full ref from the under-lock prepared-coordinate comparison | `ReviewEvidenceRecoveryTests.C1043_PreparedCoordinatesChanged` | `G105`: changed ref gives 409 and no successor | 7 |
| PC-106 | G-106: omit repository from the under-lock prepared-coordinate comparison | `ReviewEvidenceRecoveryTests.C1043_PreparedCoordinatesChanged` | `G106`: changed repository gives 409 and no successor | 7 |
| PC-107 | G-107: omit StageTaskId equality when loading the named predecessor | `ReviewEvidenceRecoveryTests.C1043_NamedPredecessor` | `G107`: foreign Review row gives 409 and no successor | 7 |
| PC-108 | G-108: omit Stage == Review predicate for the named predecessor | `ReviewEvidenceRecoveryTests.C1043_NamedPredecessor` | `G108`: named Verify row gives 409 and no successor | 7 |
| PC-109 | G-109: omit Source == Delegate predicate for the named predecessor | `ReviewEvidenceRecoveryTests.C1043_NamedPredecessor` | `G109`: named Orchestrator unbound row gives 409 and no successor | 7 |
| PC-110 | G-110: take the last evidence heading instead of rejecting headings.Count > 1 | `ReviewEvidenceRebindingTests.C1043_StandaloneAndOid` | `G110`: two valid standalone blocks produce no usable candidate | 6 |
| PC-111 | G-111: remove headingAt > nextStageAt refusal | `ReviewEvidenceRebindingTests.C1043_StandaloneAndOid` | `G111`: after-handoff evidence produces no usable candidate | 6 |
| PC-112 | G-112: write a fixed manual actor identity instead of the authenticated actor | `ReviewEvidenceRecoveryEndpointTests.C1043_ResponseAfterCommit` | `G112`: audit actor equals authenticated principal and never request-derived text | 7 |
| PC-113 | G-113: remove Outcome == Clean guard from LandApproval | `ReviewEvidenceRebindingLandingTests.C1043_LandCoordinates` | `G113`: Found variant gives review_evidence_ineligible | 8 |
| PC-114 | G-114: omit the repair-witness-failure handoff override while retaining the binding refusal | `ReviewEvidenceResettlementTests.C1043_HeaderSnapshotAndGetAgree` | `G114`: a fresh subject-ref mismatch yields NextStage=Decide, matching refusal reason and no review-evidence header in snapshot or GET | 7 |

### Out of scope

- Production repairs of the three named stranded Reviews, activation and actual
  CARD-1035/CARD-1043 land requests: runbook follow-up after ordinary Review,
  publication and server version confirmation. Tests use equivalent synthetic
  rows, never the live IDs or their mutable branches. The bound Interim case
  equivalent to a08e6d8d must refuse; no live scope upgrade.
- CARD-0743 lease contention throughput, scheduling fairness and altered retry
  budgets; no timeout increase. Existing runner continuation tests protect the
  relevant contract without rerunning the contention suite.
- Native Pty/Claude/Codex transport, Windows DPAPI, server/runner deployment and
  machine-death custody: this change is OS-neutral server metadata plus an HTTP
  CLI. The declared adapter and persistence-cut substitutes do not qualify those
  unchanged native mechanisms.
- Unchanged land Outcome notification delivery and the full landing crash engine:
  CP-12 proves actual v2 admission and remote publication with rebound identity;
  CP-10/CP-11 prove the changed Review completion path through recipient evidence.
  No publication/queue receipt is presented as a caller receipt.
- Whole Unit, namespace, full assembly, client, E2E and Pty suites. There are no
  changes to those surfaces. The closed selection below overrides the generic
  full-suite default. Parser-only aliases in AgentTaskReviewEvidenceTests are
  excluded because they cannot prove the service assertions their names suggest.
- No schema migration, global AgentTask concurrency-token change, startup scan,
  evidence-ID fallback or generic Full-copying override is part of this plan.
  Discovery of a need for one is a Plan amendment, not an implicit Code expansion.

### Checkpoints

Closed ordinary Code/Review scope, one isolated build and one exact filter per
row, no build reuse. All builds target `tests/Antiphon.Tests`; TUnit runs via
`dotnet run`, never `dotnet test`. All rows are **Any/default eligible runner**;
read runner defaults/directory before execution and omit a hard-coded runner or
platform. PostgreSQL is required by **CP-1–CP-8 and CP-10–CP-12**;
scratch Git is required by those rows except CP-8's seeded DB-only overrides.
CP-9 uses PowerShell plus the offline HTTP harness and does not require a DB.
CP-1 includes pure parser cases but its binding/summary fixtures need PostgreSQL.
A missing Docker/PostgreSQL lane is not a skipped success.

Use assembly-local `ParallelLimiter<ProcessSpawnLimit>` for Git/process cases;
serialize MessageQueue tests as the existing fixture does. Fresh cloned databases
isolate cases. Do not co-schedule Antiphon.Agents.Pty.Tests/FakeClaude. All ordinary
rows are bounded at 4–14 estimated minutes; do not expand a wildcard to unnamed
classes or silently run the assembly. `Expect` means every listed method (and
existing argument row) executes with zero failures or skips; verify the concrete
TRX roster, not discovery output.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1043-cp1/` | binding-parser-summary | `/*/Antiphon.Tests.Application/(ReviewEvidenceRebindingTests*)\|(ReviewEvidenceParserTests*)\|(StageOutcomeSummaryTests*)/*` | V-1, R-1 | all 41 executions, 0 failed/skipped | 41 | 12 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1043-cp2/` | first-settlement-warnings | `/*/*/ReviewEvidenceConsistencyTests/(C788_SubjectTipMismatchWarnsAndBinds*)\|(C788_ReviewBaseMismatchWarnsAndBinds*)` | V-1, R-1 | all 4 executions, 0 failed/skipped | 4 | 5 |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c1043-cp3/` | settlement-extraction | `/*/*/ReviewEvidenceSettlementTests/*` | V-2, R-2 | all 11 executions, 0 failed/skipped | 11 | 10 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1043-cp4/` | resettlement | `/*/*/ReviewEvidenceResettlementTests/*` | V-2, R-2 | all 9 executions, 0 failed/skipped | 9 | 12 |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin-c1043-cp5/` | runner-continuation | `/*/*/RunnerTaskSettlementTests/(Sync_uncertainty_blocks_and_reply_retries*)\|(Runner_sync_block_commits_the_completion_obligation*)\|(C788_NonCodeNoPushRoleMatrix*)` | V-2, R-2 | all 6 executions, 0 failed/skipped | 6 | 6 |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c1043-cp6/` | named-recovery | `/*/*/ReviewEvidenceRecoveryTests/*` | V-3, R-3 | all 21 executions, 0 failed/skipped | 21 | 14 |
| CP-7 | S3 | `tests/Antiphon.Tests -> bin-c1043-cp7/` | recovery-http | `/*/Antiphon.Tests.Application/(ReviewEvidenceRecoveryEndpointTests*)\|(StageOutcomeFindingEndpointTests*)/*` | V-4, R-4 | all 9 executions, 0 failed/skipped | 9 | 8 |
| CP-8 | S3 | `tests/Antiphon.Tests -> bin-c1043-cp8/` | manual-override-regression | `/*/*/AgentTaskReviewEvidenceTests/(C488_ManualFindingFieldsRestricted*)\|(C488_OverrideDoesNotCopyApproval*)` | V-3, R-3 | all 2 executions, 0 failed/skipped | 2 | 4 |
| CP-9 | S4 | `tests/Antiphon.Tests -> bin-c1043-cp9/` | recovery-cli | `/*/*/ReviewEvidenceRecoveryScriptTests/*` | V-5, R-5 | all 4 executions, 0 failed/skipped | 4 | 6 |
| CP-10 | S5 | `tests/Antiphon.Tests -> bin-c1043-cp10/` | receipt-identity | `/*/*/ReviewEvidenceRebindingDeliveryTests/(C1043_TwoSettlementReceipts*)\|(C1043_SnapshotSurvivesRebind*)\|(C1043_ReceiptBusyAndEligible*)\|(C1043_IncompletePromptIsNotReceipt*)\|(C1043_ReceiptIdentity*)\|(C1043_SpillHashRequired*)\|(C1043_FinalRefusalReceipt*)` | V-6, R-6 | all 7 executions, 0 failed/skipped | 7 | 8 |
| CP-11 | S5 | `tests/Antiphon.Tests -> bin-c1043-cp11/` | receipt-recovery | `/*/*/ReviewEvidenceRebindingDeliveryTests/C1043_Cut*` | V-6, R-6 | all 9 executions, 0 failed/skipped | 9 | 12 |
| CP-12 | S5 | `tests/Antiphon.Tests -> bin-c1043-cp12/` | rebound-land-v2 | `/*/*/ReviewEvidenceRebindingLandingTests/*` | V-7, R-7 | all 6 executions, 0 failed/skipped | 6 | 10 |

Execution floors: CP-1 = 15 new binding + 21 parser (14 methods, two expanded
to 3 and 6 cases) + 5 summary = 41. CP-2 = two methods x two arguments = 4.
CP-3 = 11 existing methods. CP-5 = two single methods + four role arguments = 6.
CP-7 = six new endpoint + three existing finding methods = 9. Other floors equal
their distinct non-parameterized method rosters. Total: **129 TUnit executions**,
including **77 new methods**. Internal loops assert additional combinations,
not additional executions. CP-10 selects exactly seven receipt methods and
CP-11 exactly nine cut methods; together they cover the whole 16-method class.

For each committed slice run the checkpoint tool once with its exact
`--after S1`, `S2`, `S3`, `S4` or `S5` selection:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1043-checkpoints -- dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-05-card-1043-unbound-review-evidence-plan.md --after S1
```

Use the corresponding literal slice for later runs. The tool's row drivers
acquire their own slots; exit 4 is a reported slot timeout, never an unleased
retry. If the foreground call returns 75, keep owning the run and call the
tool's `wait` on that run ID until terminal. Preserve unedited CHECKPOINT lines,
counts, test SHA and clean/build provenance. Commit before starting each group;
do not edit tracked source during a run. Remove only that run's owned alternate
outputs through checkpoint cleanup.

Code dispatches remain 30–60 minute slices: commission sequential follow-ons
when fixture authoring plus the listed group's verification exceeds the seat
budget, using the same slice's committed branch state. Do not drop CP rows to
make a deadline. Before ordinary Review, validate the committed-slice receipts
and run `scripts/check-evidence-diff.ps1` from the task base to pushed head.
When a later change touches an earlier group, rerun that exact row against the
new commit and report the reason; no full-suite retry. Green evidence for an
earlier slice is not relabeled as the final head.

### Cost

All timings are **estimated**, not measured. This documentation stage ran no
builds/tests/PCs and claims no green implementation. Estimates exclude host-slot
queue delay; Code reads current occupancy and reports actual wait separately.

- **Ordinary V/R floor (Code): 107 minutes**, the exact CP sum:
  CP-1 binding/parser/summary 12; CP-2 first-settlement warnings 5;
  CP-3 settlement extraction 10; CP-4 resettlement 12; CP-5 runner continuation 6;
  CP-6 named recovery 14; CP-7 recovery HTTP 8; CP-8 manual overrides 4;
  CP-9 CLI 6; CP-10 receipt identity 8; CP-11 receipt recovery 12;
  CP-12 land/v2 10. These filters are printed literally in the table.
  Within the 107, allow 24 minutes for twelve two-minute isolated builds and
  83 for test setup/execution/receipt checks. Add **10 minutes** of initial
  lane/tool/PostgreSQL qualification outside those rows: Code floor **117**.
- **Positive-control floor (Mutation): 815 minutes**. Every PC row carries its
  full exact method and cycle estimate. Apply the exact method filter specified
  above independently for baseline, red and restored green; no class run is a
  substitute. By class: B 27 PCs x 6 = 162; S 16 x 7 = 112;
  R 30 x 7 = 210; E 7 x 7 = 49; C 4 x 6 = 24;
  D 18 x 9 = 162; L 12 x 8 = 96. Sum = 815 across **114 PCs**.
  Each six-minute cycle includes three isolated builds/method runs at two minutes
  each; seven/eight/nine-minute cycles add one/two/three minutes for mutation,
  restoration inspection and slower fixture/assertion evidence. Every cycle
  includes baseline green, intended red, byte restoration and fresh green.
  Add **15 minutes** for SourceLanding discovery, copied-driver setup, external
  evidence/restoration inventory and final clean-source verification: Mutation
  floor **830**.
- Combined stage floors = **107 + 815 = 922 minutes**. Including initial Code
  setup 10 and Mutation setup/discovery 15 gives **947 minutes (15 h 47 min)**.
  Original S1–S5 authoring allowance 210–285 gives **1,157–1,232 minutes**
  including authoring and verification; ordinary Review's own reading and
  contention/retries are additional and must be priced in its commission.
  This is larger than the Plan's provisional 55–85 ordinary-minute envelope:
  actual extraction compatibility, authenticated HTTP, every delivery cut and
  method-level guard split are now enumerated. No asserted safety coverage is
  hidden in a generic unit run or a zero-minute mutation estimate.
- Batching savings estimate: writing the 77 new methods before twelve row builds
  avoids 65 builds versus one build per new method, or **130 minutes** at the
  assumed two-minute build cost. This is a comparison model, not a measured
  benchmark. PC batching saving is **0**: controls mostly touch the same parser,
  settlement, recovery, queue and approval methods and cannot safely share a
  mutation. Do not reduce the 815 floor on an assumption of parallel speedup.
  Changed/failing-row reruns and slot waits are reported additions.

Handoff audit: bodies read as listed; **guards=114, mapped=114, missing=0,
duplicate PC maps=0**. All PCs have a compiling production defect, literal method,
decisive labeled assertion, exact method-filter rule and numeric cycle budget.
They are executable obligations for the Code-created methods, not executed
evidence at this plan commit. All 77 new methods are selected by CP-1–CP-12;
V-1–V-7 and R-1–R-7 are covered by the CP union. No placeholder, deferred
unpriced guard or unresolved test seam remains.
