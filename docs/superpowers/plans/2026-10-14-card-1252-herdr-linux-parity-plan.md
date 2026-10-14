# CARD-1252 Herdr always-on parity tests on Linux

Task 5d610f1f (Code, investigate first). Two `HerdrAlwaysOnChannelParityTests` methods time out
on Linux with "never reached a Running persistent session":
`Herdr_launch_definition_starts_adopts_and_exits(Grok)` and
`Named_AlwaysOn_herdr_agent_holds_after_three_failures_keeps_its_timeout_shell_and_resumes_only_on_explicit_retry`.

## Slices

- S1: the timeout in `WaitForPersistentSessionAsync` names the agent status, persistent id,
  live-session status and every session row (status, failure reason, Grok rules state/failure,
  Herdr supervision failure). Diagnostic only; no assertion, deadline or selection changes.
- S2: fixture fix. CP-1 showed both sessions `Failed: Agent process did not become ready.`
  (Grok rules state None, Herdr supervision NonQualifying). Since f23797793 (2026-09-30; CARD-0861
  qualified sizes 2026-10-12) `RunnerGrokAdapter.WaitForReadyAsync` accepts only a captured Grok
  composer dashboard (`GrokStartupScreen.Classify`). For a Grok launch `FakeHerdrServer` paints
  `agent:grok env=...`, which is never ready on any OS; a Herdr snapshot is the pane text verbatim.
  Real Herdr shows the real Grok screen, so the FIXTURE is wrong, not the product, and no
  environment guard applies (no herdr binary is involved; the fake is in-process). The fake gains
  opt-in `GrokStartupScreen`, applied exactly like the existing CARD-0574 `CodexStartupScreen`,
  and the two tests pass the captured Grok 1.0.41 dashboard
  (`Agents/Fixtures/card1004/linux-startup-frames.json`). No assertion, deadline or skip changes.

## Verification design

- V-1: the two named methods reach a Running persistent session on Linux and pass every
  existing assertion unchanged (CP-1 red at base, CP-2 green).
- R-1: the rest of the class keeps its outcome (CP-3 full class: 10 passed, 14 inherited
  CARD-0865/CARD-0878 Linux skips, 0 failed).
- R-2: `FakeHerdrServer.cs` is also compiled by its owning project; that project builds and the
  fake's own listener tests stay green (CP-4).

PCs pending for SourceLanding Mutation (none run here):

- PC-1: drop `GrokStartupScreen` from `Herdr_launch_definition_starts_adopts_and_exits`; the Grok
  arm must go red with the not-ready timeout.
- PC-2: drop `GrokStartupScreen` from the named hold test; it must go red the same way.
- PC-3: make `StartupScreenFor` ignore Grok; both Grok cases in CP-2 go red, Codex stays green.
- PC-4: drop the post-echo startup-screen override in `PaneSendTextJson`; the Grok and Codex
  launch arms (echo on) must go red.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1252-red/` | repro | `/*/*/HerdrAlwaysOnChannelParityTests/(Herdr_launch_definition_starts_adopts_and_exits*)\|(Named_AlwaysOn_herdr_agent_holds_after_three_failures*)` | V-1 red | 4 executed; exactly 2 red before the fix (Grok launch arm, named hold), 0 skipped | 4 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1252-green/` | fixed | `/*/*/HerdrAlwaysOnChannelParityTests/(Herdr_launch_definition_starts_adopts_and_exits*)\|(Named_AlwaysOn_herdr_agent_holds_after_three_failures*)` | V-1 green | all 4 passed, 0 failed/skipped | 4 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S2 | CP-2 | class | `/*/*/HerdrAlwaysOnChannelParityTests/*` | R-1 | 24 discovered: 10 passed, 0 failed, 14 skipped (inherited CARD-0865/0878 Linux guards) | 10 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1252-runner/` | fake-owner | `/*/*/FakeHerdrServerListenerTests/*` | R-2 | all 17 passed, 0 failed/skipped | 17 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Import this table to an ignored manifest and set `timeouts.rowMinutes: 40`. Pass the committed
HEAD as `--expected-source-sha`, use `--total-timeout 150m` without `--keep-outputs`. Linux
builds use `UseAppHost=false`. The checkpoint tool is bootstrapped once through
`scripts/build-slot.ps1` into `bin-c1252-tool/` (disclosed unlisted build).

### Cost

CP-1 is one Antiphon.Tests build plus four integration methods (measured 9m34s, 6m45s of it build).
CP-2 to CP-4 run together after S2: two builds, about 20 minutes plus slot waits. CP-3 carries
14 inherited skips, so its receipt cannot be a zero-skip clean certificate; CP-2 is the clean
V-1 receipt and CP-1 is retained red evidence at d51efd2672c4ae994d303774ccb7b5d2cc483562.
No whole-Unit run (brief).
