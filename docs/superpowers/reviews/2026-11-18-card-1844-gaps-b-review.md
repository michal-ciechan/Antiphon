# CARD-1844 part B Final Review, round 1

Verdict: clean. The phase 4 restart sets `ANTIPHON_SKIP_DEV_POSTGRES=1`, a post-phase-5 desktop postgres that is not `exited` fails closed at exit 2 with the manual stop command, `-AcceptDataLossRollback` bypasses only an unreadable server2 side, and phase 6 writes `desktop` into the backup plan's snapshot-target path. No blocker.

- Reviewed source: `97b49c3ba422a2f7ee09ad9cd7ef32328b8eb23b`
- Code owner: `6a008315-ebd5-4dbb-8cf9-01098a091494`
- Base: `a95ebc4439d0789a1d3523d50b4ecb7daaebf65a` (already clean; this round reads only the range above it)
- `origin/master` pin for the merge check: `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4`
- Range: `a95ebc4439d0789a1d3523d50b4ecb7daaebf65a..97b49c3ba422a2f7ee09ad9cd7ef32328b8eb23b`
- Diff: 3 files, +456/−20. `scripts/cutover-postgres-server2.ps1`, `scripts/test-cutover-postgres-server2.ps1`, `docs/superpowers/plans/2026-11-18-postgres-gaps-b-checkpoints.md`.

## Behaviour that holds

G-1. `$script:CutoverSkipDevPostgresVariable` is `ANTIPHON_SKIP_DEV_POSTGRES`, the same name as `$script:DevPostgresSkipVariable` on part A tip `2679a4db8c483c1fd391422472e3a22fc933e96d` (`scripts/lib/dev-postgres-compose.ps1`). Phase 4 calls `Invoke-CutoverRestart -ExpectedSha $RcTip -SkipDevPostgres`, which sets that process variable to `1`. StopOnly, the old-string restart, and the rollback restart clear it. `restart-apphost.ps1` on that part A tip launches `dev-aspire.ps1`, which dotsources the compose helper and reads the process environment, so the child inherits the value. After `PHASE 5 mechanical checks passed.` the window sets `CutoverPhase5Passed` and `Assert-CutoverDesktopPostgresDown` requires `docker inspect` status `exited`. A non-zero inspect or any other status calls `Fail-Cutover` at exit 2 with `docker compose -f "<repo>/docker-compose.dev.yml" stop postgres`. `Complete-CutoverWindowExit` then prints `Do not run phase 6 for this.` The success path prints the user-environment OPERATOR sentence and the existing dispatch confirmation.

G-6. `Test-CutoverNewHasExtraRows` records `source` or `target` on a null table list, identifier, or fingerprint. Rollback refuses when the unreadable side is not `target`, with or without `-AcceptDataLossRollback`, so a desktop failure still refuses when server2 is also down (the source read is first). An unreadable target without the switch refuses and names `-AcceptDataLossRollback`. With the switch it prints the two WARNING lines and continues. Extra rows (`$extra -eq $true`) still need the switch. `Get-CutoverContentFingerprint` calls `Fail-Cutover` on `TimedOut` before that bypass, so a CARD-1980 statement timeout still refuses with or without the switch. The refusal names the table and says neither data directory is removed.

Snapshot target. `$script:CutoverSnapshotTargetDefaultPath` is `C:\Users\lndco\antiphon-snapshot-runner\snapshot-target`, the D-2 path at backup-plan commit `d04fa130454503cc12650d4cce3cb65c0fe2e3b4` on `origin/feat/card-task-36161193`. `Set-CutoverSnapshotTargetDesktop` writes the word `desktop` with `ASCIIEncoding` (no BOM) and accepts only a read-back that `-ceq 'desktop'`. `Path.IsPathRooted` returns false for the Windows default when this host is Linux, and that path is never created. The write runs after `Set-CutoverConnectionSecret -Remove` and before the rollback restart. A failed write does not block that restart; after a successful restart a failed write exits 2 with the manual write. Gate-6 refusals return before the write, so a refused rollback leaves the previous word.

## Red and green

Author messages record the red witnesses. This review did not re-execute those commits.

- `1f5e2db82e` is test-only. At `a95ebc4439`: W-31 `restart-env=sha skip=unset` and `no-inspect-after-restart`; W-32 exit 0 and `printed-success`; W-33 `restart-env=sha skip=1`; W-34 exit 3; W-35 missing the refusal. W-36 was already green.
- `9f909ff255` is the G-1 and G-6 production fix. The author recorded W-31..W-36 plus W-13, W-1, and W-23 at 9 passed, 0 failed.
- `06e91364c7` is test-only. Red at `9f909ff255`: `SnapshotTargetFile` is not a parameter, so W-37, W-38, and the other rollback cases fail before a write.
- `b71dc6516c` is the snapshot write. The author recorded W-37, W-38, W-13, W-20, W-25, W-26, W-33..W-36, and W-1 at 11 passed, 0 failed.
- `97b49c3ba4` is the plan only.

Independent green re-run at the reviewed tip, checkpoint tool `20261118-074632-dedd`, source state clean, dirtyFiles 0, fingerprint unchanged, `buildSource=notApplicable`. The tool build lease was `6842efb9-1e38-4234-bd87-0596bcd396c5` (waited 135s, held 5s, start load 11.19, exit 0). The run started at load 7.53. Tool exit 0, verdict GREEN, wall 4m55s. `validate --expected-source-sha 97b49c3ba422a2f7ee09ad9cd7ef32328b8eb23b --rows CP-1,CP-2` printed `CHECKPOINT SOURCE VALID` and exited 0.

| Row | Slot | Result |
|---|---|---|
| CP-1 `scripts/test-cutover-postgres-server2.ps1` | granted, waited 15s, executed 279.024s, wall 294.082s | exit 0. Console: `38 passed, 0 failed`, including PASS W-31 through W-38 |
| CP-2 `scripts/test-cutover-postgres-verify.ps1` | granted, waited 15s, executed 204.591s, wall 219.625s | exit 0. Console: `WITNESS passed=13 failed=0`, including PASS F6-fingerprint-timeout and PASS F6-session-pin |

Named guard batch at the same tip, `UseAppHost=false`, `--timeout 570s`, one lease per command. Build lease `07f55a24-08b3-4f0f-9e18-01d75e85f4d5` (waited 135s, held 198s, start load 6.23, maxcpucount 6) exited 0 with 0 errors and 2148 pre-existing warnings in 3m 16.86s. Run lease `b3860c95-fff4-4e47-8620-823f385329b4` (waited 60s, held 89s) started at load 15.05 after one poll at 19.67. Filter `/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/*`. Result: total 80, failed 0, skipped 0, 1m 25s 270ms, exit 0. That batch is a lease, so `reviewedSourceClean` stays false. The command rows have clean SHA receipts and `buildSource=notApplicable`, which is not verified build provenance.

## Scope that stays outside the verdict

`scripts/check-evidence-diff.ps1 -BaseRef a95ebc4439d0789a1d3523d50b4ecb7daaebf65a -HeadRef 97b49c3ba422a2f7ee09ad9cd7ef32328b8eb23b`: commits=5, entries=0, violations=0, exit 0.

`git merge-tree --write-tree e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4 HEAD`: exit 0, tree `f87554b7cda36c047b262167506260fcbb72347f`. No conflicts. Not merged.

OS witness on this range: the production script is portable with no behaviour candidate. The test script is test-only. `$env:OS` hits are non-behaviour. `restartImpact` required is false. W-8 inside the green CP-1 is the pwsh language parser plus an ASCII byte check of the cutover script, its test, `restart-apphost.ps1`, and `keep-six.ps1`. `powershell.exe` is absent here, so the owed Windows PowerShell 5.1 execution stays pending and off-row. It does not drop Full.

Impacted-classes for this range: kinds, classes, filters, windowsFilters, and owed are empty. The migrations kind is absent, so the downgrade count is 0. No session waits for input, so CARD-1083 does not apply. No producer or recipient UserPrompt trace applies. Positive controls stay pending. Mutation stays waived (operator 2026-11-12).

## Advisories (do not hold the land)

W-31 asserts `sha skip=1`, StopOnly unset, the post-restart inspect, the exited line, and `OPERATOR: confirm one dispatch`. It does not assert the new user-environment OPERATOR sentence. W-32 asserts exit 2, the manual stop, the mechanical-checks line, and the absence of the success line. It does not assert `Do not run phase 6 for this.`

The snapshot write happens before the rollback restart returns, so a restart exit 5 still leaves `desktop`. That is this slice's V-7, and W-37 covers it. Backup-plan W-5 at `d04fa130` says to leave the word when phase 6 stops before AppHost is back on the desktop volume. The constant and the successful-rollback end state match D-2. The early write on a failed restart is the difference, and it is the behaviour this slice's plan requires.

--- review evidence ---
subjectTaskId: 6a008315-ebd5-4dbb-8cf9-01098a091494
reviewedSourceSha: 97b49c3ba422a2f7ee09ad9cd7ef32328b8eb23b
reviewedSourceClean: false
ordinaryScopeCompleted: Full
reviewRound: 1
reviewedRange: a95ebc4439d0789a1d3523d50b4ecb7daaebf65a..97b49c3ba422a2f7ee09ad9cd7ef32328b8eb23b
