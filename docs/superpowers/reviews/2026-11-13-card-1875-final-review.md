# CARD-1875 final review: warning border must share the question column

Review task c95de2ba. Round 1, Final. Code owner c6cfa115-88ef-4cdf-962e-5ac8042a25ea.
Reviewed source a883568c61b74c138a883bd142646241a524f27c (clean).
Range 7e2e7d9173e550588ecdb379c21b536edfb5fef2..a883568c61b74c138a883bd142646241a524f27c.
Verdict: clean. No product defect. PC-1 stays pending.

## Behavior

`ClaudeDialogActions.IsUnsafeDeleteApproval` accepts a heading-less unsafe-delete dialog only when the warning border's leading whitespace equals the leading whitespace of `Do you want to proceed?`. Command-preview borders in the captured Review 87596cc5 screen sit two columns further right (lead 3 versus lead 1) and cannot authorize `2`.

The captured screen still matches. Both the warning border and the question are lead 1, the warning is two non-blank rows above the question, every warning row keeps the box prefix, and the text contains `rm -rf`. No tab appears in that capture. `C566_ScrolledUnsafeDeleteDialogOnlyAcceptsNo` still requires exactly one byte `2` and passed inside CP-2 (6/6).

The preview edge from Review b2cb1533 is `C1875_CommandPreviewWarningDoesNotAuthorizeNo`. At the pre-fix commit it wrote a byte. After the column check it stays modal-blocked.

Guarded-input fences are unchanged. `CheckDialogInput` still requires epoch, input revision, known history, and `Input == "2"` before the shared predicate. Watchdog sends the literal `2` and does not send Enter. `WatchdogMatcher.IsActiveUnsafeRmApproval` and `SessionRunnerRuntime.CheckDialogInput` both call `ClaudeDialogActions.IsUnsafeDeleteApproval`. Those three files are the same blobs on the reviewed tip and on the master cherry-pick.

## Master integration

`git merge-tree --write-tree origin/master a883568c61b74c138a883bd142646241a524f27c` conflicts in `ClaudeDialogActions.cs` and `ClaudeDialogInputGuardTests.cs`. That is the expected cherry-pick SHA mismatch: the branch base blobs of those files are already on master (`e1f981cc1`, `167681064`).

Cherry-pick of only e87ad93af, 71ee232ee, and a883568c6 onto origin/master fac5cc2de89cc82eb39e85cd2bf6c4df18d55ac6 applied cleanly. Scratch tip d341b96f1805861289228d9d155f9408814df537. The four changed paths are byte-identical to the reviewed tip. On that scratch, leased runs were ClaudeDialogInputGuardTests 6/6 and WatchdogServiceTests 10/10 (the C566 methods live in those classes).

## Checkpoints

CP-1 ran at the red commit because the plan's intentional failure cannot occur after the fix. CP-2 through CP-6 ran as one checkpoint-tool run at the reviewed tip. Receipt `20261113-091001-e576` validated: `CHECKPOINT SOURCE VALID source=a883568c61b74c138a883bd142646241a524f27c rows=5`.

CHECKPOINT CP-1 commit=e87ad93afcdd9a6b35b6e72842cef7fab82af902 build=ok filter=/*/*/ClaudeDialogInputGuardTests/C1875_CommandPreviewWarningDoesNotAuthorizeNo executed=1 passed=0 failed=1 skipped=0 trx=/tmp/card-1875-red-scratch/.antiphon/checkpoints/20261113-090851-060d/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=e87ad93afcdd9a6b35b6e72842cef7fab82af902 sourceState=clean buildSource=verified

Failure text: expected modal-blocked, actual written. That is the column check absent. Not a build or fixture error.

CHECKPOINT CP-2 commit=a883568c61b74c138a883bd142646241a524f27c build=ok filter=/*/*/ClaudeDialogInputGuardTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-c95de2ba/.antiphon/checkpoints/20261113-091001-e576/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=a883568c61b74c138a883bd142646241a524f27c sourceState=clean buildSource=verified

CHECKPOINT CP-3 commit=a883568c61b74c138a883bd142646241a524f27c build=ok filter=/*/*/WatchdogServiceTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-c95de2ba/.antiphon/checkpoints/20261113-091001-e576/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=a883568c61b74c138a883bd142646241a524f27c sourceState=clean buildSource=verified

CHECKPOINT CP-4 commit=a883568c61b74c138a883bd142646241a524f27c build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(DerivedImpactIndexTests*)/* executed=105 passed=105 failed=0 skipped=0 trx=/work/worktrees/task-c95de2ba/.antiphon/checkpoints/20261113-091001-e576/rows/CP-4/run.trx slot=granted waited=75s dirty=0 source=a883568c61b74c138a883bd142646241a524f27c sourceState=clean buildSource=verified

CHECKPOINT CP-5 commit=a883568c61b74c138a883bd142646241a524f27c build=reused filter=/*/*/Card1277PtyHostFixtureTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-c95de2ba/.antiphon/checkpoints/20261113-091001-e576/rows/CP-5/run.trx slot=granted waited=15s dirty=0 source=a883568c61b74c138a883bd142646241a524f27c sourceState=clean buildSource=verified

CHECKPOINT CP-6 commit=a883568c61b74c138a883bd142646241a524f27c build=reused filter=/*/*/(BlockedParkWireTests*)|(ResumeReceiptReadinessTests*)|(SessionRunnerCpuTests*)|(TerminalSeatReleaseTests*)|(TranscriptTailerBoundPollTests*)|(TranscriptTailerObservationTests*)/* executed=64 passed=64 failed=0 skipped=0 trx=/work/worktrees/task-c95de2ba/.antiphon/checkpoints/20261113-091001-e576/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=a883568c61b74c138a883bd142646241a524f27c sourceState=clean buildSource=verified

CP-6's minimum was 60. 64 executed, 0 failed. Native Windows `Card1277PtyHostFixtureTests` stays owed. The Linux copy passed 3/3.

## Other gates

- Evidence diff base 7e2e7d9173e550588ecdb379c21b536edfb5fef2 head a883568c61b74c138a883bd142646241a524f27c: commits=3 entries=0 violations=0.
- Impacted classes: pty-host-fixture and test-lane-category. Classes match CP-2, CP-3, CP-5, and CP-6 plus `TestLaneCategoryGuardTests` inside CP-4. No migration row. Downgrade caller count 0.
- OS witness lint exit 0. The predicate is portable. No startup-path change. `check-os-witnesses.ps1` does not prove runtime identity.
- `GET /api/runner-defaults` on localhost:17202 was not reachable from this mirror. No host and no OS were pinned.
- Operator 2026-11-12 waived mutation execution. PC-1 (remove the column guard, red/restore/green on `C1875_CommandPreviewWarningDoesNotAuthorizeNo`) stays pending. The red CP-1 run is the pre-fix matcher, which is that missing guard: 1 failed as an assertion, not a build error. Ordinary green does not discharge PC-1.
- Activation needs the server and every session runner restarted onto the build. A master land is not activation. The orchestrator owns that restart.

## Advisory

A warning box that keeps the prefix and omits the substring `rm -rf` still returns false, and no test isolates removal of that pre-existing `Contains` check. Filed as CARD-1879. It does not hold the land.
