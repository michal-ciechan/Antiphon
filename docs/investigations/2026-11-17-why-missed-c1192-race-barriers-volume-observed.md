# Why missed: C1192 race barrier fake go wait

rc run 06863906 (Windows Full, rc/2026-11-24 = 870a93d21f2b18c85a8b0905ab689b28e3099813), chunk
antiphon-all-profile-2102: `RemoteScriptContractTests.C1192_Race_barriers_observe_volume_state`
failed at RemoteScriptContractTests.cs:1092, `volume-go-observed` was `0` (7.31 s). CARD-1514
Debug task fde20c05.

Cause: the test fixture, not the product. `scripts/fixtures/c1192-workspace-contract.sh`
models the restore container in `simulate_restore`: it writes `slot-held`, then waits for
`/fixture/race/go` for 200 polls of 0.02 s (about 4 s). The production `driver.sh` waits until
its slot deadline epoch (2700 s by default). `c849_fixture_race_restore_barrier` writes go only
after it has seen both restores' `slot-held`. When the second restore starts more than about
4 s after the first, the first gives up, the barrier refuses `FixtureRestoreBarrierFailed`
and `go_ok` stays `0`. The fake lock probe had the same 4 s bound for `release`;
`lock-probe.ps1` waits 30 s.

On Linux server2 the first restore sees go after 2 to 5 of its 200 polls (19 direct fixture runs
at the base, 8 of them concurrent at load about 18; 20/20 TUnit runs green after the fix). The
fixture shell runs through `wsl.exe` on Windows. Under the Full run's load the barrier's
fork-heavy steps (fake `docker run` for p2, `docker exec` reads, the go touch) can take longer
than the first restore's fixed poll count. The mechanism is the fixed 4 s bound under load,
not a path or line-ending defect. The witness `C1514_Late_second_slot_still_observes_volume_go`
grants p2's slot 8 s late and reproduces the rc signature on Linux.

Fix: the fake waits use the probes' own wall-clock bounds (the driver's epoch argument and 30 s).
They end early on a killed container or on `c1192_kill_jobs`' stop marker. The barriers
touch-release refusal stops its holder and waiter.

## Introducing commit/card

introducing commit/card: b9b0fa6aeb8d8d0d90de67ac984fa24bfe27b9d4 (CARD-1192)

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- not found: no `docs/superpowers/reviews` record names CARD-1192 or b9b0fa6a. The fault is
  load-dependent, so a Review running the method alone on Linux would have passed it.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Windows Full only under
load. Merge verification waives the whole `RemoteScriptContractTests` class for the 570 s cap.
`tests/impacted-classes.json` `remote-scripts` maps `scripts/c590-remote.sh` and `scripts/*.sh`
to five deploy methods only. No entry covers `scripts/fixtures/c1192-workspace-contract.sh`
or the C1192 race methods. No change since the cut touched the test, the fixture or
`c590-remote.sh`, so no selection could have caught it. The latent bound has been present
since 2026-10-12 and fails only when the Windows WSL shell is slow.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`scripts/fixtures/c1192-workspace-contract.sh` -> `/*/*/RemoteScriptContractTests/(C1192_Race_*)|(C1192_Lock_probe_*)|(C1514_Late_second_slot_*)`

The guard that catches this class of fault is a late-arrival witness like
`C1514_Late_second_slot_still_observes_volume_go`. A fixture wait that models a production
probe must use that probe's bound. It must not use a poll count shorter than the slowest
barrier step under load.

## Exemption

none
