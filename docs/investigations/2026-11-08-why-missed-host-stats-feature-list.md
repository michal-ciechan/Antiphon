# Why missed: host-stats feature list

`RunnerCapabilitiesTests.Host_stats_feature_is_advertised_only_when_enabled` fails on master `c460b0d0a4de591b8e662f8dbc57eeac3f26d249` and on `rc/2026-11-11` at `3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e`. Review `af991039` saw it on master. It had not yet shown up in a qualification chunk. CARD-1781.

## Failure

At `c460b0d0a4de591b8e662f8dbc57eeac3f26d249` the method is 1 executed, 0 passed, 1 failed:

```
ShouldAssertException: enabled
    should be
["a", "b", "hostStatsV1"]
    but was (case sensitive comparison)
["a", "b", "hostStatsV1", "hostLoadHistoryV1"]
    difference
["a", "b", "hostStatsV1", *"hostLoadHistoryV1"*]
  at RunnerCapabilitiesTests.Host_stats_feature_is_advertised_only_when_enabled
     tests/Antiphon.SessionRunner.Tests/RunnerCapabilitiesTests.cs:76
```

`HostStatsSettings.HistoryEnabled` defaults to true. `HostStatsRoutes.CapabilityFeatures` appends `hostLoadHistoryV1` after `hostStatsV1` when that default is left on. The product list is the intended one. The CARD-0718 pin was stale.

## Introducing commit/card

introducing commit/card: `4a9963626b6b6acdbea94d7c21d765e4c9a631e2` (CARD-1514)

That commit is the master cherry-pick of `79f14fa0f529320d9d2c2f82fb7d7cd43656a03a`. It changes `CapabilityFeatures` from always appending `hostStatsV1` to:

```
settings.HistoryEnabled
    ? [.. features, HostStatsV1, HostLoadHistoryV1]
    : [.. features, HostStatsV1]
```

`RunnerCapabilitiesTests.cs` is not in that commit. `79f14fa0f529320d9d2c2f82fb7d7cd43656a03a` is an ancestor of every reviewed SHA below. `4a9963626b6b6acdbea94d7c21d765e4c9a631e2` is an ancestor of `rc/2026-11-10` (`74a8004f405bc54666fea33889839f0ff4d62ec7`), `rc/2026-11-11` (`3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e`), and master `c460b0d0a4de591b8e662f8dbc57eeac3f26d249`.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- `8da334c5`, scope Interim (commissioned Final; the report says completed scope is only Interim). Reviewed `0cc57cca7dcb6bc506bfecbde9f22638fe92e308`. Report `f50c0b425fb4592eb92a30b98e46d27db94a7703` (`docs/investigations/2026-11-07-review-load-telemetry-s1-r1.md` on that review branch). CP-1 `/*/*/(C1514HostLoadSampleTests*)|(C1514HostLoadAuthorityTests*)|(C1514HostLoadSpoolTests*)|(C1514HostLoadWireTests*)/*` 18/18. CP-3 `/*/*/PhoneHomeCommandDispatcherTests/Host_stats_*` 3/3. CP-PHONE-FULL `/*/*/PhoneHomeCommandDispatcherTests*/*` 45/45. CP-UNIT `/*/*/*/*[Category=Unit]` hit the 45-minute deadline with no TRX. No `RunnerCapabilitiesTests`.
- `d56a0975`, scope Interim. Reviewed `6e21459f78b7697976beafee49df60934531515b`. Report `667071fcf003ce089374806755ced7ef710c9a76` (`.antiphon/task-d56a0975.md`). CP-5 `/*/*/(C1514HostLoadSampleTests*)|(C1514HostLoadSpoolTests*)|(HostStatsSamplerTests*)|(HostStatsStoreTests*)|(HostStatsProbeParseTests*)|(HostStatsEndpointTests*)/*` 41/41. The report states no Unit lane. No `RunnerCapabilitiesTests`.
- `a28cac94`, scope Interim. Reviewed `036bbcba09178ded1e2319664f6df008305a6370`. Report `41adfec8551e6fd095243979d12fbda5f30e6e5e` (`.antiphon/task-a28cac94.md`). Diff-only CP-7 `/*/*/(C1514HostLoadNativeTests*)|(C1514HostLoadNativeOverheadTests*)|(HostStatsProbeLiveTests*)|(TestClassificationGuardTests*)|(NativeLinuxPolicyWitnessTests*)/(Linux_*)|(Registry_matches_compiled_metadata*)|(Policy_rows_require_pinned_attribute*)|(Pinned_attribute_contract_matches_native_platform*)|(Markers_do_not_expand_policy_exclusions*)` 7/7. No `RunnerCapabilitiesTests`.

`C1514HostLoadWireTests.CapabilitiesAndOrdinalsStayCompatible` was inside CP-1. It uses `ShouldContain("hostLoadHistoryV1")` on `new HostStatsSettings()`, so the extra token does not fail that test.

## Outside the selection

why the failing test was outside that selection (lane/category/path): session-runner suite, no `[Category("Unit")]` and no `[Category("Integration")]` on `RunnerCapabilitiesTests`, path `tests/Antiphon.SessionRunner.Tests/RunnerCapabilitiesTests.cs`.

The planned CARD-1514 rows name the new `C1514*` classes, host-stats classes, and `PhoneHomeCommandDispatcherTests`. They do not name `RunnerCapabilitiesTests`. `TestLaneCategoryGuardTests` scans only `tests/Antiphon.Tests`, so the missing category on this SessionRunner class is invisible to that census. A `/*/*/*/*[Category=Unit]` row cannot select it either. The rc session-runner suite discovers the assembly and does not apply that category filter. `tests/test-execution-policy.json` has no `RunnerCapabilitiesTests` exclusion. The class is therefore in the `rc/2026-11-11` session-runner selection, and the pre-land reviews did not run it.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`src/Antiphon.SessionRunner/HostStats*.cs` -> `/*/*/RunnerCapabilitiesTests/*`

The one method that fails is `/*/*/RunnerCapabilitiesTests/Host_stats_feature_is_advertised_only_when_enabled*`. It fails in under a second once `hostLoadHistoryV1` is appended. The CARD-1782 impacted-classes table is not on this master.

## Exemption

same cause as an already written note: none.
