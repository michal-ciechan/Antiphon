# CARD-1271 Required single-candidate Orchestrator edge

Code task / landing owner: `4fc0b3a4-4824-4feb-8a8a-d790c64d9581`.
Task base (merge-base with fetched master): `9cbe00bd52dd77367a34244c9cb9ea7d24539add`.

## Finding

A Required stage pin without candidates (forbid-only) contributes no pin slots, so
`RoutingCandidates.Compose` returns the matrix row unchanged. A one-candidate row is
not walked (`Walked: false`). With no chosen candidate, `AgentTaskService.CreateAsync`
enters the Required-single-candidate arm ("today's 409s, never Blocked"). That arm
re-checks the stage forbid list, quota and model holds. It never re-checked the walk's
kind filter. So an Orchestrator with explicit `-Complexity` and one Codex row skipped
the row as `not a delegate kind`, then was created on Codex. Elsewhere an Orchestrator
is ClaudeCode only: an explicit kind is 422, a policy kind clamps, a walk skips, and
reroute refuses (CARD-0084).

## Decision

The arm refuses before its other checks when the walk's kind filter rejects the single
candidate. It throws 409 `routing_exhausted` with the `complexityRouting` extension.
`refuseIfExhausted` does not change this: the arm never inserts Blocked, and a kind
mismatch is not cleared by a hold or reset. The walk and the arm share one predicate,
`ComplexityRoutingService.AdmitsKind`. The message tells the caller that a human
must change the row or pin, and not to pick a kind.

S1 commits the five tests and this plan. S2 makes the production change and adds the
docs paragraph in `docs/orchestration-loop.md`.

## Verification design

- V-1: `RequiredSingleCandidateRefusalTests.An_orchestrator_with_complexity_over_one_codex_row_is_409_routing_exhausted`
  covers an Orchestrator, explicit Hard, a Required pin forbidding only `grok-4.7`
  and one Plan/Hard Codex/Frontier row. It expects 409 `routing_exhausted` with message prefix
  `routing exhausted: Plan/Hard chain — gpt-6-astra not a delegate kind` and
  `An orchestrator runs only on ClaudeCode`. The routing DTO must be not walked, with one
  skipped Codex candidate, reason `not a delegate kind`, and no task row.
  Red at S1 (created on Codex), green at S2 (CP-1).
- V-2: `An_orchestrator_with_complexity_over_one_claude_row_is_created_on_it` is the
  control. The same pin over one ClaudeCode/Frontier row creates a Queued Orchestrator
  on ClaudeCode/Frontier. It is green at S1 and S2 (CP-1).
- V-3: these three tests are server witnesses for the documented Required-single-candidate
  refusals over one default-tier (Plan/Medium) row with a forbid-only Required pin.
  - `A_forbid_only_required_pin_over_one_held_default_row_is_409_model_disabled`:
    409 `model_disabled` naming `fable` and `does not satisfy the pin`.
  - `A_forbid_only_required_pin_that_forbids_the_one_default_row_is_409_routing_pin_forbidden`:
    409 `routing_pin_forbidden` starting `fable is forbidden for Plan by the human stage-wide routing pin`.
  - `A_forbid_only_required_pin_over_one_quota_low_default_row_is_409_subscription_quota_low`:
    409 `subscription_quota_low` with quota extension 3% on key `Codex`.

  Each expects no task row. They are characterisation tests, green at S1 and S2 (CP-1).
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
| PC-3 | Refuse every Orchestrator in the arm regardless of kind | `/*/*/RequiredSingleCandidateRefusalTests/An_orchestrator_with_complexity_over_one_claude_row_is_created_on_it` | RoutingExhaustedException thrown |
| PC-4 | Swallow `ModelDisabledException` in the arm | `/*/*/RequiredSingleCandidateRefusalTests/A_forbid_only_required_pin_over_one_held_default_row_is_409_model_disabled` | no exception |
| PC-5 | Skip `EnforceForbiddenAliases` in the arm | `/*/*/RequiredSingleCandidateRefusalTests/A_forbid_only_required_pin_that_forbids_the_one_default_row_is_409_routing_pin_forbidden` | no exception |
| PC-6 | Skip the quota gate in the arm | `/*/*/RequiredSingleCandidateRefusalTests/A_forbid_only_required_pin_over_one_quota_low_default_row_is_409_subscription_quota_low` | no exception |

Mutation owns all deliberate mutant, red/restore/green and missing-control cycles; Code runs none.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1271/` | new-class | `/*/*/RequiredSingleCandidateRefusalTests/*` | V-1, V-2, V-3 | S1: 4 passed/1 failed (V-1); S2: 5 passed | 5 | 12 | true | n/a |
| CP-2 | S2 | CP-1 | pin-candidates | `/*/*/RoutingPinCandidateCreateTests/*` | R-1 | 17 passed, 0 failed/skipped | 17 | 3 | true | n/a |
| CP-3 | S2 | CP-1 | complexity-create | `/*/*/ComplexityCreateTests/*` | R-1 | 14 passed, 0 failed/skipped | 14 | 3 | true | n/a |
| CP-4 | S2 | CP-1 | matrix-default | `/*/*/MatrixDefaultComplexityCreateTests/*` | R-1 | 34 passed, 0 failed/skipped | 34 | 3 | true | n/a |
| CP-5 | S2 | CP-1 | walk | `/*/*/ComplexityRoutingWalkTests/*` | R-1 | 8 passed, 0 failed/skipped | 8 | 2 | true | n/a |
| CP-6 | S2 | CP-1 | agent-kind | `/*/*/AgentTaskAgentKindTests/*` | R-1 | 26 passed, 0 failed/skipped | 26 | 3 | true | n/a |

Import this table to an ignored manifest and set `timeouts.rowMinutes: 40`. Run CP-1
at S1, then every row at S2. Each run uses the exact committed expected SHA,
`--total-timeout 150m`, `--serial` and no `--keep-outputs`, and waits to completion. Linux
builds use `UseAppHost=false`. Bootstrap the checkpoint tool once through
`scripts/build-slot.ps1` into `bin-c1271-tool/`. That tool build is the sole planned
unlisted build. Remove only this task's literal output roots.

### Cost

S1 takes about 15 minutes (build plus one class). S2 takes about 30 minutes. This is
within the 90-minute budget. No full-assembly run. Each proof selection runs at most
three normal times, with no repetition after green.
