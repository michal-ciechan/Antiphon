# CARD-1513 Review round 1 (Final): rewalk Rerouted must not spawn a held standing-agent model

Review task 5f4dce6e. Subject: Code task fdcaec9d (Grok author), branch `feat/card-task-fdcaec9d`.
Base `965ab080395310c957b04e3ea9088c5f04534670`, red `45387d1684fd39083a0c84a678bbd4b90a6f65ad`,
fix and reviewed tip `d882bbbdb75a4ae52697ae330f730f208238bb21`. Current origin/master
`9e050cf9600aef15b77d67957fc0cd68826d6b35`.

Verdict: CLEAN. No blockers, no regressions. Four advisories (below) for cards; none holds the land.

## Runs (reviewed SHA d882bbbdb, Linux server2 mirror, UseAppHost=false, native output)

| Run | Lease | Result |
|---|---|---|
| Build `tests/Antiphon.Tests` | 4a792676 waited=0s held=277s | exit 0, 0 errors |
| Dispatcher routing classes: ComplexityDispatcherTests 11, ComplexityWallRerouteTests 11, DefaultRunnerRerouteTests 16, HeldStandingAgentChainTests 7, RoutingExhaustedPlacementTests 54, RoutingPinCandidateDispatchTests 12, WallRerouteDispatchTests 15, ExpectationPipelineTests 9 | b5f881dd held=114s | total 135, failed 0, skipped 0 (fresh TRX; both new C1513 methods present and Passed) |
| Guard union: TestClassificationPolicyTests 41, TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3, TestLaneCategoryGuardTests 1, TaskPlatformGuidanceTests 5, C1729LayeringGuardTests 9, MechanicalPcContractTests 11 | a8b913d6 held=136s | total 78, failed 0, skipped 0 |
| `audit-land-verification-graph.ps1 -Check` | 9939b003 held=46s | exit 1: tool rebuilt (missing) then "evaluating Antiphon.sln copy exceeded 5 s" at load average ~32; canary removed |
| same, once more (tool already built) | d9e8f380 held=25s | exit 0, `approved`, digest 3ED4A38C...EB46 count=75 |
| `check-evidence-diff.ps1 -BaseRef 965ab0803 -HeadRef d882bbbdb` | n/a | commits=2 entries=0 violations=0, exit 0 |
| `git merge-tree --write-tree origin/master d882bbbdb` | n/a | tree f146f612efeea4a7cd6d265912e4d480b495fb84, exit 0 |

Master moved 11 commits since base; none touch the dispatcher, DispatchModelAlias, ModelAvailability,
AgentTaskService, the touched test file, any csproj/props/targets, the land-graph records or the policy
json. So the land-graph result at the tip carries to the merge.

There is no CARD-1513 plan and no `### Checkpoints` table. The Code brief commissioned it plan-less
("No checkpoint table" in the Code report), so there are no CP-n rows to match. No checkpoint-tool
receipt exists, so `reviewedSourceClean` is false: the worktree was clean at the reviewed SHA, but there
is no receipt.

Red-for-the-right-reason was not re-run here. The Code report gives red total 2/failed 1 with
`Dispatched` was 1. By construction, pre-fix `Rerouted` falls through to spawn, so the first
assertion `C1513-tick-no-spawn` is the one that goes red. PC-1 (drop the block) and PC-2 (drop the
Ephemeral short-circuit) stay pending for SourceLanding Mutation.

## Checks

1. **Paths from the tick to a spawn.** The tick hold check (`DispatchModelAlias.ForTaskAsync`) is the
   single funnel for every requeue: Retry, the Reroute API, released-seat recovery, routing-blocked
   resume and wall reroute all requeue, then meet this check. Start-now uses `RequireAsync` and never
   skips. When the standing agent's model is held, the tick now resolves as follows:
   - walk `None` (or the pin forbids a reroute): skipped with a capacity wait, as before;
   - walk chose a different (kind, level) on the same kind: the new Block;
   - walk exhausted: the existing routing-exhausted Block.

   The new check runs before the reroute transaction, so there is no Rerouted event and no kind or
   level write.
   - Alias spelling: `ModelAlias.Normalize` is kind-agnostic and folds `fable`, `Fable 5` and
     `claude-fable-5-1` to `fable`. The trigger (`ForTaskAsync`) and the new check
     (`StandingAgentModelAsync`) use the same Normalize. So any spelling that trips `modelHeld` on the
     same kind also trips the Block. `FindActiveAsync` lower-cases and ORs the kind-wide `*`.
   - A ModelId given as a tier name (`Frontier`) does not normalize, and the trigger, the check and
     the spawn all treat it alike.
   - The three out-of-scope items do not make the fix pointless:
     - released-seat recovery goes through Retry, which CARD-1496 refuses, then through the tick;
     - A-3 is a disabled clock;
     - for the cross-kind case, see A-1.
2. **Unblock.** A task Blocked here never unblocks by itself. The reason deliberately avoids
   `routing exhausted: `, so the routing-blocked resume does not requeue it. This matches CARD-1496's
   wall Block ("A human must decide"). See A-2.
   - Reason text: aliases, kind and an until timestamp only; no secrets.
   - `BlockedRoutingExhausted` is incremented the same way as the sibling `BlockedRunnerKind` arm.
   - `SkippedModelAvailability` is untouched.
3. **Controls.** Ephemeral tasks and pool agents with a null ModelId give `StandingAgentModelAsync`
   null, so their path is unchanged. Both are witnessed: the new ephemeral control, and the existing
   pool rewalk test (Rerouted=1). The diff touches only the new branch and the new private method.
   - The operator rule holds: the Block never picks a kind and never reroutes a held model.
4. **Enum, policy and migration.**
   - `RewalkOutcome` is a private nested enum. Value 4 is never persisted or serialized, so no
     migration is needed.
   - Policy json is unchanged.
   - Test assertions were only added; none was loosened. The seed helper gained an `ephemeral`
     parameter whose default (false) keeps the old fixed value.

## Advisories (cards; none blocks)

- **A-1 (pre-existing, CARD-1496 documented limit): a cross-kind reroute on a standing agent.**
  - Scenario: a ClaudeCode standing agent with ModelId `fable`, fable held, and the walk chooses
    Codex.
  - `GetActiveHoldAsync(Codex, "fable")` finds no hold, so the task reroutes to Codex on the pinned
    agent.
  - Spawn: `ResolveAgentAsync` does not restamp a standing agent's Kind, and the launch uses
    `task.AgentKind` (Codex) with an exact `agent.ModelId=fable`. A profile-bound agent throws the
    "pinned agent now runs" conflict instead.
  - This does not spawn the held Claude model, but it launches the wrong program/model pair.
    Suggest a card: Block cross-kind reroutes of standing agents that have a ModelId.
- **A-2: a held standing-agent model is handled differently depending on what the walk chose.**
  - Walk `None`: the task stays Queued with a capacity wait and resumes when the hold lapses.
  - Walk chose another candidate: the new Block, which needs a human.
  - The model is the same in both cases. The card asked for a Block, so this follows the card. A
    card could decide between a wait and a Block.
- **A-3: the Block does not supersede a capacity wait the task already holds.** If the task holds a
  QueuedTask capacity wait from an earlier skip tick, the Block leaves it in place; the reroute path
  calls `SupersedeTaskWaitsOnAsync`. The orphan sweep cancels waits only for terminal tasks. This is
  the same as `BlockQueuedChainIfExhaustedAsync`.
- **A-4: the Block is a blind write.** `AgentTask.ConcurrencyToken` is not an EF concurrency token.
  The new Block saves without the `FOR UPDATE` and token check that the reroute path takes. A Cancel
  that lands between the tick's load and this save would be overwritten to Blocked. The sibling
  Blocks in the same tick (`BlockQueuedChainIfExhaustedAsync`, `BlockRunnerKindAsync`) work the same
  way, and the window is two hold reads.
