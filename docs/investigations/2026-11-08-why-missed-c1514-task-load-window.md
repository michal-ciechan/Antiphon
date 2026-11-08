# Why missed: C1514 task-load window timeout

One committed note for the rc/2026-11-11 group-95 failure of `C1514TaskLoadWindowTests` (run 4e0d1159). The Windows TRX and tests log at `C:\Antiphon\releases\candidates\rc-2026-11-11-3364197bf1cb\logs\2026-11-08-0936-4e0d1159\` are not readable from this runner. Linux at master `f54741ef3bc08baa0493f9e0bebd2425acab6fb5` reproduced the class: 8 executed, 1 failed, 7 passed.

## Failure

The only failure is `AttemptsAndMovesKeepSeparateWindows` (11s 094ms):

```
TUnit.Engine.Exceptions.TestFailedException: [Test Failure] TimeoutException: Test 'AttemptsAndMovesKeepSeparateWindows' timed out after 00:00:10
```

The stack is the TUnit timeout handler (`TimeoutHelper.ExecuteWithTimeoutAsync`), which the runner labels as not the blocked test's stack. Stderr inside that test included `C1420 schema-cache image key=d15f4dad99ef6d37d699e6a59fd77ed4 ms=4919`. No assertion failed. The same method then finished in 16.3s (schema-cache 13.5s) and 26.2s (schema-cache 4.3s) once the class cap was lifted. The class is on the previous rc `74a8004f405bc54666fea33889839f0ff4d62ec7`, which did not fail this chunk. Host-load product code is unchanged between that cut and `3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e`. This is a test-timeout budget, not a product defect, not a Down migration, and not Windows-only.

## Introducing commit/card

introducing commit/card: `e62f9f0ca18be1a6a917d934ada7f9280f705383` (CARD-1514)

That commit (2026-11-07 16:35:19 +0000, "Measure sampler tick p95 on a steady window that excludes the slowest tick") raises `C1514HostLoadWorld.EventuallyAsync` from 5s to 15s. Master replay: `70ddb266c959663f6ea2a6cf3234b36070f7d429` (cherry-picked from `e62f9f0ca18be1a6a917d934ada7f9280f705383`). The class cap it overruns was already `Timeout(10_000)`, introduced by `3e264967d34fe59a1a4efd30463d6246abf2166e` and replayed as `067d5da7ad5b568a5e1ee0f90ed462a2ca72a432`. The fixture comment records why 15s was chosen: review run `20261107-155611-da81` cancelled the 5s cap inside `SeedPrefixAsync`. That cancellation was `C1514HostLoadHistoryTests.ReadyRecipientReadsDurableSample`, not this class. The new poll is longer than the window-class cap, so a slow poll is killed by TUnit before `EventuallyAsync` can throw. Both commits are on the previous rc, which passed this class under lighter load.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- Review `8da334c5` (round 1, report `f50c0b425fb4592eb92a30b98e46d27db94a7703`, reviewed `0cc57cca7dcb6bc506bfecbde9f22638fe92e308`) is Interim under a commissioned Final profile. CP-4 selected this class: `/*/*/(C1514HostLoadHistoryTests*)|(C1514TaskLoadWindowTests*)|(C1514HostLoadApiTests*)/*` executed=28 passed=28 failed=0. At that SHA the poll was still 5s, so the 10s class cap was above the poll. The Unit lane hit its row deadline and did not complete.
- Review `d56a0975` (round 2, report `667071fcf003ce089374806755ced7ef710c9a76`, reviewed `6e21459f78b7697976beafee49df60934531515b`) records `ordinaryScopeCompleted: Interim`. CP-1 used the same filter: executed=33 passed=32 failed=1. The one failure was `C1514HostLoadHistoryTests.ReadyRecipientReadsDurableSample` (`TaskCanceledException` in the then-5s `EventuallyAsync` during `SeedPrefixAsync`). The window class was in the selection and was not the failure. The round was not clean, so it did not approve the land. The poll was still 5s.
- Review `a28cac94` (round 3, report `41adfec8551e6fd095243979d12fbda5f30e6e5e`, checkpoint source `036bbcba09178ded1e2319664f6df008305a6370`) records `ordinaryScopeCompleted: Interim` and is diff-only. CP-7 was `/*/*/(C1514HostLoadNativeTests*)|(C1514HostLoadNativeOverheadTests*)|(HostStatsProbeLiveTests*)|(TestClassificationGuardTests*)|(NativeLinuxPolicyWitnessTests*)/(Linux_*)|(Registry_matches_compiled_metadata*)|(Policy_rows_require_pinned_attribute*)|(Pinned_attribute_contract_matches_native_platform*)|(Markers_do_not_expand_policy_exclusions*)` executed=7 passed=7 failed=0. That SHA already has the 15s poll and the 10s class cap. The selection does not name `C1514TaskLoadWindowTests`.
- Review `c7a30141` (master integration, report `436e0d35924b588e398344202798052080474f39`, subject `1705a3ee8c8102562addd96f65fe4c8c94e34ced`) is CLEAN with `reviewedSourceClean` true. It reran test-design CP-1, CP-2, CP-3, and CP-7 plus completion CP-M1. It states CP-4 was not rerun. The subject already has the 15s poll and `Timeout(10_000)` on this class.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration lane, category `Integration`, path `tests/Antiphon.Tests/Application/C1514TaskLoadWindowTests.cs`.

Rounds 1 and 2 did select the class, and it passed, while `EventuallyAsync` was still 5s. The failing shape is the 15s poll against the 10s class cap. That pair exists on the round-3 SHA and on the integration subject, and neither of those reviews reran CP-4. A Unit category filter cannot see this class. A quiet Linux CP-4 that finishes `AttemptsAndMovesKeepSeparateWindows` under 10s does not measure schema-cache plus a 100-sample `SeedPrefixAsync` against that cap. The previous Windows qualification passed the same bytes; this Linux cold run and run 4e0d1159 did not. No earlier Windows-native rerun of this class was owed for a timeout the Linux selection could already see.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/TestHelpers/C1514HostLoadFixture.cs` -> `/*/*/C1514TaskLoadWindowTests*/AttemptsAndMovesKeepSeparateWindows*`

Also glob `tests/Antiphon.Tests/Application/C1514TaskLoadWindowTests.cs` to the same filter. That method is the one that pays host startup, schema-cache, and `SeedPrefixAsync`, and it is the method Linux killed at 10s. The class filter is `/*/*/C1514TaskLoadWindowTests*/*`. CP-4 already names the class and still missed this, because those runs were either before the 15s poll or not rerun after it. A Unit lane filter does not match `[Category("Integration")]`.

## Exemption

same cause as an already written note: none

The adjacent 5s `SeedPrefixAsync` cancellation in review `d56a0975` is a different class (`C1514HostLoadHistoryTests`). The 15s poll was the repair for that flake. No existing why-missed note names this class cap.
