# CARD-0272 — Per-stage "found something" hit rate against cost

**Date:** 2026-09-02 · **Card:** CARD-0272 · **Task:** fb44052a (Plan, Frontier, Shared) ·
**Status:** plan only, nothing built (the card's own instruction).
**Relates:** CARD-0258 (land operation, shipped), CARD-0247 (trust the report, ask the same
delegate), CARD-0304 (pipeline projection keyed by role), CARD-0305 (routing pins keyed by
role), CARD-0286 (`CompletedWithoutProgress` from git facts), CARD-0159 (report evidence classes).
**Sources verified:** master `83bbdf7b`; `AgentTaskLandService`, `DelegationWorktreeService`
(`FinalizeLandAsync`), `AgentTaskReplyService.SettleAsync`, `DelegationReportFormatter`
(`ReportingContract`, `TryReadReportVerdict`), `AgentTaskEnums`, `AgentIncidentKind`,
`RoutingPin`, `AgentTaskPipelineDtos`, the live `AgentTaskEvents` / `AgentTasks` tables
(2026-08-30 .. 2026-09-02 17:16 UTC), `docs/orchestration-loop.md` at `8b2618d9`.

## Verdict up front

| Question the card asked | Answer |
|---|---|
| Has CARD-0258's land operation shipped? | **Yes, all four slices, Done 2026-08-31 07:56.** `POST /api/agent-tasks/{id}/land`, `delegate.ps1 -Land [-Verify]`, events `LandRequested`/`Landed`/`LandRefused`/`Conflicted`. 47 land runs recorded since the first one at 2026-09-01 12:57. Design around it as current, not recent. |
| What counts as a stage? | **The question a step answers, independent of who answers it.** Fixed enum `OrchestrationStage { Rebase, Verify, Cleanup, Review, FollowUp, Deploy }`. Three are the land operation's own steps (server-run, zero tokens); all six can also be run by a delegate. "Merge" is not a stage: a Merge task is the *cost of a Rebase finding*. The repo already uses `AgentTaskRole` as the word "stage" for routing pins and the pipeline projection, so delegate-run stages default from the role (Review→Review, Test→Verify, Merge→Rebase, Deploy→Deploy) and are overridable with `-Stage`. |
| How is "found something" recorded? | **Explicit, by the actor that ran the stage — which for the land operation is the server, so that arm is automatic and exact.** Delegate-run stages self-report one closing line (`[antiphon-finding:<id> found\|clean] <what>`); a missing line is an honest `Unreported` bucket, never a guess. The orchestrator has one override verb for the case where it judged differently from the delegate (`delegate.ps1 -Finding`). The card's lean toward explicit self-report is still right; the "infer from orchestrator commits" option is rejected for delegate stages (attribution is fuzzy, and post-CARD-0258 the orchestrator makes no such commits) and is not what the server arm does (it measures, it does not infer). |
| Where does it live? | **A new small table `StageOutcomes`** (structured: stage, outcome, source, cost, duration, refs). Not `AgentTaskEvents` (prose `Detail`; the repo added `FailureCode` precisely to stop parsing prose) and not `AgentIncidentKind` (per-agent, pruned at 30 days / 500 per agent, and a clean run is not an incident). One nullable `Stage` column on `AgentTasks` carries the dispatch-time declaration. |
| Reporting surface? | `GET /api/stage-outcomes` + `scripts/stage-value-report.ps1` (hit rate, cost, cost per finding, per stage, per window, per card). No UI in v1. |
| Non-git stages in scope? | **FollowUp yes** (a task created with `-OnAgent`, including CARD-0258 S2's inherited-context arm), **Deploy yes** (Deploy-role delegates in v1; the local deploy script in a later slice), **Refine no** (steering a running task is a brief amendment, not a stage run). |

**What the numbers already say (§2):** the mechanical verify the card was worried about no longer
costs the orchestrator tokens — it is the land operation's conditional build (13 real runs, 0
findings, 23 skipped because the base had not moved). The rebase step found 1 conflict in 47
(CARD-0299, resolved by a $2.51 Merge delegate). The stages that cost tokens *and* found things
today were Deploy (3 of 3 runs, ~$1.5 each) and Review (1 of 2 today). The step that failed most
was Cleanup: 9 of 47 lands ended "landed and pushed, but could not delete the branch", reported
as `LandRefused`, leaving 18 stale worktrees on disk right now. That is a defect, not a finding,
and the taxonomy must keep the two apart (§3.3).

## 1. Ground truth

### 1.1 The current stage taxonomy, from the owner doc and the code

`docs/orchestration-loop.md` §1 (rewritten by CARD-0258 S4): pick → Plan delegate → land the plan
→ Code delegate in a worktree → **trust the report** (§0 ladder: trust; ask the same delegate;
delegate the investigation) → `delegate.ps1 -Land <id>` → deploy (`scripts/deploy-local.ps1`,
one `DEPLOY VERDICT` line) → close the card. §5: after `-Land` the orchestrator's git involvement
is zero — no `git show`, no re-run of tests. So the "blanket mechanical rebuild + re-test after
every merge" the card describes (2026-08-30) has been designed out of the orchestrator and into
the server. What remains of "verify" is:

- **Server-run, inside the land operation** (`AgentTaskLandService.RunAsync`): hold behind Shared
  writers → `PrepareLandAsync` (fetch, rebase in the worktree; conflict ⇒ Blocked + auto-spawned
  Merge task) → `VerifyAsync` **only if the base moved** (`dotnet build --property:OutputPath=bin-land/`
  plus the optional `-Verify` test filter) → `FinalizeLandAsync` (ff, push, `RemoveQuietlyAsync`
  worktree, `git branch -d`). Three distinct steps, three distinct outcomes, one prose line.
- **Delegate-run**: Review-role dispatches ("Review CARD-0037's implementation, branch …"),
  Test/Debug/Custom dispatches whose title says verify or check ("CARD-0241 verify diagnosis still
  accurate", "Quick check, report back concisely"), Deploy-role dispatches ("Run a real deploy
  through the pipeline"), Merge-role dispatches (auto-spawned on conflict, and hand-dispatched
  whole landings for gym-stat because the server op's build step refuses that repo — §2.4).
- **Orchestrator-run**: reading reports and outcome lines; deciding. No git.

The word **stage already means `AgentTaskRole`** in two shipped places: `RoutingPin` ("a
stage-wide pin for a role", CARD-0305) and `AgentTaskPipelineStageDto(AgentTaskRole Role, …)`
(CARD-0304). A second, parallel "stage" vocabulary would be a mistake; §3.1 keeps the new enum
small and maps roles onto it.

The old `CardWorkflowRun` / `CardWorkflowStage` / `BoardWorkflowDefinition` tables are the YAML
tracker-workflow pipeline (stages named `analyze-codebase`, `finalize-documentation`). One run
exists in the database, ever. Not the stage concept this card means; do not extend it.

### 1.2 What is measurable today, exactly

- **Cost per delegate task is exact and already on the row**: `AgentTasks.TokensIn`,
  `CacheReadTokens`, `CacheCreationTokens`, `TokensOut`, `CostUsd` (`SettleAsync` →
  `DelegationUsageRollup.ForSessionAsync` bounded to the task's window, priced by kind and tier).
  A delegate-run stage's token cost needs no new accounting.
- **Server-run steps cost zero tokens.** Their cost is wall-clock (the build) and the queue: a
  land request → outcome delta ranges 6 s (skipped build) to 1 679 s (task 9e487ef7, a moved
  base plus queueing behind the previous land). S1 measures each step with a stopwatch inside the
  step, never from event timestamps.
- **The orchestrator's own tokens per stage are not attributable today.** Transcript entries
  carry `InputTokens`/`OutputTokens`/cache counters per API call, and every machine-origin
  prompt into the orchestrator (`[task … done]` notes, land outcome lines) is a
  `SessionQueuedMessages` row with `SourceTaskId` and `SentAt`. That is enough to attribute the
  orchestrator's *reaction turn* to a stage later (§6, S5). Not v1.
- **Report-marker compliance since 08-30** (`ReportEvidence = Marked`): Claude 56/63, Codex
  25/29, Grok 137/150 — about 90 % across kinds. A second closing marker will land at roughly
  that rate, which is why §3.2 has an `Unreported` bucket instead of a guess.
- **Follow-ups are not structurally linked.** `CreateAgentTaskRequest.FollowUpOnTask` pins the new
  task to the prior task's agent (or, since CARD-0258 S2, prefixes an inherited context packet),
  but `AgentTasks` has no column recording which task it followed up. The card's "asking the same
  delegate" stage therefore has no denominator today. S2 adds the column.

## 2. Seed evidence

### 2.1 The card's own night (2026-08-30, manual pipeline, orchestrator-run)

Six merges, each with a manual rebuild + re-test: CARD-0261 (verify found a real line-wrap
regression in the delegate's new test), CARD-0250 (a real `AgentIncidentKind = 40` collision at
rebase), four clean. 2 of 6. Pre-land-op; not backfillable — there are no events for manual
verifies. Recorded here as the baseline the card was written against.

### 2.2 Forty-seven server-run lands, 2026-09-01 12:57 → 2026-09-02 17:06 (all events)

| Step | Runs | Found | Clean | Skipped | Failed | Unreported | Notes |
|---|---|---|---|---|---|---|---|
| Rebase | 47 | **1** | 46 | — | 0 | — | Found: CARD-0299 (`843c1cd9`), 2 files incl. `AgentTaskDispatcher.cs` ctor list; Merge task `29e40dca` $2.51, 16 min. |
| Verify | 46 | **0** | 13 | 23 | 1 | 9 | The conflicted land never reached this step. Skipped = base unchanged, no build. Failed = gym-stat `87e7e1ae`, `MSB1003` no solution at repo root (misconfiguration, not a regression). Unreported = the 9 cleanup-failed refusals whose prose hides the verify result. `-Verify <filter>` was passed twice and both were skipped: **the named-test path has never run in production.** |
| Cleanup | 45 | 0 | 36 | — | **9** | — | The conflicted and the verify-failed lands never reached this step. "Landed and pushed, but could not delete `feat/card-task-…`": 6× branch still used by a worktree that `RemoveQuietlyAsync` could not remove, 3× `git branch -d` refusing an unmerged upstream ref. 18 `feat/card-task-*` branches and 18 worktrees on disk at plan time. Reported as `LandRefused`, which the doc reads as "not landed". |

Hit rate of the mechanical verify at land: 0/13 real runs, at zero token cost. Rebase: 1/47
(2.1 %), finding cost $2.51. The card's motivating question is answered for this policy: **keep
it — it is free of tokens now, it is the step that would have caught the 08-30 line-wrap class,
and it runs only when a replay actually happened.**

### 2.3 Delegate-run stages, same window (cost from the task rows)

| Stage (actor) | Runs | Found | Cost | What was found |
|---|---|---|---|---|
| Deploy (Deploy-role, gym-stat) | 3 | **3** | $2.44, $1.02, $1.19 | build `PATH`/CRLF failure → fix task `f0d476e7` $3.04; dirty canonical worktree (`docs/cards/` untracked) → Docs cleanup `5b0d29f6` $0.59; remote proof failed → fix task `6e218a35` $5.40. All three verdict lines were real blockers. |
| Review (Review-role) | 2 today, 11 since 08-30 ($51.96) | 1 today | $7.48, $6.91 | `9fc2825b`: "no blocking defects" plus one non-blocking hole → orchestrator dispatched the fix `84540d90` $2.51 (**the delegate would have said `clean`; the orchestrator acted — this is the override case**). `e488a5ab`: "No bugs", one test-coverage suggestion. |
| Rebase by delegate (Merge-role whole landings, gym-stat) | 3 | 1 | $2.50, $3.33, $4.14 | `20ccce21`: `PLAN.md` conflict resolved. ~$3.3 per land where the server op would cost $0 (§2.4). |
| Verify by delegate (Debug/Custom "verify"/"check" titles) | 2 | 0 | $3.04, $0.56 | `5d26e82e` confirmed a diagnosis with addenda; `8ce8119a` confirmed a mechanism. |
| FollowUp | unknown | — | — | No structural link (§1.2). |

### 2.4 Two defects the evidence exposes (spin-off cards, not slices)

1. **`LandRefused` conflates "not landed" with "landed, cleanup failed".** 9 of 10 refusals were
   the latter; the branch *is* on master and pushed. `docs/orchestration-loop.md` §5 tells the
   orchestrator a refusal "leaves the branch and worktree in place and names why" — true, but
   the reader concludes the land failed. Fix: a third outcome (`LandedWithResidue` event or a
   `residue=` field on `Landed`), `git branch -D` after the fast-forward SHA is confirmed equal,
   and a retry of the worktree removal after the branch delete (the "used by worktree" case is
   `RemoveQuietlyAsync` losing to file locks — CARD-0308/CARD-0033 territory). Until fixed, the
   S1 backfill (§4) classifies these as Rebase Clean / Verify Unreported / Cleanup Failed.
2. **The land verify assumes a solution at the repo root.** gym-stat (`server/GymStat.slnx`)
   refused with `MSB1003`, so its landings went to Merge-role delegates at ~$3.3 each. Fix: a
   per-repo verify command (repo config or `antiphon.areas.json` sibling), or skip the build with
   `verify: build skipped (no solution at root)` and say so.

## 3. Design

### 3.1 The stage enum and the role mapping

```csharp
/// <summary>CARD-0272. The question a pipeline step answers, independent of who answers it.</summary>
public enum OrchestrationStage
{
    /// <summary>Does the branch still apply cleanly on its target? Found = a conflict.</summary>
    Rebase = 0,
    /// <summary>Does the (rebased) work still build and pass? Found = red build or tests.</summary>
    Verify = 1,
    /// <summary>Is anything left behind — worktree, branch, bin-*, untracked junk? Found = something removed or fixed.</summary>
    Cleanup = 2,
    /// <summary>Is the work correct and complete against its plan? Found = a defect that needs a change.</summary>
    Review = 3,
    /// <summary>Did going back to the same delegate change the answer? Found = a material correction or addition.</summary>
    FollowUp = 4,
    /// <summary>Did the deploy verdict catch something? Found = a failed or partial verdict with a real cause.</summary>
    Deploy = 5,
}
```

Role → default stage when `-Stage` is omitted: `Review → Review`, `Test → Verify`,
`Merge → Rebase`, `Deploy → Deploy`; a task with `FollowUpOnTask` set → `FollowUp`; every other
role → no stage, no row. `-Stage` overrides the default (a Debug dispatch titled "verify the
diagnosis" is `-Stage Verify`; a Docs dispatch that deletes untracked junk is `-Stage Cleanup`).
`Code` and `Plan` never get a stage by default: a build always changes files, so a hit rate for
it is meaningless.

Why not `PlanReview` / `ConflictResolution` from the card's list: nothing recurs under those names.
Conflict resolution is the resolution of a Rebase finding — its cost attaches to that finding
(§3.3). Judging a plan before Execute happens inside the orchestrator's own turn; if it is ever
delegated it is `-Stage Review` on a Plan-review dispatch.

### 3.2 Outcome and source

```csharp
public enum StageOutcomeKind { Clean = 0, Found = 1, Skipped = 2, Failed = 3, Unreported = 4 }
public enum StageOutcomeSource { Server = 0, Delegate = 1, Orchestrator = 2, Backfill = 3 }
```

- `Found` = the stage produced a substantive intervention: a conflict resolved, a red build or
  test, a defect that needed a change, junk removed, a deploy stopped for a real reason.
- `Clean` = the stage ran to completion and had nothing to do.
- `Skipped` = the stage did not run and was right not to (verify with an unmoved base). Counted
  in runs, excluded from the hit-rate denominator, and it cost nothing.
- `Failed` = the stage itself broke (branch delete refused, `MSB1003`). Never a finding.
- `Unreported` = a delegate stage task settled without the finding line. Counted, costed,
  excluded from the rate — the same shape as `AgentTaskReportEvidence`: a class, not a guess.

Hit rate = Found / (Found + Clean). The report prints all five columns so a stage whose
Unreported share is high is visibly under-measured rather than quietly flattering.

### 3.3 Where it lives: `StageOutcomes`

```csharp
public class StageOutcome
{
    public Guid Id { get; set; }
    public OrchestrationStage Stage { get; set; }
    public StageOutcomeKind Outcome { get; set; }
    public StageOutcomeSource Source { get; set; }
    /// <summary>The task whose work was being checked — the landed task; the reviewed task when known.</summary>
    public Guid? SubjectTaskId { get; set; }
    /// <summary>The task that ran the stage. Null for a server-run step.</summary>
    public Guid? StageTaskId { get; set; }
    /// <summary>Denormalised from the task at write time: the card is the unit the question is asked in.</summary>
    public Guid? CardId { get; set; }
    /// <summary>Copied from the stage task at settlement; null for a server-run step.</summary>
    public decimal? CostUsd { get; set; }
    public long? TokensIn { get; set; }
    public long? TokensOut { get; set; }
    /// <summary>Stopwatch inside the step for the server; CompletedAt − DispatchedAt for a delegate.</summary>
    public int DurationSeconds { get; set; }
    /// <summary>The Merge task that resolved a Rebase finding, and what it cost. Set when that task settles.</summary>
    public Guid? ResolutionTaskId { get; set; }
    public decimal? ResolutionCostUsd { get; set; }
    /// <summary>≤ 1 000 chars: conflict files, the refusal tail head, the delegate's finding line, the orchestrator's note.</summary>
    public string Detail { get; set; } = string.Empty;
    /// <summary>A SHA, a task id, a verdict line — whatever lets a reader chase it.</summary>
    public string? Ref { get; set; }
    /// <summary>An orchestrator override points at the row it replaces; the report takes the latest per (task, stage).</summary>
    public Guid? SupersedesId { get; set; }
    public DateTime RecordedAt { get; set; }
}
```

Append-only (the `AgentTaskEvents` / `AgentIncident` habit). Indexes: `(RecordedAt)`,
`(Stage, RecordedAt)`, `(CardId)`, `(StageTaskId)`. Not pruned by incident retention. Plus one
column on `AgentTasks`: `Stage OrchestrationStage?` (the declaration), and one:
`FollowUpOfTaskId Guid?` (the missing link from §1.2).

Rejected homes, with the reason on the record:

- **`AgentTaskEventType.StageOutcome` with a prose `Detail`.** Zero migration, but the report
  would grep prose — the repo added `AgentTaskFailureCode` because "the repeat-dispatch guard
  keys on this rather than parsing that prose", and §2.2's nine Unreported rows are exactly what
  prose parsing yields. Events stay the human timeline; S3 adds one `FindingRecorded` event for
  drawer visibility of an orchestrator override, nothing else.
- **`AgentIncidentKind`.** Keyed to agent/session, pruned at 30 days / 500 per agent, and a clean
  run is not an incident. The hit rate needs months of clean runs.
- **More columns on `AgentTasks`** (`Finding`, `FindingDetail`). Enough for the delegate arm, but
  the server arm writes three rows per land and the Merge-cost attachment needs a row to attach
  to. One table for both arms, one query for the report.

### 3.4 Recording, per actor

**Server (automatic, exact).** `AgentTaskLandService.RunAsync` writes, in the same `SaveChanges`
as the event it already writes:

| Path | Rows |
|---|---|
| `prepared.Conflicted` | Rebase **Found** (`Detail` = conflict files, `Ref` = merge task id or "merge task cap reached"). |
| `!prepared.Succeeded` | Rebase **Failed** (`Detail` = `prepared.Detail`). |
| base unchanged | Rebase Clean; Verify **Skipped**. |
| base moved, verify OK | Rebase Clean; Verify **Clean** (`Detail` = "build OK" / "build OK, tests n/n", `DurationSeconds` = the build+test stopwatch). |
| base moved, verify red | Rebase Clean; Verify **Found** (`Detail` = step + tail head, `Ref` = the kept worktree path). |
| finalize OK | Cleanup Clean. |
| finalize "could not delete" / push rejected | Cleanup **Failed** (`Detail` = git stderr). Push rejection is Cleanup Failed too until spin-off 1 splits the outcome — the row's `Detail` says which. |
| Merge task settles (`AgentTaskReplyService.ResolveConflictedParentAsync`) | Find the Rebase Found row with `SubjectTaskId == merge.ParentTaskId`; set `ResolutionTaskId`, `ResolutionCostUsd`. A hand-dispatched Merge-role task with no such parent row is an ordinary delegate row (Stage Rebase by role default). |

`SubjectTaskId` = the landed task, `CardId` = its card. No discipline anywhere.

**Delegate (self-report, one line).** When `task.Stage` is set, `ReportingContract` appends one
paragraph after the report-marker paragraph:

> This task is a **{Stage}** pass. On the line before the report marker, write
> `[antiphon-finding:{id} found] <one line: what you found or changed>` if you found a defect,
> resolved a conflict, or changed a file because of what this pass found; write
> `[antiphon-finding:{id} clean]` if it ran clean. Running tests or reading code is not a
> finding; a change you had to make is.

`SettleAsync` parses it (`DelegationReportFormatter.TryReadFindingLine`, same prefix-scan shape
as `TryReadReportVerdict`, tolerant of the marker being anywhere in the last 20 lines) and writes
one row: `Source = Delegate`, `StageTaskId = task.Id`, `SubjectTaskId = task.FollowUpOfTaskId`,
`CostUsd`/tokens copied from the freshly rolled-up task, `DurationSeconds =
CompletedAt − DispatchedAt`, `Outcome = Found | Clean | Unreported`. Corroboration, not
inference: for a Worktree stage task the row's `Detail` gains `commits=<n>` from the same
post-dispatch commit count CARD-0286 already computes, so an `Unreported` row with commits is
visible in the report as probably-found. It stays `Unreported`.

**Orchestrator (override, rare).** `delegate.ps1 -Finding <taskId> -Stage <stage> -Found "<what>"`
or `-Finding <taskId> -Stage <stage> -Clean` → `POST /api/agent-tasks/{id}/finding`. Writes a
row with `Source = Orchestrator` and `SupersedesId` = the latest row for that (task, stage) if one
exists, plus a `FindingRecorded` task event so the drawer shows it. This is the card's option (a)
kept for the one case it is needed: §2.3's Review that said "no blocking defects" and was acted on
anyway. It is *not* the mechanism for clean runs — silence there is fine because the denominator
comes from the task rows, not from the orchestrator remembering to say "clean".

Why this split and not "the orchestrator records everything": the denominator (every stage run,
including the 80–95 % that run clean) must be automatic or the rate is fiction. Server rows are
automatic; delegate rows exist because the task row exists; the only human discipline left is the
override, at the moment the orchestrator is already doing something unusual.

Why not automatic inference for delegate stages: "found" for a Review is a defect *reported*, not
necessarily a file changed by the reviewer; for a Shared task `git=unattributable` is the normal
header; and the card's "orchestrator-authored commits between report and merge" no longer occur
(§1.1). Where the machine genuinely knows (the land op), §3.4's first table is that knowledge.

### 3.5 Reporting

`GET /api/stage-outcomes?since=&until=&stage=&cardId=&latestOnly=true` returns rows (latest per
(task, stage) unless `latestOnly=false`) and a summary:

```
stage    runs  found  clean  skipped  failed  unreported  hit%   usd_spent  usd_per_finding  server_secs
Rebase     47      1     46        0       0           0   2.1       2.51            2.51          …
Verify     46      0     13       23       1           9   0.0       0.00               —          …
Cleanup    45      0     36        0       9           0   0.0       0.00               —          …
Deploy      3      3      0        0       0           0 100.0       4.65            1.55           0
Review      2      1      1        0       0           0  50.0      14.39           14.39           0
```

`usd_spent` = Σ `CostUsd` of stage tasks + Σ `ResolutionCostUsd` (the Merge delegates a Rebase
finding cost); `usd_per_finding` = `usd_spent / found`. `scripts/stage-value-report.ps1
[-Since <date>] [-Until <date>] [-Stage <name>] [-Card CARD-nnnn] [-Json]` prints that table and,
below it, one line per Found/Failed/Unreported row (date, stage, task, card, cost, detail head).
HTTP, not SQL: the house front door is the API (`docs/ops-http.md`), and a dashboard panel later
reads the same endpoint.

## 4. Backfill (part of S1)

A one-shot `StageOutcomeBackfillService` (the `DelegationCostBackfillService` shape: idempotent,
runs at startup, logs counts) derives rows with `Source = Backfill` from existing events for every
`LandRequested` since 2026-09-01: `Conflicted` → Rebase Found (+ `ResolutionTaskId` from the
Merge task whose `ParentTaskId` matches, cost from its row); `Landed` "build skipped" → Rebase
Clean + Verify Skipped + Cleanup Clean; `Landed` "build OK…" → Verify Clean; `LandRefused`
"could not delete" → Rebase Clean + Verify Unreported + Cleanup Failed; `LandRefused` "build
failed" → Verify Failed; `LandRefused` push rejected → Cleanup Failed. `DurationSeconds` = request
→ outcome delta, flagged as such in `Detail` (`duration=request-to-outcome`). Delegate-run rows are
**not** backfilled — there is no marker to read — so the report's first delegate numbers start at
S2's deploy. §2.3 stays prose.

## 5. Slices

| Slice | Work | Files | Tests | Estimate |
|---|---|---|---|---|
| **S1** server arm + backfill + read endpoint | `OrchestrationStage`, `StageOutcomeKind`, `StageOutcomeSource`, `StageOutcome` entity, `AppDbContext` map, migration `AddStageOutcomes` (table + `AgentTasks.Stage` + `AgentTasks.FollowUpOfTaskId`), `AgentTaskLandService.RunAsync` rows with per-step stopwatch, `ResolveConflictedParentAsync` attaches the Merge cost, `StageOutcomeBackfillService`, `GET /api/stage-outcomes` + `StageOutcomeService.SummariseAsync` | `server/Domain/Enums/OrchestrationStage.cs`, `server/Domain/Entities/StageOutcome.cs`, `server/Infrastructure/Data/AppDbContext.cs`, `server/Migrations/2026…_AddStageOutcomes.cs`, `server/Application/Services/AgentTaskLandService.cs`, `AgentTaskReplyService.cs` (`ResolveConflictedParentAsync` attaches the Merge cost), `server/Application/Services/StageOutcomeService.cs`, `StageOutcomeBackfillService.cs`, `server/Api/Endpoints/StageOutcomeEndpoints.cs`, `Program.cs` DI | `DelegationWorktreeTests`: extend the five land tests (`land_happy_path…`, `land_conflict…`, `land_push_rejection…`, `land_verify_failure…`, `a_clean_change_lands…`) to assert the rows written; new `StageOutcomeBackfillTests` (one event pattern each, idempotent re-run); `StageOutcomeSummaryTests` (hit rate excludes Skipped/Failed/Unreported, latest-per-(task,stage)) | 3–4 h Code (opus or Grok; a Worktree task) |
| **S2** delegate arm | `CreateAgentTaskRequest.Stage`, role→stage default and `FollowUpOnTask` → `FollowUp` + `FollowUpOfTaskId` in `AgentTaskService.CreateAsync`, `delegate.ps1 -Stage <Rebase\|Verify\|Cleanup\|Review\|FollowUp\|Deploy>` (422 on an unknown name; `-ListAreas`-style help line), `ReportingContract` paragraph when `Stage` is set, `DelegationReportFormatter.TryReadFindingLine`, `SettleAsync` row write with `commits=` corroboration | `server/Application/Dtos/AgentTaskDtos.cs`, `AgentTaskService.cs`, `scripts/delegate.ps1`, `DelegationReportFormatter.cs`, `AgentTaskReplyService.cs`, `.claude/skills/antiphon-delegate/SKILL.md` | `DelegationUnitTests` (where `TryReadReportVerdict` is pinned; add the finding line parse: found / clean / absent / marker mid-report / both markers present), `AgentTaskServiceIntegrationTests` (Review defaults to Review, Test to Verify, Merge to Rebase, Code to null, `-OnAgent` to FollowUp and sets `FollowUpOfTaskId`, explicit `-Stage` wins), settlement test: row written with copied cost, `Unreported` when the line is absent, contract text present only when Stage is set | 3–4 h Code |
| **S3** orchestrator override | `POST /api/agent-tasks/{id}/finding` (`{stage, found, detail}`), `delegate.ps1 -Finding <id> -Stage … -Found "…"` / `-Clean`, `SupersedesId`, `AgentTaskEventType.FindingRecorded = 24` | `AgentTaskEndpoints.cs`, `StageOutcomeService.cs`, `AgentTaskEnums.cs`, `scripts/delegate.ps1` | endpoint test: override supersedes the delegate row and the summary counts the override only; a finding on a task with no stage creates the row with the given stage | 1–1.5 h Code |
| **S4** report + docs | `scripts/stage-value-report.ps1` (ASCII-only, `-Since/-Until/-Stage/-Card/-Json`, `card.ps1`'s `Invoke-Antiphon` helper), `docs/orchestration-loop.md` (§5: what the three land rows mean and that a `LandRefused` "could not delete" is a Cleanup failure, not a failed land, until spin-off 1; §0/§3: `-Stage` on verify-shaped dispatches and the finding line; a new §9 item pointing at the report), `docs/ops-http.md` + `docs/antiphon-api.md` route lines, `docs/agent-card-lifecycle.md` one sentence (a finding row is evidence, never a card move) | scripts, docs | `scripts/test-stage-value-report.ps1` (the `test-client-mode.ps1` pattern: fixture JSON → expected table) | 1.5–2 h (Docs/Code, Codex or Grok) |

Total ≈ 9–12 h. S1 alone already answers the card's question for the land pipeline with 47 runs
of backfilled history on the day it deploys. S1 → S2 → S3 are sequential (S2 needs the table and
the `Stage` column; S3 needs both arms to have something to override); S4 can start after S1 and
finish after S3.

## 6. Later, not v1

- **S5 orchestrator reaction cost.** For each machine-origin prompt into an orchestrator session
  (`SessionQueuedMessages` with `SourceTaskId`, `Origin = Delegation`, `SentAt`), sum the
  transcript token counters from the matching `UserPrompt` entry to the next `UserPrompt`, and
  attribute them to the stage the source task ran (or to Rebase/Verify/Cleanup for a `land:` key).
  That is the orchestrator's own token cost of *reading* a stage's outcome — the number the card's
  motivation was really about, now that the orchestrator no longer runs the verify itself.
- **`deploy-local.ps1` posting a Deploy row** (`DEPLOY VERDICT: failed` → Found with the detail;
  `ok` → Clean). Script-run stage, no task; needs a token-less POST path or a `-Card` argument.
- **A dashboard panel** on the delegations History tab reading `GET /api/stage-outcomes`.
- **Auto-upgrading `Unreported` to `Found` on commit evidence.** Deliberately not: it is the
  inference the card and the house style both step away from. Revisit only if the Unreported
  share stays above ~20 % after S2 and the `commits=` corroboration is right every time.

## 7. Not in scope

- Refine (`-Refine`, `Refined` events): a brief amendment to a running task, not a stage run.
- Check-role interpretations: already exempt from report markers (CARD-0302); not a stage.
- Changing any land-operation behaviour (spin-off 1) or its verify command (spin-off 2). S1 only
  records what the operation already does.
- Any change to what counts as a delegate's report verdict (`ReportEvidence`, nudges).

## 8. Decisions for the caller

1. **Accept the taxonomy**: six actor-independent stages, role defaults, `-Stage` override,
   Merge as the cost of a Rebase finding rather than a stage. The alternative (a stage enum that
   mirrors `AgentTaskRole`) would give the repo two "stage" vocabularies; rejected in §1.1/§3.1.
2. **Accept the recording split**: server automatic, delegate self-report line, orchestrator
   override verb, `Unreported` bucket with no inference. This confirms the card's lean toward
   explicit self-report, with the denominator made automatic so the lean does not depend on
   discipline for the common clean case.
3. **New table `StageOutcomes`** over an event type or an incident kind (§3.3).
4. **FollowUp and Deploy are in scope for v1**; Refine is not.
5. **File the two spin-off cards** from §2.4 (the `LandRefused` conflation with 18 stale
   worktrees today; the per-repo land verify command). Both are land-operation defects this
   plan's data made visible; neither is this card's build.
6. **Dispatch**: S1 as one Code task (Worktree; `-Verify "/*/Antiphon.Tests.Application/*DelegationWorktree*|/*/Antiphon.Tests.Application/*StageOutcome*"`
   on its `-Land`), then S2, then S3, with S4 as a Docs/Code task after S1. Land with `-Land`,
   which will itself write the first live rows.

## 9. Card addendum (ready to paste)

```
## Plan (2026-09-02, task fb44052a)

Plan: docs/superpowers/plans/2026-09-02-card-0272-stage-hit-rate-vs-cost-plan.md.

CARD-0258's land operation HAS shipped (Done 08-31; 47 runs 09-01 12:57 .. 09-02 17:06). The
blanket mechanical verify this card was written against no longer costs the orchestrator tokens:
it is the land op's conditional build (13 real runs, 0 found, 23 skipped as base-unchanged; the
-Verify test path has never run). Rebase found 1/47 (CARD-0299, $2.51 Merge delegate). Cleanup
failed 9/47 ("could not delete branch", reported as LandRefused; 18 stale worktrees) - a defect,
not a finding. Delegate-run stages that found things today: Deploy 3/3 (~$1.5 each), Review 1/2.

Design: stage = the question a step answers, actor-independent - OrchestrationStage { Rebase,
Verify, Cleanup, Review, FollowUp, Deploy }; roles default onto it (Review, Test->Verify,
Merge->Rebase, Deploy), -Stage overrides. Outcome { Clean, Found, Skipped, Failed, Unreported }.
Recording: server-run land steps automatic and exact; delegate stages self-report one closing
line [antiphon-finding:<id> found|clean]; orchestrator override delegate.ps1 -Finding for the
rare disagreement; no inference. Storage: new StageOutcomes table + AgentTasks.Stage +
AgentTasks.FollowUpOfTaskId; not AgentTaskEvents (prose) or AgentIncidentKind (pruned,
per-agent). Report: GET /api/stage-outcomes + scripts/stage-value-report.ps1. FollowUp and
Deploy in scope; Refine out.

Slices: S1 server arm + backfill of the 47 lands + read endpoint (3-4h); S2 delegate arm (3-4h);
S3 override verb (1-1.5h); S4 script + docs (1.5-2h). Spin-off cards to file: LandRefused
conflates landed-with-cleanup-failure; land verify needs a per-repo build target (gym-stat
MSB1003 sends its lands to $3.3 Merge delegates). Next: dispatch S1.
```
