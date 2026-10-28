# Final Review ebe7ba48: CARD-1597 timing streaming read (subject 34d54a5e)

Reviewed tip b71d9b3179571d8444a494f78fa6c2cb5dc0490f (parent 0694a8ade, base rc/2026-10-27 f84228f285dd3e4ad7b5dc8260cbc9f819256f50) on server2/Linux, pwsh 7, read-only.

Outcome: two defects. The streaming logic keeps the post-exit, fail-closed and memory-cap guarantees, but the change breaks the Windows PowerShell 5.1 load of `test-phase-evidence.ps1` (D-1, PLAUSIBLE) and adds a wall-clock witness that went red at the tip in 1 of 4 runs (D-2, CONFIRMED).

## Defects

### D-1 (PLAUSIBLE): the new Add-Type does not compile on Windows PowerShell 5.1, so rc cuts would refuse with exit 3
- Where: `scripts/lib/test-phase-evidence.ps1:871` (`new ReadOnlySpan<byte>(line, 0, length).IndexOf(needle)` in `JsonlTimingScanner.IndexOf`). `Initialize-TestPhaseStreamReaders` (top level, line ~877) runs this Add-Type on every platform when the file is dot-sourced. The base had no Windows Add-Type in this file, because `Initialize-TestPhaseLinuxIdentity` returns early on Win32NT.
- Failure: Windows PowerShell 5.1 runs on .NET Framework, which has no `ReadOnlySpan<T>` (that type lives in System.Memory and is not an Add-Type reference), so the compile fails with CS0246. Add-Type then throws a terminating "Cannot add type. Compilation errors occurred."
  - The Windmill RC job runs `powershell -NoProfile -File C:\src\Antiphon\scripts\release-cut.ps1` (`scripts/windmill/antiphon-release-candidates.json`). On a new attempt with a blank `-TimingRoot`, `release-cut.ps1:121` dot-sources this lib inside the `try` at line 110. The `catch` at line 137 then records `refusal=Cannot add type...` and runs `exit 3`.
  - `release-selection.ps1:295` has an explicit 5.1 branch, so this path is meant to run on 5.1.
  - The Windmill nightly (`powershell ... nightly-run.ps1`) reaches `nightly-run-impl.ps1:147` under `ErrorActionPreference=Continue`: there it is error noise and the run continues.
- Evidence: I could not run real 5.1 here. On pwsh 7 I reproduced the propagation: a dot-sourced lib whose Add-Type fails to compile, called inside `try` with EAP Continue, gave `CAUGHT exit3 refusal=Cannot add type. Compilation errors occurred.` (`scratchpad/caller.ps1`). `TestPhaseSummaryTests.C1514T_PS51AndRerun` only parses the file and runs pwsh 7, and it skips on Linux, so no existing test would catch this.
- Why: the CARD-1514 plan still owes Windows CP-8 (Windows PowerShell 5.1). Both libs keep `#requires -Version 5.1`, and a scheduled rc cut must not refuse because a timing helper failed to compile.
- Fix: replace the Span search with a plain byte loop so the C# compiles as C# 5 on .NET Framework, keeping the rest of the block netfx-clean. Or load the scanners only when `$PSVersionTable.PSEdition -eq 'Core'`, wrap the Add-Type in try/catch, and fail closed to `timing-read-refused`. Add a Windows 5.1 witness that dot-sources the lib through `powershell.exe` and runs `release-cut.ps1 -WhatIf`/seams with a blank TimingRoot.

### D-2 (CONFIRMED): `five-thousand-result-trx-streams-inside-budget` is load-sensitive and went red at the tip
- Where: `scripts/test-qualification-timing.ps1`, the new `$wideDocOk` assertion (`$readSw ... -lt 200 -and $aggSw ... -lt 200`, and it requires `Complete` stats).
- Failure: my first tip run in this worktree (server2, load average about 15 to 24) printed `MEASURED trx-5000-read-ms=203.6`, `terminal-ms=313.4` and then `FAIL five-thousand-result-trx-streams-inside-budget`. The read hit the 200 ms deadline, so the code correctly failed closed and the test went red. Three later runs of the same bytes passed, at 38.4/112.8, 40.9/90.7 and 32.8/73.6 ms.
- Why: the test makes a hard wall-clock assertion exactly at the production budget with no margin. That breaks the test-performance standard (budget near 2x p95), and the scripts lane will flake on loaded runners.
- Fix: split the claims. (a) Check correctness (count 5000, p50 2500, p95 4750, max 5000, why '') under a larger injected deadline. (b) Check boundedness separately: elapsed < 200 ms plus a stated margin, and an incomplete read gives `timing-read-refused`. Alternatively, retry only the timing half with a recorded p95.

## Runs (all through scripts/build-slot.ps1, at b71d9b317 unless noted)
| Run | Result |
|---|---|
| `test-qualification-timing.ps1`, worktree, run 1 | 67 PASS, 2 FAIL: launch-path-opens-no-timing-file (strace absent), five-thousand (D-2) |
| same, runs 2 and 3 | 68 PASS, 1 FAIL (strace only) |
| control copy of HEAD (`git archive`), scratch | 68 PASS, 1 FAIL (strace only); read 38.4 ms, terminal 112.8 ms |
| RED: HEAD tree with base `test-phase-evidence.ps1` | five-thousand FAIL (read 0.7 ms, empty stats); unterminated-tag and heartbeat PASS (guards); 67 PASS |
| MUT M1: tag cap removed (`len < 0`) | unterminated-trx-tag-stays-inside-budget FAIL, so the cap is witnessed |
| MUT M2: Cursor in-stream deadline check removed (`_deadline < 0`) | all green except strace, so the during-stream deadline is NOT witnessed (A-1) |
| Antiphon.Tests build (default OutputPath, own worktree) | exit 0 |
| `(TestPhaseSummaryTests*)\|(TestPhaseSupervisorTests*)\|(TestTimingRecorderTests*)\|(TestTimingLifecycleTests*)\|(TestTimingChokePointTests*)` | total 27, passed 25, skipped 1 (C1514T_PS51AndRerun, Windows-only), failed 1: C1514T_Overhead timed out at 300 s |
| check-evidence-diff base..HEAD | commits=2 violations=0 |

C1514T_Overhead is an in-process CPU-overhead benchmark (Integration) in `TestTimingChokePointTests`. It never touches the ps1 lib, and this diff changes only the ps1 lib, its harness and docs. I attribute the timeout to the loaded host but did not rerun it at base.

## Priority answers
1. Guarantees.
   - Post-exit only: the reads are reached only from the terminal update, and the heartbeat witness stays non-terminal (it passes, and also passes at base as a guard).
   - FIFO, symlink and unreadable files: Linux opens with `O_NONBLOCK|O_NOFOLLOW` and fstat refuses non-regular files; the FIFO and denied-file rows pass.
   - No XmlReader is used. The scanner is a hand byte scanner: it expands no DTD or entities, `<!DOCTYPE` is skipped, nesting is an int depth with no recursion, and a pathological or unterminated tag is refused at 256 KiB (M1 shows this).
   - The deadline is checked before every 8 KiB `Read`, so overrun is bounded by one 8 KiB read plus per-row post-processing. That matches the base for slow disks.
   - Exit, result and coverage were unchanged in every AGG probe (`exit=3 result=KEEP-3`).
   - Total `Invoke-QualificationPostRunAggregation` wall time can exceed 200 ms: I saw 188 to 234 ms with complete stats on 1.6 MB and 313 ms in the D-2 run. Only the reads stop at 200 ms; the sidecar writes come after. The base shows the same pattern (521 ms on its first call).
2. Realism (server2 measurements).
   - 21.5 MB TRX, 5000 results with about 4 KB StdOut each: Summarize completes in 60 to 125 ms (StdOut is skipped by IndexOf) and AGG in 95 to 101 ms with full stats.
   - 1.6 MB TRX, 5000 results with full TUnit-style attributes: Summarize 72 to 151 ms, AGG 188 to 234 ms.
   - Under load the 640 KB witness hit the deadline and was refused (fail-closed).
   - The tag-heavy byte path runs at only about 20 MB/s, so the budget is marginal for real chunks and load-dependent. The fallback is always refusal, never a slow read.
   - `Read-TestPhaseTrxRows` (executed manifest and summarize-test-phases, fresh 200 ms each) refused both the 1.6 MB and 21.5 MB files (201 ms, rows=0). Those consumers therefore gain nothing beyond about 1 MB. This is not a regression: the base refused anything over 256 KiB.
   - Real Windows sizes remain unmeasured.
3. Stats.
   - On the 5000-result and 21.5 MB fixtures, the streamed p50/p95/max/count equal an independent XmlDocument + invariant `TimeSpan.TryParse` oracle (5000 / 2500 / 4750 / 5000).
   - The base cannot be compared on files over 256 KiB, and it refused even a 60 KB, 600-result fixture by timeout.
   - Per-value comparison against the base `[TimeSpan][string]` conversion, run under de-DE culture: identical for valid TRX forms. These cases were checked:
     - c-format
     - `d.hh:mm:ss.f`
     - negative
     - bare `5` (5 days)
     - missing
     - garbage
     - comma decimal (rejected by both)
     - leading space
   - The new parser diverges only on malformed values (A-2).
   - Duplicate ids and names are each counted, the same as the base.
   - Outcome is ignored for ranking, the same as the base.
4. Encoding and test gating.
   - ASCII: both changed scripts are ASCII-only.
   - PS 5.1: see D-1.
   - The strace witness (`launch-path-opens-no-timing-file`, lines 691-760) predates this change and is NOT gated. When strace is missing it asserts `Test-Path $openStrace` and FAILs. It did not run in my environment (no strace), and it also uses mkfifo, chmod and ln without any OS gate (A-3).
5. Red witnesses: done; see the Runs table (base lib, M1 and M2). Every mutation was applied to scratch copies only; the worktree was never edited.

## Advisories
- A-1: no witness covers the in-stream TRX deadline. M2 survives because the unterminated case trips the tag cap first. Add a large, fast-to-produce, slow-to-scan TRX (for example 50k results or many-attribute tags) that must refuse under the 200 ms budget.
- A-2: the `TrySeconds` fast path accepts out-of-range components (`00:75:00` gives 4500, `00:00:60` gives 60, `25:00:00` gives 90000 where TimeSpan.Parse gives 25 days). It accepts fractions longer than 7 digits, and an int overflow on huge hour strings yields garbage. TRX writers never emit these forms, but stats can now diverge on malformed input; range-check, or use TimeSpan.TryParse for anything out of range.
- A-3: the pre-existing strace, mkfifo and chmod rows are ungated on both Windows and strace-less Linux.
- A-4: aggregate memory (the Rows and Lines lists, up to 16 KiB StdOut per row) is bounded only by the 200 ms budget, not by a count cap.
- A-5: StdOut is truncated at 16 KiB, so an `ANTIPHON_TEST_PHASE_ID` marker after 16 KiB of output is now lost; the base kept it within 256 KiB.
- A-6: the JSONL prefilter needs compact `"recordKind":"header"`, so a spaced writer would be dropped.
- A-7: `ReadSpecial` ends a comment at `->` rather than `-->`.
- A-8: the dot-source now Add-Types and warms up through `Update-QualificationChunkTiming`. Load took 4.1 s versus 2.8 s at base on this host, a cost every consumer pays.
- A-9: ties in Slowest now order by unrounded seconds, then ordinal name; the base used rounded seconds, then culture-aware name.

Scratch evidence (not committed): /tmp/claude-1654/-work-worktrees-task-ebe7ba48/a8984774-1a2c-4f20-bedd-eee7141b00c9/scratchpad (qtiming-*.log, tp.log, trx/rev-tp.trx, probe.ps1, dur.ps1, gen.ps1, fx/). Left in place per the brief's no-delete rule: that scratch directory, including the ctl2/red2 trees of about 190 MB each, and tests/**/bin and obj in this worktree, which are gitignored.
