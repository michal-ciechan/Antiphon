# CARD-0675: land a `-StartRef` repair through the reviewed adoption lane

Date: 2026-09-30. Stage: Plan, with the verification design folded in (the brief asks for the closed
`### Checkpoints` table). Next: Code. Baseline: `a8b4e9e5a0715a644a21c4270dac8b4d60810d4a` on
`feat/card-task-2b4441a8` (clean tree, equal to `origin/master`); the desktop server reports the
same SHA with `land-v2`. Card `f980c996-8892-4045-9594-979eb74de795`, Antiphon board
`8988ca03-7414-47ad-b0b6-51556c701703`, read with `scripts/card.ps1 get CARD-0675`. Evidence: the
source files named below at the baseline; `GET /api/agent-tasks/6eed34e6` and
`GET /api/agent-tasks/5dc73c10` read 2026-09-30 00:25Z; `GET /api/runner-defaults` and
`GET /api/session-runners` read at the same time. No code was changed, no build or test was run.

## Outcome and scope

The card's premise is stale in one respect and right in three. CARD-0753 landed the adopt-repair
operation the card asks for (`delegate.ps1 -Land <owner> -FromTask <repair> -ExpectedSourceSha <sha>
-ReviewEvidenceId <id>`, commit `1b5b282a`, active on the server since 2026-09-26), and its close note
keeps CARD-0675 open "until the recovery path has been used once for real". What is still missing,
and what this plan delivers:

1. **Proof on the incident's exact shape.** No test drives adoption with the owner's desktop mirror
   diverged from its remote (local `L` != remote `R`) and the repair cut at `R`. The card asks for
   exactly that ("test both, including a stale local owner branch"). S1 adds real-Git tests for the
   diverged mirror, the repair cut at the stale mirror, a repair cut below both tips, a detached
   mirror, and the wrong-subject Review.
2. **The `-RepairSource` half of the ask.** `-RepairSource` cuts the repair at the owner's *desktop*
   ref and never looks at origin, so on a runner-bound owner whose mirror lags origin it would have
   silently started at the wrong commit. That is why the orchestrator reached for `-StartRef` in the
   first place. S2 makes `-RepairSource` refuse, with the `-StartRef` + `-FromTask` route in the
   message, when origin's owner tip is ahead of or diverged from the mirror.
3. **An operator recipe and the one missing CLI line.** The flags exist; the route from "repair
   settled on its own branch" to "landed through the owner" is not written down as a recipe, the
   orchestrator bundle still says `-StartRef` "grants no land" without naming the route, and
   `delegate.ps1 -Status` cannot show a Review task's evidence ID or an adoption's uncontained
   owner patches. S3 closes those.
4. **Closing the card.** The card closes only after one real adoption; the plan ends with the
   live drill and the exact commands (caller-owned, not a checkpoint).

Out of scope, stated so Code does not drift into them:

- Combining `-RepairSource` with `-StartRef` (D-1). Landing the repair task itself (D-1).
- Changing the automatic runner settlement sync (CARD-0657, in Review) or its
  `runner_sync_diverged` / `unclaimed_or_unmatched_commit` verdicts. Adoption does not depend on
  the repair's terminal status.
- New API fields or EF migrations. Every persisted field the tests assert already exists.
- The Mutation verification lane and the CARD-0753 positive controls still pending there.

## Ground truth

| The card assumes | What the code does at `a8b4e9e5` |
|---|---|
| There is no adopt-repair path; the only fix is a manual reset of the owner's desktop worktree and a force-push of the owner branch. | `AgentTaskLandService.RequestAsync` (`server/Application/Services/AgentTaskLandService.cs:93-109, 189-254`) admits `adoptFromTaskId`; `AgentTaskLandSourceResolver.RecoverSourceAsync` (`AgentTaskLandSourceResolver.cs:220-460`) pins `local-before`/`source`/`remote-before` under `refs/antiphon/land/<owner>/<request>/adopt/`, moves the owner's local ref with `update-ref --no-deref <ref> <S> <L>` (a compare-and-swap on `L`), resets the owner worktree, then pushes the owner ref with `--force-with-lease=<ref>:<R>` under the repository lease. The ordinary detached rebase, verification and publication follow. `delegate.ps1` sends the body only to `/land/v2` (`scripts/delegate.ps1:756-808`). |
| `-Land <owner> -ExpectedSourceSha <repair sha>` is refused `source_remote_diverged`. | Still true for a plain land, by design: `ClassifyAsync` (`AgentTaskLandSourceResolver.cs:506-518`) refuses a diverged `L`/`R` pair at line 144-145. A plain land never moves the owner ref. This is the control, not the defect. |
| `-Land <repair>` is refused "must have succeeded before it can land", and a repair is not the landing owner anyway. | A `-StartRef` task is an ordinary Code owner of its own branch; a plain land still requires Succeeded (`AgentTaskLandService.cs:108-109`). Adoption never lands the repair: it lands *through the owner*. The repair is an eligible adoption **source** while Succeeded, Blocked or Failed (`LandApproval.RecoveryStatusEligible`, `LandApproval.cs:101-102`; admission at `AgentTaskLandService.cs:209-211`). |
| Runner tasks settle Failed until CARD-0657 lands, which blocks the land. | The repair `6eed34e6` settled Failed `CompletedWithoutProgress` (`unclaimed_or_unmatched_commit`, desktop mirror at `8035c0ea`, origin at `fcd657c2`). That status does not block adoption. The owner `5dc73c10` was Succeeded; CARD-0657 is in Review and is not a prerequisite. |
| The repair was "dispatched with `-StartRef` at the original owner's SHA". | `worktreeBaseSha = 8035c0eae89f7ecbb3bf57c73beacff3ef7fad5e` (Explicit), which was the owner's **remote** tip. The owner's own report says it rebased onto master and pushed with a lease over `a0a5b7b6`; its desktop mirror stayed at `a0a5b7b6` because settlement sync hit `repository_lease_busy` (`progressEvidence.reason`). So `L = a0a5b7b6`, `R = 8035c0ea`, diverged. Nobody force-pushed by hand before the land attempt; the divergence came from the owner's legitimate rebase plus a skipped sync. |
| The manual force-push was the only way. | The final land (`operation 352417be`, `recoveryMode: None`, `reviewEvidenceId: null`) happened after the manual reset. Today the same state is exactly the adoption lane's input: lineage passes because `source.WorktreeBaseSha == R` (`AgentTaskLandSourceResolver.cs:319-352`), the CAS moves `L -> S`, the lease push moves `R -> S`. Untested on this shape: see S1. |
| `-RepairSource` "can't be combined with `-StartRef`". | True by design (`AgentTaskService.cs:296-313`, `worktree_start_ref_mode`; `docs/ops-http.md:405-435`). Not changed (D-1). |
| Letting `-RepairSource` work with runner tasks is an alternative fix. | `AgentTaskDispatcher.PrepareRepairSourceAsync` (`AgentTaskDispatcher.cs:6215-6290`) reads the owner's desktop ref (`RevParseCommitAsync(task.RepoPath, ownerRef)`) and the owner's registered worktree; it never observes origin. For `5dc73c10` it would have cut the repair at `a0a5b7b6`, not `8035c0ea`, with no warning. The dispatch baseline capture right after it (`CaptureProgressBaselineAsync`, `:6294-6330`) already observes the owner's remote with one bounded `ls-remote` (`ObserveRemoteForBaselineAsync`, `:6333`). S2 moves that observation in front of the base choice. |
| "Test both, including a stale local owner branch." | `AgentTaskLandAdoptionTests.C753_ReviewedRepairAdoptionObeysLocalCasAndRemoteLease` (`tests/Antiphon.Tests/Application/AgentTaskLandAdoptionTests.cs:338-465`) sets owner local == owner remote == repair base. `C753_ReviewedSelfRecoveryAlignsRewrittenOwnerSourceAndPublishes` covers the self-recovery lane only. No test has `L != R` with the base at `R`, at `L`, or below both. |
| A Review of the repair is enough. | The Review's evidence row binds `SubjectTaskId` from the `subjectTaskId` line of the `--- review evidence ---` block, `ReviewedSourceRef` = that subject's branch, `ReviewedRepositoryPath` = its repo, and authorises any same-card Worktree subject (`AgentTaskReplyService.cs:4350-4420`, `ReviewSubjectAuthorized`). Adoption requires that row to name the **repair** as subject at exactly `S`, Clean, `CommissionedRound = Final` and `OrdinaryScopeCompleted = Full` (`LandApproval.LoadRecoveryEvidenceAsync`, `LandApproval.cs:91-99`; `LoadUsableEvidenceAsync` checks subject, SHA, ref, repository, supersession). An omitted `-VerificationRound` is Final for Code/Review (`InterimVerificationPolicy.ResolveRound`), so an ordinary Review works when its brief names the repair as subject; the stage-review bundle already says "owner unless brief names -FromTask" (`server/Bundles/stage-review.md:9`). A Review whose subject is the owner cannot adopt the repair (`review_evidence_subject_mismatch`). |
| Nothing else is required of the owner. | Adoption inspects the owner's registered worktree with `IdentityAndStatus` (`AgentTaskLandSourceResolver.cs:280-317`): it must exist, be on its branch and be clean (`source_dirty`, `source_branch_mismatch`, `LandingGit.cs:402, 441`). A runner-bound owner's desktop mirror is written by nobody, so it is normally clean and on-branch. An interrupted reset resumes only while the checkout still equals the pinned `L`. |
| The orchestrator can compose the land command from what it sees. | The Review's completion header carries `review-evidence=<id>; subject=<guid>; reviewed-sha=<sha>` (`DelegationReportFormatter.cs:666-667`) and `GET /api/agent-tasks/{review}` exposes `reviewEvidence` (`ReviewEvidenceDto`, `AgentTaskDtos.cs:624-630`). `delegate.ps1 -Status` prints neither (`scripts/delegate.ps1:666-753`), and prints an adoption receipt without its `recoveryUncontainedPatches`. |
| Documentation covers the route. | `docs/orchestration-loop.md:116-117, 149-150` says a `-StartRef` repair can be adopted after Clean Final/Full Review; `docs/ops-http.md:122` documents the body. `docs/antiphon-api.md:387-392` lists `/land/v2` without `adoptFromTaskId`/`recoverReviewedSource`; `server/Bundles/orchestrator.md:63-69` says `-StartRef` "grants no land" and stops there; `.claude/skills/antiphon-delegate/SKILL.md:146` documents `-RepairSource` and never mentions `-FromTask`. |

Live fleet at plan time: `runner-defaults.globalRunnerId = server2`; `server2` is `draining`,
`acceptingNewWork: false`; `server2-temp` (Linux, `workspaceRepositoryV1`) and `desktop` (Windows,
capacity 2) accept work. This plan embeds no runner; the checkpoints name the Linux lane.

## Decisions

### D-1: Adoption stays the single supported way to land a `-StartRef` repair

CARD-0753's decision stands: a repair's reviewed tip lands **through the original owner** by
`-FromTask`, with the owner ref moved under lease and a compare-and-swap, and the owner's historical
status preserved. Rejected:

- *Let `-Land <repair>` land the repair as a new owner.* It breaks the owner continuity every other
  record hangs on (card transition, landing evidence, CARD-0684 D-8 start-base provenance) and a
  Failed repair still could not be an ordinary owner without inventing a status rewrite.
- *Make `-RepairSource` accept `-StartRef` (the card's second option).* `-RepairSource`'s whole
  meaning is "cut at the owner's ref and attribute a fast-forward of that ref". A repair that must
  start elsewhere is asking for a non-fast-forward move of the owner ref, which is precisely the
  reviewed, leased, pinned operation adoption already is. Two ways to force-move an owner ref would
  be two audits, two race matrices and two sets of refusals.

### D-2: `-RepairSource` refuses a stale owner mirror instead of guessing

`PrepareRepairSourceAsync` observes origin's owner ref once, with the same bounded `ls-remote`
seam the baseline capture uses (`ObserveRemoteForBaselineAsync`), before it chooses the base. Then:

| Origin vs desktop mirror | Action |
|---|---|
| `Present`, equal | unchanged: repair cut at the mirror SHA |
| `Present`, mirror is an ancestor of origin (mirror **behind**) | fail the repair at dispatch: `repair_source_owner_remote_ahead: owner <short> local=<L> remote=<R>. The desktop mirror is behind origin; dispatch -Worktree -StartRef <R> and land it with -Land <owner> -FromTask <repair> after Review.` |
| `Present`, neither is an ancestor (**diverged**) | fail the repair at dispatch: `repair_source_owner_diverged: ...` with the same SHAs and route |
| `Present`, origin is an ancestor of the mirror (mirror ahead, unpushed desktop work) | unchanged: the mirror is the truth |
| `Missing` (owner never pushed) or `NotConfigured` | unchanged |
| `Unavailable` (origin unreachable) | unchanged base, plus a dispatch warning `owner remote unobserved (<reason>); repair base is the desktop ref <L>` (fail-open, the same policy as the baseline's `progress=unavailable`) |
| local ancestry probe fails | `repair_source_identity_unavailable` (fail closed, like every other local git failure in that method) |

Failing at the claim, through the existing `FailAsync(claimed, prep.FailureReason)` arm, is what
`repair_source_identity_unavailable` already does; the caller gets one Failed task whose reason is
the command to run next. The guard must not `fetch` into `refs/heads/*` or `refs/remotes/*`
(`RepairSourceSettlementTests.C499_V18` asserts the dispatcher never does). Rejected: fetching or
fast-forwarding the mirror inside the claim (a repository mutation in the dispatch path that
CARD-0657's settlement sync owns under its own lease, and which refuses divergence anyway), and
silently cutting the repair at origin's tip (the prep warning, the owner's worktree and the
attribution baseline would then disagree about the base).

### D-3: The lineage rule and the containment record are unchanged

The source's recorded base (`WorktreeBaseSha`) must equal, or descend from, one of: the owner's
current remote tip, the owner's local tip, their pinned "before" values, or the superseded request's
reviewed SHA (`AgentTaskLandSourceResolver.cs:319-352`). A base below both tips refuses
`adopt_source_lineage` **before any pin, CAS or push**. A base at the stale mirror `L` while origin
is at `R` is admitted; `git cherry <S> <R>` records whether every `R` patch is contained and lists
the uncontained commit IDs on the request and receipt (`RecoveryPatchesContained`,
`RecoveryUncontainedPatches`). S1 tests both arms and S3 prints the list. Rejected: refusing the
uncontained case outright (CARD-0753 chose visibility; the reviewer of `S` saw the branch, and a
refusal would send the caller back to a manual force-push, which is the thing this card removes).

### D-4: Real Git, red first, mutation-sensitive assertions

Every S1 and S2 test runs real Git through `LandingSafetyHarness`/`LandingGitFixture` or
`RepairSourceWorld` (both already have a bare origin). A test passes only on the production line it
guards: the `--force-with-lease=<ownerRef>:<R>` argument, the `update-ref ... <S> <L>` old-value
argument, the receipt SHAs, the refusal code and the absence of any push are asserted, never a
"landed" boolean alone. Mock-only success proves nothing about a force-move.

### D-5: No schema or API change

`RecoveryMode`, `RecoverySourceTaskId`, `RecoveryStartBaseSha`, `RecoveryLocalBeforeSha`,
`RecoveryOwnerRemoteBeforeSha/AfterSha`, `RecoveryPatchesContained`, `RecoveryUncontainedPatches`
and `ReviewEvidenceDto` already exist and are already serialised. S2 adds two failure-reason
prefixes (strings). S3 changes only `scripts/delegate.ps1` output and documentation.

### D-6: Lane

All rows are platform-neutral and run on the Linux session runner (`server2-temp` at plan time,
git 2.47.3 observed on this runner; `GET /api/session-runners` decides at Code time). No
`-Runner` pin. The CLI tests spawn `pwsh`, present on the Linux image. Do not co-schedule
`tests/Antiphon.Agents.Pty.Tests`.

### D-7: The card closes on a real adoption, not on green checkpoints

The CARD-0753 close condition is kept: after this lands and the server is restarted, the caller
runs the drill in "Post-land acceptance" once on a real card and quotes the `Recovery receipt` line
in the close reason.

## Implementation slices

### S1: prove adoption on the incident shape (tests only)

New class `tests/Antiphon.Tests/Application/StartRefRepairAdoptionTests.cs` (`[Category("Integration")]`,
`[ParallelLimiter<ProcessSpawnLimit>]`), built on `LandingSafetyHarness` the way
`C753_ReviewedRepairAdoptionObeysLocalCasAndRemoteLease` is, with one private helper that builds the
incident: owner pushed at `L`; in a second clone of the fixture remote, rewrite the owner branch
(amend or rebase onto an independent target commit) and push with a lease so origin holds `R` while
the owner's registered worktree and local ref stay at `L`; cut the repair worktree at a chosen base,
commit `S`, push the repair branch; seed the same-card source task with `WorktreeBaseSha = base` and
the chosen status; add a Clean Final/Full Review row bound to the source at `S` (reuse the existing
`AddReviewAsync` shape; move it to a shared internal helper if both classes need it).

- **V-1** `C675_DivergedOwnerMirrorAdoptsRepairCutAtRemoteTip` `[Arguments(Failed)]`,
  `[Arguments(Succeeded)]`: base = `R`. Expect publication; request `RecoveryLocalBeforeSha == L`,
  `RecoveryOwnerRemoteBeforeSha == R`, operation `RecoveryOwnerRemoteAfterSha == S`,
  `RecoveryPatchesContained == true`; trace contains `update-ref --no-deref <ownerRef> S L` and
  `--force-with-lease=<ownerRef>:R`; the owner's status is unchanged; the repair worktree survives
  cleanup; the repair branch stays on origin at `S`.
- **V-2** `C675_PlainLandOnDivergedMirrorStillRefuses`: same fixture, plain request with
  `expectedSourceSha = S` and no recovery. Expect `SourceRefusalReason == source_remote_diverged`,
  zero landing operations, no `push` in the trace, owner local still `L`, origin still `R`.
- **V-3** `C675_RepairCutAtStaleMirrorRecordsUncontainedRemotePatches`: base = `L`, origin `R` has
  one distinct patch. Expect publication, `RecoveryPatchesContained == false`,
  `RecoveryUncontainedPatches` names `R`'s commit, lease still `:R`.
- **V-4** `C675_RepairCutBelowBothOwnerTipsRefusesLineage`: base = the seed commit below `L` and
  `R`. Expect `adopt_source_lineage`, zero operations, no `refs/antiphon/land/*/adopt/*` pins, no
  `update-ref` on the owner ref, no `push`, owner local `L`, origin `R`.
- **V-5** `C675_DetachedOwnerMirrorRefusesBeforeMutation`: owner worktree `checkout --detach` at
  `L`. Expect `source_branch_mismatch` (or the exact identity reason `LandingGit.InspectAsync`
  returns), no pins, no push. The dirty arm stays with
  `C753_DirtyOwnerCheckoutRefusesBeforeReviewedSourceMutation`.
- **V-6** `C675_OwnerBoundOrInterimReviewCannotAdoptRepair` `[Arguments("owner-subject")]`,
  `[Arguments("interim")]`: a Clean Final/Full row whose subject is the **owner** at `S`, and a
  repair-bound row with `CommissionedRound = Interim`. Expect `RequestAsync` to throw
  `review_evidence_subject_mismatch` / `review_verification_scope_ineligible` and zero request rows.

### S2: `-RepairSource` base-currency guard

`server/Application/Services/AgentTaskDispatcher.cs`, `PrepareRepairSourceAsync` (after the
registration and symbolic-HEAD checks, before the "occupied source ... routing to an isolated
branch" warning): observe origin's owner ref through `ObserveRemoteForBaselineAsync`, classify with
two `merge-base --is-ancestor` probes on the desktop repository, and apply the D-2 table. Put the
two reason prefixes next to `repair_source_identity_unavailable` as constants so the tests and the
docs quote one spelling.

Tests in `tests/Antiphon.Tests/Application/RepairSourceDispatchTests.cs` (Slow, `RepairSourceWorld`,
which already pushes the owner to a bare origin at setup):

- **V-7** `C675_RepairSourceRefusesWhenOwnerRemoteIsAheadOrDiverged` `[Arguments("behind")]`,
  `[Arguments("diverged")]`: before `DispatchAsync`, push a commit to `OwnerRef` from a second clone
  (`CommitFromSecondCloneAsync`) or force-push an amended owner commit from that clone. Expect the
  repair `Failed`, `FailureReason` starting with the code and containing both SHAs and `-StartRef`,
  no repair worktree registered, owner local and remote unchanged (`Snapshot`).
- **V-8** `C675_RepairSourceProceedsWhenRemoteEqualLocalAheadOrUnreachable` `[Arguments("equal")]`,
  `[Arguments("local-ahead")]`, `[Arguments("unreachable")]`: unchanged routing at the owner's
  local SHA; `local-ahead` commits in the owner tree without pushing; `unreachable` renames the bare
  remote directory before dispatch and expects the warning naming the unobserved remote. Assert the
  trace has no `fetch` into `refs/heads/` or `refs/remotes/` (the V18 invariant).

Docs in the same slice: the Repair source paragraph of `docs/orchestration-loop.md` and the
`repairSourceTaskId` paragraph of `docs/ops-http.md` name both codes and the route.

### S3: operator surface

- `scripts/delegate.ps1` `-Status`: after the `Delegate:` line, when the task detail carries
  `reviewEvidence`, print `Review evidence: <id>; subject <subjectTaskId>; reviewed <sha>; ref <ref>;
  outcome <outcome>`. Under `Recovery receipt:` (and the request's `Recovery:` line) print
  `Uncontained owner patches: <comma list>` when `recoveryUncontainedPatches` is non-empty, and
  `Owner patches contained` when `recoveryPatchesContained` is true.
- `docs/orchestration-loop.md`: a short recipe **"Landing a `-StartRef` repair (CARD-0675)"** after
  the existing sentence at line 149-150: (1) Review the repair's pushed tip with a brief that names
  the repair as `subjectTaskId` (a Final round; Interim cannot approve); take
  `review-evidence=<id>` from the completion header or `-Status <review>`; (2)
  `delegate.ps1 -Land <owner> -FromTask <repair> -ExpectedSourceSha <S> -ReviewEvidenceId <id>`;
  (3) `-Status <owner>` shows `Recovery: AdoptReviewedSource ...` then `Recovery receipt: ...`.
  Preconditions in one list: owner Succeeded/Blocked/Failed (never Canceled); repair settled and
  not itself landing; repair base equal to or above the owner's current remote or local tip; owner
  desktop worktree present, clean, on its branch. Refusals in one table: `adopt_source_lineage`,
  `source_dirty`/`source_branch_mismatch`, `review_evidence_subject_mismatch`,
  `review_verification_scope_ineligible`, `adopt_source_remote_changed`,
  `adopt_source_push_rejected`, `source_remote_diverged` (plain land, expected on a diverged
  mirror; use the recipe).
- `server/Bundles/orchestrator.md:63-69`: one sentence after "grants no land": "To land a
  `-StartRef` repair, Review its pushed tip with the repair named as subject, then
  `-Land <owner> -FromTask <repair> -ExpectedSourceSha <sha> -ReviewEvidenceId <id>`." Keep the
  bundle inside its existing size guard (`StandingPipelinePolicyDocumentationTests`).
- `.claude/skills/antiphon-delegate/SKILL.md:146`: add the `-FromTask` / `-RecoverReviewedSource`
  row beside `-RepairSource`. `docs/antiphon-api.md:387-392`: add `adoptFromTaskId?` and
  `recoverReviewedSource?` to the `/land/v2` body.
- Tests: `DelegateScriptAdoptTests` gains **V-9** `C675_StatusPrintsReviewEvidenceOfAReviewTask`
  (stub body with `reviewEvidence`) and **V-10** `C675_StatusPrintsUncontainedOwnerPatches` (stub
  landing with `recoveryPatchesContained=false`, two IDs). `RepairSourceDocumentationTests` gains
  **V-11** `C675_DocsNameTheStartRefRepairLandingRoute`: `docs/orchestration-loop.md` contains
  `-FromTask`, `adopt_source_lineage` and `repair_source_owner_diverged`; `docs/ops-http.md`
  contains `repair_source_owner_remote_ahead`; `server/Bundles/orchestrator.md` and the delegate
  `SKILL.md` contain `-FromTask`; `docs/antiphon-api.md` contains `adoptFromTaskId`.

Commit order: S1 (tests, red first against the fixture, then green on unchanged production code
except where a test finds a real defect, which is reported and fixed in the same slice), S2, S3.
Each slice is committed and pushed before the next begins.

## Verification design

### Coverage inventory

| ID | Guards | Class.Method | Lane |
|---|---|---|---|
| V-1 | adoption on a diverged mirror with the repair cut at `R` (the incident) | `StartRefRepairAdoptionTests.C675_DivergedOwnerMirrorAdoptsRepairCutAtRemoteTip` x2 | Integration, real Git |
| V-2 | plain land still refuses the same mirror | `StartRefRepairAdoptionTests.C675_PlainLandOnDivergedMirrorStillRefuses` | Integration, real Git |
| V-3 | base at stale `L`: uncontained `R` patches recorded, lease still `:R` | `StartRefRepairAdoptionTests.C675_RepairCutAtStaleMirrorRecordsUncontainedRemotePatches` | Integration, real Git |
| V-4 | base below both tips refuses before any mutation | `StartRefRepairAdoptionTests.C675_RepairCutBelowBothOwnerTipsRefusesLineage` | Integration, real Git |
| V-5 | detached owner mirror refuses before any mutation | `StartRefRepairAdoptionTests.C675_DetachedOwnerMirrorRefusesBeforeMutation` | Integration, real Git |
| V-6 | Review subject/round binding for adoption | `StartRefRepairAdoptionTests.C675_OwnerBoundOrInterimReviewCannotAdoptRepair` x2 | Integration |
| V-7 | `-RepairSource` refuses a behind or diverged mirror | `RepairSourceDispatchTests.C675_RepairSourceRefusesWhenOwnerRemoteIsAheadOrDiverged` x2 | Integration (Slow), real Git |
| V-8 | `-RepairSource` unchanged when equal, local-ahead or unreachable | `RepairSourceDispatchTests.C675_RepairSourceProceedsWhenRemoteEqualLocalAheadOrUnreachable` x3 | Integration (Slow), real Git |
| V-9 | `-Status` prints a Review's evidence | `DelegateScriptAdoptTests.C675_StatusPrintsReviewEvidenceOfAReviewTask` | Integration (pwsh) |
| V-10 | `-Status` prints uncontained owner patches | `DelegateScriptAdoptTests.C675_StatusPrintsUncontainedOwnerPatches` | Integration (pwsh) |
| V-11 | docs, bundle and skill name the route and the codes | `RepairSourceDocumentationTests.C675_DocsNameTheStartRefRepairLandingRoute` | Unit |
| R-1 | CARD-0753 adoption, self recovery, supersession and cleanup unchanged | `AgentTaskLandAdoptionTests` (15) | Integration, real Git |
| R-2 | repair dispatch and settlement attribution unchanged by the guard | `RepairSourceDispatchTests` (24), `RepairSourceSettlementTests` (36) | Integration (Slow) |
| R-3 | existing CLI land/status/StartRef contracts | `DelegateScriptAdoptTests` (3), `DelegateScriptLandStatusTests` (19), `DelegateScriptStartRefTests` (11) | Integration (pwsh) |
| R-4 | admission refusals and repair-target refusal unchanged | `AgentTaskLandRequestTests` (29), `RepairSourceLandRefusalTests` (3) | Integration |
| R-5 | `-StartRef` provisioning unchanged | `WorktreeStartRefDispatchTests` (30) | Integration (Slow) |
| R-6 | Unit lane, including the bundle size and policy guards S3 touches | `[Category=Unit]`, `StandingPipelinePolicyDocumentationTests` (4) named | Unit |

The counts in parentheses are executed results at the baseline (argument expansions counted);
they are the roster floors below, before the new methods are added.

### Red and green expectations per checkpoint

- CP-1 red first: V-1 goes red when the resolver's lease push is replaced by `--force` or the CAS
  old value is dropped; V-4 goes red when the lineage loop admits an unmatched base; V-3 goes red
  when the cherry record is skipped; V-2 and V-5 go red only if production changes, so they are
  written first against the fixture and must fail on a deliberately wrong expected code before the
  expected code is put in.
- CP-2: control; no new red.
- CP-3 red first: V-7 is red until the D-2 guard exists; V-8 stays green throughout (it pins the
  unchanged arms) and is written first.
- CP-4: control for S2; no new red.
- CP-5 red first: V-9 and V-10 are red until the `-Status` lines exist.
- CP-6: V-11 is red until every named document carries the string.
- CP-7: controls; no new red.

### Platform per row

All rows run on the Linux lane (D-6). On Linux the checkpoint tool adds `UseAppHost=false` itself.
`RepairSourceDispatchTests`, `RepairSourceSettlementTests` and `WorktreeStartRefDispatchTests` are
registered Slow; the new S2 methods live in a registered class, so no allow-list change is needed.
The new `StartRefRepairAdoptionTests` class is Integration, not Slow; if any of its methods exceeds
the 5 s tripwire, register the class rather than loosening a timeout.

### Positive controls for the later Mutation stage

Method-scoped, red then restored, not run by Code:

- PC-1: in `RecoverSourceAsync`, push with `--force` instead of `--force-with-lease=<ref>:<R>`;
  expect V-1 red on the trace assertion.
- PC-2: drop the old-value argument from `update-ref --no-deref <ref> <S> <L>`; expect V-1 red
  and `C753_ReviewedRepairAdoptionObeysLocalCasAndRemoteLease("local-cas")` red.
- PC-3: set `lineage = true` when no candidate matched; expect V-4 red.
- PC-4: skip the `git cherry` record; expect V-3 red.
- PC-5: treat `Behind` as `Equal` in the D-2 guard; expect V-7 `"behind"` red.
- PC-6: remove the `Review evidence:` line from `-Status`; expect V-9 red.

### Execution

Commit S1, S2 and S3 in order, pushing each. One checkpoint-tool run for the committed group:

```text
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-30-card-0675-startref-repair-landing-plan.md --after S1-S3 --max-wait 570s
```

After exit 75 call `wait <run-id> --max-wait 570s` until the exit is not 75; never end the turn
while a run is live. A tool bootstrap build, if needed, goes through `scripts/build-slot.ps1` and
releases its lease before the executor starts. Report each `CHECKPOINT` line with its counts and
reruns, and every unlisted command with a reason. In the Markdown cells below `\|` is a literal
filter OR; the executable filter has a plain `|`.

### Cost

Ordinary Code verification floor: 14 + 12 + 12 + 14 + 10 + 12 + 15 = **89 minutes** (one isolated
build of `Antiphon.Tests` in CP-1, reused by every later row; rows overlap two at a time on Linux,
the Unit row runs alone). Authoring and red-first tests: about 5-6 hours across three slices (S1 is
the bulk: one fixture helper and six real-Git methods). Code `-ExpectAbout`: 7 hours. Add the
observed build-slot wait on a busy host.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c675/` | startref-adoption | `/*/Antiphon.Tests.Application/(StartRefRepairAdoptionTests*)/*` | V-1, V-2, V-3, V-4, V-5, V-6 | all 8 results (2 + 1 + 1 + 1 + 1 + 2); 0 failed, 0 skipped | 8 | 14 | |
| CP-2 | S1-S3 | CP-1 | adoption-r | `/*/Antiphon.Tests.Application/(AgentTaskLandAdoptionTests*)/*` | R-1 | all 15 results; 0 failed, 0 skipped | 15 | 12 | |
| CP-3 | S1-S3 | CP-1 | repair-source-guard | `/*/Antiphon.Tests.Application/(RepairSourceDispatchTests*)/*` | V-7, V-8, R-2 | all 29 results: 24 existing + 2 + 3; 0 failed, 0 skipped | 29 | 12 | |
| CP-4 | S1-S3 | CP-1 | repair-settlement-r | `/*/Antiphon.Tests.Application/(RepairSourceSettlementTests*)/*` | R-2 | all 36 results; 0 failed, 0 skipped | 36 | 14 | |
| CP-5 | S1-S3 | CP-1 | cli-status | `/*/Antiphon.Tests.Application/(DelegateScriptAdoptTests*)\|(DelegateScriptLandStatusTests*)\|(DelegateScriptStartRefTests*)/*` | V-9, V-10, R-3 | all 35 results: 5 adopt (3 + 2) + 19 status + 11 start-ref; 0 failed, 0 skipped | 35 | 10 | |
| CP-6 | S1-S3 | CP-1 | unit | `/*/*/*/*[Category=Unit]` | V-11, R-6 | all Unit, `RepairSourceDocumentationTests` (2) and `StandingPipelinePolicyDocumentationTests` (4) present, >= 3400 executed, 0 failed | 3400 | 12 | true |
| CP-7 | S1-S3 | CP-1 | admission-and-startref-r | `/*/Antiphon.Tests.Application/(AgentTaskLandRequestTests*)\|(RepairSourceLandRefusalTests*)\|(WorktreeStartRefDispatchTests*)/*` | R-4, R-5 | all 62 results: 29 request + 3 refusal + 30 start-ref dispatch; 0 failed, 0 skipped | 62 | 15 | |

`AgentTaskLandApprovalRecoveryTests` holds the older `source_remote_diverged` control at line 173
but also four legacy cases that were red at the CARD-0753 base; it is deliberately not a row here.
V-2 is the divergence control for this shape.

## Post-land acceptance (caller-owned; closes the card)

Landing publishes; it does not activate. After the land: `git pull --rebase` in the main checkout,
`scripts/restart-apphost.ps1`, confirm `GET /api/version` is the landed SHA and still advertises
`land-v2`. Then one real adoption, on a real card, on the Linux lane the fleet offers that day:

1. Owner: `delegate.ps1 -Role Code -Worktree -Card <card> -Goal <small real change>`; wait for it
   to settle and note its remote tip `O` from `-Status`.
2. Repair: `delegate.ps1 -Role Code -Worktree -Card <card> -StartRef <O> -Goal <small follow-up>`;
   wait for it to settle (Succeeded or Failed both qualify) and note its pushed tip `S`.
3. Review: `delegate.ps1 -Role Review -Card <card> -Goal "Review <repair short id> at <S>; the
   evidence subject is the repair"`; take `review-evidence=<id>` from the completion header or
   `-Status <review>` (S3).
4. `delegate.ps1 -Land <owner> -FromTask <repair> -ExpectedSourceSha <S> -ReviewEvidenceId <id>`.
5. `delegate.ps1 -Status <owner>` shows `Recovery: AdoptReviewedSource; source task <repair> ...`,
   then `Recovery receipt: AdoptReviewedSource ...; owner remote <O> -> <S>` and
   `Publication: Landed`. Quote that line in the close reason.

If step 4 refuses, the code names the cause (recipe table in S3); a refusal other than those listed
is a new card, not a manual force-push.

## Handoff

Code implements S1, S2, S3 in order from this plan, committing and pushing each slice, and runs
CP-1 through CP-7 as the closed list with the checkpoint tool. The one default to confirm or change,
not blocking Code: D-2 fails a `-RepairSource` dispatch on a stale mirror rather than holding it;
a hold would wait for a settlement sync that a diverged mirror can never pass.
