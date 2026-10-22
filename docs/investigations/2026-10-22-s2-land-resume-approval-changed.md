# CARD-0167 S2: replacement operations inherit obsolete Review evidence

Debug task: `3505c834-474d-4663-b9df-2e7212662719`. Observed on 2026-10-22,
10:10–10:20 UTC. This investigation changed documentation and created the explicitly
requested Backlog card only. It did not request a land, alter operational state,
restart a service, or touch the Windows qualification.

## Operator action

Use the existing reviewed-owner recovery path with the **new** approval:

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Land 26316601-9107-4837-971e-878be98a1d46 -ExpectedSourceSha 98b37901ba4976dcf06428d8bc9c19a7233ee153 -ReviewEvidenceId c59210da-dbfe-4796-897b-7487fc4a9e3a -RecoverReviewedSource
```

**Not executed.** This command removes the identified approval-inheritance failure;
publication still depends on the ordinary source/worktree, target, verification and
build-slot checks. It is a real land request and may align the owner's checkout under
the recovery protocol. The current source already equals the reviewed SHA, so no new
source rewrite is needed for this incident. Wait for its correlated publication
outcome and caller receipt before starting S3 on the assumption that S2 has landed.

The evidence GUID above is copied from the API and database. The brief's abbreviated
hyphenated example had an incorrect grouping; its 32-hex form was correct.

The original conflict is **not** still blocking admission. Both refused retries
created fresh operations with the new SHA but inherited the **old Review evidence ID**.
`-RecoverReviewedSource` selects the branch in the factory that binds both approval
fields to the current request. No new Review or database edit is needed to address
this mismatch.

## Evidence and identities

`GET /api/version` returned live server SHA
`ceb6e3d6b78471db6831258699c493b8eb2d619b` and capability `land-v2`.
Source citations below use **that live commit**, obtained with `git show`, unless
explicitly marked checkout. The assigned Debug base is
`60f594b762e9a1b2d5d77712c0a7e70d1fd4f2b8`; some line numbers differ.

Reads used the documented task/version HTTP routes and the documented desktop
PostgreSQL port 17280 on the API host. The database connection issued
`BEGIN READ ONLY`, an eight-second local statement timeout, task-scoped SELECTs,
then `ROLLBACK`; it selected no credential columns. Every network command had a
hard timeout. No private-notes route, provider authentication file, or Codex home
was accessed. Temporary raw responses are not committed; the decisive rows are
transcribed below.

| Alias | Full identity |
|---|---|
| Owner | `26316601-9107-4837-971e-878be98a1d46` |
| Merge helper | `23b40f9c-9848-4659-9704-8a8302068dab` |
| Old source A | `4cbb2d30476971f8418eb826e211127d7c296c52` |
| New source B | `98b37901ba4976dcf06428d8bc9c19a7233ee153` |
| Old evidence E1 | `a7a4b3b3-688b-4987-82e5-bad89d60fb50` |
| New evidence E2 | `c59210da-dbfe-4796-897b-7487fc4a9e3a` |
| First request R0 | `16b73b9e-2a92-432a-ae54-1906b68ca173` |
| Retry request R1 | `03500e19-2d6c-4f17-b0cd-6ca8fa14330b` |
| Retry request R2 | `0fbd3a1f-c348-46f4-ac89-0d30d9c3f5a6` |
| Original operation O0 | `efd767f0-bb79-44fa-bf44-3aca3de7c160` |
| First retry operation O1 | `ef9d9ace-c5cc-4faf-8997-9230d6bb14b8` |
| Second retry operation O2 | `6a0f010c-c28e-4bfb-918b-d1785490418a` |

The database query scoped `AgentTasks`, `AgentTaskLandRequests`,
`AgentTaskLandings`, and `StageOutcomes` to the owner. Enum values below are
decoded from `server/Domain/Enums/LandingEnums.cs`: phase 11 = Refused,
request state 4 = Completed, 5 = Superseded, mode 0 = Fresh.

### Requests: persisted approval and final state

| Request | Requested UTC | ExpectedSourceSha | ReviewEvidenceId | LandingOperationId | State / IsPending | RecoveryMode |
|---|---|---|---|---|---|---|
| R0 | 08:29:09.509912 | A | E1 | O0 | Superseded / false | None |
| R1 | 09:55:11.363897 | B | E2 | O1 | Completed / false | None |
| R2 | 09:56:48.703896 | B | E2 | O2 | Completed / false | None |

R0's `LastWriterOperation=merge-supersession`,
`LastWriterAt=2026-10-22T08:55:44.047376Z`, terminal event
`20bebded-3230-406a-8fd2-bc34025ebdfa`. R1 and R2 have writer `terminal` at
09:55:24.918559 and 09:59:46.167165 respectively. All three have resolved source
snapshots and null VerifyFilter; none has a pending request or superseding-request
pointer. Merge settlement supersedes R0 directly; it does not create a recovery
request on its behalf.

### Operations: the actual mismatch

| Operation | Created UTC | OriginalSourceSha / ReviewedSourceSha | ReviewEvidenceId | ApprovalLandRequestId | Active | Phase / Publication / Mode | LastReason |
|---|---|---|---|---|---|---|---|
| O0 | 08:29:19.653886 | A / A | E1 | R0 | false | Refused / Unconfirmed / Fresh | rebase_conflict |
| O1 | 09:55:18.959896 | B / B | **E1** | R1 | false | Refused / Refused / Fresh | resume_approval_changed |
| O2 | 09:59:38.975342 | B / B | **E1** | **R1** | true | Refused / Refused / Fresh | resume_approval_changed |

All operations have `Cleanup=NotStarted`, null `VerifiedSourceSha`, null
`RemoteConfirmedAt`, null VerificationFilter and RecoveryMode None. Owner status
is Succeeded; `ActiveLandingId=O2`, `CurrentLandRequestId=R2`, and
`LandRequestedAt=null`. The historical DeliverableRef remains A.

For R1 versus O1, the source SHA and approval request ID match; **only evidence
differs**. For R2 versus O2, the source SHA still matches; **evidence and approval
request ID differ**. R1 predates R2, so the protocol's permitted old-request
provenance check passes; the evidence equality check then fails. A request's
Completed state and an operation's Refused phase describe different entities;
there is no operation `Status=Conflicted` or `Status=NeedsResolution` field.

`GET /api/agent-tasks/{owner}` independently exposes the R2/O2 fields and both
LandRefused timeline events. R1's event at 09:55:24.869588 names O1; R2's at
09:59:46.163684 names O2. Both snapshot `landingMode=Fresh`. Their Outcome
notifications are Confirmed. These are confirmed refusal deliveries, not
publication evidence.

The Merge helper's detail is Succeeded / Shared / Merge, session Stopped. Its
report records rebase onto `a38e1e1bd0f515d6ae7a6682f537a5fe5e4cdd49`, resolving
the enum add/add with master's bytes, and a leased force-push to B. Review
`bfee2bb0-7900-40f1-8f44-44d564f7b5a1` independently reports tree equivalence,
CP-2 11/11 and the additional CP-1 15/15 at B, with source-qualified receipts.
These test results are prior Review evidence, not tests run by Debug.

Both E1 and E2 are stored Clean Review rows with the same owner, repository and
`refs/heads/feat/card-task-26316601`; `reviewedSourceClean=true`,
`CommissionedRound=Final`, `OrdinaryScopeCompleted=Full`. Their reviewed SHAs are
A and B respectively. The owner-scoped Review rows have no supersession link.
The successful admission of R1/R2 also establishes that E2 passed usability
validation at those requests. A read-only `git ls-remote` during this investigation
returned exactly B for that owner branch.

## Exact code path

1. Ordinary Merge settlement calls `ResolveConflictedParentAsync`.
   `AgentTaskReplyService.cs:2544` restores Succeeded and clears the land clocks;
   `:2556`–`:2563` supersedes the pending NeedsResolution request and stamps
   `merge-supersession`. It does not rewrite old Review evidence or the owner's
   historical completion tip. The recovery-mode exclusion is at `:2539`.
2. New admission validates E2 against B, then creates R1/R2:
   `AgentTaskLandService.cs:253`–`:259`. A genuinely pending ordinary request would
   reject changed SHA/evidence at `:157`–`:163`; R0 was already superseded here.
3. `AgentTaskLandService.cs:602`–`:607` makes `resumeExisting=false`: the predecessor
   phase is Refused and its approval request differs. `:632`–`:634` resolves the
   new request. This disproves the suspected selection of the original operation
   for resume.
4. `AgentTaskLandSourceResolver.cs:194`–`:210` loads the predecessor but returns it
   only if active, unpublished and **not Refused**. Here it calls the factory for
   a replacement. `AgentTaskLandRequestWriter.cs:211`–`:226` atomically deactivates
   the predecessor, attaches the new operation, and advances the task/request
   pointers.
5. `LandOperationFactory.cs:92` takes B from the current request.
   `:95`–`:97` retains the previous ApprovalLandRequestId when its SHA equals B;
   hence O1 binds R1, while O2 also binds R1. **`:98`–`:99` always prefers
   `previous?.ReviewEvidenceId` for RecoveryMode.None**, regardless of changed SHA
   or changed evidence. Both replacements therefore copy E1. This expression is
   identical in the live commit and assigned checkout.
6. The resolver has already attached the operation when the protocol runs.
   `AgentTaskLandingProtocol.cs:189`–`:210` enters its existing-operation branch,
   now with newly created O1/O2, not O0. With Phase Inspected and no source pin,
   Mode remains Fresh. `RecheckApprovalAsync` first validates the request's E2
   against B at `:688`–`:690`, which passes. The request-link checks at
   `:729`–`:735` pass. The SHA check at `:738` passes. The **evidence equality
   check at `:740` fails** with `resume_approval_changed`. The same checks are at
   checkout lines 748/750. The refusal name is broader than an actual resumed
   operation; it is also used on this newly attached operation.

The protocol guard is doing its job. Fixing it to allow E1/E2 disagreement would
hide the factory defect and corrupt approval provenance.

## Conflict protocol and alternatives

The owner policy is `docs/orchestration-loop.md:180` (reviewed-owner recovery),
`:194` (NeedsResolution supersession), `:201` (recovery conflict needs another
explicit reviewed recovery), and `:1634` (rewritten pushed source recovery).
The HTTP/CLI contract is `docs/ops-http.md:194`–`:195`.

An ordinary NeedsResolution request retains its approval and rejects changed
fields. A successful ordinary Merge helper supersedes that request, as happened
here. A refused operation is retained for history and replaced on the next
explicit approved attempt. A conflict during an already reviewed recovery keeps
NeedsResolution; a completed helper cannot turn it into an ordinary approval.
The caller must explicitly commission the new reviewed recovery. Live helpers,
live Queued/Held/Running requests and uncertain prior publication remain guards.

| Proposed action | Result or risk |
|---|---|
| Recommended command above: B/E2 plus `-RecoverReviewedSource` | Existing supported path. Owner Code/Worktree/Succeeded and E2 Final/Full meet admission requirements. Factory chooses current request ID and E2, bypassing the defective inheritance. Fresh source/worktree and target checks can still refuse; no end-to-end success is claimed. |
| Repeat ordinary B/E2 land | Produces another fresh operation that again copies E1. Retrying alone cannot repair the approval tuple. |
| Reissue A/E1 | A is no longer the owner branch tip. Resolver rejects reviewed-source mismatch; tree equivalence does not authorize moving the branch back. Restoring A would discard the Merge rewrite and likely replay the original conflict. |
| Reissue B/E1 | Admission rejects `review_evidence_sha_mismatch`: E1 reviewed A. |
| `-Land 23b40f9c-9848-4659-9704-8a8302068dab` | The Merge helper is Shared, with no owned branch. Admission requires Worktree (`AgentTaskLandService.cs:94`–`:95`). It is not a landing owner. |
| `-FromTask 23b40f9c-9848-4659-9704-8a8302068dab` | Adoption requires a same-card **Code/Worktree** source (`:214`–`:220`); this helper is Merge/Shared. |
| Re-review A or re-review B again | A no longer matches the current branch. Another E2-like Review of B still encounters predecessor E1 inheritance on ordinary land. Existing E2 already provides the necessary new-tip Review. |
| Abandon/supersede through an invented verb or SQL update | No generic documented operation-abandon command is needed or supplied. The documented supersession mechanism is explicit reviewed recovery. Manual row changes would bypass approval/history/concurrency guarantees. Cleanup abandonment is a separate published-residue concept. |
| Omit the evidence ID | Not a useful repair: ordinary factory construction still copies E1, and subsequent evidence/SHA validation can fail. Do not discard the existing reviewed approval to evade a guard. |

The historical settlement-tip warning is informational. In
`ReviewEvidenceBindingService.cs:54`–`:64`, it compares the new Review SHA to the
owner's completion progress, then still returns the new candidate. LandApproval
checks subject/ref/repository and the exact reviewed SHA (`:107`–`:120`), not
equality to DeliverableRef. The warning did not reject E2 and is not the cause.

## Product fix and filed work

Created **CARD-1485**, Backlog, High/Now, on board Antiphon
`8988ca03-7414-47ad-b0b6-51556c701703`; card ID
`e6b5bbd4-1f48-42df-b7b1-7e79c09b06d9`.

Duplicate checks completed: searches `resume_approval_changed` (0), `landing`
(309), `approval` (37), plus all 1,483 cards and the CARD-1443..1483 range. All
enumerations completed without truncation. Read full CARD-1446 and CARD-1449
descriptions: cleanup retirement and prior refused-retry regressions overlap in
area but do not cover this stale-evidence copy. CARD-1484 appeared concurrently;
its full description concerns CARD-1462 S4 tests and is unrelated.

The narrow fix belongs in `LandOperationFactory`: choose the approval as one
consistent tuple. When an accepted replacement changes SHA, Review evidence
(including explicit null), or approval authority, bind both ReviewEvidenceId and
ApprovalLandRequestId to that request. Preserve original provenance for genuinely
identical-approval retries and preserve publication/cleanup recovery behavior.
Retain predecessor rows and pins; do not weaken the protocol equality checks.

**Automatic supersession/rebinding decision:** ordinary Merge settlement already
supersedes the conflict request. A new explicitly approved land should replace the
refused operation and bind its new Review automatically. Merge completion alone
must not transfer E1's approval to B or rewrite the historical settled tip. Record
post-Merge lineage separately if a later UX change needs it; no new schema or
automatic approval is necessary for this fix. The subject stays the original Code
owner; E2 already correctly binds that subject/ref to B.

CARD-1485 contains a 34–49 minute Code brief: tests-first commit, one production
fix, eight final integration executions through `LandingProtocolHarness` and its
isolated database. It includes the observed live red witness above and requires
an executable expected-red witness before the fix. Production/Windows runs and
bulk cleanup are excluded. The ordinary selection is reproduced below for a
durable handoff; methods are **proposed**, not present or run by Debug.

### Verification design

- V-1 `ReplacementAfterSourceRewriteUsesNewReview`: persisted A/E1 refusal, source
  becomes B, valid E2 approval; observe new operation B/E2/new request, old row
  retained/inactive, and successful publication. The first binding assertion must
  fail before the fix with expected E2 versus actual E1.
- V-2 `ReplacementAtSameShaUsesNewReview`: A/E1 to A/E2 binds both new evidence and
  new request, while retaining the old row.
- V-3 `ExplicitCallerReplacementDoesNotInheritReview`: accepted unlatched caller
  approval with null evidence stays null, without bypassing SHA/ref guards.
- V-4 `IdenticalApprovalRetryRetainsProvenance`: same SHA/evidence/filter retains
  the original approval linkage, creates a distinct operation and re-verifies.
- V-5 `OwnerReviewedRecoveryBindsCurrentApproval`: pins the existing workaround
  with a refused predecessor, fresh request/E2 and publication.
- R-1/R-2 existing `C488_PendingEvidenceImmutable` and
  `C488_PendingExpectedShaImmutable`; R-3
  `C488_OriginalRequestProvenanceRetained`.

Use new class `LandReplacementApprovalTests` with exactly those five
non-parameterized methods. Invoke CP-1 once at the tests-only S1 commit as the
intentional expected-red witness; retain that failing assertion. At S2, run CP-1
and CP-2 through the checkpoint tool, qualify clean source receipts, then remove
the alternate output directories. These are the closed test selections; no full
assembly run is required. Authoring 20–35 minutes, expected-red build/run about
6 minutes, final ordinary floor 8 minutes. Separate ordinary Review follows.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-landapproval/` | rewritten-source | `/*/*/LandReplacementApprovalTests/ReplacementAfterSourceRewriteUsesNewReview` | V-1 | 1 executed, 0 failed/skipped at S2; required expected-red invocation at S1 | 1 | 6 |
| CP-2 | S1-S2 | `CP-1` | approval-boundaries | `/*/*/(LandReplacementApprovalTests)\|(AgentTaskLandApprovalRequestTests)\|(AgentTaskLandApprovalRecoveryTests)/(ReplacementAtSameShaUsesNewReview)\|(ExplicitCallerReplacementDoesNotInheritReview)\|(IdenticalApprovalRetryRetainsProvenance)\|(OwnerReviewedRecoveryBindsCurrentApproval)\|(C488_PendingEvidenceImmutable)\|(C488_PendingExpectedShaImmutable)\|(C488_OriginalRequestProvenanceRetained)` | V-2,V-3,V-4,V-5,R-1,R-2,R-3 | all 7 listed methods, 0 failed/skipped | 7 | 2 |

## Investigation validation and limits

The cause is established by the exact persisted request/operation disagreements,
correlated refusal events, and matching live factory/protocol source. No source
or test files changed; no build/test driver ran. Earlier Review counts above are
attributed evidence only. The recovery command is code-validated and meets the
observed admission conditions, but was deliberately not executed. Any later
source movement, newly superseded Review, open request or worktree obstruction
must be assessed from its own returned refusal rather than silently bypassed.

To repeat the decisive database read under the documented connection, use a
read-only transaction and these bounded SELECTs (no UPDATE):

```sql
BEGIN READ ONLY;
SET LOCAL statement_timeout = '8s';
SELECT "Id", "State", "IsPending", "ExpectedSourceSha", "ReviewEvidenceId",
       "LandingOperationId", "RecoveryMode", "LastWriterOperation", "LastWriterAt"
FROM "AgentTaskLandRequests"
WHERE "TaskId" = '26316601-9107-4837-971e-878be98a1d46'
ORDER BY "RequestedAt";
SELECT "Id", "Active", "Phase", "Publication", "Mode", "OriginalSourceSha",
       "ReviewEvidenceId", "ApprovalLandRequestId", "LastReason"
FROM "AgentTaskLandings"
WHERE "TaskId" = '26316601-9107-4837-971e-878be98a1d46'
ORDER BY "CreatedAt";
ROLLBACK;
```

--- next stage ---
next: decide
handoff: pwsh -NoProfile -File scripts/delegate.ps1 -Land 26316601-9107-4837-971e-878be98a1d46 -ExpectedSourceSha 98b37901ba4976dcf06428d8bc9c19a7233ee153 -ReviewEvidenceId c59210da-dbfe-4796-897b-7487fc4a9e3a -RecoverReviewedSource ; operator action, not run by Debug. Wait for the correlated publication result before S3. Commission CARD-1485 separately for the factory fix and closed checkpoint selection above.
artifact: docs/investigations/2026-10-22-s2-land-resume-approval-changed.md
