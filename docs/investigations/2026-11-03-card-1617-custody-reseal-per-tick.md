# CARD-1617 investigation: Linux custody re-seal repeated on every observer tick

Date: 2026-11-03. Task 0eeb3129 (Investigate). Base: `origin/master` = `7b920d84aae97139998f940ee2cce3f457bb6e70` (this already includes CARD-1653 S2, `cea4ebbef`/`d1bbbfdbc`).

## Verdict

**Confirmed and reproduced. The defect is latent.** No Linux custody execution has ever existed, because `LinuxCustodyQualified` is false everywhere.

Once `runner-seal.json` exists for a Linux execution, every `seal:true` request to a live pty-host calls the root-owned kill helper again. The kill is not gated on an earlier attempt or its outcome. While the tree stays populated or unreadable, the runner's observer sends one such request every ~1 s plus the helper's own runtime, with no end. A scratch witness on the real host code measured **10 explicit-seal reads -> 10 `Terminate()` calls in 39 ms**.

### Brief premise correction

This "re-seal" does not re-hash or re-seal files. No content hash, mtime or tamper check is involved anywhere. The seal records are write-once:

- `RunnerCustodyLedger.Seal` writes `prior ?? new`.
- `HostCustodyJournal.RecordSeal` uses `_seal ??=`.
- `PtyAgentRunner` gates `RecordSeal` on `_custodySealed`.

What repeats is the **termination**: `sudo -n /usr/local/bin/antiphon-custody-kill <container>`. A hash or mtime cache does not apply. The bound belongs on the termination attempt.

## Mechanism (file:line on 7b920d84a)

1. **Explicit seal.** `VerificationCleanupService.cs:61` calls `ReadVerificationCustodyAsync(binding, seal:true)`. The runner's `ReadCustodyAsync` (`SessionRunnerRuntime.cs:510-529`) runs `_custody.Value.Seal(binding)`, which writes `runner-seal.json` (`RunnerCustodyLedger.cs:193-199`, rewriting the same bytes when it already exists). It then calls `CollectCustodyAsync(explicitSeal:true)`.
2. **The persisted seal becomes the seal flag on every later read.** `CollectCustodyAsync` (`SessionRunnerRuntime.cs:4883-4936`), live-client branch:
   - `:4906-4910` computes `var runnerSeal = RunnerSealPresent(binding); var seal = linux ? runnerSeal : ...`. For Linux, the flag equals "a `runner-seal.json` exists". It ignores `explicitSeal`.
   - `:4916` passes that flag to `_client.GetCustodyAsync(binding, seal, ct)`.
   - This is deliberate. CARD-1614 plan line 80 reads: "Carry explicit release intent from the persisted runner seal at runner ingress".
   - The dead-host branch (`:4899-4903`) is gated on `explicitSeal`, so it runs once per explicit request. The live branch has no such gate.
3. **Two callers repeat the read:**
   - **Observer loop.** `ObserveCustodyThenShutdownAsync` (`SessionRunnerRuntime.cs:4941-4972`) starts from `ShutdownHostAsync` (`:4858-4864`) when the child exits or vanishes (`:4612`, `:4749`, `:4780`). It calls `CollectCustodyAsync(explicitSeal:false)`, then `Task.Delay(1 s)` (`:4964`), until one of three things happens:
     - a receipt arrives;
     - a release disposition matches;
     - the result is `custody_observer_unavailable`, or there is no client and the state is Unknown.

     `Draining` and `Unknown/observation_failed` with a live client loop forever.
   - **Ordinary reads.** Each `GET .../custody` call (`Program.cs:240`, `seal:false`) reaches the same branch and also sends `seal:true`.
4. **Host.** `HostSession.GetCustodyAsync` (`HostSession.cs:377-396`) passes `linux ? seal : ...` to `HostCustodyJournal.ObserveAsync`. With `seal:true`, that calls `ObserveLinuxExplicitAsync` (`HostCustodyJournal.cs:236-262`) and then `PtyAgentRunner.SealAndObserveCustodyAsync` (`PtyAgentRunner.cs:575-623`).
5. **The ungated kill.** At `PtyAgentRunner.cs:597-612`, `_linuxTermination = containment.Terminate()` runs on **every** call, through the `StopSignal` recorder branch or directly. Only `RecordSeal` is guarded by `_custodySealed` (`:582-588`). `LinuxCustodyOperations.Kill` (`IPtyCustodyContainment.cs:186-218`) spawns `sudo -n antiphon-custody-kill <id>` and **synchronously** waits up to 45 s (`:202`).

   Two consequences of the synchronous wait:
   - The host's 5 s `CancelAfter` (`HostSession.cs:386`) cannot interrupt it.
   - The host `_writeGate` and `_admission` stay held for the helper's whole runtime.

   If `ReadActive()` is still non-zero after the helper, the call returns `Draining "descendants_running"` and the cycle repeats.

### When the repetition actually happens

A helper that succeeds empties the tree. Its own drain read is what lets it exit 0. The next `ReadActive` is then 0, a receipt is persisted and the loop ends. The repetition is therefore confined to helper failure or a tree that will not empty:

| Condition | Per-cycle cost | Cycle period |
|---|---|---|
| Helper refuses fast: exit 3 `NamespaceUnavailable`, `CustodyFreezerMissing`, `CustodyFreezeFailed`, `CustodyTreeUnreadable`, `CustodyThawFailed`, missing library. The tree is populated, or unreadable (`IOException` -> `observation_failed`, and the loop continues because the reason is not `custody_observer_unavailable`) | 1 sudo + bash spawn, ~20 ms | ~1 s, unbounded |
| Survivors after SIGKILL: the helper drain times out (exit 4 `KILL_DRAINING`), for example D-state tasks or a FREEZING cgroup | v1: freeze + SIGKILL every listed pid + thaw, then up to 30 one-second `cat` polls | ~31-32 s, unbounded |
| Output drain throws after a zero read (`DrainCustodyOutputAsync` `IOException`, cached in `_custodyDrainTask`) | 1 helper call on an already-empty tree, then the same exception | ~1 s, unbounded |

There are no git calls and no fsync per tick. On the Draining path the host writes no file per tick. The observer path never goes through `NoteUnresolved`. Ordinary `ReadCustodyAsync` reads write `linux-unresolved.json` once and the 30-minute warning once (`SessionRunnerRuntime.cs:531-560`).

## Evidence

- **Reproduction (scratch, not committed; reverted with `git checkout`).**
  - Setup: a copy of `LinuxCustodyReleaseHostTests.C1614_Root_exit_with_live_descendants_does_not_terminate`. Its `FakeContainment` was set so `Terminate()` leaves the pids in place (survivors). The test writes `runner-seal.json` after root exit, then calls `host.Session.GetCustodyAsync(binding, seal:true)` 10 times.
  - Output: `C1617 read 0..9: Draining descendants_running terminations=1..10`, and `C1617 10 explicit reads in 39 ms, terminations=10`. `ShouldBe(1)` failed red.
  - Same class run: the 3 existing tests passed. 1 failed, 3 succeeded, 0 skipped.
  - Build lease 26 s (after a 211 s slot wait). Run lease 5 s (after 76 s). Native output, `UseAppHost=false`, server2 load average about 36.
- **Original observation.** In the CARD-1614 slice B round 2 Review, advisory A-1 (`e2a59a098`, `.antiphon/task-392649e9.md:92-95`) reads: "4 host seal flags in 3.5 s ... `containment.Terminate()` ... not gated on `_custodySealed`". It names plan line 80 as the sanction.
- **Helper spawn cost (read-only).** On server2, `sudo -n /usr/local/bin/antiphon-custody-kill <random guid>` ran 20 times in **393 ms** (~20 ms each, load average 36.7).
  - The installed helper is the Sep-23 image, sha256 prefix `e173cce8`. The repo copy is `68d965da`. The installed one aborts at its pre-CARD-1614 `local id` bug (line 91, `id: unbound variable`, exit 1) before touching any cgroup.
  - So the figure is the sudo + bash startup floor, not a real freeze/kill.
  - `/run/antiphon-custody/namespace` is absent on this runner.
- **No production exposure.**
  - `SessionRunner__LinuxCustodyQualified` is unset on this runner. `dind-entrypoint.sh:181,185` and `antiphon-custody-common.sh:146` force it false.
  - Dev DB, `VerificationExecutions` (read-only transaction): 10 rows, all `"Backend":"windows-job-v1"`, **0 linux-cgroup**. Last row 2026-09-21.

## What the re-termination protects (the invariant a fix must keep)

The explicit seal is the only Linux authority to end the whole tracked tree (CARD-0604 D-17, CARD-1614 P-3). Custody may reach `Exited` with a receipt only after observed zero accounting and drained output. Repeating the kill buys a few things:

- a retry after a transient helper refusal;
- a second SIGKILL sweep for pids that appeared while a v1 freeze was still FREEZING.

A pending SIGKILL on a D-state task already kills it on wake, so repeating the kill adds nothing for that case.

Nothing depends on the kill happening every tick. Nothing depends on detecting tampering either: the custody records are write-once and checked by identity (`ExitedIdentityIntact`, `CanNaturalFinalize`), not by a cached seal.

## Bound that keeps the invariant (recommendation for Plan; not implemented)

Put the bound in the host, at `PtyAgentRunner.SealAndObserveCustodyAsync`'s Linux branch, where the kill is issued. Leave the runner's seal-flag formula (plan line 80) and the "explicit seal only" rule unchanged.

- **I1 (unchanged).** `Terminate` is reachable only from a `seal:true` request. Root exit, the producer seal, ordinary reads and elapsed time never terminate. A prior seal record alone never authorizes the dead-host recovery kill.
- **I2 (new).** Per host instance and execution:
  - After a `Terminate` whose observation `Succeeded`, no further `Terminate` is issued.
  - After a failed or timed-out attempt k, the next attempt waits at least `min(2^(k-1) s, 60 s)`, so the steady state is at most one attempt per minute.
  - The schedule needs an injectable clock (`PtyAgentRunner` has no `TimeProvider` today).
- **I3.** Every `seal:true` read still re-reads accounting (`ReadActive`). It can still reach drain, `Exited` and a receipt. A skipped attempt never reports zero, never writes a receipt and never writes a release disposition.
- **I4.** `_linuxTermination` holds the outcome of the last real helper call. The receipt's `terminationSucceeded`/`terminationError` fields come from an actual attempt, never from a skipped one.

A caching failure mode that matters is a recorded success hiding survivors. I3 covers it: accounting is re-read on every request, and a populated tree stays Draining.

The card's alternative is one attempt per terminal cleanup. It would need a new protocol field to tell an explicit runner request from the carried persisted seal (`CustodyRequestMessage(binding, seal)` has only one flag). It also gives up the retry after a transient refusal. Choosing between the two is a Plan decision.

## Red-first witnesses and checkpoint cost

The witnesses go in `tests/Antiphon.PtyHost.Tests/LinuxCustodyReleaseHostTests.cs`, Linux-only and portable, reusing the existing `FakeContainment`:

1. `C1617_Explicit_seal_reads_terminate_once_after_success`. Survivors stay after a successful `Terminate()`. 10 `seal:true` reads -> `Terminations == 1`, every read `Draining`, no receipt. Red today: 10.
2. `C1617_Failed_termination_retries_on_capped_backoff`. `Terminate` returns `(false, 3)` and the clock is injected. Ten reads with no clock advance -> 1. Each advance past the schedule -> +1. Never more than one attempt per 60 s once capped. Red today: 10 on the first leg.
3. `C1617_Bounded_release_still_finalizes`. After the single attempt, the fake tree empties. The next `seal:true` read returns `Exited` with a receipt that carries the recorded `terminationSucceeded`. Green today; it guards I3 and I4 against a bound that also skips accounting.

A runner-side witness is unnecessary for the host bound. The existing `RunnerCustodyReleaseTests` C1614 rows still guard I1.

Checkpoint cost, measured: one `Antiphon.PtyHost.Tests` build (26 s leased) and one class-filter run (`/*/*/LinuxCustodyReleaseHostTests/*`, 5 s leased). The slot wait dominates on loaded server2. A native NP row is optional. No sudo, desktop or Windows run is needed: the Windows `windows-job-v1` branch of `SealAndObserveCustodyAsync` is untouched, though a Plan may keep the existing Windows custody rows as a regression floor.

## Interactions

- **CARD-1653 S2** (already on master): the owned socket directory and peer check. It changes `PtyHostClient` connect/hello, `PtyHostRendezvous`, `PtyHostPeerPolicy` and launch-region code in `SessionRunnerRuntime.cs`. It does not touch `PtyAgentRunner.cs`, `HostCustodyJournal.cs`, `HostSession.GetCustodyAsync`, the custody store root, or the kill helper. The custody store (`CustodyStoreRoot`) and `/run/antiphon-custody` are separate from the socket directory. No interaction.
- **CARD-1677** (worktree retention, plan `17d27afca` on `feat/card-task-0f317c85`): the plan mentions "Git custody" only as wording. It never touches verification custody records or cgroups. No interaction.
- **CARD-1614:** the source of the behaviour (plan line 80; Review A-1).
- **Stale helper image on server2:** the installed helper predates CARD-1614's `local id` fix. That is an image or rollout fact, not part of this card.

## Remaining uncertainties

- The cost of a real kill on a populated v1 tree (freeze/thaw against a busy cgroup) was not measured. Doing so needs a qualified custody image and would modify cgroups. The code bound is a 45 s wait per attempt.
- Whether the bound should also reset on a new explicit cleanup request is a Plan choice. Resetting needs the protocol field described above.

## Not done, noted

- Fix idea, one line: gate `containment.Terminate()` in `PtyAgentRunner.SealAndObserveCustodyAsync` on "no earlier success, and the backoff has elapsed since the last failure" (injectable clock), keeping the per-read `ReadActive`.
