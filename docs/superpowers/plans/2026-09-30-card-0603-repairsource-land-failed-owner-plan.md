# CARD-0603: finish the RepairSource-to-Failed-owner landing handoff

Date: 2026-09-30. Stage: TestDesign complete; next stage: Code. Plan source examined:
`d7456a2352d15391f37eca8781c16db499a3759d`; TestDesign inspected plan/start commit
`bc838ab35ac354e8ee163266fdbf79c57625c417`. This artifact changes no product code.
Read CARD-0603 in full with `scripts/card.ps1 get CARD-0603` and its history: the
single ContentEdit revision, 2026-09-22 10:28:20, adds the sibling-ref and stale
Unconfirmed-operation incidents. Line references below are against that source SHA.

## Problem and remaining gap

The historical deadlock was real: ordinary Land required a Succeeded owner, while
RepairSource settlement conferred attribution without transferring landing ownership.
An original owner marked Failed therefore remained ineligible after a successful repair.
The workaround created extra Code work solely to acquire another landing owner.

**The landing-authority gap is already implemented by CARD-0753.** Its
[2026-09-26 plan](2026-09-26-card-0753-landing-owner-recovery-plan.md) explicitly
covers CARD-0603 as well as CARD-0675. `recoverReviewedSource` now admits a Failed
Code/Worktree owner with exact owner-bound Clean Final/Full Review. CARD-0675 also
added coverage and a recipe for adoption from a separate `-StartRef` source.
Do not implement either feature again or make repair success sufficient authority.

What remains here is a narrow operational and verification gap:

1. All three repair-task Land entry points say only "commission Land on the
   original owner". A plain Land on that Failed owner still gives only "must have
   succeeded". Those messages reconstruct the apparent deadlock despite the
   supported recovery lane.
2. The RepairSource paragraph names the original owner, but does not give the
   Failed-owner recipe next to that instruction or explicitly identify its Review
   subject. The nearby `-StartRef` recipe uses a different evidence subject; copying
   it onto a RepairSource task is refused by design.
3. No existing test joins **Failed owner before repair -> real attributed repair
   settlement -> owner-bound Review consumption -> confirmed target publication**.
   RepairSource tests seed a Succeeded owner; reviewed-recovery tests independently
   seed a Failed owner without RepairSource settlement.

The two secondary incidents need separate treatment. Current own-branch runner
publication/brief contracts address the sibling-push workaround class (CARD-0779).
Current reviewed recovery can supersede a valid NeedsResolution/Refused operation,
but deliberately cannot bypass an unresolved publication. This plan does **not**
claim the historical `land_request_mirror_disagreement` bare 500 is reproduced or
fixed. Do not erase or supersede inconsistent old publication rows to make this
card pass; preserve that incident for a separate persistence/reconciliation repair
if it recurs. No live landing or historical-row mutation was performed in Plan.

## Ground truth and root-cause evidence

| Card assumption / question | Current behavior and evidence |
|---|---|
| Failed owners are permanently unlandable. | False for explicit reviewed recovery: `server/Application/Services/AgentTaskLandService.cs:93` selects recovery mode and `:97` admits eligible Code owners; ordinary status refusal remains at `:108`. `server/Application/Services/LandApproval.cs:91` requires Final/Full evidence and `:101` includes Failed and Blocked. |
| A Succeeded repair should itself land. | Repair ownership is still deliberately refused in request admission (`AgentTaskLandService.cs:85`), background execution (`:369`), and protocol entry (`server/Application/Services/AgentTaskLandingProtocol.cs:40`). Each message omits the recovery recipe. |
| Repair attribution should clear Failed. | Recovery stores original status and source on the request (`AgentTaskLandService.cs:244`) rather than changing the owner's historical result. `tests/Antiphon.Tests/Application/AgentTaskLandRequestTests.cs:83` covers Blocked and Failed admission; `AgentTaskLandAdoptionTests.cs:257` publishes reviewed self recovery and asserts Failed remains. |
| `-FromTask <repair>` can substitute for owner recovery. | An actual RepairSource task is rejected as an adoption source at `AgentTaskLandService.cs:202`. `server/Application/Services/AgentTaskLandSourceResolver.cs:224` rechecks this at execution. `-FromTask` is for a separate eligible same-card Code/Worktree source, including an ordinary `-StartRef` continuation. |
| Reviewing the repair's report is enough. | `LandApproval.cs:55` checks Clean evidence, exact subject/SHA/ref/repository and supersession; `:91` adds Final/Full. Self recovery uses the owner as source (`AgentTaskLandService.cs:228`). Execution checks the exact current remote tip (`AgentTaskLandSourceResolver.cs:252`) and clean registered checkout (`:280`). |
| The supported interface is missing. | `scripts/delegate.ps1:768` validates recovery arguments; `:819` writes adoption/self-recovery flags and `:821` posts only to `/land/v2`. `docs/ops-http.md:122` and `docs/orchestration-loop.md:81` document the API and ownership rules. |
| Repair settlement tests prove this incident. | `tests/Antiphon.Tests/TestHelpers/RepairSourceWorld.cs:134` seeds Succeeded. `RepairSourceSettlementTests.cs:27` verifies attributed work but asserts the owner is still Succeeded at `:55`; `RepairSourceLandRefusalTests.cs:60` only queues ordinary owner Land. |
| Recovery tests prove repair settlement. | `AgentTaskLandAdoptionTests.cs:257` constructs a rewritten owner remote; `StartRefRepairAdoptionTests.cs:19` adopts another task's branch. Neither dispatches and settles a RepairSource task before owner recovery. |
| Any old Unconfirmed operation can be cleared. | `AgentTaskLandService.cs:143` permits reviewed supersession only for NeedsResolution; `:178` refuses an unresolved, non-Refused publication as `publication_unconfirmed`. `AgentTaskLandingProtocol.cs:94` controls replacement of a Refused operation. `AgentTaskLandAdoptionTests.cs:103` proves a valid conflicted operation gets a new publication. This is not evidence about every malformed legacy request/operation pair. |
| Sibling branch publication is still required. | `docs/orchestration-loop.md:872` documents current runner salvage and divergence outcomes; the runner contract requires pushing the task's own ref. Those mechanisms are independent of recovering already-attributed RepairSource work on the owner's ref. |

The original defect crossed settlement authority and landing authority. CARD-0753
supplied the explicit reviewed bridge. The residual defect is a misleading handoff
between those two paths, plus missing integration proof of their composition.

## Decisions

- **D-1 — reuse reviewed owner recovery.** Choose card option (c), already implemented.
  Keep the original owner as Land target; require exact full SHA and owner-bound
  Clean Final/Full Review. Preserve Failed, FailureCode, FailureReason and CompletedAt.
  Reject (a) landing the repair and (b) reopening/flipping the owner: both create a
  second authority model and bypass the existing evidence contract.
- **D-2 — fix refusals where callers encounter them.** Preserve
  `repair_source_landing_owner_required` and HTTP 409 at all three guards. Share
  the explanatory text through a small pure helper in the existing `LandApproval`
  utility, used by both services, so it cannot drift. Include the full owner GUID
  as a usable command argument. Explain: if the original owner is Failed or Blocked,
  commission Clean Final/Full Review of its current pushed tip, with the **owner**
  as `subjectTaskId`, then use the owner command below. Use `<full-sha>` and
  `<review-evidence-id>` placeholders; a refusal must not guess a current SHA or
  approval, query Git, commission Review, or queue recovery automatically.
- **D-3 — retain ordinary status admission.** Extend the existing ordinary-Land
  refusal for Code/Worktree owners in Failed or Blocked state with the same explicit
  recovery guidance. Preserve the original refusal category/code and the existing
  ordinary NeedsResolution resume exception. Do not suggest recovery for Canceled,
  active, non-Code, Mutation or non-Worktree tasks. No new DTO, migration, enum,
  capability marker or script flag is needed.
- **D-4 — document the two evidence subjects separately.** Add a short
  "RepairSource succeeded; owner Failed" recipe next to RepairSource in
  `docs/orchestration-loop.md`, cross-link it from `docs/ops-http.md`, and add the
  same distinction to `server/Bundles/orchestrator.md`. Self recovery reviews the
  owner; adoption reviews a separate eligible source. Never recommend `-FromTask`
  with an actual RepairSource task, another manufactured commit, direct DB status
  changes, or an operator force-push.
- **D-5 — test the joined path without changing its semantics.** Use real temporary
  Git repositories, a bare origin and isolated PostgreSQL schema. Exercise the real
  dispatcher, settlement service, land request/service, source resolver and protocol.
  The fixture may seed historical failure and a valid Review evidence row; it may
  not seed a Succeeded repair, Landed receipt, or simulated successful Git push for
  the positive case. Existing Review-evidence settlement tests own evidence creation.
- **D-6 — separate TestDesign remains required.** The matrix below fixes the
  intended coverage, counts and PC faults. TestDesign must confirm fixture wiring,
  exact assertions and mutation viability, and amend this artifact if it finds a
  missing boundary. It must not broaden this into another recovery implementation.

The operator recipe to publish (not to execute during Code verification):

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Land <owner-guid> -ExpectedSourceSha <full-sha> -ReviewEvidenceId <review-evidence-id> -RecoverReviewedSource
pwsh -NoProfile -File scripts/delegate.ps1 -Status <owner-guid>
```

Before that command, read the owner's exact origin ref and commission or reuse a
current unsuperseded Review bound to that owner/ref/repository/tip. The original
owner's registered checkout must exist, be clean and on its branch. Repair success
alone is neither Review nor publication. If a prior publication is unresolved,
inspect/recover that operation first. After the command, distinguish queued from
confirmed publication, inspect recovery provenance and cleanup separately, and
allow the historical task status to remain Failed.

## Slices

| Slice | Files and deliverable | Tests / completion condition |
|---|---|---|
| S1 — prove the original sequence | Add `tests/Antiphon.Tests/Application/RepairSourceRecoveryLandingTests.cs`; narrowly extend `tests/Antiphon.Tests/TestHelpers/RepairSourceWorld.cs` with an opt-in landing verifier override and a queue-driving helper if needed. Keep `DelegationTestServices.AddDelegationWorktreeGraph` as the graph registration. Register the new Slow integration class in `tests/Antiphon.Tests/slow-tests-allowlist.txt`. | V-1/V-2. Confirm recovery already works on this checkout; a newly discovered behavioral failure goes back to this plan before adding production authority changes. |
| S2 — actionable refusals | `server/Application/Services/LandApproval.cs`, `AgentTaskLandService.cs`, `AgentTaskLandingProtocol.cs`; only message construction/call sites and eligible ordinary-refusal wording. | V-3/V-4 in `RepairSourceLandRefusalTests.cs` and `AgentTaskLandRequestTests.cs`. Both refusal paths must remain side-effect free. |
| S3 — operator recipe and transport proof | `docs/orchestration-loop.md`, `docs/ops-http.md`, `server/Bundles/orchestrator.md`; add the explicit Failed-owner recipe. Add tests in `DelegateScriptAdoptTests.cs` and `RepairSourceDocumentationTests.cs`. | V-5/V-6 plus R-1/R-2/R-3. No change to `scripts/delegate.ps1` is expected; its happy-path self-recovery serialization lacks a direct test today. |

Commit and push each slice; execute the closed checkpoint group after S1-S3 are
committed. Do not alter `docs/cards/`. No changes to notification delivery, session
runtime, runner publication, cleanup policy or source-adoption algorithms belong
to these slices.

## Verification design

TestDesign validated the source and fixture wiring at the start commit above. The
design is ready for Code: **six new methods, ten new executions, thirty existing
executions, four checkpoint rows, eight positive controls**. These are inspected
rosters and designed assertions, not executed test or Mutation evidence. All test
paths below are under `tests/Antiphon.Tests/Application/` unless qualified otherwise.

### Inspection

| Bodies inspected | Finding / verification boundary |
|---|---|
| `TestHelpers/RepairSourceWorld.cs`, `RepairSourceSettlementTests.cs`, `TestHelpers/TurnSeeding.cs` | The owner initially succeeds; set its persisted failure before dispatch. `DoneReport` supplies the claim/handoff and `TurnSeeding` appends a terminal done token. Settlement is real; the transcript is fixture input, not provider/session-delivery evidence. V-1/V-2. |
| `TestHelpers/DelegationTestServices.cs`, `TestHelpers/LandingSafetyHarness.cs`, `TestHelpers/ControlledTaskProgressGit.cs` | Graph registration uses `TryAdd`; register the optional verifier first. The Git wrapper delegates to real Git with fault hooks unset. Copy the queue-driving pattern, not another container graph. V-1/V-2/V-3. |
| `AgentTaskLandRequestTests.cs`, `AgentTaskLandAdoptionTests.cs`, `RepairSourceLandRefusalTests.cs` | Existing argument counts are 13 admission, 7 publication and 3 RepairSource executions. The ordinary conflict code is `conflict`; the repair worker guard runs before request lookup. V-2/V-3/V-4, R-1/R-2/R-3. |
| `server/Application/Services/{AgentTaskLandService,AgentTaskLandSourceResolver,AgentTaskLandingProtocol,AgentTaskLandingState,LandApproval}.cs` | Recovery revalidates owner-bound evidence and current origin tip, retains historical status, and separately confirms publication. A no-op rebase can skip the verifier. No authority change is needed. V-1/V-2, R-1/R-3. |
| `DelegateScriptAdoptTests.cs`, `DelegateScriptRunner.cs`, `TestHelpers/LandApiStub.cs`, `scripts/delegate.ps1` | Five current script executions; self recovery with a full GUID performs a version GET and one owner POST, with no source lookup. V-5, R-2. |
| `RepairSourceDocumentationTests.cs`, RepairSource/recovery passages in `docs/orchestration-loop.md`, `docs/ops-http.md`, `server/Bundles/orchestrator.md` | Two current Unit executions. Whole-document flag searches would not prove the new recipe; V-6 must extract the new bounded passage. |
| `scripts/{build-slot,run-checkpoint}.ps1`, `src/Antiphon.SessionRunner/BuildSlotBroker.cs`, `docs/testing-and-build.md` | Passing the `.ps1` directly invokes it in the same PowerShell process; the broker reuses that holder's lease. The wrapper recipe below is source-validated, not a measured run. |

### Joined fixture and decisive observations

Use one fresh `RepairSourceWorld` per V-1/V-2 execution, with default
`ExplicitIntegration=false`, ordinary RepairSource ownership and no phone-home policy.
Keep the existing Succeeded-owner default for all old C499 callers. The extension is
an optional `ILandingVerifier` argument/property, registered **before**
`AddDelegationWorktreeGraph`, plus a small queue helper if useful. No successful Git
result, repair settlement, source resolution or publication may be substituted.

1. Persist owner `Status=Failed`, `FailureCode=CompletedWithoutProgress`, a nonempty
   `unclaimed_or_unmatched_commit` reason and its historical `CompletedAt` before
   `DispatchAsync`. Read the failure tuple back from a fresh context and retain it
   for exact comparison (including PostgreSQL timestamp precision). Assert there
   are no prior owner land requests/operations and no final-verification latch;
   this is the narrow incident fixture, not a supersession scenario.
2. Dispatch the queued repair with the real dispatcher. Require nonempty session ID,
   `Dispatched`, `RepairSourceTaskId=owner.Id`, a different registered branch/path,
   and a repair baseline at the owner's original pushed SHA. Commit `repair.md` with
   known contents on the owner's tree using `CommitInOwnerTreeAsync(..., push: true)`.
   Capture its real full SHA **S**; independently read the bare origin owner ref as S.
3. `SettleAsync(DoneReport(..., S))` reaches the real reply service using the exact
   repair task claim and terminal report token; do not manually set repair Succeeded.
   From a fresh context require Succeeded, no repair failure, progress assessment
   `ProgressObserved`, and a RepairSource/RepairSourceRemote evidence arm with
   `OwnerTaskId=owner.Id`, `ClaimedSha=VerifiedSha=S` and the owner's registered path.
   Read the owner ref from the persisted RepairSource baseline; the completion arm
   has no full-ref field and its optional `ObservedRef` need not be set for RepairSource.
   Require the historical owner failure tuple unchanged. The repair checkout remains
   registered on its assigned branch at the original baseline, and `MergeTargetRef`
   remains null. Zero landing rows exist at this point.
4. V-1 seeds one `StageOutcome`: Review/Clean/Delegate, a distinct Review stage-task ID,
   `SubjectTaskId=owner.Id`, owner full ref and repository, `ReviewedSourceSha=S`,
   `CommissionedRound=Final`, `OrdinaryScopeCompleted=Full`, no superseding row. This
   follows `AgentTaskLandAdoptionTests.AddReviewAsync`; it tests evidence consumption,
   not Review evidence generation. Record target `refs/heads/master` before admission;
   S must not already be contained there.
5. Request owner self recovery with S and the evidence ID. Assert acceptance using an
   assertion wrapper (`Should.NotThrowAsync`, then `Status == "queued"`), so PC-1
   produces a named admission assertion failure. Supply a nonempty fixture Verify
   filter, `/*/*/RepairSourceRecoveryLandingTests/C603_FailedOwnerLandsReviewedRepairedTip*`,
   to exercise the injected verifier rather than the unchanged-base skip. Dequeue
   **the actual** `AgentTaskLandQueue` entry; assert task ID, accepted request ID and
   filter, then await `RunRequestAsync` in a fresh scope. Release that claim in
   `finally`, as `LandingSafetyHarness.RunQueuedAsync` does. Do not synthesize a queue
   entry, call the protocol in place of the worker, or turn Held into completion.
6. Require `LandRunResult.Complete`, a completed nonpending request and its linked
   operation from a fresh context. Both carry `OwnerReviewedSource`, original owner
   status Failed, source task owner, owner full ref and the exact review ID; request
   expected/resolved SHA and operation original/reviewed/verified SHA equal S. Require
   `ApprovalKind=ReviewEvidence`, `ApprovalLandRequestId=request.Id`,
   `RecoveryOwnerRemoteBeforeSha=RecoveryOwnerRemoteAfterSha=S`, and the preserved
   owner failure tuple. Require one controlled verifier invocation on the operation's
   detached land worktree with that filter and `VerificationPassed=true`.
   `HasPublication(op)` must be true, with `Publication=Landed`, `RemoteConfirmedAt`
   set and `ConfirmationMethod=push-endpoint-read-fetch-ancestry`. Independently run
   Git against the bare origin: target tip and `ObservedRemoteTargetSha` equal S,
   and `git show refs/heads/master:repair.md` equals the committed contents. The
   fixture has no concurrent target changes, so exact S proves no manufactured
   source commit was needed. Require zero repair-task requests/operations, no newly
   manufactured Code task, and the repair checkout still present. Owner/land-worktree
   cleanup may leave residue; never require their deletion as publication evidence.

Use assertion messages naming the boundary and scenario. A helper returning expected
constants, an in-memory entity never reloaded, `queued`, verifier success, or a
Landed enum by itself cannot satisfy the joined test. Own/await each Git process;
mark the new class Integration + Slow, register its fully qualified name in the slow
allowlist, and use the assembly-local `ParallelLimiter<ProcessSpawnLimit>`. Existing
schema isolation and fixture timeouts apply; no real Program, provider CLI, FakeClaude
apphost or production session runner is needed.

### Delivery inventory

| Path / durable identity | Producer, persistence, consumer and receipt | Scope of evidence |
|---|---|---|
| Repair task ID + claim S + owner ID | Dispatcher baseline; seeded complete task report; real reply settlement writes progress evidence. | V-1/V-2 join real settlement to later land. Seeded transcript is explicit input; a caller note is not a delivery receipt. |
| Owner ID + request ID + review ID/S + operation ID | `RequestAsync` commits and enqueues; real queue claim reaches `RunRequestAsync`, source resolver and protocol; bare-origin target read agrees with the durable confirmed operation. | V-1 reaches the actual destination. Admission/queue insertion alone cannot pass it. Existing request recovery is unchanged. |
| Full owner GUID + S + review ID over CLI | Real PowerShell sends version GET and `/land/v2` POST; loopback stub captures verb, path and body. | V-5 proves serialization/targeting only, not server acceptance or publication; V-1 proves those service boundaries. |

No asynchronous caller/session delivery path changes in these slices. Busy/eligible
recipient, lost-wakeup and crash-recovery transport matrices remain owned by existing
delivery tests; they are excluded here, not claimed from note rows, queue flags or
the seeded transcript. No new delivery/recovery guard is introduced.

### Proves it works now

- **V-1 (1 new execution)** —
  `RepairSourceRecoveryLandingTests.C603_FailedOwnerLandsReviewedRepairedTip`.
  Set the owner to Failed/CompletedWithoutProgress with the original
  `unclaimed_or_unmatched_commit` reason **before dispatch**. Dispatch RepairSource,
  commit relevant fixture work onto the owner's branch, push it and settle through
  `AgentTaskReplyService` using the exact task claim and a terminal done report token.
  Assert repair Succeeded and durable RepairSource evidence points to owner/SHA;
  owner failure fields remain unchanged. Seed Clean Final/Full evidence for owner/SHA,
  queue explicit self recovery, and drive the real queued Land to completion.
  Read the request and landing from a fresh DbContext: OwnerReviewedSource, original
  Failed status, owner source task/ref, review ID and approved SHA are correct;
  remote publication is confirmed and a fresh bare-origin read contains the verified
  SHA and repair patch. Assert no extra source commit was required after repair
  settlement and no repair-task request/publication exists. Cleanup may have residue;
  never use cleanup success as the publication assertion. Keep the repair checkout.
- **V-2 (3 new executions)** —
  `RepairSourceRecoveryLandingTests.C603_RepairSuccessDoesNotAuthorizeUnreviewedOwnerLand`,
  arguments `plain`, `missing-review`, `repair-subject`. Use the same actual repaired
  Failed-owner setup; submit plain Land, recovery without Review, and recovery with
  an otherwise owner-ref/SHA-matching evidence row whose subject alone is the repair.
  Expect the ordinary status refusal, `recovery_review_required`, and
  `review_evidence_subject_mismatch`, respectively. Assert zero owner requests/ops,
  unchanged remote target and unchanged owner failure fields. Specifically require
  `ConflictException`, HTTP 409 and code `conflict` for `plain`, using the full real
  repaired SHA in every request. Plain has no evidence/recovery flag; missing-review
  has recovery true and a null evidence ID; repair-subject has recovery true and a
  Clean Final/Full evidence row differing from V-1 only in `SubjectTaskId=repair.Id`.
  Assert zero new land events/notifications, no land queue claim/activity and no
  repair requests/ops after each refusal, compared with the post-settlement snapshot.
  Do not drive the worker if the faulted admission unexpectedly queues: the admission
  assertion itself must go red. The intentionally wrong-subject row isolates that
  check from the separately tested ref check. Each argument owns a new fixture.
- **V-3 (3 new executions)** —
  `RepairSourceLandRefusalTests.C603_RepairLandRefusalNamesReviewedOwnerRecovery`,
  arguments `request`, `worker`, `protocol`. Exercise `RequestAsync`,
  `RunRequestAsync` and leased `AgentTaskLandingProtocol.RunAsync` directly for a
  repair task with a Failed owner. Every entry throws the existing repair-owner code;
  its message contains the full owner ID, owner-as-Review-subject requirement,
  Clean Final/Full requirement and all three recovery flags. No requests or
  operations are created for either task. Use the real dispatched/settled repair
  setup with owner Failed; check 409, the existing code, `owner.Id.ToString("D")`
  and `-Land <that-full-ID>` rather than only the short ID. Expected tokens are
  authored in the test, never read back from the new helper. The worker call is
  `RunRequestAsync(repair.Id, null, null, ct)`: its guard precedes `LandRequestedAt`
  and request lookup, so **do not seed a land request** to reach it. The protocol
  arm loads the repair and acquires/disposes a real repository lease. Capture
  counts/queue state and target tips before each call; no new land event, request,
  operation, queue activation or target change may follow. Keep all three C499 tests.
- **V-4 (1 new execution)** —
  `AgentTaskLandRequestTests.C603_PlainFailedCodeOwnerRefusalNamesReviewedRecovery`.
  Plain Land on a Failed Code/Worktree owner remains a Conflict with the original
  status-refusal text and adds the explicit owner recovery recipe; no request/event/
  queue activation or status mutation. Exercise the service, not just the helper.
  Reuse `SeedSucceededWorktreeAsync`/`CreateLand` and persist the intended state first.
  Within this **one** test result, execute independent fixture cases for Failed and
  Blocked Code/Worktree (no existing land): both require code `conflict`, 409, the old
  `must have succeeded before it can land` phrase and the same full-owner guidance
  assertions as V-3. Include exclusion cases: Canceled/Queued/Dispatched/Working
  Code/Worktree, Failed Review/Worktree (all `conflict`); Failed Code/Shared
  (`conflict`, Worktree refusal); Failed Mutation/Worktree
  (`verification_publication_forbidden`). None
  may suggest `-RecoverReviewedSource` or create land side effects. Use a fresh
  context/queue per case and compare persisted failure tuple and land-event counts.
  Finally exercise the existing ordinary NeedsResolution resume exception with a
  real initially accepted Succeeded-owner request, then seed Blocked + NeedsResolution
  + recovery mode None and release its queue claim. Plain retry with the same SHA
  must reuse that request and queue normally, as
  `C467_V01_ExplicitPostResumesResolvedConflictWithoutResettingAge` already demonstrates.
  These internal boundary checks are not extra TUnit executions or extra CP runs.
- **V-5 (1 new execution)** —
  `DelegateScriptAdoptTests.C603_RecoverReviewedSourcePostsOwnerIdentityToV2`.
  Use `LandApiStub` and the real PowerShell script. Full owner GUID, SHA and Review
  GUID yield one version probe and one POST to that owner's `/land/v2`, body has
  `recoverReviewedSource=true` and matching SHA/Review, no `adoptFromTaskId`, zero
  legacy or repair-target POSTs. Require the entire captured roster to be exactly
  `(GET, /api/version)` then `(POST, /api/agent-tasks/<full-owner-guid>/land/v2)`;
  parse the body and assert `TryGetProperty` succeeds before checking the boolean,
  so PC-7 fails an assertion rather than throwing `KeyNotFoundException`. The stub
  is deliberately compatible/permissive and still returns queued when the flag is
  missing; it must not implement the production gate under test. Exit 0. The existing
  invalid-arguments test remains.
- **V-6 (1 new execution)** —
  `RepairSourceDocumentationTests.C603_RepairSourceFailedOwnerRecipeNamesOwnerBoundReview`.
  Read the new bounded RepairSource-recovery subsection, the ops cross-link and the
  bundle's corresponding paragraph. Assert the owner Review subject, Failed status,
  full SHA/Review flags, and distinction from `-StartRef` adoption there. A flag found
  elsewhere in the document cannot satisfy this test. This is documentation-contract
  evidence only; V-1 supplies behavior evidence. Give the orchestration recipe an
  explicit `repairsource-failed-owner-recovery` anchor and unique bold title
  `RepairSource succeeded; owner Failed`; extract through the next bold topic or
  Markdown heading, asserting both bounds before content checks. Use the same unique
  title to bound the bundle paragraph. Each bounded recipe must name `subjectTaskId`
  as the original owner, Clean Final/Full, the full-SHA/evidence/self-recovery command,
  preserved Failed history and the separate `-StartRef`/`-FromTask` route (with actual
  RepairSource adoption prohibited). Check the ops link resolves to that exact anchor
  and says owner-bound Review. Normalize whitespace only. Never fall back to a
  whole-document search or another copy if the new block is missing.

### Guards the regression

- **R-1 — existing admission controls.** Select the nine arguments of
  `C753_RecoveryRefusesIneligibleOwnerOrReviewWithoutRequest`, the two of
  `C753_ExplicitReviewedRecoveryKeepsTerminalOwnerStatus`, and the two of
  `C753_RecoverySupersedesOnlyAfterMergeHelperIsInactive`. This keeps eligibility,
  evidence gates and the live-helper boundary visible without a full assembly run.
- **R-2 — existing RepairSource/CLI controls.** The three existing C499 refusal/
  ordinary-owner tests, five existing `DelegateScriptAdoptTests` executions and two
  existing `RepairSourceDocumentationTests` executions remain in their bounded rows.
- **R-3 — existing publication controls.** Select the seven executions in CP-3:
  two conflicted-recovery statuses plus one each for refused-operation supersession,
  dirty checkout, moved reviewed remote, rewritten self recovery and cleanup retry.
  This preserves the meaningful publication boundary around the new incident test;
  it is not a replay of the historical malformed mirror-disagreement record.

### Guard inventory and scope

| Guard / assertion under control | Evidence | Positive control |
|---|---|---|
| G-1: existing Failed-owner recovery admission is reachable after real repair settlement | V-1 | PC-1 |
| G-2: ordinary Land still requires Succeeded (outside the existing resume exception) | V-2 plain | PC-2 |
| G-3: repair success does not replace a required recovery Review | V-2 missing-review | PC-3 |
| G-4: self-recovery evidence subject must be owner, never repair | V-2 repair-subject | PC-4 |
| G-5: each repair refusal gives an explicit owner recovery command | V-3 request/worker/protocol | PC-5 |
| G-6: eligible ordinary status refusals disclose the recovery route | V-4 | PC-6 |
| G-7: the real CLI carries explicit self-recovery authority to the owner endpoint | V-5 | PC-7 |
| G-8: the incident-specific recipe states the owner Review subject in its own block | V-6 | PC-8 |

Control inventory: **8 sites, 8 distinct PC mappings, 0 missing mappings, 0 duplicate
PC mappings**. G-5/G-6/G-8 guard actionable guidance, not new landing authority.
This is not a new mutation qualification of every CARD-0753 guard. Historical owner
failure fields, Final/Full/unsuperseded SHA/ref/repository evidence, eligible roles,
current remote tip, clean checkout, live-helper exclusion, publication identity and
cleanup-only authority remain unchanged inherited contracts. V-1/V-2/V-4 and R-1/R-3
assert the named boundaries; no new fault sites for those inherited contracts are
commissioned here. SourceLanding rejection is also unchanged and outside the new
V-4 matrix: its operation FK and immutable insert-time identity must not be fabricated
by changing `SeedSucceededWorktreeAsync`'s already-saved task. Malformed legacy
mirror-disagreement rows, real incident recovery,
runner sibling publication, deployment, caller-message transport and full-suite
qualification remain outside CARD-0603 verification for the reasons above.

### Positive controls

Execute these only in the commissioned post-land SourceLanding Mutation stage.
For each PC use the named method prefix plus `*`, separate fresh baseline/red/green
outputs and results, exact source restoration and a rebuilt green. Require driver
exit 1 with the intended assertion failure for red; a compiler error, fixture error,
missing TRX, timeout or zero selected tests is not a control. Each new test method
has a mapped production or documentation fault; argument rows are identified below.

| PC | Test / argument coverage | Deliberate fault and required red observation |
|---|---|---|
| PC-1 | V-1 | Remove Failed from `LandApproval.RecoveryStatusEligible`. Real repair settlement still succeeds; `RequestAsync` now throws `recovery_owner_ineligible` and the named `Should.NotThrowAsync` recovery-admission assertion fails. Restore and require publication green. |
| PC-2 | V-2 `plain` | Disable only the ordinary non-Succeeded admission guard in `AgentTaskLandService.RequestAsync`. Plain Land now queues; the expected Conflict/zero-request assertion must fail. |
| PC-3 | V-2 `missing-review` | Delete only the null-evidence throw in the recovery block of `RequestAsync`, and wrap its `LoadRecoveryEvidenceAsync` call in `if (evidenceId is not null)`. Keep `source ??= task`, the recovery mode and request construction intact. The fixture's final-verification latch is false. Admission queues with null Review; `Should.ThrowAsync<ConflictException>` fails (expected `recovery_review_required`), rather than a null `.Value`/source crash or a downstream worker refusal. |
| PC-4 | V-2 `repair-subject` | Disable the subject-ID comparison in `LandApproval.LoadUsableEvidenceAsync` while keeping SHA/ref/repository gates. The intentionally otherwise-matching row admits incorrectly; the subject refusal/zero-request assertion fails. |
| PC-5 | V-3 all three entries | Remove `-RecoverReviewedSource` from the shared repair refusal guidance. All three entry-point argument results fail their actionable-message assertion while retaining their existing 409 code. This controls the new guidance, not merely the old refusal. |
| PC-6 | V-4 | Restore the old ordinary Failed-owner refusal text without the recovery suffix. The new message assertion fails although the call still refuses. |
| PC-7 | V-5 | Remove the `recoverReviewedSource` body assignment in `scripts/delegate.ps1`'s Land branch. The permissive stub still accepts the real script POST and the JSON `TryGetProperty(...).ShouldBeTrue()` assertion fails; a missing-key exception is not the intended red. |
| PC-8 | V-6 | Remove the owner-as-Review-subject sentence from the bounded new orchestration recipe while retaining other recovery documentation. The subsection assertion must fail. |

PC-2/3 share the request method and run separately. PC-1/4/5 may share the approval
utility after S2 and also run separately; do not batch interacting faults. V-1/V-2
are contract regressions expected to be green on current behavior, not pretend
pre-fix reds. V-3/V-4/V-6 can expose the present missing guidance before S2/S3; V-5
pins existing correct serialization. Mutation proves all can detect an actual fault.

Every phase uses the following exact method filter and execution floor. Parameter
rows are inspected in TRX; do not use a literal argument suffix. Baseline and restored
green require all selected executions passed and zero skipped; red requires driver
exit 1 and precisely the failure roster below, with zero skipped. The expected
two surviving V-2 arguments are part of the control and must remain green.

| PC | Exact `-Filter` | `-MinExecuted` / phase executed | Intended red counts / argument |
|---|---|---:|---|
| PC-1 | `/*/Antiphon.Tests.Application/RepairSourceRecoveryLandingTests/C603_FailedOwnerLandsReviewedRepairedTip*` | 1 | 1 failed, 0 passed; recovery admission |
| PC-2 | `/*/Antiphon.Tests.Application/RepairSourceRecoveryLandingTests/C603_RepairSuccessDoesNotAuthorizeUnreviewedOwnerLand*` | 3 | 1 failed, 2 passed; plain |
| PC-3 | `/*/Antiphon.Tests.Application/RepairSourceRecoveryLandingTests/C603_RepairSuccessDoesNotAuthorizeUnreviewedOwnerLand*` | 3 | 1 failed, 2 passed; missing-review |
| PC-4 | `/*/Antiphon.Tests.Application/RepairSourceRecoveryLandingTests/C603_RepairSuccessDoesNotAuthorizeUnreviewedOwnerLand*` | 3 | 1 failed, 2 passed; repair-subject |
| PC-5 | `/*/Antiphon.Tests.Application/RepairSourceLandRefusalTests/C603_RepairLandRefusalNamesReviewedOwnerRecovery*` | 3 | 3 failed, 0 passed; request/worker/protocol guidance |
| PC-6 | `/*/Antiphon.Tests.Application/AgentTaskLandRequestTests/C603_PlainFailedCodeOwnerRefusalNamesReviewedRecovery*` | 1 | 1 failed, 0 passed; missing guidance |
| PC-7 | `/*/Antiphon.Tests.Application/DelegateScriptAdoptTests/C603_RecoverReviewedSourcePostsOwnerIdentityToV2*` | 1 | 1 failed, 0 passed; missing JSON flag |
| PC-8 | `/*/Antiphon.Tests.Application/RepairSourceDocumentationTests/C603_RepairSourceFailedOwnerRecipeNamesOwnerBoundReview*` | 1 | 1 failed, 0 passed; bounded owner-subject sentence |

Use `-Expect Class.Method` from each filter, unique `bin-c603-pcN-<phase>/`
outputs and fresh result directories in the SourceLanding task's external evidence
root. Copy the unchanged checkpoint driver and its `lib/build-slot.ps1` there before
mutation, following `docs/testing-and-build.md`'s Mutation recipe. Each cycle records
the exact source patch/digest, completed build/run exit receipts and argument roster;
restore exact source bytes, refresh timestamps and rebuild green before the next
fault. Do not repair test assertions to accommodate a mutation. Unexpected baseline
failures or a red caused only by setup/compilation return to Code; they are not PC passes.

### Execution, platform and receipt rules

The live runner-defaults and catalogue were read during Plan: the global preference
resolves to Linux, while its then-selected runner was not accepting new work; another
Linux runner and Windows desktop were eligible. These are transient routing facts,
not host pins. Re-read both endpoints when commissioning; omit `-Runner` and
`-Platform` unless the current routing or a real platform requirement calls for them.
The card and every checkpoint are **Any**: ordinary Linux execution is sufficient;
the same rows qualify on Windows. No native Windows behavior is changed and no
Windows-only acceptance gate is added. Linux needs Git, pwsh, pinned SDK and the
testcontainer Docker/Postgres lane; the checkpoint script adds `UseAppHost=false`.
These fixtures do not require the missing Linux FakeClaude apphost. A future fixture
choice that launches it requires a plan revision, not a skip reported as coverage.

Retain the Plan brief's CARD-0823 exception: **do not launch `tools/Antiphon.Checkpoints`**.
Run one row at a time with `scripts/run-checkpoint.ps1` under the build-slot wrapper.
Pass the `.ps1` directly after `--`, as below: the wrapper invokes a PowerShell script
in its own process (`scripts/build-slot.ps1:120`), and the broker returns that holder's
existing lease (`src/Antiphon.SessionRunner/BuildSlotBroker.cs:79`). Do not insert a
second `pwsh` there, which would acquire another slot while the first remains held.
The row driver still performs its normal slot handling. Use no `-NoSlot` override.

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card0603-CP-1 -- ./scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Tests -OutputPath bin-c603-cp1/ -Filter '/*/Antiphon.Tests.Application/(RepairSourceRecoveryLandingTests*)|(RepairSourceLandRefusalTests*)/*' -Expect RepairSourceRecoveryLandingTests.C603_FailedOwnerLandsReviewedRepairedTip,RepairSourceRecoveryLandingTests.C603_RepairSuccessDoesNotAuthorizeUnreviewedOwnerLand,RepairSourceLandRefusalTests.C603_RepairLandRefusalNamesReviewedOwnerRecovery,RepairSourceLandRefusalTests.C499_V24ii_LandRequestIsRefusedForARepairTask,RepairSourceLandRefusalTests.C499_V24iii_ProtocolEntryIsRefusedForARepairTask,RepairSourceLandRefusalTests.C499_V24iv_OwnerLandRequestStillQueues -MinExecuted 10 -ResultsRoot .antiphon/c603-cp1-attempt1
```

Apply that command shape to each table row, with its exact name, output, filter,
expected roster, minimum and a fresh results path. For `-Expect`, pass all selected
`Class.Method` names as one comma-separated argument: CP-1's two V-1/V-2 methods,
V-3 and the three existing C499 methods; CP-2/CP-3's named method-filter operands;
CP-4's six script and three documentation methods. Require the table's **exact** count
and every argument row: `MinExecuted` alone is only a floor and cannot detect an
accidental extra selection. All four rows include their own
isolated build; no `-NoBuild` is planned. Run serially, including builds, because
OutputPath does not isolate shared `obj/`. Retain each driver's build/run exit
receipt, TRX roster and `CHECKPOINT CP-n` line with actual executed/passed/failed/
skipped counts, commit and reruns. Count the expected argument expansions, not
assertions. A nonzero skip for any planned execution needs investigation. Exit 4
is a slot timeout to report, never permission to run unleased; report any driver
fallback to unleased execution as such. Stop and diagnose a red row; rerun that
same row only after a committed repair or a stated, evidenced transient cause.

CARD-0835: before each ordinary row, record full HEAD plus
`git status --porcelain=v1 --untracked-files=all` and require clean source; save those
receipts outside tracked source. Check the same after completion and forbid source
edits while the row runs. A bare `commit=HEAD` receipt cannot certify dirty source.
For Mutation record the intentional patch/digest and phase separately; never present
a dirty PC run as a clean-SHA ordinary result.

CARD-0818/0828 affect checkpoint-tool test classes, not this selection. Deliberately
omit the broad Unit/Checkpoints namespace/full-suite rows that would import those
hangs; the exact affected Unit documentation class and integration methods provide
this card's bounded scope. No retry loop, broad baseline suite, build of the solution,
client build or E2E run is authorized by this table. Any added run must have a stated
reason and cost in the report. No builds/tests are required for this documentation-only
TestDesign change; runtime counts and PCs remain pending Code/Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c603-cp1/` | repair-to-publication | `/*/Antiphon.Tests.Application/(RepairSourceRecoveryLandingTests*)\|(RepairSourceLandRefusalTests*)/*` | V-1, V-2, V-3, R-2 | exactly 10 passed: V-1=1, V-2=3, V-3=3, C499=3; 6 methods across both classes; 0 failed/skipped | 10 | 12 |
| CP-2 | S1-S3 | `tests/Antiphon.Tests -> bin-c603-cp2/` | recovery-admission | `/*/Antiphon.Tests.Application/AgentTaskLandRequestTests/(C603_PlainFailedCodeOwnerRefusalNamesReviewedRecovery*)\|(C753_RecoveryRefusesIneligibleOwnerOrReviewWithoutRequest*)\|(C753_ExplicitReviewedRecoveryKeepsTerminalOwnerStatus*)\|(C753_RecoverySupersedesOnlyAfterMergeHelperIsInactive*)` | V-4, R-1 | exactly 14 passed: 1 new result (internal boundary cases) + 9 + 2 + 2 existing arguments; all 4 methods; 0 failed/skipped | 14 | 8 |
| CP-3 | S1-S3 | `tests/Antiphon.Tests -> bin-c603-cp3/` | recovery-publication-guards | `/*/Antiphon.Tests.Application/AgentTaskLandAdoptionTests/(C753_ConflictedRecoveryPreservesOwnerStatusAndRefusesPlainResume*)\|(C753_SupersededConflictedOperationGetsFreshReviewedPublication*)\|(C753_DirtyOwnerCheckoutRefusesBeforeReviewedSourceMutation*)\|(C753_MovedReviewedRemoteTipRefusesWithoutTargetPublication*)\|(C753_ReviewedSelfRecoveryAlignsRewrittenOwnerSourceAndPublishes*)\|(C753_PublishedRecoveryCleanupRetryUsesPersistedOwnerAuthority*)` | R-3 | exactly 7 passed: 2 conflict arguments + 5 single existing results; all 6 methods; 0 failed/skipped | 7 | 12 |
| CP-4 | S1-S3 | `tests/Antiphon.Tests -> bin-c603-cp4/` | recovery-cli-and-recipe | `/*/Antiphon.Tests.Application/(DelegateScriptAdoptTests*)\|(RepairSourceDocumentationTests*)/*` | V-5, V-6, R-2 | exactly 9 passed: script 5 existing + V-5, documentation 2 existing + V-6; all 9 methods across both classes; 0 failed/skipped | 9 | 8 |

### Cost

Ordinary Code checkpoint floor: **40 minutes**, four isolated builds and 40 executed
test results (10 new, 30 existing). Estimates include roughly 5-7 minutes per cold
Antiphon.Tests build; they are planning allowances, not measurements from this task.
CP-1 and CP-3 are the slow rows: real Git, schema creation, dispatch/settlement and
publication, estimated 12 minutes each. CP-2 and CP-4 still pay a fresh build and
schema/PowerShell startup; neither is a seconds-only "unit check". Cold restore/image
pulls and broker queue time are additional. Windows may take longer; report actuals
rather than dropping a row to meet the estimate. TestDesign retained the estimates
without running builds; there is no new timing evidence to justify changing them.

Allow 2-4 hours for implementation/fixture authoring beyond the 40-minute ordinary
floor, 1-2 hours for separate TestDesign, and about an hour for Review. Post-land
Mutation is separate: eight method-scoped control cycles, **24 isolated phase builds
and 48 executions** (16 baseline, 16 red, 16 restored green), estimated **120-240
minutes**, particularly the real-Git flow. This includes repeated selection of all
three V-2 arguments for each of PC-2/3/4. No additional build-count saving is claimed:
the requested four ordinary builds and independent fault cycles are preserved. Do not
hide those runs in the ordinary estimate or claim PC-clean from the Code checkpoints.

## Acceptance and handoff

TestDesign confirmed the six new methods/ten expanded executions by source inspection,
specified an assertion failure and phase roster for all eight PCs, and checked the
same-process wrapper recipe against its implementation. Builds/tests/PCs were not run.
Code implements the three slices and reports all four CP rows against committed clean
source. Review checks that only guidance
changed in production and that the joined regression actually publishes through
the historical Failed owner.

After ordinary Review and confirmed land, the caller records the usual linked
SourceLanding Mutation obligation. Before relying on the changed refusal in a real
incident, confirm the served `/api/version` includes the landed commit and `land-v2`;
publication alone is not activation. If a real Failed-owner RepairSource incident is
available, use its current owner-bound Review and explicit recovery command and retain
the receipt; do not manufacture code or repeat publication just for this card.
Plan/TestDesign neither deploys nor authorizes changing historical task statuses.

Next: **Code**, using this artifact. The core recovery implementation already
exists; finish the incident regression and actionable handoff, preserving that contract.
