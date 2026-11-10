# Review bce24013: CARD-1514 hang capture round 2 (native TRX reader repair), Final

Subject: origin/feat/card-task-7d420b75 tip 14a1cf198326a97b5c2def31ccc976d3eca0e727 (Grok Debug task 7d420b75-ab3f-41df-b9c2-f9dae774c018).
Range (round 2, diff only): 89fd55cefbcbe6a86a589d663fe1ffa8d574192f..14a1cf198326a97b5c2def31ccc976d3eca0e727 (red 84f2ed7fa, fix a01b38fb9, report 14a1cf198).
Verdict: the code is CLEAN, and round-1 D-1 is repaired. The branch is NOT landable as it stands. L-1 (below) is a merge-tree conflict with origin/master a173a87a9, caused by master's c2c28776e.

## L-1 (land blocker): merge-tree conflict with origin/master

- Where: `scripts/lib/nightly-tests-impl.ps1`, two hunks.
- Failure: `git merge-tree --write-tree origin/master 14a1cf198` exits 1 with `CONFLICT (content)`. origin/master is a173a87a987afbcd60827ec0975c9999c2b91fc7, the brief's pin. The round-1 tip 89fd55cef conflicts the same way, so the conflict is not from the round-2 diff. Master's c2c28776e ("count rostered Windows skips as expected coverage", landed 2026-11-10 07:36) changed both places:
  1. In `Invoke-NightlyScheduledChunk`, master changed `} elseif ($run.ExitCode -ne 0) {` to `} elseif ($run.ExitCode -ne 0 -and -not $exitTolerated) {`. The branch puts its `if ($completedHang) { ... } else { TIMEOUT }` block directly above that line.
  2. In the locals of `Invoke-AntiphonNightlyTests`, master adds `$expectedSkipWarnings = @()` / `$expectedSkipRoster = @()` and the branch adds `$harnessHangReason = ''` at the same spot.
- Why it blocks: git-common says a branch is landable only if merge-tree reports no conflicts. The land step rebases, and both hunks would stop that rebase.
- Fix (Merge task, owner 7d420b75): resolve as a union. Keep the branch's completed-hang block and master's `-and -not $exitTolerated` on the `elseif`. Keep all three locals. Then rerun `-Case Group4NativeTrxReader`, `-Case Group4CompletedHang` and the consumer classes below at the merged tip. I found no semantic interaction: `$exitTolerated` only gates the non-timeout `elseif` branch.

## Checks asked in the brief

1. Native reader. My own probe: `Add-Type` of the real `.cs` in a fresh pwsh, then reflection on `ExecutionEvidenceFinished`/`TryReadTrxSummary`.
   - True: the case fixture shape (119/119), the real `c487-probe/all.trx` rewritten to 119/119 (with and without a UTF-8 BOM), attributes in swapped order, single quotes, and a `t:` namespace prefix.
   - False: notExecuted=1, executed 118 of 119, `total ran: 118`, no `request execute stop`, failed=3 with outcome Completed, outcome Failed, a TRX with no ResultSummary, a truncated Counters tag, a UTF-16 TRX (the safe direction: the chunk falls back to the watchdog), an escaped `&lt;ResultSummary...&lt;Counters` inside an earlier UnitTestResult StdOut when the real summary is Failed, the same escaped text inside the summary's own StdOut, escaped `&lt;Counters` when there is no real Counters element, `<CountersX>`, and an `xtotal=` attribute ahead of the real `total=`.
   - Only one shape breaks the reader: an unescaped CDATA `<ResultSummary...><Counters .../>` inside the ResultSummary's own StdOut after a real Failed summary. That gives True. MTP's TRX writer escapes text and does not emit CDATA, so this is A-3 only.
   - Cost: the probe re-reads only the last 2 MB every 500 ms, and only until the first hit. Before the TRX exists, each probe is one `File.Exists`. On a 198 MB TRX each probe took 6.4 ms. A Completed TRX that never qualifies (failed>0) is re-read every poll at 13.4 ms per probe, which is bounded. If the ResultSummary's own output exceeds 2 MB, the reader returns False and the chunk falls back to the watchdog. That direction is safe.
2. Never green, and no kill of a healthy host.
   - The completed-hang branch is unchanged from round 1: `row.result=FAIL`, outcome TESTS (publicationTerminal failed-run), TestsPassed and CoverageComplete false.
   - `NightlyCompletionGrace.Wait` checks `childrenExited` before the grace on every 10 ms poll, so a host that exits during the grace leaves the loop un-hung.
   - The grace is armed only after `ExecutionEvidenceFinished`: Completed, failed=0, executed==total, notExecuted=0, `request execute stop`, and `total ran` == total.
   - A-2 stale-evidence probe: with the three-argument form and notBefore=now, a TRX and diag both 5 min old give False; a fresh TRX with a stale diag gives False; both fresh give True. A diag directory holding an old marker-bearing `.diag` and a newer fresh one picks the fresh one and gives True. Production passes `Observation.RootStartedAtUtc`.
3. One implementation. `Invoke-NightlyCompletionGrace` is deleted, and nothing in the repo references it except the witness that asserts it is gone. `Test-Group4CompletedHang` now drives `[Antiphon.Nightly.NightlyCompletionGrace]::Wait`, which is the loop `NativeProcessOwner` calls.
4. Counts:
   - At 14a1cf198: Group4NativeTrxReader 27/27 and Group4CompletedHang 25/25 (each exit 0).
   - Red at 84f2ed7fa: Group4NativeTrxReader 13 passed / 14 failed of 27, matching the claim.

## Mutants (detached tip worktree, each restored with `git checkout`, tree clean after)

| Mutant | Case | Result |
|---|---|---|
| M1 reader back to `text.LastIndexOf("ResultSummary")` | NativeTrxReader | RED 4 (case shape, real shape, prefixed/quotes, fresh age) |
| M2 drop `if (failed > 0) return false;` | NativeTrxReader | RED 1 (rejects failed above zero) |
| M3 age check compares to MinValue | NativeTrxReader | RED 3 (stale trx, stale diag, same-instant) |
| M4 `if (gone) break;` -> `if (gone && finishedAt < 0) break;` (a healthy host that exits after the markers is killed as hung) | CompletedHang + NativeTrxReader | SURVIVES (A-1) |
| M5 grace compares to watchdog | CompletedHang | RED 1 (classified hung inside the grace) |
| M6 production `notBefore = DateTime.MinValue` (stale filter unwired) | both | SURVIVES (A-2) |

## Inherited default-case results (my runs at base 566d64b17 and at tip, every run leased)

The five cases fail identically at the base and at the tip:
- G039: 5/1, `entry missing expanded uid`.
- G040: 16/2, `partial selection not complete` and `production discovery generated`.
- G044: throws `Nightly native custody requires Windows.`
- G051: 4/1, `trx-diagnostic-mismatch` with ordinary trx=0.
- G144: 1/1, `production native argv writes TRX` trx=False.

All five are Linux environment results, not real defects. G044 hits the explicit Windows guard. G039, G040 and G144 drive `Invoke-E` end-to-end, which needs native custody (Windows). G051's probe TRX is empty on Linux. These match the recorded Linux reds.

## Ordinary scope at 14a1cf198 (server2 Linux; build and runs each leased; native output, UseAppHost=false)

- Build: `dotnet build tests/Antiphon.Tests`, 0 errors.
- Guard union `(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(TestLaneCategoryGuardTests*)`: 53/53, 0 failed, 0 skipped. Breakdown: 41 policy, 5 registry, 3 native, 2 tripwire, and 1 each for TestClassificationGuard and TestLaneCategoryGuard.
- Script-consumer classes (unlisted run). Reason: these classes read the changed `nightly-tests-impl.ps1`, `nightly-owned-process.cs` or `test-nightly-tests.ps1`, and the run checks that the change breaks none of them. 53/53, 0 failed, 0 skipped: C1514LandingSplitBudget 5, NightlyDiscoveryParser 6, NightlyExecutionDiagnostic 7, NightlyPrerequisite 2, NightlyScripts 5, RcFullTimingContract 10, RcFullWatchdogHarness 2, TestPhaseSupervisor 2, ToolchainFloorWarning 14.
- `check-evidence-diff.ps1 -BaseRef 89fd55cef -HeadRef 14a1cf198`: commits=3, entries=1, violations=0, exit 0. Base 566d64b17 is not an ancestor (base_not_ancestor), so the range used the reviewed SHA.
- merge-tree: conflict, see L-1.
- PCs: none discharged; all stay pending.
- reviewedSourceClean is false. The plan has no checkpoint-tool table for this Debug task, so these were direct leased runs without SHA-validated receipts.

## Advisories (cards; they never hold a land)

- A-1 (test adequacy): no witness proves that a host which exits during the grace is not classified hung (M4 survives). Add a `Wait` case: `finished` true at once, `childrenExited` true after about 100 ms, grace 300 ms. Expect CompletedButHung=false and TimedOut=false.
- A-2 (wiring witness): nothing checks that production passes the process start into the age filter (M6 survives). The native path is Windows-only. Add a source-shape assertion that `NightlyCompletionGrace.Wait` is called with `ExecutionEvidenceFinished(..., notBefore)` and that `notBefore = Observation.RootStartedAtUtc`, or run it on the Windows lane.
- A-3: the PowerShell post-hoc `Test-NightlyExecutionFinished` (impl ~1360) has no age filter. A real watchdog timeout rerun into the same `-LogRoot`, with a stale Completed TRX and diag present, is labelled completed-but-hung (TESTS) instead of TIMEOUT. It stays red, it is unreachable from nightly-run and release-cut, and the label is the only error. The same item covers the CDATA-in-summary false positive (theoretical).
- A-4: `summary.harnessHang` is witnessed only by text presence. No executed producer-to-summary test sets it, because the native path needs Windows. The field has no consumer yet. Note it for the rc repair loop.
- Carried from round 1: the 60 s grace is still unmeasured, and the kill is now live. `execution-stop-to-exit-seconds` is logged so a p95 can be taken. Until that measurement replaces the placeholder, a host that takes more than 60 s from the markers to exit turns a PASS into FAIL.
