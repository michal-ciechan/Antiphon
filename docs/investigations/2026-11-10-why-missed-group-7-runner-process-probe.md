# Why missed: group-7 runner process probe deadline wall

One committed note for the rc/2026-11-15 group-7 failure of `RunnerProcessProbeTests.Late_process_start_after_the_one_second_deadline_is_reaped` (run 6591e378). CARD-1781.
Owner: [why-missed write-up](../orchestration-loop.md#why-missed-write-up-card-1781).

Run 6591e378, chunk `antiphon-all-group-7.trx`, candidate `C:\Antiphon\releases\candidates\rc-2026-11-15-ac6d00022e94\logs\2026-11-10-1248-6591e378`, rc/2026-11-15 = `ac6d00022e940ee3b90584c7d313567b4491c498`. The desktop extract `docs/investigations/2026-11-10-run-6591e378-group-7-runner-process-probe-windows-failure.md` was not on origin when this note was written. The failure text below is the in-progress Custom read `a58543ac` after it opened that TRX.

## Failure

`Antiphon.Tests.AgentTui.RunnerProcessProbeTests.Late_process_start_after_the_one_second_deadline_is_reaped`. Chunk window `2026-11-11T02:09:24Z`–`02:11:03Z`. The test started `2026-11-11T02:09:36.5215383+00:00` and ended `2026-11-11T02:09:39.3388340+00:00` (wall about 2.817s).

The stopwatch starts when the start seam is entered and stops when `RunAsync` returns. It measured 2.642s. The assertions before it had already passed: `TimedOut` true, `Started` false, `CleanupConfirmed` false. The failing assertion is `elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2))` at `tests/Antiphon.Tests/AgentTui/RunnerProcessProbeTests.cs` (then line 748). The reap-after-release assertions never ran.

The one-second `CancellationTokenSource` is created at the start of `RunAsync`, before the seam. After the seam, `WaitAsync` returns in the cancellation catch and `InterruptedBeforeStart` builds that result with no further wait. A 2.642s return is the timer callback arriving late, not a wrong result.

This chunk is not a Docker-outage victim. `docs/investigations/2026-11-10-docker-outage-victim-chunks.txt` on `origin/feat/card-task-88c9a263` (`37548b733`) names only `antiphon-all-group-115` through `118`, which ran `2026-11-10T16:50Z`–`16:51Z` while the engine pipe was down (`16:46Z`–`16:55Z`). Group-7 ran the next morning. The desktop read counted about 55 overlapping results inside this same process at the opening of the chunk (launch-resolver and secret-protector tests). Group-8 started when group-7 finished, so no second qualification chunk overlapped. `Blocking_path_inspection_observes_the_one_second_deadline` in the same class, same 1s timeout and 2s wall, passed at 1.016s later in the chunk (`02:10:26Z`–`02:10:27Z`) after that herd.

`RunnerProcessProbe.cs`, `IRunnerProcessProbe.cs`, `RunnerProcessProbeTests.cs` and `RunnerProcessProbeRedactionTests.cs` are identical from `ac6d00022` to `origin/master` `bc71a95e7710532a9a68416f1d7b9e158fe96a15`. The failure is not already fixed on master.

Linux at that master tip, filter `/*/*/RunnerProcessProbeTests/Late_process_start_after_the_one_second_deadline_is_reaped*`, `UseAppHost=false`, native output: quiet 1/1, method duration `00:00:02.9742581` (lease `f4e0d5a4-1998-47ad-beb4-548552c0bfb8`); 24 `sha256sum /dev/zero` burners, 1/1, method duration `00:00:03.5497526` (lease `7a82085f-eb88-4017-b972-792bd629be98`). Both stayed under the 2s wall. That load is not the in-process herd. The Windows TRX is the red witness.

After the bound became `WaitAsync(5_284 ms)`, the same filter on the rebuilt native binary passed 1/1, method duration `00:00:02.4668701` (lease `a3203ea4-8185-4bb1-bb0a-4ea75fac2a20`, `--no-build`). The three state assertions stayed. Product code is unchanged.

## Introducing commit/card

introducing commit/card: `3949403c94e4b7295ec8f6ae2669f8ab6178f5d3` (no card)

`fix(agent-tui): harden discovery and probe recovery` (2026-08-13). `git blame` still attributes the 2s wall to that commit. No board card matches the subject. `CARD-0050` (`3f792ec790cb87544f788740cfedfe156227b347`, 2026-08-19) later widened the default probe timeout to 30s and the cold-pwsh tree stops to 8s after a measured >6s start under load, and recorded `probe file 89/89` in isolation. It left this deadline wall at 2s. The comment in the same file says a test whose scenario needs the timeout must not sit near cold-pwsh latency. This scenario needs the timeout, and the 2s figure is 1s of slack over the 1s token, not a cold-start budget.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- No stored review for `3949403c94`. The only recorded selection on the later timeout edit is the CARD-0050 commit message: probe file 89/89 in isolation, plus a concurrent double-suite whose reproduced failures were cold pwsh starts, not this wall.
- CARD-1414 Final `a830c7b4-d172-457a-aa3b-24291cba542d`, scope Full, selection the whole `RunnerProcessProbeTests` class, CP-1 29/29 (run `20261021-183339-ce07`), reviewed `2ecc3f0da162731823e03eee91bf3503924965e2`. Outcome Found (empty bounded PATH), not a miss of this method.
- CARD-1414 Final `ddd0512c-831c-48bb-85c4-d545ed1d60b7`, scope Full, CP-1 31/31 (run `20261021-192714-f351`) at `1fc13a51e03131a0d477d77934dcdfe344b1d5d9`. Outcome Found (relative PATH entries).
- CARD-1414 Final `0853362f-ad25-406e-87f8-fe38b962cf25`, scope Full, `reviewedSourceClean=true`, selection `/*/Antiphon.Tests.AgentTui/RunnerProcessProbeTests/*`, CP-1 33/33 (run `20261021-204750-28d8`) at `738a11bf8f3ef6dce2759e72c44b7dfda702dad5`. That class filter includes `Late_process_start_after_the_one_second_deadline_is_reaped`. It passed there.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration lane, categories `Integration` and `Slow`, path `tests/Antiphon.Tests/AgentTui/RunnerProcessProbeTests.cs`.

The method was inside the CARD-1414 class filter. What that filter did not run is a Windows chunk whose opening herd delayed the timer callback. A Unit filter does not match `[Category("Integration")]`. `tests/impacted-classes.json` has no row naming `RunnerProcessProbe` or this class, so a diff that does not touch the probe file never selects it. A quiet Linux run of the class, which is what those Final reviews ran, still passes: the state is correct and the wall stays under 2s.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`server/Infrastructure/Agents/Tui/RunnerProcessProbe.cs` -> `/*/*/RunnerProcessProbeTests/*`

Also map `tests/Antiphon.Tests/AgentTui/RunnerProcessProbeTests.cs` and `server/Application/Interfaces/IRunnerProcessProbe.cs` to that class filter. The method that failed is `/*/*/RunnerProcessProbeTests/Late_process_start_after_the_one_second_deadline_is_reaped*`. The deadline and the reaper are shared with the rest of the class, so the class is the narrow set.

Proposed row (not applied; `ImpactedClassesSelectorTests` pins `tests/impacted-classes.json`):

```json
{
  "id": "runner-process-probe",
  "globs": [
    "server/Infrastructure/Agents/Tui/RunnerProcessProbe.cs",
    "server/Application/Interfaces/IRunnerProcessProbe.cs",
    "tests/Antiphon.Tests/AgentTui/RunnerProcessProbeTests.cs"
  ],
  "classes": [
    "RunnerProcessProbeTests"
  ]
}
```

No `"windows": "required"`. The cause is timer latency under an in-process herd, which is not Windows-only. This runner's quiet and CPU-burner runs stayed green. A quiet Linux pre-land run of the filter would not have failed. The Windows chunk is what observed 2.642s.

## Exemption

same cause as an already written note: none

Distinct from the CARD-1418 redaction budget (`Probe_redacts_many_same_line_assignments_near_the_output_limit_within_budget`, a 2.5s floor after an 1886 ms sample) and from the CARD-0050 cold-pwsh tree stops. Those are other clocks in the same file.
