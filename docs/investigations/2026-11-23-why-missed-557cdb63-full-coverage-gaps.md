# Why Full 557cdb63 kept two coverage gaps

Windows Full run `557cdb63` at `ecc9bfaa5bfa91c80aa022bca09f86aa7b693a91` ended with
`coverageComplete=false`. Two causes were unrelated to the seven failed tests. The analysis is in
`docs/investigations/2026-11-23-rc-run-557cdb63-final-analysis.md` on `feat/card-task-865f7412`.

## Explicit benchmarks stayed required

Group 142 planned `RcManifestScaleTests.ScaleSamples` and `ScaleReplay`, but neither ran. The summary
reported `missing-expanded-row`, `missing-required-class RcManifestScaleTests`, and the suite-union
row. These methods are the CARD-1514 M3 scale benchmarks (W2-nightly S08, commit `f7e2e04a8`). The
class is `[Explicit]`, needs `MP_SOURCE_SHA`, and has a one-hour timeout per method. A class filter
never runs an `[Explicit]` test.

In a profile lane, only an rc exclusion row in `tests/test-execution-policy.json` removes a class from
the census. Commit `810f891c1` added both gates to `tests/live-lanes.json` as `diagnostic`. That
disposition means the gate has no exclusion, so the class stayed required. No check compared a gate's
`Explicit` shape with its disposition. Every other Explicit gate is `core` or `policy-excluded`.

**Fix.** The rc profile now has an exclusion row for the class (owner CARD-1514), because the class is
opt-in by design. The policy hash was resealed. The catalog now records both gates as
`policy-excluded`. The live-lane input pin and the legacy rc exclusion digest were re-pinned.
`LiveLaneInventoryTests.ExplicitGatesAreNeverFullRequired` now fails for any Explicit gate that the rc
policy leaves `diagnostic`.

## Five Linux-only skips had no Windows roster rows

These five methods skip off Linux:

- `QualificationTimingTests.Fifo_sidecar_does_not_delay_launch`
- `QualificationTimingTests.Unsafe_journals_are_refused_by_the_bounded_reader`
- `QualificationTimingTests.Launch_path_opens_no_timing_file`
- `QualificationTimingTests.Linux_owned_launch_is_a_known_custody_gap`
- `ReviewEvidenceResettlementTests.C2053_GitHelperDeadlineNamesTestAndHelper`

They reached Full as `unexecuted-outcome` because `tests/expected-platform-skips.json` had no
`windows` rows for them. Commit `2a02fc5a9` rostered only the two `QualificationTimingTests` methods in
`QualificationTimingTests.cs`. The four methods in the partial file `QualificationTimingTests.Runs.cs`
were missed. C2053 was new.

The source-gap and reason-gap scans only check entries already in the roster, so they cannot find a
missing one. `LinuxOnlyExpectedSkipClosureTests` checks closure in both directions, but it covered
only four fixed classes and read a single file per class.

**Fix.** The five `windows` rows now use each method's exact skip text, and the EPS census pin moved
from 28 to 33. The closure test now covers `QualificationTimingTests` (both partial files) and
`ReviewEvidenceResettlementTests`. It also requires that every `ClassName.cs` and `ClassName.*.cs`
beside a subject be listed, so a missed partial file fails the test.

## Stale-roster warnings

`NightlySuiteRunner` compares the full platform roster with each suite's own discovery. Every
SessionRunner entry is missing from the Antiphon discovery, and every Antiphon entry is missing from
the other suites. The warnings therefore list every `windows` roster entry: 28 then, 33 now. They do
not affect the coverage verdict and do not indicate a rename or deletion. Scoping them to the owning
suite is a harness change and was not made here.
