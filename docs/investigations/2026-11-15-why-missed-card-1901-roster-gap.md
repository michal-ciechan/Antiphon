# CARD-1901 round 3: native Linux gate roster gap

Base: `e791df1b7970038b167488f187c77fde01a7a004`. The exact existing test
`/*/*/WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates`
reproduced the review blocker: 1 executed, 0 passed, 1 failed, 0 skipped (25.295s).
The assertion names `c1210-unrostered-gate:
Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105ResumePostRemovalCensusCaseAsync`.
The leased base build passed, with 0 errors and 2106 warnings, in 237.54s.
Raw evidence is ignored under `.antiphon/c1901-r3-base-{build,red}.log` and
`.antiphon/c1901-r3-base-red/run.trx`.

The round-2 Code plan's guard selectors CP-10a through CP-10d did not select
`WindowsUnitPlatformAdmissionTests`; the Code report contains no execution of that
class. Its separately recorded incomplete raw-string OS lexer check did not cover
this source-closure invariant. Round-2 Review selected the admission class and
found the gap. The method was introduced by the round-2 census-method split in
`5e7b5d6b3bad77ca49ccbe3d79aa15aaf1f04e2b`.

Every new method calling `C1008HostFixture.RequireNativeLinux()` needs a source
roster entry, including private helpers. The three public census callers already
guard before invoking this helper. The smaller accounting repair is one
`WindowsUnitSkipRoster.Existing` row with `ExistingReason`, matching the existing
helper rows. `Existing` is source-closure accounting, outside the child runtime
roster, so the helper adds no runtime test or skip count. No gate, assertion,
timeout or production behavior needs to change.

The cheapest selector guard is one `impacted-classes.json` kind selecting
`WindowsUnitPlatformAdmissionTests` for `RemoteScriptContractTests.cs`, the
`RollingVolumeRecycleScriptTests.cs` file that actually declares
`C1008HostFixture`, and the roster itself. A standalone `C1008HostFixture.cs` does
not exist. This catches the omitted guard whenever those files change without
adding another test implementation.

Initial `/proc/loadavg`: `2.35 2.82 4.83 2/3065 4195`; after the base build:
`12.73 10.19 7.49 3/2918 6771`. These are sparse contended-host samples, not p95.
Native Windows execution remains separately owed. Mutation is waived by the
operator (2026-11-12). The four known master composition conflicts belong to Merge.

Verification after the repair is recorded below when complete.
