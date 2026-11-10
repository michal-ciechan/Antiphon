# Review CARD-1514 composer probe clock

Final Review, round 1, of Code owner `59804dca-bbe8-4219-9dde-6234a042b78d`.
Reviewed source `5bcd5b511e05f7fcd1213b6205099ad78e682785` on `feat/card-task-59804dca`.
Range `057bf1f0361bb507dd11a818763e9e094fe1d192..5bcd5b511e05f7fcd1213b6205099ad78e682785`.
Verdict: CLEAN. No product defect. Positive controls stay pending.

## Behavior

`ComposerInputProbe.RunAsync` takes an optional `TimeProvider`. Null becomes `TimeProvider.System`.
Elapsed time is `GetElapsedTime` from `GetTimestamp`. `Task.Delay` takes that provider.
Production callers (`ClaudeStartupReadiness`) omit the argument. The server build compiled.
There is no DI registration. The parameter is last, so existing positional calls still bind
`CancellationToken` to `ct`.

The budget check still runs before another retype. Units stay `TimeSpan`. The default path
still waits in real time: `A_token_that_never_renders_fails_and_the_launch_is_told` took 327ms
against a 300ms budget. The stepped-clock cap test took 34ms, not the 1000ms budget, so it
does not sleep. The 600ms sibling took 34ms and still expects two writes and `NeverAppeared`.

The old cap assertions (exactly 3 token writes) remain. `NeverAppeared` was added. The red
commit `a69d40e84` pointed that same 3-write assertion at `OvershootingPollTime`, which the
sibling now shows returns 2. The fix commit splits the two clocks. That is not a weaker assertion.

A mutant that drops the write cap would write at 0, 100, ..., 900ms (10 writes) and fail the
stepped test. A mutant that checks the budget after the retype would write a third time on the
600ms jump and fail the sibling. Those mutants were not executed. PCs stay pending.

## Checkpoints

No `### Checkpoints` table exists for this rc repair. The commissioned rows were run through
`scripts/run-checkpoint.ps1` at the reviewed SHA (`dirty=0`, `sourceState=clean`, `buildSource=verified`).

| Row | Result |
|---|---|
| probe-clock `ComposerInputProbeTests` + `ClaudeStartupReadinessTests` | 33 executed, 33 passed, 0 failed. Probe class 16. Readiness class 17. |
| guards nine-class union | 78 executed, 78 passed, 0 failed. |
| pty-fixture `Card1277PtyHostFixtureTests` (impacted `pty-host-fixture`, Linux row, `-NoBuild`) | 3 executed, 3 passed, 0 failed. |

The brief's 135 guard count is the older union. This tree's nine-class union is 78, which matches
`docs/testing-and-build.md`. Migration row did not match. Downgrade selector count 0.
Evidence diff: 2 commits, 0 violations, exit 0.
`git merge-tree --write-tree origin/master 5bcd5b511e05f7fcd1213b6205099ad78e682785` exit 0,
tree `c503186eb072908af572ff2d069aa87c8f92846f`. `origin/master` is `f125755c1648533c473ce46311c307788b4a9b65`.

## Owed, not passed

`FakeClaudeContractTests` probe methods: 3 selected, 3 skipped, "ConPTY only on Windows", exit 8.
`ClaudeInputProbeCanaryTests`: 2 selected, 2 skipped, headed Windows, exit 8. Manual and pending.
The same `Card1277PtyHostFixtureTests` filter is `windows: required`, so the Windows lane still
owes it. This Linux 3/3 does not discharge that lane. No new OS branch was added.

## Advisories

The why-missed note's proposed `composer-input-probe` impacted-classes row was not added.
`ImpactedClassesSelectorTests` pins `tests/impacted-classes.json`. That does not hold the land.
