# Why missed: the CARD-1984 overlap probes on Windows

rc run `a6278259`, rc/2026-11-28 (cut `997f9b8d9ffc4ba870d2fd56caf7c03781591624`, candidate
`rc-2026-11-28-997f9b8d9ffc`), chunk `antiphon-all-group-134`: 453 tests, 451 passed, 2 failed.
Both are in `tests/Antiphon.Tests/Scripts/ScriptModelSpawnLaneTests.cs`:

- `Probe_A_overlaps_probe_B`: `TimeoutException: peer did not arrive within 120s`, after 2 minutes.
- `Probe_B_overlaps_probe_A`: `InvalidOperationException: peer already gave up`.

CARD-1514 Debug task b536c209. This fix changes only tests and policy.

## Introducing commits and card

- `c9bf704375` (CARD-1984, 2026-11-18 00:28Z), `red witnesses for the script-model spawn lane`,
  added W-2: the two probes.
- `d9790f13d4` (00:46Z) added `ScriptModelSpawnLimit` with `Resolve`.
- Plan: `docs/superpowers/plans/2026-11-17-card-1984-spawn-lane-plan.md`.
- Reviews: plan rounds 1 and 2, then the round-2 final review
  `docs/superpowers/reviews/2026-11-18-card-1984-r2-final-review.md`.
- The card reached master before the rc/2026-11-28 cut `997f9b8d9ffc`.

## Diagnosis: Linux-only by construction

W-2 proves that the script-model lane is at least 2 wide. Each probe:

1. publishes its start time;
2. waits up to 120 s for the other probe's start time;
3. requires the two starts to be less than 1 s apart.

Both probes carry `[ParallelLimiter<ScriptModelSpawnLimit>]`, so they can overlap only when the
limiter's width is 2 or more.

On Windows the width is 1:

- `tests/Antiphon.Tests/TestHelpers/ScriptModelSpawnLimit.cs:25-29` reads the width once, from
  `Resolve(..., OperatingSystem.IsWindows())`.
- `Resolve` returns 1 for Windows at `ScriptModelSpawnLimit.cs:42-43`. Only the
  `ANTIPHON_TEST_SCRIPT_MODEL_SPAWN_LIMIT` override, checked first at `:40-41`, can change that.
- This is the plan's decision. D-3 says "Windows stays 1 until a measured WSL sample exists", and
  O-6 confirms it ("Keep Windows at width 1 until a WSL sample exists? **Yes.**").

So on Windows the second probe cannot start until the first probe ends. The first probe waits
120 s, records `ProbeGaveUp`, and throws the `TimeoutException`. The second probe then starts,
sees `ProbeGaveUp`, and throws "peer already gave up". The rc chunk shows exactly that pattern.
The plan's own W-2 row describes it as the expected red: "width 1: B cannot start until A ends,
A times out".

On server2 Linux the width is 6 (`Clamp(24/4, 2, 6)`, or the build-slot lease's
`maxcpucount=6`). Both probes pass there; see the evidence below.

W-1 already pins the Windows width of 1 platform-neutrally:
`[Arguments(24, "6", "", true, 1)]` on `Lane_width_is_six_on_a_24_core_linux_host`. A Windows
version of W-2 that asserts serialisation would add nothing W-1 does not already pin, and it
would cost 120 s per run. It would also have to be a different test, because a probe pair that
asserts "never overlap" proves nothing about the lane.

## Fix

This follows the CARD-1514 precedent for the CARD-1957 shims
(`docs/investigations/2026-11-17-why-missed-chmod-in-windows-tests.md`).

1. Both probes carry `[NativeLinuxOnly("Linux-only by construction: ...")]`. That
   `SkipAttribute` skips with the reason on any non-Linux host, so an ad hoc or Unit-lane Windows
   run reports a skip instead of a 2-minute red.
2. `tests/test-execution-policy.json` `profiles.rc.exclusions` gains one class row for
   `ScriptModelSpawnLaneTests`. It lists exactly the two methods, with `platform: windows`, the
   same "Linux-only by construction:" reason, and owner CARD-1514. Windows Full then does not
   launch them. Linux still requires them.
3. The re-pin is in its own commit:
   - `policyHash`: `5c478a13...e352` -> `62b6e12ca16729f0d33294ea59203c911c32197807805bffd42bb6087a0af981`.
   - `scripts/test-live-lane-run.ps1` `PinnedInputDigest`: `5e8c1644...8994` ->
     `8642f40fdb27bf4ed1b8db804fea937c625d4840140b0d486852a9ba8212458f`.
   - `LegacyExclusionDigest` (`6d6ff646...252a`) is unchanged, because Linux-only rows stay out
     of it.

Several things did not change:

- No `NativeLinuxGate.Require` call was added, so `WindowsUnitSkipRoster` and the C1210 closure
  are unchanged.
- `tests/expected-platform-skips.json` is unchanged, because the rc profile excludes the two
  methods rather than tolerating a skip.
- The derived impact index regenerates unchanged.
- No assertion, timeout or limiter width changed. The 120 s rendezvous and the 1 s overlap bound
  are as CARD-1984 wrote them.

## Other CARD-1984 lane tests: no same blind spot

| Test | Windows behaviour | Evidence |
|---|---|---|
| `Lane_width_is_six_on_a_24_core_linux_host` (13 cases) | pure `Resolve` arithmetic; passes on any host | not among the run's failures (only the two probes failed in the chunk); class row 36/36 on Linux |
| `Script_model_families_use_the_wide_lane`, `Unlimited_script_model_methods_*` (W-3/W-4) | reflection and IL walks over attributes; no spawn | not among the run's failures (only the two probes failed in the chunk); class row 36/36 on Linux |
| `Wide_lane_methods_write_no_executable_in_process`, `In_process_executable_write_is_a_finding`, `Out_of_process_or_interpreted_script_is_not_a_finding` (W-10) | IL walks. The `ExecutableWriteFixture` bodies that start `chmod`/`bash` are walked, never run (comment "Walked, never run.") | not among the run's failures (only the two probes failed in the chunk); class row 36/36 on Linux |
| `Wide_lane_methods_reach_no_checkout_writer`, `Checkout_writer_reach_is_a_finding` (W-11) | IL walks over a stand-in type | not among the run's failures (only the two probes failed in the chunk); class row 36/36 on Linux |
| `Wide_lane_sources_name_no_fixed_temp_path`, `Fixed_temp_literal_is_a_finding` (W-5) | source text scan | not among the run's failures (only the two probes failed in the chunk); class row 36/36 on Linux |
| `ProcessSpawnLimitTests` script-model floor | attribute census; no width assertion on `ScriptModelSpawnLimit` | no `new ScriptModelSpawnLimit().Limit` exists in the tests |
| `FixtureExecutable` callers (HostJq, RollingVolumeRecycleCensusChurn, RollingVolumeRecycleScript `C1008_Dry_run_preview_binds_a_dry_run_cleanup_receipt`, `LoadComposeV218` callers in RollingProductionMount and RemoteScriptContract) | each sits under a class-level or method-level `[NativeLinuxOnly]`, so none reaches `/usr/bin/install` on Windows | source census on this branch; the round-2 final review states the same |

The only lane tests that need width 2 or more are W-2's two probes.

A residual Linux risk is not changed here. On Linux, `ANTIPHON_TEST_SCRIPT_MODEL_SPAWN_LIMIT=1`
or a build-slot grant of `maxcpucount=1` (exported as `ANTIPHON_BUILD_SLOT_MAXCPUCOUNT`) would also
make the width 1 and turn W-2 red the same way. Today server2 grants 6.

## Why Linux-only verification missed it

- Every CARD-1984 run was on server2 Linux, where the width is 6:
  - the Code checkpoints (CP-1 `ScriptModelSpawnLaneTests/*`, every case, 0 failed or skipped);
  - the stress rows;
  - the round-2 final review;
  - the Merge rows.
- The plan knew both facts and did not connect them. The W-2 row says width 1 makes the
  rendezvous time out, and D-3/O-6 set Windows to width 1. No row asked "where does W-2 run?".
- The round-2 final review checked Windows only for `FixtureExecutable`. Its words were "The wide
  lane's Windows width is 1; these methods do not execute there". It did not apply the same
  question to the W-2 probes, which do execute on Windows and depend on width 2 or more.
- No static guard covers this. The A5 census (`test-rc-platform.ps1 -Case
  LinuxOnlyWindowsExclusionPolicy`) and `NativeLinuxPolicyWitnessTests` only check that existing
  markers and policy rows agree, and the C1210 roster closure only accounts for existing
  `NativeLinuxGate` calls. A method with no marker is invisible to all three.
- The first lane that runs `Antiphon.Tests` on Windows is the Windows Full run: rc qualification
  or the Windows nightly. It runs after land and does not gate it. Here that was rc run
  `a6278259`.

## Cheapest pre-land guard (follow-up card, not built here)

Add a reflection guard to `ScriptModelSpawnLaneTests` or `ProcessSpawnLimitTests` that runs on
Linux in the standing guard union. The concrete detection is a platform-skip guard for tests that
rely on lane width.

- **Scope.** Every `[Test]` method in the assembly that does both of these:
  - carries `[ParallelLimiter<T>]` for a limiter whose Windows width is 1;
  - depends on overlap, either because it awaits a peer's `TaskCompletionSource` or because a
    declared list names it a width witness.

  To find the limiters, call each limiter's `Resolve(..., isWindows: true)`.
- **Pass condition.** Each such method carries `[NativeLinuxOnly]` and has a matching rc windows
  exclusion row.
- **Simplest form.** Name the width witnesses explicitly in a list, as `StrictKeep` does. Then
  assert that every listed witness whose limiter resolves to 1 on Windows is
  `[NativeLinuxOnly]` and excluded on Windows.
- **What it would have caught.** W-2 at CARD-1984's Code stage, before Review.

## Evidence (server2 Linux, tip of this branch)

| Row | Selection | Result |
|---|---|---|
| index driver | `regenerate-derived-impact-index.ps1` (driver built first), run twice | `derived-impact-index unchanged` both times |
| build | `dotnet build tests/Antiphon.Tests -p:UseAppHost=false` | 0 errors, 3m01s |
| lane class | `ScriptModelSpawnLaneTests/*` | 36/36 passed, 0 skipped; both probes passed, about 3 ms each |
| guard union + skip closure | the 10 named guard classes plus `ExpectedPlatformSkipRosterTests`, `LinuxOnlyExpectedSkipClosureTests` | 90 total, 89 passed, 1 skipped (`C1210_Windows_linux_only_outcomes_are_exact`, expected on Linux); C1729 `VIOLATIONS 0 LISTED 162` |
| derived index | `DerivedImpactIndexTests/*` | 27/27 passed |
| A5 census | `test-rc-platform.ps1 -Case LinuxOnlyWindowsExclusionPolicy` | 6/6 PASS |
| live-lane pins | `test-live-lane-run.ps1 -Case C1545_WrapperOnlyPaidManifest` | 21/21 PASS |

The load gate was 1-minute average below 16. Every build and
run went through `build-slot.ps1` with `ANTIPHON_LINUX_COMPILER_POLICY=0`, and every run used
`--timeout 570s`.

Windows was not run here: nothing may run on the desktop during the live qualification. Two
things prove the Windows outcome:

- The rc exclusion row keeps both probes out of Windows Full. The A5 census and
  `NativeLinuxPolicyWitnessTests` check that row against the markers.
- `NativeLinuxOnlyAttribute.ShouldSkip` returns `!OperatingSystem.IsLinux()` for any other
  Windows execution.

The next Windows Full run on a candidate carrying this fix should show these two methods as
excluded, not executed.
