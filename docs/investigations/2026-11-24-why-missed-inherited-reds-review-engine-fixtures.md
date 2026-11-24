# Why the review mirror and derived impact failures escaped

CARD-2064 Final/Full Review `b6941f8f` recorded three failing methods across two classes at
`0ed95b1d08a2ff0e253eb80245b80670adf69014`. Debug reran each class once on server2 at
`79d788a08694494bb6d67109cef5f369b372a7ca`, using one native build and separately leased class
runs.
[Final/Full Review note](https://github.com/michal-ciechan/Antiphon/blob/3f705730cbfde775f99a0e8f564a0a57b08253e9/docs/superpowers/reviews/2026-11-24-card-2064-csharp-review-r2.md)

| Method | Result at `79d788a08` |
|---|---|
| `TestFixtureCleanupGuardTests.Git_fixtures_use_read_only_safe_directory_cleanup` | Passed, 1/1. Removing the exception in `6ebcaf5e02332cc53f97cc3e320ce8643b2137c6` made it fail, 0/1, with `Use ReadOnlyDirectory.Delete for Git fixture trees.` and `tests/Antiphon.Tests/Review/ReviewMirrorCheckoutTests.cs:67 (root)`. |
| `DerivedImpactIndexTests.committed_index_matches_the_builder` | Failed at `committed.ShouldBe(index.Utf8)`. The builder output has the `ScriptHarnessHandleProbe.cs` edge to `ScriptHarnessHandleProbeTests` and `ScriptHarnessWindowsOwnershipTests`. The committed JSON lacks that edge. |
| `DerivedImpactIndexTests.regenerate_command_reports_changed_then_unchanged` | Failed at `first.Exit.ShouldBe(0, first.Text)`, with exit 1 and `The application to execute does not exist: '/work/worktrees/ws-03-d4/scripts/regenerate-derived-impact-index/bin/Debug/net9.0/Antiphon.DerivedImpactIndex.dll'`. |

The index class finished with 25 passed, 2 failed and 0 skipped out of 27.

## Introducing commits

- `a3d539025f568dff5e709ef7e1dec1fcbb8692cb` added the review-engine Docker bundle and its
  fixture's raw recursive delete. The guard scans Git fixture source files, and a real-Git
  fixture shares that file. `66fa8d14846d8c0c4fd48bf59b944d298b30c61d` admitted the runner
  fixture's delete through an exception. The runner fixture now disposes the runner and
  calls `ReadOnlyDirectory.Delete` in `finally`, and the exception is gone.
- The Docker push added the mirror tests and their index edges without refreshing the committed
  index. Replaying the same builder on its parent
  `2f4cfa8df9d5b51a1554e5adae5716b2e422c620` reported unchanged. Replaying it at
  `a3d539025f568dff5e709ef7e1dec1fcbb8692cb` reported changed and added the review mirror edges.
  `3189159aa0b2b06b97c083ee89b185576edd76ce` added review sign-in coverage and further
  edges. Both sets were missing at the reviewed master tip. Regeneration replay was unchanged
  at `68ffc35842fb1356e9d8414794b21dc8444a27d0` and added one edge at its immediate successor
  `58e3f88dcce5eb02251492806ef6d3b2c06c14be`, which introduced the handle probe and its two
  test callers for rc group 136. That edge was still missing when
  `79d788a08694494bb6d67109cef5f369b372a7ca` published the handle probe repair.
- CARD-1838 commit `3bc6239d39a43aa14a476002fe77d1435d9f158a` introduced the regeneration
  witness with `-NoBuild`. The group 131 repair
  `b5d8930f9d8ba5345144b41f308d344efcf4c2cc` added the driver to the solution build. A
  standalone `Antiphon.Tests` build still omitted it. Unit 41 in
  `1ccd67354572548aa4325e7a7b71705d7f1f6ba8` switched the test to the C# command and kept the
  native output assumption. The test project now stages the driver beside the test assembly.
  The witness checks the command's exact child arguments and runs the staged driver against a
  synthetic index to verify both changed and unchanged output.

## Approving reviews and missed coverage

- The Docker commits went directly to master. The recorded Linux smoke checks cover Compose
  startup, health and version, sign-in behaviour, and mirror coverage against a local Git
  origin. The available records contain no source-bound Merge receipt for either guard class.
  [Docker smoke record](../features/012-review-engine/getting-started.md)
- CARD-2064 Review `69d20d94` was Interim. Final/Full Review `b6941f8f` ran the cleanup
  guard in its guard union and impacted subset, and the index class in the Merge guard rows.
  It reproduced the three reds on its master comparison point. These reviews covered the
  separate CARD-2064 subject at `650e778cd12cdad34d424978ea045b70ab07c37e`.
- Earlier CARD-1838 Code and Merge runs had prepared the driver for their witnesses.
  Unit 41 recorded 27 passing index tests with the built driver. A project-only
  checkpoint without that prerequisite exposed the missing test build dependency.
  [Group 131 coverage](2026-11-10-why-missed-g131%20DerivedImpactIndex%2BReleaseGateFollowup.md#approving-reviews)
  [Unit 41 record](../superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md)
- A short Merge's fixed subset omits these classes. The authored witness rows and range-based
  impacted rows supply additional coverage. The `test-fixture-cleanup` rule was already on
  master for the Docker push, and replaying the selector on that patch selects the guard.
  The Final review and ordinary Merge procedure could have caught the cleanup violation.
  [Merge verification](../orchestration-loop.md#merge-verification-is-short-2026-11-18)
- The group 136 Debug addressed handle-probe diagnostics with narrow Windows and synthetic
  Linux witnesses. Its recorded scope left out the index parity check. The final-tree Merge
  regeneration and guard should run after the last source commit in each wave. The nightly
  Unit backstop and rc qualification also own both Unit classes.
  [Group 136 diagnostics](2026-11-24-group-136-diagnostics.md)
  [Merge process](../orchestration-loop.md#merge-process)
  [Code and Review verification](../testing-and-build.md#default-codereview-recipe)

## Cheapest pre-land checks

| Trigger | Gate |
|---|---|
| `tests/**/*.cs` | Keep the existing `test-fixture-cleanup` selector and execute `/*/*/TestFixtureCleanupGuardTests/*` in the Final or Merge checkpoint. |
| `tests/Antiphon.Tests/Antiphon.Tests.csproj`, `scripts/regenerate-derived-impact-index/**`, `scripts/lib/Antiphon.Nightly/ImpactIndexRegeneration.cs` or `tests/Antiphon.Tests/Scripts/DerivedImpactIndexTests.cs` | Run `/*/*/DerivedImpactIndexTests/regenerate_command_reports_changed_then_unchanged` from an isolated `Antiphon.Tests` checkpoint build. |
| Final merged source tree | Regenerate the index once, then run `/*/*/DerivedImpactIndexTests/committed_index_matches_the_builder` on the final tree. |

Regenerate at land after final integration. The repair adds a fixture cleanup dependency,
removes the derived edge created by the guard exception, and captures the missing handle-probe
edge. The explicit `test-fixture-cleanup` selector still covers every test source file.
The branch carries regenerated bytes for its own checkpoint. On the landing tree, run the
repository generator under a build slot and commit `tests/derived-impact-index.json` if it
reports changed. [Merge process](../orchestration-loop.md#merge-process)

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label land-derived-index -- scripts/antiphon regenerate-derived-impact-index
```

## Prior write-up

The earlier missing-driver instance affected the solution qualification route.
The same prerequisite was still missing on the test-project route.
[Group 131 why-missed note](2026-11-10-why-missed-g131%20DerivedImpactIndex%2BReleaseGateFollowup.md)
