Repaired four fixture mechanisms; latest scoped Linux results are 69 passed, zero remaining failures/skips. Native Windows rerun remains required.

## Changes and attribution

Base was freshly fetched origin/master 9a3e6c7a53d2e98fd8582dd7d0c0ef1cb4be87a9. Assigned branch remained feat/card-task-af2b78c8. No product files, nightly cost inventory, native desktop process/lock, Unit lane, Review, release/tag/deployment, rebase or target merge was used.

- 366272929ffce66169abc2e81e52b8c8065d2ee6 records the forced-Windows ownership admission red and its repeatable witness.
- 26802745996a62a943b421bff8e56e837d2361fc adds Before(Test) admission to ScriptHarnessLinuxOwnershipTests. All 21 methods reach Linux supervisor/procfs/process-group behavior; they retain native Linux execution. Non-Linux throws SkipTestException with: `Native Linux process-group ownership qualification requires Linux (procfs, setsid and POSIX signals).` Forced linux/windows/osx string decisions pass after the fix.
- d343dec7636fc274eb8ee236ac9f78a479f9fc2b records retirement admission and Windows read-only deletion witnesses before their fixes.
- cdaed80e6777b52dabf695bfe41d7571e339d072 separates three mixed retirement methods into their existing portable methods plus guarded Linux host methods: C994_Host_strict_volume_admission_remains_independent, C994_Host_resume_never_runs_container_cleanup, C994_Host_bridge_transport_preserves_literal_context. Removing only the added guarded method boundaries reproduces the original file byte for byte; every assertion remains. Class inventory changes 13 -> 16. CARD-1577 owns pricing; nightly-class-costs.json was not changed. Existing portable C1008_Transport_scripts_are_ascii also covers the transport ASCII predicate.
- The same fixture slice clears FileAttributes on the owned loose Git object immediately before the recovery hook deletes it. Its existing FAULT-HIT, recoverability and object-absence assertions remain. A witness compiles the actual hook expression, forces an apostrophe/backslash Windows path, and maps IO.File to an in-memory read-only Windows object: before, UnauthorizedAccessException precedes FAULT-HIT; after, the exact path is normalized then deleted. This is emulated Windows semantics, not a native receipt or proof of the original Windows TRX's precise cause.
- 9bd0399085a723ff877c984213944fcc28eb2439 records the inherited C578 failures and forced-Windows argv red.
- f280999da92222c60c81ee812abf90dc14909958 replaces Start-C578Runner's shared bin-c578h/ argument with the fixture's already unique OutputPath. Added runtime/version/OS/path-separator context to harness output, summary and C578 evidence, plus the selected output path. Assertions, deadlines, retries, production checkpoint confinement and ownership are unchanged.

## Inherited checkpoint red and correction

The first exact RunCheckpointScriptTests run at cdaed80e6777b52dabf695bfe41d7571e339d072 completed 24 passes and two real failures: C578_NoSuccessReceiptForInterruptedBuild and C578_FailedBuildKeepsLogAndExit. Both refused pre-existing-build-source because the preceding C578 case had already populated the shared output.

Confirmed at exact original base in a clean disposable shared clone: BuildLogVisibleBeforeExit passed (harness exit 0, 19.773s), then InterruptedBuild failed (exit 1, 17.914s) and FailedBuild failed (exit 1, 13.850s), each with the same refusal. This needed no base build: these are the original fake-dotnet harness cases. The assigned checkout/branch was never switched. Clone was removed after all local children exited.

checkpoint-output-windows-witness.ps1 extracts the actual argv builder and cuts immediately before Process.Start. Forced Windows repository/results paths reproduce shared output before, and distinct fixture outputs after. An initial witness setup error used a nonexistent Linux C: PSDrive; it is retained separately and is not red evidence. The corrected witness keeps only its inert shim-root POSIX while forcing the Windows repository/results strings.

## Verification and costs

One isolated build at cdaed80e6777b52dabf695bfe41d7571e339d072, through scripts/build-slot.ps1: exit 0, 0 errors, 1196 warnings (including RouteHandlerAnalyzer AD0001); 3m25.75s compiler envelope, lease held 207s, wait 0s, maxcpucount 6. OutputPath=bin-af2b78c8/, UseAppHost=false. No second build. Later commits change only script fixtures/witness/report; the compiled C# source remains identical. Test DLL SHA256 before and after runs: 941a202925b0811028e37e7b15bebdc5cbcb4f4eb67369222d53e79c5da9ce0e.

All build/test/witness/harness drivers were leased and awaited. The commissioned direct --no-build fallback avoids the checkpoint reader's >64 MiB DLL limit; no checkpoint certificate or reviewedSourceClean assertion is claimed. Host Debian 12 Linux x64, SDK 10.0.402, runtime .NET 9.0.21, TUnit 1.44.0.0. Load samples are retained (one-minute samples approximately 1.8-22.7).

| Exact class | First scoped TRX | Latest distinct outcome | Initial test-host duration |
|---|---:|---:|---:|
| ScriptHarnessLinuxOwnershipTests | 21 passed | 21 passed | 52.846s |
| RetiredTempContainerScriptTests | 16 passed | 16 passed | 185.748s |
| EvidenceDeletionGuardTests | 6 passed | 6 passed | 196.439s |
| RunCheckpointScriptTests | 24 passed / 2 failed | 26 passed | 467.584s |

Filters were `/*/*/<ClassName>/*`, each with fresh TRX and nonzero class membership. Driver envelopes were respectively 147.805s, 278.500s, 290.459s, 560.653s. Approximately 90s per initial invocation was PowerShell call-operator wildcard expansion before launching the leased child, separate from test/slot time. Targeted follow-up uses literal argv via ProcessStartInfo's Node equivalent (spawnSync), avoiding that launch cost.

After f280999da92222c60c81ee812abf90dc14909958, five separate exact-method TRXs passed 1/1 each: C578_BuildLogVisibleBeforeExit, C578_NoSuccessReceiptForInterruptedBuild, C578_FailedBuildKeepsLogAndExit, C585_AsciiOnly, C585_LineFormat. Driver durations 19.764s, 18.819s, 20.936s, 17.117s, 28.368s. Filter form: `/*/*/RunCheckpointScriptTests/<ExactMethod>`. Their C578 captures include runtime, command arguments, stdout/stderr and output identity.

A first combined method filter selected zero tests (exit 8), retained as invalid selection with no verification credit; the five exact-method invocations replace it. Initial red is preserved, not relabeled. Latest coverage is 69 distinct passing tests across the named slices, not a fresh full Windows or whole-group run at one SHA. All four Windows string/source witnesses have real assertion reds then greens; ownership/admission witnesses map only the skip exception while compiling the actual guard bodies, without native setup.

Per-test/group timing sidecars and TRXs retain membership and durations. Legacy tests lack nested external-call instrumentation: CLI/filesystem/phase attribution remains unknown/remainder, not zero. No timeout or cost policy was widened.

## Native rerun and handoff

On the caller-owned Windows qualification lane after landing, build once into a fresh isolated output through scripts/build-slot.ps1, then run separate direct --no-build exact class rows with fresh TRXs:

| Class | Required Windows outcome |
|---|---|
| ScriptHarnessLinuxOwnershipTests | 21 explicit Linux-only skips, no native setup |
| RetiredTempContainerScriptTests | 13 portable passes + 3 explicit C1008 Linux-host skips |
| EvidenceDeletionGuardTests | 6 passes, zero skips; confirm the real read-only-object failure mechanism |
| RunCheckpointScriptTests | 26 passes, zero skips; repeat all C578 cases in the same qualification order |

Each filter is `/*/*/<ClassName>/*`. Set C578_EVIDENCE_ROOT to a fresh native evidence directory to retain the new runtime and inner process/output captures. Expect 45 Windows executions plus 24 intentional Linux-only skips, 69 results total. These skips are admission evidence, not native Linux execution credit. The original Windows failure causes are not fully qualified by these Linux results/emulators. Group 74 had no accessible evidence and was not added to scope.

Repeat the four standalone source/string witnesses through build-slot.ps1 with `pwsh -NoProfile -File scripts/fixtures/<name>.ps1`: script-harness-linux-admission-witness, retired-temp-platform-witness, evidence-recovery-windows-witness, checkpoint-output-windows-witness. They read source from the repository root and launch no native ownership primitive.

Evidence root: /work/worktrees/task-af2b78c8/.antiphon/evidence-af2b78c8/. classes-summary.json, trx-summary.json, base-repro.json, fixed-exact-summary.json, test-durations.tsv, timing*/ sidecars, witness logs, C578 inner captures and build log retain all outcomes/provenance. Generated evidence stays ignored. Final report: /work/worktrees/task-af2b78c8/.antiphon/task-af2b78c8.md.

Cleanup: 33 exact inventoried bin-af2b78c8 outputs, 38 newly created checkpoint fixture outputs and the disposable base clone removed. DLL hash was checked before removal. Lease audit: 23 real host grants, each with matching release (fake fixture leases excluded). No remaining test/build child in the assigned checkout. cleanup.json and final-owned-processes.json retain the record.

Source publication checks: evidence guard over base..f280999da92222c60c81ee812abf90dc14909958 passed (6 commits, 3 evidence entries, 0 violations). Fetched origin/master remains 9a3e6c7a53d2e98fd8582dd7d0c0ef1cb4be87a9; merge-tree is conflict-free, tree ce22ea08a0fc1fdb604e87d4704d4d4fdf517219. The final report-only publication has separate evidence-guard-final.log and merge-tree-final.txt records. All executable changes are pushed; no Review was commissioned under the explicit fixture-only exception.

--- next stage ---
next: land
handoff: Land the pushed fixture-only branch; rerun the four Windows rows before closing native qualification. Tell CARD-1577 the retirement class now has 16 cases, with no pricing edit here.
artifact: /work/worktrees/task-af2b78c8/.antiphon/task-af2b78c8.md
