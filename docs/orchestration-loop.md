# The orchestration loop

How a card gets from the board to shipped, using delegates. Written from what actually worked
2026-08-13..16, including the parts that did not. The aim is that this is repeatable without
re-deriving it, and mechanical enough to automate later.

The orchestrator's job is to **decide, verify and record**. The reading, the writing and the running
are delegated. The orchestrator's context is the scarce resource: spend it on judgement, not on
archaeology - and verification is not archaeology's quieter cousin. It is trust, by default.

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

**Also delegated: the landing mechanics.** For a delegated Worktree task, the orchestrator orders
the landing with `delegate.ps1 -Land <id>` (optionally `-Verify <filter>`); the server fetches,
rebases, verifies when required, fast-forwards, pushes, and cleans up. The resulting
`Landed` / `LandedWithResidue` / `LandRefused` outcome line is the confirmation. `Landed` means
the target advanced and cleanup finished; `LandedWithResidue` means the target advanced and
cleanup left a branch or directory (re-run `-Land` to retry cleanup); `LandRefused` means the
target did not advance. A request survives a server restart. A 409 means a land is running in
this server now — wait for its outcome event. A `Warning` "did not finish (server restarted);
re-running" is informational. The orchestrator decides the order and what a refusal means, but
does none of those git operations itself.

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

A pipeline stage IS an `AgentTaskRole` — `Investigate`, `Plan`, `TestDesign`, `Code`, `Review`
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
Code  (Verify folded in by default) ── runs every V-n/R-n and each PC-n red-then-green,
  │    reports every item in a table
  │ next: land | review (escalation — see below) | code (slices remain) | decide
  ▼
Review  ── the escalation, not the default: dispatched only for a `complexity:hard` card, a
  │        safety-critical label, or when Code itself settled `next: review`
  │ next: land | code (defects named) | decide
  ▼
delegate.ps1 -Land <id>  ──▶  deploy  ──▶  close the card
```

`next: land` is the cue for `-Land` — see §5. `next: decide` means the artifact already exists
under stated defaults; take the `## Decisions` section straight to `AskUserQuestion`, the task is
**done**, not blocked. A stage report with no block still settles — `next=unmarked` — and the fix
is to send it back to the *same* delegate (§0's ladder), never to read the diff instead.
`Debug`/`Test`/`Coverage`/`Docs`/`Commit`/`Deploy`/`Merge`/`Custom` are **helpers**, dispatched
*inside* a stage (a `Debug` for a red test during Code, a `Docs` slice); they carry no stage
bundle and the block is optional for them (§2).

Landing (fetch, rebase, verify, fast-forward, push, worktree removal, branch deletion) is the
`-Land` operation's job, not a manual step — see §5. The fleet's stage glance — one line per card,
in-flight / queued / ready per stage, generalised across all five stage roles (CARD-0146 S4) — is
`/orchestrator?tab=pipeline`.

A Worktree task branches from its merge target, or from master HEAD when none is set — never from
a sibling task's branch (CARD-0215). Land a Plan with `delegate.ps1 -Land <id>` before dispatching
Execute, so the plan commit is on master and the build worktree contains it. The dispatcher holds
Execute while that plan's land is in flight, and warns (a `Warning` event plus a WhenIdle note
naming the branch and tip) when the plan branch is simply not landed. A `Landed` line carrying
`unlanded-sibling=` means a same-card branch is still stranded; land or drop it. Two 2026-08-10
cases (the CARD-0002 design doc and the CARD-0001 fix) sat unmerged for 9 hours before anyone
noticed.

### Picking

Lowest `rank` first — the formula already prefers a card that **changes how everything else gets done** over one more feature.
Prefer a card whose plan already exists — but check properly, see below.

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

**Stage vs helper.** `Investigate`, `Plan`, `TestDesign`, `Code`, `Review` are pipeline **stages**
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
| `Code` | write or change code; Verify folds in by default — runs and reports every verification-design item (stage) | opus (override `-Level High`) |
| `Review` | judge whether logic is correct — the escalation over folded Verify, not the default (stage) | fable |
| `Debug` | find out why something is broken | opus |
| `Coverage` | check what a change missed | opus |
| `Merge` | resolving a conflict left behind by a worktree task (auto-spawned after TryMergeBackAsync fails, rarely dispatched by hand) | opus |
| `Docs` | prose, markdown, comments | sonnet |
| `Commit` | git plumbing, branches, PRs | sonnet |
| `Test` / `Deploy` | RUN a thing and report what happened | haiku |

Why Verify folds into Code by default, and Review is the escalation rather than a standing stage:
§0's trust-the-report rule (2026-08-30) already has Code report a verification table; a standing
separate Review on every card would double the Frontier-tier dispatches on the cards the
alternation rule deliberately keeps cheap. The knob is the card's `complexity:` label — see the
shape table below.

### Default stage shape by complexity (CARD-0352's `complexity:` label)

| Label | Investigate | Plan | TestDesign | Code (Verify folded) | Review | Dispatches |
|---|---|---|---|---|---|---|
| `complexity:easy` | skipped unless the cause is unknown | yes, **folds TestDesign** (section required) | folded | yes | no | 2 |
| `complexity:medium` | only if the root cause is unconfirmed | yes | **separate** | yes | no | 3–4 |
| `complexity:hard` | yes unless already diagnosed | yes | **separate** | yes | **separate** | 4–5 |

Overrides, in order: a `safety-critical` label or a Code report saying `next: review` forces a
separate Review; the operator can force any stage separate in the brief; `-Land -Verify <filter>`
is available at every land regardless of this shape. Not code-enforced — this is a skill-doc rule
plus the stage bundles' `next:` vocabulary (CARD-0096's batch control is where acting on `next=`
automatically would live, and it is not built).

**Two different "Verify"s, never renamed.** `AgentTask.Stage : OrchestrationStage?` (CARD-0272:
Rebase/Verify/Cleanup/Review/FollowUp/Deploy) is the **landing-step** outcome `-Land` records into
`StageOutcomes` — its `Verify` is "did the build/test step of a land pass." The pipeline's Verify,
above, is a different question — "did the delegated work do what it claimed" — answered either by
Code's folded verification-table run or by a separate `Review` dispatch. Same English word, two
axes (`AgentTask.Stage` vs `AgentTask.NextStage`); neither is renamed to disambiguate, so read the
column, not the word.

**WIP defaults (documented rule, CARD-0146 D7 — dates are the operator instructions that fixed
these, 2026-09-01/02).** `RecommendedInFlight = 1` for every stage role. Plan-side WIP (Investigate
+ Plan + TestDesign together) is 1; Execute (Code) WIP is 1; Plan holds once the Code stage's
`ready` list reaches 2 rows ("planning should only stop if more than 1 card waiting to execute");
alternate one complex/UI card with one medium/simple card, and prefer GitHub-linked cards. All of
this is advisory — CARD-0147's create-time concurrency gate is the hard stop, this is the
judgement call underneath it.

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

Start a new project through `POST /api/projects/setup` (or `scripts/project.ps1 new -Dir ... -Orchestrator -Start`): it creates the project, board, and preset agent in one transaction and returns readiness. `POST /api/agents` remains for adding an agent to a project that already exists.

Create and start an agent through `POST /api/agents` + `POST /api/agents/{id}/start` (or the UI).
Never launch the `claude` CLI directly, and never a `launch-remote` script. When setting
`modelLevel`, send it as the string `"Frontier"` (or `"High"` / `"Medium"` / `"Low"`). A numeric
token (`0`, `1`, `99`) is a **400** — the wire is the member name, not the enum ordinal (CARD-0007).
Frontier maps to fable (Claude) by default, or `grok-4.6` when `Kind=Grok` is passed.
If `delegate.ps1` 409s `model_disabled`, pick an alias from `available` or wait until
`disabledUntil`; do not retry the same kind/tier. If the 409 also says the available list does
not satisfy a routing pin, wait, pass `-IgnoreModelDisabled` to queue, or replace the pin — do
not pick from `available`. Dispatch is sequential-by-default: a 409 `concurrency_limit` names
this project's occupants and cap; wait, or re-send with `-IgnoreConcurrencyLimit` only when
the user asked for parallel work this turn. Other projects' work never counts against yours.

### Reuse first

**Default: `delegate.ps1`.** Unrelated new work needs nothing special — the warm pool reuses an idle
agent in the same directory (compacted first) and spawns a fresh ephemeral delegate only when none
fits. Sequential follow-up that must keep context: `-OnAgent <taskId>` (already on the script and in
the skill). Parallelism on one model: let the pool spawn another, or pass `-Worktree`. That *is* the
"2–3 reusable workers per directory+model+tier, scale only for real parallelism" policy, implemented
by the pool rather than by named rows.

`POST /api/agents` is for a **standing identity**, not a unit of work: an orchestrator seat, a
channel-bound agent, the check interpreter, or a human-facing named worker that should outlive a
card. Pass an existing `BoardId` (the project's real board). A unique `workingDirectory` that is not
a real checkout is how 21 extra `gym-stat-*` projects appeared.

Work you need to hear finish is a task; creating an agent gives you an identity, not a report.
Pin a task onto an existing named standing agent with `delegate.ps1 -Agent <name|slug|guid>`
(CARD-0291): it queues while that agent is busy, delivers into the live session, and settles with
the normal `[task … done]` note. A raw session message to a child is for steering work you already
dispatched, never for handing over work — no completion note will ever arrive for it.

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
a plan, the plan-doc path convention, and the full `next:` vocabulary; for `stage-code`, that every
`PC-n` runs red-then-green and is reported in a table, and that landing/deploy are not this
delegate's job; for `stage-test-design`, the exact `## Verification design` sub-structure
(`V-n`/`R-n`/`PC-n`/Out of scope/Cost); for `stage-investigate`, that a fix idea is one line under
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
# has it settled?  (one request, no model)
curl -s localhost:17202/api/agent-tasks | python -c "..."   # filter by short id

# the stored report — available BEFORE the notification
pwsh -NoProfile -File scripts/delegate.ps1 -Status <id>

# the truest progress signal: commits exist before reports do
git log --oneline master..feat/card-task-<id>

# what it is doing right now
curl -s localhost:17202/api/sessions/<sessionId>/transcript
```

**Traps:**

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
pwsh -NoProfile -File scripts/delegate.ps1 -Land <id>
# Add -Verify "<treenode-filter>" when that narrow test is part of the landing decision.
```

The server performs the fetch, rebase, conditional build and optional named test, fast-forward,
push, worktree removal, and branch deletion. The request is a row (`LandRequestedAt`); the
in-process channel is only a hand-off. A restart re-runs anything still pending that this
process does not hold. `delegate.ps1 -Land` prints "Queued land" or "Requeued land" from the
202 `{ status: "queued" | "requeued" }`. A 409 means a land is running in this server now —
wait for the outcome event; it never fires for a request no process holds. Three interrupted
attempts refuse (`LandRefused`); `-Land` again starts a new request. A `Warning` "did not
finish (server restarted); re-running" is informational.

Read the resulting `Landed`, `LandedWithResidue`, or `LandRefused` event and its outcome line.
`Landed` reports the merged SHA, pushed remote ref, verification result, and `worktree removed`.
`LandedWithResidue` keeps the `landed …` prefix and ends `cleanup incomplete: <what remains>` —
the target advanced; re-run `-Land` to retry cleanup. `LandRefused` means the target did not
advance (fetch, remote-ahead, rebase, verify, fast-forward, or push failed) and leaves the
branch and worktree in place naming why. A `Landed` line that also carries
`unlanded-sibling=<id>:<branch>` (comma-separated if several) means a same-card kept branch is
not an ancestor of the rebased HEAD — land or drop that sibling; the server warns rather than
refusing.

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

**Each land outcome is also three `StageOutcome` rows (CARD-0272)** — Rebase, Verify, Cleanup —
because those are three different questions even though one land op answers all three. `Landed`
"build OK…" writes Rebase Clean, Verify Clean (with the verify detail), Cleanup Clean.
`LandedWithResidue` writes the same Rebase/Verify but Cleanup Failed, naming what remains — the
target still advanced, only cleanup did not finish. A bare `LandRefused` (fetch/rebase/push
failed before any build ran) writes Rebase Failed alone. Historically, before CARD-0328 shipped
`LandedWithResidue` as its own event (2026-09-03), a "could not delete branch/worktree" cleanup
failure was reported under `LandRefused` too, which read as "did not land" even though the target
had advanced — the plan behind this card flagged that as 9 of 47 backfilled runs misclassified.
The backfill that derives `StageOutcome` rows from that older history still recognizes a
`LandRefused` whose detail says "could not delete" and correctly writes Rebase Clean / Verify
Unreported / Cleanup Failed for it, so the mapping is accurate even for pre-0328 rows; it is not
an open problem, just a shape worth knowing when reading old rows. `GET /api/stage-outcomes` (via
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
- **Card files under `docs/cards/<slug>/` are generated one-way from the board (CARD-0004).** The
  database is the source of truth; every sync overwrites the files. Edit the card (`scripts/card.ps1`),
  never the file. A 60 s tick and `POST /api/boards/{id}/card-files/sync?dryRun=` are the only
  triggers — there is no enqueue from `CardService`. Settings (`CardFileSync`): `Enabled` (default
  true; off means the feature does not exist), `AutoCommit` (production **false** — files write, no
  commit until an operator turns it on), `IntervalSeconds` (default 60, floor 5; `0` is manual-only
  and leaves the endpoint on). `Enabled=false` is **409** `card_file_sync_disabled`; a concurrent
  run on the same repo is **409** `card_file_sync_running`. When AutoCommit is on, the commit is
  path-scoped (`git add -A -- <dir>` then `git commit --only -m "antiphon: sync card files (<board>)"
  --trailer antiphon=true -- <dir>`), **never pushed**, and the message **never names a card
  identifier** (`CardThreadService` greps identifiers and would list every sync commit on every
  card's thread). Guards skip the commit (files stay written; retry next tick; log once per reason
  change): `rebase_in_progress`, `merge_in_progress`, `cherry_pick_in_progress`, `detached_head`,
  `conflicted_paths` under the directory, `git_error` (an `index.lock` held by a delegate is the
  expected one). Archived cards keep their file; archived boards and projects are skipped.
- Findings that outlive the card go in `docs/investigations/`. Agent scratch output lands in
  `.antiphon/`, which is **gitignored** — an 11 KB proven root-cause writeup was nearly lost that way.

---

## 8. Clean up

For delegated Worktree tasks, worktree removal and branch deletion are the `-Land` operation's own
job. Do not run them as a manual orchestrator step after a `Landed` outcome. A
`LandedWithResidue` outcome is the cleanup-retry verb: re-run `-Land` (it short-circuits
prepare/verify and only retries removal). A `LandRefused` outcome deliberately keeps both so a
follow-up delegate can work from the failure.

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

- **`-Scope` is a list of AREA NAMES and/or path globs, and a hold is now a visible `Held` event — never a silent wait** (CARD-0063). The column is `Scope` (was `ScopeGlob`); each comma-separated element is compared independently, area names by EXACT match against [`antiphon.areas.json`](antiphon.areas.json) at the repo root, paths by the old literal-prefix rule. Before this the whole string was one glob compared by string prefix: in 623 live tasks it produced exactly ONE hold, a false one (`card-reopen-cli` held `card-reopen-client` because one label prefixes the other), and missed five genuine collisions where two running tasks' comma-lists shared an element outright. The policy is now per workspace PAIR, not per area: **Shared↔Shared serialises** (one checkout, one `git status`, one `bin/`), **anything with a Worktree only warns** (it collides at merge, and blocking it throws away the parallelism worktrees exist to give), **ReadOnly is outside the lease in both directions**, and an intersection only in a `weight: allow` area (`docs`, the only one) costs nothing. `Delegation:SerialiseSharedWriters` (default **true**) additionally holds a Shared task behind any running Shared task in the same repo *with no scope declared at all* — the skill doc has said since 2026-08-18 that disjoint scopes do not make two shared writers safe, and this is the server asking that question instead of the caller remembering to. An area name the map does not know is ACCEPTED as an opaque label plus a `Warning` event, never a rejected create: a bookkeeping field must not refuse a launch over a typo. Nothing enforces what a delegate actually writes — drift is RECORDED at settlement (`ObservedScope`, a `ScopeDrift` event, `drift=` in the completion header) and never blocks, holds, kills or re-types anything; a PreToolUse path hook was considered and rejected because it could only ever be armed in a worktree, where an out-of-area write is already harmless. `pwsh -File scripts/delegate.ps1 -ListAreas` prints the map; the create response prints what your scope just cost; the completion header's `overlapping-running=` names the still-running tasks whose areas this one touched, which is the whole of the merge-ordering deliverable. **Extend the map when two tasks collide in an area, naming it for the work, not the folder** — and never give a glob a leading wildcard in its file name (`Services/*Profile.cs`), because the literal prefix collapses to the directory and the area silently swallows everything in it (`AreaMapContractTests` fails the build on it).

### Preserved Gotcha #36

- **A pre-dispatch failure is reminded on the check ramp until the caller hears, and it counts in the attention feed while that reminder is armed** (CARD-0231; CARD-0220 sent the one-shot `[task … failed]` note via `FailAndNotifyAsync` but nothing ever looked again): `AgentTaskDispatcher.FailAndNotifyAsync` arms `NextCheckAt`/`CheckCount` when `DispatchedAt is null` (`ArmFailureReminder` — first look at the 5-minute ramp base, not `ExpectedDurationMinutes`, because that number describes work that never started). `RemindUnacknowledgedFailuresAsync` (registered in `TickAsync` right after scheduled checks) re-sends the note only while nothing shows the caller has heard — note `Sent`, a human Drop (`Canceled`), `LastPolledResultAt`, or `ReadAt`. A still-Pending note with attempts left is in flight: advance the schedule, send nothing. After 10 reminders the ramp stops; the attention row (`AttentionKind.FailureUnacknowledged`, Error / Broken) stays until acknowledgement. `RunScheduledChecksAsync` must keep filtering to `Dispatched`/`Working` — Failed + `NextCheckAt` is now a legal state and must never reach the check worker.

### Preserved Gotcha #37

- **A killed `git worktree add` leaves git's own `locked initializing` behind, and a locked registration with no directory failed every future dispatch of that task id** (CARD-0220): `WorktreeManager`'s 30 s per-command timeout killed the checkout under IO load (a quiet add of this repo is 5.4 s), the catch deleted the directory, and the timeout's `TaskCanceledException` escaped the dispatcher's per-task catch and killed the tick. `worktree add` now has its own budget (`Git:WorktreeAddTimeoutSeconds`, 180), a timeout is a `TimeoutException`, a failed add rolls back fully (directory → `remove --force --force` → `prune` → branch), `CreateAsync` heals a registered-but-missing worktree (re-attaching the branch, never deleting it), and a dispatch failure reaches the caller as a `[task … failed]` note through `FailAndNotifyAsync`. `git worktree remove --force --force` clears a locked+missing entry in one command; it does NOT clear one whose directory is partially present — delete the directory first.

### Preserved Gotcha #38

- **A restart can strand "working" forever — two distinct ways** (REQUIREMENT, live miss 2026-08-08, Antiphon-Opus badged Working for 30+ min while idle): (1) *Backfill reordering*: stored transcript sequences are ARRIVAL-ordered — `PersistTranscriptAsync` rebases entries past the session max, so a catch-up sync that lands entries missed during a server restart/stream gap puts stale pre-gap activity ABOVE the already-persisted TurnEnd, and the seq-only working rule read mid-turn forever. Both server `IsWorkingAsync` and client `isWorking()` now carry a timestamp override (record timestamps survive reordering; equal ts keeps the seq verdict); the runner's `TranscriptWorkingState` deliberately has NO override (its mirror is file-ordered). `SessionRunnerEventPump` also catches up ALL runner sessions on every (re)connect — never rely on the lazy GET-transcript sync — and `SyncTranscriptAsync` fires the turn-end queue flush for boundaries that only ever arrive via backfill (the live path dedups them as "seen" and stays silent). (2) *Dead mid-turn process*: a session relaunched after dying mid-turn (reboot/kill) has no TurnEnd coming, ever. The launch paths write a synthetic `SessionRestartBoundary` (a turn END in all working-rule implementations) and, on a genuine `--resume`, queue an auto-continue prompt (`AgentSessionSettings.ResumeAutoContinue`, WhenIdle so it serialises after the launch note). Pinned by `SessionMessageQueueServiceTests` (backfill/boundary cases), `AgentControlServiceIntegrationTests` resume-recovery pair, and the client `isWorking` tests.
<!-- CARD-0254 preserved source ends -->
