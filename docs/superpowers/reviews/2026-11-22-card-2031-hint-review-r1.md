# CARD-2031 review round 1

Final Review of the desktop runner SHA-stamp hint. Reviewed source is Code task
`3a33b568-172c-4618-baa6-4d562535792e` at
`e005e6e61d32ce3c856a4d30b27150f1806ce446`, range
`e0e747772967f8ed4e2085e79aac21c4b056fa3b..e005e6e61d32ce3c856a4d30b27150f1806ce446`.
Verdict: clean. No product blocker. Positive controls stay pending. Land the Code
owner with `-RecoverReviewedSource` because that task missed its window.

## Behavior

`scripts/session-runner-restart-health.ps1` prints one warning only after a
healthy result, only when `-Hard` is absent, and only when `ProbeBuild` returns
a `build` object whose `commitSha` is not a 40-hex SHA and the listener identity
still matches `result.runner`. The warning is `Write-Host`. The function returns
the same result object. `scripts/restart-session-runner.ps1` still exits
`$result.exitCode`.

A missing body, a non-200 status, invalid JSON, a thrown probe, or an identity
mismatch is swallowed by the empty catch or skipped by the build-object test.
Those paths leave the outcome and exit code alone. Production `ProbeBuild` calls
`GET http://127.0.0.1:17204/capabilities` with `CancelAfter` of 1000 ms on
`GetAsync`, `ResponseContentRead`, and a 65536-byte buffer. The health probe
path is unchanged. A full 40-hex SHA does not warn, including a SHA that is not
HEAD. `-Hard` does not enter the hint. The runbook and bootstrap both require
`pwsh -NoProfile -File scripts/restart-session-runner.ps1 -Hard` and tell the
operator to verify `build.commitSha` against checkout HEAD with
`scripts/check-daemon-build.ps1`. Neither new paragraph instructs `-KillSessions`.

Added script and test bytes are ASCII. The new PowerShell does not use a
ternary, `??`, `?.`, `&&`, `||`, `-Parallel`, `ConvertFrom-Json -AsHashtable`,
or `Join-String`.

## Checkpoints

CP-1 and CP-2 were not separate invocations. At this SHA their filters are
subsets of CP-3 and CP-5. The first CP-3/CP-4 run is environmental and is not
the verdict: `/usr/local/bin/pwsh` is a symlink, and the fixture resolver
requires a regular file. The rerun put `/opt/powershell` on `PATH`. That rerun
supersedes CP-3 and CP-4. CP-5 through CP-8 are from the first run and were
already green.

Run `20261122-015941-eb9a` (CP-3 and CP-4 superseded; CP-5 through CP-8 authoritative). Source clean, `buildSource=verified`. Verdict RED exit 1 from CP-3 and CP-4 only.

```
CHECKPOINT CP-3 commit=e005e6e61d32ce3c856a4d30b27150f1806ce446 build=ok filter=/*/*/RunnerRestartScriptCompatibilityTests/* executed=29 passed=0 failed=29 skipped=21 trx=/work/worktrees/ws-10-d2/.antiphon/checkpoints/20261122-015941-eb9a/rows/CP-3/run.trx slot=granted waited=30s dirty=0 source=e005e6e61d32ce3c856a4d30b27150f1806ce446 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=e005e6e61d32ce3c856a4d30b27150f1806ce446 build=reused filter=/*/*/(RunnerRestartHealthTests*)|(RunnerRestartPreflightSafetyTests*)|(RunnerRestartPreflightCacheTests*)|(ProcessSpawnLimitTests*)/* executed=97 passed=33 failed=64 skipped=17 trx=/work/worktrees/ws-10-d2/.antiphon/checkpoints/20261122-015941-eb9a/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=e005e6e61d32ce3c856a4d30b27150f1806ce446 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=e005e6e61d32ce3c856a4d30b27150f1806ce446 build=ok filter=/*/*/(DockerStackDocumentationTests*)|(GrokRunnerImageContractTests*)|(StandingPipelinePolicyDocumentationTests*)|(ZombieCensusPlatformTests*)|(KeepSixDocumentationTests*)|(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(ProcessSpawnLimitTests*)/* executed=212 passed=212 failed=0 skipped=1 trx=/work/worktrees/ws-10-d2/.antiphon/checkpoints/20261122-015941-eb9a/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=e005e6e61d32ce3c856a4d30b27150f1806ce446 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=e005e6e61d32ce3c856a4d30b27150f1806ce446 build=reused filter=/*/*/DerivedImpactIndexTests/* executed=27 passed=27 failed=0 skipped=0 trx=/work/worktrees/ws-10-d2/.antiphon/checkpoints/20261122-015941-eb9a/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=e005e6e61d32ce3c856a4d30b27150f1806ce446 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=e005e6e61d32ce3c856a4d30b27150f1806ce446 build=reused filter=/*/*/(IncrementalSelectionSoundnessTests*)|(LandGeneratedArtifactTests*)|(RcInputManifestTests*)|(RcInputOwnersMapTests*)|(ReleaseImpactEnvelopeTests*)/* executed=58 passed=58 failed=0 skipped=0 trx=/work/worktrees/ws-10-d2/.antiphon/checkpoints/20261122-015941-eb9a/rows/CP-8/run.trx slot=granted waited=15s dirty=0 source=e005e6e61d32ce3c856a4d30b27150f1806ce446 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=e005e6e61d32ce3c856a4d30b27150f1806ce446 build=ok filter=/*/*/ProcessSpawnLimitTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/ws-10-d2/.antiphon/checkpoints/20261122-015941-eb9a/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=e005e6e61d32ce3c856a4d30b27150f1806ce446 sourceState=clean buildSource=verified
```

Run `20261122-022713-81f0` (authoritative CP-3 and CP-4, `PATH=/opt/powershell`). Verdict GREEN exit 0.

```
CHECKPOINT CP-3 commit=e005e6e61d32ce3c856a4d30b27150f1806ce446 build=ok filter=/*/*/RunnerRestartScriptCompatibilityTests/* executed=29 passed=29 failed=0 skipped=21 trx=/work/worktrees/ws-10-d2/.antiphon/checkpoints/20261122-022713-81f0/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=e005e6e61d32ce3c856a4d30b27150f1806ce446 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=e005e6e61d32ce3c856a4d30b27150f1806ce446 build=reused filter=/*/*/(RunnerRestartHealthTests*)|(RunnerRestartPreflightSafetyTests*)|(RunnerRestartPreflightCacheTests*)|(ProcessSpawnLimitTests*)/* executed=96 passed=96 failed=0 skipped=18 trx=/work/worktrees/ws-10-d2/.antiphon/checkpoints/20261122-022713-81f0/rows/CP-4/run.trx slot=granted waited=45s dirty=0 source=e005e6e61d32ce3c856a4d30b27150f1806ce446 sourceState=clean buildSource=verified
```

Minima met on the authoritative rows: CP-3 29, CP-4 96, CP-5 212, CP-6 27, CP-7 2, CP-8 58. CP-5's one skip is `Windows_registration_uses_the_wmi_census`. That is not a Linux pass of the Windows registration branch. CP-3's 21 skips include the `powershell.exe` rows. Those stay native pending, as the plan says.

## Attack

One scratch mutant removed the unstamped check in `session-runner-restart-health.ps1` and ran `Sha_stamp_hint_is_advisory` through the build slot (`PATH=/opt/powershell`, `--timeout 570s`). Result: total 12, failed 4, succeeded 7, skipped 1. The four failures are unknown, null, and short stamps on a soft restart, plus unknown on wait-only: each expected 1 warning and got 0. The printed result JSON on those failures still had `"outcome":"healthy"` and `"exitCode":0`. The `powershell.exe` row skipped. The file was restored with `git checkout -- scripts/session-runner-restart-health.ps1`. The tree was clean afterward. PC-1 and PC-2 remain pending for SourceLanding Mutation.

## Generated checks

- Derived index driver built under the slot (`UseAppHost=false`, native output). Two `-NoBuild` regenerations both printed `derived-impact-index unchanged`. `tests/derived-impact-index.json` stayed clean.
- `scripts/antiphon evidence-diff --base e0e747772967f8ed4e2085e79aac21c4b056fa3b --head e005e6e61d32ce3c856a4d30b27150f1806ce446` exited 0: commits=4, entries=0, violations=0.
- `scripts/audit-land-verification-graph.ps1 -Check` exited 0: `approved`, digest `A9BB27C595EA171463F0ED13725E198AA64385B1744B9E0BA2D211829C65A7CF`, count 80. The diff does not change land-graph pin files or nightly policy inputs, so those pins were not recomputed.
- `git merge-tree --write-tree` against fetched `origin/master` `6f6a789e07f031496ae99e58ee6e563d30837a64` exited 0, tree `3ba07064d200da0559f4439b90127deda0c2b836`.
- No migration file is in the range. Downgrade selector count is 0.
- OS-witness lint exited 0 for base `e0e747772967f8ed4e2085e79aac21c4b056fa3b` and head `e005e6e61d32ce3c856a4d30b27150f1806ce446`. The only candidate is the pre-existing `OperatingSystem.IsWindows()` hang skip (base line 39, head line 82), disposition non-behavior. The new hint is portable. Windows PowerShell and Windows TCP owner cases remain native pending. The lint does not prove runtime identity.

## Advisories

These do not hold the land.

- On this host, restart-fixture runs need `/opt/powershell` on `PATH` because `/usr/local/bin/pwsh` is a symlink. That is the existing resolver rule, already recorded for earlier cards. The product hint does not depend on it.
- The warning text says "first restart" on every later soft unstamped result. The plan asks for one hint per such result, not a once-only latch.
- `ReadAsStringAsync` has no token of its own. The 1000 ms token is on `GetAsync` with `ResponseContentRead`, so a hung body is inside that cancelled call. `Hung_http_response` nulls `ProbeBuild`, so a mutant that drops only the 1000 ms argument would not be caught by that test.

## Review evidence

```
subjectTaskId: 3a33b568-172c-4618-baa6-4d562535792e
reviewedSourceSha: e005e6e61d32ce3c856a4d30b27150f1806ce446
reviewedSourceClean: true
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: e0e747772967f8ed4e2085e79aac21c4b056fa3b..e005e6e61d32ce3c856a4d30b27150f1806ce446
```
