# CARD-1091: correct the remote-prep config-lock rationale

Date: 2026-10-12. Plan task: `5091044b-8592-4b80-bb58-abfa62a1cf9e`.
Assigned branch: `feat/card-task-5091044b`, fast-forward-only from
`a91ba68a72185d57d39e87b218e459c5df2451de`.
Inspected landed `origin/master`: `a91ba68a72185d57d39e87b218e459c5df2451de`.
Status: design only; verification is a separate TestDesign stage. Next: **test-design**.

## Outcome and remaining scope

The production defect is already fixed. CARD-1076 S2 commit
`678f246fad59a82bf4d9ff8aee7ee8eb8f2eeab4`, contained in inspected master,
removed `-u` from `RemoteWorkspaceService.PushBranchAsync`. It also added two
command assertions that would reject its return. The current prep push is
`git push origin <branch>`; it does not install desktop upstream tracking.

CARD-1091 is only partially satisfied: D-3 in the CARD-1076 plan still says the
push writes upstream configuration and that Git's ref/config locks retry.
The remaining implementation is one documentation correction. Do not recreate
the landed production fix or change worktree tracking behavior to address an
obsolete call site. The historical S1 review was valid for the code it reviewed;
S2 subsequently removed its cause.

This dispatch changes only this plan. It runs no builds, tests, or Mutation.

## Ground truth

File/line references below refer to the inspected master SHA above. The live
CARD-1091 description was read with `scripts/card.ps1 get CARD-1091 -Board Antiphon`.

| What the card assumes | What landed code/documentation does | Planning consequence |
|---|---|---|
| The remote-prep push passes `-u` and writes shared upstream configuration. | `server/Application/Services/RemoteWorkspaceService.cs:114-123` passes `["push", "origin", task.WorktreeBranch]`, with the configured budget and `remote-prep-push` task tag. Commit `678f246fad59a82bf4d9ff8aee7ee8eb8f2eeab4` removed `-u`. | No production edit is required. Credit S2 explicitly. |
| A new guard is needed against upstream tracking returning. | `tests/Antiphon.Tests/Application/RemoteWorktreeMirrorTests.cs:124-139` asserts the exact push command, default budget, and task tag; its comment explicitly cites CARD-1091 and the missing config-lock retry. `tests/Antiphon.Tests/Application/RemoteWorkspacePreparerTests.cs:435-455` asserts the exact argument array and configured budget through the preparer. | Reuse existing coverage; do not add a duplicate command-string test. These are inspected assertions, not fresh passing receipts. |
| D-3's lock-retry explanation is correct. | `docs/superpowers/plans/2026-10-06-card-1076-remote-prep-push-fence-plan.md:101-104` still claims an upstream config write and ref/config locks with retry. `server/Infrastructure/Git/LandingGit.cs:56-87` makes one process invocation, without an application config-lock retry loop. | Correct the rationale; do not replace it with another general promise about Git lock retries. |
| Removing `-u` could prevent the mirror from finding its branch. | `server/Application/Services/RemoteWorkspaceService.cs:130-135` sends the explicit branch, SHA, mirror name, and normalized repository to the runner. | The mirror contract does not require desktop branch upstream configuration. |
| A remote-tracking base may make the worktree cut a config writer. | `server/Infrastructure/Git/WorktreeManager.cs:103-118` still uses `worktree add -b <branch> <path> <baseRef>` without `--no-track` and rolls back failed adds. The fallback is local `master` (`server/Application/Settings/GitSettings.cs:9`); project/configured base selection is in `server/Application/Services/DelegationWorktreeService.cs:236`. | Preserve existing base/tracking policy. The specific competing prep-push config write has already been removed; do not promise that every worktree cut is config-free. |
| The LIVE tagged child stops fencing only in S1 and the budget remains future work. | `server/Infrastructure/Git/RepositoryChildJournal.cs:108-145` admits a well-formed live record of the allowlisted purpose. `RemoteWorkspaceService.cs:117-119` already supplies the budget and tag. | Both S1 and the relevant S2 behavior are landed. Preserve journal admission and crash visibility. |
| Operational documentation also needs the no-upstream behavior added. | `docs/orchestration-loop.md:925`, `docs/ops-http.md:144`, and `docs/antiphon-api.md:460` already document the push without `-u`. `tests/Antiphon.Tests/Application/RunnerBranchContractDocumentationTests.cs:32-58` pins that contract in the living owners. | Change the stale plan rationale only; avoid unrelated owner-document churn. |

## Decisions

- **D-1 — Scope the remaining work to the stale D-3 rationale.** The runtime
  fix and two guards are already landed. Preserve the CARD-1076 plan's original
  inspection SHA and historical ground-truth table; add a dated CARD-1091
  correction at D-3 to distinguish the implemented command from its original
  rationale. Rejected: treating the entire card as already complete while its
  expressly requested wording correction remains outstanding; repeating S2.
- **D-2 — Explain safety through removal of the upstream write.** State that
  the explicit prep push publishes the branch and can update its local
  remote-tracking ref, but does not request upstream installation. Do not claim
  config-lock retry or general immunity to Git contention. Ordinary Git failure
  still follows the existing prep failure/backoff path. Rejected: adding a retry
  loop to `LandingGit`, extending the upload budget, or putting the upload back
  under the repository lease; none addresses the remaining documentation defect.
- **D-3 — Preserve worktree tracking semantics.** Do not add `--no-track` to
  `WorktreeManager`. It would change all applicable worktree cuts and is no
  longer needed to remove this particular concurrent writer. Rejected: combining
  both of the card's alternative fixes after one has already landed.
- **D-4 — Reuse the existing regression guards.** No new test code is proposed
  for a reversible prose correction. The exact-command assertions already
  reject reintroducing `-u`. The suggested real concurrent remote-base cut is
  not a completion requirement for this reduced scope: it needs a separate
  native fixture, barriers, and cleanup without establishing a new changed
  runtime behavior. This plan does not claim that concurrency scenario was
  executed or proved by a fake. Rejected: a sleep-based race test or a prose
  string test solely to force a test change.
- **D-5 — Keep verification separate and placement unpinned.** TestDesign
  finalizes the minimal verification contract before implementation. No human
  decision or unconfirmed product default is needed. The assigned Plan task has
  `ANTIPHON_TASK_TOKEN`; HTTP reads use it without exposing it. Any downstream
  checkpoint executor retains its own task-token binding when available and
  explicitly reports absence if unavailable.

## Implementation slice and file ownership

| Slice | Owned files | Work and completion condition | Tests / checks |
|---|---|---|---|
| **S1 — Correct D-3** | `docs/superpowers/plans/2026-10-06-card-1076-remote-prep-push-fence-plan.md` | Replace the D-3 rationale with the facts below and attribute the runtime change to S2. Preserve historical evidence, the journal decision, and the rejected alternatives. Commit and push the documentation slice. | Static review of D-3 against the cited source and S2 diff; proposed CP-1 reuses four existing push contract methods in `tests/Antiphon.Tests/Application/RemoteWorktreeMirrorTests.cs` without editing them. |

TestDesign owns this new plan's verification amendment, including the final
`## Verification design` and checkpoint table. S1 owns no production or test
source. Do not edit generated `docs/cards/` files. No sessions or input waits
are added or changed.

Proposed replacement rationale for the old D-3 paragraph:

> CARD-1091 correction (2026-10-12): CARD-1076 S2 removed `-u`; the prep push
> is `git push origin <task-branch>`. It publishes the branch and may update
> `refs/remotes/origin/<task-branch>`, but does not install the desktop branch's
> upstream configuration. The runner mirror receives the branch, SHA, and
> repository explicitly and does not need that upstream. This removes the
> shared config write identified by the S1 review. There is no config-lock
> retry guarantee: a worktree cut from a remote-tracking base can still write
> shared configuration under Git's normal tracking rules. Keeping the tagged
> push journal retains crash visibility and the existing recovery behavior.

Keep D-3's existing rejected alternatives after the corrected rationale. The
correction must not imply that the push cannot fail, that arbitrary Git hooks
cannot write configuration, or that live-process observation proves completion.

## Verification handoff (proposed, not executed)

TestDesign should retain this reduced scope unless a newly fetched master
contradicts the cited findings. The documentation acceptance check is a direct
review of the corrected D-3: it names the no-`-u` command and S2 provenance,
removes the claim of config-lock retry, qualifies the remote-base worktree case,
and preserves the live/dead journal distinction. `git diff --check` must pass.
The old historical ground-truth table remains a record of its original SHA.

The proposed runtime regression group uses only existing Unit methods:

| ID | Existing class.method | Purpose |
|---|---|---|
| R-1 | `RemoteWorktreeMirrorTests.Push_reports_the_head_it_pushed` | Exact no-`-u` push, returned SHA, configured default budget and task tag. |
| R-2 | `RemoteWorktreeMirrorTests.Push_reads_the_desktop_origin_and_the_mirror_request_carries_its_https_identity` | Prep reads and returns the normalized repository identity. This method has two internal origin cases but one executed result. |
| R-3 | `RemoteWorktreeMirrorTests.Push_refuses_when_the_desktop_origin_cannot_be_read` | Failed origin inspection prevents pushing. |
| R-4 | `RemoteWorktreeMirrorTests.Push_failure_reports_and_does_not_invent_a_sha` | Push failure remains a reported unsuccessful preparation. |

These guards do not verify a real config-lock race. If TestDesign finds evidence
that the current explicit push still writes upstream config, measure in an
isolated repository: common-config bytes before/after, actual child argv,
upstream keys, Git diagnostics/exit, and cut/rollback outcome while a tagged
push is held at an acknowledged barrier. Reopen runtime scope from that evidence;
do not add `--no-track` or retries speculatively. No such experiment ran here.

### Lane and execution contract

`GET /api/runner-defaults` (revision 2) and `GET /api/session-runners` were read
on 2026-10-12 at approximately 07:28 UTC. The default resolved to an available,
dispatch-eligible Linux lane; an eligible Windows lane was also present. These
are planning observations, not reservations. Re-read placement at dispatch.
Neither S1 nor CP-1 needs a particular OS or host: omit `-Runner` and `-Platform`.
If an inherited platform pin needs clearing, use `-Platform Any`.

CP-1 uses the **Any / managed Unit lane**, one isolated build, and one bounded
method group. It does not boot the real server, need PostgreSQL, or invoke a live
runner. Estimated ordinary checkpoint time is 5 minutes including the build;
the doc edit/review should take about 10 minutes. No whole-Unit or assembly run.

After TestDesign finalizes the plan and S1 is committed, use
`tools/Antiphon.Checkpoints run --plan <this-plan> --rows CP-1
--expected-source-sha <committed-sha>` through the documented build-slot gate.
The tool bootstrap uses a separate `bin-c1091-tool/` output if it needs building;
report that bootstrap as the explained extra build. Preserve task-token binding,
use forward slashes in output paths, await the run until exit is not 75, and
retain the unedited CHECKPOINT line with source/slot provenance and counts.
Slot timeout is not permission to run unleased. Remove the task's alternate
output directories after completion; generated receipts remain ignored.

### Checkpoints

Proposed for TestDesign; no checkpoint was executed by Plan. The lane is stated
above rather than added as an unsupported checkpoint-manifest column.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1091-push/` | prep-push-contract | `/*/Antiphon.Tests.Application/RemoteWorktreeMirrorTests/Push_*` | R-1, R-2, R-3, R-4 | Exactly the four named methods; 4 executed, 0 failed/skipped | 4 | 5 | true |

## Completion and limits

CARD-1091 can close after the D-3 correction lands with the finalized verification
receipt. The runtime credit belongs to CARD-1076 S2, not to this docs slice.
This plan is not a new clean-source test certificate. Post-land Mutation was
**NOT run**. No production or test code was changed and no build/test was run
by this Plan dispatch.
