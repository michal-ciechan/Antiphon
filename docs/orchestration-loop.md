# The orchestration loop

How a card gets from the board to shipped, using delegates. Written from what actually worked
2026-08-13..16, including the parts that did not. The aim is that this is repeatable without
re-deriving it, and mechanical enough to automate later.

The orchestrator's job is to **decide, verify and record**. The reading, the writing and the running
are delegated. The orchestrator's context is the scarce resource: spend it on judgement, not on
archaeology - and verification is not archaeology's quieter cousin. It is trust, by default.

An **external** orchestrator (ChatGPT / Codex outside an Antiphon TUI) uses a named Delegation
Capability, not `Delegation:AllowedRoots` and not Codex remote control. The operator procedure and
the `delegate.ps1 -Capability` UX live in [ops-http.md](ops-http.md).

## Orchestrator operational autonomy (restart, rollout)

An orchestrator MAY restart or upgrade the AppHost, restart session runners, and run docker-based
server2 rollouts without asking a human. This includes pulling the canonical checkout with
`git pull --rebase`, running `scripts/restart-apphost.ps1`, restarting the desktop runner even
while detached PtyHosts are active (they are re-adopted), the cold first Seed, and the
`deploy-server2.ps1 -Rolling` phases `deploy-temp`, `drain-old`, `redeploy-old`, and `drain-temp`. Restart the
standing server2 runner only through those rolling phases. Follow the
[AppHost restart](apphost-runbook.md) and [staged server2 rollout](docker-stack.md#staged-server2-rolling-rollout-card-0934) runbooks.
During a staged server2 rolling rollout, keep dispatching new work to `server2-temp` until the standing `server2` is fully ready (`redeploy-old` exit 0 and smoke/Both passed); the moment it is ready send new work to `server2` and start `drain-temp` (which waits for temp's running tasks, `-WaitIdleMinutes 240`); there must always be a server2 (standing or temp) accepting work, and the orchestrator never pauses dispatch or drops below its in-flight target (6) because of a rollout. Tasks on `server2-temp` do not block `drain-temp`, it waits for them to settle. Canonical statement: [Continuous dispatch during the rollout](docker-stack.md#continuous-dispatch-during-rollout).
For a desktop runner restart, use `pwsh -NoProfile -File scripts/restart-session-runner.ps1`;
use `-Hard` only for a planned supervisor refresh under the AppHost runbook.
`-KillSessions` kills detached sessions and is human-only, never part of this autonomy grant.

Before and after an AppHost pull or restart, verify that queued lands have finished, no worktree is
half-reset, the canonical source-root `HEAD` matches `GET /api/version` SHA, and `/health` succeeds.
Wait for any land in progress before pulling; never pull while a land runs: the pull fast-forwards
the canonical checkout a land writes to. The server2 rolling phases need no land freeze: lands are
desktop work and the census waits only for a land bound to the runner being recycled (CARD-1353).
After a restart, inspect the
registered child journal. Never use `-AllowWorktree` or touch the user's untracked files in the
canonical checkout. If an intended upgrade makes the before-version differ from the new `HEAD`,
record the old matching SHA before pulling and require the new matching SHA after restarting.
Before an AppHost or desktop runner restart, ensure `logs/apphost.restart.lock` and
`logs/apphost.launch.lock` are clear; AppHost exit 3 refuses the restart, so inspect ownership
and both locks before retrying.

Orchestrators may perform the documented in-container disk cleanup of a **running**
main as the runner user, and remove the retired temp project's volumes. They
must ALWAYS retire temp (`retire-temp`) once scheduling is back on main, under the
[volume recycling policy](docker-stack.md#volume-recycling-and-disk-reclaim-card-1008).
Every drain, zero-work, routing, land, unpublished-work and unreferenced-volume
precondition for volume removal in that procedure must pass. Gate 8 observes exit
and removes only proven owned exited temp containers; always run gate 9
`retire-temp` afterward, including already-absent cleanup. Normal `retire-temp`
also recovers older exited retirement; its pinned offline publication audit and
strict volume proof remain mandatory. Main volume recycling by hand is not
authorized or documented: it is scripted only, to be implemented inside
`redeploy-old` / `deploy-parent` by CARD-1008. Stopping main by hand makes
`Assert-ZeroCounters` refuse with `RunnerCounterUnknown` before deployment.

Still requires a human: `Reset`, Docker prune, recycling runner-state or cache volumes
beyond that policy (main state/cache recycling requires explicit opt-in), deleting
deployment markers or donor tars, any removal while a recycling precondition fails,
and other destructive or irreversible steps outside that grant; deleting other data;
killing other sessions or alwaysOn agents;
changing budgets, routing pins, or settings; spend beyond a sanctioned canary; handling secrets;
touching the user's untracked files; or touching the standing server2 runner container outside
the rolling phases or the documented in-container cleanup of a running main. Pause for explicit human authorization before any such step, except the
operator's CARD-0934 authorization to stop remaining server2 sessions after the staged rollout's
four-hour drain cap, under [that procedure](docker-stack.md#staged-server2-rolling-rollout-card-0934).

---

## 0. What the orchestrator may read, and what it must send out

1. **Trust the report.** The default, every time. A settled task's own report - what it changed,
   what it ran, what passed - is the evidence. Merge on it, close on it, move on. Re-reading a diff
   or re-running a named test "just to be sure" is not diligence here; it is spending the
   orchestrator's context on a question the delegate already answered. The evidence is the
   task's full `Result` (`delegate.ps1 -Status <id>`, or the task drawer Report section). A
   `[task … done]` note may be a distillation of that report (CARD-0330,
   `antiphon-output-distiller`) once `OutputDistillerMode=Apply`; until then the note is still
   the raw text and the distilled bullets live on the task as `DistilledResult`. Either way the
   distilled bullets are a pointer, not a substitute. If the summary is not enough, poll the
   full report — that stamps `FullReadAt` on an Applied ledger row.
2. **Ask the same delegate.** Real reason for concern - the report is vague, contradicts itself, or
   skips something the brief asked for - is answered by going back to the agent that did the work,
   not by reading its diff cold. Reply into the same task asking for the missing detail. It has the
   context; re-deriving that context from the code is the archaeology this whole doc exists to
   stop, just moved one stage later.
3. **Delegate the investigation - rare.** Only when the delegation pipeline itself is broken -
   unreachable agent, a stuck task, something wrong with the pipeline rather than the work - does
   direct reading become the answer, and even then it is a `Debug`/`Plan` delegate's job. "This
   one's quick" is the rationalisation that produced CARD-0246's inline fix (eight reads in ninety
   seconds, then an Edit, a build, a commit and a deploy, all in the orchestrator's own context) -
   the exact thing this ladder exists to make rare.

**The standing rule (CARD-0017)**

Delegate the reading. When you need to know how something works - what a file contains, where
something is called, what shape the data is, whether an endpoint exists - send a delegate and
take its answer. Do not read it into your own context. This holds even when the answer looks one
grep away, and even when the delegate is another frontier-tier agent: your context is the scarce
resource for the whole run, and every file read into it is capacity the run never gets back.
Read directly only what you must quote exactly or must judge personally.

The canonical copy is `server/Bundles/orchestrator.md`, which every sub-orchestrator launch
composes and which a standing orchestrator carries when the `orchestrator` bundle is attached;
this copy exists so AGENTS.md has an owner to route to. A standing orchestrator's register is
`ReplyStyle`, chosen per agent, not prose in its prompt append. A standing orchestrator's bundle
composition is kept current automatically: an idle seat with drifted bundles is relaunched with
`--resume` at its next idle window (CARD-0334), so a bundle edit reaches it without a manual
restart.

For a channel-facing standing agent, `Phone` is an explicit reply-style option for its
human Telegram/Slack summaries, including task completions, checks and scheduled prompts.
The full worker report and stage handoff retain their own contracts; do not put Phone
rules into delegation briefs. AlwaysOn or an Orchestrator name alone does not select it.
See [CARD-0417](superpowers/plans/2026-09-07-card-0417-channel-reply-conciseness-plan.md)
for the required real-reply canary before expansion.

**Also delegated: the landing mechanics.** For a delegated Worktree task, the orchestrator orders
the landing with `delegate.ps1 -Land <id> -ExpectedSourceSha <full-sha>` (optionally `-Verify <filter>` and `-ReviewEvidenceId`); the server fetches,
rebases, verifies when required, fast-forwards, pushes, and cleans up. The resulting
`Landed` / `AlreadyPresent` / `LandedWithResidue` outcome records confirmed remote containment.
Publication and cleanup have separate durable statuses. `LandRefused` leaves publication
unconfirmed and can follow local target advancement. A cleanup retry emits `LandingCleanup`
for the same operation, without another publication. A request survives a server restart.

CARD-1379 durable land selection: the single reader reconciles committed pending
requests before its first selection and before every subsequent selection.
Wakeups are hints, including cleanup retries; a failed or incomplete scan admits
nothing. Ready requests sort by original `RequestedAt`, then ordinal lowercase
request ID. Legacy pending tasks are normalized without changing their age.
A Held request is attempted once per sweep generation, so later eligible work
can proceed; its original ordering key returns at the next periodic sweep.
Snapshots hide provisional entries until reconciliation completes. Entry identity
still prevents an old completion from releasing a newer request's claim.

CARD-1379 verification holds the land-execution lock through publication and
cleanup. The repository mutation lease covers source resolution, preparation,
rebase and recovery pins, then ends together with its Git read-cache scope.
An atomic verification-window marker fences ordinary writers during the build
slot wait and verifier children. Only the owning process's live capability
admits typed full-SHA fetches and distinct ordinary managed-worktree claims;
purpose strings and a marker alone grant nothing. Unknown or unjoined child
evidence still fences. Repair/adoption, SourceLanding, Interim and parked work
retain ordinary admission.

After joined verification and build-slot release, the window closes before a
new mutation lease and read scope are acquired. The protocol reloads durable
authority and validates the immutable candidate ticket, checkout identities,
tracked cleanliness, locks, sequencers, pins, source, approval and destination.
It refuses changed candidates instead of resetting them under an old result.
CARD-1463: the tracked-cleanliness, lock and sequencer refusals
(`verification_tracked_tree_changed`, `verification_sequencer_changed`, the
index-lock codes) apply to the land worktree. The task worktree is probed with the
same read-only inventory (`GIT_OPTIONAL_LOCKS=0`, the CARD-1449 C688 list), but a
finding there is guarded-cleanup residue under CARD-0688 D-2/D-12, never a
refusal: the land publishes and cleanup ends `Refused` with `source_dirty`,
`source_branch_mismatch`, `active_sequencer` or the index-lock code. A task
coordinate, active-operation pointer or status moved during the window refuses as
`task_coordinates_changed`, judged before the lease and pending-operation checks.
The existing remote target observation, push CAS, confirmation and bounded
target-race reprepare path remain authoritative. Completed artifacts do not
make a restarted Prepared operation verified.
The ticketed re-entry keeps the operation's mode: a fresh land settles as `Fresh` on its
terminal `mode=` detail and `LandingMode`; only a fresh entry that finds an operation
mid-flight relabels it `ResumePublication` (CARD-1449).

Generated verification evidence stays ignored (CARD-1015); essential receipt lines and
provenance belong in the stored task report. Ignored output does not dirty tracked source.
Program registers `FileSystemEvidenceRetention` (CARD-1446 S4a): evidence-class ignored files
are copied, byte-verified, under `WorktreeCleanup:RetainedEvidenceRoot` (default
`<Git:WorktreeBasePath>/.retained/<task>/<attempt>/`) before the tree is removed, and an unlisted
`.antiphon` spill is evidence (`**/.antiphon/**`, S4b). Publication can still succeed while
cleanup refuses on a protected name and retains residue. Whole completed-card worktree removal belongs to CARD-1017;
this policy supplies neither that deletion authority nor durable raw-artifact storage.
A 409 with code `land_running` means a land is running in this server now: wait for its outcome
event. For a 409 with another code, read its code and message and follow the route it names; the
table below gives tested examples. A `Warning` "did not finish (server restarted); re-running" is
informational. The orchestrator decides the order and what a refusal means, but does none of those
git operations itself.

| Conflict code | Test (TUnit, Antiphon.Tests) | What it asserts |
|---|---|---|
| `land_running` | `AgentTaskLandApprovalRequestTests.C488_ActiveRequestConflicts` | a second request for a task with a queued land is refused `land_running`; one pending request remains |
| `repair_source_landing_owner_required` | `RepairSourceLandRefusalTests.C603_RepairLandRefusalNamesReviewedOwnerRecovery` | a RepairSource task's land is refused with status 409; no land request or landing is recorded, the event count is unchanged and the queue is empty |
| `review_evidence_subject_mismatch` | `AgentTaskLandApprovalRequestTests.C788_AdoptionSubjectMismatchNamesSourceAndFlag` | an owner-subject Review with `AdoptFromTaskId` is refused, naming `-FromTask <source>` |

**Reviewed owner recovery (CARD-0753).** A Code/Worktree owner that settled Failed or Blocked can
land only through an explicit Clean Final/Full Review of the exact current pushed source tip.
The Review evidence's `subjectTaskId` names the task whose exact current pushed tip was reviewed:

| Operation | Evidence subject | Landing command |
|---|---|---|
| Ordinary owner review | Original Code/Worktree landing owner | `-Land <owner> -ExpectedSourceSha <sha> -ReviewEvidenceId <evidence>` |
| Recover reviewed owner branch | Same owner | Same flags plus `-RecoverReviewedSource` |
| Adopt another eligible task's reviewed branch | Source task named by `-FromTask` | `-Land <owner> -FromTask <source> -ExpectedSourceSha <sha> -ReviewEvidenceId <evidence>` |

The Review's own task ID and `-StartRef` do not establish source identity. The caller's brief
must name both owner and source when they differ. Keep explicit `-Card`: a fresh same-card Review
may name the source. A follow-up Review must name its `FollowUpOfTaskId` as the evidence subject;
commission a fresh same-card Review when an old-owner follow-up conflicts. An adopted source
retains its branch and lands through the original owner. The server validates subject, ref,
repository and SHA,
then pins and aligns the owner source under the repository lease; adoption pushes the owner ref
with an exact `--force-with-lease`. A NeedsResolution request may be superseded by a new reviewed
request; Queued, Held and Running requests cannot. The original task status stays historical.
The request and landing receipt record the old owner remote tip, source relationship, and whether
the reviewed replacement contains each old owner patch; uncontained commit IDs remain visible.
An open land-origin Merge helper with a live session blocks supersession; an inactive helper is
canceled as the new reviewed request takes custody. If a helper fails or is canceled while its
request still needs resolution, the owner gets a Warning and a fresh Conflict notification.
If a reviewed recovery rebases with conflicts, the owner keeps its historical status and the
request remains NeedsResolution. A plain `-Land` or a completed Merge helper cannot turn that
request into an ordinary land; commission a new reviewed recovery after resolving the source.
An unconfirmed previous publication needs its own recovery before another source is adopted.
After publication, a cleanup-only retry uses that operation's saved recovery authority even if
the owner's later task status changes; it cannot publish again or select a new source. Cleanup
does not remove an owner worktree while its task is Queued, Dispatched or Working.

Reviewed owner recovery and adoption prove identity from live worktree registrations
before moving the owner ref and again immediately before resetting its checkout.
After CAS, only identity is checked: HEAD and branch must be the reviewed tip and
the common directory, registered path, Git directory and symbolic ref must match
the pre-CAS sample. A missing, redirected or otherwise unprovable registration
refuses without resetting or repairing metadata. A post-CAS refusal is
`adopt_local_changed`; it keeps the ref at the reviewed tip, the old index/bytes,
durable reset intent and recovery pins. Cleanup also samples live registration;
uncertainty retains the checkout and any already-confirmed publication.

**Repair source (CARD-0499).** When a Code Worktree task must work on a branch that is already
checked out elsewhere, pass `delegate.ps1 -RepairSource <owner-guid>` (full GUID of the original
Code/Worktree landing owner). Antiphon records that owner, routes the repair onto its own unique
branch at the owner's recorded SHA, and attributes a claimed post-dispatch commit on the owner's
ref. `-RepairSource` alone sets no merge target and grants no Land: a repair task's
`-Land` is refused `repair_source_landing_owner_required`. Integrate through the original owner
(or an explicit merge target equal to the owner's branch). A task tagged `-RepairSource` is not
eligible for `-FromTask` adoption. A separate Code/Worktree source started with `-StartRef` can
be adopted after its pushed tip is reviewed; the original owner remains the landing target.
At dispatch, `-RepairSource` checks the owner's desktop ref against origin. A mirror behind origin
fails with `repair_source_owner_remote_ahead`; a diverged mirror fails with
`repair_source_owner_diverged`. Use `-Worktree -StartRef <remote-sha>` for the repair and the
reviewed `-FromTask` adoption route below. If origin cannot be observed, dispatch uses the desktop
ref and records an `owner remote unobserved` warning.
Historical Failed status is preserved.

<a id="repairsource-failed-owner-recovery"></a>
**RepairSource succeeded; owner Failed.** Read the original owner's exact current pushed
full SHA from its origin ref. Commission or reuse an unsuperseded Clean Final/Full Review
whose `subjectTaskId` is the original Code/Worktree owner, with that owner's ref,
repository and full SHA. Then request owner self recovery:

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Land <owner-guid> -ExpectedSourceSha <full-sha> -ReviewEvidenceId <review-evidence-id> -RecoverReviewedSource
pwsh -NoProfile -File scripts/delegate.ps1 -Status <owner-guid>
```

The registered owner checkout must be clean and on its branch. Inspect the status receipt
for confirmed target publication; a queued request is not publication. The original owner's
Failed status, failure code, reason and completion time remain historical. A separate
`-StartRef` Code/Worktree source instead uses an owner Land with `-FromTask <source-guid>`
and a Review whose `subjectTaskId` is that separate source. An actual `-RepairSource`
task is ineligible for `-FromTask` adoption.

<a id="delegate-start-ref"></a>
**Continuing a sibling's work (CARD-0613).** To start a delegate from a commit other than the
default base - continuing an interrupted stage, or picking up where another task's branch got to -
pass the base as a DISPATCH PARAMETER, never as `git checkout -B ...` prose in the goal:

For a fresh Worktree bound to a card, CARD-0442 automatically selects a clean, committed,
quiescent tip from that card's unlanded work in the same local Git repository. The create
response previews the selected task, branch and full SHA; dispatch checks again and cuts a
different task branch at the observed SHA. A pending sibling land holds dispatch. Divergent
eligible tips refuse creation with `worktree_base_ambiguous`; choose one with `-BaseTask <id>`
or explicitly omit the history with `-FreshWorktree`. An invalid, dirty, active or unavailable
explicit source is refused. Inspect the create preview and the dispatch event for changes
between create and launch. Landing still uses the original destination and explicit `-Land`.

```
pwsh -NoProfile -File scripts/delegate.ps1 -Role Code -Worktree -StartRef <full-sha> -Goal <work>
```

(For a long brief, read the file yourself and pass the resulting string to `-Goal`; the script has
no `-GoalFile`.) The task still gets its own task branch, cut at that commit and reported as its
`WorktreeBranch` (see [Delegate branch names](#delegate-branch-names-card-1213)); the named
source branch is untouched and can stay checked out in its own worktree. `-StartRef` accepts a
branch, remote-tracking ref, commit tag or SHA the server's repository can already resolve locally,
or a full SHA origin has (fetched once at create under the repository lease)
- prefer a full SHA. It requires `-Worktree` and is refused alongside `-Shared`/`-ReadOnly`,
`-OnAgent`/`-Agent`, `-RepairSource` and `-SourceLanding`. It is distinct from `-RepairSource`:
that one attributes work committed on ANOTHER task's branch and refuses `-Land`; `-StartRef` only
chooses where this task's own branch starts, sets no merge target, and changes nothing about
landing. Before relying on it, confirm the running build has it (`GET /api/version`) and that the
task detail shows both the requested ref and the recorded base - an older server silently ignores
an unknown optional JSON property.
After a Clean Final/Full Review of a `-StartRef` repair's pushed tip, `-FromTask` can adopt that
branch into the original Code owner's source under the reviewed recovery contract above.

#### Delegate branch names (CARD-1213)

A Worktree delegate's directory is `card-task-<id>`, where `<id>` is the first eight hex digits of
the task id, and the task records its branch as `WorktreeBranch`. With `Delegation:CardBranchNames`
off, a new task branch is `feat/card-task-<id>`. With it on, a task bound to a card whose identifier
is `CARD-<n>` (1-8 ASCII digits) gets `feat/card-<n>-<stage>-<id>` when it has no runner, or when
its runner advertises `workspaceCardBranchV1` both in its registered descriptor and in a live
capability read. `<stage>` is the task's role in lower case (for example `testdesign` or `code`),
or `orch` for an orchestrator. With it on, the branch is still `feat/card-task-<id>` for an unbound
task, a card row that is missing or whose identifier is not `CARD-<n>`, an unknown role, a runner
whose descriptor or live read lacks that exact feature or whose descriptor cannot be read, and
a SourceLanding snapshot. A retried creation adopts the branch already registered for the task's
worktree, in either form. The `-StartRef` and retired-follow-up tests pin `feat/card-task-<id>`
with the setting off only, so read the name from the task's `WorktreeBranch` rather than
rebuilding it from the task id.

#### Landing a `-StartRef` repair (CARD-0675)

A task dispatched with `-RepairSource` can never be that source: `-FromTask` is refused `adopt_source_invalid` while `RepairSourceTaskId` is set, and the accepted route is a fresh Code worktree started with `-StartRef` and no `-RepairSource` that records at least one commit.

Review the repair's pushed tip with the **repair task** named as `subjectTaskId` in the Review
brief. Use a Final round with full ordinary scope; Interim evidence cannot approve adoption. Take
`review-evidence=<id>` from the Review completion header or `delegate.ps1 -Status <review>`.
Then run:

```
pwsh -NoProfile -File scripts/delegate.ps1 -Land <owner> -FromTask <repair> -ExpectedSourceSha <S> -ReviewEvidenceId <id>
pwsh -NoProfile -File scripts/delegate.ps1 -Status <owner>
```

The owner must be Succeeded, Blocked or Failed, never Canceled. The repair must be settled and
not itself landing. Its recorded base must equal or descend from the owner's current remote or
local tip. The owner's desktop worktree must exist, be clean and be checked out on its branch.
`-Status <owner>` shows `Recovery: AdoptReviewedSource` and then `Recovery receipt:` with owner
patch containment; uncontained owner commit IDs are printed when present.

| Refusal | Action |
|---|---|
| `adopt_source_lineage` | Cut a new repair at the owner tip and Review it. |
| `source_dirty`, `source_branch_mismatch` | Inspect the owner's registered checkout, branch, index and working files. Preserve real edits; resolve the cause before a new request. |
| `review_evidence_subject_mismatch`, `review_verification_scope_ineligible` | Review the repair itself in a Final/Full round. |
| `adopt_source_remote_changed`, `adopt_source_push_rejected` | Inspect the owner remote tip and request a fresh reviewed adoption. |
| `source_remote_diverged` | Plain land refused the diverged mirror; use `-FromTask` as above. |

A fresh explicitly reviewed recovery can finish a prior half-reset only when the prior request's
durable adoption intent and L/S pins identify the same owner, worktree, repository, source and
remote endpoint. The index and actual tracked bytes must still equal the pinned old tip, with no
untracked non-ignored files or unsupported checkout transforms. Unrelated ignored residue
(`.antiphon/inbox`, `bin-*/`, `obj/`) is permitted. An ignored path is refused as `source_dirty`
if it matches a path the reviewed tip S tracks, is an ignored file or symlink at a parent of an
S-tracked path, or lies below an S-tracked file. An ignored independent nested Git repository
(listed by Git as `dir/`) is admitted only with a real, non-reparse `.git` directory,
positive outer ignore evidence, no outer tracked content and no reviewed-path collision.
File-based administrative pointers and unknown evidence still fail closed as
`recovery_checkout_unproven`. Publication cleanup and retirement preserve directories
with their own `.git` entry, even beneath disposable ignored output names: the owner tree,
registration and branch remain with `ignored_content_preserved`. A repository discovered
after unregistering is preserved in the recorded set-aside tree; later resumes preserve it
there too. No private Git command executes. This strict proof is implemented by
[`LandingGit.InspectRecoveryCheckoutAsync`](../server/Infrastructure/Git/LandingGit.cs), with
residue and obstruction coverage in
[`AgentTaskLandHalfResetTests`](../tests/Antiphon.Tests/Application/AgentTaskLandHalfResetTests.cs)
and [`LandRecoveryCheckoutTests`](../tests/Antiphon.Tests/Infrastructure/LandRecoveryCheckoutTests.cs).
An unknown proof remains
`source_dirty` with `recovery_checkout_unproven`; do not reset the owner tree based only on HEAD.
The repair never runs for ordinary land or cleanup-only requests. The request's
`RecoveryWitnessRequestId` records the prior intent that authorized a successful repair.

A delegate that nonetheless ends up off its own branch is no longer settled as a false failure.
Antiphon reads the registered checkout's actual HEAD and accepts a task-scoped claim line
(`[antiphon-progress:<full-guid> commit=<full-sha>]`, on its own unquoted line) reachable from it,
or a task branch reset into a divergent lineage, as `PrimaryAlternate` progress. That prevents the
wrong verdict and NOTHING else: the branch is left for review, nothing merges back, and the
completion note says `progress=primary-alternate`. Integrating that work is still an explicit,
human-ordered step.

**Post-land server activation check (CARD-0495).** A land confirms publication, not
server activation. Before relying on newly landed server behavior, record the landing
receipt's verified commit, confirm the canonical checkout contains it, and check the
running API's `GET /api/version`. A client that expects `land-v2` POSTs only
`/api/agent-tasks/{id}/land/v2` and refuses (exit 1, zero land POSTs) when the
marker is absent; it never falls back to `/land`. If the build does not demonstrably
include the required change, and the landed commit is the rc tip those hosts run,
restart onto that tip with it as `-ExpectedServerSha`, then confirm the reported SHA
and a direct capability/feature probe. A master-only land is not that restart. Do not treat `/health`, a runner SHA, a pushed branch, or
a succeeded delegate as activation evidence. Use the landing receipt's post-rebase
identity, not an assumption that the Code worktree SHA survived landing unchanged.
On every terminal card move or close, including UI/API moves and tracker closes that
bypass `card.ps1`, perform this activation check as an operator step. A successful
card move and task settlement do not verify deployment. AppHost and server2 run the current `rc/YYYY-MM-DD` rc tip, never master. A
master-only land is not a restart. When a restart is due, compare the running
`GET /api/version` SHA with that rc tip, and probe the changed feature. See
[release gates](release-gates.md). For runner or
fake-gateway changes, run `pwsh -File scripts/check-daemon-build.ps1` from that
checkout, restart or redeploy only the affected service when indicated, and recheck
its reported build. A remote worktree's own HEAD is not a deployment target.
An already-running descendant build is sufficient when local ancestry can be
established and the required feature/capability is present; unknown/unrelated build
history cannot establish activation. Record desired and observed full SHAs in the
deployment report. After out-of-band publication, update the canonical checkout
using the existing runbook before choosing the intended deployment HEAD. Executable
procedure: [apphost-runbook.md](apphost-runbook.md). A new enum member (a role, a workspace mode) is a
server capability exactly like `land-v2`: the script accepting it proves nothing about the served
build.

After a restart, a phone-home runner still ineligible after `Alarms:RunnerGraceSeconds`
raises a `RunnerUnavailable` attention row and queues notes to its callers; no separate
session-scoped watch loop is re-armed.

**Also automatic: what a stage run found.** A land op writes its own `StageOutcome` rows with no
orchestrator action (§5). A Review/Test/Merge/Deploy delegate — or any dispatch given `-Stage`
(§3) — is asked to end its report with a one-line `[antiphon-finding:<id> found|clean]` self-report
instead; trusting that line is the same rule as trusting the report itself, and overriding it
(`delegate.ps1 -Finding …`) is rare enough that it should feel like an exception, not a habit.

Since CARD-0247, a `PreToolUse` hook in this repo nudges at the third consecutive cold source read
(it never blocks; `ANTIPHON_ORCHESTRATOR=0` silences it for a hacking session) - the hook is the
backstop at the third read; the rule is the bundle's - and a server sweep records each run as an
`OrchestratorInvestigation` Warning on the attention feed. A row there is not a fault to fix in
the code - it is a habit to fix in the next brief.

---

## 1. The cycle (CARD-0146)

A pipeline stage IS an `AgentTaskRole` — `Investigate`, `Plan`, `TestDesign`, `Code`, `Mutation`, `Review`
(`AgentTaskRoles.IsStage`). Every stage-role report closes with a fixed
`--- next stage ---` block (`next:` / `handoff:` / `artifact:`, D2 in the plan) instead of prose;
settlement parses it onto `AgentTask.NextStage` / `NextHandoff` and the completion header carries
`next=<token>`. The orchestrator reads that bit and dispatches the named stage — it does not
re-derive "what happens now" from the report body:

```
pick a card
  ▼
Investigate  ── skipped when the root cause is already diagnosed
  │ next: plan | investigate (unconfirmed) | decide (several live hypotheses) | none
  ▼
Plan  ── never skipped
  │ next: test-design (hard/medium default) | code (easy — the ## Verification design
  │        section is required IN the plan doc when this stage folds) | decide | investigate
  ▼
TestDesign  ── separate dispatch for hard/medium; folded into the Plan dispatch for easy (D4)
  │ next: code | plan (design as written can't be verified) | decide
  ▼
Code  -- ordinary V/R, commit/push, PCs pending (including zero PCs)
  | next: review | code (ordinary work remains) | decide
  v
Review -- separate ordinary read-only gate at every complexity
  | next: land | code (defects) | decide
  v
record companion -> -Land <original-code-task> -> confirmed O/L -> required deploy
  | original may close Done with verification pending
  v
Mutation -- fresh SourceLanding Worktree on companion; every PC/variant and discovery
  | next: none (clean) | decide (completed finding); incomplete is failed/blocked
  v
explicit companion disposition and guarded CleanupVerification
```

`next: land` names the original Code landing owner for `-Land` — see §5 and CARD-0478 below. Mutation findings using `next: decide` first get explicit triage, not an automatic human question. `next: decide` means the artifact already exists
under stated defaults; take the `## Decisions` section straight to `AskUserQuestion`, the task is
**done**, not blocked. A stage report with no block still settles — `next=unmarked` — and the fix
is to send it back to the *same* delegate (§0's ladder), never to read the diff instead.
`Debug`/`Test`/`Coverage`/`Docs`/`Commit`/`Deploy`/`Merge`/`Custom` are **helpers**, dispatched
*inside* a stage (a `Debug` for a red test during Code, a `Docs` slice); they carry no stage
bundle and the block is optional for them (§2).

Landing (fetch, rebase, verify, fast-forward, push, worktree removal, branch deletion) is the
`-Land` operation's job, not a manual step — see §5. The rc-to-master merge commit is not this
operation; it is manual ([release gates](release-gates.md)): a `Merge`-role task started at the
target tip that pushes its merge as a fast-forward, see
[Merging the rc line into master](release-gates.md#merging-the-rc-line-into-master-merge-role). The fleet's stage glance — one line per card,
in-flight / queued / ready per stage, generalised across all six stage roles (CARD-0146 S4) — is
`/orchestrator?tab=pipeline`.

A Worktree task records the ref it was cut from. Precedence is repair SHA, then an explicit
requested ref, then the merge target, then the project's `BaseBranch` / `Git:DefaultBranch` /
`master` when that ref resolves to a commit, then `HEAD` with a warning naming the failed default
(CARD-0508 S1). CARD-0442 selects an eligible same-card unlanded tip ahead of the ordinary
default when no repair, StartRef or SourceLanding base is requested. Containment of a kept sibling is patch-aware (`git cherry`): a
rebase-landed branch is silent. The dispatcher still holds while a sibling land is in flight, and
still warns when a divergent kept branch is simply not landed. CARD-0540 snapshots full sibling
commit IDs and emits one warning per surviving observed tip: identical tips share a deterministic
representative, strict ancestors are covered by containing tips, and divergent tips remain separate.
Unknown tips or failed ancestry probes retain visibility. Any original eligible sibling's landing
hold takes precedence over this reduction. The warning names the representative's full observed SHA
and counts other covered branches; it confers no cleanup or publication authority. That warning is a durable
dispatch-base obligation (`AgentTaskLandNotifications.Kind = DispatchBase`, null `RequestId`)
captured with the successful claim; its absence from the parent session is a defect, not an
expected loss. The reduced identities, body and route freeze in the claim transaction; recovery
delivers those original obligations without regrouping moved refs. Historical intents are unchanged.
A sibling holds dispatch only while its current request is pending in Queued, Held or Running;
NeedsResolution emits the ordinary warning. An explicit StartRef that equals or descends from
the request's reviewed source SHA bypasses that sibling check even if a Merge helper later
rewrites the sibling's local tip.
Land a Plan with `delegate.ps1 -Land <id>` when the plan should be published immediately;
a same-card Execute can continue its committed branch before that land. A `Landed`
line carrying `unlanded-sibling=` means a same-card branch is still stranded; land or drop it. Two 2026-08-10
cases (the CARD-0002 design doc and the CARD-0001 fix) sat unmerged for 9 hours before anyone
noticed. An inspection-unknown Warning names a branch whose containment could not be established.
Inspect that history and decide whether it needs integration; the warning alone does not prove omission.

**Dispatch hazard: a card with many kept sibling branches (CARD-1349).** The desktop dispatch
tick is one serial loop, and before it cuts a Worktree it walks every kept sibling branch of the
card (`EvaluateCardSiblingBaseAsync`: `merge-base`, `rev-list` and `cherry` per sibling, each
under the 5-minute `LandingGit` budget). On 2026-10-19 CARD-1311 had 64 such siblings and that
walk took 13–18 minutes per task; every later task in the tick stayed Queued meanwhile, including
rows whose runner mirror was already recorded, because the deduped "remote workspace preparation
is in flight" hold is not rewritten until the tick ends. Nothing was stuck on server2, the preps
themselves took 14–26 seconds, and a restart only restarts the walk. Until the walk is budgeted
or batched, bind release and rollout work to a fresh card rather than a long-lived card with many
landed-and-kept branches, and read a long `Queued` as "whose sibling walk is the tick inside"
before touching a runner ([remote-prep stall](investigations/2026-10-19-server2-remoteprep-stall.md)
on `feat/card-task-3f531f98`).

### Picking

Lowest `rank` first — the formula already prefers a card that **changes how everything else gets done** over one more feature.
Prefer a card whose plan already exists — but check properly, see below.

<a id="standing-pipeline-policy"></a>
### Standing pipeline policy: four per stage, six on server2, read limits first (CARD-0533)

**This is the orchestrator's default for working the Antiphon board.** It was the operator's
standing instruction on every overnight run from 2026-09-13/14 through 2026-09-18/19, updated by
the operator's 2026-10-01 ruling, and is recorded here so a fresh seat starts from it. The user's words in a
session override it for that session; nothing below needs restating to apply. The delivered copy
is the `orchestrator` bundle; this section carries the reasons.

1. **Each pipeline stage at up to four; at most six tasks on server2 across all stages.** These
   are the operator's defaults. Before dispatching, read the effective-settings route
   (CARD-0881): `GET /api/agent-tasks/pipeline?projectId=<id>` (or `?unscoped=true` for the null
   bucket). Its single `concurrencyScopes` entry carries the absolute and each stage's effective
   limit with its source (`default` is the imported seed; `global` and `project` are runtime
   overrides), the `open`/`parallel`/`queued` counts and the nonnegative `parallelRemaining` and
   `queuedRemaining` slots; `hosts` carries each host's `effectiveLimit`, `configured`, `declared`,
   `source` (`budget`, `config` or `runner`), `inFlight` and `remaining`; `runners` carries
   capacity, occupied seats, `dispatchEligible` and `acceptingNewWork`; `runnerDefaults` carries
   the global and per-kind runner defaults (there is no platform default). Use its limits:
   Antiphon's enforced limits are the ceiling, so use the lower effective stage cap: the lower of four and the enforced stage limit
   applies. Run Investigate, Plan, TestDesign, Code, Review and Mutation in parallel, each task in
   its own `-Worktree`, never more tasks in one stage than its cap. Prefer server2
   (`-Runner server2`) while it is eligible; shield the desktop/Windows machine by using it only
   when work absolutely requires it (a Windows-only test row, native ConPTY or desktop-only
   behaviour), scoped to that piece, as in CARD-0778 CP-3 and CARD-0801 W-1. A
   `PUT /api/hosts/server2/budget` with `{ "maxInFlight": <n>, "reason": "<why>" }` can
   hold new dispatch, but is an operator-only setting; the orchestrator does not write it on its
   own initiative. The orchestrator still keeps its six-task server2 default when no lower host
   budget is enforced. During a staged server2 rolling rollout, hold that target of 6 by
   dispatching to the accepting runner in [continuous dispatch](docker-stack.md#continuous-dispatch-during-rollout):
   `server2-temp` until `redeploy-old` exits 0 and smoke/Both has passed, then `server2` as
   `drain-temp` starts. Live operating settings: read the file at ANTIPHON_ORCHESTRATOR_INSTRUCTIONS
   (or GET /api/orchestrator-instructions) at session start, after compaction and when a
   settings-changed note arrives; it outranks the numbers in AGENTS.md.
2. **On every completion, dispatch the named next stage.** Read `next=` and `handoff:` off the
   completion header (§1, CARD-0146). `next=unmarked` goes back to the same delegate for the
   missing block (§0's ladder); it is never guessed from the diff.
3. **Land promptly.** Once a stage's work is confirmed — a Plan or investigation artifact, a
   reviewed Code branch — run `-Land` then (§5), not at the end of the night. Unlanded work is
   how the 9-hour strandings under "Is there a solid plan?" happened.
4. **Keep the Code stage at its depth cap.** The depth cap is four unless Antiphon enforces a
   lower stage limit. Count Code rows that are in flight, queued or `ready` (a settled Plan or
   TestDesign whose `next:` is code) on the pipeline snapshot. Below the cap, pull the next
   unstarted Backlog card with no open blocker — lowest `rank` first, as under Picking — and
   start it through Investigate/Plan toward Code. `investigateBacklog.items` leaves a card with an
   open blocker out; `investigateBacklog.blocked` counts those held cards and `blockedItems` lists
   up to five with their `blockedBy` identifiers (CARD-0100,
   `AgentTaskPipelineStatusTests.C100_investigate_backlog_omits_blocked_cards_and_lists_them_as_blocked`;
   rules in [agent-card-lifecycle.md](agent-card-lifecycle.md#blocked-by-another-card-card-0100)). At the cap, start no new Plan toward Code;
   Investigate, Plan and TestDesign for cards already in the pipe still run. This is CARD-0146
   D7's "planning should only stop if more than 1 card waiting to execute", with its pull side
   stated as well.
5. **Same source area as an in-flight Code task: defer, even with a free Code slot.** A Worktree
   task whose scope intersects a running one is warned, not held (CARD-0063, preserved detail
   below), so nothing stops multiple Code tasks editing the same file and conflicting at merge. When a
   card's plan names the same file or area as another card's in-flight Code task, hold its Code
   dispatch until that task lands, and say so on the card thread. CARD-0537's plan (2026-09-19)
   sequenced its first slice behind CARD-0535's Code task for exactly this reason; the deferral
   costs an hour, the conflict costs a Merge task and a second Review.
6. **File a card the moment a structural defect is found.** Investigate and Review turn up bugs
   outside their card's scope; each one gets its own Backlog card now (`card.ps1 new`, with the
   evidence), never a note to batch later. A finding that lives only in a chat reply or a report
   body is gone at the next compaction.
7. **`-IgnoreConcurrencyLimit` answers the absolute cap, never a same-stage collision.** A 409
   `concurrency_limit` carries `axis` (`absolute` or `role`) and the `open` occupants with their
   roles; the absolute axis wins the report when both caps are exceeded, so read the list, not
   only the axis. `axis: role`, or any listed occupant in the role you are dispatching: defer —
   that is rule 1, and the flag would lift the per-role cap along with the absolute one.
   `axis: absolute` with no occupant in that role: re-send with `-IgnoreConcurrencyLimit`,
   because rule 1 is the standing request for cross-stage parallelism. CARD-0505 adds
   `population` and `canOverride`: the flag is legal only when `population` is `open` and
   `canOverride` is true. A queued population, `canOverride` false, or a same-stage collision
   never justifies it. Read `GET /api/agent-tasks/pipeline?projectId=` (or `?unscoped=true` for
   the null bucket) and `scripts/dispatch-concurrency.ps1` for the effective mode, sources,
   revisions, and open/parallel/queued counts. The shipped role defaults stay Code 2, Review 2,
   and other stages 1; the operator's four-per-stage and six-on-server2 instruction still bounds
   commissioning. An API ceiling is not permission to spend up to it.

Create-time per-project and per-role gates run before host admission (CARD-0654). Passing
those gates can still leave a task Queued when its assigned host is at its dispatch budget.
The pipeline's `hosts` summary shows each host's current count and effective limit;
`inFlightAgainstCap` counts local work only. Runner seats and the local budget are separate.
Raising a host budget releases eligible held work on the next dispatch tick. This round keeps
the existing explicit/default/local placement order; local overflow from a full default runner
is a follow-on placement policy.

### Keep-six in-flight reminder (CARD-1320)

The in-flight target is a server setting, one row per standing orchestrator, default off.
`GET /api/orchestrators/keep-six` lists it, and section 9 of the orchestrator instructions
renders a row only when that row is enabled or held. A reminder names the shortfall. Dispatch
up to that shortfall under the same pipeline rules, or declare a hold with
`scripts/keep-six.ps1`. The server does not dispatch.

<a id="standing-operating-rules"></a>
### Standing operating rules (operator, 2026-10-22; CARD-1469)

The operator's standing rules for every orchestrator seat, in full. The `orchestrator` bundle, AGENTS.md and the antiphon-orchestrator and antiphon-delegate skills point here and restate nothing; numbers that the generated instructions file owns (caps, host budgets, pins, the keep-six target) are referenced, never copied.

1. **Failures get a Debug task.** A failed dispatch, launch or infrastructure failure (`[task ... failed]` with a launch or save error, `Agent process did not become ready`, `Process exited`, `Dispatch failed before a session existed`) gets a Debug delegate immediately, briefed with the exact error text, times, task ids and what else was in flight; one retry is allowed as first aid so the in-flight target holds, but a retry is never the whole response. Search the board before filing the fix card; when the operator asks for Codex Astra debug tasks, dispatch them with `-Kind Codex -Level Frontier` and raise the Debug role cap through `scripts/dispatch-concurrency.ps1 set` with the operator's words as the reason.
2. **Keep the in-flight target on the landing host.** The target counts tasks running on server2 that land onto master (section 9 of the generated instructions carries the live number; the operator said 7 on 2026-10-22), and desktop-only Windows work such as qualification monitors or CP-6 style runs is extra. Refill to the target after every completion; when the project cap is lower, raise it through `scripts/dispatch-concurrency.ps1 set` with the operator's words as the reason rather than exceeding it with `-IgnoreConcurrencyLimit`.
3. **An Interim Review cannot approve a land.** When the plan's closed checkpoint list holds a row that cannot finish on the loaded host (a whole Unit lane), have the Code owner amend the plan first, descoping the row with the reason and the minimal-testing rule below and recording inherited reds as documented non-receipt rows, so that scope Full is reachable before the Final Review is commissioned.
4. **Review verdict policy, no whack-a-mole.** `found` is for a regression, a new reachable fail-open or a test that cannot fail. After two rounds of example-by-example repairs, change the design (default-deny, one canonical pipeline or normaliser, a table with a witness per row) and tell the Review to report a new bypass class with one witness, never more examples of a closed class.
5. **Verify the card binding.** Read the card title before binding a task to a card identifier (a task was once bound to the wrong card from memory); when no card fits, file the card first.
6. **Do not Reply to a complete report that is Blocked only by runner sync.** A task whose report is complete and whose only block is `runner_sync_lease_busy` or a pending desktop sync needs no Reply, even when the Blocked settlement's warning asks for one (CARD-1082; CARD-1472 fixes the server side): Review and `-Land` use the pushed branch, and the settlement sync sweep clears the block. A Reply reopens the task as a new model turn, and a token-only answer then replaces the retained report with an empty result (Debug fc28850a, [docs/investigations/2026-10-22-report-contract-and-first-prompt-noise.md](investigations/2026-10-22-report-contract-and-first-prompt-noise.md)). Reply only when the report is genuinely missing, and then ask for the full report with its closing token, never just "stop".
7. **Master lands are not activation.** AppHost and server2 run the rc tip, so a land never needs a restart by itself; restart only when the rc tip changed (the post-land server activation check in section 0).
8. **Minimal testing.** One red witness plus one narrow green per task, with the rc roster as the gate; no whole-Unit or whole-lane runs from Code or Review.
9. **rc ports.** Bug and test fixes for code already on the rc line go to master and to the rc line (a reviewed cherry-pick that makes a new rc tip); feature work is master-only. Check that the rc carries the prerequisite chain before porting (CARD-0856 found the rc lacked 27 CARD-1427/1428/1432 commits). Startup-path picks also require the exact-candidate native probe before publication and the post-push restore rehearsal in [Startup-path rc picks](release-gates.md#startup-path-rc-picks); use the [forward recovery](apphost-runbook.md#failed-startup-path-pick) if activation fails.

10. **Test performance (operator, 2026-10-23).** Apply [test performance standards](test-performance-standards.md). Dispatch a Debug task, with exact selection, source/host/run identity and timing evidence, for any row, batch, selection or run expected or observed over 30 minutes; a retry alone is insufficient. Routine speedups and preflights need no further permission. After establishing ownership and retaining evidence, an orchestrator may stop an abandoned/uncreditable qualification through its supported stop path and join cleanup without asking; this grants no authority to kill another session or bypass custody. Before the next full rc run, require instrumentation ready to record per-test time, exact batch membership and phase attribution (CARD-1514). Destructive/one-way/secret/publish/promote/tag actions retain their existing authorization requirements.

#### Operator standing instructions (2026-10-23)

The operator's wording is preserved here; [the numbered standards](test-performance-standards.md)
turn it into design and review obligations:

> For testing, if it is more than 30 minutes start debugging. Are there slow sections of the app we can move out to their own projects and selectively run when only those change. Make sure we set up AppHost and the database ONCE PER RUN rather than per test or per class. Speed up setup by snapshotting databases and things so we do not need to do everything on every single test launch or test class/run. We need thorough observability in tests (and the live app) to understand how much time is spent on setting up, including spinning up and waiting for docker or similar processes, setting up / prepping data, spinning up the web host, migrations, various other setup, actually running the tests, and teardown. Are timeouts aggressive enough? If a test takes 40 s we should not have a timeout longer than 2 minutes (approx double). We need to start recording in a test where time is going, especially around external calls (db, io, http, cli, other external). Make sure this is committed and used as part of our design/review etc.
>
> This is a monorepo: make sure we have categories/apps per batch so that if something in the messaging layer changes that batch runs, server, Claude runner, Codex runner, shared pty runner, git operations handler (should be its own csproj), so we can scope future rc runs to only the projects that actually need testing (including downstream of those projects).
>
> before the next full rc run record time per test, which tests are in each batch, and where time is going.
>
> we want to speed up releases, tasks and testing so do what is needed (do not ask for permission for routine actions).

Generated instructions section 8 reads `Delegation:OrchestratorInstructions:StandingInstructions`
(`OrchestratorInstructionsSettings`, startup `IOptions`): at most 10 lines of 300 characters.
There is no add-standing-instruction API/script; refresh only re-renders the loaded settings.
The orchestrator owns machine configuration (see [bootstrap overlays](bootstrap.md#3-secrets)),
preserves existing lines, appends a short owner pointer within those limits, and activates it
under the restart runbook before checking `GET /api/orchestrator-instructions` section 8.
A Docs task reports the exact proposed configuration command/text; it never writes live settings
or edits the generated file. The full wording stays here, outside section 8's size cap.

### Post-land miss retrospective (CARD-0811)

Rule 6's post-land sibling. A defect that a Clean Review approved and that was found only in the
running system gets, alongside its ordinary fix card, a retrospective that records why the
pipeline missed it and a Low-tier first pass at the instruction change that would have caught
it. A defect Review or Mutation caught before land is the pipeline working; it is not a retrospective,
so do not fire this for every bug fix.

**Trigger: all five hold, each read from the API now, not from memory.**

1. The fix card names the landed card (`CARD-nnnn`) and the observation: who saw what, where
   (operator report, live incident, a Debug or Investigate finding against the running system)
   and when.
2. `GET /api/stage-outcomes?cardId=<landed-card-guid>&stage=Review` returns a latest row with
   `outcome: Clean`, a `reviewedSourceSha` (C) and `supersedesId: null`. A latest `Found` row
   means the pipeline caught it before land, or this retrospective already ran; stop.
3. `GET /api/agent-tasks/<subjectTaskId>` from that row shows `landing.publication` `Landed` or
   `AlreadyPresent`, `landing.verifiedSha` (L) and `landing.remoteConfirmedAt` (T).
4. The observation is later than T and was made against a running build whose
   `GET /api/version` SHA contains L (`git merge-base --is-ancestor <L> <version-sha>`). A
   defect seen only in a worktree, on an unlanded branch or before activation is not a
   retrospective.
5. The landed card's `Post-land verification:` companion (CARD-0478) does not already report the
   same defect from Mutation.

**Record it before dispatching anything.** Create or discover one same-board Backlog companion:
title `Post-land retrospective: <landed identifier>`, label `post-land-retrospective`, stable key
`post-land-retrospective:<review-outcome-guid>` in the description. Search the board's Backlog,
In Progress and Done cards client-side for the key before creating (`GET /api/cards` needs
`boardId` and `status`); the key is discoverable text, not DB uniqueness, and one Review outcome
gets one retrospective. Record the landed and fix card GUIDs, the Review task and outcome GUIDs,
C, L, T, the observation source, `investigation pending` and `override pending`. Exclude the
companion from feature picking; never Spawn it.

Then override the Review finding so `scripts/stage-value-report.ps1` counts the miss:
`delegate.ps1 -Finding <review-task-guid> -Stage Review -Found "post-land miss: <fix card> <one
line>"`, but only when `landing.cleanup` is `Complete`. The override supersedes the Clean row, and
a later `-Land` retry for residue or cleanup loads evidence by ID and refuses a superseded row
(`review_evidence_superseded`); while cleanup is `Pending` or `Refused`, leave `override pending`
on the companion and record the override after the cleanup retry lands.

**Task A, the retrospective: `-Role Investigate -Worktree -Card <companion-guid>`, role-default
tier.** Evidence only, no fix design; the fix card owns the fix. It writes
`docs/investigations/<date>-card-<landed nnnn>-post-land-retrospective.md` with exactly these
headings: `## Defect` (what the running system did, from the observation); `## What passed it`
(the Review report's evidence lines, the plan's V/R rows and the checkpoint lines that covered the
changed path, quoted with task and outcome GUIDs); `## Why it was missed`, exactly one of
`not-designed` (no V/R row covered the case), `designed-not-asserted` (a row named it but its
test did not assert the outcome), `asserted-wrong-layer` (asserted below the layer that failed),
`bundle-gap` (the reviewer followed the bundle and the bundle does not ask for it),
`reviewer-deviation` (the bundle asks for it and the report shows it was skipped),
`environment-only` (reproducible only with the live host, data or provider), with the evidence
for that one; `## Not done, noted`. It closes `next: none`. The brief carries every GUID from the
companion, the plan path and the Review task's report (`GET /api/agent-tasks/<review-task-guid>`).
Land it.

**Task B, the instruction-gap pass: `-Role Docs -Kind ClaudeCode -Level Low` (or `-Kind Codex
-Level Low`) `-Worktree -Card <companion-guid>`, after Task A lands.** Input: the retrospective
file and its `## Why it was missed` line. Output: one appended section
`## Instruction-gap proposal (Low tier)` in the same file, at most 25 lines, opening with one
verdict, `no-instruction-change`, `doc-change`, `bundle-change` or `escalate`, then for a change
the exact file, the sentence to add and, for a stage bundle, the sentence to remove so the file
stays within its 2,480-character cap. Task B never edits a bundle, a test, or any doc other than
its own section; a `not-designed` or `environment-only` miss usually reads
`no-instruction-change`. Land it.

**Disposition: yours, one action, no new column or alert.** `no-instruction-change`: close the
companion Done, naming the classification and the file. `doc-change` or `bundle-change`: file one
Backlog card `Instruction gap: <one line>` with the proposal as its description and label `instruction-gap`, linking the retrospective, then close
the companion naming it; the ordinary pipeline's Plan judges the proposal, not you. `escalate`: move the companion to NeedsDecision
with the question on the move revision. Never apply a Low-tier proposal to a bundle directly:
bundles are size-capped and test-pinned and change through Code and Review like any file.

### Reprioritising the backlog

An agent that can call the API — `delegate.ps1 -Role Custom` (or Plan), or a `ScheduleKind.Prompt`
schedule — writes order through the same endpoints a human drag uses. Nothing runs on the
orchestrator tick. Tracker writes stay explicit.

1. Read `GET /api/cards?status=Backlog&boardId=<b>` and, for cards it intends to move,
   `GET /api/cards/{id}/thread` for the plan/task context.
2. Leave `importanceProvenance: Human` ratings alone unless the reason says why
   (`overrideHumanRatings` stays false; the response lists what was skipped).
3. Prefer one `card.ps1 order` with an ordered file and one reason over N `reorder` calls; put
   the argument for the order in the reason.
4. Never move a card between columns; reprioritising is not dispatching. A card in an active
   column is the tick's business.
5. Report the before/after top-ten in the delegate report so the operator can undo from history.

### "Is there a solid plan?"

Two places, and the second is the one people forget:

1. `docs/superpowers/specs/` — but **read the date**. A plan written before a big refactor may name
   files that no longer exist. CARD-0019's client section targeted a board page that CARD-0042 had
   replaced two days earlier; it had to be replanned.
2. **Unmerged branches.** Plans get stranded. Run this, not `git branch -r`:

   ```bash
   for b in $(git branch -r --list "origin/feat/*" | tr -d ' '); do
     git cherry master "$b" | grep -q '^+' && echo "UNAPPLIED: $b"
   done
   ```

   `git cherry` compares by patch-id, so it distinguishes "rebased and landed under a different
   hash" from "never landed". On 2026-08-16, 8 of 10 unmerged branches were already applied and
   exactly 2 held real work — including a 187-line plan for an open card.

A plan is **not** solid if it predates a refactor of the area, assumes an API that shipped
differently, or is under ~20 lines for a feature. Replanning costs ~$5; implementing the wrong thing
costs an hour and a merge.

---

## Windows Review and Debug routing

**CARD-1011 published policy.** The prompt change landed at `8bb0cea0` after
WQ-2 and amended WQ-3 were met; WQ-1 was operator-excluded for CARD-1022's
legacy inbox deprecation. The Debug pin was updated on 2026-10-04; fresh
effective pin reads that day confirmed the ordered pair below for both roles.
WQ-4 remains post-activation acceptance work. Re-read effective pins for current routing.

Policy on release: Review and Debug on every platform, including Linux, follow
the same policy; use Human Required pins with Grok/High then ClaudeCode/High (`grok-4.7`, then `opus`).
The operator approved this role-wide Debug scope (D-1); pins have no OS key.
Preserve unrelated Human card exceptions. Queued tasks retain their recorded selection.
The role-tier table below is fallback policy; an effective Required pin precedes it.
Grok orchestrators remain unsupported; this policy concerns their worker dispatches.

OS placement is independent of the provider pair. A Windows requirement uses
`-Platform Windows`; otherwise omit `-Runner` and `-Platform` and read runtime
defaults/inventory. `-Platform Any` removes an inherited platform constraint.
Normally omit `-Kind` and `-Level` to preserve ordered availability walking.
OS needs do not authorize `-IgnoreRoutingPin`. A held/unavailable Grok candidate
can walk to the listed Opus candidate. An exhausted walked list follows normal
host selection, including Any defaults, and persists a durable Blocked task on
the selected eligible host (CARD-1021). It starts no session. The response carries
a task ID and routing warning; track that task and its grouped attention instead
of resubmitting or choosing an unlisted provider. `-RefuseIfExhausted` retains
the opt-in 409 `routing_exhausted`; independent invalid or unavailable placement
still refuses before insertion. Recovery may use only the approved list and
preserves the task's frozen host and platform requirement.
Tests seed this policy only in isolated databases, never production.

### Evidence gates

Actual ModernConPty requires real Review evidence; WQ-1 is excluded by the operator for CARD-1022.
A fresh worktree must reach Ready. Record whether trust appeared; send no startup input without observed trust, or exactly one y if it appeared, then require cleared trust and Ready.
Delivery requires a matching complete UserPrompt transcript.
Settlement requires a final report and confirmed release ownership.
WQ-2 and WQ-3 gate prompt landing and Debug pin activation; WQ-1 is operator-excluded.
The [qualification ledger](investigations/2026-10-03-card-1011-windows-grok-qualification.md)
records WQ-2 and amended WQ-3 as met, WQ-1 as excluded (not passed), and the full
canary identities; WQ-4 remains pending. Backend configuration must be
restored and the actual restored host verified. Configured modern with inbox
fallback, fixtures, Sent status and one successful canary cannot discharge these gates.

### Activation after qualification and independent Final/Full Review

1. Record confirmed land of the reviewed implementation and its publication SHA.
2. Advance the canonical source, perform canonical restart and /api/version
   verification against that SHA. Never deploy/restart from the Code worktree.
3. Serialize affected dispatches; re-read role/card pins, `/api/runner-defaults`,
   `/api/session-runners`, pipeline and host occupancy. Save complete old pin rows.
   Perform the approved Debug pin write and readback, checking scope, strength,
   provenance, aliases and order. Keep Review unless an authorized drift correction
   is required. Do not overwrite unrelated exceptions or newer edits.
4. Confirm bundle stamp and idle-gated refresh of the standing orchestrator;
   inspect its composed stamp and `BundlesOutOfDate`. Audit only the relevant
   custom append for an old Windows exception. Notify is not composition evidence.
5. Require WQ-4 complete receipt and release from Windows Debug with no kind,
   level or bypass override. Record acceptance, then commission SourceLanding PCs.

The following write is for the activation owner **after** the gates, never a
Code test, tracker tick or automatic fleet migration:

```powershell
pwsh -NoProfile -File scripts/routing-pin.ps1 get -Role Debug -Json
pwsh -NoProfile -File scripts/routing-pin.ps1 get -Role Review -Json
pwsh -NoProfile -File scripts/routing-pin.ps1 set -Role Debug -Provenance Human -Strength Required -Candidates 'Grok/High,ClaudeCode/High' -Reason 'CARD-1011: qualified Windows Grok; approved role-wide Debug policy, Grok first and Opus fallback.'
```

Read both pins back after the write. If activation fails, pause new affected
dispatches and restore only this activation's saved pin edits after checking
for newer writes. Restore backend settings canonically and verify the actual host.
Source rollback requires a reviewed revert, normal land/restart and policy refresh.

### Explicit startup recovery

Startup failure requires explicit authorized recovery; do not retry Grok automatically.
Preserve task/session/failure-code and the bounded named startup capture before
recovering. The authorized recipe keeps `-Role Review` or `-Role Debug`,
`-Platform Windows`, `-Kind ClaudeCode -Level High`, and no -IgnoreRoutingPin.
Explicit kind narrows to that already-listed fallback; a conflicting card pin
needs an operator decision. Exhausted availability produces durable Blocked after
normal placement, subject to the independent admission rules above. Startup
failure does not promise a new automatic retry or authorize login/settings changes.
See [agent kinds](agent-kinds.md#5-grok-xai-grok-build-tui) for `screenReason` and
capture diagnosis; captures and screen redraws do not prove recipient delivery.

## 2. Tiers

The role sets the model. Do not override without a reason stated in the goal.

**Stage vs helper.** `Investigate`, `Plan`, `TestDesign`, `Code`, `Mutation`, `Review` are pipeline **stages**
(`AgentTaskRoles.IsStage`, §1) — each carries its own `server/Bundles/stage-*.md` standing-rules
bundle and is expected to close with the `--- next stage ---` block. Every other role is a
**helper**, dispatched *inside* a stage to answer one narrow question (a red test during Code, a
conflict left by a worktree task, a docs slice); it carries no stage bundle and the handoff block
is optional for it (typically `next: none` when present at all).

| Role | For | Tier |
|---|---|---|
| `Investigate` | confirm a card's root cause before any fix is designed — evidence only, no fix design (stage) | opus (escalate fable) |
| `Plan` | decompose, design, choose an approach (stage) | fable |
| `TestDesign` | write the `## Verification design` section for a landed plan — separate dispatch for `complexity:hard`/`medium`, folded into Plan for `easy` (stage, same tier as Plan) | fable |
| `Code` | write or change code; implements tests and runs ordinary V/R; commits/pushes then hands off ordinary Review (stage) | opus (override `-Level High`) |
| `Mutation` | post-land PCs and missing-control discovery in a fresh SourceLanding snapshot (stage) | Low |
| `Review` | ordinary read-only pre-land review for every complexity; judges implementation, ordinary evidence and pending PC design (stage) | fable |
| `Debug` | find out why something is broken | opus |
| `Coverage` | check what a change missed | opus |
| `Merge` | resolving a conflict left behind by a worktree task (auto-spawned after TryMergeBackAsync fails, rarely dispatched by hand) | opus |
| `Docs` | prose, markdown, comments | sonnet |
| `Commit` | git plumbing, and the CARD-0527 settle child that commits a Shared task's leftover dirty paths through `POST /api/agent-tasks/{id}/commit` | sonnet |
| `Test` / `Deploy` | RUN a thing and report what happened | haiku |

Code owns implementation and ordinary V/R; ordinary Review follows before land. Post-land
Mutation owns all deliberate PCs/variants and missing-control discovery in an independent
snapshot, freeing the Code role slot for another card. See docs/mechanical-pc-contract.md. Commission binding-only at explicit Frontier, then eligible mechanical-execution at Low; one open Mutation per O.

There are two supported Mutation producers (CARD-0604 D-17/D-19). The default is Windows local
inherited execution (`windows-job-v1`). The persistent server2 runner is the other:
`delegate.ps1 -Role Mutation -Runner server2 -Worktree -SourceLanding <operation>` creates the
verification snapshot on the runner at the exact landed sha and runs the battery inside a
root-owned cgroup the session cannot leave (`linux-cgroup-v1`). The nested Docker daemon inside
that runner is still never an executor: the placement shim clears the tracked tree's supplementary
groups, so a Mutation session cannot reach the nested socket at all.

### Commit on settle

A Succeeded Shared task's own footprint is committed in-process at settle through `GatedCommitService`
(ignore-rule gate, no push) unless `-NoCommit` / `commitOnSettle=Never`, the project column, or
`Delegation:CommitOnSettle` turns it off. Unattributable dirty trees spawn a Commit-role child
routed by the live pin. Worktree merge-back uses the same gate.

A `CommitRecoveryStarted` event is the durable obligation saved before the gate mutates Git; it is
resolved by a `CommitRecoveryNotNeeded` or `CommitRecoveryAbandoned` event naming it, or by a later
`Committed` event (CARD-0547, `CommitRecoveryObligations`). `CommitFailed` resolves it only after
the settlement history search proves no commit exists. While an obligation is unresolved the
overdue watchdog and the dead-session reconciler hold the task for up to
`Delegation:CommitRecoveryHoldMinutes` (default 720; `<= 0` disables the hold) so the report
re-hand can record `committed:`; past the hold they fail it, write `CommitRecoveryAbandoned` by
name, and the parent's note carries `git=commit-recovery-abandoned:<id8>` plus the `git log` recipe
(the commit, if any, is local and unpushed). Retry/reroute/escalate refuse with 409
`commit_recovery_pending` unless `/retry` is sent `{"abandonCommitRecovery":true}`. The attention
feed shows the hold as `CommitRecoveryPending`. The Commit-child audit reads trailers through Git's
parser (`GitWorkspaceService.ReadTrailersAsync`), so any `trailer.separators` spelling is accepted
and body prose is not.

The identity search behind every one of those reads is two `git log` legs and never `--reflog`
(CARD-0527: the full reflog walk cost 74-135s in a many-worktree checkout and blew the git budget
on every settle since go-live): `log --all …` for every ref, then `log --walk-reflogs HEAD …` for
every commit this checkout's HEAD has pointed at within reflog retention, which is what still finds
a gated commit whose branch was deleted. The reflog leg is skipped on a validated unborn HEAD.
When that search, the repository inspection or `status` is unavailable AND no unresolved obligation
or already-found settlement commit says a commit may exist, the settlement degrades instead of
re-handing forever: the task settles once with `uncommitted:N (history search unavailable)`,
`uncommitted:N (repository inspection unavailable)`, `commit refused: status inspection unavailable`
or `no commit needed (status inspection unavailable)`, plus the usual `Report names N file(s) still
uncommitted in the shared checkout:` Warning event; no commit is attempted and no Commit child is
spawned. With an obligation the CARD-0547 hold is unchanged. On the Worktree side a successful
gated commit whose receipt is not yet readable no longer fails the merge-back: the merge proceeds
and the outcome detail carries `gated commit receipt pending (operation <id>)`.

### Default stage shape by complexity (CARD-0352's `complexity:` label)

| Label | Investigate | Plan | TestDesign | Code | Review | Mutation after land | Dispatches |
|---|---|---|---|---|---|---|---|
| `complexity:easy` | if cause unknown | yes | folded into Plan | yes | separate | yes | 4 (+1 Investigate) |
| `complexity:medium` | if cause unknown | yes | separate | yes | separate | yes | 5 (+1 Investigate) |
| `complexity:hard` | unless diagnosed | yes | separate | yes | separate | yes | 5 (+1 Investigate) |

Hard/safety-critical labels affect Review depth, not placement. The operator may force folded
stages separate. Existing `-Land -Verify <filter>` remains available. This is an explicit
caller workflow, not a parser-enforced transition graph or automatic dispatch engine.

**Two different "Verify"s, never renamed.** `AgentTask.Stage : OrchestrationStage?` (CARD-0272:
Rebase/Verify/Cleanup/Review/FollowUp/Deploy) is the **landing-step** outcome `-Land` records into
`StageOutcomes` — its `Verify` is "did the build/test step of a land pass." The pipeline's Verify,
above, is a different question — "did the delegated work do what it claimed" — answered either by
Code's ordinary V/R, mandatory pre-land `Review`, and post-land Mutation's deliberate PCs. Same English word, two
axes (`AgentTask.Stage` vs `AgentTask.NextStage`); neither is renamed to disambiguate, so read the
column, not the word.

**WIP defaults (CARD-0146 D7, CARD-0533, operator ruling 2026-10-01).** Four per pipeline stage
and six tasks on server2 across all stages are orchestrator defaults, not server configuration.
The 2026-09-01 reading that plan-side stages share one slot is superseded: they run concurrently,
each in its own worktree. Code is fed to its depth cap (in flight + queued + ready); Plan toward
Code holds at that cap and resumes below it. Alternate one complex/UI card with one medium/simple
card, and prefer GitHub-linked cards. Feed depth and alternation are advisory. CARD-0147's
create-time gate is the hard stop: `MaxOpenTasks` defaults to 6 per project scope across all
runners, counting Queued, Dispatched and Working non-specialist tasks but not Blocked ones. Its
live value is exposed only in a 409 `concurrency_limit` response, `axis: absolute`.
Blocked is outside MaxOpenTasks and still occupies a runner seat. A Blocked session keeps its runner seat until parking releases it. At capacity, read GET /api/session-runners/{id}/slots. orphan=true is not a count of free seats: it marks a seat with no live desktop session or no owner task, and a live Blocked child reads orphan=false with its park field. Answer or cancel each Blocked child.
An accepted Reply changes a live Blocked task to Working; it does not itself free the runner seat. Use -Continue only for a Blocked question with standing authority; -Refine returns 409 on Blocked. While parking is disabled, do not assume an automatic release deadline.
The role gate
uses the effective dispatch-concurrency policy (CARD-0505). A fresh database imports
`Delegation:RolePolicy:<role>:RecommendedInFlight` and `Delegation:MaxOpenTasks` once; later
values come from `GET/PUT /api/dispatch-concurrency` and the project route, or the Settings →
Concurrency tab ([below](#concurrency-settings-tab-card-0506)), not from a restart.
The import defaults are Code 2, Review 2 and other named roles 1, mode `LegacyOpen`.
`SeparateQueues` is an explicit opt-in. Read effective values, occupancy, hosts, runners and runner defaults from the
effective-settings route, `GET /api/agent-tasks/pipeline?projectId=<id>` (CARD-0881), before
dispatching; the lower of that role limit and the operator's four-task default applies. Its
`hosts` block reports each host's `effectiveLimit`, `inFlight` and `remaining`; `runners` reports
capacity, occupied seats and eligibility; `runnerDefaults` reports the global and per-kind runner
defaults, with no platform default. CARD-0505 stores the dispatch limits at runtime.
There is no enforced server2 six-task cap beyond its seats unless a host budget is set.
An operator can set
`PUT /api/hosts/server2/budget` with `{ "maxInFlight": <n>, "reason": "<why>" }` to hold excess
new dispatch (`hostBudget`); the orchestrator does not write it on its own initiative. The
desktop delegated-task cap `MaxConcurrentTasks` defaults to 2 and does not
count phone-home seats. Antiphon's role and absolute 409 limits and dispatcher limits are the
hard ceiling. On 409 defer; `-IgnoreConcurrencyLimit` is allowed only for the existing absolute-axis
case with no same-stage occupant, and only when `population` is `open` and `canOverride` is true.
The full rule set is §1's standing pipeline policy.

**A `Test` agent runs and reports. It does not repair.** The boundary, stated so it is not a matter
of taste:

| Allowed at `Test` (haiku) | Escalate to `Debug` (opus) |
|---|---|
| Run a suite, report pass/fail counts and the failing names | Explain *why* something failed |
| Re-run a failure **in isolation** to establish flaky-vs-real | Change any production or test code |
| Re-run at a known-good commit (stash / worktree) to establish pre-existing-vs-caused | Widen a timeout, loosen an assertion, add a retry |
| Bisect by re-running | Decide a failure is "expected" and move on |

Isolating an error is narrowing *where* it lives; fixing it is deciding *what is wrong*. The first
is cheap and mechanical, the second is the expensive judgement this tiering exists to buy. A haiku
agent that starts editing a test to make it pass is the worst possible outcome — it is the exact
instinct that left a live 64 KB-truncation reproduction red for weeks by treating a real defect as a
flaky test.

Say this in the brief explicitly; do not assume the role name carries it.

<a id="runner-placement"></a>
**Runner defaults are not model pins.** Read `GET /api/runner-defaults` and `GET /api/session-runners`
when a fresh task needs a host. Normally omit `-Runner`; the runtime default places the task.
Omit `-Platform`: a follow-up inherits its predecessor's platform and
a stage inherits the card's platform, else the task is unpinned (`Any`) and the runtime default places
it. To unpin a stage on a pinned card pass `-Platform Any` explicitly. Pass a specific platform only
when that piece of work requires it: a test, tool, behaviour or OS-specific API; path or line-ending
handling; file locking; process or terminal behaviour; an OS-only test or probe; or evidence meaningful
only on that OS. Habit, a stage name, "Review always on X", or preferring the current host are not
requirements. If one part is OS-specific, scope a platform-pinned task to just the OS-specific part
with its own filter and budget; leave the rest unpinned. A pipeline tick never writes defaults.
A user-requested settings change is `GET` then `PUT /api/runner-defaults` with the current
revision, a reason, and Human provenance.
`Delegation:DefaultRunnerId` is import-only after the first runtime revision.

**The matrix governs an ordinary Worker dispatch; a pin is the exception.** For role-wide routing,
write the role × complexity matrix with `scripts/complexity-chain.ps1` or the Routing tab.
An ordinary Worker create without an explicit kind/level, standing agent or model-disable bypass
uses `Delegation:DefaultComplexity` (shipped `Medium`) when a role, any-role or config row resolves.
RolePolicy supplies the fallback when that row is unset. A Required pin with candidates can
override the row; a Preferred pin prepends candidates. Escalation, timeout and WIP remain RolePolicy settings.

| Behaviour | Named test | What it asserts or does not cover |
|---|---|---|
| A plain Worker uses the default-tier role row; an unset row uses RolePolicy | `MatrixDefaultComplexityCreateTests.A_plain_create_walks_the_role_row_at_the_default_tier`, `An_unset_row_keeps_the_role_policy_path` | Plan/Medium selects Codex/Frontier and stamps Medium; unset Plan leaves Complexity null |
| An any-role Medium row stamps every non-specialist Worker role, including the Commit and Merge children, subject to the explicit-ask, agent and Required-pin exclusions | `MatrixDefaultComplexityCreateTests.The_stamp_predicate_excludes_specialists_orchestrators_agents_explicit_asks_and_ignore_model_disabled`, `The_settlement_commit_child_takes_the_commit_row`, `The_server_created_merge_child_takes_the_merge_row`; `ComplexityChainRoleHttpTests.Get_role_Plan_returns_effective_resolvedFrom` | Predicate exclusions, role-cell child stamps and Plan's any-role fallback are tested separately; a 14-role any-role dispatch sweep is not covered by these tests |
| Explicit kind/level bypasses the default row with a warning; an explicit complexity combined with kind/level is refused | `MatrixDefaultComplexityCreateTests.An_explicit_kind_is_not_stamped_and_warns`; `ComplexityCreateTests.Explicit_kind_plus_complexity_is_422` | An explicit kind leaves Complexity null and names the bypassed cell; explicit kind plus complexity is 422 |
| Required pin candidates replace the cell; Preferred candidates precede it | `ComplexityEffectiveComposeTests.A_required_pin_replaces_the_cell_and_sources_pin`, `A_preferred_pin_prepends_and_sources_pin_plus_chain`; `RoutingPinCandidateCreateTests.Required_list_plus_complexity_bypasses_the_chain` | Literal effective lists and sources; the Required list is walked instead of the chain |
| A candidate-free Required pin leaves the chain selecting | `ComplexityRoutingComposeTests.A_required_pin_without_kind_or_level_does_not_bypass_the_chain` | The composed list retains chain candidates |
| A failed default-tier chain read creates the task on RolePolicy with a warning; with an explicit complexity it fails (CARD-1278) | `MatrixDefaultComplexityCreateTests.A_default_tier_create_whose_expiry_sweep_loses_every_race_is_created_with_the_warning`, `An_explicit_complexity_create_whose_expiry_sweep_loses_every_race_still_fails` | An expiry sweep that loses its 3 attempts: Queued with `Routing read failed (DbUpdateConcurrencyException); using today's path.` and no stamp; the explicit `Medium` create throws and saves no task. HTTP status mapping is in [the API doc](antiphon-api.md) sweep table |
| A held single Required target is refused; the unpinned exhausted default row becomes Blocked | `RoutingPinCandidateCreateTests.Required_single_candidate_held_is_still_409_model_disabled_with_coda`; `MatrixDefaultComplexityCreateTests.An_exhausted_default_row_blocks_with_the_cell_name` | 409 `model_disabled` for the single pin; Blocked with the cell named for the exhausted default row |
| A standing-agent pin is displayed as agent-bound | `ComplexityEffectiveHttpTests.Get_role_view_reports_an_agent_only_stage_pin_as_agent_bound` | `source: agent`, empty candidate list, `governsDefault: false`, and a withholding reason |

`delegate.ps1 -Pin` records a card exception. Read `complexity-chain.ps1 get -Role <R>` to see the
stored row and effective overlay. On routing exhaustion, relay the result to the operator for a
Retry, explicit Reroute, Cancel or hold change; do not pick a kind yourself.

**Required forbid-only pin over a one-candidate row (CARD-1271).** A forbid-only Required pin
leaves the row selecting, so a one-candidate row produces a single target, not a walk
(`RequiredSingleCandidateRefusalTests` V-1 asserts `walked: false`). These witnessed shapes are
refused with a 409 and no task row (each test asserts zero rows):

- held: `model_disabled`
- forbidden by the pin: `routing_pin_forbidden`
- quota-limited: `subscription_quota_low`
- an Orchestrator with an explicit complexity whose one candidate is Codex: `routing_exhausted`

Witnesses are the five refusal methods of `RequiredSingleCandidateRefusalTests`, including a held
ClaudeCode Orchestrator (`model_disabled`, not `routing_exhausted`). They use an explicit Hard row
for the Orchestrator cases and a default Medium row for the others. The class's sixth method is a
control: for an Orchestrator, the walk chooses one available ClaudeCode row and the task is
created on it. That create takes the `routingWalk.Chosen` branch of `AgentTaskService.CreateAsync`, not the arm.

**Operators read routing at `/settings?tab=routing`** (CARD-0333): the global
Routing settings tab shows live model availability, best-effort subscription-usage observations
(explicitly provider/profile-level, never a per-model quota, and "Unknown" rather than a guessed
number when monitoring is off or no sample exists), runner defaults, the Role policy section, the
active stage-wide/card pins with the Required/Preferred wording above, and the effective
(role × complexity) matrix with inline Configure/Clear. It is the read path for diagnosing "why did
this route where it did," not a new way to change RolePolicy or to control a running session.

**Role policy section and delegate modal level (CARD-0097).** Data: `GET /api/role-policy` (its
block in [antiphon-api.md](antiphon-api.md)). RolePolicy is the `Delegation:RolePolicy`
configuration section; pins: `scripts/routing-pin.ps1` and `/api/routing-pins`.

| Behaviour | Test (runner, file) | What it asserts |
|---|---|---|
| Section order and heading | Vitest `C097 V10 section-order`, `client/src/features/settings/RoutingSettingsTab.test.tsx` | `runner-defaults-section` precedes `role-policy-section`, which precedes `routing-matrix-row-any`; heading `Role policy` |
| Rows come from the route, in route order | Vitest `C097 V08 rows-from-dto-not-mirror`, `client/src/features/settings/RolePolicySection.test.tsx` | a 15-row fixture with an unknown role renders 15 `role-policy-row-*` rows; Mutation shows `ClaudeCode/Low (haiku)`, not `Frontier`; Custom shows `not configured`; TestDesign precedes Mutation |
| Default column label | Vitest `C097 V05 <case>`, `client/src/features/settings/rolePolicyModel.test.ts` | `null-kind-low` (kind null, alias `haiku`) → `ClaudeCode/Low (haiku)`; `configured-kind-undefined` (kind null, alias `""`) → `unknown/Medium ()`; `not configured — DefaultLevel High (opus)`; `unknown` for a null level; `not configured — DefaultLevel unknown` |
| Escalate and Timeout columns | Vitest `C097 V24 <case>`, `client/src/features/settings/rolePolicyModel.test.ts` | `High (manual)`, `—`, `Frontier after 7 min quiet`, `240 min`, `off` (timeout 0) |
| `Effective next create` column label (a projection of `effective`, not a create result) | Vitest `C097 V06 <case>`, `client/src/features/settings/rolePolicyModel.test.ts` | `Required Human pin → Codex/Frontier (gpt-6-astra)`; `default → ClaudeCode/Low (haiku)`; candidate-free pin → `no candidate (agent-only or forbid-only; see the pin)`; unknown source → `unknown` |
| Stage pin column | Vitest `C097 V25 stage-pin-column-and-effective`, `client/src/features/settings/RolePolicySection.test.tsx` | Plan pin shows `Required Human pin`, numbered candidates with availability and the reason; both next-create lines read `Required Human pin → ClaudeCode/Frontier (fable), skipping held gpt-6-astra` (CARD-1250); an unpinned row shows `—` |
| Route failure stays inside the section | Vitest `C097 V09 role-policy-failure-keeps-siblings`, `client/src/features/settings/RoutingSettingsTab.test.tsx` | 500 → `Could not load role policy.` in the section; availability, runner defaults and matrix still render |
| Section has no editor; its copy names `scripts/routing-pin.ps1` as the pin writer | not covered by a test | — |
| Delegate modal preselects the role-policy level | Vitest `C097 V16 server-level-preselects`, `client/src/features/delegations/DelegateModal.test.tsx` | Mutation row `High` → `tier-High`, `delegate-tier-source-rolePolicy`; create body `modelLevel: null` |
| An `effective.source` `pin` level wins in the modal | Vitest `C097 V17 stage-pin-wins`, same file | Plan row with `level` `Frontier` and a Required ClaudeCode/High single pin → `delegate-tier-source-pin`, `tier-High`, no `tier-Frontier` |
| Route failure discloses unknown routing | Vitest `C097 V18 route-failure-discloses-unknown`, same file | 500 → `delegate-tier-source-unknown`, no `tier-Low` promise; Delegate enabled after typing a goal |
| Null policy level falls back to `AGENT_TASK_ROLES` | Vitest `C097 V30 undefined-policy-level-falls-back-to-constant`, same file | Mutation `level: null` → `delegate-tier-source-fallback`, `tier-Low` |
| Sub-orchestrator floor depends on server configuration | Vitest `C097 V27 orchestrator-floor-is-a-server-setting`, same file | Sub-orchestrator + Mutation (`Low`) → exact unknown-floor text and no High/Low badge |
| A withheld role-policy target falls back to `AGENT_TASK_ROLES` | Vitest `C1234 unsupported-policy-kind-falls-back-to-constant`, same file | Mutation `level High`, `kind Raw`, `effective` target null → `delegate-tier-source-fallback`, `tier-Low`, no `tier-High` |
| Project setup names the default tier only when it is a model level | Vitest `C1234 delegation step names the default tier only when it is a model level`, `client/src/features/settings/ProjectSetupModal.test.tsx` | `Medium` → `default Medium tier.`; missing, null or 999 → `default tier unknown.` |

The next-create previews combine role policy, the active stage pin and the role's effective
complexity cells. A one-slot pin composition bypasses the matrix, including a Preferred pin that
deduplicates against policy. Other eligible Workers walk the configured default tier; pin-list
walks without a row filter kinds, stage forbids, holds and quota. Orchestrator walks accept
ClaudeCode and retain the walked level. Single targets can be refused with 409. This includes a Required forbid-only pin with one
default-row candidate: held, quota-limited or forbidden means refusal (V17 D1; V21), even when
the row came from any-role or config. Other exhausted row/list paths become Blocked under
the UI defaults (V3, V15, V17 exhaustion controls). The configured single-target Orchestrator floor is absent
from the reads, so levels below Frontier are shown as unknown. Missing candidate availability,
partial targets and standing-agent details are disclosed as server dependencies. availableNow
includes the null-owner quota gate; a held single target may mask quota, whose create-time check
runs first. These are 15-second snapshots, without card pins or an admission reservation.
Vitest C1250 V17's 58 server-cited rows and V18–V21's exact modal assertions bind the repair;
V1–V16 and C097 V25 bind the existing paths. See the
[repair plan](superpowers/plans/2026-10-14-card-1250-next-create-preview-plan.md).

For card-origin Hand back or a possible CARD-<digits> binding in the derived title, the modal
displays “unknown — a card pin may apply; routing depends on the server” with no target badge.
Identifier boundaries are exact for ASCII neighbours. A non-ASCII neighbour counts as a
possible boundary because browser and server Unicode tables can differ, so this can show
unknown when the server would not bind (C1250 V22 Unicode drift and ASCII controls).
Title derivation and explicit-card context are also covered by C1250 V22;
V23 covers Hand back, goal edits and the card-free control. Bare #N in a goal does not bind;
an explicit API card field can use #N. The Settings column assumes a card-free create.
The modal admission note names card pins, standing-agent details, quota, holds, provider sign-in,
concurrency, workspace and runner conditions as server dependencies (V24).

**Stage pins and the matrix:** the matrix row can show candidates a stage pin keeps out of use.
Read the live pins with
`GET /api/routing-pins?role=<Role>` or `scripts/routing-pin.ps1 get -Role <Role>` (or
`/settings?tab=routing`) before assuming the matrix is misconfigured. Clearing a pin, or
downgrading it from Required to Preferred, is an operator decision made through
`scripts/routing-pin.ps1`; CARD-0333's matrix UI is read-only for pins.

**Matrix cells with a failed read (CARD-1258, CARD-1270).** A role cell's effective view reads
the stage pin, the chain, the chain metadata, a standing agent's label and each candidate's
availability; a failed part appends `<part> read failed (<type>)` to the cell's alert. The cell
renders a value derived from a failed read as unknown, not as a definite candidate list or
`then this row`. Values from reads that succeeded stay definite, with one exception: after a
failed stage-pin read an own, inherited or configuration origin and the stored candidates are
hidden although the chain read succeeded, because the unread pin can override them (C1258 row
below); an Unset cell keeps `Stored cell: unset`, which the successful chain read established
(C1258 `Unset` rows). The Configure editor still
opens with those stored values; its save writes only the stored row (V-2 `stage pin` editor rows).
The PUT replaces the whole row (a blank reason stores the default `<cell> chain` label, a null
expiry stores none), so the editor asks for an explicit overwrite before it writes over a value it
could not read.

A read that has not returned is not a value either. React Query 5 reports an offline query with
no data as pending and paused (`isLoading` false, no data, no error), so each section of the
Routing tab keys on the pending state: it shows `<subject> loading.` or
`<subject> unknown (read paused offline).` in place of its values, its empty text or a failure,
the matrix builds no Any role row and offers no Configure without the list, and a role row
offers no Configure while its own read is pending. An editor opened from a role cell disables
Save and Clear while that cell's read is pending again (C1270 D-1 rows below).

| Behaviour | Test (runner, file) | What it asserts |
|---|---|---|
| Each read of the Routing tab in each query state: loading, paused offline, failed, refetch failed, fetching with stale data, success with an empty result, success | Vitest `C1270 D-1 $read read $state`, `client/src/features/settings/RoutingSettingsTab.test.tsx` | 62 cases over the list, Plan role row, routing pins, availability, usage, role policy, runner defaults, revisions and session runners reads; each asserts the React Query state it reached, its own loading, paused or failure text and no other, the populated values only on success or while a refetch is in flight, the empty text only on an empty success, and Configure only on success |
| Offline from the first render, then reconnect | Vitest `C1270 D-1 offline from the first render shows no value and sends no PUT after reconnect`, same file | availability, usage, role policy, runner defaults, runner catalogue and matrix show their paused text; no `Unset`, `dispatch blocks`, empty text, `Could not load`, Any role row or Configure; after reconnect the cells load and no PUT or DELETE is sent |
| An editor whose role-cell read is pending again | Vitest `C1270 D-1 editor whose cell read returns to pending blocks %s until the read loads`, same file | `Save and Clear are disabled until the cell's read loads: Plan effective cells unknown (read paused offline).`; Save and Clear override disabled; both enabled after the read loads; no write sent |
| Pending-read labels | Vitest `C1270 D-1 pending read label <case>`, `C1270 D-1 routing pins read label <case>`, `routingSettingsModel.test.ts` | loading, paused offline and pending-idle texts; null once the read loaded, also while a later refetch is paused |
| Clear override after a failed chain read words the cell as unknown | Vitest `C1270 D-2 clear copy after a chain read failure falls back to <fallback>`, `routingSettingsModel.test.ts`; `C1270 D-2 clear confirmation after a chain read failure on <tier>`, `RoutingSettingsTab.test.tsx` | `Clearing Plan/<tier> deletes this cell's own rule, with its reason and expiry, if one exists; its current value is unknown (chain read failed (…)). Without an own rule this cell …`; no `currently replaces` |
| Clear override after a failed chain metadata read names the unread reason and expiry | Vitest `C1270 D-2 clear copy after a chain metadata read failure names the unread reason and expiry (<fallback>)`; `C1270 D-2 clear confirmation after a chain metadata read failure on <tier> names the unread reason and expiry` | the confirmation adds `The stored reason and expiry are unknown (chain metadata read failed (…)); clearing deletes them with the row.`; Clear itself stays behind `Confirm clear` without the overwrite checkbox |
| A failed stage-pin read on an Unset cell | Vitest `C1258 V-2 stage-pin failure on an Unset cell keeps Stored cell: unset <tier>`, `RoutingSettingsTab.test.tsx` | `Effective routing unknown` and `Stored cell: unset`; no RolePolicy candidate, other origin or `dispatch blocks` |
| Every displayed cell field against each failed read, with a Preferred and a Required pin, on the default and a non-default tier | Vitest `C1270 V-2 $read read failure with a $strength pin on $tier`, `client/src/features/settings/RoutingSettingsTab.test.tsx` | 32 cases: literal label, candidate, stored-origin, provenance, reason, expiry, availability, alert, banner and Configure texts, and the definite texts that must be absent |
| A failed stage pin read makes the route unknown for every source | Vitest `C1258 V-2 stage-pin failure suppresses route promises $source/$tier`, same file; `C1258 V-1 stage-pin failure $source/$tier`, `routingSettingsModel.test.ts` | `Effective routing unknown`; no effective or stored candidate and no stored origin |
| A failed chain read under a Preferred pin is unknown; under a Required or standing-agent pin the route stays definite | Vitest `C1270 V-1 effective label <case>`, `client/src/features/settings/routingSettingsModel.test.ts`; the V-2 `chain` rows | Preferred: `Effective routing unknown` with no candidate list, also when the default tier appended RolePolicy; Required: `Required Human pin → Codex/Frontier (gpt-6-astra)`; both `Stored cell: unknown (read failed)` |
| A failed chain or chain metadata read shows the omitted reason and expiry as unknown | The V-2 `chain` and `chain metadata` rows | `Provenance, reason and expiry unknown: chain read failed (…)` or `Reason and expiry unknown: chain metadata read failed (…)`; the stored reason and `Expires` line are not rendered |
| A failed availability read is unknown, not unavailable | Vitest `C1270 V-1 availability <case>`; the V-2 `availability` rows | `availability unknown: availability read failed (…)`; a held reason or free text mentioning the marker renders unchanged |
| A failed standing-agent read names the agent id with `(name unknown)` | `C1270 V-1 effective label <case>`; the V-2 `standing agent` rows | A missing agent keeps `(unavailable)`; a failed read does not |
| A failed routing-pins or role-policy read leaves the cell definite | The V-2 `routing pins` and `role policy` rows | Cell texts match the no-failure row; the section shows its own failure text |
| A failed chain or chain metadata read hides a stored reason the response still carries | Vitest `C1270 V-2 $read read failure beside a stored reason on $tier` | The unknown line is shown; no `Plan cell` and no `Expires` in the cell |
| A standing-agent pin with a failed chain read keeps the agent route | `C1270 V-1 effective label required-standing-agent-with-chain-read-failure`, `preferred-standing-agent-with-chain-read-failure`; Vitest `C1270 V-2 standing agent pin with a chain read failure keeps the agent route with a $strength pin` | The agent label stays definite; `Stored cell: unknown (read failed)` and the provenance, reason and expiry unknown line |
| The Configure editor after a failed chain or chain metadata read | Vitest `C1270 V-2 editor after a $label read failure on $tier`, `RoutingSettingsTab.test.tsx` | Chain: `Current value unknown (chain read failed (…))…`, no candidate row, empty Reason and Expires, Save and Clear override disabled until `Overwrite the unread stored cell…` is ticked, then one PUT. Chain metadata: the read candidates, `Stored reason and expiry unknown (…)…`, empty Reason and Expires, Save disabled until `Overwrite the unread reason and expiry…` is ticked, then a PUT with null reason and expiry. Controls (none, stage pin, availability, standing agent): stored candidates, reason and expiry, no checkbox |
| Routing pins loading, failed, or stale after a failed refetch | Vitest `C1270 V-2 routing pins %s` | Row `Stage and card pins loading.` or `Stage and card pins unknown (routing pins read failed: …)`; card list `Card-specific pins loading.` or `Card-specific pins unknown (…).`; no banner, card-pin count, card pin or `No card-specific pins` |
| Runner-defaults history and runner catalogue reads | Vitest `C1270 V-2 runner defaults history read %s`, `C1270 V-2 runner defaults catalogue read %s`, `RunnerDefaultsSection.test.tsx` | `History unknown (revisions read failed: …).` or `History loading.` in place of `No revisions yet.`; a stored runner reads `<id> · catalogue unknown`, not `missing from catalogue` |

**A Preferred stage pin on an unset non-default tier lists the pin alone.** The effective view
appends RolePolicy after a Preferred pin only on the default tier; on another tier an explicit
`-Complexity` walk has an empty row after the pin, so the cell's `then this row` adds no
candidate and an unusable RolePolicy is not diagnosed
(`ComplexityEffectiveComposeTests.A_preferred_pin_on_an_empty_cell_appends_policy_only_for_the_default_tier`,
`A_non_default_preferred_pin_does_not_report_an_unused_policy_as_unlaunchable`;
`ComplexityEffectiveHttpTests.A_preferred_pin_on_empty_tiers_exposes_policy_only_on_the_default_tier`).

**Complexity chains (CARD-0090, CARD-0332, CARD-0097).** Pass `-Complexity Hard|Medium|Easy` when the work's hardness
should pick (kind, level) from an ordered fallback list, instead of an explicit `-Kind`/`-Level`.
The list is a sparse (role × complexity) matrix: the walk reads the role cell, then the any-role
chain, then the config default. Exhaustion follows the refusal/Blocked rules below. Without `-Complexity`, an ordinary Worker uses the
configured default tier when a row resolves, or RolePolicy when it does not (tests above).
Combining `-Complexity` with `-Kind` or `-Level` is 422 (`ComplexityCreateTests.Explicit_kind_plus_complexity_is_422`).
Config defaults ship empty; write the live lists with `complexity-chain.ps1 set` (`-Role Plan` for
a cell, omit `-Role` for the any-role row). When the chain
is exhausted, a Required pin with one effective candidate applies single-target 409 checks
(V17 D1). Otherwise the task is **Blocked** (or 409 `routing_exhausted` with `-RefuseIfExhausted`):
**relay that to the operator; do not pick a kind yourself.** A human answers with Retry,
`delegate.ps1 -Reroute <id> -Kind … -Level …`, Cancel, or by clearing a hold. Auto-resume onto
an already-listed candidate when capacity returns is executing the instruction, not a new guess.
A usage-wall reroute is ordinary work on the new candidate: the walled session's capacity wait
ends with the session, and the requeued row dispatches on the next tick unless the new candidate
is itself held (CARD-0481).

**Role-wide writes and stage-pin removal (CARD-0097).** Read the effective view explicitly before
and after a row write that clears a pin:

```powershell
pwsh -NoProfile -File scripts/complexity-chain.ps1 get -Role Review
pwsh -NoProfile -File scripts/complexity-chain.ps1 set -Role Review -Complexity All -Candidates Codex/Frontier -ClearStagePin
pwsh -NoProfile -File scripts/complexity-chain.ps1 get -Role Review
```

`-ClearStagePin` deletes the whole stage pin, including its forbidden list, dates and agent
assignment. It performs no automatic effective read-back. Inspect those fields before choosing
the clear; the two `get -Role` commands above are operator work.

| Behaviour | Named test | What it asserts or does not cover |
|---|---|---|
| Three cell writes precede the stage-pin DELETE | `ComplexityChainScriptTests.ClearStagePin_deletes_the_stage_pin_only_after_three_2xx` | Exact Hard/Medium/Easy PUTs, pin lookup, then DELETE by id; the card pin and another role's stage pin remain; no effective read-back request |
| Partial writes leave the stage pin in place | `ComplexityChainScriptTests.ClearStagePin_is_skipped_when_a_tier_failed` | A failed tier stops the sequence before the pin clear |
| `-ClearStagePin` needs a named role and `-Complexity All` | `ComplexityChainScriptTests.ClearStagePin_requires_All_and_a_named_role` | Invalid combinations exit before HTTP |
| GET prints the effective overlay | `ComplexityChainScriptTests.Get_Role_prints_the_effective_line` | Literal effective line from the role-view DTO |
| Operator compares the effective view before and after clear, including forbidden aliases, dates and agent assignment | not covered by a test | Manual acceptance; the script test uses a pin id and checks deletion, not a field-rich live pin |

**Generated matrix instructions (CARD-0097).** The server projects the matrix into section 5 of
`ANTIPHON_ORCHESTRATOR_INSTRUCTIONS.md` and `GET /api/orchestrator-instructions`. Pins, model levels
and standing instructions become sections 6, 7 and 8. Activation needs a server build containing
S-F; inspect `/api/version` after the caller's restart.

| Behaviour | Named test | What it asserts or does not cover |
|---|---|---|
| The matrix names the default complexity and stage-pin overlay | `OrchestratorInstructionsRendererTests.Sections_render_caps_runners_defaults_holds_pins_levels_and_standing_lines`; `OrchestratorInstructionsRefreshTests.Covered_writes_each_regenerate_and_notify` (`routing-pin`) | Literal headings and sample role line; the live snapshot reflects the Code stage pin |
| A failed chain read is shown as unknown in the affected tier | `OrchestratorInstructionsRefreshTests.A_failed_chain_read_marks_the_affected_file_cells_unknown` | Hard, Medium, Easy and simultaneous failures on Plan; persisted file labels include `unknown (chain read failed)` within the 160-byte line cap; matrix elision still uses the route pointer |
| Regeneration reports unreadable cells and their recovery through the existing notice path | `OrchestratorInstructionsRefreshTests.A_failed_chain_read_notice_reports_unknown_and_recovery_reports_the_candidate` | One candidate-to-unknown notice; the same failure keeps revision/count unchanged; recovery sends the reverse transition and removes the file's unknown marker |
| A default-chain or stage-pin read fault makes the effective target and source unknown | `OrchestratorInstructionsRefreshTests.Failed_reads_are_unknown_in_file_and_notice_then_recover` (`chain-default`, `stage pin`); `A_failed_chain_read_notice_reports_unknown_and_recovery_reports_the_candidate` (Medium, simultaneous) | Literal source-to-unknown transitions in the direct delta and a separate bounded file detail; failure notices exclude `→ rolePolicy` and `→ unset` |
| Read faults stay distinct from observed empty settings | `Failed_reads_are_unknown_in_file_and_notice_then_recover` (24 rows in the preceding class) | Default/non-default chain, pin, availability, metadata, standing-agent, policy/local-budget, runner, runner-default, hold and pin-list faults crossed with file/notice; exception-type changes retain revision/count; recovery adds one notice with observed values. The configured default complexity remains Medium |
| Long previous values retain the unknown reason in the delta | `OrchestratorInstructionsDeltaTests.A_long_known_value_keeps_the_unknown_transition_reason_visible` | A 300-character effective value becomes unknown within the 120-character cap; recovery starts with the unknown-to-value transition |
| Combined unknown reasons retain the matrix byte cap and elision order | `OrchestratorInstructionsRendererTests.Unknown_matrix_details_keep_the_byte_cap_and_matrix_first_elision` | 14 short/combined-unknown rows; diagnostic lines stay within 160 UTF-8 bytes; comma-separated reasons are not candidate elisions; at full minus one the matrix collapses before the sample card pin |
| API and HTTP-guide routing pointers resolve to the owner heading | `OrchestratorInstructionsGuidanceTests.Routing_cross_references_resolve_to_an_existing_owner_heading` | Two API pointers and one HTTP-guide pointer name an existing bold paragraph heading |
| Role lines use up to three candidates per tier, then `+N`, and at most 160 UTF-8 bytes | `OrchestratorInstructionsRendererTests.The_matrix_section_is_bounded_per_line_and_elided_in_place_under_pressure` | 14 role lines with eight candidates per tier; line-byte limits and `+5`; at `full - 1` the matrix collapses in place while the card pin remains |
| Truncation order is matrix, card pins, holds, standing lines, runner features | The preceding renderer test; `Oversized_snapshot_truncates_in_priority_order_under_the_byte_cap` | Matrix first-step elision and the retained trailing cut-notice order; arbitrary snapshots fitting a universal body cap are not covered |
| Cell upsert and clear fire the existing settings signal once per saved write; a repeated cell body produces no new notice | `OrchestratorInstructionsRefreshTests.Covered_writes_each_regenerate_and_notify` (`complexity-chain`) | Signal reasons/counts, revision, file bytes, notice counts, clear and already-clear behavior |
| The notice arrives as a complete prompt | `OrchestratorInstructionsRefreshTests.A_cell_write_notice_is_received_as_one_complete_user_prompt_equal_to_its_queued_body` | One Sent queue row, one matching post-baseline UserPrompt, exact body, recipient version and 14 snapshot roles |
| An unchanged matrix has no delta; a changed cell names its role/tier | `OrchestratorInstructionsDeltaTests.An_identical_matrix_rewrite_produces_no_delta_and_a_changed_cell_names_the_role` | Empty delta for separately allocated equal rows; literal Plan Medium delta |
| Notice persistence/enqueue ordering is retained | not covered by a new test | The existing path saves the version before enqueue; a crash or enqueue failure in that interval can lose the notice. This slice adds no recovery mechanism |

**Matrix migration runbook (operator).** Prerequisites: CARD-1234 and CARD-1213 are landed; the
caller activates S-A/S-B/S-D/S-E/S-F and checks `/api/version` plus the role-view `effective` and
`defaultComplexity` fields. The following migration and rollback steps are copied from the
[landed matrix plan §6.3–6.5](superpowers/plans/2026-10-14-card-0097-matrix-plan.md#6-rollout-and-migration).
These are operator instructions; the live migration and rollback are not acceptance work performed by S-F.

3. **Migration (operator).** `pwsh -NoProfile -File scripts/migrate-stage-pins-to-cells.ps1 -OutDir <dir>` (dry
   run) against the live server; read the table and keep the printed snapshot path; `-Apply -OutDir <dir>`; then for
   each role run `complexity-chain.ps1 get -Role <R>` and read the Routing tab (each migrated cell now says
   `Required Human pin → …` above an own rule whose list equals the pin's). Only then clear the pins, one role at a
   time, with `pwsh -NoProfile -File scripts/routing-pin.ps1 clear -Role <R>`, and re-read `get -Role <R>`: the
   `effective:` line now reads `role` with the same list. The expected cell contents, from the live pins:

   | Role (pin id) | Hard = Medium = Easy |
   |---|---|
   | Plan (`bbdce6e6`) | ClaudeCode/Frontier (fable) |
   | TestDesign (`90e8d896`) | ClaudeCode/Frontier (fable) |
   | Code (`4683c049`) | ClaudeCode/High (opus) |
   | Review (`50328b61`) | Codex/Frontier (gpt-6-astra) |
   | Investigate (`de218932`), Mutation (`64e43198`), Debug (`909836ed`), Coverage (`858afdbb`), Docs (`11a17ef6`), Commit (`68d8d684`), Test (`6855ae74`), Deploy (`f890319f`), Merge (`e6d574aa`) | ClaudeCode/Frontier (fable), ClaudeCode/High (opus) |
   | Custom | no pin, no cell: RolePolicy fallback (`DefaultLevel` High, opus) |

   The reasons carried over are the pin reasons with the `(migrated from stage pin <id>)` suffix. Mutation's RolePolicy
   default is `Low` (`DelegationSettings.cs:357`); the migrated cell says Frontier then High because that is what the
   pin says today — the operator may edit that row afterwards. Writing the three tiers identically means `-Complexity
   Hard|Easy` behave like the default until the operator differentiates them.
4. **What changes for callers after the pins are cleared.** An explicit `-Kind`/`-Level` that disagrees with the row
   runs as asked with a warning instead of 409 `routing_pin_conflict` (OD-2); a plain dispatch whose row is exhausted
   is Blocked and auto-resumes (OD-6); a settlement Commit child or a conflict-resolver Merge child whose row is
   exhausted is Blocked with a note to the settled task's caller (D-3); until the Merge pin is cleared the Merge child
   keeps ignoring it, as today (D-3 step 3); an Orchestrator-kind create and an `ignoreModelDisabled` create follow
   RolePolicy, not the row (OD-7, V-A7, V-A6); `delegate.ps1 -Pin` still writes a card pin, which the cell shows as an
   exception (OD-5); the Role policy section's `Effective next create` column and the delegate modal's tier preview
   read the default-tier row for a Worker create (CARD-1250, superseding D-6). Holds, quota, forbid lists on remaining pins and reroute are unchanged.
5. **Rollback, in this order (R3-03).** Pins first, cells second, so that no interval exists in which neither a pin
   nor the migrated cells decide a create:

   | Step | Operator action | Why in this position | Bound by |
   |---|---|---|---|
   | 1 | For each role whose pin was cleared in §6.3: `pwsh -NoProfile -File scripts/routing-pin.ps1 set -Role <R> -Provenance Human -Strength Required -Candidates <list> -Reason <text>`, list and reason from the §6.3 table | A Required pin with candidates bypasses the row for an ordinary create (`RoutingCandidates.cs:58-63`) and for the two children (D-3 step 3). Deleting the Review cells first would put a plain Worker/Review create on RolePolicy's ClaudeCode/Frontier (`DelegationSettings.cs:358`) until the Review pin is back, where before the migration it ran Codex/Frontier | — (operator step) |
   | 2 | Read back each role: `complexity-chain.ps1 get -Role <R>` prints `effective: pin …`; stop here if any `set` failed — nothing has been removed yet | the pin, not the cell, is the governing rule from here on | V-D6 (the line) |
   | 3 | `pwsh -NoProfile -File scripts/migrate-stage-pins-to-cells.ps1 -Restore -Snapshot <first snapshot> -OutDir <dir>` | the script re-checks step 1 itself and refuses with zero writes while a migrated role has no Required stage pin (D-9 precondition 1); with pins back, the order in which cells are removed or re-PUT does not matter | V-E12 (refusal, then proceed), V-E6 (per-case action), V-E4 `restore` (stop on non-2xx) |

   Cells written by the row editor after the first snapshot are overwritten or deleted by step 3, which is why the
   runbook says to restore from the first snapshot and before any editor use. An Auto before-image is cleared, not
   re-created (D-9). An operator who wants the cells gone without recreating the pins is not rolling back: that is
   `complexity-chain.ps1 clear -Role <R> -Complexity All` per role (S-D), chosen explicitly.

**A Blocked-on-question child (CARD-0294 S1+S2).** The parent `[task … blocked]` note carries
`reason:` / `asks:` / `authority:` / `next:` above the body, outside the excerpt window. If
`authority:` names something, `delegate.ps1 -Continue <id>` is the one action that replays it;
otherwise `-Reply` if you can answer, else put `asks:` in your chat reply now — never `NO_REPLY`
a blocked note. Dispatch with `-Authority "<the user's own words>"` whenever the user has
pre-approved a sequence. Auto-continue, the 5-minute bound-chat notice, and the unmarked
zero-progress Block are follow-on slices of the same card.

The best single result of this period came from a `Debug` agent; the cheapest useful one was a haiku
check at **$0.12**.

### Concurrency settings tab (CARD-0506)

`/settings?tab=concurrency` edits the CARD-0505 dispatch limits through the routes in
[antiphon-api.md](antiphon-api.md) (Dispatch concurrency routes). Each row below states what the
named test asserts and nothing more; behaviour outside these rows is not documented here. `C506 …`
tests are Vitest titles in the named `client/src` test file, run with
`pwsh -NoProfile -File scripts/test-client.ps1 <file>`. The one TUnit method is named with its class
and file.

Navigation:

| Behaviour | Test (file) |
|---|---|
| Tabs read Routing, Concurrency, Status in that order; a router link, back and forward to `?tab=concurrency` or `?tab=routing` select that tab and panel; an unknown `tab` selects Templates | `C506 V16 controlled-tab` (`SettingsPage.test.tsx`) |
| `?tab=concurrency&project=P` selects Concurrency, shows P's revision 4 with Scope `P`, and requests P's route once and the global route zero times | `C506 V15 project-deeplink` (`ConcurrencySettingsTab.test.tsx`) |
| Choosing Scope `Q` sets `project=Q`, unchecks P's draft switch and empties the reason; Save then PUTs to Q's route; choosing Global defaults removes `project` | `C506 V15 scope-selector` (`ConcurrencySettingsTab.test.tsx`) |
| An unknown `project` id shows Scope `Unknown project (<id>)` and a load error whose Retry requests the route again; no Save button, no table, no PUT | `C506 V15 missing-project` (`ConcurrencySettingsTab.test.tsx`) |
| Clicking a tab keeps `project` and an unrelated `keep` parameter | `C506 S3F1 active-tab-click`, `C506 S3F1 away-and-back`, `C506 S3F1 unknown-tab` (`ConcurrencySettingsTab.test.tsx`) |
| The Projects row button `Project concurrency` navigates to `/settings` with `tab=concurrency` and that row's `project`, keeping `keep` | `C506 V17 project-row-link`, `C506 S3F1 row-link-keeps-params` (`ProjectConfig.test.tsx`) |

Editing (`DispatchConcurrencyEditor.test.tsx` unless named):

| Behaviour | Test |
|---|---|
| 14 role rows plus Overall (15 rows), 16 with a DTO-only `FutureRole`; inherited cells show value and source label (`Global override`, `Built-in default`) | `C506 V08 rows-and-sources`, `C506 V08 dto-only-role` |
| `mode` is displayed as text, with no mode combobox or textbox | `C506 V08 rows-and-sources` |
| `scripts/dispatch-concurrency.ps1 set` sends a file's `mode` with the other overrides | TUnit `DispatchConcurrencyScriptTests.Set_reads_then_sends_exact_snapshot` (`tests/Antiphon.Tests/Scripts/DispatchConcurrencyScriptTests.cs`; run through the build slot per [testing-and-build.md](testing-and-build.md)) |
| `usePutDispatchConcurrency` sends exactly the keys `expectedRevision`, `expectedGlobalRevision`, `overrides`, `reason` (trimmed), `provenance: "Human"` to the scope's route | `C506 V05 global`, `C506 V05 project-zero`, `C506 V05 project-existing` (`dispatchConcurrency.test.ts`) |
| With no saved overrides, switching on Code max parallel and setting 1 sends `overrides: { roles: { Code: { maxParallel: 1 } } }` (project and global) | `C506 V09 sparse-project`, `C506 V09 sparse-global` |
| After another writer changes only `mode` at r8 (live event or 409), the next Save body carries r8's `mode` when r8 has one and omits it when r8 has none, with the saved limit overrides and the edited cell | `C506 S2F1 live mode-changed`, `… mode-added`, `… mode-removed`, and the three `C506 S2F1 conflict …` cases |
| No limit on Overall max queued, Review max parallel or Review max queued sends that field as `null`; Revert then sends the body without it; Overall max parallel offers no No limit checkbox | `C506 V10 overall-queued`, `C506 V10 review-parallel`, `C506 V10 review-queued` |
| The `Clear all overrides` button opens a confirm with no write; `Clear overrides` sends `overrides: {}`; Cancel writes nothing and keeps the draft and reason; the button is disabled with no saved overrides and enabled with a mode-only override | `C506 V11 global`, `C506 V11 project`, `C506 V11 cancel`, `C506 V11 schema-only-disabled` |
| A 422 for `roles.Code.maxParallel` shows its message in the `role="alert"` error and on the Code max parallel input (`aria-invalid`, accessible description); the value is kept and no success notification appears | `C506 V13 field-error` |
| Notification `No change` when the PUT returns the sent revision; `Saved revision 8` and an emptied reason when it returns r8 | `C506 V14 no-change`, `C506 V14 saved` |

No editor test returns a server 422 for `reason`. `C506 V03 reason <variant>` (`dispatchConcurrencyModel.test.ts`)
asserts that `validateDraft` reports `reason` for an empty, whitespace-only or 401-character reason.

Revision conflict (409 `dispatch_concurrency_revision_conflict`):

| Behaviour | Test |
|---|---|
| After the 409: a second scope GET, the alert `Saved revision changed (now global r8, project r5)` (global case: `project r0`) with `Code max parallel: your draft 1, now saved 3`; project case: edited value and reason kept; one PUT; the next Save sends the refetched revisions | `C506 V12 conflict-project`, `C506 V12 conflict-global` |
| The refetch answers 503: refresh-failed notice, Save disabled and writes nothing, draft kept; `Reload saved settings` shows the conflict alert and re-enables Save, which sends the new revision | `C506 V12 refresh-fails` |

Live updates: `DispatchConcurrencyChanged` marks the global and project
`dispatchConcurrency` scope and revisions queries and the pipeline query invalidated; the cached
project scope data is unchanged, and `['unrelated']` and `['runnerDefaults']` are not invalidated
(`C506 V07 changed-event`, `useSignalRInvalidation.test.ts`).

| Scenario in the test | Asserted result | Test |
|---|---|---|
| Code cell edited to 1 with a reason; the event's refetch returns project r5 / global r8 with Code saved as 3 | Conflict alert with that diff; value 1 and reason kept; next Save sends r5 / r8 | `C506 V12 live-dirty` |
| No edits; the refetch returns project r5 / global r8 with Code saved as 3 | Code switch on with 3, no conflict alert; next Save sends r5 / r8 with that override | `C506 V12 live-clean` |
| No edits; project r4 overrides Code to 1 and the refetch lowers the inherited Code value 3 → 2 at global r8 | Inherited cell shows 2; override stays on with 1; next Save sends r4 / r8 | `C506 V12 inherited-change` |

A refetch to a lower or reused revision (no live event):

| Scenario in the test | Asserted result | Test |
|---|---|---|
| Global r7 (Code 3), no edits; the refetch returns restored r3 (Code 1, Review max queued 9) | Revision 3, Code 1, Review max queued 9, no conflict alert; next Save sends r3 / r3 | `C506 S2R3 rollback-global-clean` |
| Project r4 (Code 1), no edits; the refetch returns project r2 or global r3 with Code 3 | Code 3, Review max queued override off; next Save sends the restored revisions | `C506 S2R3 rollback-project-project-clean`, `… rollback-project-global-clean` |
| Code edited to 2 with a reason; the refetch returns a restored document | Conflict alert with one diff item; value 2 and reason kept; the untouched field follows the restored document in the next Save | `C506 S2R3 rollback-dirty-global`, `… rollback-dirty-project` |
| Global r7 → r3 → r7 again with Code 1, then Review max queued edited to 6 | Code effective 1; next Save sends r7 with Code 1 and Review 6 | `C506 S2R3 reused-revision` |
| The refetch returns r7 again with Code 1 instead of 3 | Code 1, no conflict alert; next Save carries Code 1 | `C506 S2R3 same-revision-new-content` |

A refetch that changes only occupancy has no committed test (Review 7826b06f diagnostic
`R4 occupancy-only refresh of dirty form updates occupancy without banner`).

Known limitations with no committed test (Review 24d4e81c diagnostics; `DispatchConcurrencyEditor.tsx`
comments on `settleSave` and the render fold; `.antiphon/task-87938db0.md` Residuals):

| Limitation | Evidence |
|---|---|
| Restore during a pending PUT: a lower or reused-revision read seen while the PUT is held is replaced by the late reply | Diagnostics `R3 restore during PUT lower read remains current after late reply`, `R3 restore during PUT reused read remains current after late reply` |
| Timestamp collision: after a Save whose refresh failed, a read with the same data object and `dataUpdatedAt` is not adopted | Diagnostic `R3 same store time equal read after failed save refresh is current` |
| Late Save after unmount: whether the continuation invalidates the shared query cache | No test asserts it |

`C506 S2R3 unmount-pending` asserts that a Save released after its project editor unmounts shows one
success notification, and that a new global editor then shows revision 7 with Code max parallel enabled.

### Launching an agent

The hosted dispatcher owns independent serial admission and maintenance loops (CARD-1519).
Each pass opens its own service scope. Full queued admission is due every
`max(1, Delegation:PollIntervalSeconds)` seconds, default five, even while maintenance
waits. A pass already executing adds its remaining duration; inspection, lease, Git,
preparation and launch work add their own time. Repository signals coalesce and can
accelerate a ready batch, but cannot reset or outrank a due full pass. Every attempt
still rechecks capacity and claims through the existing gates. Shutdown joins both
loops and any abandoned sweep's owned scope; an expired host shutdown deadline does
not prove that join completed. `TickAsync` remains a sequential maintenance then
admission wrapper for direct callers.

Every admission pass reconciles sibling inspections against the full queued snapshot
before selecting a ready batch (CARD-1457). Unrelated queued results, workers and
attempt markers survive a partial pass; terminal and empty snapshots still clean
them up. Retention does not grant claim authority: intent, age and frozen refs keep
their existing checks.

A Queued runner-bound task whose mirror is not recorded yet stays Queued. Its deduplicated `Held` detail is one of three texts: remote workspace preparation is in flight (the branch push and mirror were requested); preparation failed N times in a row and the next attempt is not before an instant (exponential backoff, base 30 seconds, cap 900 seconds, reset when the mirror is recorded); or the runner is not dispatch-eligible. Those traces are not a warning on every tick. A warning is written once per failed preparation attempt.

The preparer pushes that branch with `git push origin <branch>` and no `-u` (CARD-1076). The desktop task branch does not track origin, so the worktree does not show itself ahead of or behind `origin/<branch>`. The objects are still on origin for the runner to fetch. One push runs at a time per repository, inside the preparer and off the dispatcher tick; a task waiting for its turn stays in flight. `RemoteWorkspacePreparer.Progress().BehindTaskId` is the current push-gate holder while that task waits for its turn (CARD-1093), read under the gate lock on each call. A waiter names whoever holds the turn now. It is null when that task is pushing or mirroring, and null while the holder is empty. The push budget is `Delegation:RemotePrepPushBudgetMinutes` (default 20, floor 1). The child journal is tagged purpose `remote-prep-push` with the task id. A live record with that purpose does not fence the repository mutation lease. A dead, reused, or unknown record still fences, and the unavailable sentence still names `scripts/recover-repository-children.ps1` with `purpose=remote-prep-push` and the task id. When the pipeline shows `remotePrep` with a holder for many minutes, that holder's push is the upload to watch. Do not run `recover-repository-children.ps1` against that live record, and do not restart the server: the push starts again from scratch.

`GET /api/agent-tasks/pipeline` queue reason `repositoryLease` is the current-stint repository-lease hold. `heldBy` is the owner named by the lease sentence, else the task holding a running land on the same repository, else empty when the journal is only a fence. `remotePrep` is remote preparation in flight or backing off. Its `heldBy` is that current push-gate holder from the status read, or empty. `hostBudget` remains the runner-host budget reason.

A runner-bound task crosses the dispatcher's claim twice (CARD-0672). The first crossing cuts its desktop worktree and starts the preparer, under the repository mutation lease. A later re-arm, when the desktop worktree is already cut, the mirror is not recorded, and the progress baseline is already captured, takes no repository lease: that claim runs no git. A re-arm with no baseline still takes the lease, because capturing the baseline does. The second, once the mirror is recorded, only claims the row and launches into the mirror, and takes **no** lease: a runner session never writes the desktop checkout, and only the leased settlement sync moves it (invariant I-A). Repair, SourceLanding and Interim claims keep the lease on every crossing. A queued task refused the lease registers as a lease waiter; a land about to acquire that repository's lease yields while any waiter is registered (hold `repository_lease_yielded_to_dispatch`), so the dispatcher's next tick takes the lease in the gap. Each land request yields for at most `Delegation:LandYieldToDispatchMaxSeconds` (default 90, 0 disables the yield), so N queued waiters cost the land queue about N × 5 seconds. A registered waiter keeps its turn, and its original age, across a tick whose only hold for that task is a prerequisite of the cut: the same-card base inspection is pending, or the observation went stale at claim (CARD-1427; membership never registers a waiter, only a refusal does). The same-card inspection result is not consumed by being read: a cut the lease refused reuses it on its next attempt, until its intent changes, the claim's revalidation under the lease finds it stale (the task is then re-inspected at once and keeps its turn), the task leaves the queue, or 10 minutes pass (CARD-1428). A sibling-land hold or an unknown-integration result is still re-inspected every time, because only a fresh observation lifts it. The 32-entry inspection capacity counts in-flight work only. `HeldAged` rows and the `DispatchHeld` item carry a per-class wait ledger (`leaseWait=`, `prepWait=`, `runnerWait=`, `capWait=`, `otherWait=`, `class=`; attention `holdClass`) so lease starvation and runner capacity read apart.

Start a new project through `POST /api/projects/setup` (or `scripts/project.ps1 new -Dir ... -Orchestrator -Start`): it creates the project, board, and preset agent in one transaction and returns readiness. `POST /api/agents` remains for adding an agent to a project that already exists.

Create and start an agent through `POST /api/agents` + `POST /api/agents/{id}/start` (or the UI).
Never launch the `claude` CLI directly, and never a `launch-remote` script. When setting
`modelLevel`, send it as the string `"Frontier"` (or `"High"` / `"Medium"` / `"Low"`). A numeric
token (`0`, `1`, `99`) is a **400** — the wire is the member name, not the enum ordinal (CARD-0007).
Frontier maps to fable (Claude) by default, or `grok-4.7` when `Kind=Grok` is passed.
If `delegate.ps1` 409s `model_disabled`, pick an alias from `available` or wait until
`disabledUntil`; do not retry the same kind/tier. If the 409 also says the available list does
not satisfy a routing pin, wait, pass `-IgnoreModelDisabled` to queue, or replace the pin — do
not pick from `available`. Dispatch is sequential-by-default at the gate: a 409 `concurrency_limit` names
this project's occupants, the cap and the `axis` it tripped. On a board worked under §1's standing
policy that policy is the request for cross-stage parallelism: re-send with `-IgnoreConcurrencyLimit`
when the axis is `absolute` and no listed occupant shares the stage you are dispatching; defer when
the axis is `role` or a same-stage occupant is listed. Elsewhere, only when the user asked for
parallel work this turn. Other projects' work never counts against yours.

### Reuse first

**Default: `delegate.ps1`.** Unrelated new work needs nothing special — the warm pool reuses an idle
agent in the same directory (compacted first) and spawns a fresh ephemeral delegate only when none
fits. Sequential follow-up that must keep context: `-OnAgent <taskId>` for a local pool delegate
or a standing agent (already on the script and in the skill). An extant remote pool delegate
refuses follow-up Create with HTTP 422 `follow_up_remote_pool_unsupported`, before inserting a
task: remote pool continuations cannot reuse that process. Publish the intended source, then
create a fresh task with `-Worktree -StartRef <published-sha>` without `-OnAgent`. This refusal
also applies to explicit `-Shared` and `-ReadOnly`; explicit `-Worktree` retains
`workspace_existing_agent_conflict`. A retired Worktree delegate follows the frozen-tip path
described below. Parallelism on one model: let the pool spawn another, or pass `-Worktree`. That *is* the
"2–3 reusable workers per directory+model+tier, scale only for real parallelism" policy, implemented
by the pool rather than by named rows.

The unpinned pool skips sessions whose transcript still reads working. Retirement by TTL or
directory cap protects a working session with transcript activity inside the idle TTL and
restarts its idle clock; a full TTL of silence permits retirement. Shared release warns when
it pools a mid-turn session. This preserves process ownership while a resumed turn finishes.

`POST /api/agents` is for a **standing identity**, not a unit of work: an orchestrator seat, a
channel-bound agent, the check interpreter, or a human-facing named worker that should outlive a
card. Pass an existing `BoardId` (the project's real board). A unique `workingDirectory` that is not
a real checkout is how 21 extra `gym-stat-*` projects appeared.

Work you need to hear finish is a task; creating an agent gives you an identity, not a report.
Pin a task onto an existing named standing agent with `delegate.ps1 -Agent <name|slug|guid>`
(CARD-0291): it queues while that agent is busy, delivers into the live session, and settles with
the normal `[task … done]` note. A raw session message to a child is for steering work you already
dispatched, never for handing over work — no completion note will ever arrive for it. A follow-up
behind a Working task waits visibly (`Held` naming the agent and task; `HeldAged` at 300/900 s;
`DispatchHeld` on the attention feed). A follow-up onto an agent parked on a Blocked task is refused
409 `follow_up_agent_blocked`; its text names the recovery that applies. A candidate park of the Blocked task (a current-attempt published park in Parked or ResumePending) whose linked release Reply admission admits is that 409 and says the published seat was released, whether or not the session row still reads live; reply and do not cancel. When no candidate park gives the Reply or mismatch advice, a live Blocked session is that 409 and names Reply or cancel, and a dead session names cancel and re-send. The 422 follow_up_remote_pool_unsupported fires before the Blocked branch and names that task and Reply only when the Blocked task's current attempt has a confirmed published park whose seat release matches Reply admission, including the settlement revision (CARD-1103). When no candidate park of the task is admitted, a confirmed published park whose seat release does not match that admission does not name Reply; that parked answer cannot be continued (CARD-1103). HasConfirmedPublishedParkAsync is scoped to the task's current attempt and accepts Parked or ResumePending only (CARD-1103). Mark-read records the first ReadAt without changing the task's ConcurrencyToken, the settlement revision a seat release records; a matching confirmed-park Reply after a read still queues one new attempt (CARD-1144). Reply to a confirmed published park whose release identity no longer matches, with no accepted answer pending, returns 409 park_release_identity_mismatch before any old-session input is queued (CARD-1144). The remote-pool 422 guidance and confirmed-park Reply continuation use the same EF queries, RunnerSeatReleaseQueries.ForAttempt for release identity and RunnerSeatReleaseQueries.Confirmed for the confirmed receipt; that guidance remains one database read (CARD-1146). Local 409 follow_up_agent_blocked guidance gives the published-seat Reply advice (the seat was released; reply and do not cancel) only when the shared admission query that Reply uses admits the linked release of a candidate park of the Blocked task, a current-attempt published park in Parked or ResumePending (CARD-1154). Across all candidate parks of that task, any admitted park gives the published-seat Reply advice; otherwise, if some candidate has a linked release in state Confirmed with a non-null ConfirmedAt that fails admission, the 409 gives the mismatch text, which says that parked answer cannot be continued and names neither Reply nor cancel; otherwise the ordinary live/dead advice applies (CARD-1154). Confirmed-park guidance performs one database read per blocked follow-up, independent of the number of candidate parks; the existing session-liveness read is separate (CARD-1154). A retired
Worktree follow-up continues that task's committed tip: Antiphon freezes the local branch SHA and
cuts a new worktree there on the follow-up task's own new branch, reported as its `WorktreeBranch`
(see [Delegate branch names](#delegate-branch-names-card-1213)). It does not reuse the old
directory, reset the old branch, or fall back to master. If that tip cannot be proven, create refuses
`follow_up_source_unavailable` and inserts nothing; pass `-Worktree -StartRef <sha>` to choose a
commit yourself. Explicit `-Shared` or `-ReadOnly` is a new task and is warned as such. A live
follow-up, `-Agent`, or a routing pin that names an existing agent keeps that agent's checkout
(Shared, or ReadOnly when that was explicit). Explicit `-Worktree` together with one of those is
`workspace_existing_agent_conflict`. Cancel retires a pool agent only when the canceled task ran on it and nothing else open still pins it.

---

## 3. Writing a brief

**Runner task branches are fast-forward-only (CARD-0779).** Never ask a runner-bound Worktree
delegate to rebase, amend, reset a pushed commit, or force-push its task branch. The server
publishes a safe unpushed mirror tip at settlement. To bring source current, land the task (the
land rebases onto the target) or dispatch a fresh task with `-StartRef` on the wanted base.
Confirmation-only work belongs in Review, not a Code task requiring a new commit.

**The standing rules are delivered automatically now (CARD-0058). Do not retype them.** A
non-specialist delegate launches with `server/Bundles/delegate-basics.md` composed into its launch
instructions. That file is **canonical**; this section deliberately does not restate its contents, because a rule
restated in three places drifts in three places, and it drifted here first. Improve a rule by
editing that file in a PR: a fresh launch composes the edited text. A warm-pool reuse delivers the
brief into the running session with no launch, so that session keeps the instructions it launched
with. A sub-orchestrator additionally launches with `server/Bundles/orchestrator.md`.

The carrier depends on the provider (`ComposeDelegateArgs`, `server/Application/Services/AgentTaskDispatcher.cs`):
Claude Code receives `--append-system-prompt <text>` and Codex `-c developer_instructions=<text>`.
Grok receives a `GrokRulesPayload`: the runner writes it to a runner-owned `rules.md` and passes
`--rules` with a bootstrap instruction naming that file (`GrokRulesTransport.Bootstrap`;
`src/Antiphon.SessionRunner/SessionRunnerRuntime.cs`). A Herdr-backed session's launch arguments
are written into a launch script (`src/Antiphon.SessionRunner/HerdrLaunchScript.cs`).

| Behaviour | Test | What it asserts |
|---|---|---|
| Claude and Grok carriers equal the expanded composition; Codex carries git-common | `DelegateBundleLaunchTests.C1213_ProvidersReceiveExpandedInstructions` (Antiphon.Tests) | the `--append-system-prompt` value, the Codex `developer_instructions` value and the Grok rules payload contain the git-common render and no `{{`; the Claude and Grok payloads equal the composition |
| Bundles ride the launch, not the brief | `DelegateBundleLaunchTests.bundles_ride_the_launch_and_never_the_brief` (Antiphon.Tests) | the brief has no `[bundle:` header and no delegate-basics text |
| A warm-pool reuse delivers into the live session with no launch | `AgentTaskPoolTests.a_queued_task_takes_over_a_warm_agent_in_its_directory` (Antiphon.Tests) | the task takes the warm agent's live session id; its brief is queued to that session |
| A Herdr launch script keeps arguments intact | `HerdrLaunchShapeTests.Script_content_doubles_single_quotes_is_BOM_and_preserves_newline_args_for_generic_arguments` (Antiphon.SessionRunner.Tests) | quotes are doubled and multi-line arguments are preserved in the script |
| The Grok runner writes the payload to its rules file and passes `--rules` | `GrokRulesAdoptionTests.Restart_recovers_only_a_verified_rules_receipt_and_retains_files_after_worktree_removal` (Antiphon.SessionRunner.Tests) | the receipt file holds the payload bytes; for a non-Herdr child off Windows the captured native argv contains `--rules` |

**Git rules reach delegates as git-common (CARD-1213).** `delegate-basics` carries `{{GIT-COMMON}}`,
so each non-specialist delegate launches with `server/Bundles/Fragments/git-common.md`. Do not
restate those rules in a brief; supply the per-task values only. A runner-bound brief's runner branch
contract prints the branch, base SHA and push command, and restates the rebase, amend, reset and
force-push prohibitions in one sentence, because a recovered session may have launched before
git-common existed; it cites git-common for the remaining rules.

| Behaviour | Test (TUnit, Antiphon.Tests) | What it asserts |
|---|---|---|
| Code, Review, Mutation and orchestrator compositions carry the git-common sentences | `InstructionBundleTests.C1213_GitContractPins` (`Application/InstructionBundleTests.cs`) | each pinned sentence occurs once per composition; the orchestrator bundle contains "Delegates carry git-common; never restate git rules in briefs." |
| The runner branch contract keeps per-task values and the protections | `DelegationReportFormatterTests.Runner_worktree_brief_names_mirror_and_fast_forward_contract` (`Application/DelegationUnitTests.cs`) | for `feat/card-task-7f3a2b91` and `feat/card-1213-code-7f3a2b91`: the exact `Assigned branch:` line, push refspec, ls-remote ref, `fast-forward-only from <40-hex>:` followed by the protection sentence, and the word `git-common` |
| A brief recovered into a session without git-common carries the protections | `DelegationDispatchRecoveryBoundaryTests.C1213_Recovered_brief_keeps_branch_protections_without_git_common` (`Application/DelegationDispatchRecoveryBoundaryTests.BranchProtections.cs`) | for both branch forms, a session whose stamp lacks git-common: the queue's ensure path inserts a brief whose stored payload (read through its spill) holds the protection sentence and exact push refspec; the session records one UserPrompt that completely matches the queued message body, which in this fixture is a spill pointer |

**A stage-role Worker also launches with its own `server/Bundles/stage-<role>.md`** (CARD-0146 S3,
via `InstructionBundles.ForDelegate`), composed alongside `delegate-basics`. That bundle already
carries the stage's **standing** shape: for `stage-plan`, that a design living only in chat is not
a plan, the plan-doc path convention, and the full `next:` vocabulary; for `stage-code`, ordinary V/R and committed SHA with PCs pending; for `stage-mutation`,
every PC red/restore/green and missing-control discovery, with original Code landing owner; for `stage-test-design`, the exact `## Verification design` sub-structure
(Inspection/`V-n`/`R-n`/Guard inventory/`PC-n`/Out of scope/Cost); for `stage-investigate`, that a fix idea is one line under
"Not done, noted", never a design; for `stage-review`, read-only. **None of that belongs in the
brief** — repeating it only costs the delegate's attention. What the brief must still supply is
what no bundle can (below), plus, stage-specific: the **previous stage's `handoff:` line, verbatim**
(that is the sentence the next brief is built from — D2), its `artifact:` path, and — only when the
card is `complexity:easy` and this is a Plan dispatch — the sentence "the test-design stage is
folded into this dispatch; the `## Verification design` section is required."

So a brief is now **the goal plus the state of today**, and the state is the part no bundle can
carry:

- **Name the known-failing tests** and say to verify by stashing. The *rule* is in the bundle; today's
  actual red list is not — it changes weekly, and a bundle naming last week's would be worse than
  silence.
- **Say what is already done** ("slices 1 and 5 are landed as X and Y, build on them, do not redo")
  and **what is out of scope**.
- **Say the warning count, the flaky suites, the ports in use** — anything true this afternoon and
  false next month.
- Give **outcomes, not procedures**. The delegate decides how.
- **OS branch witnesses (Code and Review).** Apply [the mandatory witness checklist](testing-and-build.md#os-branch-witnesses): identify changed OS branches and real production factories/properties, inspect setup and outcome assertions including absent optional inputs, name exact checkpoints and pending native work, and check startup impact before an rc pick. A seam alone cannot satisfy it. Keep required execution incomplete until fresh receipts exist; standing rule 4 and D-2 retain their verdict meanings.
- **A Code brief names the checkpoint list.** Add one line, `checkpoints: <plan artifact path>@<full plan commit sha> section "### Checkpoints"`, the shape the server already renders for an Interim `selection:`. The table stays in the committed plan (owner: [testing-and-build.md](testing-and-build.md), Checkpoint manifest); `-ExpectAbout` is the sum of its `EstimatedMinutes` column (a time budget, not the `Min` execution counts) plus authoring. The delegate runs that table through the checkpoint tool (`dotnet run --project tools/Antiphon.Checkpoints -- run --plan <plan.md>`), one run per committed slice group, then `wait` until the exit is not 75. The `stage-code` bundle carries the obligation to run it as a closed list and to report per row; do not restate that. Since CARD-0589 every row and every other build/test driver waits for a host build slot (`scripts/run-checkpoint.ps1` itself, anything else through `scripts/build-slot.ps1`), so a Cost on a busy host adds the expected slot wait: `GET :17204/build-slots` (server2: the runner container's `:8080/build-slots`) shows occupancy and the queue. Slots compose with seats: CARD-0654 seats decide how many sessions a host runs, slots decide how many of their builds run at once, the live memory floor goes last. A report's `BUILD SLOT timeout` (exit 4) is a row not run, to retry later or end `blocked`; a `BUILD SLOT unleased` line is an unbudgeted run Review reads as such ([testing-and-build.md](testing-and-build.md), Build slots).
- `repeat-proof: at most 3 normal + 2 loaded repetitions per unchanged proof selection; none required after green. Exceed only for a flake already demonstrated by Review; cite that Review, filter, reason and revised budget.` Count total executions per case across the selection; CARD-0885's one-time five-repeat benchmark is its named exception.
- **Pass `-Stage` on a Debug/Docs/Custom dispatch that is actually answering a landing-step
  question** (CARD-0272) — a title like "verify CARD-nnnn still builds" or "clean up the stale
  worktrees" gets `-Stage Verify` / `-Stage Cleanup` even though the role itself never defaults
  one. Review/Test/Merge/Deploy and an `-OnAgent` follow-up already get a stage from their role;
  Code and Plan never do, and don't need one. A staged dispatch's brief carries a self-report
  finding line — `[antiphon-finding:<id> found|clean]` on the line before the report token — and
  `delegate.ps1 -Finding <id> -Stage … -Found "…"` / `-Clean` is the orchestrator's override when
  it judged differently. Full flag reference: `.claude/skills/antiphon-delegate/SKILL.md`.

The test of whether this worked is mechanical: briefs are stored on task rows, so sample the next
week's and grep for the six rule-paragraphs. If they are still being typed, the inversion did not
take and this section is still too gentle.

Bundles reach a delegate **at launch**. A warm pool delegate keeps the bundles it started with until
the pool retires it (`Delegation:PoolIdleRetireMinutes`, configured default 60 idle minutes). A
standing agent's refresh lanes and refusals are in the policy-refresh table of
[`server/Bundles/README.md`](../server/Bundles/README.md). If a rule change matters urgently to a
pool delegate or work in flight, say it in the brief for that dispatch.

**Do not shorten a goal to avoid the spill (CARD-0353).** An ordinary brief over its inline ceiling is
typed as a pointer instead of inline (`AgentTaskDispatcher.FitBriefForTyping`); under the default ceilings an ordinary Grok or
Codex brief takes a pointer by design (CARD-0084/CARD-0099 default-deny). Shortening the goal drops
context the delegate needed and fights §0's rule that the orchestrator hands over full context.
A pointer is not evidence that the payload was read. Wire submission needs a transcript UserPrompt
that completely matches the submitted body; consumption needs separate evidence that the payload
was readable and retained, or that the delegate read it. Before classifying a session that shows
only a pointer, inspect its transcript and its spill and recovery state; §4 covers boot stalls.

| Behaviour | Test (TUnit, Antiphon.Tests) | What it asserts |
|---|---|---|
| Grok's inline brief ceiling is 0 bytes | `GrokDeliveryShapeTests.Narrowing_for_a_joining_composer_touches_the_inline_ceiling_and_nothing_else` | on InboxConhost and ModernConPty, `ForAgentKind(Grok)` gives `BriefInlineMaxBytes` 0 |
| Codex's inline brief ceiling on ModernConPty is 0 bytes | `GrokDeliveryShapeTests.An_unmeasured_kind_is_join_safe_by_default` | on ModernConPty, `ForAgentKind` for Codex, OpenCode and Raw gives `BriefInlineMaxBytes` 0 |
| A pointer whose local spill is missing, corrupt or unreadable is not submitted | `DelegationDispatchRecoveryBoundaryTests.C1150_Local_spill_must_be_present_and_intact` | the ensure path returns Unavailable; the task is Blocked with the input-unavailable reason; the row stays Pending; the session has zero UserPrompts |

---

## 4. Checking on a delegate

On completion, treat `review-evidence-warning=review_evidence_sha_not_review_base` and
`review-evidence-warning=review_evidence_subject_tip_mismatch` as reasons to inspect the
Review's checkout base, named subject and pushed tip before land. A `progress=none` warning
means a non-Code Worktree task reported success without attributable pushed progress;
inspect its branch before accepting a claimed artifact. The warning does not change the
task's settlement status.

A `ParkedMessage` attention row on a finished task now clears itself within roughly 10 minutes: the
queue sweep discards the stale machine-origin message rather than retrying it. A parked row that
remains is deliberately one whose content may still need a human decision (for example a UI/channel
message, a completion note, or work on a session with an open task).

**Check-ins are automatic now (CARD-0047, slices 1-3).** Delegate with `-ExpectAbout <minutes>`
(1-1440, defaults to 10) and the server arms a schedule: a deterministic, read-only probe of the
task row, the delegate's session/transcript, its queue and its incidents — plus its git log for a
worktree task — lands in your session as a `[check <id> #n] ...` note, first around the minute mark
you declared, then backing off along a Fibonacci ramp fixed from a 5-minute base (5, 10, 15, 25, 40,
60, 60 …, capped at 60 minutes — CARD-0061) for up to 10 checks. Every gap is rounded to a
human-readable number on the way out — nearest 5 below 30 minutes, nearest 10 from 30 to 60 — a
separate step from the ramp itself, so it keeps the schedule legible even if the base or the ramp
change later; the shipped sequence above is already round, so this doesn't move it. The declared
duration schedules only the first check; it no longer scales the ramp. It costs no model call today
and cannot write to the delegate at all.
**A check note is a progress report, never a completion** — the delegate's own `[task <id> done]`
note still arrives separately, and a check note can never be mistaken for it or settle anything
(see `.claude/skills/antiphon-delegate/SKILL.md`).

**Elapsed counts from the latest reply (CARD-0348).** The check header's `elapsed` bit and the
duration on a completion note use `max(DispatchedAt, RepliedAt)`. When a reply reset the clock, the
header adds `after reply; dispatched … ago` and the completion note carries both `since reply` and
`since dispatch`. An orchestrator judging a stall reads `elapsed` together with `activity` —
`elapsed` after a reply is "how long since we answered", not "how long since dispatch". This is not
`AgentSession.LaunchResumedAt` (CARD-0340's interrupted-launch clock).

**A session showing only its own prompt, and WORKING, is a provider stall (CARD-0353/CARD-0312).**
The Overdue attention row states the prompt's age, the boot-notice and operator thresholds, and the stage.
The check digest names the stall — a `BOOT TURN` line on `SESSION`, and a `DEADLINE:` line
naming `BootModelWait` — and the harness only DETECTS it (CARD-1151): it never fails, stops,
retries or releases the session, and holds no alias.
A transcript-confirmed boot prompt with no model reply is detection only while the session is Working, runner-listed, or safety evidence is unknown. At eight minutes it records BootStallDetected; at the bounded operator threshold (20 minutes with defaults) it asks for an operator decision without failure, retry, input, stop or seat release.
The general and role deadlines do not terminalize that unresolved boot episode; positive model progress returns it to ordinary deadline policy.
The task's events carry `BootStallDetected` and then `BootStallNeedsOperator` Warnings, and its
Overdue attention row turns Error at the operator threshold.
The operator threshold is `prompt + max(positive boot wait, operator wait)`: the boot wait is `Delegation:BootModelWaitDeadlineMinutes` (zero when `<= 0`), and the operator wait is `Delegation:ModelWaitDeadlineMinutes`, or 20 minutes when that is `<= 0`, so a disabled model-wait deadline still waits for a boot wait above 20 minutes.
The decision is yours: inspect the
session, then keep waiting, reply, or explicitly cancel or retry the task (expect the same
provider on a retry). `Delegation:BootStallRepeatHoldMinutes` is retired and holds nothing. Owner
and test pins: [session runtime invariants](session-runtime-invariants.md) ("A boot stall is
detection, never a stop"). For a Grok session, `~/.grok/sessions/<id>/events.jsonl` (`phase_changed: waiting_for_model` with
no `first_token`) and `~/.grok/logs/unified.jsonl` (`shell.turn.inference_start` with no
`inference_done`) are the diagnostics; see `docs/agent-kinds.md`.

**A taskless AlwaysOn session showing only its boot prompt is reported, not recovered (CARD-1156).**
A taskless AlwaysOn boot stall is detection, never a stop (CARD-1156, operator decision option A): the boot reply watchdog records at most one Warning receipt per episode (accepted generation, launch clock and boot prompt sequence) from the boot due (`Delegation:BootModelWaitDeadlineMinutes`, 8 minutes) and at most one Error receipt from the operator threshold (20 minutes with defaults), each in its own context and transaction, keeps the watch armed, and never stops, kills, fails, restarts, latches or types into the session or writes its supervision state.
A hung taskless AlwaysOn boot is reported, not recovered: the session keeps running and keeps its seat until the model replies, an operator acts or the process exits; CARD-0079 remains the only automatic stop of a Working session, and no deadline releases the seat.
Its `LivenessProbeFailed` attention row is Warning from the boot due and Error from the operator
threshold, both computed from the session's current boot facts; its evidence gives the prompt time,
its age and both due times. The decision is yours: inspect the session or its
transcript, then keep waiting, reply through the session, or explicitly Stop and Start/resume the
agent. Owner and test pins: [session runtime invariants](session-runtime-invariants.md) ("A taskless
AlwaysOn boot stall is detection, never a stop").

**A session past the general deadline is Failed, not recovered, when it has ingested rows.** The model-wait (20) and local-execution (90) clocks and the 240-minute ceiling fail the task without killing or retrying; the reason names the clock and the session. Bind-refusal recovery is not attempted on a session that has ingested rows — an overdue mid-turn worker is still working, not an unbound success (CARD-0551). The session was NOT killed; read it before you decide.

**A Check-role task settles Succeeded when it has produced a reading (CARD-0302).** `LOOKS STUCK` /
`BLOCKED` in that reading is evidence on the **checked** task (its Check event / parent `[check …]`
note), never the Check row's own `Status`. The interpreter's job is the reading; finishing it is
`done`, not a question for the operator. Reply or Cancel on a Check row is unsafe — those verbs
enqueue into or kill the standing `antiphon-check-interpreter` session. Empty or `failed`
interpretations stay degraded (no reading on the checked task). Do not parse LOOKS STUCK as a
classifier input; PastExpectedIdle / ChecksSpent / DeadSession already surface actually-stuck
delegates and already attach the latest interpretation as evidence.

**A task that fails before it is ever dispatched still gets the ramp (CARD-0231).** CARD-0220 already
sends a one-shot `[task <id> failed]` note through `FailAndNotifyAsync`; that note can sit unsubmitted
or be lost, and the orchestrator bundle says not to poll. The same `NextCheckAt` / `CheckCount`
columns now arm on a never-dispatched failure (first look at the 5-minute ramp base, not
`-ExpectAbout`, because that number describes work that never started) and a sweep re-sends the note
only while nothing shows the caller has heard — note `Sent`, a human Drop, `delegate.ps1 -Status`, or
opening the task drawer. It is a reminder, not a check: no probe, no interpreter, no session. While
the reminder is armed the attention feed lists it as `FailureUnacknowledged` in the **Broken** group
(counted; not a push, not an alert). The ramp stopping after 10 reminders is not acknowledgement —
the row stays until something hears.

**Do not infer from silence** as general practice still holds, but the specific cause behind the
worst observed lags is now found and fixed, not just worked around. It was never a slow pipeline —
task `817682e9` traced the 90-minutes-late and never-arrived notifications to CARD-0055's root
cause: a queued completion note was marked Sent as soon as the screen merely redrew after Enter, so
a swallowed Enter left the note sitting unsubmitted in the composer (one sat there 104 minutes,
only reaching the transcript when a LATER delivery's Enter pushed it in), and a second note was
lost outright when its own Enter resubmitted the first note's stale body instead. CARD-0055
(shipped `4bb65fb`..`165da34`) fixes it at the source: a delivery is now Delivered only when a
matching `UserPrompt` transcript record appears, with Enter re-presses and a late-confirm brake
against double-submission — see the CLAUDE.md gotcha for the mechanism. The schedule above and the
manual probes below still earn their keep as a safety net and for a faster look, but they are no
longer standing in for this specific defect.

Cheapest first:

```bash
# has it settled?  (one request, no model; CARD-0515 envelope is { scope, items, excluded })
curl -s localhost:17202/api/agent-tasks | python -c "import json,sys; d=json.load(sys.stdin); items=d['items'] if isinstance(d, dict) else d; ..."   # filter .items by short id

# the stored report — available BEFORE the notification
pwsh -NoProfile -File scripts/delegate.ps1 -Status <id>

# the truest progress signal: commits exist before reports do
git log --oneline master..<worktreeBranch>   # the task's recorded WorktreeBranch; see Delegate branch names

# what it is doing right now
curl -s localhost:17202/api/sessions/<sessionId>/transcript
```

**Traps:**

- Task detail and `delegate.ps1 -Status` include a read-time `Session:` line: status, working/idle
  and last transcript time, or its end time. Failed/Blocked completion headers carry
  `session=live-working|live-idle|ended`, observed before release. The API-error failure reason
  also names that observation. `Recovery ended (...)` can still describe an exhausted or
  human-required recovery without a later real prompt; it cannot settle from an older stub
  after a real resume. Task terminal status alone does not establish that its session ended.
- **Do not scan processes by command-line substring.** A scan for `*bin-c45*` matched *the scanning
  command itself*, "finding" a runaway agent that was the orchestrator's own session, and the
  follow-up kill terminated its own tool shell. Exclude self and ancestors, or key on session
  ownership.
- **Do not trust the agent's account of what it left behind.** One reported its spec "untracked, not
  committed" when the commit existed and was pushed. Check the repo.
- **`*.ansi.log` files are raw pty capture**, not structured logs. Grepping them yields escape
  sequences. Use `GET /capabilities` and the API instead.

---

## 5. Order the landings and read the outcome lines

### Runner sync outcomes (CARD-0779)

The task's dispatch baseline is B, origin's exact owned-branch tip is S, and its runner mirror
tip is T. `runner_no_pushed_progress` means S and T stayed at B; a Code task needs new attributed
work. `runner_mirror_unavailable` in evidence means the runner could not be inspected or lacks
`workspacePublishV1`; inspect that runner and its retained mirror. `runner_mirror_publish_failed`
means T descends from S but its plain push failed; repair the runner's push access and retry
settlement. `runner_mirror_diverged` means T is rebased, reset or behind S: keep the mirror,
dispatch a fresh Code task pinned to that runner with `-StartRef` at the wanted base, cherry-pick
the retained commits from the old local branch, and push the new task branch. `runner_sync_diverged`
means S itself diverged from B after a force-push. Review that exact pushed S in a Worktree Review
with `-StartRef <S>`, then use `delegate.ps1 -Land <owner> -ExpectedSourceSha <S>
-ReviewEvidenceId <guid> -RecoverReviewedSource`. The last recovery is explicit; settlement does
not adopt a rewritten branch.

CARD-1082. Delegation:RunnerSyncDebtOnSettlement defaults to true. A complete report whose only desktop failure is runner_sync_lease_busy, with a full server-observed tip, records progressEvidence.remoteSync.state Pending. Pending is never Confirmed. A debt row is created only for the lease-contention case of a complete report that settles Succeeded or Failed. Dirty, diverged, and unfetched Code stay Blocked with no debt row. The policy accepts only the task's own owned ref as the observed tip, and a Code report with no progress evidence under Pending stays Blocked. A Blocked lease-busy settlement's warning says Runner sync unavailable and then reply, never Runner sync pending; its evidence still records Pending. Pending evidence without a debt row means the task stayed Blocked and a reply is required. When the settlement is not blocked, the report's next= is kept, the caller warning says synced later, and it says No reply is needed. The workspace note contains desktop-sync=pending. -Land uses the pushed branch the same way it does for a synchronized owner. The sweep does not read the kill switch. It retries a Pending debt, and backoff is 1, 2, 4, then 5 minutes. An empty debt table is 1 statement per tick. A synchronized settle is 30 statements and debtSql 0. A pending-review retry is 40 statements and debtSql 1. The kill switch off is 31 statements and debtSql 0. Held debt raises attention SessionDisagreement keyed settlement-sync-debt: and names the recovery. The task attempt, baseline or worktree changed; this debt will not be fast-forwarded. Origin moved past the recorded source; the sweep does not follow it. The desktop checkout is dirty. This debt stays Held. Review that exact pushed S in a Worktree Review with -StartRef is the diverged recovery. Pending debt older than the threshold warns desktop sync still pending (lease contention). RunnerSyncDebtAttentionMinutes defaults to 30. Ready, Superseded, and Pending that is not yet older than that threshold raise nothing. Operators read syncDebt on task detail. A Held debt is re-checked every 60 minutes by restamping NextAttemptAt 60 minutes ahead: the re-check reads no filesystem and matches no path, runs no Git, fast-forwards nothing, and does not supersede, because AgentTaskSyncDebts has no registration id that a complete retirement can be bound to. The attention warning stays until that binding exists.

**`next: land` in the completion header is the cue to run `-Land`** (CARD-0146 D2/§1) — read it
off `next=land` rather than deciding from the report body that a stage is done. `next: code` or
`next: review` naming remaining work is the cue to dispatch that stage instead; `next: decide`
routes to `AskUserQuestion`, not to a land.

**Landing order comes from the completion header.** A note whose header carries
`overlapping-running=<ids>` says those tasks were still running when this one settled and touched
the same areas. Land this branch first, or expect its rebase to replay onto their work
(CARD-0063).

For a succeeded Worktree task, make the ordered landing decision with one call:

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Land <original-code-task> -ExpectedSourceSha <full-reviewed-sha>
# Optional: -ReviewEvidenceId <review-outcome-guid> -Verify "<treenode-filter>"
```

The server performs the fetch, rebase, conditional build and optional named test, fast-forward,
push, worktree removal, and branch deletion. The request is a row (`LandRequestedAt`); the
in-process channel is only a hand-off. A restart re-runs anything still pending that this
process does not hold. `delegate.ps1 -Land` prints "Queued land" or "Requeued land" from the
202 `{ status: "queued" | "requeued" }`. A 409 `land_running` means a land is running in this
server now: wait for the outcome event. For another 409 code, see the conflict-code table in
§0 ("Also delegated: the landing mechanics"). Three interrupted
attempts refuse (`LandRefused`); `-Land` again starts a new request. A `Warning` "did not
finish (server restarted); re-running" is informational. A land yields at admission, before it
takes the repository mutation lease, while a queued dispatch waits for that lease (hold
`repository_lease_yielded_to_dispatch`, CARD-0672); the sweep re-picks it within
`Delegation:LandSweepSeconds`, and after `Delegation:LandYieldToDispatchMaxSeconds` (default 90)
it proceeds with one `Warning` "yield budget exhausted"; admission ends the budget, so a retried
request yields afresh. The yield is not a caller note. The waiter a land yields to persists
across the dispatcher's inspection-pending and stale-observation ticks (CARD-1427), and the cut's
same-card inspection result survives the refusal (CARD-1428), so back-to-back lands give a queued
worktree cut its turn within one land interval plus one yield budget; the land queue's position
reporting and sweep re-pick are unchanged.

The in-process channel is one global single reader, not a queue per repository. A LandAged
note for a request that is still waiting reports that snapshot. Position is one-based among
waiting requests only: the request the worker is executing has no waiting position, the next
waiting request is position 1, and the one behind it is position 2. The note also carries the
waiting count and the time the snapshot was observed. The executing predecessor is named from
that snapshot. A predecessor for a different repository is a queue blocker, not this
repository's lease owner. A durable pending request this process has not replayed says
`queue position=unknown (awaiting replay)` and does not treat a stale database Running row as
a live holder. A blank `holder= ()` is not a diagnosis. Time waiting still starts at last
progress, not at dequeue.

A held land request tells its caller once per holder, not once per hold episode (CARD-0641).
Each change of hold reason or holder is still a `Held` event with its own `HoldEpisode`,
`HeldSince` and aging clocks. The caller note is gated by the request's
`HoldNotificationOwnerKey` anchor: null means no Held note yet, `unknown` means one was sent
without an identified owner, `task:<guid-N>` names the known holder. The first hold of a
request always notes. A later note is minted only when a different known task holds it. A
reason change, a lease reacquisition by the same task, or an unknown/untagged observation
sends nothing. Unknown refines to a known task without a note, and never erases a known
anchor. Anchor, event and note commit in the same task-locked transaction, so a rolled-back
hold does not consume the first note. The anchor survives restart, admission and completion.
A new request starts at null. A request that already has Held notes from before the column
existed adopts the current owner, or `unknown`, and does not replay them. It adopts on its
first post-upgrade evaluation even when nothing about the hold changed, so a later takeover
is still compared against a real anchor. Existing notes are never rewritten or deleted. A
lease tagged with a task id names that owner even when the task row is missing.

A present `.git/index.lock` in a checkout the land is about to mutate holds the request
before admission (`git_index_lock_stale` when the file is at least five minutes old with no
git process started at or before it; `git_index_lock_held` when it is younger or a candidate
git process is still running). The hold consumes no attempt; the 5 s sweep re-picks the
request, so deleting the file resumes the land without a new POST. The same codes refuse
in-protocol immediately before `rebase` / `rebase --abort` / `merge --ff-only`. Hold and
refusal detail name the lock path and the exact `Remove-Item` command. The pipeline never
deletes an `index.lock`, including after a timeout kill of a GitWorkspaceService child.
`GIT_OPTIONAL_LOCKS=0` stops reads from creating one; any leftover lock is surfaced on the
next land. `update-ref` advances are not blocked by a lock in a checkout
that does not have the target branch.

CARD-1536: a source `index_lock_path_error` may reach published cleanup only when the
active operation has a valid publication receipt and matching task, repository,
source/target refs and common directory, and the source is a canonical ordinary
directory with a positively absent `.git` entry and no matching live registration.
Failed observations and aliases retain the hold. This exception still probes the
dedicated land and main target checkouts. It grants no deletion authority: guarded
cleanup rechecks receipt, pins and remote containment, preserves the replacement,
and records `CleanupRetry` / `Refused` / `unregistered_directory` on the same operation
with a completed request and a `LandingCleanup` event, without another publication.

An explicit retry of an eligible terminal `Refused` operation creates a fresh operation,
even when the source is unchanged. It repeats validation and verification as required,
retains the previous operation and recovery pins, and may refuse again if the target or
other prerequisites remain unsafe. Target repair or restart alone does not authorize a
retry. The one exception is a schema-3 operation refused `remote_changed_before_push`:
the same request replaces it and rebases onto the new tip, up to
`Delegation:LandTargetRaceRetries` (default 2, 0 disables), under the same lease and
attempt. Each retry writes one `Warning` "raced with a push to `<remote>:<ref>` (retry n of N)".
When the budget is spent the refusal names the count and the answer is `-Land` again with
the same SHA, never a hand push of `master`. `push_rejected` means the remote did not move;
`-Land` again resumes that push. Publication recovery and guarded cleanup retain their
existing operation and evidence.

Read the task's structured `landing` evidence and the outcome's operation ID, original source,
verified commit, observed remote commit, confirmation time, mode and cleanup status.
`AlreadyPresent` records independent containment without claiming a successful push.
`LandedWithResidue` records publication with incomplete cleanup. Re-POST retries guarded cleanup;
its `LandingCleanup` event updates the same publication rather than counting another one.
A schema-3 `LandRefused` leaves the task branch, the task worktree and the canonical checkout
as they were: schema 3 never moves the local target before publication. Its evidence names
the last acknowledged checkpoint. A schema-2 row (pre-2026-09-25) may already have advanced
local `master`; reset that checkout to `origin/master` with fetch and `reset --hard`, then
`-Land` again. Missing source components alone never prove success.
A `Landed` line that also carries
`unlanded-sibling=<id>:<branch>` (comma-separated if several) means a same-card kept branch is
not present in the pinned verified SHA by patch id (`git cherry`) — land or drop that sibling; the
server warns rather than refusing. A rebase-landed sibling no longer appears. An
inspection-unknown Warning is separate from `unlanded-sibling=`: the server could not prove
whether the branch is contained. Inspect its history and decide on integration or disposal;
successful publication and cleanup are unchanged.

After `-Land`, the orchestrator's own git involvement is **zero**. Do not re-run `git show`,
`git diff`, `gh run view`, or tests to double-check a `Landed` outcome. This is the same
trust-the-report rule used for content review, extended to the server-measured land step itself.
If the outcome raises a real question, dispatch a follow-up or `Review` delegate rather than
inspecting the branch directly.

A refusal is a judgement call, not a silent gate: dispatch a follow-up to repair it, defer the
landing, or drop the branch.

**`-Land`'s outcome auto-delivers to the caller session**, via the same `SessionMessageQueueService`
`WhenIdle` mechanism used for a task's own `[task <id> done]` completion (see
`AgentTaskLandService.DeliverAsync`). Wait for it; do not run a manual `GET /api/agent-tasks/{id}`
poll loop after `-Land` — that only produces a duplicate delivery of the same outcome (CARD-0386).

**Landing records stage outcomes (CARD-0272)** for Rebase, Verify and Cleanup as applicable.
Skipped verification names its reason. Cleanup retries add no second Rebase/Verify result.
Publication with residue records Cleanup Failed while retaining confirmed publication.
Historically, before CARD-0328 shipped
`LandedWithResidue` as its own event (2026-09-03), a "could not delete branch/worktree" cleanup
failure was reported under `LandRefused` too, which read as "did not land" even though the target
had advanced — the plan behind this card flagged that as 9 of 47 backfilled runs misclassified.
CARD-0448 F2 retires the background backfill from that older event prose: historical `Landed`,
`LandedWithResidue` and `LandRefused` text cannot prove which steps succeeded. Existing backfill
rows remain historical projections and are not landing receipts or deletion authority. The
landing operation owns new structured stage rows; no event prose is reparsed into green rows.
`GET /api/stage-outcomes` (via
`scripts/stage-value-report.ps1`, §9) is where these land automatically alongside delegate
self-reported and orchestrator-overridden stage rows for Review/Test/Merge/Deploy passes.

---

## 6. Deploy

Deploy remains an orchestrator decision: batch and order restarts around the landings. Execute the
local deploy through one script:

```powershell
pwsh -NoProfile -File scripts/deploy-local.ps1
```

It restarts the AppHost, waits for health, runs the local stack verification without a browser
smoke, and checks the live EF migration history against `server/Migrations/`. Its final line is
the deploy result: `DEPLOY VERDICT: ok` or `DEPLOY VERDICT: failed <detail>`. Read that one line;
do not reconstruct the former multi-command deploy sequence by hand.

For a separately scoped `am-service` remote instance in the fixed
`/home/mc/antiphon-messaging` layout, select its SSH destination explicitly:
`pwsh -NoProfile -File scripts/deploy-am-service.ps1 -SshTarget '<user>@<confirmed-host>'`.
Replace the placeholders with the confirmed destination; there is no default target.
See [remote messaging operations](telegram-bot-ops.md#deploying-an-optional-remote-messaging-service).
Once a Deploy-role brief explicitly
authorizes `-Deploy`, that Deploy-role delegate may run this script and report its final
`REMOTE DEPLOY VERDICT`; it may not reconstruct the SSH/tar/Compose sequence ad hoc. Its default
run is a read-only preflight. Its Telegram round-trip check is manual and operator-initiated
in Family, whose binding is configured in the channel settings (CARD-1243, 2026-10-14).
A test message reaches a real family group; an agent must not send test traffic there on
its own. Deployment authorization does not authorize test messages.

---

## 7. Close the card — orchestrator writes the verdict, haiku executes it

For implementation cards, Done may precede Mutation only after ordinary Review, confirmed
publication, a linked pending companion and all explicit deployment/acceptance conditions.
Name pending verification in the close reason; never call pending PCs clean. The companion
has its own explicit disposition and cleanup (CARD-0478 below).

Split by what each part actually is:

- **The verdict is judgement and stays with the orchestrator.** It is synthesis across the whole run
  — what shipped, what was corrected, what was disproved, what is still open, which other cards it
  touches. A haiku agent cannot see that from the repo.
- **The move and the cleanup in §8 are mechanical — delegate them.** Hand the agent the verdict text
  and the card identifier; it runs `pwsh -File scripts/card.ps1 close CARD-nnnn -ReasonFile <path>`
  (or `-Reason` for something short) and reports the result. Closing cancels the card's unstarted
  delegates and leaves started ones with an attention row (CARD-0738), so close after the last
  delegate settles, or expect a Canceled note for anything that had not started.

- **A terminal move preserves its `reason`; use it as the verdict** — what shipped, what was
  corrected, what is still open, with commit hashes.
- **In Progress and Review are no longer yours to move by hand (CARD-0040).** A task bound to a card
  moves it: to In Progress when the task dispatches, to Review when the last open task settles
  `Succeeded`, within 60 s either way, with `card-transitions` as the actor on the revision. Bind it
  by leading the brief's title with `CARD-nnnn` or by passing `delegate.ps1 -Card CARD-nnnn`, which
  prints `- bound to CARD-nnnn` at dispatch so a mis-binding is visible immediately.
- **Done is still yours**, and so is any move you make on purpose: the sweep only acts on evidence
  NEWER than your last move, so dragging a card back with a reason is respected until the next
  dispatch. Nothing automates Review → Done.
- **A move into an active column used to spawn an agent silently — CARD-0051 made that opt-in.**
  Two dead sessions and a stray worktree came from one such PATCH before the fix. `card.ps1 move`
  (and the API underneath it) now only starts a session when `-Spawn` / `spawn: true` is passed, and
  says so when it suppressed one. Muscle memory from before the fix should assume nothing starts
  unless asked.
- Corrections to a card's text go through `card.ps1 edit` (`PATCH /api/cards/{id}/content`,
  CARD-0019), which records a revision with a reason. Before that shipped, a wrong card could only
  be corrected by filing another card.
- **`scripts/card.ps1` is the preferred way to touch a card from a shell now** — identifier
  addressing, the limits endpoint, and file-backed text are documented in its own header comment,
  which is canonical; see also the AGENTS.md synopsis. `server/Bundles/board-api.md` (attached to an
  agent that works the board directly) still documents the raw API for callers that can't shell out
  to the CLI — fix a wrong rule there, not here. Everything *around* the card — which agents exist,
  what they are running, a board's columns, a live session's transcript — is
  [ops-http.md](ops-http.md); read it rather than grepping the endpoint files for a route.
- **Card files are opt-in publications (CARD-0408), generated one-way from the board.**
  Edit the card, never generated files. Boards default off; Unknown repository visibility
  blocks publication. Private notes never export. Revocations reconcile existing files even
  on archived/off boards; disabling the feature freezes cleanup. Exact-path commits never
  push. The full policy, pending-cleanup and ignore contract supersedes CARD-0004 here:
  [card-file-privacy.md](card-file-privacy.md).
- Findings that outlive the card go in `docs/investigations/`. Agent scratch output lands in
  `.antiphon/`, which is **gitignored** — an 11 KB proven root-cause writeup was nearly lost that way.

---

## 8. Clean up

For delegated Worktree tasks, worktree removal and branch deletion are the `-Land` operation's own
job. Do not run them as a manual orchestrator step after a `Landed` outcome. A
`LandedWithResidue`, or `AlreadyPresent` with incomplete cleanup, can be retried with `-Land`.
The retry refreshes remote proof and validates every surviving component before removal,
then emits `LandingCleanup`. Refusal preserves remaining components and recovery pins;
it never restores components that were already absent or independently removed.

```powershell
Get-ChildItem C:\src\Antiphon -Recurse -Depth 3 -Directory |
  Where-Object { $_.Name -like 'bin-*' -or $_.Name -match '\s$' }
```

A directory whose name ends in a space must be deleted via the `\\?\` prefix; normal path APIs
cannot open it.

---

## 9. What to automate first

**Scheduled prompts have shipped** (CARD-0057 phase 1, `scripts/schedule.ps1`) — a `Daily` schedule
to the orchestrator is how the unmerged-branch sweep from §1 runs. The check-in timer (CARD-0047)
and the card CLI (CARD-0051, `scripts/card.ps1`) have shipped. In rough order of payback for what's left:

1. **The unmerged-branch sweep** from §1 — a `Daily` schedule to the orchestrator that reports genuinely unapplied work.
2. **A post-merge deploy script** that reads the diff and decides which restarts §6 requires.

**Per-stage hit rate vs. cost has shipped** (CARD-0272, `GET /api/stage-outcomes`). Read it with
`pwsh -File scripts/stage-value-report.ps1 [-Since iso] [-Until iso] [-Stage name] [-Card CARD-nnnn]
[-Json]` — a table of runs, found/clean/skipped/failed/unreported, hit %, USD spent, USD per
finding and server seconds per stage. Use it before deciding whether a stage-shaped dispatch (§3)
is worth its cost, not just when the card asks for a report.

---

## 10. Distiller prompt review (CARD-0330)

**Report custody and delivery (CARD-0419).** Four limits have separate purposes:
`DistillMinChars` defaults to 4,000 UTF-16 string characters (a rough reading
preference, not tokenization); `DistillMaxRawChars` remains 20,000 inclusive for
model work; the existing summary gates remain 1,500 characters / 0.6 ratio;
transport retains its 3,000 / 14,400 character profiles and final UTF-8 byte guard.
An explicit minimum override (including 1,200) still wins. Internal specialist
goals need room for the prompt wrapper around 20,000 raw characters: the
`AllowWrappedSpecialistGoal` migration widens task Goal storage to text; public
create still accepts at most 20,000 characters. Downgrading this column requires
review of retained wrapped goals; do not truncate them to make a rollback pass.

Settled non-specialist Session replies at or above the configured minimum retain
an exact UTF-8 copy, independent of mode, availability, gates and model input cap:
`<main-checkout>\\.antiphon\\reports\\<full-task-guid>\\<exact-sha256>.md`.
Linked worktrees resolve to their persistent primary checkout. Unsupported or
non-Git layouts may use an absolute `Delegation:ReportStorageRoot`, for example
`C:\Antiphon\reports`; temporary directories and worktrees are refused. This
setting grants no access. Repository destinations must be ignored and untracked;
the store may add a narrow local Git exclude, never edit tracked `.gitignore`.
Reports survive service restart and delegate worktree removal while the main
checkout/root is retained. They have no TTL, are not disk-loss backups, and must
be excluded from generic scratch cleanup while task rows reference them.

Canonical storage wins when available. When unavailable, an oversized report
still runs the legacy transport backstop and populates `ResultFilePath`, including
target Session replies. A pre-existing delegate-authored legacy file is preserved;
Apply advertises it as the full report only if its exact bytes equal `Result`.
Storage failure keeps settlement and its authoritative raw Result. No historical
settled rows or sent notes are backfilled.

Timely gate-approved Apply replaces the original pending, unattempted note with
its header, summary, verified absolute report path and deterministic deliverable
markers. File validation happens before delivery/DB locks; path and raw identity
are rechecked inside them. An unavailable/corrupt/changed file gets the task API
recovery pointer. Rejected, unavailable, busy, expired or lost optional work keeps
the raw/marked-excerpt fallback and finite hold. Shadow leaves the raw note;
disabled invokes no model. A matching parent API poll keeps its existing
suppression semantics. File reads stamp neither `LastPolledResultHash` nor
`FullReadAt`: the latter measures API reads, not total full-report readership.

**Live acceptance is a separate gate.** Production remains Shadow. CARD-0419
closure/general Apply rollout still requires CARD-0392 reviewed recovery evidence
(or an explicit narrower release decision), acceptance of the optional raw
fallback, and CARD-0330 human rollout governance below. The isolated canary is
`OutputDistillationApplyCanaryTests.Real_apply_and_long_fallback_reach_parent_and_files_survive_cleanup`.
Its **operator** hand-writes
`C:\src\Antiphon\.antiphon\acceptance\card-0419\approval.json` only after a
scoped human decision, using the committed
[approval example](examples/card-0419-canary-approval.example.json). That example
is deliberately invalid approval. The operator replaces the example reference,
sets the approved UTC window, names Review/ClaudeCode/the approved current Low
alias, and records an allowed availability check within five minutes of launch.
Set `ANTIPHON_DISTILLER_CANARY_APPROVAL_FILE` to that absolute file and both
`ANTIPHON_HEADED_TESTS=1` and `ANTIPHON_DISTILLER_APPLY_CANARY=1` for the explicit
test. The test never writes its own approval, weakens a hold, or reroutes a model.
The fixture owns its database, random runner and manifest directory, uses a
refusing external-message adapter, and restarts only its own host. See the full
[canary procedure and evidence requirements](superpowers/plans/2026-09-07-card-0419-distillation-delivery-plan.md#mandatory-real-apply-evidence).
An unexecuted, skipped, rejected, expired or incomplete canary is pending/failed
acceptance, never a passing deployment verdict.

A second standing haiku seat, `antiphon-output-distiller`, distils finished delegate reports
after they are written. It is furniture on the agents page beside `antiphon-check-interpreter`
and `antiphon-diagnose`. The seat's prompt is `server/Bundles/output-distiller.md`. A human
merges every change to that file; nothing else may write it, the row's `SystemPromptAppend`,
or the live session. The gates in `OutputDistillationGate` are code and are out of scope for
this loop.

**Shadow → Apply.** `Delegation:OutputDistillerMode` ships as **Shadow**: every eligible
report is distilled, a ledger row is written, `DistilledResult` is stored on the task, and the
queued `[task … done]` note is left untouched. Flip to **Apply** only after a week of ledger
(`scripts/distiller.ps1 -Stats`). In Apply the held note's body is replaced with header +
distilled bullets + a pointer to the full report; `NoteHeader` and `ContentDigest` stay the
raw report's. `OutputDistillerEnabled=false` returns today's note with no other change.

**Optional-work deadline (CARD-0432 S1-S2).** Admission captures mode and one absolute
deadline, normally 45 seconds after completion-note admission begins. The bounded request
queue admits three waiting requests with explicit refusal; it never drops an accepted request
to make room. Held-model preflight uses the same pinned model alias as dispatch, before
provisioning and again afterward. Known held work immediately keeps the raw note without a
new model turn or a per-report generic unavailable incident. Check and Diagnose retain their
existing runner policies.

Queueing, provisioning, dispatch waiting and application consume the original deadline.
Expired queued Distill tasks and never-typed briefs are canceled; already-attempted input
continues the existing confirmation/recovery contract. Apply takes the session delivery lock
and validates the exact pending, unattempted note, source, raw digest and full-report-read
marker under database row locks. Equality with the deadline is expired. A separate two-second
cleanup allowance covers cancellation, hold release and ledger writes together; the finite raw
hold remains authoritative if database cleanup cannot complete. Incident publication and idle
parent-note confirmation run in owned workers, outside the serial optional-work reader.

`DegradedHeld` and `DegradedExpired` are appended outcomes; historical values and nullable
history remain compatible. `DegradedExpired` will absorb requests that previously waited in a
queue and timed out later. The headline degrade rate may therefore look worse: that is intended
fail-fast behavior, not evidence of a regression. Queue wait, specialist wait, decision/expiry
phase and availability identity are recorded separately. `CleanupMs` records elapsed cleanup
before the ledger save; the debug cleanup phase includes the final save. Eventual run-cost
accounting and the fleet event-pump isolation change require their later implementation stages.

**Weekly trigger.** One CARD-0057 `Prompt` schedule, Daily, Monday `09:00` `Europe/London`,
target `Antiphon-Orchestrator`, `WhenTargetDown=Queue`. Live id
`d687a6bf-e286-4592-92a7-799386235a68` (created 2026-09-05). Recreate with:

```powershell
pwsh -NoProfile -File scripts/schedule.ps1 new `
  -Name "Output distiller prompt review" `
  -Agent Antiphon-Orchestrator `
  -Repeat Daily -AtLocal 09:00 -DaysOfWeek 1 `
  -TimeZone "Europe/London" -WhenTargetDown Queue `
  -PromptFile <path-to-the-PromptText-below>
```

**PromptText** (the schedule body; keep it identical when recreating):

```
Weekly output-distiller prompt review (CARD-0330). Do not read the distillation ledger yourself. Dispatch one Review-role delegate:

pwsh -NoProfile -File scripts/delegate.ps1 -Role Review -Worktree -Level High -ExpectAbout 30 -Title "distiller prompt review" -Card CARD-0330 -Goal "<paste the brief template from docs/orchestration-loop.md section 10>"

Then wait for that delegate's report. Do not edit server/Bundles/output-distiller.md yourself.
```

**Delegate brief template** (the `-Goal` the orchestrator pastes; fill the ISO timestamps for
the last 7 days):

```
Review the output-distiller prompt for the window since <since-ISO>.

Read GET /api/distillations/stats?since=<since-ISO> and GET /api/distillations?since=<since-ISO>
(and the Flagged / RejectedOverCompressed / RejectedUnderCompressed rows). Compare each flagged
or rejected sample's raw report with DistilledResult. Judge which prompt sentence caused each
loss or each verbosity.

Evidence bar (must ALL hold before you edit anything): at least 20 distillations in the window,
AND at least one of: 3 or more LostInformation/Noisy flags, OR 10 percent or more of rows
rejected by the gates, OR FullReadAt on 25 percent or more of Applied rows.

If the bar is met: edit server/Bundles/output-distiller.md only. Bump the contract vN line in
that file and OutputDistillation.ContractVersion together. INVARIANTS sentences stay verbatim.
The file must stay at most 3000 characters and keep opening with "You are the Antiphon OUTPUT
DISTILLER (contract v". Do not change OutputDistillationGate or any other gate. Run
InstructionBundleTests and OutputDistillationGateTests. Commit and push a branch. Open a card
in Review with card.ps1 new carrying: the window numbers, the flagged samples you acted on,
the diff, the stamps before/after, and one line of reason per changed sentence.

If the bar is not met: report the numbers and the sentence "no change warranted". Do not open
a card, do not edit the bundle, do not write SystemPromptAppend, and do not type into the
distiller session.
```

**Merge, then restart.** A human reads the Review card and merges or rejects. After a merge,
stop and start the seat (`POST /api/agents/{id}/stop` then `/start`) so the new version
composes. The `BundlesOutOfDate` badge on the agents page is the signal that the running
session is still on the old text. Rollback is `git revert` of that merge and the same
stop/start. The ledger's `BundleStamp` column is how "did v2 do better than v1" is answered.

**What this loop must not do:** write a bundle, `SystemPromptAppend`, or a live session
automatically; edit the INVARIANTS block; grow the bundle past 3 000 characters; change the
gates. The loop tunes what the seat is asked to keep; the gates decide what it is allowed to
drop.


<!-- CARD-0254 preserved source begins -->

## CARD-0254 preserved operational detail

### Preserved Gotcha #4

- **`-Scope` is a list of AREA NAMES and/or path globs, and a hold is now a visible `Held` event — never a silent wait** (CARD-0063). The column is `Scope` (was `ScopeGlob`); each comma-separated element is compared independently, area names by EXACT match against [`antiphon.areas.json`](antiphon.areas.json) at the repo root, paths by the old literal-prefix rule. Before this the whole string was one glob compared by string prefix: in 623 live tasks it produced exactly ONE hold, a false one (`card-reopen-cli` held `card-reopen-client` because one label prefixes the other), and missed five genuine collisions where two running tasks' comma-lists shared an element outright. The policy is now per workspace PAIR, not per area: **Shared↔Shared serialises** (one checkout, one `git status`, one `bin/`), **anything with a Worktree only warns** (it collides at merge, and blocking it throws away the parallelism worktrees exist to give), **ReadOnly is outside the lease in both directions**, and an intersection only in a `weight: allow` area (`docs`, the only one) costs nothing. `Delegation:SerialiseSharedWriters` (default **true**) additionally holds a Shared task behind any running Shared task in the same repo *with no scope declared at all* — the skill doc has said since 2026-08-18 that disjoint scopes do not make two shared writers safe, and this is the server asking that question instead of the caller remembering to. An area name the map does not know is ACCEPTED as an opaque label plus a `Warning` event, never a rejected create: a bookkeeping field must not refuse a launch over a typo. CARD-0535: the repository-mutation-lease hold and the concurrency-cap skip now trace through `TraceHeldAsync` like the other holds and name their holder (a Running land on the same repo, else a journal fence, else occupied-by-another-process); a Queued task's hold ages into `HeldAged` at `Delegation:DispatchHeldWarningSeconds` / `DispatchHeldErrorSeconds` (300/900) with attention `DispatchHeld` (`ConditionKey` `dispatch-held:{id}`); dated routing pins are excluded. CARD-0537: a follow-up (`-OnAgent`) whose pinned agent is not Idle is the same `Held`/`HeldAged`/`DispatchHeld` path; create refuses 409 `follow_up_agent_blocked` when that agent is parked on a Blocked task; cancel deletes the pool row only when this task ran on it and no other open task still pins it. Nothing enforces what a delegate actually writes — drift is RECORDED at settlement (`ObservedScope`, a `ScopeDrift` event, `drift=` in the completion header) and never blocks, holds, kills or re-types anything; a PreToolUse path hook was considered and rejected because it could only ever be armed in a worktree, where an out-of-area write is already harmless. `pwsh -File scripts/delegate.ps1 -ListAreas` prints the map; the create response prints what your scope just cost; the completion header's `overlapping-running=` names the still-running tasks whose areas this one touched, which is the whole of the merge-ordering deliverable. **Extend the map when two tasks collide in an area, naming it for the work, not the folder** — and never give a glob a leading wildcard in its file name (`Services/*Profile.cs`), because the literal prefix collapses to the directory and the area silently swallows everything in it (`AreaMapContractTests` fails the build on it).

### Preserved Gotcha #36

- **A pre-dispatch failure is reminded on the check ramp until the caller hears, and it counts in the attention feed while that reminder is armed** (CARD-0231; CARD-0220 sent the one-shot `[task … failed]` note via `FailAndNotifyAsync` but nothing ever looked again): `AgentTaskDispatcher.FailAndNotifyAsync` arms `NextCheckAt`/`CheckCount` when `DispatchedAt is null` (`ArmFailureReminder` — first look at the 5-minute ramp base, not `ExpectedDurationMinutes`, because that number describes work that never started). `RemindUnacknowledgedFailuresAsync` (registered in `TickAsync` right after scheduled checks) re-sends the note only while nothing shows the caller has heard — note `Sent`, a human Drop (`Canceled`), `LastPolledResultAt`, or `ReadAt`. A still-Pending note with attempts left is in flight: advance the schedule, send nothing. After 10 reminders the ramp stops; the attention row (`AttentionKind.FailureUnacknowledged`, Error / Broken) stays until acknowledgement. `RunScheduledChecksAsync` must keep filtering to `Dispatched`/`Working` — Failed + `NextCheckAt` is now a legal state and must never reach the check worker.

### Preserved Gotcha #37

- **A killed `git worktree add` leaves git's own `locked initializing` behind, and a locked registration with no directory failed every future dispatch of that task id** (CARD-0220): `WorktreeManager`'s 30 s per-command timeout killed the checkout under IO load (a quiet add of this repo is 5.4 s), the catch deleted the directory, and the timeout's `TaskCanceledException` escaped the dispatcher's per-task catch and killed the tick. `worktree add` now has its own budget (`Git:WorktreeAddTimeoutSeconds`, 180), a timeout is a `TimeoutException`, a failed add rolls back fully (directory → `remove --force --force` → `prune` → branch), `CreateAsync` heals a registered-but-missing worktree (re-attaching the branch, never deleting it), and a dispatch failure reaches the caller as a `[task … failed]` note through `FailAndNotifyAsync`. `git worktree remove --force --force` clears a locked+missing entry in one command; it does NOT clear one whose directory is partially present — delete the directory first.

The preceding CARD-0220 paragraph records historical recovery behavior. CARD-0448 supersedes
its forced-removal, recursive-deletion and unowned healing instructions: uncertain leftovers
are preserved, and only invocation-owned unfinished creation can use guarded rollback.
Use the current CARD-0448 cleanup contract below for operations.

### Preserved Gotcha #38

- **A restart can strand "working" forever — two distinct ways** (REQUIREMENT, live miss 2026-08-08, Antiphon-Opus badged Working for 30+ min while idle): (1) *Backfill reordering*: stored transcript sequences are ARRIVAL-ordered — `PersistTranscriptAsync` rebases entries past the session max, so a catch-up sync that lands entries missed during a server restart/stream gap puts stale pre-gap activity ABOVE the already-persisted TurnEnd, and the seq-only working rule read mid-turn forever. Both server `IsWorkingAsync` and client `isWorking()` now carry a timestamp override (record timestamps survive reordering; equal ts keeps the seq verdict); the runner's `TranscriptWorkingState` deliberately has NO override (its mirror is file-ordered). `SessionRunnerEventPump` also catches up ALL runner sessions on every (re)connect — never rely on the lazy GET-transcript sync — and `SyncTranscriptAsync` fires the turn-end queue flush for boundaries that only ever arrive via backfill (the live path dedups them as "seen" and stays silent). (2) *Dead mid-turn process*: a session relaunched after dying mid-turn (reboot/kill) has no TurnEnd coming, ever. The launch paths write a synthetic `SessionRestartBoundary` (a turn END in all working-rule implementations) and, on a genuine `--resume`, queue an auto-continue prompt (`AgentSessionSettings.ResumeAutoContinue`, WhenIdle so it serialises after the launch note). Pinned by `SessionMessageQueueServiceTests` (backfill/boundary cases), `AgentControlServiceIntegrationTests` resume-recovery pair, and the client `isWorking` tests.
<!-- CARD-0254 preserved source ends -->

## CARD-0448 landing evidence and conservative cleanup

CARD-1380 S1: a land takes the OS-backed `land-execution.lock` under the canonical
Git common directory before the repository mutation lease and holds both through
verification, publication and cleanup. A busy execution lock records
`land_execution_busy` without consuming an attempt. Unfinished child journals
still fence the mutation lease after a restart; loss of the execution lock alone
does not authorize reuse of the land worktree.

`Landing:ReuseVerificationArtifacts` defaults to `true`. The verifier retains
owned build generations under
`<git-common>/antiphon/land-verify/v1/<toolchain-key>/`, outside the disposable
land worktree. The key binds the SDK selected in that worktree, OS, process
architecture and explicit Debug configuration. `owner.json` binds repository and
toolchain. A generation is marked building before use and ready only after the
build and any selected tests succeed. Failure, cancellation or an interrupted
write leaves it unusable; the next attempt creates a new generation and retains
the old one. Foreign markers, unmarked existing roots and linked paths are
retained with a `land_artifacts cold` diagnostic and a fresh owned temporary
fallback. There is no recursive cache janitor. Setting reuse to `false` selects
owned cold outputs for diagnosis. `Landing:BuildSlotMemoryFloorFallbackMinutes` and
`Landing:BuildSlotMemoryFloorFallbackMaxCpuCount` (CARD-1461, default off) bound the verifier's
wait behind the runner's memory floor with an unleased capped build; the rule and the line it
prints are in the desktop land verifier paragraph under Build slots in
[testing-and-build.md](testing-and-build.md).

Build and selected tests share the same artifacts and build slot. Each selected
test invocation writes to a fresh owned results directory; only its exact TRX
path can authorize success. An older passing TRX cannot satisfy a missing report.
S2 replaces the unconditional `base_unchanged` shortcut for new publication.
`Landing:ForceVerificationBuild` defaults to `false`; `true`, a nonempty `-Verify`
filter, or any target other than `refs/heads/master` requires a build. AlreadyPresent
still proves exact remote containment without a new publication or a promised build,
even when force-build is set. Historical `base_unchanged` receipts remain readable.

On master, only regular mode-100644 changes to `docs/**/*.md` and root `README.md`
can skip. The policy reads a NUL-delimited raw diff of the pinned `TargetBeforeSha`
and actual `RebasedSourceSha`, including both names of renames and both file modes.
Empty diffs qualify under the same checks. All other paths, type/mode changes,
links, submodules, malformed output and failed reads build. Bundle Markdown and
docs JSON are build inputs. Review, approval, source checks, publication CAS,
confirmation and cleanup remain required; this is not an advisory-review exemption.

The candidate must also match the audited dotnet-build definition inventory in
`LandVerificationBuildGraph`. A path is included when its lowercased extension
ends with `proj` or is `.props`, `.targets`, `.sln`, `.slnx`, `.slnf`, or `.rsp`,
when the file name starts with `Directory.Build.`, when the lowercased file name
ends with `proj.user` (a tracked `<project>.user` is imported by its project;
CARD-1409, Review 23aedc52 D4), or when the lowercased file name is
`global.json`, `nuget.config`, `packages.lock.json`, `packages.config`, or
`directory.packages.props`. Editing an unrelated script leaves that digest
unchanged, so a docs-only skip still applies. `-Check` and `-Update` refuse an
EmbeddedResource, Content, None, Compile, or AdditionalFiles include that can
select `docs/**/*.md` or the root `README.md`. Matching is case-insensitive and
happens after one `%XX` hex decode of the expanded text. Every such include is resolved from the
definition file's own directory and from each importing project's directory,
including through another import. The nearest `Directory.Build.props`,
`Directory.Build.targets` and `Directory.Packages.props` above a project count as
imported, and so does a tracked `<project>.user` beside the project; their
includes resolve from the importing project's directory (CARD-1409, Review
23aedc52 D3 and D4). An `Import`
`Project` path that starts with `$(MSBuildThisFileDirectory)` or
`$(MSBuildProjectDirectory)` is expanded from that property and is not prefixed
with the importing file's folder again. `$(MSBuildThisFileDirectory)` in an
include is that file's directory. `$(MSBuildProjectDirectory)` is the directory
being checked. An `Import` `Project` path that still contains an unknown
`$(...)`, an `@(...)` item reference or a `%(...)` metadata reference is refused
and is not followed. There is no allow-list: `$(MSBuildExtensionsPath)`,
`$(NuGetPackageRoot)` and `$([MSBuild]::GetPathOfFileAbove(...))` refuse too, so
a future chained `Directory.Build.props` that needs one of them changes the audit
first (CARD-1409). An `Import` with an attribute other than `Project`,
`Condition` or `Label`, such as `Sdk`, is refused; `Condition` is not evaluated,
so a conditional import is still followed. An `Import` `Project` is split on `;`
as MSBuild splits it, and every non-empty part is audited as its own import
(Review 23aedc52 D1). Each part must be tracked at HEAD inside the repository as
a `*proj`, `*proj.user`, `.props` or `.targets` file: an empty `Project`, a path
outside the repository, a path absent from HEAD or a tracked file with another
extension is refused, and the message names the importing file, the whole
attribute text and the resolved path. A part that contains `*` or `?` is refused
outright, whatever it matches: land digests only definition files, so a later
non-definition match of an accepted wildcard would never be re-audited (Review
23aedc52 D2). The real repository has no wildcard import; a future one changes
the audit first. `Microsoft.Common.props` and `Microsoft.Common.targets` import
`$(MSBuildProjectExtensionsPath)$(MSBuildProjectFile).*.props` and `.*.targets`,
`obj/` by default, and the audit does not follow those wildcards either: a
tracked definition file under any `obj/` path segment, in any letter case, is
refused by its path whatever it contains, and a definition file that sets
`BaseIntermediateOutputPath`, `MSBuildProjectExtensionsPath` or
`IntermediateOutputPath` is refused because a redirected extension directory
cannot be evaluated; reading one of those properties inside another property,
as `Directory.Build.targets` does, is not setting it (CARD-1409, Review 8989ece8
F1). The inventory already digests a tracked `obj/` `.props` or `.targets`, so
no manifest covers such a tree and a docs-only land builds. Beyond those
named refusals the audit is default-deny (CARD-1409, Review 31c77116): a
definition file may set only an allow-listed property name, wherever the
`PropertyGroup` sits and including a task `Output` `PropertyName`; declare only
an allow-listed item type, each under a rule (a path item such as `Compile`,
`Content`, `None`, `EmbeddedResource`, `AdditionalFiles`, `EditorConfigFiles`,
`GlobalAnalyzerConfigFiles` or one of the repository's own target-time items
must stay inside the repository and never select docs markdown or the root
README; a `ProjectReference` must resolve to a tracked `*proj` from every
directory it is evaluated from; `PackageReference`, `FrameworkReference` and
`InternalsVisibleTo` are bare names; an include carrying `@(` or `%(` refuses;
item metadata names are allow-listed per item type, so a `ProjectReference`
`AdditionalProperties` or `Properties` and any unknown name refuse);
and hold under `<Project>` only `PropertyGroup`, `ItemGroup`, `Import`, `Target`
and an allow-listed `<Sdk>`, with an allow-listed `Sdk` attribute. Every
tracked `.rsp` is parsed: each non-comment line is one of `-nodeReuse`,
`-maxcpucount`, `-verbosity` or `-nologo` with a checked value, and `-p:`,
`-property:`, `@file` and any other switch refuse. The lists are seeded from
the tracked definition files and kept in the script and in the CARD-1409 plan's
inventory; anything outside them refuses naming the file and the construct, so
a new property, item type, SDK or switch is a reviewed edit of the script,
never a silent acceptance. The failure names the definition
file, the construct and the include, or the import path when the import cannot be resolved.
Regenerating the manifest does not approve that include. A file item is
classified by what it resolves to, not by its spelling (CARD-1409 repair 4,
Review 937bb1c9): every `$(...)` in a path item's `Include` or `Update` is
expanded from the tracked definition files' own property values (the two
directory properties, an output or intermediate root, a `Pkg*` path property
with `GeneratePathProperty`, an `MSBuild` task output, every `PropertyGroup`
value of the name), a reference the audit cannot resolve statically refuses by
name (`$(MSBuildStartupDirectory)`, `$(DocsDir)`, an `Exec` output), and a path
rooted at an output or package root accepts any tail without `..`. The resolved
pattern is expanded against every blob tracked at HEAD with a real glob matcher,
honouring `Exclude`: the root `README.md`, an exact `docs/` path other than the
one allow-listed input (the CARD-0449 captures JSON, by exact path), a wildcard
whose first segment can be `docs` or is `**` (whatever it matches today), and a
markdown or text match outside the document allow-list seeded from the real tree
each refuse naming the resolved path. A property value that reads a file through
a property function outside an output root, or whose literal part is a `docs/`
path, refuses. Every resolved path goes through one normaliser before it is
compared (CARD-1409 repair 5, Review f0fb9035): a segment the Windows land host
would read differently (a trailing `.` or space, a `:` past the drive prefix, an
8.3 `~1` short name) refuses naming the segment, for includes, excludes,
imports, project references, output-root tails, tracked matches and tracked
definition paths alike; a free-text property value is read as the host reads it
and refuses only when that reading is `docs/` or the root README. Item
operations form a closed table (the CARD-1409 plan's O-table): `Include` and
`Update` are censused, `Update` metadata included; `Exclude` and `Remove` only
subtract (metadata on a `Remove` refuses); `KeepMetadata`, `RemoveMetadata`,
`KeepDuplicates`, `MatchOnMetadata` and an element with no operation refuse by
name; any other attribute is metadata that must be allow-listed. ProjectReference
admits `IsAspireProjectResource` as Aspire resource-registration metadata; it does
not change the project closure or reference evaluation globals. Unknown metadata
names still refuse in both attribute and child-element form. A path item
whose pattern reaches a tracked symbolic link refuses naming the link. Every
item or import text is read in MSBuild's order by one pipeline (CARD-1409
repair 6, Review c64987ea): properties and metadata are expanded with their
values in escaped form, the expanded text is split on `;` (an escaped `%3B` is
not a separator, a `;` inside an expanded value is), each part is decoded once
(`%252E` is the literal `%2E`, a malformed escape stays literal, `%00` refuses)
and only then normalised, so an escape a property value splices in is read as
the path the build reads; a decoded `$(` is literal text, an escaped `*` is
read as a wildcard for an include and dropped from an `Exclude`, and the tail
of a file-reading property function may not climb out of its output root. A
property-value part that carries an item, metadata or property-function
reference is skipped on its own and the other parts of the same value are
still classified (CARD-1409 repair 7, Review 7db88932). Where only the two
directory properties are expanded (an `Import`, a property-value path, a name
item), the text is split on `;` first and a directory property at the start of
each part is that directory, so `a;$(MSBuildThisFileDirectory)b` is `a` and
that directory's `b`, as MSBuild expands before it splits (CARD-1409 repair 8,
Review d43d1e4a). A tracked definition path holding a character
MSBuild escapes in `$(MSBuildProjectDirectory)` (`%`, `$`, `@`, `;`, `'`,
`(`, `)`, `*`, `?`) refuses by name, because MSBuild leaves the same characters
unescaped in `$(MSBuildThisFileDirectory)` and the audit does not re-implement
that escaping (CARD-1409 repair 9, Review 4ecdb753). An `Import` is followed as described above. Target bodies are not
evaluated beyond that: a task parameter (`Exec`, `Copy`, `ReadLinesFromFile`)
can name any file and the audit refuses no task (CARD-1409 plan D-18, D-27). Any other definition-file edit still changes the
digest, so the skip stops until the manifest is regenerated.

The fingerprint is SHA-256 over ordinal-sorted `git ls-tree -r -z <candidate>`
records selected by that inventory, each terminated by NUL (mode, type, object ID, path).
`scripts/audit-land-verification-graph.ps1` writes
`server/Application/Services/LandVerificationBuildGraph.manifest.txt`: the uppercase
digest, then the selected count. That embedded manifest is the audited fingerprint,
never a hand-edited constant and never accepted from the candidate. A missing,
short, or non-hex manifest, a digest or count mismatch, an unreadable tree, and a
definition-file change force a build. Unknown repositories build. No MSBuild
evaluation is run to decide a skip.

Author-time `-Update` requires the pre-check and real MSBuild evaluation. CARD-1528
also checks every tracked definition's evaluation expressions, including inactive
branches, imported definitions and custom/default item metadata. Filesystem property
functions and unknown, dynamic or chained call identities refuse; only the explicit
lexical members in `EvaluationExpressionPolicy` are admitted. Exactly pinned Target
bodies keep their separate reviewed trust boundary; OS pins do not exempt evaluation
expressions. CARD-1540 also refuses timestamp metadata, dynamic metadata-name arguments
and plain properties outside the explicit reviewed set. Every evaluation element that reads
a reviewed environment knob (including OS) needs an exact pin on its own attributes and
direct text, even in an inactive branch. Only MSBuildThisFileDirectory and
MSBuildProjectDirectory are admitted without these pins. The recorder and canary remain additional checks. `-Check` validates the
pre-check and exact digest/pins record. The record's SDK line retains the author-time
evaluator identity as provenance and must name a canonical stable SDK release selectable
by the hashed `global.json` (currently a stable pin with `rollForward: latestMinor`;
other policies and prerelease pins refuse until explicitly qualified). `-Check` always
requires a built evaluator (`-ToolPath` or the documented default path) and successful
real evaluation under the checking host's SDK before accepting, even when the recorded
SDK matches that host. This catches docs reached by SDK imports, items or globs, without
rewriting any approval records. Updating the record still requires successful real
evaluation and records the SDK used. The evaluator does not restore and does not run a
target. It evaluates each solution pass on its own clock: twice the loaded Linux pass p95
measured on server2 at `8004259c` (22.328 s to the first `not-built` line under 8 CPU
burners, so 45 s), and on Windows at least the 120 s one-pass floor from qualification
`28bbb58e`, which finished a pass and then refused because both passes and the canary copy
shared one 120 s wait. That Windows 120 s floor is provisional until a Windows pass
duration is recorded. The canary is a plain byte copy, not a hard link. Its Linux budget is
twice the plain-copy p95 measured on server2 on 2026-10-29 (samples 1957 ms, 2253 ms and 2249 ms;
nearest-rank p95 2253 ms, so 5 s; see `EvaluationBudget.MeasuredCopyP95Milliseconds`).
The Windows copy budget is PROVISIONAL with three native samples: task `0a72f317` on
DESKTOP-KTLKPIF measured the existing `CanaryRepository.Create` at 33515.1436, 25745.3282
and 25641.6397 ms on 2026-10-29. The prior 12 s budget failed on the combined tree.
Nearest-rank p95 is 33515.1436 ms; `EvaluationBudget.WindowsCopyMeasurementMultiplier`
is `ceil(2 * 33515.1436 / 2253) = 30`, giving 68 s after rounding. Evidence is retained at
`C:\Antiphon\releases\task-0a72f317\copy-samples.json`; remeasure on the next native
qualification. A canary directory left when the audit script process itself dies
is not swept on the next run. Raise the Windows pass floor by editing
`EvaluationBudget.WindowsPassFloorSeconds`, the Linux copy measurement by editing
`EvaluationBudget.MeasuredCopyP95Milliseconds`, and the provisional Windows multiplier by
editing `EvaluationBudget.WindowsCopyMeasurementMultiplier`, all in
`tools/Antiphon.LandGraphAudit/EvaluationBudget.cs`, from a fresh measurement. The script
waits for the tool's printed `ceiling`, which adds only `EvaluationBudget.StartupGraceSeconds`
(60). A timeout still exits 2. `not-built` lines
stay informational and do not refuse. This policy
adds zero land-time runtime work: land still compares the embedded manifest only.

A skip records `Stage=Verify`, `Outcome=Skipped`, `Detail=non-code`, with
`VerificationPassed=false`. The landing's `VerificationCommand` records
`Skipped(non-code)`, target/candidate SHAs and the audited graph fingerprint;
structured logs include task, operation, request and decision reason. Executed
builds keep their existing slot accounting and failure refusal. A skipped build
takes no compiler slot. The repository mutation lease remains held throughout.
Stable paths alone are
not timing evidence; real cold/warm measurements remain post-activation work.
This is master-first work; RC adoption and activation require the operator's go.

The task detail's `landing` object records operation identity, phase, source/verified/remote
SHAs, publication and cleanup separately. AlreadyPresent records independent containment
without claiming a push. A failed or interrupted publication can follow local target advance.
Legacy event prose and disappeared branches grant no cleanup authority. Re-POST preserves an
unfinished operation; cleanup retry requires its saved receipt and fresh remote containment.

Automatic removal now refuses opaque ignored files, dirty/mismatched sources, unregistered
leftovers and unknown receipts. CARD-0459 adds typed `SettledTask` retirement (explicit
`NoFurtherWorkspaceUse` release, unique full-task owner, remote containment) and cleanup-only
retries of a confirmed publication. The Hangfire job `antiphon:worktree-residue` (daily 10:00
Europe/London) is the only scheduler; `WorktreeJanitorHostedService` is no longer registered.
`PruneStaleAsync` remains fail-closed (`typed_removal_authority_required`). Shortening TTL or
invoking the janitor does not grant deletion authority. A retirement claim is refused by a
workspace-use `Launch` row only while that row's owner is live (CARD-0664): a task that is
Queued/Dispatched/Working/Blocked or has a pending land, a session that is
Created/Starting/Running/Stopping, or a row younger than `WorktreeResidue:LaunchGraceMinutes`
(default 15). Other `Launch` rows are orphans; the claim releases them. Task settlement, cancel
and dispatch failure, land outcome, session exit, kill and launch failure, and a refused
admitting operation release their rows after their own commit. Herdr adoption attributes its row
to the adopted session. `WorktreeResidue:Execute` ships false;
activation is a commissioned deploy after Review/land, and CARD-1446 S4d presumes S4a-S4c are
deployed first: copy-based evidence retention, the `**/.antiphon/**` evidence default, and the
Publication lane's pacing. A queued cleanup retry stamps the operation's `landing:<opId>` cursor
`NotBefore = now + WorktreeResidue:CleanupRetryCooldownDays` (default 7; 0 disables) and the lane
holds it as `cooldown` until then; a `Refused` cleanup with `WorktreeResidue:MaxCleanupRetries`
(default 3; 0 disables) earlier queued retries becomes `Cleanup=Abandoned` (one `LandingCleanup`
event, worktree kept, candidate `Refused/cleanup_abandoned`) and leaves the lane; an explicit
`-Land` on the task is its retry path. `MaxActionsPerRun` stays 25. A daily run writes a `Held` candidate row
only when the candidate's `<Lane>|<Outcome>|<ReasonCode>` differs from the cursor's `LastOutcomeKey` (its last
non-preview verdict); action rows and preview rows are always written, run counters count every evaluation, the
run log says `candidates=M rows=N unchanged=M-N`, and `Retention:WorktreeResidueHeldRowRetentionDays` (default 30;
0 disables) prunes `Held`/`Preview` rows older than that while keeping run headers and action rows (CARD-1446
D-5). CARD-0452's ignored-content guard is
unchanged. Failed-add
rollback and stale-registration healing retain uncertain state for inspection. They no longer
force-remove or recursively erase a directory. This increases residue intentionally.

Repository mutation exclusion uses the canonical Git common directory. Mutating Git,
worktree-creation and verifier children write an atomic standing journal under
`<git-common-dir>/antiphon/children/` before launch. The owning invocation clears its exact
journal only after awaiting its child and draining output. An unacknowledged journal after
worker death keeps admission held, including a dead or reused root PID: that PID alone cannot
prove all descendants exited. Malformed/torn records also hold. Do not unlink `landing.lock`,
Git locks or uncertain child records to force admission; inspect the recorded process identity
and surviving work first. Recovery never kills an unrelated process by PID alone.
Use `pwsh -NoProfile -File scripts/recover-repository-children.ps1 -Repository <checkout>`
to preview the records. After confirming the recorded children's descendants have exited,
repeat with `-Execute -ConfirmDescendantsExited`. The command holds the same repository lock,
clears only valid records whose exact PID/start identity is gone or whose owner recorded its
root as already exited (`Completed`, CARD-0661: the PID is never looked up), and retains live, unreadable,
malformed or torn evidence. Exit 3 means busy or retained evidence; exit 0 means none remains.
A server restart alone does not establish descendant exit; a machine reboot does. Unknown
start intents still require investigation. Recovery clears admission, not Git sequencer/lock
state or publication evidence; retry the original operation through its normal recovery path.
Antiphon inspects every registered repository's child journal at startup, on a fenced
land or dispatch, and every `Alarms:SweepMinutes` (15 by default). A stale record raises
a `RepositoryChildJournalStale` attention row with the recovery command; orchestrators
watch the attention feed for this condition.

Creation records now distinguish unfinished intent from a completed/reused checkout. An
owned missing checkout can be reconstructed from its recorded Git admin/index without
forcing checkout over surviving files. Failed-add rollback requires unchanged initial SHA,
registration and an empty status including ignored contents; unknown hook output is retained.
Local child cleanup also rechecks its captured parent SHA, checkout and sequencer state.

A changed preparation at `Verified`, before any target-advance intent, requires an explicit
request and fresh leased source inspection to open a new operation. Source, target, target
checkout, destination and selected verification-filter changes require fresh preparation;
the old operation and pins remain. Automatic recovery cannot replace it, and an operation
that has started target advancement or publication must retain its unresolved evidence.
After rebase, the source must still match HEAD observed when that command completed before
the preparation can be recorded or a local child merge can advance its parent.

The CARD-0448 continuation is not rollout-ready until its complete verification matrix and
creation-recovery/admission coverage are accepted. Do not deploy a checkpoint independently.

## CARD-0443 receipt-backed cleanup checkpoint

The [CARD-0443 plan](superpowers/plans/2026-09-14-card-0443-receipt-backed-cleanup-plan.md)
adds one durable cleanup attempt per land request. The attempt records independent
initial/retry command intents, first Git failure, capture and final component facts.
Committed command slots remain spent across restart. This journal is evidence, not
deletion authority: every removal still requires the current receipt, genuine lease,
canonical identities, remote containment and both content inspections.

After a failed directory removal, the coordinator checkpoints its result and captures
bounded Handle and native delete-access observations. Only a normally exited nonzero
Git removal plus an identity-validated native sharing error 32/33 can nominate one
additional ordinary Git removal. Checkpoint, capture and retry work share a monotonic
ten-second allowance, including one 250 ms delay. Partial or unknown state retains
residue. Branch deletion is never retried automatically.

Configure `WorktreeLockDiagnostics:HandleExecutablePath` as an absolute trusted Windows
executable path. Collection requires existing elevated access and completed Handle
license setup; the server does not elevate, search PATH, accept the license or invoke
a shell. Missing prerequisites are recorded as unavailable. The independent native
probe does not delete or modify files. Observed owners are not authority to stop them.

Task detail and the existing immutable Outcome expose bounded capture evidence, with
prior-request provenance when appropriate. Successful cleanup keeps `LastReason` null
while retaining earlier diagnostics. The existing complete UserPrompt receipt remains
the delivery verdict. Worktree release logs distinguish `StopRequested`, `StopReturned`
and `StopFailed`; none asserts operating-system process exit.

This is an implementation checkpoint, not rollout acceptance. Ordinary test coverage,
worker-death/delivery cuts and real Handle qualification remain incomplete; see the
[Code checkpoint](investigations/2026-09-14-card-0443-code-checkpoint.md).

## Land request and caller receipt (CARD-0467)

An accepted request has its own identity before a landing operation exists. A hold
records the current reason, writer and episode without consuming an attempt. Blocked
writers still exclude landing. Publication, cleanup and caller receipt are separate
facts; a Succeeded delegate is not evidence of publication. A terminal land transaction
owes a durable notification, and an independent worker recovers the same keyed queue
row. Only a complete correlated UserPrompt after the attempt floor confirms receipt.
Status polling does not discharge it. Unexpected execution failures persist a bounded
`terminalFailureCode`, `failureDiagnosticId` and exception type on the request (and in
the Outcome body) without claiming a source refusal; Git inspection failures may add a
fixed command template, exit code and generated diagnostic code, never raw stderr.
The same diagnostic identity correlates the request row, terminal event, notification
and server log. The StageTestDesign delivery inventory and
StageReview audit require producer-to-recipient evidence for changed asynchronous paths.

### Code, ordinary Review, Land, then Mutation (CARD-0478)

Code implements tests, runs every ordinary V/R, commits/pushes and returns `next: review`,
even with zero PCs. Review is separate for every complexity and judges the diff, ordinary
evidence and pending guard/PC inventory read-only. It does not require executed PCs before
land. Clean Review returns `next: land`, defects `next: code`; preserve the original Code
task ID as landing owner. Code's cost is authoring plus ordinary V/R; Mutation's is every
PC/variant plus discovery and reporting. TestDesign retains both numeric floors and their sum.

Before implementation land, create or discover one ordinary same-board Backlog companion:
`Post-land verification: <original identifier>`, label `post-land-verification`, stable key
`post-land-verification:<original-code-task-guid>`. Record full board/card GUIDs, Code/Review
task IDs, reviewed C, plan, commissioning project (including null), pending PC inventory and
`publication pending`. Append the reverse link through a content revision; preserve existing
human description and metadata. Serialize commissioning, inspect linked threads/tasks after
interruption and reconcile conflicting duplicates before dispatch. Missing acknowledgements
never authorize duplicate cards/tasks. The stable text key is discoverable, not DB uniqueness.
Exclude these cards from fresh feature picking; never Spawn a card session for a companion.

Order `delegate.ps1 -Land <original-code-task-id>`. Its correlated publication outcome starts
the continuation, not a synthetic next-stage report. Only structured HasPublication for
Landed, AlreadyPresent or LandedWithResidue qualifies. Queued land, task success, push exit,
local advance, event prose and LandRefused do not. Record O (operation), L=VerifiedSourceSha
and R=ObservedRemoteTargetSha alongside ordinary-reviewed C; rebase can make C differ from L.
Mutation tests L, even after master advances or reverts. Changed implementation needs fresh
ordinary V/R and Review; never relabel C evidence as L.

Update the same companion with O/L/R. Use a file-backed brief containing the Review handoff
verbatim, full reports/plan, all PC IDs/variants, both card GUIDs, Code/Review task IDs,
C/O/L/R, restart target for context and the persistent evidence root:

```powershell
$reviewGoal = Get-Content -LiteralPath '<review-brief-file>' -Raw
pwsh -NoProfile -File scripts/delegate.ps1 -Role Review -Card <original-card-guid> -ReadOnly -Dir '<code-worktree>' -Title 'ordinary review' -Goal $reviewGoal
# After Review and companion recording:
pwsh -NoProfile -File scripts/delegate.ps1 -Land <original-code-task-id>
# After confirmed publication and required deployment:
$mutationGoal = Get-Content -LiteralPath '<mutation-brief-file>' -Raw
pwsh -NoProfile -File scripts/delegate.ps1 -Role Mutation -Card <verification-card-guid> -Worktree -SourceLanding <operation-guid> -Title 'post-land mutation checks' -ExpectAbout <pc-floor-plus-analysis> -Goal $mutationGoal
# After settlement, restoration and durable evidence:
pwsh -NoProfile -File scripts/delegate.ps1 -CleanupVerification <mutation-task-id>
```

Use the same commissioning project and authorized repository. SourceLanding accepts only a
fresh Worker/Mutation/Worktree, no standing pin, OnAgent, Shared, ReadOnly or merge target.
Record the accepted task ID in a companion revision. Admission serializes same-O open tasks
including Blocked; a 409 names the existing task or refusal. Preserve the pending obligation
after quota/sign-in/capacity refusal; no silent provider fallback. A new explicit same-O attempt
requires prior terminal evidence/restoration assessment. Normal task queues own running-state
visibility; no tick creates cards or spends quota. Next-card Code may use its own Worktree.

Before mutation check exact managed creation, HEAD=L and clean tracked source/index; inventory
outputs. Await all commands, use exact-method green/compiling-defect/intended-red/restore/fresh-
build/green cycles and retain nonzero counts/assertions per variant. Higher-tier analysis runs discovery even with
zero PCs; Low does not locate sites. Missing tests or repairs become findings. Do not reset, substitute latest master,
commit/push even evidence amendments, merge, land or deploy from the snapshot. Use local
inherited execution only; never give snapshot access to an external executor, broker, remote
service or pre-existing process. Preserve full evidence/restoration outside the worktree at
the assigned common-Git verification root. Interrupted work retains contaminated paths.

Original Done means shipped: ordinary V/R and Review passed, publication confirmed, companion
recorded and other explicit deployment/acceptance conditions met. Close with C/O/L, Review ID
and `post-land Mutation pending: <companion>`, never PC-clean. Companion activity cannot reopen
the original automatically. Existing card statuses and explicit tracker actions are unchanged.
All PCs/variants and discovery clean after restoration: Mutation `done`, `next: none`; caller
closes the companion with L, counts, evidence and restoration. Findings: `done`, `next: decide`;
caller reads the full report, creates linked remediation and keeps verification open. Incomplete,
interrupted, unavailable or unknown evidence is failed/blocked, not clean. Explicit abandonment
uses Canceled with reason/successor, never Done/Clean.

Mutation's `next: decide` is a triage continuation, not automatically AskUserQuestion. Distinguish
coverage survivor/missing detection from reproducible unmutated-L product regression, invalid/
noncompiling/equivalent control and infrastructure failure. Report Where/Failure/Why/Fix,
severity, guard/PC and C/O/L evidence. Check the companion thread before creating each actionable
remediation; group one root cause. Forward fix at current target through ordinary V/R, Review
and a new land. Record O2/L2 and explicitly commission a new snapshot on this companion; old
Found evidence remains at L. Never automatically reland the original, revert a surviving mutant,
reset or force-push master. A justified revert is a new reviewed commit and deliberate landing.
Only real operator choices belong on a NeedsDecision move/reopen revision and attention feed;
do not create an alert-sink message or new status for a finding.

Cleanup is separate from both publication and test verdict. CleanupVerification irreversibly
seals this task's complete launch set, imports exact native receipts and reads fresh committed
authority under the genuine repository lease before deletion. A started attempt requires the
original container's descendant-zero observation and drained output; dead root, terminal row,
kill success or worker restoration alone never proves custody. Never-reserved evidence is a
separate closed path. Every attempt, exact L/creation/registration/ref, no owners/sequencer,
restoration and hashed task-owned outputs must match. Unknown files, dirty/replaced trees or
missing/unsupported custody remain actionable residue. No force or recursive deletion, and
cleanup never kills. A restored Failed/Canceled battery may clean without changing its verdict.
See docs/testing-and-build.md for the restoration schema and docs/ops-http.md for inspection.

Ship source admission, launch custody, no-autosave/no-land fences, cleanup and these contracts
together. Complete ordinary Code and Review before landing/deploying the feature. The caller
uses the canonical main-checkout runbook and verifies loaded SourceLanding behavior, composed
Code/Review/Mutation hashes and runner/host tracking directly; health alone is insufficient.
Only then commission the feature's own pending PCs. Never use a prose-only, Shared or Debug
fallback. Preserve active PC commands: await and restore through their owner before adopting
the new order, keep evidence at its tested SHA and rerun affected controls at the actual L.
No pins/holds/quotas, historical reports, role ordinals or parser tokens change; `verify` still
aliases Review and optional `-Stage Verify` remains outcome accounting.

## D-2 advisory policy (CARD-1281)

Code and Review must apply this policy alongside their stage contract. The shared
`delegate-basics` bundle points here because the stage bundles retain their existing
size caps and verbatim instruction guards. A summary does not replace this policy.

Required checkpoint obligations in the Code and Review stage contracts remain enforced. Missing required rows, zero counts, unjustified unlisted/ungated runs, broad runs without named invariant/cost or unprovable builds leave verification incomplete. Missing acceptance, lost assertions, failed tests and inadequate tests remain substantive.

FINDINGS (D-2): Report product/acceptance behavior, verification completeness and advisory documentation separately; record introduced/inherited/unresolved attribution independently. No Review rerun solely for advisory docs/evidence corrections, and no Code repair solely for them. Missing trustworthy required verification remains an incomplete gate, never fabricated Clean/Full or an introduced regression. Initial ordinary Review remains required. Preserve historical outcomes and costs; an unclassified legacy Found still needs Review, never relabeling.

EVIDENCE: Existing same-source receipts may be attached/corrected through deterministic receipt validation and an append-only audit without another model Review. If required execution is absent, request only the exact missing selection once. Until the correction validator/audit exists, or if source identity/provenance is unprovable, retain the incomplete gate. A finding override or recovery command cannot manufacture newly validated execution or Clean.

ADVISORY SOURCE: Freeze the reviewed Code branch; publish the valid reviewed tip first, then commission a separate Docs helper with no open product acceptance. Retain its exact committed SHA and delta digest; use its own unlatched explicit caller approval, never the old Review or reviewedSourceClean. An Interim-latched owner/helper cannot use this path. Eligibility requires inspection of the full delta and its consumers: ordinary narrative only, excluding instructions, policy, test selections, schemas, executable examples, generated inputs, configuration and mixed code. Unknown meaning or unsupported content takes ordinary Review; Markdown extensions alone prove nothing. Comment exemptions remain refused until a language-aware equivalence proof is implemented, excluding directives, annotations, suppressions, generated inputs and behavior-bearing documentation.

D-2 next-stage classification: advisory-only follow-ups are recorded separately and do not force Code or another Review. Substantive repairs or exact missing execution require code; incomplete verification never authorizes land. Final Review may authorize land only when behavior has no substantive finding and required verification is complete. An ungated run, a broad run without a named invariant, a missing row, a zero count, or an unprovable build is verification incomplete and is exact missing execution: its next stage is code for that selection once, never land and never another Review only to relabel it.

ADVISORY (D-2): No Review rerun solely for advisory docs/evidence corrections; initial ordinary Review still applies. Keep behavior, verification completeness, advisory corrections and introduced/inherited/unresolved attribution separate. Missing acceptance, failed tests or untrustworthy required verification cannot become Clean/Full by relabeling. Run an exact missing selection once; attach existing same-source receipts only through deterministic validation and append-only audit when available, otherwise retain the incomplete gate. Preserve historical outcomes/costs. Freeze reviewed source: publish its valid tip first, then use a separate Docs helper's exact SHA, delta digest and unlatched caller approval for inspected narrative-only corrections. Never copy the old Review/source-clean assertion, use an Interim latch escape, or classify instructions, policy, executable/generated/configuration content or unproved comments as narrative.

<a id="verification-rounds"></a>
## Interim and Final verification rounds (CARD-0544, dormant)

Every new Code/Review task carries verification profile v1. `Final` is the default and the only
round that can approve a land: Review reports `ordinaryScopeCompleted: Full`, a Clean Final/Full
outcome binds the exact reviewed SHA, and the completion header shows
`verification=Final; scope=Full; final-review=none`. `Interim` is opt-in per card
(`codeVerificationPolicy`/`reviewVerificationPolicy = AllowInterim`) and only while
`InterimVerification:Enabled` and the nightly backstop readiness receipt/monitor are current; it
runs the selection recorded in a committed plan section against a Clean Final baseline, caps its
handoff at `next=review`, and latches the owner so land requires a later Final/Full Review. The
feature ships disabled with every card `FullOnly` and no enablement path; do not request Interim.
Completion notes of profile-v1 tasks are durable obligations (see
[session-runtime-invariants.md](session-runtime-invariants.md)); dispatch from the header's
`next=`, never re-read the body.

The first Code and the first Review of a card always run `Final`, the full ordinary sweep; an
omitted `-VerificationRound` is `Final`. `Interim` is explicit only: the card's role policy must
allow it, and the request names `-VerificationSubject` (the original landing owner),
`-VerificationBaselineOutcome` (a full-scope Review outcome) and `-VerificationSelectionFile`
(the committed selection as JSON). A refused Interim request is never resubmitted silently as a
different round. A clean Interim Review routes to a fresh Final Review of the unchanged
candidate, never to land; a latched owner lands only with a Clean Final/Full Review.

CARD-0835 adds an explicit `reviewedSourceClean: true|false` line to the bare Review evidence
block. Review may report true only after its complete required checkpoint selection passes
`validate-checkpoint-receipt.ps1` (or the tool's equivalent) for `reviewedSourceSha`, with
`dirty=0`, stable clean source and verified build binding. Settlement stores the nullable
assertion on the Review outcome. Evidence-backed land admission and every unpublished resume
require true; missing or false returns `review_evidence_source_not_clean`. Old outcomes remain
null and need a fresh Review. An authorized explicit caller SHA without a Review evidence ID
retains its separate approval path; a rejected evidence ID never falls back to that path.

## Expectation watchdog (CARD-0650)

An enabled, explicit board directive can run a minute Hangfire observation sweep independently
of tracker ticks and ordinary caller-note delivery. It watches queued pipeline age, a proven
dispatch-wide fence, in-flight capacity below the configured runner targets, dispatched tasks
without usable sessions or task-local activity, and undelivered caller notes. It records durable
episodes and Check/card audits, prompts the standing orchestrator through the direct session
input path, and pages the configured operator conversation if the prompt is unsafe, uncertain or
unanswered by its persisted deadline. The orchestrator answers with a whole line
`[expectation-ack:<nudge-guid>]` followed by the action or reason; that answer does not clear a
still-observed condition. An unanswered episode receives operator reminders within the configured
repeat bound and no periodic retyping. This watcher observes repository journals only; CARD-0648
owns recovery. Check `GET /api/expectation-watchdog?boardId=<guid>` for actual scan and delivery
state before relying on it.
