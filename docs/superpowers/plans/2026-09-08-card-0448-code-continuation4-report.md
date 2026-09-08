# CARD-0448 continuation 4

Acceptance is in progress. This checkpoint does not authorize landing or deployment.

Task: `4692ec26`. Branch: `feat/card-task-4692ec26`.
Worktree: `C:\Antiphon\worktrees\card-task-4692ec26`.
Starting commit: `ea924414`, fetched from `feat/card-task-c86499fb`. That branch was
already checked out elsewhere, so this task uses its own branch at the exact requested SHA.

## Installed acceptance fixtures

Commit `1be12848` installs the reviewed pending fixtures from the previous continuation:

- 24 real dispatcher admission races: Shared/follow-up writers, Fresh/AlreadyPresent/
  ResumePublication/CleanupRetry, land-first/dispatch-first/dispatch-before-acquire.
  The fixture resolves the actual dispatcher, commits claims in a private schema, and
  has no hosted launch worker. It checks pending state, attempts and prohibited mutations.
- Source boundary cases expand from 18 to 90 data rows. BeforeRebaseIntent and
  BeforePushIntent now fire from their actual preceding Git commands. Immediate intent,
  verifier and command-suffix assertions distinguish a missing guard from a later refusal.
- C24 starts a second OS worker after killing the original worker while its identified
  Git child remains live. That recovery worker independently checks admission from both
  main and linked paths. The test retains unrelated-child and child-start-identity checks.
- The common safety harness has an optional service registration hook; the two new
  process-spawning classes are included in the limiter census.

The obsolete preparation/retry and replacement-policy installers were not applied.

## Execution record

`bin-c448-c4` builds with 0 errors and 133 warnings. The first fixture run is pending;
individual completed test messages are not a completed run verdict.
The exact pre-change incident adapter also builds at `f202fcc1` in the independent
`C:\Antiphon\worktrees\c448-4692ec26-baseline` checkout (0 errors, 132 warnings).
It has not yet earned incident RED credit.

Local execution scripts, logs, TRX and command evidence are under this task's `.antiphon`
and isolated `tests/Antiphon.Tests/bin-c448-*` outputs. The 86 adapted mutation definitions
have unique anchors against the current source; definitions are not execution evidence.

## Destructive caller census

The expanded plan search was rerun against Application, Infrastructure/Git, Domain and
all client source. Raw results: `.antiphon/c4-caller-census.txt`. This classifies the current
callers; the final executed evidence ledger must still establish their acceptance.

| Caller/path | Authority and exclusion | Final guard / associated tests |
|---|---|---|
| `AgentTaskLandingProtocol` publication and cleanup | Leased operation, acknowledged pins/intents, independent push-endpoint containment and committed receipt | `AgentTaskLandBoundaryTests`, preparation, publication, recovery, checkpoint and persistence-failure classes |
| `GuardedWorktreeRemoval.RemoveAsync` publication | Typed request plus fresh persisted operation/task authority and current repository lease | Repeated source/content inspection, final durable authority, ordinary remove, registration inspection and exact-old-SHA branch deletion; removal/cleanup matrix and `LandingRemovalControlTests` |
| `DelegationWorktreeService` settlement/local merge | Shared repository lease; distinct LocalMerge authority bound to source and parent SHAs | Source and parent checkout/sequencer/content rechecks; local merge safety and delegation worktree tests |
| `WorktreeManager` unfinished creation rollback | Invocation-owned unfinished creation intent and repository lease | Unchanged initial SHA, registration and empty status including ignored content; ordinary removal then exact branch CAS; creation safety and delegation worktree tests |
| Raw `WorktreeManager.RemoveAsync` / `TryRemoveAsync` | No typed authority: refuse | No deletion; default-interface/removal-authority and legacy Git integration tests |
| `PruneStaleAsync` / janitor | Age is not authority; routes to refused raw removal | Retains task work and reports no removal; manager Git integration tests |
| `WorktreeResidueSweepService` | Legacy event/preview does not supply authority; raw removal refuses | Reinspection and retained bytes/counts; residue sweep tests |
| `LandingVerifier` output | Unique external artifact directory; no output deletion or archive-disposability exception | Retains outputs and journals owned child; verifier tests |
| `WorkspaceHookService.RunBeforeRemoveAsync` | No production caller | No reachable hook-triggered removal in this census |
| `GitService.DeleteBranchAsync` | Explicit `WorkflowEngine.DeleteWorkflowAsync` caller, separate from task landing | Existing local/remote workflow branch deletion is outside this card; no landing/removal/recovery caller reaches it. Landing fixtures independently check their pre-published remote source survives. |
| `StageOutcomeBackfillService` and client outcome consumers | Historical prose grants no publication or cleanup authority | Backfill is inert; structured event/DTO handling is covered by stage/pipeline/client tests |

No landing path calls the legacy branch-ancestor shortcut or unconditional interrupted-rebase
abort. The only worktree removal commands in these production paths are ordinary Git removal;
the retained explicit workflow deletion is not task-worktree cleanup.

## Remaining work

Complete the new fixture run and fix any failures, execute the baseline incident adapter,
finish the required independent mutation variants, refresh the per-variant ledger, and run
the final unmutated combined regression. The canceled prior combined run receives no credit.
