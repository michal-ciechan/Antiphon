# CARD-0146 — Pipeline stages are roles; each stage carries a bundle and closes with a `next:` handoff

**Date:** 2026-09-03 (Plan pass, task c649c979 — design only; no production code changed, no tests run)
**Card:** CARD-0146 "Standardize the investigate/design/build delegation pipeline with self-reporting next-stage handoff" (Backlog, Normal/Normal, labels meta/workflow/orchestration/complex)
**Verified against:** worktree `feat/card-task-c649c979` @ `9ba098c6`, the live board on 17202, and the operator's project memory as of 2026-09-03 20:xx Z.
**Coordinates with:** CARD-0147 (plan landed `9ba098c6`, unbuilt — its per-role grouping key is confirmed here, not replaced), CARD-0151 (Backlog — inherits this vocabulary), CARD-0332 (S1 shipped `7126b93f` — the matrix is keyed by role, so the two new roles are two new rows), CARD-0333 (UI over 0332), CARD-0352 (S1–S3 landed — `complexity:hard|medium|easy` labels are the shape knob below), CARD-0304 / CARD-0301 (pipeline endpoint + phone view — the `ready` bridge generalises), CARD-0272 (`AgentTask.Stage` / `OrchestrationStage` — a name collision this plan routes around), CARD-0330 (distiller — the handoff block is an anchor it must keep), CARD-0096 (batch control — the automation consumer this plan feeds but does not build).

**Sources (read this pass):** the card; `docs/orchestration-loop.md`; `.claude/skills/antiphon-delegate/SKILL.md`; `scripts/{delegate,card,routing-pin,complexity-chain}.ps1`; `server/Bundles/{README,delegate-basics,orchestrator}.md`; `server/Application/Services/{DelegationReportFormatter,InstructionBundles,AgentTaskService,AgentTaskReplyService,AgentTaskPipelineStatusService,AgentTaskDispatcher,BlockedNote,ComplexityRoutingService}.cs`; `server/Application/Settings/DelegationSettings.cs`; `server/Domain/Enums/{AgentTaskEnums,OrchestrationStage}.cs`; `server/Domain/Entities/{AgentTask,StageOutcome,Workflow,Stage,StageExecution,CardWorkflowRun,CardWorkflowStage}.cs`; `client/src/api/agentTasks.ts`; the plans for CARD-0140, 0147, 0301, 0304, 0330, 0332, 0352; the investigation docs for CARD-0117, 0135, 0137; cards 0136, 0151, 0332, 0333, 0351, 0281, 0017; and the operator memory files `feedback_prefer_grok_dispatch`, `feedback_execute_wip_2_on_grok`, `feedback_verify_land_completion_before_restart`, `feedback_delegate_worktree_decision`, `feedback_prefer_sequential_dispatch`, `feedback_trust_reports_delegate_verification`, `feedback_estimate_as_verification_floor_plus_authoring`, `feedback_plans_land_on_master_fast`, `feedback_use_short_dispatch_titles`, `reference_delegate_self_cleanup_false_alarm`, `reference_report_git_attribution_tag`, `feedback_never_merge_master_while_shared_task_runs`.

---

## Verdict up front

1. **A pipeline stage IS an `AgentTaskRole`.** Two roles are added — `Investigate` and `TestDesign` — and the rest of the card's vocabulary maps onto roles that already exist (`Plan` = Design, `Code` = Build, `Review` = separate Verify). Nothing else in the system needs a second axis: routing pins, complexity chains (CARD-0332), `RolePolicy` tiers, `RecommendedInFlight` WIP, the pipeline endpoint, the phone view and CARD-0147's concurrency grouping are all already keyed by role. CARD-0304 and CARD-0301 both said "adding a real Investigate role is a taxonomy card, not this one" — this is that card.
2. **Merge/cleanup is not a stage anybody dispatches any more, and neither is deploy.** Since CARD-0272 the landing is `delegate.ps1 -Land <id>` (server-side fetch → rebase → verify → fast-forward → push → worktree removal → branch deletion, each step recorded as a `StageOutcome`), and deploy is the orchestrator's batched `deploy-local.ps1` (§6 of the loop doc). The card's "fold Merge/Cleanup into Build" option is **rejected on evidence**: the one night a Build delegate self-merged (2026-08-23, CARD-0137's task `6e2ec08d`) the harness raised a false "NOT merged" header because the delegate had removed its own worktree before settlement, and CARD-0215/CARD-0331 have since made the server the only party that knows landing order and land-queue state.
3. **Verify folds into Build by default.** The Build delegate runs the tests and positive controls the TestDesign stage wrote down, and reports each one; the orchestrator trusts that report (loop doc §0, reversal recorded 2026-08-30). A **separate `Review` dispatch is the escalation**, chosen by the card's complexity label (hard) or a safety-critical area, or requested by the Build report itself (`next: review`). `-Land -Verify <filter>` remains the server-side gate for one named test.
4. **Test/Verification Design is a first-class stage with its own role, contract and deliverable, and by default its own dispatch** for `complexity:hard` and `complexity:medium` cards; for `complexity:easy` it folds into the Plan dispatch as a mandatory section of the plan doc. Either way the deliverable is the same: a `## Verification design` section in the plan doc with a fixed sub-structure that names, in advance, which assertions need a positive control. Build executes that list and reports on every item. Same tier as Plan (Frontier), per the operator's instruction.
5. **The handoff is a fixed block, not prose.** Every stage-role report ends with a `--- next stage ---` block (`next:` / `handoff:` / `artifact:`) immediately above the existing `[antiphon-report:<id> …]` token. Settlement parses it into `AgentTask.NextStage` + `NextHandoff`, the completion header gains a `next=` bit beside `git=` / `deliverable=`, and the pipeline endpoint's `ready` list is generalised from "a landed plan is ready for Code" to "a task that declared `next: X` is ready for X". A report without the block still settles — `next=unmarked`, the way `report=unmarked` already works.
6. **The per-stage templates are bundles, one per stage role**, in `server/Bundles/stage-*.md`, composed at launch through the existing `InstructionBundles.ForDelegate` role map. They reach every kind (Claude `--append-system-prompt`, Grok `--rules`, Codex `-c developer_instructions=`), survive compaction, and are versioned by content hash. **No `-Stage` flag** (`-Role` already is one, and `AgentTask.Stage` is taken by CARD-0272), **no `scripts/delegate-stage-templates/`** (a second home for standing rules is the drift the bundle directory exists to end).
7. **Templates never name a kind.** Routing stays where it lives today — pins, chains, holds, the quota gate — all keyed by role. The evidence is blunt: the operator's kind instruction changed **fifteen times in ten days** (table below). A template that said "Grok" would have been wrong on eleven of those days.

Five slices, ~2 days. S1–S3 are the vocabulary, the handoff and the bundles and are useful alone; S4 generalises the pipeline projection; S5 is docs, the skill, and card annotations.

---

## What is true today (checked in code, not from the card)

| Card assumes | True on `9ba098c6` | Consequence |
|---|---|---|
| The delegation report is "free text today" and a structured field "may need tooling" | The brief already ends with a **fixed token** the harness parses (`[antiphon-report:<id> done\|blocked\|failed]`, `DelegationReportFormatter.ReportingContract` `:244–283`); a Blocked note is server-composed with fixed `reason:` / `asks:` / `authority:` / `next:` fields (`BlockedNote.cs:111–115`); settlement already regex-extracts a `docs/**/*.md` deliverable path from the report (`AgentTaskReplyService.ResolveDeliverableAsync` `:2104–2140`, called `:661`) and the completion header carries `report=`, `git=`, `drift=`, `deliverable=` bits (`DelegationReportFormatter.cs:395–436`). | The structured-vs-prose question is settled by precedent: fixed tail tokens parsed by code, degrading to a header flag when absent. The handoff block is one more of these. |
| Stage vocabulary needs inventing | CARD-0304 fixed **stage = `AgentTaskRole`** for the pipeline endpoint and CARD-0301 renders it; `AgentTaskPipelineStatusService.VisibleRoles` is `Enum.GetValues<AgentTaskRole>()` minus specialists (`:35`), so a new role appears in the pipeline with no code. CARD-0147's plan (landed today) groups its concurrency cap by role and says "revisit the grouping key when CARD-0146 ships; do not invent a parallel stage enum". | Add roles; do not add a stage enum. |
| "Investigate" exists as a stage | No role. CARD-0301's 7-day census (343 tasks): investigations were dispatched as `Plan` (some of 111), `Debug` (28) or `Custom` (8). | The vocabulary is being spent on the wrong role because the right one does not exist; RolePolicy/pins cannot route it and the pipeline cannot show it. |
| "Ready for the next stage" is something the orchestrator re-derives | `AgentTaskPipelineStatusService.BuildReady` (`:224–262`) already derives **one** edge mechanically: newest Succeeded `Plan` per card with a verified `DeliverablePath` under `docs/superpowers/plans/`, no newer/open `Code` task, card not terminal → a `ready` row under Code. The dispatcher also **holds** a Code Worktree task while its plan's land is in flight (CARD-0215). | The handoff block generalises this edge to every stage instead of hard-coding Plan→Code. |
| Templates need a place to live | `server/Bundles/*.md` are named, hashed, launch-composed standing-rule blocks; the role map is `InstructionBundles.ForDelegate` (`:163–189`: Orchestrator → `orchestrator`+`delegate-basics`, else `delegate-basics`, specialists none). `AgentTaskDispatcher` `:3280–3300` composes them for all three kinds. `CommandLineBudgetChars` = 30,000 (`DelegationSettings.cs:277`); `delegate-basics` is 3,489 chars, `orchestrator` 6,004. | One bundle per stage role, ≤ 2,500 chars each, leaves > 20 KB of budget. |
| A `-Stage` convenience flag would be new | `AgentTask.Stage` already exists as `OrchestrationStage?` (CARD-0272: Rebase / Verify / Cleanup / Review / FollowUp / Deploy — the **landing-step outcome** questions `-Land` records into `StageOutcomes`). | Do not add `-Stage`; call this concept "pipeline stage" in prose and `NextStage` in schema. `OrchestrationStage.Verify` (the `-Land` build/test step) and the card's "Verify" stage are different things; the role for a separate verify dispatch stays `Review`. |
| Routing reads "the live quota/availability signal once CARD-0136 ships" | CARD-0136 is Done (`7fe9bfc`): `SubscriptionQuotaGate` 409s at create. Holds (CARD-0022/0309/0335) pause dispatch per alias. Chains (CARD-0090, matrix CARD-0332 S1) skip held candidates. Pins (CARD-0305) outrank chains. `RolePolicy` is the provenance-less fallback. | Everything needed for kind-free templates exists. The dispatch line is `-Role <stage> -Complexity <label>`; the server picks the kind. |
| Merge/cleanup is a delegate's follow-up step | `delegate.ps1 -Land <id>` (CARD-0272) does fetch/rebase/verify/ff/push/remove/delete server-side and records each as a `StageOutcome`; the loop doc §5/§8 say the orchestrator's git involvement after `-Land` is zero. `Merge` role = the conflict specialist the server auto-spawns. | Landing is an orchestrator-ordered server operation, not a stage a delegate reports `next:` for beyond `next: land`. |
| The old workflow engine might be the home for stages | `Workflow` / `Stage` / `StageExecution` / `CardWorkflowRun` / `CardWorkflowStage` / `WorkflowDefinition` are the **card-spawn YAML workflow-template** path (`WorkflowEngine`, `IStageExecutor`, board `tracker:` front matter). Delegated tasks never touch it. | Not this card. Unifying the two is a separate, larger decision; nothing here makes it harder. |

---

## Evidence from tonight (what already works, and what the design must not break)

Grounding the design in this session's real practice, from the operator's memory files (dates are the entries' own):

- **The cycle that ran all night:** pick a card → Plan dispatch in a worktree → `delegate.ps1 -Land <planTask>` → Execute dispatch **in a worktree, from the landed plan** → `-Land <buildTask>` → `restart-apphost.ps1` → close the card with a verdict. Land-before-next-stage is enforced by the CARD-0215 dispatcher hold; land-before-restart was learned the hard way (CARD-0331, `feedback_verify_land_completion_before_restart`: a queued land dropped by a restart stranded a task at `LandRequested`).
- **WIP discipline (2026-09-01 → 09-02):** Execute WIP 2 → **1**; Plan WIP 1; "planning should only stop if more than 1 card waiting to execute" — i.e. Plan holds when the execute-ready queue reaches 2. Heavy fan-out is the exception (`feedback_prefer_sequential_dispatch`: three concurrent build-and-test dispatches drove free memory to 0.48 GB on 2026-08-31).
- **Selection cadence (2026-09-03 04:3x):** alternate one complex/UI card (fable) with one medium/simple card (Sol/Grok); prefer GitHub-linked cards.
- **Tier by complexity at the Plan stage (2026-09-02/03):** complex or UI → fable; medium → Sol; simple → Grok. Execute/verify/land → Grok, restated after every reset.
- **The kind instruction changed fifteen times in ten days:** 08-24 "everything Grok"; 08-25 four-way split (terra / luna / Grok / fable); 08-30 "Codex terra for all work" (Grok at 95 %); 08-31 lifted; 08-31 "grok for everything apart from planning"; 09-01 "fable never for execute"; 09-01 "all Claude fable/opus for everything"; 09-01 "plan fable, execute opus"; 09-01 wall hit, "plan opus/fable, execute grok"; 09-02 three-tier Plan split; 09-02 23:17 fable+opus held → Sol; 09-03 00:37 post-reset restatement; 09-03 04:3x alternation; 09-03 19:51 "no Anthropic for 1.5 h"; 09-03 20:0x manual Sol hold. **Every one of these is a pin, a chain row or a hold** — not a template line. Two gotchas from the same file confirm the layering already does the work when used: a hold does not override a pin (09-02 23:2x), and an auto-spawned Merge child ignores pins (09-02, same evening).
- **Trust the report (2026-08-30 reversal, now loop doc §0):** the orchestrator does not re-run tests or re-read diffs; concern goes back to the same delegate. Positive-control discipline lives in the *delegate's* verification, not the orchestrator's: CARD-0136 closed with "T10/T13 positive-control red confirmed before green"; CARD-0140's plan enumerates T1–T10 per slice and says which test "must land in the same commit" as its guard.
- **What went wrong when the handoff was prose:** a Plan task settled Succeeded with a complete design in chat and `git=no changes` (CARD-0346, 2026-09-03) — the artifact was never written and the orchestrator had to transcribe it; 218 of 289 task titles this week were 300-char goal excerpts because nothing forced a short one (CARD-0351/0352); the CARD-0330 census found the caller's loss is the *excerpt banner* eating defects 2–4 of a 7 KB review — i.e. what matters must be at the tail, where the token already is.
- **Every non-Claude brief spills to a file** and the typed prompt is a pointer (CARD-0353 correction, 2026-09-03 16:2x). Standing stage rules therefore cannot ride in the brief for Grok/Codex without paying the spill every time; the bundle channel is an argument and pays nothing.

---

## Decisions

### D1 (card "What to design"). The stage vocabulary, and its mapping onto roles

| Pipeline stage (card's word) | `-Role` | New? | Default tier (`RolePolicy`) | Default workspace | Deliverable | Allowed `next:` |
|---|---|---|---|---|---|---|
| **Investigate** | `Investigate` | **yes, `= 14`** | High, escalate Frontier (mirrors `Debug`; the "cheap" is a kind decision made by pins/chains, not a level) | `-Worktree` | `docs/investigations/<date>-card-nnnn-<slug>.md` | `plan` (root cause confirmed), `investigate` (not confirmed — says what would resolve it), `decide` (several live hypotheses; design must hedge), `none` (not a bug / already fixed / other repo) |
| **Plan / design fix** | `Plan` | no | Frontier | `-Worktree` | `docs/superpowers/plans/<date>-card-nnnn-<slug>-plan.md` | `test-design` (default for hard/medium), `code` (only when the `## Verification design` section is present — the easy fold), `decide` (plan written under stated defaults; decisions enumerated), `investigate` (the card's premise is wrong; says what to measure) |
| **Test / verification design** | `TestDesign` | **yes, `= 15`** | **Frontier** (operator instruction: same tier as Plan) | `-Worktree` | the `## Verification design` section appended to the plan doc (D5) | `code`, `decide`, `plan` (the design as written cannot be verified; names the gap) |
| **Build** (Verify folded) | `Code` | no | Frontier | `-Worktree`, always (`feedback_delegate_worktree_decision`, third miss) | commits on `feat/card-task-<id>`; the report's per-item verification table | `land`, `review` (a positive control failed, or the plan was wrong in a way the delegate patched), `code` (slices left; names them), `decide` |
| **Verify** (separate) | `Review` | no | Frontier | `-ReadOnly -Dir <build worktree>` (the branch is still there — nothing lands until the orchestrator orders it) | the review report (defects with Where/Failure/Why/Fix) | `land`, `code` (defects to fix; names them), `decide` |
| **Merge / cleanup** | — | — | — | — | `delegate.ps1 -Land <id>` → `Landed` / `LandedWithResidue` / `LandRefused` line | — (the orchestrator's action on `next: land`) |
| **Deploy** | — | — | — | — | `deploy-local.ps1` → `DEPLOY VERDICT` (orchestrator, batched per §6; the `Deploy` role stays for am-service) | — |
| **Close** | — | — | — | — | orchestrator verdict; `card.ps1 close` (a haiku delegate may execute it, §7) | — |

Rules that make the table hold:

- **No renames.** `Plan` stays `Plan` and `Code` stays `Code`; "Design", "Build" and "Execute" are prose/UI aliases (CARD-0304 already declared Execute an alias of Code). Two vocabularies for one axis is what CARD-0352 refused for complexity; the same applies to roles. The handoff line accepts the aliases and stores the canonical role.
- **`Debug`, `Test`, `Coverage`, `Docs`, `Commit`, `Deploy`, `Merge`, `Custom` are helpers, not stages.** They are dispatched *inside* a stage (a `Debug` for a red test during Build; a `Docs` slice; the auto-spawned `Merge` on a land conflict). They carry no stage bundle, and the handoff block is optional for them (typically `next: none`).
- **`AgentTaskRoles.IsStage(role)`** = `Investigate | Plan | TestDesign | Code | Review`. It is the predicate the handoff requirement, the `next=unmarked` bit, the stage bundles and S4's readiness all key on.
- **A stage is skippable when its question is already answered.** Investigate is skipped when the card's root cause is diagnosed (the card says so, or an earlier Investigate settled `next: plan`); Plan is never skipped (a "just build it" card is a Plan that settles in five minutes with `next: code`); TestDesign is folded, never skipped (D5); Review is skipped by default (D4).

### D2 (card Q1). The handoff is a fixed block above the report token, parsed at settlement

**Format** — the last thing in the report before the existing token:

```
--- next stage ---
next: plan
handoff: root cause confirmed - ComposerInputProbe reads the /usage overlay as idle, so VerifiedPromptSubmitter types into a closed composer; fix belongs in the probe's overlay check
artifact: docs/investigations/2026-09-03-card-0137-overlay-focus-normal-delivery-investigation.md
[antiphon-report:6e2ec08d done]
```

- `next:` — **required for stage roles**, one token, case-insensitive: `investigate | plan | test-design | code | review | land | decide | none`. Aliases accepted and normalised: `design→plan`, `build|execute→code`, `verify→review`, `merge|cleanup→land`, `testdesign|test design→test-design`.
- `handoff:` — **required**, one physical line, ≤ 400 chars. The crisp sentence the next brief is built from: for Investigate the confirmed mechanism (or exactly what is still uncertain and what would resolve it); for Plan/TestDesign the slice count and anything the builder must know first; for Code the verification tally and the restart need (`restart: server` / `runner` / `none`); for Review the defect count or "no defects".
- `artifact:` — optional, one repo-relative `docs/**/*.md` path. When present and the file exists (disk, then worktree branch — the same two roots `ResolveDeliverableAsync` checks), it **wins** over the regex's first match, which today is "the first `docs/*.md` mentioned anywhere in the report" and can point at a doc the delegate merely cited.
- `next: decide` is accompanied by a `## Decisions` section in the body: numbered items, each `D-n: <question> — default: <the choice the artifact was written under>`. That is the list `AskUserQuestion` takes verbatim. The task is **done**, not blocked — the artifact exists under the defaults; `blocked` + `asks:` remains the shape for "I cannot produce the artifact without an answer".
- Anything after the report token is ignored (existing rule). The block must precede it, which is also why it survives: the pty tail is the fragment measured to survive every mangling (`ReportingContract` doc-comment), and `FitReport`'s head+tail excerpt keeps it.

**Why a block and not disciplined prose.** Every mechanism in this harness that had to be reliable moved from prose to a fixed tail token and a parser with a degraded path: the report token (CARD-0046), the blocked note fields (CARD-0294), the deliverable pointer (CARD-0230), the `git=` tripwire (CARD-0261), the Check contract's one-line grammar (CARD-0339), the Diagnose grammar (CARD-0352). Prose is what produced the 300-char titles and the `git=no changes` plan. The orchestrator is a model too: a `next=` bit in the header is read at a glance; "what should happen now" re-derived from 3 KB of prose is the tax this card exists to remove. And the block is what CARD-0096's batch control can act on later without a second parser.

**Storage and surfacing (S2).** `AgentTask.NextStage` (`PipelineHandoffKind?`: `Investigate, Plan, TestDesign, Code, Review, Land, Decide, None`) and `AgentTask.NextHandoff` (`string?`, ≤ 400). Parsed once at settlement beside `ResolveDeliverableAsync` — an enrichment, never a settlement gate. Header bit `next=<token>` after `deliverable=`; stage role with no block → `next=unmarked`; unparseable token → `next=unrecognised:<first 24 chars>`. A `Warning` event is **not** written — the header bit is the signal, same as `report=unmarked`. The task drawer shows `next` + `handoff` under the deliverable line.

**The distiller (CARD-0330) treats the block as an anchor**: its deterministic gate must fail a distillation that drops `next:` or `handoff:`; the header is harness-built anyway. Add the block to that plan's anchor list at its execute time (one line on CARD-0330).

### D3 (card Q2). Templates are stage bundles; the brief keeps the state of today; no `-Stage` flag, no template directory

**`server/Bundles/stage-investigate.md`, `stage-plan.md`, `stage-test-design.md`, `stage-code.md`, `stage-review.md`**, each ≤ 2,500 chars (pinned by test), composed by `ForDelegate` as `[<stage bundle>, delegate-basics]` for a Worker in that role (`[orchestrator, delegate-basics]` for an Orchestrator is unchanged — a sub-orchestrator is not a stage). Each bundle carries, and only carries, the stage's **standing** rules:

| Bundle | Must say | Must not say |
|---|---|---|
| `stage-investigate` | Evidence only: measure, reproduce, cite file:line and transcript/DB rows. **Forbidden to design or implement a fix**; a fix idea goes in one line under "Not done, noted". Write `docs/investigations/<date>-card-nnnn-<slug>.md` (date = today, slug from the card), commit + push. `next:` vocabulary and what "confirmed" means (a mechanism, reproduced or reconstructed from stored evidence, with the uncertainties listed). | Anything about which model it is on; today's red tests. |
| `stage-plan` | Decisions with reasons and rejected alternatives; ground truth table; slices with files/tests; a `## Verification design` section is **required** when the brief says the test-design stage is folded (easy), otherwise `next: test-design`. Plan doc path convention. `next: decide` shape. "A design that only lives in chat is not a plan" (CARD-0346). | The kind; the WIP rules (orchestrator's). |
| `stage-test-design` | Read the plan doc first; append `## Verification design` with the D5 sub-structure; every guard that protects a safety-critical assertion gets a `PC-n` positive control; state the suites forced and the verification floor in minutes; `next: code` only when Build could execute the section without inventing anything. | A second copy of the fix design. |
| `stage-code` | Execute the landed plan and **its verification section**: run each `V-n`/`R-n`, run each `PC-n` as red-then-green, report every item pass/fail in a table; a PC the plan missed for a guard you touched is added and named; `next: land` only when every PC went red-then-green and nothing unplanned blocks; otherwise `next: review` or `next: decide` saying which. Name the restart need in `handoff:`. Never widen a timeout / loosen an assertion (already in delegate-basics; the stage bundle points at it rather than repeating). | Git landing steps (server's), deploy. |
| `stage-review` | Read the diff against the plan and its verification section; re-run claimed tests; run the listed PCs; defects as Where/Failure/Why/Fix; `next: land` or `next: code` with the defect list. Read-only. | Fixing anything. |

**What stays in the brief** (loop doc §3, unchanged): the card, the artifact path(s) from the previous stage, the `handoff:` line of the previous stage verbatim, today's known-red tests, what already landed, what is out of scope, the `-Authority` text, and — for Plan when the card is `complexity:easy` — the sentence "the test-design stage is folded into this dispatch; the verification section is required".

**Why not `delegate.ps1 -Stage`.** `-Role Investigate` *is* the switch: it selects the bundle, the tier, the pin row, the chain row, the WIP recommendation and the pipeline lane. A second flag would be a second name for the same thing, and `Stage` is already a column with a different meaning (CARD-0272). What the card wanted from `-Stage` — "pre-fills the standard framing so a dispatch only supplies the card-specific content" — is exactly what the bundle channel does, and it does it for warm-pool reuse and Grok/Codex spills too, which a CLI-side prefill would not.

**Why not `scripts/delegate-stage-templates/`.** A template file the caller pastes into `-Goal` is typed into the pty (or spilled to a file for Grok/Codex) on every dispatch, pays the delegate's attention every time, and becomes the third copy of rules that already drifted twice (the skill doc's own history under "What the delegate is told"). Bundles are argument-delivered, hashed, and reach every future launch from one PR. The **dispatch recipes** — the one-line `delegate.ps1` invocation per stage — do belong somewhere the orchestrator reads: the skill doc (S5), not a directory of prompts.

### D4 (card Q3). Verify folds into Build; Review is the escalation; the complexity label is the knob

Default shape per card, driven by the `complexity:` label CARD-0352's Diagnose seat writes (until its S4 sweep lands, the orchestrator judges it the same way it already judges Plan tier):

| Label | Investigate | Plan | TestDesign | Code (Verify folded) | Review | Dispatches |
|---|---|---|---|---|---|---|
| `complexity:easy` | skipped unless the card says the cause is unknown | yes, **folds TestDesign** (section required) | folded | yes | no | 2 |
| `complexity:medium` | only if the root cause is unconfirmed | yes | **separate** | yes | no | 3–4 |
| `complexity:hard` | yes unless already diagnosed | yes | **separate** | yes | **separate** | 4–5 |

Overrides, in order: a `safety-critical` card label, or a Build report that says `next: review`, forces the separate Review; the operator can force any stage separate in the brief; a `-Land -Verify <filter>` is available at every land regardless.

Why fold by default: the loop's §0 rule ("trust the report … re-running a named test just to be sure is not diligence") is the operator's explicit 2026-08-30 reversal, and CARD-0330's census shows the Build report already carries the verification table (the 7 KB reviews were dense, not padded). A standing separate Verify on every card would double the Frontier-tier dispatches on the half of cards the alternation rule deliberately keeps cheap. Why keep Review as a real stage: the same census shows the two most valuable reports of the period were reviews that found four numbered defects each. Why the knob is the complexity label and not a new field: it is the one axis the fleet already routes on (CARD-0332), labels on (CARD-0352), alternates on (memory 09-03 04:3x) and will show in settings on (CARD-0333). One knob, four consumers.

**Not code-enforced in this card.** The shape is a skill-doc rule plus the stage bundles' `next:` vocabulary; CARD-0096's batch control is where "dispatch the next stage automatically from `next=`" belongs, and S4 gives it the data.

### D5 (card, "Additional required stage"). Test/Verification Design: its own role, its own dispatch for hard/medium, one deliverable shape either way

**Separate dispatch or a section?** Both, keyed by complexity (D4): separate for hard/medium, folded for easy. The reasons for a *separate* default where it matters:

- The operator asked for "a deliberate design pass, not an afterthought", at Plan tier. A second deliverable at the end of a long Plan turn is structurally the afterthought — it is written when the agent's budget and attention are lowest. A fresh dispatch that reads the landed plan cold is a second pair of eyes on the design for free, and it is the shape that already produced the positive-control tables on CARD-0136/0140.
- Separability: a revised fix design re-runs TestDesign without redoing Plan (`next: test-design` from the revising Plan task), and a rejected verification section sends the design back (`next: plan`) without touching Build.
- Visibility (the card's CARD-0151 concern): a folded stage is invisible in an audit keyed on task rows. With a separate row it is a `TestDesign` task with its own cost, duration, kind and `next=`; when folded, the audit rule is explicit — **a `Plan` task that settles `next: code` covered TestDesign; one that settles `next: test-design` did not** — so CARD-0151 can attribute either way from `Role` + `NextStage` and nothing extra is stored.

Why not `-OnAgent` on the design agent as the default: a live follow-up forces `Workspace = Shared` in the agent's directory (`AgentTaskService.CreateAsync` `:262–274`), which for a landed Plan is a worktree that `-Land` has already removed. The uniform pattern tonight is land-then-next-stage-in-a-fresh-worktree (CARD-0215 hold), and TestDesign follows it. `-OnAgent` stays an allowed optimisation when the orchestrator deliberately lands once after both.

**The deliverable — `## Verification design`, appended to the plan doc** (one file Build reads; the section is owned by the TestDesign stage whoever writes it):

```
## Verification design
### Proves it works now
- V-1: <behaviour> · <layer: unit | integration | E2E | live probe> · <test or command> · <expected>
### Guards the regression
- R-1: <the future change that would reintroduce the defect> · caught by <test> because <assertion>
### Positive controls  (Build runs each: break, see red, revert, see green — and reports all three)
- PC-1: break <guard> by <one-line edit>; expect <test> red
### Out of scope
- <what is deliberately not tested, and why>
### Cost
- suites forced: <assemblies / filters>; verification floor ≈ <N> min  (feeds -ExpectAbout: floor + authoring)
```

**Positive controls are specified in advance** — that is the answer to the card's second TestDesign question. The `PC-n` list is the contract: Build must run every one and report red-then-green per item; a Build report whose table lacks a listed PC is what `next: review` is for. The **Cost** block is what makes `-ExpectAbout` honest (`feedback_estimate_as_verification_floor_plus_authoring`: the missing term was always the verification floor).

**Tier:** `RolePolicy["TestDesign"] = Frontier`, `RecommendedInFlight = 1`. Pins/chains route the kind exactly as for Plan; the operator's "Sol for medium planning" applies to TestDesign the moment a `(TestDesign, Medium)` cell or pin says so.

**CARD-0151:** not landed (Backlog). When it is planned it inherits `AgentTaskRole` as the stage axis and `NextStage` as the transition evidence; the folded-stage attribution rule above is the only convention it needs from here. Recorded on CARD-0151 at execute (S5).

### D6 (card Q4). Routing: templates and recipes never name a kind

- The dispatch line for every stage is `delegate.ps1 -Role <stage> -Complexity <hard|medium|easy> -Card CARD-nnnn -Title "…" -Worktree -ExpectAbout <floor+authoring>`. **No `-Kind`, no `-Level`** unless the operator says so for that dispatch, in which case the reason goes in the goal (existing rule).
- The server resolves the kind: Human pin on `(card, role)` → stage-wide pin on `role` → chain cell `(role, complexity)` (CARD-0332) → any-role chain → config → RolePolicy. Holds, the quota gate and provider sign-in refuse or skip candidates along the way.
- **An operator kind instruction is a pin or a chain row, written with `routing-pin.ps1` / `complexity-chain.ps1`**, not a memory-file rule the orchestrator applies by hand. The fifteen-flip table is the case for this: each of those was a one-line pin/chain/hold, and the two gotchas of 09-02 both came from the instruction living in memory while a stale pin lived in the DB.
- **On landing S1 the operator writes the rows for the two new roles** that mirror the live instruction (tonight: Investigate → Grok stage-wide pin; TestDesign → same as Plan's per-complexity choice). Not seeded by the migration — CARD-0090/0332's "no guessed seed" decision stands.
- `RolePolicy` defaults for the new roles are only the provenance-less fallback: `Investigate` High→Frontier (like `Debug`), `TestDesign` Frontier (like `Plan`).

### D7. WIP and ordering stay the orchestrator's rules; the projection stops guessing

- `RecommendedInFlight = 1` for both new roles. Tonight's rules become the documented default in the loop doc §1: Plan-side WIP 1 (Investigate + Plan + TestDesign together), Execute WIP 1, Plan holds when the Code stage's `ready` list has ≥ 2 rows, alternate complexity, GitHub-linked first. Advisory, as CARD-0304 decided; CARD-0147's create-time 409 is the hard stop.
- S4 generalises `ready`: a settled Succeeded stage task with `NextStage = X ∈ {Investigate, Plan, TestDesign, Code, Review}` whose card has no open or newer task in role X, and whose card is not terminal/NeedsDecision/archived → a `ready` row under stage X carrying `sourceTaskId`, `sourceRole`, `deliverablePath`, `handoff`. The legacy Plan→Code artifact bridge stays as the fallback for rows with `NextStage = null`. `land` and `decide` produce no ready row: the first is the orchestrator's `-Land`, the second is a human's.
- The CARD-0215 hold generalises with it: a stage-role Worktree task is held while a same-card sibling's land is in flight, not only a Code task behind a Plan.

### D8. Name collision with CARD-0272, resolved by naming

`AgentTask.Stage : OrchestrationStage?` and `StageOutcomes` stay exactly as they are — they answer "what did each landing step find" and feed the hit-rate report. This card adds `NextStage : PipelineHandoffKind?` and says "pipeline stage" in every doc. The word "Verify" appears in both vocabularies: `OrchestrationStage.Verify` is the `-Land` build/test step; the pipeline's Verify is the Build delegate's own run (folded) or a `Review` dispatch (separate). Neither is renamed; the loop doc gets one sentence saying so.

---

## Slices

Sequential; each lands through `-Land` and is useful alone. Line numbers at `9ba098c6`.

### S1 — Vocabulary: two roles, every place the role set is spelled out (~3 h)

**Files:** `server/Domain/Enums/AgentTaskEnums.cs` (`Investigate = 14`, `TestDesign = 15`; `AgentTaskRoles.IsStage`), `server/Application/Settings/DelegationSettings.cs` (`RolePolicy` entries `:293–311`), `server/Application/Services/ComplexityRoutingService.cs` (`RoutableRoles` `:27` — insert in pipeline order: Investigate, Plan, TestDesign, Code, Review, then the rest), `server/Application/Services/RoutingPinService.cs` (the routable-role guard beside the specialist refusal `:526`), `scripts/delegate.ps1` `:15`, `scripts/routing-pin.ps1` `:33`, `scripts/complexity-chain.ps1` `:33` (ValidateSets), `client/src/api/agentTasks.ts` (`AgentTaskRole` union `:14`, `AGENT_TASK_ROLES`), the CARD-0301 stage labels (`Investigate`, `Test design`), `docs/antiphon-api.md` role list.

**Tests:** `AgentTaskServiceIntegrationTests` role-policy defaults (new entries, `RecommendedInFlight = 1`); `DelegateLaunchArgvIntegrityTests` (`:89`, `:124`) and `SpecialistRoleContractTests` (`:28`) iterate `Enum.GetValues<AgentTaskRole>()` and must stay green unedited; `AgentTaskPipelineStatusTests` — the stage count assertion moves from 11 to 13 visible roles, in enum order; `ComplexityChainServiceTests` — a `(TestDesign, Hard)` cell round-trips; `Scripts/*ScriptTests` if a ValidateSet is pinned there; client `agentTasks` type test / `attentionVisuals`-style totality test if one keys on roles (grep says none does today — confirm at execute).

**Not in S1:** no bundle, no parser. A `-Role Investigate` dispatch after S1 launches with `delegate-basics` only and routes via RolePolicy/pins like any other role.

**Operator step on landing:** write the Investigate/TestDesign pins or chain cells that mirror the live routing instruction (D6).

### S2 — The handoff block: contract text, parser, columns, header bit (~4 h)

**Files:** `server/Application/Services/DelegationReportFormatter.cs` — `ReportingContract` gains a role-aware paragraph (the block spec, ≤ 700 chars, only for `IsStage(role)`; the pointer path's abbreviated contract `:587–606` gains one line "close with the `--- next stage ---` block above the token"); header bits `:395–436` gain `next=`. New `server/Application/Services/PipelineHandoff.cs` (`PipelineHandoffKind` enum; `TryParse(report) → (kind?, handoff?, artifactPath?, rawToken?)`: last `--- next stage ---` before the token, alias map, ≤ 400-char handoff, path must match `DeliverablePathPattern`). `server/Domain/Entities/AgentTask.cs` (`NextStage`, `NextHandoff`), `AppDbContext` config, hand-written migration `AddAgentTaskNextStage` (attributes in-file, snapshot edited by hand — CARD-0332's note about the daemons locking `bin/` applies), `AgentTaskReplyService.cs` — call the parser beside `ResolveDeliverableAsync` (`:661`) and let `artifact:` win when it resolves; task DTO + drawer (`TaskDetailBody.tsx`) show `next` / `handoff`.

**Tests:** `PipelineHandoffParseTests` — every token and alias; missing block → null; two blocks → the last; block after the token → ignored; handoff clipped at 400; `artifact:` pointing at a file that does not exist → falls back to the regex path; a report whose first `docs/*.md` mention is a *cited* doc but whose `artifact:` names the real one → the real one wins (the CARD-0346 shape, inverted). `AgentTaskReplyIntegrationTests` — a settled Investigate report stamps `NextStage = Plan`, header carries `next=plan`; a stage role without the block → `next=unmarked`; a `Docs` task without it → no bit. `InstructionBundleTests`/formatter tests — the contract paragraph appears for the five stage roles and for no other; the Check/Diagnose contracts are untouched (`CheckReportingContract`, `DiagnoseReportingContract`). Positive control: remove the `IsStage` guard and assert the `Docs` contract test goes red.

**CARD-0330 note:** add `next:` / `handoff:` to its anchor list (one line on that card; if its S1–S3 land first, one test there).

### S3 — Five stage bundles and the role map (~4 h)

**Files:** `server/Bundles/stage-{investigate,plan,test-design,code,review}.md` (D3 table; ≤ 2,500 chars each; ASCII-safe like the others), `server/Application/Services/InstructionBundles.cs` — constants and `ForDelegate` `:163–189`: Worker + `IsStage(role)` → `[stage-<role>, DelegateBasics]`; Orchestrator unchanged; specialists unchanged. `server/Antiphon.Server.csproj` embedded-resource glob already covers `Bundles/*.md` — confirm. `server/Bundles/README.md` "Which agent carries which bundle" paragraph.

**Tests:** `InstructionBundleTests` — the exact key set (it pins the catalog, so the five new keys are a deliberate edit); each stage bundle ≤ 2,500 chars; `ForDelegate(Worker, Investigate)` = `[stage-investigate, delegate-basics]` and `ForDelegate(Worker, Docs)` = `[delegate-basics]`; composed size for `(Worker, Code)` with both attachments a delegate could realistically carry stays under `CommandLineBudgetChars`; the INVARIANTS sentences of each bundle pinned by substring (the same shape as the orchestrator bundle's "Delegate the reading" pin from CARD-0017): investigate forbids fix design; test-design requires the PC list; code requires red-then-green per PC and `next: land` gating; review is read-only. `DelegateBundleLaunchTests` — a Grok `Investigate` launch carries `--rules` with the `[bundle:stage-investigate v…]` stamp; a Codex one `-c developer_instructions=`.

### S4 — Generalise `ready` and the sibling-land hold (~3 h)

**Files:** `server/Application/Services/AgentTaskPipelineStatusService.cs` (`BuildReady` `:224–262` → handoff-driven readiness per D7 with the legacy Plan→Code fallback), `server/Application/Dtos/AgentTaskPipelineDtos.cs` (`AgentTaskPipelineReadyDto` + `sourceRole`, `handoff`), `AgentTaskDispatcher.cs` (the CARD-0215 hold predicate: any `IsStage` Worktree task, not only Code), `client/src/api/agentTasks.ts` DTO mirror, the CARD-0301 panel (ready rows render on every stage; its "Code stage only" note goes), `client/src/test/fixtures/contract/pipeline.json` recaptured from `ContractSnapshotTests.Pipeline_status_contract`, `docs/antiphon-api.md` pipeline section.

**Tests:** `AgentTaskPipelineStatusTests` — an Investigate task settled `next: plan` yields a Plan-stage ready row; a Plan settled `next: test-design` yields a TestDesign row and **no** Code row even though a plan doc exists (the fold rule); a Plan settled `next: code` yields a Code row; `next: land` / `next: decide` / `next: none` yield none; a newer open task in the target role consumes readiness; a `NextStage = null` Plan with a plan doc still yields the legacy Code row. `DelegationScopeHoldTests`/dispatcher tests — a TestDesign Worktree task is held while its card's Plan land is in flight. Contract snapshot recapture.

### S5 — Docs, skill, cards (~2 h)

- `docs/orchestration-loop.md`: §1 cycle diagram rewritten as the stage pipeline with the handoff; §2 tier table gains the two roles and the "stage vs helper" split; §3 gains "what the stage bundle already says vs what the brief must say"; the D4 shape table; the D7 WIP defaults as the documented rule (with the memory entries' dates as provenance); the D8 sentence on the two "Verify"s; §5 says `next: land` is the cue for `-Land`.
- `.claude/skills/antiphon-delegate/SKILL.md`: role table gains the roles; a **Stage recipes** section — one `delegate.ps1` line per stage, kind-free, with the brief's required lines (previous stage's `handoff:` verbatim, artifact path, fold sentence for easy cards) and the `-ExpectAbout` band from the verification section's Cost block.
- `AGENTS.md` "Cards and tracker": one line — stages are roles; every stage report ends with the `--- next stage ---` block; the orchestrator dispatches the next stage from `next=`, never from re-reading the body.
- `server/Bundles/orchestrator.md`: one paragraph — read `next=` and `handoff:` from the completion header/tail; dispatch the named stage; `next=unmarked` on a stage role is a report to send back to the same delegate, not a reason to read the diff.
- Cards, via `card.ps1 edit` with a reason: CARD-0147 (grouping key confirmed = role; add the two roles to its default per-role cap), CARD-0151 (vocabulary = `AgentTaskRole` + `NextStage`; folded-stage attribution rule), CARD-0301 (ready rows on every stage after S4), CARD-0330 (anchor list), CARD-0333 (two more grid rows). This card's own description already carries the TestDesign bullet (edited in this pass).

---

## Ground truth the executor needs (line numbers at `9ba098c6`)

- `AgentTaskRole` `AgentTaskEnums.cs:26–40`: `Custom=0 … Merge=10, Check=11, Distill=12, Diagnose=13`. `AgentTaskRoles.IsSpecialist` / `NotSpecialist` `:303–314`.
- `RolePolicy` defaults `DelegationSettings.cs:293–311`; `RecommendedInFlightFor` `:823`; startup validator `:915–939`; `CommandLineBudgetChars = 30_000` `:277`.
- `InstructionBundles.ForDelegate` `:163–189`; keys `:79–91`; catalog pinned by `InstructionBundleTests` (exact key set); `ForDelegate` iterated over every role at `:424`.
- Bundle composition and per-kind flag: `AgentTaskDispatcher.cs:3280–3300` (`--append-system-prompt` / `--rules` / `-c developer_instructions=`), budget guard before anything is added.
- `ReportingContract` `DelegationReportFormatter.cs:244–283`; `CheckReportingContract` / `DiagnoseReportingContract` follow; pointer-path abbreviated contracts `:587–606`; completion header bits `:395–436` (`report=` `:424`, `git=`, `deliverable=`).
- `ResolveDeliverableAsync` `AgentTaskReplyService.cs:2104–2140` (regex `` `?(?<path>docs/[\w./-]+\.md)`? ``, first existing match wins; disk roots then worktree branch); called at `:661`.
- `BlockedNote.cs:111–115` — the existing `next:` line on a *blocked* note (an action cue for the caller); the handoff block's `next:` is a stage token on a *done* report. The two never appear on the same note.
- `AgentTaskPipelineStatusService.cs`: `VisibleRoles` `:35`; `BuildReady` `:224–262`; `CodeConsumesReadiness`; ready only under Code `:174`.
- Follow-up on a live agent forces `Workspace = Shared` + the agent's directory `AgentTaskService.cs:262–274`; a retired agent → fresh delegate with an inherited-context goal `:227–239`.
- `AgentTask.Stage : OrchestrationStage?` `AgentTask.cs:33`; `OrchestrationStage` = Rebase/Verify/Cleanup/Review/FollowUp/Deploy; `StageOutcome` rows written by `AgentTaskLandService.cs:161–260`.
- ValidateSets: `delegate.ps1:15`, `routing-pin.ps1:33`, `complexity-chain.ps1:33` (+ `Any`). Client union `agentTasks.ts:14–30`.
- Live routing state on 2026-09-03: one stage-wide Human/Required pin, `Code → Grok`; fable+opus manual holds until 20:22 Z; `gpt-5.6-sol` manual hold ~1 h from 20:0x Z. `ComplexityChains`: 0 rows. Cards with a `complexity:` label: 0 (CARD-0352 S4 not landed).

---

## Risks, and what is deliberately not done

- **The vocabulary is only as good as the reports that use it.** A Grok/Codex delegate that ignores the block still settles (`next=unmarked`); the orchestrator's rule (S5) is to send that back to the same delegate, which is exactly the loop doc's §0 ladder. Measure after a week the way §3 measures brief hygiene: grep settled stage-role tasks for `NextStage IS NULL`.
- **Enum growth touches many pins.** Three test files iterate the enum and a fourth pins the bundle catalog; the plan names each so the executor edits deliberately rather than discovering them red.
- **A folded TestDesign can still be skimped.** The mitigation is the fixed section structure (a missing `### Positive controls` heading is a Build `next: review`), not a second dispatch on easy cards — the operator's cost signals tonight (alternation, WIP 1, holds) say the cheap lane must stay cheap.
- **Not done: automatic next-stage dispatch.** `next=` is the input CARD-0096 needs; acting on it is that card, with its own spend gate.
- **Not done: any change to `-Land`, `deploy-local.ps1`, `OrchestrationStage`, or the YAML workflow-template engine.**
- **Not done: renaming `Plan`/`Code`, or a `-Stage` flag, or a template directory** (D3).
- **Not done: a `safety-critical` label taxonomy.** D4 names it as an override; defining which areas are safety-critical (delivery, pty, sessions per AGENTS.md's safety triggers) is one line in the skill doc, not a schema.
