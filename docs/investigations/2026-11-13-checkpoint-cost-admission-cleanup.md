# Checkpoint cost: admission, guards and output custody

Three historical runs are healthy but expensive. Admission, repeated cold builds,
guard work and output deletion are separate costs. This investigation preserves
all historical outcomes; it does not qualify a release or demonstrate a complete
Code/Review run under 15 minutes.

## Evidence and phase accounting

Read-only copies of the three report JSON/Markdown files, executor logs, host
snapshots, resolved manifests, requests and per-row TRX are retained under
`.antiphon/c1864/historical/<run-id>/` in task `5e62de4f`. Analysis scripts and
`phases.txt` are ignored evidence. Originals:

| Selection | Run / source | Original root | Host load at terminal snapshot |
|---|---|---|---|
| CARD-1836 S3a | `20261113-011310-f84a` / `553dd32f60589ad1794729785411de1d213a4096` | `/work/worktrees/task-3ca2caa6/.antiphon/checkpoints/20261113-011310-f84a` | 26.85 / 26.44 / 26.08 |
| CARD-1836 S4a | `20261113-050644-1f79` / `f47407281797cdc6db615181a7f0cf2381d94f31` | `/work/worktrees/task-6c1c4d05/.antiphon/checkpoints/20261113-050644-1f79` | 28.56 / 26.44 / 24.41 |
| CARD-1820 S1R2 | `20261113-035744-2c23` / `c99d3e411cf3188637cdd87b8d8da2e14f4dab9a` | `/work/worktrees/task-ee621172/.antiphon/checkpoints/20261113-035744-2c23` | 51.48 / 56.57 / 55.45 |

All are Debian 12, 24 cores, SDK 10.0.404, with a shared four-slot broker granting
maxcpucount 6. These are single contended observations, not measured p95 samples.
S4a's actual receipt has **166** CP-90 results; the brief's 162 is not the terminal
count. S3a has 135 in CP-90 and another 27 DerivedImpact cases in CP-93.

Seconds below: build wall is the full scheduled build envelope, including its
slot wait and source/output custody; compiler is MSBuild `Time Elapsed`, nested
inside it. `held` is the grant-to-release interval from the executor log, not
compiler time. Each reused build is counted once. Startup includes discovery:
separate discovery timestamps are unavailable. Host tail is last case end to
process exit. Row remainder is execution minus the monotonic test-host wall;
it includes row checks/evidence, and is not a separately measured custody span.
Global cleanup cannot honestly be allocated to individual CP rows.

| Run / row | Passed | Build wall (compiler; held) | Build wait | Row wait | Startup + discovery | Test window | Host tail | Row remainder |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| S3a CP-2 | 4 | 431.680 (215.980; 261.051) | 15 | 0 | 15.030 | 42.931 | 0.610 | 1.620 |
| S3a CP-90 | 135 | 567.055 (287.700; 362.457) | 0 | 105 | 20.291 | 117.370 | 0.563 | 0.716 |
| S3a CP-91 | 2 | 311.833 (88.400; 100.709) | 0 | 195 | 6.189 | 0.417 | 1.178 | 1.742 |
| S3a CP-93 | 27 | reused CP-90 | 0, reused | 15 | 8.177 | 567.990 | 0.655 | 0.603 |
| S3a CP-92 | 2 | 380.540 (42.740; 51.670) | 165 | 330 | 4.627 | 0.019 | 0.730 | 1.141 |
| S4a CP-8 | 8 | 563.205 (209.860; 248.954) | 255 | 0 | 10.057 | 100.658 | 1.136 | 1.183 |
| S4a CP-90 | 166 | 370.364 (252.730; 311.346) | 0 | 0 | 20.078 | 721.106 | 1.303 | 1.150 |
| S4a CP-91 | 2 | 99.058 (20.870; 29.290) | 0 | 15 | 5.809 | 0.008 | 0.533 | 1.015 |
| S4a CP-92 | 2 | 129.192 (32.650; 43.848) | 0 | 0 | 5.135 | 0.206 | 0.890 | 1.116 |
| S1R2 CP-2 | 4 | 1163.687 (314.440; 421.470) | 0 | 45 | 12.686 | 0.392 | 0.609 | 2.406 |
| S1R2 CP-3 | 2 | reused CP-2 | 0, reused | 0 | 13.934 | 31.900 | 1.762 | 3.261 |
| S1R2 CP-101 | 78 | reused CP-2 | 0, reused | 0 | 39.157 | 155.190 | 1.510 | 4.556 |
| S1R2 CP-102 | 1 | reused CP-2 | 0, reused | 0 | 22.626 | 48.615 | 0.983 | 3.704 |
| S1R2 CP-103 | 57 | reused CP-2 | 0, reused | 0 | 15.391 | 6.765 | 0.681 | 2.973 |
| S1R2 CP-104 | 1 | reused CP-2 | 0, reused | 0 | 13.923 | 10.864 | 0.687 | 2.995 |

| Run | Budget wall | Charged execution | Admission-only union | Output cleanup | Evidence / output discovery / report publication | Outcome |
|---|---:|---:|---:|---:|---:|---|
| S3a | 2699.831 | 2189.734 | 510.097 | 174.622 | 0.149 / 0.433 / 0.050 | 170 passed, 0 failed/skipped |
| S4a | 1951.005 | 1680.995 | 270.010 | 206.817 | 0.138 / 0.436 / 0.077 | 178 passed, 0 failed/skipped |
| S1R2 | 1894.806 | 1849.756 | 45.050 | 279.560 | 0.354 / 0.154 / 0.051 | 143 passed, 0 failed/skipped |

The outer report walls are 2699.990, 1951.115 and 1895.032 seconds; their slightly
different start/end clocks are preserved. Never sum overlapping build/row spans
as whole-run wall. Queue delay overlapping another activity remains charged
execution; only admission-only union is subtracted by the budget clock.

## Admission and lease lifetime

Historical 409 replies say `build_slot_busy`, occupied=4/budget=4; they do not
implicate the memory floor. The live broker was also enabled, budget=4,
occupied=4, maxcpucount=6, with two waiters at the read. GET `/metrics` returned
405, consistent with the old shared-broker pin described in testing-and-build.
No broker setting, lease or process was changed. Four slots with seven desired
agent seats means simultaneous drivers queue; an agent seat is not a build slot.
FIFO waiters poll at 15 seconds. Multiple short rows reacquire at the back of the
queue; waits of 195/330 seconds can dwarf the corresponding 9.526/6.517-second
row execution. A 15-second polling interval alone does not explain a 330-second
wait. Without broker lifecycle metrics, which competing holder caused each wait
is unknown; there is no evidence here of an abandoned lease.

`RunScheduler.RunBuildAsync` deliberately holds the lease beyond the compiler:
source-boundary observation, graph/write-root preflight, binding invalidation,
admission ledger check, and the confined driver's in-slot before/after checks
are inside it. The build lease ends before outside-ledger verification and
output receipt sealing. The table's held-minus-compiler gaps range 8.420 to
107.030 seconds. They are combined custody/orchestration remainders, not all
avoidable work. A row lease covers build-stamp validation, launch/run, host-output
adoption, TRX/report and source-boundary checks; it ends on method return.
Global output cleanup occurs after all leases release. No build-slot semantic
change or early release is warranted by these records.

## Guard and output cost remaining after CARD-1861/1862

CARD-1861 removed the full test-project dependency from the DerivedImpact driver
(367.68 -> 4.03 seconds observed build-log time). CARD-1862's replay reduction
(approximately 519 -> 197 seconds, supplied prior finding) is separate. The
historical CP-90/93 timings above predate those optimizations; do not subtract
their savings from a future wall budget as if queues and overlaps were fixed.

S4a CP-90 is already one TUnit process running its entire named guard union.
Its 721.106-second case window includes DerivedImpactIndexTests 27-case duration
sum 557.659 seconds and TestClassificationPolicyTests 41-case sum 141.914
seconds; sums can overlap. S3a's policy sum is 116.296 seconds; S1R2's is
153.855 seconds. The classification class rebuilds an identical ordinary TUnit
probe for C487_G066, both C487_G067 argument rows and both C487_G069 rows. It also
has one deliberately different G065 method-level Slow probe with live assembly
inspection. This is a safe setup-sharing opportunity after the replay repair.

Output cleanup removes only the current process's recorded/sealed `bin-<id>`
roots and their owned descendants. The artifact layout contains referenced
projects' compiler files and redirected `artifacts/obj`, so many intermediate
files are inside those roots. Native project `bin/` and `obj/`, pre-existing or
changed entries, unrelated roots and checkpoint/TRX evidence are preserved.
Deletion is individual anchored unlink plus nonrecursive directory removal;
no recursive-delete optimization is added here.

`CheckpointOutputLedger.Remove` calls `Check`, inventories entries, and calls
`DeleteRecorded` for each. Each entry gets content/identity/link/containment and
Git preflight again. `CheckpointWrite.Preflight` reaches `PreflightRoots`, so
there is at least one new Git child per entry, with additional root checks.
The 207–280 seconds is not ordinary recursive filesystem deletion time.
CARD-1327 already identifies this mechanism and is in Review; this task does
not compete with that frozen implementation. Its earlier representative root
had about 2500 entries. Historical roots here were successfully deleted and
no file-count/unlink/Git subphase counters were retained: their exact counts
and filesystem-versus-Git time are **unknown**, not zero or a fabricated 2500.

Background cleanup after publishing GREEN is unsafe without a durable joined
owner and amended receipt semantics: deletion authority is process-local and
current receipts include cleanup/refusals. Moving it would either lose that
authority or report completion before custody finishes. It would reduce visible
latency rather than compute/IO cost. Keep synchronous finalization.

A representative fresh root from this task was counted read-only after its build,
while the red row queued: at 06:04:05 UTC, `tests/Antiphon.Tests/bin-c1864-red`
contained **2989 files, 538 directories, 1,143,951,761 bytes** (descendants;
root excluded). `representative-output-count.json` records the observation.
That is 3527 descendant preflights plus the root if a subsequent owned deletion
completes, before extra whole-root checks. It is an observed current graph size,
not a recovered count or measured Git-process total for the historical runs.

## Implemented guard setup sharing

`TestClassificationPolicyTests` receives a TUnit `PerClass` data source with a
lazy ordinary-probe task. The existing temporary-directory wrapper finishes
cleanup before publishing its immutable result. All ordinary assertions still
inspect the real compiled probe's discovery, inheritance, row/data-source,
Slow-category and explicit-test results. The distinct method-level Slow probe
and any live inspection callback always use a fresh build and existing teardown.
Failures stay failed in the lazy; no retry, persisted source cache or new delete
code was introduced. The process-spawn limiter remains on the class.

The unchanged new witness makes two ordinary requests, requires both directories
to be gone, requires the same immutable result, and checks 8 ordinary cases,
2 Slow cases and explicit-case absence. The full class includes every original
case plus this witness. The green setup log measures the first cold setup
separately from later cache lookups, because TUnit can schedule another ordinary
consumer first. This avoids claiming a zero-cost lookup as cold-start savings.

Red source `b07b31a59f4792cab15b8e5f306e3e7c3fa25262`, checkpoint
`20261113-055525-2a24`: 1 executed, 0 passed, 1 failed, 0 skipped. It fails at the
intended `ShouldBeSameAs` assertion after both real probes returned their expected
metadata and both directories were removed; it is not a build/fixture-error red.
The two complete ordinary requests cost **19.441 seconds**. Red row execution
32.078 seconds includes 11.121 seconds startup/discovery, 19.532 seconds case
window, 0.428 seconds host tail and 0.997 seconds row remainder. Build compiler
196.80 seconds, lease 244.084 seconds, build admission 150 seconds, row admission
195 seconds. Run budget wall 662.403 = execution 317.331 + admission-only 345.072
seconds. Output is intentionally retained on red.

Green source `5c382378124c68e37d757cd134264ac631670d7f`, checkpoint
`20261113-060839-3382`: **42 passed, 0 failed, 0 skipped**, exit 0. A read-only TRX
roster comparison with S4a preserves all 41 original display names, with zero
missing/extra original cases; the only addition is C1864. The witness happened
to populate the lazy first, so its **13.991 seconds includes the 13.980-second
cold setup**. There is exactly one ordinary setup log for the full class. No
warm-cache lookup is substituted for that cold measurement.

The same-host two-request observation falls **19.441 -> 13.991 seconds**, a
**5.450-second (28.0%) observed reduction**. One cold build plus three probe
launches replaces two builds plus six launches in this witness. For the existing
five ordinary consumers, the structural reduction is five -> one builds and
fifteen -> three launches; the new witness's two requests also share that result.
An end-to-end CP-90 saving or repeated-sample p95 is not measured. Both runs used
server2/Debian 12, SDK 10.0.404 and .NET 9.0.23. Start load was 23.25/29.69/25.67
(red) versus 26.49/26.08/24.64 (green), with four shared broker slots. This is a
single before/after observation under changing load, not a controlled benchmark.

Green row execution is 69.835 seconds: startup/discovery 8.777, case window
58.794, host tail 0.935, row remainder 1.322 seconds. Green build wall 427.364,
compiler 304.65, lease 374.841, executed 404.487 seconds; build and row wait are
both zero. Green budget wall 578.609, execution 578.608, admission-only 0.001
seconds; the outer report wall is 578.776 seconds. Finalization includes evidence
0.359, output discovery 0.134, output cleanup **77.863** seconds; report publication
is another 0.543 seconds. Outputs were deleted, no cleanup refusals. This lower
cleanup observation has a different selection/load/root count and **is not a
cleanup saving caused by this patch**. Similarly, overall run wall is not the
probe-saving benchmark. The unused other-stage build definition in each receipt
was not executed; each qualification actually ran one isolated build and one row.

Source start/end are clean, dirty=0, with stable fingerprint
`397f8e00ab344209adf9dbaad3eba27661db7c2c0ba8da470294cab0084f1b97` and verified
build binding. Tool receipt validation for CP-2 exits 0, `CHECKPOINT SOURCE VALID`.
Green report SHA256:
`C7AA61FA6A8BA541DA6EE53E04DD98A92D025F7B753B188665E5C00A8EF0DA7B`;
green TRX SHA256:
`E8135784FF835174C4D9EA439F42941A9384315BB4903C0A518169BB2032D360`.

The two declared prerequisite builds outside the checkpoint selection used the
host gate, native outputs and UseAppHost=false: tool bootstrap (14.08 seconds
compiler, 210 seconds wait, 17 seconds held, 11 warnings/0 errors) and standalone
index driver (12.90 seconds compiler, zero wait, 14 seconds held, 0 warnings/errors).
The latter was needed to regenerate derived-impact metadata after the test edit;
regeneration completed with unchanged bytes. The initial abbreviated expected
source SHA was refused before launch; the corrected full-SHA invocation is the
retained red run, not an additional test execution. Neither full historical
selection nor the whole Unit lane was repeated. Mutation checks were explicitly
waived in the brief. All owned runs, leases and tool-copy cleanup are joined.

The final documentation commit records receipts for the exact tested code SHA
above; it does not relabel that evidence as execution at a later narrative tip.
Initial ordinary Review remains next. No native Windows timing was collected.
## Deferred work and existing cards

| Owner | Smallest next action | Available cost / limit |
|---|---|---|
| CARD-1327 (Review) | Finish its batched Git preflight design and race witnesses while retaining per-entry checks and exact recorded unlink paths. | Historical global cleanup 174.622/206.817/279.560 seconds; savings not measured by this task. Do not edit its frozen subject. |
| CARD-1493 (Done) / plan authors | Use existing explicit CP-n reuse for compatible project, After, Repeat and properties; run the existing duplicate-build warning at plan creation. | S1R2 already shares one build across all six rows; S3a CP-93 reuses CP-90. S4a CP-8 is After S4a while CP-90 is After all, so current importer correctly refuses automatic cross-group reuse. Author a shared snapshot group only when it preserves the staged proof. |
| CARD-1629 (Review) | Continue opt-in, source-bound cross-run immutable build reuse; no stale/foreign cache admission. | Its separate cold-vs-incremental sample 170 -> 29 seconds is not a measured saving for these graphs. Fresh OutputPath alone defeats incrementality. |
| CARD-1627 / CARD-1489 deployment obligation | Arrange idle-broker publication of the metrics-capable pin through the documented operations owner, then observe normal work. | Current GET metrics still 405. Do not raise the four-slot budget or release other holders to make the queue disappear. No host work authorized by this Debug was performed. |
| CARD-1514 timing instrumentation | Add build admission/driver/seal/finalization subspans, unlink and Git subprocess counts, byte/file totals. | Large build custody remainders and deleted historical roots make precise attribution impossible; no unsupported hashing/file-count saving is claimed. |
| CARD-1862 (Review) | Land the existing fresh-input selector replay optimization and retain its proof. | The historical 557.659-second DerivedImpact case sum dominates S4a; do not rerun that green union just for timings or duplicate the ongoing fix. |

The full union is already one process. Joining unrelated CP rows into one process
would remove distinct row evidence, deadlines and exclusivity; silently doing so
would change what the plan proves. Safe future plan authoring can combine a
named exact union only while retaining every acceptance case and coverage check.
There is no safe performance-driven replacement of the required guard selection
or automatic "faster default" that omits slow guards.

No timeout, assertion, broker semantics, required selection or deletion authority
was weakened. The next meaningful whole-run measurement is the next required
ordinary Review/Code run after these independently reviewed repairs land; no
repeat full historical selection was launched for a benchmark.

## Retained report digests and checkpoint lines

Historical report.json SHA256, in table order:

- S3a: `E4A96006D386F77E513700EE1F66BF46882B537C6110EA8F5A2B81E4CB928931`
- S4a: `B69F626B0A0383BB47FEC4EDA9B44AB380CEB118C188560B371AD5C774DFA801`
- S1R2: `B06414F8A5A5CDCC791EF50570E0DBDF897E667287D84838DA44090028A70B3B`

These unedited lines preserve the new red/green outcomes and their source binding:

```
CHECKPOINT CP-1 commit=b07b31a59f4792cab15b8e5f306e3e7c3fa25262 build=ok filter=/*/*/TestClassificationPolicyTests/C1864_ordinary_probe_is_shared_after_its_directory_is_removed executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-5e62de4f/.antiphon/checkpoints/20261113-055525-2a24/rows/CP-1/run.trx slot=granted waited=195s dirty=0 source=b07b31a59f4792cab15b8e5f306e3e7c3fa25262 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=5c382378124c68e37d757cd134264ac631670d7f build=ok filter=/*/*/TestClassificationPolicyTests/* executed=42 passed=42 failed=0 skipped=0 trx=/work/worktrees/task-5e62de4f/.antiphon/checkpoints/20261113-060839-3382/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=5c382378124c68e37d757cd134264ac631670d7f sourceState=clean buildSource=verified
```

Verification plan: `docs/superpowers/plans/2026-11-13-checkpoint-cost-probe-plan.md`.
Red test commit: `b07b31a59f4792cab15b8e5f306e3e7c3fa25262`.
Implementation/green commit: `5c382378124c68e37d757cd134264ac631670d7f`.
Task base: `ab7af12c699039270af7e22a5d7fd794035bc325`.
Full settlement checks and final published tip are recorded in
`.antiphon/task-5e62de4f.md` and the caller report.
