# Final Review 4c — qualification timing observability (subject 6dcf5337)

Reviewed tip fe47b985bc34b31af38f7817ad852edb2f705e75 (origin/feat/card-task-6dcf5337), read-only, server2/Linux.
Subject range: 9f00008facfc48e75555c08df59887019b3f8dfe..fe47b985 (11 commits, 9 files: scripts + 2 docs).
The stated base f3e302eb is not an ancestor. The branch was cut from rc at 9f00008f. The rc commits since then (through 3fe85aa3e) touch no file in the subject's diff, so the land rebase has no textual overlap. merge-tree was not run, per the operator note.

Verdict: no regression and no new fail-open. No findings. Advisories only.

## Priority answers

1. **Timing reads before the chunk exit.** None. All 6 pre-exit `Update-QualificationChunkTiming` calls are non-terminal: the heartbeat, the launch-failure catch, the discovery-failure catch, e2e prereq, and unstarted. A non-terminal call reads nothing. Its only filesystem touch is the `Write-TestPhaseTextAtomic` lstat, and it skips a FIFO or a symlink. The single `-Terminal` call runs after `$row`, `Set-NightlyOutcome` and the exit code are fixed. It sits inside a function-wide `try/catch {}` and assigns no caller state, so it cannot change the exit, result or coverage. On Linux, bounded reads use lstat, then `open(O_RDONLY|O_NONBLOCK|O_CLOEXEC|O_NOFOLLOW)` (657408 is correct for x86_64), then an fstat regular-file check. They stop at 256 KiB, 4000 lines (and a comma/colon density limit of 4000) and 200 ms on one stopwatch. A refused read nulls every phase field and sets `why=timing-read-refused` only when `why` is blank.
2. **Windows launch path.** `Invoke-NightlyOwnedProcess` is byte-identical to base. In `nightly-owned-process.ps1` the only change is the untyped `$Hooks`; its sole caller, `test-nightly-native.ps1`, passes a NativeProcessHooks or `$null`. When the `.exe` exists, `Resolve-NightlyTestHost` returns the same `tests\<A>\bin\Debug\net9.0\<A>.exe` with an empty prefix, so the native args are unchanged. The discovery hash is unchanged too: `Get-NightlyAssemblyHash` already preferred the `.dll`. See A2 and A3 for the deliberate non-launcher changes on Windows.
3. **Sealed timing root / replay.** Consistent.
   - `release-cut` copies the sealed `timingRoot`, including a blank one. It throws `selection-attempt-conflict` only when the caller passes a different non-blank root.
   - With a sealed blank root, `nightly-run-impl` does not resolve, because `SelectionAttemptPath` is set. `nightly-tests` sends `TimingRoot=''` into the hop check (`release-selection.ps1:359`, compares `'' -ceq ''`). It then resolves `<LogRoot>/timing-<RunId>` only for writes.
   - The summary fills `selection.timingRoot` in memory only. The sealed attempt file is not rewritten.
   - So the blank root recorded by run 8f055f87 replays safely. Witnessed by `blank-sealed-replay-preserves-attempt`, `explicit-timing-root-is-sealed` and `absent-timing-root-defaults-for-new-attempt`.
4. **ASCII.** All 7 changed scripts are ASCII-only (grep for non-ASCII bytes found none).
5. **Red witnesses.**
   - The caps and the hostile-file guarantees have real assertions: `post-run-hostile-files-keep-exit-inside-budget` covers a FIFO TRX, a 100 MB line, a chmod-000 file, a symlink, a FIFO shard and a dense sidecar, and asserts under 0.2 s, exit 7 kept and `why`. Also the `*-inside-budget` rows and `launch-path-opens-no-timing-file` (strace openat).
   - Gap: see A1. No mutation was run, because the review is read-only.

## Runs at fe47b985 (all through scripts/build-slot.ps1)

| Run | Result |
|---|---|
| `scripts/test-qualification-timing.ps1` | exit 0, 66 PASS / 0 FAIL, 47 s |
| `dotnet build tests/Antiphon.Tests` to `bin-r101/` | 0 errors, 504 warnings, 3m02s |
| Nine classes, from that one output with no rebuild | 72 total, 71 passed, 0 failed, 1 skipped, 3 min |
| `check-evidence-diff.ps1` (9f00008f..fe47b985) | 11 commits, 0 violations |

Per-class counts for the nine classes: NightlyVerificationContractTests 38, NightlyExecutionDiagnosticTests 7, TestPhaseSummaryTests 7, NightlyDiscoveryParserTests 6, NightlyScriptsTests 5, C1514LandingSplitBudgetTests 4, NightlyPrerequisiteTests 2, TestPhaseSupervisorTests 2, TestPhaseCustodyTests 1. The skipped test is `C1514T_PS51AndRerun`, which is Windows-only by design.

The brief does not name its nine classes. I chose the nine Antiphon.Tests classes that exercise the changed scripts.

No whole Unit lane was run, per the brief. All PCs stay pending for SourceLanding Mutation. Windows CP-8 is still owed.

Evidence is in `.antiphon/r101/` (gitignored): `qualtiming.log`, `build.log`, `classes.log` and `trx/`. Nothing was deleted, per the operator note. The build output `tests/Antiphon.Tests/bin-r101/` is left for settlement.

## Advisories (not regressions)

- **A1 — ordering witness gap.** No test pins that the real heartbeat call in `Invoke-AntiphonNightlyTests` (`nightly-tests-impl.ps1:1153`) is non-terminal.
  - `fifo-sidecar-does-not-delay-launch` would stay green if that call gained `-Terminal`, because the bounded readers refuse a FIFO by lstat.
  - `launch-path-opens-no-timing-file` calls `Update-QualificationChunkTiming` directly rather than through the nightly loop.
  - Suggested PC for Mutation: add `-Terminal` at line 1153 and assert that the pre-launch sidecar has no `why=timing-read-refused` and no TRX open.
- **A2 — real chunks will mostly read as refused.** A 72-test TRX here is 103,650 bytes, about 1.4 KB per test, so the 256 KiB cap is roughly 180 tests. Real nightly Antiphon.Tests chunks are larger. Most native chunks will record `why=timing-read-refused`, with null p50/p95/slowest and null phase sums, because a TRX refusal also nulls the JSONL phase fields. This is fail-closed, but the observability goal is mostly unmet on real data. The first Windows nightly should confirm this before anyone relies on `timingChunks`.
- **A3 — changes on the Windows nightly path outside the launcher.**
  - The phase recorder env is now enabled for profile `nightly` as well as `rc`. This is intended and documented, and it is overhead for every native child, so the CP-8 overhead preflight matters.
  - The rc phase directory moved from `<LogRoot>/timing` to `<LogRoot>/timing-<RunId>`. `Get-TestPhaseTimingDirectory` follows it.
  - `Resolve-NightlyTestHost` falls back to `dotnet exec <dll>` when the apphost `.exe` is missing. Before, that launch failed.
  - `summary.json` `selection` is now round-tripped through `ConvertTo-Json -Depth 8`.
- **A4 — minor.**
  - Every dot-source of `test-phase-evidence.ps1` runs `Initialize-TestPhaseReadWarmup`, which writes and removes a 256 KiB temp file.
  - `Get-QualificationPhaseSums` lists the timing directory with `Get-ChildItem` before any budget check. The listing itself is unbounded.
  - `$script:QualificationTimingRoot` persists in the session after `Invoke-AntiphonNightlyTests` returns.
- **Known gap, not re-reported.** Linux supervisor-death custody, as documented.

## Process notes

- I did not use `git merge-tree --write-tree`, `git worktree add`, any delete command, or any mutation.
- The review ran in place in my own worktree, which is already at the exact subject SHA. I did not create a scratch detached worktree.
- `reviewedSourceClean` is false only because there is no checkpoint-tool receipt with build provenance; the brief prescribed build-slot runs. Every run was green at fe47b985.
