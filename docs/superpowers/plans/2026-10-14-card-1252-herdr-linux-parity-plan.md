# CARD-1252 Herdr always-on parity tests on Linux

Task 5d610f1f (Code, investigate first). Two `HerdrAlwaysOnChannelParityTests` methods time out
on Linux with "never reached a Running persistent session":
`Herdr_launch_definition_starts_adopts_and_exits(Grok)` and
`Named_AlwaysOn_herdr_agent_holds_after_three_failures_keeps_its_timeout_shell_and_resumes_only_on_explicit_retry`.

## Slices

- S1: the timeout in `WaitForPersistentSessionAsync` names the agent status, persistent id,
  live-session status and every session row (status, failure reason, Grok rules state/failure,
  Herdr supervision failure). Diagnostic only; no assertion, deadline or selection changes.
- S2: fix decided from the CP-1 evidence (fixture or product), red-first.

## Verification design

- V-1: the two named methods reach a Running persistent session on Linux and pass every
  existing assertion unchanged (CP-1 red at base, CP-2 green).
- R-1: the rest of the class keeps its outcome (CP-2 full class).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1252-red/` | repro | `/*/*/HerdrAlwaysOnChannelParityTests/(Herdr_launch_definition_starts_adopts_and_exits*)\|(Named_AlwaysOn_herdr_agent_holds_after_three_failures*)` | V-1 red | 4 executed; exactly 2 red before the fix (Grok launch arm, named hold), 0 skipped | 4 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Import this table to an ignored manifest and set `timeouts.rowMinutes: 40`. Pass the committed
HEAD as `--expected-source-sha`, use `--total-timeout 150m` without `--keep-outputs`. Linux
builds use `UseAppHost=false`. The checkpoint tool is bootstrapped once through
`scripts/build-slot.ps1` into `bin-c1252-tool/` (disclosed unlisted build).

### Cost

CP-1 is one Antiphon.Tests build plus four integration methods, about 6 minutes plus slot waits.
No whole-Unit run (brief).
