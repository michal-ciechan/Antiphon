# Why missed: C1759 hosted-scan and RemoteWorkspacePreparer cancel load flakes (Debug c4b4ad81)

Date: 2026-11-09. Base: master `a0e14b3e1`. Fix: `5a30c6cbbfd709c1aa51d7140ae54a721b647e42` (test-only).
Host: server2 Linux, 24 cores, shared with other delegates (load average 11-19 during these runs).

## Summary

Both reds were test timing assumptions, not product races. Each test had a wall-clock stop or
wait that did not match the work it was meant to cover. Earlier reports each called the failure a
load flake, saw it pass alone, and moved on. No card was filed and no test was changed.

| Test | Defect | Fix |
|---|---|---|
| `AgentTaskLandNotificationReconcileLoadTests.C1759_HostedScanSkipsUndueUnlinkedNotes` | Stopped the hosted worker once the due and provider rows had settled. The asserted linked row could still be later in the same pass. | Wait for the hosted `notification-scan` boundary (one whole pass), 30 s bound unchanged. |
| `RemoteWorkspacePreparerTests.Cancel_of_a_queued_runner_task_returns_while_its_mirror_is_pending_and_a_late_success_is_still_recorded` (and the cancel half of `C1032_CaptureAttemptAndCancellation`) | Started the tick without awaiting it. The whole cold tick then had to finish inside the 10 s request wait. Sibling tests give the tick `TickBound` (15 s) first. | Pass `TickBound` and the tick to `WaitForRequestsAsync`. A timeout now names the elapsed time and the tick's status. |

## 1. C1759_HostedScanSkipsUndueUnlinkedNotes

### Mechanism

The test seeds four notes (due, future, linked, provider) with `Guid.NewGuid()` ids (random v4,
`LandNotificationPayload.Create`). The hosted page (`LandNotificationReconcileQueries.DueNoteIds`)
returns due, linked and provider in **id order**, and the worker reconciles them one by one. The
old test polled every 100 ms until `due.QueueMessageId != null && provider.State == NotRequired`,
then called `StopAsync`. When the linked note had the highest id (one ordering in three), the pass
could be cancelled after the provider reconcile and before the linked note's compiled
`NoteById` load. The assertion `linked undue note was not loaded` then failed.

The captured failure trace (base, alone, run 8 of 10) shows exactly that: the compiled page, the
provider and due note loads, and repeated poll `EXISTS` reads, with no load of the linked note.

### Clock question (4d91773c1 comparison)

This is not the E2E defect from Debug 61d7498e, where the scan read the real clock while the test
clock was ahead. The linked note is on the page because `QueueMessageId != null`, a predicate with no
clock in it. The due note was loaded and queued in the failing pass. The page's `now` comes from the
scope's `TimeProvider` (System in this harness), and the seeds are ±1 minute / +2 hours away from it.

### Evidence

| Run | Code | Result |
|---|---|---|
| Alone, leased, one process per run | base a0e14b3e1 | **1 failed / 11** (the failure ran beside a parallel RWP loop) |
| Loaded 165-test batches (land-1 + land-2 + RemoteWorkspacePreparerTests, parallelism 16-32) | base / timing-probe build | 0 failed / 3 batches |
| Forced order: linked id `ffff...f0`, 500 ms delay at `land-rescan-reconciled` for the other rows (scratch probe, not committed) | old test | **3 failed / 3** (`linked undue note was not loaded`) |
| Same probe | fix | 3 passed / 3 |
| Alone | fix 5a30c6cb | 20 passed / 20 |
| CARD-1801 land-1 filter (34 tests) | fix 5a30c6cb | 34 passed / 34 |
| Loaded: land-1 + land-2 + whole RemoteWorkspacePreparerTests, `--maximum-parallel-tests 32` (165 tests) | fix 5a30c6cb | 165 passed / 165 |

The pattern matches the neighbours: `AgentTaskLandNotificationRecoveryTests.C1705` and `.H5` already
wait for the `notification-scan` boundary. `AgentTaskLandNotificationRecoveryTests` (263 notes) waits
for the asserted positive condition itself, so it does not have this defect.

### History

- Introduced: `6db9130df` (2026-11-07, CARD-1759, "witness one compiled land-note load and the due
  scan filter"). The poll-then-stop shape has been there since then. `593ba300b` (CARD-1769) kept it.
- Flaky for about two days. Recorded failures: 2.
  - Code b255559a (CARD-1801 D-1 repair, report `e3733bb5a`, 2026-11-09): "I treat it as a timing
    flake in an untouched test. I widened nothing."
  - Merge 5c209018 (report `63f6ce16c`, 2026-11-09): CP land-1 33/34, then 4/4 and 34/34 on rerun.
    The report named it "Owed: Debug card", and this task is that card.

## 2. RemoteWorkspacePreparerTests cancel

### Mechanism

`Cancel_of_...` runs `var tick = rig.TickAsync();` and then calls `WaitForRequestsAsync`, which had a
fixed 10 s wall-clock deadline. Without the awaited tick, that 10 s covers the whole first dispatcher
tick: claim transaction, preparer `Task.Run`, task read, push turn, `BeginMirrorPreparationAsync`,
`ResolveSlotDecisionAsync`, and the phone-home write. On a cold process this includes first-use EF
query compilation. Every sibling first awaits `TickAsync().WaitAsync(TickBound)` (15 s) and only then
spends the 10 s wait on the residual. The failure text in every report is `saw 0`: the request had
not been sent yet. No report saw a late or lost request.

### Measurement (probe build, scratch only)

Tick start to first WorkspaceMirror request:

| Condition | n | Range |
|---|---|---|
| Cold process, alone (beside one parallel single-test loop) | 6 | 1.9-5.4 s |
| Cold process beside two heavy batch processes (165 + 199 tests, parallelism 32) | 8 | 1.9-6.8 s |
| Warm, inside a 165-195 test batch, parallelism 32 | 2 | 0.23-0.81 s (`C1032` cancel half: 0.02-0.83 s) |

The worst cold sample under load was 6.8 s, against a 10 s budget (1.5x). `TickBound` (15 s) is
about 2x that worst sample (R5), and it is the bound the siblings already give the same tick.

### Evidence

| Run | Code | Result |
|---|---|---|
| Alone | base (10) and timing-probe build (6) | 0 failed / 16 |
| Cold, beside two heavy batches | base (probe) | 0 failed / 8 |
| Loaded batches with the whole class (165, 165, 165, 195 tests) | base / timing-probe build | 0 failed / 4 batches |
| Alone | fix 5a30c6cb | 20 passed / 20 |
| Loaded 165-test batch | fix 5a30c6cb | 165 passed / 165 |

I did not reproduce the natural failure on this host at load average 11-19. The recorded failures
were at load average about 24-43 on 24 cores (task cf298805: "~43"; task 32a1e275: "24/32/24").
In Review fd0ebdc3, **eight sibling tests in the same run also failed their 15 s `TickBound`**. At that
load the cold tick is past any per-test budget. The fix removes this method's extra exposure (10 s
versus the siblings' 15 s + 10 s) and makes the next timeout say whether the tick was still running
(slow) or had finished without a request (a product defect). It does not make a cold tick under
load average 40 fit any budget. That needs admission control (see the rule below).

### History

- Introduced: the method first appears in CARD-0633 S3 / CARD-0629 (`1d3c34ff0` / `6bf779993`,
  2026-09-24). On master a0e14b3e1 it still had the unawaited tick and the fixed 10 s wait.
- Flaky for at least 16 days: 10 failure mentions in 9 reports. Some of the 2026-11-06 reports may
  describe the same CARD-0824 S5b-2 run.
  - 32a1e275 (2026-10-24, report on master): "Load sensitivity is a hypothesis... Preserve this
    intermittent failure for native triage."
  - cf298805 (2026-11-02): "Treated as a load flake of a pre-existing wall-clock bound; no timeout
    or assertion was changed."
  - 6db87da1 (2026-11-03): "saw no request within its 10 s wait."
  - Debug 32c41b68 (2026-11-06, `docs/investigations/2026-11-06-debug-s5b-2-13-failures.md`):
    category (b) "inherited fixture/load timing failures; all thirteen pass on master."
  - 204a327a (2026-11-06, twice): "depends on the broader selection/load. No timeout, assertion or
    retry was added."
  - 2b1406ee (2026-11-06) and Review fd0ebdc3 (2026-11-06): named it with the 15 s tick-bound
    siblings.
  - Review 6394f325 (2026-11-09) A-4: "Load flake... green 1/1 alone."
  - Merge 5c209018 (2026-11-09): "Load flake."

## Why nothing quarantined or fixed them

Each report did what the rules ask of a single stage. The test was outside the task's diff, a rerun
passed alone, and the rules forbid loosening a timeout or adding a retry. So each report recorded
"load flake, passes alone" and moved on. Only Debug 32c41b68 classified the RWP failure, and its
outcome was "inherited, passes on master", with no owner. Nothing joins the reports: no ledger counts
a method that is red in one report and green on rerun, and "file a card" is advice in the report
body, not an action. The same method failed ten times in sixteen days across nine tasks before a
Debug task was dispatched. That dispatch came only because a Merge report listed it under "Owed".
The C1759 flake was caught after two occurrences because Merge 5c209018 wrote it as owed work.

## Proposed rule and card (CARD-1801/1803 area)

**Rule:** a test method that a stage report or checkpoint receipt records as failed, and that then
passes on rerun ("passes alone", "load flake", "intermittent", "inherited"), is a flake
observation. Two observations of the same fully qualified method from different tasks within 14
days must become a Debug card automatically. A report that calls a failure a flake must name the
Debug card or the open card it joins. "No card exists" is not an acceptable end state.

**Proposed card:** *Flake ledger: auto-file a Debug card on the second "passes alone" red of a
method.*
1. The settlement report parser (the CARD-1801 checkpoint receipt and the report-lines path)
   records each red-then-green method as `(method, task, sha, row, host load, failure first line)`.
2. A second observation from another task within 14 days files a Debug card through `card.ps1`,
   searching first and appending to an open card when one exists. The card carries both report
   references, and the next keep-six/pipeline tick surfaces it to the orchestrator.
3. The Debug brief template asks for the measured duration of the waited operation (p95 alone
   and loaded) and a check of the neighbours with the same shape, per
   docs/test-performance-standards.md R5.
4. When sibling tests fail their own bounds in the same run (Review fd0ebdc3), host admission
   for that row is a separate finding. Raising the method's bound does not cover it.
