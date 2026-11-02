# CARD-1610 S4: where the card is and what to dispatch next

Date: 2026-11-02. Investigate task a45f88c8 (server2, read-only). Base: origin/master
`10c8c2471633c7c0212294afc57348878021e183`. No build or test was run.

## Verdict

S1, S2 and S3 are on master. S4 has never been dispatched: there is no branch, report
or Code task for it. The plan's S4 is one slice in the plan, but it has two parts with
different gates:

- **S4a** is the CLI, the create-time budget binding and the client. It has no external gate.
- **S4b** is the Deploy-script drain slices and the docker-stack docs. It waits on CARD-1615
  under plan D-6.

The next stage is a **short Plan amendment for S4a**. Code is not next, for two reasons:

- The plan's checkpoint table puts S4 rows inside the shared S2-S4 group: CP-10..CP-20, the
  CP-21..CP-105 sweep and one CP-15 build. S2 and S3 have already landed on that group.
- V-13 mixes delegate.ps1 cases with the drain cases that S4b owns.

A Code stage runs the plan table as a closed list, so it needs a closed S4a-only table first.

## (1) Slice table

| Item | State | Evidence |
|---|---|---|
| S1 visibility and janitor safety | Delivered on master | 0f9f7da4f and the C1610 S1 witnesses (81168ada8..698bf1b73); FailedTaskSessionFacts.cs, FailedTaskSessionOwnership.cs; docs/session-runtime-invariants.md:358 ("seat held" attention row) |
| S2 ceiling hold and typed controls | Delivered on master (rebased land) | AgentTaskDispatcher.CeilingHold.cs, AgentTaskReplyService.CeilingHold.cs, CeilingHoldFacts.cs; ReplyToAgentTaskRequest carries CeilingAction/EpisodeId/ExtendMinutes/OperatorExtend/CeilingReason (AgentTaskDtos.cs:676-686); endpoint AgentTaskEndpoints.cs:239-249; migration 20261030114738_AddTaskCeilingSupervision |
| S3 notices, triage and fencing | Delivered on master | LateFailedReportForwarder.cs, TaskSupervisionNotice*; 07627dad5, 1e7f97d2c, e73aa7645, 3213f70a2, 150ede839, 743ea9470 (the land of branch 18255e41; Final Review 7743ab23 bb41288c9 was Clean with inherited reds only) |
| C1518 pin of S2 precedence | On master | 5252fdeda |
| S4a: `delegate.ps1 -Status` "seat held" line (card item 2) | Not started | delegate.ps1 has no `failedSession` or `ceiling` read (grep: only line 590 "cost ceiling"); the server already sends `failedSession` (FailedTaskSessionFactsDto, AgentTaskDtos.cs:411-418) and `ceilingHold` (AgentTaskDtos.cs:491) |
| S4a: declared budget at create (`-CeilingMinutes`/`-CeilingReason`) | Not started; the server half is also missing | Entity columns `RequestedCeilingMinutes`/`RequestedCeilingReason` exist (AgentTask.cs:149-150, migration 20261030114738), but nothing in server/ writes them. CreateAgentTaskRequest has no ceiling field, and CeilingHoldFacts.cs:30-32 reads a value no request path can set. So a requested budget is unreachable today. The plan lists "dispatch/clone/formatter/DTO budget bindings" under S4 (plan line 707) |
| S4a: typed ceiling Reply and operator Extend in delegate.ps1 | Not started | The server accepts it; the CLI does not |
| S4a: client CeilingHoldCard.tsx and TaskDetailBody wiring and CeilingHold.test.tsx | Not started | client/src/features/delegations has no CeilingHold*; agentTasks.ts has `failedSession` (line 427) but no `ceilingHold` type; the attention kind `CeilingHold` exists (attention.ts:220, attentionVisuals.ts:59) |
| S4a: docs/ops-http.md (create budget, typed reply, Status line) | Not started | no `CeilingMinutes` or `operatorExtend` in docs/*.md |
| S4a: TaskCeilingScriptTests (V-13 status, budget and reply cases, 5 of 11) | Not started | no such file under tests/Antiphon.Tests/Scripts |
| S4b: deploy-server2.ps1 delegated drain (default 5, max 8, refuse all, 9-minute budget) | Not started; gated | deploy-server2.ps1:12 still `WaitIdleMinutes = 480` with no token predicate on it. The gate (plan D-6, lines 515-519, 719-721) is CARD-1615 admission resolution plus CARD-1609 retirement. CARD-1609 is Done (close report 52219abc). CARD-1615 is in Review with only Plan/Review rounds (latest Plan c1db9627), no Code, and nothing on master |
| S4b: docker-stack.md steps 4/8 sliced examples (step 5 authority kept verbatim) | Not started; gated | as above |
| S4b: TaskCeilingScriptTests drain cases (V-13, 6 of 11) | Not started; gated | as above |

## (2) Platform and decisions

- **server2/Linux can do all of S4a:**
  - The server DTO binding and service mapping are server-integration TUnit tests.
  - delegate.ps1 offline script tests run under Linux pwsh, with ProcessSpawnLimit and the slow-allowlist entry.
  - The client test uses `scripts/test-client.ps1` with Vitest (jsdom).
  - No headed lane, signed-in Claude, Playwright/Chromium or native-Windows row is needed. server2 has no Chromium (memory), so do not add an E2E row.
- **Native Windows:** none is required. S4a touches no Windows-only path. delegate.ps1 is not a
  daemon/auto-start script, but keeping it ASCII is cheap.
- **S4b** is also offline and Linux-capable, through the script harness with no live rollout. It waits on a
  sequencing decision, not on a platform.
- **Operator/orchestrator decisions:**
  1. Should S4b wait for CARD-1615 Code to land? **Recommended default: yes**, as plan D-6 says.
     Deliver S4a now. Move S4b to a follow-up card linked to CARD-1615, so CARD-1610 can close after S4a.
  2. Accept that card Wanted items 1 and 4 are superseded by the reviewed plan (D-3 and D-5; see section 5).
     **Recommended default: yes.** No new verb, and no automatic Failed-to-Succeeded settlement.

## (3) Next stage to dispatch now

- **Role:** Plan (amendment), High (Opus), `-Runner server2`, Worktree.
- **-StartRef:** `origin/master`, currently `10c8c2471633c7c0212294afc57348878021e183`. The plan file is
  on master. Write the amendment as a **new** file,
  `docs/superpowers/plans/2026-11-02-card-1610-s4a-cli-client-plan.md`. Do not edit the
  round-3 plan, which would risk an add/add conflict (memory: plan-file-add-add-conflict).
- **Inputs:**
  - The round-3 plan, `docs/superpowers/plans/2026-10-29-card-1610-failed-task-live-session-plan.md`: D-3 (lines 260-298), D-4 (299-412), D-6 (486-527), D-8 (555-588), S4 row (707), V-13/V-14 (747-748), CP-13/14/18/20.
  - The S2/S3 schema-fix plan `2026-10-31-card-1610-s2s3-schema-fix-plan.md`, as a format example of a closed repair table.
  - This document.
  - The master sources named in section 1.
- **Scope:**
  - Re-baseline S4a against master. Name the exact files: CreateAgentTaskRequest and the create, retry and clone mapping in AgentTaskService.cs; delegate.ps1 Create, Reply and Status; agentTasks.ts; new CeilingHoldCard.tsx and TaskDetailBody.tsx; docs/ops-http.md.
  - Split V-13 so it covers 5 S4a script cases. The 6 drain cases go to S4b.
  - Add server-integration witnesses for the create binding (valid, missing reason, 0/-1/721, retry keeps an explicit value and re-resolves a default).
  - Produce a closed S4a checkpoint table that imports clean:
    - red rows, then green rows for TaskCeilingScriptTests (S4a cases), CeilingHold.test.tsx (3), and the new create-binding class.
    - full affected classes: TaskCeilingPolicyTests, TaskCeilingHoldTests, TaskCeilingControlTests, AgentTaskReplyIntegrationTests if touched, the DelegateScript* contracts, and guard rows (TestLaneCategoryGuard/TestClassification/ProcessSpawnLimit).
  - It does not need a Full Unit sweep.
- **Rows involved, from the old table:**
  - Replaced by the new table: CP-13, CP-14, CP-18, CP-20.
  - Reused as full-class rows: CP-15/16, CP-8-style guards.
  - Not re-run: CP-21..CP-105 (S2/S3 sweep, already Final-reviewed).
- **Duration:** Plan about 60-90 minutes, docs only, with a `checkpoint import --plan` validation. A later S4a Code
  stage should take about 3-4 hours. Its biggest cost is the AgentTaskReplyIntegrationTests class (341 cases,
  about 25-minute estimate), so include it only if the reply service is touched. S4a should not need to touch it.
- **Risks:**
  - **Conflict with CARD-0824 S1 Code 00abf1bf**, in flight on server2. Its branch (merge base ead93feb0) edits:
    - `server/Application/Dtos/AgentTaskDtos.cs`: the CreateAgentTaskRequest tail, which adds `WorkspaceColdReset` after `FreshWorktree`, exactly where a ceiling field would go.
    - `server/Application/Services/AgentTaskService.cs:1514`: the same create initializer.
    - `scripts/delegate.ps1`: Create params and body.
    - `server/Domain/Entities/AgentTask.cs`, the model snapshot, `docs/ops-http.md` and `docs/docker-stack.md`.

    The Plan stage itself does not conflict. **The S4a Code stage should start after CARD-0824 S1 lands, or the
    land will hit textual conflicts in those files.** S4a needs no migration, because the columns already exist.
    That avoids a snapshot conflict as long as the plan forbids one.
  - CARD-1624 (Review 253f7e9d) and the CARD-1514 landing fence (Review bd1e86ea, group 111) touch none of the
    S4a files (checked by `git diff --stat` against the merge base).
  - The reconciler fixes branch was not identified among open tasks, so its overlap is unknown.
  - A Code stage could treat the whole S2-S4 table as binding and rerun the 100-row sweep. The amendment must say that it supersedes the S4 rows.

## (4) Draft brief (paste-ready)

> **Plan amendment, CARD-1610 S4a (CLI, create budget, client).**
>
> **Goal.** Write `docs/superpowers/plans/2026-11-02-card-1610-s4a-cli-client-plan.md`. It is a Code-ready
> amendment to the reviewed round-3 plan, covering only S4a:
> - the create-time `ceilingMinutes`/`ceilingReason` binding, through CreateAgentTaskRequest to RequestedCeiling*, with retry, clone and brief/status echo;
> - `delegate.ps1 -CeilingMinutes/-CeilingReason` on Create;
> - a typed `-CeilingAction Continue|Extend -EpisodeId [-ExtendMinutes] [-OperatorExtend -CeilingReason]` Reply, using the existing owner-only token helper for operator mode;
> - `delegate.ps1 -Status` lines from `failedSession` ("Failed, session still Running: seat held" or "session state unknown: release unconfirmed") and from `ceilingHold`;
> - client `ceilingHold` typing, CeilingHoldCard.tsx in TaskDetailBody and CeilingHold.test.tsx (3);
> - docs/ops-http.md.
>
> **Out of scope:** S4b (deploy-server2.ps1 drain slices, docker-stack.md steps 4/8, and the six V-13 drain cases). S4b
> waits for CARD-1615 Code under D-6. List it as a follow-up.
>
> **Invariants:**
> - Docs only. No product code, no build, no test run except `checkpoint import --plan` validation.
> - New plan file. Do not edit the round-3 plan.
> - No migration: the columns exist in 20261030114738.
> - The 720 cap, the 1..1000-character reason and operator authority stay as CeilingHoldFacts.cs and AgentTaskEndpoints.cs implement them. Re-read them; do not redesign.
> - No new stop verb. No change to Cancel or Retry.
> - Nothing auto-kills a Working session.
>
> **Closed checkpoints:** one Antiphon.Tests build for red and one for green. Rows:
> - TaskCeilingScriptTests S4a cases (red, then green);
> - the create-binding witness class (red, then green);
> - CeilingHold.test.tsx through test-client.ps1 with a JSON receipt of total=3 (red, then green);
> - full classes TaskCeilingPolicyTests, TaskCeilingHoldTests and TaskCeilingControlTests;
> - the delegate script contract classes the change touches;
> - lane/classification/ProcessSpawnLimit guard rows.
>
> State that this table supersedes round-3 CP-13/14/18/20 for S4a, and that CP-21..CP-105 are not re-run.
> Every row must be Linux-runnable. No E2E, headed or native-Windows row.
>
> **Ordering:** S4a Code dispatches after CARD-0824 S1 lands, because of shared hunks in AgentTaskDtos.cs,
> AgentTaskService.cs and delegate.ps1.
>
> **Stop conditions:**
> - Report blocked if master's S2 typed-reply contract differs from what the round-3 plan assumes, in a way that changes behaviour rather than wording.
> - Report blocked if the create binding needs a schema change.
> - Report blocked if `checkpoint import` refuses the table after two fixes.
>
> **Next:** code (S4a).

## (5) Stale or contradicted premises

- **Card Wanted item 1** asks to allow cancel, or a new verb, on a Failed task with a live session.
  Plan D-3 deliberately rejected that: Retry already stops the old session through RequeueCoreAsync.
  The card's "no sanctioned way" premise is false for Retry.
- **Card Wanted item 3** (a progress-aware ceiling, slot waits excluded) was superseded by D-1/D-6.
  The hold replaced it: the ceiling now holds Working instead of failing, and queue time is not subtracted.
  That part is delivered in S2.
- **Card Wanted item 4** asks to settle the Failed task from a late report. D-5 removed that: S3 forwards a
  triage note, and the outcome stays Failed. It is delivered in that reduced form.
- The plan's S4 row bundles Deploy with the CLI and client. On master only the Deploy part is externally
  gated (CARD-1615 has no Code), so treating S4 as one blocked unit is stale.
- The plan's S4 row implies that only "bindings" remain. In fact the create DTO has **no** ceiling field, so
  the S2 `requested` source path in CeilingHoldFacts.cs:30-32 is dead code until S4a.
- The 52219abc close report lists the missing S4 items correctly. Its task branch
  `feat/card-task-52219abc` (679b696bd) holds an unrelated rc-hop debug report. The close text is in the
  task's stored Result (GET /api/agent-tasks/52219abc-…), not in card history. The card history ends at
  revision 46 (2026-10-31, Review 7743ab23).

## Unknowns

- Whether S2/S3 are active on the product hosts. The close report says master ead93feb0 equalled the
  rc/2026-11-01 tip; GET /api/version was not checked here.
- The branch for the in-flight reconciler fixes was not found among open tasks, so its overlap was not checked.
- The exact V-13 split (5 + 6) is inferred from the V-13 row's own grouping (2 + 2 + 1 + 6). The Plan amendment confirms it.

## Not done, noted

- A fix idea, not designed here: add `ceilingMinutes`/`ceilingReason` to CreateAgentTaskRequest after
  CARD-0824's `WorkspaceColdReset`, and map it in the create initializer next to it.
