# CARD-1271 Required single-candidate Orchestrator edge

Code task / landing owner: `4fc0b3a4-4824-4feb-8a8a-d790c64d9581`.
Task base (merge-base with fetched master): `9cbe00bd52dd77367a34244c9cb9ea7d24539add`.

## Finding

A Required stage pin without candidates (forbid-only) contributes no pin slots, so
`RoutingCandidates.Compose` returns the matrix row unchanged. A one-candidate row is
not walked (`Walked: false`). With no chosen candidate, `AgentTaskService.CreateAsync`
enters the Required-single-candidate arm (its source comment: "today's 409s, never Blocked").
That arm re-checks the stage forbid list, quota and model holds. At the task base it did not
re-check the walk's kind filter, so an Orchestrator with explicit `-Complexity` and one Codex
row skipped the row as `not a delegate kind`, then was created on Codex (V-1 red at S1).
Other paths keep an Orchestrator on ClaudeCode (CARD-0084): an explicit Codex or Grok kind
is a 422 `ValidationException`
(`AgentTaskAgentKindTests.an_orchestrator_cannot_be_asked_to_run_on_Codex` and `..._on_Grok`),
a policy kind clamps (`a_promoted_role_does_not_make_its_orchestrators_unrunnable`), a walk skips
(`RoutingPinCandidateCreateTests.Orchestrator_skips_non_Claude_pin_candidates`), and reroute
refuses (source: `AgentTaskService`, "An orchestrator cannot be rerouted off ClaudeCode").

## Decision

The arm refuses before its other checks when the walk's kind filter rejects the single
candidate. It throws 409 `routing_exhausted` with the `complexityRouting` extension.
The arm is an `else if` ahead of the `request.RefuseIfExhausted` branch in
`AgentTaskService.CreateAsync`, so that flag is not read on this path (source; no test sets
it). V-1, V-3 and V-4 each assert no task row. `AdmitsKind` reads only the task kind and the
candidate kind, so a hold or reset does not change its answer (source). The walk and the arm
share that predicate, `ComplexityRoutingService.AdmitsKind`. The message tells the caller
that a human must change the row or pin, and not to pick a kind.

S1 commits the five tests and this plan. S2 makes the production change and adds the
docs paragraph in `docs/orchestration-loop.md`. The D-1 repair (Review f28f0f79, Code task
`7551b8d4`) adds V-4, rebinds PC-3 to it and corrects V-2, the class summary and the docs.

## Verification design

- V-1: `RequiredSingleCandidateRefusalTests.An_orchestrator_with_complexity_over_one_codex_row_is_409_routing_exhausted`
  covers an Orchestrator, explicit Hard, a Required pin forbidding only `grok-4.7`
  and one Plan/Hard Codex/Frontier row. It expects 409 `routing_exhausted` with message prefix
  `routing exhausted: Plan/Hard chain — gpt-6-astra not a delegate kind` and
  `An orchestrator runs only on ClaudeCode`. The routing DTO must be not walked, with one
  skipped Codex candidate, reason `not a delegate kind`, and no task row.
  Red at S1 (created on Codex), green at S2 (CP-1).
- V-2: `An_orchestrator_with_complexity_over_one_claude_row_is_created_on_it` is a control:
  the same pin over one available ClaudeCode/Frontier row creates a Queued Orchestrator on
  ClaudeCode/Frontier. The walk chooses that candidate, so create takes the
  `routingWalk.Chosen` branch, not the Required-single-candidate arm (an `else if` after it
  in `AgentTaskService.CreateAsync`). It stayed green under the scratch PC-3 demonstration
  below and witnesses no arm mutant. It is green at S1 and S2 (CP-1).
- V-3: these three tests are server witnesses for the documented Required-single-candidate
  refusals over one default-tier (Plan/Medium) row with a forbid-only Required pin.
  - `A_forbid_only_required_pin_over_one_held_default_row_is_409_model_disabled`:
    409 `model_disabled` naming `fable` and `does not satisfy the pin`.
  - `A_forbid_only_required_pin_that_forbids_the_one_default_row_is_409_routing_pin_forbidden`:
    409 `routing_pin_forbidden` starting `fable is forbidden for Plan by the human stage-wide routing pin`.
  - `A_forbid_only_required_pin_over_one_quota_low_default_row_is_409_subscription_quota_low`:
    409 `subscription_quota_low` with quota extension 3% on key `Codex`.

  Each expects no task row. They are characterisation tests, green at S1 and S2 (CP-1).
- V-4: `An_orchestrator_with_complexity_over_one_held_claude_row_is_409_model_disabled`
  (D-1 repair) is the ClaudeCode Orchestrator that does enter the arm: explicit Hard, one
  Plan/Hard ClaudeCode/Frontier row, the forbid-only Required pin (`grok-4.7`) and a hold on
  `fable`. The held candidate is skipped, so the arm runs; `AdmitsKind` passes, and the arm
  must still refuse with 409 `model_disabled` naming `fable` and `does not satisfy the pin`,
  with no task row. Green on the fixed code (CP-1); red under PC-3 (scratch demonstration in
  the repair evidence).
- R-1: the full affected integration classes stay green at S2:
  `RoutingPinCandidateCreateTests` (includes `Orchestrator_skips_non_Claude_pin_candidates`
  and the pinned single-candidate `model_disabled`), `ComplexityCreateTests`,
  `MatrixDefaultComplexityCreateTests`, `ComplexityRoutingWalkTests` (shared predicate)
  and `AgentTaskAgentKindTests` (Orchestrator kind refusals) (CP-2..CP-6).
- R-2: review the final diff by hand. Also run both receipt validators, the task-base
  evidence-diff validator, fresh-master merge-tree, and the clean-status and remote-SHA checks.

The brief excludes the whole Unit lane. That exclusion is an explicit bounded-class
exception to the Final profile and is disclosed, not discharged. The change touches
one create arm and one shared predicate; the named classes bound its effect. No live
acceptance, deployment or restart. Restart: server (on activation, ordinary land).

### Positive controls (pending SourceLanding Mutation)

| ID | Deliberate defect | Detecting filter | Expected failure |
|---|---|---|---|
| PC-1 | Remove the `AdmitsKind` refusal from the single-candidate arm | `/*/*/RequiredSingleCandidateRefusalTests/An_orchestrator_with_complexity_over_one_codex_row_is_409_routing_exhausted` | no exception; task created on Codex |
| PC-2 | Drop the Orchestrator clause from `ComplexityRoutingService.AdmitsKind` | `/*/*/RoutingPinCandidateCreateTests/Orchestrator_skips_non_Claude_pin_candidates` | Grok chosen, not ClaudeCode |
| PC-3 | Refuse every Orchestrator in the arm regardless of kind | `/*/*/RequiredSingleCandidateRefusalTests/An_orchestrator_with_complexity_over_one_held_claude_row_is_409_model_disabled` (V-4) | RoutingExhaustedException thrown instead of ModelDisabledException |
| PC-4 | Swallow `ModelDisabledException` in the arm | `/*/*/RequiredSingleCandidateRefusalTests/A_forbid_only_required_pin_over_one_held_default_row_is_409_model_disabled` | no exception |
| PC-5 | Skip `EnforceForbiddenAliases` in the arm | `/*/*/RequiredSingleCandidateRefusalTests/A_forbid_only_required_pin_that_forbids_the_one_default_row_is_409_routing_pin_forbidden` | no exception |
| PC-6 | Skip the quota gate in the arm | `/*/*/RequiredSingleCandidateRefusalTests/A_forbid_only_required_pin_over_one_quota_low_default_row_is_409_subscription_quota_low` | no exception |

Mutation owns the deliberate mutant, red/restore/green and missing-control cycles that discharge
these controls. Code task `4fc0b3a4` ran no mutations (Review d5e71c6e, D-2); Code task `7551b8d4` ran one scratch
PC-3 demonstration (D-1 repair evidence below) that discharges nothing.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1271/` | new-class | `/*/*/RequiredSingleCandidateRefusalTests/*` | V-1, V-2, V-3, V-4 | S1: 4 passed/1 failed (V-1); S2: 5 passed; D-1 repair: 6 passed | 6 | 12 | true | n/a |
| CP-2 | S1 | CP-1 | pin-candidates | `/*/*/RoutingPinCandidateCreateTests/*` | R-1 | 17 passed, 0 failed/skipped | 17 | 3 | true | n/a |
| CP-3 | S1 | CP-1 | complexity-create | `/*/*/ComplexityCreateTests/*` | R-1 | 14 passed, 0 failed/skipped | 14 | 3 | true | n/a |
| CP-4 | S1 | CP-1 | matrix-default | `/*/*/MatrixDefaultComplexityCreateTests/*` | R-1 | 34 passed, 0 failed/skipped | 34 | 3 | true | n/a |
| CP-5 | S1 | CP-1 | walk | `/*/*/ComplexityRoutingWalkTests/*` | R-1 | 8 passed, 0 failed/skipped | 8 | 2 | true | n/a |
| CP-6 | S1 | CP-1 | agent-kind | `/*/*/AgentTaskAgentKindTests/*` | R-1 | 26 passed, 0 failed/skipped | 26 | 3 | true | n/a |

Import this table to an ignored manifest and set `timeouts.rowMinutes: 40`. CP-1..CP-6 share
the S1 build group (Build column): run `--rows CP-1` at the S1 commit, then CP-1..CP-6 at the S2 commit. Each run uses the exact committed expected SHA,
`--total-timeout 150m`, `--serial` and no `--keep-outputs`, and waits to completion. Linux
builds use `UseAppHost=false`. Bootstrap the checkpoint tool once through
`scripts/build-slot.ps1` into `bin-c1271-tool/`. That tool build is the sole planned
unlisted build. Remove only this task's literal output roots.

### Cost

S1 takes about 15 minutes (build plus one class). S2 takes about 30 minutes. This is
within the 90-minute budget. No full-assembly run. Each proof selection runs at most
three normal times, with no repetition after green.

### Code evidence

The S1 red run used commit `099d53805dd00e4d692003fad8fbfa9e2e1d4328` (its diff from the task base touches the test class and this plan). Run `20261014-221749-fce9`:

```
CHECKPOINT CP-1 commit=099d53805dd00e4d692003fad8fbfa9e2e1d4328 build=ok filter=/*/*/RequiredSingleCandidateRefusalTests/* executed=5 passed=4 failed=1 skipped=0 trx=/work/worktrees/task-4fc0b3a4/.antiphon/checkpoints/20261014-221749-fce9/rows/CP-1/run.trx slot=granted waited=45s dirty=0 source=099d53805dd00e4d692003fad8fbfa9e2e1d4328 sourceState=clean buildSource=verified
FAILED Antiphon.Tests.Application.RequiredSingleCandidateRefusalTests.An_orchestrator_with_complexity_over_one_codex_row_is_409_routing_exhausted (CP-1) ShouldAssertException: Task `f.Create(new( "orchestrate it", Kind: AgentTaskKind.Orchestrator, Role: AgentTaskRole.Plan, Complexity: TaskComplexity.Hard, Workspace: WorkspaceMode.Shared))`     should throw Antiphon.Server.Application.Exceptions.RoutingExhaustedException     but did not -> rows/CP-1/failures.md
```

The S2 green run used commit `ea67a044aad9c0160a03fd84f293555df9929856` (fix and docs). Run `20261014-222544-9869`:

```
CHECKPOINT CP-1 commit=ea67a044aad9c0160a03fd84f293555df9929856 build=ok filter=/*/*/RequiredSingleCandidateRefusalTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-4fc0b3a4/.antiphon/checkpoints/20261014-222544-9869/rows/CP-1/run.trx slot=granted waited=15s dirty=0 source=ea67a044aad9c0160a03fd84f293555df9929856 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=ea67a044aad9c0160a03fd84f293555df9929856 build=reused filter=/*/*/RoutingPinCandidateCreateTests/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-4fc0b3a4/.antiphon/checkpoints/20261014-222544-9869/rows/CP-2/run.trx slot=granted waited=15s dirty=0 source=ea67a044aad9c0160a03fd84f293555df9929856 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=ea67a044aad9c0160a03fd84f293555df9929856 build=reused filter=/*/*/ComplexityCreateTests/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-4fc0b3a4/.antiphon/checkpoints/20261014-222544-9869/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=ea67a044aad9c0160a03fd84f293555df9929856 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=ea67a044aad9c0160a03fd84f293555df9929856 build=reused filter=/*/*/MatrixDefaultComplexityCreateTests/* executed=34 passed=34 failed=0 skipped=0 trx=/work/worktrees/task-4fc0b3a4/.antiphon/checkpoints/20261014-222544-9869/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=ea67a044aad9c0160a03fd84f293555df9929856 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=ea67a044aad9c0160a03fd84f293555df9929856 build=reused filter=/*/*/ComplexityRoutingWalkTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-4fc0b3a4/.antiphon/checkpoints/20261014-222544-9869/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=ea67a044aad9c0160a03fd84f293555df9929856 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=ea67a044aad9c0160a03fd84f293555df9929856 build=reused filter=/*/*/AgentTaskAgentKindTests/* executed=26 passed=26 failed=0 skipped=0 trx=/work/worktrees/task-4fc0b3a4/.antiphon/checkpoints/20261014-222544-9869/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=ea67a044aad9c0160a03fd84f293555df9929856 sourceState=clean buildSource=verified
```

Both validators accept the S2 green receipt for CP-1..CP-6 (exit 0, `CHECKPOINT SOURCE VALID rows=6`):
`scripts/validate-checkpoint-receipt.ps1` and the tool's `validate`. Both reject the S1 red
receipt with exit 2 (`cleanup_refused`, failed row). The fresh TRX method names match the
filters, and their counts match the Min column. The tool reported `unlisted: none`. The
one unlisted build Code task `4fc0b3a4` reports is the planned tool bootstrap (`build-slot.ps1`, label `c1271-tool`,
slot granted after 45s). The whole Unit lane was not run, per the brief, so that
disclosure stays open.

### D-1 repair evidence (Code task 7551b8d4)

The checkpoint tool ran CP-1..CP-6 (the six lines below) at commit `7a5e5a8be95974c2bf3a470e3ab2b0c264f815b4` (V-4 test, rebound PC-3, corrected docs). Run `20261014-230418-12cf` used `--serial` and `--total-timeout 150m`, with rowMinutes, totalMinutes and per-row timeoutMinutes 40/150/40 in the ignored manifest, and no `--keep-outputs`:

```
CHECKPOINT CP-1 commit=7a5e5a8be95974c2bf3a470e3ab2b0c264f815b4 build=ok filter=/*/*/RequiredSingleCandidateRefusalTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-7551b8d4/.antiphon/checkpoints/20261014-230418-12cf/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=7a5e5a8be95974c2bf3a470e3ab2b0c264f815b4 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=7a5e5a8be95974c2bf3a470e3ab2b0c264f815b4 build=reused filter=/*/*/RoutingPinCandidateCreateTests/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-7551b8d4/.antiphon/checkpoints/20261014-230418-12cf/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=7a5e5a8be95974c2bf3a470e3ab2b0c264f815b4 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=7a5e5a8be95974c2bf3a470e3ab2b0c264f815b4 build=reused filter=/*/*/ComplexityCreateTests/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-7551b8d4/.antiphon/checkpoints/20261014-230418-12cf/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=7a5e5a8be95974c2bf3a470e3ab2b0c264f815b4 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=7a5e5a8be95974c2bf3a470e3ab2b0c264f815b4 build=reused filter=/*/*/MatrixDefaultComplexityCreateTests/* executed=34 passed=34 failed=0 skipped=0 trx=/work/worktrees/task-7551b8d4/.antiphon/checkpoints/20261014-230418-12cf/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=7a5e5a8be95974c2bf3a470e3ab2b0c264f815b4 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=7a5e5a8be95974c2bf3a470e3ab2b0c264f815b4 build=reused filter=/*/*/ComplexityRoutingWalkTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-7551b8d4/.antiphon/checkpoints/20261014-230418-12cf/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=7a5e5a8be95974c2bf3a470e3ab2b0c264f815b4 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=7a5e5a8be95974c2bf3a470e3ab2b0c264f815b4 build=reused filter=/*/*/AgentTaskAgentKindTests/* executed=26 passed=26 failed=0 skipped=0 trx=/work/worktrees/task-7551b8d4/.antiphon/checkpoints/20261014-230418-12cf/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=7a5e5a8be95974c2bf3a470e3ab2b0c264f815b4 sourceState=clean buildSource=verified
```

The run reported `unlisted: none`, builds 1, verdict GREEN exit=0 and `outputs: deleted bin-c1271/`.
The CP-1 TRX names the six methods of the class (`testName` attributes), including
`An_orchestrator_with_complexity_over_one_held_claude_row_is_409_model_disabled`. Both validators,
`scripts/validate-checkpoint-receipt.ps1` and the tool's `validate`, accept the receipt (exit 0,
`CHECKPOINT SOURCE VALID source=7a5e5a8be95974c2bf3a470e3ab2b0c264f815b4 rows=6`).

Two unlisted runs went through `build-slot.ps1`:

- `c1271r-tool`: the tool bootstrap into `tools/Antiphon.Checkpoints/bin-c1271r-tool/`, since deleted.
- `c1271r-pc3-demo`: one scratch PC-3 demonstration required by the repair brief. It ran in a
  detached worktree at `7a5e5a8be` with the arm condition changed to
  `request.Kind == AgentTaskKind.Orchestrator || !AdmitsKind(...)`, built into `bin-c1271pc3/`, with
  the filter `/*/*/RequiredSingleCandidateRefusalTests/*`. Result: total 6, failed 1, succeeded 5. The one
  failure was V-4: it expected `ModelDisabledException` but `RoutingExhaustedException` was thrown. V-2 stayed green under the
  mutant, which confirms D-1. Both the mutant and the scratch worktree were removed, and nothing was committed.
  This demonstration does not discharge PC-3. PC-1..PC-6 stay pending for SourceLanding Mutation
  (Status column of the candidates file).

The whole Unit lane was not run, per the brief, so that disclosure stays open.
