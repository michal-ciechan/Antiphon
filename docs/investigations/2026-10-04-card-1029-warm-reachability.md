# CARD-1029: remote warm reachability

Date: 2026-10-04. Task: `0293f857`. Investigation only; no code, test,
configuration, card or running-session changes.

## Verdict

**Confirmed, reconstructed from stored production task/events and source.**
Fresh explicitly remote Create requires Worktree, and Retry preserves it. Neither
is a producer for the unpinned remote Shared warm-pool state in the regression.
However, **remote Shared rows are not exclusively legacy state**: an omitted-runner
live follow-up can create one today. Five retained production rows demonstrate
that producer; all failed before acquiring a session. The current regression
seeds both a Shared task and an eligible pool agent outside a task-worktree
directory. It establishes a retained-state dispatcher regression, not an admitted
Create/Retry-to-warm-recipient journey, nor evidence that its seeded pool state
ever existed in production.

There is also a separate code-admitted route through an existing remote **standing**
Grok/Claude agent. It reaches standing placement rather than the pool-candidate
branch, and cannot qualify the planned remote Codex pool matrix.

## Source and evidence identity

- Assigned branch: `feat/card-task-0293f857`, inspected source/plan HEAD
  `faa86d73833343ef9225c6aa766be19314848aca`.
- Plan: [CARD-1029 verification gaps](../superpowers/plans/2026-10-04-card-1029-inert-observation-verification-gaps-plan.md),
  especially D-3, I0, remote matrix and its 18 checkpoints.
- Read CARD-1029 through `pwsh -NoProfile -File scripts/card.ps1 get CARD-1029
  -Board Antiphon`: card `851daf52-0873-4596-a07f-02869ff5488a`, board
  `8988ca03-7414-47ad-b0b6-51556c701703`, revisions=3. It explicitly owns
  missing verification, not restoration of CLI admission.
- Live `GET /api/version` on October 4 returned
  `version=bb18064ba647e0ddb03cae4da437ab60ed447d98`, informational version
  `1.0.0+bb18064ba647e0ddb03cae4da437ab60ed447d98`, capabilities `land-v2`
  and `operator-shutdown-v1`. The three production mechanism files below are
  byte-identical between that Git object and assigned HEAD.
- Compared the same mechanism files with plan-pinned
  `9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd`. The only difference in those
  files is the placement-shape `RoutingExhausted` argument; the follow-up,
  retention, workspace, reuse and cold-launch guards discussed here agree.
- Evidence is DB-backed task/event DTOs read over the authorized HTTP interface,
  not a direct SQL export. Essential row facts and exact event excerpts are
  embedded below. Filtered retrieval JSON remains ignored at
  `.antiphon/card-1029-row-provenance.json`, SHA-256
  `7fe78231e4e9469326009cca8baf8278c5a3a38fbc25cf79f28b2dcd020a5c71`.
  The committed report does not depend on that temporary mirror file surviving.

## Admission, Retry and warm dispatch

All file:line references in this report bind assigned source HEAD.

| Path | Mechanism and evidence | Reachability classification |
|---|---|---|
| Explicit remote Create + Shared | `AgentTaskService.cs:1200-1224`: only a non-null **request** remote runner enters this guard; Shared throws `ValidationException`, "A runner-bound task must use a Worktree workspace." Pins and follow-ups also refuse. Task construction follows at `:1300`, persistence later. | Refused before task insertion. |
| Fresh automatic remote Create | `DefaultRunnerRoutingPolicy.cs:237-244` excludes existing processes and non-Worktree workspaces; kind defaults also call the exclusion at `:267-274`. Constrained automatic placement uses that exclusion at `AgentTaskService.cs:1697`. | Fresh Shared does not automatically acquire a remote runner. |
| Explicit remote Worktree Create | `AgentTaskService.cs:1328,1362` stores Workspace and selected RunnerId. Real remote fixture calls Create with Worktree and runner at `CodexCliRemoteDeliveryFixture.cs:103-108`, then real Tick/preparer/Tick at `:109-119`. | Ordinary cold producer path. Stored production predecessor below supplies independent Create/dispatch evidence. |
| Retry of that task | `AgentTaskService.cs:2619-2648` reads the existing row, checks independent provider auth and calls Requeue. `:3072-3158` increments Attempt, queues the same task, stops/removes the prior ephemeral delegate and clears AgentSessionId/DispatchedAt, but assigns neither Workspace nor RunnerId. | Worktree remains Worktree; no promotion to Shared or warm pool reuse. |
| Retry of an already retained Shared row | The same Requeue retains Shared and RunnerId; auth/workspace admission can still refuse. It is not a new Create, and Retry does not call Create's explicit-remote shape guard. | Retained-state path only; cannot repair or prove the provenance of the eligible pool agent. |
| Worktree dispatch | `AgentTaskDispatcher.cs:4525-4540` enters TryReuse only when Workspace is Shared. Cold launch calls the remote policy at `:4754-4759`; `PhoneHomeLaunchPolicy.cs:167-172` requires Worktree. | Real Worktree Create/Retry does not enter the warm branch. |
| Shared pool reuse | `AgentTaskDispatcher.cs:6573-6581` declines a pinned pool delegate in a task-worktree directory. The unpinned candidate path also excludes those directories at `:6644-6647`. Matching runner/kind/tier/project/env and reservation are additional conditions at `:6625-6650`. | A Shared row alone is insufficient; its existing process must also qualify. |

The existing remote auth-Retry test is not a real-Create end-to-end witness:
`CodexPhoneHomeCreateTests.cs:278-296` directly seeds a failed Worktree row;
`:219-226` retries it and checks Queued/runner/kind. These are inspected assertions,
not results from a run in this investigation.

## The production Shared producer and observed failure

The missed branch is runner inheritance **after** explicit-remote validation:

1. With `FollowUpOnTask` and an extant prior agent, Create sets `liveFollowUp`,
   `retainBoundRunner` and the agent/prior runner at `AgentTaskService.cs:469-474`.
2. It refuses explicit Worktree for that live process, then rewrites the request
   to Shared (unless explicitly ReadOnly), the same AgentId/kind/tier and existing
   directory at `:526-547`. Workspace resolution occurs at `:630-631`.
3. When the caller omitted RunnerId, the explicit-remote guard at `:1200-1201`
   sees null and does not run. The resolved retained runner is assigned at
   `:1253-1263`, with source `existing-process` and reason `existing_process`.
   Construction stores `Workspace=Shared` and the inherited remote runner.
4. A normal remote Worktree delegate is a pool agent:
   `AgentTaskDispatcher.cs:6104,6114-6117` stores its worktree directory,
   IsPoolDelegate=true and task runner. `DelegationWorktreeService.cs:305,368`
   creates identifier `task-{shortId}`; `WorktreeManager.cs:18-19` supplies
   `card-` directory/branch prefixes.
5. The Shared follow-up enters reuse, but a pool agent in `card-task-` plus eight
   hex digits is excluded (`AgentTaskDispatcher.cs:6575-6581,7522-7535`). If no
   reuse succeeds, cold launch calls the Worktree policy and throws. This
   explains the persisted failure below without a CLI compatibility refusal.

On October 4, scoped `GET /api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703`
returned 3,159 task summaries. After excluding null/empty runner and the
`desktop`/`local` aliases, exactly five had Workspace=Shared. Desktop aliases
are local (`AgentTaskDispatcher.cs:568-572`); counting them as remote would
misclassify historical rows. This is the returned board scope, not a census
of every project, unscoped task or deleted row.

Every row below has Attempt=1, Status=Failed, RunnerSelectionSource=ExistingProcess,
AgentSessionId=null and DispatchedAt=null. Every Created event says
`follow-up on the live agent` and `runner source=existing-process requested=unset`.
Every Failed event says exactly:

> Dispatch failed before a session existed: A runner-bound task must use a Worktree workspace.

| Full task Id | Kind / runner | Created UTC -> Failed UTC | Existing working-directory suffix |
|---|---|---|---|
| `0cae04b9-3853-4f3f-b10d-304df67ff716` | Grok / server2 | 2026-09-26 08:06:01.717649 -> 08:06:04.795733 | `card-task-2cc36aa6` |
| `0d9eaac8-7d00-430c-90ef-14d5a543c013` | Codex / server2 | 2026-09-29 17:11:41.632521 -> 17:11:42.023472 | `card-task-606486b1` |
| `bb0c5a65-9011-4d3e-b865-1ec19fb590b7` | Codex / server2-temp | 2026-09-30 00:25:22.459611 -> 00:25:25.515837 | `card-task-3111804b` |
| `c7359fcf-b9ce-476c-8a21-d91472f4a5bc` | Codex / server2-temp | 2026-09-30 08:50:37.815003 -> 08:51:27.771596 | `card-task-b9d3b16c` |
| `ac06af26-d744-4862-9cc0-464cce9df564` | Codex / server2 | 2026-10-03 23:29:04.481336 -> 23:29:07.833125 | `card-task-b657a1e2` |

Detailed provenance for the last row:

- Predecessor `b657a1e2-767d-4bcb-b25a-1cf2ed925be3` is Worktree/Codex/High,
  runner `server2`, placement Explicit, created `2026-10-03T22:54:56.485079Z`.
  Its event records worktree
  `C:\Antiphon\worktrees\card-task-b657a1e2`, branch
  `feat/card-task-b657a1e2`, from
  `10f6249730dee4f467ae8d15c9d2b270b585493a`.
- It dispatched on session `ff1de0a3-aac3-471b-96a9-234c9a33035d` at
  `22:55:23.513577Z`, succeeded at `23:17:59.881914Z`, and retains AgentId
  `95ea863d-e254-4f9b-8e07-d51965a9ea5e`.
- The Shared continuation has that same AgentId and worktree directory.
  Its Created event ends:
  `follow-up on the live agent [runner source=existing-process requested=unset default=server2 selected=server2 reason=existing_process]`.
- It failed 3.351789 seconds after creation with the exact policy message above.
  The shared agent/directory, Created branch text and failure reconstruct the
  continuation mechanism; they are not a UserPrompt delivery receipt. The task
  detail does not expose the raw FollowUpOfTaskId, so that exact request field
  was not independently queried from SQL.

These rows postdate remote task admission. History places the Worktree Create
guard in `24bd06edff13bef18b09d63a6c60cfb150e18036` (CARD-0604, September 22),
and runner retention in `04c6830e3` (CARD-0710, September 25). They cannot be
described as leftovers from a demonstrated earlier legal remote Shared Create.

## What the seeded regression actually proves

`PhoneHomeTaskDispatchProjectionTests.Signed_out_codex_runner_still_claims_and_reuses_a_warm_session`
was introduced in `d241c2feed6ec810d83ac6ba14df5ba5d6079ab6` on October 4.
Its exact arrangement at assigned source:

- `SeedAsync` inserts a remote Queued Worktree row directly (`:512-521`).
- The test changes it to Shared and clears worktree coordinates (`:55-66`),
  then inserts a Running remote session and Idle remote pool agent directly
  (`:67-84`). The directory is a random `antiphon-remote-dispatch-ws*`
  temp directory (`:609-612`), not a `card-task-*` worktree.
- That satisfies the otherwise unavailable pool-candidate state. Assertions
  require Dispatched, same agent/session/runner, reuse event, queued message
  and zero launch specs (`:93-106`). There is no Create/Retry producer here,
  and no complete recipient UserPrompt assertion in this method.

The [CARD-0959 repair evidence](2026-10-04-card-0959-path-and-dispatch-repair.md)
records the regression and inherited red/green checkpoint history. This
investigation does not relabel those receipts or rerun them. A genuine historical
producer for this precise **unpinned non-worktree remote pool** state was not
found. "Retained/seeded-state regression" is justified; "legacy legal admission"
is not established. Settlement also distinguishes Shared pooling from non-Shared
retirement (`AgentTaskDispatcher.cs:7157-7168`).

For completeness, a standing-agent pin with omitted RunnerId takes another
branch: `AgentTaskService.cs:801-817` retains the standing runner, `:824-858`
defaults workspace to Shared and reuses its directory, and `:1253-1263`
restores that runner. `AgentTaskDispatcher.cs:6565-6568` routes a non-pool
agent to `PlaceOnStandingAgentAsync`, which can adopt a live session
(`:6807,6884-6925`). Named remote kinds are Grok/Claude, not Codex
(`PhoneHomeLaunchPolicy.cs:93-106,191-192`). This is source reachability,
without a positive standing-recipient transcript from this census.

## Effect on the existing checkpoint and control obligations

This is a qualification finding, not an edited verification design. No row or
control is discharged, removed, executed or newly added by this investigation.
All 192 original active IDs and the separate PC-256 remain obligations.

| Existing checkpoint(s) | Consequence of the evidence |
|---|---|
| CP-11..14 (V-23..26, S4) | Their remote Retry column is a real Worktree producer path. Their Codex warm and recreated-warm columns qualify seeded retained state, not fresh remote admission. A real Worktree follow-up produces the observed failure instead of a positive pool receipt; standing Grok/Claude adoption is a different branch. |
| CP-16 (V-27/R-6/PC-256) | Preserve the exact seeded auth regression's coverage claim: pre-claim/claim/reuse/queue and zero launch. It proves neither remote Create admission nor complete recipient receipt nor historical production origin of its eligible pool agent. |
| CP-4 (existing V-23..26 consumers) | No source/count change. Existing local warm coverage cannot be promoted to remote pool producer coverage; remote fixture is cold Worktree delivery. |
| CP-6, CP-9, CP-10 (Retry and fault cuts) | The reachable remote Create/Retry state is Worktree. A Shared conversion or continuation must not silently stand in for that state. Their count/filter obligations otherwise remain unchanged. |
| CP-1..3, CP-5, CP-7..8, CP-15, CP-17..18 | No changed mechanism or checkpoint disposition from I0. In particular neither native obligation nor durable cold-queue recovery is discharged by the warm regression. |

The proposed 114 matrix vectors partition as follows; these are vector counts,
not executed TUnit results:

| Checkpoint | Total proposed | Worktree Retry | Seeded warm | Seeded recreated-warm |
|---|---:|---:|---:|---:|
| CP-11 | 18 | 6 | 6 | 6 |
| CP-12 | 12 | 4 | 4 | 4 |
| CP-13 | 36 | 12 | 12 | 12 |
| CP-14 | 48 | 16 | 16 | 16 |
| Total | 114 | 38 | 38 | 38 |

Manual control qualification implications:

- **PC-225/228/231/234 (Create):** remote obligations bind admitted Worktree
  requests; warm arrangement is not their Create witness.
- **PC-226/229/232/235 (Retry):** bind the same real failed Worktree task and
  retained runner/workspace. The seeded warm regression cannot detect these
  Retry guards.
- **PC-227/230/233/236 (dispatch), PC-240 (zero warm probes):** separate
  cold producer-to-recipient evidence from seeded remote pool/recreated-pool
  evidence. The original PC-240 mutation calls `_runners.Local`; qualification
  must identify which selected client/counter detects a remote-only mutation
  rather than infer it from a local assertion.
- **PC-253 (final launch):** remote final launch is the cold Worktree branch;
  successful warm reuse has zero launches and cannot witness that guard.
- **PC-256:** retains its exact historical signed-out/claim mutation obligation
  and seeded-state limitation. It is the additional ID, not part of the 192.
- **All other IDs:** no reachability-based retirement or retargeting is proved
  here. Delivery/recovery controls PC-191/192/218/219/220/246 still require their
  independent producer/queue/generation/receipt evidence. Archived admission
  PC-204 is not revived by this finding.

## Remaining uncertainty and limits

- The precise eligible unpinned remote Codex pool state has no demonstrated
  production producer or historical DB lineage. Resolving that requires a
  retained task+agent+session history showing a non-worktree remote pool agent
  and the producer/version that created it, or a real admitted request sequence
  reaching it without fixture-only workspace mutation.
- Five failure rows establish the continuation producer and matching failure
  mechanism, but do not expose per-dispatch branch traces or an agent's historic
  IsPoolDelegate flag. The pool/path exclusion is reconstructed from the
  predecessor's production construction and recorded directory; current agent
  metadata cannot replace historical state.
- The board census is time- and scope-bounded. It proves no successful remote
  Shared journey among those five returned rows, not universal absence.
- Standing-agent reuse is source-reachable but has no positive recipient receipt
  here. It cannot resolve remote Codex coverage because named Codex is excluded.
- Retry workspace preservation is source reconstruction and inspected existing
  seeded-test arrangement; no new Retry, test, build or mutation was performed.
  Future ordinary/Mutation qualification must measure actual task/session/queue
  identity and the selected recipient's complete current UserPrompt.

## Not done, noted

The live-follow-up Shared admission followed by cold Worktree refusal is a separate producer/dispatcher defect for caller triage; no repair was designed or implemented under CARD-1029 verification.

The confirmed-mechanism Investigate stage routes to Plan. That handoff reconciles
the existing D-3/I0 premise before the requested TestDesign control qualification;
it does not commission a production repair.

--- next stage ---
next: plan
handoff: Reconcile D-3/I0 for TestDesign control qualification: remote Create/Retry stays Worktree; the seeded Codex warm pool has no proven producer; real omitted-runner follow-ups persist Shared but fail the pool/worktree guards. Separate retained-state and producer-to-recipient claims for CP-11..14/CP-16 and all 192 controls plus PC-256; triage the continuation defect separately.
artifact: docs/investigations/2026-10-04-card-1029-warm-reachability.md
