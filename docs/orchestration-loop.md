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
A 409 means a land is running in
this server now — wait for its outcome event. A `Warning` "did not finish (server restarted);
re-running" is informational. The orchestrator decides the order and what a refusal means, but
does none of those git operations itself.

**Repair source (CARD-0499).** When a Code Worktree task must work on a branch that is already
checked out elsewhere, pass `delegate.ps1 -RepairSource <owner-guid>` (full GUID of the original
Code/Worktree landing owner). Antiphon records that owner, routes the repair onto its own unique
branch at the owner's recorded SHA, and attributes a claimed post-dispatch commit on the owner's
ref. `-RepairSource` alone sets no merge target and grants no Land: a repair task's
`-Land` is refused `repair_source_landing_owner_required`. Integrate through the original owner
(or an explicit merge target equal to the owner's branch). Historical Failed tasks are not
backfilled; prose-only "work in that other worktree" is still unsupported.

**Continuing a sibling's work (CARD-0613).** To start a delegate from a commit other than the
default base - continuing an interrupted stage, or picking up where another task's branch got to -
pass the base as a DISPATCH PARAMETER, never as `git checkout -B ...` prose in the goal:

```
pwsh -NoProfile -File scripts/delegate.ps1 -Role Code -Worktree -StartRef <full-sha> -Goal <work>
```

(For a long brief, read the file yourself and pass the resulting string to `-Goal`; the script has
no `-GoalFile`.) The task still gets its own `feat/card-task-<id>`, cut at that commit; the named
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
include the required change, restart from the canonical checkout with the intended
full HEAD as `-ExpectedServerSha`, then confirm the reported SHA and a direct
capability/feature probe. Do not treat `/health`, a runner SHA, a pushed branch, or
a succeeded delegate as activation evidence. Use the landing receipt's post-rebase
identity, not an assumption that the Code worktree SHA survived landing unchanged.
An already-running descendant build is sufficient when local ancestry can be
established and the required feature/capability is present; unknown/unrelated build
history cannot establish activation. Record desired and observed full SHAs in the
deployment report. After out-of-band publication, update the canonical checkout
using the existing runbook before choosing the intended deployment HEAD. Executable
procedure: [apphost-runbook.md](apphost-runbook.md). A new enum member (a role, a workspace mode) is a
server capability exactly like `land-v2`: the script accepting it proves nothing about the served
build.

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
`-Land` operation's job, not a manual step — see §5. The fleet's stage glance — one line per card,
in-flight / queued / ready per stage, generalised across all six stage roles (CARD-0146 S4) — is
`/orchestrator?tab=pipeline`.

A Worktree task records the ref it was cut from. Precedence is repair SHA, then an explicit
requested ref, then the merge target, then the project's `BaseBranch` / `Git:DefaultBranch` /
`master` when that ref resolves to a commit, then `HEAD` with a warning naming the failed default
(CARD-0508 S1). The card's current kept sibling is not chosen as a base in this release
(CARD-0215 policy is unchanged). Containment of a kept sibling is patch-aware (`git cherry`): a
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
Land a Plan with `delegate.ps1 -Land <id>` before dispatching Execute as a
convenience so the plan commit is on master — it is not required for a correct base. A `Landed`
line carrying `unlanded-sibling=` means a same-card branch is still stranded; land or drop it. Two 2026-08-10
cases (the CARD-0002 design doc and the CARD-0001 fix) sat unmerged for 9 hours before anyone
noticed.

### Picking

Lowest `rank` first — the formula already prefers a card that **changes how everything else gets done** over one more feature.
Prefer a card whose plan already exists — but check properly, see below.

### Standing pipeline policy: one task per stage, Code fed to two (CARD-0533)

**This is the orchestrator's default for working the Antiphon board.** It was the operator's
standing instruction on every overnight run from 2026-09-13/14 through 2026-09-18/19 and is
recorded here so a fresh seat starts from it instead of being told again. The user's words in a
session override it for that session; nothing below needs restating to apply. The delivered copy
is the `orchestrator` bundle; this section carries the reasons.

1. **One task per stage, stages in parallel.** Run at most one task in each stage role
   (Investigate, Plan, TestDesign, Code, Mutation, Review) at a time, and let different stages
   run concurrently — each in its own `-Worktree`, so they never serialise on the shared
   checkout. Never two tasks in the same stage at once. Before dispatching a stage, read that
   stage's in-flight row on `GET /api/agent-tasks/pipeline` (or `/orchestrator?tab=pipeline`),
   not your memory of what you dispatched.
2. **On every completion, dispatch the named next stage.** Read `next=` and `handoff:` off the
   completion header (§1, CARD-0146). `next=unmarked` goes back to the same delegate for the
   missing block (§0's ladder); it is never guessed from the diff.
3. **Land promptly.** Once a stage's work is confirmed — a Plan or investigation artifact, a
   reviewed Code branch — run `-Land` then (§5), not at the end of the night. Unlanded work is
   how the 9-hour strandings under "Is there a solid plan?" happened.
4. **Keep the Code stage at a depth of two.** Count Code rows that are in flight, queued or
   `ready` (a settled Plan or TestDesign whose `next:` is code) on the pipeline snapshot. Fewer
   than two: pull the next unstarted Backlog card — lowest `rank` first, as under Picking — and
   start it through Investigate/Plan toward Code. Already two: start no new Plan toward Code;
   Investigate, Plan and TestDesign for cards already in the pipe still run. This is CARD-0146
   D7's "planning should only stop if more than 1 card waiting to execute", with its pull side
   stated as well.
5. **Same source area as an in-flight Code task: defer, even with a free Code slot.** A Worktree
   task whose scope intersects a running one is warned, not held (CARD-0063, preserved detail
   below), so nothing stops two Code tasks editing the same file and conflicting at merge. When a
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
   because rule 1 is the standing request for cross-stage parallelism.

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
| `Mutation` | post-land PCs and missing-control discovery in a fresh SourceLanding snapshot (stage) | Frontier |
| `Review` | ordinary read-only pre-land review for every complexity; judges implementation, ordinary evidence and pending PC design (stage) | fable |
| `Debug` | find out why something is broken | opus |
| `Coverage` | check what a change missed | opus |
| `Merge` | resolving a conflict left behind by a worktree task (auto-spawned after TryMergeBackAsync fails, rarely dispatched by hand) | opus |
| `Docs` | prose, markdown, comments | sonnet |
| `Commit` | git plumbing, and the CARD-0527 settle child that commits a Shared task's leftover dirty paths through `POST /api/agent-tasks/{id}/commit` | sonnet |
| `Test` / `Deploy` | RUN a thing and report what happened | haiku |

Code owns implementation and ordinary V/R; ordinary Review follows before land. Post-land
Mutation owns all deliberate PCs/variants and missing-control discovery in an independent
snapshot, freeing the Code role slot for another card.

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

**WIP defaults (documented rule, CARD-0146 D7, restated by CARD-0533 — dates are the operator
instructions that fixed these, 2026-09-01/02 and 2026-09-13/14).** `RecommendedInFlight = 1` for
every stage role, and that is the per-stage rule: one Investigate, one Plan, one TestDesign, one
Code, one Mutation and one Review may all be in flight together, never two tasks in the same
stage. The 2026-09-01 reading that the plan-side stages share one slot is superseded — they run
concurrently, each in its own worktree. Code is fed to a depth of two (in flight + queued +
ready); Plan toward Code holds at that depth ("planning should only stop if more than 1 card
waiting to execute") and resumes below it. Alternate one complex/UI card with one medium/simple
card, and prefer GitHub-linked cards. All of this is advisory — CARD-0147's create-time
concurrency gate is the hard stop, this is the judgement call underneath it; the full rule set is
§1's standing pipeline policy.

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

**Routing pins beat RolePolicy.** A Human pin on a card+role (or a stage-wide pin for that role)
is what the next `delegate.ps1` create reads; RolePolicy remains the provenance-less fallback when
no pin exists. A Human pin survives a RolePolicy edit and an Auto rewrite (409 `routing_pin_human`).
`scripts/routing-pin.ps1` is the write surface; `delegate.ps1 -Pin` records this dispatch as Human
Required. A pin naming a held alias is still 409 `model_disabled` (CARD-0309) — pins consume
`Require`, they do not write a hold, and `ignoreRoutingPin` is not `ignoreModelDisabled`.

A stage pin with `candidates` is how the operator's role-shaped fallback is recorded
(CARD-0322): `routing-pin.ps1 set -Role Plan -Candidates ClaudeCode/Frontier,ClaudeCode/High,Codex/Frontier`.
The list is walked with CARD-0090's chain mechanics (holds, quota, stage forbid). A **Required**
list is the whole candidate set; a **Preferred** list is tried first, then the request/role policy
(or, with `-Complexity`, the chain — never both). Card list vs stage list never concatenate: the
card pin wins as a whole row. On `routing exhausted` relay to the operator — never pick a kind
yourself.

Precedence between a stage-wide pin and a role's complexity cell: a **Required** pin *bypasses* the
matrix cell for that role entirely — the cell is never consulted while the pin stands. A
**Preferred** pin *prepends* to the matrix candidates and falls through to the cell if the pinned
target is unavailable. A **card-scoped** pin applies only to that one card, never to every task in
that role stage-wide — do not read a card pin's banner as a global change. `RolePolicy` itself is
unrelated to this matrix: it remains the non-complexity fallback (default kind/level, escalation,
timeout, WIP) for work not launched with `-Complexity`; CARD-0333's matrix does not govern or
supersede it (CARD-0097 is the separate, still-open RolePolicy visibility/editing follow-up).

**Operators read and reason about all of this at `/settings?tab=routing`** (CARD-0333): the global
Routing settings tab shows live model availability, best-effort subscription-usage observations
(explicitly provider/profile-level, never a per-model quota, and "Unknown" rather than a guessed
number when monitoring is off or no sample exists), the active stage-wide/card pins with the
Required/Preferred wording above, and the effective (role × complexity) matrix with inline
Configure/Clear. It is the read path for diagnosing "why did this route where it did," not a new
way to change RolePolicy or to control a running session.

**Deployment note:** as of tonight there is a live stage-wide **Human Required Code → Grok** pin.
It bypasses every Code-role complexity cell — an operator looking only at the Code row of the
matrix will see candidates that are not actually being used. This is expected, not a bug; check
`/settings?tab=routing` (or `GET /api/routing-pins?role=Code`) for the pin before assuming the
matrix is misconfigured. Clearing this pin, or downgrading it from Required to Preferred, is an
explicit operator decision made through `scripts/routing-pin.ps1` — it is never automatically
migrated or cleared by CARD-0333's matrix UI, which is read-only for pins.

**Complexity chains (CARD-0090, CARD-0332).** Pass `-Complexity Hard|Medium|Easy` when the work's hardness
should pick (kind, level) from an ordered fallback list, instead of an explicit `-Kind`/`-Level`.
The list is a sparse (role × complexity) matrix: the walk reads the role cell, then the any-role
chain, then the config default, then Blocked. A Required stage pin still bypasses the cell.
An explicit pair is never rerouted — combining `-Complexity` with `-Kind` or `-Level` is 422.
Config defaults ship empty; write the live lists with `complexity-chain.ps1 set` (`-Role Plan` for
a cell, omit `-Role` for the any-role row). When the chain
is exhausted the task is **Blocked** (or 409 `routing_exhausted` with `-RefuseIfExhausted`):
**relay that to the operator and never pick a kind yourself.** A human answers with Retry,
`delegate.ps1 -Reroute <id> -Kind … -Level …`, Cancel, or by clearing a hold. Auto-resume onto
an already-listed candidate when capacity returns is executing the instruction, not a new guess.
A usage-wall reroute is ordinary work on the new candidate: the walled session's capacity wait
ends with the session, and the requeued row dispatches on the next tick unless the new candidate
is itself held (CARD-0481).

**A Blocked-on-question child (CARD-0294 S1+S2).** The parent `[task … blocked]` note carries
`reason:` / `asks:` / `authority:` / `next:` above the body, outside the excerpt window. If
`authority:` names something, `delegate.ps1 -Continue <id>` is the one action that replays it;
otherwise `-Reply` if you can answer, else put `asks:` in your chat reply now — never `NO_REPLY`
a blocked note. Dispatch with `-Authority "<the user's own words>"` whenever the user has
pre-approved a sequence. Auto-continue, the 5-minute bound-chat notice, and the unmarked
zero-progress Block are follow-on slices of the same card.

The best single result of this period came from a `Debug` agent; the cheapest useful one was a haiku
check at **$0.12**.

### Launching an agent

A Queued runner-bound task whose mirror is not recorded yet stays Queued. Its deduplicated `Held` detail is one of three texts: remote workspace preparation is in flight (the branch push and mirror were requested); preparation failed N times in a row and the next attempt is not before an instant (exponential backoff, base 30 seconds, cap 900 seconds, reset when the mirror is recorded); or the runner is not dispatch-eligible. Those traces are not a warning on every tick. A warning is written once per failed preparation attempt.

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
fits. Sequential follow-up that must keep context: `-OnAgent <taskId>` (already on the script and in
the skill). Parallelism on one model: let the pool spawn another, or pass `-Worktree`. That *is* the
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
409 `follow_up_agent_blocked` — reply with `-Reply` or cancel the Blocked task, then re-send. A retired
Worktree follow-up continues that task's committed tip: Antiphon freezes the local branch SHA and
cuts a new `feat/card-task-<id>` worktree there. It does not reuse the old directory, reset the old
branch, or fall back to master. If that tip cannot be proven, create refuses
`follow_up_source_unavailable` and inserts nothing; pass `-Worktree -StartRef <sha>` to choose a
commit yourself. Explicit `-Shared` or `-ReadOnly` is a new task and is warned as such. A live
follow-up, `-Agent`, or a routing pin that names an existing agent keeps that agent's checkout
(Shared, or ReadOnly when that was explicit). Explicit `-Worktree` together with one of those is
`workspace_existing_agent_conflict`. Cancel retires a pool agent only when the canceled task ran on it and nothing else open still pins it.

---

## 3. Writing a brief

**The standing rules are delivered automatically now (CARD-0058). Do not retype them.** Every
delegate launches with `server/Bundles/delegate-basics.md` composed into its
`--append-system-prompt` — foreground-only, no sub-delegation, commit-and-push each slice,
`OutputPath` with a forward slash, verify pre-existing red by stashing before blaming yourself. That
file is **canonical**; this section deliberately does not restate its contents, because a rule
restated in three places drifts in three places, and it drifted here first. Improve a rule by
editing that file in a PR: every future dispatch gets the better version, with nothing to
reconcile. A sub-orchestrator additionally launches with `server/Bundles/orchestrator.md`.

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
- **A Code brief names the checkpoint list.** Add one line, `checkpoints: <plan artifact path>@<full plan commit sha> section "### Checkpoints"`, the shape the server already renders for an Interim `selection:`. The table stays in the committed plan (owner: [testing-and-build.md](testing-and-build.md), Checkpoint manifest); `-ExpectAbout` is the sum of its `EstimatedMinutes` column (a time budget, not the `Min` execution counts) plus authoring. The `stage-code` bundle carries the obligation to run it as a closed list and to report per row; do not restate that.
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

Bundles reach a delegate **at launch**, which has one bounded consequence worth knowing: a warm
pool delegate keeps the bundles it started with until it retires (60 minutes idle). Nothing types
bundles into a live session; instead a standing agent that is idle is relaunched with `--resume`
and the new composition (CARD-0334), keeping its conversation. For a pool delegate or work in
flight there is no such relaunch — if a rule change matters urgently there, say it in the brief for
that dispatch.

**Goal length never affects delivery, and shortening a goal buys nothing (CARD-0353).** A Claude
delegate types its brief inline up to ~40 KB on the modern ConPTY. **Every non-Claude delegate —
Grok, Codex — receives a POINTER at any length**: the ceiling for those kinds is 0 bytes by design
(CARD-0084/CARD-0099 default-deny), so the brief is written to
`.antiphon/task-<short>-brief.md` and the typed message says so, beginning `YOUR BRIEF IS NOT IN
THIS MESSAGE`. That pointer IS complete delivery — nothing further is queued, and the delegate
reads the file before it does anything else. So "shorten the goal to avoid the spill" avoids
nothing and drops context the delegate needed; it also fights §0's rule that the orchestrator hands
over full context. If a session shows only that pointer and nothing else, see §4: it is a provider
stall, not a delivery failure.

---

## 4. Checking on a delegate

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
The prompt reached the transcript, so delivery is not the problem; the model has not produced its
first token. The check digest names it — a `BOOT TURN` line on `SESSION`, and a `DEADLINE:` line
naming `BootModelWait` — and the harness handles it: at
`Delegation:BootModelWaitDeadlineMinutes` (8, measured) the task is failed with
`ProviderUnresponsive`, the session is killed (it produced nothing, so nothing is lost) and the
task is **retried once** at the same kind and tier. A second stall on the same task fails without
retrying and names the alias; two stalls on the same `(kind, alias)` inside
`Delegation:BootStallRepeatHoldMinutes` (30) put that alias on an AutoDetected hold. Cancel and
retry by hand only if you cannot wait out the deadline — and if you do, expect the same provider.
For a Grok session, `~/.grok/sessions/<id>/events.jsonl` (`phase_changed: waiting_for_model` with
no `first_token`) and `~/.grok/logs/unified.jsonl` (`shell.turn.inference_start` with no
`inference_done`) are the diagnostics; see `docs/agent-kinds.md`.

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
git log --oneline master..feat/card-task-<id>

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
202 `{ status: "queued" | "requeued" }`. A 409 means a land is running in this server now —
wait for the outcome event; it never fires for a request no process holds. Three interrupted
attempts refuse (`LandRefused`); `-Land` again starts a new request. A `Warning` "did not
finish (server restarted); re-running" is informational.

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

An explicit retry of an eligible terminal `Refused` operation creates a fresh operation,
even when the source is unchanged. It repeats validation and verification as required,
retains the previous operation and recovery pins, and may refuse again if the target or
other prerequisites remain unsafe. Target repair or restart alone does not authorize a
retry. Publication recovery and guarded cleanup retain their existing operation and evidence.

Read the task's structured `landing` evidence and the outcome's operation ID, original source,
verified commit, observed remote commit, confirmation time, mode and cleanup status.
`AlreadyPresent` records independent containment without claiming a successful push.
`LandedWithResidue` records publication with incomplete cleanup. Re-POST retries guarded cleanup;
its `LandingCleanup` event updates the same publication rather than counting another one.
`LandRefused` can follow local target advancement or an unconfirmed push; its evidence names
the last acknowledged checkpoint. Missing source components alone never prove success.
A `Landed` line that also carries
`unlanded-sibling=<id>:<branch>` (comma-separated if several) means a same-card kept branch is
not present in the pinned verified SHA by patch id (`git cherry`) — land or drop that sibling; the
server warns rather than refusing. A rebase-landed sibling no longer appears.

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

For the separately scoped `am-service` remote target, the same rule applies through
`pwsh -NoProfile -File scripts/deploy-am-service.ps1`. Once a Deploy-role brief explicitly
authorizes `-Deploy`, that Deploy-role delegate may run this script and report its final
`REMOTE DEPLOY VERDICT`; it may not reconstruct the SSH/tar/Compose sequence ad hoc. Its default
run is a read-only preflight, and its human traffic check remains the Antiphon-Family test group.

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
  (or `-Reason` for something short) and reports the result.

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
activation is a commissioned deploy after Review/land. CARD-0452's ignored-content guard is
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
build/green cycles and retain nonzero counts/assertions per variant. Run discovery even with
zero PCs. Missing tests or repairs become findings. Do not reset, substitute latest master,
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
