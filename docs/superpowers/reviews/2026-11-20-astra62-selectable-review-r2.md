# Review r2: gpt-6.2-astra selectable fix (Code task a028435d)

Reviewer task `ffb8e095` (Claude Opus; author Grok). Ordinary round 2: the diff since the r1 SHA,
blockers and regressions only.
Subject `origin/feat/card-task-a028435d` tip `e00891f6961b10a4cbda2bb9a03f60faf45b0df9`, range
`3bc8ac66fb1aba3f27fa01afbb4d8378ac3b962f..e00891f6961b10a4cbda2bb9a03f60faf45b0df9`. Master is
`c124449e3b7d3c1ba0508edfb9a3c2274356f427`; the brief named `4493e8b4e`, and master had moved on.
This ran on Linux (server2) only. Windows was not observed.

**Verdict: BLOCKER (1).** D-2 is closed. D-1 is closed in both directions for the unpinned shop
and both warm sites. The new pinned-pool decline adds one regression: it sits before the busy
check (the CARD-1481 shape). The pinned branch also does not apply the shop's alias-history
exclusion.

## Blocker

### R2-D-1 A pinned alias task on a busy pool delegate launches a second session on that row

- **Where:** `server/Application/Services/AgentTaskDispatcher.cs:8874-8880`. The new
  `!PoolDelegateRelease.MayWarmPool(claimed.RequestedModelAlias)` → `SpawnFresh` runs before
  `if (pinned.Status != AgentStatus.Idle || pinned.PoolIdleSince is null) return WaitForAgent;`
  (`:8929`). On `SpawnFresh`, `ResolveAgentAsync` (`:7822-7834`) returns the pinned row, and the
  dispatch launches a NEW session on it.
- **Failure:** Take a task created with `agentId` = a pool delegate, `model: gpt-6.2-astra` and
  explicit kind/level, while that delegate is Running another task or holds a Blocked task's
  session with no park row (parking is off by default). It no longer waits (`HeldForAgent`). It
  launches a second session on the busy row mid-task. That is exactly the defect of CARD-1481
  repair D-1(b) (Final Review df795c38), whose fix moved the effort decline after the busy check
  (`:8931-8934`). At r1 tip `3bc8ac66f` the same task waited, so this is a regression this fix
  introduced. Its test `a_pinned_alias_task_does_not_reuse_its_warm_pool_delegate` seeds only a
  FREE warm delegate.
- **Second hole, same branch (fix together):** the pinned branch checks only the CLAIMED task's
  alias. The unpinned shop and the desktop query exclude a row with any alias task
  (`!_db.AgentTasks.Any(t => t.AgentId == a.Id && t.RequestedModelAlias != null)`), but the
  pinned branch does not. `MarkIdleForJanitor` sets `Idle` + `PoolIdleSince` with the process
  alive in two cases: the CARD-0085 `killSession: false` arm for `CompletedWithoutProgress`
  (`AgentTaskReplyService.cs:1566`, `:2923-2926`) and a kill that did not take (`:2950`ff,
  dispatcher sweep `:9650`). A ladder task (a Frontier Review, say) pinned to that row then
  passes every check down to `agent = pinned` and inherits the gpt-6.2-astra process. The brief
  asked whether a Frontier/Review task can never inherit alias state. On the pinned path it can.
- **Why:** The r2 fix is fail-closed in the pool shop and at the warm sites. The pinned branch
  got a claimed-side check placed above the busy gate, and no history-side check.
- **Fix:** In the pinned pool branch, move the alias decline below the
  `Status != Idle || PoolIdleSince is null → WaitForAgent` gate, beside
  `ClaudeEffortMatchesPool`. Make it symmetric: `SpawnFresh` when the claimed task has an alias
  OR the pinned row has any task with a non-null `RequestedModelAlias`. That is the same
  predicate as the shop. Add witnesses: (a) a pinned alias task on a Running pool delegate →
  `WaitForAgent`, with no second session; (b) a ladder task pinned to an Idle pool row whose
  last task carried an alias and whose session is live → `SpawnFresh`, `AgentSessionId` not
  reused. Optionally add (c): an alias Shared task settled through `AgentTaskReplyService`'s
  release (the main settle site, not only the sweep) → killed. Today only
  `ReleaseUnownedPoolDelegatesAsync` has a witness.

## Answers to the brief

1. **D-1 coverage.** I grepped every `PoolWarm(` call and every pool selection predicate. There
   are two warm sites, `AgentTaskReplyService.cs:2894` and the sweep `AgentTaskDispatcher.cs:9622`,
   and both are gated on `MayWarmPool`. Two selection queries exist: the shop at
   `AgentTaskDispatcher.cs:8950` and desktop memory at `.DesktopMemory.cs:62`. Both are gated on
   the claimed alias and exclude alias-history rows. `PlaceOnStandingAgentAsync` is unchanged:
   standing seats launch their own model, and create's `model_alias_conflict` covers them. The
   only remaining hole is the pinned pool branch (R2-D-1). On the ordinary settle path an alias
   process is killed. The CompletedWithoutProgress and failed-kill cases leave it Idle. The shop
   exclusion protects those rows; the pinned path does not.
2. **D-2 coverage: complete.** Every non-test assignment to task `AgentKind`/`ModelLevel` is at
   `AgentTaskDispatcher.cs:1861-1863` and `:2041-2043`, and at `AgentTaskService.cs:3347-3353`,
   `:3644-3646` and `:3484`. Only `:3484` (Escalate) keeps the alias. Escalate hold-checks
   `DispatchModelAlias.AfterRequeueAsync` (the slug that launches) and moves only the level; the
   kind never changes. It still runs the operator's explicit slug at the new level's effort,
   which is consistent with r1's retry/escalate ruling. The event text naming the ladder alias
   is r1 A-1 (advisory). `ResumeRoutingBlockedAsync` clearing the alias is effectively
   unreachable for alias tasks. It applies only to list-governed tasks (`Complexity` or
   `RoutingPinId`). Model-only, complexity-plus-model and matrix-default creates with a model now
   refuse or skip the matrix. The plan documents it. The clear is not surfaced in the event detail
   (advisory A-2).
3. **Regression with alias null: none found.** `MayWarmPool(null)` is true. The shop and desktop
   predicates add one `NOT EXISTS` on `RequestedModelAlias != null`. CP-1 52/52 and CP-2 30/30
   pass. The 422 fires only when `Model` is non-blank and both kind and level are null after
   pin/standing fill. `delegate.ps1` without `-Model`, standing agents and the matrix default path
   are unaffected. CP-3: `MatrixDefaultComplexityCreateTests`, `ComplexityDispatcherTests` and
   `HeldStandingAgentChainTests` passed 59/59.
4. **Client:** one optional `requestedModelAlias?: string | null` field on `AgentTaskSummaryDto`
   with no reader, so behaviour does not change. I did not typecheck it separately. It is a
   type-only addition.
5. **Witnesses:** the new tests assert outcomes: the session is in `stopper.Killed`, the
   `ReuseOutcome`, `AgentId`/`AgentSessionId`, the launched argv on reroute, 422 status and no
   row written. I did not re-run red at `e8c59c742`; the plan records it. There is no busy-pinned
   witness (R2-D-1).
6. **Pins:** `git merge-tree --write-tree origin/master HEAD` is clean (tree `e5f0477a6c98`). In
   a detached probe of that tree (commit `2ccbdfc07`, worktree removed), `-PrintInputDigest`
   computed `15137e48291f1b0c70668a7e967965ed1391f64812cddea271387d48ce1a9365`, which equals
   master's stated `PinnedInputDigest`. No pinned input is edited. I did NOT run the
   derived-impact-index regeneration on the merged tree: the 1-minute load stayed above 16. The
   land's rebase verification owns it, as in r1 A-4.

## Advisories (not land-blocking)

- A-1 (r1 A-1 remains): Escalated/Retried event text names the ladder alias while an alias task
  launches its slug.
- A-2: Dispatch re-walk and routing-blocked resume clear `RequestedModelAlias` without naming
  that in the Rerouted event detail. The clear is unreachable for current alias tasks; name it if
  a future path makes it reachable.

## Evidence

Checkpoint run `20261120-124645-69bf` at `e00891f69` from a review plan extract of the subject's
own rows CP-1/2/3/5/8 (`.antiphon/r2-plan.md`, gitignored). It used one build `bin-r2astra/`
(about 12 min under load), `buildSource=verified`, `dirty=0`, `sourceState=clean` and
`unlisted: none`, with verdict GREEN exit 0.
`validate --evidence report.json --expected-source-sha e00891f69…`: `CHECKPOINT SOURCE VALID rows=5`.

- CP-1 `AgentTaskPoolTests`: 52/52, 0 skipped (67 s)
- CP-2 `CodexDelegateDispatchTests`: 30/30 (49 s)
- CP-3 matrix/complexity/held-standing: 59/59 (47 s)
- CP-5 guard batch (policy, guard, slow-test registry/tripwire, NativeLinux witness, mechanical
  PC, platform guidance, C1729 layering, lane category): 81/81 (121 s)
- CP-8 `check-evidence-diff` `3bc8ac66f..HEAD`: exit 0

These rows were not re-run: CP-4 (alias display classes; this fix does not touch them), CP-6
(index), CP-7 (land graph) and CP-9 (branch pin; the merged-tree recomputation above replaces it).
The run started at a 1-minute load of 19.1. The load was 13.9 at the check just before.
PCs: none in the plan; mutation is waived (operator 2026-11-12).
