# CARD-1313 Windows rows: blocked before execution

Test task `0d0adf42`; candidate and task base `585303a77a4cf758d83be104bed7ad1e95082559`; branch `feat/card-task-0d0adf42`.

**CP-19, CP-22 and CP-23 are not run.** No test/build driver was started, no failure was rerun, and no Windows qualification is claimed. The brief prohibits contacting production runner 17204 or AppHost 17202. Both mandatory build-slot entry points default to `http://localhost:17204/build-slots`, and this session has no `ANTIPHON_BUILD_SLOTS_URL` override. Clarification was requested about allowing only that broker endpoint or providing an isolated broker URL; no answer was available when this report was prepared. No production endpoint was contacted, no service was restarted, and no slot bypass or deliberately unreachable broker was used.

## Counts and cost

| Row | Required executions | Actual executed | Passed | Failed | Skipped | Build/test wall time | Outcome |
|---|---:|---:|---:|---:|---:|---:|---|
| CP-19 | 3 | 0 | 0 | 0 | 0 | 0 s | Blocked before build |
| CP-22 | 1 | 0 | 0 | 0 | 0 | 0 s | Blocked before build |
| CP-23 | 1 | 0 | 0 | 0 | 0 | 0 s | Blocked before producing build |

Discovery executions: **0**. Producing assembly hashes, expanded rosters, fresh TRX, source/build certificates and CHECKPOINT lines: **none**, because no driver ran. There are no alternate build outputs to remove. The zero failure counts mean no tests ran; they are not green results. Build/test elapsed time is exactly zero; preflight/read time was not instrumented.

## Failure and blocker table

| Name | Error / evidence | Inherited from base? | Isolated rerun |
|---|---|---|---|
| Build-slot endpoint restriction | Brief prohibits 17204; `scripts/lib/build-slot.ps1` defaults there on Windows; no alternate broker environment setting exists. A permitted broker endpoint or clarification is required before building. | Environment/instruction conflict; not a candidate test failure. | Not applicable: no driver started. |
| Full-plan import compatibility | Static inspection shows CP-6/7/8/12/13/14/23 have fractional `EstimatedMinutes`; `PlanTableImporter` requires positive integers and imports the whole table before applying `--rows`. This was not an executed importer failure in this pass. | Yes: present in the supplied candidate/task base. The candidate's S1-S3 Code report already documents the same limitation for its portable rows. | Not applicable: static preflight finding, not a failed test. |
| Runtime test failures | Unknown: none of the five required executions ran. | Not determined. | None available to rerun. |

No production or test code was fixed. An ignored execution subset was prepared at `.antiphon/c1313-windows-rows-plan.md` (SHA-256 `BA9C032C99F13593E33140B3C4116D367803D1DABCA88512E09A17A397F3EBE1`). It retains the three approved row IDs, exact filters, floors, outputs, build reuse, serial flags and `After=all`. Its sole metadata normalization is CP-23 `EstimatedMinutes=1`, the importer's minimum integer, instead of the approved historical `0.5`. The original plan is unchanged. This subset has not been run or importer-validated.

## Commands and resumption

Executed source/environment checks included:

```powershell
git status --short
git branch --show-current
git rev-parse HEAD
Get-Item Env:ANTIPHON_BUILD_SLOTS_URL -ErrorAction SilentlyContinue
Get-Content -LiteralPath 'scripts/lib/build-slot.ps1' -TotalCount 100
Get-Content -LiteralPath 'tools/Antiphon.Checkpoints/Manifest/PlanTableImporter.cs'
git diff --quiet
git status --porcelain=v1
```

The initial source checks returned a clean tree on the assigned branch at the full candidate SHA above. Relevant owner: `docs/testing-and-build.md`, Checkpoint runner tool / Build slots. Requested filters remain exactly:

```text
CP-19 /*/*/TestSelectionTests/C1313_PathIdentity
CP-22 /*/*/ClaudeVerifiedDeliveryTests/Fake_evidence_appears_then_submit_completes_a_turn
CP-23 /*/*/ClaudeVerifiedDeliveryTests/C1313_DeliveryDiscovery
```

The following are **proposed commands, not executed commands**. First resolve the broker restriction and set `ANTIPHON_BUILD_SLOTS_URL` to the permitted broker URL. Resume from a clean worktree at the candidate SHA; the later report-only commit must not be relabeled as the tested candidate. Bootstrap the required checkpoint tool through the gate, using the installed regular PowerShell executable:

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -File scripts/build-slot.ps1 -Label c1313-windows-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1313-windows-tool/ -nodeReuse:false
dotnet tools/Antiphon.Checkpoints/bin-c1313-windows-tool/net9.0/Antiphon.Checkpoints.dll run --plan .antiphon/c1313-windows-rows-plan.md --rows CP-19,CP-22,CP-23 --expected-source-sha 585303a77a4cf758d83be104bed7ad1e95082559 --keep-outputs --max-wait 570s
```

The checkpoint tool acquires its own per-driver slots; do not wrap its executing rows in a second slot-owning layer. Continue `wait --run <run-id> --max-wait 570s` until terminal if exit 75 is returned. The bootstrap is an additional build necessary to use the required tool, not an extra verification lane.

The producing main assembly still needs its once-per-build discovery; the PTY build needs the exact `/*/*/ClaudeVerifiedDeliveryTests/*` discovery with five delivery UIDs, excluding the discovery assertion itself. Use the plan's argument vector, existing owned process boundary and nightly converters, with the actual assembly hash and evidence outside sealed output. Preserve actual commands, discovery/row counts, wall costs, unedited CHECKPOINT lines and source/build receipts. Run main and PTY assemblies sequentially. Rerun every failed method alone once without repairing code; only then assess base inheritance. Remove only inventoried task-owned alternate outputs after consumers finish.

--- next stage ---
next: test
handoff: Resolve the permitted build-slot broker endpoint, then execute CP-19/22/23 and their discovery prerequisites at the pinned candidate. Windows verification remains incomplete.
artifact: docs/investigations/2026-10-16-card-1313-windows-rows.md
