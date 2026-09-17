# CARD-0552 investigation: Mutation as a tracked pipeline stage

Date: 2026-09-17. Task 0d3ff398 (Investigate, Shared). Evidence only; no fix designed here.

## Outcome in one line

Mutation is a fully wired stage role with server-side SourceLanding admission, a stage bundle,
a WIP slot, a routing pin and a parsed `next: mutation` token, but the two links that would make
Mutation debt visible were never exercised: no companion card has ever been created (0 of 546
cards on the board), and nothing after a land emits a `next=mutation` handoff, so the pipeline
glance's Mutation stage stays empty and hidden. The real backlog is 12 landed cards owing about
914 positive controls (442 of them from tonight's six), not six cards.

## 1. How the auto-tracked stages work today (CARD-0146)

Confirmed from `docs/orchestration-loop.md` §1 (lines 118-160) and the code it names:

- A stage is any `AgentTaskRole` in `AgentTaskRoles.IsStage`
  (`server/Domain/Enums/AgentTaskEnums.cs:387-393`): Investigate, Plan, TestDesign, Code, Review,
  Mutation. Each has a bundle `server/Bundles/stage-<role>.md`; `stage-mutation.md` exists.
- Settlement parses the report's `--- next stage ---` block with `PipelineHandoff.TryParse`
  (`server/Application/Services/PipelineHandoff.cs:132-187`) onto `AgentTask.NextStage` /
  `NextHandoff`, and the completion header carries `next=<token>`
  (`DelegationReportFormatter.cs:649-650`). `mutation` is a recognised alias mapping to
  `PipelineHandoffKind.Mutation` and `TryToStageRole` maps it to `AgentTaskRole.Mutation`
  (`PipelineHandoff.cs:33`, `:86-88`).
- The orchestrator dispatches from the header's `next=` and never re-reads the body
  (`orchestration-loop.md:123-125`, `:623-625`, `:1205-1209`).
- Card auto-move is a 60 s sweep, `CardWorkTransitionService.ScanAsync`
  (`server/Application/Services/CardWorkTransitionService.cs:66-160`): candidates are Backlog /
  InProgress / Review cards with at least one bound non-specialist task; `Decide()` (lines
  173-206) moves Backlog -> InProgress on the newest open dispatch and InProgress -> Review when
  the newest settle is Succeeded. Done cards are never candidates. There is no role-specific rule;
  a Mutation task bound to a Backlog companion card would move it exactly like any other task.
- The stage glance (`/api/agent-tasks/pipeline`, `AgentTaskPipelineStatusService.BuildReady`,
  lines 209-283) builds ready rows only from a Succeeded stage-role task whose `NextStage` maps
  to a stage role; it skips cards that are Done / Canceled / NeedsDecision (line 224). Review's
  handoff after a clean review is `next: land`, which produces no ready row (`PipelineHandoff`
  `TryToStageRole` default branch), and a land is an `AgentTaskLanding` row plus a notification,
  not a stage report. The client hides stages with no rows
  (`client/src/features/orchestrator/pipelineStageModel.ts:24-27`).

Live readback of the glance at 2026-09-17 20:5xZ: Mutation stage `inFlight 0, queued 0, ready 1`.
The single ready row is CARD-0495 (status Review), sourced from a Code task's legacy
`next: mutation`, i.e. the pre-CARD-0478 contract. None of the 12 landed cards below appears.

## 2. Mutation's actual dispatch mechanism today

- `delegate.ps1 -SourceLanding <guid>` sends `sourceLandingOperationId`
  (`scripts/delegate.ps1:72`, `:800`). Nothing else in the script is Mutation-specific.
- `AgentTaskService` refuses the request unless it is Worker / Mutation / Worktree with no pin,
  follow-up or merge target (`AgentTaskService.cs:229-235`, code `verification_source_mode`), and
  refuses Mutation with ReadOnly workspace (`:238-240`).
- At create, inside a transaction (`AgentTaskService.cs:1061-1076`), `SourceLandingAdmission`
  (`server/Application/Services/SourceLandingAdmission.cs`) requires: an `AgentTaskLanding` row
  with `HasPublication` (line 31); a source task in the same project whose `CardId` differs from
  the Mutation task's card (line 34-37, code `verification_source_identity_mismatch`); both cards
  on the same board (39-42); the same git common directory (43-48); runner custody support
  `windows-job-v1` (55-61); and one open task per operation via an advisory lock (64-73, code
  `verification_source_already_open`). It stamps `SourceLandingSha = VerifiedSourceSha`.
- The distinct-companion requirement is therefore enforced by code, but nothing checks the
  companion's title, label or stable key; `post-land-verification` appears in no `.cs` file
  (grep over `server/`).
- After admission the task runs with custody fields on `AgentTask`
  (`server/Domain/Entities/AgentTask.cs:175-186`: `SourceLandingOperationId`, `SourceLandingSha`,
  `VerificationCreationJson`, cleanup seal/residue/removal flags) and `VerificationExecution`
  rows. Landing a Mutation task is refused (`AgentTaskLandService.cs:78-79`), commit-on-settle is
  disabled for it (`CommitOnSettleEligibility.cs:12`), and cleanup is the separate
  `-CleanupVerification` action.
- The land outcome the caller receives names the operation: `FormatOutcome`
  (`AgentTaskLandService.cs:370-375`) renders `operation=<O> ... verified=<L>` and it is
  delivered as the `Outcome` land notification (`:744`). So the orchestrator holds O and L at
  close time; there is no place it must store them other than the card.
- Routing: a stage-wide Required pin for Mutation exists (ClaudeCode / High / opus, set
  2026-09-16 "Codex out of credits"), and `RecommendedInFlight` for Mutation is 1
  (glance readback). CARD-0146 D7 gives Mutation its own WIP of 1 (`orchestration-loop.md:307-309`).

What actually happened when it was used. Across all 1,964 agent tasks on the server there are 4
Mutation-role tasks; 3 belong to another board (bd4b30fb, Shared workspace, no SourceLanding). The
only Antiphon one, 9a5d9aa6 (CARD-0499, 2026-09-14, Codex terra), was created without
`sourceLandingOperationId` (task detail: `sourceLandingOperationId: null`); the delegate stopped
itself before PC-1 and returned `next: decide` ("this task lacks the required confirmed
SourceLanding commissioning record"). Zero SourceLanding Mutations have ever been admitted or
completed on this board. All earlier PC batteries ran as `Custom`-role Worktree tasks on the Code
branch before land (10 tasks 2026-09-12..13, $68.30, 505 min in total; e.g. 37 PCs in 118 min
for $15.96, 32 PCs in 88 min for $19.95, all opus tier).

## 3. Where the PC inventory is recorded

Purely prose. There is no structured field anywhere:

- `Card` (`server/Domain/Entities/Card.cs`) has `LabelsJson`, `TerminalReason`, verification
  policy enums (CARD-0544) and nothing about controls.
- `AgentTask` has the custody columns above and `NextStage` / `NextHandoff`; no PC count,
  inventory or result. `AgentTaskLanding` records SHAs, push and cleanup facts; no link to a
  Mutation task. `VerificationExecution` records custody generations, not PC outcomes.
- Grep of `server/` for `PositiveControl`, `PcCount`, `PcInventory`, `MutationStatus`,
  `MutationPending`, `PostLandVerification`: only the role's doc comment
  (`AgentTaskEnums.cs:77`) and an unrelated worktree-lock status string.
- The inventories live in plan docs as `| PC-n |` table rows under
  `docs/superpowers/plans/` and in close-reason sentences such as "50 PCs pending post-land
  Mutation". The stage-code contract even says so: "PCs: every PC stays pending for method-scoped
  SourceLanding Mutation" (`DelegationReportFormatter.cs:281`).
- The CARD-0478 plan (`docs/superpowers/plans/2026-09-10-card-0478-mutation-after-land-plan.md`,
  lines 68-76) designed the durable record as a companion card: title
  `Post-land verification: <original identifier>`, label `post-land-verification`, stable key
  `post-land-verification:<code-task-guid>` in the description, pending PC inventory in prose.
  It explicitly rejected a memory-only promise, an attention-only item ("not a work queue"), a
  new verification table or automatic dispatch engine, and a new board status. Guards G-124,
  G-126, G-133 (`PC-124/126/133`) pin "no automatic Done", "no implicit clean" and "recording the
  obligation authorises no tick dispatch".

## 4. Scale of the landed-but-unmutated backlog

Method: `GET /api/cards?boardId=8988ca03...&status=Done` (392 cards), then `GET /api/cards/{id}`
for each because the list DTO truncates `terminalReason`; regex over the full close reason for
pending / deferred PC language; cross-checked against `| PC-n |` rows in the plan doc and against
every task bound to the card after its close. Cards in Backlog 138, InProgress 2, Review 14,
NeedsDecision 0, Canceled 0: none carries the `post-land-verification` label or title.

Landed since the CARD-0478 order took effect (2026-09-11) and still owing PCs:

| Card | Closed | PCs owed | Close-reason evidence | Plan-doc rows |
|---|---|---|---|---|
| CARD-0503 | 09-12 | 4 | "PC-1..PC-4 mutation verification ... deferred" | (battery d14b07d1 covered the earlier PCs) |
| CARD-0461 | 09-14 | 117 | "117 PCs still pending, not run tonight" | 117 |
| CARD-0514 | 09-14 | 96 | "96 Mutation PCs ... never run ... no confirmed SourceLanding operation" | 96 |
| CARD-0443 | 09-15 | 218 | "All 218 Mutation PCs remain unexecuted" | 218 distinct ids |
| CARD-0501 | 09-15 | 9 | "R3-PC-1/2/3 plus the full existing inventory ... pending" | 9 distinct ids |
| CARD-0481 | 09-16 | 28 | "PC-1 through PC-28 (Mutation) remain unexecuted" | 28 distinct ids |
| CARD-0549 | 09-17 | 86 | close reason silent; `docs/investigations/2026-09-16-card-0527-f17-f18-repair.md:55-66` says PC-1..80 plus PC-81..86 pending | n/a |
| CARD-0462 | 09-17 | 119 | "All PC-28..146 (119 controls) remain pending" | 119 |
| CARD-0544 | 09-17 | 107 | "107 CARD-0544 controls ... pending; 13 controls deferred to CARD-0545" | 128 (13 moved to 0545) |
| CARD-0540 | 09-17 | 28 | "PC-1..28 pending post-land Mutation" | 28 G rows |
| CARD-0545 | 09-17 | 52 | "52 PCs pending post-land Mutation" | 52 |
| CARD-0547 | 09-17 | 50 | "50 PCs pending post-land Mutation" | 56 |

Total: 12 cards, 914 PCs. Tonight's six (0549, 0462, 0544, 0540, 0545, 0547): 442.

Two more cards are ambiguous and excluded from the total: CARD-0502 (32 PCs; a Custom battery
78a9feca ran them on the Code branch at C in the same minute the card closed) and CARD-0499
(37 PCs; Custom battery d98e015b at C on 09-13, then the failed SourceLanding attempt above).
Both have evidence at C, none at L, which `orchestration-loop.md:1118-1119` says does not count.
Including them: 14 cards, 983 PCs.

Two of the twelve cannot be SourceLanding-mutated as landed: CARD-0514 and CARD-0499 were pushed
by manual rebase+merge, so no `AgentTaskLanding` row with publication exists and admission
(`SourceLandingAdmission.cs:29-32`) will refuse them.

At the observed ad-hoc rate (about 3 min and $0.50 per PC at opus tier, 88-121 min per 32-37
PC battery), 914 PCs is roughly 45 hours and $450 of Mutation WIP=1 work. This is why the stage
is throttled by design; the design question is visibility, not urgency.

## 5. What exists versus what is missing

Exists and works:

- Stage role, bundle, WIP slot, routing pin, `next: mutation` parsing, Mutation ready-row
  projection when a task's `NextStage` is Mutation (§1).
- SourceLanding admission, custody, no-land / no-commit fences, cleanup (§2).
- Land outcome that names O and L to the caller (§2).
- Card auto-move for any card-bound task, which would move a companion card
  Backlog -> InProgress -> Review with no new code (§1).
- Board label filter in the client (`client/src/features/board/boardShapeModel.ts:100`) and
  `card.ps1 -Labels` (replace semantics, `scripts/card.ps1:585`, `:620`).
- The attention feed's read-time projection pattern (40+ kinds in
  `server/Application/Dtos/AttentionDtos.cs`, e.g. `CardStalled` built in
  `AttentionService.cs:646-730`, `LandOutcomeUnconfirmed`, `CommitRecoveryPending`).

Missing, with evidence:

1. The companion card has never been created: 0 cards with the label or title across all 546
   cards. Every one of the 12 close reasons says "N PCs pending" instead of the required
   "post-land Mutation pending: <companion>" (`orchestration-loop.md:1155-1156`). The step is a
   caller-side prose obligation with no server assist and no check.
2. No `next=mutation` source after a land: Review hands off `land`; `-Land` settles through
   `SettleLandedAsync` (`AgentTaskLandService.cs:414-431`) with an event and notification, never a
   stage handoff, and `BuildReady` excludes Done cards. So the glance cannot show the debt even
   though the Mutation column exists.
3. No structured PC count anywhere (§3); the number is only recoverable by regex over the close
   reason or by counting plan-doc rows, and CARD-0549 shows the close reason can be silent.
4. No `AgentTaskLanding -> Mutation task` link, so "which operations have a completed sourced
   Mutation" is a join on `AgentTask.SourceLandingOperationId` that nothing projects today.
5. Legacy-landed cards (manual merge) have no operation to source from.
6. The `post-land-verification` exclusion from fresh picking is prose only.
7. The cards API has no label filter (`server/Api/Endpoints/CardEndpoints.cs:44-56`), so a
   label-based queue is visible on the board UI but not queryable by an orchestrator.

## 6. Options observed against the evidence (not a design)

For Plan to weigh, ordered smallest first:

- Companion card as designed by CARD-0478, but created by the land outcome rather than by
  orchestrator memory. Debt = Backlog cards labelled `post-land-verification`; existing auto-move
  gives InProgress / Review for free; close reason "Mutation complete: N/N" is the terminal marker.
  Gap left: no count without a structured field; no API label filter.
- Pipeline ready row for Mutation derived from a confirmed publication with no Succeeded sourced
  Mutation task (lift the Done-card exclusion for that one source). Gives the orchestrator the
  same "dispatch from `ready`" surface as every other stage, no schema change, keeps dispatch
  explicit (G-133 preserved).
- Attention projection "MutationPending" per landed card: cheapest visibility, but CARD-0478
  rejected attention-only as "not a work queue"; workable only alongside one of the above.
- A structured pending-control count (on the landing row or companion card) needs a parser of
  close reasons or plan docs; CARD-0478 excluded a full PC-result parser.
- A new CardStatus / column: rejected by CARD-0478 D-3, touches `CardStatus`, every board's
  columns, transitions and the client; the largest option and the one the plan already ruled out.

Trigger policy evidence: cost above (about 3 min/PC), WIP=1, and the 0478 guard that ticks create
no tasks. Nightly exists as a Windmill job (`docs/nightly-watchdog.md`), but PCs must run on local
inherited SourceLanding children only (`stage-mutation.md`, `orchestration-loop.md:1147-1150`).

Findings convention: `stage-mutation.md` and `orchestration-loop.md:1164-1173` already define
`next: decide` triage with four outcome classes and linked remediation cards; no new convention
is needed for a PC that fails to go red, only enforcement of the companion where the finding lives.

## Remaining uncertainties

- Whether batteries run at C (CARD-0502, CARD-0499, and the pre-0478 Custom tasks) should be
  treated as discharged; the doc says no, practice says yes.
- CARD-0501's count (9 from plan ids) and CARD-0549's (86 from the repair doc) are not stated in
  their close reasons.
- The 14 Review-status and 2 InProgress cards were not counted; they will add to the backlog as
  they land.
- Whether the land-outcome note text is retained verbatim in a queryable place beyond the
  `AgentTaskEvent` detail was not traced.

## Not done, noted

Fix idea: have `-Land`'s confirmed publication create/refresh the CARD-0478 companion card and
project a Mutation ready row from "publication with no sourced Mutation", so the existing
`next=` dispatch loop covers Mutation with no new column.
