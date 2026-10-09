# CARD-1153 Windows verification: CP-35 and CP-36

Both rows passed: CP-35 executed/passed 5/5 and CP-36 executed/passed 6/6; zero failures or skips. No source or test changes, no reruns, no failure-driven baseline run. The only tracked change is this verification note.

Verified source: `25bd11100f294be551f3ac48b02fbbfe9325bd02`, equal to freshly fetched `origin/master` when the run began. Branch: `feat/card-task-413cf221`. Worktree: `C:\Antiphon\worktrees\card-task-413cf221`.

Host: Microsoft Windows 10 Pro x64, version `10.0.19045`, build `19045`; 8 logical cores. .NET SDK `10.0.300`; test runtime .NET `9.0.20`, TUnit `1.44.0.0`, Microsoft Testing Platform `2.2.2`. Run started 2026-10-09 02:00:47 UTC and finished 02:02:04 UTC (03:00:47–03:02:04 Europe/London).

Selection comes verbatim from the first `### Checkpoints` table in `docs/superpowers/plans/2026-10-08-card-1153-test-design.md`: CP-35 minimum/expected 5, CP-36 minimum/expected 6. Both classes ran their real Windows `cmd.exe` witnesses. The whole Unit lane, whole assembly, and whole suite were not run.

## Command and build

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1153-windows-413cf221 -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1153-tool-w413cf221/ -- run --plan docs/superpowers/plans/2026-10-08-card-1153-test-design.md --rows CP-35,CP-36 --serial --expected-source-sha 25bd11100f294be551f3ac48b02fbbfe9325bd02
```

The isolated tool bootstrap is necessary to execute the checkpoint driver. The checkpoint selection performed one isolated test-project build, `tests/Antiphon.SessionRunner.Tests -> bin-c1153-final-windows/`, then reused it for CP-36. No `UseAppHost=false` override was supplied. The test build took 30.6051138 seconds including driver overhead (MSBuild reported 27.71 seconds), with 51 warnings and zero errors. No slow-build intervention was needed.

Outer bootstrap/run lease: `07a1f112-5500-48e4-a4df-7287ce9f70b6`, granted with zero wait, maxcpucount 4, released after 91 seconds. Test build lease: `d45e10a6-b49c-4cfb-b0f4-66034bdd04b5`, granted with zero wait, maxcpucount 4, released. Broker: `http://localhost:17204/build-slots`. Every selected driver had its own granted and released lease. The wrapper notes that the tool's implicit bootstrap build uses the default node count; the checkpoint test build uses the granted maxcpucount and nodeReuse=false.

## Per-row results

| Row | Exact filter | Executed | Passed | Failed | Skipped | Row driver duration | Test host wall | TRX tests wall | Lease (zero wait) |
|---|---|---:|---:|---:|---:|---:|---:|---:|---|
| CP-35 | `/*/*/RunnerSessionGenerationTests/*` | 5 | 5 | 0 | 0 | 19.1902874 s | 11.8024341 s | 9.5206238 s | `6207c238-16d5-48fc-8a3d-e8ec2cdd2082` |
| CP-36 | `/*/*/CompactionContinuationStopTests/*` | 6 | 6 | 0 | 0 | 19.4658715 s | 12.5040444 s | 8.2756307 s | `fb9938db-3d36-4c37-b6a0-61e91bc00276` |

Per-row commands to select the same checks through the tool are the command above with `--rows CP-35` or `--rows CP-36` respectively. The actual verification used the combined serial selection, preserving build reuse. No direct ad hoc test command was run.

TRX paths:

- CP-35: `C:\Antiphon\worktrees\card-task-413cf221\.antiphon\checkpoints\20261009-030043-f4db\rows\CP-35\run.trx`
- CP-36: `C:\Antiphon\worktrees\card-task-413cf221\.antiphon\checkpoints\20261009-030043-f4db\rows\CP-36\run.trx`

Evidence root: `C:\Antiphon\worktrees\card-task-413cf221\.antiphon\checkpoints\20261009-030043-f4db`. `report.json`, `report.md`, `executor.log`, build log, row console logs and TRX remain ignored; only this Markdown note is committed. The generic footer lists eight manifest build definitions, but seven are unused in state.json: only `bin-c1153-final-windows` was actually built for this selection.

## Source validation and cleanup

```powershell
pwsh -NoProfile -File scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261009-030043-f4db/report.json -ExpectedSourceSha 25bd11100f294be551f3ac48b02fbbfe9325bd02 -Rows CP-35,CP-36
```

Exit 0: `CHECKPOINT SOURCE VALID source=25bd11100f294be551f3ac48b02fbbfe9325bd02 rows=2`. Both receipts have dirty=0, sourceState=clean and buildSource=verified. The tool automatically removed its shadow copy and owned test-build outputs on green. The uniquely named tool bootstrap output was removed after resolving and verifying its absolute target inside this worktree. No source edits occurred while any driver was running.

No assertion failed, so inherited-red or environment-vs-product failure classification is not applicable. The four CP-36 failures previously recorded on the Linux mirror in the test design do not reproduce on this Windows host: all six cases passed without changes.

## Unedited checkpoint receipts

```text
CHECKPOINT CP-35 commit=25bd11100f294be551f3ac48b02fbbfe9325bd02 build=ok filter=/*/*/RunnerSessionGenerationTests/* executed=5 passed=5 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-413cf221\.antiphon\checkpoints\20261009-030043-f4db\rows\CP-35\run.trx slot=granted waited=0s dirty=0 source=25bd11100f294be551f3ac48b02fbbfe9325bd02 sourceState=clean buildSource=verified
CHECKPOINT CP-36 commit=25bd11100f294be551f3ac48b02fbbfe9325bd02 build=reused filter=/*/*/CompactionContinuationStopTests/* executed=6 passed=6 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-413cf221\.antiphon\checkpoints\20261009-030043-f4db\rows\CP-36\run.trx slot=granted waited=0s dirty=0 source=25bd11100f294be551f3ac48b02fbbfe9325bd02 sourceState=clean buildSource=verified
```

--- next stage ---
next: land
handoff: CARD-1153 Windows CP-35 and CP-36 are green; land this verification note only.
artifact: docs/investigations/2026-10-09-card-1153-windows-rows.md
