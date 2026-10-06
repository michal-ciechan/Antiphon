# CARD-1109: settle a Code report whose pushed commits the desktop could not fetch, on the runner's attested ancestry

Date: 2026-10-07. Plan task: `f772e6a5-c3a6-4efa-9242-7c961f097613` (Frontier, Linux runner mirror).
Inspected source: `3687e07bc82a1963f27f3838a83493f7ab7263b3` (origin/master at planning time; the task
branch `feat/card-task-f772e6a5` was fast-forwarded to it). CARD-1082 S4b inspected on its review
branch `feat/card-task-3fa6dc4f` at `cfa234b05653c86590728b58cbd6f1e764dfb7bc`; CARD-1082 S5, S6 and
S7 are not started.
Card: Antiphon CARD-1109, `c4bf8702-4130-4333-88ab-4adc8321ff08`.
Status: Plan complete with the verification design folded in. **next: decide** (D-1 reverses a note
in CARD-1082 D-3, D-9 makes the Code dispatch wait for a measurement); then land this plan and run
the `### Checkpoints` table as a closed list, slice by slice.

## Verdict on the brief's first question

CARD-1109 is **not** closed by CARD-1082 S5. S5 wires `SettlementSyncDebtPolicy.Classify` and
`BlockReason` into `AgentTaskReplyService` and inserts the debt row; it changes nothing about how
progress is read. The progress arm S3 already landed keeps a Pending result whose objects are not
local Indeterminate: `TaskCompletionProgressService.EvaluateRemoteAsync` gates its Pending branch on
`SourceDescends == true` **and** a local `RevParseCommitAsync(S)` (`server/Application/Services/TaskCompletionProgressService.cs:190-193`)
and otherwise returns `Unknown(runner_sync_lease_busy)` (`:230`); the policy blocks a Code task on an
Indeterminate read (`server/Application/Services/SettlementSyncDebtPolicy.cs:46-49`). The CARD-1082
plan says so in D-4 ("**Residual gap.** Under a lease held for the whole budget by a long land, a Code
task that pushed new commits is lease-busy at the *observation fetch*, its objects are not local, and
it stays Blocked as today"), in Outcome item 4, and keeps the three legacy tests that manufacture
exactly this case Blocked (R-2). S5's own Code test (V-21) makes S's objects local with a test fetch
before holding the lease, so S5 never exercises the unfetched case. The gap is distinct; this plan
closes it.

## Outcome and scope

After CARD-1082 S5 activates, a runner-bound Code task whose report says `done`, whose branch is
pushed to origin at S, and whose settlement sync found the desktop repository lease busy for the
whole budget settles in one of two ways:

- S's objects reached the desktop repository during a free moment of the budget: Succeeded with
  `remoteSync.state = Pending`, progress attributed against S (CARD-1082 D-4).
- S's objects never reached the desktop repository: **Blocked**, `next=decide`, "repair the desktop
  checkout ... then reply", one more model turn to re-settle an identical report.

After this card, the second case settles exactly like the first when the runner that owns the pushed
mirror attests, on the existing `WorkspacePublish` wire, that its mirror tip is S and that S descends
from the dispatch base b. The server already observes S itself on the task's exact owned ref with
`ls-remote`; the runner supplies only the ancestry fact the desktop cannot read without the lease.
Everything else stays Blocked: no runner answer, a runner answer whose tip is not S, a report claim
that is not S, a moved remote baseline, a diverged or dirty answer, or the switch off. The debt
sweep from CARD-1082 S4b later fetches S under the lease and re-reads the ancestry on the server; a
false attestation ends the debt **Held** with the Git reason and reaches attention, and the desktop
checkout is never moved by it.

Out of scope: lease hold time and contention (CARD-0743); fetching without the repository lease
(CARD-0499 G-24, CARD-0657 D-3); a new runner operation that attests claim containment or lists
b..S (deferred, D-2); parking activation (CARD-1083); CARD-1113, CARD-1115 and CARD-1119 (their
items are compatible, see the last ground-truth row); the `git=` header and `deliverable=` for an
attested settlement (they keep today's absence, CARD-1082 D-7).

## Ground truth

| Card/brief assumption | Observed code (file:line at `3687e07bc`) | Design consequence |
|---|---|---|
| A Code task whose fresh commits were never fetched during the budget still ends Blocked. | Confirmed end to end. `TaskProgressGit.ObserveExactRefCoreAsync` reads the tip with `ls-remote` (`server/Infrastructure/Git/TaskProgressGit.cs:112-124`), answers `Present` without a lease only when `cat-file -e` finds the object (`:122-124`), else returns `repository_lease_busy` carrying the advertised SHA when it cannot take the lease (`:128-141`). `RemoteWorkspaceService.SyncOwnedCheckoutAsync` turns that into `Unavailable runner_sync_lease_busy` with `SourceDescends = null` (`server/Application/Services/RemoteWorkspaceService.cs:416-417`). The Pending arm requires `SourceDescends == true` and a local object (`TaskCompletionProgressService.cs:190-193`), otherwise `Unknown(LeaseBusy)` (`:230`). `SettlementSyncDebtPolicy.BlockReason` blocks Code on Indeterminate (`SettlementSyncDebtPolicy.cs:46-49`); today `AgentTaskReplyService.RemoteSyncBlockReason` blocks every unconfirmed result (`server/Application/Services/AgentTaskReplyService.cs:1989-1999`, applied at `:864-866`). | The closure must supply the ancestry fact without objects (D-1, D-3), or the objects without the lease (rejected, D-2). |
| "its progress read has no objects to attribute against". | Confirmed. The claim rules (`:200-216`), `PresentBaselineTips` (`:1042-1047`) and `ClassifyNoveltyAsync` (`:1013-1030`) all run `rev-parse`/`merge-base` in `baseline.CanonicalRepository`, the desktop repository. | Without objects only string facts are decidable: S ≠ b, claim == S, every present baseline tip == b. The attested arm decides exactly those and nothing else (D-4). |
| Closure option 1: "a lease-free observation fetch (CARD-0743 territory)". | The CARD-0499 plan rules it out: "Fetch/pin mutations acquire the common-directory lease and child journal; contention is unavailable evidence, never an unlocked fallback" (`docs/superpowers/plans/2026-09-12-card-0499-worktree-progress-attribution-plan.md:174-175`), guarded by G-24/PC-24 (`:553`, `:601`) and `TaskProgressGitTests.C499_R16_LeaseContentionIsUnavailableWithoutAnUnlockedFetch` (`tests/Antiphon.Tests/Application/TaskProgressGitTests.cs:86-112`, asserts no `fetch` and no `update-ref`). CARD-0657 D-3 repeats the rule (`docs/superpowers/plans/2026-09-24-card-0657-runner-settlement-sync-plan.md:98-113`); CARD-1082 lists it out of scope. | Rejected (D-2). Reopening the lease discipline is a different card with a different owner. |
| Closure option 2: "a runner-attested ancestry answer on the publish wire". | The wire already carries it. `PhoneHomeWorkspacePublishResponse(Tip, Relation, DescendsFromBaseline, Dirty, Pushed, Refusal)` (`src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs:209-210`). The runner computes `DescendsFromBaseline` as `merge-base --is-ancestor BaselineSha tip` in its own mirror (`src/Antiphon.SessionRunner/RunnerWorkspaceService.cs:319-320`), `Relation` as tip vs `RemoteSha ?? BaselineSha` with `"equal"` when they match (`:321-332`), and returns without pushing when `request.Publish` is false (`:334-335`). The server always sends `Publish = true` (`server/Application/Services/PhoneHomeRunnerMirrorPublisher.cs:21`), calls the publisher only when the observation is `Missing` or `Present` (`RemoteWorkspaceService.cs:331-334`), and drops `DescendsFromBaseline`: `WithMirror` copies Tip, Relation, Dirty, Pushed and Refusal only (`:394-400`) and `RemoteSyncEvidence` has no field for it (`server/Application/Dtos/TaskProgressDtos.cs:79-96`). | No contract or runner change. One server-side inspect call on the lease-busy path, two additive fields on the result and the evidence (D-3). The feature flag is the existing `workspacePublishV1` (`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:147`). |
| CARD-1082 D-3 rejected "taking the runner's `WorkspacePublish` relation as ancestry proof (CARD-1065 D-2 rejects runner/report claims as proof; `Relation` is T vs S, not b)". | CARD-1065 D-2 rejects "report claims as publication proof, `MirrorPushed=true` as clean proof, local tracking refs as remote proof" (`docs/superpowers/plans/2026-10-05-card-1065-blocked-task-parking-plan.md:133-170`): it is about **publication**, which stays server-observed here (`ls-remote` on the exact owned ref). The CARD-1082 note is about `Relation`, which is indeed T vs S; it does not mention `DescendsFromBaseline`, which is T vs b. The server already acts on the runner's negative answers (`Relation` diverged/behind refuses `runner_mirror_diverged`, `RemoteWorkspaceService.cs:342-346`). | D-1 admits only the ancestry fact, only when T == S, records provenance in the evidence, and names the sweep as the verifier (D-5). This is the one decision that reverses a recorded note; it is the owner's to confirm (next: decide). |
| "the desktop repository lease held by a long land". | `WhileLeaseBusyAsync` waits in 10-second slices up to `RunnerSyncBudgetSeconds` (default 120, `server/Application/Settings/DelegationSettings.cs:639`) measured from the first busy sighting (`RemoteWorkspaceService.cs:554-593`); `LeaseWaiting` leaves the task open for the next sweep (`AgentTaskReplyService.cs:1946-1954`). CARD-0743 owns lease hold time. | The budget, the slice and `LeaseWaiting` are untouched. The runner is asked once, only after the budget is spent (D-3). |
| What the report claims. | The progress line is `[antiphon-progress:<id> commit=<full sha>]` (regex `TaskCompletionProgressService.cs:14`, parsed by `ParseClaim` `:283`); the runner-branch brief tells the delegate to write the SHA `git ls-remote` printed for its branch (`server/Application/Services/DelegationReportFormatter.cs:225`). | In the ordinary case the claim **is** S. The attested arm decides `claim absent or claim == S` only; any other claim stays Indeterminate and Blocked (D-4). |
| Which baseline tips novelty is checked against. | `PresentBaselineTips` yields `LocalSha` (b) and `Remote.Sha` only when `Remote.State == Present` (`:1042-1047`). The primary remote baseline is the task's own branch observed at capture (`server/Application/Services/AgentTaskDispatcher.cs:6714`, `:6748-6753`), which the preparer pushed at b; CARD-1115 records that a lease-busy capture stores `Remote.State = Unavailable`, which `PresentBaselineTips` excludes. | Ordinarily every present tip equals b. The attested arm requires that (string compare) and is Indeterminate otherwise (D-4). |
| "Pending is never Confirmed; never rewrite settlement evidence; never mint approval". | `Confirmed` is Synchronized or NoPushedProgress with a desktop SHA (`server/Application/Dtos/RemoteSettlementSyncDtos.cs:63-64`). The S4b sweep writes only the debt row and ends Ready only on a confirmed sync at the recorded source, Held for Refused (`SettlementSyncRecoveryService.cs:97-150` on `cfa234b05`). `LandApproval` and `AgentTaskLandingProtocol` read neither `RemoteSync` nor the evidence JSON (CARD-1082 ground truth, re-checked by grep at `3687e07bc`). | Untouched. V-7, V-14 and R-6 re-assert them with attestation present. |
| "no new per-tick statement cost on the CPU-constrained desktop". | The change lives inside settlement (one runner call bounded by the `WorkspacePublish` operation timeout of 90 s, `server/Infrastructure/Agents/SessionRunner/PhoneHomeLiveConnection.cs:315`) and inside the S4b sweep's existing Git. | No new query, table, sweep or event type. |
| "CARD-1065 parking invariants untouched". | Parking owns Blocked tasks only. Every test that enables the mirror publisher (`tests/Antiphon.Tests/Application/RunnerTaskSettlementTests.cs:27`, `:46`, `:307`; `ReviewEvidenceResettlementTests.cs:41` with `syncDebt: false`; `tests/Antiphon.Tests/TestHelpers/ReviewRecoveryWorld.cs:33`, Review role) holds no lease or is not a Code task. The CARD-1082 R-2 trio (`RunnerTaskSettlementTests.cs:499`, `:583`, `:629`) runs without a publisher. | No legacy verdict changes. The R-2 trio becomes this card's "no runner answer" control (R-1). |
| "Measure how often it still happens after CARD-1082 is activated and then plan the closure". | Not measurable today: S5 is unlanded, and the desktop database and server log are unreachable from the runner mirror. | The plan is written now; the Code dispatch waits for the measurement (D-9, exact query in "Activation and rollout"). |
| CARD-1113, CARD-1115, CARD-1119 adjacency. | 1113 item 1 pins `FullRef` in `Eligible`; item 2 and 1119 item 2 pass the evaluated evidence and its reason through `BlockReason`; 1115 gates baseline pins on `Present`. | Compatible: the attested admission is a separate predicate beside `Eligible`; the attested arm yields ProgressObserved or `runner_sync_lease_busy`, never a new Indeterminate reason; the baseline rule here consumes `PresentBaselineTips`, which already excludes an Unavailable remote baseline. None of those cards is a prerequisite. |

Platform read on 2026-10-06: `GET /api/runner-defaults` has `globalRunnerId = server2`;
`GET /api/session-runners` lists desktop (windows, capacity 2, occupied 0), server2 (linux, draining,
not accepting work) and server2-temp (linux, capacity 10, occupied 5, `workspacePublishV1`). Every
slice and checkpoint runs on the Linux lane with real Git and the isolated PostgreSQL fixture; no
`-Runner` pin and no `-Platform` is needed.

## Decisions

### D-1. Admit the runner's ancestry attestation for a Pending Code result whose objects are not local, only when the mirror tip equals the server-observed tip

`SettlementSyncDebtPolicy.Classify` gains one more output beside `Pending`: `AncestryAttested`
(`bool?`, additive on `RemoteSettlementSyncResult`). It is `true` when **all** of these hold on the
result it just classified Pending:

- `SourceDescends is null` (the server could not read ancestry itself; a server-read result keeps
  CARD-1082 D-4 and needs no attestation),
- `GitObjectId.IsFull(RemoteSha)` and `MirrorSha == RemoteSha` and `MirrorRelation == "equal"` (the
  runner's mirror tip is the very object the server observed on origin),
- `MirrorDescendsFromBaseline == true` (the runner read `merge-base --is-ancestor b T` in its mirror),
- `MirrorInspection is null` and `MirrorPushed == false` (a clean inspection, not a publish),
- `DelegationSettings.RunnerSyncDebtAttestedAncestry` is true (new bool, **default true**, no
  validator; documented beside `RunnerSyncDebtOnSettlement`).

It is `false` when the runner answered with a tip and any condition failed, and `null` when there
was no mirror answer or the result is not Pending. `Pending` itself is minted exactly as CARD-1082
D-1/D-2 say; this card never mints Pending.

*Trust model.* Three facts combine: the server itself observed S on `refs/heads/feat/card-task-<id8>`;
the runner states its mirror tip is S and the server compares it; the runner states b ⊑ S. The
runner is the component that pushed S and already decides refusals the server acts on
(`runner_mirror_diverged`). A false positive attestation can produce only a wrong Succeeded verdict
for a Code task whose branch really is at S: no desktop mutation follows (Pending is never Confirmed,
`MergeBackAsync` leaves the branch for review, CARD-1082 G-10), no approval follows (land reads
neither field, G-12), Review and `-Land` use the pushed branch at S, and the settlement sync sweep
re-reads the ancestry server-side under the lease and holds the debt on contradiction (D-5). The
evidence JSON records what was admitted (`ancestryAttested = true`) beside the runner's raw answer.

*Why default on:* the card's ask is the closure; CARD-1082 D-1 chose on-by-default for the same
reason and CARD-1077 recorded the cost of a dormant path. The switch is nested inside CARD-1082's:
with `RunnerSyncDebtOnSettlement = false` nothing here is reachable.

Rejected: trusting `Relation == "descends"` (T ahead of S: the server observed S, not T, and the
branch would move under the evidence); admitting the response of a publish the settlement itself
made (`descends`, `MirrorPushed = true`, then a lease-busy re-observation): that mirror held the
delegate's unpushed work, the report's claim cannot name its tip, and the shape stays Blocked and
is counted by the measurement; trusting `MirrorPushed` or `Dirty` for anything (CARD-1065
D-2); attesting for non-Code roles (no verdict depends on it; CARD-1082 D-4 already settles them);
a new capability flag (the field exists on `workspacePublishV1`); recording the attestation as
`SourceDescends = true` (that field means server-read and gates the local-object arm).

### D-2. Rejected closures

- **Lease-free observation fetch.** Reopens CARD-0499 G-24 and CARD-0657 D-3 (every desktop
  repository mutation is leased and journaled so `scripts/recover-repository-children.ps1` can
  reason about live children), and `C499_R16` pins it. Contention belongs to CARD-0743.
- **A longer or role-specific budget.** Holds the model turn and the seat for the whole wait;
  rejected by CARD-1043 D-1 and CARD-1065 D-6.
- **Blocked now, promoted later by the sweep.** CARD-1082 D-5 says the sweep "never promotes a
  verdict"; a Blocked task owes a decision; parking is dormant (CARD-1083) and a Reply still costs a
  turn.
- **A new runner operation returning claim containment and the b..S commit list.** Needs contracts,
  a runner rollout and a capability flag. Deferred: the measurement (D-9) also counts how often the
  claim is not S; if that is material, file it as the follow-up to this card.
- **A server-owned observation object store with alternates.** New repository root, Windows path
  custody, retention; the same trust gain is available with no new storage.

### D-3. The server asks the runner once, inspect-only, after a spent lease-busy observation of a Code task

In `RemoteWorkspaceService.SyncOwnedCheckoutAsync`, the `"repository_lease_busy"` arm (`:416-417`)
calls `_mirrorPublisher.InspectAsync(task, b, observed.Sha, ct)` when **all** hold: `observeSpent`
(the budget is spent; `LeaseWaiting` leaves the task open and the next sweep retries), `reportedTips
is null` (ordinary settlement; bind-refusal recovery and both sweeps pass tips and never publish or
inspect), `task.Role == AgentTaskRole.Code`, `GitObjectId.IsFull(observed.Sha)` and `observed.Sha != b`,
`mirror is null` (no publish response yet), and `RunnerSyncDebtAttestedAncestry` is on (the service
already reads `DelegationSettings`, `:48`, `:58-60`). A null answer or an exception is
`MirrorInspection = runner_mirror_unavailable`, exactly like a failed publish (`:347-354`); no
attestation. When the after-publish re-observation is the lease-busy one (`:369-382`), no inspect
call is made (`mirror` is set): that publish response describes a mirror the settlement itself had
to push (`Relation = "descends"`, `MirrorPushed = true`), which D-1 records but never admits; that
shape stays Blocked as today. `WithMirror` (`:394-400`) also copies `DescendsFromBaseline` into the new
`RemoteSettlementSyncResult.MirrorDescendsFromBaseline`; `RemoteSyncEvidence` gains
`MirrorDescendsFromBaseline` and `AncestryAttested`, and `From` copies both. All fields are trailing
optional parameters (positional constructors in tests stay valid; older JSON round-trips).

`IRunnerMirrorPublisher` gains `InspectAsync(task, baselineSha, remoteSha, ct)`;
`PhoneHomeRunnerMirrorPublisher` sends the existing request with `Publish = false`, same capability
check, same 90-second operation timeout. The three test fakes implement it (`LocalMirrorPublisher`
without the push; `SeatMirrorPublisher` and `BlockingMirrorPublisher` return null).

*Why once and only when spent:* each 10-second slice already re-observes; attesting before the budget
is spent would be discarded when the lease frees. *Why Code only:* the only verdict that depends on
ancestry; `IsEligible` already reads the role (`:148-152`). *Why not publish:* a mirror ahead of S
would move the branch under the evidence; the ordinary salvage path (`:331-334`) is unchanged.

Rejected: inspecting on every lease-busy observation (wasted runner calls); inspecting for Reviews
and Plans to enrich the header (CARD-1082 D-7 keeps today's absence; separate card if wanted).

### D-4. Progress: an admitted attestation attributes S as Primary with string-only rules

`TaskCompletionProgressService.EvaluateRemoteAsync`, Pending arm, after the local-object branch
(`:190-228`) did not apply: when `prepared.AncestryAttested == true`, `RemoteSha` is full and `!= b`,
`claim.Sha is null || claim.Sha == RemoteSha`, and every `PresentBaselineTips(baseline)` entry equals
b, return `ProgressObserved` with the Primary source `ClaimedSha = claim.Sha`, `VerifiedSha = S`,
`LocalObserved = DesktopBeforeSha ?? b`, `RemoteObserved = S`, `Reason =
RemoteSettlementSyncReasons.AttestedAncestry` (`"runner_attested_ancestry"`, new constant appended
after S4b's three). Otherwise the arm falls through to today's `Unknown(runner_sync_lease_busy)`.
`ProgressWarning` (`:345-378`) emits `progress=runner-attested; commit=<S>` for that source so the
caller's note carries the provenance line the same way `progress=primary-remote` does.
`AllowsAutomaticWorkspaceMutation` (`:392-399`) stays true for a Primary source, and `MergeBackAsync`
still returns "left for review" because Pending is not Confirmed: no desktop mutation.

*Why claim == S only:* without objects, C ⊑ S and novelty of C are decidable only when C is S
(b ⊑ S and S ≠ b imply S ⋢ b). *Why every present tip == b:* novelty against a moved remote baseline
needs objects. *Why Indeterminate, not NoAttributedProgress, for a foreign claim:* a Failed verdict
is an accusation; string facts do not support it. *Why a source `Reason` on a positive arm:* it is
additive, visible in `progressEvidence.sources[].reason`, and no reader treats a ProgressObserved
Primary source's reason as a defect.

### D-5. The settlement sync sweep is the verifier; a contradicted attestation is Held

No sweep change. S4b's `SyncSettledAsync` (`RemoteWorkspaceService.cs:191-207`) runs
`SyncCoreAsync` with `reportedTips = [S]`, which fetches S under a free lease, reads ancestry before
the lease (`:433-454`) and again under it (`:488-500`), and refuses `runner_sync_diverged` or
`runner_sync_rewound`; `SaveAsync` holds a Refused result with that reason (`SettlementSyncRecoveryService.cs:114-117`
on `cfa234b05`), and CARD-1082 S6's attention projection warns on Held. The settlement evidence is
never rewritten: `ancestryAttested = true` stays as the record of what was admitted, the debt row's
`ReasonCode` says what the server later read. V-14 proves this with a fake runner that lies.

Rejected: a new `settlement_sync_attestation_contradicted` reason (the Git reason names the cause,
and the evidence shows `ancestryAttested` beside it); retrying the attestation (the server has the
objects by then and needs no runner answer).

### D-6. Caller text, projections, CLI

- `SettlementSyncDebtPolicy.PendingWarning` appends, when `AncestryAttested == true`: `Ancestry
  <b>..<S> is attested by runner <RunnerId> from its mirror (tip equal to origin); the settlement
  sync sweep re-reads it under the lease.` The sentence never contains "then reply".
  `WorkspaceNote` is unchanged.
- `GET /api/agent-tasks/{id}` exposes `progressEvidence.remoteSync.mirrorDescendsFromBaseline` and
  `.ancestryAttested` with no DTO work: `ProgressEvidenceDto` embeds `RemoteSyncEvidence`
  (`TaskProgressDtos.cs:116-120`).
- `scripts/delegate.ps1` status rendering (`:743`, which CARD-1082 S6 extends) appends
  `ancestry=runner-attested` when `ancestryAttested` is true.
- Docs: three sentences, listed in S5, pinned by `RunnerBranchContractDocumentationTests`.

### D-7. Kill switch semantics

With `RunnerSyncDebtAttestedAncestry = false` every path is byte-for-byte CARD-1082's: the service
does not inspect (D-3), `Classify` leaves `AncestryAttested` null (D-1), the progress arm falls
through (D-4), the Code task blocks `runner_sync_lease_busy` with "then reply". Both gates read the
one setting; V-2 and V-13 pin each.

### D-8. Legacy tests

The CARD-1082 R-2 trio keeps asserting Blocked: without a publisher there is no runner answer, which
is this card's R-1 control. No `syncDebt:` or other opt-out is added to any legacy test. Any other
legacy verdict change is a plan finding to report, not something to adjust.

### D-9. Dispatch gate: measure after CARD-1082 S5 activates, then run this plan

The card asks for the measurement first. The plan exists now so the measurement is a go/no-go, not
a design input. Dispatch S1 only after the AppHost restart that activates CARD-1082 S5 and one
measurement window (seven days, matching CARD-1082's activation note), using the query in
"Activation and rollout". Go when the residual appears at least daily or any seat-hours were lost to
it; otherwise leave CARD-1109 in Backlog with this plan attached. The measurement also reports how
often the claim is not the observed tip (D-2's deferred option).

### D-10. Keep fleet placement dynamic

No `-Runner` or `-Platform` in any brief for this card; Code and Review run where
`GET /api/runner-defaults` sends them (server2-temp today). Every checkpoint row is Linux-capable.

## Sequencing against CARD-1082

| This plan | Must follow | Why |
|---|---|---|
| S1 | CARD-1082 S4b landed | Both append to `RemoteSettlementSyncReasons` in `server/Application/Dtos/RemoteSettlementSyncDtos.cs` (S4b adds three constants at `:109-117` on its branch); S1 appends after them. `tests/Antiphon.Tests/TestHelpers/RunnerSettlementWorld.cs` gains S4b's sweep helper that S4 here calls. |
| S2, S3 | S1 | They read the fields S1 adds. S2 and S3 touch disjoint files and may run in parallel. Neither depends on CARD-1082 S5: like CARD-1082 S3, their arms are unreachable until `Classify` is wired. |
| S4 | CARD-1082 S5 landed | Needs `Classify` wired into settlement and the debt insert; adds to `RunnerTaskSettlementTests`, S5's test file (26 → 31 results after S5; 34 after S4 here). |
| S5 | CARD-1082 S6 landed | Adds sentences after S6's Pending paragraphs in `docs/orchestration-loop.md` and `docs/session-runtime-invariants.md`, extends S6's `delegate.ps1` line, and adds to `RunnerBranchContractDocumentationTests` (4 → 5 after S6; 6 after S5 here). |

No slice here runs concurrently with a CARD-1082 S5, S6 or S7 Code task (same source areas:
`AgentTaskReplyService`, settlement tests, docs). S1 to S3 touch no file CARD-1082 S5 to S7 name.

## Implementation slices

Each slice is one Code task of 35 to 60 minutes. Commit and push each slice before its checkpoint
group; never rebase or force-push the task branch. Besides its own rows, each slice runs the existing
classes named in its row.

| Slice | Budget | Files | Deliverable and named tests |
|---|---|---|---|
| S1: inspect on a spent lease-busy observation; result and evidence fields | 60 min | `server/Application/Dtos/RemoteSettlementSyncDtos.cs` (`MirrorDescendsFromBaseline`, `AncestryAttested` on the result; `AttestedAncestry` reason); `server/Application/Dtos/TaskProgressDtos.cs` (`RemoteSyncEvidence` two fields, `From`); `server/Application/Interfaces/IRunnerMirrorPublisher.cs` (`InspectAsync`); `server/Application/Services/PhoneHomeRunnerMirrorPublisher.cs` (`Publish = false`); `server/Application/Services/RemoteWorkspaceService.cs` (`SyncOwnedCheckoutAsync` lease-busy arm, `WithMirror`); `tests/Antiphon.Tests/Application/RunnerSettlementSyncTests.cs` (`LocalMirrorPublisher.InspectAsync` + `Inspections` counter, `SyncWorld.Service` gains `DelegationSettings? settings`); `tests/Antiphon.Tests/Application/BlockedTaskParkReleaseFixture.cs` (`SeatMirrorPublisher.InspectAsync` returns null); new `tests/Antiphon.Tests/Application/RunnerWorkspaceInspectTests.cs` (real `RunnerWorkspaceService`, model `TaskParkPublicationTests.cs:337`) | `RunnerSettlementSyncTests`: `C1109_SpentObservationLeaseBusyCarriesRunnerAttestation`, `C1109_InspectionIsSkippedForNonCodeWaitingRecoveryAndSwitchOff` (4 arguments: `review`, `waiting`, `recovery`, `switch-off`), `C1109_AheadMirrorIsNotAttestedAndNeverPushed`; `RunnerWorkspaceInspectTests.C1109_InspectRequestNeverPushesAndReportsBaselineAncestry`. Whole `RunnerSettlementSyncTests` and `C499_R16` stay green. Production effect before CARD-1082 S5: one inspect call on an already-spent Code path and two evidence fields; the verdict is unchanged. |
| S2: policy admission, setting, warning sentence | 40 min | `server/Application/Settings/DelegationSettings.cs` (`RunnerSyncDebtAttestedAncestry`, default true); `server/Application/Services/SettlementSyncDebtPolicy.cs` (`Attestable`, `Classify` sets `AncestryAttested`, `PendingWarning` sentence); new `tests/Antiphon.Tests/Application/SettlementSyncAttestationPolicyTests.cs` (not `SettlementSyncDebtPolicyTests`, which CARD-1082 S6 extends); `tests/Antiphon.Tests/Application/DelegationLeaseSettingsTests.cs` | `SettlementSyncAttestationPolicyTests`: `C1109_ClassifyAdmitsAttestationWhenMirrorTipEqualsObservedTip`, `C1109_ClassifyRefusesAttestation` (7 arguments: `mirror-tip-differs`, `relation-descends`, `descends-false`, `descends-null`, `server-read-ancestry`, `mirror-refusal`, `switch-off`), `C1109_EvidenceRecordsAttestationAndStaysUnconfirmed`, `C1109_PendingWarningNamesTheAttestingRunner`; `DelegationLeaseSettingsTests.C1109_AttestedAncestryDefaultsOn`. Whole `SettlementSyncDebtPolicyTests` stays green. |
| S3: attested progress arm and provenance line | 45 min | `server/Application/Services/TaskCompletionProgressService.cs` (`EvaluateRemoteAsync` Pending arm, `ProgressWarning`); `tests/Antiphon.Tests/Application/RunnerCompletionProgressTests.cs` (`Pending(...)` helper gains `attested: bool?`) | `RunnerCompletionProgressTests`: `C1109_AttestedPendingWithoutLocalObjectsAttributesTheObservedTip` (no claim and claim == S in one method), `C1109_AttestedPendingStaysIndeterminate` (3 arguments: `foreign-claim`, `moved-remote-baseline`, `not-admitted`). Whole class stays green. |
| S4: settlement integration and the lying-runner control (after CARD-1082 S5) | 60 min | `tests/Antiphon.Tests/Application/RunnerTaskSettlementTests.cs`; `tests/Antiphon.Tests/TestHelpers/RunnerSettlementWorld.cs` (`CreateAsync` gains `IRunnerMirrorPublisher? publisher` and `bool attestedAncestry = true`); test-local `LyingMirrorPublisher`. Production files only if a defect appears, repaired in its owning slice's files and reported. | `RunnerTaskSettlementTests`: `C1109_CodeLeaseBusyWithUnfetchedObjectsSettlesSucceededOnRunnerAttestation` (settlement, then `SweepSettlementSyncAsync` → Ready), `C1109_AttestedAncestryDisabledKeepsUnfetchedCodeBlocked`, `C1109_FalseAttestationIsHeldByTheSettlementSyncSweep`. Whole `RunnerTaskSettlementTests`, `ReviewEvidenceResettlementTests`, `BlockedTaskSyncRecoveryTests`, `BlockedTaskParkDeliveryTests`, `SettlementSyncRecoveryTests`, `TerminalRunnerSeatReleaseTests` stay green (CP-6, CP-7, CP-8). |
| S5: docs, CLI line, doc pin (after CARD-1082 S6) | 35 min | `docs/orchestration-loop.md`, `docs/session-runtime-invariants.md`, `docs/ops-http.md`; `scripts/delegate.ps1` (`:743` line); `tests/Antiphon.Tests/Application/RunnerBranchContractDocumentationTests.cs` | `RunnerBranchContractDocumentationTests.C1109_runner_attested_ancestry_is_documented`. Sentences: (1) `docs/orchestration-loop.md`, Runner sync outcomes, after the CARD-1082 `runner_sync_lease_busy → Pending` paragraph: "A Code task whose pushed commits the desktop could not fetch inside the budget settles Pending only on the runner's attested ancestry (`ancestryAttested` in `progressEvidence.remoteSync`): the runner's mirror tip must equal origin's tip and descend from the dispatch base, and the report's claim must be that tip. The settlement sync sweep re-reads the ancestry under the lease and holds the debt `runner_sync_diverged` or `runner_sync_rewound` if the attestation was false; without a runner answer the task still blocks `runner_sync_lease_busy` (CARD-1109)." (2) `docs/session-runtime-invariants.md`, after the CARD-1082 paragraph (else after the CARD-0672 I-A bullet at `:459-462`): "**Runner-attested ancestry is a verdict input, never a sync confirmation (CARD-1109).** A runner's `DescendsFromBaseline` answer may settle a Code task Succeeded with Pending sync when its mirror tip equals the server-observed origin tip; it never confirms the desktop, never moves it, and the leased sweep re-reads it." (3) `docs/ops-http.md:186-191`: extend the field list with `mirrorDescendsFromBaseline` and `ancestryAttested`, and add "For a Code task whose observation was lease-busy for the whole budget, settlement sends the same request with `Publish = false` to inspect the mirror without pushing." |

After S5 the Code task for the last slice (or Review) runs CP-10, CP-11 and CP-12 at the final
committed SHA so the whole closed list has one certificate.

## Verification design

### Inspection

| Read | Why |
|---|---|
| `RemoteWorkspaceService.cs:140-215` (`IsEligible`, `SyncAsync`, `SyncParkedAsync`, `SyncSettledAsync`), `:309-470` (`SyncOwnedCheckoutAsync` through the acquire outcome), `:554-597` (`WhileLeaseBusyAsync`, `LeaseReason`) | The lease-busy arms the inspect joins, the publish guard it must not widen, `WithMirror`. |
| `TaskProgressGit.cs:59-180` | Where the advertised tip comes from and why objects are absent. |
| `TaskCompletionProgressService.cs:14`, `:84-92`, `:154-235` (Pending arm), `:283-320` (`ParseClaim`), `:345-399` (`ProgressWarning`, `AllowsAutomaticWorkspaceMutation`), `:1013-1055` | The arm to extend, the claim shape, the baseline tips. |
| `SettlementSyncDebtPolicy.cs` (entire), `DelegationSettings.cs:636-654`, `:1210-1217` | The rule and the setting to add beside. |
| `PhoneHomeContracts.cs:206-210`, `RunnerWorkspaceService.cs:277-349`, `PhoneHomeRunnerMirrorPublisher.cs`, `IRunnerMirrorPublisher.cs` | The wire, what the runner computes, the `Publish = false` path. |
| `SettlementSyncRecoveryService.cs` (`cfa234b05`), `RunnerSettlementWorld.cs` (same branch, `SweepSettlementSyncAsync`) | The verifier and the test helper S4 calls. |
| Tests: `RunnerSettlementSyncTests.cs:144-230` (fakes), `:535-620` (C1082 lease-busy tests), `:916-940` (`LeaseBusySignal`), `:1135-1392` (`SyncWorld`); `RunnerCompletionProgressTests.cs:202-320`, `:332-352`; `SettlementSyncDebtPolicyTests.cs:1-60`, `:128-155`; `RunnerTaskSettlementTests.cs:95-125`, `:499-690`; `TaskParkPublicationTests.cs:325-345`; `TaskProgressGitTests.cs:86-112` | Fixtures to extend and the models for each new test. |

### Proves it works now

| V | Behaviour | Test (class-qualified) |
|---|---|---|
| V-1 | Code task, S pushed, objects not local, lease held for the budget: the result is `Unavailable runner_sync_lease_busy` with `RemoteSha = S`, `SourceDescends = null`, `MirrorSha = S`, `MirrorRelation = "equal"`, `MirrorDescendsFromBaseline = true`, `MirrorPushed = false`; the fake's `Inspections == 1`, `Calls == 0`; the desktop trace has no `fetch`, `merge-base` or `update-ref`; S is still not local; HEAD is the baseline. | `RunnerSettlementSyncTests.C1109_SpentObservationLeaseBusyCarriesRunnerAttestation` |
| V-2 | No inspection for a Review task, for a wait that is not yet spent (`runner_sync_lease_waiting`), for bind-refusal recovery (`reportedTips`), or with the switch off; `MirrorDescendsFromBaseline` is null in each. | `RunnerSettlementSyncTests.C1109_InspectionIsSkippedForNonCodeWaitingRecoveryAndSwitchOff` (4 results) |
| V-3 | Mirror ahead of S (one unpushed commit): the inspection reports `Relation = "descends"`, `Pushed = false`, `MirrorSha != S`; origin's tip is still S after the sync. | `RunnerSettlementSyncTests.C1109_AheadMirrorIsNotAttestedAndNeverPushed` |
| V-4 | The runner's `PublishAsync` with `Publish = false` on a mirror ahead of origin returns `descends`, `DescendsFromBaseline = true`, `Pushed = false`, and origin's tip is unchanged; with the mirror at origin's tip it returns `equal`. | `RunnerWorkspaceInspectTests.C1109_InspectRequestNeverPushesAndReportsBaselineAncestry` |
| V-5 | `Classify` sets `AncestryAttested = true` on a lease-busy result with `MirrorSha == RemoteSha`, `equal`, `DescendsFromBaseline = true`, no inspection refusal, `SourceDescends = null`; the state is Pending. | `SettlementSyncAttestationPolicyTests.C1109_ClassifyAdmitsAttestationWhenMirrorTipEqualsObservedTip` |
| V-6 | Each failed condition yields `AncestryAttested` false (tip differs, relation descends, descends false, mirror refusal) or null (descends null, server-read ancestry, switch off), while the state is still Pending where CARD-1082 says so. | `SettlementSyncAttestationPolicyTests.C1109_ClassifyRefusesAttestation` (7 results) |
| V-7 | `RemoteSyncEvidence.From` records both fields; `Confirmed` is false and `ConfirmedSha` null with attestation present; the JSON round-trips and older JSON without the fields reads back null. | `SettlementSyncAttestationPolicyTests.C1109_EvidenceRecordsAttestationAndStaysUnconfirmed` |
| V-8 | The pending warning names the runner, b, S, "attested", "re-reads it under the lease"; still "synced later" and "No reply is needed"; never "then reply". | `SettlementSyncAttestationPolicyTests.C1109_PendingWarningNamesTheAttestingRunner` |
| V-9 | `RunnerSyncDebtAttestedAncestry` defaults true and validates with no failure either way. | `DelegationLeaseSettingsTests.C1109_AttestedAncestryDefaultsOn` |
| V-10 | Pending with `AncestryAttested = true`, objects not local: no claim, and claim == S, both give ProgressObserved with Primary `VerifiedSha = S`, `Reason = runner_attested_ancestry`, `LocalObserved = b`; `ProgressWarning` is `progress=runner-attested; commit=<S>`; no sync command ran; S still not local; HEAD baseline. | `RunnerCompletionProgressTests.C1109_AttestedPendingWithoutLocalObjectsAttributesTheObservedTip` |
| V-11 | Attested but claim ≠ S; attested but a Present remote baseline tip ≠ b; `MirrorDescendsFromBaseline = true` with `AncestryAttested` null: all Indeterminate `runner_sync_lease_busy`. | `RunnerCompletionProgressTests.C1109_AttestedPendingStaysIndeterminate` (3 results) |
| V-12 | Code, S pushed from the runner clone, objects never fetched, lease held for the budget, `mirrorPublish: true`: Succeeded; `NextStage` is the report's; `RemoteSync.State = Pending`, `ObservedSha = S`, `ConfirmedSha = null`, `AncestryAttested = true`; Primary `VerifiedSha = S` with the attested reason; the Warning contains "attested" and "synced later"; the header has `source <S> (desktop-sync=pending)`; the obligation has no `next=decide`; no `merge` ran; HEAD is the baseline; one debt row Pending with `SourceSha = S`. After the lease is released, `SweepSettlementSyncAsync` ends the debt Ready with `ConfirmedSha = S`, HEAD is S, the task is still Succeeded and the evidence JSON bytes are unchanged. | `RunnerTaskSettlementTests.C1109_CodeLeaseBusyWithUnfetchedObjectsSettlesSucceededOnRunnerAttestation` |
| V-13 | Same world with `attestedAncestry: false`: Blocked, `next=decide`, the handoff starts with "Runner sync blocked", `RemoteSync.Reason = runner_sync_lease_busy`, `AncestryAttested` null, the fake's `Inspections == 0`. | `RunnerTaskSettlementTests.C1109_AttestedAncestryDisabledKeepsUnfetchedCodeBlocked` |
| V-14 | A lying publisher answers `(S, "equal", true, false, false, null)` for a branch force-pushed to an unrelated root: settlement is Succeeded Pending with `AncestryAttested = true`; the sweep (lease free) ends the debt **Held** `runner_sync_diverged`, HEAD is the baseline, S is not merged, the task status and evidence JSON are unchanged, and one attention item names the reason (when CARD-1082 S6 has landed; else assert the Held row only). | `RunnerTaskSettlementTests.C1109_FalseAttestationIsHeldByTheSettlementSyncSweep` |
| V-15 | The owner docs contain `ancestryAttested`, `mirrorDescendsFromBaseline`, "Runner-attested ancestry is a verdict input", `CARD-1109`. | `RunnerBranchContractDocumentationTests.C1109_runner_attested_ancestry_is_documented` |

### Guards the regression

| R | Negative control | Test |
|---|---|---|
| R-1 | No runner answer (no publisher): Code with unfetched objects under a held lease stays Blocked/Decide with the obligation. | existing `RunnerTaskSettlementTests.Runner_sync_block_commits_the_completion_obligation`, `Sync_uncertainty_blocks_and_reply_retries`, `Sustained_lease_contention_spans_sweeps_without_holding_up_other_settlements` |
| R-2 | CARD-1082's Pending rules (local objects, S == b, kill switch, Pending never Confirmed) are unchanged. | whole `RunnerCompletionProgressTests`, whole `SettlementSyncDebtPolicyTests` |
| R-3 | Service lease semantics, the publish guard, mirror refusals and the no-unlocked-fetch rule are unchanged. | whole `RunnerSettlementSyncTests` (includes `Diverged_runner_mirror_blocks_without_changing_desktop`, `Bind_refusal_recovery_never_publishes_the_mirror`, `Unavailable_mirror_feature_keeps_no_progress_verdict_with_evidence`); `TaskProgressGitTests.C499_R16_LeaseContentionIsUnavailableWithoutAnUnlockedFetch` |
| R-4 | CARD-1043 resettlement and CARD-1065 park paths are unchanged. | whole `ReviewEvidenceResettlementTests`, `BlockedTaskSyncRecoveryTests`, `BlockedTaskParkDeliveryTests` |
| R-5 | The seat release ledger is unchanged. | whole `TerminalRunnerSeatReleaseTests` |
| R-6 | The sweep's Ready/Held/Superseded rules and its evidence immutability are unchanged. | whole `SettlementSyncRecoveryTests` |

### Guard inventory

| G | Decision: guard | PC |
|---|---|---|
| G-1 | D-1: admission needs `MirrorSha == RemoteSha` and `Relation == "equal"` | PC-1 |
| G-2 | D-7: the policy honours the switch | PC-2 |
| G-3 | D-7: the service does not inspect with the switch off | PC-3 |
| G-4 | D-3: inspection never publishes | PC-4 |
| G-5 | D-3: inspection is Code-only | PC-5 |
| G-6 | D-4: the claim must be S | PC-6 |
| G-7 | D-4: every present baseline tip must be b | PC-7 |
| G-8 | D-4: the arm keys on `AncestryAttested`, not the raw mirror field | PC-8 |
| G-9 | D-4: provenance is recorded on the source | PC-9 |
| G-10 | D-5: a false attestation is Held by the sweep | PC-10 |
| G-11 | D-6: the warning names the attestation | PC-11 |
| G-12 | D-3: the runner's `Publish = false` never pushes | PC-12 |
| G-13 | D-1: Pending with attestation is never Confirmed | PC-13 |

### Positive controls

Mutation runs break/red/restore/green after land; Code runs ordinary V/R; Review judges guard
independence. Each row is one compiling production defect applied singly; use the exact method
filter, never the class. Zero executions, a build or fixture failure, or a timeout is not red.
Restore source and rebuild before the green run. Controls sharing a file run sequentially.

| PC | Break guard by compiling defect | Exact detecting filter | Expected red assertion |
|---|---|---|---|
| PC-1 | G-1: `Attestable` drops the `MirrorSha == RemoteSha` conjunct. | `/*/*/SettlementSyncAttestationPolicyTests/C1109_ClassifyRefusesAttestation` | `G-1`: the `mirror-tip-differs` arm has `AncestryAttested == true`, expected false. |
| PC-2 | G-2: `Classify` ignores `RunnerSyncDebtAttestedAncestry`. | `/*/*/SettlementSyncAttestationPolicyTests/C1109_ClassifyRefusesAttestation` | `G-2`: the `switch-off` arm has `AncestryAttested == true`, expected null. |
| PC-3 | G-3: `SyncOwnedCheckoutAsync` inspects regardless of the setting. | `/*/*/RunnerSettlementSyncTests/C1109_InspectionIsSkippedForNonCodeWaitingRecoveryAndSwitchOff` | `G-3`: the `switch-off` arm sees `Inspections == 1`, expected 0. |
| PC-4 | G-4: the lease-busy arm calls `PublishAsync` instead of `InspectAsync`. | `/*/*/RunnerSettlementSyncTests/C1109_AheadMirrorIsNotAttestedAndNeverPushed` | `G-4`: origin's tip advanced past S, or `Calls == 1`. |
| PC-5 | G-5: the role check is removed. | `/*/*/RunnerSettlementSyncTests/C1109_InspectionIsSkippedForNonCodeWaitingRecoveryAndSwitchOff` | `G-5`: the `review` arm sees `Inspections == 1`, expected 0. |
| PC-6 | G-6: the arm accepts any present claim. | `/*/*/RunnerCompletionProgressTests/C1109_AttestedPendingStaysIndeterminate` | `G-6`: the `foreign-claim` arm is ProgressObserved, expected Indeterminate. |
| PC-7 | G-7: the baseline-tips check is skipped. | `/*/*/RunnerCompletionProgressTests/C1109_AttestedPendingStaysIndeterminate` | `G-7`: the `moved-remote-baseline` arm is ProgressObserved. |
| PC-8 | G-8: the arm reads `MirrorDescendsFromBaseline == true` instead of `AncestryAttested == true`. | `/*/*/RunnerCompletionProgressTests/C1109_AttestedPendingStaysIndeterminate` | `G-8`: the `not-admitted` arm is ProgressObserved. |
| PC-9 | G-9: the source arm omits `Reason = runner_attested_ancestry`. | `/*/*/RunnerCompletionProgressTests/C1109_AttestedPendingWithoutLocalObjectsAttributesTheObservedTip` | `G-9`: the Primary source's reason is null; the warning line is absent. |
| PC-10 | G-10: `TaskProgressGit.IsAncestorAsync` returns true on exit code 1. | `/*/*/RunnerTaskSettlementTests/C1109_FalseAttestationIsHeldByTheSettlementSyncSweep` | `G-10`: the debt is Ready and HEAD moved, expected Held `runner_sync_diverged` with HEAD at the baseline. |
| PC-11 | G-11: `PendingWarning` omits the attestation sentence. | `/*/*/SettlementSyncAttestationPolicyTests/C1109_PendingWarningNamesTheAttestingRunner` | `G-11`: the text lacks "attested by runner". |
| PC-12 | G-12: the runner's `PublishAsync` ignores `request.Publish`. | `/*/*/RunnerWorkspaceInspectTests/C1109_InspectRequestNeverPushesAndReportsBaselineAncestry` | `G-12`: `Pushed == true` or origin's tip moved. |
| PC-13 | G-13: `Confirmed` includes Pending when `AncestryAttested == true`. | `/*/*/SettlementSyncAttestationPolicyTests/C1109_EvidenceRecordsAttestationAndStaysUnconfirmed` | `G-13`: `Confirmed` is true, `ConfirmedSha` not null. |

### Checkpoints

Exactly one isolated build and one filter per row. Counts are TUnit executed results: a method with
N `[Arguments]` rows contributes N. Rosters counted at `3687e07bc`: `RunnerSettlementSyncTests` 29,
`RunnerCompletionProgressTests` 11, `SettlementSyncDebtPolicyTests` 17 (7 methods),
`TaskProgressGitTests` 11 (8 methods), `RunnerTaskSettlementTests` 26 (22 methods),
`DelegationLeaseSettingsTests` 14 (5 methods), `RunnerBranchContractDocumentationTests` 4,
`ReviewEvidenceResettlementTests` 9, `BlockedTaskSyncRecoveryTests` 2, `BlockedTaskParkDeliveryTests`
4, `TerminalRunnerSeatReleaseTests` 39 (29 methods); `SettlementSyncRecoveryTests` 9 (6 methods) on
`cfa234b05`. CARD-1082 S5 adds 5 to `RunnerTaskSettlementTests`, S6 adds 4 to
`SettlementSyncDebtPolicyTests`, 1 to `SettlementSyncRecoveryTests` and 1 to
`RunnerBranchContractDocumentationTests`, S7 adds 1 to `ReviewEvidenceResettlementTests`; a row whose
count depends on one of those says so, and the Code report states which had landed at its base SHA.
Confirm the TRX roster equals the expected set, not merely at least `Min`.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1109-cp1/` | sync-service | `/*/*/(RunnerSettlementSyncTests*)\|(RunnerWorkspaceInspectTests*)/*` | V-1, V-2, V-3, V-4, R-3 | exact 36 results (29 existing + 6 C1109_* + 1 inspect), 0 failed/skipped | 36 | 11 | |
| CP-2 | S1 | CP-1 | observation-guard | `/*/*/TaskProgressGitTests/C499_R16_LeaseContentionIsUnavailableWithoutAnUnlockedFetch*` | R-3 | exact 1 result, 0 failed/skipped | 1 | 3 | |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1109-cp3/` | policy-settings | `/*/*/(SettlementSyncAttestationPolicyTests*)\|(DelegationLeaseSettingsTests*)/*` | V-5, V-6, V-7, V-8, V-9 | exact 25 results (10 new + 14 existing + 1 new), 0 failed/skipped | 25 | 6 | |
| CP-4 | S2 | CP-3 | policy-regression | `/*/*/SettlementSyncDebtPolicyTests/*` | R-2 | exact 17 results at the base (21 once CARD-1082 S6 has landed), 0 failed/skipped | 17 | 4 | |
| CP-5 | S3 | `tests/Antiphon.Tests -> bin-c1109-cp5/` | progress | `/*/*/RunnerCompletionProgressTests/*` | V-10, V-11, R-2 | exact 15 results (11 existing + 4 C1109_*), 0 failed/skipped | 15 | 7 | |
| CP-6 | S4 | `tests/Antiphon.Tests -> bin-c1109-cp6/` | settlement | `/*/*/RunnerTaskSettlementTests/*` | V-12, V-13, V-14, R-1 | exact 34 results (31 after CARD-1082 S5 + 3 C1109_*), 0 failed/skipped | 34 | 15 | true |
| CP-7 | S4 | CP-6 | legacy-evidence-park-sweep | `/*/*/(ReviewEvidenceResettlementTests*)\|(BlockedTaskSyncRecoveryTests*)\|(BlockedTaskParkDeliveryTests*)\|(SettlementSyncRecoveryTests*)/*` | R-4, R-6 | exact 24 results (9 + 2 + 4 + 9; 26 once CARD-1082 S6 and S7 have landed), 0 failed/skipped | 24 | 14 | true |
| CP-8 | S4 | CP-6 | seat-release | `/*/*/TerminalRunnerSeatReleaseTests/*` | R-5 | exact 39 results, 0 failed/skipped | 39 | 10 | true |
| CP-9 | S5 | `tests/Antiphon.Tests -> bin-c1109-cp9/` | docs | `/*/*/RunnerBranchContractDocumentationTests/*` | V-15 | exact 6 results (4 existing + 1 from CARD-1082 S6 + 1), 0 failed/skipped | 6 | 4 | |
| CP-10 | all | `tests/Antiphon.Tests -> bin-c1109-final/` | final-units | `/*/*/(RunnerSettlementSyncTests*)\|(RunnerWorkspaceInspectTests*)\|(RunnerCompletionProgressTests*)\|(SettlementSyncAttestationPolicyTests*)\|(SettlementSyncDebtPolicyTests*)\|(DelegationLeaseSettingsTests*)\|(TaskProgressGitTests*)\|(RunnerBranchContractDocumentationTests*)/*` | V-1 to V-11, V-15, R-2, R-3 | exact 114 results (35 + 1 + 15 + 10 + 21 + 15 + 11 + 6), 0 failed/skipped | 114 | 13 | |
| CP-11 | all | CP-10 | final-settlement | `/*/*/(RunnerTaskSettlementTests*)\|(SettlementSyncRecoveryTests*)\|(BlockedTaskSyncRecoveryTests*)/*` | V-12, V-13, V-14, R-1, R-6 | exact 46 results (34 + 10 + 2), 0 failed/skipped | 46 | 14 | true |
| CP-12 | all | CP-10 | final-legacy | `/*/*/(BlockedTaskParkDeliveryTests*)\|(ReviewEvidenceResettlementTests*)\|(TerminalRunnerSeatReleaseTests*)/*` | R-4, R-5 | exact 53 results (4 + 10 + 39), 0 failed/skipped | 53 | 12 | true |

Run each group once its slice is committed, through the checkpoint tool:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1109-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1109-driver/ --nologo
dotnet tools/Antiphon.Checkpoints/bin-c1109-driver/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-07-card-1109-unfetched-commits-settlement-plan.md --after S1 --expected-source-sha "$(git rev-parse HEAD)"
```

The last slice runs `--rows CP-10,CP-11,CP-12` at the final committed SHA. Continue `wait` while
the exit is 75. Exit 4 is a slot timeout: report the row as not run. Preserve every tool-produced
`CHECKPOINT` line. No unlisted build or test loop; a failure-driven rerun is the same `CP-n` with its
reason and commit. Code and Review run `scripts/check-evidence-diff.ps1` over the task range. Delete
only producer-owned `bin-c1109-*` outputs; evidence stays ignored.

### Cost

Ordinary Code V/R floor = **113 minutes**, the sum of the `EstimatedMinutes` column
(11 + 3 + 6 + 4 + 7 + 15 + 14 + 10 + 4 + 13 + 14 + 12). Authoring: about 4 hours across five slices.
`-ExpectAbout` for each Code dispatch is its slice's rows plus its authoring budget. Mutation
(post-land): 13 method-scoped controls, about 75 minutes.

## Activation and rollout

The behaviour activates with the AppHost restart that picks up S4 (S1 to S3 are unreachable until
CARD-1082 S5 is wired and change no verdict on their own). To roll back only this card set
`Delegation:RunnerSyncDebtAttestedAncestry = false` and restart; a debt row already accepted keeps
recovering through the S4b sweep. Operators read `progressEvidence.remoteSync.ancestryAttested` on
task detail, the `progress=runner-attested` warning line in the caller's note, and the
`settlement-sync-debt:*` attention items: a Held `runner_sync_diverged` or `runner_sync_rewound`
beside `ancestryAttested = true` is a runner that attested wrongly; inspect that runner's mirror and
treat the branch as CARD-0779's `runner_sync_diverged` recovery describes.

**Measurement (D-9), run on the desktop against the local PostgreSQL after the CARD-1082 S5
activation restart, over one week.** Status 3 is Blocked, role 2 is Code; the evidence JSON names the
reason; a non-Code Pending settlement also writes a `runner_sync_lease_busy` warning, so count by
status and role, not by warning alone.

```sql
-- Residual: Code tasks still Blocked on lease contention after the activation restart.
SELECT date_trunc('day', t."CompletedAt") AS day, count(*) AS blocked_code_lease_busy
FROM "AgentTasks" t
WHERE t."Status" = 3 AND t."Role" = 2 AND t."RunnerId" IS NOT NULL
  AND t."CompletionProgressEvidenceJson" LIKE '%"runner_sync_lease_busy"%'
  AND t."CompletedAt" >= '<activation restart, UTC>'
GROUP BY 1 ORDER BY 1;

-- Of those, how many reports claimed a commit other than the observed tip (D-2's deferred option).
SELECT count(*) FROM "AgentTasks" t
WHERE t."Status" = 3 AND t."Role" = 2
  AND t."CompletionProgressEvidenceJson" LIKE '%"runner_sync_lease_busy"%'
  AND t."CompletedAt" >= '<activation restart, UTC>'
  AND t."CompletionProgressEvidenceJson" !~ ('"claimedSha":"' || substring(t."CompletionProgressEvidenceJson" from '"observedSha":"([0-9a-f]{40,64})"') || '"');
```

API fallback from any host: `GET /api/agent-tasks?projectId=<id>&status=Blocked`, then
`GET /api/agent-tasks/{id}` for each item and keep those with `role == "Code"`,
`progressEvidence.assessment == "Indeterminate"` and `progressEvidence.remoteSync.reason ==
"runner_sync_lease_busy"`.

## Risks and notes for Code and Review

- **The runner must answer `equal`.** `RunnerWorkspaceService.PublishAsync` compares its tip with
  `request.RemoteSha ?? BaselineSha`; the inspect call passes the advertised S, so `equal` means T
  == S. `LocalMirrorPublisher.InspectAsync` must mirror that order (`remoteSha ?? baselineSha`),
  as its `PublishAsync` already does.
- **Objects stay absent in tests.** In `SyncWorld` the desktop has not fetched the runner's push;
  V-1, V-10 and V-12 assert `HasObjectAsync(S)` is false before and after. Do not pre-pin S through
  `TaskProgressGit`; that is the observation under test.
- **One state, one place.** `Classify` alone sets `AncestryAttested`; the progress arm never reads
  `MirrorDescendsFromBaseline` directly (G-8). `TaskCompletionProgressService.PrepareAsync`'s
  direct-evaluation path still sees the raw result and stays Indeterminate.
- **Additive records.** `RemoteSettlementSyncResult` and `RemoteSyncEvidence` gain trailing optional
  parameters; do not reorder. `ProgressEvidenceDto` picks the new fields up unchanged.
- **Fakes implement `InspectAsync` without pushing.** A fake whose inspect pushes would make V-3 and
  V-12 pass for the wrong reason; Review checks the fake.
- **`[Arguments]` counts are promises.** CP rows count results; a new test may not add parameter
  expansion without updating this table.
- **Lease timing in tests.** Reuse `controlledSyncClock: true`, `LeaseBusy.First/Next` and the
  `RunnerSyncBudgetSeconds` advance exactly as `Sync_uncertainty_blocks_and_reply_retries` does; bound
  awaits with `SliceReturnGuard`. V-12's sweep step releases the lease first and uses
  `SweepSettlementSyncAsync` as `C1082_PendingDebtRecoversThroughTheDispatcherSweep` does.
- **The lying runner is a test double, not a runner change.** V-14's `LyingMirrorPublisher` is
  test-local; the real runner's answer is what `RunnerWorkspaceInspectTests` pins.
- **No new attention kind, event type, reason beyond `runner_attested_ancestry`, table or sweep.**
