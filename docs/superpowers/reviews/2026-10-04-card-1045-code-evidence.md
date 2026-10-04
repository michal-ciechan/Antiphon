# CARD-1045 Code evidence

S1 implemented; all seven Windows checkpoints passed (7 executed, 7 passed, 0 failed/skipped). Ordinary verification is complete and ready for Review.

Original Code task / landing owner: `1363353b-4c42-4986-87ea-1717c39aa167`.
Branch: `feat/card-task-1363353b`.
Worktree: `C:\Antiphon\worktrees\card-task-1363353b`.
Task base / landed plan: `c53cb64758b342ef4acb1eee9483b09f85ec3803`.
Implementation and actual tested source: `844cd22f40c233e6b5b2e9ff7c1538fe4980285d` (pushed before checkpoint execution).
This evidence is a later documentation-only commit; product and test source are unchanged since the tested commit.
Plan: `docs/superpowers/plans/2026-10-04-card-1045-nightly-child-environment-plan.md`.

Changed files: `scripts/lib/nightly-owned-process.ps1`, `scripts/lib/nightly-owned-process.cs`, `scripts/test-nightly-native.ps1`, `scripts/fixtures/nightly/owned-child/Program.cs`, `tests/Antiphon.Tests/Scripts/NightlyNativeOwnershipTests.cs`.

The wrapper preserves null versus an explicit empty array. CreateProcess receives a null pointer for inheritance; explicit blocks contain only supplied nonempty values and retain double-NUL Unicode framing. The fixture reads the actual raw child block, discloses only synthetic values and policy-name presence, and receives control via argv. Each new method requires its decisive PASS label; the harness accepts observations only after exit zero, completed cleanup and both drained terminal sentinels. Existing ownership assertions and timeouts are unchanged. No parent environment mutation was added to production.

The brief explicitly overrides its generic Final profile: "No whole-Unit run and no whole Final Unit lane ... run only the closed checkpoint list." D-4 of the landed plan likewise requires only seven methods. CP-1..CP-7 cover every method of the full affected integration class. No Unit/full assembly/nightly/qualification run was performed or claimed. No deferred ordinary V/R IDs or manual acceptance remain for this scoped brief.

| ID | Outcome | Checkpoint |
|---|---|---|
| V-1 | Passed; omitted and explicit null inherit the exact sentinel; parent unchanged | CP-1, 1 result |
| V-2 | Passed; unlisted parent name absent, one case-insensitive override, exact spaces/equals/Unicode values; parent unchanged | CP-2, 1 result |
| V-3 | Passed; literal empty map starts successfully and excludes both selected parent names; parent unchanged | CP-3, 1 result |
| V-4 | Passed; empty/null names absent in raw block, retained value exact, all-cleared replacement, real full-copy policy clears absent and unrelated sentinel retained; parent unchanged | CP-4, 1 result |
| R-1 | Passed; assignment/refusal/argv, descendants/root wait/breakaway, and pipe/log drain/production cleanup | CP-5..CP-7, 3 results |

Execution: checkpoint tool `run --plan docs/superpowers/plans/2026-10-04-card-1045-nightly-child-environment-plan.md --after S1 --expected-source-sha 844cd22f40c233e6b5b2e9ff7c1538fe4980285d --max-wait 50s`, followed by foreground `wait --run 20261004-195858-c491 --max-wait 50s` until terminal exit 0. One isolated product build, seven serial exact method runs, no reruns/repetitions. Wall time 3m26s. Every build/row slot was granted with zero wait. Source remained frozen through the run.

Declared unlisted tooling work: isolated checkpoint-tool bootstrap via `scripts/build-slot.ps1 -Label c1045-tool-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1045-tool/ --nologo '-nodeReuse:false'`. The first invocation omitted quotes and PowerShell split `-nodeReuse:false`, yielding MSB1041 before compilation; corrected invocation passed with 1 existing CS8602 warning and 0 errors. Both leases were granted, waited=0s. This is the plan-authorized tool bootstrap, not another product build/test.

Read-only checks: PowerShell syntax and ASCII checks, `git diff --check`, independent fresh TRX inspection for all seven exact class/method names and nonzero counts, and `validate-checkpoint-receipt.ps1` against the full tested SHA and CP-1..CP-7 (exit 0, source valid, clean receipts and verified build provenance). `scripts/check-evidence-diff.ps1` ran over the full task base..HEAD, with zero violations; it is rerun after this documentation commit before settlement.

Runtime evidence root: `C:\Antiphon\worktrees\card-task-1363353b\.antiphon\checkpoints\20261004-195858-c491` (ignored).
Structured receipt: `report.json`; unedited checkpoint report: `report.md`; fresh TRX: `rows/CP-n/run.trx`.
The tool deleted its product output `bin-c1045-env/` and stopped/deleted the executor shadow. Automatic approval review rejected deletion of the verified, literal isolated bootstrap directory `C:\Antiphon\worktrees\card-task-1363353b\tools\Antiphon.Checkpoints\bin-c1045-tool` with only "blocked by policy" and no further reason. That ignored directory remains for caller cleanup. No processes are left running by this task.

Platform routes reread: runner-defaults revision 2, no kind overrides/unresolved references; session-runners showed eligible Windows and Linux descriptors plus an unavailable draining descriptor. Native execution used the Windows lane, with no runner pin or fleet location in source.

PC-1 pending: null -> empty typed array, exact `C1045_NullEnvironmentInherits`; witness `C1045 null inherits parent sentinel`.
PC-2 pending: seed explicit-map dictionary from parent, exact `C1045_SuppliedEnvironmentReplaces`; witness `C1045 supplied map excludes parent sentinel`.
PC-3 pending: admit maps only when Count > 0, exact `C1045_EmptyEnvironmentDoesNotInherit`; witness `C1045 empty map excludes parent sentinel`.
PC-4 pending: serialize empty entries, exact `C1045_ClearedEntriesAreAbsent`; witness `C1045 cleared names absent from child block`.
Each is a direct child-observation assertion against the corresponding production boundary, not a self-comparison/constant. No deliberate mutant or red/restore/green cycle was run here; SourceLanding Mutation owns reachability proof after Review and implementation land. CARD-1039 custody PCs retain their separate obligations and receive no new completion credit.

Restart: none. No server or runner restart is required for these scripts; the ordinary next harness process loads them from source. Caller owns any operational activation/qualification separately.

--- next stage ---
next: review
handoff: Review S1 on Windows with no runner pin using CP-1..CP-7 only; validate source-qualified evidence, then caller lands original Code task 1363353b and commissions method-scoped SourceLanding Mutation for pending PC-1..PC-4.
artifact: docs/superpowers/reviews/2026-10-04-card-1045-code-evidence.md

## Unedited checkpoint report

```text
--- checkpoint report ---
run: 20261004-195858-c491   manifest: C:\Antiphon\worktrees\card-task-1363353b\.antiphon\checkpoints\20261004-195858-c491\manifest.resolved.yaml
commit: 844cd22f40c233e6b5b2e9ff7c1538fe4980285d  branch: feat/card-task-1363353b  worktree: C:\Antiphon\worktrees\card-task-1363353b  host: Microsoft Windows 10.0.19045 cores=8
source: 844cd22f40c233e6b5b2e9ff7c1538fe4980285d state=clean buildSource=verified
CHECKPOINT CP-1 commit=844cd22f40c233e6b5b2e9ff7c1538fe4980285d build=ok filter=/*/*/NightlyNativeOwnershipTests/C1045_NullEnvironmentInherits executed=1 passed=1 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-1363353b\.antiphon\checkpoints\20261004-195858-c491\rows\CP-1\run.trx slot=granted waited=0s dirty=0 source=844cd22f40c233e6b5b2e9ff7c1538fe4980285d sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=107.0764311s startup=3.0201484s testsWall=3.8268694s teardown=0.3823516s hostWall=7.2294276s
CHECKPOINT CP-2 commit=844cd22f40c233e6b5b2e9ff7c1538fe4980285d build=reused filter=/*/*/NightlyNativeOwnershipTests/C1045_SuppliedEnvironmentReplaces executed=1 passed=1 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-1363353b\.antiphon\checkpoints\20261004-195858-c491\rows\CP-2\run.trx slot=granted waited=0s dirty=0 source=844cd22f40c233e6b5b2e9ff7c1538fe4980285d sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=0s startup=2.7858357s testsWall=2.8924645s teardown=0.4116366s hostWall=6.089995s
CHECKPOINT CP-3 commit=844cd22f40c233e6b5b2e9ff7c1538fe4980285d build=reused filter=/*/*/NightlyNativeOwnershipTests/C1045_EmptyEnvironmentDoesNotInherit executed=1 passed=1 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-1363353b\.antiphon\checkpoints\20261004-195858-c491\rows\CP-3\run.trx slot=granted waited=0s dirty=0 source=844cd22f40c233e6b5b2e9ff7c1538fe4980285d sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=0s startup=2.680234s testsWall=3.1592904s teardown=0.4073778s hostWall=6.2469625s
CHECKPOINT CP-4 commit=844cd22f40c233e6b5b2e9ff7c1538fe4980285d build=reused filter=/*/*/NightlyNativeOwnershipTests/C1045_ClearedEntriesAreAbsent executed=1 passed=1 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-1363353b\.antiphon\checkpoints\20261004-195858-c491\rows\CP-4\run.trx slot=granted waited=0s dirty=0 source=844cd22f40c233e6b5b2e9ff7c1538fe4980285d sourceState=clean buildSource=verified
PHASES CP-4 slotWait=0s build=0s startup=2.2049308s testsWall=2.3567717s teardown=0.4541838s hostWall=5.0159341s
CHECKPOINT CP-5 commit=844cd22f40c233e6b5b2e9ff7c1538fe4980285d build=reused filter=/*/*/NightlyNativeOwnershipTests/C1039_AssignBeforeResume executed=1 passed=1 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-1363353b\.antiphon\checkpoints\20261004-195858-c491\rows\CP-5\run.trx slot=granted waited=0s dirty=0 source=844cd22f40c233e6b5b2e9ff7c1538fe4980285d sourceState=clean buildSource=verified
PHASES CP-5 slotWait=0s build=0s startup=2.5694175s testsWall=3.9845672s teardown=0.439838s hostWall=6.9938893s
CHECKPOINT CP-6 commit=844cd22f40c233e6b5b2e9ff7c1538fe4980285d build=reused filter=/*/*/NightlyNativeOwnershipTests/C1039_DescendantExit executed=1 passed=1 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-1363353b\.antiphon\checkpoints\20261004-195858-c491\rows\CP-6\run.trx slot=granted waited=0s dirty=0 source=844cd22f40c233e6b5b2e9ff7c1538fe4980285d sourceState=clean buildSource=verified
PHASES CP-6 slotWait=0s build=0s startup=3.3486594s testsWall=4.0522165s teardown=0.443655s hostWall=7.8446051s
CHECKPOINT CP-7 commit=844cd22f40c233e6b5b2e9ff7c1538fe4980285d build=reused filter=/*/*/NightlyNativeOwnershipTests/C1039_DrainBeforeReturn executed=1 passed=1 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-1363353b\.antiphon\checkpoints\20261004-195858-c491\rows\CP-7\run.trx slot=granted waited=0s dirty=0 source=844cd22f40c233e6b5b2e9ff7c1538fe4980285d sourceState=clean buildSource=verified
PHASES CP-7 slotWait=0s build=0s startup=2.133475s testsWall=3.6754586s teardown=0.3928341s hostWall=6.2018267s
unlisted: none (the tool ran no other build or test command)
wall: 3m26s  sequential-equivalent: 1m32s  builds: 1  max-concurrent-builds: 1  rows: 7 green 0 red 0 skipped
outputs: deleted bin-c1045-env/
evidence: C:\Antiphon\worktrees\card-task-1363353b\.antiphon\checkpoints\20261004-195858-c491\report.md
verdict: GREEN exit=0

```
