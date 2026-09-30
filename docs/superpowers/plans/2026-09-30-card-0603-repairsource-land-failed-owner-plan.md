# CARD-0603: finish the RepairSource-to-Failed-owner landing handoff

Date: 2026-09-30. Stage: Plan; next stage: TestDesign. Source examined:
`d7456a2352d15391f37eca8781c16db499a3759d`. This artifact changes no product code.
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

This is a proposed executable design for the separate TestDesign stage, not a claim
that any test or positive control ran during Plan. All paths below are under
`tests/Antiphon.Tests/Application/` unless qualified otherwise.

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
  unchanged remote target and unchanged owner failure fields. The intentionally
  wrong-subject row isolates that check from the separately tested ref check.
- **V-3 (3 new executions)** —
  `RepairSourceLandRefusalTests.C603_RepairLandRefusalNamesReviewedOwnerRecovery`,
  arguments `request`, `worker`, `protocol`. Exercise `RequestAsync`,
  `RunRequestAsync` and leased `AgentTaskLandingProtocol.RunAsync` directly for a
  repair task with a Failed owner. Every entry throws the existing repair-owner code;
  its message contains the full owner ID, owner-as-Review-subject requirement,
  Clean Final/Full requirement and all three recovery flags. No requests or
  operations are created for either task. Keep all three existing C499 tests.
- **V-4 (1 new execution)** —
  `AgentTaskLandRequestTests.C603_PlainFailedCodeOwnerRefusalNamesReviewedRecovery`.
  Plain Land on a Failed Code/Worktree owner remains a Conflict with the original
  status-refusal text and adds the explicit owner recovery recipe; no request/event/
  queue activation or status mutation. Exercise the service, not just the helper.
- **V-5 (1 new execution)** —
  `DelegateScriptAdoptTests.C603_RecoverReviewedSourcePostsOwnerIdentityToV2`.
  Use `LandApiStub` and the real PowerShell script. Full owner GUID, SHA and Review
  GUID yield one version probe and one POST to that owner's `/land/v2`, body has
  `recoverReviewedSource=true` and matching SHA/Review, no `adoptFromTaskId`, zero
  legacy or repair-target POSTs. Exit 0. The existing invalid-arguments test remains.
- **V-6 (1 new execution)** —
  `RepairSourceDocumentationTests.C603_RepairSourceFailedOwnerRecipeNamesOwnerBoundReview`.
  Read the new bounded RepairSource-recovery subsection, the ops cross-link and the
  bundle's corresponding paragraph. Assert the owner Review subject, Failed status,
  full SHA/Review flags, and distinction from `-StartRef` adoption there. A flag found
  elsewhere in the document cannot satisfy this test. This is documentation-contract
  evidence only; V-1 supplies behavior evidence.
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

Fixture requirements: register `ILandingVerifier` with a controlled verifier for the
joined test before graph registration (or explicitly replace it), so fixture landing
does not run a nested repository build. Git, lease ownership, source resolution and
remote observation remain real. No real Program host, provider CLI or production
session runner is required. `RepairSourceWorld` already resolves the landing graph
through `DelegationTestServices`; avoid a copied container graph. Own and await every
command; use isolated schema counts and the assembly-local ProcessSpawnLimit.

### Positive controls

Execute these only in the commissioned post-land SourceLanding Mutation stage.
For each PC use the named method prefix plus `*`, separate fresh baseline/red/green
outputs and results, exact source restoration and a rebuilt green. Require driver
exit 1 with the intended assertion failure for red; a compiler error, fixture error,
missing TRX, timeout or zero selected tests is not a control. Each new test method
has a mapped production or documentation fault; argument rows are identified below.

| PC | Test / argument coverage | Deliberate fault and required red observation |
|---|---|---|
| PC-1 | V-1 | Remove Failed from `LandApproval.RecoveryStatusEligible`. The positive flow cannot reach confirmed publication and fails on the recovery admission/outcome, despite real successful repair settlement. Restore and require publication green. |
| PC-2 | V-2 `plain` | Disable only the ordinary non-Succeeded admission guard in `AgentTaskLandService.RequestAsync`. Plain Land now queues; the expected Conflict/zero-request assertion must fail. |
| PC-3 | V-2 `missing-review` | In request admission bypass both the missing-review rejection and its immediate evidence-load call when the ID is absent, so null does not merely crash. The request queues without Review; expected `recovery_review_required` and zero persisted requests fail. |
| PC-4 | V-2 `repair-subject` | Disable the subject-ID comparison in `LandApproval.LoadUsableEvidenceAsync` while keeping SHA/ref/repository gates. The intentionally otherwise-matching row admits incorrectly; the subject refusal/zero-request assertion fails. |
| PC-5 | V-3 all three entries | Remove `-RecoverReviewedSource` from the shared repair refusal guidance. All three entry-point argument results fail their actionable-message assertion while retaining their existing 409 code. This controls the new guidance, not merely the old refusal. |
| PC-6 | V-4 | Restore the old ordinary Failed-owner refusal text without the recovery suffix. The new message assertion fails although the call still refuses. |
| PC-7 | V-5 | Remove the `recoverReviewedSource` body assignment in `scripts/delegate.ps1:820`. The captured real script POST lacks the flag and fails the JSON-body assertion. |
| PC-8 | V-6 | Remove the owner-as-Review-subject sentence from the bounded new orchestration recipe while retaining other recovery documentation. The subsection assertion must fail. |

PC-2/3 share the request method and run separately. PC-1/4/5 may share the approval
utility after S2 and also run separately; do not batch interacting faults. V-1/V-2
are contract regressions expected to be green on current behavior, not pretend
pre-fix reds. V-3/V-4/V-6 can expose the present missing guidance before S2/S3; V-5
pins existing correct serialization. Mutation proves all can detect an actual fault.

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

Use the brief's CARD-0823 exception: **do not launch `tools/Antiphon.Checkpoints`**.
Run one row at a time with `scripts/run-checkpoint.ps1` under the build-slot wrapper.
Pass the `.ps1` directly after `--`, as below: the wrapper invokes a PowerShell script
in its own process (`scripts/build-slot.ps1:120`), and the broker returns that holder's
existing lease (`src/Antiphon.SessionRunner/BuildSlotBroker.cs:79`). Do not insert a
second `pwsh` there, which would acquire another slot while the first remains held.
The row driver still performs its normal slot handling. Use no `-NoSlot` override.

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card0603-CP-1 -- ./scripts/run-checkpoint.ps1 -Name CP-1 -Project tests/Antiphon.Tests -OutputPath bin-c603-cp1/ -Filter '/*/Antiphon.Tests.Application/(RepairSourceRecoveryLandingTests*)|(RepairSourceLandRefusalTests*)/*' -Expect RepairSourceRecoveryLandingTests,RepairSourceLandRefusalTests -MinExecuted 10 -ResultsRoot .antiphon/c603-cp1-attempt1
```

Apply that command shape to each table row, with its exact name, output, filter,
expected classes, minimum and a fresh results path. All four rows include their own
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
reason and cost in the report. No builds/tests are required for this Plan-only file.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c603-cp1/` | repair-to-publication | `/*/Antiphon.Tests.Application/(RepairSourceRecoveryLandingTests*)\|(RepairSourceLandRefusalTests*)/*` | V-1, V-2, V-3, R-2 | exactly 10: 4 new joined-flow + 3 new entry-point + 3 existing; both classes, 0 failed/skipped | 10 | 12 |
| CP-2 | S1-S3 | `tests/Antiphon.Tests -> bin-c603-cp2/` | recovery-admission | `/*/Antiphon.Tests.Application/AgentTaskLandRequestTests/(C603_PlainFailedCodeOwnerRefusalNamesReviewedRecovery*)\|(C753_RecoveryRefusesIneligibleOwnerOrReviewWithoutRequest*)\|(C753_ExplicitReviewedRecoveryKeepsTerminalOwnerStatus*)\|(C753_RecoverySupersedesOnlyAfterMergeHelperIsInactive*)` | V-4, R-1 | exactly 14: 1 + 9 + 2 + 2; all four methods, 0 failed/skipped | 14 | 8 |
| CP-3 | S1-S3 | `tests/Antiphon.Tests -> bin-c603-cp3/` | recovery-publication-guards | `/*/Antiphon.Tests.Application/AgentTaskLandAdoptionTests/(C753_ConflictedRecoveryPreservesOwnerStatusAndRefusesPlainResume*)\|(C753_SupersededConflictedOperationGetsFreshReviewedPublication*)\|(C753_DirtyOwnerCheckoutRefusesBeforeReviewedSourceMutation*)\|(C753_MovedReviewedRemoteTipRefusesWithoutTargetPublication*)\|(C753_ReviewedSelfRecoveryAlignsRewrittenOwnerSourceAndPublishes*)\|(C753_PublishedRecoveryCleanupRetryUsesPersistedOwnerAuthority*)` | R-3 | exactly 7: 2 + 1 + 1 + 1 + 1 + 1; all six methods, 0 failed/skipped | 7 | 12 |
| CP-4 | S1-S3 | `tests/Antiphon.Tests -> bin-c603-cp4/` | recovery-cli-and-recipe | `/*/Antiphon.Tests.Application/(DelegateScriptAdoptTests*)\|(RepairSourceDocumentationTests*)/*` | V-5, V-6, R-2 | exactly 9: 6 script + 3 documentation; both classes, 0 failed/skipped | 9 | 8 |

### Cost

Ordinary Code checkpoint floor: **40 minutes**, four isolated builds and 40 executed
test results (10 new, 30 existing). Estimates include roughly 5-7 minutes per cold
Antiphon.Tests build; they are planning allowances, not measurements from this task.
CP-1 and CP-3 are the slow rows: real Git, schema creation, dispatch/settlement and
publication, estimated 12 minutes each. CP-2 and CP-4 still pay a fresh build and
schema/PowerShell startup; neither is a seconds-only "unit check". Cold restore/image
pulls and broker queue time are additional. Windows may take longer; report actuals
rather than dropping a row to meet the estimate. TestDesign should revise cost if
fixture measurements justify it, before Code commissions a changed manifest.

Allow 2-4 hours for implementation/fixture authoring beyond the 40-minute ordinary
floor, 1-2 hours for separate TestDesign, and about an hour for Review. Post-land
Mutation is separate: eight method-scoped control cycles with distinct phase builds
can add 2-4 hours, particularly the real-Git flow. Do not hide those runs in the
ordinary estimate or claim PC-clean from the Code checkpoints.

## Acceptance and handoff

TestDesign confirms the six new methods/ten expanded executions, makes every PC
specific enough to produce the expected assertion red, and validates the same-process
wrapper recipe without widening scope. Code implements the three slices and reports
all four CP rows against committed clean source. Review checks that only guidance
changed in production and that the joined regression actually publishes through
the historical Failed owner.

After ordinary Review and confirmed land, the caller records the usual linked
SourceLanding Mutation obligation. Before relying on the changed refusal in a real
incident, confirm the served `/api/version` includes the landed commit and `land-v2`;
publication alone is not activation. If a real Failed-owner RepairSource incident is
available, use its current owner-bound Review and explicit recovery command and retain
the receipt; do not manufacture code or repeat publication just for this card.
The Plan stage neither deploys nor authorizes changing historical task statuses.

Next: **TestDesign**, using this artifact. The core recovery implementation already
exists; finish the incident regression and actionable handoff, preserving that contract.
