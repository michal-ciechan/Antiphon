# CARD-1273 Standing_history_recovery Grok-on-Herdr cases

Task fecd81fb (Code, investigate first). Follow-up of CARD-1252 (plan
`docs/superpowers/plans/2026-10-14-card-1252-herdr-linux-parity-plan.md`). The method
`HerdrAlwaysOnChannelParityTests.Standing_history_recovery_preserves_native_identity_and_queued_reply`
is skipped on Linux for its eight argument rows by one CARD-0865 guard. Its two Grok-on-Herdr rows
build `FakeHerdrServer` without the CARD-1252 opt-in `GrokStartupScreen`, so their Grok launches
probably cannot become ready (`GrokStartupScreen.Classify`) whatever CARD-0865 says.

## Slices

- S1 (probe): the Linux guard is removed so every argument row runs on Linux and each skip reason
  is checked against an observed outcome. No assertion, deadline or fixture change.
- S2 (fix): decided from CP-1 and recorded in the evidence file before it is committed.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1273-red/` | probe | `/*/*/HerdrAlwaysOnChannelParityTests/Standing_history_recovery_preserves_native_identity_and_queued_reply*` | V-1 red, per-row reasons | 8 executed, 0 skipped; the Grok-on-Herdr rows red with the not-ready launch failure; the other six rows recorded as observed | 8 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Import this table to an ignored manifest and set `timeouts.rowMinutes: 40`. Pass the committed
HEAD as `--expected-source-sha`, use `--total-timeout 150m` without `--keep-outputs`. Linux
builds use `UseAppHost=false`. The checkpoint tool is bootstrapped once through
`scripts/build-slot.ps1` into `bin-c1273-tool/` (disclosed unlisted build).
