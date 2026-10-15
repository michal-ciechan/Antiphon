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
- S2 (fix): CP-1 (run `20261015-003609-6511`) failed each row, for three different reasons
  (evidence file `docs/superpowers/evidence/2026-10-15-card-1273-code.md`). The Grok-on-Herdr
  rows give the fake the captured Grok 1.0.41 dashboard (`GrokStartupScreen`, as CARD-1252) and
  run on Linux. The Linux guard is narrowed to the observed reasons: PtyHost rows (native prompt
  absent from the fake CLI JSONL at the 90 s deadline) and the Claude-on-Herdr rows (no UserPrompt
  after the delivery baseline). No assertion, deadline or other fixture change.

## Verification design

- V-1: the Grok-on-Herdr rows of the method pass on Linux with every existing assertion unchanged
  (CP-1 red without the screen, CP-2 green with it).
- R-1: the rest of the class keeps its outcome (CP-3, full class).

PCs pending for SourceLanding Mutation (none run here):

- PC-1: drop `GrokStartupScreen` from the Standing_history_recovery fake; both Grok-on-Herdr rows
  must go red with the `FakeHerdrServer never saw pane.send_text` timeout.
- PC-2: lift the narrowed Linux guards; the PtyHost and Claude-on-Herdr rows must go red with the
  reasons the skip texts name.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1273-red/` | probe | `/*/*/HerdrAlwaysOnChannelParityTests/Standing_history_recovery_preserves_native_identity_and_queued_reply*` | V-1 red, per-row reasons | 8 executed, 0 skipped; the Grok-on-Herdr rows red with the not-ready launch failure; the other six rows recorded as observed | 8 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1273-green/` | fixed | `/*/*/HerdrAlwaysOnChannelParityTests/Standing_history_recovery_preserves_native_identity_and_queued_reply*` | V-1 green | the two Grok-on-Herdr rows passed, 0 failed; the six guarded rows skipped on Linux | 2 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S2 | CP-2 | class | `/*/*/HerdrAlwaysOnChannelParityTests/*` | R-1 | 0 failed; skips are the inherited CARD-0865/0878 Linux guards minus the two un-skipped rows | 12 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Import this table to an ignored manifest and set `timeouts.rowMinutes: 40`. Pass the committed
HEAD as `--expected-source-sha`, use `--total-timeout 150m` without `--keep-outputs`. Linux
builds use `UseAppHost=false`. The checkpoint tool is bootstrapped once through
`scripts/build-slot.ps1` into `bin-c1273-tool/` (disclosed unlisted build).
