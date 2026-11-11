# Why missed: run d88de419 g29 ChannelOutboundUnifiedCrash red

Debug task 415d0fa1. Failure: rc qualification run `d88de419` on `rc/2026-11-16` =
`6f8741d872d63507827014f972ccf71196722d4a`, chunk `antiphon-all-group-29`, one cell:
`ChannelOutboundUnifiedCrashTests.C519_Captured_death_needs_no_wake_signal(trailing)`,
`TaskCanceledException` from `UnifiedOutboundTransport.WaitForAsync` under `OwnedProbe.ReachAsync`
after 30 s. The Windows extract is
`docs/investigations/2026-11-10-run-d88de419-g29 ChannelOutboundUnifiedCrash red-windows-failure.md`
(task 62d5b3f9, `origin/feat/card-task-62d5b3f9` at `6b257e770`).

## Classification

**Test fixture timing defect, exposed by one child stall under load.** Not a regression in the cut
and not a Windows product defect.

- Nothing in the class, `UnifiedOutboundTransport.cs`, the probe project or the channel outbound
  services changed between `ac6d00022` (run 6591e378, class green in group 28) and `6f8741d87`.
  Master `d17d4bf35` carries the same bytes.
- The class passed 420 of 420 Windows cells in the 20 earlier rc runs (extract note). This is
  1 of 441.
- Linux server2, this branch's base `d17d4bf35`: the class passed 21/21 alone (17 m 57 s).

The defect is in the harness. The cut probe's parent waited a fixed 30 s from `Process.Start` for the
marker or an exit. The child's own deadline is also 30 s, but it starts only in
`UnifiedRecoveryProbe.RunAsync`, after runtime startup and config read. So the parent always gave up
first. A child stall could therefore only surface as a bare parent-side cancellation. The child's
stdout and stderr were discarded on that path, and the child logged to no provider. The first
occurrence was undiagnosable by construction. `CompleteAsync` (every `RecoverAsync`) had the same
inversion: a 30 s parent wait against the same 30 s child deadline.

What stalled the child in d88de419 is still unknown. The artefacts cannot show it (extract note,
"Deterministic or timing"). The fix makes the next occurrence say so. It does not hide it: a stall
still fails the cell.

## Fix (master, this branch)

| Commit | What |
|---|---|
| `4ee075ae98f4333736d19c85ab3ed56ba4736ab1` | Red witness `C519_Stalled_cut_probe_fails_with_its_own_evidence`, with a probe `StallPoint` seam that stalls at `capture-committed` without a marker. On the old harness it failed in 49 s with `TaskCanceledException`, which is the d88de419 shape. |
| `ecc05f510703e4a955180a05007950babedef8ba` | The child takes `DeadlineSeconds` from the parent. It writes phases (run-start, host-started, each cycle, the first crossing of each probe point, deadline-expired) and warnings to stderr. Lines carry ms since process start, plus exception types and SQL states only, never messages, because the config holds the DB password. The parent waits `ProbeDeadline + ProbeStartupAndExit` (30 s + 30 s) from process start in `ReachAsync` and `CompleteAsync`. On expiry it kills the child and reports elapsed time, the staged `.tmp` marker and both streams. Each reach and completion prints `c519-probe reach|complete <ms>` to test stdout, so Full-run TRX gives the p95 for this budget. |

Budget rationale (test-performance-standards R5). The child's 30 s deadline stays the bound on a
stall. It was not loosened, and no assertion changed. The parent's extra 30 s only covers child
runtime startup and orderly exit, so the child always ends first and its evidence reaches the
failure. Measured on server2 at `ecc05f510` (TRX stdout of the two-class run below): reach p50 9.3 s, p95 10.8 s,
max 13.0 s (n=42); completion p50 7.9 s, p95 9.9 s, max 12.3 s (n=204). The child deadline is 2.8x the
reach p95. The parent backstop is 60 s. EF model build alone is about 4 s of each child (phase log). Windows p95 comes from the next Full rc TRX
`c519-probe` lines. If the Windows p95 is above 15 s, the 30 s child deadline itself needs a
measured raise. That has not happened yet: the slowest Windows passing cells in d88de419 put reach
at about 5-10 s (extract note).

Native Windows proof is still owed. Run
`--treenode-filter "/*/*/ChannelOutboundUnifiedCrashTests/*"` on the desktop through
`scripts/build-slot.ps1` once d88de419 completes. If a cell stalls again, its failure now names
the child's last phase.

Verification on server2 Linux (branch `feat/card-task-415d0fa1`):

| Run | Source | Result |
|---|---|---|
| Class alone, before the fix | `d17d4bf35` | 21/21 passed, 17 m 57 s |
| Stall witness, red | `4ee075ae9` | failed in 49 s: `TaskCanceledException` (the d88de419 shape) |
| Stall witness, green | `ecc05f510` | passed in 49 s; the child exited at 30.6 s with its phase log, which ends `point capture-committed` |
| `ChannelOutboundUnifiedCrashTests` + `ChannelOutboundUnifiedTransportTests` | `ecc05f510` | 39/39 passed, 40 m 56 s |
| Guard union: TestLaneCategoryGuard, TestClassificationPolicy, TestClassificationGuard, SlowTestRegistryMerge, SlowTestTripwire, NativeLinuxPolicyWitness, ProcessSpawnLimit, DerivedImpactIndex, BuildOutputNesting | `ecc05f510` | 150/150 passed |
| `scripts/regenerate-derived-impact-index.ps1` | `ecc05f510` | index unchanged |

## Introducing change and the gates that approved it

- Introduced by `0c84bcdd4` ("test(channel): add unified hosted crash and recipient proof",
  2026-10-05), CARD-0519 slice S12g (unified outbound recovery). The 30 s `WaitForAsync` reach poll,
  the 30 s `CompleteAsync` wait and the probe's 30 s `RunAsync` deadline all arrived in that one
  commit. They have not changed since.
- Verification: S12g Code task `9e4b5c54` (report `d06b0984b`) ran CP-74 (12 preparation cells) and
  CP-75 (9 publication cells) at `cca0a1756` on server2 Linux. 21/21 passed. The rows were the whole
  class. CARD-0519 landed dormant through owner tasks `5724b53e`, `6ff18828`, `e739d9aa`, `170e6f0d`
  and `23e91309` (review-column triage 2026-10-08). The final S12e/f/g one-SHA requalification was
  never commissioned.

## Why the gates missed it

- **The class was in the selection.** It was not left out. CP-74/CP-75 ran every cell. A green
  run cannot show this defect, because it needs a child stall. A child stall needs load, and
  qualification Full runs on the desktop are where that load happens.
- **No row exercised the stall path.** No test made the child stall, so nobody saw that the
  parent's 30 s always expired before the child's 30 s. Nobody saw either that the timeout path
  threw away the child's streams. Review checked assertions on returned state. It did not check the
  failure mode of the harness waits.
- **The budgets were never measured (R5).** No phase timings were recorded, so no review could
  compare 30 s with a loaded p95, or see that two equal 30 s bounds start at different instants.

## Cheapest pre-land check (impacted-classes row)

A change to `tests/Antiphon.Tests/TestHelpers/UnifiedOutboundTransport.cs` or
`tests/Antiphon.ChannelOutbound.Probe/**` should run the stall witness. It is one cell, about 50 s
on server2, with Docker for Redpanda and app-db.

| Trigger paths | Row |
|---|---|
| `tests/Antiphon.Tests/TestHelpers/UnifiedOutboundTransport.cs`, `tests/Antiphon.ChannelOutbound.Probe/**` | `/*/*/ChannelOutboundUnifiedCrashTests/C519_Stalled_cut_probe_fails_with_its_own_evidence` |

The full `main.outbound-broker` group (`tests/test-selection-policy.json`) already lists the class
as a member. The row above is the cheap single-cell guard on the harness's failure mode.
