# CARD-1613 native Windows verification

## Final outcome

CP-9, CP-10 and CP-11 pass natively after two small test-harness repairs: **7 passed, 0 failed, 0 skipped**. The additional stamp-helper selection passes **4/4**. The comparison covers **41 of 45 policy rows**, with **518 expanded cases at each requested revision and zero outcome, reason or identity/multiplicity differences**. The brief's Antiphon.Tests-first budget fallback was used: four policy rows in three smaller suites remain unverified. Two portable landing tests fail at the implementation tip and pass at the requested base; this is not a land approval.

Assigned branch: `feat/card-task-fa7879ff`.

- Red-evidence commit, pushed: `6088ccae17d24197a00b85b35791072f56657793`.
- Repair commit, pushed and verified: `88be8bb427e12f69d8005a7ab32aa1fbc5cfd2ce`.
- Final documentation commit is the commit containing this report; no code changed after the verified repair commit.

### Repairs and verification

`tests/Antiphon.Tests/Scripts/NativeLinuxPolicyExecutionTests.cs` now checks the actual native platform. On Windows it checks the exact nine-node TRX identity, the eight exact skip reasons, the passing portable node, and the portable-only side effect. Missing side-effect files are treated as empty. A deliberately changed reason must be rejected. The Linux assertions remain in place; they were not rerun in this Windows task.

`tests/Antiphon.Tests/Scripts/OuterRowBuildStamp.cs` accepts the checkpoint's existing isolated-output properties only after independently deriving the expected ArtifactsPath and UseArtifactsOutput values and verifying the full source stamp. It does not rewrite an existing stamp. `OuterRowBuildStampTests.cs` adds coverage for acceptance without replacement and rejection of changed paths, flags and extra properties. The original CP-10 failure was reproduced at the requested base before fixing it.

Final run: `20261029-223252-7dd2`, source `88be8bb427e12f69d8005a7ab32aa1fbc5cfd2ce`, clean at start/end, buildSource verified. Host: native Windows 10.0.19045. Counts are executed/passed/failed/skipped:

| Row | Counts | Row wall |
|---|---|---|
| CP-9 | 4/4/0/0 | 31.3s |
| CP-10 | 1/1/0/0 | 40.2s |
| CP-11 | 2/2/0/0 | 51.3s |
| STAMP | 4/4/0/0 | 18.2s |

One fresh isolated build served the three exact planned filters and the justified extra helper selection. The imported plan's Windows output was renamed to `bin-fa7879ff-repair` because the original failed run's output remained. The STAMP filter was `/*/*/OuterRowBuildStampTests/*`. Every build/test slot was granted with zero wait; no unleased driver was used. Final wall time was **28m19s**, including **781.582s build** and **707.107s finalization/owned cleanup**. The executor finished with exit 0 and the repair output directory was confirmed absent. No timeout was widened and no work was left running. Cleanup's cost is material even though the run remained below 30 minutes.

Receipt: `C:\Antiphon\worktrees\card-task-fa7879ff\.antiphon\checkpoints\20261029-223252-7dd2\report.json` and adjacent `report.md`.

Validation returned exit 0:

```text
CHECKPOINT SOURCE VALID source=88be8bb427e12f69d8005a7ab32aa1fbc5cfd2ce rows=4
```

Validation command from the assigned worktree:

```powershell
dotnet tools/Antiphon.Checkpoints/bin-fa7879ff-tool/Antiphon.Checkpoints.dll validate --evidence .antiphon/checkpoints/20261029-223252-7dd2/report.json --expected-source-sha 88be8bb427e12f69d8005a7ab32aa1fbc5cfd2ce --rows CP-9,CP-10,CP-11,STAMP
```

### Exact comparison and remaining work

The comparison is specifically requested base `ade54222543381e2e1b31ad79d8ae33a4035f70b` versus original implementation `aa72491b72b1af57141e7c37ac50a384485755a0`, not the repaired source. All 518 cases are NotExecuted at both revisions. The comparer matches case identity ordinally, checks multiplicity, and compares outcome plus the SHA-256 of each untrimmed decoded TRX skip reason's UTF-8 bytes. Every selected policy row and named method is present, with zero non-policy nodes. The final rerun again produced zero differences. These intentional skips are diagnostic evidence, not passing qualification tests.

Rerun the existing evidence comparison without a build:

```powershell
pwsh -NoProfile -File .antiphon/c1613-windows-fa7879ff/compare-results.ps1 -Inputs .antiphon/c1613-windows-fa7879ff/comparison-inputs.json
```

The following four policy rows were **not run at either revision**:

| Suite | Outstanding rows |
|---|---|
| Antiphon.SessionRunner.Tests | CompletedCardWorkspaceRemovalTests (C1032_InspectionR3Mode and C1032_RemovalLinuxIgnoredFifoRefuses); HostStatsProbeLiveTests.Linux_probe_reads_live_proc |
| Antiphon.PtyHost.Tests | LinuxCgroupCustodyTests |
| Antiphon.Agents.Pty.Tests | UnixPtyArgvTests |

Prepared exact manifests remain in detached task-created checkouts: base `C:\Antiphon\worktrees\c1613-base-fa7879ff\.antiphon\fa7879ff-base-exact.yaml`; original tip `C:\Antiphon\worktrees\c1613-tip-fa7879ff\.antiphon\fa7879ff-tip-exact.yaml`. Select only `CMP-session-runner-methods,CMP-pty-host-classes,CMP-agents-pty-classes`, serially through the checkpoint runner, then add their TRX paths to comparison-inputs.json. The original-tip detached checkout has not been built. The assigned worktree's ignored evidence root also contains the exact filters, selection generation scripts and both manifests.

### Confirmed additional defects; no land approval

The broad initial tip selection timed out without a TRX but its console contains explicit assertion failures for:

- `LandGraphEvaluationTests.C1483_S4_RealRepositoryApproves`: `KeepNativeLinuxPolicyFixturesInMainAssembly` has a missing landing-graph pin.
- `LandGraphEvaluationTests.C1483_S5_CheckRequiresEvaluationRecord`: EmbeddedResource `Fixtures\NativeLinuxPolicy\*.cs.txt` selects a text document forbidden by the definition-file audit; the diagnostic says `-Update` cannot approve it.

Both exact tests were rerun at the requested base and **passed (2 executed, 2 passed, 0 failed, 0 skipped)** with clean source and verified build provenance. Base TRX: `C:\Antiphon\worktrees\c1613-base-fa7879ff\.antiphon\fa7879ff-base-portable\BASE-land-portable-20261029-222439-fef7\run.trx`. They are regressions observed at the implementation tip relative to the requested base, not inherited red. The native repairs do not change the offending project target or resource items; these defects remain open. Caller should commission a Code fix for fixture packaging/landing compatibility and complete the four outstanding comparison rows before seeking a land approval. No target merge, publication or activation was attempted.

### Retained outputs and custody

The successful repair output was deleted by its owning checkpoint executor. The following task-created outputs remain: assigned worktree `tests\Antiphon.Tests\bin-c1613-windows`, assigned worktree `tools\Antiphon.Checkpoints\bin-fa7879ff-tool`, and base worktree `tests\Antiphon.Tests\bin-fa7879ff-base-antiphon`. The original and base runs were red and kept their outputs; the tool output remains available for evidence validation/continuation. Detached comparison worktrees and ignored raw evidence are retained.

Automatic approval review rejected recursive removal of the task-created original output with `blocked by policy`. The rejection was not bypassed. The canonical checkout was only read for the assigned brief; AppHost, production runners, other people's paths and the rc branch were not changed. All executors and test hosts were awaited to completion. Generated JSON/TRX/logs remain ignored; only this individual Markdown report is committed.

## Historical red-evidence slice (preserved)

## Preserved red evidence before repairs

Assigned source: `aa72491b72b1af57141e7c37ac50a384485755a0`.
Requested comparison base: `ade54222543381e2e1b31ad79d8ae33a4035f70b`.
Host: native Windows 10.0.19045, .NET 9.0.22, SDK 10.0.300.

Checkpoint run `20261029-213135-7252` used the committed plan, selecting only CP-9, CP-10 and CP-11. Its source was clean and buildSource verified. Counts are executed/passed/failed/skipped:

| Row | Counts | Result |
|---|---|---|
| CP-9 | 4/3/1/0 | NativeLinuxPolicyExecutionTests always asserted Linux's nine-pass result on Windows. The real child correctly returned 1 passed, 8 skipped, and no marked side effects. |
| CP-10 | 1/0/1/0 | `c1210-child-source-stamp`: OuterRowBuildStamp expected no properties, but the checkpoint build records ArtifactsPath and UseArtifactsOutput. |
| CP-11 | 2/2/0/0 | Both retained C1050 parents passed. |

Receipt: `C:\Antiphon\worktrees\card-task-fa7879ff\.antiphon\checkpoints\20261029-213135-7252\report.md`.
Wall 17m06s; isolated build 822.668s; row walls 26.924s, 21.639s and 70.928s. Build and rows received host slots.

Base run `20261029-220712-2bfe` reproduced CP-10: 1 executed, 1 failed, identical source-stamp assertion. This failure predates CARD-1613. The native parent failure is present at this task's immutable starting source. No repair had been applied for either observation.

## Antiphon.Tests comparison

All 41 policy rows are covered: 165 expanded class-row cases and 353 expanded method-row cases. All **518** are NotExecuted at both requested revisions. Per-case identity/multiplicity, outcome and UTF-8 skip-reason digest comparison reports **zero differences**. Exact-selection receipts have clean source and verified build provenance; their intentional skips are comparison evidence, not green qualification certificates.

Machine-readable observations, row coverage, TRX hashes and comparison inputs are in `C:\Antiphon\worktrees\card-task-fa7879ff\.antiphon\c1613-windows-fa7879ff\` (`comparison.json`, `case-observations.json`, `row-coverage.json`, `comparison-inputs.json`). The original TRX files remain at the absolute paths recorded there. Base receipts are under `C:\Antiphon\worktrees\c1613-base-fa7879ff\.antiphon\checkpoints\20261029-220712-2bfe\`.

The first attempt selected all 41 whole classes, including portable siblings. It hit the unchanged 15-minute row timeout without a TRX. Its console recorded two additional failures: `LandGraphEvaluationTests.C1483_S4_RealRepositoryApproves` refused the new `KeepNativeLinuxPolicyFixturesInMainAssembly` target's missing pin; `C1483_S5_CheckRequiresEvaluationRecord` refused embedded `Fixtures/NativeLinuxPolicy/*.cs.txt` documents. The narrower retry selected exactly the policy's class rows and named methods and checked every returned node against the policy, with no extras. The whole-class attempt is retained at `.antiphon/c1613-windows-fa7879ff/tip-antiphon/CMP-tip-antiphon-20261029-214950-988c/`.

Base attribution of those two portable landing tests was still running when this red-evidence slice was committed. The three smaller suites and repaired native checkpoints remain pending in this historical slice.

## Custody and constraints

The canonical checkout, AppHost and production runners were not modified. All build/test drivers used the host slot gate. No tags, deployment, target publication, rebase or branch switch occurred. Comparison checkouts are detached and task-created.

Automatic approval review rejected recursive removal of the task-created `C:\Antiphon\worktrees\card-task-fa7879ff\tests\Antiphon.Tests\bin-c1613-windows` directory with `blocked by policy`. It remains intact. Repairs will use a fresh isolated output. The initial bootstrap command had a quoting error in `-nodeReuse:false` before any compilation; the quoted retry succeeded with 0 errors and 5 warnings.

## Checkpoint receipt excerpts (essential lines unedited)

~~~~text
--- checkpoint report ---
run: 20261029-213135-7252   manifest: C:\Antiphon\worktrees\card-task-fa7879ff\.antiphon\checkpoints\20261029-213135-7252\manifest.resolved.yaml
commit: aa72491b72b1af57141e7c37ac50a384485755a0  branch: feat/card-task-fa7879ff  worktree: C:\Antiphon\worktrees\card-task-fa7879ff  host: Microsoft Windows 10.0.19045 cores=8
source: aa72491b72b1af57141e7c37ac50a384485755a0 state=clean buildSource=verified
CHECKPOINT CP-9 commit=aa72491b72b1af57141e7c37ac50a384485755a0 build=ok filter=/*/*/(NativeLinuxPolicyWitnessTests*)|(NativeLinuxPolicyExecutionTests*)/* executed=4 passed=3 failed=1 skipped=0 trx=C:\Antiphon\worktrees\card-task-fa7879ff\.antiphon\checkpoints\20261029-213135-7252\rows\CP-9\run.trx slot=granted waited=0s dirty=0 source=aa72491b72b1af57141e7c37ac50a384485755a0 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-9 waited=0s executed=24.865s wall=26.924s
PHASES CP-9 slotWait=0s build=822.6680116s startup=7.9814814s testsWall=3.4342653s teardown=0.6327367s hostWall=12.0486525s
FAILED Antiphon.Tests.Scripts.NativeLinuxPolicyExecutionTests.Linux_child_runs_nine_nodes_and_records_side_effects (CP-9) ShouldAssertException: live     should be empty but had 30     items and was ["linux-counts passed=1 failed=0 skipped=8", "direct-class.field seen=0 want=1", "direct-class.ctor seen=0 want=1", "direct-class.before seen=0 want=1", "direct-class.body seen=0 want=1", "direct-method.field seen=0 want=1", "direct-method.ctor seen=0 want=1", "direct-method.before seen=0 want=1", "direct-method.body seen=0 want=1", "inherited.field seen=0 want=1", "inherited.ctor seen=0 want=1", "inherited.before seen=0 want=1", "inherited.body seen=0 want=1", "marked-derived.field seen=0 want=1", "marked-derived.ctor seen=0 want=1", "marked-derived.before seen=0 want=1", "marked-derived.body seen=0 want=1", "nested.field seen=0 want=1", "nested.ctor seen=0 want=1", "nested.before seen=0 want=1", "nested.body seen=0 want=1", "generic.field seen=0 want=1", "generic.ctor seen=0 want=1", "generic.before seen=0 want=1", "generic.body seen=0 want=1", "overload.field seen=0 want=2", "overload.ctor seen=0 want=2", "overload.before seen=0 want=2", "overload-int.body seen=0 want=1", "overload-string.body seen=0 want=1"]  Additional Info:     linux-counts passed=1 failed=0 skipped=8; direct-class.field seen=0 want=1; direct-class.ctor seen=0 want=1; direct-class.before seen=0 want=1; direct-class.body seen=0 want=1; direct-method.field seen=0 want=1; direct-method.ctor seen=0 want=1; direct-method.before seen=0 want=1; direct-method.body seen=0 want=1; inherited.field seen=0 want=1; inherited.ctor seen=0 want=1; inherited.before seen=0 want=1; inherited.body seen=0 want=1; marked-derived.field seen=0 want=1; marked-derived.ctor seen=0 want=1; marked-derived.before seen=0 want=1; marked-derived.body seen=0 want=1; nested.field seen=0 want=1; nested.ctor seen=0 want=1; nested.before seen=0 want=1; nested.body seen=0 want=1; generic.field seen=0 want=1; generic.ctor seen=0 want=1; generic.before seen=0 want=1; generic.body seen=0 want=1; overload.field seen=0 want=2; overload.ctor seen=0 want=2; overload.before seen=0 want=2; overload-int.body seen=0 want=1; overload-string.body seen=0 want=1 Telemetry --------- Microsoft Testing Platform collects usage data in order to help us improve your experience. The data is collected by Microsoft and are not shared with anyone. You can opt-out of telemetry by setting the TESTINGPLATFORM_TELEMETRY_OPTOUT or DOTNET_CLI_TELEMETRY_OPTOUT environment variable to '1' or 'true' using your favorite shell.  Read more about Microsoft Testing Platform telemetry: https://aka.ms/testingplatform/telemetry skipped Runs (10ms)   direct class runs on Linux skipped Marked (0ms)   direct method runs on Linux skipped GenericCase (0ms)   generic class runs on Linux skipped Inherited (0ms)   inherited method runs on Linux skipped FromBase (0ms)   marked derived class runs on Linux skipped Nested (0ms)   nested class runs on Linux skipped Case(1) (0ms)   overload int runs on Linux skipped Case(a) (0ms)   overload string runs on Linux HTML test report written to: C:\Users\lndco\AppData\Local\Temp\c1613-probe-tiq4lfjq.xau\Antiphon.NativeLinuxPolicy.Probe-windows-net9.0-report.html    In process file artifacts produced:     - C:\Users\lndco\AppData\Local\Temp\c1613-probe-tiq4lfjq.xau\Antiphon.NativeLinuxPolicy.Probe-windows-net9.0-report.html     - C:\Users\lndco\AppData\Local\Temp\c1613-probe-tiq4lfjq.xau\probe.trx  Test run summary: Passed! - C:\Antiphon\worktrees\card-task-fa7879ff\tests\Antiphon.Tests\bin-c1613-windows\native-linux-probe\Antiphon.NativeLinuxPolicy.Probe.dll (net9.0|x64)   total: 9   failed: 0   succeeded: 1   skipped: 8   duration: 614ms -> rows/CP-9/failures.md
CHECKPOINT CP-10 commit=aa72491b72b1af57141e7c37ac50a384485755a0 build=reused filter=/*/*/WindowsUnitPlatformAdmissionTests/C1210_Windows_linux_only_outcomes_are_exact executed=1 passed=0 failed=1 skipped=0 trx=C:\Antiphon\worktrees\card-task-fa7879ff\.antiphon\checkpoints\20261029-213135-7252\rows\CP-10\run.trx slot=granted waited=0s dirty=0 source=aa72491b72b1af57141e7c37ac50a384485755a0 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-10 waited=0s executed=20.438s wall=21.639s
PHASES CP-10 slotWait=0s build=0s startup=4.2821787s testsWall=3.793982s teardown=0.6311832s hostWall=8.7074283s
FAILED Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact (CP-10) ShouldAssertException: launch.SourceVerified     should be True     but was False  Additional Info:     c1210-child-source-stamp -> rows/CP-10/failures.md
CHECKPOINT CP-11 commit=aa72491b72b1af57141e7c37ac50a384485755a0 build=reused filter=/*/*/C1008PlatformContractTests/(C1050_Windows_remote_outcomes_are_exact*)|(C1050_Windows_rolling_outcomes_are_exact*) executed=2 passed=2 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-fa7879ff\.antiphon\checkpoints\20261029-213135-7252\rows\CP-11\run.trx slot=granted waited=0s dirty=0 source=aa72491b72b1af57141e7c37ac50a384485755a0 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-11 waited=0s executed=69.024s wall=70.928s
PHASES CP-11 slotWait=0s build=0s startup=3.9821636s testsWall=50.0758398s teardown=0.4514062s hostWall=54.5099387s
BUDGET wall=1025.482s execution=1025.475s admissionOnly=0.007s
unlisted: none (the tool ran no other build or test command)
wall: 17m06s  sequential-equivalent: 1m59s  builds: 5  max-concurrent-builds: 1  rows: 1 green 2 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1613-windows/
evidence: C:\Antiphon\worktrees\card-task-fa7879ff\.antiphon\checkpoints\20261029-213135-7252\report.md
verdict: RED exit=1
~~~~

~~~~text
--- checkpoint report ---
run: 20261029-223252-7dd2   manifest: C:\Antiphon\worktrees\card-task-fa7879ff\.antiphon\checkpoints\20261029-223252-7dd2\manifest.resolved.yaml
commit: 88be8bb427e12f69d8005a7ab32aa1fbc5cfd2ce  branch: feat/card-task-fa7879ff  worktree: C:\Antiphon\worktrees\card-task-fa7879ff  host: Microsoft Windows 10.0.19045 cores=8
source: 88be8bb427e12f69d8005a7ab32aa1fbc5cfd2ce state=clean buildSource=verified
CHECKPOINT CP-9 commit=88be8bb427e12f69d8005a7ab32aa1fbc5cfd2ce build=ok filter=/*/*/(NativeLinuxPolicyWitnessTests*)|(NativeLinuxPolicyExecutionTests*)/* executed=4 passed=4 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-fa7879ff\.antiphon\checkpoints\20261029-223252-7dd2\rows\CP-9\run.trx slot=granted waited=0s dirty=0 source=88be8bb427e12f69d8005a7ab32aa1fbc5cfd2ce sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-9 waited=0s executed=30.069s wall=31.326s
PHASES CP-9 slotWait=0s build=781.5823377s startup=11.4341364s testsWall=4.0971329s teardown=0.7099963s hostWall=16.2414247s
CHECKPOINT CP-10 commit=88be8bb427e12f69d8005a7ab32aa1fbc5cfd2ce build=reused filter=/*/*/WindowsUnitPlatformAdmissionTests/C1210_Windows_linux_only_outcomes_are_exact executed=1 passed=1 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-fa7879ff\.antiphon\checkpoints\20261029-223252-7dd2\rows\CP-10\run.trx slot=granted waited=0s dirty=0 source=88be8bb427e12f69d8005a7ab32aa1fbc5cfd2ce sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-10 waited=0s executed=38.787s wall=40.156s
PHASES CP-10 slotWait=0s build=0s startup=4.5389291s testsWall=22.585364s teardown=0.5924653s hostWall=27.7170281s
CHECKPOINT CP-11 commit=88be8bb427e12f69d8005a7ab32aa1fbc5cfd2ce build=reused filter=/*/*/C1008PlatformContractTests/(C1050_Windows_remote_outcomes_are_exact*)|(C1050_Windows_rolling_outcomes_are_exact*) executed=2 passed=2 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-fa7879ff\.antiphon\checkpoints\20261029-223252-7dd2\rows\CP-11\run.trx slot=granted waited=0s dirty=0 source=88be8bb427e12f69d8005a7ab32aa1fbc5cfd2ce sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-11 waited=0s executed=50.188s wall=51.299s
PHASES CP-11 slotWait=0s build=0s startup=4.552782s testsWall=34.0168968s teardown=0.3682698s hostWall=38.9383263s
CHECKPOINT STAMP commit=88be8bb427e12f69d8005a7ab32aa1fbc5cfd2ce build=reused filter=/*/*/OuterRowBuildStampTests/* executed=4 passed=4 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-fa7879ff\.antiphon\checkpoints\20261029-223252-7dd2\rows\STAMP\run.trx slot=granted waited=0s dirty=0 source=88be8bb427e12f69d8005a7ab32aa1fbc5cfd2ce sourceState=clean buildSource=verified
CHECKPOINT-TIMING STAMP waited=0s executed=16.466s wall=18.221s
PHASES STAMP slotWait=0s build=0s startup=5.4747633s testsWall=0.9783921s teardown=0.4051706s hostWall=6.8583934s
BUDGET wall=1699.153s execution=1699.145s admissionOnly=0.008s
unlisted: none (the tool ran no other build or test command)
wall: 28m19s  sequential-equivalent: 2m21s  builds: 5  max-concurrent-builds: 1  rows: 4 green 0 red 0 skipped
outputs: deleted bin-fa7879ff-repair/
evidence: C:\Antiphon\worktrees\card-task-fa7879ff\.antiphon\checkpoints\20261029-223252-7dd2\report.md
verdict: GREEN exit=0
~~~~

~~~~text
--- checkpoint report ---
run: BASE-land-portable-20261029-222439-fef7
commit: ade54222543381e2e1b31ad79d8ae33a4035f70b  branch:   worktree: C:\Antiphon\worktrees\c1613-base-fa7879ff  host:  cores=0
source: ade54222543381e2e1b31ad79d8ae33a4035f70b state=clean buildSource=verified
CHECKPOINT BASE-land-portable commit=ade54222543381e2e1b31ad79d8ae33a4035f70b build=reused filter=/*/*/LandGraphEvaluationTests/(C1483_S4_RealRepositoryApproves*)|(C1483_S5_CheckRequiresEvaluationRecord*) executed=2 passed=2 failed=0 skipped=0 trx=C:\Antiphon\worktrees\c1613-base-fa7879ff\.antiphon\fa7879ff-base-portable\BASE-land-portable-20261029-222439-fef7\run.trx slot=granted waited=0s dirty=0 source=ade54222543381e2e1b31ad79d8ae33a4035f70b sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
wall: 0m00s  sequential-equivalent: 0m00s  builds: 0  max-concurrent-builds: 0  rows: 1 green 0 red 0 skipped
outputs: none
verdict: GREEN exit=0
~~~~

--- next stage ---
next: review
handoff: Review the Windows harness repairs. Keep the landing gate incomplete until the two confirmed fixture/landing defects and four remaining comparison rows are addressed.
artifact: C:\Antiphon\worktrees\card-task-fa7879ff\.antiphon\task-fa7879ff.md
