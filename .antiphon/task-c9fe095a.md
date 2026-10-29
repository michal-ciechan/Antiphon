# Land-graph audit round 3 (c9fe095a)

Outcome: the witness-clock fail-open is removed. Nothing on the production script or the tool command line can skip evaluation or print `approved` without both MSBuild passes and the canary copy. The scratch `-Check` left every tracked file byte-identical and left no canary.

Branch `feat/card-task-c9fe095a`, fast-forward from `5ab632c73fca93ed794f4db2d58d0dd78abb6b72`.

## Commits

- `653e01c86fe74a8aa75d452d9122deb1b6e8a463` `fix: refuse land-graph approval without evaluation`
- `f038db25fcf91effa74cf25c6e5d1bbbf0e95e93` `fix: refresh the land-graph approval for the audit core project`

The second commit is the regenerated manifest. The new `tools/Antiphon.LandGraphAudit.Core` project is a land-graph definition input (`*.csproj`), so the digest moved from count 65 to count 66. That refresh is required, not a new defect. Digest `841DD41B42D127CC768ABEC521683496D2C385ECFE753A4E725FEB5ADD54D052`, count `66`, SDK `10.0.403` (this host; `global.json` pins `10.0.204` with `rollForward: latestMinor`), pins blob unchanged `749d65387b4e9ae7f84fba8c7b6f42658b430dc7`.

Diff `5ab632c73..f038db25`: 18 files, +808 / -313.

## D-1

`-WitnessClock` is gone from `scripts/audit-land-verification-graph.ps1`. The tool no longer accepts `--witness-clocks`, `--pass-budget-ms`, `--copy-budget-ms`, `--hold-pass1-ms`, `--hold-copy-ms`, `--hold-pass2-ms`, or `--touch-canary`. An unknown argument exits 2 with `unknown or incomplete argument` before MSBuild and does not print `approved`. `RemovedWitnessArgumentsCannotApprove` passed for each of those flags.

`approved` is printed only by `AuditPhases.Run` in `tools/Antiphon.LandGraphAudit.Core/PhaseClock.cs`, after pass 1, the copy, and pass 2 each return 0. Tests call that method with `ManualPhaseClock` and evaluator delegates. The fake replaces the evaluation work and the clock. It does not decide approval.

Library results, all Passed:

- `PerPassClocksApproveWhenEachPassUsesSeventyPercent`
- `Pass1TimeoutExits2AndNamesThePass`
- `Pass2TimeoutExits2NamesThePassAndRemovesCanary`
- `CopyTimeoutExits2NamesTheCopyAndRemovesCanary`
- `RefusingEvaluatorIsNotApproved`

`EvaluationBudgetRefusesSlowDotnet` passed. The shim prints the SDK for `--version`, otherwise sleeps, prints `refused`, and exits 1. Its body does not contain `approved`.

`RefusingFixtureRejectsEveryRemainingFlag` passed (47.5 s). On the PackageTags fixture that reads a docs file, every remaining flag combination (`-Check` / `-Update`, `-ToolPath`, `-EvaluationBudgetSeconds`) exits non-zero, leaves both approval records byte-identical, and does not print `approved`. The script rejects an unknown `-WitnessClock`. The only `GetEnvironmentVariable` literal in the tool is `NUGET_PACKAGES`, and setting it does not change the decision on `-Check` or `-Update`. `ProductionSurfaceHasNoWitnessClock` passed.

`-EvaluationBudgetSeconds` still only overrides the script wait. It does not skip evaluation and does not approve by itself. Timeout still exits 2. The script still maps a non-zero tool exit to `audit refused` and exits 1.

## D-2 mutation red/green

`EachCanaryGuardIsNecessary` passed. For each guard the real script kept the sentinel, and a scratch copy of the script with that one check replaced by `if ($false) { return }` deleted only that unique victim:

| Guard | Real script | Mutated scratch copy |
|---|---|---|
| name | kept the uppercase-hex canary sentinel | deleted that victim |
| parent | kept the canary outside the expected parent | deleted that victim |
| rebuilt | kept the trailing-separator record's sentinel | deleted that victim |
| repo-root | kept the sentinel when the record was the repo root itself | deleted that victim |

`RecordedCanaryDeleteRejectsUnsafeRecords` passed. Each of these deletes nothing and the sentinel survives: repo root, empty, whitespace, the canary's parent, `/`, a directory outside the expected parent, a trailing separator, uppercase hex, an injected newline, and a symlink (Linux `Directory.CreateSymbolicLink`; Windows falls back to `cmd mklink /J` inside the same test, with `SkipTestException` only for the slow-dotnet shim). The symlink case unlinks only the link. The target sentinel remains. `RecordedCanaryDeleteRemovesOnlyTheExactCanary` passed, including a `..` path that normalizes to the exact canary.

## Windows copy budget (provisional)

Knob: `EvaluationBudget.WindowsCopyMeasurementMultiplier = 5` in `tools/Antiphon.LandGraphAudit/EvaluationBudget.cs`.

Linux copy p95 is `MeasuredCopyP95Milliseconds = 2253` (samples 1957, 2253, 2249 ms on 2026-10-29). Linux budget is twice that, rounded up: 5 s. Windows copy budget is 5 times that Linux measurement, rounded up: `(2253 * 5 + 999) / 1000 = 12` s. Documented as PROVISIONAL in `EvaluationBudget.cs` and `docs/orchestration-loop.md` until a native Windows plain-copy p95 replaces it. Windows pass floor stays `WindowsPassFloorSeconds = 120`. Tool ceiling is pass * 2 + copy + `StartupGraceSeconds` (60): Linux 155 s, Windows today 312 s.

## Other advisories

The unused `RealPath(string, Dictionary<string, string>?)` overload is removed. `CanaryRepository.DeleteRoot` logs `canary delete failed` instead of swallowing the error. Stale canary directories left when the audit process itself dies are not swept. A sweep would be a new destructive path and was not added.

## Tests

One slot build, `UseAppHost=false`, `--property:OutputPath=bin-c9fe095a/`. Lease `2f310e4b-df50-4142-b86e-049995f7becb`, 0 errors, 1368 pre-existing warnings. Tests then `dotnet exec` of `tests/Antiphon.Tests/bin-c9fe095a/Antiphon.Tests.dll` through `scripts/build-slot.ps1`. Lease `1dfa4c6a-0d82-4428-9c98-382499ae49bf`, waited 886 s, held 1566 s, `--maximum-parallel-tests 6`.

Filter:

```
/*/*/(LandGraphEvaluationTests*)|(LandNonCodeSkipTests*)|(LandGraphAuditCleanupTests*)|(RcRosterRepairTests*)|(WindowsUnitPlatformAdmissionTests*)|(C1514LandingSplitBudgetTests*)/*
```

TRX `/tmp/c9fe-tests/c9fe.trx`: total 255, executed 254, passed 254, failed 0, notExecuted 1. The skip is `C1210_Windows_linux_only_outcomes_are_exact` (Windows placement). `C1210_Roster_closure_matches_source_gates` passed. `C1483_S5_CheckRequiresEvaluationRecord` passed against the committed digest. No native Windows tests were run.

## Scratch clone

`git clone --local` of this worktree to `/work/c9fe095a-audit-clone` (same filesystem as `/work/repos/antiphon/.git`). A `/tmp` clone failed earlier with `Invalid cross-device link` and was not used.

HEAD `f038db25fcf91effa74cf25c6e5d1bbbf0e95e93`. Tracked-file hash before and after, 9143 files:

`bfaddc159648b8f14ac24399c370b4fd53ef73b4f4d5a7c7786393a2c6355491`

`git status --porcelain` empty before and after. `-Check` through build-slot, lease `52378d02-4812-451f-868e-3895b5b4db48`, waited 481 s, held 29 s, exit 0. Output: digest `841DD41B42D127CC768ABEC521683496D2C385ECFE753A4E725FEB5ADD54D052` count `66`, `phase=pass2`, `approved`. Canary `/work/antiphon-landgraph-canary-ba42fa94ce214dcbbe29e86c0ddee89c` was removed by the script (`CANARY_GONE`). The clone and every `bin-c9fe095a` directory were then deleted by literal path.

## Merge-tree (report only; not merged)

`origin/master` `f8bddb6efda7b0e8f50f3b45744087e52188bc66`: `git merge-tree --write-tree origin/master HEAD` exit 1. Conflicts:

- `.antiphon/task-af2b78c8.md` (add/add)
- `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md` (add/add)
- `docs/orchestration-loop.md` (content)
- `scripts/audit-land-verification-graph.ps1` (content)
- `scripts/lib/test-phase-evidence.ps1` (content)
- `scripts/test-scripts-lane-repairs.ps1` (add/add)
- `server/Application/Services/LandVerificationBuildGraph.evaluation.txt` (content)
- `server/Application/Services/LandVerificationBuildGraph.manifest.txt` (content)
- `tests/Antiphon.Tests/Application/LandGraphEvaluationScriptTests.cs` (content)
- `tests/Antiphon.Tests/Scripts/ScriptsLaneRepairTests.cs` (add/add)

The first stdout line was tree `e228f1f94b8d515459f12d3ac5911d0592136745`. That tree is conflicted. Do not land it. Several conflicts are files this task did not author; the branch is based on rc and master has diverged. Master was not merged in.

`origin/rc/2026-10-27` `91b45006b4d76f144c2651180039aece5a9d7938`: `git merge-tree --write-tree origin/rc/2026-10-27 HEAD` exit 0, clean tree `7f8b39071519b63325662a373f42fbd99e4c403d`.

No new defect appeared. No tags, no publish, no rebase, no force-push.

--- next stage ---
next: review
handoff: review the fail-open removal on feat/card-task-c9fe095a
artifact: .antiphon/task-c9fe095a.md
