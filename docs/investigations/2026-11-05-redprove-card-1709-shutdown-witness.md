# CARD-1709 shutdown witness red-proof

**Complete: 20/20 pristine repeats passed, B11b was killed at the intended shutdown assertion, the restored witness passed 1/1, and the requested channel neighbors passed 120/120.** No production or test repair is included. The only committed deliverable is this report.

Task: `38ec4219`, Linux/server2, 2026-11-05. Tested source was
`ba1b7371a30c1d532aa0b67430d8eff0b3036e06`, the tip of Code `51b0c438`.
Execution used a fresh detached worktree at `/work/worktrees/redprove-38ec4219`,
outside `/tmp`. The assigned report branch is `feat/card-task-38ec4219` and
remains a fast-forward descendant of that source. Nothing ran on the desktop.

This is the explicitly commissioned ordinary-worktree Custom red-proof, with
the ordinary Mutation sweep waived. It makes no SourceLanding, checkpoint,
whole-suite, Review or release-qualification claim. The brief defines one
CARD-1709 mutant, B11b; its concluding reference to “both mutants” does not
define a second control. No claim is made for a companion CARD-1719 control.

The design owner is
[the shutdown-witness synchronization plan](../superpowers/plans/2026-11-05-card-1709-shutdown-witness-synchronization.md).
B11b is taken from the report at
`origin/feat/card-task-6569dd36:docs/investigations/2026-11-05-mutation-1716-1719-1709.md`.
That report's original notice-only mutant pass remains a historical finding;
these executions establish the changed witness's behavior at the newer tip.

## Results

Exact witness, used for all repeats and both sides of the mutation cycle:

```text
/*/*/TaskSupervisionReceiptTests*/C1709_Pass_whose_only_busy_item_is_a_supervision_notice_retries_before_the_backstop
```

| Execution | Passed | Failed | Process wall seconds | Admission seconds | Slot held seconds |
|---|---:|---:|---:|---:|---:|
| Pristine tip, 20 separate processes in one lease | 20 | 0 | 924.701 | 240 | 931 |
| B11b enabled, exact witness | 0 | 1 expected | 44.197 | 0 | 45 |
| Fresh restored build, exact witness | 1 | 0 | 47.902 | 60 | 49 |
| Restored channel neighbors | 120 | 0 | 238.560 | 360 | 239 |

All 142 results executed; 141 passed and the sole failure was the intended
mutant assertion. No test was skipped and no admission or execution timed out.
Every pristine process executed exactly one case against the same pristine
server DLL, rather than using a selector-off packed mutant as its baseline.
Twenty repeats were explicitly authorized by this brief; none was retried.

The pristine repeat batch took 15m24.701s of combined process wall time.
TRX test durations, including fixture work charged to the case, were
14.862s minimum, 19.552s median, 32.061s nearest-rank p95 and 34.570s maximum;
their sum was 456.730s. Admission is separate from these values. This is a
20-run sample under the observed host load, not an unlimited determinism claim.

The neighbor selection was:

```text
/*/*/(ChannelBridge*)|(ChannelInboundRecoveryTests*)|(ChannelInputWithheldIncidentTests*)/*
```

Its compiled roster contained `ChannelBridgeTests` 41/41,
`ChannelInboundRecoveryTests` 48/48 and
`ChannelInputWithheldIncidentTests` 31/31, all passing.

## Mutation and the red assertion

Only the recovery-loop wait in
`server/Infrastructure/Orchestration/CompletionNoteWorkHostedService.cs`
was temporarily edited. With `ANTIPHON_REDPROVE_38EC4219=B11b`, the token
argument to `flushes.Recovery.WaitAsync(nextSweep, clock, ct)` became
`CancellationToken.None`, exactly B11b's behavior. A single environment-gated
mutation build contained that control; no test source changed.

The fresh TRX records test-driver exit 2, one executed case and one failure:

```text
TimeoutException: The operation has timed out.
TaskSupervisionReceiptTests.C1709.cs:line 338
await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(10));
```

The test first awaited `BackstopTimerCreated` at line 333, then failed at the
existing bounded shutdown check. This is the intended assertion, not an
observation timeout, build failure, fixture error, zero-test run or outer
process timeout. The supervising evidence driver returned success only because
the test process returned its expected failure code; both codes are retained.

## Builds, provenance and restoration

One pristine cold build preceded the repeats. The mutation and restored builds
used the same worktree's incremental native outputs, with a fresh compiler run
following each source change. Each build and each test driver acquired its own
host build-slot lease; the authorized repeat loop held one lease for all twenty
no-build test processes. All runs were foreground and awaited, sequentially.

| Build | MSBuild elapsed | Admission seconds | Slot held seconds | Warnings | Errors |
|---|---|---:|---:|---:|---:|
| Pristine | 3m15.58s | 121 | 196 | 1695 | 0 |
| Mutant | 2m05.50s | 330 | 126 | 1672 | 0 |
| Restored | 2m27.81s | 75 | 149 | 1672 | 0 |

Builds used `dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj
-p:UseAppHost=false -nodeReuse:false` through `scripts/build-slot.ps1`.
Tests used `dotnet run --project tests/Antiphon.Tests --no-build
-p:UseAppHost=false -- --treenode-filter <selection> --report-trx
--report-trx-filename run.trx --results-directory <fresh-evidence-path>`.
Every driver used `TEMP=/tmp`; direct builds used native default outputs.
Non-dotnet test wrappers emitted the gate's CPU-cap warning; they launched
only `--no-build` test processes and contained no build loop or slot bypass.

Admission was explicitly bounded at ten minutes per lease. Per-process test
bounds were 180s for the exact witness and 480s for the neighbor pack. The
repeat loop was bounded at 28m; combined outer driver bounds were 38m for
builds/repeats, 14m for exact runs and 19m for neighbors, with a 20s termination
grace. These provisional driver bounds implement the brief's generous-bound
instruction; existing test observation/shutdown deadlines were unchanged.
No individual selection or repeat batch exceeded thirty minutes.

Producer and staged server DLL hashes matched within every build. Their SHA-256:

- Pristine: `eb59306e927bf30563d254556e9fe65da55657af6873c54041aac453c014bc45`.
- Mutant: `7383be053cb703a0e824e19c40b229eeb5552fc4838f0f979c96f66f13a34815`.
- Restored: `1e3501c77e7aca768c4a1bb33b8ac5b4993518b590cadd67465fe6276886a1c2`.

The mutant DLL contained the environment selector; both restored copies lacked
it. Restored binary hashes are recorded separately, not claimed byte-identical
to the pristine build. Both restored test executions recorded the restored
server hash.

Restoration used the literal command:

```text
git checkout -- server/Infrastructure/Orchestration/CompletionNoteWorkHostedService.cs
```

The restored file matched HEAD Git blob
`02b03081193396ffb5595159e63a17742d82357b` and baseline SHA-256
`9cd1897ddc4fe7e312bb718f17af807a3a63c5a8071115447ef3ce70560b2fd2`.
The untouched witness file also matched its baseline Git/SHA-256 hashes.
The tracked tree was clean before the pristine build, before mutation, after
restoration and after the final test. Final source hashes were rechecked before
cleanup. No mutant was committed.

## Evidence and cleanup

Persistent raw evidence: `/work/evidence/task-38ec4219`.
It contains the archived driver `run.cjs`, source plan/report, `mutant.patch`,
identity and source hashes, all three build logs/exits and DLL hash records,
twenty repeat directories, mutant/restored/neighbor directories with commands,
selector, exit, binary hash, process times, full logs, HTML and fresh TRX,
`repeat-summary.json`, `build-summary.json`, `summary-final.json`,
`red-assertion.txt`, `restoration-hashes.json`, output inventory and cleanup record.
Per-test membership and duration are retained in TRX. Uninstrumented container,
database preparation/clone, body subphase, teardown and external-call spans
remain unknown; unattributed time is not reported as zero.

All owned processes had exited and leases were released before cleanup.
After hash verification, the exact owned worktree was removed with
`git worktree remove --force /work/worktrees/redprove-38ec4219`, removing its
72 inventoried bin/obj output roots and any test-result outputs. Absence of the
worktree was verified. Raw evidence is outside it and remains available.
No `rm`, glob deletion, `git clean`, landing, rc/master push, deployment or
`land-graph -Update` was used.

The scoped CARD-1709 evidence is complete. No production fix or further repeat
is indicated by these results. The caller can use the existing authority for
test-only red/green evidence and combine any separately commissioned companion
control before a joint land.

--- next stage ---
next: land
handoff: CARD-1709 B11b killed; 20/20 repeats, restored witness and 120/120 neighbors green. Combine separately commissioned companion evidence if landing jointly.
artifact: docs/investigations/2026-11-05-redprove-card-1709-shutdown-witness.md
