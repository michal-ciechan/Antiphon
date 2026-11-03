# CARD-1617 plan: bound the Linux custody kill repeated per tick

Date: 2026-11-03. Task 4b2ef72c (Plan). Base: `bcfe77b3d685301030ed1a84bc4ccfdb7a6190a6`
(`origin/master`; it contains the investigation base `7b920d84a` and CARD-1653 S2
`cea4ebbef`/`d1bbbfdbc`). Investigation: `docs/investigations/2026-11-03-card-1617-custody-reseal-per-tick.md`
on `feat/card-task-0eeb3129` @ `a5ce15c96`. No commit since `7b920d84a` touches
`PtyAgentRunner.cs`, `IPtyCustodyContainment.cs`, `src/Antiphon.PtyHost/`, the test class or
`SessionRunnerRuntime.cs` (checked with `git log 7b920d84a..HEAD -- <paths>`: empty).

## Outcome

Bound the termination in the host, inside the Linux (`_containment`) branch of
`PtyAgentRunner.SealAndObserveCustodyAsync`:

- after a real attempt whose outcome `Succeeded`, `Terminate` is never issued again by that runner;
- after failed attempt k, the next attempt waits at least `min(2^(k-1) s, 60 s)`, measured from the
  end of attempt k on an injectable monotonic clock;
- every `seal:true` read still re-reads accounting (`ReadActive`) and can still drain and receipt;
- `_linuxTermination` changes only on a real attempt.

The runner, the protocol, the seal flag formula (CARD-1614 plan line 80), the "explicit seal only"
rule and the Windows job-object branch are unchanged.

## Decisions

- **D-1. Host-side capped backoff, not one attempt per terminal cleanup.** Chosen because:
  - It is local to the one line that issues the kill. It bounds every caller: the 1 s runner
    observer, ordinary `GET .../custody` reads, `ReleaseFailedLinuxAsync`, and any future caller.
  - It needs no protocol field, no host/runner version negotiation and no change to
    `SessionRunnerRuntime.cs`. That file is where CARD-1653 S2 landed and CARD-1614's carried seal
    lives.
  - It keeps the retry after a transient helper refusal (exit 3 `NamespaceUnavailable`,
    `CustodyFreezerMissing`, ...), which the one-attempt option loses until the next orchestrator
    cleanup.

  **Rejected: one attempt per terminal cleanup.** `CustodyRequestMessage(binding, seal)` has one
  flag, and the runner deliberately turns a persisted `runner-seal.json` into `seal:true`. Telling an
  explicit cleanup apart from the carried seal needs a new message field, a runner change at
  `CollectCustodyAsync` (`SessionRunnerRuntime.cs:4906-4916`) and mixed-version handling. It also
  makes the retry cadence depend on the orchestrator's CleanupVerification retry.
- **D-2. Rejected: bound in the runner observer** (stop sending `seal:true` after the first). It
  contradicts CARD-1614 plan line 80 / P-3 (carried release intent). Ordinary reads would stay
  unbounded, and any other caller of the host would bypass the bound.
- **D-3. Rejected: terminate once ever.** A fast refusal would leave the tree Draining forever with
  no retry.
- **D-4. No retry after a success, even if the tree is populated again.** The helper exits 0 only
  after its own drain read found the tree empty. A tree that is populated after that is not a
  transient refusal, and re-killing it every tick is exactly the unbounded loop. It stays
  `Draining "descendants_running"`. The runner's existing `linux-unresolved.json` record and its
  30-minute warning surface it. A new explicit cleanup does **not** reset the schedule: that needs
  the D-1 protocol field, and the 60 s cap already bounds how stale a retry can be.
- **D-5. Clock: `TimeProvider`, monotonic (`GetTimestamp`/`GetElapsedTime`), default
  `TimeProvider.System`.** This matches `ProcessStopIntentStore` (`_clock`) and
  `GrokStartupReadiness` in the same layers. Wall-clock `DateTime.UtcNow` is rejected: it can jump
  and cannot be tested.
  - The seam is a public `PtyAgentRunner.UseCustodyClockForTests(TimeProvider)`. It has the same
    "fixed once the runner has started" guard as `UseContainmentForTests`, because Agents.Pty
    internals are not visible to `Antiphon.PtyHost.Tests`.
  - `HostSession` gets an `internal UseCustodyClockForTests` pass-through.
- **D-6. The backoff is measured from the end of the attempt.** A 45 s helper is never followed at
  once by another. The shift is computed with integers: `k >= 7 ? 60 : 1 << (k-1)` seconds.
- **D-7. An attempt is any entry into the existing termination block.** That includes a recorder
  that never invoked `send`, or a `Terminate` that threw: `ProcessStopIntentStore.Send` swallows
  the throw and leaves `termination` null, giving `(false, 0)`. The direct (no recorder) branch
  lets the exception escape. The schedule update sits in a `finally`, so all of these count as a
  **failed** attempt. Only `Succeeded == true` counts as success.
- **D-8. Four witnesses, not three.** The fourth (W4) covers D-7's null/throw path, which a bound
  keyed on a non-null outcome would miss. It costs about 1 s.
- **D-9. The Windows lane is not run by Code.** The modern (job-object) branch is byte-unchanged,
  and the desktop is reserved for the rc Windows qualification. The Windows pin is a Review diff
  check plus owed Windows rows (see Platform).
- **D-10. Documentation.** S2 adds one sentence to the CARD-1614 bullet in
  `docs/session-runtime-invariants.md` (line ~1125): the kill helper is issued at most once after a
  success, and after failures on a 1 s doubling backoff capped at 60 s; every explicit read
  re-reads accounting (CARD-1617).

## Ground truth (card / brief vs code at `bcfe77b3d`)

| Card or brief says | Code does |
|---|---|
| A "re-seal" repeats every tick | Seal records are write-once (`RunnerCustodyLedger.Seal` `prior ?? new`, `HostCustodyJournal.RecordSeal` `_seal ??=`, `_custodySealed` gate at `PtyAgentRunner.cs:582-588`). What repeats is `containment.Terminate()` at `PtyAgentRunner.cs:597-606` (`sudo -n antiphon-custody-kill <container>`) |
| Owner file: `SessionRunnerRuntime.cs` observer loop :4586-4604 | :4586-4604 is the ProcessVanished exit publication. The observer is `ObserveCustodyThenShutdownAsync` (:4941-4972, `Task.Delay(1 s)`), and the seal flag is computed in `CollectCustodyAsync` (:4906-4916). The fix leaves the runner unchanged |
| Owner: "PtyHost HostSession seal path" | `HostSession.GetCustodyAsync` (:375-396) -> `HostCustodyJournal.ObserveAsync` (:116-144) -> `ObserveLinuxExplicitAsync` (:238-262) or `ReleaseFailedLinuxAsync` -> `PtyAgentRunner.SealAndObserveCustodyAsync` (Agents.Pty assembly). The kill is in the runner class, not HostSession |
| Reviewer: 4 helper calls in 3.5 s | Investigation scratch witness: 10 `seal:true` reads -> 10 `Terminate()` in 39 ms |
| Witness "counts kill-helper invocations over N ticks" | One tick reaches the host as one `seal:true` `GetCustodyAsync`. The host-level `FakeContainment.Terminations` over N reads is the count, and the runner observer itself needs no witness (investigation; `RunnerCustodyReleaseTests` C1614 rows guard I1) |
| Investigation: "no fsync per tick" | **Correction.** On Linux, `LaunchAsync` sets `StopIntents` (`HostSession.cs:131`), so every attempt goes through `RecordStop` -> `ProcessStopIntentStore.Attempt`. That writes a **new** `stop-intents/<session>-<ticks>-<requestId>.json` per attempt, with `WriteThrough` and a directory fsync (`ProcessStopIntentStore.cs:175-204`). Today that is one new durable file per tick. The bound cuts it to the attempt rate (<= 1/min once capped) |
| `PtyAgentRunner` has a clock | It has none. `ProcessStopIntentStore` and `GrokStartupReadiness` use `TimeProvider` |
| Host 5 s `CancelAfter` bounds a read | The helper wait is synchronous (up to 45 s, `IPtyCustodyContainment.cs:202`) and holds `_writeGate` and `_admission`. Unchanged here: the bound cuts how often this happens, not how long one attempt lasts |
| Linux custody runs somewhere | It does not. `LinuxCustodyQualified` is false everywhere, and the dev DB has 0 `linux-cgroup` executions. The defect is latent, and this card does not advertise Linux custody |

## Invariants

- **I1 (unchanged).** `Terminate` is reachable only from a `seal:true` request. Root exit, the
  producer seal (`ObserveLinuxAccountingAsync(fence:true)`), ordinary reads and elapsed time never
  terminate. A prior seal record alone never authorizes the dead-host recovery kill (runner
  `explicitSeal` gate at `SessionRunnerRuntime.cs:4899-4903`).
- **I2 (new).** Per runner instance (one host session, one execution):
  - after a real attempt whose outcome `Succeeded`, no further `Terminate`;
  - after failed attempt k (D-7), the next attempt is issued only when the elapsed time since the
    end of attempt k is >= `min(2^(k-1) s, 60 s)`.
- **I3.** Every `seal:true` read still calls `ReadActive()`. A populated tree returns Draining. A
  zero tree still drains output and can receipt. A skipped attempt never fabricates a zero, never
  writes a receipt and never writes a release disposition by itself.
- **I4.** `_linuxTermination` (and therefore the receipt's `TerminationSucceeded`/`TerminationError`
  via `CustodyTermination`) holds the outcome of the last real attempt. A skipped read leaves it
  untouched.
- **I5.** The Windows `windows-job-v1` branch (`ModernConPtyConnection` path after the
  `_containment` block) and `WindowsJobContainment` are byte-unchanged.
- **I6.** The bound state is read and written only under `_writeGate`, which already serializes
  `SealAndObserveCustodyAsync`. No new lock.

## Slices

### S1: red-first witnesses and the inert seam (one commit, pushed before S2)

- `src/Antiphon.Agents.Pty/PtyAgentRunner.cs`:
  - add `private TimeProvider _custodyClock = TimeProvider.System;`;
  - add `public void UseCustodyClockForTests(TimeProvider clock)`, with the same guard as
    `UseContainmentForTests`;
  - **nothing reads the clock yet**.
- `src/Antiphon.PtyHost/HostSession.cs`: `internal void UseCustodyClockForTests(TimeProvider clock) => _runner.UseCustodyClockForTests(clock);` beside `UseContainmentForTests`.
- `tests/Antiphon.PtyHost.Tests/LinuxCustodyReleaseHostTests.cs`:
  - **`FakeContainment` gains three knobs, defaults preserving today's behaviour:**
    - `bool KeepPids` (default false: `Terminate` clears `Pids` as now);
    - `PtyTerminationObservation Result` (default `new(true, 0)`);
    - `Exception? Throw` (default null; thrown after `Terminations++`).
  - **A private `ManualClock : TimeProvider`:**
    - `TimestampFrequency => TimeSpan.TicksPerSecond`;
    - `GetTimestamp()` returns an `Interlocked`-read tick field;
    - `Advance(TimeSpan)`.
  - **A private helper `ExitedWithSurvivorAsync(FakeContainment, ManualClock)`.** It does the
    root-exit setup from `C1614_Root_exit_with_live_descendants_does_not_terminate`:
    - inbox backend, Linux cgroup binding, `LingerTtl` 24 h, the fake pid `424242`;
    - hold-until launch, release, wait `Status == Exited` (10 s loop as today);
    - write `runner-seal.json` like the investigation;
    - return host, store, binding and child pid.

    Each test keeps the class's `try/finally { KillChild; Finish }` shape.
  - **Four tests**, each starting with `if (!OperatingSystem.IsLinux()) throw new SkipTestException(...)`
    like the class's existing three. No `NativeLinuxOnly` row is needed: the class is not in
    `NativeLinuxWindowsBaseline.json` and uses the skip idiom. The tests are W1-W4 under Verification
    design.

The expected S1 outcome is W1, W2 and W4 red on their `Terminations` assertion (CP-1), and W3 and
the three C1614 rows green (CP-2).

### S2: the bound (one commit) and the doc sentence

- `src/Antiphon.Agents.Pty/PtyAgentRunner.cs`, Linux branch only:
  - fields `_terminationSucceeded` (bool), `_terminationFailures` (int), `_lastTerminationEnd` (long);
  - `private bool TerminationDue()` per I2/D-6;
  - wrap the existing `if (StopSignal is { } stop) {...} else _linuxTermination = containment.Terminate();`
    block in `if (TerminationDue()) { try { <unchanged block> } finally { <update schedule from this attempt's outcome per D-7>; _lastTerminationEnd = _custodyClock.GetTimestamp(); } }`.
    "This attempt's outcome" is a local captured inside the try (null when the block threw), never
    the previous `_linuxTermination`;
  - `ReadActive()` and everything after it stay unchanged and unconditional;
  - update the method's doc comment: explicit Linux release terminates at most once after success
    and otherwise on a capped backoff.
- `docs/session-runtime-invariants.md`: D-10 sentence.
- No other file. Review checks by diff that every changed line in `PtyAgentRunner.cs` is a new
  field, the seam, `TerminationDue`, or inside the `_containment is { } containment` block (I5).

## Verification design

The witnesses are portable Linux tests over the real `HostSession`/`HostCustodyJournal`/`PtyAgentRunner`
with the class's `FakeContainment` (no sudo, no cgroup, no helper). A "read" is
`await host.Session.GetCustodyAsync(binding, seal: true, CancellationToken.None)`.

| Test | Setup | Assertions | Red today because | Guards |
|---|---|---|---|---|
| **W1** `C1617_Explicit_seal_reads_terminate_once_after_success` | `KeepPids=true`, `Result=(true,0)` | 10 reads, with `clock.Advance(10 min)` between reads. Every read is `Draining`/`descendants_running` with a null `Receipt`. Afterwards `Terminations == 1`, and `producer-receipt.json` and `linux-release-disposition.json` are both absent | 10 terminations | I2 success arm; elapsed time never re-arms after success |
| **W2** `C1617_Failed_termination_retries_on_capped_backoff` | `KeepPids=true`, `Result=(false,3)` | 5 reads at t=0 give 1 termination. Then for k = 1..8, with `w = min(2^(k-1),60)` s: advance `w - 1 ms`, read, count unchanged; advance `1 ms`, read, count +1. That runs through the cap twice, at 64 s -> 60 s and again at 60 s. Every read is Draining. Last leg: `Pids.Clear()` with no clock advance, read gives `Exited`, and the receipt has `ActiveProcesses 0`, `TerminationSucceeded false`, `TerminationError 3` | 5 terminations on the first leg | I2 failure arm, exact boundary, cap; I3 (skipped read still finalizes); I4 (receipt from the last real attempt) |
| **W3** `C1617_Bounded_release_still_finalizes` | `KeepPids=true`, `Result=(true,0)` | Read 1 is Draining. Then `Pids.Clear()`, and read 2 gives `Exited`, `producer-receipt.json` exists, and the receipt has `TerminationSucceeded true`, `TerminationError 0`. No `Terminations` assertion | Green today (guard) | I3 and I4 for the success arm. Red under a bound that skips accounting or overwrites the outcome on a skipped read |
| **W4** `C1617_Throwing_termination_counts_as_failed_attempt` | `KeepPids=true`, `Throw=new IOException("c1617")` | Read 1 is Draining, because the host recorder swallows the throw (D-7): `Terminations == 1`. 3 reads at t=0 leave it at 1. Advance 1 s, read: 2 | 4 on the second leg | D-7; a bound keyed on a non-null outcome |

Positive controls (Mutation stage, method-scoped, one mutant at a time, on S2's tip):

| PC | Mutant in `PtyAgentRunner.cs` Linux branch | Must turn red |
|---|---|---|
| PC-1 | `TerminationDue` ignores `_terminationSucceeded` (success treated as failure, so backoff applies) | W1 |
| PC-2 | `TerminationDue` returns true whenever `_terminationFailures > 0` (no backoff) | W2, W4 |
| PC-3 | Boundary `>` instead of `>=` | W2 |
| PC-4 | Exponent `1 << k` instead of `1 << (k-1)` | W2 |
| PC-5 | Cap removed (no `min 60`) | W2 (k=7 leg) |
| PC-6 | Skip `ReadActive` and return `new(1,false)` when the attempt is skipped | W2 last leg, W3 |
| PC-7 | Set `_linuxTermination = new(false,0)` on a skipped read | W3 |
| PC-8 | Schedule updated only when the outcome is non-null (D-7 broken) | W4 |
| PC-9 | Clock read from `TimeProvider.System` instead of `_custodyClock` | W2 (manual advance has no effect) |
| PC-10 | Remove the bound entirely (S1 production state) | W1, W2, W4 (same as CP-1) |

Each PC runs only its listed methods with the exact `/*/*/LinuxCustodyReleaseHostTests/<Method>`
filter, and must show the named assertion failing (not a build or fixture error). CP-1 already
records PC-10's red.

### Checkpoints

Lane: Linux (server2 default per `GET /api/runner-defaults` `globalRunnerId: server2`). The
witnesses skip on Windows, and a skip is `row_failed`. Each row is one isolated build output or a
`--no-build` reuse in the same `After` group, and one exact filter. The importer's off-Windows
`UseAppHost=false` default applies.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.PtyHost.Tests -> bin-c1617-red/` | c1617-red | `/*/*/LinuxCustodyReleaseHostTests/(C1617_Explicit_seal_reads_terminate_once_after_success*)\|(C1617_Failed_termination_retries_on_capped_backoff*)\|(C1617_Throwing_termination_counts_as_failed_attempt*)` | W1,W2,W4 red-first; PC-10 | 3 executed, all 3 FAILED on the `Terminations` assertion (W1 10 vs 1, W2 first leg 5 vs 1, W4 second leg 4 vs 1); record RED, not a pass | 3 | 4 | false |
| CP-2 | S1 | CP-1 | c1617-guard-base | `/*/*/LinuxCustodyReleaseHostTests/(C1614_*)\|(C1617_Bounded_release_still_finalizes*)` | W3 green before the fix; fixture knobs keep C1614 rows | 4 executed (3 C1614 + W3), 0 failed, 0 skipped | 4 | 1 | false |
| CP-3 | S2 | `tests/Antiphon.PtyHost.Tests -> bin-c1617/` | c1617-host | `/*/*/LinuxCustodyReleaseHostTests/*` | W1-W4 green; I1-I4; C1614 rows | all 7 listed (C1614 x3, C1617 x4), 0 failed, 0 skipped | 7 | 4 | false |
| CP-4 | S2 | CP-3 | custody-floor | `/*/*/(HostCustodyPlatformTests*)\|(CustodyReceiptBackendTests*)/*` | HostSession seam does not disturb binding refusal or receipt shapes | 6 executed (1 + 5), 0 failed, 0 skipped | 6 | 1 | false |

Suffix check: no C1617 method name is a prefix of another. `C1614_*` matches exactly the three
existing C1614 methods. `HostCustodyPlatformTests*` and `CustodyReceiptBackendTests*` match one
class each in `Antiphon.PtyHost.Tests`. Run S1's rows (CP-1, CP-2) as one checkpoint run at the S1
commit and S2's rows (CP-3, CP-4) as a second run at the S2 commit. Only the S2 run's receipt is
validated clean (`validate --rows CP-3,CP-4`). The S1 run's CP-1 red is recorded evidence.

Import check (this Plan, at the plan text committed with it): `dotnet tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll import --plan docs/superpowers/plans/2026-11-03-card-1617-custody-kill-bound-plan.md --out .antiphon/c1617-import.yaml` gave `imported 4 rows`, exit 0. It derived two builds (`bin-c1617-red`, `bin-c1617`) and a 15-minute row deadline for each row.

Fixture risk: W2 last leg and W3 reach `Exited` only if the inbox pty reader ends after root exit (`DrainCustodyOutputAsync` awaits `_readTask`). If CP-2 shows W3 as `Unknown`/`observation_canceled`, that is a fixture fact. Report it blocked with the TRX. Do not widen the host 5 s bound or add retries.

Owed, not in the closed list (D-9): the Windows regression floor, `Antiphon.Agents.Pty.Tests`
`/*/*/PtyCustodyTests/*` and `Antiphon.PtyHost.Tests` `/*/*/HostCustodyTests/*`. These run at the
next rc Windows qualification that carries this change. `LinuxCgroupCustodyTests` (NativeLinuxOnly,
real helpers) stays the CARD-1614 native-pending lane; no qualified custody image exists on server2.

## Interactions

- **CARD-1653 S2** (`cea4ebbef`, `d1bbbfdbc`, on master): it changed `PtyHostClient`,
  `PtyHostPeerCredentials`, `PtyHostSocketDirectory`, `PtyHostPeerPolicy`, `PtyHostRendezvous`,
  `SessionRunnerRuntime.cs` and `SessionRunnerSettings.cs`. It did not touch `PtyAgentRunner.cs`,
  `HostSession.cs`, `HostCustodyJournal.cs` or the test class (`git show --stat`).
  - A peer refusal leaves the runner without a client. That is the dead-host branch, gated on
    `explicitSeal`, which this card does not change.
  - The socket directory is separate from the custody store and `stop-intents/`.
  - No interaction.
- **CARD-1677** (plan `26a269690`/`17d27afca`, not on master): its files are `RunnerWorkspaceRetention`,
  `RunnerTempSweep`, `PhoneHome*`, `WorkspaceSlotSeams`, server attention/settings and C1677 tests.
  It touches no custody, PtyHost or Agents.Pty file. **Confirmed: no interaction.**
- **CARD-1614**: the source of the carried seal (plan line 80) and of Review A-1. Its contracts are
  kept (I1).

## Platform

Code: Linux. The default runner (server2) is Linux, so omit `-Runner`. Pass `-Platform Linux`
only if the dispatcher could pick a Windows seat, since the witnesses need Linux.

No session that waits for input is added or changed, so the parking checklist is not applicable.

## Performance budget

Apply [performance standards](../../testing-and-build.md#performance-standards) and
[test-performance-standards](../../test-performance-standards.md). This Plan ran no tests.

**Measured** (investigation `a5ce15c96`, server2 Linux x64, kernel 4.15, load average ~36, one sample):
- `Antiphon.PtyHost.Tests` incremental native build lease 26 s, after a 211 s slot wait;
- class run of 4 tests (3 C1614 + 1 scratch) 5 s lease, after a 76 s wait;
- 10 `seal:true` reads 39 ms;
- helper sudo+bash spawn floor about 20 ms per call.

CARD-1614's CP-52 budget for this class was 2 minutes. A cold `bin-c1617*/` build is not measured;
it includes the `CopyOwnedPtyHostAppHost` child linux-x64 build. **No p95 exists.** Every figure
is provisional.

Selection and phases:
- **Selection.** 4 new methods plus 3 existing (CP-3), 6 floor (CP-4).
- **Shared setup.** None is expensive: no container, database, migration or AppHost.
- **Per-test phases.** Host start (in-process `HostHarness`), an inbox `/bin/sh` launch, the hold
  release, and the wait for Exited, which the existing tests show is under 1 s, bounded by the
  class's 10 s loop. Then N reads: W2 is about 22 reads at a few ms each. Teardown is `Finish`,
  bounded at 8 s.
- **Clock.** The injected clock makes the backoff schedule cost no wall time. There is no `Task.Delay`
  in the new tests except the class's existing 50 ms Exited poll.
- **Isolation.** Each test has its own host, temp dir and custody store. The class's
  `ParallelLimiter<ProcessSpawnLimit>` serializes process spawns.
- **Expected per-test time:** under 2 s.

Timeouts:
- No new `[Timeout]` attribute, matching the class.
- Inner bounds are unchanged: the 10 s Exited loop (measured under 1 s), `Finish` 8 s, and the host's
  5 s per-read `CancelAfter`.
- Row deadlines come from the importer's `EstimatedMinutes` rule (3x, 15-minute floor). That is a
  documented importer default, not 2x a p95: the measured rows are under 1 minute leased. Code
  records the per-row build and execution times as the first samples.

Totals:
- Closed-list floor: **10 minutes** (CP-1 4 + CP-2 1 + CP-3 4 + CP-4 1).
- Slot wait is reported separately: 76-211 s per lease was observed on loaded server2.
- Nothing approaches 30 minutes, so no Debug task is needed.

Recording: CHECKPOINT lines and the `report.json` per run under the checkpoint results root
(ignored), with per-row build and execution times. Code reports W2's read count and the elapsed
time of its first leg.

Product cost after the fix:
- A stuck Linux release costs one `ReadActive` (a cgroup procs read) per tick.
- In steady failure it costs at most one helper attempt and one durable stop-intent file per
  minute, instead of one of each per tick.

Residual, not this card: one attempt can still hold `_writeGate` for up to 45 s.
