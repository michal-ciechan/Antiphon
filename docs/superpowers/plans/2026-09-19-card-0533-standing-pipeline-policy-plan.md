# CARD-0533: The standing pipeline policy is written where a fresh orchestrator reads it

Status: Plan, written under stated defaults (D-1 to D-10). Verification design is included (§Verification design), so the next stage is Code. Documentation-only: four markdown files and one documentation-pinning test; no server or script code changes.

Authoring baseline: `c4a857c0` (branch `feat/card-task-dc120df1`). Plan task: `dc120df1`. Card: CARD-0533 (Antiphon board). Line numbers below are at `c4a857c0`.

Owners to read before changing these areas: [orchestration](../../orchestration-loop.md) (the target file; §0 read rules, §1 cycle, §2 WIP and launch, §5 landing), [`server/Bundles/README.md`](../../../server/Bundles/README.md) (what a bundle may carry, the drift badge, the command-line budget), [agent card lifecycle](../../agent-card-lifecycle.md) (card state, filing), [testing and build](../../testing-and-build.md) (TUnit filters, alternate output path).

## Outcome and scope

The card's policy is written once, in full, with reasons, in `docs/orchestration-loop.md`, and delivered to a fresh orchestrator by the two things such a session actually loads without being told: `AGENTS.md` (imported by `CLAUDE.md` at every Claude launch) and the `orchestrator` bundle (`server/Bundles/orchestrator.md`, composed into every orchestrator seat and sub-orchestrator at launch). The orchestrator skill's judgement layer is brought into line so the three statements never disagree. A small documentation test pins the load-bearing phrases in all four files so the policy cannot silently drop out of one copy again, which is the defect the card describes.

The seven rules, in the card's order: one task per stage with stages in parallel; dispatch the named next stage on every completion; land promptly; keep Code fed to a depth of two by pulling the next Backlog card; file a card for every structural defect immediately; `-IgnoreConcurrencyLimit` for the absolute cap only, never for a same-stage collision; and the 2026-09-18/19 refinement, defer a Code dispatch whose source area overlaps a Code task already in flight.

Out of scope: the server's `ConcurrencyLimitException.Coda` string and `scripts/delegate.ps1`'s header and parameter comments, which still state the old override rule (§Not done, noted); any harness enforcement of the same-area deferral (§D-7); routing, pins, tiers, and the Mutation recipe.

## Ground truth

| Card premise or plausible shortcut | Confirmed at `c4a857c0` | Design consequence |
|---|---|---|
| "Not currently documented anywhere in AGENTS.md or docs/orchestration-loop.md." | Partly wrong, and worse than absent. `docs/orchestration-loop.md:324-330` documents a *different* WIP rule from 2026-09-01/02: "Plan-side WIP (Investigate + Plan + TestDesign together) is 1; Execute (Code) WIP is 1". `:429-431` says re-send with `-IgnoreConcurrencyLimit` "only when the user asked for parallel work this turn". `AGENTS.md` has nothing on WIP or the override. Neither file has the Code-depth-two pull rule, the same-area deferral, or the axis rule. | The WIP paragraph is rewritten to defer to the new section, not left beside it (S1b). The launch paragraph's override sentence is amended (S1c). |
| The doc is enough: "so a fresh orchestrator session picks it up automatically". | A fresh seat loads `AGENTS.md` (via the `CLAUDE.md` import) and its composed bundles. `server/Bundles/orchestrator.md` is what every `Orchestrator` task and every seat with the `orchestrator` attachment receives (`InstructionBundles.cs:200-209`, README "Which agent carries which bundle"). `docs/orchestration-loop.md` is read on demand; it is in `PolicyRefreshSettings.DefaultInstructionFiles` (`SupervisionSettings.cs:91-98`), so a change to it makes a running seat re-read it ("AGENTS.md, docs/orchestration-loop.md changed (re-read them now...)", `ChannelContractsTests.cs:177`), but nothing loads it at launch. README: "A rule that earns standing status gets PR'd into a file here. Recorded anywhere else — a findings doc, a skill, one orchestrator's habit — it reaches nobody." | Full text and reasons in the doc (S1); a rule-shaped paragraph in the bundle (S2); a one-line pointer in AGENTS.md (S3). D-1, D-2, D-3. |
| The bundle is neutral on this. | It states the opposite rule: "Dispatch is sequential-by-default: a 409 `concurrency_limit` names this project's occupants and cap; wait, or re-send with `-IgnoreConcurrencyLimit` only when the user asked for parallel work this turn." (`server/Bundles/orchestrator.md`, "Child work goes through `delegate.ps1`" paragraph). | That sentence is replaced by the standing-policy paragraph (S2). Leaving it means the seat that matters most composes the old rule. |
| The orchestrator skill already has it. | `.claude/skills/antiphon-orchestrator/SKILL.md` §0 (2026-09-16/17) has "1 task per pipeline stage, in parallel across stages", `-Worktree` by default, per-project concurrency, and file-a-card-immediately. It lacks the Code-depth-two pull rule, the same-area deferral and the axis rule, and its last bullet, "Prefer sequential dispatch, one stage transition at a time, unless the user explicitly asks for parallel fan-out", reads against rule 1. | §0 is aligned and points at the doc as owner (S4). D-4. |
| "Refused only by the absolute cap" is something the orchestrator has to infer. | The 409 body is mechanical. `DelegationOpenGate.ToProblem` (`:79-95`) sets `axis` to `"absolute"` when the absolute cap is exceeded, else `"role"`; `role` is filled only on the role axis; `open` lists every open occupant on the absolute axis and only same-role occupants on the role axis, each with `shortId role status`. **Absolute wins when both are exceeded**, so an `absolute` 409 can hide a same-stage occupant in its list. `delegate.ps1:534-551` prints `ErrorDetails.Message`, the problem JSON, so the caller sees `axis`, `role` and `open`. | Rule 7 says: read `axis` *and* scan `open` for the role being dispatched (D-6). |
| The override only lifts the absolute cap. | `AgentTaskDtos.cs:145-150`: true "queues anyway and records a Warning naming the counts" for "the fleet or this role"; `ConcurrencyLimitException.FormatOverrideWarning` names both counts. `delegate.ps1:170-176`: "project/role in-flight cap (default 3 absolute, 1 per named role)", one-shot. Defaults: `MaxOpenTasks = 3` (create gate), `RecommendedInFlight = 1` for every stage role (`DelegationSettings.cs:30, 322-327`), `MaxConcurrentTasks = 6` (dispatcher). | The flag would also bypass the per-role cap, which is the mechanical form of rule 1; hence "never for a same-stage collision". |
| The harness already stops two Code tasks colliding on the same files. | For Worktree tasks it does not. `docs/orchestration-loop.md:1010` (CARD-0063): "Shared↔Shared serialises ... anything with a Worktree only warns (it collides at merge...)". The default dispatch is `-Worktree`. CARD-0537's plan sequenced its S1 behind CARD-0535's Code task `b4ee43c2` by hand (§Dependency on CARD-0535, 2026-09-19). | The same-area deferral is a documented judgement rule, not an assumed hold (D-7). |
| "Highest priority first" needs a new definition. | §1 "Picking": "Lowest `rank` first — the formula already prefers a card that changes how everything else gets done". `card.ps1 get` prints `rank`. | Rule 4 reuses Picking; no second ordering is introduced. |
| The Code count is by hand. | `GET /api/agent-tasks/pipeline` (`AgentTaskEndpoints.cs:48-52`; `docs/antiphon-api.md:394`) is an in-flight / queued / blocked / ready snapshot per stage; queued rows carry `queueReason` (`concurrencyCap`, `siblingLandInFlight`, ...); ready rows sit on the stage a settled report's `next:` named. `/orchestrator?tab=pipeline` is the same glance (`orchestration-loop.md:166-168`). | Rule 4 counts Code depth as in flight + queued + ready on that snapshot (D-5). |
| "Land promptly" and "ask the same agent for the missing block" are new. | Both are already in the doc: §1 "Land a Plan with `delegate.ps1 -Land <id>` before dispatching Execute ... sat unmerged for 9 hours" (`:180-186`), §5 "`next: land` ... is the cue to run `-Land`", and §0/§1 "`next=unmarked` ... send it back to the *same* delegate (§0's ladder), never to read the diff instead" (`:151-154`); the bundle says the same. | Rules 2 and 3 cross-reference; they do not duplicate. |
| No test cares about these files. | `InstructionBundleTests.the_worst_case_composition_measured_sits_far_under_the_budget` composes every role with `board-api` and a reply style under `CommandLineBudgetChars` (30,000); bundle state guards forbid "CS8604" and "JobObject"; `orchestrator.md` is 8,955 bytes, `delegate-basics.md` 6,473. `CommitOnSettleDocumentationTests` pins phrases in other sections of the same doc and of AGENTS.md (`-NoCommit`, "never `--reflog`"). No test pins the sentences being replaced (`grep sequential-by-default tests/` is empty). | The bundle paragraph stays under about 1,200 characters (S2); the new test mirrors `CommitOnSettleDocumentationTests` (S5). |
| A bundle edit needs a manual restart of every seat. | README "The drift badge": the agent DTOs expose `BundlesOutOfDate`; the seat "picks the new instructions up at its next launch". `orchestration-loop.md:53-56`: an idle seat with drifted bundles is relaunched with `--resume` at its next idle window (CARD-0334). | Acceptance is the seat's stamp changing after the ordinary post-land restart (V-5), executed by the orchestrator, not by Code. |

## Decisions

### D-1. The full policy lives in `docs/orchestration-loop.md` §1, as a new subsection after "Picking"

§1 is "The cycle": what to dispatch, when, from which signal. The policy is the cycle's standing shape, so it goes there, immediately after "### Picking" (whose ordering it reuses) and before "### Reprioritising the backlog". Rejected: `AGENTS.md` as the sole home, because AGENTS.md is the index and safety core, one line per rule with an owner pointer, and a seven-rule policy with reasons does not fit its style. Rejected: §2 beside the WIP paragraph, because §2 is "Tiers" (which model, which caps) and the WIP paragraph is already the odd one out there; instead that paragraph is rewritten to defer to §1 (S1b).

### D-2. The bundle carries the rule, because that is the only path that reaches a fresh seat

`server/Bundles/orchestrator.md` gets one rule-shaped paragraph (no dates, no card identifiers of live work, no counts that will be wrong tomorrow: the README's rule-versus-state line) replacing the two sentences that state the old override rule. This is a markdown edit under `server/`, versioned by content hash, and reaches seats at their next launch or CARD-0334 relaunch. Rejected: leaving the bundle alone, which would leave the seat composing "sequential-by-default ... only when the user asked for parallel work this turn" while the doc says the opposite; the card's stated goal fails for exactly the session it names.

The paragraph is phrased for "a board you are working through its pipeline", not for every orchestrator turn: a seat answering a one-off request is not pulling Backlog cards. "Unless the user says otherwise this session" is the override clause; a session instruction beats the standing rule.

### D-3. AGENTS.md gets one bullet under "Cards and tracker", pointing at the owner

The file's own shape: a one-sentence rule, then `Owner:`. It sits after the pipeline-stages bullet (the `--- next stage ---` one) because the two are the same mechanism read from two sides. Rejected: reproducing the numbered list.

### D-4. The orchestrator skill's §0 is aligned and defers to the doc

The skill is the judgement layer a human-invoked `/antiphon-orchestrator` session loads; it already carries four of the seven rules and one bullet that reads against rule 1. It is rewritten so all seven appear in short form, the "prefer sequential dispatch" bullet becomes "one stage transition per completion; parallelism comes from cards sitting at different stages, not from fanning out", and §0 names `docs/orchestration-loop.md` §1 as owner. Rejected: leaving it, because a contradiction at the judgement layer is what the operator would otherwise correct by hand each night.

### D-5. Code depth is two, counted as in flight + queued + ready

The card says "exactly 2"; the dispatch brief says "roughly 2". Both are the same rule stated from different sides: below two, pull; at two, start no new Plan toward Code. Three arises only from an event the orchestrator did not start (a Review returning `next: code` on a card whose Code had already left the count) and is drained, not corrected. Rule 1 keeps Code *in flight* at one, so the steady state is one running and one waiting. The count is read from `GET /api/agent-tasks/pipeline` (or the pipeline tab), never from memory of what was dispatched. Rejected: counting only in-flight rows, which would pull a new card every time a Code task ran, and ignoring ready rows, which are precisely the queued work the card is counting.

### D-6. The override rule is stated in terms of the 409's `axis` and `open` fields

Because the absolute axis wins the report when both caps are exceeded, the rule has two conditions, both required: `axis` is `absolute` **and** no occupant in `open` is in the role being dispatched. Either `axis: role` or a same-role occupant means defer. This is rule 1 applied to the 409, and it is what keeps the flag (which lifts both caps) from ever creating a same-stage pair. Rejected: "if the message names your role, defer", which misses the same-role occupant hidden inside an absolute list.

### D-7. The same-area deferral is a documented judgement rule; no harness change under this card

Worktree scope intersections warn and do not hold (CARD-0063), by design, because holding would throw away the parallelism worktrees exist for. Making Code-versus-Code intersections a hold is a real product question (it would need a `queueReason` and a `Held` event) and is not this card. The rule names the signal the orchestrator has today: the plan's file list and the pipeline snapshot's in-flight Code row. Filing a card for a harness hold is left to the orchestrator (§Not done, noted).

### D-8. The server `Coda` string and the script comments are not touched

`ConcurrencyLimitException.Coda` ("Re-send with ignoreConcurrencyLimit=true if the user asked for parallel work this turn.") and the `delegate.ps1` header and `-IgnoreConcurrencyLimit` comments state the old rule. They are code and script, the card is documentation-only, and the 409 text is the sentence a model reads at the moment of decision, so the mismatch is material. It is listed for a follow-up card rather than widened into this one.

### D-9. TestDesign is folded; next stage is Code

Documentation-only work is the easy shape in the complexity table (TestDesign folded into Plan). The verification section below is executable as written: one new documentation test, three existing test classes re-run, one post-land acceptance check.

### D-10. Wording keeps the file's voice

Bold lead sentences, numbered rules, CARD references and dates where the doc already uses them, cross-references to sections rather than repeated text. The bundle paragraph is plain prose with the same vocabulary (`-Worktree`, `-IgnoreConcurrencyLimit`, `concurrency_limit`, `axis`). Four phrases are held identical across all four files so the test can pin them: `never two tasks in the same stage`, `depth of two`, `-IgnoreConcurrencyLimit`, `axis`.

## Slices

Commit each slice as it completes (`docs(CARD-0533): ...`; the test slice `test(CARD-0533): ...`), push after each commit. Line numbers are at `c4a857c0`; re-locate by the quoted text.

### S1. `docs/orchestration-loop.md`

**S1a. New subsection.** Insert after "### Picking" (ends `:195`, "Prefer a card whose plan already exists — but check properly, see below.") and before "### Reprioritising the backlog" (`:197`):

````markdown
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
````

**S1b. Rewrite the WIP paragraph** at `:324-330` (starts `**WIP defaults (documented rule, CARD-0146 D7`), replacing the whole paragraph with:

````markdown
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
````

**S1c. Amend the launch paragraph** at `:429-431`. Replace, from `Dispatch is sequential-by-default:` through `Other projects' work never counts against yours.`, with:

````markdown
Dispatch is sequential-by-default at the gate: a 409 `concurrency_limit` names
this project's occupants, the cap and the `axis` it tripped. On a board worked under §1's standing
policy that policy is the request for cross-stage parallelism: re-send with `-IgnoreConcurrencyLimit`
when the axis is `absolute` and no listed occupant shares the stage you are dispatching; defer when
the axis is `role` or a same-stage occupant is listed. Elsewhere, only when the user asked for
parallel work this turn. Other projects' work never counts against yours.
````

Leave every other sentence in those paragraphs as it is. `CommitOnSettleDocumentationTests` pins phrases elsewhere in this file; do not touch §Commit on settle or the CARD-0254 detail.

### S2. `server/Bundles/orchestrator.md`

In the paragraph beginning `Child work goes through `delegate.ps1``, delete from `Dispatch is sequential-by-default:` through `Other projects' work never counts against yours.` (the paragraph then ends at `...only to steer work you already dispatched.`). Insert this new paragraph immediately after it:

````markdown
When you are working a board through its pipeline, this is the standing policy unless the user
says otherwise this session. One task per pipeline stage (Investigate, Plan, TestDesign, Code,
Mutation, Review), stages running in parallel with each other, each in its own -Worktree, never
two tasks in the same stage. On every completion dispatch the named next stage. Land a stage's
work as soon as it is confirmed. Keep the Code stage at a depth of two (in flight, queued and
ready together, read from GET /api/agent-tasks/pipeline): below two, pull the next unstarted
Backlog card, lowest rank first, and start it through Plan toward Code; at two, start no new Plan
toward Code. A card whose Code work touches the same source area as a Code task already in flight
waits for that task to land, even with a free Code slot. File a Backlog card the moment
Investigate or Review finds a structural defect; never batch them. A 409 `concurrency_limit`
carries `axis` and the open occupants with their roles: re-send with `-IgnoreConcurrencyLimit`
only when the axis is `absolute` and no occupant is in the stage you are dispatching; when it is
`role`, or a same-stage occupant is listed, defer. Other projects' work never counts against
yours. The reasons are in docs/orchestration-loop.md §1.
````

Keep it under about 1,200 characters and free of dates, card identifiers and counts (the bundle state guards in `InstructionBundleTests`). The file's version changes automatically; nothing to bump. The `Merge` note in README ("detach the old key" on rename) does not apply; the key is unchanged.

### S3. `AGENTS.md`

Under "### Cards and tracker", insert after the bullet that ends `never by re-reading the report body (CARD-0146).`:

````markdown
- An orchestrator working a board runs one task per pipeline stage with the stages in parallel, each in its own worktree, and never two tasks in the same stage; keeps the Code stage at a depth of two by pulling the next Backlog card; lands as soon as a stage is confirmed; files a card for every structural defect the moment it is found; and passes `-IgnoreConcurrencyLimit` only for an `axis: absolute` 409 with no same-stage occupant — a same-stage collision, or a Code task touching the same source area as one already in flight, defers instead. Owner: [docs/orchestration-loop.md](docs/orchestration-loop.md) §1 (CARD-0533).
````

### S4. `.claude/skills/antiphon-orchestrator/SKILL.md`

Replace §0 ("## 0. Policy defaults, unless the user says otherwise") in full with:

````markdown
## 0. Policy defaults, unless the user says otherwise

The owner is `docs/orchestration-loop.md` §1, "Standing pipeline policy" (CARD-0533); this is the
short form.

- **1 task per pipeline stage, in parallel across stages.** Never two tasks in the same stage at
  once. Before dispatching a stage, check that role's in-flight row on
  `GET /api/agent-tasks/pipeline` (or a known task by `GET /api/agent-tasks/{id}`, see §5), not
  your memory.
- **`-Worktree` by default on every dispatch.** Shared checkout only when explicitly told to
  default to Shared (globally/per-project/per-invocation), or when a task must continue on a
  branch that's already checked out elsewhere (see §4).
- **One stage transition per completion.** Read `next=`/`handoff:` off the header and dispatch
  that stage (§1). Parallelism comes from different cards sitting at different stages, not from
  fanning out several dispatches at once.
- **Code stage at a depth of two** (in flight + queued + ready). Below two, pull the next unstarted
  Backlog card, lowest rank first, and start it through Plan toward Code; at two, start no new Plan
  toward Code.
- **Same source area as an in-flight Code task: defer that card's Code**, even with a free slot —
  a worktree scope overlap only warns (CARD-0063), and the conflict lands on you at merge
  (CARD-0535/CARD-0537, 2026-09-19).
- **Land as soon as a stage's work is confirmed** (§6); don't hold landings to the end of a
  session.
- **Concurrency is per-project.** The absolute cap (`concurrency_limit` 409) and each stage's
  `recommendedInFlight` are both scoped to the project the board belongs to — unrelated boards
  (other projects) never count against it.
- **`-IgnoreConcurrencyLimit` only for `axis: absolute` with no same-stage occupant** in the 409's
  `open` list. `axis: role`, or a listed occupant in the role you are dispatching: defer. The
  absolute axis wins the report when both caps trip, so read the list.
- **File a card immediately** for any structural bug/defect found during Investigate or Review,
  before moving on. Don't let a real finding evaporate into a chat message.
````

### S5. `tests/Antiphon.Tests/Application/StandingPipelinePolicyDocumentationTests.cs`

Mirror `CommitOnSettleDocumentationTests` (same root resolution via `DelegateScriptRunner.RepoRoot`, same `File.ReadAllText` + `ShouldContain` shape). Before matching, collapse every whitespace run to a single space and compare case-insensitively: the pinned phrases wrap across markdown lines and one copy capitalises "Never". Tests are named in §Verification design. Commit it as its own slice after S1-S4; a documentation pin does not need a red-first run, it needs to be green at the commit that carries it.

## Verification design

### Simulating the conditions

Nothing runs; the deliverable is text. Verification is (a) the four copies carry the same load-bearing phrases and the old contradiction is gone, (b) the bundle still composes under budget and past the state guards, (c) the other documentation pins in the same files still hold, (d) after land and restart, the standing seat's bundle stamp drifts and is refreshed.

Build to an alternate output path while the daemons hold `bin/`: `--property:OutputPath=bin-0533/` (forward slash), and delete every `bin-0533` directory before finishing.

### Guards

| Guard | Where | Asserts |
|---|---|---|
| V-1 `the_policy_phrases_are_pinned_in_every_copy` | new `StandingPipelinePolicyDocumentationTests` | Each of `AGENTS.md`, `docs/orchestration-loop.md`, `server/Bundles/orchestrator.md`, `.claude/skills/antiphon-orchestrator/SKILL.md` contains all of: `never two tasks in the same stage`, `depth of two`, `-IgnoreConcurrencyLimit`, `axis` — after whitespace runs are collapsed to one space and ignoring case (S5). |
| V-2 `the_doc_owns_the_section_and_the_old_wip_reading_is_gone` | same class | `docs/orchestration-loop.md` contains `### Standing pipeline policy` and `CARD-0533`; does not contain `TestDesign together) is 1`. |
| V-3 `the_bundle_no_longer_states_the_old_override_rule` | same class | `server/Bundles/orchestrator.md` does not contain `only when the user asked for parallel work this turn`; contains `same source area`. |
| V-4 `the_agents_index_points_at_the_owner` | same class | `AGENTS.md` contains `§1 (CARD-0533)`. |
| V-5 (existing) | `InstructionBundleTests` whole class | Budget, state guards, 8-hex version, summary length all green with the edited bundle. |
| V-6 (existing) | `CommitOnSettleDocumentationTests` whole class | The other pins in `docs/orchestration-loop.md` and `AGENTS.md` survive S1/S3. |
| V-7 (existing) | `InstructionFileStampTests`, `PolicyRefreshDeltaTests` | Stamp mechanics unaffected by content (expected green; run to be sure). |
| V-8 (post-land, orchestrator) | live stack | After `-Land` and the ordinary restart (§6), `GET /api/agents` shows the standing orchestrator seat with `BundlesOutOfDate` true until its next launch; after its CARD-0334 idle relaunch (or a manual stop/start) its `ComposedBundleStamp` names the new `orchestrator v<hash>`. A fresh `delegate.ps1 -Orchestrator` dispatch composes the new text on first launch. |

Commands (from the worktree; TUnit, never `dotnet test`):

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-0533/ -- --treenode-filter "/*/*/StandingPipelinePolicyDocumentationTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-0533/ -- --treenode-filter "/*/*/InstructionBundleTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-0533/ -- --treenode-filter "/*/*/CommitOnSettleDocumentationTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-0533/ -- --treenode-filter "/*/*/InstructionFileStampTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-0533/ -- --treenode-filter "/*/*/PolicyRefreshDeltaTests/*"
```

Report counts per class and any failure by name. Do not run the whole `Antiphon.Tests` assembly for this card.

### Positive controls (for the Mutation stage; method-scoped, red then green)

| PC | Mutation | Expected red |
|---|---|---|
| PC-1 | In `server/Bundles/orchestrator.md` change `never two tasks in the same stage` to `never two tasks in one stage` | V-1 fails naming the bundle |
| PC-2 | In `docs/orchestration-loop.md` reinsert the sentence `Plan-side WIP (Investigate + Plan + TestDesign together) is 1` into the WIP paragraph | V-2 fails |
| PC-3 | In `AGENTS.md` delete the S3 bullet | V-1 and V-4 fail |
| PC-4 | In `server/Bundles/orchestrator.md` append `Today's known-red test is JobObject.` | `InstructionBundleTests` state guard fails |

Filter: `--treenode-filter "/*/*/StandingPipelinePolicyDocumentationTests/<method>"`; restore each mutation before the next.

## Risks

- **Two truths for a while.** Until the land restarts the server and the seat relaunches, a running seat carries the old bundle. Acceptable; it is the same window every bundle edit has, and the doc's re-read notice covers the doc half.
- **The Coda string still says the old rule** in the 409 body itself (D-8). An orchestrator that reads only the 409 detail and not its instructions will follow the old rule. Mitigated by the bundle paragraph naming the 409 by code and telling the seat what to read in it; removed only by the follow-up.
- **Rule 4 is a spend rule.** Pulling Backlog cards is model spend the operator has asked for on this board; the bundle scopes it to "working a board through its pipeline" and to "unless the user says otherwise this session" so a seat answering one question does not start pulling cards.

## Not done, noted

- **Follow-up card (recommended, not filed here):** align `ConcurrencyLimitException.Coda` (`server/Application/Exceptions/ConcurrencyLimitException.cs:15-17`) and the `scripts/delegate.ps1` header (`:13-15`) and `-IgnoreConcurrencyLimit` parameter comment (`:170-176`) with rule 7, so the 409 text tells the caller to check `axis` and the same-stage occupants rather than "if the user asked for parallel work this turn". Code change with `AgentTaskConcurrencyLimitTests` and `DelegateScriptKindTests` to re-run.
- **Possible product card:** a `queueReason` and `Held` event for a Code dispatch whose scope intersects an in-flight Code task's scope, turning rule 5 from judgement into a visible hold. CARD-0063 deliberately chose warn-only for Worktree pairs; reopening that is a design decision, not a doc fix.
- The user-level memory that Execute runs two concurrent Grok worktree tasks predates this card's directive; the card and this plan keep Code in flight at one and the *depth* at two. Not a repo change.
