# Review 08ac06f9: CARD-1514 rc coverage rule, expected platform-skip roster

Subject: origin/feat/card-task-2e950cf7 tip `d1579709165b1f43216bd9f163866ad7afa118c2` (Grok Code author).
Round 1, Final, full scope. Base `52e13fdca8dcb780ef4afd3ca9247bc32d28bfe4`, master `0ce91e6a5de9c91cbd57800cddf00543c3abd520`.

**Verdict: CLEAN. No blockers.** I found no way for this change to hide a regression. Advisories are listed below; they go to cards and do not hold the land.

## Reruns at d1579709

| What | Result |
|---|---|
| Build `tests/Antiphon.Tests` (build slot, native output, UseAppHost=false) | exit 0, 168 s |
| TUnit union of the touched classes and the guard batch (build slot) | **59/59 passed**, 0 failed, 0 skipped. Per class: TestClassificationPolicyTests 41, SlowTestRegistryMergeTests 5, LiveLaneRunnerTests 4 (incl. WrapperOnlyPaidManifest), NativeLinuxPolicyWitnessTests 3, SlowTestTripwireTests 2, ExpectedPlatformSkipRosterTests 1, NightlyCoreTests 1, TestLaneCategoryGuardTests 1, TestClassificationGuardTests 1 |
| `test-expected-platform-skips.ps1 -Case Verdict` | 14/14 PASS |
| `test-nightly-core.ps1 -Case CoreMatchesFallback` | 18/18 PASS |
| Red witness at 37bddc551 (git-archive scratch copy) | 7 failed / 7 passed (expected-skip-accepted, prefix-extension, autoload, all-skip-exit-8, passed-with-expected-skip, stale-roster-warns, trx-debug-trace). Matches the claim |
| Live-lane protected-input digest, recomputed independently in node (path, LF, content, NUL; SHA-256) | tip `4bc09f38...dbf74ec` = new pin; master `fb0af076...0e98a3` = old pin. d15797091 changes only the pinned constant |
| check-evidence-diff 52e13fdca..d1579709 | commits=3 entries=0 violations=0, exit 0 |
| merge-tree vs origin/master 0ce91e6a5 | clean, tree f926153e4 |
| ASCII check, all 7 changed files | 0 non-ASCII bytes |
| PS 5.1 syntax | only `::new`, `-cnotin`, `New-Object` generics, and Ordinal StartsWith. All are valid in 5.1 |

The brief quotes an "84-test filter (83+1)". I did not get Code's exact filter, and my union of the named classes and the guard batch is 59 tests. Every named class ran with a nonzero count.

## (1) The verdict function: real data and attacks

The probe (scratchpad `probe.ps1`) calls `ConvertTo-NightlyCoverageVerdict` with the committed roster. It uses the exact DebugTrace texts quoted in the dce78ee0 note.

| Case | Result |
|---|---|
| All 25 rostered methods plus the C1734 tool/script duplicate (26 rows), exit 0 | cc=True tp=True, expectedSkips=26, no reasons |
| Each of the 12 exit-8 single-test chunks | **12/12** tolerated, cc=True tp=True |
| Rostered test, new reason that extends the prefix (`20 missing-context-NEW...`) | accepted (prefix matching, by design; see A-6) |
| Class-level skip: a non-rostered sibling method with the same reason | rejected: unexecuted-outcome, process-exit-8 |
| Method name with an extra suffix, or truncated to a prefix | rejected (identity compare is exact and ordinal) |
| Identity carrying arguments `...(tool)` | rejected. The real TRX identity is `class.method` with no arguments, so a roster entry covers every argument case of that method. Each row is still checked on its own |
| 1 expected skip + 1 failed, exit 8 | testsPassed=false, failed-row and process-exit-8 |
| Expected skip + Unknown outcome, exit 8 | rejected |
| Two expected skips, exit 8 | tolerated |
| Zero rows, exit 8 | rejected: zero-terminal-rows, process-exit-8 |
| Outcome=Skipped | unchanged: `skipped:` makes the run incomplete |
| Platform `Windows` (capitalised) | rejected. The loader also throws on a non-lowercase platform |
| Linux entry while running on Windows | rejected |
| Empty roster `@()` / `{"entries":[]}` | no tolerance: the old fail behaviour |
| NotExecuted with no `Skipped:` trace (crash or not run) | rejected |
| Roster file empty, whitespace, `{}`, `entries:null`, a bare array, bad platform, empty reason, test without a dot, invalid JSON | all **throw**. In `Invoke-AntiphonNightlyTests` the throw is caught, which sets overallFailed and coverageComplete=false (fails closed) |
| Roster file missing | `$expectedSkipRoster=@()`: no tolerance (fails closed). The autoload is not reached, because the impl passes `@()` and not `$null` |

**Exit-8 chunk with an undiscovered test.** The verdict tolerates exit 8 when every TRX row is an expected skip. However, `ConvertTo-NightlyNativeSuiteVerdict` still checks the chunk's selection discovery, which is written from `chunk.nodes`, against the execution diagnostic. A planned UID that never ran gives `missing-expanded-row`, and `missing-required-class` and `missing-execution-diagnostic` work the same way, so coverageComplete=false. The row result stays `FAIL` because the `pass` override requires coverageComplete. `succeeded = -not overallFailed -and coverageComplete` is then false, and `Test-ReleaseGateGreenFlags` needs coverageComplete to be a real true. This is from code reading; I ran no live chunk.

`$exitTolerated` is set back to `$false` at the top of each dot-sourced chunk, so it does not leak into the next chunk.

## (2) Can a candidate's roster hide a regression?

The roster is read from `$RepoRoot`, the candidate checkout, and it is not part of policyHash. An entry absorbs a row only when all of these hold:
- the outcome is NotExecuted,
- the DebugTrace starts with `Skipped: <prefix>`,
- the fully qualified identity matches exactly,
- the platform matches exactly.

The cases that matter:
- **A rostered test that starts failing.** It still fails, because a Failed row is never absorbed.
- **A rostered test whose gate now skips on every platform.** Linux runs have no Linux entries, so they still report unexecuted-outcome.
- **A real failure hidden in the same test the entry names.** This needs the candidate to edit the test source so it skips on Windows with that exact text, and to add the entry. That is a reviewed source change of the same kind as deleting the test or adding `[NativeLinuxOnly]`/WindowsUnitSkipRoster rows. The policy hash is a self-compare inside the run, so it is not a pin either.
- **A new Windows skip that is not on the roster.** It still fails the gate. The asymmetry runs in the safe direction.

A roster entry does mask its own test on Windows by construction: that test never runs on Windows. This is acceptable for these 25. Each is a Linux-only mechanism (inode, inotify, renameat2, bash/flock, PATH shim, sh symlink) or a lane skip (C680), and each skipped on every earlier Windows Full run. Before this change the Windows Full gate could never go green, so no protection is lost.

Minimum extra guard: advisory A-2.

## (3) Stale-roster path and the census

`Get-NightlyExpectedSkipSourceGaps` runs once per run for the current platform and only produces warnings. The `EPS census-matches-source` check (25 unique, 0 gaps) and `census-reports-missing` both pass. The census is a regex heuristic: `class X` and `Method(` must appear in the same `.cs` file, so a partial class split across files gives a false-positive warning. `KnownTestIdentities` in the verdict is wired only by the harness; production never passes it (A-5).

## Advisories (each needs a card; none holds the land)

- **A-1 Tolerated skips are not on the receipt.**
  - Where: nightly-coverage.ps1 `ConvertTo-NightlyNativeSuiteVerdict` return, and nightly-tests-impl.ps1 chunk row.
  - Failure: a green Windows Full summary does not name which roster entries absorbed a NotExecuted row. The `expectedSkips` count is dropped, the row keeps `exitCode=8` next to `result=pass`, and when the run is red for another cause `New-NightlyTitle` (nightly-report.ps1:639) prints "antiphon exit 8" bits.
  - Why: auditability of a gate tolerance.
  - Fix: carry the `expectedSkipIdentities` list and the roster SHA-256 into the chunk row and summary. Skip exit-8 bits when `processExitTolerated` is set.
- **A-2 The roster is unsealed candidate data.**
  - Where: tests/expected-platform-skips.json, census.
  - Failure: an entry plus a source skip in one commit is accepted with no machine check that the reason text is really the method's skip literal.
  - Fix: add a census assertion that `reasonPrefix` appears as a string literal inside the named method body, or in its `[NativeLinuxOnly]` attribute, and record the roster digest in the summary (A-1).
- **A-3 Duplicate inventory.**
  - Where: 10 of the 25 entries (RemoteScriptContract C1105, RollingVolumeRecycle C1008 x2, VolumeRetirementApplyGate x7) are already in `tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs`. That roster has bidirectional closure (`C1210_Roster_closure_matches_source_gates`).
  - Failure: two rosters can drift apart.
  - Fix: generate one from the other, or check that they agree.
- **A-4 C680 is a lane skip, not a platform skip.**
  - What: `20 missing-context` comes from `LiveClaudeRefusal` when no live context is present. It also skips on Linux Full.
  - Fix: add a reason class (`platform`/`lane`) to each entry.
- **A-5 Dead production parameter.** `KnownTestIdentities`/`stale-roster` in the verdict is never wired by the impl.
- **A-6 Prefix matching is loose.** `Skipped:` with no following space is accepted, and any extension of a short prefix (`20 missing-context`, `Claude trust spawn uses a claude symlink to sh.`) is accepted. The exact identity and platform match keeps this harmless.

## Checkpoint receipt

The plan extract is `.antiphon/review-08ac06f9-plan.md`, with one row CP-1 using the same union filter. The run id, verdict and validate line are in the final report.

Run `20261110-071228-7e42`, verdict GREEN exit=0, wall 18m44s (cold build on loaded server2):

    CHECKPOINT CP-1 commit=d1579709165b1f43216bd9f163866ad7afa118c2 build=ok filter=/*/*/(TestLaneCategoryGuardTests*)|(ExpectedPlatformSkipRosterTests*)|(NightlyCoreTests*)|(LiveLaneRunnerTests*)|(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)/* executed=59 passed=59 failed=0 skipped=0 slot=granted waited=60s dirty=0 source=d1579709165b1f43216bd9f163866ad7afa118c2 sourceState=clean buildSource=verified
    CHECKPOINT SOURCE VALID source=d1579709165b1f43216bd9f163866ad7afa118c2 rows=1
