# CARD-0811: post-land miss retrospective and Low-tier instruction-gap pass

Date: 2026-09-29. Stage: Plan, with TestDesign folded in (docs and process only, easy-shaped;
CARD-0811 carries no `complexity:` label). Next: Code.
Baseline: `d2fb7536eb6385822b5d306fc49157effa6de35c`. Plan task `2503d3a1`.

## Outcome and scope

Add one standing orchestrator process: when a defect that a Clean Review approved is found only
in the running system after land, the orchestrator records a same-board companion, overrides
the Review finding once it is safe to do so, dispatches an Investigate task that writes why the
pipeline missed it, then a Low-tier Docs task that drafts the instruction change, and disposes
of the draft with one of three fixed actions. Nothing fires for a defect Review or Mutation
caught before land.

Footprint: one subsection in `docs/orchestration-loop.md`, one sentence in
`server/Bundles/orchestrator.md`, one bullet in `.claude/skills/antiphon-orchestrator/SKILL.md`,
and one new Unit docs-contract test class. No server code, schema, API, client, card status,
tick, label filter or scheduled action is added. The exact prose is in the appendix so the Code
stage copies it rather than composing it.

## Ground truth

The full card was read from the board on 2026-09-29 (`d81e54d6-17ca-4fc8-a329-d5173b830b61`,
board `8988ca03-7414-47ad-b0b6-51556c701703`, In Progress, no labels, both verification
policies FullOnly, platform Any). Live API reads below were made from this Plan task.

| Card assumption or proposal | Code or durable record | Implication |
|---|---|---|
| The orchestrator can tell a post-land miss from a pre-land catch. | `GET /api/stage-outcomes?cardId=<guid>&stage=Review` returns per-card rows with `outcome`, `source`, `reviewedSourceSha`, `supersedesId` (`StageOutcomeDto`). `GET /api/agent-tasks/<id>` carries `landing.publication`, `landing.verifiedSha`, `landing.remoteConfirmedAt`, `landing.cleanup` (`LandingEnums.cs`). Verified live: card `db24e0b7` has one Review row `Clean/Delegate` with C `629d2dcc`; its subject task `f5d7a783` shows `Landed`, L `7286fc6c`, confirmed 2026-09-28T00:06:23Z, cleanup `Refused`. | The trigger is a checklist over existing reads. No new signal, status or event is needed. |
| "Why it was missed" has no home. | `server/Bundles/stage-investigate.md` already requires `docs/investigations/<date>-card-nnnn-<slug>.md`, evidence only, commit and push; `docs/orchestration-loop.md` section 7 sends findings that outlive a card there. | Reuse the Investigate stage; fix the file name and headings in the brief. |
| A companion task should run alongside the fix. | CARD-0478 defines the companion pattern: title, label `post-land-verification`, stable key in the description, client-side discovery, never Spawn, no tick creates cards or spends quota (`docs/orchestration-loop.md` lines 1393-1403). `GET /api/cards` refuses an unfiltered read and has no label filter (`docs/ops-http.md` line 201). | Mirror that pattern with its own title, label and key. Discovery is by `boardId` plus `status`, then a client-side search for the key. |
| A cheap model drafts bundle changes. | Stage bundles are capped at 2,480 trimmed characters (`TaskPlatformGuidanceTests`) and 2,500 (`InstructionBundleTests`), ASCII-only, and sentence-pinned by tests. `server/Bundles/README.md` says a brief-specific fact never belongs in a bundle. | The Low-tier task proposes; it never edits a bundle. A bundle proposal must name the sentence it removes. |
| Part 1 is "any tier". | Tiers table: `Investigate` opus, `Docs` sonnet, `-Level Low` is haiku or Codex Luna (`docs/agent-kinds.md` section 3). | Task A uses the Investigate role default. Task B is `-Role Docs -Level Low`. |
| The miss should feed the hit-rate signal. | `delegate.ps1 -Finding <task> -Stage Review -Found "..."` writes an Orchestrator `StageOutcome` that supersedes the latest row (`StageOutcomeService.RecordFindingAsync`); `scripts/stage-value-report.ps1` counts it. `LandApproval.LoadUsableEvidenceAsync` refuses superseded evidence with `review_evidence_superseded`, and residue or cleanup retries go through `-Land` again. | Record the override only when `landing.cleanup` is `Complete`; otherwise carry `override pending` on the companion. |
| Home: next to the rule that files a card for a structural defect. | That rule is item 6 of "Standing pipeline policy" in `docs/orchestration-loop.md`; its delivered copy is one sentence in `server/Bundles/orchestrator.md` (12,286 chars, no per-file cap, composition budget 30,000 in `DelegationSettings.CommandLineBudgetChars`); the orchestrator skill repeats it at `.claude/skills/antiphon-orchestrator/SKILL.md` line 42. | Three edits: the owner subsection, one bundle sentence, one skill bullet. |
| A docs card still needs a closed checkpoint list and a test that can go red. | `PostLandMutationContractTests` is the docs-contract precedent (Unit, reads docs and composed bundles); CARD-0688 CP-11 is the precedent for a `git grep` non-TUnit row. | One new Unit class plus one grep row. |
| Platform. | `GET /api/runner-defaults` on 2026-09-29: global default `server2`, kinds Grok, ClaudeCode, Codex. `GET /api/session-runners`: one Windows and one Linux runner. | Every task here is platform Any; omit `-Runner` and `-Platform`. |

## Decisions

### D-1: the trigger is a five-point checklist run when the defect arrives

The orchestrator is the party that receives a live defect (an operator message, an incident, a
Debug or Investigate report against the running system). It fires the process only when every
point in the appendix checklist holds, each read from the API at that moment: the fix card names
the landed card and the observation; the landed card's latest Review row is Clean with C and no
`supersedesId`; the subject task's landing is Landed or AlreadyPresent with L and T; the
observation is later than T against a running build containing L; the CARD-0478 companion does
not already report the defect from Mutation.

Rejected: automatic detection on card reopen or a pipeline tick (no tick creates cards or spends
quota; findings never automatically reopen the original; a reopen carries nothing that separates
post-land from pre-land). Rejected: a new `StageOutcomeKind` or `StageOutcomeSource` (schema,
client and report changes for a signal the existing override already carries). Rejected: a label
on the fix card alone (not discoverable by Review outcome, so it cannot deduplicate).

### D-2: the durable record is a companion card, a gated Review override and an investigation file

Companion: `Post-land retrospective: <landed identifier>`, label `post-land-retrospective`,
stable key `post-land-retrospective:<review-outcome-guid>`. One Review outcome gets one
retrospective. The override on the Review task makes the miss visible to
`stage-value-report.ps1`; it is recorded only when `landing.cleanup` is `Complete`, because a
later `-Land` retry loads the evidence by ID and refuses a superseded row. The investigation file
is the durable narrative (section 7's rule that findings outliving a card go to
`docs/investigations/`).

Rejected: the override alone (invisible on the board and blocked while cleanup is pending).
Rejected: the card alone (loses the hit-rate signal the card asks for). Rejected: a card
description or chat reply as the only home for the retrospective (rule 6's own reasoning: a
finding in a report body is gone at the next compaction).

### D-3: Task A is Investigate at its role default; Task B is Docs at Low tier, after A lands

Task A: `-Role Investigate -Worktree -Card <companion-guid>`. The stage bundle already forbids
fix design and requires a committed `docs/investigations/` file; the brief fixes the file name
`docs/investigations/<date>-card-<landed nnnn>-post-land-retrospective.md` and four headings.
`## Why it was missed` is exactly one of six classifications (D-5).

Task B: `-Role Docs -Kind ClaudeCode -Level Low` (or `-Kind Codex -Level Low`)
`-Worktree -Card <companion-guid>`, dispatched after Task A lands. It appends one section,
`## Instruction-gap proposal (Low tier)`, of at most 25 lines to the same file and lands.

Rejected: one opus task for both parts (defeats the cheap-model goal and mixes evidence with
proposal). Rejected: Task B as ReadOnly reporting only (the proposal would live only in a report
body). Rejected: `-OnAgent` the Investigate task (keeps the higher tier). Rejected: Task B editing
a bundle or a test (size caps and sentence pins; changes go through Code and Review).

### D-4: home is the orchestration loop, with one sentence in the bundle and one bullet in the skill

`docs/orchestration-loop.md` gets `### Post-land miss retrospective (CARD-0811)` directly after
the "Create-time per-project and per-role gates" paragraph that closes the standing policy list,
before "### Reprioritising the backlog". `server/Bundles/orchestrator.md` gets one sentence after
"File a Backlog card the moment Investigate or Review finds a structural defect; never batch
them." The orchestrator skill gets one bullet after its "File a card immediately" bullet.
`AGENTS.md` is untouched: the owner document is already linked from its table.

Rejected: a new bundle (nothing to attach it to; every orchestrator would need an attachment
row). Rejected: a stage bundle change (no stage runs this process, and stage bundles are at their
caps). Rejected: an `AGENTS.md` bullet (safety core, not process detail).

### D-5: closed vocabularies for the classification and the verdict

Classification, exactly one: `not-designed`, `designed-not-asserted`, `asserted-wrong-layer`,
`bundle-gap`, `reviewer-deviation`, `environment-only`. Verdict, exactly one:
`no-instruction-change`, `doc-change`, `bundle-change`, `escalate`. The Low-tier task branches
on the classification; a later census can grep both. Rejected: free text (not mechanical, not
countable, and a Low-tier model drifts without it).

### D-6: disposition is one of three fixed actions and never applies a Low-tier draft directly

`no-instruction-change`: close the companion Done, naming the classification and the file.
`doc-change` or `bundle-change`: file one Backlog card `Instruction gap: <one line>` with the
proposal as its description and label `instruction-gap`, then close the companion naming it; the
ordinary pipeline's Plan judges the proposal. `escalate`: move the companion to NeedsDecision
with the question on the move revision (the existing decision path; no new column or alert).
Rejected: the orchestrator judging or applying the proposal itself (the card wants escalation to
a higher tier or a human, and rule 6 wants every finding filed, not batched).

### D-7: verification is folded and docs-contract shaped

The change is prose. Verification is one new Unit class that pins the load-bearing sentences in
the three edited files and the composed orchestrator bundle, plus the existing classes that read
those files. Each new test can go red by deleting the sentence it names (PC-1, PC-2). No build of
the server, client or runner is involved beyond the test project.

## Implementation slices

One committed group; commit each slice, then run the checkpoint table once.

| Slice | Files | Content |
|---|---|---|
| S1 | `docs/orchestration-loop.md` | Insert appendix A verbatim at the D-4 position. |
| S2 | `server/Bundles/orchestrator.md`; `.claude/skills/antiphon-orchestrator/SKILL.md` | Insert appendix B sentence and appendix C bullet at the D-4 positions. Keep the bundle's existing sentences; the composition budget test must stay green. |
| S3 | `tests/Antiphon.Tests/Application/PostLandRetrospectiveContractTests.cs` (new) | Four Unit tests from the verification design, using the `RepoFile` root walk from `PostLandMutationContractTests`. |

Code does not create a companion card, run the process, or edit any generated `docs/cards/` file.
Code runs no positive control; Mutation does after land.

## Appendix A: the `docs/orchestration-loop.md` subsection (verbatim)

```markdown
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
```

## Appendix B: the `server/Bundles/orchestrator.md` sentence (verbatim)

Insert immediately after "File a Backlog card the moment Investigate or Review finds a
structural defect; never batch them." and before "A 409 `concurrency_limit` carries `axis`":

```text
A defect a Clean Review approved that is found only in the running system after land gets the post-land retrospective companion (`Post-land retrospective: <identifier>`, label `post-land-retrospective`) with its Investigate task and Low-tier Docs pass from docs/orchestration-loop.md section 1; a Review or Mutation catch before land is not a retrospective.
```

Reflow the paragraph to the file's existing line width, keeping `Post-land retrospective: <identifier>`,
`label `post-land-retrospective``, and `is not a retrospective` each unbroken on one line; the
sentence is ASCII.

## Appendix C: the orchestrator skill bullet (verbatim)

Insert after the "File a card immediately" bullet at `.claude/skills/antiphon-orchestrator/SKILL.md`
line 42-43:

```markdown
- **A defect found live after land that a Clean Review approved gets a post-land retrospective**
  (companion card, Investigate, then a Low-tier instruction-gap pass; `docs/orchestration-loop.md`
  section 1, CARD-0811). A Review or Mutation catch before land is not a retrospective.
```

## Appendix D: brief skeletons for the orchestrator

Task A goal file: companion, landed, fix card GUIDs; Review task and outcome GUIDs; C, L, T; the
observation source verbatim; the landed plan path; the exact investigation file name; the four
headings and six classifications from appendix A; "evidence only, no fix design; next: none".

Task B goal file: the investigation file path at Task A's landed commit; the classification line;
the four verdicts; the 25-line and one-section limits; "stage bundles cap at 2,480 trimmed
characters, so a bundle proposal names the sentence it removes"; "never edit a bundle, a test or
another doc".

## Platform, execution and rollout

Every task is platform Any; omit `-Runner` and `-Platform`; rediscover placement at dispatch.
Code runs the table below through the checkpoint tool once after S1-S3 are committed:

```text
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-29-card-0811-post-land-retrospective-plan.md --after S1-S3 --max-wait 50s
```

then `wait <run-id> --max-wait 50s` until the exit is not 75. Any bootstrap build goes through
`scripts/build-slot.ps1`. Remove `bin-c811` output directories before finishing. Land through
the ordinary caller workflow; a newly commissioned orchestrator receives the bundle sentence, an
existing orchestrator session keeps its earlier composition until relaunch. Rollback is a
reviewed revert. No server restart is required for the docs, but the bundle sentence reaches
delegates only from a server built at or after the landed commit (`GET /api/version`).

## Verification design

Designed on 2026-09-29 in this Plan; nothing was built or run. New class
`tests/Antiphon.Tests/Application/PostLandRetrospectiveContractTests.cs`, `[Category("Unit")]`,
four nonparameterized methods, root located by walking up from `AppContext.BaseDirectory` to
`AGENTS.md` as `PostLandMutationContractTests.RepoFile` does. Assertions are `ShouldContain`
on exact substrings from the appendices, `Case.Insensitive` only where noted.

| ID | Exact test | Required observations |
|---|---|---|
| V-1 | `C811_V1_LoopStatesTriggerGateAndDurableRecord` | `docs/orchestration-loop.md` contains `### Post-land miss retrospective (CARD-0811)`, `stage-outcomes?cardId=`, `supersedesId: null`, `landing.remoteConfirmedAt`, `git merge-base --is-ancestor`, `post-land-retrospective:<review-outcome-guid>`, ``label `post-land-retrospective` ``, `review_evidence_superseded`, `landing.cleanup`, `override pending`, and `is not a retrospective`. |
| V-2 | `C811_V2_LoopStatesRolesClassificationAndDisposition` | The same file contains `-Role Investigate -Worktree -Card <companion-guid>`, `-Role Docs -Kind ClaudeCode -Level Low`, `-post-land-retrospective.md`, all six classification tokens, all four verdict tokens, `Instruction gap:`, ``label `instruction-gap` ``, `NeedsDecision` and `Never apply a Low-tier proposal to a bundle directly`. |
| V-3 | `C811_V3_OrchestratorBundleCarriesTheSibling` | `InstructionBundleComposer.Compose(InstructionBundles.ForDelegate(AgentTaskKind.Orchestrator, AgentTaskRole.Custom)).Text` contains `Post-land retrospective: <identifier>`, ``label `post-land-retrospective` ``, `is not a retrospective`, and still contains `File a Backlog card the moment`. Every character of `InstructionBundles.All["orchestrator"].Text` between the two anchor sentences in appendix B is below U+0080. |
| V-4 | `C811_V4_SkillPointsAtTheOwner` | `.claude/skills/antiphon-orchestrator/SKILL.md` contains `post-land retrospective` (`Case.Insensitive`), `CARD-0811` and `docs/orchestration-loop.md`. |
| R-1 | Existing `PostLandMutationContractTests`, `TaskPlatformGuidanceTests`, `InstructionBundleTests` | The CARD-0478 pins on the loop document and skill, the stage-bundle caps, the orchestrator platform pins and the 30,000-character composition budget stay green after the insertions. |
| R-2 | Existing `InstructionBundleTests.the_worst_case_composition_measured_sits_far_under_the_budget` | The orchestrator composition with `board-api` and a style stays under budget with the new sentence. |
| R-3 | CP-2 grep | The stable key, the refusal code and the phrase `post-land retrospective` are present across the three edited files. |

Each V test names one sentence whose deletion turns it red, which is the stub rule's bar. None
of them is evidence that an orchestrator obeys the process; that is the card's accepted limit
for a prose change, as it was for CARD-0478.

### Positive controls for the later Mutation stage

- PC-1: delete the sentence containing `post-land-retrospective:<review-outcome-guid>` from
  `docs/orchestration-loop.md`. Run exactly
  `/*/*/PostLandRetrospectiveContractTests/C811_V1_LoopStatesTriggerGateAndDurableRecord`;
  expect one failure. Restore, rebuild, expect one pass.
- PC-2: delete the appendix B sentence from `server/Bundles/orchestrator.md`. Run exactly
  `/*/*/PostLandRetrospectiveContractTests/C811_V3_OrchestratorBundleCarriesTheSibling`; expect
  one failure. Restore, rebuild, expect one pass.

Code does not run these; Mutation records its own leased, method-scoped red and restore runs in a
fresh SourceLanding snapshot.

### Cost

Ordinary Code floor: 8 + 1 = **9 minutes**, including one isolated test build. Authoring and
audit estimate: 20-30 minutes; Code `-ExpectAbout`: 35-40 minutes. Mutation: two PC cycles,
about 10 minutes plus its snapshot preflight. Per retrospective at run time: one Investigate task
at the opus default, one Low-tier Docs task, two lands and up to one new Backlog card.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c811/` | instruction-contracts | `/*/*/(PostLandRetrospectiveContractTests*)\|(PostLandMutationContractTests*)\|(TaskPlatformGuidanceTests*)\|(InstructionBundleTests*)/*` | V-1, V-2, V-3, V-4, R-1, R-2 | all listed, 0 failed, 0 skipped (4 + 30 + 9 + 60 results) | 103 | 8 |
| CP-2 | S1-S3 | n/a | docs-named | `git grep -n -e "post-land-retrospective:<review-outcome-guid>" -e "review_evidence_superseded" -e "post-land retrospective" -- docs/orchestration-loop.md server/Bundles/orchestrator.md .claude/skills/antiphon-orchestrator/SKILL.md` | R-3 | >= 5 matching lines across all three files, exit 0 | n/a | 1 |
