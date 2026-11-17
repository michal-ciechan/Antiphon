# Empty "Failed" TRX chunks (one NotExecuted row, zero executed): cause and verdict effect

Date: 2026-11-17. Investigate task 57e7098c (read-only). Master read: `c3cc0bb9b7b1d0c55464d07cd8e60a63b4239028`.
Stopped run a21d8174 candidate source: `9b98828eb34eb552ce62bcde5a64dd045a872a88` (`C:\Antiphon\releases\checkout-053aa2ea`).
Live run 06863906 candidate source: `870a93d21f2b18c85a8b0905ab689b28e3099813` (`C:\Antiphon\releases\checkout-4b599b91`).
`git diff` between the two candidates and master is empty for `tests/expected-platform-skips.json`,
`scripts/lib/nightly-coverage.ps1` and `scripts/lib/nightly-tests-impl.ps1`. All `file:line` below are at master `c3cc0bb9b`.

## Verdict

**This is not a defect and it does not block a clean run.** Each of the 12 chunks selects exactly one test.
That test has an explicit, intended Windows skip (`Skip.Test(...)` or `RequireNativeLinux()`), so
Microsoft Testing Platform exits with **code 8 ("zero tests ran")**. TUnit's TRX writer then stamps
`ResultSummary outcome="Failed"` and a RunInfo error "Exit code indicates failure: '8'". Every one of the
12 tests is on `tests/expected-platform-skips.json` for `platform: windows`, and each has the exact reason text.
The supervisor therefore **tolerates exit 8**, scores the chunk **`pass`**, and leaves `overallFailed`,
`testsPassed` and `coverageComplete` untouched. I reproduced this offline with the candidate's own verdict
code over the stored evidence (all 12 `result=pass`). A negative control with an empty roster gave
`process-exit-8` plus `unexecuted-outcome` (FAIL).

The `Failed` summary is a cosmetic artefact of TUnit/MTP exit code 8. It matters only to readers or tools
that grep TRX `ResultSummary/@outcome`. The supervisor's verdict does not read that attribute.

## (1) The 12 chunks (stopped run a21d8174)

Logs: `C:\Antiphon\releases\candidates\rc-2026-11-23-9b98828eb34e\logs\2026-11-16-1037-a21d8174`.
Every TRX has the same shape: `Counters total=1 executed=0 passed=0 failed=0 notExecuted=1`,
`ResultSummary outcome="Failed"` and RunInfo `outcome="Error"` "Exit code indicates failure: '8'". The `-tests.log`
says `Test run summary: Zero tests ran ... total: 1 failed: 0 succeeded: 0 skipped: 1`. Each chunk's
`-selection.discovery.json` holds exactly one node with `excluded:false`.

| chunk | test (class.method) | DebugTrace (skip reason) | roster match |
|---|---|---|---|
| all-profile-976 | `Antiphon.Tests.Application.LandGraphEvaluationTests.EvaluationBudgetRefusesSlowDotnet` | Skipped: The slow evaluator is a PATH shim named dotnet; Windows process start resolves dotnet.exe. | windows, exact |
| all-profile-2068 | `Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_extraction_is_closed` | Skipped: CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately. | windows, exact |
| all-profile-2260 | `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1008_Host_proof_admits_only_an_authenticated_private_origin` | (same CARD-1050 text) | windows, exact |
| all-profile-2261 | `Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1008_Host_proof_file_omits_credentials` | (same CARD-1050 text) | windows, exact |
| all-profile-1845 | `Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests.C1717_B2_ReusedInodeIsANewDirectoryIdentity` | Skipped: Inode reuse is the Linux identity; Windows file ids are a separate native witness. | windows, exact |
| all-profile-1846 | `...WorktreeNoFollowDeleteTests.C1717_B3_BytesChangedDuringTheWalkAreKept` | Skipped: The mid-walk receipt checks are the Linux no-follow deleter; Windows needs a native witness. | windows, exact |
| all-profile-1847 | `...WorktreeNoFollowDeleteTests.C1717_B3_LateRepositoryMarkerAboveTheWalkStopsIt` | Skipped: The late-marker walk is the Linux no-follow deleter. | windows, exact |
| all-profile-1848 | `...WorktreeNoFollowDeleteTests.C1717_B3_LateRepositoryMarkerStopsTheWalk` | Skipped: The late-marker walk is the Linux no-follow deleter. | windows, exact |
| all-profile-1849 | `...WorktreeNoFollowDeleteTests.C1717_B3_NameAddedToAnUnwalkedDirectoryHolds` | Skipped: The mid-walk receipt checks are the Linux no-follow deleter; Windows needs a native witness. | windows, exact |
| all-profile-1850 | `...WorktreeNoFollowDeleteTests.C1717_B3_NameAddedToTheWalkedDirectoryHolds` | (same mid-walk text) | windows, exact |
| all-profile-1854 | `...WorktreeNoFollowDeleteTests.C1717_B3_WriteThroughAnOpenHandleDuringTheHashIsKept` | Skipped: The inotify-timed writer is Linux; the Windows deny-write hash is C1717_B3_WindowsOpenWriterRefusesTheDelete. | windows, exact |
| all-profile-1855 | `...WorktreeNoFollowDeleteTests.C1717_D5_ExchangedSymlinkDoesNotDeleteOutsideFiles` | Skipped: The ancestor exchange uses Linux renameat2. | windows, exact |

A full streaming scan of all 1,176 TRX files in the a21d8174 directory found **13** files whose
summary is not `Completed`. Twelve are the rows above (failed=0). The thirteenth is
`antiphon-all-group-135` (failed=1), a real, separate failure. Five other chunks hold NotExecuted rows
(`all-group-5/87/91/111/134`, `session-runner-all-group-5`). They show `Completed`, because at least one
test in each of them executed, so MTP exits 0. Exit 8 occurs only when **every** selected test is skipped.

**Why "Failed":** the cause is the MTP exit code, not a counter or a timeout. TUnit 1.44 / MTP 2.2.2 returns 8 (ZeroTests) when
nothing executed. The TRX reporter turns any non-zero exit into `ResultSummary outcome="Failed"` with an
Error RunInfo. There was no run-level timeout (`runWallSeconds` is about 3 s, and timing.json has `lastPhase: complete`). There
was no empty filter either, because the filter selected exactly one discovered node and that test ran to its skip.

**Where the supervisor turns a TRX into a chunk result:**
- `scripts/lib/nightly-tests-impl.ps1:1523` starts `row.result` as `FAIL` for any non-zero exit.
- `scripts/lib/nightly-tests-impl.ps1:1564-1570` calls `ConvertTo-NightlyNativeSuiteVerdict` with `-ProcessExit`,
  `-Platform` and `-ExpectedSkipRoster`. The roster is loaded at `:2126-2129` from `tests/expected-platform-skips.json`.
- `scripts/lib/nightly-coverage.ps1:2301-2305` passes the TRX rows to `ConvertTo-NightlyCoverageVerdict`.
- `scripts/lib/nightly-coverage.ps1:2004-2018` moves each NotExecuted row that matches a roster entry into
  `expectedSkipRows`. `Find-NightlyExpectedSkipEntry` (`:1925-1943`) requires the exact identity, the platform and
  `DebugTrace == "Skipped: " + reasonPrefix`. `allExpectedSkips` is true when every row is a rostered skip.
- `scripts/lib/nightly-coverage.ps1:2033-2034`: **`if ($ProcessExit -eq 8 -and $allExpectedSkips) { $processExitTolerated = $true }`**.
  Otherwise the code records `process-exit-8` and sets testsPassed=false.
- `scripts/lib/nightly-tests-impl.ps1:1571-1574` sets `exitTolerated` and records `row.processExitTolerated`.
  `:1616-1617` sets `row.result = 'pass'`, and `:1684` skips the `overallFailed` branch because `-not $exitTolerated`.
- The exit-8 tolerance landed in `c2c28776e` "fix: count rostered Windows skips as expected coverage" (2026-11-10).
  `a7b248c93` (CARD-1828) added the tolerated-skip report and the exact skip-text and roster-drift checks.

**Reproduction:** `ConvertTo-NightlyNativeSuiteVerdict` from `9b98828e` (extracted to scratch, not the live
checkout), run over each chunk's TRX, selection discovery and execution `.diag` with `-ProcessExit 8 -Platform windows -ProfileAware`
and the candidate roster, gave this for all 12: `coverageComplete=True testsPassed=True processExitTolerated=True toleratedSkips=1 reasons=[]`, so `result=pass`.
Negative control (same input, empty roster): `coverageComplete=False testsPassed=False`,
`reasons=[unexecuted-outcome:...C1717_B2_ReusedInodeIsANewDirectoryIdentity; process-exit-8]`.

**Linux:** not measured. I read no Linux TRX for these tests in this time box. All 12 are gated by
`!OperatingSystem.IsLinux()` / `C1008HostFixture.RequireNativeLinux()` (for example
`tests/Antiphon.Tests/Infrastructure/WorktreeNoFollowDeleteTests.cs:102-104` and `:313-315`, and
`tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:2791`, `:2996`). By design they execute on Linux.
Neither the a21d8174 nor the live failed-first seed (`prior-failures.json`) names any of them.

## (2) Defect or intended?

They are intended skips. Each one is an explicit in-test platform gate with a rostered reason. None is a filter or
selection bug, and none is a real failure hidden by zero executed tests: the skip fires before the body runs.
- The four older chunks were already recorded as "not failures" in
  `docs/investigations/2026-11-16-run-160200cb-stopped.md` (origin/feat/card-task-495cb51f).
- The 8 "new" C1717 chunks are not new behaviour. In run 160200cb, `all-profile-1845..1855` appear in the
  list of chunks still to run, so that run never reached them before it stopped. a21d8174 is the first run that executed them.
- One real gap is named in the skip text. CARD-1717 (S4a audit B, **Review**, Critical/Now) says "native Windows ... still owed".
  The B2, B3 and D5 witnesses run on Linux only. On Windows the only native witness is
  `C1717_B3_WindowsOpenWriterRefusesTheDelete`. The skip is honest, but it leaves Windows D-5/B3 coverage open on that card.
  That is a coverage obligation and not a qualification failure.

## (3) Effect on a Full run's final verdict

A tolerated chunk does **not** stop a run from being clean:
- `summary.json` gets `succeeded = (-not $overallFailed -and $coverageComplete)` (`nightly-tests-impl.ps1:2250`).
  Neither input changes for a tolerated chunk (see (1)). `outcome` stays green, `reasons` gets no entry, and the
  skip appears only in the CARD-1828 tolerated-skip report and in `warnings` when the roster is stale.
- The publication terminal is `failed-run` only when `overallFailed` (`:2204-2205`), so it is unaffected.
- The stopped-run seed export counts a NotExecuted-only chunk with summary `Failed` as finished
  (`scripts/lib/nightly-stopped-run-seed.ps1:362-366`). This matches the orchestrator's observation.

Consumers that would **not** treat them as clean (latent today, so it does not block the current gate):
- `scripts/lib/rc-admission.ps1:186-190` (pass-reuse admission) requires `ResultSummary outcome="Completed"`
  and `notExecuted=0`. These chunks are never admitted for reuse, so a later candidate always re-runs them.
  The cost is about 3 s each. This does not mark a failure.
- `scripts/lib/release-authority.ps1:841-842` (`chunk-exit` when exitCode is not 0), `:862` (`chunk-zero-executed`) and
  `:865-867` (`uid-not-passed` for any required UID that is not Passed). The required UIDs subtract only profile exclusions
  (`:351-364`), not the expected-skip roster. No production script writes the `authority/ledger.json` this code reads.
  The only callers are `publish-release.ps1` and `promote-release.ps1`, and the a21d8174 candidate root has no `authority/`.
  If that D-15 ledger is ever wired to qualification runs, these 12 chunks (and every rostered skip) will block publication.
  This is a real latent inconsistency between the two gates.

## (4) Live run 06863906

Logs: `C:\Antiphon\releases\candidates\rc-2026-11-24-870a93d21f2b\logs\2026-11-17-0029-06863906`.
A streaming scan at 2026-11-17T03:05Z found **68 TRX files, every summary `Completed`, and notExecuted=0 in all of them**.
No chunk of this shape has run yet. `dispatch-schedule.json` places `all-profile-1845..1855` at dispatch ordinals
192-202, `all-profile-976` at 775, and `all-profile-2260/2261` at 1150/1151.

**Prediction:** if these chunk ids still select the same single tests (the roster, verdict code and test sources do not differ
between `9b98828e` and `870a93d2`), each of 1845-1850, 1854, 1855, 976, 2068, 2260 and 2261 will again produce a TRX with
`outcome="Failed"`, exit 8 and one NotExecuted row. The supervisor will score each one `pass`
(`processExitTolerated=true`), list it under tolerated skips, and leave `succeeded` unaffected. 1851-1853 are not
roster entries and should pass normally.
Uncertainty: chunk ids come from the run's own profile discovery. I did not confirm the live mapping of id to test because the
per-chunk `-selection.discovery.json` is written only when each chunk runs.

## (5) Recommendations (ranked; nothing implemented)

1. **No change needed for the current rc gate.** A run can be fully clean with these 12 chunks present. Readers and
   watchers should judge a chunk by the supervisor verdict (`processExitTolerated`/tolerated-skip list) or by
   TRX `failed`>0, never by TRX `ResultSummary/@outcome`. Evidence needed: none beyond this report. Optionally, a one-line doc note
   in `docs/release-gates.md` near line 310.
2. **Align `release-authority.ps1` with the roster before the D-15 ledger is wired.** In
   `Test-ReleaseGateExecutionLedger`, subtract roster entries matching `platform` from the required UIDs or accept them as expected-skip,
   and accept exit 8 under the same `allExpectedSkips` rule (`release-authority.ps1:841-842`, `:862`, `:865-867`).
   Evidence needed: a red `scripts/test-release-gate.ps1` variant with a rostered NotExecuted UID and exit 8, then green.
3. **Optional cosmetic/cost fix: do not dispatch a chunk whose whole selection is a rostered skip on this platform.**
   Fold it into a neighbouring chunk so that something executes, or record it as tolerated without launching. This would remove 12
   test-host launches and the misleading `Failed` summaries. It would also let rc-admission reuse these chunks
   (`rc-admission.ps1:186-190`). Evidence needed: a planner test plus proof that the census and tolerated-skip list still name each UID.
4. **Coverage, not qualification:** the Windows native witness for C1717 B2/B3/D5 is owed on CARD-1717 (Review). Track it there.
   Do not try to make these tests "run" on Windows.

Not done, noted: none of the above implemented (Investigate is read-only).
