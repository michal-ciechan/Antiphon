# Probe redaction test budget floor (operator instruction, 2026-10-21)

Operator instruction: raise the time allowance of
`RunnerProcessProbeTests.Probe_redacts_many_same_line_assignments_near_the_output_limit_within_budget`
to 2.5 s. It flaked at 1886 ms against a 1828 ms budget on a loaded Linux runner with no
redaction change. The budget is derived (best line-delimited time x 1.35), so the change floors
the effective allowance at 2500 ms with `Math.Max`. Test-only; no production change; no other
test touched. Investigation of the test is CARD-1418 and is not part of this change.

## Verification design

V-1: the single test method passes with the floored budget. A red witness is not meaningful for a
widened budget (the change only admits slower runs), so no red row is planned.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | all | Antiphon.Tests -> bin-c1418/ | probe-budget | `/*/Antiphon.Tests.AgentTui/RunnerProcessProbeTests/Probe_redacts_many_same_line_assignments_near_the_output_limit_within_budget` | V-1 | all listed, 0 failed | 1 | 6 |

### Cost

Ordinary floor is 6 minutes.
