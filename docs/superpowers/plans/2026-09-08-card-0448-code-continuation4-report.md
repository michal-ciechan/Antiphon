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

Subsequent fixture changes cover the named remaining variants: first/final ignored-content
guards with immediate status-read-count assertions; source untracked/registration and target
staged changes during verification; four post-fast-forward target changes; changed source/
target at C02/C03/C07/C08/C09 and a C03 pin collision; and both acknowledgement outcomes
at the individual source-pin-result save. Their execution is recorded below. The optional
SaveFault predicate selects that subphase without moving its boundary.

## Execution record

`bin-c448-c4` builds with 0 errors and 133 warnings. `c4-fixtures-01.trx` completed
66 executed / 65 passed / 1 failed / 0 skipped (43m50s). All 24 admission races and
30 source-boundary selectors passed; RepositoryMutationLeaseTests passed 11/12.
The sole failure, C24 killed-worker recovery, stopped before child-PID acknowledgement.
It receives no C24 credit. The worker fixture now pins Git identity, signing and its blocking
hook explicitly instead of inheriting global configuration, and reports worker startup errors.
The rebuilt `c4-c24-02.trx` passed 1/1 with no skips. Evidence records recovery worker
14696/start ticks 639244966056988289 independently holding both main and source admission,
separate from test process 24284. The original failed run remains recorded as a setup failure.
The exact pre-change incident adapter also builds at `f202fcc1` in the independent
`C:\Antiphon\worktrees\c448-4692ec26-baseline` checkout (0 errors, 132 warnings).
`c4-incident-baseline.trx` executed 1 / passed 0 / failed 1 / skipped 0 with the intended
directory-preservation assertion. The old production shortcut and remover actually deleted
the clean unique detached checkout and local branch. Fixture source S was
`cb7df822e40f4c1ba48045bdc75b82ca0b139afa`, unique detached U was
`aa198f3c482329a7d01e6e91380d08e199631347`, and the independently observed remote source
remained S. This is baseline incident RED credit, not an implemented-code failure.

Client: TaskDrawer 18/18 and pipelineStageModel 34/34 passed in separate wrapper invocations
with exit 0. `npm run build` passed. The first expanded backend build found one fixture
expression-tree compilation error (CS8122); the predicate was corrected to ordinary equality
comparisons before rebuilding. The next build passed with 0 errors and 131 warnings.
No execution credit is assigned to the failed build.

The focused expanded-fixture sequence completed with 33/33 passing, no failures or skips:

| TRX suffix under `bin-c448-c4-next/TestResults` | Executed / passed | Scope |
|---|---:|---|
| `c4-next-LandingRemovalControlTests.trx` | 6 / 6 | First/final ignored-content guards, three protected path classes each |
| `c4-next-AgentTaskLandBoundaryTests.trx` | 4 / 4 | Target advance/switch/dirty/staged changes after real fast-forward |
| `c4-next-AgentTaskLandConcurrencyTests.trx` | 10 / 10 | Source and target changes during verification |
| `c4-next-AgentTaskLandCheckpointMatrixTests.trx` | 11 / 11 | Changed C02/C03/C07/C08/C09 state and C03 pin collision |
| `c4-next-AgentTaskLandPersistenceFailureTests.trx` | 2 / 2 | Before/after-commit source-pin-result acknowledgement loss |

The queue wrapper's foreign-process exit-code check prevented its automatic mutation step;
the original tool completion was exit 0 and all five fresh TRX files passed. Mutation execution
was started directly after those verdicts and the C24 rerun were checked. This was a queue
bookkeeping failure, not a canceled run or a test failure.

The policy fixture build and `c4-policy-01.trx` passed 92/92 with no skips: 26 identity
decisions, 38 cleanup/local-parent/target decisions, 19 state-policy cases and 9 TRX counter
cases. These call production decisions with controlled replies and mutation-capable downstream
fakes; the real-Git matrices remain required companions. Target-decision controls invoke the
existing private decision through reflection, avoiding a new production public test API.
The TRX counter predicate was extracted verbatim into an internal helper; its boolean policy
and production caller behavior are unchanged. Individual controls replace the earlier broad
verification-policy mutation as independent-clause evidence.

`c4-push-ack-01.trx` passed 2/2: both push-result save acknowledgement outcomes preserve
the source until a fresh restart confirms the accepted remote update, without a second push.
`c4-real-verifier-01.trx` passed 3/3 after the counter extraction (real selected pass, selected
failure and zero-selection cases).

The task-owned `.antiphon/policy-project/PolicyControls.csproj` links the exact four policy
test source files and the production server. It contains no service host, real process or DB
fixtures. `c4-policy-adapter-01.trx` passed the identical 92 named cases/outcomes in 1 second,
compared with 39 seconds in the normal assembly. `c4-policy-equivalence.json` records that
comparison and project hash. Its controls have a separate output tree and fingerprinted
manifest. The final normal-assembly regression remains mandatory.

Individual mutation results, including pending variants, are tracked in
[the control ledger](2026-09-08-card-0448-code-continuation4-controls.md). A broad policy
mutation or the existence of a definition is not independent-variant acceptance.

The first linked-policy batch completed all 70 independent controls. Every control has a
passing complete selection, an intended assertion RED, and a passing rebuilt restoration;
all 70 RED oracles were inspected individually (`c4-oracle-review.json`). Identity/status
omissions accepted invalid snapshots, crossed receipt/local-parent omissions issued the
forbidden destructive commands, and target/verification omissions accepted invalid evidence.
The separate normal-project controls plus this batch total 84 current-filter triples; the
old classification-only PC2 run is superseded and the broad verification-label control remains
supporting evidence only. These totals do not close the still-pending real-Git controls.

The next fixture build passed with 0 errors and 133 warnings. Its first normal-assembly
selection passed 50/50 cleanup-policy rows. Added fixtures cover each final content reading,
durable authority changed during inspection, save acknowledgement prerequisites, exact stored
target recovery, settlement's first mutation under a held lease, nested acquisition requests,
multiple destinations, and verifier pass/fail/cancel/throw outcomes. Their remaining selected
runs are in progress, so compilation is not credited as execution.

The destination, namespace and fingerprint predicates are duplicated in identity and publication
policy. The prepared controls omit both copies of one identical predicate while preserving the
other decisions; omitting only one copy is intentionally redundant. The implementation has no
archive-based deletion exception: opaque reports and verifier output are retained, regardless of
archive existence. The planned output control restores deletion of pre-existing `bin-land`
and checks actual verifier pass/fail/cancel retention; no archive-deletion feature is added.

Local execution scripts, logs, TRX and command evidence are under this task's `.antiphon`
and isolated `tests/Antiphon.Tests/bin-c448-*` outputs. The adapted mutation definitions
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

The S6 manual documentation review checked orchestration sections 0/5/8, the HTTP/API
landing entries, `delegate.ps1` and server bundles against current structured outcomes.
The current contract distinguishes confirmed publication, AlreadyPresent and cleanup retry,
and warns that refusal can follow local advancement. The preserved CARD-0220 forced-rollback
paragraph now explicitly identifies its instructions as historical and superseded.

Finish the required independent mutation variants, refresh the per-variant ledger, and run
the final unmutated combined regression. The canceled prior combined run receives no credit.
